using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using SlotMath.Core.Expressions;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler.Ir;

/// <summary>
///     Direct node effects and ordered continuations. A continuation list
///     denotes sequential fan-out with shared state, not mutually exclusive CFG edges.
/// </summary>
internal enum IrRootKind
{
    Named,
    Node,
    Measurement
}

internal readonly record struct IrExpressionKey(IrRootKind Kind, string Owner, string Slot);

internal sealed record NodeIr(
    string NodeId,
    IrEffects Effects,
    IReadOnlyList<IrExpressionKey> ExpressionKeys,
    IReadOnlyList<(string Port, string Target)> Continuations);

/// <summary>
///     Compile-time expression regions and resource effects. No IDs or labels
///     select mechanic-specific behavior. Unknown operations invalidate all state facts.
/// </summary>
internal sealed class CompilerIr
{
    private CompilerIr(IReadOnlyDictionary<IrExpressionKey, ExpressionIr> expressions,
        IReadOnlyDictionary<string, NodeIr> nodes,
        IrEffects effects, IReadOnlyDictionary<string, ExprValue> initial)
    {
        Expressions = expressions;
        Nodes = nodes;
        Effects = effects;
        StableInitialValues = initial;
    }

    internal IReadOnlyDictionary<IrExpressionKey, ExpressionIr> Expressions { get; }
    internal IReadOnlyDictionary<string, NodeIr> Nodes { get; }
    internal IrEffects Effects { get; }
    internal IReadOnlyDictionary<string, ExprValue> StableInitialValues { get; }

