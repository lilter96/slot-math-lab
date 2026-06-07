namespace SlotMath.Core.Mechanics.Transforms;

/// <summary>
/// Adds a row or column at the specified position, populated with given cells.
/// </summary>
public sealed class ExpandTransform : ITransform
{
    private readonly ExpandDirection _direction;
    private readonly int _position;
    private readonly BoardCell[] _cells;

    public ExpandTransform(ExpandDirection direction, int position, BoardCell[] cells)
    {
        _direction = direction;
        _position = position;
        _cells = cells;
    }

    public (Board NewBoard, object? NewState) Apply(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);

        Board result = _direction == ExpandDirection.Row
            ? board.AddRow(_position, _cells)
            : board.AddColumn(_position, _cells);

        return (result, state);
    }
}
