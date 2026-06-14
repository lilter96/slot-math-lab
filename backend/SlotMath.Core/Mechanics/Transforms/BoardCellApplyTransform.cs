namespace SlotMath.Core.Mechanics.Transforms;

/// <summary>
/// Reads accumulated cell data from state under <see cref="StateKey"/> and
/// applies it to the board per <see cref="ApplyMode"/>.
///
/// State is returned UNCHANGED — only the board is modified.
/// Pair with <see cref="BoardCellAccumulatorTransform"/> to compose any
/// "collect now, re-apply later" mechanic without writing game-specific code:
///   draw → BoardCellAccumulator → BoardCellApply → evaluator
///
/// Configurable parameters — all exposed as UI properties:
///   StateKey  — dictionary key to read position data from (string[] of "row,col")
///   ApplyMode — what to do at each accumulated position:
///                 OverlaySymbol → set cell to SymbolId
///                 LockCells     → set cell.IsLocked = true
///   SymbolId  — required for OverlaySymbol mode; unused for others
/// </summary>
public sealed class BoardCellApplyTransform : IFastPathTransform
{
    public string StateKey { get; }
    public CellApplyMode ApplyMode { get; }
    public string? SymbolId { get; }

    public BoardCellApplyTransform(string stateKey, CellApplyMode applyMode, string? symbolId = null)
    {
        ArgumentNullException.ThrowIfNull(stateKey);
        if (applyMode == CellApplyMode.OverlaySymbol)
            ArgumentNullException.ThrowIfNull(symbolId,
                "SymbolId is required when ApplyMode = OverlaySymbol");
        StateKey  = stateKey;
        ApplyMode = applyMode;
        SymbolId  = symbolId;
    }

    public (Board NewBoard, object? NewState) Apply(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);

        var stateDict = state as Dictionary<string, object?>;
        if (stateDict == null
            || !stateDict.TryGetValue(StateKey, out var val)
            || val is not string[] positions
            || positions.Length == 0)
            return (board, state);

        var newBoard = board;
        foreach (var s in positions)
        {
            var comma = s.IndexOf(',');
            if (comma <= 0) continue;
            if (!int.TryParse(s.AsSpan(0, comma), out var r)
                || !int.TryParse(s.AsSpan(comma + 1), out var c)) continue;
            if ((uint)r >= (uint)newBoard.Rows || (uint)c >= (uint)newBoard.Cols) continue;

            newBoard = ApplyMode switch
            {
                CellApplyMode.OverlaySymbol => ApplyOverlay(newBoard, r, c),
                CellApplyMode.LockCells     => ApplyLock(newBoard, r, c),
                _                           => newBoard,
            };
        }

        return (newBoard, state);
    }

    private Board ApplyOverlay(Board board, int r, int c)
    {
        var cell = board[r, c];
        if (!cell.IsEmpty && cell.Symbols![0] == SymbolId) return board;
        return board.SetCell(r, c, new BoardCell { Symbols = new[] { SymbolId! } });
    }

    private static Board ApplyLock(Board board, int r, int c)
    {
        var cell = board[r, c];
        return cell.IsLocked ? board : board.SetCell(r, c, cell.WithLocked(true));
    }
}
