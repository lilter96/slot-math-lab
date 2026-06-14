using SlotMath.Core.Mechanics;
using SlotMath.Core.Mechanics.Evaluators;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Mechanics;

public class StandardEvaluatorsTests
{
    // Helper: create a paytable from a flat dictionary of symbol→(counts, payouts)
    private static Paytable MakePaytable(params (string SymbolId, int[] Counts, decimal[] Payouts)[] entries)
    {
        return new Paytable
        {
            Id = "test",
            Entries = entries.Select(e => new PaytableEntry
            {
                SymbolId = e.SymbolId,
                Counts = e.Counts,
                Payouts = e.Payouts.Select(p => p.ToString("G")).ToArray()
            }).ToArray()
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  LinesEvaluator
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Lines_3xA_OnCenterPayline_HandComputed()
    {
        // Board 3×3, all A's on center row (row 1)
        var board = new Board(3, 3)
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 2, new BoardCell { Symbols = new[] { "A" } });

        var paytable = MakePaytable(("A", new[] { 3, 4, 5 }, new[] { 10m, 25m, 100m }));

        // Payline: center row = [1,1,1] (row index per column)
        var paylines = new PaylineSet
        {
            Id = "test-lines",
            Paylines = new[]
            {
                new Payline { Positions = new[] { 1, 1, 1 } } // center row
            }
        };

        var evaluator = new LinesEvaluator(paytable, paylines);
        var wins = evaluator.Evaluate(board, null);

        // Hand-computed: 3×A on 1 payline → 10 payout
        Assert.Single(wins);
        Assert.Equal("A", wins[0].SymbolId);
        Assert.Equal(3, wins[0].Count);
        Assert.Equal(10m, wins[0].Payout);
        Assert.Equal(3, wins[0].Positions.Length);
    }

    [Fact]
    public void Lines_NoMatch_ReturnsEmpty()
    {
        var board = new Board(3, 3)
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "B" } })
            .SetCell(1, 2, new BoardCell { Symbols = new[] { "C" } });

        var paytable = MakePaytable(("A", new[] { 3 }, new[] { 10m }));

        var paylines = new PaylineSet
        {
            Id = "test",
            Paylines = new[] { new Payline { Positions = new[] { 1, 1, 1 } } }
        };

        var evaluator = new LinesEvaluator(paytable, paylines);
        var wins = evaluator.Evaluate(board, null);
        Assert.Empty(wins);
    }

    [Fact]
    public void Lines_WildSubstitutes_ForMatchingSymbol()
    {
        // WILD in col 1 substitutes for A to make 3×A on center row
        var board = new Board(3, 3)
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "WILD" } })
            .SetCell(1, 2, new BoardCell { Symbols = new[] { "A" } });

        var paytable = MakePaytable(("A", new[] { 3 }, new[] { 10m }));

        var paylines = new PaylineSet
        {
            Id = "test",
            Paylines = new[] { new Payline { Positions = new[] { 1, 1, 1 } } }
        };

        var evaluator = new LinesEvaluator(paytable, paylines, wildSymbolId: "WILD");
        var wins = evaluator.Evaluate(board, null);

        Assert.Single(wins);
        Assert.Equal("A", wins[0].SymbolId);
        Assert.Equal(3, wins[0].Count);
        Assert.Equal(10m, wins[0].Payout);
    }

    [Fact]
    public void Lines_6x4_Board_WithDiagonalPayline()
    {
        // 6 rows × 4 cols. Payline: descending diagonal [0,1,2,3]
        var board = new Board(6, 4)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "K" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "K" } })
            .SetCell(2, 2, new BoardCell { Symbols = new[] { "K" } })
            .SetCell(3, 3, new BoardCell { Symbols = new[] { "K" } });

        var paytable = MakePaytable(("K", new[] { 3, 4 }, new[] { 15m, 50m }));

        var paylines = new PaylineSet
        {
            Id = "diagonal",
            Paylines = new[]
            {
                new Payline { Positions = new[] { 0, 1, 2, 3 } } // descending diagonal
            }
        };

        var evaluator = new LinesEvaluator(paytable, paylines);
        var wins = evaluator.Evaluate(board, null);

        Assert.Single(wins);
        Assert.Equal("K", wins[0].SymbolId);
        Assert.Equal(4, wins[0].Count);
        Assert.Equal(50m, wins[0].Payout);
    }

    [Fact]
    public void Lines_MultiplePaylines_MultipleWins()
    {
        // Board 3×3: top row AAA, bottom row BBB
        var board = new Board(3, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 2, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(2, 0, new BoardCell { Symbols = new[] { "B" } })
            .SetCell(2, 1, new BoardCell { Symbols = new[] { "B" } })
            .SetCell(2, 2, new BoardCell { Symbols = new[] { "B" } });

        var paytable = MakePaytable(
            ("A", new[] { 3, 4, 5 }, new[] { 5m, 10m, 20m }),
            ("B", new[] { 3, 4, 5 }, new[] { 8m, 16m, 32m })
        );

        var paylines = new PaylineSet
        {
            Id = "top-and-bottom",
            Paylines = new[]
            {
                new Payline { Positions = new[] { 0, 0, 0 } }, // top row
                new Payline { Positions = new[] { 2, 2, 2 } }, // bottom row
            }
        };

        var evaluator = new LinesEvaluator(paytable, paylines);
        var wins = evaluator.Evaluate(board, null);

        Assert.Equal(2, wins.Length);
        Assert.Contains(wins, w => w.SymbolId == "A" && w.Payout == 5m);
        Assert.Contains(wins, w => w.SymbolId == "B" && w.Payout == 8m);
    }

    [Fact]
    public void Lines_IsPure()
    {
        var board = new Board(3, 3)
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 2, new BoardCell { Symbols = new[] { "A" } });

        var paytable = MakePaytable(("A", new[] { 3 }, new[] { 10m }));
        var paylines = new PaylineSet
        {
            Id = "p",
            Paylines = new[] { new Payline { Positions = new[] { 1, 1, 1 } } }
        };
        var eval = new LinesEvaluator(paytable, paylines);

        var w1 = eval.Evaluate(board, null);
        var w2 = eval.Evaluate(board, null);
        Assert.Equal(w1.Length, w2.Length);
        for (var i = 0; i < w1.Length; i++)
            Assert.Equal(w1[i], w2[i]);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  WaysEvaluator
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ways_3Columns_EachWithOneA_HandComputed()
    {
        // Left-to-right: any A in each column wins
        var board = new Board(3, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(2, 2, new BoardCell { Symbols = new[] { "A" } });

        var paytable = MakePaytable(("A", new[] { 3, 4, 5 }, new[] { 10m, 25m, 100m }));

        var eval = new WaysEvaluator(paytable);
        var wins = eval.Evaluate(board, null);

        // 1 way × 3 symbols → payout 10
        Assert.Single(wins);
        Assert.Equal("A", wins[0].SymbolId);
        Assert.Equal(3, wins[0].Count);
        Assert.Equal(10m, wins[0].Payout);
    }

    [Fact]
    public void Ways_MultiplePositionsPerColumn_MultipliesWays()
    {
        // 2 A's in col 0, 1 A in col 1, 2 A's in col 2 → 2×1×2 = 4 ways
        var board = new Board(3, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "A" } }) // 2nd A in col 0
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "A" } }) // 1 A in col 1
            .SetCell(0, 2, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 2, new BoardCell { Symbols = new[] { "A" } }); // 2nd A in col 2

        var paytable = MakePaytable(("A", new[] { 3 }, new[] { 10m }));

        var eval = new WaysEvaluator(paytable);
        var wins = eval.Evaluate(board, null);

        // 2×1×2 = 4 ways, each paying 10
        Assert.Single(wins);
        Assert.Equal("A", wins[0].SymbolId);
        Assert.Equal(3, wins[0].Count);
        Assert.Equal(40m, wins[0].Payout); // 4 ways × 10 per way
    }

    [Fact]
    public void Ways_GapInColumns_NoWin()
    {
        // A in col 0, no A in col 1 → break, no win
        var board = new Board(3, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            // col 1: no A
            .SetCell(0, 2, new BoardCell { Symbols = new[] { "A" } });

        var paytable = MakePaytable(("A", new[] { 3 }, new[] { 10m }));

        var eval = new WaysEvaluator(paytable);
        var wins = eval.Evaluate(board, null);

        // Only 1 consecutive A → doesn't meet min count of 3
        Assert.Empty(wins);
    }

    [Fact]
    public void Ways_6x4_Board_HandComputed()
    {
        // 6×4 board: A on all 4 columns in various rows
        var board = new Board(6, 4)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(2, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 2, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(5, 3, new BoardCell { Symbols = new[] { "A" } });

        var paytable = MakePaytable(("A", new[] { 3, 4 }, new[] { 10m, 50m }));
        var eval = new WaysEvaluator(paytable);
        var wins = eval.Evaluate(board, null);

        Assert.Single(wins);
        Assert.Equal("A", wins[0].SymbolId);
        Assert.Equal(4, wins[0].Count); // 4 consecutive columns
        Assert.Equal(50m, wins[0].Payout); // 4-of-a-kind = 50
    }

    [Fact]
    public void Ways_IsPure()
    {
        var board = new Board(3, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 2, new BoardCell { Symbols = new[] { "A" } });
        var paytable = MakePaytable(("A", new[] { 3 }, new[] { 10m }));
        var eval = new WaysEvaluator(paytable);

        var w1 = eval.Evaluate(board, null);
        var w2 = eval.Evaluate(board, null);
        Assert.Equal(w1.Length, w2.Length);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ClusterEvaluator (flood-fill)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Cluster_2x2Block_HandComputed()
    {
        // 2×2 block of A's
        var board = new Board(4, 4)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "A" } });

        var paytable = MakePaytable(("A", new[] { 3, 4, 5, 6 }, new[] { 2m, 10m, 25m, 50m }));

        var eval = new ClusterEvaluator(paytable, minClusterSize: 3);
        var wins = eval.Evaluate(board, null);

        // Single cluster of 4 A's
        Assert.Single(wins);
        Assert.Equal("A", wins[0].SymbolId);
        Assert.Equal(4, wins[0].Count);
        Assert.Equal(10m, wins[0].Payout);
        Assert.Equal(4, wins[0].Positions.Length);
    }

    [Fact]
    public void Cluster_TwoSeparateGroups_TwoWins()
    {
        // Two separate 3-A clusters
        var board = new Board(4, 4)
            // Cluster 1: top-left
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "A" } })
            // Gap at (1,1), (2,3)
            // Cluster 2: bottom-right
            .SetCell(2, 2, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(2, 3, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(3, 3, new BoardCell { Symbols = new[] { "A" } });

        var paytable = MakePaytable(("A", new[] { 3, 4 }, new[] { 5m, 15m }));
        var eval = new ClusterEvaluator(paytable, minClusterSize: 3);
        var wins = eval.Evaluate(board, null);

        Assert.Equal(2, wins.Length);
        Assert.All(wins, w => Assert.Equal("A", w.SymbolId));
        Assert.All(wins, w => Assert.Equal(3, w.Count));
    }

    [Fact]
    public void Cluster_BelowMinSize_NoWin()
    {
        var board = new Board(3, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "A" } }); // only 2 adjacent

        var paytable = MakePaytable(("A", new[] { 2, 3 }, new[] { 1m, 5m }));
        var eval = new ClusterEvaluator(paytable, minClusterSize: 3);
        var wins = eval.Evaluate(board, null);

        Assert.Empty(wins);
    }

    [Fact]
    public void Cluster_DiagonalOnly_NotConnected()
    {
        // Diagonal adjacency should NOT connect (only orthogonal)
        var board = new Board(3, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(2, 2, new BoardCell { Symbols = new[] { "A" } });

        var paytable = MakePaytable(("A", new[] { 3 }, new[] { 5m }));
        var eval = new ClusterEvaluator(paytable, minClusterSize: 3);
        var wins = eval.Evaluate(board, null);

        // Diagonal-only adjacency → each is isolated cluster of 1 → below min size
        Assert.Empty(wins);
    }

    [Fact]
    public void Cluster_WildConnects_SymbolsIntoCluster()
    {
        // WILD should act as a connector between A's
        var board = new Board(3, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "WILD" } })
            .SetCell(0, 2, new BoardCell { Symbols = new[] { "A" } });

        var paytable = MakePaytable(("A", new[] { 3 }, new[] { 10m }));
        var eval = new ClusterEvaluator(paytable, minClusterSize: 3, wildSymbolId: "WILD");
        var wins = eval.Evaluate(board, null);

        // WILD connects the two A's → cluster of 3
        Assert.Single(wins);
        Assert.Equal("A", wins[0].SymbolId);
        Assert.Equal(3, wins[0].Count);
    }

}

