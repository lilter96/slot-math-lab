namespace SlotMath.Core.Mechanics.Transforms;

/// <summary>
/// Replaces symbols on the board according to a mapping.
/// Multi-symbol cells have each symbol checked against the map.
/// Symbols not in the map are left as-is.
/// </summary>
public sealed class MorphTransform : ITransform
{
    private readonly IReadOnlyDictionary<string, string> _mapping;

    public MorphTransform(IReadOnlyDictionary<string, string> mapping)
    {
        _mapping = mapping;
    }

    public (Board NewBoard, object? NewState) Apply(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);

        var result = board;
        foreach (var (r, c, cell) in board.AllCells())
        {
            if (cell.IsEmpty || cell.Symbols is not { Length: > 0 } symbols)
                continue;

            var newSymbols = new string[symbols.Length];
            var changed = false;

            for (var i = 0; i < symbols.Length; i++)
            {
                if (_mapping.TryGetValue(symbols[i], out var replacement))
                {
                    newSymbols[i] = replacement;
                    changed = true;
                }
                else
                {
                    newSymbols[i] = symbols[i];
                }
            }

            if (changed)
                result = result.SetCell(r, c, cell.WithSymbols(newSymbols));
        }

        return (result, state);
    }
}
