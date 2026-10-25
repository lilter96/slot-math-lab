using System.Collections.Frozen;
using System.Globalization;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler.Ir;

/// <summary>
///     Structured memory SSA for the runtime's continuation functions.
///     Shared graph tails remain calls; sequential fan-out is never modeled as a CFG phi.
/// </summary>
internal sealed class MemorySsaBuilder
{
    private readonly Dictionary<string, Node> _authored;
    private readonly HashSet<ContinuationKey> _building = [];
    private readonly GraphConfig _config;
    private readonly Dictionary<string, (string Port, string Target)[]> _edges;
    private readonly IReadOnlyDictionary<IrExpressionKey, ExpressionIr> _expressions;
    private readonly Dictionary<ContinuationKey, MemoryFunction> _functions = [];
    private readonly IReadOnlyList<MeasurementDefinition> _measurements;
    private readonly IReadOnlyDictionary<string, NodeIr> _nodes;
    private readonly Dictionary<int, MemoryVersion> _versions = [];
    private int _region;
    private int _step;
    private int _version;

    private MemorySsaBuilder(GraphConfig config, IReadOnlyDictionary<string, NodeIr> nodes,
        IReadOnlyDictionary<IrExpressionKey, ExpressionIr> expressions,
        IReadOnlyList<MeasurementDefinition> measurements)
    {
        _config = config;
        _nodes = nodes;
        _expressions = expressions;
        _measurements = measurements;
        _authored = config.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        _edges = nodes.ToDictionary(pair => pair.Key, pair => pair.Value.Continuations.ToArray(),
            StringComparer.Ordinal);
    }

    internal static MemorySsa Build(GraphConfig config, IReadOnlyDictionary<string, NodeIr> nodes,
        IReadOnlyDictionary<IrExpressionKey, ExpressionIr> expressions,
        IReadOnlyList<MeasurementDefinition> measurements)
    {
        var builder = new MemorySsaBuilder(config, nodes, expressions, measurements);
        return builder.Build();
    }

    private int Version(MemoryKind kind, IEnumerable<int> inputs, IEnumerable<string>? writes,
        bool unknown, bool mayWrite, string owner, string phase)
    {
        var id = ++_version;
        _versions[id] = new MemoryVersion(id, kind, Array.AsReadOnly(inputs.ToArray()),
            (writes ?? []).ToFrozenSet(StringComparer.Ordinal), unknown, mayWrite, owner, phase);
        return id;
    }

    private MemoryRegion Finish(Region region) => new(++_region, region.Role, region.Input, region.Current,
        region.Steps.AsReadOnly(), IrEffects.Union(region.Steps.Select(step => step.Effects)));

    private void Step(Region region, MemoryStepKind kind, string owner, string phase, IrEffects effects,
        int? result = null, MemoryRegion[]? children = null, MemoryCall? call = null,
        IrExpressionKey? expression = null, int? header = null, int? cap = null)
    {
        MemoryRegion[] nested = children ?? [];
        region.Steps.Add(new MemoryStep(++_step, kind, owner, phase, region.Current, result ?? region.Current,
            IrEffects.Union(new[] { effects }.Concat(nested.Select(child => child.Effects))),
            Array.AsReadOnly(nested), call, expression, header, cap));
        region.Current = result ?? region.Current;
    }

    private void Write(Region region, string owner, string phase, IEnumerable<string> keys,
        bool may = false, bool unknown = false)
    {
        var names = keys.ToArray();
        var result = Version(MemoryKind.Write, [region.Current], names, unknown, may, owner, phase);
        Step(region, MemoryStepKind.Write, owner, phase,
            new IrEffects(IrEffect.WriteState | (unknown ? IrEffect.Unknown : IrEffect.None), writes: names), result);
    }

    private void Expression(Region region, string owner, IrExpressionKey key, string phase)
    {
        IrEffects effects = _expressions.TryGetValue(key, out ExpressionIr? expression)
            ? expression.Effects
            : IrEffects.Opaque;
        Step(region, MemoryStepKind.Read, owner, phase, effects, expression: key);
        if ((effects.Flags & IrEffect.WriteState) != 0)
        {
            Write(region, owner, phase + ":writes", effects.Writes, true, !effects.Known);
        }
    }

    private void Named(Region region, string owner, string? expression, string phase)
    {
        if (expression is not null)
        {
            Expression(region, owner, new IrExpressionKey(IrRootKind.Named, expression, ""), phase);
        }
    }

