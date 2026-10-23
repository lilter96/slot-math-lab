using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Optimization;
using SlotMath.Core.Random;

namespace SlotMath.Core.Compiler;

internal interface ICompiledSampling<S, T>
{
    ISamplingRunner<S, T> CreateRunner(S initial, MeasurementCollector? measurements = null,
        string[]? persistentKeys = null, LoopTerminationEvidence? loopEvidence = null);

    /// <summary>
    ///     An instance of this program for one worker's exclusive use until
    ///     <see cref="Return" />. Instances are interchangeable: which one runs a
    ///     chunk never affects its samples.
    /// </summary>
    ICompiledSampling<S, T> Rent() => this;

    void Return(ICompiledSampling<S, T> instance)
    {
    }
}

internal interface ISamplingRunner<S, T>
{
    double? RawPayout => null;
    T Run(SeededRandom rng, CancellationToken token);
    S ExportState();

    void ObserveRound(double payout)
    {
    }

    void ResetTrajectory()
    {
    }
}

// Exact interpreters and program analysis unwrap this annotation. Sampled
// execution recognizes the plan at its root; composing a Slot above it safely
// falls back to the reference program, with ordinary monadic semantics.
internal sealed class SamplingPlanSlot(Slot<Dictionary<string, object?>, BigInteger> reference, SamplingPlan plan)
    : Slot<Dictionary<string, object?>, BigInteger>, IAnnotationNode,
        ICompiledSampling<Dictionary<string, object?>, BigInteger>
{
    object IAnnotationNode.InnerUntyped => reference;

    public ISamplingRunner<Dictionary<string, object?>, BigInteger> CreateRunner(Dictionary<string, object?> initial,
        MeasurementCollector? measurements = null, string[]? persistentKeys = null,
        LoopTerminationEvidence? loopEvidence = null) =>
        plan.CreateRunner(initial, measurements, persistentKeys, loopEvidence);

    public ICompiledSampling<Dictionary<string, object?>, BigInteger> Rent() => plan.Rent();

    public void Return(ICompiledSampling<Dictionary<string, object?>, BigInteger> instance) =>
        plan.Return((SamplingPlan)instance);
}

internal sealed class SamplingPlan : ICompiledSampling<Dictionary<string, object?>, BigInteger>
{
    private static readonly Dictionary<string, ExprValue> NoConstants = new();
    private readonly BigInteger _cap;
    private readonly Dictionary<(string, string?), Chain> _chains = new();
    private readonly GraphConfig _config;
    private readonly Dictionary<string, ExprValue> _constants;
    private readonly SamplingCell[] _defaults;

    private readonly SamplingExpressions _expressions;

    // A plan is immutable while it runs, but workers that read one instance
    // from several cores measurably slow each other down. Each concurrent
    // worker therefore leases its own replica, built from the same inputs.
    private readonly ConcurrentBag<SamplingPlan> _idle = new();
    private readonly Dictionary<string, int> _layout = new(StringComparer.Ordinal);
    private readonly IReadOnlySet<string> _maskFields;
    private readonly Dictionary<int, MeasurementBinding<SamplingFrame>> _measurementExpressions = new();
    private readonly IReadOnlyList<MeasurementDefinition> _measurements;
    private readonly Dictionary<string, Node> _nodes;
    private readonly Dictionary<string, Edge[]> _outgoing;
    private readonly Func<SamplingFrame, SeededRandom, BigInteger> _run;
    private readonly BigInteger _scale;
    private readonly SamplingShapes _shapes;
    private readonly bool _simplifyExpressions;
    private readonly GraphConfig _sourceConfig;
    private SamplingPlan? _general;
    private int _leased;
    private SamplingPlan? _withoutMasks;

