namespace SlotMath.Core.Mechanics.Transforms;

/// <summary>
/// Clears cells at winning positions, except for locked cells which are preserved.
/// </summary>
public sealed class RemoveWinningTransform : IFastPathTransform
{
    private readonly IReadOnlySet<(int Row, int Col)> _winningPositions;

    public RemoveWinningTransform(IReadOnlySet<(int Row, int Col)> winningPositions)
    {
        _winningPositions = winningPositions;
    }

    public (Board NewBoard, object? NewState) Apply(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);

        var result = board;
        foreach (var (r, c) in _winningPositions)
        {
            var cell = result[r, c];
            if (!cell.IsLocked)
                result = result.SetCell(r, c, new BoardCell());
        }

        return (result, state);
    }
}
