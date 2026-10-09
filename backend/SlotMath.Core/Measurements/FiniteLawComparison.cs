using SlotMath.Core.Math;

namespace SlotMath.Core.Measurements;

public sealed record ExactLawComparisonRequest(RationalOutcome[] Left, RationalOutcome[] Right, string Unit = "value units");
public sealed record RationalLawDifference(string Value, string LeftProbability, string RightProbability, string Difference);
public sealed record ExactLawComparisonReport(bool Equal, string LeftMean, string RightMean, string MeanDifference,
    string TotalVariation, string CdfDistance, RationalLawDifference[] Support, string Unit, string Assumptions)
{
    public string? AuthoredInputSha256 { get; init; }
    public string? AlgorithmVersion { get; init; }
    public string? CoreBinarySha256 { get; init; }
}

public static partial class FiniteModelAnalysis
{
    /// <summary>Rational equality of complete independently supplied scalar laws.
    /// Zero-mass atoms and textual rational representations cannot create a false
    /// discrepancy. Partial laws are rejected rather than silently renormalized.</summary>
    public static ExactLawComparisonReport Compare(ExactLawComparisonRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Unit is null || request.Unit.Length > 24) throw new ArgumentException("Use a compatible scalar unit of at most 24 characters.");
        var left = Law(request.Left, cancellationToken); var right = Law(request.Right, cancellationToken);
        var support = left.Keys.Union(right.Keys).Order().ToArray();
        Rational ml = Rational.Zero, mr = Rational.Zero, absolute = Rational.Zero, cumulative = Rational.Zero, distance = Rational.Zero;
        var rows = new List<RationalLawDifference>(support.Length);
        foreach (var value in support)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var p = left.GetValueOrDefault(value, Rational.Zero); var q = right.GetValueOrDefault(value, Rational.Zero); var delta = p - q;
            ml = Budget(ml + value * p); mr = Budget(mr + value * q);
            absolute = Budget(absolute + (delta < Rational.Zero ? -delta : delta)); cumulative = Budget(cumulative + delta);
            var effect = cumulative < Rational.Zero ? -cumulative : cumulative; distance = Max(distance, effect);
            rows.Add(new(value.ToString(), p.ToString(), q.ToString(), delta.ToString()));
        }
        return new(absolute == Rational.Zero, ml.ToString(), mr.ToString(), Budget(ml - mr).ToString(), Budget(absolute / 2).ToString(), distance.ToString(), rows.ToArray(), request.Unit,
            "Complete normalized scalar laws with compatible subjects and units, supplied independently. Exact rational equality, total variation and discrete CDF distance compare the full union of positive support. Mean agreement alone does not establish law equality. These results do not establish that either law describes the compiled game.");
    }
    private static SortedDictionary<Rational, Rational> Law(RationalOutcome[] outcomes, CancellationToken token)
    {
        if (outcomes is not { Length: > 0 and <= 1024 }) throw new ArgumentException("Each law requires 1–1024 exact outcome/probability atoms.");
        var law = new SortedDictionary<Rational, Rational>(); var seen = new HashSet<Rational>(); var mass = Rational.Zero;
        foreach (var atom in outcomes)
        {
            token.ThrowIfCancellationRequested();
            if (atom is null) throw new ArgumentException("Law atoms cannot be null.");
            var value = Parse(atom.Value); var p = Parse(atom.Probability);
            if (p < Rational.Zero || p > Rational.One || !seen.Add(value)) throw new ArgumentException("Use unique exact outcomes and probabilities in [0,1].");
            mass = Budget(mass + p); if (p > Rational.Zero) law.Add(value, p);
        }
        if (mass != Rational.One) throw new ArgumentException("Both laws must sum to one exactly; incomplete probability is not renormalized.");
        return law;
    }
}
