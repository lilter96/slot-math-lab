using System.Globalization;
using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Math.Structured;

// ═══════════════════════════════════════════════════════════════════════════
//  SymbolicExecutor — one pass over the graph that builds the payout term
//
//  Follows GraphCompiler's chain semantics node for node, but runs the graph
//  once for all outcomes: a draw introduces a random variable instead of
//  forking, a branch runs both arms and merges the two states field by field
//  (if-conversion), and a loop is unrolled to its cap with the exit states
//  merged from the last iteration backwards, so the body always sees the
//  plain state of "the loop is still running".
//
//  A loop with a long cap is not unrolled but summarized: its body runs once
//  on a state of parameters, and LoopChain solves the Markov reward chain
//  that pass describes. What follows the loop sees its exit state and the
//  amounts it accumulated as terms again.
//
//  Anything this pass does not model exactly raises StructuredDeclined, and
//  the caller keeps the classic interpreter's answer.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>A statement that can fail on some outcomes, with the condition under which it runs.</summary>
internal readonly record struct Obligation(Term Guard, Term Result, string Where);

/// <summary>How loops are evaluated.</summary>
internal enum LoopMode
{
    /// <summary>Summarize loops above the threshold, with full laws where that is cheap and moments otherwise.</summary>
    Auto,

    /// <summary>Summarize them, carrying accumulated amounts in the chain state so that every result has a law.</summary>
    Law,

    /// <summary>Summarize them, carrying accumulated amounts by their moments.</summary>
    Moments,

    /// <summary>Unroll every loop.</summary>
    Unroll
}

internal sealed record LoopOptions(
    LoopMode Mode,
    int ChainAbove,
    long LawWork,
    Rational? PruneBelow,
    int PruneAboveBits,
    long PruneAboveWork,
    long SecondWork,
    long ZeroWork);

/// <summary>How one execution of a loop was evaluated; <see cref="Reason" /> is its exit classification as a term.</summary>
internal sealed record LoopRun(
    string LoopId,
    string Mode,
    int Cap,
    int States,
    int Entries,
    Term Guard,
    Term Reason,
    Rational Pruned);

internal sealed partial class SymbolicExecutor
{
    private const int MaxEntries = 4096;
    private const int MaxTemplatePasses = 8;

    public readonly List<LoopRun> Loops = [];

    /// <summary>Why a loop was not summarized the way that was tried first.</summary>
    public readonly List<string> Notes = [];

    public readonly List<Obligation> Obligations = [];
    private readonly GraphConfig _config;
    private readonly TermFactory _f;
    private readonly Dictionary<string, List<Edge>> _incoming = new();
    private readonly Inference _inference;
    private readonly Dictionary<string, HashSet<string>> _live = new();
    private readonly LoopOptions _loops;
    private readonly Dictionary<string, Node> _nodes;
    private readonly Dictionary<string, List<Edge>> _outgoing = new();
    private readonly Dictionary<(string, string?), List<string>> _spines = new();
    private readonly Translator _translator;
    private readonly BigInteger _winScale;
    private Dictionary<string, int> _drawCount = new();
    private Term _guard;
    private HashSet<string>? _repeated;

    // Inside a loop's template pass: the prefix of its draws' names, and how deep such passes are nested.
    private string _scope = "";
    private int _templateDepth;
    private int _templates;

    public SymbolicExecutor(GraphConfig config, BigInteger winScale, TermFactory factory, Inference inference,
        LoopOptions loops)
    {
        _config = config;
        _winScale = winScale;
        _f = factory;
        _inference = inference;
        _loops = loops;
        _translator = new Translator(factory);
        _guard = factory.TrueT;
        _nodes = config.Nodes.ToDictionary(n => n.Id);
        foreach (Node node in config.Nodes)
        {
            _incoming[node.Id] = [];
            _outgoing[node.Id] = [];
        }

        foreach (Edge edge in config.Edges)
        {
            if (_incoming.TryGetValue(edge.TargetNodeId, out List<Edge>? into))
            {
                into.Add(edge);
            }

            if (_outgoing.TryGetValue(edge.SourceNodeId, out List<Edge>? from))
            {
                from.Add(edge);
            }
        }
    }

    private static StructuredDeclinedException Declined(string reason) => new(reason);

    // ── Top level (GraphCompiler.ProgramBuilder.Build) ───────────────────

    /// <summary>The round payout in sub-credit units, before the win cap.</summary>
    public Term Run(IReadOnlyDictionary<string, object?> initialValues)
    {
        CheckSupported();
        var state = new Dictionary<string, Field>(StringComparer.Ordinal);
        foreach (var (key, value) in initialValues)
        {
            state[key] = new Field(_f.Const(StateValues.Convert(value)), false);
        }

        List<string> entries = _config.Nodes.Where(n => _incoming[n.Id].Count == 0).Select(n => n.Id).ToList();
        if (entries.Count == 0)
        {
            throw Declined("the graph has no entry node");
        }

        MetricsSinkNode sink = _config.Nodes.OfType<MetricsSinkNode>().First();
        if (sink.Settlement is not null)
        {
            throw Declined("the sink applies a settlement policy");
        }

        if (sink.WinStateKey is { } winKey)
        {
            foreach (var entry in entries)
            {
                Chain(entry, sink.Id, null, _f.NullT, ref state);
            }

            if (!state.TryGetValue(winKey, out Field payout))
            {
                throw Declined($"payout field '{winKey}' is never written");
            }

            if (payout.MaybeAbsent)
            {
                throw Declined($"payout field '{winKey}' is not written on every path");
            }

            // ReadStateWin: a number times the win scale, which must come out whole (checked with the result);
            // any other value is a type error, as it is for the multiplication.
            return _f.Arith(BinaryOp.Mul, payout.Value, _f.Const(ExprValue.Number(_winScale)));
        }

        var parts = new List<(ExprValue, Term)>();
        foreach (var entry in entries)
        {
            parts.Add((V.One, Whole(Chain(entry, sink.Id, null, _f.NullT, ref state))));
        }

        return _f.Lin(V.Zero, parts);
    }

