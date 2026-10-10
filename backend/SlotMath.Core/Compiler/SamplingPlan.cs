using System.Globalization;
using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Random;
using SlotMath.Core.Measurements;

namespace SlotMath.Core.Compiler;

internal interface ICompiledSampling<S, T>
{
    ISamplingRunner<S, T> CreateRunner(S initial, MeasurementCollector? measurements = null, string[]? persistentKeys = null, SlotMath.Core.Math.LoopTerminationEvidence? loopEvidence = null);

    /// <summary>An instance of this program for one worker's exclusive use until
    /// <see cref="Return"/>. Instances are interchangeable: which one runs a
    /// chunk never affects its samples.</summary>
    ICompiledSampling<S, T> Rent() => this;
    void Return(ICompiledSampling<S, T> instance) { }
}

internal interface ISamplingRunner<S, T>
{
    T Run(SeededRandom rng, CancellationToken token);
    S ExportState();
    void ObserveRound(double payout) { }
    double? RawPayout => null;
    void ResetTrajectory() { }
}

// Exact interpreters and program analysis unwrap this annotation. Sampled
// execution recognizes the plan at its root; composing a Slot above it safely
// falls back to the reference program, with ordinary monadic semantics.
internal sealed class SamplingPlanSlot(Slot<Dictionary<string, object?>, BigInteger> reference, SamplingPlan plan)
    : Slot<Dictionary<string, object?>, BigInteger>, IAnnotationNode, ICompiledSampling<Dictionary<string, object?>, BigInteger>
{
    object IAnnotationNode.InnerUntyped => reference;
    public ISamplingRunner<Dictionary<string, object?>, BigInteger> CreateRunner(Dictionary<string, object?> initial, MeasurementCollector? measurements = null, string[]? persistentKeys = null, SlotMath.Core.Math.LoopTerminationEvidence? loopEvidence = null) => plan.CreateRunner(initial, measurements, persistentKeys, loopEvidence);
    public ICompiledSampling<Dictionary<string, object?>, BigInteger> Rent() => plan.Rent();
    public void Return(ICompiledSampling<Dictionary<string, object?>, BigInteger> instance) => plan.Return((SamplingPlan)instance);
}

internal sealed class SamplingPlan : ICompiledSampling<Dictionary<string, object?>, BigInteger>
{
    // A plan is immutable while it runs, but workers that read one instance
    // from several cores measurably slow each other down. Each concurrent
    // worker therefore leases its own replica, built from the same inputs.
    private readonly System.Collections.Concurrent.ConcurrentBag<SamplingPlan> _idle = new();
    private int _leased;
    private delegate object? Chain(SamplingFrame frame, SeededRandom rng, object? input);