    private void Observe(Region region, string node)
    {
        (MeasurementDefinition measurement, int index)[] selected = _measurements
            .Select((measurement, index) => (measurement, index)).Where(pair =>
                pair.measurement.NodeId == node || pair.measurement.Options?.EntryNodeId == node ||
                pair.measurement.Options?.ExitNodeId == node).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        var observed = new Region("collector-if-present", region.Current);
        IEnumerable<IrExpressionKey> keys = selected.SelectMany(pair => _expressions.Keys
            .Where(key =>
                key.Kind == IrRootKind.Measurement && key.Owner == pair.index.ToString(CultureInfo.InvariantCulture))
            .OrderBy(key => key.Slot, StringComparer.Ordinal));
        foreach (IrExpressionKey key in keys)
        {
            Expression(observed, node, key, "observation-expression");
        }

        MemoryRegion child = Finish(observed);
        var result = child.Result == region.Current
            ? region.Current
            : Version(MemoryKind.Phi, [region.Current, child.Result], [], false, false, node,
                "observation-conditional-merge");
        Step(region, MemoryStepKind.Observe, node, "continuation-entry-observation",
            new IrEffects(IrEffect.Observe | IrEffect.MayThrow), result, [child]);
    }

    private void Invoke(Region region, string owner, ContinuationKey target, FlowChannel channel, string phase)
    {
        MemoryFunction function = Function(target);
        IrEffects effects = function.Effects;
        var result = effects.Writes.Count == 0 && effects.Known
            ? region.Current
            : Version(MemoryKind.Call, [region.Current], effects.Writes, !effects.Known, true, owner, phase);
        var call = new MemoryCall(target, region.Current, result, channel);
        Step(region, MemoryStepKind.Call, owner, phase, effects, result, call: call);
    }

    private MemoryFunction Function(ContinuationKey key)
    {
        if (_functions.TryGetValue(key, out MemoryFunction? cached))
        {
            return cached;
        }

        if (!_building.Add(key))
        {
            throw new InvalidOperationException("Unstructured continuation cycle in memory SSA.");
        }

        var input = Version(MemoryKind.Input, [], [], false, false, key.NodeId, "function-input");
        var region = new Region("continuation", input);
        Observe(region, key.NodeId);
        if (key.NodeId != key.StopNodeId)
        {
            Node node = _authored[key.NodeId];
            (string Port, string Target)[] edges = _edges[key.NodeId];
            Step(region, MemoryStepKind.Cancellation, node.Id, "node-checkpoint", new IrEffects(IrEffect.Cancellation));
            if (node is BranchNode branch && edges.Any(edge => edge.Port is "true" or "false"))
            {
                Branch(region, branch, edges, key.StopNodeId);
            }
            else if (node is LoopNode loop && edges.Any(edge => edge.Port == "body"))
            {
                Loop(region, loop, edges, key.StopNodeId);
            }
            else
            {
                foreach (IrExpressionKey expression in _nodes[node.Id].ExpressionKeys)
                {
                    Expression(region, node.Id, expression, "node-expression");
                }

                IrEffects effects = _nodes[node.Id].Effects;
                Step(region, MemoryStepKind.Read, node.Id, "node-output", effects);
                if (effects.Writes.Count != 0 || !effects.Known)
                {
                    Write(region, node.Id, "node-commit", effects.Writes, !effects.Known, !effects.Known);
                }

                FlowChannel channel = node is GetStateNode or PutStateNode or ModifyStateNode
                    ? FlowChannel.OriginalInput
                    : FlowChannel.NodeOutput;
                foreach ((string Port, string Target) edge in edges)
                {
                    Invoke(region, node.Id, new ContinuationKey(edge.Target, key.StopNodeId), channel,
                        "sequential-continuation");
                }
            }
        }

        MemoryRegion body = Finish(region);
        var result = new MemoryFunction(key, body, body.Effects);
        _functions[key] = result;
        _building.Remove(key);
        return result;
    }

    private void Branch(Region region, BranchNode node, (string Port, string Target)[] edges, string? stop)
    {
        Named(region, node.Id, node.ConditionId, "branch-condition");
        var arms = new List<MemoryRegion>();
        foreach (var port in new[] { "true", "false" })
        {
            var arm = new Region(port, region.Current);
            (string Port, string Target) edge = edges.FirstOrDefault(edge => edge.Port == port);
            if (edge.Target is not null)
            {
                Invoke(arm, node.Id, new ContinuationKey(edge.Target, stop), FlowChannel.OriginalInput,
                    port + "-continuation");
            }

            arms.Add(Finish(arm));
        }

        var result = arms[0].Result == arms[1].Result
            ? arms[0].Result
            : Version(MemoryKind.Phi,
                arms.Select(arm => arm.Result), [], false, false, node.Id, "branch-merge");
        Step(region, MemoryStepKind.Branch, node.Id, "branch", new IrEffects(IrEffect.MayThrow), result, [.. arms]);
    }