    private void CheckSupported()
    {
        if (_config.Plugins is { Length: > 0 })
        {
            throw Declined("the graph uses plugins");
        }

        foreach (Node node in _config.Nodes)
        {
            switch (node)
            {
                case MapNode map:
                    CheckMap(map);
                    break;
                case LibraryNode: throw Declined($"node '{node.Id}' is an unexpanded subgraph");
                case DrawNode draw when draw.DrawWeights is not { Length: > 0 }:
                    if (Reserved(draw.BoardStateKey))
                    {
                        throw Declined($"node '{node.Id}' writes a loop counter");
                    }

                    Strips(draw);
                    break;
                case DrawNode draw when draw.WeightExpressionId is not null &&
                                        _config.Expressions?.ContainsKey(draw.WeightExpressionId) == true:
                    throw Declined($"draw '{node.Id}' computes its weights");
                case DrawNode draw
                    when Reserved(draw.StateWriteKey):
                    throw Declined($"node '{node.Id}' writes a loop counter");
                case DataNode data
                    when Reserved(data.StateKey):
                    throw Declined($"node '{node.Id}' writes a loop counter");
                case PutStateNode put
                    when put.StateKey is null || Reserved(put.StateKey):
                    throw Declined($"node '{node.Id}' writes a loop counter");
                case GetStateNode get
                    when get.StateKey?.StartsWith("__iter_", StringComparison.Ordinal) == true:
                    throw Declined($"node '{node.Id}' reads a loop counter as data");
                case ModifyStateNode modify
                    when Reserved(modify.OutputKey):
                    throw Declined($"node '{node.Id}' writes a loop counter");
                case ModifyStateNode modify
                    when modify.OutputKey is null && Expression(modify.ExpressionId) is not null:
                    throw Declined($"node '{node.Id}' uses the legacy numeric write");
                case LoopNode loop
                    when !_outgoing[loop.Id].Any(e => e.SourcePort == "body"):
                    throw Declined($"loop '{node.Id}' has no body port");
                case LoopNode loop when loop.MaxIterations < 0: throw Declined($"loop '{node.Id}' has a negative cap");
            }
        }
    }

    /// <summary>
    ///     GraphCompiler.ExtractBigInteger: an integer amount is itself, anything else is zero. An amount proven to be
    ///     a whole number stays the term it is, so that sums of such amounts remain sums.
    /// </summary>
    private Term Whole(Term amount) =>
        amount.IsNum && _inference.BoundOf(amount) is { Step.Denominator.IsOne: true } ? amount : _f.Extract(amount);

    private static bool Reserved(string? key) =>
        key is not null && (key.StartsWith("__iter_", StringComparison.Ordinal) ||
                            key.StartsWith("__wins_", StringComparison.Ordinal));

    private Expression? Expression(string? id) =>
        id is not null && _config.Expressions is { } expressions &&
        expressions.TryGetValue(id, out Expression? expression)
            ? expression
            : null;

    // ── Statements ───────────────────────────────────────────────────────

    /// <summary>Translate one expression and record it when some outcome can make it fail.</summary>
    private Term Statement(Expression expression, Dictionary<string, Field> state, string where)
    {
        _f.FailSeen = false;
        Term result = _translator.Translate(expression, state);
        Record(result, where);
        return result;
    }

    private void Record(Term result, string where)
    {
        if (result is ConstTerm { IsErr: true } && _guard == _f.TrueT)
        {
            throw Declined($"{where} fails on every round");
        }

        if (_f.FailSeen || result.OwnFail || result is ConstTerm { IsErr: true })
        {
            Obligations.Add(new Obligation(_guard, result, where));
        }

        _f.FailSeen = false;
    }

    /// <summary>
    ///     A control-flow condition: a Boolean, anything else being an evaluation error. A condition that fails
    ///     whenever it is evaluated is reported as <paramref name="dead" />: its obligation holds only if no outcome
    ///     reaches this point, and then whatever follows on this path is selected by no outcome either.
    /// </summary>
    private Term Condition(Expression expression, Dictionary<string, Field> state, string where, out bool dead)
    {
        _f.FailSeen = false;
        Term raw = _translator.Translate(expression, state);
        Term condition = raw.IsBool ? raw : _f.If(raw, _f.TrueT, _f.FalseT);
        Record(condition, where);
        dead = condition is ConstTerm { IsErr: true };
        return dead ? _f.FalseT : condition;
    }

