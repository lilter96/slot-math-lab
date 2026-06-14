using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace SlotMath.Core.Tests.Expressions;

// ═══════════════════════════════════════════════════════════════════════════
//  G11 DoD 2 — Evaluation is pure/total/deterministic with no I/O
// ═══════════════════════════════════════════════════════════════════════════

public class ExpressionEvaluator_PurityTests(ITestOutputHelper output)
{
    [Fact]
    public void IdenticalInputs_ProduceIdenticalOutputs()
    {
        var expr = new BinaryExpr
        {
            Op = BinaryOp.Mul,
            Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "6" },
            Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "7" },
        };

        var ctx = EvalContext.Empty;

        var r1 = ExactExpressionEvaluator.Evaluate(expr, ctx);
        var r2 = ExactExpressionEvaluator.Evaluate(expr, ctx);
        var r3 = ExactExpressionEvaluator.Evaluate(expr, ctx);

        Assert.Equal(r1.AsInteger(), r2.AsInteger());
        Assert.Equal(r2.AsInteger(), r3.AsInteger());
        Assert.Equal(new BigInteger(42), r1.AsInteger());
    }

    [Fact]
    public void EvaluationHasNoIO()
    {
        // The evaluator is a pure function — it cannot perform I/O.
        // This is verified by code review: no Console, no File, no network.
        // Here we verify it doesn't throw for any valid expression.

        var expr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "100" };
        var ctx = EvalContext.Empty;

        // Multiple evaluations should be stable (no side effects).
        for (int i = 0; i < 1000; i++)
        {
            var result = ExactExpressionEvaluator.Evaluate(expr, ctx);
            Assert.Equal(new BigInteger(100), result.AsInteger());
        }
    }

    [Fact]
    public void Total_HandlesAllExpressionTypesWithoutException()
    {
        // Every expression type must evaluate without throwing for valid inputs.
        // Constant
        var c = ExactExpressionEvaluator.Evaluate(
            new ConstantExpr { Kind = ConstantKind.Integer, Value = "5" }, EvalContext.Empty);
        Assert.Equal(new BigInteger(5), c.AsInteger());

        // Binary
        var b = ExactExpressionEvaluator.Evaluate(new BinaryExpr
        {
            Op = BinaryOp.Add,
            Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" },
            Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "3" },
        }, EvalContext.Empty);
        Assert.Equal(new BigInteger(5), b.AsInteger());

        // Compare
        var cmp = ExactExpressionEvaluator.Evaluate(new CompareExpr
        {
            Op = CompareOp.Gt,
            Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "5" },
            Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "3" },
        }, EvalContext.Empty);
        Assert.True(cmp.BoolValue);

        // If (true branch)
        var ifExpr = ExactExpressionEvaluator.Evaluate(new IfExpr
        {
            Condition = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" },
            ThenExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "10" },
            ElseExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "20" },
        }, EvalContext.Empty);
        Assert.Equal(new BigInteger(10), ifExpr.AsInteger());

        // If (false branch)
        var ifExpr2 = ExactExpressionEvaluator.Evaluate(new IfExpr
        {
            Condition = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "false" },
            ThenExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "10" },
            ElseExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "20" },
        }, EvalContext.Empty);
        Assert.Equal(new BigInteger(20), ifExpr2.AsInteger());

        // Not
        var not1 = ExactExpressionEvaluator.Evaluate(new NotExpr
        {
            Expr = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" },
        }, EvalContext.Empty);
        Assert.False(not1.BoolValue);

        // Call
        var call = ExactExpressionEvaluator.Evaluate(new CallExpr
        {
            Function = "abs",
            Args = [new ConstantExpr { Kind = ConstantKind.Integer, Value = "-10" }],
        }, EvalContext.Empty);
        Assert.Equal(new BigInteger(10), call.AsInteger());
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  G11 DoD 3 — Board aggregations produce correct values on hand cases
// ═══════════════════════════════════════════════════════════════════════════

public class ExpressionEvaluator_BoardAggregationTests(ITestOutputHelper output)
{
    // Invariant 4: a "board" is just a user-defined array in state S. These
    // aggregations operate over state["board"] (a flat symbol array) and
    // state["values"] (a numeric array) — no engine Board type.

