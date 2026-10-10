using System.Globalization;
using SlotMath.Core.Expressions;
namespace SlotMath.Core.Measurements;

/// <summary>Transactional observation collector. An interrupted round publishes only separate lifecycle exposure.
/// Subjects are explicit; absent round components are zero-filled, episodes close only at
/// their declared exit. No game state or game RNG is mutated by instrumentation.</summary>
internal sealed class MeasurementCollector(IReadOnlyList<MeasurementDefinition> definitions)
{
    private readonly MeasurementAccumulator[] _round = new MeasurementAccumulator[definitions.Count];
    private readonly MeasurementAnalysisAccumulator?[] _staging = definitions.Select(d => d.Options is { } options ? new MeasurementAnalysisAccumulator(options) : null).ToArray();
    private readonly SubjectBuffer?[] _subjects = new SubjectBuffer?[definitions.Count];
    private readonly Stack<SubjectBuffer>?[] _episodes = new Stack<SubjectBuffer>?[definitions.Count];
    private readonly HashSet<string>?[] _awardIds = new HashSet<string>?[definitions.Count];
    private readonly long[] _entries = new long[definitions.Count], _exits = new long[definitions.Count], _duplicates = new long[definitions.Count];
    private readonly bool[] _matchingChild = new bool[definitions.Count];
    private readonly bool[] _lifecycleComplete = new bool[definitions.Count];
    public MeasurementAccumulator[] Total { get; } = new MeasurementAccumulator[definitions.Count];
    public MeasurementAccumulator[] Delta { get; } = new MeasurementAccumulator[definitions.Count];
    public IReadOnlyList<MeasurementDefinition> Definitions => definitions;
    public double? CompletedRoundValue(int index) => _round[index].Count == 1 && _round[index].Errors == 0 && _round[index].Excluded == 0 ? _round[index].Sum : null;
    private bool _prepared;
    private bool _finished;
    private long _roundIndex;
    private bool _captureAll;
    private readonly WitnessAccumulator[] _witnessStaging = definitions.Select(_ => new WitnessAccumulator()).ToArray();
    private readonly SubjectBuffer?[] _witnessSubjects = new SubjectBuffer?[definitions.Count];
    private readonly long[] _ordinals = new long[definitions.Count];
    private readonly string?[] _points = new string?[definitions.Count];
    public MeasurementSnapshot[] RoundSnapshot() => Snapshot(definitions, _round);
    public void Begin(long roundIndex = 0, bool captureAll = false)
    {
        _prepared = _finished = false; Array.Fill(_lifecycleComplete, true); _roundIndex = roundIndex; _captureAll = captureAll; Array.Clear(_ordinals); Array.Clear(_points); Array.Clear(_witnessSubjects);
        Array.Clear(_round); Array.Clear(_subjects); Array.Clear(_entries); Array.Clear(_exits); Array.Clear(_duplicates); Array.Clear(_matchingChild);
        for (var i = 0; i < _witnessStaging.Length; i++) { _witnessStaging[i].Clear(); _round[i].Witnesses = _witnessStaging[i]; }
        for (var i = 0; i < _staging.Length; i++) if (_staging[i] is { } accumulator) { accumulator.Reset(); _round[i].Analysis = accumulator; }
        foreach (var episodes in _episodes) episodes?.Clear(); foreach (var ids in _awardIds) ids?.Clear();
    }
    public void Point<T>(int index, string nodeId, T state, MeasurementBinding<T> binding)
    {
        _points[index] = nodeId; _ordinals[index]++;
        var definition = definitions[index]; var options = definition.Options;
        if (options?.Subject == "transition") { Transition(index, nodeId, state, binding, options); return; }
        if (options?.Subject == "episode")
        {
            var stack = _episodes[index] ??= new();
            if (nodeId == options.ExitNodeId)
            {
                if (stack.Count == 0) { _lifecycleComplete[index] = false; _round[index].Observations++; Error(index, "Episode exit has no matching entry."); }
                else
                {
                    var episode = stack.Pop(); _witnessSubjects[index] = episode;
                    try { if (!episode.Excluded && binding.ExitFilter is { } exit) episode.Excluded = !Predicate(exit(state)); }
                    catch (Exception ex) when (IsMeasurementError(ex)) { episode.Invalid = true; episode.ErrorMessage ??= ex.Message; }
                    try { episode.ExitReason = binding.ExitReason is { } reason ? ExitClassification.Read(reason(state)) : "unclassified"; }
                    catch (Exception ex) when (IsMeasurementError(ex)) { episode.Invalid = true; episode.ErrorMessage ??= ex.Message; }
                    Emit(index, episode, options); _witnessSubjects[index] = null; _exits[index]++;
                    _staging[index]!.Lifecycle(episode.Group, exits: 1);
                }
            }
            if (nodeId == options.EntryNodeId)
            {
                _entries[index]++;
                if (stack.Count >= 16) { _lifecycleComplete[index] = false; _round[index].Observations++; Error(index, "Episode nesting exceeds the 16-level limit."); return; }
                var parent = stack.Count == 0 ? null : stack.Peek();
                var episode = new SubjectBuffer { Id = $"{_roundIndex}:{_entries[index]}", ParentId = parent?.Id, Depth = stack.Count + 1, EpisodeOrdinal = parent is null ? _entries[index] : ++parent.Children }; stack.Push(episode);
                try { episode.Excluded = binding.EntryFilter is { } entry && !Predicate(entry(state)); if (!episode.Excluded) episode.Group = Key(binding.Group, state); }
                catch (Exception ex) when (IsMeasurementError(ex)) { episode.Invalid = true; episode.ErrorMessage = ex.Message; }
                _staging[index]!.Lifecycle(episode.Group, entries: 1);
            }
        }
        if (definition.NodeId == nodeId) Observe(index, state, binding);
    }
    public void Observe<T>(int index, T state, Func<T, ExprValue>? value, Func<T, ExprValue>? filter, double payout = 0)
        => Observe(index, state, new(value, filter), payout);
    public void Observe<T>(int index, T state, MeasurementBinding<T> binding, double payout = 0, double? rawPayout = null)
    {
        var options = definitions[index].Options;
        var aggregate = options?.Subject is "round" or "episode";
        SubjectBuffer? subject = null;
        if (options?.Subject == "round") subject = _subjects[index] ??= new();
        if (options?.Subject == "episode")
        {
            if (_episodes[index] is not { Count: > 0 } stack) { _round[index].Observations++; Error(index, "Observation is outside its declared episode."); return; }
            subject = stack.Peek();
            if (subject.Excluded) return;
        }
        if (!aggregate) _round[index].Observations++;
        try
        {
            if (binding.Filter is { } filter)
            {
                var predicate = filter(state); if (predicate.Kind != ExprType.Boolean) throw new InvalidOperationException("Measurement filter must return Boolean.");
                if (!predicate.BoolValue) { if (!aggregate) _round[index].Excluded++; return; }
            }
            ExprValue? expressionValue = null;
            var number = options?.Source switch
            {
                "count" => 1d,
                "rawPayout" => rawPayout ?? throw new InvalidOperationException("Raw settlement is unavailable for this program."),
                "capDeduction" => rawPayout is { } raw ? raw - payout : throw new InvalidOperationException("Raw settlement is unavailable for this program."),
                "turnover" => options.Stake,
                "net" => payout - options.Stake,
                _ => binding.Value is null ? payout : Numeric((expressionValue = binding.Value(state)).Value, options?.Source == "event")
            };
            var pair = options?.PairRole == "wager" && options.Subject == "round" ? null : binding.Pair is null ? options?.PairRole == "wager" ? options.Stake : (double?)null : Numeric(binding.Pair(state));
            if (options?.PairRole == "wager" && pair is <= 0) throw new InvalidOperationException("External wager must be positive for every completed paid round.");
            var weight = binding.Weight is null ? (double?)null : Numeric(binding.Weight(state));
            var group = options?.Subject == "episode" ? subject!.Group : Key(binding.Group, state);
            if (binding.AwardId is not null)
            {
                var id = Key(binding.AwardId, state)!; var ids = _awardIds[index] ??= new(StringComparer.Ordinal);
                if (ids.Count >= 4096 && !ids.Contains(id)) throw new InvalidOperationException("Award ID set exceeds the 4096-per-round budget.");
                if (!ids.Add(id)) { _duplicates[index]++; Capture(index, "duplicateAward", number, pair, group, "Award ID " + id + " was observed again in this paid round."); }
            }
            if (subject is not null) { subject.Add(number, pair, weight, group, options!.Reduction); if (options.Subject == "episode" && subject.Values.Count < options.OrdinalLimit) subject.Values.Add(number); }
            else Add(index, number, pair, weight, group, options?.Assertion == "zero" && (expressionValue is { } exact
                ? exact.Kind == ExprType.Boolean ? exact.BoolValue : !exact.NumberNumerator.IsZero : number != 0));
            _matchingChild[index] = true;
            if (_staging[index] is { } analysis && (subject is null || !subject.HasMatchingChild))
            {
                var firstInEpisode = options?.Subject == "episode" && !subject!.HasMatchingChild;
                analysis.MatchingChild(group, firstInEpisode);
                if (subject is not null) subject.HasMatchingChild = true;
            }
        }
        catch (Exception ex) when (IsMeasurementError(ex))
        { if (subject is not null) { subject.Invalid = true; subject.ErrorMessage ??= ex.Message; } else Error(index, ex.Message); }
    }
    public void ObservePayout(int index, double payout) => Observe(index, payout, (Func<double, ExprValue>?)null, null, payout);
    private void Transition<T>(int index, string nodeId, T state, MeasurementBinding<T> binding, MeasurementOptions options)
    {
        var stack = _episodes[index] ??= new();
        if (nodeId == options.EntryNodeId)
        {
            _entries[index]++;
            if (stack.Count >= 16) { _lifecycleComplete[index] = false; _round[index].Observations++; Error(index, "Transition nesting exceeds the 16-level limit."); return; }
            var subject = new SubjectBuffer(); stack.Push(subject);
            try
            {
                if (binding.EntryFilter is { } entry) subject.Excluded = !Predicate(entry(state));
                if (!subject.Excluded && binding.Filter is { } filter) subject.Excluded = !Predicate(filter(state));
                if (!subject.Excluded)
                {
                    subject.Group = Key(binding.Group, state);
                    subject.Add(StateCode(binding.Value!(state)), null, null, subject.Group, "first");
                }
            }
            catch (Exception ex) when (IsMeasurementError(ex)) { subject.Invalid = true; subject.ErrorMessage = ex.Message; }
            _staging[index]!.Lifecycle(subject.Group, entries: 1);
        }
        if (nodeId != options.ExitNodeId) return;
        _round[index].Observations++;
        if (stack.Count == 0) { _lifecycleComplete[index] = false; Error(index, "Transition exit has no matching entry."); return; }
        var completed = stack.Pop(); _exits[index]++;
        _staging[index]!.Lifecycle(completed.Group, exits: 1);
        if (completed.Invalid) { Error(index, completed.ErrorMessage!); return; }
        if (completed.Excluded) { _round[index].Excluded++; return; }
        try
        {
            if (binding.ExitFilter is { } exit && !Predicate(exit(state))) { _round[index].Excluded++; return; }
            Add(index, completed.First, StateCode(binding.Value!(state)), null, completed.Group);
            _matchingChild[index] = true;
            _staging[index]!.MatchingChild(completed.Group);
        }
        catch (Exception ex) when (IsMeasurementError(ex)) { Error(index, ex.Message); }
    }
    public void CompleteRound<T>(int index, T state, MeasurementBinding<T> binding, double payout, double? rawPayout)
    {
        _points[index] = null; _ordinals[index]++;
        var definition = definitions[index];
        if (definition.Options?.Subject == "transition") return;
        if (definition.NodeId is null) Observe(index, state, binding, payout, rawPayout);
        if (definition.Options is not { Subject: "round", PairRole: "wager" } options) return;
        var subject = _subjects[index] ??= new();
        try
        {
            var cost = binding.Pair is null ? options.Stake : Numeric(binding.Pair(state));
            if (!(cost > 0)) throw new InvalidOperationException("External wager must be positive for every completed paid round.");
            subject.Cost = cost;
        }
        catch (Exception ex) when (IsMeasurementError(ex)) { subject.Invalid = true; subject.ErrorMessage ??= ex.Message; }
    }
    public void Prepare()
    {
        if (_prepared) return;
        _prepared = true;
        for (var i = 0; i < definitions.Count; i++) if (definitions[i].Options is { } options)
        {
            if (options.Subject == "round") Emit(i, _subjects[i] ?? new(), options);
            var unclosed = _episodes[i]?.Count ?? 0;
            if (_episodes[i] is { } open)
                foreach (var subject in open) _staging[i]!.Lifecycle(subject.Group, unclosed: 1);
            if (unclosed > 0) { _round[i].Observations += unclosed; _round[i].Errors += unclosed; _round[i].FirstError ??= "Episode remains open at paid-round settlement."; }
            (_round[i].Analysis ??= new(options)).Parent(_matchingChild[i], _entries[i], _exits[i], unclosed, _awardIds[i]?.Count ?? 0, _duplicates[i], _round[i].Count, _round[i].Sum);
        }
    }
    public void Commit()
    {
        if (_finished) throw new InvalidOperationException("The paid-round observation transaction has already finished.");
        Prepare();
        Merge(Total, _round); Merge(Delta, _round);
        _finished = true;
    }
    public void Interrupt(PaidRoundInterruption reason)
    {
        if (_finished) return;
        _finished = true;
        for (var i = 0; i < definitions.Count; i++)
        {
            if (definitions[i].Options is not { Subject: "episode" or "transition" } options) continue;
            var openGroups = new Dictionary<string, long>(StringComparer.Ordinal);
            if (_episodes[i] is { } episodes)
                foreach (var subject in episodes)
                    if (subject.Group is { } group) openGroups[group] = openGroups.GetValueOrDefault(group) + 1;
            var audit = new MeasurementAccumulator { Analysis = new(options) };
            audit.Analysis.InterruptFrom(reason, _staging[i]!, _entries[i], _exits[i], _episodes[i]?.Count ?? 0, _lifecycleComplete[i], openGroups);
            Total[i].Merge(audit); Delta[i].Merge(audit);
        }
    }
    private void Emit(int index, SubjectBuffer subject, MeasurementOptions options)
    {
        _round[index].Observations++;
        if (subject.Invalid) { Error(index, subject.ErrorMessage ?? "Invalid subject."); return; }
        if (subject.Excluded) { _round[index].Excluded++; return; }
        if (subject.Count == 0 && options.Reduction is "first" or "last" or "min" or "max" or "average" or "delta")
        { _round[index].Excluded++; return; }
        try { Add(index, subject.Value(options.Reduction), subject.Cost ?? (subject.PairCount > 0 ? subject.PairValue(options.Reduction) : options.Pair is null ? null : 0),
            subject.WeightCount > 0 ? subject.WeightSum / subject.WeightCount : null, subject.Group);
            if (options.Subject == "episode") _staging[index]!.Episode(subject.Depth, subject.Values, subject.Count, subject.ExitReason, subject.Group); }
        catch (Exception ex) when (IsMeasurementError(ex)) { Error(index, ex.Message); }
    }
    private void Add(int index, double value, double? pair, double? weight, string? group, bool assertionViolation = false)
    {
        // Validate the basic accumulator before touching additional statistics.
        var next = _round[index]; next.Add(value);
        if (definitions[index].Options is { } options) (next.Analysis ??= new(options)).Add(value, pair, weight, group, assertionViolation);
        _round[index] = next;
        Capture(index, "first", value, pair, group); Capture(index, "minimum", value, pair, group); Capture(index, "maximum", value, pair, group);
        if (assertionViolation) Capture(index, "assertionViolation", value, pair, group, "Authored residual was exactly nonzero or the failure predicate was true before binary64 report conversion.");
        if (definitions[index].Options is { ReferenceDistribution.Length: > 0 } reference && !reference.ReferenceDistribution.Any(p => p.Value == value && p.Probability > 0)) Capture(index, "unexpectedSupport", value, pair, group);
    }
    private void Error(int index, string message) { _round[index].Errors++; _round[index].FirstError ??= message.Length > 240 ? message[..240] : message; Capture(index, "invalid", detail: message); }
    private void Capture(int index, string kind, double? value = null, double? pair = null, string? group = null, string? detail = null)
    {
        if (!_captureAll && Total[index].Witnesses?.IsCandidate(kind, value, _roundIndex, _ordinals[index]) == false) return;
        var witnesses = _round[index].Witnesses ??= new();
        if (!witnesses.IsCandidate(kind, value, _roundIndex, _ordinals[index])) return;
        witnesses.Add(new(_roundIndex, _ordinals[index], _points[index], kind, value, pair, group, detail is { Length: > 240 } ? detail[..240] : detail) { EpisodeId = _witnessSubjects[index]?.Id, ParentEpisodeId = _witnessSubjects[index]?.ParentId, EpisodeDepth = _witnessSubjects[index]?.Depth, EpisodeOrdinal = _witnessSubjects[index]?.EpisodeOrdinal });
    }
    private static bool IsMeasurementError(Exception ex) => ex is ExpressionEvaluationException or InvalidOperationException or FormatException or ArithmeticException or ArgumentException;
    private static bool Predicate(ExprValue value) => value.Kind == ExprType.Boolean ? value.BoolValue : throw new InvalidOperationException("Lifecycle scope must return Boolean.");
    private static double Numeric(ExprValue value, bool boolean = false)
    {
        if (boolean) { if (value.Kind != ExprType.Boolean) throw new InvalidOperationException("Event measurement must return Boolean."); return value.BoolValue ? 1 : 0; }
        if (value.Kind != ExprType.Number) throw new InvalidOperationException("Measurement value must return Number.");
        var result = value.AsDouble(); if (!double.IsFinite(result)) throw new InvalidOperationException("Measurement value is nonfinite."); return result;
    }
    private static double StateCode(ExprValue value)
    {
        if (value.Kind != ExprType.Number || !value.NumberDenominator.IsOne || System.Numerics.BigInteger.Abs(value.NumberNumerator) > 9_007_199_254_740_991)
            throw new InvalidOperationException("Transition states require exact integer codes within the safe integer range. Map categorical or continuous states explicitly before observing them.");
        return (double)value.NumberNumerator;
    }
    private static string? Key<T>(Func<T, ExprValue>? expression, T state)
    {
        if (expression is null) return null; var key = expression(state);
        var text = key.Kind switch { ExprType.String or ExprType.Symbol => key.StringValue!, ExprType.Number => key.NumberNumerator.ToString(CultureInfo.InvariantCulture) + (key.NumberDenominator.IsOne ? "" : "/" + key.NumberDenominator.ToString(CultureInfo.InvariantCulture)), ExprType.Boolean => key.BoolValue ? "true" : "false", _ => throw new InvalidOperationException("Group and award IDs must be scalar values.") };
        if (text.Length > 128) throw new InvalidOperationException("Group and award IDs must be at most 128 characters."); return text;
    }
    public static void Merge(MeasurementAccumulator[] target, MeasurementAccumulator[] source)
    { for (var i = 0; i < target.Length; i++) target[i].Merge(source[i]); }
    public static MeasurementSnapshot[] Snapshot(IReadOnlyList<MeasurementDefinition> definitions, MeasurementAccumulator[] values, bool ordered = true)
        => values.Select((value, i) => value.Snapshot(definitions[i].Id, ordered)).ToArray();