    private static bool? Known(Term condition) => condition is ConstTerm constant ? constant.Value.BoolValue : null;

    // ── State merging ────────────────────────────────────────────────────

    /// <summary>
    ///     The state that is <paramref name="then" /> where the condition holds and <paramref name="otherwise" />
    ///     elsewhere.
    /// </summary>
    private Dictionary<string, Field> Merge(Term condition, Dictionary<string, Field> then,
        Dictionary<string, Field> otherwise)
    {
        var merged =
            new Dictionary<string, Field>(System.Math.Max(then.Count, otherwise.Count), StringComparer.Ordinal);
        foreach ((var key, Field a) in then)
        {
            if (!otherwise.TryGetValue(key, out Field b))
            {
                merged[key] = new Field(_f.If(condition, a.Value, _f.ErrT), true);
            }
            // A failing condition is reported by its own statement, so equal fields need no conditional.
            else if (a.Value == b.Value)
            {
                merged[key] = new Field(a.Value, a.MaybeAbsent || b.MaybeAbsent);
            }
            else
            {
                merged[key] =
                    new Field(
                        _f.Fault == StructuredFault.MergeKeepsThenArm ? a.Value : _f.If(condition, a.Value, b.Value),
                        a.MaybeAbsent || b.MaybeAbsent);
            }
        }

        foreach ((var key, Field b) in otherwise)
        {
            if (!then.ContainsKey(key))
            {
                merged[key] = new Field(_f.If(condition, _f.ErrT, b.Value), true);
            }
        }

        _f.FailSeen = false;
        return merged;
    }

    private Term MergeValue(Term condition, Term then, Term otherwise)
    {
        Term merged = then == otherwise ? then : _f.If(condition, then, otherwise);
        _f.FailSeen = false;
        return merged;
    }

    // ── Join points ──────────────────────────────────────────────────────

    /// <summary>The nodes every execution starting at <paramref name="start" /> passes through, in order.</summary>
    private List<string> Spine(string start, string? sinkId)
    {
        if (_spines.TryGetValue((start, sinkId), out List<string>? cached))
        {
            return cached;
        }

        var spine = new List<string>();
        var seen = new HashSet<string>();
        var at = start;
        while (at is not null && seen.Add(at))
        {
            spine.Add(at);
            if (at == sinkId)
            {
                break;
            }

            List<Edge> outgoing = _outgoing[at];
            switch (_nodes[at])
            {
                case BranchNode when outgoing.Any(e => e.SourcePort is "true" or "false"):
                    Edge? onTrue = outgoing.FirstOrDefault(e => e.SourcePort == "true");
                    Edge? onFalse = outgoing.FirstOrDefault(e => e.SourcePort == "false");
                    at = onTrue is null || onFalse is null
                        ? null
                        : Join(onTrue.TargetNodeId, onFalse.TargetNodeId, sinkId);
                    break;
                case LoopNode when outgoing.Any(e => e.SourcePort == "body"):
                    at = outgoing.FirstOrDefault(e => e.SourcePort is "exit" or "out")?.TargetNodeId;
                    break;
                default:
                    at = outgoing.Count == 1 ? outgoing[0].TargetNodeId : null;
                    break;
            }
        }

        return _spines[(start, sinkId)] = spine;
    }

    private string? Join(string a, string b, string? sinkId)
    {
        HashSet<string> other = Spine(b, sinkId).ToHashSet();
        return Spine(a, sinkId).FirstOrDefault(other.Contains);
    }

    // ── Chains (GraphCompiler.CompileChain) ──────────────────────────────

