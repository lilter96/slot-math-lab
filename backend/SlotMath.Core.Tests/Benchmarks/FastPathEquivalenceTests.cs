using System.Numerics;
using CsCheck;
using SlotMath.Core.Expressions;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Mechanics.Evaluators;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Benchmarks;

// ═══════════════════════════════════════════════════════════════════════════
//  FastPathEquivalenceTests — Invariant 11 / D25
//
//  "Fast-paths are proven equivalent." Each optional C# fast-path must agree
//  with the canonical independent reference on ≥10,000 randomly-generated
//  inputs (CsCheck PBT with auto-shrinking, pinned seeds per D8).
//
//  Mechanics proven here:
//
//  1. LINES (single payline, 3 cells)
//     Canonical:  explicit conditional expression over cells[0..2].
//     Fast-path:  LinesEvaluator with wildSymbol = "W".
//     Coverage:   exhaustive 27 combos + 10,000 PBT cases (D25).
//
//  2. WAYS (2 rows × 3 cols fixed-size board, symbols {H, L, W})
//     Canonical:  independent per-column count × paytable lookup.
//     Fast-path:  WaysEvaluator.
//     Coverage:   10,000 PBT cases + mandatory edge cases (D25).
//
//  3. CLUSTER (3×3 fixed-size board, minCluster=3, symbols {H, L})
//     Canonical:  independent BFS flood-fill.
//     Fast-path:  ClusterEvaluator.
//     Coverage:   10,000 PBT cases + mandatory edge cases (D25).
//
//  D8 compliance: all PBT tests use pinned string seeds so CI is
//  deterministic.  CsCheck auto-shrinks any failure to the minimal failing
//  board — this is the D25 negative-control: any divergence is immediately
//  minimized and reported.
//
//  CsCheck 4.0 API notes:
//    Gen.OneOfConst(T[])  — uniform pick from a constant array.
//    gen.Array[n]         — Gen<T[]> of exactly n elements.
//    gen.Array[a, b]      — Gen<T[]> of a..b elements.
//    Gen.Select(g1, g2)   — Gen<(T1, T2)> (value tuple).
//    gen.Sample(act, seed, iter) — run PBT with pinned seed.
// ═══════════════════════════════════════════════════════════════════════════

using Dict = Dictionary<string, object?>;

public sealed class FastPathEquivalenceTests
{
    // ── Common expression helpers ────────────────────────────────────────

    private static FieldAccessExpr Cell(int idx) =>
        new() { Target = "state", Path = ["cells", idx.ToString()] };

    private static ConstantExpr StrConst(string v) =>
        new() { Kind = ConstantKind.String, Value = v };

    private static ConstantExpr IntConst(int v) =>
        new() { Kind = ConstantKind.Integer, Value = v.ToString() };

    // ── Board helpers shared by Ways and Cluster sections ────────────────
    //  A "board" is a flat row-major symbol array in state (invariant 4).
    //  The canonical oracles below index this array DIRECTLY (no kernel
    //  helpers) to stay independent of SlotMath.Core (D25).

    private static Dict MakeBoard(string[][] grid)
    {
        var rows = grid.Length;
        var cols = grid[0].Length;
        var flat = new object?[rows * cols];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
                flat[r * cols + c] = grid[r][c];
        return new Dict { ["board"] = flat, ["rows"] = rows, ["cols"] = cols };
    }

    private static int RowsOf(Dict s) => (int)s["rows"]!;
    private static int ColsOf(Dict s) => (int)s["cols"]!;
    private static string SymAt(Dict s, int r, int c) =>
        (string)((object?[])s["board"]!)[r * ColsOf(s) + c]!;

    // Symbols used in PBT generators
    private static readonly string[] LineSyms = ["H", "L", "W"];
    private static readonly string[] WaysSyms = ["H", "L", "W"];
    private static readonly string[] ClusterSyms = ["H", "L"];

    // ════════════════════════════════════════════════════════════════════
    //  1. LINES equivalence — single 3-cell payline, symbols {H, L, W}
    // ════════════════════════════════════════════════════════════════════

    private const string H = "H";
    private const string L = "L";
    private const string W = "W";

    private static Expression CellIsSymOrWild(int pos, string sym) => new BinaryExpr
    {
        Op = BinaryOp.Or,
        Left = new CompareExpr { Op = CompareOp.Eq, Left = Cell(pos), Right = StrConst(sym) },
        Right = new CompareExpr { Op = CompareOp.Eq, Left = Cell(pos), Right = StrConst(W) },
    };

