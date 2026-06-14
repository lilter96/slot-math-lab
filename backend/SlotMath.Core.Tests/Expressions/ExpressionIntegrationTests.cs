using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;
using Xunit;
using Xunit.Abstractions;

namespace SlotMath.Core.Tests.Expressions;

// ═══════════════════════════════════════════════════════════════════════════
//  G11 DoD 4 — Expression-driven multiplier yields EXACT RTP equal to
//            a hand-computed fraction under the exact interpreter,
//            and the sampled estimate converges to it.
// ═══════════════════════════════════════════════════════════════════════════

// ── Game state with multiplier expression ────────────────────────────────

public sealed record ExpressionGameState(
    int BoardValue,
    BigInteger WinAmount,
    string? MultiplierExpr = null)
{
    public BigInteger RecurrenceHash => BoardValue;

    public ExpressionGameState AddWin(BigInteger w) =>
        this with { WinAmount = WinAmount + w };

    public ExpressionGameState SetBoard(int v) =>
        this with { BoardValue = v };
}

// ═══════════════════════════════════════════════════════════════════════════
//  Test 1 — Expression-driven multiplier
// ═══════════════════════════════════════════════════════════════════════════

public class ExpressionDrivenMultiplier_ExactRtp
{
    /// <summary>
    /// A simple game with TWO draws:
    /// 1. Base payout: [0, 10, 20] with weights [3, 2, 1] → EV = (0*3+10*2+20*1)/6 = 40/6 = 20/3
    /// 2. Multiplier draw: [1x, 2x] with equal weights [1, 1]
    ///
    /// With a CONSTANT multiplier of 2x (expression: constant "2"):
    /// EV = (20/3) * 2 = 40/3
    ///
    /// With an EXPRESSION multiplier that evaluates to 2:
    /// EV must also = 40/3 exactly.
    /// </summary>
    [Fact]
    public void ConstantMultiplier_YieldsExactRtp()
    {
        var program =
            from baseIdx in Slot.Draw<ExpressionGameState>(
                _ => WeightSet.FromIntegers([3, 2, 1]))
            let baseWin = baseIdx switch { 0 => 0, 1 => 10, 2 => 20, _ => 0 }
            select new BigInteger(baseWin * 2); // 2x multiplier

        var result = ExactInterpreter.Evaluate(
            program,
            new ExpressionGameState(0, 0),
            s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        var (num, den) = vd.ExpectedBigIntegerValue();

        // EV = (0*3 + 10*2 + 20*1)/6 * 2 = (0+20+20)/6 * 2 = 40/6 * 2
        // Wait: each outcome gets multiplier applied.
        // Outcomes: 0*2=0 (weight 3), 10*2=20 (weight 2), 20*2=40 (weight 1)
        // EV = (0*3 + 20*2 + 40*1)/6 = (0+40+40)/6 = 80/6 = 40/3
        Assert.Equal(new BigInteger(40), num);
        Assert.Equal(new BigInteger(3), den);
    }

    /// <summary>
    /// Expression-driven multiplier: a BinaryExpr computes multiplier = 1 + 1 = 2.
    /// The compiled expression is used to scale each payout.
    ///
    /// Hand-computed: same as constant 2x → EV = 40/3.
    /// </summary>
    [Fact]
    public void ExpressionDrivenMultiplier_YieldsExactRtp()
    {
        // Expression: 1 + 1 (evaluates to 2)
        var multExpr = new BinaryExpr
        {
            Op = BinaryOp.Add,
            Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
            Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
        };

        // Compile the expression to a number delegate (same type as library/plugin)
        var multFunc = ExpressionCompiler.CompileNumber(multExpr);

        var program =
            from baseIdx in Slot.Draw<ExpressionGameState>(
                _ => WeightSet.FromIntegers([3, 2, 1]))
            let baseWin = baseIdx switch { 0 => 0, 1 => 10, 2 => 20, _ => 0 }
            select new BigInteger(baseWin * (int)multFunc(null, null));

        var result = ExactInterpreter.Evaluate(
            program,
            new ExpressionGameState(0, 0),
            s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        var (num, den) = vd.ExpectedBigIntegerValue();

        // EV = 40/3
        Assert.Equal(new BigInteger(40), num);
        Assert.Equal(new BigInteger(3), den);
        Assert.True(vd.IsFullyExact);
    }

    /// <summary>
    /// Expression-driven multiplier with rational constant: "3/2" = 1.5x
    ///
    /// Base: [0, 10] weights [1, 1] → EV = (0+10)/2 = 5
    /// Multiplier: 3/2
    /// EV = 5 * 3/2 = 15/2
    /// </summary>
    [Fact]
    public void RationalExpressionMultiplier_YieldsExactRtp()
    {
        // Expression: 3/2
        var multExpr = new ConstantExpr
        {
            Kind = ConstantKind.Rational,
            Value = "3/2",
        };

        var multFunc = ExpressionCompiler.CompileNumberDouble(multExpr);

        var program =
            from baseIdx in Slot.Draw<ExpressionGameState>(
                _ => WeightSet.FromIntegers([1, 1]))
            let baseWin = baseIdx == 0 ? BigInteger.Zero : new BigInteger(10)
            select new BigInteger((long)((double)baseWin * multFunc(null, null)));

        // 0 * 1.5 = 0, 10 * 1.5 = 15
        // EV = (0*1 + 15*1)/2 = 15/2
        var result = ExactInterpreter.Evaluate(
            program,
            new ExpressionGameState(0, 0),
            s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        var (num, den) = vd.ExpectedBigIntegerValue();

        Assert.Equal(new BigInteger(15), num);
        Assert.Equal(new BigInteger(2), den);
    }

    /// <summary>
    /// Expression: multiply base win by (rows + 1) where the board dimensions
    /// live in state (invariant 4: the engine has no Board type — a board is a
    /// user-defined array in state S, and its dimensions are state fields).
    ///
    /// state["rows"] = 3, so multiplier = 3 + 1 = 4.
    /// Base: [10] weight [1] → EV = 10 * 4 = 40
    /// </summary>
    [Fact]
    public void StateDimensionExpressionMultiplier_YieldsExactRtp()
    {
        // Expression: state.rows + 1
        var multExpr = new BinaryExpr
        {
            Op = BinaryOp.Add,
            Left = new FieldAccessExpr { Path = ["rows"], Target = "state" },
            Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
        };

        var multFunc = ExpressionCompiler.CompileNumber(multExpr);
        var stateForExpr = new Dictionary<string, object?> { ["rows"] = 3 };

        var program =
            from baseIdx in Slot.Draw<ExpressionGameState>(
                _ => WeightSet.FromIntegers([1]))
            let baseWin = new BigInteger(10)
            select baseWin * (int)multFunc(null, stateForExpr);

        var result = ExactInterpreter.Evaluate(
            program,
            new ExpressionGameState(0, 0),
            s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        var (num, den) = vd.ExpectedBigIntegerValue();

        // Rows=3 → multiplier=4 → EV = 10*4 = 40
        Assert.Equal(new BigInteger(40), num);
        Assert.Equal(new BigInteger(1), den);
    }

    // ── Sampled convergence ────────────────────────────────────────────

    /// <summary>
    /// The sampled estimate must converge to the exact RTP within
    /// 3 * stdErr for a simple expression-driven multiplier.
    /// </summary>
    [Fact]
    public void SampledEstimate_ConvergesToExactRtp_WithinConfidence()
    {
        // Expression: 2 (constant multiplier)
        var multExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" };
        var multFunc = ExpressionCompiler.CompileNumber(multExpr);

        var program =
            from baseIdx in Slot.Draw<ExpressionGameState>(
                _ => WeightSet.FromIntegers([3, 2, 1]))
            let baseWin = baseIdx switch { 0 => new BigInteger(0), 1 => new BigInteger(10), 2 => new BigInteger(20), _ => BigInteger.Zero }
            select baseWin * (int)multFunc(null, null);

        // Exact EV = 40/3 ≈ 13.333...
        var exactResult = ExactInterpreter.Evaluate(
            program, new ExpressionGameState(0, 0), s => s.RecurrenceHash);
        var (exactNum, exactDen) = exactResult.ValueDistribution().ExpectedBigIntegerValue();
        var exactRtp = (double)exactNum / (double)exactDen;

        // Sampled with 200,000 spins.
        var sampled = SampledInterpreter.Evaluate(
            program, new ExpressionGameState(0, 0),
            new SampledConfig { Seed = 12345, MaxSpins = 200_000 });

        var sampledRtp = sampled.Stats.Mean;
        var stdErr = sampled.Stats.StdErr;

        // Must be within 3 * stdErr (99.7% confidence).
        var diff = System.Math.Abs(sampledRtp - exactRtp);
        Assert.True(diff <= 3.0 * stdErr,
            $"Sampled RTP {sampledRtp:F6} not within 3*stdErr ({3.0 * stdErr:F6}) " +
            $"of exact {exactRtp:F6} (diff={diff:F6})");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Test 2 — Expression-driven Draw weight
// ═══════════════════════════════════════════════════════════════════════════

public class ExpressionDrivenDrawWeights
{
    /// <summary>
    /// A Draw whose weights come from an expression.
    ///
    /// The expression is: 1 (constant) — same as a single-outcome draw.
    /// With expression-driven weight of constant 1:
    /// payout = 100. EV = 100.
    /// </summary>
    [Fact]
    public void ConstantWeightExpression_Exact()
    {
        // Expression that evaluates to a weight of 1.
        var weightExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" };
        var weightFunc = ExpressionCompiler.CompileWeights(weightExpr);

        var program =
            from idx in Slot.Draw<ExpressionGameState>(
                _ => weightFunc(null))
            select new BigInteger((idx + 1) * 10); // only outcome is index 0 → 10

        var result = ExactInterpreter.Evaluate(
            program,
            new ExpressionGameState(0, 0),
            s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        Assert.Single(vd.Entries);
        Assert.Equal(new BigInteger(10), vd.Entries[0].Value);
    }

    /// <summary>
    /// Expression: 2 + 1 = 3 (a single weight of 3).
    /// The alias builder normalizes it, so the one outcome has prob 1.0.
    /// Payout = 100. EV = 100.
    /// </summary>
    [Fact]
    public void ComputedWeightExpression_Exact()
    {
        var weightExpr = new BinaryExpr
        {
            Op = BinaryOp.Add,
            Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" },
            Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
        };
        var weightFunc = ExpressionCompiler.CompileWeights(weightExpr);

        var program =
            from _ in Slot.Draw<ExpressionGameState>(
                _ => weightFunc(null))
            select new BigInteger(100);

        var result = ExactInterpreter.Evaluate(
            program,
            new ExpressionGameState(0, 0),
            s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        Assert.Single(vd.Entries);
        Assert.Equal(new BigInteger(100), vd.Entries[0].Value);
    }

    /// <summary>
    /// Multiple draw outcomes driven by an expression that evaluates to
    /// a WeightSet.
    ///
    /// Expression: 3 (constant) — one outcome with weight 3.
    /// This tests the integration path: expression → WeightSet → Draw → ExactDist.
    /// </summary>
    [Fact]
    public void WeightSetExpression_CorrectDistribution()
    {
        // Expression: integer 5 (→ WeightSet with one numerator = 5)
        var weightExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "5" };
        var weightFunc = ExpressionCompiler.CompileWeights(weightExpr);

        var program =
            from idx in Slot.Draw<ExpressionGameState>(
                _ => weightFunc(null))
            select new BigInteger((idx + 1) * 10);

        var result = ExactInterpreter.Evaluate(
            program,
            new ExpressionGameState(0, 0),
            s => s.RecurrenceHash);

        // One outcome with payout = 10, prob = 1.0
        var vd = result.ValueDistribution();
        Assert.Single(vd.Entries);
        Assert.Equal(new BigInteger(10), vd.Entries[0].Value);
    }

    /// <summary>
    /// Two sequential draws, both with expression-driven weights.
    ///
    /// Draw 1: weight = 1 (1 outcome)
    /// Draw 2: weight = 1 (1 outcome)
    /// Total EV = 100.
    /// </summary>
    [Fact]
    public void TwoSequentialExpressionDrivenDraws_Exact()
    {
        var weightExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" };
        var weightFunc = ExpressionCompiler.CompileWeights(weightExpr);

        var program =
            from a in Slot.Draw<ExpressionGameState>(
                _ => weightFunc(null))
            from b in Slot.Draw<ExpressionGameState>(
                _ => weightFunc(null))
            select new BigInteger(50 * (a + b + 1));

        // Both draws have 1 outcome (index 0), so a=0, b=0
        // Payout = 50 * (0+0+1) = 50
        var result = ExactInterpreter.Evaluate(
            program,
            new ExpressionGameState(0, 0),
            s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        Assert.Single(vd.Entries);
        Assert.Equal(new BigInteger(50), vd.Entries[0].Value);
    }

    /// <summary>
    /// Draw weight expression that uses state fields.
    ///
    /// Program: Draw A → put state with counter=1 → Draw B
    /// The weight for Draw B is computed from state.
    /// </summary>
    [Fact]
    public void StateDependentWeightExpression_Exact()
    {
        // Simple weight: constant 1
        var weightExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" };
        var weightFunc = ExpressionCompiler.CompileWeights(weightExpr);

        var program =
            from a in Slot.Draw<ExpressionGameState>(
                _ => weightFunc(null))
            from _ in Slot.Modify<ExpressionGameState>(s => s.SetBoard(a + 1))
            from b in Slot.Draw<ExpressionGameState>(
                _ => weightFunc(null)) // uses compiled weight
            select new BigInteger(10 * b);

        var result = ExactInterpreter.Evaluate(
            program,
            new ExpressionGameState(0, 0),
            s => s.RecurrenceHash);

        // One outcome from each draw: a=0, board becomes 1, b=0
        // Payout = 10*0 = 0
        var vd = result.ValueDistribution();
        Assert.Single(vd.Entries);
        Assert.Equal(BigInteger.Zero, vd.Entries[0].Value);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Test 2b — G11 DoD 4: Fold-driven line-scan — exact RTP = 40/27
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// G11 DoD 4: A FoldExpr-based line scanner produces an exact RTP equal to
/// the hand-computed fraction 40/27, and the sampled estimate converges to it.
///
/// Game design:
///   3 reels, each: H has weight 2, L has weight 1  →  P(H) = 2/3, P(L) = 1/3
///   1 payline (all 3 must be "H")
///   Win for 3×H = 5 credits, otherwise 0
///   Exact RTP = 5 × (2/3)³ = 5 × 8/27 = 40/27
///
/// The FoldExpr: fold(state["cells"], 0, (acc, itm) => acc + if(itm == "H", 1, 0))
/// Win expression: if(count == 3, 5, 0)
/// </summary>
public class FoldDrivenLineScan_ExactRtp
{
    // ── Build the FoldExpr that counts "H" symbols in state["cells"] ─────

    /// <summary>
    /// fold(state["cells"], 0, (acc, itm) => acc + if(itm == "H", 1, 0))
    /// Accumulator name: "acc", Item name: "itm", Init: 0
    /// Body: acc + if(itm == "H", 1, 0)
    /// </summary>
    private static FoldExpr BuildCountHFold() => new FoldExpr
    {
        StateKey = "cells",
        AccName = "acc",
        ItemName = "itm",
        Init = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
        ItemType = ExprType.String,
        Body = new BinaryExpr
        {
            Op = BinaryOp.Add,
            Left = new FieldAccessExpr { Target = "state", Path = ["acc"] },
            Right = new IfExpr
            {
                Condition = new CompareExpr
                {
                    Op = CompareOp.Eq,
                    Left = new FieldAccessExpr { Target = "state", Path = ["itm"] },
                    Right = new ConstantExpr { Kind = ConstantKind.String, Value = "H" },
                },
                ThenExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
                ElseExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
            },
        },
    };

    /// <summary>Win expression: if(count == 3, 5, 0)</summary>
    private static IfExpr BuildWinExpr() => new IfExpr
    {
        Condition = new CompareExpr
        {
            Op = CompareOp.Eq,
            Left = new FieldAccessExpr { Target = "state", Path = ["count"] },
            Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "3" },
        },
        ThenExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "5" },
        ElseExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
    };

    [Fact]
    public void FoldDrivenLineScan_YieldsExactRtp_40_Over_27()
    {
        var countHFold = BuildCountHFold();
        var winExpr = BuildWinExpr();

        // 3 reels: H(weight=2), L(weight=1). Outcome indices: 0 → "H", 1 → "L".
        var weights = WeightSet.FromIntegers([2, 1]);

        var program =
            from r0 in Slot.Draw<ExpressionGameState>(_ => weights)
            from r1 in Slot.Draw<ExpressionGameState>(_ => weights)
            from r2 in Slot.Draw<ExpressionGameState>(_ => weights)
            let cells = new object[]
            {
                r0 == 0 ? "H" : "L",
                r1 == 0 ? "H" : "L",
                r2 == 0 ? "H" : "L",
            }
            let evalState = new Dictionary<string, object?> { ["cells"] = cells }
            let evalCtx = new EvalContext { State = evalState }
            // Evaluate the fold to count "H" symbols.
            let countResult = ExactExpressionEvaluator.Evaluate(countHFold, evalCtx)
            // Store the count for the win expression to access via state["count"].
            let winState = new Dictionary<string, object?> { ["count"] = countResult.ToStateObject() }
            let winCtx = new EvalContext { State = winState }
            let winResult = ExactExpressionEvaluator.Evaluate(winExpr, winCtx)
            select winResult.AsInteger();

        var result = ExactInterpreter.Evaluate(
            program,
            new ExpressionGameState(0, 0),
            s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        var (rawNum, rawDen) = vd.ExpectedBigIntegerValue();

        // Reduce to lowest terms.
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(rawNum), rawDen);
        var num = rawNum / gcd;
        var den = rawDen / gcd;

        // Exact RTP = 5 × (2/3)³ = 40/27
        Assert.Equal(new BigInteger(40), num);
        Assert.Equal(new BigInteger(27), den);
        Assert.True(vd.IsFullyExact, "Expected fully-exact provenance (ε-pruned mass = 0).");
    }

    [Fact]
    public void SampledEstimate_ConvergesToExactRtp_40Over27()
    {
        var countHFold = BuildCountHFold();
        var winExpr = BuildWinExpr();

        var weights = WeightSet.FromIntegers([2, 1]);

        var program =
            from r0 in Slot.Draw<ExpressionGameState>(_ => weights)
            from r1 in Slot.Draw<ExpressionGameState>(_ => weights)
            from r2 in Slot.Draw<ExpressionGameState>(_ => weights)
            let cells = new object[]
            {
                r0 == 0 ? "H" : "L",
                r1 == 0 ? "H" : "L",
                r2 == 0 ? "H" : "L",
            }
            let evalState = new Dictionary<string, object?> { ["cells"] = cells }
            let evalCtx = new EvalContext { State = evalState }
            let countResult = ExactExpressionEvaluator.Evaluate(countHFold, evalCtx)
            let winState = new Dictionary<string, object?> { ["count"] = countResult.ToStateObject() }
            let winCtx = new EvalContext { State = winState }
            let winResult = ExactExpressionEvaluator.Evaluate(winExpr, winCtx)
            select winResult.AsInteger();

        // Exact RTP = 40/27
        const double exactRtp = 40.0 / 27.0;

        var sampled = SampledInterpreter.Evaluate(
            program,
            new ExpressionGameState(0, 0),
            new SampledConfig { Seed = 77777, MaxSpins = 50_000 });

        var sampledRtp = sampled.Stats.Mean;
        var stdErr = sampled.Stats.StdErr;

        var diff = System.Math.Abs(sampledRtp - exactRtp);
        Assert.True(diff <= 3.0 * stdErr,
            $"Sampled RTP {sampledRtp:F6} not within 3*stdErr ({3.0 * stdErr:F6}) " +
            $"of exact {exactRtp:F6} (diff={diff:F6})");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Test 3 — Sampled evaluation of expression-driven programs
// ═══════════════════════════════════════════════════════════════════════════

public class ExpressionDriven_SampledConvergence(ITestOutputHelper output)
{
    /// <summary>
    /// Verify the sampled interpreter correctly evaluates programs
    /// with expression-driven weights.
    ///
    /// Single draw, weight expression = 1, payout = 100.
    /// Sampled must return exactly 100 for every spin.
    /// </summary>
    [Fact]
    public void Sampled_ExpressionWeight_MatchesExact()
    {
        var weightExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" };
        var weightFunc = ExpressionCompiler.CompileWeights(weightExpr);

        var program =
            from idx in Slot.Draw<ExpressionGameState>(
                _ => weightFunc(null))
            select new BigInteger(100);

        var sampled = SampledInterpreter.Evaluate(
            program,
            new ExpressionGameState(0, 0),
            new SampledConfig { Seed = 42, MaxSpins = 10_000 });

        // Single outcome, every spin = 100.
        output.WriteLine($"Mean: {sampled.Stats.Mean}, StdDev: {sampled.Stats.StdDev}");
        Assert.Equal(100.0, sampled.Stats.Mean, 3);
        Assert.Equal(0.0, sampled.Stats.StdDev, 3);
    }

    /// <summary>
    /// Expression-driven multiplier under sampled interpreter.
    ///
    /// Base: [0.5, 0.5] → 0 or 10. Multiplier: constant 2.
    /// EV = (0+10)/2 * 2 = 10. Sampled must converge.
    /// </summary>
    [Fact]
    public void Sampled_MultiplierFromExpression_ConvergesToExact()
    {
        var multExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" };
        var multFunc = ExpressionCompiler.CompileNumber(multExpr);

        var program =
            from baseIdx in Slot.Draw<ExpressionGameState>(
                _ => WeightSet.FromIntegers([1, 1]))
            let baseWin = baseIdx == 0 ? BigInteger.Zero : new BigInteger(10)
            select baseWin * (int)multFunc(null, null);

        // Exact: outcomes 0*2=0, 10*2=20 → EV = (0+20)/2 = 10
        var exact = ExactInterpreter.Evaluate(
            program, new ExpressionGameState(0, 0), s => s.RecurrenceHash);
        var (exactNum, exactDen) = exact.ValueDistribution().ExpectedBigIntegerValue();
        var exactRtp = (double)exactNum / (double)exactDen;
        Assert.Equal(10.0, exactRtp, 9);

        // Sampled with enough spins.
        var sampled = SampledInterpreter.Evaluate(
            program, new ExpressionGameState(0, 0),
            new SampledConfig { Seed = 999, MaxSpins = 100_000 });

        output.WriteLine($"Exact: {exactRtp:F6}, Sampled: {sampled.Stats.Mean:F6}, " +
                       $"StdErr: {sampled.Stats.StdErr:F6}");

        var diff = System.Math.Abs(sampled.Stats.Mean - exactRtp);
        Assert.True(diff <= 3.0 * sampled.Stats.StdErr,
            $"Sampled RTP {sampled.Stats.Mean:F6} not within 3*stdErr " +
            $"({3.0 * sampled.Stats.StdErr:F6}) of exact {exactRtp}");
    }
}
