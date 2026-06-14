namespace SlotMath.Core.Mechanics.Transforms;

/// <summary>
/// Fills empty cells via a tumble/cascade mechanic: existing symbols fall down
/// to the lowest available empty position in each column, then new symbols
/// from the provided source fill the remaining empty cells from the top.
///
/// Locked cells stay in place and act as obstacles.
///
/// The symbol source is a pure function (no randomness — the caller pre-generates
/// symbols or passes a deterministic function).  This keeps the transform pure
/// and testable under the exact interpreter.
/// </summary>
public sealed class RefillTumbleTransform : IFastPathTransform
{
    private readonly Func<string[]> _newSymbolSource;

    /// <summary>
    /// Create a tumble transform.
    /// </summary>
    /// <param name="newSymbolSource">
    /// Pure function returning symbols for filling empty cells.  Called once per
    /// empty cell that needs filling, in column-major order from top to bottom.
    /// </param>
    public RefillTumbleTransform(Func<string[]> newSymbolSource)
    {
        _newSymbolSource = newSymbolSource;
    }

    public (Board NewBoard, object? NewState) Apply(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);

        var result = board;
        var newSymbols = _newSymbolSource();
        var symbolIndex = 0;

        for (var c = 0; c < result.Cols; c++)
        {
            // Collect non-empty, non-locked symbols in this column from bottom to top
            var columnSymbols = new List<BoardCell>();
            for (var r = result.Rows - 1; r >= 0; r--)
            {
                var cell = result[r, c];
                if (!cell.IsEmpty && !cell.IsLocked)
                {
                    columnSymbols.Add(cell);
                }
            }

            // Fill column from bottom with existing symbols, then new ones
            var fillIndex = 0;
            for (var r = result.Rows - 1; r >= 0; r--)
            {
                var cell = result[r, c];
                if (cell.IsLocked)
                    continue; // locked cells stay

                if (fillIndex < columnSymbols.Count)
                {
                    result = result.SetCell(r, c, columnSymbols[fillIndex]);
                    fillIndex++;
                }
                else
                {
                    // Fill with new symbol
                    var sym = symbolIndex < newSymbols.Length
                        ? newSymbols[symbolIndex++]
                        : "?";
                    result = result.SetCell(r, c,
                        new BoardCell { Symbols = new[] { sym } });
                }
            }
        }

        return (result, state);
    }
}