    private void Loop(Region region, LoopNode node, (string Port, string Target)[] edges, string? stop)
    {
        var counter = $"__iter_{node.Id}__";
        var wins = $"__wins_{node.Id}__";
        var reason = $"__exitReason_{node.Id}__";
        Write(region, node.Id, "loop-initialize", [counter, wins]);
        var header = Version(MemoryKind.Phi, [region.Current], [], false, false, node.Id, "loop-header");
        var guard = new Region("loop-guard", header);
        Step(guard, MemoryStepKind.Cancellation, node.Id, "iteration-checkpoint", new IrEffects(IrEffect.Cancellation));
        Step(guard, MemoryStepKind.Read, node.Id, "cap-before-stop", new IrEffects(IrEffect.ReadState, [counter]));
        var underCap = new Region("stop-if-under-cap", header);
        Named(underCap, node.Id, node.StopConditionId, "stop-condition");
        MemoryRegion stopped = Finish(underCap);
        var guardState = stopped.Result == header
            ? header
            : Version(MemoryKind.Phi, [header, stopped.Result], [], false, false, node.Id, "stop-conditional-merge");
        Step(guard, MemoryStepKind.Branch, node.Id, "cap-short-circuit", new IrEffects(IrEffect.MayThrow), guardState,
            [stopped]);
        var body = new Region("loop-body", guard.Current);
        Invoke(body, node.Id, new ContinuationKey(edges.First(edge => edge.Port == "body").Target, null),
            FlowChannel.Null,
            "body-call");
        Step(body, MemoryStepKind.Read, node.Id, "counter-after-body", new IrEffects(IrEffect.ReadState, [counter]));
        Write(body, node.Id, "counter-increment", [counter]);
        Step(body, MemoryStepKind.Read, node.Id, "wins-after-body-if-numeric-channel",
            new IrEffects(IrEffect.ReadState, [wins]));
        Write(body, node.Id, "wins-if-numeric-channel", [wins], true);
        _versions[header] = _versions[header] with
        {
            Inputs = Array.AsReadOnly(new[] { region.Current, body.Current })
        };
        var exit = new Region("loop-exit", guard.Current);
        Step(exit, MemoryStepKind.Read, node.Id, "exit-cap-test", new IrEffects(IrEffect.ReadState, [counter]));
        var classified = new Region("exit-reason-if-below-cap", exit.Current);
        if (node.ExitReason is not null)
        {
            Expression(classified, node.Id, new IrExpressionKey(IrRootKind.Node, node.Id, "exit-reason"),
                "exit-reason");
        }

        MemoryRegion classification = Finish(classified);
        var classifiedState = classification.Result == exit.Current
            ? exit.Current
            : Version(MemoryKind.Phi,
                [exit.Current, classification.Result], [], false, false, node.Id, "reason-conditional-merge");
        Step(exit, MemoryStepKind.Branch, node.Id, "reason-short-circuit", new IrEffects(IrEffect.MayThrow),
            classifiedState, [classification]);
        Write(exit, node.Id, "exit-reason-commit", [reason]);
        Step(exit, MemoryStepKind.Observe, node.Id, "loop-termination-evidence", new IrEffects(IrEffect.Observe));
        Step(exit, MemoryStepKind.Read, node.Id, "loop-total", new IrEffects(IrEffect.ReadState, [wins]));
        Step(region, MemoryStepKind.Loop, node.Id, "counter-controlled-loop", IrEffects.Empty, exit.Current,
            [Finish(guard), Finish(body), Finish(exit)], header: header, cap: node.MaxIterations);
        (string Port, string Target) next = edges.FirstOrDefault(edge => edge.Port is "exit" or "out");
        if (next.Target is not null)
        {
            Invoke(region, node.Id, new ContinuationKey(next.Target, stop), FlowChannel.LoopTotal,
                "loop-exit-continuation");
        }
    }

    private MemorySsa Build()
    {
        var input = Version(MemoryKind.Input, [], [], false, false, "", "external-state-after-defaults");
        var root = new Region("round", input);
        MetricsSinkNode sink = _config.Nodes.OfType<MetricsSinkNode>().Single();
        HashSet<string> incoming = _config.Edges.Select(edge => edge.TargetNodeId).ToHashSet(StringComparer.Ordinal);
        foreach (Node entry in _config.Nodes.Where(node => !incoming.Contains(node.Id)))
        {
            Invoke(root, "", new ContinuationKey(entry.Id, sink.Id), FlowChannel.Null, "entry-call");
        }

        Step(root, MemoryStepKind.Read, sink.Id, "settlement-read", _nodes[sink.Id].Effects);
        if (sink.Settlement is not null)
        {
            Write(root, sink.Id, "settlement-commit", MonetarySettlement.EvidenceKeys);
        }

        Step(root, MemoryStepKind.Observe, "", "exportable-final-state", new IrEffects(IrEffect.Observe));
        // Also inspect syntactically present nodes, keeping the compiler's existing
        // conservative treatment of unused definitions and branches.
        foreach (Node node in _config.Nodes)
        {
            Function(new ContinuationKey(node.Id, sink.Id));
        }

        return new MemorySsa(_versions.ToFrozenDictionary(), _functions.ToFrozenDictionary(), Finish(root),
            _expressions);
    }

    private sealed class Region(string role, int input)
    {
        internal readonly int Input = input;
        internal readonly string Role = role;
        internal readonly List<MemoryStep> Steps = [];
        internal int Current = input;
    }
}
