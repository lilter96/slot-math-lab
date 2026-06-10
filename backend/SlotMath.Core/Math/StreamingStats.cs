using System.Numerics;

namespace SlotMath.Core.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  StreamingStats — Welford online mean/variance, histogram, max-win tracking
//
//  Accumulates samples one at a time in O(1) per sample.  Never stores the
//  full sample array — all statistics are online.  Used by the sampled
//  Monte Carlo interpreter (G6) and consumed by metric reducers (G7).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// A single bin in the win histogram.  Covers [LowerBound, UpperBound).
/// </summary>
public sealed class HistogramBin
{
    public double LowerBound { get; }
    public double UpperBound { get; }
    public long Count { get; internal set; }

    /// <summary>Midpoint of the bin, useful for plotting.</summary>
    public double Midpoint => (LowerBound + UpperBound) / 2.0;

    internal HistogramBin(double lower, double upper)
    {
        LowerBound = lower;
        UpperBound = upper;
    }

    public override string ToString() =>
        $"[{LowerBound:F2}, {UpperBound:F2}): {Count}";
}

/// <summary>
/// Streaming statistics accumulator using Welford's online algorithm for
/// mean and variance, plus optional max-win capping and a fixed-width histogram.
///
/// Thread-safety: NOT thread-safe.  Callers must serialise Add() calls.
/// </summary>
public sealed class StreamingStats
{
    // ── Welford accumulators ────────────────────────────────────────────
    private long _n;
    private double _mean;
    private double _m2;

    // ── Range tracking ──────────────────────────────────────────────────
    private double _minObserved = double.PositiveInfinity;
    private double _maxObserved = double.NegativeInfinity;
    private long _capHitCount;
    private long _nonZeroCount;
    private readonly double? _maxWinCap;

    // ── Histogram ───────────────────────────────────────────────────────
    private readonly int _histogramBins;
    private readonly bool _hasFixedHistogram;
    private double _fixedBinWidth;
    private readonly long[] _binCounts;

    // ── Dynamic (no-cap) histogram ──────────────────────────────────────
    // Without a cap the bin range is unknown up front, so the first
    // DynamicHistogramBufferSize samples are buffered.  When the buffer
    // fills, the observed [min, max] range is frozen, the buffer is binned
    // and dropped, and later samples are binned live (outliers clamp into
    // the edge bins).  Bounded memory, non-empty counts.
    internal const int DynamicHistogramBufferSize = 65_536;
    private double[]? _dynamicBuffer;
    private int _dynamicBuffered;
    private bool _dynamicRangeFrozen;
    private double _dynamicMin;
    private double _dynamicWidth;

    /// <summary>Number of samples accumulated.</summary>
    public long Count => _n;

    /// <summary>Running mean (Welford). 0 when Count == 0.</summary>
    public double Mean => _n > 0 ? _mean : 0.0;

    /// <summary>Sample variance (unbiased estimator). 0 when Count &lt; 2.</summary>
    public double Variance => _n > 1 ? _m2 / (_n - 1) : 0.0;

    /// <summary>Sample standard deviation.</summary>
    public double StdDev => System.Math.Sqrt(Variance);

    /// <summary>Standard error of the mean = StdDev / sqrt(n).</summary>
    public double StdErr => _n > 0 ? StdDev / System.Math.Sqrt(_n) : 0.0;

    /// <summary>95% confidence interval half-width = 1.96 * StdErr.</summary>
    public double Ci95Half => 1.96 * StdErr;

    /// <summary>Minimum observed value, or NaN if no samples.</summary>
    public double MinObserved => _n > 0 ? _minObserved : double.NaN;

    /// <summary>Maximum observed value, or NaN if no samples.</summary>
    public double MaxObserved => _n > 0 ? _maxObserved : double.NaN;

    /// <summary>
    /// Number of samples that hit the configured max-win cap.
    /// 0 when no cap is set.
    /// </summary>
    public long CapHits => _capHitCount;

    /// <summary>Number of samples with non-zero values (hits).</summary>
    public long NonZeroCount => _nonZeroCount;

    /// <summary>
    /// Hit frequency = NonZeroCount / Count.  The fraction of spins that
    /// returned a non-zero win. 0 when no samples.
    /// </summary>
    public double HitFrequency => _n > 0 ? (double)_nonZeroCount / _n : 0.0;