    internal static CompilerIr Build(GraphConfig config, IReadOnlyList<MeasurementDefinition> measurements,
        IReadOnlyList<FieldDescriptor>? fields = null)
    {
        var context = new TypeCheckContext
        {
            StateFields = fields ?? StateSchemaDeriver.Derive(config),
            BoardFields = TypeCheckContext.Default.BoardFields,
            CellFields = TypeCheckContext.Default.CellFields,
            DecorationTypes = TypeCheckContext.Default.DecorationTypes,
            MeasurementFields = SettlementContext.Fields
        };
        var expressions = new Dictionary<IrExpressionKey, ExpressionIr>();
        foreach ((var key, Expression expression) in config.Expressions ?? [])
        {
            expressions[new IrExpressionKey(IrRootKind.Named, key, "")] =
                ExpressionIrBuilder.Build(expression, context);
        }

        var nodes = new Dictionary<string, NodeIr>(StringComparer.Ordinal);
        foreach (Node node in config.Nodes)
        {
            var references = new List<IrExpressionKey>();

            void Reference(string? id)
            {
                if (id is not null)
                {
                    references.Add(new IrExpressionKey(IrRootKind.Named, id, ""));
                }
            }

            void Inline(string name, Expression? expression)
            {
                if (expression is null)
                {
                    return;
                }

                var key = new IrExpressionKey(IrRootKind.Node, node.Id, name);
                expressions[key] = ExpressionIrBuilder.Build(expression, context);
                references.Add(key);
            }

            IrEffects own;
            switch (node)
            {
                case DrawManyNode batch:
                    Reference(batch.CountExpressionId);
                    own = new IrEffects(
                        IrEffect.Random | IrEffect.WriteState | IrEffect.MayThrow | IrEffect.Cancellation |
                        IrEffect.Allocate, writes: [batch.OutputKey]);
                    break;
                case DrawNode draw:
                    Reference(draw.WeightExpressionId);
                    Reference(draw.WeightVectorExpressionId);
                    Reference(draw.DistributionIndexExpressionId);
                    // Reel and opaque draw outputs are conservatively unknown. Never
                    // inspect an evaluator/transform ID to manufacture a state shape.
                    own = draw.BoundDistributions is null && draw.DrawWeights is not { Length: > 0 }
                        ? IrEffects.Opaque
                        : new IrEffects(
                            IrEffect.Random | IrEffect.WriteState | IrEffect.MayThrow | IrEffect.Cancellation,
                            writes: draw.StateWriteKey is { } key ? [key] : []);
                    break;
                case ModifyStateNode modify:
                    Reference(modify.ExpressionId);
                    own = new IrEffects(IrEffect.WriteState | IrEffect.MayThrow | IrEffect.Cancellation,
                        writes: [modify.OutputKey ?? "__modified__"]);
                    break;
                case PutStateNode put:
                    own = new IrEffects(IrEffect.WriteState | IrEffect.Cancellation, writes: [put.StateKey]);
                    break;
                case GetStateNode get:
                    own = new IrEffects(IrEffect.ReadState | IrEffect.Cancellation | IrEffect.MayThrow,
                        [get.StateKey ?? "__default__"]);
                    break;
                case DataNode data:
                    own = new IrEffects(IrEffect.WriteState | IrEffect.Allocate | IrEffect.Cancellation,
                        writes: [data.StateKey]);
                    break;
                case BranchNode branch:
                    Reference(branch.ConditionId);
                    own = new IrEffects(IrEffect.MayThrow | IrEffect.Cancellation);
                    break;
                case LoopNode loop:
                    Reference(loop.StopConditionId);
                    Inline("exit-reason", loop.ExitReason);
                    own = new IrEffects(IrEffect.WriteState | IrEffect.MayThrow | IrEffect.Cancellation,
                        writes: [$"__iter_{loop.Id}__", $"__wins_{loop.Id}__", $"__exitReason_{loop.Id}__"]);
                    break;
                case MetricsSinkNode sink:
                    own = new IrEffects(
                        IrEffect.Observe | IrEffect.MayThrow |
                        (sink.Settlement is null ? IrEffect.None : IrEffect.WriteState),
                        sink.WinStateKey is { } win ? [win] : [],
                        sink.Settlement is null ? [] : MonetarySettlement.EvidenceKeys);
                    break;
                default: own = IrEffects.Opaque; break;
            }

            foreach ((var name, Port port) in node.Inputs)
            {
                Inline("input:" + name, port.DefaultValue);
            }

            foreach ((var name, Port port) in node.Outputs)
            {
                Inline("output:" + name, port.DefaultValue);
            }

            IEnumerable<IrEffects> selected = references.Select(key =>
                expressions.TryGetValue(key, out ExpressionIr? expression) ? expression.Effects : IrEffects.Opaque);
            IrEffects effects = IrEffects.Union(selected.Prepend(own));
            if (measurements.Any(measurement =>
                    measurement.NodeId == node.Id || measurement.Options?.EntryNodeId == node.Id ||
                    measurement.Options?.ExitNodeId == node.Id))
            {
                effects = IrEffects.Union(effects, new IrEffects(IrEffect.Observe | IrEffect.MayThrow));
            }

            nodes[node.Id] = new NodeIr(node.Id, effects, references.AsReadOnly(), Array.AsReadOnly(config.Edges
                .Where(edge => edge.SourceNodeId == node.Id).Select(edge => (edge.SourcePort, edge.TargetNodeId))
                .ToArray()));
        }

        for (var index = 0; index < measurements.Count; index++)
        {
            MeasurementDefinition measurement = measurements[index];
            Expression?[] roots =
            [
                measurement.Value, measurement.Filter, measurement.Options?.Group, measurement.Options?.Pair,
                measurement.Options?.Weight, measurement.Options?.AwardId, measurement.Options?.EntryFilter,
                measurement.Options?.ExitFilter, measurement.Options?.ExitReason
            ];
            for (var root = 0; root < roots.Length; root++)
            {
                if (roots[root] is { } expression)
                {
                    expressions[
                            new IrExpressionKey(IrRootKind.Measurement,
                                index.ToString(CultureInfo.InvariantCulture),
                                root.ToString(CultureInfo.InvariantCulture))] =
                        ExpressionIrBuilder.Build(expression, context);
                }
            }
        }

        IrEffects all = IrEffects.Union(expressions.Values.Select(expression => expression.Effects)
            .Concat(nodes.Values.Select(node => node.Effects)));
        var written = new HashSet<string>(all.Writes, StringComparer.Ordinal);
        written.UnionWith(all.Bindings);
        written.UnionWith(MonetarySettlement.EvidenceKeys);
        written.UnionWith(["__default__", "__modified__"]);
        var initial = new Dictionary<string, ExprValue>(StringComparer.Ordinal);
        if (all.Known)
        {
            foreach ((var key, JsonElement value) in config.InitialState ?? [])
            {
                if (written.Contains(key))
                {
                    continue;
                }

                SamplingCell cell = SamplingCell.FromRaw(InitialStateValues.Materialize(value));
                if (cell.Readable)
                {
                    initial[key] = cell.StoredValue;
                }
            }
        }

        return new CompilerIr(expressions.ToFrozenDictionary(), nodes.ToFrozenDictionary(StringComparer.Ordinal),
            all, initial.ToFrozenDictionary(StringComparer.Ordinal));
    }
}
