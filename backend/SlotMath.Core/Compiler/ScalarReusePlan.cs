using System.Collections.Frozen;
using System.Text.Json;
using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler;

/// <summary>
///     Available scalar values in one straight continuation. A first successful
///     evaluation dominates its reuses; no evaluation or checkpoint moves.
/// </summary>
internal sealed record ScalarReusePlan(IReadOnlyDictionary<string, int> Groups, bool BudgetReached)
{
    internal int EliminatedEvaluations => Groups.Count - Groups.Values.Distinct().Count();

    internal static bool IntrinsicScalar(Expression expression) => expression is
        BinaryExpr or CompareExpr or NotExpr or BitSetExpr;

    internal static ScalarReusePlan Build(IReadOnlyList<Node> nodes,
        IReadOnlyDictionary<string, Expression> expressions, CompilerIr? ir, int maxWork = 100000,
        Func<ModifyStateNode, bool>? admit = null)
    {
        var empty = FrozenDictionary<string, int>.Empty;
        if (ir is null)
        {
            return new ScalarReusePlan(empty, false);
        }

        var available = new Dictionary<string, (int Group, IReadOnlySet<string> Reads)>(StringComparer.Ordinal);
        var groups = new Dictionary<string, int>(StringComparer.Ordinal);
        var serial = 0;
        long work = 0;
        long retainedText = 0;
        foreach (Node node in nodes)
        {
            if (++work > maxWork)
            {
                return new ScalarReusePlan(empty, true);
            }

            if (node is not ModifyStateNode modify)
            {
                available.Clear(); // RNG, observations and unfamiliar nodes are barriers.
                continue;
            }

            var key = new IrExpressionKey(IrRootKind.Named, modify.ExpressionId ?? "", "");
            if (modify.ExpressionId is null || !expressions.TryGetValue(modify.ExpressionId, out Expression? value)
                                            || !ir.Nodes.TryGetValue(node.Id, out NodeIr? owned) ||
                                            !owned.ExpressionKeys.Contains(key)
                                            || !ir.Expressions.TryGetValue(key, out ExpressionIr? root))
            {
                available.Clear();
                continue;
            }

            const IrEffect barriers = IrEffect.Unknown | IrEffect.WriteState | IrEffect.Random |
                                      IrEffect.Observe | IrEffect.Cancellation;
            if (!root.Effects.Known || (root.Effects.Flags & barriers) != 0 || root.Effects.Bindings.Count != 0 ||
                (owned.Effects.Flags & (IrEffect.Unknown | IrEffect.Observe | IrEffect.Random)) != 0)
            {
                available.Clear();
                continue;
            }

            var cost = ExpressionCost.Compute(value);
            work += cost;
            if (work > maxWork)
            {
                return new ScalarReusePlan(empty, true);
            }

            // Intrinsic scalar operators validate their operands before returning.
            // Inferred external shapes alone do not prove scalar ownership.
            if (cost >= 5 && IntrinsicScalar(value) && (admit is null || admit(modify)))
            {
                var signature = JsonSerializer.Serialize(value, JsonOptions.Default);
                retainedText += signature.Length;
                if (retainedText > 2000000)
                {
                    return new ScalarReusePlan(empty, true);
                }

                if (!available.TryGetValue(signature, out (int Group, IReadOnlySet<string> Reads) previous))
                {
                    previous = (++serial, root.Effects.Reads);
                    available[signature] = previous;
                }

                groups[node.Id] = previous.Group;
            }

            work += available.Count;
            if (work > maxWork)
            {
                return new ScalarReusePlan(empty, true);
            }

            foreach (var signature in available.Where(pair => pair.Value.Reads.Overlaps(owned.Effects.Writes))
                         .Select(pair => pair.Key).ToArray())
            {
                available.Remove(signature);
            }
        }

        HashSet<int> shared = groups.Values.GroupBy(group => group).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet();
        return new ScalarReusePlan(groups.Where(pair => shared.Contains(pair.Value))
            .ToFrozenDictionary(StringComparer.Ordinal), false);
    }
}