    private static Expression LineMatch3(string sym) => new BinaryExpr
    {
        Op = BinaryOp.And,
        Left = new BinaryExpr
        {
            Op = BinaryOp.And,
            Left = new BinaryExpr { Op = BinaryOp.And, Left = CellIsSymOrWild(0, sym), Right = CellIsSymOrWild(1, sym) },
            Right = CellIsSymOrWild(2, sym),
        },
        Right = new BinaryExpr
        {
            Op = BinaryOp.Or,
            Left = new CompareExpr { Op = CompareOp.Eq, Left = Cell(0), Right = StrConst(sym) },
            Right = new BinaryExpr
            {
                Op = BinaryOp.Or,
                Left = new CompareExpr { Op = CompareOp.Eq, Left = Cell(1), Right = StrConst(sym) },
                Right = new CompareExpr { Op = CompareOp.Eq, Left = Cell(2), Right = StrConst(sym) },
            },
        },
    };

    private static readonly Expression LinesWinExpr = new IfExpr
    {
        Condition = LineMatch3(H),
        ThenExpr = IntConst(5),
        ElseExpr = new IfExpr
        {
            Condition = LineMatch3(L),
            ThenExpr = IntConst(2),
            ElseExpr = IntConst(0),
        },
    };

    private static BigInteger AtomicLinesPayout(string[] cells)
    {
        var ctx = new EvalContext { State = new Dict { ["cells"] = cells } };
        return ExactExpressionEvaluator.Evaluate(LinesWinExpr, ctx).AsInteger();
    }

    private static decimal FastPathLinesPayout(string[] cells)
    {
        var board = MakeBoard([cells]);

        var paytable = new Paytable
        {
            Id = "lines-equiv",
            Entries =
            [
                new PaytableEntry { SymbolId = H, Counts = [3], Payouts = ["5"] },
                new PaytableEntry { SymbolId = L, Counts = [3], Payouts = ["2"] },
            ],
        };

        var paylineSet = new PaylineSet
        {
            Id = "single",
            Paylines = [new Payline { Positions = [0, 0, 0] }],
        };

        return new LinesEvaluator(paytable, paylineSet, W).Evaluate(board).Sum(w => w.Payout);
    }

    // ── 1a. Exhaustive: all 27 combinations of {H, L, W}³ ──────────────

    public static IEnumerable<object[]> AllTriples()
    {
        foreach (var c0 in LineSyms)
            foreach (var c1 in LineSyms)
                foreach (var c2 in LineSyms)
                    yield return [new[] { c0, c1, c2 }];
    }

    [Theory]
    [MemberData(nameof(AllTriples))]
    public void Lines_Exhaustive_CanonicalAtomicPayout_AgreesWithFastPath(string[] cells)
    {
        Assert.Equal((decimal)AtomicLinesPayout(cells), FastPathLinesPayout(cells));
    }

    // ── 1b. PBT: 10,000 cases with CsCheck (pinned seed, D8) ──────────
    //
    //  CsCheck auto-shrinks any failure to the minimal failing 3-cell row.

    [Fact]
    public void Lines_PBT_10k_CanonicalAtomicAgreesWithFastPath()
    {
        var symGen = Gen.OneOfConst(LineSyms);
        Gen.Select(symGen, symGen, symGen)
           .Sample(
               (c0, c1, c2) =>
               {
                   var cells = new[] { c0, c1, c2 };
                   Assert.Equal((decimal)AtomicLinesPayout(cells), FastPathLinesPayout(cells));
               },
               seed: "lines-equiv-pbt-v1",
               iter: 10_000);
    }

    // ── 1c. Mandatory edge cases (D25) ──────────────────────────────────

    [Fact]
    public void Lines_EdgeCase_AllWild_NoWin()
    {
        Assert.Equal(0m, FastPathLinesPayout(["W", "W", "W"]));
        Assert.Equal(0m, (decimal)AtomicLinesPayout(["W", "W", "W"]));
    }

    [Fact]
    public void Lines_EdgeCase_AllHigh_Wins()
    {
        Assert.Equal(5m, FastPathLinesPayout(["H", "H", "H"]));
    }

    [Fact]
    public void Lines_EdgeCase_HighAnchorWithWilds_Wins()
    {
        Assert.Equal(5m, FastPathLinesPayout(["H", "W", "W"]));
        Assert.Equal(5m, FastPathLinesPayout(["H", "W", "H"]));
        Assert.Equal(5m, FastPathLinesPayout(["H", "H", "W"]));
    }