    /// <summary>
    /// Standard error of the hit frequency proportion:
    /// sqrt(p * (1-p) / n).  0 when n &lt; 2.
    /// </summary>
    public double HitFrequencyStdErr
    {
        get
        {
            if (_n < 2) return 0.0;
            var p = HitFrequency;
            if (p <= 0.0 || p >= 1.0) return 0.0;
            return System.Math.Sqrt(p * (1.0 - p) / _n);
        }
    }

    /// <summary>The max-win cap, or null if unset.</summary>
    public double? MaxWinCap => _maxWinCap;

    /// <summary>Histogram bins (empty until BuildHistogram is called, or live when a cap is set).</summary>
    public IReadOnlyList<HistogramBin> Histogram => BuildHistogram();

    // ── Construction ─────────────────────────────────────────────────────

    /// <summary>
    /// Create a streaming stats collector.
    /// </summary>
    /// <param name="histogramBins">Number of equal-width bins for the histogram (default 50).</param>
    /// <param name="maxWinCap">Optional cap — values above this are clamped and counted as cap-hits.</param>
    public StreamingStats(int histogramBins = 50, double? maxWinCap = null)
    {
        if (histogramBins < 1)
            throw new ArgumentOutOfRangeException(nameof(histogramBins), "Must have at least 1 histogram bin.");

        _histogramBins = histogramBins;
        _maxWinCap = maxWinCap;
        _binCounts = new long[histogramBins];

        if (maxWinCap.HasValue)
        {
            _hasFixedHistogram = true;
            _fixedBinWidth = maxWinCap.Value / histogramBins;
        }
    }

    // ── Add ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Add a single sample value.  O(1) — Welford update + optional bin increment.
    /// </summary>
    public void Add(double value)
    {
        // ── Hit-frequency tracking (before clamping) ──────────────────
        if (value != 0.0)
            _nonZeroCount++;

        // ── Cap enforcement ──────────────────────────────────────────
        double clamped = value;
        if (_maxWinCap.HasValue && value > _maxWinCap.Value)
        {
            clamped = _maxWinCap.Value;
            _capHitCount++;
        }

        // ── Welford update ───────────────────────────────────────────
        _n++;
        var delta = clamped - _mean;
        _mean += delta / _n;
        var delta2 = clamped - _mean;
        _m2 += delta * delta2;

        // ── Range ────────────────────────────────────────────────────
        if (clamped < _minObserved) _minObserved = clamped;
        if (clamped > _maxObserved) _maxObserved = clamped;

        // ── Histogram ────────────────────────────────────────────────
        if (_hasFixedHistogram)
        {
            var binIndex = (int)(clamped / _fixedBinWidth);
            if (binIndex >= _histogramBins)
                binIndex = _histogramBins - 1; // clamp to last bin (includes cap value)
            if (binIndex < 0)
                binIndex = 0;
            _binCounts[binIndex]++;
        }
        else if (_dynamicRangeFrozen)
        {
            _binCounts[DynamicBinIndex(clamped)]++;
        }
        else
        {
            _dynamicBuffer ??= new double[DynamicHistogramBufferSize];
            _dynamicBuffer[_dynamicBuffered++] = clamped;
            if (_dynamicBuffered == DynamicHistogramBufferSize)
                FreezeDynamicRange();
        }
    }

    /// <summary>
    /// Add a BigInteger sample.  Converts to double (lossy for values above 2^53,
    /// which is acceptable for Monte Carlo statistics).
    /// </summary>
    public void Add(BigInteger value) => Add((double)value);

    /// <summary>
    /// Freeze the dynamic histogram range at the currently observed
    /// [min, max], bin the buffered samples, and drop the buffer.
    /// </summary>
    private void FreezeDynamicRange()
    {
        var range = _maxObserved - _minObserved;
        if (range <= 0)
            range = 1.0;

        _dynamicMin = _minObserved;
        _dynamicWidth = range / _histogramBins;
        _dynamicRangeFrozen = true;

        var buffer = _dynamicBuffer!;
        for (var i = 0; i < _dynamicBuffered; i++)
            _binCounts[DynamicBinIndex(buffer[i])]++;

        _dynamicBuffer = null;
        _dynamicBuffered = 0;
    }

    private int DynamicBinIndex(double value)
    {
        var binIndex = (int)((value - _dynamicMin) / _dynamicWidth);
        if (binIndex >= _histogramBins) return _histogramBins - 1;
        if (binIndex < 0) return 0;
        return binIndex;
    }

    // ── Histogram ────────────────────────────────────────────────────────

