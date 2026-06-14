using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Expressions;

// ═══════════════════════════════════════════════════════════════════════════
//  D1 — partial-operation located errors (G11, invariant 3)
//
//  Division/modulo by zero, out-of-range index, and min/max over an empty
//  array each produce a deterministic, located evaluation error on BOTH paths.
//  sum/product/count over an empty array return identities (0, 1, 0).
// ═══════════════════════════════════════════════════════════════════════════

public class ExpressionPartialOpTests
{
    private static ConstantExpr Int(long v) =>
        new() { Kind = ConstantKind.Integer, Value = v.ToString() };

    // ── Division by zero ──────────────────────────────────────────────────

    [Fact]
    public void DivisionByZero_Exact_ProducesLocatedError()
    {
        var expr = new BinaryExpr { Op = BinaryOp.Div, Left = Int(5), Right = Int(0) };

        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => ExactExpressionEvaluator.Evaluate(expr, EvalContext.Empty));
        Assert.Equal(EvalErrorCodes.DivisionByZero, ex.Code);
        Assert.Equal("/", ex.Location);
    }

    [Fact]
    public void DivisionByZero_Sampled_ProducesSameLocatedError()
    {
        // The sampled path delegates to the exact evaluator, so the located
        // error is identical on both paths (D1).
        var expr = new BinaryExpr { Op = BinaryOp.Div, Left = Int(5), Right = Int(0) };

        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => SampledExpressionEvaluator.Evaluate(expr, EvalContext.Empty));
        Assert.Equal(EvalErrorCodes.DivisionByZero, ex.Code);
    }

    [Fact]
    public void NormalDivision_StillWorks()
    {
        var expr = new BinaryExpr { Op = BinaryOp.Div, Left = Int(6), Right = Int(2) };
        var result = ExactExpressionEvaluator.Evaluate(expr, EvalContext.Empty);
        Assert.Equal(new BigInteger(3), result.NumberNumerator / result.NumberDenominator);
    }

    // ── Empty min/max vs sum/product/count identities ─────────────────────

    [Theory]
    [InlineData(AggregateFunc.Min)]
    [InlineData(AggregateFunc.Max)]
    public void EmptyMinMax_ProducesLocatedError(AggregateFunc func)
    {
        // A predicate that matches nothing ⇒ the aggregate sees an empty set.
        var expr = new AggregateExpr
        {
            Func = func,
            StateKey = "board",
            ItemName = "sym",
            Predicate = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "false" },
        };
        var ctx = OneCellBoardCtx();

        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => ExactExpressionEvaluator.Evaluate(expr, ctx));
        Assert.Equal(EvalErrorCodes.EmptyMinMax, ex.Code);
    }

    [Theory]
    [InlineData(AggregateFunc.Sum, 0)]
    [InlineData(AggregateFunc.Count, 0)]
    [InlineData(AggregateFunc.Product, 1)]
    public void EmptySumProductCount_ReturnIdentities(AggregateFunc func, int identity)
    {
        var expr = new AggregateExpr
        {
            Func = func,
            StateKey = "board",
            ItemName = "sym",
            Predicate = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "false" },
        };
        var ctx = OneCellBoardCtx();

        var result = ExactExpressionEvaluator.Evaluate(expr, ctx);
        Assert.Equal(new BigInteger(identity), result.NumberNumerator / result.NumberDenominator);
    }

    // ── Out-of-range index ────────────────────────────────────────────────

    [Fact]
    public void IndexOutOfRange_ProducesLocatedError()
    {
        var state = new Dictionary<string, object?>
        {
            ["arr"] = new object[] { (BigInteger)10, (BigInteger)20 },
        };
        var expr = new FieldAccessExpr { Target = "state", Path = ["arr", "5"] };
        var ctx = new EvalContext { State = state };

        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => ExactExpressionEvaluator.Evaluate(expr, ctx));
        Assert.Equal(EvalErrorCodes.IndexOutOfRange, ex.Code);
    }

    [Fact]
    public void IndexInRange_Works()
    {
        var state = new Dictionary<string, object?>
        {
            ["arr"] = new object[] { (BigInteger)10, (BigInteger)20 },
        };
        var expr = new FieldAccessExpr { Target = "state", Path = ["arr", "1"] };
        var ctx = new EvalContext { State = state };

        var result = ExactExpressionEvaluator.Evaluate(expr, ctx);
        Assert.Equal(new BigInteger(20), result.NumberNumerator / result.NumberDenominator);
    }

    private static EvalContext OneCellBoardCtx() => new()
    {
        State = new Dictionary<string, object?> { ["board"] = new object?[] { "5" } },
    };
}
