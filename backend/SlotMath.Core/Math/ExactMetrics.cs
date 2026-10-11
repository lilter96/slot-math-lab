using System.Numerics;

namespace SlotMath.Core.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  ExactMetrics — compute all G7 metrics from a Dist<BigInteger>
//
//  Every metric carries provenance Exact (when unpruned) or
//  ExactInterval (when pruned).  The volatility/variance is computed
//  from the *full distribution* — not a sample — so it is exact.
//
//  Per-feature contributions sum to total RTP exactly (rational equality)
//  on the exact path.
// ═══════════════════════════════════════════════════════════════════════════

public static class ExactMetrics
{
    /// <summary>
    ///     Compute the full SlotMathReport from an exact BigInteger-valued distribution.
    /// </summary>
    /// <param name="dist">The exact win distribution from the interpreter.</param>
    /// <param name="maxWinCap">Optional max-win cap for cap-hit probability (in credits).</param>
    /// <param name="featureBreakdowns">
    ///     Optional per-feature distributions.  Each feature's dist is summed to produce
    ///     the per-feature RTP breakdown.  The sum of per-feature expected values must
    ///     equal the total expected value exactly.
    /// </param>
    /// <param name="numHistogramBins">Number of histogram bins (default 50).</param>
    /// <param name="winScale">
    ///     Sub-credit scale of the distribution's values.  The compiler emits win
    ///     amounts multiplied by this factor so fractional paytable payouts stay
    ///     exact integers; the report converts back to credits (rationals get the
    ///     scale folded into their denominators — exactness is preserved).
    /// </param>
    public static SlotMathReport Compute(
        Dist<BigInteger> dist,
        BigInteger? maxWinCap = null,
        IReadOnlyDictionary<string, Dist<BigInteger>>? featureBreakdowns = null,
        int numHistogramBins = 50,
        BigInteger? winScale = null)
    {
        ProvenanceTag provenance = dist.IsFullyExact
            ? ProvenanceTag.Exact
            : ProvenanceTag.MassLoss(new Rational(dist.PrunedNumerator, dist.PrunedDenominator).ToDouble());

        BigInteger scale = winScale is { } s && s > 1 ? s : BigInteger.One;
        BigInteger? capScaled = maxWinCap * scale;

        RtpMetric rtp = ComputeRtp(dist, capScaled);
        HitFrequencyMetric hitFreq = ComputeHitFrequency(dist);
        VolatilityMetric volatility = ComputeVolatility(dist, rtp.DisplayValue);
        MaxWinMetric maxWin = ComputeMaxWin(dist, capScaled);
        WinHistogramMetric histogram = ComputeHistogram(dist, capScaled, numHistogramBins);
        PerFeatureBreakdownMetric? perFeature = ComputePerFeatureBreakdown(dist, featureBreakdowns, rtp);

        var report = new SlotMathReport
        {
            AggregateProvenance = dist.IsFullyExact ? Provenance.Exact : Provenance.ExactWithMassLoss,
            Rtp = rtp,
            HitFrequency = hitFreq,
            Volatility = volatility,
            MaxWin = maxWin,
            Histogram = histogram,
            PerFeatureBreakdown = perFeature
        };

        return scale == BigInteger.One ? report : ScaleToCredits(report, scale);
    }

    /// <summary>
    ///     Convert a report computed over scale-multiplied integer win amounts
    ///     back into credit units.  Rational values stay exact (the scale folds
    ///     into denominators); displays divide; probabilities are unaffected.
    /// </summary>
    /// <summary>
    ///     A display quotient. Operands a double can hold are divided as doubles, as before; larger ones (a law
    ///     over many weighted draws has a denominator far beyond the double range) are divided exactly and rounded once.
    /// </summary>
    private static double Ratio(BigInteger numerator, BigInteger denominator) =>
        numerator.GetBitLength() < 1000 && denominator.GetBitLength() < 1000
            ? (double)numerator / (double)denominator
            : new Rational(numerator, denominator).ToDouble();