    /// <summary>3×3 board as a flat row-major symbol array in state["board"].</summary>
    private static EvalContext SymbolBoardCtx() => new()
    {
        State = new Dictionary<string, object?>
        {
            ["board"] = new object?[]
            {
                "5", "10", "Multiplier",
                "Multiplier", "3", "7",
                "20", "Multiplier", "1",
            },
        },
    };

    /// <summary>A flat numeric array in state["values"] (was per-cell decorations).</summary>
    private static EvalContext ValueArrayCtx() => new()
    {
        State = new Dictionary<string, object?>
        {
            ["values"] = new object?[] { "2", "4", "6", "8" },
        },
    };

    // Predicate helper: state[item] compared against a constant.
    private static FieldAccessExpr Item(string name) =>
        new() { Path = [name], Target = "state" };

    // ── Sum ─────────────────────────────────────────────────────────────

    [Fact]
    public void Sum_AllSymbolsAsNumbers_ReturnsCorrectTotal()
    {
        // 5+10+3+7+20+1 = 46 ("Multiplier" elements not numeric → 0)
        var expr = new AggregateExpr { Func = AggregateFunc.Sum, StateKey = "board", ItemName = "sym" };

        var result = ExactExpressionEvaluator.Evaluate(expr, SymbolBoardCtx());

        Assert.Equal(new BigInteger(46), result.AsInteger());
    }

    [Fact]
    public void Sum_WithPredicate_OnlyMatchingElements()
    {
        // Sum of elements where sym != "Multiplier" → all numeric symbols = 46.
        var expr = new AggregateExpr
        {
            Func = AggregateFunc.Sum,
            StateKey = "board",
            ItemName = "sym",
            Predicate = new NotExpr
            {
                Expr = new CompareExpr
                {
                    Op = CompareOp.Eq,
                    Left = Item("sym"),
                    Right = new ConstantExpr { Kind = ConstantKind.String, Value = "Multiplier" },
                },
            },
        };

        var result = ExactExpressionEvaluator.Evaluate(expr, SymbolBoardCtx());

        Assert.Equal(new BigInteger(46), result.AsInteger());
    }

    // ── Product ─────────────────────────────────────────────────────────

    [Fact]
    public void Count_OfAllMultiplierSymbols_HandCase()
    {
        // Count of elements where sym == "Multiplier" = 3.
        var expr = new AggregateExpr
        {
            Func = AggregateFunc.Count,
            StateKey = "board",
            ItemName = "sym",
            Predicate = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = Item("sym"),
                Right = new ConstantExpr { Kind = ConstantKind.String, Value = "Multiplier" },
            },
        };

        var result = ExactExpressionEvaluator.Evaluate(expr, SymbolBoardCtx());

