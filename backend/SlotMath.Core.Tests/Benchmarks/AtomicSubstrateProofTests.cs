using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Benchmarks;

// ═══════════════════════════════════════════════════════════════════════════
//  AtomicSubstrateProofTests — G9 "atoms not molecules" proof
//
//  Demonstrates that:
//    1. FoldExpr correctly iterates over state arrays (unit tests).
//    2. A complete slot game can be expressed from ONLY substrate atoms
//       (Draw / Modify / GetState) + expression trees (FoldExpr / IfExpr /
//       CompareExpr) — zero IEvaluator implementations, zero ITransform
//       implementations, zero Board type.
//    3. The exact interpreter produces E[win] = 444/343 (hand-computed).
//
//  Game spec: 1×3 reel, symbols H(3/7), L(3/7), W-wild(1/7).
//  Win rule: 3-of-a-kind, wilds complete any symbol.
//    HHH (or wilds) → 5   LLL (or wilds) → 2   WWW → 3   else → 0
//
//  Hand-computed RTP (bet=1):
//    P(H-effective win) = (4/7)³ − (1/7)³ = 63/343  × payout 5 = 315/343
//    P(L-effective win) = 63/343                     × payout 2 = 126/343
//    P(WWW)             = (1/7)³  = 1/343            × payout 3 =   3/343
//    E[win] = 444/343
// ═══════════════════════════════════════════════════════════════════════════

using Dict = Dictionary<string, object?>;

public sealed class AtomicSubstrateProofTests
{
    // ── Symbol table ────────────────────────────────────────────────────
    private const string H = "H";
    private const string L = "L";
    private const string W = "W";
    private static readonly string[] Syms = [H, L, W];
    private static readonly WeightSet CellWeights = WeightSet.FromNumerators([3, 3, 1]);

    // ── Expression builders ─────────────────────────────────────────────
    private static FieldAccessExpr StateField(string name) =>
        new() { Target = "state", Path = [name] };

    private static FieldAccessExpr Cell(int idx) =>
        new() { Target = "state", Path = ["cells", idx.ToString()] };

    private static ConstantExpr StrConst(string v) =>
        new() { Kind = ConstantKind.String, Value = v };

    private static ConstantExpr IntConst(int v) =>
        new() { Kind = ConstantKind.Integer, Value = v.ToString() };

    // ── Shared expression: find first non-wild symbol via FoldExpr ──────
    //   fold("cells", "", (acc, item) => if acc=="" && item!="W" then item else acc)
    private static readonly FoldExpr WinSymFold = new()
    {
        StateKey = "cells",
        AccName = "_acc",
        ItemName = "_item",
        Init = StrConst(""),
        Body = new IfExpr
        {
            Condition = new BinaryExpr
            {
                Op = BinaryOp.And,
                Left = new CompareExpr
                {
                    Op = CompareOp.Eq,
                    Left = StateField("_acc"),
                    Right = StrConst(""),
                },
                Right = new CompareExpr
                {
                    Op = CompareOp.Neq,
                    Left = StateField("_item"),
                    Right = StrConst(W),
                },
            },
            ThenExpr = StateField("_item"),
            ElseExpr = StateField("_acc"),
        },
    };

    // ── FoldExpr unit tests ──────────────────────────────────────────────

    [Fact]
    public void FoldExpr_ReturnsFirstNonWild_H()
    {
        var ctx = new EvalContext { State = new Dict { ["cells"] = new[] { H, L, W } } };
        var result = ExactExpressionEvaluator.Evaluate(WinSymFold, ctx);
        Assert.Equal(H, result.StringValue);
    }

    [Fact]
    public void FoldExpr_ReturnsFirstNonWild_L_WhenFirstIsWild()
    {
        var ctx = new EvalContext { State = new Dict { ["cells"] = new[] { W, L, H } } };
        var result = ExactExpressionEvaluator.Evaluate(WinSymFold, ctx);
        Assert.Equal(L, result.StringValue);
    }

    [Fact]
    public void FoldExpr_AllWild_ReturnsEmptyString()
    {
        var ctx = new EvalContext { State = new Dict { ["cells"] = new[] { W, W, W } } };
        var result = ExactExpressionEvaluator.Evaluate(WinSymFold, ctx);
        Assert.Equal("", result.StringValue);
    }

    [Fact]
    public void FoldExpr_SingleElement_ReturnsItWhenNonWild()
    {
        var ctx = new EvalContext { State = new Dict { ["cells"] = new[] { H } } };
        var result = ExactExpressionEvaluator.Evaluate(WinSymFold, ctx);
        Assert.Equal(H, result.StringValue);
    }