    private sealed class SubjectBuffer
    {
        public long Count, PairCount, WeightCount, NonZero, PairNonZero;
        public double Sum, Min, Max, First, Last, PairSum, PairFirst, PairLast, PairMin, PairMax, WeightSum;
        public string? Id, ParentId; public int Depth; public long EpisodeOrdinal, Children;
        public string ExitReason = "condition"; public List<double> Values = [];
        public string? Group;
        public bool Invalid;
        public bool Excluded;
        public bool HasMatchingChild;
        public string? ErrorMessage;
        public double? Cost;
        public void Add(double x, double? pair, double? weight, string? group, string reduction)
        {
            if (Group is not null && group is not null && Group != group) throw new InvalidOperationException("A subject's group changed. Use an entry-cohort key or group individual observations.");
            Group ??= group; if (Count == 0) First = Min = Max = x; else { Min = System.Math.Min(Min, x); Max = System.Math.Max(Max, x); }
            Sum += x; Last = x; Count++; if (x != 0) NonZero++;
            if (pair is { } y) { if (PairCount == 0) PairFirst = PairMin = PairMax = y; else { PairMin = System.Math.Min(PairMin, y); PairMax = System.Math.Max(PairMax, y); } PairSum += y; PairLast = y; PairCount++; if (y != 0) PairNonZero++; }
            if (weight is { } w) { WeightSum += w; WeightCount++; }
            if (!double.IsFinite(Sum) || !double.IsFinite(PairSum) || !double.IsFinite(WeightSum)) throw new ArithmeticException("Subject reduction exceeds finite range.");
        }
        public double Value(string reducer) => Reduce(reducer, Count, Sum, Min, Max, First, Last, NonZero);
        public double PairValue(string reducer) => Reduce(reducer, PairCount, PairSum, PairMin, PairMax, PairFirst, PairLast, PairNonZero);
        private static double Reduce(string reducer, long count, double sum, double min, double max, double first, double last, long nonZero) => reducer switch
        { "sum" => sum, "count" => count, "any" => nonZero > 0 ? 1 : 0, "all" => count > 0 && nonZero == count ? 1 : 0, "first" => first, "last" => last, "min" => min, "max" => max, "average" => sum / count, "delta" => last - first, _ => throw new InvalidOperationException("Unknown subject reducer.") };
    }
}
