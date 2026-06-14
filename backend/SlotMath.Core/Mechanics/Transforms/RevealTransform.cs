namespace SlotMath.Core.Mechanics.Transforms;

/// <summary>
/// Marks all non-empty cells as "revealed" by setting the "revealed"
/// decoration to "true".  Empty and already-revealed cells are left unchanged.
/// </summary>
public sealed class RevealTransform : IFastPathTransform
{
    public (Board NewBoard, object? NewState) Apply(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);

        var result = board;
        foreach (var (r, c, cell) in board.AllCells())
        {
            if (!cell.IsEmpty && cell.GetDecoration("revealed") != "true")
            {
                result = result.SetCell(r, c, cell.WithDecoration("revealed", "true"));
            }
        }

        return (result, state);
    }
}
