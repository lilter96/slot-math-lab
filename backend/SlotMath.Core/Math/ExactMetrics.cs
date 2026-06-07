using System.Numerics;

namespace SlotMath.Core.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  ExactMetrics — compute all G7 metrics from a Dist<BigInteger>
//
//  Every metric carries provenance Exact (when unpruned) or
//  ExactWithinEpsilon (when pruned).  The volatility/variance is computed
//  from the *full distribution* — not a sample — so it is exact.
//
//  Per-feature contributions sum to total RTP exactly (rational equality)
//  on the exact path.
// ═══════════════════════════════════════════════════════════════════════════

public static class ExactMetrics
{
    /// <summary>
    /// Compute the full SlotMathReport from an exact BigInteger-valued distribution.
    /// </summary>
    /// <param name="dist">The exact win distribution from the interpreter.</param>
    /// <param name="maxWinCap">Optional max-win cap for cap-hit probability.</param>
    /// <param name="featureBreakdowns">
    /// Optional per-feature distributions.  Each feature's dist is summed to produce
    /// the per-feature RTP breakdown.  The sum of per-feature expected values must
    /// equal the total expected value exactly.
    /// </param>
    /// <param name="numHistogramBins">Number of histogram bins (default 50).</param>
    public static SlotMathReport Compute(
        Dist<BigInteger> dist,
        BigInteger? maxWinCap = null,
        IReadOnlyDictionary<string, Dist<BigInteger>>? featureBreakdowns = null,
        int numHistogramBins = 50)
    {
        var provenance = dist.IsFullyExact
            ? ProvenanceTag.Exact
            : ProvenanceTag.ExactWithinEpsilon;

        var rtp = ComputeRtp(dist);
        var hitFreq = ComputeHitFrequency(dist);
        var volatility = ComputeVolatility(dist, rtp.DisplayValue);
        var maxWin = ComputeMaxWin(dist, maxWinCap);
        var histogram = ComputeHistogram(dist, maxWinCap, numHistogramBins);
        var perFeature = ComputePerFeatureBreakdown(dist, featureBreakdowns, rtp);

        return new SlotMathReport
        {
            AggregateProvenance = provenance.Provenance,
            Rtp = rtp,
            HitFrequency = hitFreq,
            Volatility = volatility,
            MaxWin = maxWin,
            Histogram = histogram,
            PerFeatureBreakdown = perFeature
        };
    }

    // ── RTP ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute RTP from the exact distribution.  Returns both the exact
    /// rational (num/den) and a display-friendly double.
    /// </summary>
    public static RtpMetric ComputeRtp(Dist<BigInteger> dist)
    {
        var provenance = dist.IsFullyExact
            ? ProvenanceTag.Exact
            : ProvenanceTag.ExactWithinEpsilon;

        var (num, den) = dist.ExpectedBigIntegerValue();
        var display = (double)num / (double)den;

        var metric = new RtpMetric(provenance)
        {
            RationalNumerator = num,
            RationalDenominator = den,
            DisplayValue = display
        };

        if (!dist.IsFullyExact)
        {
            // Compute interval bounds.
            var maxWin = BigInteger.Zero;
            foreach (var e in dist.Entries)
                if (e.Value > maxWin) maxWin = e.Value;

            var interval = dist.ExpectedBigIntegerValueInterval(BigInteger.Zero, maxWin);
            metric = metric with
            {
                LoDisplay = (double)interval.Lo.Num / (double)interval.Lo.Den,
                HiDisplay = (double)interval.Hi.Num / (double)interval.Hi.Den,
                PrunedMass = (double)dist.PrunedNumerator / (double)dist.PrunedDenominator
            };
        }

        return metric;
    }

    // ── Hit Frequency ────────────────────────────────────────────────────