    /// <summary>
    /// Build (or rebuild) the histogram from accumulated data.
    /// When a max-win cap is set at construction, the histogram is live (updated
    /// on each Add).  Otherwise, bins are computed from the observed [min, max]
    /// range at call time.
    /// </summary>
    public HistogramBin[] BuildHistogram()
    {
        var bins = new HistogramBin[_histogramBins];

        if (_n == 0)
        {
            for (var i = 0; i < _histogramBins; i++)
                bins[i] = new HistogramBin(i, i + 1);
            return bins;
        }

        if (_hasFixedHistogram)
        {
            for (var i = 0; i < _histogramBins; i++)
            {
                bins[i] = new HistogramBin(
                    i * _fixedBinWidth,
                    (i + 1) * _fixedBinWidth)
                {
                    Count = _binCounts[i]
                };
            }
            return bins;
        }

        // ── Dynamic binning (no cap) ──────────────────────────────────────
        if (_dynamicRangeFrozen)
        {
            // Range frozen: live bin counts are authoritative.
            for (var i = 0; i < _histogramBins; i++)
            {
                bins[i] = new HistogramBin(
                    _dynamicMin + i * _dynamicWidth,
                    _dynamicMin + (i + 1) * _dynamicWidth)
                {
                    Count = _binCounts[i]
                };
            }
            return bins;
        }

        // Still buffering: bin the buffered samples over the current
        // [min, max] range without freezing it.
        var range = _maxObserved - _minObserved;
        if (range <= 0)
            range = 1.0;
        var binWidth = range / _histogramBins;

        var counts = new long[_histogramBins];
        var buffer = _dynamicBuffer;
        for (var i = 0; i < _dynamicBuffered; i++)
        {
            var binIndex = (int)((buffer![i] - _minObserved) / binWidth);
            if (binIndex >= _histogramBins) binIndex = _histogramBins - 1;
            if (binIndex < 0) binIndex = 0;
            counts[binIndex]++;
        }

        for (var i = 0; i < _histogramBins; i++)
        {
            bins[i] = new HistogramBin(
                _minObserved + i * binWidth,
                _minObserved + (i + 1) * binWidth)
            {
                Count = counts[i]
            };
        }

        return bins;
    }

    /// <summary>
    /// Merge another StreamingStats into this one: Welford accumulators are
    /// combined with Chan's parallel algorithm, and histograms are merged
    /// when the layouts are compatible.
    ///
    /// Histogram merging:
    /// - Identical fixed-cap layouts (same cap, same bin count) add exactly.
    /// - Dynamic histograms merge exactly while the other side is still
    ///   buffering raw samples; a frozen dynamic histogram merges
    ///   approximately by re-binning its counts at bin midpoints.
    /// </summary>
    public void Merge(StreamingStats other)
    {
        if (other._n == 0) return;

        // ── Welford (Chan) ───────────────────────────────────────────
        if (_n == 0)
        {
            _mean = other._mean;
            _m2 = other._m2;
            _n = other._n;
        }
        else
        {
            var totalN = _n + other._n;
            var delta = other._mean - _mean;
            _m2 = _m2 + other._m2 + delta * delta * _n * other._n / totalN;
            _mean = (_n * _mean + other._n * other._mean) / totalN;
            _n = totalN;
        }

        if (other._minObserved < _minObserved) _minObserved = other._minObserved;
        if (other._maxObserved > _maxObserved) _maxObserved = other._maxObserved;
        _capHitCount += other._capHitCount;
        _nonZeroCount += other._nonZeroCount;

        // ── Histogram ────────────────────────────────────────────────
        if (_hasFixedHistogram && other._hasFixedHistogram
            && _fixedBinWidth == other._fixedBinWidth
            && _binCounts.Length == other._binCounts.Length)
        {
            for (var i = 0; i < _binCounts.Length; i++)
                _binCounts[i] += other._binCounts[i];
            return;
        }

        if (_hasFixedHistogram || other._hasFixedHistogram)
        {
            // Mismatched layouts: re-bin the other side approximately.
            RebinFrom(other);
            return;
        }

        // Both dynamic.
        if (!other._dynamicRangeFrozen)
        {
            // The other side still holds raw samples — merge exactly.
            var buffer = other._dynamicBuffer;
            for (var i = 0; i < other._dynamicBuffered; i++)
                AddSampleToHistogram(buffer![i]);
        }
        else
        {
            RebinFrom(other);
        }
    }

