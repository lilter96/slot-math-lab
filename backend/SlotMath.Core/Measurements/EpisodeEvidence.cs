namespace SlotMath.Core.Measurements;

public sealed record OrdinalMoment(int Depth, int Ordinal, long Count, double Minimum, double Maximum, double Mean, double? SampleVariance);
public sealed record CrossOrdinalMoment(int Depth, int First, int Second, long PairedEpisodes, double? Covariance, double? Correlation);
public sealed record EpisodeProfile(long IncludedEpisodes, long OverflowEpisodes, OrdinalMoment[] Ordinals, CrossOrdinalMoment[] CrossOrdinals, IReadOnlyDictionary<string, long> ExitReasons, string Population);
internal sealed class EpisodeEvidence(int limit)
{
    private readonly Dictionary<(int, int), RunningMoments> _ordinals = new();
    private readonly Dictionary<(int, int, int), Cross> _cross = new();
    private readonly Dictionary<string, long> _reasons = new(StringComparer.Ordinal);
    private long _episodes, _overflow;
    public void Reset() { _ordinals.Clear(); _cross.Clear(); _reasons.Clear(); _episodes = _overflow = 0; }
    public void Add(int depth, IReadOnlyList<double> values, long totalValues, string reason)
    {
        _episodes++; if (totalValues > limit && limit > 0) _overflow++;
        _reasons[reason] = _reasons.GetValueOrDefault(reason) + 1;
        for (var i = 0; i < values.Count; i++)
        {
            var key = (depth, i + 1); _ordinals.TryGetValue(key, out var m); m.Add(values[i], false); _ordinals[key] = m;
            for (var j = i + 1; j < values.Count; j++)
            { var pair = (depth, i + 1, j + 1); if (!_cross.TryGetValue(pair, out var c)) _cross[pair] = c = new(); c.Add(values[i], values[j]); }
        }
    }
    public void Merge(EpisodeEvidence other)
    {
        _episodes += other._episodes; _overflow += other._overflow;
        foreach (var (key, value) in other._reasons) _reasons[key] = _reasons.GetValueOrDefault(key) + value;
        foreach (var (key, value) in other._ordinals) { _ordinals.TryGetValue(key, out var m); m.Merge(value, false); _ordinals[key] = m; }
        foreach (var (key, value) in other._cross) { if (!_cross.TryGetValue(key, out var c)) _cross[key] = c = new(); c.Merge(value); }
    }
    public EpisodeProfile Snapshot() => new(_episodes, _overflow,
        _ordinals.OrderBy(p => p.Key).Select(p => new OrdinalMoment(p.Key.Item1, p.Key.Item2, p.Value.Count, p.Value.Min, p.Value.Max, p.Value.Mean, p.Value.Count > 1 ? p.Value.M2 / (p.Value.Count - 1) : null)).ToArray(),
        _cross.OrderBy(p => p.Key).Select(p => new CrossOrdinalMoment(p.Key.Item1, p.Key.Item2, p.Key.Item3, p.Value.X.Count,
            p.Value.X.Count > 1 ? p.Value.Co / (p.Value.X.Count - 1) : null,
            p.Value.X.M2 > 0 && p.Value.Y.M2 > 0 ? p.Value.Co / System.Math.Sqrt(p.Value.X.M2 * p.Value.Y.M2) : null)).ToArray(), new Dictionary<string, long>(_reasons),
        "Valid explicitly closed episodes only; observations belong to the innermost episode. Ordinals count included observations. Covariance uses episodes containing both ordinals and is conditional on reaching both positions. Later ordinals remain outside the configured profile.");
    private sealed class Cross
    {
        public RunningMoments X, Y; public double Co;
        public void Add(double x, double y) { var dx = x - X.Mean; X.Add(x, false); Y.Add(y, false); Co += dx * (y - Y.Mean); }
        public void Merge(Cross b) { var n = X.Count + b.X.Count; Co += b.Co + (n == 0 ? 0 : (b.X.Mean - X.Mean) * (b.Y.Mean - Y.Mean) * ((double)X.Count * b.X.Count / n)); X.Merge(b.X, false); Y.Merge(b.Y, false); }
    }
}