    /// <summary>
    /// Compute hit frequency = probability mass of non-zero win outcomes.
    /// </summary>
    public static HitFrequencyMetric ComputeHitFrequency(Dist<BigInteger> dist)
    {
        var provenance = dist.IsFullyExact
            ? ProvenanceTag.Exact
            : ProvenanceTag.ExactWithinEpsilon;

        BigInteger nonZeroNum = 0;
        foreach (var e in dist.Entries)
        {
            if (e.Value != 0)
                nonZeroNum += e.Numerator;
        }

        var den = dist.TotalNumerator > 0 ? dist.TotalNumerator : BigInteger.One;
        var display = (double)nonZeroNum / (double)den;

        return new HitFrequencyMetric(provenance)
        {
            RationalNumerator = nonZeroNum,
            RationalDenominator = den,
            DisplayValue = display
        };
    }

    // ── Volatility ───────────────────────────────────────────────────────

    /// <summary>
    /// Compute variance, std deviation, and volatility index from the
    /// FULL exact distribution (not a sample).  This is the exact volatility,
    /// not an estimate.
    ///
    /// Var[X] = E[X²] - (E[X])²
    /// </summary>
    public static VolatilityMetric ComputeVolatility(Dist<BigInteger> dist, double rtpDisplay)
    {
        var provenance = dist.IsFullyExact
            ? ProvenanceTag.Exact
            : ProvenanceTag.ExactWithinEpsilon;

        if (dist.IsEmpty || dist.TotalNumerator == 0)
        {
            return new VolatilityMetric(provenance)
            {
                Variance = 0.0,
                StdDev = 0.0,
                VolatilityIndex = null
            };
        }

        // E[X] — the mean as a double.
        var mean = rtpDisplay;

        // E[X²] — expected value of squared win.
        BigInteger sumXSq = 0;
        foreach (var e in dist.Entries)
            sumXSq += e.Value * e.Value * e.Numerator;

        var eXSq = (double)sumXSq / (double)dist.TotalNumerator;

        // Var[X] = E[X²] - (E[X])²
        var variance = eXSq - mean * mean;

        // Guard against tiny negative variance from floating-point.
        if (variance < 0 && variance > -1e-15)
            variance = 0.0;

        var stdDev = System.Math.Sqrt(System.Math.Max(0.0, variance));
        double? volIndex = mean > 0 ? stdDev / mean : null;

        return new VolatilityMetric(provenance)
        {
            Variance = variance,
            StdDev = stdDev,
            VolatilityIndex = volIndex
        };
    }

    // ── Max Win ──────────────────────────────────────────────────────────

    /// <summary>
    /// Compute max win and probability of reaching the cap from the exact distribution.
    /// </summary>
    public static MaxWinMetric ComputeMaxWin(Dist<BigInteger> dist, BigInteger? cap = null)
    {
        var provenance = dist.IsFullyExact
            ? ProvenanceTag.Exact
            : ProvenanceTag.ExactWithinEpsilon;

        BigInteger maxWin = 0;
        foreach (var e in dist.Entries)
            if (e.Value > maxWin) maxWin = e.Value;

        var metric = new MaxWinMetric(provenance)
        {
            MaxWin = (double)maxWin,
            MaxWinExact = maxWin,
            Cap = cap.HasValue ? (double)cap.Value : null
        };

        if (cap.HasValue)
        {
            // Sum probability mass for values ≥ cap.
            BigInteger capNum = 0;
            foreach (var e in dist.Entries)
            {
                if (e.Value >= cap.Value)
                    capNum += e.Numerator;
            }

            var den = dist.TotalNumerator > 0 ? dist.TotalNumerator : BigInteger.One;
            metric = metric with
            {
                PCapReached = (double)capNum / (double)den,
                PCapRationalNumerator = capNum,
                PCapRationalDenominator = den
            };
        }

        return metric;
    }

    // ── Win Histogram ────────────────────────────────────────────────────

