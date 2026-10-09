using M = System.Math;

namespace SlotMath.Core.Measurements;

/// <summary>Bounded, worker-private sufficient statistics. Merge copies every mutable container:
/// round buffers and progress deltas can be cleared without corrupting retained evidence.</summary>
internal sealed class MeasurementAnalysisAccumulator(MeasurementOptions options)
{
    public MeasurementAnalysisAccumulator Empty() => new(options);
    private readonly double _alpha = (1 - options.Confidence) / options.ErrorFamilySize;
    private readonly double _critical = StatisticalInference.NormalCritical((1 - options.Confidence) / options.ErrorFamilySize);
    private RunningMoments _moments, _paired;
    private readonly PairDiagnostics _joint = new(options.SupportLimit);
    private bool _groupsComplete = true;
    private RunningMoments _weightedMoments;
    private RunningMoments _weightedPairMoments;
    private RunningMoments _weightedEventMoments;
    private double _weightedCoMoment;
    private double _weightedEvents;
    private RunningMoments _clusterSums, _clusterCounts;
    private double _clusterCoMoment;
    private double _coMoment, _weightSum, _weightSquares, _weightedSum, _minWeight = double.PositiveInfinity, _maxWeight;
    private long _events, _distinctParents, _entries, _exits, _unclosed, _uniqueAwards, _duplicateAwards, _assertionViolations;
    private bool _supportComplete = true;
    private readonly SortedDictionary<double, long> _support = new();
    private readonly SortedDictionary<double, double> _weightSupport = new();
    private readonly Dictionary<(double From, double To), long> _transitions = new();
    private bool _transitionsComplete = true;
    private readonly long[] _binCount = new long[options.BinEdges.Length + 1];
    private readonly double[] _binSum = new double[options.BinEdges.Length + 1], _binSquares = new double[options.BinEdges.Length + 1];
    private readonly long[] _tailCount = new long[options.Thresholds.Length];
    private readonly double[] _tailSum = new double[options.Thresholds.Length], _tailSquares = new double[options.Thresholds.Length];
    private readonly Dictionary<string, MeasurementAnalysisAccumulator> _groups = new(StringComparer.Ordinal);
    private readonly SequenceAccumulator? _sequence = options.Lags.Length > 0 ? new(options.Lags, options.SupportLimit) : null;

    public void Reset()
    {
        _joint.Reset(); _groupsComplete = true;
        _moments = _paired = default; _coMoment = _weightSum = _weightSquares = _weightedSum = _maxWeight = 0; _minWeight = double.PositiveInfinity;
        _clusterSums = _clusterCounts = default; _clusterCoMoment = 0;
        _weightedMoments = _weightedPairMoments = _weightedEventMoments = default; _weightedEvents = _weightedCoMoment = 0;
        _transitions.Clear(); _transitionsComplete = true;
        _events = _distinctParents = _entries = _exits = _unclosed = _uniqueAwards = _duplicateAwards = _assertionViolations = 0;
        _supportComplete = true; _support.Clear(); _weightSupport.Clear(); Array.Clear(_binCount); Array.Clear(_binSum); Array.Clear(_binSquares);
        Array.Clear(_tailCount); Array.Clear(_tailSum); Array.Clear(_tailSquares); _sequence?.Reset();
        foreach (var group in _groups.Values) group.Reset();
    }