    private static SlotMathReport ScaleToCredits(SlotMathReport report, BigInteger scale)
    {
        var s = (double)scale;

        RtpMetric rtp = report.Rtp;
        if (rtp.RationalNumerator is { } num && rtp.RationalDenominator is { } den)
        {
            (BigInteger rNum, BigInteger rDen) = Rational.Reduce(num, den * scale);
            rtp = rtp with { RationalNumerator = rNum, RationalDenominator = rDen };
        }

        rtp = rtp with
        {
            Provenance = rtp.Provenance with { Lo = rtp.Provenance.Lo / s, Hi = rtp.Provenance.Hi / s },
            DisplayValue = rtp.DisplayValue / s,
            LoDisplay = rtp.LoDisplay / s,
            HiDisplay = rtp.HiDisplay / s
        };

        VolatilityMetric volatility = report.Volatility with
        {
            Variance = report.Volatility.Variance / (s * s),
            StdDev = report.Volatility.StdDev / s
            // VolatilityIndex = StdDev / Mean is scale-invariant.
        };

        MaxWinMetric maxWin = report.MaxWin with
        {
            MaxWin = report.MaxWin.MaxWin / s,
            // Exact value only stays exact when the scale divides it.
            MaxWinExact = report.MaxWin.MaxWinExact is { } mw && mw % scale == 0
                ? mw / scale
                : null
        };

        WinHistogramMetric histogram = report.Histogram with
        {
            Bins = Array.ConvertAll(report.Histogram.Bins,
                b => b with { LowerBound = b.LowerBound / s, UpperBound = b.UpperBound / s })
        };

        PerFeatureBreakdownMetric? perFeature = report.PerFeatureBreakdown;
        if (perFeature is not null)
        {
            FeatureRtpContribution[] features = Array.ConvertAll(perFeature.Features, f =>
            {
                FeatureRtpContribution scaled = f with
                {
                    RtpContribution = f.RtpContribution / s
                    // FractionOfTotal and FeatureHitFrequency are ratios —
                    // scale-invariant.
                };
                if (f.RationalNumerator is { } fn && f.RationalDenominator is { } fd)
                {
                    (BigInteger fNum, BigInteger fDen) = Rational.Reduce(fn, fd * scale);
                    scaled = scaled with { RationalNumerator = fNum, RationalDenominator = fDen };
                }

                return scaled;
            });
            perFeature = perFeature with { Features = features, TotalRtpDisplay = perFeature.TotalRtpDisplay / s };
            if (perFeature.TotalRtpRationalNumerator is { } tn
                && perFeature.TotalRtpRationalDenominator is { } td)
            {
                (BigInteger tNum, BigInteger tDen) = Rational.Reduce(tn, td * scale);
                perFeature = perFeature with { TotalRtpRationalNumerator = tNum, TotalRtpRationalDenominator = tDen };
            }
        }

        return report with
        {
            Rtp = rtp,
            Volatility = volatility,
            MaxWin = maxWin,
            Histogram = histogram,
            PerFeatureBreakdown = perFeature
        };
    }

