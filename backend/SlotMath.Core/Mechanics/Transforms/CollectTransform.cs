namespace SlotMath.Core.Mechanics.Transforms;

/// <summary>
/// Extracts values from qualifying cells on the board, clears those cells,
/// and accumulates the total into the state as a decimal.
///
/// The predicate selects which cells to collect; the decorationKey names the
/// decoration that holds the numeric amount (parsed as decimal).
/// </summary>
public sealed class CollectTransform : ITransform
{
    private readonly Func<BoardCell, bool> _predicate;
    private readonly string _decorationKey;

    public CollectTransform(Func<BoardCell, bool> predicate, string decorationKey)
    {
        _predicate = predicate;
        _decorationKey = decorationKey;
    }

    public (Board NewBoard, object? NewState) Apply(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);

        var result = board;
        decimal total = state is decimal d ? d : 0m;

        foreach (var (r, c, cell) in board.AllCells())
        {
            if (cell.IsEmpty) continue;
            if (!_predicate(cell)) continue;

            // Extract amount from decoration
            if (cell.Decorations is not null &&
                cell.Decorations.TryGetValue(_decorationKey, out var amountStr) &&
                decimal.TryParse(amountStr, out var amount))
            {
                total += amount;
            }

            // Clear the cell
            result = result.SetCell(r, c, new BoardCell());
        }

        return (result, total);
    }
}
