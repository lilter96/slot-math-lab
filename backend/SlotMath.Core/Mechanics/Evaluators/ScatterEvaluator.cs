using SlotMath.Core.Model;

namespace SlotMath.Core.Mechanics.Evaluators;

/// <summary>
/// Evaluates wins using the "scatter / pays-anywhere" mechanic.
///
/// Counts total occurrences of each paytable symbol across the entire board,
/// regardless of position.  If the total count meets a paytable threshold,
/// the win is awarded.
///
/// No adjacency or positional constraints — just total count.
/// </summary>
public sealed class ScatterEvaluator : IEvaluator
{
    private readonly Paytable _paytable;

    public ScatterEvaluator(Paytable paytable)
    {
        _paytable = paytable;
    }

    public Win[] Evaluate(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);
        var wins = new List<Win>();

        foreach (var entry in _paytable.Entries)
        {
            var symbolId = entry.SymbolId;
            var positions = new List<(int Row, int Col)>();
            var count = 0;

            foreach (var (r, c, cell) in board.AllCells())
            {
                if (cell.IsEmpty) continue;
                if (cell.Symbols!.Contains(symbolId))
                {
                    count++;
                    positions.Add((r, c));
                }
            }

            if (count == 0) continue;

            var payout = LookupPayout(symbolId, count);
            if (payout > 0)
            {
                wins.Add(new Win
                {
                    SymbolId = symbolId,
                    Count = count,
                    Positions = positions.ToArray(),
                    Payout = payout,
                    EvaluatorName = "Scatter"
                });
            }
        }

        return wins.ToArray();
    }

    private decimal LookupPayout(string symbolId, int count)
    {
        var entry = _paytable.Entries.FirstOrDefault(e => e.SymbolId == symbolId);
        if (entry == null) return 0;

        // Find the largest count threshold that doesn't exceed actual count
        var best = -1;
        for (var i = 0; i < entry.Counts.Length; i++)
            if (entry.Counts[i] <= count && (best < 0 || entry.Counts[i] > entry.Counts[best]))
                best = i;

        if (best < 0) return 0;
        return decimal.Parse(entry.Payouts[best]);
    }
}
