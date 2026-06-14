using SlotMath.Core.Model;

namespace SlotMath.Core.Mechanics.Evaluators;

/// <summary>
/// Evaluates wins along configurable paylines (left-to-right matching).
///
/// For each payline, reads the symbols column-by-column. The first symbol
/// (or WILD) starts the match; subsequent columns extend it if the symbol
/// matches or WILD substitutes.  The match stops at the first non-matching,
/// non-wild symbol.  The resulting count is looked up in the paytable.
///
/// Multiple paylines can produce multiple wins for the same symbol.
/// </summary>
public sealed class LinesEvaluator : IFastPathEvaluator
{
    private readonly Paytable _paytable;
    private readonly PaylineSet _paylineSet;
    private readonly string? _wildSymbolId;

    public LinesEvaluator(Paytable paytable, PaylineSet paylineSet, string? wildSymbolId = null)
    {
        _paytable = paytable;
        _paylineSet = paylineSet;
        _wildSymbolId = wildSymbolId;
    }

    public Win[] Evaluate(IReadOnlyDictionary<string, object?> state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var cells = GridState.Cells(state);
        var cols = GridState.Cols(state);
        var wins = new List<Win>();

        foreach (var payline in _paylineSet.Paylines)
        {
            var positions = payline.Positions;
            if (positions.Length == 0) continue;

            // Get first symbol on the payline
            var firstCell = GetCellAt(cells, cols, 0, positions);
            if (GridState.IsEmpty(firstCell)) continue;

            var firstSym = GridState.Symbol(firstCell);
            var matchSymbol = firstSym == _wildSymbolId ? null : firstSym;

            var matchedPositions = new List<(int, int)> { (positions[0], 0) };
            var count = 1;

            for (var col = 1; col < positions.Length; col++)
            {
                var cell = GetCellAt(cells, cols, col, positions);
                if (GridState.IsEmpty(cell)) break;

                var sym = GridState.Symbol(cell);
                if (sym == _wildSymbolId || (matchSymbol != null && sym == matchSymbol))
                {
                    // WILD starts the match if firstSym was WILD
                    if (matchSymbol == null && sym != _wildSymbolId)
                        matchSymbol = sym;

                    matchedPositions.Add((positions[col], col));
                    count++;
                }
                else
                {
                    break;
                }
            }

            if (matchSymbol != null && count > 0)
            {
                var payout = LookupPayout(matchSymbol, count);
                if (payout > 0)
                {
                    wins.Add(new Win
                    {
                        SymbolId = matchSymbol,
                        Count = count,
                        Positions = matchedPositions.ToArray(),
                        Payout = payout,
                        EvaluatorName = "Lines"
                    });
                }
            }
        }

        return wins.ToArray();
    }

    private static object? GetCellAt(object?[] cells, int cols, int column, int[] positions)
    {
        var row = positions[column];
        var idx = GridState.Index(row, column, cols);
        return idx >= 0 && idx < cells.Length ? cells[idx] : null;
    }

    private decimal LookupPayout(string symbolId, int count)
    {
        var entry = _paytable.Entries.FirstOrDefault(e => e.SymbolId == symbolId);
        if (entry == null) return 0;

        var idx = Array.IndexOf(entry.Counts, count);
        if (idx < 0) return 0;

        return decimal.Parse(entry.Payouts[idx]);
    }
}