    private SamplingPlan(GraphConfig config, BigInteger scale, IReadOnlyList<MeasurementDefinition> measurements,
        Dictionary<string, ExprValue> constants, SamplingShapes shapes, IReadOnlySet<string> maskFields,
        bool simplifyExpressions = true)
    {
        _sourceConfig = config;
        _simplifyExpressions = simplifyExpressions;
        if (simplifyExpressions)
        {
            config = GraphSimplifier.Specialize(config, constants, maskFields).Config;
        }

        _config = config;
        _scale = scale;
        _measurements = measurements;
        _constants = constants;
        _shapes = shapes;
        _maskFields = maskFields;
        _nodes = config.Nodes.ToDictionary(n => n.Id);
        _outgoing = config.Nodes.ToDictionary(n => n.Id,
            n => config.Edges.Where(e => e.SourceNodeId == n.Id).ToArray());
        _expressions = new SamplingExpressions(SlotIndex, constants: constants, shapes: shapes, maskFields: maskFields,
            inlineMaskFields: SamplingMaskState.InferInline(config, maskFields));
        for (var i = 0; i < measurements.Count; i++)
        {
            _measurementExpressions[i] = new MeasurementBinding<SamplingFrame>(
                CompileMeasurement(measurements[i].Value), CompileMeasurement(measurements[i].Filter),
                CompileMeasurement(measurements[i].Options?.Group), CompileMeasurement(measurements[i].Options?.Pair),
                CompileMeasurement(measurements[i].Options?.Weight),
                CompileMeasurement(measurements[i].Options?.AwardId),
                CompileMeasurement(measurements[i].Options?.EntryFilter),
                CompileMeasurement(measurements[i].Options?.ExitFilter),
                CompileMeasurement(measurements[i].Options?.ExitReason));
        }

        var defaults = new Dictionary<int, SamplingCell>();
        foreach ((var key, JsonElement value) in config.InitialState ?? new Dictionary<string, JsonElement>())
        {
            SamplingCell cell = SamplingCell.FromRaw(InitialStateValues.Materialize(value));
            defaults[SlotIndex(key)] = cell;
            if (cell.TryCachedValue(out ExprValue valueToRemember))
            {
                _expressions.Remember(valueToRemember);
            }
        }

        MetricsSinkNode sink = config.Nodes.OfType<MetricsSinkNode>().Single();
        _cap = new BigInteger(sink.WinCap!.Value) * scale;
        HashSet<string> incoming = config.Edges.Select(e => e.TargetNodeId).ToHashSet();
        Chain[] entries = config.Nodes.Where(n => !incoming.Contains(n.Id)).Select(n => CompileChain(n.Id, sink.Id))
            .ToArray();
        var winSlot = sink.WinStateKey is { } keyName ? SlotIndex(keyName) : -1;
        var settlementSlots =
            sink.Settlement is null ? [] : MonetarySettlement.EvidenceKeys.Select(WriteSlot).ToArray();
        _run = (frame, rng) =>
        {
            BigInteger win = BigInteger.Zero;
            foreach (Chain entry in entries)
            {
                var result = entry(frame, rng, null);
                if (winSlot < 0)
                {
                    win += Extract(result);
                }
            }

            ExprValue exactWin = ExprValue.Rational(win, scale);
            if (winSlot >= 0)
            {
                SamplingCell cell = frame.Cells[winSlot];
                var key = sink.WinStateKey!;
                if (!cell.Present)
                {
                    throw new ExpressionEvaluationException("EVAL_MISSING_STATE", $"Payout field '{key}' is absent.",
                        key);
                }

                if (cell.Value.Kind != ExprType.Number ||
                    (cell.HasRaw && cell.Raw is not (BigInteger or int or long or ExprValue)))
                {
                    throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", $"Payout field '{key}' must be numeric.",
                        key);
                }

                exactWin = cell.Value;
            }

            if (sink.Settlement is { } policy)
            {
                ExprValue after = policy.Apply(exactWin);
                frame.Cells[settlementSlots[0]] = SamplingCell.FromRaw(exactWin);
                frame.Cells[settlementSlots[1]] = SamplingCell.FromRaw(after);
                frame.Cells[settlementSlots[2]] = SamplingCell.FromRaw(ExprValue.Rational(
                    after.NumberNumerator * exactWin.NumberDenominator -
                    exactWin.NumberNumerator * after.NumberDenominator,
                    after.NumberDenominator * exactWin.NumberDenominator));
                exactWin = after;
            }

            {
                BigInteger scaled = exactWin.NumberNumerator * scale;
                if (scaled % exactWin.NumberDenominator != 0)
                {
                    throw new ExpressionEvaluationException("EVAL_PAYOUT_PRECISION",
                        "State payout exceeds the declared settlement/paytable precision.", sink.WinStateKey);
                }

                win = scaled / exactWin.NumberDenominator;
            }
            frame.RawPayout = win;
            return win.Sign < 0
                ? throw new InvalidOperationException("Round payouts must be non-negative.")
                : BigInteger.Min(win, _cap);
        };
        _defaults = new SamplingCell[_layout.Count];
        foreach ((var index, SamplingCell cell) in defaults)
        {
            _defaults[index] = cell;
        }
    }

    public ISamplingRunner<Dictionary<string, object?>, BigInteger> CreateRunner(Dictionary<string, object?> initial,
        MeasurementCollector? measurements = null, string[]? persistentKeys = null,
        LoopTerminationEvidence? loopEvidence = null)
    {
        // Constants assume the graph's own initial state. A runner that starts
        // from a different value of one uses a plan without that assumption.
        SamplingPlan plan = initial.Keys.Any(_constants.ContainsKey)
            ? LazyInitializer.EnsureInitialized(ref _general,
                () => new SamplingPlan(_sourceConfig, _scale, _measurements, NoConstants, _shapes,
                    SamplingMaskState.None, _simplifyExpressions))
            : initial.Keys.Any(_maskFields.Contains)
                ? LazyInitializer.EnsureInitialized(ref _withoutMasks,
                    () => new SamplingPlan(_sourceConfig, _scale, _measurements, _constants, _shapes,
                        SamplingMaskState.None, _simplifyExpressions))
                : this;
        return new Runner(plan, initial, measurements, persistentKeys, loopEvidence);
    }

    public static Slot<Dictionary<string, object?>, BigInteger> Wrap(GraphConfig config, BigInteger scale,
        Slot<Dictionary<string, object?>, BigInteger> reference, IReadOnlyList<MeasurementDefinition> measurements,
        bool simplifyExpressions = true)
    {
        // Unsupported operators use the general interpreter. Eligibility is
        // checked before compilation, rather than silently catching failures.
        if (config.Plugins.Length != 0 || config.Nodes.Any(n => n switch
            {
                DrawNode d => d.BoundDistributions is null
                              && (d.DrawWeights is not { Length: > 0 } || d.WeightExpressionId is not null),
                MapNode => true,
                LoopNode l => !config.Edges.Any(e => e.SourceNodeId == l.Id && e.SourcePort == "body"),
                LibraryNode => true,
                _ => false
            }))
        {
            return reference;
        }

        SamplingPlan plan;
        IReadOnlySet<string> maskFields = SamplingMaskState.Infer(config, measurements);
        try
        {
            plan = new SamplingPlan(config, scale, measurements, ConstantState(config, measurements),
                new SamplingShapes(), maskFields, simplifyExpressions);
        }
        catch (ConstantStateConflict)
        {
            plan = new SamplingPlan(config, scale, measurements, NoConstants, new SamplingShapes(), maskFields,
                simplifyExpressions);
        }

        return new SamplingPlanSlot(reference, plan);
    }

    // Initial state that no node, loop, settlement or iteration variable can
    // assign. Such a field holds its initial value for every round of a runner
    // whose own initial state does not override it.
    private static Dictionary<string, ExprValue> ConstantState(GraphConfig config,
        IReadOnlyList<MeasurementDefinition> measurements)
    {
        var written =
            new HashSet<string>(MonetarySettlement.EvidenceKeys, StringComparer.Ordinal)
            {
                "__modified__",
                "__default__"
            };
        var expressions = new List<Expression?>();
        if (config.Expressions is { } shared)
        {
            expressions.AddRange(shared.Values);
        }

        foreach (Node node in config.Nodes)
        {
            switch (node)
            {
                case ModifyStateNode { OutputKey: { } key }: written.Add(key); break;
                case DrawNode { StateWriteKey: { } key }: written.Add(key); break;
                case PutStateNode put: written.Add(put.StateKey); break;
                case DataNode data: written.Add(data.StateKey); break;
                case LoopNode loop:
                    written.Add($"__exitReason_{loop.Id}__");
                    written.Add($"__iter_{loop.Id}__");
                    written.Add($"__wins_{loop.Id}__");
                    expressions.Add(loop.ExitReason);
                    break;
            }
        }

        foreach (MeasurementDefinition d in measurements)
        {
            expressions.AddRange([
                d.Value, d.Filter, d.Options?.Group, d.Options?.Pair, d.Options?.Weight, d.Options?.AwardId,
                d.Options?.EntryFilter, d.Options?.ExitFilter, d.Options?.ExitReason
            ]);
        }

        if (!expressions.All(e => Variables(e, written)))
        {
            return NoConstants;
        }

        var constants = new Dictionary<string, ExprValue>(StringComparer.Ordinal);
        foreach ((var key, JsonElement value) in config.InitialState ?? new Dictionary<string, JsonElement>())
        {
            if (written.Contains(key))
            {
                continue;
            }

            SamplingCell cell = SamplingCell.FromRaw(InitialStateValues.Materialize(value));
            if (cell.Readable)
            {
                constants[key] = cell.StoredValue;
            }
        }

        return constants;
    }

    // Collects the names an expression assigns while iterating. False for a form this analysis does not know.
    internal static bool Variables(Expression? expression, HashSet<string> names)
    {
        bool Named(params string?[] bound)
        {
            foreach (var name in bound)
            {
                if (name is not null)
                {
                    names.Add(name);
                }
            }

            return true;
        }

        return expression switch
        {
            null or ConstantExpr or FieldAccessExpr => true,
            NotExpr e => Variables(e.Expr, names),
            BinaryExpr e => Variables(e.Left, names) && Variables(e.Right, names),
            CompareExpr e => Variables(e.Left, names) && Variables(e.Right, names),
            IfExpr e => Variables(e.Condition, names) && Variables(e.ThenExpr, names) && Variables(e.ElseExpr, names),
            CallExpr e => e.Args.All(a => Variables(a, names)),
            BitSetExpr e => e.Args.All(a => Variables(a, names)),
            AggregateExpr e => Named(e.ItemName) && Variables(e.Predicate, names) && Variables(e.ValueExpr, names),
            MapExpr e => Named(e.ItemName, e.IndexName) && Variables(e.Body, names),
            FilterExpr e => Named(e.ItemName, e.IndexName) && Variables(e.Predicate, names),
            FoldExpr e => Named(e.AccName, e.ItemName, e.IndexName) && Variables(e.Init, names) &&
                          Variables(e.Body, names),
            _ => false
        };
    }

    internal SamplingPlan Rent() =>
        Interlocked.CompareExchange(ref _leased, 1, 0) == 0 ? this :
        _idle.TryTake(out SamplingPlan? replica) ? replica :
        new SamplingPlan(_sourceConfig, _scale, _measurements, _constants, _shapes, _maskFields, _simplifyExpressions);

    internal void Return(SamplingPlan instance)
    {
        if (ReferenceEquals(instance, this))
        {
            Volatile.Write(ref _leased, 0);
        }
        else if (_idle.Count < Environment.ProcessorCount)
        {
            _idle.Add(instance);
        }
    }

    private Func<SamplingFrame, ExprValue>? CompileMeasurement(Expression? expression) =>
        expression is null ? null : _expressions.Compile(expression);

    // The slot of a field that is assigned at run time.
    private int WriteSlot(string key) =>
        _constants.ContainsKey(key) ? throw new ConstantStateConflict(key) : SlotIndex(key);

    private int SlotIndex(string key)
    {
        if (_layout.TryGetValue(key, out var index))
        {
            return index;
        }

        index = _layout.Count;
        _layout.Add(key, index);
        return index;
    }

    private Func<SamplingFrame, ExprValue>? Expression(string? id) =>
        id is not null && _config.Expressions!.TryGetValue(id, out Expression? e) ? _expressions.Compile(e) : null;

    private SamplingDelegate<bool>? Condition(string? id) =>
        id is not null && _config.Expressions!.TryGetValue(id, out Expression? e)
            ? _expressions.BindCondition(e)
            : null;

    private bool Observed(string id) =>
        _measurements.Any(d => d.NodeId == id || d.Options?.EntryNodeId == id || d.Options?.ExitNodeId == id);

    private Step StepFor(Node node)
    {
        if (node is not ModifyStateNode modify)
        {
            return new Step(null, -1, null, CompileOutput(node));
        }

        if (modify.ExpressionId is null || !_config.Expressions!.TryGetValue(modify.ExpressionId, out Expression? e))
        {
            return default;
        }

        var output = WriteSlot(modify.OutputKey ?? "__modified__");
        return _expressions.Reduce(e) is FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } field &&
               field.Path[0] == modify.OutputKey
            ? new Step(null, output, field.Path[0], null)
            : new Step(
                _expressions.BindWrite(e, output, modify.OutputKey is null,
                    modify.OutputKey is { } key && _expressions.IsInlineMaskField(key)), output, null, null);
    }

    private static bool StraightNode(Node node) => node is ModifyStateNode
        or DrawNode { BoundDistributions: null };

    private Chain CompileChain(string id, string? sink)
    {
        if (id == sink)
        {
            return ObserveChain(id, (_, _, input) => input);
        }

        if (_chains.TryGetValue((id, sink), out Chain? existing))
        {
            return existing;
        }

        Node node = _nodes[id];
        Edge[] edges = _outgoing[id];
        Chain chain;
        if (node is BranchNode branch && edges.Any(e => e.SourcePort is "true" or "false"))
        {
            Edge? yesEdge = edges.FirstOrDefault(e => e.SourcePort == "true");
            Edge? noEdge = edges.FirstOrDefault(e => e.SourcePort == "false");
            if (branch.ConditionId is { } conditionId && _config.Expressions!.TryGetValue(conditionId,
                                                          out Expression? condition)
                                                      && _expressions.TryConstantBool(condition, out var decided))
            {
                // An immutable condition needs neither the unused chain nor its
                // temporary frame slots. Overrides use the existing general plan.
                Edge? edge = decided ? yesEdge : noEdge;
                Chain? selected = edge is null ? null : CompileChain(edge.TargetNodeId, sink);
                chain = (s, rng, input) =>
                {
                    s.CheckCancellation();
                    return selected is null ? BigInteger.Zero : selected(s, rng, input);
                };
            }
            else
            {
                SamplingDelegate<bool>? test = Condition(branch.ConditionId);
                Chain? yes = yesEdge is null ? null : CompileChain(yesEdge.TargetNodeId, sink);
                Chain? no = noEdge is null ? null : CompileChain(noEdge.TargetNodeId, sink);
                chain = (s, rng, input) =>
                {
                    s.CheckCancellation();
                    Chain? next = test is not null && test.Invoke(s) ? yes : no;
                    return next is null ? BigInteger.Zero : next(s, rng, input);
                };
            }
        }
        else if (node is LoopNode loop)
        {
            Chain body = CompileChain(edges.First(e => e.SourcePort == "body").TargetNodeId, null);
            Edge? exitEdge = edges.FirstOrDefault(e => e.SourcePort is "exit" or "out");
            Chain? exit = exitEdge is null ? null : CompileChain(exitEdge.TargetNodeId, sink);
            SamplingDelegate<bool>? stop = Condition(loop.StopConditionId);
            Func<SamplingFrame, ExprValue>? reasonExpression =
                loop.ExitReason is null ? null : _expressions.Compile(loop.ExitReason);
            var reasonSlot = WriteSlot($"__exitReason_{loop.Id}__");
            var evidenceKey = new LoopTerminationEvidence.Key(loop.Id);
            var counter = WriteSlot($"__iter_{loop.Id}__");
            var wins = WriteSlot($"__wins_{loop.Id}__");
            chain = (s, rng, _) =>
            {
                s.Cells[counter] = SamplingCell.FromInt(0);
                s.Cells[wins].SetTyped(ExprValue.Number(0));
                while (true)
                {
                    s.CheckCancellation();
                    var iteration = s.Cells[counter].HasRaw && s.Cells[counter].Raw is int i ? i : 0;
                    if (iteration >= loop.MaxIterations || (stop is not null && stop.Invoke(s)))
                    {
                        break;
                    }

                    var result = body(s, rng, null);
                    iteration = s.Cells[counter].HasRaw && s.Cells[counter].Raw is int j ? j : 0;
                    s.Cells[counter] = SamplingCell.FromInt(iteration + 1);
                    if (result is BigInteger or Win[])
                    {
                        BigInteger amount = Extract(result);
                        SamplingCell accumulated = s.Cells[wins];
                        BigInteger previous =
                            accumulated.Present &&
                            ((!accumulated.HasRaw && accumulated.Value.Kind == ExprType.Number &&
                              accumulated.Value.NumberDenominator.IsOne) ||
                             (accumulated.HasRaw && accumulated.Raw is BigInteger))
                                ? accumulated.Value.AsInteger()
                                : 0;
                        s.Cells[wins].SetTyped(ExprValue.Number(previous + amount));
                    }
                }

                var iterations = (int)s.Cells[counter].Raw!;
                var reason = iterations >= loop.MaxIterations ? "modelLimit" :
                    reasonExpression is null ? "condition" : ExitClassification.Read(reasonExpression(s));
                s.Cells[reasonSlot].SetTyped(ExprValue.String(reason));
                s.LoopEvidence?.Observe(evidenceKey, iterations, loop.MaxIterations, reason);
                var total = s.Cells[wins].Export();
                return exit is null ? total : exit(s, rng, total);
            };
        }
        else if (StraightNode(node) && edges.Length <= 1)
        {
            // A straight run of updates and draws executes as one loop, not as
            // one nested call per node: a spin is a run of a few hundred nodes.
            var run = new List<Step>();
            var runNodes = new List<Node>();
            Node current = node;
            string? target;
            while (true)
            {
                runNodes.Add(current);
                run.Add(StepFor(current));
                if (_outgoing[current.Id].Length == 0)
                {
                    target = null;
                    break;
                }

                target = _outgoing[current.Id][0].TargetNodeId;
                if (target == sink || Observed(target) || !StraightNode(_nodes[target]) ||
                    _outgoing[target].Length > 1)
                {
                    break;
                }

                current = _nodes[target];
            }

            Step[] steps = run.ToArray();
            Chain? tail = target is null ? null : CompileChain(target, sink);
            var discardOutput = tail is null && current is ModifyStateNode;

            object? Interpreted(SamplingFrame s, SeededRandom rng, object? input)
            {
                foreach (ref readonly Step step in steps.AsSpan())
                {
                    s.CheckCancellation();
                    if (step.Write is { } write)
                    {
                        write.Invoke(s);
                    }
                    else if (step.Self is { } field)
                    {
                        s.Touch(step.Output, field);
                    }
                    else if (step.Draw is { } draw)
                    {
                        input = draw(s, rng, input);
                    }
                }

                return discardOutput ? null : input;
            }

            SamplingRunDelegate native =
                _expressions.BindRun(runNodes, _config.Expressions!, _scale, Interpreted, discardOutput);
            chain = tail is null ? native.Invoke : (s, rng, input) => tail(s, rng, native.Invoke(s, rng, input));
        }
        else
        {
            Chain output = CompileOutput(node);
            Chain[] next = edges.Select(e => CompileChain(e.TargetNodeId, sink)).ToArray();
            var passInput = node is GetStateNode or PutStateNode or ModifyStateNode;
            chain = (s, rng, input) =>
            {
                s.CheckCancellation();
                var result = output(s, rng, input);
                if (next.Length == 0)
                {
                    return result;
                }

                var value = passInput ? input : result;
                if (next.Length == 1)
                {
                    return next[0](s, rng, value);
                }

                BigInteger total = BigInteger.Zero;
                foreach (Chain target in next)
                {
                    total += Extract(target(s, rng, value));
                }

                return total;
            };
        }

        chain = ObserveChain(id, chain);
        _chains[(id, sink)] = chain;
        return chain;
    }

    private Chain ObserveChain(string id, Chain inner)
    {
        var indexes = _measurements.Select((d, i) => (d, i))
            .Where(p => p.d.NodeId == id || p.d.Options?.EntryNodeId == id || p.d.Options?.ExitNodeId == id)
            .Select(p => p.i).ToArray();
        if (indexes.Length == 0)
        {
            return inner;
        }

        return (frame, rng, input) =>
        {
            if (frame.Measurements is { } collector)
            {
                foreach (var index in indexes)
                {
                    collector.Point(index, id, frame, _measurementExpressions[index]);
                }
            }

            return inner(frame, rng, input);
        };
    }

    private Chain CompileOutput(Node node)
    {
        switch (node)
        {
            case DrawNode { BoundDistributions: { } family } draw:
            {
                SamplingDraw[] choices = family.Select(row => _shapes.BindDraw(row, _scale)).ToArray();
                foreach (SamplingDraw choice in choices)
                foreach (ExprValue value in choice.StateValues)
                {
                    _expressions.Remember(value);
                }

                Func<SamplingFrame, ExprValue> selector = Expression(draw.DistributionIndexExpressionId)!;
                var familyDestination = draw.StateWriteKey is null ? -1 : WriteSlot(draw.StateWriteKey);
                return (frame, random, _) => choices[DrawData.FamilyIndex(selector(frame), choices.Length)]
                    .Run(frame, random, familyDestination);
            }
            case DrawNode draw:
                DrawWeight[] weights = draw.DrawWeights!;
                SamplingDraw boundDraw = _shapes.BindDraw(weights, _scale);
                foreach (ExprValue name in boundDraw.StateValues)
                {
                    _expressions.Remember(name);
                }

                var destination = draw.StateWriteKey is null ? -1 : WriteSlot(draw.StateWriteKey);
                return (s, rng, _) => boundDraw.Run(s, rng, destination);
            case ModifyStateNode modify:
                Func<SamplingFrame, ExprValue>? expression = Expression(modify.ExpressionId);
                if (expression is null)
                {
                    return (_, _, _) => null;
                }

                var output = WriteSlot(modify.OutputKey ?? "__modified__");
                return (s, _, _) =>
                {
                    ExprValue value = expression(s);
                    s.Cells[output].SetTyped(modify.OutputKey is null ? ExprValue.Number(value.AsInteger()) : value);
                    return null;
                };
            case GetStateNode get:
                var getIndex = SlotIndex(get.StateKey ?? "__default__");
                return (s, _, _) => s.Cells[getIndex].Present ? s.Cells[getIndex].Export() : null;
            case PutStateNode put:
                var putIndex = WriteSlot(put.StateKey);
                return (s, _, input) =>
                {
                    s.Cells[putIndex] = SamplingCell.FromRaw(input);
                    return input;
                };
            case DataNode data:
                var dataIndex = WriteSlot(data.StateKey);
                var raw = data.Values.Select(v =>
                    BigInteger.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger n)
                        ? (object)n
                        : v).ToArray();
                SamplingCell cell = SamplingCell.FromRaw(raw);
                _expressions.Remember(cell.Value);
                return (s, _, input) =>
                {
                    s.Cells[dataIndex] = cell;
                    return input;
                };
            case BranchNode branch:
                Func<SamplingFrame, ExprValue>? test = Expression(branch.ConditionId);
                return (s, _, input) =>
                    test is null || SamplingExpressions.RequiredBoolean(test(s)) ? input : BigInteger.Zero;
            case MetricsSinkNode: return (_, _, input) => input;
            default: throw new NotSupportedException($"Sampling plan does not support {node.GetType().Name}.");
        }
    }

    private BigInteger Extract(object? value) => value switch
    {
        BigInteger n => n,
        decimal n => ScaleWin(n),
        Win[] wins => ScaleWin(wins.Sum(w => w.TotalWin)),
        _ => BigInteger.Zero
    };

    private BigInteger ScaleWin(decimal value) =>
        new(decimal.Round(value * (decimal)_scale, 0, MidpointRounding.ToEven));

    private delegate object? Chain(SamplingFrame frame, SeededRandom rng, object? input);

    // One node of a straight run: a state update (Value into Output), a field
    // assigned to itself (Self), or a draw.
    private readonly record struct Step(SamplingDelegate<bool>? Write, int Output, string? Self, Chain? Draw);

    private sealed class Runner(
        SamplingPlan plan,
        Dictionary<string, object?> initial,
        MeasurementCollector? measurements,
        string[]? persistentKeys,
        LoopTerminationEvidence? loopEvidence) : ISamplingRunner<Dictionary<string, object?>, BigInteger>
    {
        private readonly SamplingFrame _frame = new(plan._layout, plan._defaults, initial,
            plan._measurements.Count == 0 && measurements is null && persistentKeys is not { Length: > 0 });

        private readonly bool[]? _retained = persistentKeys is { Length: > 0 }
            ? plan._layout.OrderBy(p => p.Value).Select(p => persistentKeys.Contains(p.Key, StringComparer.Ordinal))
                .ToArray()
            : null;

        private bool _started;
        public void ResetTrajectory() => _started = false;
        public double? RawPayout => (double)_frame.RawPayout / (double)plan._scale;

        public void ObserveRound(double payout)
        {
            if (measurements is null)
            {
                return;
            }

            for (var i = 0; i < plan._measurements.Count; i++)
            {
                _frame.Settlement = new SettlementContext(payout, RawPayout, plan._measurements[i].Options?.Stake ?? 1);
                MeasurementBinding<SamplingFrame> binding = plan._measurementExpressions[i];
                measurements.CompleteRound(i, _frame, binding, payout, RawPayout);
            }

            _frame.Settlement = null;
        }

        public BigInteger Run(SeededRandom rng, CancellationToken token)
        {
            _frame.Reset(token, _started ? _retained : null);
            _started = true;
            _frame.Measurements = measurements;
            _frame.LoopEvidence = loopEvidence;
            return plan._run(_frame, rng);
        }

        public Dictionary<string, object?> ExportState() => _frame.Export();
    }
}