    // ── RTP ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     Compute RTP from the exact distribution.  Returns both the exact
    ///     rational (num/den) and a display-friendly double.
    /// </summary>
    public static RtpMetric ComputeRtp(Dist<BigInteger> dist, BigInteger? maxWinCap = null)
    {
        if (maxWinCap is { } cap && cap <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWinCap));
        }

        Rational known = Rational.Zero;
        foreach (Dist<BigInteger>.Entry entry in dist.Entries)
        {
            if (entry.Value.Sign < 0)
            {
                throw new InvalidOperationException("Payouts must be non-negative.");
            }

            known += new Rational(entry.Value * entry.Numerator, dist.Denominator);
        }

        var mass = new Rational(dist.PrunedNumerator, dist.PrunedDenominator);
        var lo = System.Math.BitDecrement(known.ToDouble());
        var hi = maxWinCap is { } bound ? System.Math.BitIncrement((known + mass * bound).ToDouble()) : (double?)null;
        ProvenanceTag provenance = dist.IsFullyExact ? ProvenanceTag.Exact
            : hi is { } upper ? ProvenanceTag.Interval(lo, upper, mass.ToDouble(), BoundSource.DeclaredWinCap)
            : ProvenanceTag.MassLoss(mass.ToDouble());
        return new RtpMetric(provenance)
        {
            RationalNumerator = known.Numerator,
            RationalDenominator = known.Denominator,
            DisplayValue = known.ToDouble(),
            LoDisplay = !dist.IsFullyExact && hi is not null ? lo : null,
            HiDisplay = !dist.IsFullyExact ? hi : null,
            PrunedMass = !dist.IsFullyExact ? mass.ToDouble() : null
        };
    }

    // ── Hit Frequency ────────────────────────────────────────────────────

    /// <summary>
    ///     Compute hit frequency = probability mass of non-zero win outcomes.
    /// </summary>
    public static HitFrequencyMetric ComputeHitFrequency(Dist<BigInteger> dist)
    {
        ProvenanceTag provenance = dist.IsFullyExact
            ? ProvenanceTag.Exact
            : ProvenanceTag.MassLoss(new Rational(dist.PrunedNumerator, dist.PrunedDenominator).ToDouble());

        BigInteger nonZeroNum = 0;
        foreach (Dist<BigInteger>.Entry e in dist.Entries)
        {
            if (e.Value != 0)
            {
                nonZeroNum += e.Numerator;
            }
        }

        BigInteger den = dist.Denominator;
        var display = Ratio(nonZeroNum, den);

        return new HitFrequencyMetric(provenance)
        {
            RationalNumerator = nonZeroNum,
            RationalDenominator = den,
            DisplayValue = display
        };
    }

    // ── Volatility ───────────────────────────────────────────────────────

    /// <summary>
    ///     Compute variance, std deviation, and volatility index from the
    ///     FULL exact distribution (not a sample).  This is the exact volatility,
    ///     not an estimate.
    ///     Var[X] = E[X²] - (E[X])²
    /// </summary>
    public static VolatilityMetric ComputeVolatility(Dist<BigInteger> dist, double rtpDisplay)
    {
        ProvenanceTag provenance = dist.IsFullyExact
            ? ProvenanceTag.Exact
            : ProvenanceTag.MassLoss(new Rational(dist.PrunedNumerator, dist.PrunedDenominator).ToDouble());

        if (dist.IsEmpty || dist.TotalNumerator == 0)
        {
            return new VolatilityMetric(provenance) { Variance = 0.0, StdDev = 0.0, VolatilityIndex = null };
        }

        // E[X] — the mean as a double.
        var mean = rtpDisplay;

        // E[X²] — expected value of squared win.
        BigInteger sumXSq = 0;
        foreach (Dist<BigInteger>.Entry e in dist.Entries)
        {
            sumXSq += e.Value * e.Value * e.Numerator;
        }

        var eXSq = Ratio(sumXSq, dist.Denominator);

        // Var[X] = E[X²] - (E[X])²
        var variance = eXSq - mean * mean;

        // Guard against tiny negative variance from floating-point.
        if (variance < 0 && variance > -1e-15)
        {
            variance = 0.0;
        }

        var stdDev = System.Math.Sqrt(System.Math.Max(0.0, variance));
        double? volIndex = mean > 0 ? stdDev / mean : null;

        return new VolatilityMetric(provenance) { Variance = variance, StdDev = stdDev, VolatilityIndex = volIndex };
    }

    // ── Max Win ──────────────────────────────────────────────────────────

    /// <summary>
    ///     Compute max win and probability of reaching the cap from the exact distribution.
    /// </summary>
    public static MaxWinMetric ComputeMaxWin(Dist<BigInteger> dist, BigInteger? cap = null)
    {
        ProvenanceTag provenance = dist.IsFullyExact
            ? ProvenanceTag.Exact
            : ProvenanceTag.MassLoss(new Rational(dist.PrunedNumerator, dist.PrunedDenominator).ToDouble());

        BigInteger maxWin = 0;
        foreach (Dist<BigInteger>.Entry e in dist.Entries)
        {
            if (e.Value > maxWin)
            {
                maxWin = e.Value;
            }
        }

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
            foreach (Dist<BigInteger>.Entry e in dist.Entries)
            {
                if (e.Value >= cap.Value)
                {
                    capNum += e.Numerator;
                }
            }

            BigInteger den = dist.Denominator;
            metric = metric with
            {
                PCapReached = Ratio(capNum, den),
                PCapRationalNumerator = capNum,
                PCapRationalDenominator = den
            };
        }

        return metric;
    }

    // ── Win Histogram ────────────────────────────────────────────────────

    /// <summary>
    ///     Build a win histogram from the exact distribution by binning outcomes
    ///     into equal-width bins over [0, maxWin] (or [0, cap] if a cap is set).
    ///     Each bin carries exact rational probability mass.
    /// </summary>
    public static WinHistogramMetric ComputeHistogram(
        Dist<BigInteger> dist,
        BigInteger? maxWinCap = null,
        int numBins = 50)
    {
        ProvenanceTag provenance = dist.IsFullyExact
            ? ProvenanceTag.Exact
            : ProvenanceTag.MassLoss(new Rational(dist.PrunedNumerator, dist.PrunedDenominator).ToDouble());

        if (numBins < 1)
        {
            numBins = 1;
        }

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
        {
            maxVal = (double)maxWinCap.Value;
        }
        else
        {
            BigInteger max = 0;
            foreach (Dist<BigInteger>.Entry e in dist.Entries)
            {
                if (e.Value > max)
                {
                    max = e.Value;
                }
            }

            maxVal = (double)max;
        }

        if (maxVal <= 0)
        {
            maxVal = 1.0;
        }

        var binWidth = maxVal / numBins;
        var binNumerators = new BigInteger[numBins];
        BigInteger den = dist.TotalNumerator;

        foreach (Dist<BigInteger>.Entry e in dist.Entries)
        {
            var val = (double)e.Value;
            var binIndex = (int)(val / binWidth);
            if (binIndex >= numBins)
            {
                binIndex = numBins - 1;
            }

            if (binIndex < 0)
            {
                binIndex = 0;
            }

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
                Probability = den > 0 ? Ratio(binNumerators[i], den) : 0.0
            };
        }

        return new WinHistogramMetric(provenance) { Bins = bins };
    }

    // ── Per-Feature RTP Breakdown ────────────────────────────────────────

    /// <summary>
    ///     Compute per-feature RTP breakdown.  Each feature's Distribution is
    ///     summed independently.  The sum of all feature expected values is
    ///     verified against the total expected value (rational equality).
    /// </summary>
    public static PerFeatureBreakdownMetric? ComputePerFeatureBreakdown(
        Dist<BigInteger> totalDist,
        IReadOnlyDictionary<string, Dist<BigInteger>>? featureBreakdowns,
        RtpMetric? totalRtp = null)
    {
        if (featureBreakdowns == null || featureBreakdowns.Count == 0)
        {
            return null;
        }

        ProvenanceTag provenance = totalDist.IsFullyExact
            ? ProvenanceTag.Exact
            : ProvenanceTag.MassLoss(new Rational(totalDist.PrunedNumerator, totalDist.PrunedDenominator).ToDouble());

        // Compute total RTP rational if not provided.
        BigInteger totalNum = totalRtp?.RationalNumerator
                              ?? totalDist.ExpectedBigIntegerValue().Num;
        BigInteger totalDen = totalRtp?.RationalDenominator
                              ?? totalDist.ExpectedBigIntegerValue().Den;
        var totalDisplay = (double)totalNum / (double)totalDen;

        var contributions = new List<FeatureRtpContribution>();
        BigInteger sumNum = 0;
        BigInteger sumDen = 1;

        foreach ((var name, Dist<BigInteger> featureDist) in featureBreakdowns)
        {
            if (featureDist.IsEmpty)
            {
                continue;
            }

            (BigInteger fNum, BigInteger fDen) = featureDist.ExpectedBigIntegerValue();

            // Compute feature hit frequency.
            BigInteger featureNonZeroNum = 0;
            BigInteger featureTotal = featureDist.TotalNumerator;
            foreach (Dist<BigInteger>.Entry e in featureDist.Entries)
            {
                if (e.Value != 0)
                {
                    featureNonZeroNum += e.Numerator;
                }
            }

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

            // Accumulate the exact rational sum.  Denominators of individual
            // features need not divide each other, so the running sum keeps
            // its own LCM denominator — naive scaling by (totalDen / fDen)
            // truncates whenever fDen ∤ totalDen.
            BigInteger newDen = Rational.Lcm(sumDen, fDen);
            sumNum = sumNum * (newDen / sumDen) + fNum * (newDen / fDen);
            sumDen = newDen;
        }

        (BigInteger reducedSumNum, BigInteger reducedSumDen) = Rational.Reduce(sumNum, sumDen);

        var metric = new PerFeatureBreakdownMetric(provenance)
        {
            Features = contributions.ToArray(),
            TotalRtpDisplay = (double)reducedSumNum / (double)reducedSumDen,
            TotalRtpRationalNumerator = reducedSumNum,
            TotalRtpRationalDenominator = reducedSumDen
        };

        return metric;
    }

    // ── Per-State RTP Breakdown ──────────────────────────────────────────

    /// <summary>
    ///     Compute per-state RTP breakdown from a state-valued distribution
    ///     Dist
    ///     <(S FinalState, BigInteger Win)>
    ///         .  Groups outcomes by final
    ///         state and computes expected value per state.
    /// </summary>
    public static PerFeatureBreakdownMetric? ComputePerStateBreakdown<S>(
        Dist<(S FinalState, BigInteger Win)> stateDist,
        Func<S, string> stateLabeler)
        where S : notnull
    {
        ProvenanceTag provenance = stateDist.IsFullyExact
            ? ProvenanceTag.Exact
            : ProvenanceTag.MassLoss(new Rational(stateDist.PrunedNumerator, stateDist.PrunedDenominator).ToDouble());

        if (stateDist.IsEmpty || stateDist.TotalNumerator == 0)
        {
            return null;
        }

        BigInteger totalDen = stateDist.TotalNumerator;

        // Group by state label.
        var stateNumerators = new Dictionary<string, BigInteger>();
        var stateHitNumerators = new Dictionary<string, BigInteger>();

        foreach (Dist<(S FinalState, BigInteger Win)>.Entry e in stateDist.Entries)
        {
            var label = stateLabeler(e.Value.FinalState);

            stateNumerators.TryGetValue(label, out BigInteger sn);
            stateNumerators[label] = sn + e.Value.Win * e.Numerator;

            stateHitNumerators.TryGetValue(label, out BigInteger shn);
            if (e.Value.Win != 0)
            {
                stateHitNumerators[label] = shn + e.Numerator;
            }
        }

        var contributions = new List<FeatureRtpContribution>();
        BigInteger sumNum = 0;

        foreach ((var label, BigInteger stateNum) in stateNumerators)
        {
            stateHitNumerators.TryGetValue(label, out BigInteger hitNum);

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
            FeatureRtpContribution c = contributions[i];
            contributions[i] = c with { FractionOfTotal = totalDisplay > 0 ? c.RtpContribution / totalDisplay : 0.0 };
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
