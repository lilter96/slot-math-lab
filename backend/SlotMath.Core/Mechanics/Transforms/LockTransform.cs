namespace SlotMath.Core.Mechanics.Transforms;

/// <summary>
/// Locks the specified board positions, making cells sticky (unremovable).
/// </summary>
public sealed class LockTransform : ITransform
{
    private readonly IReadOnlySet<(int Row, int Col)> _positions;

    public LockTransform(IEnumerable<(int Row, int Col)> positions)
    {
        _positions = new HashSet<(int, int)>(positions);
    }

    public (Board NewBoard, object? NewState) Apply(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);

        var result = board;
        foreach (var (r, c) in _positions)
        {
            var cell = result[r, c];
            if (!cell.IsLocked)
                result = result.SetCell(r, c, cell.WithLocked(true));
        }

        return (result, state);
    }
}
