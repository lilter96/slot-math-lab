using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler;

// Scalar replacement uses established whole-program mask invariants. Reads
// after writes, or repeated writes, can avoid materializing intermediate values.
internal static class SamplingMaskLocals
{
    internal static IReadOnlyDictionary<string, bool> Find(IReadOnlyList<Node> nodes,
        CompilerIr? ir, Func<string, bool> isMask)
    {
        var writes = new Dictionary<string, int>(StringComparer.Ordinal);
        var initialReads = new HashSet<string>(StringComparer.Ordinal);
        var subsequentReads = new HashSet<string>(StringComparer.Ordinal);
        if (ir is null || nodes.Count > 4096)
        {
            return new Dictionary<string, bool>();
        }

        foreach (Node node in nodes)
        {
            if (!ir.Nodes.TryGetValue(node.Id, out NodeIr? owned) || !owned.Effects.Known ||
                (owned.Effects.Flags & IrEffect.Observe) != 0)
            {
                return new Dictionary<string, bool>();
            }

            foreach (var field in owned.Effects.Reads)
            {
                (writes.ContainsKey(field) ? subsequentReads : initialReads).Add(field);
            }

            if (node is ModifyStateNode { OutputKey: { } key } && isMask(key))
            {
                writes[key] = writes.GetValueOrDefault(key) + 1;
            }
        }

        return writes.Where(pair => pair.Value > 1 || subsequentReads.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => initialReads.Contains(pair.Key), StringComparer.Ordinal);
    }
}
