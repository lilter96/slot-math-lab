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

    public (Board NewBoard, object? NewState) Apply(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);
        var stateDict = state as Dictionary<string, object?> ?? new Dictionary<string, object?>();

        var newStateDict = ExtractMode switch
        {
            CellExtractMode.Count    => MergeCount(board, stateDict),
            CellExtractMode.Position => MergeStringSet(board, stateDict, ExtractPosition),
            CellExtractMode.Symbol   => MergeStringSet(board, stateDict, ExtractSymbol),
            _                        => stateDict,
        };

        return (board, newStateDict);
    }

    // ── Count mode ────────────────────────────────────────────────────────

    private Dictionary<string, object?> MergeCount(Board board, Dictionary<string, object?> state)
    {
        var count = 0;
        for (var r = 0; r < board.Rows; r++)
            for (var c = 0; c < board.Cols; c++)
                if (Matches(board[r, c])) count++;

        var existing = state.TryGetValue(StateKey, out var v) && v is int i ? i : 0;
        var merged = MergeMode switch
        {
            CellMergeMode.Sum     => existing + count,
            CellMergeMode.Max     => System.Math.Max(existing, count),
            CellMergeMode.Replace => count,
            _                     => count,
        };

        return new Dictionary<string, object?>(state) { [StateKey] = merged };
    }

    // ── String-set modes (Position / Symbol) ──────────────────────────────

    private Dictionary<string, object?> MergeStringSet(
        Board board,
        Dictionary<string, object?> state,
        Func<int, int, BoardCell, string?> extractor)
    {
        var currentSet = CollectFromBoard(board, extractor);

        if (MergeMode == CellMergeMode.Replace)
        {
            var arr = currentSet.OrderBy(s => s, StringComparer.Ordinal).ToArray();
            return new Dictionary<string, object?>(state) { [StateKey] = arr };
        }

        // Union: merge with existing set
        if (state.TryGetValue(StateKey, out var val) && val is string[] existing)
            foreach (var s in existing) currentSet.Add(s);

        var merged = currentSet.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        return new Dictionary<string, object?>(state) { [StateKey] = merged };
    }

    private HashSet<string> CollectFromBoard(Board board, Func<int, int, BoardCell, string?> extractor)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var r = 0; r < board.Rows; r++)
            for (var c = 0; c < board.Cols; c++)
            {
                var cell = board[r, c];
                if (!Matches(cell)) continue;
                var extracted = extractor(r, c, cell);
                if (extracted != null) set.Add(extracted);
            }
        return set;
    }

    // ── Extractors ────────────────────────────────────────────────────────

    private static string ExtractPosition(int row, int col, BoardCell _) => $"{row},{col}";

    private static string? ExtractSymbol(int _, int __, BoardCell cell) =>
        cell.IsEmpty ? null : cell.Symbols![0];

    // ── Filter ────────────────────────────────────────────────────────────

    private bool Matches(BoardCell cell)
    {
        if (cell.IsEmpty) return false;
        return SymbolFilter == null || cell.Symbols!.Contains(SymbolFilter);
    }
}