    [Fact]
    public void FoldExpr_EmptyArray_ReturnsInit()
    {
        var ctx = new EvalContext { State = new Dict { ["cells"] = Array.Empty<string>() } };
        var result = ExactExpressionEvaluator.Evaluate(WinSymFold, ctx);
        Assert.Equal("", result.StringValue);
    }

    [Fact]
    public void StateArrayIndexAccess_ReturnsCorrectElement()
    {
        var ctx = new EvalContext { State = new Dict { ["cells"] = new[] { H, L, W } } };
        Assert.Equal(H, ExactExpressionEvaluator.Evaluate(Cell(0), ctx).StringValue);
        Assert.Equal(L, ExactExpressionEvaluator.Evaluate(Cell(1), ctx).StringValue);
        Assert.Equal(W, ExactExpressionEvaluator.Evaluate(Cell(2), ctx).StringValue);
    }

    // ── Win amount expression ───────────────────────────────────────────
    //
    //  cells[i] matches winSym if: cells[i] == state.winSym || cells[i] == "W"
    //  allMatch: all 3 cells match winSym
    //  allWild:  all 3 cells are "W"
    //
    //  win = if allWild then 3
    //        else if winSym == "H" && allMatch then 5
    //        else if winSym == "L" && allMatch then 2
    //        else 0
    //
    //  (allMatch is safe to reuse across both branches: it reads state.winSym
    //   which is already H or L at evaluation time, so "cells[i] == winSym"
    //   checks the right symbol in each branch.)

    private static Expression CellMatchesSym(int i) => new BinaryExpr
    {
        Op = BinaryOp.Or,
        Left = new CompareExpr { Op = CompareOp.Eq, Left = Cell(i), Right = StateField("winSym") },
        Right = new CompareExpr { Op = CompareOp.Eq, Left = Cell(i), Right = StrConst(W) },
    };

    private static readonly Expression AllMatch = new BinaryExpr
    {
        Op = BinaryOp.And,
        Left = new BinaryExpr { Op = BinaryOp.And, Left = CellMatchesSym(0), Right = CellMatchesSym(1) },
        Right = CellMatchesSym(2),
    };

    private static readonly Expression AllWild = new BinaryExpr
    {
        Op = BinaryOp.And,
        Left = new BinaryExpr
        {
            Op = BinaryOp.And,
            Left = new CompareExpr { Op = CompareOp.Eq, Left = Cell(0), Right = StrConst(W) },
            Right = new CompareExpr { Op = CompareOp.Eq, Left = Cell(1), Right = StrConst(W) },
        },
        Right = new CompareExpr { Op = CompareOp.Eq, Left = Cell(2), Right = StrConst(W) },
    };

    private static readonly Expression WinExpr = new IfExpr
    {
        Condition = AllWild,
        ThenExpr = IntConst(3),
        ElseExpr = new IfExpr
        {
            Condition = new BinaryExpr
            {
                Op = BinaryOp.And,
                Left = new CompareExpr
                { Op = CompareOp.Eq, Left = StateField("winSym"), Right = StrConst(H) },
                Right = AllMatch,
            },
            ThenExpr = IntConst(5),
            ElseExpr = new IfExpr
            {
                Condition = new BinaryExpr
                {
                    Op = BinaryOp.And,
                    Left = new CompareExpr
                    { Op = CompareOp.Eq, Left = StateField("winSym"), Right = StrConst(L) },
                    Right = AllMatch,
                },
                ThenExpr = IntConst(2),
                ElseExpr = IntConst(0),
            },
        },
    };

    // ── Win expression unit tests ────────────────────────────────────────

    private static BigInteger EvalWin(string[] cells)
    {
        var s = new Dict { ["cells"] = cells, ["winSym"] = "", ["win"] = BigInteger.Zero };
        var ctx = new EvalContext { State = s };
        // Step 1: compute win symbol
        s["winSym"] = ExactExpressionEvaluator.Evaluate(WinSymFold, ctx).StringValue ?? "";
        // Step 2: compute win amount
        return ExactExpressionEvaluator.Evaluate(WinExpr, ctx).AsInteger();
    }

    [Theory]
    [InlineData(new[] { "H", "H", "H" }, 5)]
    [InlineData(new[] { "L", "L", "L" }, 2)]
    [InlineData(new[] { "W", "W", "W" }, 3)]
    [InlineData(new[] { "H", "H", "W" }, 5)]  // wild completes H
    [InlineData(new[] { "W", "L", "L" }, 2)]  // wild completes L
    [InlineData(new[] { "H", "W", "W" }, 5)]  // 2 wilds + H
    [InlineData(new[] { "H", "L", "H" }, 0)]  // no 3-of-a-kind
    [InlineData(new[] { "H", "L", "W" }, 0)]  // broken
    [InlineData(new[] { "L", "H", "L" }, 0)]  // broken
    public void WinExpression_CorrectForKnownInputs(string[] cells, int expected)
    {
        Assert.Equal(new BigInteger(expected), EvalWin(cells));
    }