public class Evaluator_EdgeCases
{
    [Fact]
    public void WaysEvaluator_AcrossColumns()
    {
        var eval = new WaysEvaluator(new Paytable
        {
            Id = "t",
            Entries = new[] { new PaytableEntry { SymbolId = "A", Counts = new[] { 3 }, Payouts = new[] { "5" } } }
        });
        var board = new Board(3, 3)
            .SetCell(0, 0, new BoardCell().WithSymbols("A"))
            .SetCell(0, 1, new BoardCell().WithSymbols("A"))
            .SetCell(0, 2, new BoardCell().WithSymbols("A"));
        var wins = eval.Evaluate(board, null);
        Assert.NotEmpty(wins);
    }

    [Fact]
    public void ClusterEvaluator_NoCluster()
    {
        var eval = new ClusterEvaluator(new Paytable
        {
            Id = "t",
            Entries = new[] { new PaytableEntry { SymbolId = "X", Counts = new[] { 5 }, Payouts = new[] { "50" } } }
        }, minClusterSize: 5);
        var board = new Board(3, 3)
            .SetCell(0, 0, new BoardCell().WithSymbols("X"))
            .SetCell(1, 1, new BoardCell().WithSymbols("X"));
        var wins = eval.Evaluate(board, null);
        Assert.Empty(wins);
    }
}
