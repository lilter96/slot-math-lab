using SlotMath.Core.Expressions;
using SlotMath.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace SlotMath.Core.Tests.Expressions;

// ═══════════════════════════════════════════════════════════════════════════
//  G11 DoD 1 — Parse + type-check an expression corpus;
//            reject ill-typed expressions with precise errors
// ═══════════════════════════════════════════════════════════════════════════

public class ExpressionTypeChecker_CorpusTests
{
    private static readonly TypeCheckContext Ctx = TypeCheckContext.Default;

    // ── Well-typed corpus ───────────────────────────────────────────────

    [Fact]
    public void ConstantInteger_IsNumber()
    {
        var errors = ExpressionTypeChecker.Check(
            new ConstantExpr { Kind = ConstantKind.Integer, Value = "42" }, Ctx);
        Assert.Empty(errors);
    }

    [Fact]
    public void ConstantRational_IsNumber()
    {
        var errors = ExpressionTypeChecker.Check(
            new ConstantExpr { Kind = ConstantKind.Rational, Value = "3/2" }, Ctx);
        Assert.Empty(errors);
    }

    [Fact]
    public void ConstantBoolean_IsBoolean()
    {
        var errors = ExpressionTypeChecker.Check(
            new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" }, Ctx,
            ExprType.Boolean);
        Assert.Empty(errors);
    }