    [Fact]
    public void Lines_EdgeCase_WildAnchor_Substitutes()
    {
        Assert.Equal(5m, FastPathLinesPayout(["W", "H", "H"]));
        Assert.Equal(2m, FastPathLinesPayout(["W", "L", "L"]));
    }

    // ════════════════════════════════════════════════════════════════════
    //  2. WAYS equivalence
    //
    //  Canonical: independent left-to-right per-column count, ways = product.
    //  Fast-path: WaysEvaluator.
    //  Coverage:  10,000 PBT cases (2-row × 3-col boards) + edge cases (D25).
    // ════════════════════════════════════════════════════════════════════

    private static Paytable WaysPaytable() => new()
    {
        Id = "ways-equiv",
        Entries =
        [
            new PaytableEntry { SymbolId = "H", Counts = [2, 3, 4, 5], Payouts = ["2","5","10","25"] },
            new PaytableEntry { SymbolId = "L", Counts = [2, 3, 4, 5], Payouts = ["1","2","5","10"] },
        ],
    };

    private static decimal WaysPaytableLookup(string sym, int count)
    {
        var tbl = WaysPaytable();
        var entry = tbl.Entries.FirstOrDefault(e => e.SymbolId == sym);
        if (entry == null) return 0;
        var idx = Array.IndexOf(entry.Counts, count);
        return idx < 0 ? 0 : decimal.Parse(entry.Payouts[idx]);
    }

    private static decimal WaysCanonical(Dict board, string targetSym, string? wild)
    {
        var colCounts = new List<int>();
        for (var col = 0; col < ColsOf(board); col++)
        {
            var cnt = 0;
            for (var row = 0; row < RowsOf(board); row++)
            {
                var sym = SymAt(board, row, col);
                if (string.IsNullOrEmpty(sym)) continue;
                if (sym == targetSym || sym == wild) cnt++;
            }
            if (cnt == 0) break;
            colCounts.Add(cnt);
        }
        if (colCounts.Count < 2) return 0;
        var totalWays = colCounts.Aggregate(1, (a, b) => a * b);
        return WaysPaytableLookup(targetSym, colCounts.Count) * totalWays;
    }

    private static decimal WaysCanonicalTotal(Dict board)
    {
        var nonWild = new[] { "H", "L" };
        return nonWild.Sum(sym => WaysCanonical(board, sym, W));
    }

    private static decimal WaysFastPath(Dict board) =>
        new WaysEvaluator(WaysPaytable(), W).Evaluate(board).Sum(w => w.Payout);

    // ── 2a. PBT: 10,000 cases on 2×3 boards (pinned seed, D8) ─────────
    //
    //  Six-cell board gives plenty of variety; CsCheck shrinks failures to
    //  the minimal failing configuration.

    [Fact]
    public void Ways_PBT_10k_CanonicalCountAgreesWithFastPath()
    {
        var symGen = Gen.OneOfConst(WaysSyms);
        // 2-row × 3-col board: 6 cells as a flat tuple
        Gen.Select(symGen, symGen, symGen, symGen, symGen, symGen)
           .Sample(
               (c00, c01, c02, c10, c11, c12) =>
               {
                   var board = MakeBoard([
                       [c00, c01, c02],
                       [c10, c11, c12],
                   ]);
                   Assert.Equal(WaysCanonicalTotal(board), WaysFastPath(board));
               },
               seed: "ways-2x3-pbt-v1",
               iter: 10_000);
    }

    // ── 2b. Mandatory edge cases (D25) ──────────────────────────────────

    [Fact]
    public void Ways_EdgeCase_AllWild_CanonicalAgreesWithFastPath()
    {
        // Wilds count toward every non-wild symbol's column count, so an
        // all-wild board pays for both H and L. Canonical and fast-path agree.
        var board = MakeBoard([["W", "W", "W"], ["W", "W", "W"]]);
        Assert.Equal(WaysCanonicalTotal(board), WaysFastPath(board));
    }

    [Fact]
    public void Ways_EdgeCase_AllH_2x2_MinBoard()
    {
        // 2×2 all H: H@count2 pays 2; ways = 2*2 = 4 → payout = 8.
        var board = MakeBoard([["H", "H"], ["H", "H"]]);
        Assert.Equal(WaysCanonicalTotal(board), WaysFastPath(board));
    }