    public void Parent(bool matching, long entries, long exits, long unclosed, long uniqueAwards, long duplicateAwards, long? observations = null, double sum = 0)
    { if (matching) _distinctParents++; _entries += entries; _exits += exits; _unclosed += unclosed; _uniqueAwards += uniqueAwards; _duplicateAwards += duplicateAwards;
        if (observations is { } count)
        { var dx = sum - _clusterSums.Mean; var dy = count - _clusterCounts.Mean; _clusterCoMoment += dx * dy * _clusterSums.Count / (_clusterSums.Count + 1d); _clusterSums.Add(sum); _clusterCounts.Add(count); }
        foreach (var group in _groups.Values) group.Parent(group._moments.Count > 0, 0, 0, 0, 0, 0, group._moments.Count, group._moments.Sum); }
    public void Add(double value, double? pair, double? weight, string? group, bool assertionViolation = false)
    {
        if (_groupsComplete && group is not null && !_groups.ContainsKey(group) && _groups.Count >= options.GroupLimit)
        { _groupsComplete = false; _groups.Clear(); }
        if (options.LowerBound is { } low && value < low || options.UpperBound is { } high && value > high)
            throw new InvalidOperationException("Measurement value violates its declared bounds; inference would be invalid.");
        var powers = value * value;
        if (!double.IsFinite(powers * powers) || pair is { } y && !double.IsFinite(y * y)
            || weight is { } w && (!(w >= 0) || !double.IsFinite(w * w) || !double.IsFinite(w * value)))
            throw new InvalidOperationException("Measurement moments or likelihood weight exceed finite numeric range.");
        var moments = _moments; moments.Add(value);
        var paired = _paired; if (pair is { } previewPair) paired.Add(previewPair);
        var weighted = _weightedMoments;
        var weightedPair = _weightedPairMoments;
        var weightedEvent = _weightedEventMoments;
        if (weight is { } previewWeight)
        {
            var product = previewWeight * value; weighted.Add(product, highOrder: false);
            weightedEvent.Add(value != 0 ? previewWeight : 0, highOrder: false);
            if (pair is { } previewSecondary) weightedPair.Add(previewWeight * previewSecondary, highOrder: false);
            if (!double.IsFinite(_weightSum + previewWeight) || !double.IsFinite(_weightSquares + previewWeight * previewWeight) || !double.IsFinite(_weightedSum + product))
                throw new InvalidOperationException("Accumulated likelihood weights exceed finite numeric range.");
        }
        if (pair is { } secondary)
        {
            var dx = value - _moments.Mean; var dy = secondary - _paired.Mean;
            _joint.Add(value, secondary);
            _paired = paired; _coMoment += dx * dy * _moments.Count / (_moments.Count + 1d);
        }
        _moments = moments; if (value != 0) _events++;
        if (options.Assertion == "zero" && assertionViolation) _assertionViolations++;
        if (options.Subject == "transition" && pair is { } nextState && _transitionsComplete)
        {
            var key = (value, nextState);
            if (!_transitions.ContainsKey(key) && _transitions.Count >= options.SupportLimit) { _transitionsComplete = false; _transitions.Clear(); }
            else _transitions[key] = _transitions.GetValueOrDefault(key) + 1;
        }
        if (_supportComplete)
        {
            if (_support.TryGetValue(value, out var count)) _support[value] = count + 1;
            else if (_support.Count < options.SupportLimit) _support[value] = 1;
            else { _supportComplete = false; _support.Clear(); _weightSupport.Clear(); }
            if (_supportComplete && weight is { } supportWeight) _weightSupport[value] = _weightSupport.GetValueOrDefault(value) + supportWeight;
        }
        var bin = Array.BinarySearch(options.BinEdges, value); bin = bin >= 0 ? bin + 1 : ~bin;
        _binCount[bin]++; _binSum[bin] += value; _binSquares[bin] += powers;
        for (var i = 0; i < options.Thresholds.Length; i++) if (value >= options.Thresholds[i])
        { _tailCount[i]++; _tailSum[i] += value; _tailSquares[i] += powers; }
        if (weight is { } likelihood)
        {
            if (pair is { } weightedSecondary)
            {
                _weightedCoMoment += (likelihood * value - _weightedMoments.Mean) * (likelihood * weightedSecondary - _weightedPairMoments.Mean) * _weightedMoments.Count / (_weightedMoments.Count + 1d);
                _weightedPairMoments = weightedPair;
            }
            _weightSum += likelihood; _weightSquares += likelihood * likelihood; _weightedSum += likelihood * value;
            _minWeight = M.Min(_minWeight, likelihood); _maxWeight = M.Max(_maxWeight, likelihood); _weightedMoments = weighted;
            _weightedEventMoments = weightedEvent;
            if (value != 0) _weightedEvents += likelihood;
        }
        _sequence?.Add(value);
        if (_groupsComplete && group is not null)
        {
            if (!_groups.TryGetValue(group, out var child)) _groups.Add(group, child = GroupAccumulator());
            child.Add(value, pair, weight, null, assertionViolation);
        }
    }

