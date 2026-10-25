using System.Collections.Frozen;

namespace SlotMath.Core.Compiler.Ir;

internal enum MemoryKind
{
    Input,
    Write,
    Phi,
    Call
}

internal enum MemoryStepKind
{
    Read,
    Write,
    Call,
    Branch,
    Loop,
    Observe,
    Cancellation
}

internal enum FlowChannel
{
    OriginalInput,
    NodeOutput,
    Null,
    LoopTotal
}

internal readonly record struct ContinuationKey(string NodeId, string? StopNodeId);

/// <summary>
///     A sparse whole-state version. Writes to disjoint keys do not clobber
///     one another. Calls and conditional writes conservatively include their predecessor.
/// </summary>
internal sealed record MemoryVersion(
    int Id,
    MemoryKind Kind,
    IReadOnlyList<int> Inputs,
    IReadOnlySet<string> Writes,
    bool UnknownWrites,
    bool MayWrite,
    string Owner,
    string Phase);

internal sealed record MemoryCall(ContinuationKey Target, int Input, int Result, FlowChannel Channel);

internal sealed record MemoryStep(
    int Id,
    MemoryStepKind Kind,
    string Owner,
    string Phase,
    int Input,
    int Result,
    IrEffects Effects,
    IReadOnlyList<MemoryRegion> Regions,
    MemoryCall? Call = null,
    IrExpressionKey? Expression = null,
    int? LoopHeader = null,
    int? IterationCap = null);

internal sealed record MemoryRegion(
    int Id,
    string Role,
    int Input,
    int Result,
    IReadOnlyList<MemoryStep> Steps,
    IrEffects Effects);

internal sealed record MemoryFunction(ContinuationKey Key, MemoryRegion Body, IrEffects Effects);

internal sealed record ReachingMemory(IReadOnlySet<int> Definitions, bool Unknown);

internal sealed class MemorySsa
{
    internal MemorySsa(IReadOnlyDictionary<int, MemoryVersion> versions,
        IReadOnlyDictionary<ContinuationKey, MemoryFunction> functions, MemoryRegion root,
        IReadOnlyDictionary<IrExpressionKey, ExpressionIr> expressions)
    {
        Versions = versions;
        Functions = functions;
        Root = root;
        Expressions = expressions;
        WrittenFields = versions.Values.SelectMany(version => version.Writes).ToFrozenSet(StringComparer.Ordinal);
        UnknownWrites = versions.Values.Any(version => version.UnknownWrites);
    }

    internal IReadOnlyDictionary<int, MemoryVersion> Versions { get; }
    internal IReadOnlyDictionary<ContinuationKey, MemoryFunction> Functions { get; }
    internal MemoryRegion Root { get; }
    internal IReadOnlyDictionary<IrExpressionKey, ExpressionIr> Expressions { get; }
    internal IReadOnlySet<string> WrittenFields { get; }
    internal bool UnknownWrites { get; }
    internal bool MayWrite(string field) => UnknownWrites || WrittenFields.Contains(field);

    internal ReachingMemory ReachingDefinitions(int version, string field)
    {
        if (!Versions.ContainsKey(version))
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        var pending = new Stack<int>();
        pending.Push(version);
        var seen = new HashSet<int>();
        var definitions = new HashSet<int>();
        var unknown = false;
        while (pending.TryPop(out var id))
        {
            if (!seen.Add(id))
            {
                continue;
            }

            MemoryVersion current = Versions[id];
            if (current.Kind == MemoryKind.Input)
            {
                definitions.Add(id);
                continue;
            }

            var clobber = current.UnknownWrites || current.Writes.Contains(field);
            if (clobber)
            {
                definitions.Add(id);
                unknown |= current.UnknownWrites;
                if (!current.MayWrite)
                {
                    continue;
                }
            }

            foreach (var input in current.Inputs)
            {
                pending.Push(input);
            }
        }

        return new ReachingMemory(definitions.ToFrozenSet(), unknown);
    }

    internal bool SameValueSource(int before, int after, string field)
    {
        ReachingMemory a = ReachingDefinitions(before, field);
        ReachingMemory b = ReachingDefinitions(after, field);
        return !a.Unknown && !b.Unknown && a.Definitions.SetEquals(b.Definitions);
    }

    /// <summary>
    ///     Limited straight-region motion query. Totality, guards, memory
    ///     versions and every intervening observable/error/cancellation boundary matter.
    /// </summary>
    internal bool CanReuseRead(MemoryRegion region, int earlierStep, int laterStep, string field,
        ExpressionIr expression, IEnumerable<string> overrides)
    {
        if (earlierStep < 0 || laterStep < earlierStep || laterStep >= region.Steps.Count
            || !expression.Effects.CanSpeculate || !expression.AcceptsOverrides(overrides)
            || !expression.Effects.Reads.SetEquals([field]))
        {
            return false;
        }

        MemoryStep first = region.Steps[earlierStep];
        MemoryStep last = region.Steps[laterStep];
        if (first.Expression is not { } key || last.Expression != key ||
            !Expressions.TryGetValue(key, out ExpressionIr? owned)
            || !ReferenceEquals(owned, expression))
        {
            return false;
        }

        const IrEffect barriers = IrEffect.Unknown | IrEffect.Random | IrEffect.Observe | IrEffect.Cancellation |
                                  IrEffect.MayThrow;
        for (var index = earlierStep + 1; index < laterStep; index++)
        {
            if ((region.Steps[index].Effects.Flags & barriers) != 0)
            {
                return false;
            }
        }

        return SameValueSource(region.Steps[earlierStep].Input, region.Steps[laterStep].Input, field);
    }
}