    /// <summary>
    ///     Run from <paramref name="nodeId" /> to the end of its chain, or up to (not including)
    ///     <paramref name="stopAt" />. Returns the data-flow value at that point.
    /// </summary>
    private Term Chain(string nodeId, string? sinkId, string? stopAt, Term value, ref Dictionary<string, Field> state)
    {
        while (true)
        {
            _f.Budget.Tick();
            if (nodeId == stopAt)
            {
                return value;
            }

            if (sinkId is not null && nodeId == sinkId)
            {
                return stopAt is null ? value : throw Declined("a branch arm ends before its join");
            }

            Node node = _nodes[nodeId];
            List<Edge> outgoing = _outgoing[nodeId];

            if (node is BranchNode branch && outgoing.Any(e => e.SourcePort is "true" or "false"))
            {
                var onTrue = outgoing.FirstOrDefault(e => e.SourcePort == "true")?.TargetNodeId;
                var onFalse = outgoing.FirstOrDefault(e => e.SourcePort == "false")?.TargetNodeId;
                Term condition = Expression(branch.ConditionId) is { } test
                    ? Condition(test, state, $"branch '{nodeId}'", out _)
                    : _f.FalseT;

                if (Known(condition) is { } pass)
                {
                    if ((pass ? onTrue : onFalse) is not { } taken)
                    {
                        return stopAt is null ? _f.ZeroT : throw Declined("a branch arm ends before its join");
                    }

                    nodeId = taken;
                    continue;
                }

                var join = onTrue is null || onFalse is null ? null : Join(onTrue, onFalse, sinkId);
                if (join is null && stopAt is not null)
                {
                    throw Declined("a branch arm ends before its join");
                }

                Term outer = _guard;
                var thenState = new Dictionary<string, Field>(state, StringComparer.Ordinal);
                Dictionary<string, Field> elseState = state;
                _guard = _f.And(outer, condition);
                Term thenValue = onTrue is null ? _f.ZeroT : Chain(onTrue, sinkId, join, value, ref thenState);
                _guard = _f.And(outer, _f.Not(condition));
                Term elseValue = onFalse is null ? _f.ZeroT : Chain(onFalse, sinkId, join, value, ref elseState);
                _guard = outer;
                _f.FailSeen = false;

                state = Merge(condition, thenState, elseState);
                value = MergeValue(condition, thenValue, elseValue);
                if (join is null)
                {
                    return value;
                }

                nodeId = join;
                continue;
            }

            if (node is LoopNode loop && outgoing.FirstOrDefault(e => e.SourcePort == "body") is { } body)
            {
                value = Loop(loop, body.TargetNodeId, ref state);
                if (outgoing.FirstOrDefault(e => e.SourcePort is "exit" or "out") is not { } exit)
                {
                    return stopAt is null ? value : throw Declined("a branch arm ends before its join");
                }

                nodeId = exit.TargetNodeId;
                continue;
            }

            Term output = Output(node, value, outgoing.Count == 0, ref state);
            if (outgoing.Count == 0)
            {
                return stopAt is null ? output : throw Declined("a branch arm ends before its join");
            }

            // State operations are side effects: the data-flow value passes through unchanged.
            Term next = node is GetStateNode or PutStateNode or ModifyStateNode ? value : output;
            if (outgoing.Count == 1)
            {
                nodeId = outgoing[0].TargetNodeId;
                value = next;
                continue;
            }

            // Fan-out: every downstream path runs in turn on the same input, and the amounts add up.
            if (stopAt is not null)
            {
                throw Declined("a branch arm fans out before its join");
            }

            Term sum = _f.ZeroT;
            foreach (Edge edge in outgoing)
            {
                sum = _f.Lin(V.Zero,
                [
                    (V.One, Whole(sum)),
                    (V.One, Whole(Chain(edge.TargetNodeId, sinkId, null, next, ref state)))
                ]);
            }

            return sum;
        }
    }

    // ── Loops (GraphCompiler.CompileLoopChain) ───────────────────────────

    private Term Loop(LoopNode loop, string bodyId, ref Dictionary<string, Field> state)
    {
        // A loop inside a template pass has parameters for its entry state, which have no law to start a chain from.
        if (_templateDepth == 0 && _loops.Mode != LoopMode.Unroll && loop.MaxIterations > _loops.ChainAbove &&
            Summarize(loop, bodyId, ref state) is { } summarized)
        {
            return summarized;
        }

        Term amount = Unroll(loop, bodyId, ref state);
        if (_templateDepth == 0)
        {
            Loops.Add(new LoopRun(loop.Id, "unrolled", loop.MaxIterations, 0, 0, _guard,
                state[$"__exitReason_{loop.Id}__"].Value, Rational.Zero));
        }

        return amount;
    }

    private Term Unroll(LoopNode loop, string bodyId, ref Dictionary<string, Field> state)
    {
        var iterKey = $"__iter_{loop.Id}__";
        var winsKey = $"__wins_{loop.Id}__";
        var reasonKey = $"__exitReason_{loop.Id}__";
        Expression? stop = Expression(loop.StopConditionId);
        Term outer = _guard;
        state = new Dictionary<string, Field>(state, StringComparer.Ordinal)
        {
            [iterKey] = new(_f.ZeroT, false),
            [winsKey] = new(_f.ZeroT, false)
        };

        var exits = new List<(Term Condition, Dictionary<string, Field> State)>();
        var iteration = 0;
        for (; iteration < loop.MaxIterations; iteration++)
        {
            // At the cap the loop stops without evaluating the stop expression.
            var dead = false;
            Term condition = stop is null
                ? _f.FalseT
                : Condition(stop, state, $"stop condition of loop '{loop.Id}'", out dead);
            if (dead)
            {
                break;
            }

            if (Known(condition) is { } stopped)
            {
                if (stopped)
                {
                    break;
                }
            }
            else
            {
                var exit = new Dictionary<string, Field>(state, StringComparer.Ordinal);
                exit[reasonKey] = new Field(ExitReason(loop, exit), false);
                exits.Add((condition, exit));
                _guard = _f.And(_guard, _f.Not(condition));
                _f.FailSeen = false;
            }

            Term result = Chain(bodyId, null, null, _f.NullT, ref state);
            state = new Dictionary<string, Field>(state, StringComparer.Ordinal)
            {
                [iterKey] = new(_f.Const(ExprValue.Number(iteration + 1)), false),
                // Only an integer amount is accumulated; any other body result leaves the total as it is.
                [winsKey] = new(_f.Lin(V.Zero, [(V.One, state[winsKey].Value), (V.One, Whole(result))]), false)
            };
        }

        state[reasonKey] =
            new Field(
                iteration >= loop.MaxIterations ? _f.Const(ExprValue.String("modelLimit")) : ExitReason(loop, state),
                false);
        _guard = outer;
        for (var i = exits.Count - 1; i >= 0; i--)
        {
            state = Merge(exits[i].Condition, exits[i].State, state);
        }

        return state[winsKey].Value;
    }

