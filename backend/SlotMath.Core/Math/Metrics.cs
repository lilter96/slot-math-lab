using System.Numerics;

namespace SlotMath.Core.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  G7 Metric types — every value is provenance-tagged
//
//  Each metric is computable from BOTH the exact distribution (Dist<BigInteger>)
//  and the sampled statistics (StreamingStats / SampledResult).
//
//  On the exact path: rational representation preserved; display float is
//  secondary and never used to derive RTP or other ratios.
// ═══════════════════════════════════════════════════════════════════════════

// ── RTP (Return To Player) ─────────────────────────────────────────────

/// <summary>
/// RTP (Return To Player) metric.  On the exact path, carries both the
/// exact rational (Num/Den) and a display-friendly double.  On the sampled
/// path, carries the mean with stdErr and CI95.
/// </summary>
public sealed record RtpMetric
{
    /// <summary>Provenance of this RTP value.</summary>
    public ProvenanceTag Provenance { get; init; }

    // ── Rational (exact path only — null on sampled) ──────────────────
    /// <summary>Exact rational numerator.  Null when provenance is Sampled.</summary>
    public BigInteger? RationalNumerator { get; init; }

    /// <summary>Exact rational denominator.  Null when provenance is Sampled.</summary>
    public BigInteger? RationalDenominator { get; init; }

    // ── Display float ─────────────────────────────────────────────────
    /// <summary>RTP as a display-friendly double.  Always present.</summary>
    public double DisplayValue { get; init; }

    // ── Interval (ExactInterval only) ────────────────────────────
    /// <summary>Lower bound of the ε-interval (display). Null when not ε-pruned.</summary>
    public double? LoDisplay { get; init; }

    /// <summary>Upper bound of the ε-interval (display). Null when not ε-pruned.</summary>
    public double? HiDisplay { get; init; }

    /// <summary>Pruned probability mass (display). Null when not ε-pruned.</summary>
    public double? PrunedMass { get; init; }

    // ── Sampled fields (Sampled only) ─────────────────────────────────
    /// <summary>Number of Monte Carlo spins. Null when exact.</summary>
    public long? SampleCount { get; init; }

    /// <summary>Standard error of the mean. Null when exact.</summary>
    public double? StdErr { get; init; }

    /// <summary>95% CI half-width. Null when exact.</summary>
    public double? Ci95Half { get; init; }

    public RtpMetric(ProvenanceTag provenance) => Provenance = provenance;

    /// <summary>Human-readable summary.</summary>
    public override string ToString()
    {
        var p = Provenance.Label;
        if (RationalNumerator.HasValue)
            return $"RTP={RationalNumerator}/{RationalDenominator} ≈ {DisplayValue:F6} [{p}]";
        return $"RTP={DisplayValue:F6} ± {Ci95Half:F6} [{p}, n={SampleCount}]";
    }
}

// ── Hit Frequency ──────────────────────────────────────────────────────

/// <summary>
/// Probability of a non-zero win — the fraction of spins that return
/// at least one unit.  On the exact path this is an exact rational
/// probability.  On the sampled path it is nonZeroSpins / totalSpins.
/// </summary>
public sealed record HitFrequencyMetric
{
    public ProvenanceTag Provenance { get; }

    /// <summary>Exact rational numerator. Null when sampled.</summary>
    public BigInteger? RationalNumerator { get; init; }

    /// <summary>Exact rational denominator. Null when sampled.</summary>
    public BigInteger? RationalDenominator { get; init; }

    /// <summary>Display-friendly value in [0, 1].</summary>
    public double DisplayValue { get; init; }

    /// <summary>Sample count (sampled only).</summary>
    public long? SampleCount { get; init; }

    /// <summary>Standard error for a proportion: sqrt(p*(1-p)/n) (sampled only).</summary>
    public double? StdErr { get; init; }

    public HitFrequencyMetric(ProvenanceTag provenance) => Provenance = provenance;

    public override string ToString()
    {
        if (RationalNumerator.HasValue)
            return $"HitFreq={RationalNumerator}/{RationalDenominator} ≈ {DisplayValue:P4} [{Provenance.Label}]";
        return $"HitFreq={DisplayValue:P4} [{Provenance.Label}, n={SampleCount}]";
    }
}

// ── Volatility ─────────────────────────────────────────────────────────

/// <summary>
/// Volatility metrics: variance, standard deviation, and volatility index
/// (coefficient of variation = σ / μ).  On the exact path these are computed
/// from the full distribution (not a sample).  On the sampled path they are
/// from the Welford accumulators.
/// </summary>
public sealed record VolatilityMetric
{
    public ProvenanceTag Provenance { get; }