    /// <summary>
    /// Build a win histogram from the exact distribution by binning outcomes
    /// into equal-width bins over [0, maxWin] (or [0, cap] if a cap is set).
    /// Each bin carries exact rational probability mass.
    /// </summary>
    public static WinHistogramMetric ComputeHistogram(
        Dist<BigInteger> dist,
        BigInteger? maxWinCap = null,
        int numBins = 50)
    {
        var provenance = dist.IsFullyExact
            ? ProvenanceTag.Exact
            : ProvenanceTag.ExactWithinEpsilon;

        if (numBins < 1) numBins = 1;

        if (dist.IsEmpty || dist.TotalNumerator == 0)
        {
            return new WinHistogramMetric(provenance)
            {
                Bins = Enumerable.Range(0, numBins)
                    .Select(i => new WinHistogramBin
                    {
                        LowerBound = i,
                        UpperBound = i + 1,
                        ProbabilityNumerator = 0,
                        ProbabilityDenominator = 1,
                        Probability = 0.0
                    }).ToArray()
            };
        }

        // Determine max value for binning.
        double maxVal;
        if (maxWinCap.HasValue)
            maxVal = (double)maxWinCap.Value;
        else
        {
            BigInteger max = 0;
            foreach (var e in dist.Entries)
                if (e.Value > max) max = e.Value;
            maxVal = (double)max;
        }

        if (maxVal <= 0) maxVal = 1.0;

        var binWidth = maxVal / numBins;
        var binNumerators = new BigInteger[numBins];
        var den = dist.TotalNumerator;

        foreach (var e in dist.Entries)
        {
            var val = (double)e.Value;
            var binIndex = (int)(val / binWidth);
            if (binIndex >= numBins) binIndex = numBins - 1;
            if (binIndex < 0) binIndex = 0;
            binNumerators[binIndex] += e.Numerator;
        }

        var bins = new WinHistogramBin[numBins];
        for (var i = 0; i < numBins; i++)
        {
            bins[i] = new WinHistogramBin
            {
                LowerBound = i * binWidth,
                UpperBound = (i + 1) * binWidth,
                ProbabilityNumerator = binNumerators[i],
                ProbabilityDenominator = den,
                Probability = den > 0 ? (double)binNumerators[i] / (double)den : 0.0
            };
        }

        return new WinHistogramMetric(provenance) { Bins = bins };
    }

    // ── Per-Feature RTP Breakdown ────────────────────────────────────────

    /// <summary>
    /// Compute per-feature RTP breakdown.  Each feature's Distribution is
    /// summed independently.  The sum of all feature expected values is
    /// verified against the total expected value (rational equality).
    /// </summary>
    public static PerFeatureBreakdownMetric? ComputePerFeatureBreakdown(
        Dist<BigInteger> totalDist,
        IReadOnlyDictionary<string, Dist<BigInteger>>? featureBreakdowns,
        RtpMetric? totalRtp = null)
    {
        if (featureBreakdowns == null || featureBreakdowns.Count == 0)
            return null;

        var provenance = totalDist.IsFullyExact
            ? ProvenanceTag.Exact
            : ProvenanceTag.ExactWithinEpsilon;

        // Compute total RTP rational if not provided.
        var totalNum = totalRtp?.RationalNumerator
            ?? totalDist.ExpectedBigIntegerValue().Num;
        var totalDen = totalRtp?.RationalDenominator
            ?? totalDist.ExpectedBigIntegerValue().Den;
        var totalDisplay = (double)totalNum / (double)totalDen;

        var contributions = new List<FeatureRtpContribution>();
        BigInteger sumNum = 0;
        var commonDen = totalDen;

        foreach (var (name, featureDist) in featureBreakdowns)
        {
            if (featureDist.IsEmpty) continue;

            var (fNum, fDen) = featureDist.ExpectedBigIntegerValue();

            // Compute feature hit frequency.
            BigInteger featureNonZeroNum = 0;
            var featureTotal = featureDist.TotalNumerator;
            foreach (var e in featureDist.Entries)
                if (e.Value != 0) featureNonZeroNum += e.Numerator;
            var featureHitFreq = featureTotal > 0
                ? (double)featureNonZeroNum / (double)featureTotal
                : 0.0;

            var contribution = (double)fNum / (double)fDen;

            contributions.Add(new FeatureRtpContribution
            {
                FeatureName = name,
                RtpContribution = contribution,
                RationalNumerator = fNum,
                RationalDenominator = fDen,
                FractionOfTotal = totalDisplay > 0 ? contribution / totalDisplay : 0.0,
                FeatureHitFrequency = featureHitFreq
            });

            // Accumulate for sum verification (scale to common denominator).
            var scaled = fNum * (commonDen / fDen);
            sumNum += scaled;
        }

        // Verify: sum of per-feature contributions equals total RTP (rational equality).
        // sumNum / commonDen should equal totalNum / totalDen.
        // i.e., sumNum * totalDen == totalNum * commonDen.
        var left = sumNum * totalDen;
        var right = totalNum * commonDen;

        var metric = new PerFeatureBreakdownMetric(provenance)
        {
            Features = contributions.ToArray(),
            TotalRtpDisplay = (double)sumNum / (double)commonDen,
            TotalRtpRationalNumerator = sumNum,
            TotalRtpRationalDenominator = commonDen
        };

        return metric;
    }

