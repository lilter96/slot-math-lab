namespace SlotMath.Core.Mechanics;

/// <summary>
/// A single cell on the slot board. Immutable value type.
/// Supports multi-symbol cells, empty cells, locked cells, and
/// arbitrary string-keyed decorations.
///
/// Equality is value-based: symbols are compared by content, decorations
/// by key-value pairs. Two cells with identical data are equal regardless
/// of how they were constructed.
/// </summary>
public readonly struct BoardCell : IEquatable<BoardCell>
{
    /// <summary>Symbol IDs in this cell. Empty or null = empty cell.</summary>
    public string[]? Symbols { get; init; }

    /// <summary>Whether this cell is locked (sticky) — cannot be removed or replaced.</summary>
    public bool IsLocked { get; init; }

    /// <summary>
    /// Arbitrary decorations (e.g. "multiplier": "2", "revealed": "true").
    /// Used by transforms and evaluators to attach metadata without changing the cell type.
    /// </summary>
    public Dictionary<string, string>? Decorations { get; init; }

    /// <summary>True when Symbols is null or empty.</summary>
    public bool IsEmpty => Symbols is not { Length: > 0 };

    /// <summary>
    /// Returns a copy of this cell with an updated decoration value.
    /// If the value is null, the key is removed.
    /// </summary>
    public BoardCell WithDecoration(string key, string? value)
    {
        var newDecorations = Decorations is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(Decorations);

        if (value is null)
            newDecorations.Remove(key);
        else
            newDecorations[key] = value;

        return this with { Decorations = newDecorations.Count == 0 ? null : newDecorations };
    }

    /// <summary>
    /// Returns a copy of this cell with the locked state changed.
    /// </summary>
    public BoardCell WithLocked(bool locked) => this with { IsLocked = locked };

    /// <summary>
    /// Returns a copy of this cell with the specified symbols.
    /// </summary>
    public BoardCell WithSymbols(params string[] symbols) => this with { Symbols = symbols };

    /// <summary>
    /// Gets a decoration value, or null if not present.
    /// </summary>
    public string? GetDecoration(string key)
    {
        if (Decorations is not null && Decorations.TryGetValue(key, out var value))
            return value;
        return null;
    }

    // ── Value equality ──────────────────────────────────────────────────

    public bool Equals(BoardCell other)
    {
        if (IsLocked != other.IsLocked) return false;

        // Compare Symbols by content
        var sa = Symbols;
        var sb = other.Symbols;
        if (sa is null || sa.Length == 0)
        {
            if (sb is not null && sb.Length > 0) return false;
        }
        else
        {
            if (sb is null || sa.Length != sb.Length) return false;
            for (var i = 0; i < sa.Length; i++)
                if (!string.Equals(sa[i], sb[i], StringComparison.Ordinal))
                    return false;
        }

        // Compare Decorations by key-value pairs
        var da = Decorations;
        var db = other.Decorations;
        if (da is null || da.Count == 0)
        {
            if (db is not null && db.Count > 0) return false;
        }
        else
        {
            if (db is null || da.Count != db.Count) return false;
            foreach (var kv in da)
                if (!db.TryGetValue(kv.Key, out var v) || !string.Equals(kv.Value, v, StringComparison.Ordinal))
                    return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is BoardCell other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(IsLocked);

        if (Symbols is { Length: > 0 } s)
            foreach (var sym in s)
                hash.Add(sym);

        if (Decorations is { Count: > 0 } d)
        {
            // Sort keys for deterministic hash
            foreach (var key in d.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                hash.Add(key);
                hash.Add(d[key]);
            }
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(BoardCell left, BoardCell right) => left.Equals(right);
    public static bool operator !=(BoardCell left, BoardCell right) => !left.Equals(right);
}