    /// <summary>
    ///     Evaluate the loop as a chain and put its outcome into <paramref name="state" />. Returns null, with nothing
    ///     changed, when the loop is not a chain this pass can describe; the caller then unrolls it.
    /// </summary>
    private Term? Summarize(LoopNode loop, string bodyId, ref Dictionary<string, Field> state)
    {
        var limit = _f.Budget.Limit;
        LoopMode[] modes = _loops.Mode == LoopMode.Auto ? [LoopMode.Law, LoopMode.Moments] : [_loops.Mode];
        foreach (LoopMode mode in modes)
        {
            // Under Auto the full law is tried first, on a small allowance: it either is cheap or is not the way.
            // No attempt may take more than half of what is left: unrolling the loop must stay affordable.
            var trial = _loops.Mode == LoopMode.Auto && mode == LoopMode.Law;
            var allowance = (limit - _f.Budget.Used) / 2;
            _f.Budget.Limit = _f.Budget.Used + (trial ? System.Math.Min(allowance, _loops.LawWork) : allowance);
            try
            {
                return Chained(loop, bodyId, ref state, mode == LoopMode.Law);
            }
            catch (StructuredBudgetException exceeded) when (exceeded.Work && _f.Budget.Used <= limit)
            {
                Notes.Add(trial
                    ? $"loop '{loop.Id}' as {mode}: beyond the allowance for a full law"
                    : $"loop '{loop.Id}' as {mode}: beyond half of the remaining work");
            }
            catch (Exception exception) when (exception is LoopChainDeclinedException or StructuredDeclinedException
                                                  or InsufficientExecutionStackException)
            {
                Notes.Add($"loop '{loop.Id}' as {mode}: {exception.Message}");
            }
            finally
            {
                _f.Budget.Limit = limit;
                _f.FailSeen = false;
            }
        }

        return null;
    }

    private Pass Template(LoopNode loop, string bodyId, Dictionary<string, Field> entry,
        Dictionary<string, ParamTerm> parameters)
    {
        (Term guard, Dictionary<string, int> counts, var scope, var recorded) =
            (_guard, _drawCount, _scope, Obligations.Count);
        _guard = _f.TrueT;
        _drawCount = new Dictionary<string, int>();
        _scope = $"{loop.Id}@{_templates++}/";
        _templateDepth++;
        try
        {
            var state = new Dictionary<string, Field>(entry, StringComparer.Ordinal);
            foreach ((var key, ParamTerm parameter) in parameters)
            {
                // A parameter that can be an error stands for a field some executions do not have: reading it is recorded.
                state[key] = new Field(parameter, parameter.CanErr);
            }

            // The stop condition and the exit reason are checked state by state, so they leave no obligation here.
            Term? stop = null;
            if (Expression(loop.StopConditionId) is { } test)
            {
                Term raw = _translator.Translate(test, state);
                stop = raw.IsBool ? raw : _f.If(raw, _f.TrueT, _f.FalseT);
            }

            Term? reason = loop.ExitReason is null ? null : _translator.Translate(loop.ExitReason, state);
            _f.FailSeen = false;

            Term result = Chain(bodyId, null, null, _f.NullT, ref state);
            var (iterKey, winsKey) = ($"__iter_{loop.Id}__", $"__wins_{loop.Id}__");
            state = new Dictionary<string, Field>(state, StringComparer.Ordinal)
            {
                [iterKey] = new(_f.Lin(V.One, [(V.One, state[iterKey].Value)]), false),
                [winsKey] = new(_f.Lin(V.Zero, [(V.One, state[winsKey].Value), (V.One, Whole(result))]), false)
            };
            return new Pass(stop, reason, state, Obligations.GetRange(recorded, Obligations.Count - recorded));
        }
        finally
        {
            Obligations.RemoveRange(recorded, Obligations.Count - recorded);
            (_guard, _drawCount, _scope) = (guard, counts, scope);
            _templateDepth--;
            _f.FailSeen = false;
        }
    }

