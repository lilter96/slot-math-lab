using M = System.Math;
namespace SlotMath.Core.Measurements;

/// <summary>Ordered concatenation monoid. Only prefix/suffix up to the maximum requested lag
/// are retained. Cross-chunk gaps, streaks and lag products are restored during the merge.</summary>
internal sealed class SequenceAccumulator(int[] lags, int stateLimit = 256)
{
    private readonly int _boundary = lags.Max();
    private readonly List<double> _prefix = [], _suffix = [];
    private readonly double[] _product = new double[lags.Length], _left = new double[lags.Length], _right = new double[lags.Length];
    private readonly long[] _pairs = new long[lags.Length];
    private RunningMoments _moments;
    private long _events, _leadingDrought, _trailingDrought, _leadingHits, _trailingHits, _longestHits, _longestDrought, _gaps;
    private double _gapSum;
    private double _firstState, _lastState;
    private long _firstLength, _lastLength, _equalPairs;
    private bool _singleRun = true, _dwellComplete = true;
    private readonly Dictionary<double, (long Runs, long Observations, long Maximum)> _dwell = new();
    public void Reset()
    {
        _prefix.Clear(); _suffix.Clear(); Array.Clear(_product); Array.Clear(_left); Array.Clear(_right); Array.Clear(_pairs);
        _moments = default; _events = _leadingDrought = _trailingDrought = _leadingHits = _trailingHits = _longestHits = _longestDrought = _gaps = 0; _gapSum = 0;
        _firstState = _lastState = 0; _firstLength = _lastLength = _equalPairs = 0; _singleRun = _dwellComplete = true; _dwell.Clear();
    }
    public void Add(double value)
    {
        if (_moments.Count == 0) { _firstState = _lastState = value; _firstLength = _lastLength = 1; }
        else if (value == _lastState) { _equalPairs++; _lastLength++; if (_singleRun) _firstLength++; }
        else { if (!_singleRun) Dwell(_lastState, _lastLength); _singleRun = false; _lastState = value; _lastLength = 1; }
        for (var i = 0; i < lags.Length; i++) if (_moments.Count >= lags[i])
        { var previous = _suffix[^lags[i]]; _product[i] += previous * value; _left[i] += previous; _right[i] += value; _pairs[i]++; }
        if (_prefix.Count < _boundary) _prefix.Add(value);
        if (_suffix.Count == _boundary) _suffix.RemoveAt(0); _suffix.Add(value);
        if (value == 0)
        {
            if (_events == 0) _leadingDrought++;
            _trailingDrought++; _trailingHits = 0; _longestDrought = M.Max(_longestDrought, _trailingDrought);
        }
        else
        {
            if (_moments.Count == _leadingHits) _leadingHits++;
            if (_events > 0) { _gaps++; _gapSum += _trailingDrought; }
            _events++; _trailingDrought = 0; _trailingHits++; _longestHits = M.Max(_longestHits, _trailingHits);
        }
        _moments.Add(value);
    }
    public void Merge(SequenceAccumulator b)
    {
        var aCount = _moments.Count; var bCount = b._moments.Count; if (bCount == 0) return;
        if (!_dwellComplete || !b._dwellComplete) { _dwellComplete = false; _dwell.Clear(); }
        else foreach (var (state, run) in b._dwell) Dwell(state, run.Observations, run.Runs, run.Maximum);
        if (aCount == 0) { _firstState = b._firstState; _lastState = b._lastState; _firstLength = b._firstLength; _lastLength = b._lastLength; _singleRun = b._singleRun; _equalPairs = b._equalPairs; }
        else
        {
            var same = _lastState == b._firstState;
            if (same) { if (!_singleRun && !b._singleRun) Dwell(_lastState, _lastLength + b._firstLength); }
            else { if (!_singleRun) Dwell(_lastState, _lastLength); if (!b._singleRun) Dwell(b._firstState, b._firstLength); }
            if (_singleRun && same) _firstLength += b._firstLength;
            _lastLength = b._singleRun && same ? _lastLength + b._lastLength : b._lastLength;
            _lastState = b._lastState; _singleRun = _singleRun && b._singleRun && same; _equalPairs += b._equalPairs + (same ? 1 : 0);
        }
        for (var i = 0; i < lags.Length; i++)
        {
            for (var j = 0; j < M.Min(b._prefix.Count, lags[i]); j++)
            {
                var index = aCount + j - lags[i]; if (index < 0 || index >= aCount) continue;
                var x = _suffix[(int)(index - aCount + _suffix.Count)]; var y = b._prefix[j];
                _product[i] += x * y; _left[i] += x; _right[i] += y; _pairs[i]++;
            }
            _product[i] += b._product[i]; _left[i] += b._left[i]; _right[i] += b._right[i]; _pairs[i] += b._pairs[i];
        }
        _longestHits = M.Max(M.Max(_longestHits, b._longestHits), _trailingHits + b._leadingHits);
        _longestDrought = M.Max(M.Max(_longestDrought, b._longestDrought), _trailingDrought + b._leadingDrought);
        _gapSum += b._gapSum; _gaps += b._gaps;
        if (_events > 0 && b._events > 0) { _gapSum += _trailingDrought + b._leadingDrought; _gaps++; }
        if (_events == 0) _leadingDrought += b._leadingDrought;
        if (_leadingHits == aCount) _leadingHits += b._leadingHits;
        _trailingDrought = b._events == 0 ? _trailingDrought + bCount : b._trailingDrought;
        _trailingHits = b._trailingHits == bCount ? _trailingHits + bCount : b._trailingHits;
        _events += b._events; _moments.Merge(b._moments);
        foreach (var value in b._prefix) if (_prefix.Count < _boundary) _prefix.Add(value);
        var tail = _suffix.Concat(b._suffix).TakeLast(_boundary).ToArray(); _suffix.Clear(); _suffix.AddRange(tail);
    }
    public SequenceSummary Snapshot(bool ordered) => new(_moments.Count, _events, _longestHits, _longestDrought, _gaps,
        _gaps == 0 ? null : _gapSum / _gaps,
        lags.Select((lag, i) => (lag, value: ordered && _moments.M2 > 0 && _pairs[i] > 0
            ? (_product[i] - _moments.Mean * (_left[i] + _right[i]) + _pairs[i] * _moments.Mean * _moments.Mean) / _moments.M2 : (double?)null))
            .ToDictionary(p => p.lag, p => p.value), ordered, _equalPairs, M.Max(0, _moments.Count - 1),
        _dwell.OrderBy(p => p.Key).Select(p => new StateDwell(p.Key, p.Value.Runs, p.Value.Observations, p.Value.Maximum, (double)p.Value.Observations / p.Value.Runs)).ToArray(), _dwellComplete);
    private void Dwell(double state, long observations, long runs = 1, long? maximum = null)
    {
        if (!_dwellComplete) return;
        if (!_dwell.TryGetValue(state, out var value) && _dwell.Count >= stateLimit) { _dwellComplete = false; _dwell.Clear(); return; }
        _dwell[state] = (value.Runs + runs, value.Observations + observations, M.Max(value.Maximum, maximum ?? observations));
    }
}