    [Fact]
    public void Ways_EdgeCase_AllH_3x5_MaxBoard()
    {
        // 3×5 all H: H@count5 pays 25; ways = 3^5 = 243 → payout = 6,075.
        var row = new[] { "H", "H", "H", "H", "H" };
        var board = MakeBoard([row, row, row]);
        Assert.Equal(WaysCanonicalTotal(board), WaysFastPath(board));
    }

    [Fact]
    public void Ways_EdgeCase_GapBreaksChain_NoSymbolWin()
    {
        // H chain breaks at col 1 (no H in col 1) → 0 ways for H.
        var board = MakeBoard([["H", "L", "H"], ["H", "L", "H"]]);
        Assert.Equal(WaysCanonicalTotal(board), WaysFastPath(board));
    }

    // ── 2c. Additional PBT: 3×3 boards ──────────────────────────────────

    [Fact]
    public void Ways_PBT_3x3_CanonicalAgreesWithFastPath()
    {
        var symGen = Gen.OneOfConst(WaysSyms);
        var row3 = Gen.Select(symGen, symGen, symGen, (a, b, c) => new[] { a, b, c });
        Gen.Select(row3, row3, row3, (r0, r1, r2) => new[] { r0, r1, r2 })
           .Sample(
               grid =>
               {
                   var board = MakeBoard(grid);
                   Assert.Equal(WaysCanonicalTotal(board), WaysFastPath(board));
               },
               seed: "ways-3x3-pbt-v1",
               iter: 5_000);
    }

    // ════════════════════════════════════════════════════════════════════
    //  3. CLUSTER equivalence
    //
    //  Canonical: independent BFS flood-fill.
    //  Fast-path: ClusterEvaluator.
    //  Coverage:  10,000 PBT cases (3×3 boards) + edge cases (D25),
    //             minCluster=3.
    // ════════════════════════════════════════════════════════════════════

    private const int MinCluster = 3;

    private static Paytable ClusterPaytable() => new()
    {
        Id = "cluster-equiv",
        Entries =
        [
            new PaytableEntry { SymbolId = "H", Counts = [3, 4, 5, 6, 7, 8], Payouts = ["3","5","8","12","20","30"] },
            new PaytableEntry { SymbolId = "L", Counts = [3, 4, 5, 6, 7, 8], Payouts = ["2","3","5","8","12","20"] },
        ],
    };

    private static decimal ClusterPaytableLookup(string sym, int count)
    {
        var tbl = ClusterPaytable();
        var entry = tbl.Entries.FirstOrDefault(e => e.SymbolId == sym);
        if (entry == null) return 0;
        var best = -1;
        for (var i = 0; i < entry.Counts.Length; i++)
            if (entry.Counts[i] <= count && (best < 0 || entry.Counts[i] > entry.Counts[best]))
                best = i;
        return best < 0 ? 0 : decimal.Parse(entry.Payouts[best]);
    }

    private static List<(int Row, int Col)> BfsFloodFill(
        Dict board, int startR, int startC, string sym, bool[,] visited)
    {
        var cluster = new List<(int Row, int Col)>();
        var queue = new Queue<(int, int)>();
        queue.Enqueue((startR, startC));
        visited[startR, startC] = true;
        var rows = RowsOf(board); var cols = ColsOf(board);
        int[][] dirs = [[-1, 0], [1, 0], [0, -1], [0, 1]];
        while (queue.Count > 0)
        {
            var (r, c) = queue.Dequeue();
            cluster.Add((r, c));
            foreach (var d in dirs)
            {
                var nr = r + d[0]; var nc = c + d[1];
                if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
                if (visited[nr, nc] || string.IsNullOrEmpty(SymAt(board, nr, nc))) continue;
                if (SymAt(board, nr, nc) != sym) continue;
                visited[nr, nc] = true;
                queue.Enqueue((nr, nc));
            }
        }
        return cluster;
    }