    private Term Chained(LoopNode loop, string bodyId, ref Dictionary<string, Field> state, bool lawful)
    {
        var iterKey = $"__iter_{loop.Id}__";
        var winsKey = $"__wins_{loop.Id}__";
        var entry = new Dictionary<string, Field>(state, StringComparer.Ordinal)
        {
            [iterKey] = new(_f.ZeroT, false),
            [winsKey] = new(_f.ZeroT, false)
        };

        // Fields that are constants on entry stay constants until a pass shows the body changing them: a paytable
        // must fold into the body's terms, a counter must not.
        var parameters = new Dictionary<string, ParamTerm>(StringComparer.Ordinal);
        foreach ((var key, Field field) in entry)
        {
            if (field.Value is not ConstTerm || field.MaybeAbsent)
            {
                parameters[key] = _f.NewParam(key, field.Value.Kinds, null, field.Value.CanErr || field.MaybeAbsent);
            }
        }

        Pass pass;
        var round = 0;
        while (true)
        {
            if (round == MaxTemplatePasses)
            {
                throw new LoopChainDeclinedException("the loop's state does not settle into one shape");
            }

            pass = Template(loop, bodyId, entry, parameters);
            var widened = false;
            foreach ((var key, Field next) in pass.Next)
            {
                var fails = next.Value.CanErr || next.MaybeAbsent;
                if (parameters.TryGetValue(key, out ParamTerm? parameter))
                {
                    if (next.Value == parameter ||
                        ((next.Value.Kinds & ~parameter.Kinds) == 0 && (!fails || parameter.CanErr)))
                    {
                        continue;
                    }

                    parameters[key] = _f.NewParam(key, (byte)(parameter.Kinds | next.Value.Kinds), null,
                        parameter.CanErr || fails);
                }
                else if (entry.TryGetValue(key, out Field was))
                {
                    if (next.Value == was.Value && !next.MaybeAbsent)
                    {
                        continue;
                    }

                    parameters[key] = _f.NewParam(key, (byte)(was.Value.Kinds | next.Value.Kinds), null,
                        was.Value.CanErr || fails);
                }
                else
                {
                    // Written by the body and not there before the first iteration: absent is carried as the error value.
                    parameters[key] = _f.NewParam(key, next.Value.Kinds, null, true);
                }

                widened = true;
            }

            if (!widened)
            {
                break;
            }

            round++;
        }

        // Every field the body changes is one of three things: an amount it only ever adds to and nothing else in
        // the loop reads (carried by its moments), part of the chain state, or left behind and never read again.
        HashSet<string> live = Live(loop, bodyId);
        List<string> variant = parameters.Keys.Where(key => pass.Next[key].Value != parameters[key])
            .Order(StringComparer.Ordinal).ToList();
        var deltas = new SortedDictionary<string, Term>(StringComparer.Ordinal);
        foreach (var key in variant)
        {
            ParamTerm parameter = parameters[key];
            Field next = pass.Next[key];
            if (lawful || !(key == winsKey || live.Contains(key)) || parameter.CanErr || !parameter.IsNum ||
                next.MaybeAbsent || next.Value is not LinTerm sum)
            {
                continue;
            }

            var at = Array.IndexOf(sum.Terms, parameter);
            if (at < 0 || !sum.Coefs[at].Equals(V.One))
            {
                continue;
            }

            Term delta = _f.Lin(V.Zero, [(V.One, sum), (ExprValue.Number(-1), parameter)]);
            if (!Bits.Intersects(delta.Support, parameter.Support))
            {
                deltas[key] = delta;
            }
        }

        var owner = new Dictionary<int, string>();
        foreach ((var key, ParamTerm parameter) in parameters)
        {
            owner[Bits.First(parameter.Support)] = key;
        }

        var carried = new SortedSet<string>(StringComparer.Ordinal);
        var pending = new Stack<Term>();

        void Need(Term? term)
        {
            if (term is null || !Bits.Intersects(term.Support, _f.ParamSlots))
            {
                return;
            }

            foreach (var slot in Bits.Enumerate(term.Support))
            {
                if (owner.TryGetValue(slot, out var key) && carried.Add(key))
                {
                    // Something reads it, so it is state, whatever it looked like.
                    deltas.Remove(key);
                    pending.Push(pass.Next[key].Value);
                }
            }
        }

        Need(pass.Stop);
        Need(pass.Reason);
        foreach (Obligation obligation in pass.Obligations)
        {
            Need(obligation.Guard);
            Need(obligation.Result);
        }

        foreach (var key in variant)
        {
            if (!deltas.ContainsKey(key) && (key == winsKey || live.Contains(key)) && carried.Add(key))
            {
                pending.Push(pass.Next[key].Value);
            }
        }

        for (var settled = false; !settled;)
        {
            while (pending.Count > 0)
            {
                Need(pending.Pop());
            }

            settled = true;
            foreach (Term delta in deltas.Values.ToList())
            {
                var before = carried.Count;
                Need(delta);
                settled &= carried.Count == before;
            }

            settled &= pending.Count == 0;
        }

        string[] keys = [.. carried];
        string[] amounts = [.. deltas.Keys];
        var bits = Bits.Empty;
        foreach (ParamTerm parameter in parameters.Values)
        {
            bits = Bits.Union(bits, parameter.Support);
        }

        var name = $"{loop.Id}@{_templates}";
        var template = new LoopTemplate
        {
            Name = name,
            Cap = loop.MaxIterations,
            StateKeys = keys,
            StateParams = keys.Select(key => parameters[key]).ToArray(),
            Next = keys.Select(key => pass.Next[key].Value).ToArray(),
            Deltas = amounts.Select(key => deltas[key]).ToArray(),
            Stop = pass.Stop,
            ExitReason = pass.Reason,
            Obligations = pass.Obligations,
            ParamBits = bits,
            // A field the loop steers by and nothing reads afterwards does not tell its exits apart.
            Forgotten = keys.Select(key => key != winsKey && !live.Contains(key) &&
                                           pass.Next[key].Value != parameters[key]).ToArray(),
            PruneBelow = _loops.PruneBelow,
            PruneAboveBits = _loops.PruneAboveBits,
            PruneAboveWork = _loops.PruneAboveWork
        };

        // The loop starts from whatever the round has made of the carried fields: one chain run per value of those.
        Term start = _f.Tuple(keys.Select(key => entry.TryGetValue(key, out Field field) ? field.Value : _f.ErrT)
            .ToArray());
        ExprValue[] entries;
        var total = true;
        Law? starts = null;
        if (start is ConstTerm known)
        {
            entries = [known.Value];
        }
        else if (_guard == _f.TrueT)
        {
            starts = _inference.LawOf(start);
            entries = [.. starts.Values];
        }
        else
        {
            // Only the values that occur where the loop is reached; elsewhere its outcome is selected by nothing.
            total = false;
            starts = _inference.LawOf(_f.If(_guard, _f.Tuple([start]), _f.NullT));
            entries = starts.Values.Where(value => value.Kind == ExprType.Array)
                .Select(value => value.ArrayValue![0]).ToArray();
        }

        if (entries.Length is 0 or > MaxEntries)
        {
            throw new LoopChainDeclinedException("the loop starts from too many states");
        }

        Array.Sort(entries, V.Order);
        var chain = new LoopChain(template, _f, _inference, _loops);
        var exits = new LoopExits[entries.Length];
        for (var i = 0; i < exits.Length; i++)
        {
            exits[i] = chain.Run(entries[i]);
        }

        // The probability that a round is in this loop when a run of it is cut short: each run by the probability
        // that the loop starts from its state.
        Rational cut = Rational.Zero;
        if (exits.Any(exit => !exit.Pruned.IsZero) && _f.Fault != StructuredFault.PrunedMassDropped)
        {
            var reached = new Dictionary<ExprValue, Rational>(V.ValueComparer.Instance);
            if (starts is null)
            {
                reached[entries[0]] = _guard == _f.TrueT ? Rational.One : _inference.Probability(_guard);
            }
            else
            {
                for (var i = 0; i < starts.Values.Length; i++)
                {
                    ExprValue value = starts.Values[i];
                    if (total || value.Kind == ExprType.Array)
                    {
                        reached[total ? value : value.ArrayValue![0]] = new Rational(starts.Mass[i], starts.Total);
                    }
                }
            }

            for (var i = 0; i < exits.Length; i++)
            {
                cut += reached[entries[i]] * exits[i].Pruned;
            }
        }

        (Term[] fields, Term reason, Term[] added) = chain.Materialize(start, entries, exits, name, total);

        var after = new Dictionary<string, Field>(entry, StringComparer.Ordinal);
        for (var i = 0; i < keys.Length; i++)
        {
            if (pass.Next[keys[i]].Value == parameters[keys[i]])
            {
                continue; // read by the loop and left as it was
            }

            if (template.Forgotten[i])
            {
                after[keys[i]] = new Field(_f.Poison, false);
                continue;
            }

            var position = i;
            after[keys[i]] = new Field(fields[i],
                exits.Any(exit => exit.Classes.Any(values => V.IsErr(values[position]))));
        }

        for (var i = 0; i < amounts.Length; i++)
        {
            after[amounts[i]] =
                new Field(_f.Lin(V.Zero, [(V.One, entry[amounts[i]].Value), (V.One, added[i])]), false);
        }

        foreach (var key in variant)
        {
            if (!carried.Contains(key) && !deltas.ContainsKey(key))
            {
                after[key] = new Field(_f.Poison, false);
            }
        }

        after[$"__exitReason_{loop.Id}__"] = new Field(reason, false);
        state = after;
        Loops.Add(new LoopRun(loop.Id, lawful || amounts.Length == 0 ? "chain" : "chain-moments", loop.MaxIterations,
            chain.States, entries.Length, _guard, reason, cut));
        return after[winsKey].Value;
    }