    public void Merge(MeasurementAnalysisAccumulator other)
    {
        _joint.Merge(other._joint);
        var previousParents = _clusterSums.Count;
        var parents = _clusterSums.Count + other._clusterSums.Count;
        _clusterCoMoment += other._clusterCoMoment + (parents == 0 ? 0 : (other._clusterSums.Mean - _clusterSums.Mean) * (other._clusterCounts.Mean - _clusterCounts.Mean) * ((double)_clusterSums.Count * other._clusterSums.Count / parents));
        _clusterSums.Merge(other._clusterSums); _clusterCounts.Merge(other._clusterCounts);
        var combined = _moments.Count + other._moments.Count;
        if (combined > 0 && _paired.Count > 0 && other._paired.Count > 0)
            _coMoment += other._coMoment + (other._moments.Mean - _moments.Mean) * (other._paired.Mean - _paired.Mean) * ((double)_moments.Count * other._moments.Count / combined);
        else _coMoment += other._coMoment;
        _moments.Merge(other._moments); _paired.Merge(other._paired);
        var weightedCount = _weightedMoments.Count + other._weightedMoments.Count;
        _weightedCoMoment += other._weightedCoMoment + (weightedCount == 0 ? 0 : (other._weightedMoments.Mean - _weightedMoments.Mean) * (other._weightedPairMoments.Mean - _weightedPairMoments.Mean) * ((double)_weightedMoments.Count * other._weightedMoments.Count / weightedCount));
        _weightedMoments.Merge(other._weightedMoments, false); _weightedPairMoments.Merge(other._weightedPairMoments, false); _weightedEventMoments.Merge(other._weightedEventMoments, false); _weightedEvents += other._weightedEvents;
        if (!_transitionsComplete || !other._transitionsComplete) { _transitionsComplete = false; _transitions.Clear(); }
        else foreach (var (key, count) in other._transitions)
        {
            if (!_transitions.ContainsKey(key) && _transitions.Count >= options.SupportLimit) { _transitionsComplete = false; _transitions.Clear(); break; }
            _transitions[key] = _transitions.GetValueOrDefault(key) + count;
        }
        _events += other._events; _distinctParents += other._distinctParents; _entries += other._entries; _exits += other._exits;
        _unclosed += other._unclosed; _uniqueAwards += other._uniqueAwards; _duplicateAwards += other._duplicateAwards;
        _assertionViolations += other._assertionViolations;
        _weightSum += other._weightSum; _weightSquares += other._weightSquares; _weightedSum += other._weightedSum;
        _minWeight = M.Min(_minWeight, other._minWeight); _maxWeight = M.Max(_maxWeight, other._maxWeight);
        if (!_supportComplete || !other._supportComplete) { _supportComplete = false; _support.Clear(); _weightSupport.Clear(); }
        else foreach (var (value, count) in other._support)
        {
            if (!_support.ContainsKey(value) && _support.Count >= options.SupportLimit) { _supportComplete = false; _support.Clear(); _weightSupport.Clear(); break; }
            _support[value] = _support.GetValueOrDefault(value) + count;
            if (options.Weight is not null) _weightSupport[value] = _weightSupport.GetValueOrDefault(value) + other._weightSupport.GetValueOrDefault(value);
        }
        for (var i = 0; i < _binCount.Length; i++) { _binCount[i] += other._binCount[i]; _binSum[i] += other._binSum[i]; _binSquares[i] += other._binSquares[i]; }
        for (var i = 0; i < _tailCount.Length; i++) { _tailCount[i] += other._tailCount[i]; _tailSum[i] += other._tailSum[i]; _tailSquares[i] += other._tailSquares[i]; }
        if (!_groupsComplete || !other._groupsComplete) { _groupsComplete = false; _groups.Clear(); }
        else
        {
            // Every cohort uses the same paid-parent population. A cohort first
            // seen late in a chunk, or absent from another worker's chunk, still
            // has zero numerator and denominator on those other paid rounds.
            foreach (var (key, target) in _groups)
                if (!other._groups.TryGetValue(key, out var source) || source._moments.Count == 0)
                    target.ZeroParents(other._clusterSums.Count);
            foreach (var (key, value) in other._groups)
            {
                if (value._moments.Count == 0) continue;
                if (!_groups.TryGetValue(key, out var target))
                {
                    if (_groups.Count >= options.GroupLimit) { _groupsComplete = false; _groups.Clear(); break; }
                    _groups.Add(key, target = GroupAccumulator()); target.ZeroParents(previousParents);
                }
                target.Merge(value);
            }
        }
        if (_sequence is not null && other._sequence is not null) _sequence.Merge(other._sequence);
    }

    private void ZeroParents(long count)
    {
        if (count == 0) return;
        var parents = _clusterSums.Count + count;
        _clusterCoMoment += _clusterSums.Mean * _clusterCounts.Mean * ((double)_clusterSums.Count * count / parents);
        var zero = new RunningMoments { Count = count };
        _clusterSums.Merge(zero, false); _clusterCounts.Merge(zero, false);
    }

