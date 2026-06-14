namespace SlotMath.Core.Mechanics.Transforms;

/// <summary>
/// Reads accumulated cell data from state under <see cref="StateKey"/> and
/// applies it to the board per <see cref="ApplyMode"/>.
///
/// State is returned UNCHANGED — only the board is modified.
/// Pair with <see cref="BoardCellAccumulatorTransform"/> to compose any
/// "collect now, re-apply later" mechanic without writing game-specific code:
///   draw → BoardCellAccumulator → BoardCellApply → evaluator
///
/// Configurable parameters — all exposed as UI properties:
///   StateKey  — dictionary key to read position data from (string[] of "row,col")
///   ApplyMode — what to do at each accumulated position:
///                 OverlaySymbol → set cell to SymbolId
///                 LockCells     → set cell.IsLocked = true
///   SymbolId  — required for OverlaySymbol mode; unused for others
/// </summary>
public sealed class BoardCellApplyTransform : IFastPathTransform
{
    public string StateKey { get; }
    public CellApplyMode ApplyMode { get; }
    public string? SymbolId { get; }

    public BoardCellApplyTransform(string stateKey, CellApplyMode applyMode, string? symbolId = null)
    {
        ArgumentNullException.ThrowIfNull(stateKey);
        if (applyMode == CellApplyMode.OverlaySymbol)
            ArgumentNullException.ThrowIfNull(symbolId,
                "SymbolId is required when ApplyMode = OverlaySymbol");
        StateKey  = stateKey;
        ApplyMode = applyMode;
        SymbolId  = symbolId;
    }

    public IReadOnlyDictionary<string, object?> Apply(IReadOnlyDictionary<string, object?> state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.TryGetValue(StateKey, out var val)
            || val is not string[] positions
            || positions.Length == 0)
            return state;

        var cells = GridState.Cells(state);
        if (cells.Length == 0) return state;
        var cols = GridState.Cols(state);
        if (cols <= 0) cols = cells.Length;
        var rows = cols > 0 ? cells.Length / cols : 1;

        // Copy-on-write the flat cell array (immutable state, D17).
        var next = (object?[])cells.Clone();
        var changed = false;
        foreach (var s in positions)
        {
            var comma = s.IndexOf(',');
            if (comma <= 0) continue;
            if (!int.TryParse(s.AsSpan(0, comma), out var r)
                || !int.TryParse(s.AsSpan(comma + 1), out var c)) continue;
            if ((uint)r >= (uint)rows || (uint)c >= (uint)cols) continue;

            var idx = GridState.Index(r, c, cols);
            var updated = ApplyMode switch
            {
                CellApplyMode.OverlaySymbol => ApplyOverlay(next[idx]),
                CellApplyMode.LockCells     => ApplyLock(next[idx]),
                _                           => next[idx],
            };
            if (!ReferenceEquals(updated, next[idx])) { next[idx] = updated; changed = true; }
        }

        return changed ? GridState.With(state, GridState.CellsKey, next) : state;
    }

    private object? ApplyOverlay(object? cell)
    {
        // Overlay sets the symbol; a record cell keeps its other fields.
        if (GridState.Symbol(cell) == SymbolId) return cell;
        return cell is IReadOnlyDictionary<string, object?> d
            ? new Dictionary<string, object?>(d) { [GridState.SymbolField] = SymbolId! }
            : SymbolId!;
    }

    private static object? ApplyLock(object? cell)
    {
        if (GridState.IsLocked(cell)) return cell;
        // Locking requires a record cell to carry the flag.
        var record = cell is IReadOnlyDictionary<string, object?> d
            ? new Dictionary<string, object?>(d)
            : new Dictionary<string, object?> { [GridState.SymbolField] = GridState.Symbol(cell) };
        record[GridState.LockedField] = true;
        return record;
    }
}
