namespace SlotMath.Core.Math;

public sealed record LoopTerminationSummary(
    string NodeId,
    long CompletedInvocations,
    long ModelLimitCompletions,
    long ConditionCompletions,
    long TotalIterations,
    int MinimumIterations,
    int MaximumIterations)
{
    public IReadOnlyDictionary<string, long>? ExitReasons { get; init; }
}

/// <summary>
///     Worker-private, bounded transactional loop evidence. A completed loop
///     in a subsequently interrupted round contributes nothing to settled evidence.
/// </summary>
internal sealed class LoopTerminationEvidence
{
    private const int MaximumLoops = 256;
    private readonly Bank _delta = new();
    private readonly Bank _round = new();
    private readonly Bank _total = new();
    private bool _roundComplete = true;
    public bool Complete { get; private set; } = true;

    public void Begin()
    {
        _round.Clear();
        _roundComplete = true;
    }

    public void Observe(string nodeId, int iterations, int cap, string? reason = null)
        => Observe(new Key(nodeId), iterations, cap, reason);

    internal void Observe(Key nodeId, int iterations, int cap, string? reason = null)
    {
        if (iterations < 0 || cap <= 0)
        {
            throw new InvalidOperationException("Invalid compiled loop counter.");
        }

        Counter? counter = _round.Get(nodeId);
        if (counter is null)
        {
            _roundComplete = false;
            return;
        }

        var atLimit = iterations >= cap;
        counter.Observe(iterations, atLimit, reason ?? (atLimit ? "modelLimit" : "condition"));
    }

    public void Commit()
    {
        Complete &= _roundComplete;
        Merge(_round, _total);
        Merge(_round, _delta);
    }

    public void ClearDelta() => _delta.Clear();

    public void Merge(LoopTerminationEvidence other)
    {
        Complete &= other.Complete;
        Merge(other._total, _total);
    }

    public void MergeDelta(LoopTerminationEvidence other)
    {
        Complete &= other.Complete;
        Merge(other._delta, _total);
    }

    public LoopTerminationSummary[] Snapshot() => _total.Counters.OrderBy(v => v.Key.NodeId, StringComparer.Ordinal)
        .Select(v => new LoopTerminationSummary(v.Key.NodeId, v.Value.CompletedInvocations,
            v.Value.ModelLimitCompletions,
            v.Value.ConditionCompletions, v.Value.TotalIterations, v.Value.MinimumIterations, v.Value.MaximumIterations)
        {
            ExitReasons = v.Value.SnapshotReasons()
        }).ToArray();

    private void Merge(Bank source, Bank target)
    {
        foreach ((Key nodeId, Counter value) in source.Counters)
        {
            Counter? counter = target.Get(nodeId);
            if (counter is null)
            {
                Complete = false;
                continue;
            }

            counter.Add(value);
        }
    }

    // Compiled loops bind this once. Round and delta banks can then reuse the
    // ordinal hash while preserving equality across independently bound plans.
    internal readonly struct Key : IEquatable<Key>
    {
        public readonly string NodeId;
        private readonly int _hash;

        public Key(string nodeId)
        {
            NodeId = nodeId;
            _hash = StringComparer.Ordinal.GetHashCode(nodeId);
        }

        public bool Equals(Key other) => string.Equals(NodeId, other.NodeId, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is Key other && Equals(other);
        public override int GetHashCode() => _hash;
    }

    // Reuse worker-private counters, but never share mutable objects between
    // round, settled and delta banks. Clearing a bank also releases its keys.
    private sealed class Bank
    {
        private readonly List<Counter> _pool = [];
        public readonly Dictionary<Key, Counter> Counters = new();
        public void Clear() => Counters.Clear();

        public Counter? Get(Key nodeId)
        {
            if (Counters.TryGetValue(nodeId, out Counter? counter))
            {
                return counter;
            }

            var index = Counters.Count;
            if (index >= MaximumLoops)
            {
                return null;
            }

            if (index == _pool.Count)
            {
                _pool.Add(new Counter());
            }

            counter = _pool[index];
            counter.Reset();
            Counters.Add(nodeId, counter);
            return counter;
        }
    }

    private sealed class Counter
    {
        private readonly Dictionary<string, long> _reasons = new(StringComparer.Ordinal);
        private long _conditionReasons, _modelLimitReasons;
        public long CompletedInvocations, ModelLimitCompletions, ConditionCompletions, TotalIterations;
        public int MinimumIterations, MaximumIterations;

        public Dictionary<string, long> SnapshotReasons()
        {
            var copy = new Dictionary<string, long>(_reasons, StringComparer.Ordinal);
            if (_conditionReasons != 0)
            {
                copy.Add("condition", _conditionReasons);
            }

            if (_modelLimitReasons != 0)
            {
                copy.Add("modelLimit", _modelLimitReasons);
            }

            return copy;
        }

        public void Reset()
        {
            CompletedInvocations = ModelLimitCompletions = ConditionCompletions = TotalIterations = 0;
            MinimumIterations = int.MaxValue;
            MaximumIterations = 0;
            _conditionReasons = _modelLimitReasons = 0;
            _reasons.Clear();
        }

        public void Observe(int iterations, bool atLimit, string reason)
        {
            var invocations = checked(CompletedInvocations + 1);
            var limits = checked(ModelLimitCompletions + (atLimit ? 1 : 0));
            var conditions = checked(ConditionCompletions + (atLimit ? 0 : 1));
            var total = checked(TotalIterations + iterations);
            var reasonCount = reason == "condition" ? _conditionReasons :
                reason == "modelLimit" ? _modelLimitReasons : _reasons.GetValueOrDefault(reason);
            var reasons = checked(reasonCount + 1);
            CompletedInvocations = invocations;
            ModelLimitCompletions = limits;
            ConditionCompletions = conditions;
            TotalIterations = total;
            MinimumIterations = System.Math.Min(MinimumIterations, iterations);
            MaximumIterations = System.Math.Max(MaximumIterations, iterations);
            if (reason == "condition")
            {
                _conditionReasons = reasons;
            }
            else if (reason == "modelLimit")
            {
                _modelLimitReasons = reasons;
            }
            else
            {
                _reasons[reason] = reasons;
            }
        }

        public void Add(Counter value)
        {
            var invocations = checked(CompletedInvocations + value.CompletedInvocations);
            var limits = checked(ModelLimitCompletions + value.ModelLimitCompletions);
            var conditions = checked(ConditionCompletions + value.ConditionCompletions);
            var total = checked(TotalIterations + value.TotalIterations);
            var conditionReasons = checked(_conditionReasons + value._conditionReasons);
            var modelLimitReasons = checked(_modelLimitReasons + value._modelLimitReasons);
            // Validate all additions before mutating a settled counter.
            foreach (var (reason, count) in value._reasons)
            {
                _ = checked(_reasons.GetValueOrDefault(reason) + count);
            }

            foreach (var (reason, count) in value._reasons)
            {
                _reasons[reason] = checked(_reasons.GetValueOrDefault(reason) + count);
            }

            CompletedInvocations = invocations;
            ModelLimitCompletions = limits;
            ConditionCompletions = conditions;
            TotalIterations = total;
            _conditionReasons = conditionReasons;
            _modelLimitReasons = modelLimitReasons;
            MinimumIterations = System.Math.Min(MinimumIterations, value.MinimumIterations);
            MaximumIterations = System.Math.Max(MaximumIterations, value.MaximumIterations);
        }
    }
}
