using System.Text.Json;
using System.Text.Json.Nodes;
using SlotMath.Core.Model;
using SlotMath.Core.Serialization;

namespace SlotMath.Core.Measurements;

public sealed record ComponentAccountingRequest(string Name, string TotalMeasurementId, string[] ComponentMeasurementIds, string ResidualMeasurementId, string? GroupKey = null);
public sealed record AccountingComponent(string MeasurementId, string Name, long Count, double? Mean, double? Sum, double? SampleVariance, double? PaidTurnoverContribution);
public sealed record AccountingCovariance(string LeftMeasurementId, string RightMeasurementId, string PairMeasurementId, double? Covariance);
public sealed record ComponentAccountingReport(string Name, string Status, string Unit, string? NodeId, string? GroupKey, long Count,
    AccountingComponent[] Components, AccountingCovariance[] Covariances, double? TotalMean, double? TotalSampleVariance,
    double? ComponentMeanSum, double? ComponentVarianceSum, double? TwiceCovarianceSum, double? ReconstructedVariance,
    double? MeanResidual, double? VarianceResidual, double? NumericalTolerance, long? ExactViolations, string Detail)
{
    public string AlgorithmVersion { get; init; } = "component-accounting-v1";
}

/// <summary>Algebraic reconciliation of a common, unweighted observed population.
/// The numeric identity is descriptive. The separately authored exact residual
/// checks each included observation before report conversion; neither proves a
/// model's complete population law.</summary>
public static class ComponentAccounting
{
    public static Expression ValueExpression(MeasurementDefinition definition) => definition.Value ?? new FieldAccessExpr { Target = "measurement", Path = ["payout"] };
    public static Expression ResidualExpression(Expression total, IReadOnlyList<Expression> components)
    {
        if (components.Count is < 2 or > 6) throw new ArgumentException("Choose 2–6 payout components.");
        var sum = components[0];
        for (var i = 1; i < components.Count; i++) sum = new BinaryExpr { Op = BinaryOp.Add, Left = sum, Right = components[i] };
        return new BinaryExpr { Op = BinaryOp.Sub, Left = total, Right = sum };
    }

