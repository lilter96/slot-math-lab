using SlotMath.Core.Model;

namespace SlotMath.Core.Mechanics.Evaluators;

/// <summary>
/// Evaluates wins using flood-fill clustering with orthogonal adjacency.
///
/// Symbols of the same kind that are orthogonally adjacent (up/down/left/right)
/// form a cluster.  WILD symbols connect with any other symbol, extending
/// the cluster.  Clusters below the minimum size are ignored.
///
/// Payout is per-cluster from the paytable lookup by cluster size.
/// </summary>
public sealed class ClusterEvaluator : IFastPathEvaluator
{
    private readonly Paytable _paytable;
    private readonly int _minClusterSize;
    private readonly string? _wildSymbolId;

    public ClusterEvaluator(Paytable paytable, int minClusterSize = 3, string? wildSymbolId = null)
    {
        _paytable = paytable;
        _minClusterSize = minClusterSize;
        _wildSymbolId = wildSymbolId;
    }

    public Win[] Evaluate(IReadOnlyDictionary<string, object?> state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var cells = GridState.Cells(state);
        var rows = GridState.Rows(state);
        var cols = GridState.Cols(state);
        var visited = new bool[rows, cols];
        var wins = new List<Win>();

        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                if (visited[r, c]) continue;
                var cell = cells[GridState.Index(r, c, cols)];
                if (GridState.IsEmpty(cell)) continue;

                var sym = GridState.Symbol(cell);
                if (sym == _wildSymbolId) continue; // wilds are connectors, not cluster starters

                // Flood-fill this cluster
                var cluster = FloodFill(cells, rows, cols, r, c, sym, visited);
                if (cluster.Count >= _minClusterSize)
                {
                    var payout = LookupPayout(sym, cluster.Count);
                    if (payout > 0)
                    {
                        wins.Add(new Win
                        {
                            SymbolId = sym,
                            Count = cluster.Count,
                            Positions = cluster.Select(p => (p.Row, p.Col)).ToArray(),
                            Payout = payout,
                            EvaluatorName = "Cluster"
                        });
                    }
                }
            }

        return wins.ToArray();
    }

    private List<(int Row, int Col)> FloodFill(object?[] cells, int rows, int cols,
        int startR, int startC, string targetSymbol, bool[,] visited)
    {
        var cluster = new List<(int Row, int Col)>();
        var queue = new Queue<(int Row, int Col)>();
        queue.Enqueue((startR, startC));
        visited[startR, startC] = true;

        var dirs = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) }; // orthogonal only

        while (queue.Count > 0)
        {
            var (r, c) = queue.Dequeue();
            cluster.Add((r, c));

            foreach (var (dr, dc) in dirs)
            {
                var nr = r + dr;
                var nc = c + dc;
                if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
                if (visited[nr, nc]) continue;

                var cell = cells[GridState.Index(nr, nc, cols)];
                if (GridState.IsEmpty(cell)) continue;

                var sym = GridState.Symbol(cell);
                if (sym == targetSymbol || sym == _wildSymbolId)
                {
                    visited[nr, nc] = true;
                    queue.Enqueue((nr, nc));
                }
            }
        }

        return cluster;
    }

    private decimal LookupPayout(string symbolId, int count)
    {
        var entry = _paytable.Entries.FirstOrDefault(e => e.SymbolId == symbolId);
        if (entry == null) return 0;
        var idx = Array.IndexOf(entry.Counts, count);
        if (idx < 0)
        {
            // Use the largest count that doesn't exceed the cluster size
            var best = -1;
            for (var i = 0; i < entry.Counts.Length; i++)
                if (entry.Counts[i] <= count && (best < 0 || entry.Counts[i] > entry.Counts[best]))
                    best = i;
            if (best < 0) return 0;
            idx = best;
        }
        return decimal.Parse(entry.Payouts[idx]);
    }
}
