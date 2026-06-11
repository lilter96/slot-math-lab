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
//  1. SCATTER
//     Canonical:  FoldExpr counting cells that match a target symbol.
//     Fast-path:  ScatterEvaluator.
//     Coverage:   20 randomly-generated boards (2×2 .. 4×4), all 5 symbols.
//
//  2. LINES (single payline, 3 cells)
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

    private static FieldAccessExpr StateField(string name) =>
        new() { Target = "state", Path = [name] };

    private static FieldAccessExpr Cell(int idx) =>
        new() { Target = "state", Path = ["cells", idx.ToString()] };

    private static ConstantExpr StrConst(string v) =>
        new() { Kind = ConstantKind.String, Value = v };

    private static ConstantExpr IntConst(int v) =>
        new() { Kind = ConstantKind.Integer, Value = v.ToString() };

    // ════════════════════════════════════════════════════════════════════
    //  1. SCATTER equivalence
    // ════════════════════════════════════════════════════════════════════

    // Canonical atomic expression:
    //
    //   scatter_count(sym) =
    //     fold("cells", 0, (_cnt, _cell) =>
    //       if _cell == sym then _cnt + 1 else _cnt)
    //
    // Counts the number of cells in state["cells"] (string[], row-major)
    // that equal the target symbol.

    private static FoldExpr ScatterCountExpr(string sym) => new()
    {
        StateKey = "cells",
        AccName = "_cnt",
        ItemName = "_cell",
        Init = IntConst(0),
        Body = new IfExpr
        {
            Condition = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = StateField("_cell"),
                Right = StrConst(sym),
            },
            ThenExpr = new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = StateField("_cnt"),
                Right = IntConst(1),
            },
            ElseExpr = StateField("_cnt"),
        },
        ItemType = ExprType.String,
    };

    // ── Board / paytable helpers ─────────────────────────────────────────

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

    private static string[] FlattenBoard(Board board)
    {
        var flat = new List<string>(board.Rows * board.Cols);
        for (var r = 0; r < board.Rows; r++)
            for (var c = 0; c < board.Cols; c++)
            {
                var cell = board[r, c];
                flat.Add(cell.IsEmpty ? "" : cell.Symbols![0]);
            }
        return flat.ToArray();
    }

    // Paytable: every count 1..maxCount pays 1, so ScatterEvaluator always
    // creates a Win for any non-zero count (and Win.Count equals raw count).
    private static Paytable ScatterPaytable(int maxCount) => new()
    {
        Id = "scatter-equiv",
        Entries = AllSymbols.Select(sym => new PaytableEntry
        {
            SymbolId = sym,
            Counts = Enumerable.Range(1, maxCount).ToArray(),
            Payouts = Enumerable.Repeat("1", maxCount).ToArray(),
        }).ToArray(),
    };

    private static string[][] RandomGrid(int seed, int rows, int cols)
    {
        var rng = new System.Random(seed);
        return Enumerable.Range(0, rows)
            .Select(_ => Enumerable.Range(0, cols)
                .Select(_ => AllSymbols[rng.Next(AllSymbols.Length)])
                .ToArray())
            .ToArray();
    }

    // ── Scatter: 20 boards × 5 symbols each ─────────────────────────────

    [Theory]
    [InlineData(0,  2, 2)]
    [InlineData(1,  2, 2)]
    [InlineData(2,  3, 3)]
    [InlineData(3,  3, 3)]
    [InlineData(4,  2, 3)]
    [InlineData(5,  3, 2)]
    [InlineData(6,  4, 4)]
    [InlineData(7,  4, 4)]
    [InlineData(8,  2, 2)]
    [InlineData(9,  3, 3)]
    [InlineData(10, 2, 4)]
    [InlineData(11, 4, 2)]
    [InlineData(12, 3, 4)]
    [InlineData(13, 4, 3)]
    [InlineData(14, 2, 2)]
    [InlineData(15, 3, 3)]
    [InlineData(16, 4, 4)]
    [InlineData(17, 2, 3)]
    [InlineData(18, 3, 2)]
    [InlineData(19, 4, 4)]
    public void Scatter_CanonicalAtomicCount_AgreesWithFastPath(int seed, int rows, int cols)
    {
        var grid = RandomGrid(seed, rows, cols);
        var board = MakeBoard(grid);
        var flat = FlattenBoard(board);
        var paytable = ScatterPaytable(rows * cols);

        var fastPathWins = new ScatterEvaluator(paytable)
            .Evaluate(board, null)
            .ToDictionary(w => w.SymbolId, w => (BigInteger)w.Count);

        foreach (var sym in AllSymbols)
        {
            var ctx = new EvalContext { State = new Dict { ["cells"] = flat } };
            var atomicCount = ExactExpressionEvaluator
                .Evaluate(ScatterCountExpr(sym), ctx)
                .AsInteger();

            var fastPathCount = fastPathWins.GetValueOrDefault(sym, BigInteger.Zero);

            Assert.Equal(atomicCount, fastPathCount);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  2. LINES equivalence — single 3-cell payline, symbols {H, L, W}
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
}