    public MeasurementAnalysis Snapshot(long errors, bool ordered)
    {
        var n = _moments.Count; var mean = _moments.Mean; var variance = n > 1 ? _moments.M2 / (n - 1) : (double?)null;
        var alpha = _alpha; var z = _critical;
        var independent = options.IndependentSubjects && errors == 0 && _unclosed == 0 && _duplicateAwards == 0;
        var se = independent && variance is { } v ? M.Sqrt(M.Max(0, v) / n) : (double?)null;
        NumericInterval? meanCi = se is > 0 ? new(mean - z * se.Value, mean + z * se.Value, "Fixed-count normal approximation", "Independent subjects; finite variance; asymptotic approximation, not a tail-coverage proof.")
            : independent && n > 0 && options.LowerBound is { } constant && options.UpperBound == constant ? new(constant, constant, "Authored constant bound", "Every possible value equals the proven bound.") : null;
        NumericInterval? probabilityCi = independent && n > 0 && options.Weight is null ? StatisticalInference.ExactBinomial(_events, n, alpha) : null;
        NumericInterval? weightedCi = null;
        NumericInterval? weightedEventCi = null;
        if (independent && _weightedMoments.Count > 1 && _weightedMoments.M2 > 0)
        {
            var width = z * M.Sqrt(_weightedMoments.M2 / ((_weightedMoments.Count - 1d) * _weightedMoments.Count));
            weightedCi = new(_weightedMoments.Mean - width, _weightedMoments.Mean + width, "Ordinary importance-sampling mean, fixed count", "Independent proposal observations; valid target/proposal likelihood ratios, full target support coverage and finite weighted variance; asymptotic approximation.");
        }
        if (independent && _weightedEventMoments.Count > 1 && _weightedEventMoments.M2 > 0)
        {
            var width = z * M.Sqrt(_weightedEventMoments.M2 / ((_weightedEventMoments.Count - 1d) * _weightedEventMoments.Count));
            weightedEventCi = new(_weightedEventMoments.Mean - width, _weightedEventMoments.Mean + width, "Importance-weighted nonzero-event probability, fixed count", "Independent proposal subjects; valid likelihood weights and full target support; asymptotic normal approximation.");
        }
        NumericInterval? clusterCi = null;
        if (options.IndependentParents && errors == 0 && _unclosed == 0 && _duplicateAwards == 0 && _clusterSums.Count > 1 && _clusterCounts.Sum > 0)
        {
            var ratio = _clusterSums.Sum / _clusterCounts.Sum; var parents = _clusterSums.Count;
            var residualVariance = M.Max(0, (_clusterSums.M2 + ratio * ratio * _clusterCounts.M2 - 2 * ratio * _clusterCoMoment) / (parents - 1));
            var width = z * M.Sqrt(residualVariance / parents) / _clusterCounts.Mean;
            if (residualVariance > 0) clusterCi = new(ratio - width, ratio + width, "Paid-round cluster ratio, fixed count", "Independent paid rounds; all within-round observations clustered together; asymptotic delta-method approximation.");
        }
        var booleanValue = options.Source == "event" && options.Subject is "observation" or "session" || options.Reduction is "any" or "all" && options.Subject != "observation";
        var lowBound = options.LowerBound ?? (booleanValue ? 0 : (double?)null); var highBound = options.UpperBound ?? (booleanValue ? 1 : (double?)null);
        var sequential = independent && n > 0 && lowBound is { } lower && highBound is { } upper
            ? StatisticalInference.SequentialMean(mean, n, lower, upper, alpha) : null;
        var pair = _paired.Count == 0 ? null : Pair(n, variance, independent, z);
        var weightedRatio = _weightedPairMoments.Sum != 0 ? Finite(_weightedMoments.Sum / _weightedPairMoments.Sum) : null;
        NumericInterval? weightedRatioCi = null;
        if (independent && n > 1 && weightedRatio is { } weightedR && _weightedPairMoments.Count == n)
        {
            var residualVariance = M.Max(0, (_weightedMoments.M2 + weightedR * weightedR * _weightedPairMoments.M2 - 2 * weightedR * _weightedCoMoment) / (n - 1));
            if (residualVariance > 0)
            {
                var width = z * M.Sqrt(residualVariance / n) / M.Abs(_weightedPairMoments.Mean);
                if (double.IsFinite(width) && double.IsFinite(weightedR - width) && double.IsFinite(weightedR + width)) weightedRatioCi = new(weightedR - width, weightedR + width, "Importance-weighted paired ratio, fixed count", "Independent proposal subjects; valid likelihood ratios and complete target support; nonzero expected cost; asymptotic delta-method approximation.");
            }
        }
        var support = _support.Select(p => new ValueFrequency(p.Key, p.Value, p.Key * p.Value)).ToArray();
        var bins = Enumerable.Range(0, _binCount.Length).Select(i => new DistributionBin(i == 0 ? null : options.BinEdges[i - 1], i == options.BinEdges.Length ? null : options.BinEdges[i], _binCount[i], _binSum[i], _binSquares[i])).ToArray();
        var tails = options.Thresholds.Select((t, i) => new TailSummary(t, _tailCount[i], n == 0 ? 0 : (double)_tailCount[i] / n, _tailSum[i], _tailCount[i] == 0 ? null : _tailSum[i] / _tailCount[i], n == 0 ? 0 : _tailSquares[i] / n)).ToArray();
        double? mad = _supportComplete && n > 0 ? _support.Sum(p => M.Abs(p.Key - mean) * p.Value) / n : null;
        var moments = new MomentSummary(n == 0 ? null : _moments.M2 / n + mean * mean, n == 0 ? null : _moments.M2 / n, variance,
            variance is { } varX && mean > 0 ? M.Sqrt(M.Max(0, varX)) / mean : null,
            _moments.M2 > 0 ? Finite(M.Sqrt(n) * (_moments.M3 / _moments.M2) / M.Sqrt(_moments.M2)) : null,
            _moments.M2 > 0 ? Finite((_moments.M4 / _moments.M2) * (n / _moments.M2) - 3) : null, mad);
        var comparison = options.ReferenceDistribution.Length > 0 && _supportComplete && n > 0 && (options.Weight is null || _weightSum > 0) ? Compare(n, independent) : null;
        var checks = new List<VerificationCheck>();
        AssertionSummary? assertion = null;
        if (options.Assertion == "zero")
        {
            var status = errors > 0 || _unclosed > 0 || _duplicateAwards > 0 ? "invalid" : _assertionViolations > 0 ? "discrepancy" : n == 0 ? "insufficient" : "noObservedViolations";
            assertion = new("zero", n, _assertionViolations, status);
            checks.Add(new("exact-zero-assertion", status, _assertionViolations, 0, _assertionViolations,
                "Each valid scoped expression must be exactly zero or false before binary64 report conversion. Invalid observations cannot pass. No observed violations describes the checked population; it does not prove unobserved cases."));
        }
        if (errors > 0 || _unclosed > 0 || _duplicateAwards > 0) checks.Add(new("observation-integrity", "invalid", errors + _unclosed + _duplicateAwards, 0, null, "Expression errors, unmatched lifecycle boundaries or duplicate award IDs invalidate this measurement."));
        if (options.ReferenceMean is { } reference)
        {
            var ratioReference = options.ReferenceStatistic == "ratio" || options.PairRole == "wager";
            var probabilityReference = options.ReferenceStatistic == "probability" || options.Source == "event" && options.Subject is "observation" or "session" || options.Reduction is "any" or "all" && options.Subject != "observation";
            var interval = options.Weight is not null ? ratioReference ? weightedRatioCi : probabilityReference ? weightedEventCi : weightedCi : ratioReference ? pair?.RatioInterval : probabilityReference ? sequential ?? probabilityCi : sequential ?? meanCi ?? clusterCi; var tolerance = options.Tolerance ?? 0;
            var estimate = options.Weight is not null ? ratioReference ? weightedRatio : probabilityReference && options.ReferenceStatistic == "probability" ? n == 0 ? null : (double?)_weightedEvents / n : _weightedMoments.Mean : ratioReference ? pair?.Ratio : probabilityReference ? n == 0 ? null : (double?)_events / n : mean;
            var status = errors > 0 || _unclosed > 0 || _duplicateAwards > 0 ? "invalid" : n == 0 || interval is null ? "insufficient" : interval.Lower >= reference - tolerance && interval.Upper <= reference + tolerance ? "withinPrecision" : interval.Upper < reference - tolerance || interval.Lower > reference + tolerance ? "discrepancy" : "insufficient";
            checks.Add(new("mean-equivalence", status, n == 0 ? null : estimate, reference, n == 0 ? null : estimate - reference, "Acceptance requires the entire uncertainty interval inside the reference tolerance. A compatible wide interval is insufficient."));
        }
        if (comparison is { UnexpectedObservations: > 0 }) checks.Add(new("reference-support", "discrepancy", comparison.UnexpectedObservations, 0, null, "Observed outcomes lie outside the reference probability support."));
        if (comparison?.PValue is { } pValue) checks.Add(new("distribution-goodness-of-fit", pValue < alpha ? "discrepancy" : "notRejected", pValue, alpha, null,
            "Fixed-count Pearson test against a prespecified independent PMF; family-adjusted alpha. Failure to reject does not prove equality or precision. Repeated live looks do not provide a sequential decision rule."));
        return new(n, n == 0 ? null : _moments.Min, n == 0 ? null : _moments.Max, n == 0 ? null : mean, n == 0 ? null : _moments.Sum,
            options.Subject, options.Reduction, _distinctParents, _entries, _exits, _unclosed, _uniqueAwards, _duplicateAwards,
            moments, pair, support, _supportComplete, bins, options.Quantiles.Select(q => Quantile(q, n, bins)).ToArray(), tails,
            options.Quantiles.Select(q => UpperTail(q, n)).ToArray(), AbsoluteDeviation(n, mean), meanCi,
            probabilityCi, clusterCi, sequential, se, variance is > 0 && options.Tolerance is > 0 ? Finite(M.Ceiling(z * z * variance.Value / M.Pow(options.Tolerance.Value, 2))) : null,
            options.Weight is null ? null : new(_weightSum, _weightSquares, _weightSquares > 0 ? Finite((_weightSum / _weightSquares) * _weightSum) : null,
                n == 0 ? null : _weightedSum / n, _weightSum > 0 ? _weightedSum / _weightSum : null, double.IsFinite(_minWeight) ? _minWeight : 0, _maxWeight, weightedCi, n == 0 ? null : _weightedEvents / n, weightedEventCi, weightedRatio, weightedRatioCi),
            _sequence?.Snapshot(ordered), TransitionSnapshot(), _transitionsComplete, comparison, checks.ToArray(), _groups.Where(p => p.Value._moments.Count > 0).ToDictionary(p => p.Key, p => p.Value.Snapshot(errors, ordered), StringComparer.Ordinal))
        {
            GroupsComplete = _groupsComplete, Assertion = assertion,
            Normalization = _clusterSums.Count == 0 ? null : new(_clusterSums.Count,
                options.PairRole == "wager" ? _paired.Sum > 0 ? Finite(_paired.Sum) : null : Finite(_clusterSums.Count * options.Stake),
                options.PairRole == "wager" ? "Sum of included complete paid-round wager pairs; scope defines conditional mode turnover. Unweighted proposal observations when likelihood weights are present."
                    : "All settled paid parents multiplied by the pinned fixed external cost; absent cohort/component parents remain in the denominator. Values and cost must use compatible monetary units. Unweighted proposal observations when likelihood weights are present.")
        };
    }
    private TransitionFrequency[] TransitionSnapshot()
    {
        var exposure = _transitions.GroupBy(p => p.Key.From).ToDictionary(g => g.Key, g => g.Sum(p => p.Value));
        return _transitions.OrderBy(p => p.Key.From).ThenBy(p => p.Key.To).Select(p => new TransitionFrequency(p.Key.From, p.Key.To, p.Value, exposure[p.Key.From], (double)p.Value / exposure[p.Key.From])).ToArray();
    }
    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
    private MeasurementAnalysisAccumulator GroupAccumulator() => new(options with { Group = null, ReferenceMean = null, ReferenceDistribution = [] });
    private NumericInterval? AbsoluteDeviation(long n, double mean)
    {
        if (n == 0) return null;
        if (_supportComplete) { var exact = _support.Sum(p => M.Abs(p.Key - mean) * p.Value) / n; return new(exact, exact, "Exact empirical absolute deviation", "Descriptive population of retained observations."); }
        double lower = 0, upper = 0;
        for (var i = 0; i < _binCount.Length; i++) if (_binCount[i] > 0)
        {
            var l = i == 0 ? _moments.Min : M.Max(options.BinEdges[i - 1], _moments.Min);
            var h = i == options.BinEdges.Length ? _moments.Max : M.Min(options.BinEdges[i], _moments.Max);
            var count = _binCount[i]; var sum = _binSum[i];
            lower += M.Abs(sum - count * mean);
            upper += h == l ? count * M.Abs(l - mean) : ((count * h - sum) * M.Abs(l - mean) + (sum - count * l) * M.Abs(h - mean)) / (h - l);
        }
        return new(lower / n, M.Max(lower, upper) / n, "Convex fixed-bin empirical enclosure", "Uses bin sums and observed extrema; this is an empirical bound, not a population confidence interval.");
    }
    private UpperTailEstimate UpperTail(double q, long n)
    {
        if (n == 0) return new(q, 1 - q, null, null, null, null, null, "No observations");
        if (q == 1) return new(q, 0, _moments.Max, _moments.Max, _moments.Max, null, null, "Limit of the empirical upper-tail mean at q=1");
        var required = (1 - q) * n; var remaining = required; double lower = 0, upper = 0;
        if (_supportComplete)
        {
            foreach (var (value, count) in _support.Reverse()) { var take = M.Min(remaining, count); lower += take * value; remaining -= take; if (remaining <= 0) break; }
            upper = lower;
        }
        else for (var i = _binCount.Length - 1; i >= 0 && remaining > 0; i--)
        {
            var count = _binCount[i]; if (count == 0) continue; var take = M.Min(remaining, count); var sum = _binSum[i];
            var l = i == 0 ? _moments.Min : M.Max(options.BinEdges[i - 1], _moments.Min);
            var h = i == options.BinEdges.Length ? _moments.Max : M.Min(options.BinEdges[i], _moments.Max);
            lower += take == count ? sum : take * sum / count;
            upper += take == count ? sum : M.Min(take * h, sum - (count - take) * l);
            remaining -= take;
        }
        var shareA = _moments.Min < 0 || _moments.Sum <= 0 ? (double?)null : Finite(lower / _moments.Sum); var shareB = _moments.Min < 0 || _moments.Sum <= 0 ? (double?)null : Finite(upper / _moments.Sum);
        return new(q, 1 - q, _supportComplete ? lower / required : null, lower / required, M.Max(lower, upper) / required,
            shareA is { } a && shareB is { } b ? M.Min(a, b) : null, shareA is { } c && shareB is { } d ? M.Max(c, d) : null,
            _supportComplete ? "Exact empirical highest-tail mass; fractional tie allocation" : "Fixed-bin upper-tail enclosure; observed extrema and within-bin sums");
    }

