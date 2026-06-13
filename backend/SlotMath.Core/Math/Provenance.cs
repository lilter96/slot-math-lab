namespace SlotMath.Core.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  Provenance — tags every metric value with its computation path (PRD v3.1, D5)
//
//  Exact:             no pruning, no truncation mass, no plugins — the value is
//                     the exact rational for the (capped) game.
//  ExactInterval:     ε-pruning and/or loop-cap truncation occurred; the value
//                     is a guaranteed enclosure [lo, hi] with width bounded by
//                     prunedMass × declared win cap.  boundSource records where
//                     the finite win bound came from.
//  ExactWithMassLoss: exact calculation with mass loss where the max remaining
//                     win cannot be proven/bounded.  NON-REGULATORY.
//  Sampled:           Monte Carlo estimate { n, mean, stdErr, ci95, capHits,
//                     loopCapHits }.
//
//  Aggregation rule (no silent mixing): Sampled dominates, then mass loss,
//  then interval, else Exact.
//
//  Invariant: No metric value is ever returned without a provenance tag.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>How a metric value was computed — the provenance kind (D5).</summary>
public enum Provenance
{
    /// <summary>Fully exact rational computation, no pruning or truncation applied.</summary>
    Exact,

    /// <summary>
    /// Exact path with ε-pruning and/or loop-cap truncation.  The value is a
    /// guaranteed enclosure [lo, hi]; pruned probability mass is tracked.
    /// </summary>
    ExactInterval,

    /// <summary>
    /// Exact calculation with mass loss whose contribution to the result cannot
    /// be bounded (no provable max remaining win).  Non-regulatory.
    /// </summary>
    ExactWithMassLoss,

    /// <summary>Monte Carlo estimate from N independent samples.</summary>
    Sampled
}

/// <summary>
/// The provenance of the finite win bound used to size an <see cref="Provenance.ExactInterval"/>
/// enclosure (D5).
/// </summary>
public enum BoundSource
{
    /// <summary>No bound (used for Exact / Sampled / mass-loss).</summary>
    None,

    /// <summary>The bound is a proven maximum remaining win.</summary>
    ProvenMaxWin,

    /// <summary>The bound is the game's declared round win cap (D19).</summary>
    DeclaredWinCap,

    /// <summary>The bound is a user-supplied cap.</summary>
    UserCap
}

/// <summary>
/// A metric value annotated with its provenance and optional confidence /
/// error bounds: interval bounds (lo, hi, prunedMass, boundSource) for the
/// exact paths, and (n, mean, stdErr, ci95, capHits, loopCapHits) for sampled.
/// </summary>
public sealed record ProvenanceTag
{
    /// <summary>The provenance kind.</summary>
    public Provenance Provenance { get; }

    // ── ExactInterval / ExactWithMassLoss fields ─────────────────────────
    /// <summary>Lower bound of the enclosure (display). Null unless an interval.</summary>
    public double? Lo { get; init; }

    /// <summary>Upper bound of the enclosure (display). Null unless an interval.</summary>
    public double? Hi { get; init; }

    /// <summary>Pruned / truncated probability mass (display). Null when exact/sampled.</summary>
    public double? PrunedMass { get; init; }

    /// <summary>Provenance of the finite win bound used to size the interval (D5).</summary>
    public BoundSource BoundSource { get; init; } = BoundSource.None;

    // ── Sampled fields ───────────────────────────────────────────────────
    /// <summary>Number of Monte Carlo rounds. Null unless sampled.</summary>
    public long? N { get; init; }

    /// <summary>Sample mean. Null unless sampled.</summary>
    public double? Mean { get; init; }

    /// <summary>Standard error of the mean. Null unless sampled.</summary>
    public double? StdErr { get; init; }

    /// <summary>95% CI half-width. Null unless sampled.</summary>
    public double? Ci95 { get; init; }

    /// <summary>Win-cap hits observed (sampled). Null unless sampled.</summary>
    public long? CapHits { get; init; }

    /// <summary>Loop-cap hits observed (sampled). Null unless sampled.</summary>
    public long? LoopCapHits { get; init; }

    public ProvenanceTag(Provenance provenance) => Provenance = provenance;

