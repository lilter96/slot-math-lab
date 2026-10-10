using System.Security.Cryptography;
using System.Text.Json;

namespace SlotMath.Core.Measurements;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record VerificationCriterion(string MeasurementId, string Check, long MinimumCount = 1);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record VerificationProfile(string Name, double FamilyConfidence, VerificationCriterion[] Criteria);
public sealed record VerificationCriterionResult(string MeasurementId, string Name, string Check, string Status, long Count,
    long MinimumCount, double? AllocatedAlpha, NumericInterval? Interval, VerificationCheck? Evidence, string Detail);
public sealed record VerificationProfileReport(string Name, string ProfileHash, string Status, double FamilyConfidence,
    double AllocatedAlpha, VerificationCriterionResult[] Criteria, string Detail)
{ public string AlgorithmVersion { get; init; } = "predeclared-verification-v1"; }

/// <summary>A bounded, predeclared decision contract over pinned measurement evidence.
/// Logical checks describe observed cases. Statistical acceptance retains every marginal
/// method's assumptions; Bonferroni allocation cannot turn approximate intervals into proofs.</summary>
public static class ProfileVerification
{
    public static string Hash(VerificationProfile profile) => Convert.ToHexStringLower(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new { version = "predeclared-verification-v1", profile }, JsonOptions.Default)));

    public static void Validate(VerificationProfile profile, IReadOnlyList<MeasurementDefinition> plan)
    {
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 80 || profile.Criteria is not { Length: >= 1 and <= 32 }
            || !double.IsFinite(profile.FamilyConfidence) || profile.FamilyConfidence is < .5 or > .999999)
            throw new ArgumentException("A verification profile needs a name (maximum 80 characters), 1–32 required checks and family confidence between 0.5 and 0.999999.");
        if (plan.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != plan.Count)
            throw new ArgumentException("Measurement identities must be unique.");
        var used = new HashSet<(string, string)>(); var alpha = 0d;
        foreach (var criterion in profile.Criteria)
        {
            if (criterion is null || string.IsNullOrEmpty(criterion.MeasurementId) || !used.Add((criterion.MeasurementId, criterion.Check))
                || criterion.MinimumCount is < 1 or > 10_000_000)
                throw new ArgumentException("Required checks must be distinct and have a minimum matching count between 1 and 10,000,000.");
            var definition = plan.SingleOrDefault(d => d.Id == criterion.MeasurementId)
                ?? throw new ArgumentException($"Required measurement '{criterion.MeasurementId}' is absent from the collection plan.");
            var options = definition.Options;
            if (options is not null && (!(options.Confidence > .5 && options.Confidence < .999999) || options.ErrorFamilySize is < 1 or > 256
                || options.ReferenceMean is { } mean && !double.IsFinite(mean) || options.Tolerance is { } tolerance && (!double.IsFinite(tolerance) || tolerance <= 0)))
                throw new ArgumentException("Pinned interval confidence, family size, reference and tolerance must be valid and finite.");
            switch (criterion.Check)
            {
                case "observation-integrity": break;
                case "exact-zero-assertion" when options is { Assertion: "zero", Subject: "observation" }: break;
                case "reference-support" when options is { ReferenceDistribution.Length: > 0, Weight: null }: break;
                case "mean-equivalence" when options is { ReferenceMean: not null, Tolerance: > 0, Weight: null, ReferenceStatistic: "mean" or "ratio" or "probability" }:
                    if (!options.IndependentSubjects && !options.IndependentParents)
                        throw new ArgumentException("Mean precision requires declared subject or paid-parent independence and a supported interval.");
                    if (!options.IndependentSubjects && (ReferenceSemantics.Probability(options) || ReferenceSemantics.Ratio(options)))
                        throw new ArgumentException("Probability and paired-ratio criteria require independent complete subjects. Paid-parent clustering alone currently supports numeric mean intervals.");
                    alpha += (1 - options.Confidence) / options.ErrorFamilySize;
                    break;
                default: throw new ArgumentException($"'{criterion.Check}' is unsupported or its measurement contract is missing. Goodness-of-fit non-rejection is not a precision criterion.");
            }
        }
        if (alpha > (1 - profile.FamilyConfidence) * (1 + 1e-12))
            throw new ArgumentException("The required mean checks exceed the profile's family error budget. Raise their confidence or error-family size before launch.");
    }

    public static VerificationProfileReport Calculate(VerificationProfile profile, IReadOnlyList<MeasurementDefinition> plan,
        IReadOnlyList<MeasurementSnapshot> snapshots, bool completed)
    {
        Validate(profile, plan);
        if (snapshots.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != snapshots.Count)
            throw new ArgumentException("Duplicate snapshot identities cannot establish verification evidence.");
        var rows = profile.Criteria.Select(c => Evaluate(c, plan.Single(d => d.Id == c.MeasurementId), snapshots.SingleOrDefault(s => s.Id == c.MeasurementId), completed)).ToArray();
        var status = rows.Any(r => r.Status == "discrepancy") ? "discrepancy" : rows.Any(r => r.Status == "invalid") ? "invalid"
            : !completed || rows.Any(r => r.Status == "insufficient") ? "insufficient" : "criteriaMet";
        return new(profile.Name, Hash(profile), status, profile.FamilyConfidence, rows.Sum(r => r.AllocatedAlpha ?? 0), rows,
            "Only these checks were declared before launch. Logical checks cover observed cases; statistical checks require every marginal interval's assumptions. Family allocation uses the sum of marginal error budgets without assuming independence between checks. Approximate intervals retain approximate coverage. A sampled profile is not an exact payout-law certificate.");
    }

    private static VerificationCriterionResult Evaluate(VerificationCriterion criterion, MeasurementDefinition definition, MeasurementSnapshot? snapshot, bool completed)
    {
        var options = definition.Options; var analysis = snapshot?.Analysis;
        var alpha = criterion.Check == "mean-equivalence" ? (1 - options!.Confidence) / options.ErrorFamilySize : (double?)null;
        VerificationCriterionResult Row(string status, string detail, NumericInterval? interval = null, VerificationCheck? check = null)
            => new(definition.Id, definition.Name, criterion.Check, status, snapshot?.Count ?? 0, criterion.MinimumCount, alpha, interval, check, detail);
        if (snapshot is null) return Row("insufficient", "The pinned measurement has no snapshot evidence.");
        if (snapshot.Count < 0 || snapshot.Excluded < 0 || snapshot.Errors < 0 || snapshot.Observations < 0
            || snapshot.Count > snapshot.Observations || snapshot.Excluded > snapshot.Observations - snapshot.Count
            || snapshot.Errors != snapshot.Observations - snapshot.Count - snapshot.Excluded
            || snapshot.Errors > 0 || analysis is { UnclosedEpisodes: > 0 } or { DuplicateAwards: > 0 }
            || analysis is not null && analysis.Count != snapshot.Count
            || snapshot.Count > 0 && (snapshot.Mean is not { } mean || !double.IsFinite(mean) || snapshot.Sum is not { } sum || !double.IsFinite(sum)))
            return Row("invalid", "Errors, unreconciled exposure, inconsistent summaries, duplicate awards or unclosed episodes invalidate the required population.");
        // Detect failures even when the planned precision or exposure has not yet
        // been reached. Never hide a real violation behind an insufficient count.
        if (criterion.Check == "exact-zero-assertion")
        {
            var assertion = analysis?.Assertion;
            if (assertion is null) return Row("insufficient", "Exact assertion evidence is unavailable.");
            if (assertion.Kind != "zero" || assertion.Checked != snapshot.Count || assertion.Violations < 0 || assertion.Violations > assertion.Checked)
                return Row("invalid", "Assertion exposure does not match the required measurement.");
            if (assertion.Violations > 0) return Row("discrepancy", "An exact residual failed before binary64 report conversion.", check: new("exact-zero-assertion", "discrepancy", assertion.Violations, 0, assertion.Violations, "Observed exact violations."));
        }
        if (criterion.Check == "reference-support")
        {
            if (analysis?.Comparison is { UnexpectedObservations: > 0 } comparison)
                return Row("discrepancy", "Observed values lie outside the authored reference support.", check: new("reference-support", "discrepancy", comparison.UnexpectedObservations, 0, null, "Unexpected support observations."));
            if (analysis is null || !analysis.SupportComplete || analysis.Comparison is null)
                return Row("insufficient", "Full empirical support comparison is unavailable or its retention budget was exceeded.");
        }
        if (!completed || snapshot.Count < criterion.MinimumCount)
            return Row("insufficient", completed ? "The required matching count was not reached. Excluded observations do not count." : "A running, cancelled or interrupted prefix cannot satisfy the final profile.");
        if (criterion.Check != "mean-equivalence")
            return Row("noObservedViolations", "The completed required population meets this observed-case check; unobserved cases are not proven.");
        if (analysis is null) return Row("insufficient", "The required uncertainty evidence is unavailable.");
        var ratio = ReferenceSemantics.Ratio(options!);
        var booleanValue = ReferenceSemantics.BooleanValue(options);
        var probability = ReferenceSemantics.Probability(options);
        var interval = ratio ? analysis.Pair?.RatioInterval : probability ? (booleanValue ? analysis.SequentialMeanInterval : null) ?? analysis.ProbabilityInterval
            : analysis.SequentialMeanInterval ?? analysis.MeanInterval ?? analysis.ClusteredMeanInterval;
        if (interval is null) return Row("insufficient", "No supported interval is available for the declared reference statistic.");
        if (!double.IsFinite(interval.Lower) || !double.IsFinite(interval.Upper) || interval.Lower > interval.Upper)
            return Row("invalid", "The uncertainty interval is nonfinite or unordered.");
        var reference = options.ReferenceMean!.Value; var tolerance = options.Tolerance!.Value;
        var within = interval.Lower >= reference - tolerance && interval.Upper <= reference + tolerance;
        var disjoint = interval.Upper < reference - tolerance || interval.Lower > reference + tolerance;
        var verdict = within ? "withinPrecision" : disjoint ? "discrepancy" : "insufficient";
        return Row(verdict, within ? "The complete uncertainty interval is inside the pinned tolerance." : disjoint ? "The uncertainty interval is outside the pinned tolerance." : "Compatible evidence is too wide for the declared precision.", interval,
            analysis.Checks.SingleOrDefault(c => c.Id == "mean-equivalence"));
    }
}
