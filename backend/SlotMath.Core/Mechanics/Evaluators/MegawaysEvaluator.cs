using SlotMath.Core.Model;

namespace SlotMath.Core.Mechanics.Evaluators;

/// <summary>
/// Evaluates wins using the Megaways mechanic: variable-height reels where
/// the number of ways is the product of the active symbol-count in each
/// consecutive column.
///
/// Each column has a specified maximum height (cells beyond the height are
/// ignored).  Within the active height, all positions are eligible.
///
/// Ways = ∏(symbol count in consecutive columns).
/// Payout = ways × paytable payout for the match length.
/// </summary>
public sealed class MegawaysEvaluator : IEvaluator
{
    private readonly Paytable _paytable;
    private readonly int[] _reelHeights;
    private readonly string? _wildSymbolId;

    /// <summary>
    /// Create a Megaways evaluator.
    /// </summary>
    /// <param name="paytable">The paytable.</param>
    /// <param name="reelHeights">Height of each reel (column). Must match board.Cols.</param>
    /// <param name="wildSymbolId">Optional wild symbol that substitutes.</param>
    public MegawaysEvaluator(Paytable paytable, int[] reelHeights, string? wildSymbolId = null)
    {
        _paytable = paytable;
        _reelHeights = reelHeights;
        _wildSymbolId = wildSymbolId;
    }

    public Win[] Evaluate(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (_reelHeights.Length != board.Cols)
            throw new ArgumentException(
                $"Reel heights length ({_reelHeights.Length}) must match board columns ({board.Cols})");

        var wins = new List<Win>();

        foreach (var entry in _paytable.Entries)
        {
            var symbolId = entry.SymbolId;
            var waysPerColumn = new List<int>();
            var allPositions = new List<(int Row, int Col)>();

            for (var col = 0; col < board.Cols; col++)
            {
                var height = _reelHeights[col];
                var count = 0;

                for (var row = 0; row < height && row < board.Rows; row++)
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
                    break; // gap in consecutive columns
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
                        EvaluatorName = "Megaways"
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
