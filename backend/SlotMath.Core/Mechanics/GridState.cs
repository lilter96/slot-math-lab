namespace SlotMath.Core.Mechanics;

// ═══════════════════════════════════════════════════════════════════════════
//  GridState — read/write helpers for a board held as a user-defined array in
//  state S (invariant 4: the engine has NO Board type; a "board" is just a flat
//  row-major array in the state dictionary).
//
//  An element (a "cell") is either:
//    • a plain symbol string                  — simple games, and
//    • a Dictionary<string, object?> record   — complex games carrying
//      { "symbol", "multiplier", "is_locked", … } metadata.
//
//  These are stateless functions over the state dictionary, used by trusted
//  fast-paths and transforms.  They are NOT a board type — nothing is stored,
//  hashed, or memoised here; the canonical state remains plain arrays/records.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Conventions and helpers for reading/writing a board encoded as a flat
/// row-major array in the game state dictionary (invariant 4).
/// </summary>
public static class GridState
{
    /// <summary>Default state key holding the flat board array.</summary>
    public const string CellsKey = "board";

    /// <summary>State key holding the board row count.</summary>
    public const string RowsKey = "rows";

    /// <summary>State key holding the board column count.</summary>
    public const string ColsKey = "cols";

    /// <summary>Cell-record field holding the symbol id.</summary>
    public const string SymbolField = "symbol";

    /// <summary>Cell-record field marking a locked cell.</summary>
    public const string LockedField = "is_locked";

    /// <summary>
    /// Read the flat cell array at <paramref name="key"/>.  Accepts
    /// <c>object?[]</c>, <c>string[]</c>, or any enumerable; returns an empty
    /// array when absent or of an unexpected type.
    /// </summary>
    public static object?[] Cells(IReadOnlyDictionary<string, object?> state, string key = CellsKey)
    {
        if (!state.TryGetValue(key, out var v) || v is null)
            return [];
        return v switch
        {
            // string[] is assignable to object?[] via array covariance, so the
            // object?[] arm also matches it — reading elements works either way.
            object?[] a => a,
            System.Collections.IEnumerable e when v is not string => e.Cast<object?>().ToArray(),
            _ => [],
        };
    }

    /// <summary>Read an integer dimension (rows/cols) from state; 0 if absent.</summary>
    public static int Dim(IReadOnlyDictionary<string, object?> state, string key) =>
        state.TryGetValue(key, out var v) && v is not null
            ? v is System.Numerics.BigInteger integer ? checked((int)integer) : Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture)
            : 0;

    public static int Rows(IReadOnlyDictionary<string, object?> state) => Dim(state, RowsKey);

    public static int Cols(IReadOnlyDictionary<string, object?> state) => Dim(state, ColsKey);

    /// <summary>The symbol id of a cell (string cell → itself; record cell → its "symbol").</summary>
    public static string Symbol(object? cell) => cell switch
    {
        string s => s,
        IReadOnlyDictionary<string, object?> d when d.TryGetValue(SymbolField, out var v) => v as string ?? "",
        _ => "",
    };

    /// <summary>True when a cell holds no symbol (e.g. removed by a cascade).</summary>
    public static bool IsEmpty(object? cell) => string.IsNullOrEmpty(Symbol(cell));

    /// <summary>True when a record cell is flagged locked.</summary>
    public static bool IsLocked(object? cell) =>
        cell is IReadOnlyDictionary<string, object?> d
        && d.TryGetValue(LockedField, out var v)
        && v is true or "true";

    /// <summary>Read a record-cell field (decoration/metadata); null for string cells.</summary>
    public static object? Field(object? cell, string field) =>
        cell is IReadOnlyDictionary<string, object?> d && d.TryGetValue(field, out var v) ? v : null;

    /// <summary>Linear index of (row, col) in a row-major flat array.</summary>
    public static int Index(int row, int col, int cols) => row * cols + col;

    /// <summary>
    /// Enumerate cells with their (row, col) positions.  Falls back to a single
    /// row when no column count is present.
    /// </summary>
    public static IEnumerable<(int Row, int Col, object? Cell)> Enumerate(
        IReadOnlyDictionary<string, object?> state, string key = CellsKey)
    {
        var cells = Cells(state, key);
        var cols = Cols(state);
        if (cols <= 0) cols = cells.Length;
        for (var i = 0; i < cells.Length; i++)
            yield return cols > 0 ? (i / cols, i % cols, cells[i]) : (0, i, cells[i]);
    }

    /// <summary>
    /// Return a copy of <paramref name="state"/> with <paramref name="key"/> set
    /// to <paramref name="value"/> (immutable update, D17).
    /// </summary>
    public static Dictionary<string, object?> With(
        IReadOnlyDictionary<string, object?> state, string key, object? value)
    {
        var next = new Dictionary<string, object?>(state) { [key] = value };
        return next;
    }
}