    /// <summary>
    ///     The fields something may read once the loop is over. A loop that can run more than once in a round finds
    ///     what its previous run left, so its own body counts as a reader too.
    /// </summary>
    private HashSet<string> Live(LoopNode loop, string bodyId)
    {
        if (_live.TryGetValue(loop.Id, out HashSet<string>? known))
        {
            return known;
        }

        if (_repeated is null)
        {
            // Loop bodies, and anything at all once a node fans out: its paths run in turn and may meet again.
            _repeated = [];
            var fans = _config.Nodes.Any(node => node is not (BranchNode or LoopNode) && _outgoing[node.Id].Count > 1);
            foreach (Node node in _config.Nodes)
            {
                if (fans)
                {
                    _repeated.Add(node.Id);
                }
                else if (node is LoopNode && _outgoing[node.Id].FirstOrDefault(e => e.SourcePort == "body") is { } body)
                {
                    _repeated.UnionWith(Reach(body.TargetNodeId));
                }
            }
        }

        // A loop that runs once in a round is read only by what follows its exit, and by the sink.
        HashSet<string>? after = null;
        if (!_repeated.Contains(loop.Id))
        {
            after = [];
            foreach (Edge edge in _outgoing[loop.Id])
            {
                if (edge.TargetNodeId != bodyId)
                {
                    after.UnionWith(Reach(edge.TargetNodeId));
                }
            }
        }

        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Node node in _config.Nodes)
        {
            if (after is not null && !after.Contains(node.Id) && node is not MetricsSinkNode)
            {
                continue;
            }

            switch (node)
            {
                case ModifyStateNode modify: Translator.Reads(Expression(modify.ExpressionId), names); break;
                case BranchNode branch: Translator.Reads(Expression(branch.ConditionId), names); break;
                case LoopNode other:
                    Translator.Reads(Expression(other.StopConditionId), names);
                    Translator.Reads(other.ExitReason, names);
                    break;
                case GetStateNode get: names.Add(get.StateKey ?? "__default__"); break;
                case MetricsSinkNode { WinStateKey: { } winKey }: names.Add(winKey); break;
            }
        }