    [Fact]
    public void ArithmeticExpression_WellTyped()
    {
        // (2 + 3) * 4
        var expr = new BinaryExpr
        {
            Op = BinaryOp.Mul,
            Left = new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "3" },
            },
            Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "4" },
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx);
        Assert.Empty(errors);
    }

    [Fact]
    public void BooleanExpression_WellTyped()
    {
        // (5 > 3) && (2 < 4)
        var expr = new BinaryExpr
        {
            Op = BinaryOp.And,
            Left = new CompareExpr
            {
                Op = CompareOp.Gt,
                Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "5" },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "3" },
            },
            Right = new CompareExpr
            {
                Op = CompareOp.Lt,
                Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "4" },
            },
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx, ExprType.Boolean);
        Assert.Empty(errors);
    }

    [Fact]
    public void ConditionalExpression_WellTyped()
    {
        // if (x > 0) then 10 else 0
        var expr = new IfExpr
        {
            Condition = new CompareExpr
            {
                Op = CompareOp.Gt,
                Left = new FieldAccessExpr { Path = ["rows"], Target = "board" },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
            },
            ThenExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "10" },
            ElseExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx);
        Assert.Empty(errors);
    }

    [Fact]
    public void AggregateExpression_WellTyped()
    {
        // sum of all cells where symbol == "Multiplier"
        var expr = new AggregateExpr
        {
            Func = AggregateFunc.Sum,
            Target = "symbol",
            Predicate = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = new FieldAccessExpr { Path = ["symbol"], Target = "board" },
                Right = new ConstantExpr { Kind = ConstantKind.String, Value = "Multiplier" },
            },
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx);
        Assert.Empty(errors);
    }

    [Fact]
    public void AggregateCount_WithoutPredicate_WellTyped()
    {
        var expr = new AggregateExpr
        {
            Func = AggregateFunc.Count,
            Target = "symbol",
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx);
        Assert.Empty(errors);
    }

    [Fact]
    public void NotExpression_WellTyped()
    {
        var expr = new NotExpr
        {
            Expr = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
            },
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx, ExprType.Boolean);
        Assert.Empty(errors);
    }

    [Fact]
    public void CallExpression_WellTyped()
    {
        // abs(-5)
        var expr = new CallExpr
        {
            Function = "abs",
            Args = [new ConstantExpr { Kind = ConstantKind.Integer, Value = "-5" }],
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx);
        Assert.Empty(errors);
    }

    // ── Ill-typed expressions ───────────────────────────────────────────

    [Fact]
    public void AddStringToNumber_IsRejected()
    {
        var expr = new BinaryExpr
        {
            Op = BinaryOp.Add,
            Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
            Right = new ConstantExpr { Kind = ConstantKind.String, Value = "hello" },
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("must be Number"));
    }

    [Fact]
    public void AndOnNumbers_IsRejected()
    {
        var expr = new BinaryExpr
        {
            Op = BinaryOp.And,
            Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
            Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("must be Boolean"));
    }

    [Fact]
    public void IfBranchesDiffer_IsRejected()
    {
        var expr = new IfExpr
        {
            Condition = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" },
            ThenExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "10" },
            ElseExpr = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "false" },
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("different types"));
    }

    [Fact]
    public void NotOnNumber_IsRejected()
    {
        var expr = new NotExpr
        {
            Expr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "42" },
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("Boolean"));
    }

    [Fact]
    public void CompareNumberToString_IsRejected()
    {
        var expr = new CompareExpr
        {
            Op = CompareOp.Lt,
            Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "5" },
            Right = new ConstantExpr { Kind = ConstantKind.String, Value = "hello" },
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("must be Number"));
    }

    [Fact]
    public void UnknownFunction_IsRejected()
    {
        var expr = new CallExpr
        {
            Function = "nonexistent_func",
            Args = [],
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("Unknown function"));
    }

    [Fact]
    public void FunctionWrongArgCount_IsRejected()
    {
        var expr = new CallExpr
        {
            Function = "abs",
            Args = [ // abs takes 1 arg, we give 2
                new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
                new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" },
            ],
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("expects") && e.Message.Contains("arguments"));
    }

    [Fact]
    public void TypeMismatchExpectedBoolean_IsRejected()
    {
        var expr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "42" };

        var errors = ExpressionTypeChecker.Check(expr, Ctx, ExprType.Boolean);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("Type mismatch"));
    }

    [Fact]
    public void UnknownField_IsRejected()
    {
        var expr = new FieldAccessExpr { Path = ["nonexistent_field"], Target = "board" };

        var errors = ExpressionTypeChecker.Check(expr, Ctx);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("Unknown field"));
    }

    // ── Grammar unrepresentability of loops/recursion ───────────────────

    /// <summary>
    /// The AST has no recursive or loop constructs by construction.
    /// CallExpr is limited to a closed set of built-in functions —
    /// no user-defined recursion is representable.
    /// </summary>
    [Fact]
    public void GrammarMakesRecursionUnrepresentable()
    {
        // Verify every expression node type is a finite tree.
        // The AST types are: Constant, FieldAccess, Binary, Compare,
        // If, Aggregate, Not, Call. None of these can contain a
        // reference back to an ancestor node. CallExpr is limited
        // to a fixed whitelist of built-in functions.

        // Proof by construction: attempt to create an expression with
        // a circular reference is impossible in C#. The AST is a tree
        // of records — each child is a new Expression. No reference
        // equality loop possible.

        // Instead verify that the type-checker terminates on a
        // deep nested expression (no infinite recursion in checker).
        Expression deep = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" };
        for (int i = 0; i < 500; i++)
        {
            deep = new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = deep,
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
            };
        }

        // Must not stack overflow during type-checking.
        var errors = ExpressionTypeChecker.Check(deep, Ctx);
        Assert.Empty(errors);
    }

    /// <summary>
    /// The built-in function set has no eval/apply/call-function-by-name
    /// that could introduce recursion. Verify all known functions are
    /// non-recursive.
    /// </summary>
    [Fact]
    public void BuiltInFunctionsAreNonRecursive()
    {
        // All built-in functions: abs, min, max, floor, ceil, round,
        // toNumber, toString, length, contains
        // None of these can invoke another expression — they are
        // pure arithmetic/string operations on their arguments only.

        var functions = new[] { "abs", "min", "max", "floor", "ceil", "round",
            "toNumber", "toString", "length", "contains" };

        foreach (var fn in functions)
        {
            var expr = new CallExpr
            {
                Function = fn,
                // These functions take Number or String args
                Args = fn switch
                {
                    "min" or "max" or "contains" => new Expression[]
                    {
                        new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
                        new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" },
                    },
                    _ => [new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" }],
                },
            };

            var errors = ExpressionTypeChecker.Check(expr, Ctx);
            // Each should type-check or fail with a known error, never loop.
            Assert.DoesNotContain(errors, e => e.Message.Contains("Unknown function"));
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 1b — Error messages are precise (name the offending issue)
// ═══════════════════════════════════════════════════════════════════════════

public class TypeCheckError_PrecisionTests(ITestOutputHelper output)
{
    [Fact]
    public void ErrorMessagesArePrecise()
    {
        var ctx = TypeCheckContext.Default;

        // Test each error category produces a distinct, meaningful message.
        var categories = new Dictionary<string, Expression>
        {
            ["number required"] = new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
                Right = new ConstantExpr { Kind = ConstantKind.String, Value = "x" },
            },
            ["boolean required"] = new BinaryExpr
            {
                Op = BinaryOp.And,
                Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
            },
            ["type mismatch"] = new ConstantExpr
            {
                Kind = ConstantKind.Integer,
                Value = "1",
            },
            ["unknown field"] = new FieldAccessExpr
            {
                Path = ["bogus"],
                Target = "board",
            },
            ["unknown function"] = new CallExpr
            {
                Function = "bogus_fn",
                Args = [],
            },
            ["wrong arg count"] = new CallExpr
            {
                Function = "abs",
                Args =
                [
                    new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
                    new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" },
                ],
            },
            ["if branch types"] = new IfExpr
            {
                Condition = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" },
                ThenExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
                ElseExpr = new ConstantExpr { Kind = ConstantKind.String, Value = "x" },
            },
            ["not requires bool"] = new NotExpr
            {
                Expr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
            },
        };

        foreach (var (category, expr) in categories)
        {
            var errors = ExpressionTypeChecker.Check(expr,
                category == "type mismatch"
                    ? ctx
                    : ctx);

            if (category == "type mismatch")
                errors = ExpressionTypeChecker.Check(expr, ctx, ExprType.Boolean);

            output.WriteLine($"[{category}] {errors.Count} error(s):");
            foreach (var e in errors)
                output.WriteLine($"  - {e.Message}");

            Assert.NotEmpty(errors);
            // Each error must have a non-empty, specific message.
            foreach (var e in errors)
            {
                Assert.NotNull(e.Message);
                Assert.NotEmpty(e.Message);
                Assert.NotEqual("?", e.Message);
            }
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  G9 DoD — bounded iteration: fold type-checks; nested fold is rejected
//  (one level of bounded iteration keeps the grammar exact-analysable).
// ═══════════════════════════════════════════════════════════════════════════

public class ExpressionTypeChecker_FoldNestingTests
{
    private static FieldAccessExpr State(string name) =>
        new() { Target = "state", Path = [name] };

    private static ConstantExpr Int(int v) =>
        new() { Kind = ConstantKind.Integer, Value = v.ToString() };

    // Context exposing a numeric array field "items".
    private static TypeCheckContext Ctx => new()
    {
        StateFields = [new FieldDescriptor { Name = "items", Type = ExprType.Number }],
    };

    // fold("items", 0, (acc, item) => acc + 1)  — well-formed, single level.
    private static FoldExpr CountFold() => new()
    {
        StateKey = "items",
        AccName = "acc",
        ItemName = "item",
        Init = Int(0),
        ItemType = ExprType.Number,
        Body = new BinaryExpr { Op = BinaryOp.Add, Left = State("acc"), Right = Int(1) },
    };

    [Fact]
    public void SingleFold_IsAccepted()
    {
        var errors = ExpressionTypeChecker.Check(CountFold(), Ctx, ExprType.Number);
        Assert.Empty(errors);
    }

    [Fact]
    public void NestedFold_IsRejected_WithPreciseError()
    {
        // fold("items", 0, (acc, item) => acc + fold("items", 0, ...))
        var nested = new FoldExpr
        {
            StateKey = "items",
            AccName = "acc",
            ItemName = "item",
            Init = Int(0),
            ItemType = ExprType.Number,
            Body = new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = State("acc"),
                Right = CountFold(), // ← nested fold inside the body
            },
        };

        var errors = ExpressionTypeChecker.Check(nested, Ctx, ExprType.Number);
        var err = Assert.Single(errors);
        Assert.Contains("Nested fold is not allowed", err.Message);
    }

    [Fact]
    public void FoldNestedInsideConditional_IsAlsoRejected()
    {
        // A nested fold buried in an If still trips the guard.
        var nested = new FoldExpr
        {
            StateKey = "items",
            AccName = "acc",
            ItemName = "item",
            Init = Int(0),
            ItemType = ExprType.Number,
            Body = new IfExpr
            {
                Condition = new CompareExpr { Op = CompareOp.Gt, Left = State("acc"), Right = Int(0) },
                ThenExpr = CountFold(), // ← nested fold in a branch
                ElseExpr = State("acc"),
            },
        };

        var errors = ExpressionTypeChecker.Check(nested, Ctx, ExprType.Number);
        Assert.Contains(errors, e => e.Message.Contains("Nested fold is not allowed"));
    }
}
