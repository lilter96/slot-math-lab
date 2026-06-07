namespace SlotMath.Core.Mechanics;

/// <summary>
/// A single winning combination produced by an <see cref="IEvaluator"/>.
///
/// Carries the winning symbol, match count, board positions, and payout.
/// Supports value-based equality so wins can be compared across evaluation runs.
/// </summary>
public sealed class Win : IEquatable<Win>
{
    /// <summary>The symbol ID that triggered the win.</summary>
    public required string SymbolId { get; init; }

    /// <summary>Number of matching symbols (e.g. 3-of-a-kind, 5-of-a-kind).</summary>
    public required int Count { get; init; }

    /// <summary>Board positions that are part of this win.</summary>
    public required (int Row, int Col)[] Positions { get; init; }

    /// <summary>Base payout amount (before multipliers).</summary>
    public required decimal Payout { get; init; }

    /// <summary>
    /// Optional multiplier applied to the base payout.
    /// TotalWin = Payout × Multiplier.
    /// </summary>
    public decimal Multiplier { get; init; } = 1m;

    /// <summary>Total win = Payout × Multiplier.</summary>
    public decimal TotalWin => Payout * Multiplier;

    /// <summary>Name of the evaluator that produced this win (for debugging/provenance).</summary>
    public string? EvaluatorName { get; init; }

    // ── Value equality (positions compared by content) ──────────────

    public bool Equals(Win? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SymbolId == other.SymbolId
               && Count == other.Count
               && Payout == other.Payout
               && Multiplier == other.Multiplier
               && Positions.Length == other.Positions.Length
               && Positions.SequenceEqual(other.Positions);
    }

    public override bool Equals(object? obj) => Equals(obj as Win);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SymbolId);
        hash.Add(Count);
        hash.Add(Payout);
        hash.Add(Multiplier);
        foreach (var p in Positions)
            hash.Add(p);
        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"{Count}×{SymbolId} @ {string.Join(",", Positions.Select(p => $"({p.Row},{p.Col})"))} = {TotalWin}";
}
