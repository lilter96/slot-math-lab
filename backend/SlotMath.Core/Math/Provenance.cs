namespace SlotMath.Core.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  Provenance — tags every metric value with its computation path
//
//  Exact:        fully exact rational computation, no pruning (ε = 0).
//  ExactWithinEpsilon: exact path with ε-pruning; interval bounds carry
//                prunedMass and a reported [lo, hi] interval.
//  Sampled:      Monte Carlo estimate with n, stdErr, and CI95.
//
//  Invariant: No metric value is ever returned without a provenance tag.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// How a metric value was computed — the provenance of every number.
/// </summary>
public enum Provenance
{
    /// <summary>Fully exact rational computation, no pruning applied.</summary>
    Exact,

    /// <summary>
    /// Exact path with ε-pruning.  The value is an interval [lo, hi]
    /// and the pruned probability mass is tracked.
    /// </summary>
    ExactWithinEpsilon,

    /// <summary>Monte Carlo estimate from N independent samples.</summary>
    Sampled
}

/// <summary>
/// A metric value annotated with its provenance and optional confidence
/// / error bounds (stdErr, CI95 for sampled; interval bounds for ε-pruned).
/// </summary>
public sealed record ProvenanceTag(Provenance Provenance)
{
    /// <summary>Human-readable label for display.</summary>
    public string Label => Provenance switch
    {
        Provenance.Exact => "Exact",
        Provenance.ExactWithinEpsilon => "Exact (±ε)",
        Provenance.Sampled => "Sampled",
        _ => "Unknown"
    };

    public static ProvenanceTag Exact { get; } = new(Provenance.Exact);
    public static ProvenanceTag ExactWithinEpsilon { get; } = new(Provenance.ExactWithinEpsilon);
    public static ProvenanceTag Sampled { get; } = new(Provenance.Sampled);
}

/// <summary>
/// A provenance-tagged metric value of type T.  Every numeric metric
/// carries a provenance tag so consumers never mix exact and sampled
/// numbers silently.
/// </summary>
public sealed record Metric<T>(T Value, ProvenanceTag Provenance)
{
    public static Metric<T> ExactValue(T value) => new(value, ProvenanceTag.Exact);
    public static Metric<T> ExactWithinEpsilonValue(T value) => new(value, ProvenanceTag.ExactWithinEpsilon);
    public static Metric<T> SampledValue(T value) => new(value, ProvenanceTag.Sampled);

    public override string ToString() => $"{Value} [{Provenance.Label}]";
}