    /// <summary>Variance of the win distribution.</summary>
    public double Variance { get; init; }

    /// <summary>Standard deviation.</summary>
    public double StdDev { get; init; }

    /// <summary>
    /// Volatility index = StdDev / Mean (coefficient of variation).
    /// Null when mean is zero (undefined).
    /// </summary>
    public double? VolatilityIndex { get; init; }

    /// <summary>Sample count (sampled only).</summary>
    public long? SampleCount { get; init; }

    public VolatilityMetric(ProvenanceTag provenance) => Provenance = provenance;

    public override string ToString()
    {
        var vi = VolatilityIndex.HasValue ? $"{VolatilityIndex:F4}" : "n/a";
        return $"Vol[σ²={Variance:F4}, σ={StdDev:F4}, VI={vi}] [{Provenance.Label}]";
    }
}

// ── Max Win ────────────────────────────────────────────────────────────

/// <summary>
/// Maximum possible win and the probability of reaching the cap.
/// On the exact path: max value in the distribution and its probability mass.
/// On the sampled path: max observed and cap-hit count / total spins.
/// </summary>
public sealed record MaxWinMetric
{
    public ProvenanceTag Provenance { get; }

    /// <summary>
    /// Maximum win value.  On the exact path: the largest outcome in the
    /// distribution.  On the sampled path: the largest observed value
    /// (clamped to the cap if a cap is set).
    /// </summary>
    public double MaxWin { get; init; }

    /// <summary>
    /// Maximum win as exact BigInteger (exact path only).
    /// </summary>
    public BigInteger? MaxWinExact { get; init; }

    /// <summary>
    /// Probability of reaching or exceeding the cap.
    /// On the exact path: sum of probability mass for values ≥ cap.
    /// On the sampled path: capHits / totalSpins.
    /// </summary>
    public double PCapReached { get; init; }

    /// <summary>Exact rational numerator for P(cap reached) (exact path only).</summary>
    public BigInteger? PCapRationalNumerator { get; init; }

    /// <summary>Exact rational denominator for P(cap reached) (exact path only).</summary>
    public BigInteger? PCapRationalDenominator { get; init; }

    /// <summary>The cap value, or null if no cap is set.</summary>
    public double? Cap { get; init; }

    /// <summary>Number of cap hits (sampled only).</summary>
    public long? CapHits { get; init; }

    /// <summary>Sample count (sampled only).</summary>
    public long? SampleCount { get; init; }

    /// <summary>Standard error of P(cap reached) (sampled only).</summary>
    public double? PCapStdErr { get; init; }

    public MaxWinMetric(ProvenanceTag provenance) => Provenance = provenance;

    public override string ToString()
    {
        var capStr = Cap.HasValue ? $", cap={Cap}" : "";
        return $"MaxWin={MaxWin:F2}, P(cap)={PCapReached:P6}{capStr} [{Provenance.Label}]";
    }
}

// ── Win Histogram ──────────────────────────────────────────────────────

/// <summary>
/// A win-value histogram bin — range and probability mass or count.
/// </summary>
public sealed record WinHistogramBin
{
    /// <summary>Lower bound of this bin (inclusive).</summary>
    public double LowerBound { get; init; }

    /// <summary>Upper bound of this bin (exclusive for all but the last bin).</summary>
    public double UpperBound { get; init; }

    /// <summary>
    /// On the exact path: exact probability mass in this bin (rational numerator).
    /// Null on the sampled path.
    /// </summary>
    public BigInteger? ProbabilityNumerator { get; init; }

    /// <summary>
    /// On the exact path: the shared denominator for all bins.
    /// Null on the sampled path.
    /// </summary>
    public BigInteger? ProbabilityDenominator { get; init; }

    /// <summary>Probability mass as a display double.</summary>
    public double Probability { get; init; }

    /// <summary>Number of samples in this bin (sampled path only).</summary>
    public long? Count { get; init; }
}

/// <summary>
/// A complete win histogram with provenance.  On the exact path the bins
/// carry exact rational probability mass.  On the sampled path they carry
/// observation counts.
/// </summary>
public sealed record WinHistogramMetric
{
    public ProvenanceTag Provenance { get; }

    /// <summary>Histogram bins, ordered from lowest to highest.</summary>
    public WinHistogramBin[] Bins { get; init; } = Array.Empty<WinHistogramBin>();