    private PairSummary Pair(long n, double? variance, bool independent, double z)
    {
        var covariance = n > 1 ? _coMoment / (n - 1) : (double?)null;
        var correlation = _moments.M2 > 0 && _paired.M2 > 0 ? M.Clamp(_coMoment / M.Sqrt(_moments.M2) / M.Sqrt(_paired.M2), -1, 1) : (double?)null;
        var ratio = _paired.Sum != 0 ? Finite(_moments.Sum / _paired.Sum) : null;
        NumericInterval? interval = null;
        if (independent && n > 1 && ratio is { } r && variance is { } v && covariance is { } cov && _paired.Mean != 0)
        {
            var varResidual = M.Max(0, v + r * r * _paired.M2 / (n - 1) - 2 * r * cov);
            var width = z * M.Sqrt(varResidual / n) / M.Abs(_paired.Mean);
            if (varResidual > 0 && double.IsFinite(width) && double.IsFinite(r - width) && double.IsFinite(r + width)) interval = new(r - width, r + width, "Paired delta-method ratio, fixed count", "Independent paired subjects; nonzero, well-estimated mean denominator; asymptotic approximation.");
        }
        var varY = n > 1 ? _paired.M2 / (n - 1) : (double?)null;
        var varSum = variance is { } vx && varY is { } vy && covariance is { } cv ? Finite(M.Max(0, vx + vy + 2 * cv)) : null;
        var varDifference = variance is { } dx && varY is { } dy && covariance is { } dc ? Finite(M.Max(0, dx + dy - 2 * dc)) : null;
        var difference = n == 0 ? (double?)null : _moments.Mean - _paired.Mean;
        NumericInterval? differenceCi = null;
        if (independent && varDifference is > 0 && difference is { } d)
        { var half = z * M.Sqrt(varDifference.Value / n); if (double.IsFinite(d - half) && double.IsFinite(d + half)) differenceCi = new(d - half, d + half, "Paired difference, fixed count", "Independent paired subjects; asymptotic normal approximation including component covariance."); }
        return new(_paired.Count, _paired.Sum, n == 0 ? null : _paired.Mean, covariance, correlation, ratio, interval, difference)
        { SampleVarianceY = varY, SampleVarianceSum = varSum, SampleVarianceDifference = varDifference, DifferenceInterval = differenceCi, Joint = _joint.Snapshot(n, independent && options.Weight is null) };
    }
    private QuantileEstimate Quantile(double q, long n, DistributionBin[] bins)
    {
        if (n == 0) return new(q, null, null, null, "No observations");
        var rank = M.Max(1, (long)M.Ceiling(q * n)); long cumulative = 0;
        if (_supportComplete) foreach (var (value, count) in _support) { cumulative += count; if (cumulative >= rank) return new(q, value, value, value, "Exact empirical inverse CDF"); }
        cumulative = 0;
        foreach (var bin in bins) { cumulative += bin.Count; if (cumulative >= rank) return new(q, null, bin.Lower, bin.Upper, "Fixed-bin rank enclosure; upper endpoint exclusive"); }
        throw new InvalidOperationException("Distribution count invariant failed.");
    }
    private DistributionComparison Compare(long n, bool independent)
    {
        var reference = options.ReferenceDistribution.ToDictionary(p => p.Value, p => p.Probability);
        var keys = _support.Keys.Union(reference.Keys).Order().ToArray(); double tv = 0, cdf = 0, distance = 0, chi = 0; long unexpected = 0; var adequate = true;
        foreach (var key in keys)
        {
            var count = _support.GetValueOrDefault(key); var probability = reference.GetValueOrDefault(key); var expected = n * probability;
            var projected = options.Weight is null ? (double)count / n : _weightSupport.GetValueOrDefault(key) / _weightSum;
            var delta = projected - probability; tv += M.Abs(delta); cdf += delta; distance = M.Max(distance, M.Abs(cdf));
            if (probability == 0) { if (projected > 0) unexpected += count; } else { chi += M.Pow(count - expected, 2) / expected; if (expected < 5) adequate = false; }
        }
        var df = reference.Count(p => p.Value > 0) - 1;
        var calibrated = independent && double.IsFinite(chi) && options.Weight is null && adequate && unexpected == 0 && df > 0;
        return new(tv / 2, distance, unexpected > 0 || options.Weight is not null ? null : Finite(chi), df, adequate && options.Weight is null, unexpected,
            calibrated ? StatisticalInference.ChiSquareSurvival(chi, df) : null,
            calibrated ? "Asymptotic Pearson multinomial, prespecified PMF, all expected counts ≥ 5, independent subjects; fixed-count diagnostic only"
                : "Uncalibrated: sparse expected counts, dependent/weighted observations, unsupported outcomes or no positive degrees of freedom");
    }
}