    public static ComponentAccountingReport Calculate(ComponentAccountingRequest request, IReadOnlyList<MeasurementDefinition> plan, IReadOnlyList<MeasurementSnapshot> snapshots)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 64 || request.ComponentMeasurementIds is not { Length: >= 2 and <= 6 }
            || request.GroupKey is { Length: > 128 }) throw new ArgumentException("Name the reconciliation (up to 64 characters) and choose 2–6 components; cohort keys are at most 128 characters.");
        var requested = request.ComponentMeasurementIds.Append(request.TotalMeasurementId).Append(request.ResidualMeasurementId).ToArray();
        if (requested.Any(string.IsNullOrWhiteSpace) || requested.Distinct(StringComparer.Ordinal).Count() != requested.Length)
            throw new ArgumentException("Total, component and residual IDs must be distinct.");
        MeasurementDefinition Definition(string id) => plan.FirstOrDefault(d => d.Id == id) ?? throw new ArgumentException($"Measurement '{id}' is not in the pinned plan.");
        var total = Definition(request.TotalMeasurementId); var residual = Definition(request.ResidualMeasurementId);
        var components = request.ComponentMeasurementIds.Select(Definition).ToArray();
        foreach (var definition in components.Append(total).Append(residual)) RequireScope(total, definition);
        if (residual.Options!.Assertion != "zero" || !SameExpression(residual.Value, ResidualExpression(ValueExpression(total), components.Select(ValueExpression).ToArray())))
            throw new ArgumentException("The residual must be an exact zero assertion of total minus the ordered component sum, on the same observation population.");
        if (components.Append(total).Append(residual).Any(d => d.Options!.Pair is not null)) throw new ArgumentException("Component, total and residual measurements must have a single primary value; use separate pair measurements for covariance.");
        var pairs = new List<(int Left, int Right, MeasurementDefinition Definition)>();
        for (var i = 0; i < components.Length; i++) for (var j = i + 1; j < components.Length; j++)
        {
            var pair = plan.FirstOrDefault(d => d.Options?.Pair is not null && SameScope(total, d)
                && SameExpression(ValueExpression(d), ValueExpression(components[i])) && SameExpression(d.Options.Pair, ValueExpression(components[j])));
            // Reversed pairs have the same covariance and are equally valid.
            pair ??= plan.FirstOrDefault(d => d.Options?.Pair is not null && SameScope(total, d)
                && SameExpression(ValueExpression(d), ValueExpression(components[j])) && SameExpression(d.Options.Pair, ValueExpression(components[i])));
            if (pair is null) throw new ArgumentException($"Collect a matching paired observation for '{components[i].Name}' and '{components[j].Name}' before launching.");
            pairs.Add((i, j, pair));
        }
        var definitions = components.Append(total).Append(residual).Concat(pairs.Select(p => p.Definition)).DistinctBy(d => d.Id).ToArray();
        var values = definitions.ToDictionary(d => d.Id, d => snapshots.FirstOrDefault(s => s.Id == d.Id) ?? throw new ArgumentException($"Snapshot '{d.Id}' is missing."), StringComparer.Ordinal);
        MeasurementAnalysis? Analysis(MeasurementDefinition d) => request.GroupKey is null ? values[d.Id].Analysis
            : values[d.Id].Analysis?.Groups.GetValueOrDefault(request.GroupKey);
        var totalAnalysis = Analysis(total);
        var count = totalAnalysis?.Count ?? 0;
        double? Contribution(MeasurementAnalysis? a) => a?.Sum is { } sum && a.Normalization?.ExternalTurnover is > 0
            ? Finite(sum / a.Normalization.ExternalTurnover.Value) : null;
        var rows = components.Select(d => { var a = Analysis(d); return new AccountingComponent(d.Id, d.Name, a?.Count ?? 0, Finite(a?.Mean), Finite(a?.Sum), Finite(a?.Moments.SampleVariance), Contribution(a)); }).ToArray();
        var covarianceRows = pairs.Select(p => new AccountingCovariance(components[p.Left].Id, components[p.Right].Id, p.Definition.Id, Finite(Analysis(p.Definition)?.Pair?.Covariance))).ToArray();
        var exact = Analysis(residual)?.Assertion;
        ComponentAccountingReport Report(string status, string detail, double? means = null, double? variances = null, double? covariance = null,
            double? reconstructed = null, double? meanResidual = null, double? varianceResidual = null, double? tolerance = null) => new(request.Name, status,
                total.Unit, total.NodeId, request.GroupKey, count, rows, covarianceRows, Finite(totalAnalysis?.Mean), Finite(totalAnalysis?.Moments.SampleVariance),
                means, variances, covariance, reconstructed, meanResidual, varianceResidual, tolerance, exact?.Violations, detail);
        if (definitions.All(d => values[d.Id] is { Count: 0, Errors: 0, Analysis: null }))
            return Report("insufficient", "The selected observation points have no included observations; means and variances are undefined.");
        if (definitions.Any(d => values[d.Id].Errors > 0 || values[d.Id].Analysis is null || values[d.Id].Analysis is { DuplicateAwards: > 0 } or { UnclosedEpisodes: > 0 }
            || request.GroupKey is not null && !values[d.Id].Analysis!.GroupsComplete))
            return Report("invalid", "Invalid observations, unavailable analysis or incomplete cohort coverage prevent reconciliation.");
        if (definitions.Any(d => values[d.Id].Count != values[d.Id].Analysis!.Count || !Near(values[d.Id].Mean, values[d.Id].Analysis!.Mean)
            || values[d.Id].Count > 0 && !Near(values[d.Id].Sum, values[d.Id].Analysis!.Sum)))
            return Report("invalid", "Basic and advanced observations disagree; the saved evidence is inconsistent.");
        if (definitions.Any(d => (Analysis(d)?.Count ?? 0) != count) || exact is not null && exact.Checked != count)
            return Report("invalid", "Total, components, pair observations and exact residual have different included populations.");
        if (count == 0) return Report("insufficient", "The selected population has no included observations; means and variances are undefined.");
        if (definitions.Any(d => Analysis(d)?.Mean is not { } mean || !double.IsFinite(mean) || Analysis(d)?.Sum is not { } sum || !double.IsFinite(sum)))
            return Report("invalid", "Finite complete observed means and sums are required.");
        if (exact is null || exact.Kind != "zero") return Report("invalid", "The exact per-observation residual assertion is unavailable.");
        if (exact.Violations < 0 || exact.Violations > count || definitions.Any(d => Analysis(d)?.Moments.SampleVariance is < 0))
            return Report("invalid", "Negative sample variance or impossible exact assertion counts invalidate the evidence.");
        if (pairs.Any(p => Analysis(p.Definition)?.Pair?.Count != count)) return Report("invalid", "Paired covariance exposure does not match the component population.");
        // The pair's observed marginals must match its corresponding components,
        // not merely their count. A corrupt or mismatched snapshot is withheld.
        foreach (var pair in pairs)
        {
            var a = Analysis(pair.Definition)!; var left = Analysis(components[pair.Left])!; var right = Analysis(components[pair.Right])!;
            var reversed = !SameExpression(ValueExpression(pair.Definition), ValueExpression(components[pair.Left]));
            var x = reversed ? right : left; var y = reversed ? left : right;
            if (!Near(a.Mean, x.Mean) || !Near(a.Sum, x.Sum) || !Near(a.Moments.SampleVariance, x.Moments.SampleVariance)
                || !Near(a.Pair!.MeanY, y.Mean) || !Near(a.Pair.SumY, y.Sum) || !Near(a.Pair.SampleVarianceY, y.Moments.SampleVariance))
                return Report("invalid", "The observed pair marginals disagree with their component evidence.");
        }
        var meanSum = Sum(rows.Select(r => r.Mean)); var meanDifference = Finite(totalAnalysis!.Mean!.Value - (meanSum ?? double.NaN));
        if (count < 2) return Report(exact.Violations > 0 ? "discrepancy" : "insufficient", "At least two matching observations are required for sample covariance and variance.", meanSum, meanResidual: meanDifference);
        var diagonal = Sum(rows.Select(r => r.SampleVariance)); var cross = Finite(2 * (Sum(covarianceRows.Select(r => r.Covariance)) ?? double.NaN));
        var reconstructed = diagonal is { } v && cross is { } c ? Finite(v + c) : null;
        var varianceResidual = reconstructed is { } reconstructedValue && totalAnalysis.Moments.SampleVariance is { } actual ? Finite(actual - reconstructedValue) : null;
        var scale = Sum(rows.Select(r => r.SampleVariance is { } value ? (double?)System.Math.Abs(value) : null)
            .Concat(covarianceRows.Select(r => r.Covariance is { } value ? Finite(2 * System.Math.Abs(value)) : null)));
        var tolerance = totalAnalysis.Moments.SampleVariance is { } totalVariance && scale is { } conditionScale
            ? 1e-10 * System.Math.Max(1, System.Math.Max(System.Math.Abs(totalVariance), conditionScale)) : (double?)null;
        if (meanSum is null || meanDifference is null || diagonal is null || cross is null || reconstructed is null || varianceResidual is null || tolerance is null)
            return Report("invalid", "Finite complete means, covariances and sample variances are required.");
        var meanScale = Sum(rows.Select(r => r.Mean is { } value ? (double?)System.Math.Abs(value) : null));
        if (meanScale is null) return Report("invalid", "Component cancellation exceeds finite numeric range.");
        var meanTolerance = 1e-10 * System.Math.Max(1, System.Math.Max(System.Math.Abs(totalAnalysis.Mean.Value), meanScale.Value));
        var discrepancy = exact.Violations > 0 || System.Math.Abs(varianceResidual.Value) > tolerance || System.Math.Abs(meanDifference.Value) > meanTolerance;
        if (!discrepancy && (varianceResidual != 0 && tolerance > 1e-6 * System.Math.Max(1, System.Math.Abs(totalAnalysis.Moments.SampleVariance!.Value))
            || meanDifference != 0 && meanTolerance > 1e-6 * System.Math.Max(1, System.Math.Abs(totalAnalysis.Mean.Value))))
            return Report("numericalResolution", "The exact residual has no observed violations, but cancellation between large components prevents a meaningful IEEE754 reconstruction verdict.",
                meanSum, diagonal, cross, reconstructed, meanDifference, varianceResidual, tolerance);
        return Report(discrepancy ? "discrepancy" : "noObservedViolations",
            "Observed variance = component variances + twice all pair covariances. Numeric reconciliation uses roundoff tolerance 1e-10 times the absolute component/covariance scale; the exact residual independently counts each included mismatch before report conversion. This is a sample ledger check, with no population-law or independence guarantee.",
            meanSum, diagonal, cross, reconstructed, meanDifference, varianceResidual, tolerance);
    }

    private static bool SameScope(MeasurementDefinition first, MeasurementDefinition second) => first.Options is { Subject: "observation", Source: "value", Weight: null, PairRole: "value" } a
        && second.Options is { Subject: "observation", Source: "value", Weight: null, PairRole: "value" } b
        && first.NodeId == second.NodeId && first.Unit == second.Unit && a.Stake == b.Stake && SameExpression(first.Filter, second.Filter) && SameExpression(a.Group, b.Group)
        && SameExpression(a.AwardId, b.AwardId);
    private static void RequireScope(MeasurementDefinition first, MeasurementDefinition second)
    {
        if (!SameScope(first, second) || second.NodeId is not null && second.Value is null) throw new ArgumentException("Use the same numeric, unweighted observation point, filter, cohort key, units and external cost for total, components and pairs. Reduce a feature into state at its completed boundary before reconciling it.");
    }
    private static bool SameExpression(Expression? first, Expression? second)
    {
        JsonNode? Clean(JsonNode? node)
        {
            if (node is JsonObject obj) { obj.Remove("annotation"); foreach (var key in obj.Select(p => p.Key).ToArray()) obj[key] = Clean(obj[key]?.DeepClone()); }
            else if (node is JsonArray array) for (var i = 0; i < array.Count; i++) array[i] = Clean(array[i]?.DeepClone());
            return node;
        }
        return JsonNode.DeepEquals(Clean(JsonSerializer.SerializeToNode(first, JsonOptions.Default)), Clean(JsonSerializer.SerializeToNode(second, JsonOptions.Default)));
    }
    private static double? Sum(IEnumerable<double?> numbers)
    {
        double sum = 0, correction = 0;
        foreach (var number in numbers) { if (number is not { } value || !double.IsFinite(value)) return null; var next = value - correction; var total = sum + next; correction = (total - sum) - next; sum = total; }
        return Finite(sum);
    }
    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
    private static double? Finite(double? value) => value is { } number ? Finite(number) : null;
    private static bool Near(double? first, double? second) => first == second || first is { } a && second is { } b && System.Math.Abs(a - b) <= 1e-10 * System.Math.Max(1, System.Math.Max(System.Math.Abs(a), System.Math.Abs(b)));
}
