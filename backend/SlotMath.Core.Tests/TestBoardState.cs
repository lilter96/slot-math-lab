using SlotMath.Core.Mechanics;

namespace SlotMath.Core.Tests;

/// <summary>
/// Test helper for building a board-in-state dictionary (invariant 4: the engine
/// has no Board type — a board is a flat row-major array in state S, with its
/// dimensions, read via <see cref="GridState"/>).
/// </summary>
public static class TestBoardState
{
    /// <summary>Build a state dict from a 2D row-major symbol grid.</summary>
    public static Dictionary<string, object?> From(string?[][] grid, params (string Key, object? Value)[] extra)
    {
        var rows = grid.Length;
        var cols = rows > 0 ? grid[0].Length : 0;
        var flat = new object?[rows * cols];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
                flat[GridState.Index(r, c, cols)] = grid[r][c];

        var state = new Dictionary<string, object?>
        {
            [GridState.CellsKey] = flat,
            [GridState.RowsKey] = rows,
            [GridState.ColsKey] = cols,
        };
        foreach (var (key, value) in extra)
            state[key] = value;
        return state;
    }

    /// <summary>Build a state dict from explicit dimensions and a flat cell array.</summary>
    public static Dictionary<string, object?> Flat(int rows, int cols, object?[] cells,
        params (string Key, object? Value)[] extra)
    {
        var state = new Dictionary<string, object?>
        {
            [GridState.CellsKey] = cells,
            [GridState.RowsKey] = rows,
            [GridState.ColsKey] = cols,
        };
        foreach (var (key, value) in extra)
            state[key] = value;
        return state;
    }
}