    /// <summary>Human-readable label for display.</summary>
    public string Label => Provenance switch
    {
        Provenance.Exact => "Exact",
        Provenance.ExactInterval => "Exact (±ε)",
        Provenance.ExactWithMassLoss => "Exact (mass loss, non-regulatory)",
        Provenance.Sampled => "Sampled",
        _ => "Unknown"
    };

    /// <summary>
    /// Whether this provenance may back a regulatory certification result.
    /// <see cref="Provenance.ExactWithMassLoss"/> and <see cref="Provenance.Sampled"/>
    /// are not regulatory without additional review (D5).
    /// </summary>
    public bool IsRegulatory => Provenance is Provenance.Exact or Provenance.ExactInterval;

    public static ProvenanceTag Exact { get; } = new(Provenance.Exact);
    public static ProvenanceTag ExactInterval { get; } = new(Provenance.ExactInterval);
    public static ProvenanceTag ExactWithMassLoss { get; } = new(Provenance.ExactWithMassLoss);
    public static ProvenanceTag Sampled { get; } = new(Provenance.Sampled);

    /// <summary>Build a fully-described <see cref="Provenance.ExactInterval"/> tag.</summary>
    public static ProvenanceTag Interval(double lo, double hi, double prunedMass, BoundSource boundSource) =>
        new(Provenance.ExactInterval) { Lo = lo, Hi = hi, PrunedMass = prunedMass, BoundSource = boundSource };

    /// <summary>Build a fully-described <see cref="Provenance.ExactWithMassLoss"/> tag.</summary>
    public static ProvenanceTag MassLoss(double prunedMass) =>
        new(Provenance.ExactWithMassLoss) { PrunedMass = prunedMass };

    /// <summary>Build a fully-described <see cref="Provenance.Sampled"/> tag.</summary>
    public static ProvenanceTag SampledWith(
        long n, double mean, double stdErr, double ci95, long capHits = 0, long loopCapHits = 0) =>
        new(Provenance.Sampled)
        {
            N = n,
            Mean = mean,
            StdErr = stdErr,
            Ci95 = ci95,
            CapHits = capHits,
            LoopCapHits = loopCapHits
        };

    // ── D5 aggregation rule (no silent mixing) ───────────────────────────

    /// <summary>
    /// Aggregate component provenances per D5: if any is Sampled the result is
    /// Sampled; else if any carries unbounded mass loss it is ExactWithMassLoss;
    /// else if any carries bounded pruned/truncated mass it is ExactInterval;
    /// else Exact.
    /// </summary>
    public static Provenance Aggregate(IEnumerable<Provenance> parts)
    {
        var sampled = false;
        var massLoss = false;
        var interval = false;
        foreach (var p in parts)
        {
            switch (p)
            {
                case Provenance.Sampled: sampled = true; break;
                case Provenance.ExactWithMassLoss: massLoss = true; break;
                case Provenance.ExactInterval: interval = true; break;
            }
        }

        if (sampled) return Provenance.Sampled;
        if (massLoss) return Provenance.ExactWithMassLoss;
        if (interval) return Provenance.ExactInterval;
        return Provenance.Exact;
    }

    /// <inheritdoc cref="Aggregate(IEnumerable{Provenance})"/>
    public static Provenance Aggregate(params Provenance[] parts) =>
        Aggregate((IEnumerable<Provenance>)parts);
}

/// <summary>
/// A provenance-tagged metric value of type T.  Every numeric metric carries a
/// provenance tag so consumers never mix exact and sampled numbers silently.
/// </summary>
public sealed record Metric<T>(T Value, ProvenanceTag Provenance)
{
    public static Metric<T> ExactValue(T value) => new(value, ProvenanceTag.Exact);
    public static Metric<T> ExactIntervalValue(T value) => new(value, ProvenanceTag.ExactInterval);
    public static Metric<T> ExactWithMassLossValue(T value) => new(value, ProvenanceTag.ExactWithMassLoss);
    public static Metric<T> SampledValue(T value) => new(value, ProvenanceTag.Sampled);

    public override string ToString() => $"{Value} [{Provenance.Label}]";
}
