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
}

internal sealed class SamplingPlan
{
    private delegate object? Chain(SamplingFrame frame, SeededRandom rng, object? input);
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
        return new SamplingPlanSlot(reference, new SamplingPlan(config, scale, measurements));
    }

    private SamplingPlan(GraphConfig config, BigInteger scale, IReadOnlyList<MeasurementDefinition> measurements)
    {
        _config = config; _scale = scale; _measurements = measurements;
        _nodes = config.Nodes.ToDictionary(n => n.Id);
        _outgoing = config.Nodes.ToDictionary(n => n.Id, n => config.Edges.Where(e => e.SourceNodeId == n.Id).ToArray());
        _expressions = new SamplingExpressions(SlotIndex);
        for (var i = 0; i < measurements.Count; i++)
            _measurementExpressions[i] = new(CompileMeasurement(measurements[i].Value), CompileMeasurement(measurements[i].Filter),
                CompileMeasurement(measurements[i].Options?.Group), CompileMeasurement(measurements[i].Options?.Pair),
                CompileMeasurement(measurements[i].Options?.Weight), CompileMeasurement(measurements[i].Options?.AwardId),
                CompileMeasurement(measurements[i].Options?.EntryFilter), CompileMeasurement(measurements[i].Options?.ExitFilter));
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
        _run = (frame, rng) =>
        {
            var win = BigInteger.Zero;
            foreach (var entry in entries)
            {
                var result = entry(frame, rng, null);
                if (winSlot < 0) win += Extract(result);
            }
            if (winSlot >= 0)
            {
                var cell = frame.Cells[winSlot]; var key = sink.WinStateKey!;
                if (!cell.Present) throw new ExpressionEvaluationException("EVAL_MISSING_STATE", $"Payout field '{key}' is absent.", key);
                if (cell.Value.Kind != ExprType.Number || cell.HasRaw && cell.Raw is not (BigInteger or int or long or ExprValue))
                    throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", $"Payout field '{key}' must be numeric.", key);
                var scaled = cell.Value.NumberNumerator * scale;
                if (scaled % cell.Value.NumberDenominator != 0)
                    throw new ExpressionEvaluationException("EVAL_PAYOUT_PRECISION", "State payout exceeds the declared paytable precision.", key);
                win = scaled / cell.Value.NumberDenominator;
            }
            frame.RawPayout = win;
            return win.Sign < 0 ? throw new InvalidOperationException("Round payouts must be non-negative.") : BigInteger.Min(win, _cap);
        };
        _defaults = new SamplingCell[_layout.Count];
        foreach (var (index, cell) in defaults) _defaults[index] = cell;
    }

    public ISamplingRunner<Dictionary<string, object?>, BigInteger> CreateRunner(Dictionary<string, object?> initial, MeasurementCollector? measurements = null, string[]? persistentKeys = null, SlotMath.Core.Math.LoopTerminationEvidence? loopEvidence = null) => new Runner(this, initial, measurements, persistentKeys, loopEvidence);
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

    private int SlotIndex(string key)
    {
        if (_layout.TryGetValue(key, out var index)) return index;
        index = _layout.Count; _layout.Add(key, index); return index;
    }

    private Func<SamplingFrame, ExprValue>? Expression(string? id) => id is not null && _config.Expressions!.TryGetValue(id, out var e) ? _expressions.Compile(e) : null;

    private Chain CompileChain(string id, string? sink)
    {
        if (id == sink) return ObserveChain(id, (_, _, input) => input);
        if (_chains.TryGetValue((id, sink), out var existing)) return existing;
        var node = _nodes[id]; var edges = _outgoing[id];
        Chain chain;
        if (node is BranchNode branch && edges.Any(e => e.SourcePort is "true" or "false"))
        {
            var test = Expression(branch.ConditionId);
            var yesEdge = edges.FirstOrDefault(e => e.SourcePort == "true"); var noEdge = edges.FirstOrDefault(e => e.SourcePort == "false");
            var yes = yesEdge is null ? null : CompileChain(yesEdge.TargetNodeId, sink);
            var no = noEdge is null ? null : CompileChain(noEdge.TargetNodeId, sink);
            chain = (s, rng, input) =>
            {
                s.CheckCancellation(); var next = test is not null && SamplingExpressions.RequiredBoolean(test(s)) ? yes : no;
                return next is null ? BigInteger.Zero : next(s, rng, input);
            };
        }
        else if (node is LoopNode loop)
        {
            var body = CompileChain(edges.First(e => e.SourcePort == "body").TargetNodeId, null);
            var exitEdge = edges.FirstOrDefault(e => e.SourcePort is "exit" or "out");
            var exit = exitEdge is null ? null : CompileChain(exitEdge.TargetNodeId, sink);
            var stop = Expression(loop.StopConditionId);
            var counter = SlotIndex($"__iter_{loop.Id}__"); var wins = SlotIndex($"__wins_{loop.Id}__");
            chain = (s, rng, _) =>
            {
                s.Cells[counter] = SamplingCell.FromRaw(0); s.Cells[wins] = SamplingCell.Typed(ExprValue.Number(0));
                while (true)
                {
                    s.CheckCancellation();
                    var iteration = s.Cells[counter].HasRaw && s.Cells[counter].Raw is int i ? i : 0;
                    if (iteration >= loop.MaxIterations || stop is not null && SamplingExpressions.RequiredBoolean(stop(s))) break;
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
                s.LoopEvidence?.Observe(loop.Id, (int)s.Cells[counter].Raw!, loop.MaxIterations);
                var total = s.Cells[wins].Export();
                return exit is null ? total : exit(s, rng, total);
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
                var destination = draw.StateWriteKey is null ? -1 : SlotIndex(draw.StateWriteKey);
                return (s, rng, _) => { var index = alias.Sample(rng); if (destination >= 0) s.Cells[destination] = names[index]; return values[index]; };
            case ModifyStateNode modify:
                var expression = Expression(modify.ExpressionId);
                if (expression is null) return (_, _, _) => null;
                var output = SlotIndex(modify.OutputKey ?? "__modified__");
                return (s, _, _) => { var value = expression(s); s.Cells[output] = SamplingCell.Typed(modify.OutputKey is null ? ExprValue.Number(value.AsInteger()) : value); return null; };
            case GetStateNode get:
                var getIndex = SlotIndex(get.StateKey ?? "__default__");
                return (s, _, _) => s.Cells[getIndex].Present ? s.Cells[getIndex].Export() : null;
            case PutStateNode put:
                var putIndex = SlotIndex(put.StateKey); return (s, _, input) => { s.Cells[putIndex] = SamplingCell.FromRaw(input); return input; };
            case DataNode data:
                var dataIndex = SlotIndex(data.StateKey);
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
