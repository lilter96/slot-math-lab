namespace SlotMath.Core.Model;

// ── Symbols ───────────────────────────────────────────────────────────

public enum SymbolKind
{
    Standard,
    Wild,
    Scatter,
    Bonus,
    Multiplier,
    Money,
    Jackpot,
}

public sealed record Symbol
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public SymbolKind Kind { get; init; } = SymbolKind.Standard;
    public Dictionary<string, string>? Properties { get; init; }
}

// ── Paytable ───────────────────────────────────────────────────────────

public sealed record PaytableEntry
{
    public required string SymbolId { get; init; }
    public required int[] Counts { get; init; }
    public required string[] Payouts { get; init; } // rational strings, indexed same as Counts
}

public sealed record Paytable
{
    public required string Id { get; init; }
    public required PaytableEntry[] Entries { get; init; }
}

// ── Payline ────────────────────────────────────────────────────────────

public sealed record Payline
{
    public required int[] Positions { get; init; } // [col0row, col1row, col2row, ...]
}

public sealed record PaylineSet
{
    public required string Id { get; init; }
    public required Payline[] Paylines { get; init; }
}

// ── Reel strips ────────────────────────────────────────────────────────

public sealed record ReelStrip
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string[] Symbols { get; init; }
}

public sealed record ReelSet
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string[] StripIds { get; init; }
}

// ── Board config ───────────────────────────────────────────────────────

public sealed record BoardConfig
{
    public int Rows { get; init; } = 3;
    public int Columns { get; init; } = 5;
    public bool AllowMultiSymbol { get; init; }
    public bool AllowEmpty { get; init; }
    public bool AllowLocked { get; init; }
    public bool Growable { get; init; }
}
