namespace SlotMath.Core.Mechanics.Transforms;

/// <summary>
/// Grows or shrinks the board by the given number of rows and columns.
/// Positive values add rows/cols at the end; negative values remove from the end.
/// </summary>
public sealed class GrowShrinkTransform : ITransform
{
    private readonly int _rows;
    private readonly int _cols;

    public GrowShrinkTransform(int rows, int cols)
    {
        _rows = rows;
        _cols = cols;
    }

    public (Board NewBoard, object? NewState) Apply(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);

        var result = board;

        // Grow rows
        for (var i = 0; i < _rows; i++)
        {
            var newRow = new BoardCell[result.Cols];
            for (var c = 0; c < result.Cols; c++)
                newRow[c] = new BoardCell();
            result = result.AddRow(result.Rows, newRow);
        }

        // Shrink rows
        for (var i = 0; i < -_rows; i++)
        {
            result = result.RemoveRow(result.Rows - 1);
        }

        // Grow columns
        for (var i = 0; i < _cols; i++)
        {
            var newCol = new BoardCell[result.Rows];
            for (var r = 0; r < result.Rows; r++)
                newCol[r] = new BoardCell();
            result = result.AddColumn(result.Cols, newCol);
        }

        // Shrink columns
        for (var i = 0; i < -_cols; i++)
        {
            result = result.RemoveColumn(result.Cols - 1);
        }

        return (result, state);
    }
}
