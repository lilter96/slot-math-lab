namespace SlotMath.Core.Math;

public sealed record LoopTerminationSummary(string NodeId, long CompletedInvocations, long ModelLimitCompletions,
    long ConditionCompletions, long TotalIterations, int MinimumIterations, int MaximumIterations)
{ public IReadOnlyDictionary<string, long>? ExitReasons { get; init; } }

/// <summary>Worker-private, bounded transactional loop evidence. A completed loop
/// in a subsequently interrupted round contributes nothing to settled evidence.</summary>
internal sealed class LoopTerminationEvidence
{
    private const int MaximumLoops = 256;
    private readonly Dictionary<string, Counter> _round = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Counter> _total = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Counter> _delta = new(StringComparer.Ordinal);
    private bool _roundComplete = true;
    public bool Complete { get; private set; } = true;
    public void Begin() { _round.Clear(); _roundComplete = true; }
    public void Observe(string nodeId, int iterations, int cap, string? reason = null)
    {
        if (iterations < 0 || cap <= 0) throw new InvalidOperationException("Invalid compiled loop counter.");
        if (!_round.ContainsKey(nodeId) && _round.Count >= MaximumLoops) { _roundComplete = false; return; }
        var atLimit = iterations >= cap;
        Add(_round, nodeId, new(1, atLimit ? 1 : 0, atLimit ? 0 : 1, iterations, iterations, iterations) { Reasons = new Dictionary<string, long> { [reason ?? (atLimit ? "modelLimit" : "condition")] = 1 } });
    }
    public void Commit()
    {
        Complete &= _roundComplete;
        Merge(_round, _total); Merge(_round, _delta);
    }
    public void ClearDelta() => _delta.Clear();
    public void Merge(LoopTerminationEvidence other)
    { Complete &= other.Complete; Merge(other._total, _total); }
    public void MergeDelta(LoopTerminationEvidence other)
    { Complete &= other.Complete; Merge(other._delta, _total); }
    public LoopTerminationSummary[] Snapshot() => _total.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => new LoopTerminationSummary(v.Key, v.Value.CompletedInvocations, v.Value.ModelLimitCompletions, v.Value.ConditionCompletions, v.Value.TotalIterations, v.Value.MinimumIterations, v.Value.MaximumIterations) { ExitReasons = v.Value.Reasons }).ToArray();
    private void Merge(IReadOnlyDictionary<string, Counter> source, Dictionary<string, Counter> target)
    {
        foreach (var (nodeId, value) in source)
        {
            if (!target.ContainsKey(nodeId) && target.Count >= MaximumLoops) { Complete = false; continue; }
            Add(target, nodeId, value);
        }
    }
    private readonly record struct Counter(long CompletedInvocations, long ModelLimitCompletions, long ConditionCompletions, long TotalIterations, int MinimumIterations, int MaximumIterations) { public IReadOnlyDictionary<string, long>? Reasons { get; init; } }
    private static void Add(Dictionary<string, Counter> target, string nodeId, Counter value)
    {
        if (!target.TryGetValue(nodeId, out var previous)) { target.Add(nodeId, value); return; }
        var reasons = new Dictionary<string, long>(previous.Reasons ?? new Dictionary<string, long>());
        foreach (var (key, count) in value.Reasons ?? new Dictionary<string, long>()) reasons[key] = checked(reasons.GetValueOrDefault(key) + count);
        target[nodeId] = previous with { Reasons = reasons,
            CompletedInvocations = checked(previous.CompletedInvocations + value.CompletedInvocations),
            ModelLimitCompletions = checked(previous.ModelLimitCompletions + value.ModelLimitCompletions),
            ConditionCompletions = checked(previous.ConditionCompletions + value.ConditionCompletions),
            TotalIterations = checked(previous.TotalIterations + value.TotalIterations),
            MinimumIterations = System.Math.Min(previous.MinimumIterations, value.MinimumIterations),
            MaximumIterations = System.Math.Max(previous.MaximumIterations, value.MaximumIterations)
        };
    }
}