    private static decimal ClusterCanonical(Dict board, int minSize)
    {
        var rows = RowsOf(board); var cols = ColsOf(board);
        var visited = new bool[rows, cols];
        decimal total = 0;
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                if (visited[r, c] || string.IsNullOrEmpty(SymAt(board, r, c))) continue;
                var sym = SymAt(board, r, c);
                var cluster = BfsFloodFill(board, r, c, sym, visited);
                if (cluster.Count >= minSize)
                    total += ClusterPaytableLookup(sym, cluster.Count);
            }
        return total;
    }

    // ── 3a. PBT: 10,000 cases on 3×3 boards (pinned seed, D8) ─────────

    [Fact]
    public void Cluster_PBT_10k_CanonicalFloodFillAgreesWithFastPath()
    {
        var symGen = Gen.OneOfConst(ClusterSyms);
        // Build 3×3 via three 3-cell row generators composed with a map.
        var row3 = Gen.Select(symGen, symGen, symGen, (a, b, c) => new[] { a, b, c });
        Gen.Select(row3, row3, row3, (r0, r1, r2) => new[] { r0, r1, r2 })
           .Sample(
               grid =>
               {
                   var board = MakeBoard(grid);
                   var fp = new ClusterEvaluator(ClusterPaytable(), MinCluster)
                       .Evaluate(board).Sum(w => w.Payout);
                   Assert.Equal(ClusterCanonical(board, MinCluster), fp);
               },
               seed: "cluster-3x3-pbt-v1",
               iter: 10_000);
    }

    // ── 3b. Additional PBT: 4×5 large boards ────────────────────────────

    [Fact]
    public void Cluster_PBT_4x5_CanonicalAgreesWithFastPath()
    {
        // Use the same approach for 4×5 (max board): test 2,000 cases.
        // Board has 20 cells; all combinations would be 2^20 ≈ 1M but we sample.
        var symGen = Gen.OneOfConst(ClusterSyms);
        var rowGen = Gen.Select(symGen, symGen, symGen, symGen, symGen)
                        .Select((a, b, c, d, e) => new[] { a, b, c, d, e });
        Gen.Select(rowGen, rowGen, rowGen, rowGen)
           .Sample(
               (r0, r1, r2, r3) =>
               {
                   var board = MakeBoard([r0, r1, r2, r3]);
                   var fp = new ClusterEvaluator(ClusterPaytable(), MinCluster)
                       .Evaluate(board).Sum(w => w.Payout);
                   Assert.Equal(ClusterCanonical(board, MinCluster), fp);
               },
               seed: "cluster-4x5-pbt-v1",
               iter: 2_000);
    }

    // ── 3c. Mandatory edge cases (D25) ──────────────────────────────────

    [Fact]
    public void Cluster_EdgeCase_AllSameSymbol_3x3_OneCluster()
    {
        var board = MakeBoard([["H", "H", "H"], ["H", "H", "H"], ["H", "H", "H"]]);
        var fp = new ClusterEvaluator(ClusterPaytable(), MinCluster).Evaluate(board).Sum(w => w.Payout);
        Assert.Equal(ClusterCanonical(board, MinCluster), fp);
    }

    [Fact]
    public void Cluster_EdgeCase_Checkerboard_NoClusterMeetsMin()
    {
        // No two adjacent cells have the same symbol → no cluster ≥ 3.
        var board = MakeBoard([
            ["H", "L", "H"],
            ["L", "H", "L"],
            ["H", "L", "H"],
        ]);
        var fp = new ClusterEvaluator(ClusterPaytable(), MinCluster).Evaluate(board).Sum(w => w.Payout);
        Assert.Equal(0m, fp);
        Assert.Equal(ClusterCanonical(board, MinCluster), fp);
    }

    [Fact]
    public void Cluster_EdgeCase_ExactlyMinCluster_LShape()
    {
        // L-shaped H cluster of 3 pays 3; the remaining 6 L cells form one
        // cluster paying 8 (count=6). Total canonical = 11; fast-path agrees.
        var board = MakeBoard([
            ["H", "H", "L"],
            ["H", "L", "L"],
            ["L", "L", "L"],
        ]);
        var fp = new ClusterEvaluator(ClusterPaytable(), MinCluster).Evaluate(board).Sum(w => w.Payout);
        Assert.Equal(ClusterCanonical(board, MinCluster), fp);
    }

    [Fact]
    public void Cluster_EdgeCase_MaxBoard_4x5_AllH()
    {
        // 4×5 all H → one cluster of 20 → capped at count 8 → pays 30.
        var row5 = new[] { "H", "H", "H", "H", "H" };
        var board = MakeBoard([row5, row5, row5, row5]);
        var fp = new ClusterEvaluator(ClusterPaytable(), MinCluster).Evaluate(board).Sum(w => w.Payout);
        Assert.Equal(ClusterCanonical(board, MinCluster), fp);
    }
}