    // One node of a straight run: a state update (Value into Output), a field
    // assigned to itself (Self), or a draw.
    private readonly record struct Step(SamplingDelegate<ExprValue>? Value, int Output, bool Integer, string? Self, Chain? Draw);
    private readonly Dictionary<string, int> _layout = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string?), Chain> _chains = new();
    private readonly GraphConfig _config;
    private readonly Dictionary<string, Node> _nodes;
    private readonly Dictionary<string, Edge[]> _outgoing;
    private readonly SamplingExpressions _expressions;
    private readonly BigInteger _scale;
    private readonly BigInteger _cap;
    private readonly Func<SamplingFrame, SeededRandom, BigInteger> _run;
    private readonly SamplingCell[] _defaults;
    private readonly IReadOnlyList<MeasurementDefinition> _measurements;
    private readonly Dictionary<int, MeasurementBinding<SamplingFrame>> _measurementExpressions = new();
    private readonly Dictionary<string, ExprValue> _constants;
    private readonly SamplingShapes _shapes;
    private SamplingPlan? _general;

    public static Slot<Dictionary<string, object?>, BigInteger> Wrap(GraphConfig config, BigInteger scale, Slot<Dictionary<string, object?>, BigInteger> reference, IReadOnlyList<MeasurementDefinition> measurements)
    {
        // Unsupported operators use the general interpreter. Eligibility is
        // checked before compilation, rather than silently catching failures.
        if (config.Plugins.Length != 0 || config.Nodes.Any(n => n switch
        {
            DrawNode d => d.DrawWeights is not { Length: > 0 } || d.WeightExpressionId is not null,
            MapNode => true,
            LoopNode l => !config.Edges.Any(e => e.SourceNodeId == l.Id && e.SourcePort == "body"),
            LibraryNode => true,
            _ => false,
        })) return reference;
        SamplingPlan plan;
        try { plan = new SamplingPlan(config, scale, measurements, ConstantState(config, measurements), new()); }
        catch (ConstantStateConflict) { plan = new SamplingPlan(config, scale, measurements, NoConstants, new()); }
        return new SamplingPlanSlot(reference, plan);
    }

    private static readonly Dictionary<string, ExprValue> NoConstants = new();

    // Initial state that no node, loop, settlement or iteration variable can
    // assign. Such a field holds its initial value for every round of a runner
    // whose own initial state does not override it.
    private static Dictionary<string, ExprValue> ConstantState(GraphConfig config, IReadOnlyList<MeasurementDefinition> measurements)
    {
        var written = new HashSet<string>(MonetarySettlement.EvidenceKeys, StringComparer.Ordinal) { "__modified__", "__default__" };
        var expressions = new List<Expression?>();
        if (config.Expressions is { } shared) expressions.AddRange(shared.Values);
        foreach (var node in config.Nodes)
            switch (node)
            {
                case ModifyStateNode { OutputKey: { } key }: written.Add(key); break;
                case DrawNode { StateWriteKey: { } key }: written.Add(key); break;
                case PutStateNode put: written.Add(put.StateKey); break;
                case DataNode data: written.Add(data.StateKey); break;
                case LoopNode loop:
                    written.Add($"__exitReason_{loop.Id}__"); written.Add($"__iter_{loop.Id}__"); written.Add($"__wins_{loop.Id}__");
                    expressions.Add(loop.ExitReason); break;
            }
        foreach (var d in measurements)
            expressions.AddRange([d.Value, d.Filter, d.Options?.Group, d.Options?.Pair, d.Options?.Weight, d.Options?.AwardId, d.Options?.EntryFilter, d.Options?.ExitFilter, d.Options?.ExitReason]);
        if (!expressions.All(e => Variables(e, written))) return NoConstants;

        var constants = new Dictionary<string, ExprValue>(StringComparer.Ordinal);
        foreach (var (key, value) in config.InitialState ?? new())
        {
            if (written.Contains(key)) continue;
            var cell = SamplingCell.FromRaw(InitialStateValues.Materialize(value));
            if (cell.Readable) constants[key] = cell.StoredValue;
        }
        return constants;
    }

    // Collects the names an expression assigns while iterating. False for a form this analysis does not know.
    private static bool Variables(Expression? expression, HashSet<string> names)
    {
        bool Named(params string?[] bound) { foreach (var name in bound) if (name is not null) names.Add(name); return true; }
        return expression switch
        {
            null or ConstantExpr or FieldAccessExpr => true,
            NotExpr e => Variables(e.Expr, names),
            BinaryExpr e => Variables(e.Left, names) && Variables(e.Right, names),
            CompareExpr e => Variables(e.Left, names) && Variables(e.Right, names),
            IfExpr e => Variables(e.Condition, names) && Variables(e.ThenExpr, names) && Variables(e.ElseExpr, names),
            CallExpr e => e.Args.All(a => Variables(a, names)),
            AggregateExpr e => Named(e.ItemName) && Variables(e.Predicate, names) && Variables(e.ValueExpr, names),
            MapExpr e => Named(e.ItemName, e.IndexName) && Variables(e.Body, names),
            FilterExpr e => Named(e.ItemName, e.IndexName) && Variables(e.Predicate, names),
            FoldExpr e => Named(e.AccName, e.ItemName, e.IndexName) && Variables(e.Init, names) && Variables(e.Body, names),
            _ => false,
        };
    }

    private SamplingPlan(GraphConfig config, BigInteger scale, IReadOnlyList<MeasurementDefinition> measurements, Dictionary<string, ExprValue> constants, SamplingShapes shapes)
    {
        _config = config; _scale = scale; _measurements = measurements; _constants = constants; _shapes = shapes;
        _nodes = config.Nodes.ToDictionary(n => n.Id);
        _outgoing = config.Nodes.ToDictionary(n => n.Id, n => config.Edges.Where(e => e.SourceNodeId == n.Id).ToArray());
        _expressions = new SamplingExpressions(SlotIndex, constants: constants, shapes: shapes);
        for (var i = 0; i < measurements.Count; i++)
            _measurementExpressions[i] = new(CompileMeasurement(measurements[i].Value), CompileMeasurement(measurements[i].Filter),
                CompileMeasurement(measurements[i].Options?.Group), CompileMeasurement(measurements[i].Options?.Pair),
                CompileMeasurement(measurements[i].Options?.Weight), CompileMeasurement(measurements[i].Options?.AwardId),
                CompileMeasurement(measurements[i].Options?.EntryFilter), CompileMeasurement(measurements[i].Options?.ExitFilter), CompileMeasurement(measurements[i].Options?.ExitReason));
        var defaults = new Dictionary<int, SamplingCell>();
        foreach (var (key, value) in config.InitialState ?? new())
        {
            var cell = SamplingCell.FromRaw(InitialStateValues.Materialize(value));
            defaults[SlotIndex(key)] = cell;
            if (cell.TryCachedValue(out var valueToRemember)) _expressions.Remember(valueToRemember);
        }
        var sink = config.Nodes.OfType<MetricsSinkNode>().Single();
        _cap = new BigInteger(sink.WinCap!.Value) * scale;
        var incoming = config.Edges.Select(e => e.TargetNodeId).ToHashSet();
        var entries = config.Nodes.Where(n => !incoming.Contains(n.Id)).Select(n => CompileChain(n.Id, sink.Id)).ToArray();
        var winSlot = sink.WinStateKey is { } keyName ? SlotIndex(keyName) : -1;
        int[] settlementSlots = sink.Settlement is null ? [] : MonetarySettlement.EvidenceKeys.Select(WriteSlot).ToArray();
        _run = (frame, rng) =>
        {
            var win = BigInteger.Zero;
            foreach (var entry in entries)
            {
                var result = entry(frame, rng, null);
                if (winSlot < 0) win += Extract(result);
            }
            var exactWin = ExprValue.Rational(win, scale);
            if (winSlot >= 0)
            {
                var cell = frame.Cells[winSlot]; var key = sink.WinStateKey!;
                if (!cell.Present) throw new ExpressionEvaluationException("EVAL_MISSING_STATE", $"Payout field '{key}' is absent.", key);
                if (cell.Value.Kind != ExprType.Number || cell.HasRaw && cell.Raw is not (BigInteger or int or long or ExprValue))
                    throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", $"Payout field '{key}' must be numeric.", key);
                exactWin = cell.Value;
            }
            if (sink.Settlement is { } policy)
            {
                var after = policy.Apply(exactWin);
                frame.Cells[settlementSlots[0]] = SamplingCell.FromRaw(exactWin);
                frame.Cells[settlementSlots[1]] = SamplingCell.FromRaw(after);
                frame.Cells[settlementSlots[2]] = SamplingCell.FromRaw(ExprValue.Rational(after.NumberNumerator * exactWin.NumberDenominator - exactWin.NumberNumerator * after.NumberDenominator, after.NumberDenominator * exactWin.NumberDenominator));
                exactWin = after;
            }
            {
                var scaled = exactWin.NumberNumerator * scale;
                if (scaled % exactWin.NumberDenominator != 0)
                    throw new ExpressionEvaluationException("EVAL_PAYOUT_PRECISION", "State payout exceeds the declared settlement/paytable precision.", sink.WinStateKey);
                win = scaled / exactWin.NumberDenominator;
            }
            frame.RawPayout = win;
            return win.Sign < 0 ? throw new InvalidOperationException("Round payouts must be non-negative.") : BigInteger.Min(win, _cap);
        };
        _defaults = new SamplingCell[_layout.Count];
        foreach (var (index, cell) in defaults) _defaults[index] = cell;
    }

    internal SamplingPlan Rent() =>
        Interlocked.CompareExchange(ref _leased, 1, 0) == 0 ? this : _idle.TryTake(out var replica) ? replica : new(_config, _scale, _measurements, _constants, _shapes);

    internal void Return(SamplingPlan instance)
    {
        if (ReferenceEquals(instance, this)) Volatile.Write(ref _leased, 0);
        else if (_idle.Count < Environment.ProcessorCount) _idle.Add(instance);
    }
    public ISamplingRunner<Dictionary<string, object?>, BigInteger> CreateRunner(Dictionary<string, object?> initial, MeasurementCollector? measurements = null, string[]? persistentKeys = null, SlotMath.Core.Math.LoopTerminationEvidence? loopEvidence = null)
    {
        // Constants assume the graph's own initial state. A runner that starts
        // from a different value of one uses a plan without that assumption.
        var plan = _constants.Count != 0 && initial.Keys.Any(_constants.ContainsKey)
            ? LazyInitializer.EnsureInitialized(ref _general, () => new(_config, _scale, _measurements, NoConstants, _shapes))
            : this;
        return new Runner(plan, initial, measurements, persistentKeys, loopEvidence);
    }

    private sealed class Runner(SamplingPlan plan, Dictionary<string, object?> initial, MeasurementCollector? measurements, string[]? persistentKeys, SlotMath.Core.Math.LoopTerminationEvidence? loopEvidence) : ISamplingRunner<Dictionary<string, object?>, BigInteger>
    {
        private readonly SamplingFrame _frame = new(plan._layout, plan._defaults, initial);
        private readonly bool[]? _retained = persistentKeys is { Length: > 0 } ? plan._layout.OrderBy(p => p.Value).Select(p => persistentKeys.Contains(p.Key, StringComparer.Ordinal)).ToArray() : null;
        private bool _started;
        public void ResetTrajectory() => _started = false;
        public double? RawPayout => (double)_frame.RawPayout / (double)plan._scale;
        public void ObserveRound(double payout)
        {
            if (measurements is null) return;
            for (var i = 0; i < plan._measurements.Count; i++)
            {
                _frame.Settlement = new(payout, RawPayout, plan._measurements[i].Options?.Stake ?? 1);
                var binding = plan._measurementExpressions[i];
                measurements.CompleteRound(i, _frame, binding, payout, RawPayout);
            }
            _frame.Settlement = null;
        }
        public BigInteger Run(SeededRandom rng, CancellationToken token) { _frame.Reset(token, _started ? _retained : null); _started = true; _frame.Measurements = measurements; _frame.LoopEvidence = loopEvidence; return plan._run(_frame, rng); }
        public Dictionary<string, object?> ExportState() => _frame.Export();
    }

    private Func<SamplingFrame, ExprValue>? CompileMeasurement(Expression? expression) => expression is null ? null : _expressions.Compile(expression);

    // The slot of a field that is assigned at run time.
    private int WriteSlot(string key) => _constants.ContainsKey(key) ? throw new ConstantStateConflict(key) : SlotIndex(key);

    private int SlotIndex(string key)
    {
        if (_layout.TryGetValue(key, out var index)) return index;
        index = _layout.Count; _layout.Add(key, index); return index;
    }

    private Func<SamplingFrame, ExprValue>? Expression(string? id) => id is not null && _config.Expressions!.TryGetValue(id, out var e) ? _expressions.Compile(e) : null;
    private SamplingDelegate<bool>? Condition(string? id) => id is not null && _config.Expressions!.TryGetValue(id, out var e) ? _expressions.BindCondition(e) : null;
    private bool Observed(string id) => _measurements.Any(d => d.NodeId == id || d.Options?.EntryNodeId == id || d.Options?.ExitNodeId == id);

    private Step StepFor(Node node)
    {
        if (node is not ModifyStateNode modify) return new(null, -1, false, null, CompileOutput(node));
        if (modify.ExpressionId is null || !_config.Expressions!.TryGetValue(modify.ExpressionId, out var e)) return default;
        var value = _expressions.Bind(e); var output = WriteSlot(modify.OutputKey ?? "__modified__");
        return _expressions.Reduce(e) is FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } field && field.Path[0] == modify.OutputKey
            ? new(null, output, false, field.Path[0], null)
            : new(value, output, modify.OutputKey is null, null, null);
    }

    private Chain CompileChain(string id, string? sink)
    {
        if (id == sink) return ObserveChain(id, (_, _, input) => input);
        if (_chains.TryGetValue((id, sink), out var existing)) return existing;
        var node = _nodes[id]; var edges = _outgoing[id];
        Chain chain;
        if (node is BranchNode branch && edges.Any(e => e.SourcePort is "true" or "false"))
        {
            var test = Condition(branch.ConditionId);
            var yesEdge = edges.FirstOrDefault(e => e.SourcePort == "true"); var noEdge = edges.FirstOrDefault(e => e.SourcePort == "false");
            var yes = yesEdge is null ? null : CompileChain(yesEdge.TargetNodeId, sink);
            var no = noEdge is null ? null : CompileChain(noEdge.TargetNodeId, sink);
            chain = (s, rng, input) =>
            {
                s.CheckCancellation(); var next = test is not null && test.Invoke(s) ? yes : no;
                return next is null ? BigInteger.Zero : next(s, rng, input);
            };
        }
        else if (node is LoopNode loop)
        {
            var body = CompileChain(edges.First(e => e.SourcePort == "body").TargetNodeId, null);
            var exitEdge = edges.FirstOrDefault(e => e.SourcePort is "exit" or "out");
            var exit = exitEdge is null ? null : CompileChain(exitEdge.TargetNodeId, sink);
            var stop = Condition(loop.StopConditionId);
            var reasonExpression = loop.ExitReason is null ? null : _expressions.Compile(loop.ExitReason);
            var reasonSlot = WriteSlot($"__exitReason_{loop.Id}__");
            var counter = WriteSlot($"__iter_{loop.Id}__"); var wins = WriteSlot($"__wins_{loop.Id}__");
            chain = (s, rng, _) =>
            {
                s.Cells[counter] = SamplingCell.FromRaw(0); s.Cells[wins] = SamplingCell.Typed(ExprValue.Number(0));
                while (true)
                {
                    s.CheckCancellation();
                    var iteration = s.Cells[counter].HasRaw && s.Cells[counter].Raw is int i ? i : 0;
                    if (iteration >= loop.MaxIterations || stop is not null && stop.Invoke(s)) break;
                    var result = body(s, rng, null);
                    iteration = s.Cells[counter].HasRaw && s.Cells[counter].Raw is int j ? j : 0;
                    s.Cells[counter] = SamplingCell.FromRaw(iteration + 1);
                    if (result is BigInteger or Win[])
                    {
                        var amount = Extract(result);
                        var accumulated = s.Cells[wins];
                        var previous = accumulated.Present && (!accumulated.HasRaw && accumulated.Value.Kind == ExprType.Number && accumulated.Value.NumberDenominator.IsOne || accumulated.HasRaw && accumulated.Raw is BigInteger) ? accumulated.Value.AsInteger() : 0;
                        s.Cells[wins] = SamplingCell.Typed(ExprValue.Number(previous + amount));
                    }
                }
                var iterations = (int)s.Cells[counter].Raw!;
                var reason = iterations >= loop.MaxIterations ? "modelLimit" : reasonExpression is null ? "condition" : SlotMath.Core.Measurements.ExitClassification.Read(reasonExpression(s));
                s.Cells[reasonSlot] = SamplingCell.Typed(ExprValue.String(reason));
                s.LoopEvidence?.Observe(loop.Id, iterations, loop.MaxIterations, reason);
                var total = s.Cells[wins].Export();
                return exit is null ? total : exit(s, rng, total);
            };
        }
        else if (node is ModifyStateNode or DrawNode && edges.Length == 1)
        {
            // A straight run of updates and draws executes as one loop, not as
            // one nested call per node: a spin is a run of a few hundred nodes.
            var run = new List<Step>(); var current = node; string target;
            while (true)
            {
                run.Add(StepFor(current));
                target = _outgoing[current.Id][0].TargetNodeId;
                if (target == sink || Observed(target) || _nodes[target] is not (ModifyStateNode or DrawNode) || _outgoing[target].Length != 1) break;
                current = _nodes[target];
            }
            var steps = run.ToArray(); var tail = CompileChain(target, sink);
            chain = (s, rng, input) =>
            {
                foreach (ref readonly var step in steps.AsSpan())
                {
                    s.CheckCancellation();
                    if (step.Value is { } value)
                    {
                        var result = value.Invoke(s);
                        s.Cells[step.Output] = SamplingCell.Typed(step.Integer ? ExprValue.Number(result.AsInteger()) : result);
                    }
                    else if (step.Self is { } field) s.Touch(step.Output, field);
                    else if (step.Draw is { } draw) input = draw(s, rng, input);
                }
                return tail(s, rng, input);
            };
        }
        else
        {
            var output = CompileOutput(node);
            var next = edges.Select(e => CompileChain(e.TargetNodeId, sink)).ToArray();
            var passInput = node is GetStateNode or PutStateNode or ModifyStateNode;
            chain = (s, rng, input) =>
            {
                s.CheckCancellation(); var result = output(s, rng, input);
                if (next.Length == 0) return result;
                var value = passInput ? input : result;
                if (next.Length == 1) return next[0](s, rng, value);
                var total = BigInteger.Zero;
                foreach (var target in next) total += Extract(target(s, rng, value));
                return total;
            };
        }
        chain = ObserveChain(id, chain);
        _chains[(id, sink)] = chain; return chain;
    }

    private Chain ObserveChain(string id, Chain inner)
    {
        var indexes = _measurements.Select((d, i) => (d, i)).Where(p => p.d.NodeId == id || p.d.Options?.EntryNodeId == id || p.d.Options?.ExitNodeId == id).Select(p => p.i).ToArray();
        if (indexes.Length == 0) return inner;
        return (frame, rng, input) =>
        {
            if (frame.Measurements is { } collector)
                foreach (var index in indexes)
                {
                    collector.Point(index, id, frame, _measurementExpressions[index]);
                }
            return inner(frame, rng, input);
        };
    }

    private Chain CompileOutput(Node node)
    {
        switch (node)
        {
            case DrawNode draw:
                var weights = draw.DrawWeights!;
                var alias = WeightSet.FromIntegers(weights.Select(w => w.Weight).ToArray()).AliasTable;
                var values = weights.Select(w => (object)(new BigInteger(w.Value) * _scale)).ToArray();
                var names = weights.Select(w => SamplingCell.Typed(ExprValue.String(w.OutcomeId))).ToArray();
                foreach (var name in names) _expressions.Remember(name.Value);
                var destination = draw.StateWriteKey is null ? -1 : WriteSlot(draw.StateWriteKey);
                return (s, rng, _) => { var index = alias.Sample(rng); if (destination >= 0) s.Cells[destination] = names[index]; return values[index]; };
            case ModifyStateNode modify:
                var expression = Expression(modify.ExpressionId);
                if (expression is null) return (_, _, _) => null;
                var output = WriteSlot(modify.OutputKey ?? "__modified__");
                return (s, _, _) => { var value = expression(s); s.Cells[output] = SamplingCell.Typed(modify.OutputKey is null ? ExprValue.Number(value.AsInteger()) : value); return null; };
            case GetStateNode get:
                var getIndex = SlotIndex(get.StateKey ?? "__default__");
                return (s, _, _) => s.Cells[getIndex].Present ? s.Cells[getIndex].Export() : null;
            case PutStateNode put:
                var putIndex = WriteSlot(put.StateKey); return (s, _, input) => { s.Cells[putIndex] = SamplingCell.FromRaw(input); return input; };
            case DataNode data:
                var dataIndex = WriteSlot(data.StateKey);
                var raw = data.Values.Select(v => BigInteger.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? (object)n : v).ToArray();
                var cell = SamplingCell.FromRaw(raw); _expressions.Remember(cell.Value);
                return (s, _, input) => { s.Cells[dataIndex] = cell; return input; };
            case BranchNode branch:
                var test = Expression(branch.ConditionId);
                return (s, _, input) => test is null || SamplingExpressions.RequiredBoolean(test(s)) ? input : BigInteger.Zero;
            case MetricsSinkNode: return (_, _, input) => input;
            default: throw new NotSupportedException($"Sampling plan does not support {node.GetType().Name}.");
        }
    }

    private BigInteger Extract(object? value) => value switch
    {
        BigInteger n => n,
        decimal n => ScaleWin(n),
        Win[] wins => ScaleWin(wins.Sum(w => w.TotalWin)),
        _ => BigInteger.Zero,
    };
    private BigInteger ScaleWin(decimal value) => new(decimal.Round(value * (decimal)_scale, 0, MidpointRounding.ToEven));
}
