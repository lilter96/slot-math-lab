using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Mechanics.Evaluators;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Benchmarks;

// ═══════════════════════════════════════════════════════════════════════════
//  FastPathEquivalenceTests — Invariant 11
//
//  "Fast-paths are proven equivalent." Each optional C# fast-path must agree
//  with the canonical atomic subgraph (atoms + expressions) on ≥20 random
//  inputs.  The canonical subgraph is the source of truth; the fast-path is
//  an optimization detail.
//
//  Mechanics proven here:
//
//  1. LINES (single payline, 3 cells)
//     Canonical:  explicit conditional expression over cells[0..2].
//     Fast-path:  LinesEvaluator with wildSymbol = "W".
//     Coverage:   all 27 combinations of {H, L, W}³.
//
//  Notes on LinesEvaluator semantics (important for canonical correctness):
//    - Leading wild never starts a win: if cells[0]=="W", matchSymbol stays
//      null and any non-wild at col>0 breaks the match → always 0.
//    - Wild only extends an established match (cells[0] was a non-wild).
//    - Atomic expression uses cells[0]==matchSym as anchor — identical rule.
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

    private static readonly string[] AllSymbols = ["H", "L", "W", "S", "B"];

    private static Board MakeBoard(string[][] grid)
    {
        var rows = grid.Length;
        var cols = grid[0].Length;
        var board = new Board(rows, cols);
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
                board = board.SetCell(r, c, new BoardCell { Symbols = [grid[r][c]] });
        return board;
    }

    private static string[][] RandomGrid(int seed, int rows, int cols)
    {
        var rng = new System.Random(seed);
        return Enumerable.Range(0, rows)
            .Select(_ => Enumerable.Range(0, cols)
                .Select(_ => AllSymbols[rng.Next(AllSymbols.Length)])
                .ToArray())
            .ToArray();
    }

    // ════════════════════════════════════════════════════════════════════
    //  1. LINES equivalence — single 3-cell payline, symbols {H, L, W}
    // ════════════════════════════════════════════════════════════════════

    // Canonical atomic expression mirrors LinesEvaluator's exact semantics:
    //
    //   - cells[0] must be the non-wild anchor (leading wild → no win).
    //   - cells[1] and cells[2] must be anchor-sym OR wild.
    //
    //   win =
    //     if cells[0]=="H" && (cells[1]=="H"||cells[1]=="W") && (cells[2]=="H"||cells[2]=="W") → 5
    //     else if cells[0]=="L" && (cells[1]=="L"||cells[1]=="W") && (cells[2]=="L"||cells[2]=="W") → 2
    //     else 0

    private const string H = "H";
    private const string L = "L";
    private const string W = "W";

    private static Expression CellIsSymOrWild(int pos, string sym) => new BinaryExpr
    {
        Op = BinaryOp.Or,
        Left = new CompareExpr { Op = CompareOp.Eq, Left = Cell(pos), Right = StrConst(sym) },
        Right = new CompareExpr { Op = CompareOp.Eq, Left = Cell(pos), Right = StrConst(W) },
    };

    // Match: anchor at pos 0 (exact), cols 1+2 accept sym or wild.
    private static Expression LineMatch3(string sym) => new BinaryExpr
    {
        Op = BinaryOp.And,
        Left = new BinaryExpr
        {
            Op = BinaryOp.And,
            Left = new CompareExpr { Op = CompareOp.Eq, Left = Cell(0), Right = StrConst(sym) },
            Right = CellIsSymOrWild(1, sym),
        },
        Right = CellIsSymOrWild(2, sym),
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
        var board = new Board(1, 3)
            .SetCell(0, 0, new BoardCell { Symbols = [cells[0]] })
            .SetCell(0, 1, new BoardCell { Symbols = [cells[1]] })
            .SetCell(0, 2, new BoardCell { Symbols = [cells[2]] });

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

        var wins = new LinesEvaluator(paytable, paylineSet, W).Evaluate(board, null);
        return wins.Sum(w => w.Payout);
    }

    // All 27 combinations of {H, L, W}³

    public static IEnumerable<object[]> AllTriples()
    {
        var syms = new[] { H, L, W };
        foreach (var c0 in syms)
            foreach (var c1 in syms)
                foreach (var c2 in syms)
                    yield return [new[] { c0, c1, c2 }];
    }

    [Theory]
    [MemberData(nameof(AllTriples))]
    public void Lines_CanonicalAtomicPayout_AgreesWithFastPath(string[] cells)
    {
        var atomic = (decimal)AtomicLinesPayout(cells);
        var fastPath = FastPathLinesPayout(cells);
        Assert.Equal(fastPath, atomic);
    }

    // ════════════════════════════════════════════════════════════════════
    //  3. WAYS equivalence
    //
    //  Canonical: independent left-to-right per-column count, ways = product.
    //  Fast-path: WaysEvaluator.
    //  Coverage:  20 randomly-generated boards (2×2 .. 3×5), symbols {H,L,W}.
    //
    //  The canonical reference is written independently of WaysEvaluator
    //  to prove semantic agreement, not just code sharing.
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

    // Canonical: count matching cells per column, multiply, look up.
    private static decimal WaysCanonical(Board board, string targetSym, string? wild)
    {
        var colCounts = new List<int>();
        for (var col = 0; col < board.Cols; col++)
        {
            var cnt = 0;
            for (var row = 0; row < board.Rows; row++)
            {
                var cell = board[row, col];
                if (cell.IsEmpty) continue;
                var sym = cell.Symbols![0];
                if (sym == targetSym || sym == wild) cnt++;
            }
            if (cnt == 0) break;
            colCounts.Add(cnt);
        }
        if (colCounts.Count < 2) return 0;
        var totalWays = colCounts.Aggregate(1, (a, b) => a * b);
        var unitPay = WaysPaytableLookup(targetSym, colCounts.Count);
        return unitPay * totalWays;
    }

    [Theory]
    [InlineData(0, 2, 2)] [InlineData(1, 2, 3)] [InlineData(2, 3, 2)]
    [InlineData(3, 2, 4)] [InlineData(4, 3, 3)] [InlineData(5, 2, 5)]
    [InlineData(6, 3, 4)] [InlineData(7, 3, 5)] [InlineData(8, 2, 2)]
    [InlineData(9, 2, 3)] [InlineData(10, 3, 2)] [InlineData(11, 2, 4)]
    [InlineData(12, 3, 3)] [InlineData(13, 2, 5)] [InlineData(14, 3, 4)]
    [InlineData(15, 3, 5)] [InlineData(16, 2, 2)] [InlineData(17, 2, 3)]
    [InlineData(18, 3, 2)] [InlineData(19, 2, 4)]
    public void Ways_CanonicalCount_AgreesWithFastPath(int seed, int rows, int cols)
    {
        var syms = new[] { "H", "L", W };
        var grid = RandomGrid(seed, rows, cols);
        // Replace 3rd symbol with W for wilds.
        for (var r = 0; r < grid.Length; r++)
            for (var c = 0; c < grid[r].Length; c++)
                if (grid[r][c] == AllSymbols[2]) grid[r][c] = W;

        var board = MakeBoard(grid);
        var paytable = WaysPaytable();
        var evaluator = new WaysEvaluator(paytable, W);
        var fastPathTotal = evaluator.Evaluate(board, null).Sum(w => w.Payout);

        var canonicalTotal = syms.Where(s => s != W)
            .Sum(sym => WaysCanonical(board, sym, W));

        Assert.Equal(canonicalTotal, fastPathTotal);
    }

    // ════════════════════════════════════════════════════════════════════
    //  4. CLUSTER equivalence
    //
    //  Canonical: independent BFS flood-fill (no ClusterEvaluator code).
    //  Fast-path: ClusterEvaluator.
    //  Coverage:  20 randomly-generated boards (3×3 .. 4×5), minCluster=3.
    // ════════════════════════════════════════════════════════════════════

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
        // Find largest count entry that doesn't exceed cluster size
        var best = -1;
        for (var i = 0; i < entry.Counts.Length; i++)
            if (entry.Counts[i] <= count && (best < 0 || entry.Counts[i] > entry.Counts[best]))
                best = i;
        return best < 0 ? 0 : decimal.Parse(entry.Payouts[best]);
    }

    private static List<(int Row, int Col)> CanonicalFloodFill(
        Board board, int startR, int startC, string sym, bool[,] visited)
    {
        var cluster = new List<(int Row, int Col)>();
        var queue = new Queue<(int, int)>();
        queue.Enqueue((startR, startC));
        visited[startR, startC] = true;
        int[][] dirs = [[-1, 0], [1, 0], [0, -1], [0, 1]];
        while (queue.Count > 0)
        {
            var (r, c) = queue.Dequeue();
            cluster.Add((r, c));
            foreach (var d in dirs)
            {
                var nr = r + d[0]; var nc = c + d[1];
                if (nr < 0 || nr >= board.Rows || nc < 0 || nc >= board.Cols) continue;
                if (visited[nr, nc] || board[nr, nc].IsEmpty) continue;
                var csym = board[nr, nc].Symbols![0];
                if (csym == sym)
                {
                    visited[nr, nc] = true;
                    queue.Enqueue((nr, nc));
                }
            }
        }
        return cluster;
    }

    private static decimal ClusterCanonical(Board board, int minSize)
    {
        var visited = new bool[board.Rows, board.Cols];
        decimal total = 0;
        for (var r = 0; r < board.Rows; r++)
        for (var c = 0; c < board.Cols; c++)
        {
            if (visited[r, c] || board[r, c].IsEmpty) continue;
            var sym = board[r, c].Symbols![0];
            var cluster = CanonicalFloodFill(board, r, c, sym, visited);
            if (cluster.Count >= minSize)
                total += ClusterPaytableLookup(sym, cluster.Count);
        }
        return total;
    }

    [Theory]
    [InlineData(0, 3, 3)] [InlineData(1, 3, 4)] [InlineData(2, 4, 3)]
    [InlineData(3, 3, 5)] [InlineData(4, 4, 4)] [InlineData(5, 4, 5)]
    [InlineData(6, 3, 3)] [InlineData(7, 3, 4)] [InlineData(8, 4, 3)]
    [InlineData(9, 3, 5)] [InlineData(10, 4, 4)] [InlineData(11, 4, 5)]
    [InlineData(12, 3, 3)] [InlineData(13, 3, 4)] [InlineData(14, 4, 3)]
    [InlineData(15, 3, 5)] [InlineData(16, 4, 4)] [InlineData(17, 4, 5)]
    [InlineData(18, 3, 3)] [InlineData(19, 3, 4)]
    public void Cluster_CanonicalFloodFill_AgreesWithFastPath(int seed, int rows, int cols)
    {
        // Only H and L symbols (no wilds) to keep canonical simple.
        var rng = new System.Random(seed);
        var grid = Enumerable.Range(0, rows)
            .Select(_ => Enumerable.Range(0, cols)
                .Select(_ => rng.Next(2) == 0 ? "H" : "L")
                .ToArray())
            .ToArray();

        var board = MakeBoard(grid);
        const int minSize = 3;
        var paytable = ClusterPaytable();

        var fastPathTotal = new ClusterEvaluator(paytable, minSize).Evaluate(board, null).Sum(w => w.Payout);
        var canonicalTotal = ClusterCanonical(board, minSize);

        Assert.Equal(canonicalTotal, fastPathTotal);
    }
}
