using SlotMath.Core.Model;

namespace SlotMath.Core.Mechanics.Evaluators;

/// <summary>
/// Evaluates wins using the "ways" (payways) mechanic: left-to-right,
/// any position in each column counts.  The number of winning ways for a
/// symbol is the product of the count of that symbol in each consecutive
/// column (including WILD substitutions).
///
/// Payout = (number of ways) × (paytable payout for the match length).
/// </summary>
public sealed class WaysEvaluator : IEvaluator
{
    private readonly Paytable _paytable;
    private readonly string? _wildSymbolId;

    public WaysEvaluator(Paytable paytable, string? wildSymbolId = null)
    {
        _paytable = paytable;
        _wildSymbolId = wildSymbolId;
    }

    public Win[] Evaluate(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);
        var wins = new List<Win>();

        // For each symbol in the paytable, count consecutive columns
        foreach (var entry in _paytable.Entries)
        {
            var symbolId = entry.SymbolId;
            var waysPerColumn = new List<int>();
            var allPositions = new List<(int Row, int Col)>();

            for (var col = 0; col < board.Cols; col++)
            {
                var count = 0;
                for (var row = 0; row < board.Rows; row++)
                {
                    var cell = board[row, col];
                    if (cell.IsEmpty) continue;
                    var sym = cell.Symbols![0];
                    if (sym == symbolId || sym == _wildSymbolId)
                    {
                        count++;
                        allPositions.Add((row, col));
                    }
                }

                if (count > 0)
                {
                    waysPerColumn.Add(count);
                }
                else
                {
                    break; // gap — stop counting consecutive columns
                }
            }

            if (waysPerColumn.Count > 0)
            {
                var totalWays = waysPerColumn.Aggregate(1, (a, b) => a * b);
                var payout = LookupPayout(symbolId, waysPerColumn.Count);
                if (payout > 0)
                {
                    wins.Add(new Win
                    {
                        SymbolId = symbolId,
                        Count = waysPerColumn.Count,
                        Positions = allPositions.ToArray(),
                        Payout = payout * totalWays,
                        EvaluatorName = "Ways"
                    });
                }
            }
        }

        return wins.ToArray();
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
