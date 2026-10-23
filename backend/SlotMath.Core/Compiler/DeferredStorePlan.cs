using System.Collections.Frozen;
using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler;

/// <summary>
///     Repeated scalar stores whose fields are never read inside a straight run.
///     A generated finally publishes the latest completed write, including on failure.
/// </summary>
internal sealed record DeferredStorePlan(IReadOnlySet<string> Fields, int EliminatedStores, bool BudgetReached)
{
    internal static DeferredStorePlan Build(IReadOnlyList<Node> nodes,
        IReadOnlyDictionary<string, Expression> expressions, CompilerIr? ir, int maxWork = 100000)
    {
        var empty = new DeferredStorePlan(FrozenSet<string>.Empty, 0, false);
        if (ir is null)
        {
            return empty;
        }

        var reads = new HashSet<string>(StringComparer.Ordinal);
        var blocked = new HashSet<string>(StringComparer.Ordinal);
        var writes = new Dictionary<string, int>(StringComparer.Ordinal);
        long work = 0;
        foreach (Node node in nodes)
        {
            if (!ir.Nodes.TryGetValue(node.Id, out NodeIr? owned) || !owned.Effects.Known ||
                (owned.Effects.Flags & IrEffect.Observe) != 0 || node is not (ModifyStateNode or DrawNode))
            {
                return empty;
            }

            work += 1 + owned.Effects.Reads.Count + owned.Effects.Writes.Count;
            if (work > maxWork)
            {
                return empty with
                {
                    BudgetReached = true
                };
            }

            reads.UnionWith(owned.Effects.Reads);
            if (node is not ModifyStateNode modify)
            {
                blocked.UnionWith(owned.Effects.Writes);
                continue;
            }

            var key = new IrExpressionKey(IrRootKind.Named, modify.ExpressionId ?? "", "");
            if (modify.ExpressionId is null || !owned.ExpressionKeys.Contains(key) ||
                !expressions.TryGetValue(modify.ExpressionId, out Expression? expression) ||
                !ir.Expressions.TryGetValue(key, out ExpressionIr? root) ||
                !root.Effects.Known || root.Effects.Bindings.Count != 0 ||
                (root.Effects.Flags & (IrEffect.WriteState | IrEffect.Unknown | IrEffect.Random |
                                       IrEffect.Observe | IrEffect.Cancellation)) != 0)
            {
                return empty;
            }

            var output = modify.OutputKey ?? "__modified__";
            if (expression is not ConstantExpr && !ScalarReusePlan.IntrinsicScalar(expression))
            {
                blocked.Add(output);
            }

            // Extra declared writes are not stores emitted by this operator.
            blocked.UnionWith(owned.Effects.Writes.Where(field => field != output));
            writes[output] = writes.GetValueOrDefault(output) + 1;
        }

        HashSet<string> fields = writes.Where(pair => pair.Value > 1 && !reads.Contains(pair.Key) &&
                                                      !blocked.Contains(pair.Key))
            .Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        return new DeferredStorePlan(fields.ToFrozenSet(StringComparer.Ordinal),
            fields.Sum(field => writes[field] - 1), false);
    }
}