    // ── Atomic program ───────────────────────────────────────────────────

    private static Dict InitialState() =>
        new() { ["cells"] = Array.Empty<string>(), ["winSym"] = "", ["win"] = BigInteger.Zero };

    // State hash: encode cells as a base-4 integer (H=0, L=1, W=2, absent=3).
    // Injective over the 27 possible drawn cell combinations.
    private static BigInteger HashState(Dict s)
    {
        if (!s.TryGetValue("cells", out var c) || c is not string[] cells || cells.Length == 0)
            return BigInteger.MinusOne;
        int Sym(string x) => x == H ? 0 : x == L ? 1 : x == W ? 2 : 3;
        return cells.Aggregate(BigInteger.Zero, (acc, sym) => acc * 4 + Sym(sym));
    }

    // The game program — ONLY substrate atoms + expression trees.
    // No IEvaluator, no ITransform, no Board type.
    private static Slot<Dict, BigInteger> BuildProgram() =>
        from c0 in Slot.Draw<Dict>(_ => CellWeights)
        from c1 in Slot.Draw<Dict>(_ => CellWeights)
        from c2 in Slot.Draw<Dict>(_ => CellWeights)
            // Atom 1: store drawn cells in state
        from _ in Slot.Modify<Dict>(s => new Dict(s) { ["cells"] = new[] { Syms[c0], Syms[c1], Syms[c2] } })
            // Atom 2: compute win symbol via FoldExpr (expression tree)
        from __ in Slot.Modify<Dict>(s =>
        {
            var winSym = ExactExpressionEvaluator.Evaluate(WinSymFold, new EvalContext { State = s }).StringValue ?? "";
            return new Dict(s) { ["winSym"] = winSym };
        })
            // Atom 3: compute win amount via expression tree, return as result
        from state in Slot.GetState<Dict>()
        select ExactExpressionEvaluator.Evaluate(WinExpr, new EvalContext { State = state }).AsInteger();

    // ── KEY PROOF TEST ───────────────────────────────────────────────────

    [Fact]
    public void AtomicGame_ExactRtp_Equals_444over343()
    {
        // Build program from pure atoms + expression trees.
        // Neither IEvaluator nor ITransform is referenced anywhere in this program.
        var program = BuildProgram();

        // Run the exact interpreter (enumerates all 3³ = 27 draw outcomes).
        var result = ExactInterpreter.Evaluate(program, InitialState(), HashState);

        // Sanity: no pruning applied (game is tiny).
        Assert.True(result.Distribution.IsFullyExact,
            "Expected no epsilon-pruning for this simple game.");

        // E[win] must equal the hand-computed value 444/343.
        var (rtpNum, rtpDen) = result.ValueDistribution().ExpectedBigIntegerValue();
        Assert.Equal(new BigInteger(444), rtpNum);
        Assert.Equal(new BigInteger(343), rtpDen);
    }

    [Fact]
    public void AtomicGame_ExactDistribution_HasCorrect27Outcomes()
    {
        var result = ExactInterpreter.Evaluate(BuildProgram(), InitialState(), HashState);
        var dist = result.ValueDistribution();

        // 3 symbols × 3 cells = 27 total outcomes, but many share the same win value.
        // Total probability mass must equal 1 (numerator == denominator).
        Assert.Equal(dist.TotalNumerator, dist.Denominator);

        // Denominator must be 7³ = 343 (three independent draws, each from 7 outcomes).
        Assert.Equal(new BigInteger(343), dist.Denominator);
    }

    [Fact]
    public void AtomicGame_IsDeterministic_SameSeedSameStats()
    {
        var p = BuildProgram();
        var cfg = new SampledConfig { Seed = 42, MaxSpins = 50_000 };
        var r1 = SampledInterpreter.Evaluate(p, InitialState(), cfg);
        var r2 = SampledInterpreter.Evaluate(p, InitialState(), cfg);
        Assert.Equal(r1.Stats.Mean, r2.Stats.Mean);
    }

    [Fact]
    public void AtomicGame_SampledRtp_ConvergesTo_444over343()
    {
        var p = BuildProgram();
        var r = SampledInterpreter.Evaluate(p, InitialState(), new SampledConfig { Seed = 1, MaxSpins = 200_000 });
        var exact = 444.0 / 343.0;
        Assert.InRange(r.Stats.Mean, exact - 3 * r.Stats.StdErr, exact + 3 * r.Stats.StdErr);
    }
}
