using System.Numerics;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  G7 — Metric reducers + provenance acceptance tests
//
//  DoD items:
//    1. Every metric computable from BOTH exact distribution and sampled stats
//    2. Per-feature contributions sum to total RTP exactly on exact path
//    3. Exact-path volatility is from the full distribution
//    4. Every metric value carries a provenance tag
//    5. RTP exact rational + display float
//    6. Hit frequency, variance/std/volatility index, max win, P(cap),
//       per-feature/per-state RTP breakdown, win histogram
// ═══════════════════════════════════════════════════════════════════════════

public record MetricTestState(int BoardValue, BigInteger Win)
{
    public BigInteger RecurrenceHash => BoardValue;
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 1 — Every metric computable from EXACT distribution
// ═══════════════════════════════════════════════════════════════════════════

public class ExactMetrics_AllMetricsComputable
{
    /// <summary>
    /// Create a known exact distribution via ExactInterpreter.
    /// Draw weights [1,2,3], payouts [0,1,2].
    /// Exact RTP = (1*0 + 2*1 + 3*2) / 6 = 8/6 = 4/3.
    /// Hit freq = (2+3)/6 = 5/6 (only payout 0 is a miss).
    /// </summary>
    private static Dist<BigInteger> CreateKnownDist()
    {
        var program =
            from idx in Slot.Draw<MetricTestState>(_ => WeightSet.FromIntegers([1, 2, 3]))
            select new BigInteger(idx);

        var result = ExactInterpreter.Evaluate(
            program, new MetricTestState(0, 0), s => s.RecurrenceHash);

        return result.ValueDistribution();
    }

    [Fact]
    public void Rtp_ExactRationalAndDisplayFloat()
    {
        var dist = CreateKnownDist();
        var rtp = ExactMetrics.ComputeRtp(dist);

        // Exact rational.
        Assert.NotNull(rtp.RationalNumerator);
        Assert.NotNull(rtp.RationalDenominator);
        Assert.Equal(new BigInteger(4), rtp.RationalNumerator!.Value);
        Assert.Equal(new BigInteger(3), rtp.RationalDenominator!.Value);

        // Display float is secondary.
        Assert.Equal(4.0 / 3.0, rtp.DisplayValue, precision: 10);

        // Provenance.
        Assert.Equal(Provenance.Exact, rtp.Provenance.Provenance);
    }

    [Fact]
    public void HitFrequency_ExactRationalProbability()
    {
        var dist = CreateKnownDist();
        var hf = ExactMetrics.ComputeHitFrequency(dist);

        // P(non-zero) = (2+3)/6 = 5/6.
        Assert.Equal(new BigInteger(5), hf.RationalNumerator!.Value);
        Assert.Equal(new BigInteger(6), hf.RationalDenominator!.Value);
        Assert.Equal(5.0 / 6.0, hf.DisplayValue, precision: 10);
        Assert.Equal(Provenance.Exact, hf.Provenance.Provenance);
    }

    [Fact]
    public void Volatility_FromFullDistribution()
    {
        var dist = CreateKnownDist();
        // Outcomes: 0 (1/6), 1 (2/6), 2 (3/6).  RTP = 4/3 ≈ 1.333...
        // E[X²] = (0²*1 + 1²*2 + 2²*3) / 6 = (0 + 2 + 12) / 6 = 14/6 = 7/3 ≈ 2.333...
        // Var = E[X²] - μ² = 7/3 - (4/3)² = 7/3 - 16/9 = 21/9 - 16/9 = 5/9 ≈ 0.555...
        // σ = √(5/9) ≈ 0.745356
        // VI = σ/μ ≈ 0.745356/1.333... ≈ 0.559

        var vol = ExactMetrics.ComputeVolatility(dist, 4.0 / 3.0);

        Assert.Equal(Provenance.Exact, vol.Provenance.Provenance);
        Assert.Equal(5.0 / 9.0, vol.Variance, precision: 10);
        Assert.Equal(System.Math.Sqrt(5.0 / 9.0), vol.StdDev, precision: 10);
        Assert.NotNull(vol.VolatilityIndex);
        Assert.Equal(System.Math.Sqrt(5.0 / 9.0) / (4.0 / 3.0), vol.VolatilityIndex!.Value, precision: 10);
    }

    [Fact]
    public void MaxWin_ExactValue()
    {
        var dist = CreateKnownDist();
        var mw = ExactMetrics.ComputeMaxWin(dist);

        Assert.Equal(Provenance.Exact, mw.Provenance.Provenance);
        Assert.Equal(2.0, mw.MaxWin);
        Assert.Equal(new BigInteger(2), mw.MaxWinExact!.Value);
    }

    [Fact]
    public void PCapReached_ExactProbability()
    {
        var dist = CreateKnownDist();
        var mw = ExactMetrics.ComputeMaxWin(dist, cap: new BigInteger(2));

        // P(win >= 2) = 3/6 = 1/2.
        Assert.Equal(0.5, mw.PCapReached, precision: 10);
        Assert.Equal(new BigInteger(3), mw.PCapRationalNumerator!.Value);
        Assert.Equal(new BigInteger(6), mw.PCapRationalDenominator!.Value);
        Assert.NotNull(mw.Cap);
        Assert.Equal(2.0, mw.Cap!.Value);
    }

    [Fact]
    public void WinHistogram_HasExactProbabilities()
    {
        var dist = CreateKnownDist();
        var hist = ExactMetrics.ComputeHistogram(dist, numBins: 3);

        Assert.Equal(Provenance.Exact, hist.Provenance.Provenance);
        Assert.Equal(3, hist.BinCount);

        // Bin probabilities should sum to 1.
        var totalProb = hist.Bins.Sum(b => b.Probability);
        Assert.Equal(1.0, totalProb, precision: 10);

        // Each bin carries rational probability.
        foreach (var bin in hist.Bins)
        {
            Assert.NotNull(bin.ProbabilityNumerator);
            Assert.NotNull(bin.ProbabilityDenominator);
        }
    }

