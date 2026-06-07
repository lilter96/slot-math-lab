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
public sealed class LinesEvaluator : IEvaluator
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

    public Win[] Evaluate(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);
        var wins = new List<Win>();

        foreach (var payline in _paylineSet.Paylines)
        {
            var positions = payline.Positions;
            if (positions.Length == 0) continue;

            // Get first symbol on the payline
            var firstCell = GetCellAt(board, 0, positions);
            if (firstCell.IsEmpty) continue;

            var firstSym = firstCell.Symbols![0];
            var matchSymbol = firstSym == _wildSymbolId ? null : firstSym;

            var matchedPositions = new List<(int, int)> { (positions[0], 0) };
            var count = 1;

            for (var col = 1; col < positions.Length; col++)
            {
                var cell = GetCellAt(board, col, positions);
                if (cell.IsEmpty) break;

                var sym = cell.Symbols![0];
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

    private static BoardCell GetCellAt(Board board, int column, int[] positions)
    {
        var row = positions[column];
        return board[row, column];
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