        return _live[loop.Id] = [.. names];
    }

    private HashSet<string> Reach(string from)
    {
        var seen = new HashSet<string>
        {
            from
        };
        var pending = new Stack<string>();
        pending.Push(from);
        while (pending.Count > 0)
        {
            foreach (Edge edge in _outgoing[pending.Pop()])
            {
                if (seen.Add(edge.TargetNodeId))
                {
                    pending.Push(edge.TargetNodeId);
                }
            }
        }

        return seen;
    }

    private Term ExitReason(LoopNode loop, Dictionary<string, Field> state)
    {
        if (loop.ExitReason is null)
        {
            return _f.Const(ExprValue.String("condition"));
        }

        Term reason = Statement(loop.ExitReason, state, $"exit reason of loop '{loop.Id}'");
        // ExitClassification.Read accepts five names and nothing else.
        if (reason is { Pure: true, Vals: { } values } && values.All(v => v.Kind == ExprType.String
                                                                          && v.StringValue is "condition"
                                                                              or "modelLimit" or "payoutCap"
                                                                              or "authoredStop" or "resourceExpiry"))
        {
            return reason;
        }

        throw Declined($"exit reason of loop '{loop.Id}' is not provably a classification");
    }

    // ── Node outputs (GraphCompiler.CompileNodeOutput) ───────────────────

    private Term Output(Node node, Term input, bool deadEnd, ref Dictionary<string, Field> state)
    {
        switch (node)
        {
            case DrawNode draw when draw.DrawWeights is not { Length: > 0 }: return Spin(draw, ref state);
            case MapNode map: return Map(map, input, state);
            case DrawNode draw:
            {
                DrawWeight[] weights = draw.DrawWeights!;
                if (weights.Any(w => w.Weight < 0))
                {
                    throw Declined($"draw '{draw.Id}' has a negative weight");
                }

                // The exact interpreter never visits an outcome of weight zero.
                DrawWeight[] live = weights.Where(w => w.Weight > 0).ToArray();
                if (live.Length == 0)
                {
                    throw Declined($"draw '{draw.Id}' has no outcome of positive weight");
                }

                var occurrence = _drawCount[draw.Id] = _drawCount.GetValueOrDefault(draw.Id) + 1;
                Rv rv = _f.NewRv($"{_scope}{draw.Id}#{occurrence}",
                    live.Select(w => new BigInteger(w.Weight)).ToArray());
                _f.Budget.Tick(live.Length);
                if (draw.StateWriteKey is { } key)
                {
                    state = new Dictionary<string, Field>(state, StringComparer.Ordinal)
                    {
                        [key] = new(_f.Table(rv, live.Select(w => ExprValue.String(w.OutcomeId)).ToArray()), false)
                    };
                }

                return _f.Table(rv, live.Select(w => ExprValue.Number(new BigInteger(w.Value) * _winScale)).ToArray());
            }
            case DataNode data:
            {
                ExprValue[] parsed = data.Values.Select(text => BigInteger.TryParse(text, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out BigInteger number)
                    ? ExprValue.Number(number)
                    : ExprValue.String(text)).ToArray();
                state = new Dictionary<string, Field>(state, StringComparer.Ordinal)
                {
                    [data.StateKey] = new(_f.Const(ExprValue.Array(parsed)), false)
                };
                // With a successor the node is compiled once on a null input; as a chain end it returns its input.
                return deadEnd ? input : _f.NullT;
            }
            case GetStateNode get:
            {
                if (!state.TryGetValue(get.StateKey ?? "__default__", out Field field))
                {
                    return _f.NullT;
                }

                return field.MaybeAbsent
                    ? throw Declined($"node '{get.Id}' reads a field that is not written on every path")
                    : field.Value;
            }
            case PutStateNode put:
                state = new Dictionary<string, Field>(state, StringComparer.Ordinal)
                {
                    [put.StateKey] = new(input, false)
                };
                return input;
            case ModifyStateNode modify:
            {
                if (Expression(modify.ExpressionId) is not { } expression)
                {
                    return _f.NullT;
                }

                Term result = Statement(expression, state, $"node '{modify.Id}'");
                state = new Dictionary<string, Field>(state, StringComparer.Ordinal)
                {
                    [modify.OutputKey!] = new(result, false)
                };
                return _f.NullT;
            }
            case BranchNode branch:
            {
                if (Expression(branch.ConditionId) is not { } test)
                {
                    return input;
                }

                Term result = MergeValue(Condition(test, state, $"branch '{branch.Id}'", out _), input, _f.ZeroT);
                return result;
            }
            case MetricsSinkNode: return input;
            default: return _f.NullT;
        }
    }

    // ── Loops as chains (LoopChain) ──────────────────────────────────────

    /// <summary>One symbolic pass over a loop's body on a state of parameters.</summary>
    private sealed record Pass(Term? Stop, Term? Reason, Dictionary<string, Field> Next, List<Obligation> Obligations);
}