    [Fact]
    public void FullReport_AllMetricsPresent()
    {
        var dist = CreateKnownDist();
        var report = ExactMetrics.Compute(dist);

        Assert.NotNull(report.Rtp);
        Assert.NotNull(report.HitFrequency);
        Assert.NotNull(report.Volatility);
        Assert.NotNull(report.MaxWin);
        Assert.NotNull(report.Histogram);
        Assert.Equal(Provenance.Exact, report.AggregateProvenance);

        // Every sub-metric has provenance.
        Assert.Equal(Provenance.Exact, report.Rtp.Provenance.Provenance);
        Assert.Equal(Provenance.Exact, report.HitFrequency.Provenance.Provenance);
        Assert.Equal(Provenance.Exact, report.Volatility.Provenance.Provenance);
        Assert.Equal(Provenance.Exact, report.MaxWin.Provenance.Provenance);
        Assert.Equal(Provenance.Exact, report.Histogram.Provenance.Provenance);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 1b — Every metric computable from SAMPLED stats
// ═══════════════════════════════════════════════════════════════════════════

public class SampledMetrics_AllMetricsComputable
{
    private static StreamingStatsSnapshot CreateKnownStatsSnapshot()
    {
        var stats = new StreamingStats(maxWinCap: 100.0);
        // Add 100 samples: 60 zeros, 20 ones, 20 fives.
        for (var i = 0; i < 60; i++) stats.Add(0.0);
        for (var i = 0; i < 20; i++) stats.Add(1.0);
        for (var i = 0; i < 20; i++) stats.Add(5.0);

        return stats.Snapshot();
    }

    [Fact]
    public void Rtp_FromSampledStats()
    {
        var snap = CreateKnownStatsSnapshot();
        var rtp = SampledMetrics.ComputeRtp(snap);

        Assert.Equal(Provenance.Sampled, rtp.Provenance.Provenance);
        Assert.NotNull(rtp.SampleCount);
        Assert.Equal(100, rtp.SampleCount!.Value);
        Assert.NotNull(rtp.StdErr);
        Assert.NotNull(rtp.Ci95Half);

        // Mean = (60*0 + 20*1 + 20*5) / 100 = 120/100 = 1.2
        Assert.Equal(1.2, rtp.DisplayValue, precision: 10);

        // No rational on sampled path.
        Assert.Null(rtp.RationalNumerator);
        Assert.Null(rtp.RationalDenominator);
    }

    [Fact]
    public void HitFrequency_FromSampledStats()
    {
        var snap = CreateKnownStatsSnapshot();
        var hf = SampledMetrics.ComputeHitFrequency(snap);

        Assert.Equal(Provenance.Sampled, hf.Provenance.Provenance);
        Assert.Equal(0.40, hf.DisplayValue, precision: 10); // 40/100
        Assert.Equal(100, hf.SampleCount!.Value);
        Assert.NotNull(hf.StdErr);

        // No rational on sampled path.
        Assert.Null(hf.RationalNumerator);
        Assert.Null(hf.RationalDenominator);
    }

    [Fact]
    public void Volatility_FromSampledStats()
    {
        var snap = CreateKnownStatsSnapshot();
        var vol = SampledMetrics.ComputeVolatility(snap);

        Assert.Equal(Provenance.Sampled, vol.Provenance.Provenance);
        Assert.True(vol.Variance > 0);
        Assert.True(vol.StdDev > 0);
        Assert.NotNull(vol.VolatilityIndex);
        Assert.Equal(100, vol.SampleCount!.Value);
    }

    [Fact]
    public void MaxWin_FromSampledStats()
    {
        var snap = CreateKnownStatsSnapshot();
        var mw = SampledMetrics.ComputeMaxWin(snap);

        Assert.Equal(Provenance.Sampled, mw.Provenance.Provenance);
        Assert.Equal(5.0, mw.MaxWin);
        Assert.Null(mw.MaxWinExact); // No exact value on sampled path.
        Assert.NotNull(mw.Cap);
        Assert.Equal(100.0, mw.Cap!.Value);
        Assert.NotNull(mw.SampleCount);
        Assert.Equal(100, mw.SampleCount!.Value);
    }

    [Fact]
    public void WinHistogram_FromSampledStats()
    {
        var snap = CreateKnownStatsSnapshot();
        var hist = SampledMetrics.ComputeHistogram(snap);

        Assert.Equal(Provenance.Sampled, hist.Provenance.Provenance);
        Assert.True(hist.BinCount > 0);
        Assert.Equal(100, hist.SampleCount!.Value);

        // Each bin has a count but no rational probability.
        foreach (var bin in hist.Bins)
        {
            Assert.Null(bin.ProbabilityNumerator);
            Assert.Null(bin.ProbabilityDenominator);
            Assert.NotNull(bin.Count);
        }
    }

    [Fact]
    public void FullReport_AllMetricsPresent_AllSampled()
    {
        var snap = CreateKnownStatsSnapshot();
        // Rebuild a StreamingStats from snapshot (we need the live object).
        var stats = new StreamingStats(maxWinCap: 100.0);
        for (var i = 0; i < 60; i++) stats.Add(0.0);
        for (var i = 0; i < 20; i++) stats.Add(1.0);
        for (var i = 0; i < 20; i++) stats.Add(5.0);

        var report = SampledMetrics.Compute(stats);

        Assert.Equal(Provenance.Sampled, report.AggregateProvenance);
        Assert.NotNull(report.Rtp);
        Assert.NotNull(report.HitFrequency);
        Assert.NotNull(report.Volatility);
        Assert.NotNull(report.MaxWin);
        Assert.NotNull(report.Histogram);

        // Every sub-metric is sampled.
        Assert.Equal(Provenance.Sampled, report.Rtp.Provenance.Provenance);
        Assert.Equal(Provenance.Sampled, report.HitFrequency.Provenance.Provenance);
        Assert.Equal(Provenance.Sampled, report.Volatility.Provenance.Provenance);
        Assert.Equal(Provenance.Sampled, report.MaxWin.Provenance.Provenance);
        Assert.Equal(Provenance.Sampled, report.Histogram.Provenance.Provenance);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 2 — Per-feature contributions sum to total RTP exactly on exact path
// ═══════════════════════════════════════════════════════════════════════════

public class ExactMetrics_PerFeatureSumsToTotal
{
    /// <summary>
    /// Create a total distribution and two feature distributions.
    /// Total: outcomes from combined draws.
    /// Feature "Base": first draw outcomes.
    /// Feature "Bonus": second draw outcomes.
    ///
    /// Draw 1 (Base): weights [2,1] → 0 or 10.
    /// Draw 2 (Bonus): weights [1,2] → 0 or 5.
    ///
    /// Total outcomes: (0,0)=0 w=2/9, (0,5)=5 w=4/9, (10,0)=10 w=1/9, (10,5)=15 w=2/9.
    /// Total RTP = (0*2 + 5*4 + 10*1 + 15*2) / 9 = (0+20+10+30)/9 = 60/9 = 20/3.
    ///
    /// Base RTP alone = (0*2 + 10*1) / 3 = 10/3.
    /// Bonus RTP alone = (0*1 + 5*2) / 3 = 10/3.
    /// Sum = 10/3 + 10/3 = 20/3 = Total RTP. ✓
    /// </summary>
    private static (
        Dist<BigInteger> Total,
        Dictionary<string, Dist<BigInteger>> Features
    ) CreateFeatureDistributions()
    {
        var totalBuilder = new DistBuilder<BigInteger>();
        var baseBuilder = new DistBuilder<BigInteger>();
        var bonusBuilder = new DistBuilder<BigInteger>();

        // Enumerate all 4 branches.
        // Base: weight 2 for outcome 0, weight 1 for outcome 10.
        // Bonus: weight 1 for outcome 0, weight 2 for outcome 5.
        // Total denominator = 3 * 3 = 9.

        // (0,0)
        totalBuilder.Add(BigInteger.Zero, new BigInteger(2 * 1), new BigInteger(9));
        baseBuilder.Add(BigInteger.Zero, new BigInteger(2), new BigInteger(3));
        bonusBuilder.Add(BigInteger.Zero, new BigInteger(1), new BigInteger(3));

        // (0,5)
        totalBuilder.Add(new BigInteger(5), new BigInteger(2 * 2), new BigInteger(9));
        baseBuilder.Add(BigInteger.Zero, new BigInteger(2), new BigInteger(3));
        bonusBuilder.Add(new BigInteger(5), new BigInteger(2), new BigInteger(3));

        // (10,0)
        totalBuilder.Add(new BigInteger(10), new BigInteger(1 * 1), new BigInteger(9));
        baseBuilder.Add(new BigInteger(10), new BigInteger(1), new BigInteger(3));
        bonusBuilder.Add(BigInteger.Zero, new BigInteger(1), new BigInteger(3));

        // (10,5)
        totalBuilder.Add(new BigInteger(15), new BigInteger(1 * 2), new BigInteger(9));
        baseBuilder.Add(new BigInteger(10), new BigInteger(1), new BigInteger(3));
        bonusBuilder.Add(new BigInteger(5), new BigInteger(2), new BigInteger(3));

        var total = totalBuilder.Build();
        var features = new Dictionary<string, Dist<BigInteger>>
        {
            ["Base"] = baseBuilder.Build(),
            ["Bonus"] = bonusBuilder.Build()
        };

        return (total, features);
    }

    [Fact]
    public void PerFeatureRtp_SumsToTotalExactly()
    {
        var (total, features) = CreateFeatureDistributions();
        var totalRtp = ExactMetrics.ComputeRtp(total);
        var breakdown = ExactMetrics.ComputePerFeatureBreakdown(total, features, totalRtp);

        Assert.NotNull(breakdown);
        Assert.Equal(2, breakdown!.Features.Length);

        // Total RTP = 20/3.
        Assert.Equal(new BigInteger(20), totalRtp.RationalNumerator!.Value);
        Assert.Equal(new BigInteger(3), totalRtp.RationalDenominator!.Value);

        // Each feature RTP = 10/3.
        var baseFeature = breakdown.Features.First(f => f.FeatureName == "Base");
        var bonusFeature = breakdown.Features.First(f => f.FeatureName == "Bonus");

        Assert.Equal(new BigInteger(10), baseFeature.RationalNumerator!.Value);
        Assert.Equal(new BigInteger(3), baseFeature.RationalDenominator!.Value);
        Assert.Equal(10.0 / 3.0, baseFeature.RtpContribution, precision: 10);

        Assert.Equal(new BigInteger(10), bonusFeature.RationalNumerator!.Value);
        Assert.Equal(new BigInteger(3), bonusFeature.RationalDenominator!.Value);

        // Sum of feature contributions = 10/3 + 10/3 = 20/3 = total RTP.
        var sumNum = baseFeature.RationalNumerator!.Value * bonusFeature.RationalDenominator!.Value
                   + bonusFeature.RationalNumerator!.Value * baseFeature.RationalDenominator!.Value;
        var sumDen = baseFeature.RationalDenominator!.Value * bonusFeature.RationalDenominator!.Value;

        var totalNumScaled = totalRtp.RationalNumerator!.Value * sumDen;
        var sumNumScaled = sumNum * totalRtp.RationalDenominator!.Value;

        Assert.Equal(totalNumScaled, sumNumScaled); // Rational equality.
    }

    [Fact]
    public void PerFeature_FractionOfTotal_IsCorrect()
    {
        var (total, features) = CreateFeatureDistributions();
        var totalRtp = ExactMetrics.ComputeRtp(total);
        var breakdown = ExactMetrics.ComputePerFeatureBreakdown(total, features, totalRtp);

        Assert.NotNull(breakdown);
        Assert.Equal(2, breakdown!.Features.Length);

        // Each feature should be ~50% of total.
        foreach (var f in breakdown.Features)
        {
            Assert.True(f.FractionOfTotal > 0.49 && f.FractionOfTotal < 0.51,
                $"{f.FeatureName} fraction = {f.FractionOfTotal}, expected ~0.5");
        }
    }

    [Fact]
    public void PerFeature_HitFrequency_Tracked()
    {
        var (total, features) = CreateFeatureDistributions();
        var totalRtp = ExactMetrics.ComputeRtp(total);
        var breakdown = ExactMetrics.ComputePerFeatureBreakdown(total, features, totalRtp);

        Assert.NotNull(breakdown);
        foreach (var f in breakdown!.Features)
        {
            Assert.True(f.FeatureHitFrequency >= 0 && f.FeatureHitFrequency <= 1.0);
        }

        // Base: only outcome 10 (1/3 weight) is a hit → hit freq = 1/3.
        var baseFeature = breakdown.Features.First(f => f.FeatureName == "Base");
        Assert.Equal(1.0 / 3.0, baseFeature.FeatureHitFrequency, precision: 10);

        // Bonus: only outcome 5 (2/3 weight) is a hit → hit freq = 2/3.
        var bonusFeature = breakdown.Features.First(f => f.FeatureName == "Bonus");
        Assert.Equal(2.0 / 3.0, bonusFeature.FeatureHitFrequency, precision: 10);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 3 — Exact-path volatility is from the FULL distribution
// ═══════════════════════════════════════════════════════════════════════════

public class ExactMetrics_VolatilityFromFullDistribution
{
    /// <summary>
    /// Creates a known distribution:
    /// Outcomes: 0 (p=0.5), 10 (p=0.3), 20 (p=0.2).
    /// μ = 0*0.5 + 10*0.3 + 20*0.2 = 3 + 4 = 7.
    /// E[X²] = 0*0.5 + 100*0.3 + 400*0.2 = 30 + 80 = 110.
    /// Var = 110 - 49 = 61.
    /// σ = √61 ≈ 7.8102.
    /// VI = σ/μ ≈ 1.1157.
    /// </summary>
    private static Dist<BigInteger> CreateKnownVolatilityDist()
    {
        var builder = new DistBuilder<BigInteger>();
        builder.Add(BigInteger.Zero, new BigInteger(50), new BigInteger(100));
        builder.Add(new BigInteger(10), new BigInteger(30), new BigInteger(100));
        builder.Add(new BigInteger(20), new BigInteger(20), new BigInteger(100));
        return builder.Build();
    }

    [Fact]
    public void Variance_MatchesClosedForm()
    {
        var dist = CreateKnownVolatilityDist();
        var vol = ExactMetrics.ComputeVolatility(dist, 7.0);

        Assert.Equal(61.0, vol.Variance, precision: 10);
    }

    [Fact]
    public void StdDev_MatchesClosedForm()
    {
        var dist = CreateKnownVolatilityDist();
        var vol = ExactMetrics.ComputeVolatility(dist, 7.0);

        Assert.Equal(System.Math.Sqrt(61.0), vol.StdDev, precision: 10);
    }

    [Fact]
    public void VolatilityIndex_MatchesClosedForm()
    {
        var dist = CreateKnownVolatilityDist();
        var vol = ExactMetrics.ComputeVolatility(dist, 7.0);

        Assert.NotNull(vol.VolatilityIndex);
        Assert.Equal(System.Math.Sqrt(61.0) / 7.0, vol.VolatilityIndex!.Value, precision: 10);
    }

    [Fact]
    public void Volatility_IsNotEstimated_IsExact()
    {
        var dist = CreateKnownVolatilityDist();
        var vol = ExactMetrics.ComputeVolatility(dist, 7.0);

        // Provenance is Exact (full distribution, not a sample).
        Assert.Equal(Provenance.Exact, vol.Provenance.Provenance);

        // No sample count on exact path.
        Assert.Null(vol.SampleCount);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 4 — Every metric value carries a provenance tag
// ═══════════════════════════════════════════════════════════════════════════

public class Provenance_TagOnEveryMetric
{
    [Fact]
    public void ExactMetrics_AllTaggedExact()
    {
        var builder = new DistBuilder<BigInteger>();
        builder.Add(BigInteger.One, BigInteger.One, BigInteger.One);

        var dist = builder.Build();
        Assert.True(dist.IsFullyExact);

        var report = ExactMetrics.Compute(dist);

        Assert.Equal(Provenance.Exact, report.Rtp.Provenance.Provenance);
        Assert.Equal(Provenance.Exact, report.HitFrequency.Provenance.Provenance);
        Assert.Equal(Provenance.Exact, report.Volatility.Provenance.Provenance);
        Assert.Equal(Provenance.Exact, report.MaxWin.Provenance.Provenance);
        Assert.Equal(Provenance.Exact, report.Histogram.Provenance.Provenance);
        Assert.Equal(Provenance.Exact, report.AggregateProvenance);
    }

    [Fact]
    public void EpsilonPrunedMetrics_TaggedExactWithinEpsilon()
    {
        // Create a pruned distribution (by simulating we added pruned mass).
        var builder = new DistBuilder<BigInteger>();
        builder.Add(BigInteger.Zero, new BigInteger(1), new BigInteger(1000));
        builder.Add(new BigInteger(100), new BigInteger(999), new BigInteger(1000));
        builder.AddPrunedMass(BigInteger.One, new BigInteger(500));
        var dist = builder.Build();

        Assert.False(dist.IsFullyExact); // Has pruned mass.

        var report = ExactMetrics.Compute(dist);

        Assert.Equal(Provenance.ExactWithinEpsilon, report.Rtp.Provenance.Provenance);
        Assert.Equal(Provenance.ExactWithinEpsilon, report.HitFrequency.Provenance.Provenance);
        Assert.Equal(Provenance.ExactWithinEpsilon, report.Volatility.Provenance.Provenance);
        Assert.Equal(Provenance.ExactWithinEpsilon, report.MaxWin.Provenance.Provenance);
        Assert.Equal(Provenance.ExactWithinEpsilon, report.Histogram.Provenance.Provenance);
        Assert.Equal(Provenance.ExactWithinEpsilon, report.AggregateProvenance);
    }

    [Fact]
    public void SampledMetrics_AllTaggedSampled()
    {
        var stats = new StreamingStats();
        stats.Add(1.0);
        stats.Add(2.0);
        stats.Add(3.0);

        var report = SampledMetrics.Compute(stats);

        Assert.Equal(Provenance.Sampled, report.Rtp.Provenance.Provenance);
        Assert.Equal(Provenance.Sampled, report.HitFrequency.Provenance.Provenance);
        Assert.Equal(Provenance.Sampled, report.Volatility.Provenance.Provenance);
        Assert.Equal(Provenance.Sampled, report.MaxWin.Provenance.Provenance);
        Assert.Equal(Provenance.Sampled, report.Histogram.Provenance.Provenance);
        Assert.Equal(Provenance.Sampled, report.AggregateProvenance);
    }

    [Fact]
    public void Metric_T_TypeCarriesProvenance()
    {
        var m = Metric<double>.ExactValue(3.14);
        Assert.Equal(Provenance.Exact, m.Provenance.Provenance);
        Assert.Equal("Exact", m.Provenance.Label);

        var m2 = Metric<double>.SampledValue(3.14);
        Assert.Equal(Provenance.Sampled, m2.Provenance.Provenance);
        Assert.Equal("Sampled", m2.Provenance.Label);

        var m3 = Metric<double>.ExactWithinEpsilonValue(3.14);
        Assert.Equal(Provenance.ExactWithinEpsilon, m3.Provenance.Provenance);
        Assert.Equal("Exact (±ε)", m3.Provenance.Label);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 5 — RTP exact rational + display float
// ═══════════════════════════════════════════════════════════════════════════

public class RtpMetric_ExactRationalAndDisplay
{
    [Fact]
    public void ExactRtp_HasRationalAndDisplay()
    {
        var builder = new DistBuilder<BigInteger>();
        builder.Add(new BigInteger(1), new BigInteger(2), new BigInteger(3));
        builder.Add(new BigInteger(2), new BigInteger(1), new BigInteger(3));
        var dist = builder.Build();

        var rtp = ExactMetrics.ComputeRtp(dist);

        // Rational: (1*2 + 2*1) / 3 = 4/3.
        Assert.Equal(new BigInteger(4), rtp.RationalNumerator!.Value);
        Assert.Equal(new BigInteger(3), rtp.RationalDenominator!.Value);

        // Display: ~1.333...
        Assert.Equal(4.0 / 3.0, rtp.DisplayValue, precision: 10);

        // No sampled fields.
        Assert.Null(rtp.SampleCount);
        Assert.Null(rtp.StdErr);
        Assert.Null(rtp.Ci95Half);
    }

    [Fact]
    public void SampledRtp_HasDisplayAndConfidence()
    {
        var stats = new StreamingStats();
        for (var i = 0; i < 1000; i++) stats.Add(i % 5 == 0 ? 10.0 : 0.0);
        var snap = stats.Snapshot();

        var rtp = SampledMetrics.ComputeRtp(snap);

        // Display only, with confidence info.
        Assert.True(rtp.DisplayValue > 0);
        Assert.Null(rtp.RationalNumerator);
        Assert.Null(rtp.RationalDenominator);
        Assert.NotNull(rtp.SampleCount);
        Assert.Equal(1000, rtp.SampleCount!.Value);
        Assert.NotNull(rtp.StdErr);
        Assert.NotNull(rtp.Ci95Half);
    }

    [Fact]
    public void EpsilonPrunedRtp_HasIntervalBounds()
    {
        var builder = new DistBuilder<BigInteger>();
        builder.Add(BigInteger.Zero, new BigInteger(1), new BigInteger(1000));
        builder.Add(new BigInteger(100), new BigInteger(998), new BigInteger(1000));
        builder.AddPrunedMass(BigInteger.One, new BigInteger(1000));
        var dist = builder.Build();

        Assert.False(dist.IsFullyExact);

        var rtp = ExactMetrics.ComputeRtp(dist);

        Assert.Equal(Provenance.ExactWithinEpsilon, rtp.Provenance.Provenance);
        Assert.NotNull(rtp.LoDisplay);
        Assert.NotNull(rtp.HiDisplay);
        Assert.NotNull(rtp.PrunedMass);

        // lo < display < hi.
        Assert.True(rtp.LoDisplay!.Value <= rtp.DisplayValue + 1e-10);
        Assert.True(rtp.DisplayValue <= rtp.HiDisplay!.Value + 1e-10);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 6 — Win histogram computable from both paths
// ═══════════════════════════════════════════════════════════════════════════

public class WinHistogram_BothPaths
{
    [Fact]
    public void ExactHistogram_BinsSumToUnity()
    {
        var builder = new DistBuilder<BigInteger>();
        for (var i = 0; i < 10; i++)
            builder.Add(new BigInteger(i * 10), new BigInteger(i + 1), new BigInteger(55));
        var dist = builder.Build();

        var hist = ExactMetrics.ComputeHistogram(dist, numBins: 5);
        Assert.Equal(5, hist.BinCount);

        var totalProb = hist.Bins.Sum(b => b.Probability);
        Assert.Equal(1.0, totalProb, precision: 12);
    }

    [Fact]
    public void ExactHistogram_RationalProbabilitiesMatch()
    {
        var builder = new DistBuilder<BigInteger>();
        builder.Add(BigInteger.Zero, new BigInteger(1), new BigInteger(4));
        builder.Add(BigInteger.One, new BigInteger(3), new BigInteger(4));
        var dist = builder.Build();

        var hist = ExactMetrics.ComputeHistogram(dist, numBins: 2);

        foreach (var bin in hist.Bins)
        {
            Assert.NotNull(bin.ProbabilityNumerator);
            Assert.NotNull(bin.ProbabilityDenominator);
            Assert.Null(bin.Count); // No count on exact path.
        }

        // Probabilities sum to denominator.
        var sumNum = hist.Bins.Sum(b => (double)b.ProbabilityNumerator!.Value);
        var den = (double)hist.Bins[0].ProbabilityDenominator!.Value;
        Assert.Equal(1.0, sumNum / den, precision: 12);
    }

    [Fact]
    public void SampledHistogram_BinsHaveCounts()
    {
        var stats = new StreamingStats(maxWinCap: 100.0, histogramBins: 5);
        for (var i = 0; i < 500; i++) stats.Add(i * 0.2); // spread across [0, 100).

        var snap = stats.Snapshot();
        var hist = SampledMetrics.ComputeHistogram(snap);

        Assert.Equal(5, hist.BinCount);
        Assert.Equal(500, hist.SampleCount);

        var totalCount = hist.Bins.Sum(b => b.Count!.Value);
        Assert.Equal(500, totalCount);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 7 — Per-state RTP breakdown from exact path
// ═══════════════════════════════════════════════════════════════════════════

public class PerStateBreakdown_Exact
{
    [Fact]
    public void PerState_ComputesCorrectly()
    {
        // Program that goes through multiple states.
        // Draw board 0 or 1 → state boardValue → draw payout.
        var program =
            from board in Slot.Draw<MetricTestState>(_ => WeightSet.FromIntegers([1, 1]))
            from _ in Slot.PutState<MetricTestState>(new MetricTestState(board, 0))
            from payout in Slot.Draw<MetricTestState>(_ =>
                WeightSet.FromIntegers([1, 2, 3]))
            select new BigInteger(payout * 10);

        var result = ExactInterpreter.Evaluate(
            program, new MetricTestState(0, 0), s => s.RecurrenceHash);

        var breakdown = ExactMetrics.ComputePerStateBreakdown(
            result.Distribution, s => $"Board_{s.BoardValue}");

        Assert.NotNull(breakdown);

        // Two states: Board_0, Board_1.
        // Each state has half the probability mass (board drawn with weight 1:1).
        // Within each state: payout 0,10,20 with weights 1,2,3 (denom=6).
        // Raw sum of Win*Num for one state: 0*1 + 10*2 + 20*3 = 80.
        // Total denominator across BOTH states = 12.
        // State contribution to total RTP = 80/12 = 20/3 ≈ 6.67 each.
        // Total RTP = 40/3 ≈ 13.33.
        Assert.Equal(2, breakdown!.Features.Length);

        foreach (var f in breakdown.Features)
        {
            var expected = 20.0 / 3.0;
            Assert.Equal(expected, f.RtpContribution, precision: 10);
        }

        // Sum of per-state contributions = total RTP = 40/3.
        var totalExpected = 40.0 / 3.0;
        Assert.Equal(totalExpected, breakdown.TotalRtpDisplay, precision: 10);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Integration — Cross-check exact vs sampled metrics
// ═══════════════════════════════════════════════════════════════════════════

public class G7_ExactVsSampled_CrossCheck
{
    [Fact]
    public void SampledRtp_ConvergesToExactRtp()
    {
        // Draw: weights [1,2,3,4,5], payouts [0,1,2,3,4].
        // Exact EV = (0+2+6+12+20)/15 = 40/15 = 8/3 ≈ 2.6667.
        var program =
            from idx in Slot.Draw<MetricTestState>(_ => WeightSet.FromIntegers([1, 2, 3, 4, 5]))
            select new BigInteger(idx);

        var exactResult = ExactInterpreter.Evaluate(
            program, new MetricTestState(0, 0), s => s.RecurrenceHash);

        var exactReport = ExactMetrics.Compute(exactResult.ValueDistribution());
        var exactRtp = exactReport.Rtp;

        // Sampled with large N.
        var sampledResult = SampledInterpreter.Evaluate(program, new MetricTestState(0, 0),
            new SampledConfig { Seed = 42, MaxSpins = 100_000 });

        var sampledReport = SampledMetrics.ComputeFromResult(sampledResult);
        var sampledRtp = sampledReport.Rtp;

        // Sampled mean should be within 3*SE of exact.
        var deviation = System.Math.Abs(sampledRtp.DisplayValue - exactRtp.DisplayValue);
        var tolerance = 3.0 * sampledRtp.StdErr!.Value;

        Assert.True(deviation <= tolerance,
            $"Sampled RTP {sampledRtp.DisplayValue:F6} outside 3*SE of exact {exactRtp.DisplayValue:F6}. " +
            $"Deviation: {deviation:F6}, 3*SE: {tolerance:F6}");
    }

    [Fact]
    public void SampledHitFrequency_ConvergesToExactHitFrequency()
    {
        var program =
            from idx in Slot.Draw<MetricTestState>(_ => WeightSet.FromIntegers([1, 2, 3, 4, 5]))
            select new BigInteger(idx);

        var exactResult = ExactInterpreter.Evaluate(
            program, new MetricTestState(0, 0), s => s.RecurrenceHash);
        var exactHf = ExactMetrics.ComputeHitFrequency(exactResult.ValueDistribution());

        var sampledResult = SampledInterpreter.Evaluate(program, new MetricTestState(0, 0),
            new SampledConfig { Seed = 99, MaxSpins = 50_000 });
        var sampledHf = SampledMetrics.ComputeHitFrequency(sampledResult.Stats.Snapshot());

        var deviation = System.Math.Abs(sampledHf.DisplayValue - exactHf.DisplayValue);
        var tolerance = 3.0 * sampledHf.StdErr!.Value;

        Assert.True(deviation <= tolerance,
            $"Sampled hit freq {sampledHf.DisplayValue:F6} outside 3*SE of exact {exactHf.DisplayValue:F6}. " +
            $"Deviation: {deviation:F6}, 3*SE: {tolerance:F6}");
    }

    [Fact]
    public void SampledVolatility_ConvergesToExactVolatility()
    {
        var program =
            from idx in Slot.Draw<MetricTestState>(_ => WeightSet.FromIntegers([1, 2, 3, 4, 5]))
            select new BigInteger(idx);

        var exactResult = ExactInterpreter.Evaluate(
            program, new MetricTestState(0, 0), s => s.RecurrenceHash);
        var exactReport = ExactMetrics.Compute(exactResult.ValueDistribution());

        var sampledResult = SampledInterpreter.Evaluate(program, new MetricTestState(0, 0),
            new SampledConfig { Seed = 7777, MaxSpins = 100_000 });

        var sampledReport = SampledMetrics.ComputeFromResult(sampledResult);

        // Sample std dev should be close to exact std dev.
        var deviation = System.Math.Abs(sampledReport.Volatility.StdDev - exactReport.Volatility.StdDev);
        var relativeDeviation = exactReport.Volatility.StdDev > 0
            ? deviation / exactReport.Volatility.StdDev
            : deviation;

        Assert.True(relativeDeviation < 0.05,
            $"Sampled σ {sampledReport.Volatility.StdDev:F6} deviates from exact σ " +
            $"{exactReport.Volatility.StdDev:F6} by {relativeDeviation:P2}");
    }

    [Fact]
    public void BothPaths_ProduceCompleteReports()
    {
        var program =
            from idx in Slot.Draw<MetricTestState>(_ => WeightSet.FromIntegers([1, 2, 3, 4, 5]))
            select new BigInteger(idx * 10);

        // Exact report.
        var exactResult = ExactInterpreter.Evaluate(
            program, new MetricTestState(0, 0), s => s.RecurrenceHash);
        var exactReport = ExactMetrics.Compute(exactResult.ValueDistribution());

        // Sampled report.
        var sampledResult = SampledInterpreter.Evaluate(program, new MetricTestState(0, 0),
            new SampledConfig { Seed = 123, MaxSpins = 10_000 });
        var sampledReport = SampledMetrics.ComputeFromResult(sampledResult);

        // Both reports have all metrics.
        Assert.NotNull(exactReport.Rtp);
        Assert.NotNull(exactReport.HitFrequency);
        Assert.NotNull(exactReport.Volatility);
        Assert.NotNull(exactReport.MaxWin);
        Assert.NotNull(exactReport.Histogram);

        Assert.NotNull(sampledReport.Rtp);
        Assert.NotNull(sampledReport.HitFrequency);
        Assert.NotNull(sampledReport.Volatility);
        Assert.NotNull(sampledReport.MaxWin);
        Assert.NotNull(sampledReport.Histogram);

        // Provenance differs.
        Assert.Equal(Provenance.Exact, exactReport.AggregateProvenance);
        Assert.Equal(Provenance.Sampled, sampledReport.AggregateProvenance);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  StreamingStats hit-frequency tracking
// ═══════════════════════════════════════════════════════════════════════════

public class StreamingStats_HitFrequency
{
    [Fact]
    public void NonZeroCount_TracksCorrectly()
    {
        var stats = new StreamingStats();
        stats.Add(0.0);
        stats.Add(5.0);
        stats.Add(0.0);
        stats.Add(10.0);

        Assert.Equal(4, stats.Count);
        Assert.Equal(2, stats.NonZeroCount);
        Assert.Equal(0.5, stats.HitFrequency);
    }

    [Fact]
    public void AllZeros_ZeroHitFrequency()
    {
        var stats = new StreamingStats();
        for (var i = 0; i < 100; i++)
            stats.Add(0.0);

        Assert.Equal(0, stats.NonZeroCount);
        Assert.Equal(0.0, stats.HitFrequency);
    }

    [Fact]
    public void AllNonZeros_FullHitFrequency()
    {
        var stats = new StreamingStats();
        for (var i = 0; i < 100; i++)
            stats.Add(1.0);

        Assert.Equal(100, stats.NonZeroCount);
        Assert.Equal(1.0, stats.HitFrequency);
    }

    [Fact]
    public void HitFrequencyStdErr_CalculatedCorrectly()
    {
        var stats = new StreamingStats();
        // 25 zeros, 75 non-zeros → p = 0.75.
        for (var i = 0; i < 25; i++) stats.Add(0.0);
        for (var i = 0; i < 75; i++) stats.Add(1.0);

        var expectedSE = System.Math.Sqrt(0.75 * 0.25 / 100);
        Assert.Equal(expectedSE, stats.HitFrequencyStdErr, precision: 10);
    }

    [Fact]
    public void Merge_CombinesHitFrequency()
    {
        var s1 = new StreamingStats();
        s1.Add(0.0);
        s1.Add(1.0);

        var s2 = new StreamingStats();
        s2.Add(0.0);
        s2.Add(5.0);

        s1.Merge(s2);

        Assert.Equal(4, s1.Count);
        Assert.Equal(2, s1.NonZeroCount);
        Assert.Equal(0.5, s1.HitFrequency);
    }

    [Fact]
    public void NonZeroTracked_BeforeCapClamping()
    {
        // Hit frequency is based on original value, not clamped value.
        var stats = new StreamingStats(maxWinCap: 50.0);
        stats.Add(100.0); // Would be clamped to 50, but was originally non-zero.

        Assert.Equal(1, stats.NonZeroCount);
        Assert.Equal(1.0, stats.HitFrequency);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Edge cases
// ═══════════════════════════════════════════════════════════════════════════

public class MetricReducers_EdgeCases
{
    [Fact]
    public void EmptyDistribution_ReturnsSaneDefaults()
    {
        var dist = new Dist<BigInteger>(
            Array.Empty<Dist<BigInteger>.Entry>(),
            BigInteger.One, BigInteger.Zero,
            BigInteger.Zero, BigInteger.One);

        var report = ExactMetrics.Compute(dist);

        Assert.NotNull(report.Rtp);
        Assert.Equal(0.0, report.Rtp.DisplayValue);
        Assert.Equal(new BigInteger(0), report.Rtp.RationalNumerator!.Value);
        Assert.Equal(new BigInteger(1), report.Rtp.RationalDenominator!.Value);

        Assert.NotNull(report.HitFrequency);
        Assert.Equal(0.0, report.HitFrequency.DisplayValue);

        Assert.NotNull(report.Volatility);
        Assert.Equal(0.0, report.Volatility.Variance);

        Assert.NotNull(report.MaxWin);
        Assert.Equal(0.0, report.MaxWin.MaxWin);
    }

    [Fact]
    public void SingleOutcome_ZeroVariance()
    {
        var builder = new DistBuilder<BigInteger>();
        builder.Add(new BigInteger(5), BigInteger.One, BigInteger.One);
        var dist = builder.Build();

        var report = ExactMetrics.Compute(dist);

        Assert.Equal(0.0, report.Volatility.Variance);
        Assert.Equal(0.0, report.Volatility.StdDev);
    }

    [Fact]
    public void AllZeroOutcomes_ZeroHitFrequency()
    {
        var builder = new DistBuilder<BigInteger>();
        builder.Add(BigInteger.Zero, new BigInteger(100), new BigInteger(100));
        var dist = builder.Build();

        var hf = ExactMetrics.ComputeHitFrequency(dist);

        Assert.Equal(0.0, hf.DisplayValue);
        Assert.Equal(new BigInteger(0), hf.RationalNumerator!.Value);
    }
}

public sealed record EdgeState(int Counter, BigInteger Win)
{
    public BigInteger RecurrenceHash => Counter;
}

public class Metrics_EdgeCases
{
    [Fact]
    public void ExactMetrics_ComputesReport()
    {
        var program =
            from idx in Slot.Draw<EdgeState>(_ =>
                WeightSet.FromIntegers(new int[] { 1, 1 }))
            select idx == 0 ? BigInteger.Zero : new BigInteger(100);

        var result = ExactInterpreter.Evaluate(
            program, new EdgeState(0, 0), s => s.RecurrenceHash);
        var report = ExactMetrics.Compute(result.ValueDistribution());
        Assert.NotNull(report);
        Assert.NotNull(report.Rtp);
        Assert.NotNull(report.HitFrequency);
        Assert.NotNull(report.Volatility);
    }

    [Fact]
    public void SampledMetrics_ComputesReport()
    {
        var program =
            from idx in Slot.Draw<EdgeState>(_ =>
                WeightSet.FromIntegers(new int[] { 1, 2, 1 }))
            select idx switch { 0 => BigInteger.Zero, 1 => new BigInteger(10), 2 => new BigInteger(50), _ => BigInteger.Zero };

        var r = SampledInterpreter.Evaluate(
            program, new EdgeState(0, 0),
            new SampledConfig { Seed = 42, MaxSpins = 10_000 });
        var report = SampledMetrics.ComputeFromResult(r);
        Assert.NotNull(report);
    }

    [Fact]
    public void HybridEvaluator_ForceSampled()
    {
        var program =
            from idx in Slot.Draw<EdgeState>(_ =>
                WeightSet.FromIntegers(new int[] { 1, 1 }))
            select idx == 0 ? BigInteger.Zero : new BigInteger(10);

        var result = HybridEvaluator.Evaluate(program, new EdgeState(0, 0),
            s => s.RecurrenceHash, new RegimeConfig { ForceSampled = true, SampledSpins = 5000 });
        Assert.Equal(Provenance.Sampled, result.AggregateProvenance);
    }

    [Fact]
    public void StreamingStats_AccumulatesCorrectly()
    {
        var stats = new StreamingStats(histogramBins: 10);
        stats.Add(0.0);
        stats.Add(10.0);
        stats.Add(100.0);
        Assert.Equal(3L, stats.Count);
        Assert.True(stats.Mean > 0);
    }

    [Fact]
    public void ExactConfig_HasEpsilon()
    {
        var c = new ExactConfig { EpsilonNumerator = 1, EpsilonDenominator = 100 };
        Assert.True(c.HasEpsilon);
    }

    [Fact]
    public void Provenance_TagsAreCorrect()
    {
        Assert.Equal("Exact", Provenance.Exact.ToString());
        Assert.Equal("Sampled", Provenance.Sampled.ToString());
        Assert.Equal("ExactWithinEpsilon", Provenance.ExactWithinEpsilon.ToString());
    }

    [Fact]
    public void Dist_EmptyDistribution_ExpectedValueIsZero()
    {
        var builder = new DistBuilder<BigInteger>();
        builder.AddPrunedMass(1, 2);
        var dist = builder.Build();
        Assert.True(dist.IsEmpty);
        Assert.Equal(BigInteger.Zero, dist.Entries.Count);
        var (num, den) = dist.ExpectedBigIntegerValue();
        Assert.Equal(BigInteger.Zero, num);
        Assert.Equal(BigInteger.One, den);
    }

    [Fact]
    public void Dist_ExpectedValueInterval_PureExact()
    {
        var builder = new DistBuilder<BigInteger>();
        builder.Add(BigInteger.One, 1, 1);
        builder.Add(new BigInteger(2), 1, 1);
        var dist = builder.Build();
        Assert.True(dist.IsFullyExact);

        var (lo, hi) = dist.ExpectedBigIntegerValueInterval(BigInteger.Zero, new BigInteger(100));
        Assert.Equal(lo, hi);
    }

    [Fact]
    public void Dist_PrunedInterval_ContainsExact()
    {
        var builder = new DistBuilder<BigInteger>();
        builder.Add(BigInteger.One, 90, 100);
        builder.Add(BigInteger.Zero, 10, 100);
        // Prune the zero branch
        builder.AddPrunedMass(1, 100);
        var dist = builder.Build();

        var (lo, hi) = dist.ExpectedBigIntegerValueInterval(BigInteger.Zero, new BigInteger(100));
        Assert.True(lo.Num * hi.Den <= hi.Num * lo.Den); // lo <= hi
    }

    [Fact]
    public void DistBuilder_FrozenThrows()
    {
        var builder = new DistBuilder<BigInteger>();
        builder.Add(BigInteger.One, 1, 1);
        builder.Build();
        Assert.Throws<InvalidOperationException>(() => builder.Add(BigInteger.One, 1, 1));
    }

    [Fact]
    public void Budget_DefaultAndCustom()
    {
        var b = Budget.Default;
        Assert.NotNull(b);
        var custom = new Budget { MaxBranches = 100, MaxTime = TimeSpan.FromSeconds(5) };
        Assert.Equal(100, custom.MaxBranches);
        Assert.Equal(TimeSpan.FromSeconds(5), custom.MaxTime);
    }

    [Fact]
    public void BudgetExceededException_CarriesInfo()
    {
        var ex = new BudgetExceededException(1000, 5, new Budget { MaxBranches = 500 }, "branches");
        Assert.Contains("1000", ex.Message);
        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public void SubgraphStrategy_ToString()
    {
        var ss = new SubgraphStrategy
        {
            SubgraphId = "test",
            Strategy = EvaluationStrategy.Sampled,
            Reason = "plugin_present",
            ContainsPlugin = true,
        };
        var s = ss.ToString();
        Assert.Contains("test", s);
        Assert.Contains("plugin", s);
    }

    [Fact]
    public void RegimeResult_ToString_IncludesProvenance()
    {
        var report = ExactMetrics.Compute(new DistBuilder<BigInteger>().Build());
        var rr = new RegimeResult
        {
            Report = report,
            SubgraphStrategies = new[]
            {
                new SubgraphStrategy { SubgraphId = "root", Strategy = EvaluationStrategy.Exact, Reason = "within_budget" }
            },
            AggregateProvenance = Provenance.Exact,
            OverallStrategy = EvaluationStrategy.Exact,
        };
        var s = rr.ToString();
        Assert.Contains("Exact", s);
        Assert.Contains("root", s);
    }

    [Fact]
    public void ProgramAnalyzer_EmptyProgram()
    {
        var program = Slot.Pure<EdgeState, BigInteger>(0);
        var analysis = ProgramAnalyzer.Analyze(program);
        Assert.Equal(0, analysis.DrawCount);
        Assert.Equal(0, analysis.EstimatedBranches);
        Assert.False(analysis.ContainsPlugin);
    }

    [Fact]
    public void Slot_Annotate_PreservesBehavior()
    {
        var baseProgram =
            from idx in Slot.Draw<EdgeState>(_ =>
                WeightSet.FromIntegers(new int[] { 1 }))
            select new BigInteger(idx + 1);

        var annotated = Slot.Annotate(baseProgram, subgraphId: "test-sg", containsPlugin: true);

        var r1 = ExactInterpreter.Evaluate(baseProgram, new EdgeState(0, 0), s => s.RecurrenceHash);
        var r2 = ExactInterpreter.Evaluate(annotated, new EdgeState(0, 0), s => s.RecurrenceHash);

        var (n1, d1) = r1.ValueDistribution().ExpectedBigIntegerValue();
        var (n2, d2) = r2.ValueDistribution().ExpectedBigIntegerValue();
        Assert.Equal(n1, n2);
        Assert.Equal(d1, d2);
    }

    [Fact]
    public void Slot_Map_AppliesFunction()
    {
        var program = Slot.Pure<EdgeState, int>(5).Select(x => new BigInteger(x * 2));
        var result = ExactInterpreter.Evaluate(program, new EdgeState(0, 0), s => s.RecurrenceHash);
        var (n, d) = result.ValueDistribution().ExpectedBigIntegerValue();
        Assert.Equal(new BigInteger(10), n);
        Assert.Equal(BigInteger.One, d);
    }
}