/// <summary>Pairwise central moments (Pébay, SAND2008-6212), with explicit center shifts.
/// Population skewness and excess kurtosis are moment estimators, not bias-adjusted estimates.</summary>
internal struct RunningMoments
{
    public long Count;
    public double Mean, Sum, M2, M3, M4, Min, Max;
    public void Add(double x, bool highOrder = true) => Merge(new() { Count = 1, Mean = x, Sum = x, Min = x, Max = x }, highOrder);
    public void Merge(RunningMoments b, bool highOrder = true)
    {
        if (b.Count == 0) return; if (Count == 0) { this = b; return; }
        var n = Count + b.Count; var delta = b.Mean - Mean; var aShift = -delta * b.Count / n; var bShift = delta * Count / n;
        var a2 = aShift * aShift; var b2 = bShift * bShift;
        var m4 = !highOrder ? 0 : M4 + 4 * aShift * M3 + 6 * a2 * M2 + Count * a2 * a2
            + b.M4 + 4 * bShift * b.M3 + 6 * b2 * b.M2 + b.Count * b2 * b2;
        var m3 = !highOrder ? 0 : M3 + 3 * aShift * M2 + Count * aShift * aShift * aShift + b.M3 + 3 * bShift * b.M2 + b.Count * bShift * bShift * bShift;
        M2 += b.M2 + delta * delta * ((double)Count * b.Count / n); M3 = m3; M4 = m4;
        Mean += delta * b.Count / n; Sum += b.Sum; Min = M.Min(Min, b.Min); Max = M.Max(Max, b.Max); Count = n;
        if (!double.IsFinite(M2) || !double.IsFinite(M3) || !double.IsFinite(M4) || !double.IsFinite(Sum)) throw new ArithmeticException("Moment aggregate exceeds finite range.");
    }
}
