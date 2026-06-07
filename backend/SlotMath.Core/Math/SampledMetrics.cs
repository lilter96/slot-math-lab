namespace SlotMath.Core.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  SampledMetrics — compute all G7 metrics from sampled (Monte Carlo) results
//
//  Every metric carries provenance Sampled.  Values include standard errors
//  and confidence intervals where applicable.  The sampled volatility is
//  from the Welford online accumulators (sample variance, not population).
//
//  Per-feature breakdown is available when per-feature stats are provided.
// ═══════════════════════════════════════════════════════════════════════════

public static class SampledMetrics
{
    /// <summary>
    /// Compute the full SlotMathReport from a sampled result's streaming stats.
    /// </summary>
    /// <param name="stats">The streaming stats from a Monte Carlo run.</param>
    /// <param name="featureBreakdowns">
    /// Optional per-feature StreamingStats for per-feature RTP breakdown.
    /// </param>
    public static SlotMathReport Compute(
        StreamingStats stats,
        IReadOnlyDictionary<string, StreamingStats>? featureBreakdowns = null)
    {
        var snapshot = stats.Snapshot();

        var rtp = ComputeRtp(snapshot);
        var hitFreq = ComputeHitFrequency(snapshot);
        var volatility = ComputeVolatility(snapshot);
        var maxWin = ComputeMaxWin(snapshot);
        var histogram = ComputeHistogram(snapshot);
        var perFeature = ComputePerFeatureBreakdown(snapshot, rtp, featureBreakdowns);

        return new SlotMathReport
        {
            AggregateProvenance = Provenance.Sampled,
            Rtp = rtp,
            HitFrequency = hitFreq,
            Volatility = volatility,
            MaxWin = maxWin,
            Histogram = histogram,
            PerFeatureBreakdown = perFeature
        };
    }

    /// <summary>
    /// Compute the full SlotMathReport from a <see cref="SampledResult{S}"/>.
    /// </summary>
    public static SlotMathReport ComputeFromResult<S>(
        SampledResult<S> result,
        IReadOnlyDictionary<string, StreamingStats>? featureBreakdowns = null)
        where S : notnull
        => Compute(result.Stats, featureBreakdowns);

    // ── RTP ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute RTP from sampled statistics.  RTP = Mean, with stdErr and CI95.
    /// </summary>
    public static RtpMetric ComputeRtp(StreamingStatsSnapshot stats)
    {
        return new RtpMetric(ProvenanceTag.Sampled)
        {
            DisplayValue = stats.Mean,
            SampleCount = stats.Count,
            StdErr = stats.StdErr,
            Ci95Half = stats.Ci95Half
        };
    }

    // ── Hit Frequency ────────────────────────────────────────────────────

    /// <summary>
    /// Compute hit frequency from sampled statistics.
    /// p = nonZeroCount / totalCount, with standard error.
    /// </summary>
    public static HitFrequencyMetric ComputeHitFrequency(StreamingStatsSnapshot stats)
    {
        return new HitFrequencyMetric(ProvenanceTag.Sampled)
        {
            DisplayValue = stats.HitFrequency,
            SampleCount = stats.Count,
            StdErr = stats.HitFrequencyStdErr
        };
    }

    // ── Volatility ───────────────────────────────────────────────────────

    /// <summary>
    /// Compute volatility from sampled Welford statistics.
    /// </summary>
    public static VolatilityMetric ComputeVolatility(StreamingStatsSnapshot stats)
    {
        double? volIndex = null;
        if (stats.Mean > 0 && stats.StdDev > 0)
            volIndex = stats.StdDev / stats.Mean;

        return new VolatilityMetric(ProvenanceTag.Sampled)
        {
            Variance = stats.Variance,
            StdDev = stats.StdDev,
            VolatilityIndex = volIndex,
            SampleCount = stats.Count
        };
    }

    // ── Max Win ──────────────────────────────────────────────────────────

    /// <summary>
    /// Compute max-win metric from sampled statistics.
    /// P(cap) = capHits / totalSpins.
    /// </summary>
    public static MaxWinMetric ComputeMaxWin(StreamingStatsSnapshot stats)
    {
        var metric = new MaxWinMetric(ProvenanceTag.Sampled)
        {
            MaxWin = stats.MaxObserved,
            Cap = stats.MaxWinCap,
            SampleCount = stats.Count,
            CapHits = stats.CapHits
        };

        if (stats.MaxWinCap.HasValue && stats.Count > 0)
        {
            var pCap = (double)stats.CapHits / stats.Count;
            var pCapStdErr = stats.Count > 1
                ? System.Math.Sqrt(pCap * (1.0 - pCap) / stats.Count)
                : 0.0;

            metric = metric with
            {
                PCapReached = pCap,
                PCapStdErr = pCapStdErr
            };
        }

        return metric;
    }

    // ── Win Histogram ────────────────────────────────────────────────────

    /// <summary>
    /// Build a win histogram from sampled statistics.
    /// Each bin carries an observation count.
    /// </summary>
    public static WinHistogramMetric ComputeHistogram(StreamingStatsSnapshot stats)
    {
        var sourceBins = stats.Histogram;
        var bins = new WinHistogramBin[sourceBins.Length];

        for (var i = 0; i < sourceBins.Length; i++)
        {
            var sb = sourceBins[i];
            bins[i] = new WinHistogramBin
            {
                LowerBound = sb.LowerBound,
                UpperBound = sb.UpperBound,
                Count = sb.Count,
                Probability = stats.Count > 0
                    ? (double)sb.Count / stats.Count
                    : 0.0
            };
        }

        return new WinHistogramMetric(ProvenanceTag.Sampled)
        {
            Bins = bins,
            SampleCount = stats.Count
        };
    }

    // ── Per-Feature RTP Breakdown ────────────────────────────────────────

    /// <summary>
    /// Compute per-feature RTP breakdown from per-feature StreamingStats.
    /// Each feature's mean is its RTP contribution.  The sum of per-feature
    /// means converges to the total RTP.
    /// </summary>
    public static PerFeatureBreakdownMetric? ComputePerFeatureBreakdown(
        StreamingStatsSnapshot totalStats,
        RtpMetric totalRtp,
        IReadOnlyDictionary<string, StreamingStats>? featureBreakdowns)
    {
        if (featureBreakdowns == null || featureBreakdowns.Count == 0)
            return null;

        var contributions = new List<FeatureRtpContribution>();
        double sumRtp = 0;

        foreach (var (name, featureStats) in featureBreakdowns)
        {
            var fs = featureStats.Snapshot();
            var contribution = fs.Mean;
            sumRtp += contribution;

            contributions.Add(new FeatureRtpContribution
            {
                FeatureName = name,
                RtpContribution = contribution,
                FractionOfTotal = totalRtp.DisplayValue > 0
                    ? contribution / totalRtp.DisplayValue
                    : 0.0,
                FeatureHitFrequency = fs.HitFrequency,
                SampleCount = fs.Count
            });
        }

        return new PerFeatureBreakdownMetric(ProvenanceTag.Sampled)
        {
            Features = contributions.ToArray(),
            TotalRtpDisplay = sumRtp
        };
    }
}