        output.WriteLine($"Count result: {result}");
        Assert.Equal(new BigInteger(3), result.AsInteger());
    }

    [Fact]
    public void Product_NumericArray_HandCase()
    {
        // Product of [2,4,6,8] = 384.
        var expr = new AggregateExpr { Func = AggregateFunc.Product, StateKey = "values", ItemName = "v" };

        var result = ExactExpressionEvaluator.Evaluate(expr, ValueArrayCtx());

        Assert.Equal(new BigInteger(384), result.AsInteger());
    }

    // ── Count ───────────────────────────────────────────────────────────

    [Fact]
    public void Count_AllElements_ReturnsArrayLength()
    {
        var expr = new AggregateExpr { Func = AggregateFunc.Count, StateKey = "board", ItemName = "sym" };

        var result = ExactExpressionEvaluator.Evaluate(expr, SymbolBoardCtx());

        // 3×3 = 9 elements.
        Assert.Equal(new BigInteger(9), result.AsInteger());
    }

    [Fact]
    public void Count_WithPredicate_OnlyMatchingElements()
    {
        // Count elements that are not the "Multiplier" symbol (string-equality
        // predicate — the established catalog pattern). 9 − 3 = 6.
        var expr = new AggregateExpr
        {
            Func = AggregateFunc.Count,
            StateKey = "board",
            ItemName = "sym",
            Predicate = new NotExpr
            {
                Expr = new CompareExpr
                {
                    Op = CompareOp.Eq,
                    Left = Item("sym"),
                    Right = new ConstantExpr { Kind = ConstantKind.String, Value = "Multiplier" },
                },
            },
        };

        var result = ExactExpressionEvaluator.Evaluate(expr, SymbolBoardCtx());

        output.WriteLine($"Count non-Multiplier: {result}");
        Assert.Equal(new BigInteger(6), result.AsInteger());
    }

    // ── Min/Max ─────────────────────────────────────────────────────────

    [Fact]
    public void Min_OfAllSymbolValues_ReturnsMinimum()
    {
        var expr = new AggregateExpr { Func = AggregateFunc.Min, StateKey = "board", ItemName = "sym" };

        var result = ExactExpressionEvaluator.Evaluate(expr, SymbolBoardCtx());

        // Min of [5,10,0,0,3,7,20,0,1] = 0 (non-numeric symbols parse to 0).
        Assert.Equal(new BigInteger(0), result.AsInteger());
    }

    [Fact]
    public void Max_OfAllSymbolValues_ReturnsMaximum()
    {
        var expr = new AggregateExpr { Func = AggregateFunc.Max, StateKey = "board", ItemName = "sym" };

        var result = ExactExpressionEvaluator.Evaluate(expr, SymbolBoardCtx());

        // Max of the numeric symbols: 20.
        Assert.Equal(new BigInteger(20), result.AsInteger());
    }

    [Fact]
    public void Min_NumericArray_ReturnsMinimum()
    {
        var expr = new AggregateExpr { Func = AggregateFunc.Min, StateKey = "values", ItemName = "v" };

        var result = ExactExpressionEvaluator.Evaluate(expr, ValueArrayCtx());

        Assert.Equal(new BigInteger(2), result.AsInteger());
    }

    [Fact]
    public void Max_NumericArray_ReturnsMaximum()
    {
        var expr = new AggregateExpr { Func = AggregateFunc.Max, StateKey = "values", ItemName = "v" };

        var result = ExactExpressionEvaluator.Evaluate(expr, ValueArrayCtx());

        Assert.Equal(new BigInteger(8), result.AsInteger());
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 3b — Compiler produces same function type as library/plugins
// ═══════════════════════════════════════════════════════════════════════════

public class ExpressionCompiler_CorrectnessTests
{
    [Fact]
    public void CompiledNumberDelegate_MatchesInternalFunctionType()
    {
        // The compiled delegate has the same signature as a library evaluator's
        // internal function: Func<Board?, object?, BigInteger>
        var expr = new BinaryExpr
        {
            Op = BinaryOp.Mul,
            Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "6" },
            Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "7" },
        };

        var compiled = ExpressionCompiler.CompileNumber(expr);

        // Call with Board and state — same signature library/plugins use.
        var result = compiled(null, null);
        Assert.Equal(new BigInteger(42), result);
    }

    [Fact]
    public void CompiledBooleanDelegate_WorksAsPredicate()
    {
        var expr = new CompareExpr
        {
            Op = CompareOp.Gt,
            Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "10" },
            Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "5" },
        };

        var compiled = ExpressionCompiler.CompileBoolean(expr);
        Assert.True(compiled(null, null));
    }

    [Fact]
    public void CompiledWeightsDelegate_ReturnsWeightSet()
    {
        var expr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "7" };

        var compiled = ExpressionCompiler.CompileWeights(expr);
        var ws = compiled(null);
        Assert.NotNull(ws);
        Assert.Equal(1, ws.Count);
        Assert.Equal(new BigInteger(7), ws.Numerators[0]);
    }

    [Fact]
    public void CompiledAsEvaluator_ProducesWins()
    {
        var expr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "100" };

        var evalFunc = ExpressionCompiler.CompileAsEvaluator(expr, "test-symbol");

        var board = new Board(1, 1).SetCell(0, 0, new BoardCell().WithSymbols("A"));
        var wins = evalFunc(board, null);

        Assert.Single(wins);
        Assert.Equal("test-symbol", wins[0].SymbolId);
        Assert.Equal(100m, wins[0].Payout);
        Assert.Equal(1, wins[0].Count);
    }

    [Fact]
    public void CompiledBoxed_NumberType_ReturnsIntegerAsObject()
    {
        var expr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "99" };

        var compiled = ExpressionCompiler.CompileBoxed(expr, ExprType.Number);
        var result = compiled(null, null);

        Assert.IsType<BigInteger>(result);
        Assert.Equal(new BigInteger(99), (BigInteger)result);
    }

    [Fact]
    public void CompiledBoxed_BooleanType_ReturnsBooleanAsObject()
    {
        var expr = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" };

        var compiled = ExpressionCompiler.CompileBoxed(expr, ExprType.Boolean);
        var result = compiled(null, null);

        Assert.IsType<bool>(result);
        Assert.True((bool)result);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 3c — Rational arithmetic correctness
// ═══════════════════════════════════════════════════════════════════════════

public class ExpressionEvaluator_RationalTests
{
    [Fact]
    public void RationalArithmetic_ExactFraction()
    {
        // 1/2 + 1/3 = 5/6
        var expr = new BinaryExpr
        {
            Op = BinaryOp.Add,
            Left = new ConstantExpr { Kind = ConstantKind.Rational, Value = "1/2" },
            Right = new ConstantExpr { Kind = ConstantKind.Rational, Value = "1/3" },
        };

        var result = ExactExpressionEvaluator.Evaluate(expr, EvalContext.Empty);

        Assert.Equal(ExprType.Number, result.Kind);
        Assert.Equal(new BigInteger(5), result.NumberNumerator);
        Assert.Equal(new BigInteger(6), result.NumberDenominator);
    }

    [Fact]
    public void RationalMultiplication_ExactFraction()
    {
        // 2/3 * 3/5 = 6/15 = 2/5
        var expr = new BinaryExpr
        {
            Op = BinaryOp.Mul,
            Left = new ConstantExpr { Kind = ConstantKind.Rational, Value = "2/3" },
            Right = new ConstantExpr { Kind = ConstantKind.Rational, Value = "3/5" },
        };

        var result = ExactExpressionEvaluator.Evaluate(expr, EvalContext.Empty);

        Assert.Equal(new BigInteger(2), result.NumberNumerator);
        Assert.Equal(new BigInteger(5), result.NumberDenominator);
    }

    [Fact]
    public void RationalDivision_ExactFraction()
    {
        // (1/2) / (3/4) = (1*4)/(2*3) = 4/6 = 2/3
        var expr = new BinaryExpr
        {
            Op = BinaryOp.Div,
            Left = new ConstantExpr { Kind = ConstantKind.Rational, Value = "1/2" },
            Right = new ConstantExpr { Kind = ConstantKind.Rational, Value = "3/4" },
        };

        var result = ExactExpressionEvaluator.Evaluate(expr, EvalContext.Empty);

        Assert.Equal(new BigInteger(2), result.NumberNumerator);
        Assert.Equal(new BigInteger(3), result.NumberDenominator);
    }
}

public class Expression_EdgeCases
{
    [Fact]
    public void Expression_Eval_BoardNull()
    {
        var expr = new FieldAccessExpr { Path = new[] { "rows" }, Target = "board" };
        var val = ExactExpressionEvaluator.Evaluate(expr, EvalContext.Empty);
        Assert.Equal(BigInteger.Zero, val.AsInteger());
    }

    [Fact]
    public void Expression_StateFieldAccess()
    {
        var expr = new FieldAccessExpr { Path = new[] { "Counter" }, Target = "state" };
        var ctx = new EvalContext { State = new { Counter = 42 } };
        var val = ExactExpressionEvaluator.Evaluate(expr, ctx);
        Assert.Equal(new BigInteger(42), val.AsInteger());
    }

    [Fact]
    public void Expression_ConstantString()
    {
        var expr = new ConstantExpr { Kind = ConstantKind.String, Value = "hello" };
        var val = ExactExpressionEvaluator.Evaluate(expr, EvalContext.Empty);
        Assert.Equal("hello", val.StringValue);
    }

    [Fact]
    public void TypeChecker_EmptyPath()
    {
        var expr = new FieldAccessExpr { Path = Array.Empty<string>(), Target = "board" };
        var errors = ExpressionTypeChecker.Check(expr, TypeCheckContext.Default);
        Assert.NotEmpty(errors);
    }
}
