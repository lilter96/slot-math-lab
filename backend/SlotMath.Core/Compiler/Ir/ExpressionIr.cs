using System.Collections.Frozen;
using SlotMath.Core.Expressions;

namespace SlotMath.Core.Compiler.Ir;

[Flags]
internal enum IrEffect
{
    None = 0,
    ReadState = 1,
    WriteState = 2,
    MayThrow = 4,
    Allocate = 8,
    Random = 16,
    Observe = 32,
    Cancellation = 64,
    Unknown = 128
}

internal sealed class IrEffects
{
    internal static readonly IrEffects Empty = new(IrEffect.None);

    internal static readonly IrEffects Opaque = new(IrEffect.Unknown | IrEffect.ReadState | IrEffect.WriteState
                                                    | IrEffect.MayThrow | IrEffect.Allocate | IrEffect.Random |
                                                    IrEffect.Observe | IrEffect.Cancellation);

    internal IrEffects(IrEffect flags, IEnumerable<string>? reads = null, IEnumerable<string>? writes = null,
        IEnumerable<string>? bindings = null)
    {
        Flags = flags;
        Reads = (reads ?? []).ToFrozenSet(StringComparer.Ordinal);
        Writes = (writes ?? []).ToFrozenSet(StringComparer.Ordinal);
        Bindings = (bindings ?? []).ToFrozenSet(StringComparer.Ordinal);
    }

    internal IrEffect Flags { get; }
    internal IReadOnlySet<string> Reads { get; }
    internal IReadOnlySet<string> Writes { get; }
    internal IReadOnlySet<string> Bindings { get; }
    internal bool Known => (Flags & IrEffect.Unknown) == 0;

    internal bool CanSpeculate => (Flags & (IrEffect.MayThrow | IrEffect.WriteState | IrEffect.Random
                                            | IrEffect.Observe | IrEffect.Cancellation | IrEffect.Unknown)) == 0;

    internal static IrEffects Union(params IEnumerable<IrEffects> effects)
    {
        var flags = IrEffect.None;
        var reads = new HashSet<string>(StringComparer.Ordinal);
        var writes = new HashSet<string>(StringComparer.Ordinal);
        var bindings = new HashSet<string>(StringComparer.Ordinal);
        foreach (IrEffects effect in effects)
        {
            flags |= effect.Flags;
            reads.UnionWith(effect.Reads);
            writes.UnionWith(effect.Writes);
            bindings.UnionWith(effect.Bindings);
        }

        return new IrEffects(flags, reads, writes, bindings);
    }
}

internal enum IrOp
{
    Constant,
    ReadState,
    ReadMeasurement,
    Parameter,
    Project,
    Let,
    Binary,
    Compare,
    Not,
    Call,
    BitSet,
    If,
    And,
    Or,
    Record,
    Tuple,
    Map,
    Filter,
    Fold,
    Aggregate,
    AggregateStep,
    Opaque
}

/// <summary>
///     A single-assignment value. A shape describes the authored contract;
///     it does not prove that arbitrary external state satisfies that contract.
/// </summary>
internal sealed record IrValue(int Id, FieldDescriptor? Shape, ExprValue? Constant = null)
{
    internal ExprType? Type => Shape?.Type;
}

/// <summary>
///     Operations in one ordered scope. Child scopes execute only according
///     to their owner's control semantics; operands in sibling scopes never dominate one another.
/// </summary>
internal sealed record IrRegion(
    int Id,
    string Role,
    IReadOnlyList<IrValue> Parameters,
    IReadOnlyList<IrOperation> Operations,
    IrValue Result,
    IrEffects Effects);

internal sealed record IrOperation(
    int Id,
    IrOp Code,
    IrValue Result,
    IReadOnlyList<IrValue> Inputs,
    IReadOnlyList<IrRegion> Regions,
    IrEffects OwnEffects,
    IrEffects Effects,
    string? Detail = null,
    IReadOnlyList<string>? Path = null,
    int? Bound = null);

internal sealed class ExpressionIr(IrRegion root, IReadOnlyDictionary<string, ExprValue> guards)
{
    internal IrRegion Root { get; } = root;
    internal IrEffects Effects => Root.Effects;
    internal IReadOnlyDictionary<string, ExprValue> Guards { get; } = guards;
    internal bool AcceptsOverrides(IEnumerable<string> overriddenFields) => !overriddenFields.Any(Guards.ContainsKey);

    internal IEnumerable<IrOperation> Operations()
    {
        return Visit(Root);

        static IEnumerable<IrOperation> Visit(IrRegion region)
        {
            foreach (IrOperation operation in region.Operations)
            {
                yield return operation;
                foreach (IrOperation child in operation.Regions.SelectMany(Visit))
                {
                    yield return child;
                }
            }
        }
    }
}