    /// <summary>Total number of bins.</summary>
    public int BinCount => Bins.Length;

    /// <summary>Sample count (sampled only).</summary>
    public long? SampleCount { get; init; }

    public WinHistogramMetric(ProvenanceTag provenance) => Provenance = provenance;

    public override string ToString() =>
        $"Histogram[{BinCount} bins, {Provenance.Label}]";
}

// ── Per-Feature RTP Breakdown ──────────────────────────────────────────

/// <summary>
/// RTP contribution of a single named feature (e.g. "BaseGame", "FreeSpins",
/// "BonusGame", "Jackpot").  On the exact path the contributions sum to the
/// total RTP exactly (rational equality).  On the sampled path each feature's
/// mean is tracked independently.
/// </summary>
public sealed record FeatureRtpContribution
{
    /// <summary>Name of the feature.</summary>
    public required string FeatureName { get; init; }

    /// <summary>RTP contribution of this feature.</summary>
    public double RtpContribution { get; init; }

    /// <summary>Exact rational numerator (exact path only).</summary>
    public BigInteger? RationalNumerator { get; init; }

    /// <summary>Exact rational denominator (exact path only).</summary>
    public BigInteger? RationalDenominator { get; init; }

    /// <summary>Fraction of total RTP this feature represents.</summary>
    public double FractionOfTotal { get; init; }

    /// <summary>Hit frequency for this specific feature.</summary>
    public double FeatureHitFrequency { get; init; }

    /// <summary>Number of samples for this feature (sampled only).</summary>
    public long? SampleCount { get; init; }
}

/// <summary>
/// Per-feature RTP breakdown.  On the exact path: contributions carry
/// exact rational values, and the sum of all feature RTP contributions
/// equals the total RTP exactly (rational equality).
/// </summary>
public sealed record PerFeatureBreakdownMetric
{
    public ProvenanceTag Provenance { get; }

    /// <summary>Per-feature RTP contributions.</summary>
    public FeatureRtpContribution[] Features { get; init; } = Array.Empty<FeatureRtpContribution>();

    /// <summary>Sum of all feature RTP contributions (display).</summary>
    public double TotalRtpDisplay { get; init; }

    /// <summary>Exact sum numerator (exact path only).</summary>
    public BigInteger? TotalRtpRationalNumerator { get; init; }

    /// <summary>Exact sum denominator (exact path only).</summary>
    public BigInteger? TotalRtpRationalDenominator { get; init; }

    public PerFeatureBreakdownMetric(ProvenanceTag provenance) => Provenance = provenance;

    public override string ToString()
    {
        var features = string.Join(", ", Features.Select(f =>
            $"{f.FeatureName}={f.RtpContribution:F6} ({f.FractionOfTotal:P1})"));
        return $"Features[{features}] total={TotalRtpDisplay:F6} [{Provenance.Label}]";
    }
}

// ── Aggregate Slot Math Report ─────────────────────────────────────────

/// <summary>
/// Complete slot-math metric report — all G7 metrics in one structure.
/// Every sub-metric carries its own provenance tag.
/// </summary>
public sealed record SlotMathReport
{
    /// <summary>Aggregate provenance across all metrics.</summary>
    public Provenance AggregateProvenance { get; init; }

    /// <summary>Return To Player.</summary>
    public required RtpMetric Rtp { get; init; }

    /// <summary>Hit frequency.</summary>
    public required HitFrequencyMetric HitFrequency { get; init; }

    /// <summary>Volatility (variance, std dev, volatility index).</summary>
    public required VolatilityMetric Volatility { get; init; }

    /// <summary>Max win and probability of reaching cap.</summary>
    public required MaxWinMetric MaxWin { get; init; }

    /// <summary>Win-value histogram.</summary>
    public required WinHistogramMetric Histogram { get; init; }

    /// <summary>Per-feature RTP breakdown.</summary>
    public PerFeatureBreakdownMetric? PerFeatureBreakdown { get; init; }

    /// <summary>Human-readable summary.</summary>
    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Slot Math Report [{AggregateProvenance}] ===");
        sb.AppendLine($"  {Rtp}");
        sb.AppendLine($"  {HitFrequency}");
        sb.AppendLine($"  {Volatility}");
        sb.AppendLine($"  {MaxWin}");
        sb.AppendLine($"  {Histogram}");
        if (PerFeatureBreakdown != null)
            sb.AppendLine($"  {PerFeatureBreakdown}");
        return sb.ToString();
    }
}
