namespace SlotMath.Core.Model;

public enum PrecomputationScope
{
    State,
    Local
}

/// <summary>Ordered optimization domains. A miss always evaluates the original expression.</summary>
public sealed record PrecomputationDomain
{
    public string Name { get; init; } = string.Empty;
    public PrecomputationScope Scope { get; init; }
    public PrecomputationValue[] Values { get; init; } = [];
}

public sealed record PrecomputationValue
{
    public ConstantKind Kind { get; init; }
    public string Value { get; init; } = string.Empty;

    internal ConstantExpr Expression => new()
    {
        Kind = Kind,
        Value = Value
    };
}

public sealed record PrecomputationHint
{
    public PrecomputationDomain[] Domains { get; init; } = [];
    public long ExpectedUses { get; init; }
    public int MaxEntries { get; init; } = 65536;
    public long MaxWork { get; init; } = 1_000_000;
    public long MaxBytes { get; init; } = 8 * 1024 * 1024;
}