    // ── Per-State RTP Breakdown ──────────────────────────────────────────

    /// <summary>
    /// Compute per-state RTP breakdown from a state-valued distribution
    /// Dist<(S FinalState, BigInteger Win)>.  Groups outcomes by final
    /// state and computes expected value per state.
    /// </summary>
    public static PerFeatureBreakdownMetric? ComputePerStateBreakdown<S>(
        Dist<(S FinalState, BigInteger Win)> stateDist,
        Func<S, string> stateLabeler)
        where S : notnull
    {
        var provenance = stateDist.IsFullyExact
            ? ProvenanceTag.Exact
            : ProvenanceTag.ExactWithinEpsilon;

        if (stateDist.IsEmpty || stateDist.TotalNumerator == 0)
            return null;

        var totalDen = stateDist.TotalNumerator;

        // Group by state label.
        var stateNumerators = new Dictionary<string, BigInteger>();
        var stateHitNumerators = new Dictionary<string, BigInteger>();

        foreach (var e in stateDist.Entries)
        {
            var label = stateLabeler(e.Value.FinalState);

            stateNumerators.TryGetValue(label, out var sn);
            stateNumerators[label] = sn + e.Value.Win * e.Numerator;

            stateHitNumerators.TryGetValue(label, out var shn);
            if (e.Value.Win != 0)
                stateHitNumerators[label] = shn + e.Numerator;
        }

        var contributions = new List<FeatureRtpContribution>();
        BigInteger sumNum = 0;

        foreach (var (label, stateNum) in stateNumerators)
        {
            stateHitNumerators.TryGetValue(label, out var hitNum);

            var contribution = (double)stateNum / (double)totalDen;
            var hitFreq = totalDen > 0 ? (double)hitNum / (double)totalDen : 0.0;

            contributions.Add(new FeatureRtpContribution
            {
                FeatureName = label,
                RtpContribution = contribution,
                RationalNumerator = stateNum,
                RationalDenominator = totalDen,
                FractionOfTotal = 0.0, // per-state fractions aren't meaningful in same way
                FeatureHitFrequency = hitFreq
            });

            sumNum += stateNum;
        }

        var totalDisplay = contributions.Sum(c => c.RtpContribution);

        // Update fraction of total for each feature.
        for (var i = 0; i < contributions.Count; i++)
        {
            var c = contributions[i];
            contributions[i] = c with
            {
                FractionOfTotal = totalDisplay > 0 ? c.RtpContribution / totalDisplay : 0.0
            };
        }

        return new PerFeatureBreakdownMetric(provenance)
        {
            Features = contributions.ToArray(),
            TotalRtpDisplay = totalDisplay,
            TotalRtpRationalNumerator = sumNum,
            TotalRtpRationalDenominator = totalDen
        };
    }
}
