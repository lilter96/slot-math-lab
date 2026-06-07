namespace SlotMath.Core.Mechanics.Transforms;

/// <summary>
/// Shifts a single column up or down by one position.
/// Locked cells stay in place and block the nudge for their position.
/// </summary>
public sealed class NudgeTransform : ITransform
{
    private readonly int _column;
    private readonly NudgeDirection _direction;

    public NudgeTransform(int column, NudgeDirection direction)
    {
        _column = column;
        _direction = direction;
    }

    public (Board NewBoard, object? NewState) Apply(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);

        var rows = board.Rows;
        var col = board.GetColumn(_column);
        var newCol = new BoardCell[rows];

        if (_direction == NudgeDirection.Down)
        {
            // Row 0 gets empty, rows 1..n-1 shift down from 0..n-2
            newCol[0] = new BoardCell();
            for (var r = 0; r < rows - 1; r++)
            {
                newCol[r + 1] = col[r].IsLocked ? col[r] : col[r];
            }
            // Actually: shift non-locked cells down, locked stay
            // Simpler: shift all cells down, but locked cells at destination block
            for (var r = rows - 1; r > 0; r--)
            {
                if (col[r].IsLocked) continue;        // locked stays
                var src = col[r - 1];
                if (src.IsLocked) continue;            // locked source doesn't move
                newCol[r] = src;
            }
        }
        else // Up
        {
            // Row n-1 gets empty, rows 0..n-2 shift up from 1..n-1
            newCol[rows - 1] = new BoardCell();
            for (var r = 0; r < rows - 1; r++)
            {
                if (col[r].IsLocked) continue;        // locked stays
                var src = col[r + 1];
                if (src.IsLocked) continue;            // locked source doesn't move
                newCol[r] = src;
            }
        }

        // Preserve locked cells at their positions
        for (var r = 0; r < rows; r++)
        {
            if (col[r].IsLocked)
                newCol[r] = col[r];
        }

        return (board.SetColumn(_column, newCol), state);
    }
}