    /// <summary>
    /// Approximate merge of another histogram into this one by adding each
    /// of its bin counts at the bin midpoint.
    /// </summary>
    private void RebinFrom(StreamingStats other)
    {
        foreach (var bin in other.BuildHistogram())
        {
            if (bin.Count == 0) continue;

            // Buffering targets take samples one by one until the range
            // freezes; frozen/fixed targets take the whole count at once.
            var remaining = bin.Count;
            while (remaining > 0 && !_hasFixedHistogram && !_dynamicRangeFrozen)
            {
                AddSampleToHistogram(bin.Midpoint);
                remaining--;
            }
            if (remaining == 0) continue;

            if (_hasFixedHistogram)
            {
                var binIndex = (int)(bin.Midpoint / _fixedBinWidth);
                if (binIndex >= _histogramBins) binIndex = _histogramBins - 1;
                if (binIndex < 0) binIndex = 0;
                _binCounts[binIndex] += remaining;
            }
            else
            {
                _binCounts[DynamicBinIndex(bin.Midpoint)] += remaining;
            }
        }
    }

    /// <summary>
    /// Route a value into this instance's histogram without touching the
    /// Welford accumulators (used by merging).
    /// </summary>
    private void AddSampleToHistogram(double value)
    {
        if (_hasFixedHistogram)
        {
            var binIndex = (int)(value / _fixedBinWidth);
            if (binIndex >= _histogramBins) binIndex = _histogramBins - 1;
            if (binIndex < 0) binIndex = 0;
            _binCounts[binIndex]++;
        }
        else if (_dynamicRangeFrozen)
        {
            _binCounts[DynamicBinIndex(value)]++;
        }
        else
        {
            _dynamicBuffer ??= new double[DynamicHistogramBufferSize];
            _dynamicBuffer[_dynamicBuffered++] = value;
            if (_dynamicBuffered == DynamicHistogramBufferSize)
                FreezeDynamicRange();
        }
    }

    // ── Snapshot ─────────────────────────────────────────────────────────

    /// <summary>
    /// Return a point-in-time snapshot.  The returned object is independent
    /// and will not change as more samples are added to this instance.
    /// </summary>
    public StreamingStatsSnapshot Snapshot()
    {
        return new StreamingStatsSnapshot(
            Count: _n,
            Mean: Mean,
            Variance: Variance,
            StdDev: StdDev,
            StdErr: StdErr,
            Ci95Half: Ci95Half,
            MinObserved: MinObserved,
            MaxObserved: MaxObserved,
            CapHits: _capHitCount,
            NonZeroCount: _nonZeroCount,
            HitFrequency: HitFrequency,
            HitFrequencyStdErr: HitFrequencyStdErr,
            MaxWinCap: _maxWinCap,
            Histogram: BuildHistogram());
    }

    public override string ToString()
    {
        if (_n == 0) return "StreamingStats[empty]";
        return $"StreamingStats[n={_n}, μ={Mean:F4}, σ={StdDev:F4}, " +
               $"range=[{MinObserved:F2}, {MaxObserved:F2}], capHits={_capHitCount}, hitFreq={HitFrequency:P4}]";
    }
}

/// <summary>
/// Immutable snapshot of streaming statistics at a point in time.
/// </summary>
/// <param name="Count">Number of samples.</param>
/// <param name="Mean">Running mean.</param>
/// <param name="Variance">Sample variance.</param>
/// <param name="StdDev">Sample standard deviation.</param>
/// <param name="StdErr">Standard error of the mean.</param>
/// <param name="Ci95Half">95% CI half-width (1.96 * StdErr).</param>
/// <param name="MinObserved">Minimum observed value.</param>
/// <param name="MaxObserved">Maximum observed value.</param>
/// <param name="CapHits">Number of cap hits.</param>
/// <param name="NonZeroCount">Number of non-zero samples.</param>
/// <param name="HitFrequency">Fraction of non-zero samples.</param>
/// <param name="HitFrequencyStdErr">Standard error of hit frequency proportion.</param>
/// <param name="MaxWinCap">Configured max-win cap, if any.</param>
/// <param name="Histogram">Histogram bins at snapshot time.</param>
public sealed record StreamingStatsSnapshot(
    long Count,
    double Mean,
    double Variance,
    double StdDev,
    double StdErr,
    double Ci95Half,
    double MinObserved,
    double MaxObserved,
    long CapHits,
    long NonZeroCount,
    double HitFrequency,
    double HitFrequencyStdErr,
    double? MaxWinCap,
    HistogramBin[] Histogram
);
