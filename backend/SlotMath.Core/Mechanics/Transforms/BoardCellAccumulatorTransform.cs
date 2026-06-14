namespace SlotMath.Core.Mechanics.Transforms;

/// <summary>
/// Scans the board for cells matching <see cref="SymbolFilter"/>, extracts data
/// from them per <see cref="ExtractMode"/>, and merges the result into
/// <see cref="StateKey"/> in state per <see cref="MergeMode"/>.
///
/// The board is returned UNCHANGED — only state is updated.
/// Pair with <see cref="BoardCellApplyTransform"/> when the accumulated data
/// needs to be written back to the board on future iterations.
///
/// Configurable parameters — all exposed as UI properties:
///   SymbolFilter  — which symbol to match (null = any non-empty cell)
///   ExtractMode   — what data to pull from matching cells:
///                     Position → "row,col" strings  (for sticky positions)
///                     Symbol   → symbol id strings  (for collection meters)
///                     Count    → integer count       (for numeric accumulators)
///   StateKey      — dictionary key to read/write in state
///   MergeMode     — how to combine this board's data with accumulated state:
///                     Union   → set union of string[]  (for sticky / collect)
///                     Replace → overwrite (only keep current board's data)
///                     Sum     → integer add             (for counters)
///                     Max     → integer max             (for max-ever tracking)
/// </summary>
public sealed class BoardCellAccumulatorTransform : IFastPathTransform
{
    public string? SymbolFilter { get; }
    public CellExtractMode ExtractMode { get; }
    public string StateKey { get; }
    public CellMergeMode MergeMode { get; }

    public BoardCellAccumulatorTransform(
        string? symbolFilter,
        CellExtractMode extractMode,
        string stateKey,
        CellMergeMode mergeMode = CellMergeMode.Union)
    {
        ArgumentNullException.ThrowIfNull(stateKey);
        SymbolFilter = symbolFilter;
        ExtractMode  = extractMode;
        StateKey     = stateKey;
        MergeMode    = mergeMode;
    }

    public IReadOnlyDictionary<string, object?> Apply(IReadOnlyDictionary<string, object?> state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var cells = GridState.Cells(state);
        var cols = GridState.Cols(state);
        if (cols <= 0) cols = cells.Length; // single-row fallback

        return ExtractMode switch
        {
            CellExtractMode.Count    => MergeCount(cells, state),
            CellExtractMode.Position => MergeStringSet(cells, cols, state, ExtractPosition),
            CellExtractMode.Symbol   => MergeStringSet(cells, cols, state, ExtractSymbol),
            _                        => state,
        };
    }

    // ── Count mode ────────────────────────────────────────────────────────

    private IReadOnlyDictionary<string, object?> MergeCount(object?[] cells, IReadOnlyDictionary<string, object?> state)
    {
        var count = cells.Count(Matches);

        var existing = state.TryGetValue(StateKey, out var v) && v is int i ? i : 0;
        var merged = MergeMode switch
        {
            CellMergeMode.Sum     => existing + count,
            CellMergeMode.Max     => System.Math.Max(existing, count),
            CellMergeMode.Replace => count,
            _                     => count,
        };

        return GridState.With(state, StateKey, merged);
    }

    // ── String-set modes (Position / Symbol) ──────────────────────────────

    private IReadOnlyDictionary<string, object?> MergeStringSet(
        object?[] cells, int cols,
        IReadOnlyDictionary<string, object?> state,
        Func<int, int, object?, string?> extractor)
    {
        var currentSet = CollectFromBoard(cells, cols, extractor);

        if (MergeMode == CellMergeMode.Replace)
        {
            var arr = currentSet.OrderBy(s => s, StringComparer.Ordinal).ToArray();
            return GridState.With(state, StateKey, arr);
        }

        // Union: merge with existing set
        if (state.TryGetValue(StateKey, out var val) && val is string[] existing)
            foreach (var s in existing) currentSet.Add(s);

        var merged = currentSet.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        return GridState.With(state, StateKey, merged);
    }

    private HashSet<string> CollectFromBoard(object?[] cells, int cols, Func<int, int, object?, string?> extractor)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var idx = 0; idx < cells.Length; idx++)
        {
            var cell = cells[idx];
            if (!Matches(cell)) continue;
            var (r, c) = cols > 0 ? (idx / cols, idx % cols) : (0, idx);
            var extracted = extractor(r, c, cell);
            if (extracted != null) set.Add(extracted);
        }
        return set;
    }

    // ── Extractors ────────────────────────────────────────────────────────

    private static string ExtractPosition(int row, int col, object? _) => $"{row},{col}";

    private static string? ExtractSymbol(int _, int __, object? cell) =>
        GridState.IsEmpty(cell) ? null : GridState.Symbol(cell);

    // ── Filter ────────────────────────────────────────────────────────────

    private bool Matches(object? cell)
    {
        if (GridState.IsEmpty(cell)) return false;
        return SymbolFilter == null || GridState.Symbol(cell) == SymbolFilter;
    }
}
