using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;
using Xunit;

namespace SlotMath.Core.Tests.Expressions;

// ═══════════════════════════════════════════════════════════════════════════
//  G9 DoD — map/filter expressions: correct values, exact rationals,
//  nesting guard, type-checker acceptance/rejection
// ═══════════════════════════════════════════════════════════════════════════

public class MapExpr_EvaluationTests
{
    private static EvalContext StateWith(Dictionary<string, object?> d) =>
        new() { State = d };

    // ── Basic correctness ────────────────────────────────────────────────

    [Fact]
    public void Map_IntegerArray_DoublesEachElement()
    {
        // map(state.nums, n => n * 2)  where nums = [1, 2, 3]
        var expr = new MapExpr
        {
            StateKey = "nums",
            ItemName = "n",
            ItemType = ExprType.Number,
            Body = new BinaryExpr
            {
                Op = BinaryOp.Mul,
                Left = new FieldAccessExpr { Path = ["n"], Target = "state" },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" },
            },
        };

        var state = new Dictionary<string, object?>
        {
            ["nums"] = new object[] { (BigInteger)1, (BigInteger)2, (BigInteger)3 },
        };

        var result = ExactExpressionEvaluator.Evaluate(expr, StateWith(state));

        Assert.Equal(ExprType.Array, result.Kind);
        Assert.NotNull(result.ArrayValue);
        Assert.Equal(3, result.ArrayValue.Count);
        Assert.Equal(ExprValue.Number(2), result.ArrayValue[0]);
        Assert.Equal(ExprValue.Number(4), result.ArrayValue[1]);
        Assert.Equal(ExprValue.Number(6), result.ArrayValue[2]);
    }

    [Fact]
    public void Map_StringArray_TransformsToConstants()
    {
        // map(state.cells, c => if c=="H" then 5 else 1)
        var expr = new MapExpr
        {
            StateKey = "cells",
            ItemName = "c",
            ItemType = ExprType.String,
            Body = new IfExpr
            {
                Condition = new CompareExpr
                {
                    Op = CompareOp.Eq,
                    Left = new FieldAccessExpr { Path = ["c"], Target = "state" },
                    Right = new ConstantExpr { Kind = ConstantKind.String, Value = "H" },
                },
                ThenExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "5" },
                ElseExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
            },
        };

        var state = new Dictionary<string, object?> { ["cells"] = new string[] { "H", "L", "H" } };

        var result = ExactExpressionEvaluator.Evaluate(expr, StateWith(state));

        Assert.Equal(ExprType.Array, result.Kind);
        Assert.Equal(3, result.ArrayValue!.Count);
        Assert.Equal(ExprValue.Number(5), result.ArrayValue[0]);
        Assert.Equal(ExprValue.Number(1), result.ArrayValue[1]);
        Assert.Equal(ExprValue.Number(5), result.ArrayValue[2]);
    }

    [Fact]
    public void Map_EmptyArray_ReturnsEmptyArray()
    {
        var expr = new MapExpr
        {
            StateKey = "empty",
            ItemName = "x",
            Body = new ConstantExpr { Kind = ConstantKind.Integer, Value = "99" },
        };

        var state = new Dictionary<string, object?> { ["empty"] = new object[0] };
        var result = ExactExpressionEvaluator.Evaluate(expr, StateWith(state));

        Assert.Equal(ExprType.Array, result.Kind);
        Assert.Empty(result.ArrayValue!);
    }

    [Fact]
    public void Map_MissingStateKey_ReturnsEmptyArray()
    {
        var expr = new MapExpr
        {
            StateKey = "noSuchKey",
            ItemName = "x",
            Body = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
        };

        var result = ExactExpressionEvaluator.Evaluate(expr, StateWith(new()));

        Assert.Equal(ExprType.Array, result.Kind);
        Assert.Empty(result.ArrayValue!);
    }

    [Fact]
    public void Map_RationalBody_PreservesRationals_OnExactPath()
    {
        // map([1,2,3], n => n * 1/3)  — result items must be exact rationals
        var expr = new MapExpr
        {
            StateKey = "mults",
            ItemName = "m",
            ItemType = ExprType.Number,
            Body = new BinaryExpr
            {
                Op = BinaryOp.Mul,
                Left = new FieldAccessExpr { Path = ["m"], Target = "state" },
                Right = new ConstantExpr { Kind = ConstantKind.Rational, Value = "1/3" },
            },
        };

        var state = new Dictionary<string, object?>
        {
            ["mults"] = new object[] { (BigInteger)1, (BigInteger)2, (BigInteger)3 },
        };

        var result = ExactExpressionEvaluator.Evaluate(expr, StateWith(state));

        Assert.Equal(ExprType.Array, result.Kind);
        Assert.Equal(3, result.ArrayValue!.Count);
        // 1 * 1/3 = 1/3
        Assert.Equal(ExprValue.Rational(1, 3), result.ArrayValue[0]);
        // 2 * 1/3 = 2/3
        Assert.Equal(ExprValue.Rational(2, 3), result.ArrayValue[1]);
        // 3 * 1/3 = 1/1
        Assert.Equal(ExprValue.Number(1), result.ArrayValue[2]);
    }
}

public class FilterExpr_EvaluationTests
{
    private static EvalContext StateWith(Dictionary<string, object?> d) =>
        new() { State = d };

    // ── Basic correctness ────────────────────────────────────────────────

    [Fact]
    public void Filter_IntegerArray_KeepsMatchingElements()
    {
        // filter(state.nums, n => n > 2)  where nums = [1,2,3,4,5]
        var expr = new FilterExpr
        {
            StateKey = "nums",
            ItemName = "n",
            ItemType = ExprType.Number,
            Predicate = new CompareExpr
            {
                Op = CompareOp.Gt,
                Left = new FieldAccessExpr { Path = ["n"], Target = "state" },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" },
            },
        };

        var state = new Dictionary<string, object?>
        {
            ["nums"] = new object[] { (BigInteger)1, (BigInteger)2, (BigInteger)3, (BigInteger)4, (BigInteger)5 },
        };

        var result = ExactExpressionEvaluator.Evaluate(expr, StateWith(state));

        Assert.Equal(ExprType.Array, result.Kind);
        Assert.Equal(3, result.ArrayValue!.Count);
        Assert.Equal(ExprValue.Number(3), result.ArrayValue[0]);
        Assert.Equal(ExprValue.Number(4), result.ArrayValue[1]);
        Assert.Equal(ExprValue.Number(5), result.ArrayValue[2]);
    }

    [Fact]
    public void Filter_StringArray_KeepsMatchingStrings()
    {
        // filter(state.cells, c => c == "H")
        var expr = new FilterExpr
        {
            StateKey = "cells",
            ItemName = "c",
            ItemType = ExprType.String,
            Predicate = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = new FieldAccessExpr { Path = ["c"], Target = "state" },
                Right = new ConstantExpr { Kind = ConstantKind.String, Value = "H" },
            },
        };

        var state = new Dictionary<string, object?> { ["cells"] = new string[] { "H", "L", "W", "H" } };

        var result = ExactExpressionEvaluator.Evaluate(expr, StateWith(state));

        Assert.Equal(ExprType.Array, result.Kind);
        Assert.Equal(2, result.ArrayValue!.Count);
        Assert.Equal(ExprType.String, result.ArrayValue[0].Kind);
        Assert.Equal("H", result.ArrayValue[0].StringValue);
        Assert.Equal("H", result.ArrayValue[1].StringValue);
    }

    [Fact]
    public void Filter_NoneMatch_ReturnsEmptyArray()
    {
        var expr = new FilterExpr
        {
            StateKey = "nums",
            ItemName = "n",
            ItemType = ExprType.Number,
            Predicate = new CompareExpr
            {
                Op = CompareOp.Gt,
                Left = new FieldAccessExpr { Path = ["n"], Target = "state" },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "100" },
            },
        };

        var state = new Dictionary<string, object?>
        {
            ["nums"] = new object[] { (BigInteger)1, (BigInteger)2, (BigInteger)3 },
        };

        var result = ExactExpressionEvaluator.Evaluate(expr, StateWith(state));

        Assert.Equal(ExprType.Array, result.Kind);
        Assert.Empty(result.ArrayValue!);
    }

    [Fact]
    public void Filter_AllMatch_ReturnsCopy()
    {
        var expr = new FilterExpr
        {
            StateKey = "nums",
            ItemName = "n",
            ItemType = ExprType.Number,
            Predicate = new CompareExpr
            {
                Op = CompareOp.Gt,
                Left = new FieldAccessExpr { Path = ["n"], Target = "state" },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
            },
        };

        var state = new Dictionary<string, object?>
        {
            ["nums"] = new object[] { (BigInteger)1, (BigInteger)2, (BigInteger)3 },
        };

        var result = ExactExpressionEvaluator.Evaluate(expr, StateWith(state));

        Assert.Equal(3, result.ArrayValue!.Count);
    }
}

public class MapFilter_PipelineTests
{
    private static EvalContext StateWith(Dictionary<string, object?> d) =>
        new() { State = d };

    // ── Filter → store → fold pipeline ───────────────────────────────────

    [Fact]
    public void Filter_StoredInState_ThenFoldedToCount_ExactRational()
    {
        // cells = ["H","L","H","W","H"]
        // filtered = filter(cells, c => c=="H")   → ["H","H","H"]
        // count = fold(filtered, 0, (acc,_) => acc+1) = 3
        //
        // This proves the full round-trip:
        //  1. FilterExpr → ExprValue.Array
        //  2. ToStateObject() → object[]  (written to state["filtered"])
        //  3. FoldExpr reads it back → exact BigInteger result

        var cells = new string[] { "H", "L", "H", "W", "H" };

        var filterExpr = new FilterExpr
        {
            StateKey = "cells",
            ItemName = "c",
            ItemType = ExprType.String,
            Predicate = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = new FieldAccessExpr { Path = ["c"], Target = "state" },
                Right = new ConstantExpr { Kind = ConstantKind.String, Value = "H" },
            },
        };

        // Evaluate the filter
        var state = new Dictionary<string, object?> { ["cells"] = cells };
        var filtered = ExactExpressionEvaluator.Evaluate(filterExpr, StateWith(state));

        Assert.Equal(ExprType.Array, filtered.Kind);
        Assert.Equal(3, filtered.ArrayValue!.Count);

        // Write filtered result to state and fold over it
        state["filtered"] = filtered.ToStateObject();

        var countFold = new FoldExpr
        {
            StateKey = "filtered",
            AccName = "acc",
            ItemName = "_",
            ItemType = ExprType.String,
            Init = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
            Body = new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = new FieldAccessExpr { Path = ["acc"], Target = "state" },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
            },
        };

        var count = ExactExpressionEvaluator.Evaluate(countFold, StateWith(state));
        Assert.Equal(ExprType.Number, count.Kind);
        Assert.Equal(new BigInteger(3), count.AsInteger());
    }

    [Fact]
    public void Map_StoredInState_ThenFoldedToSum_ExactRational()
    {
        // mults = [1, 2, 3, 4]
        // doubled = map(mults, m => m * 2)  → [2, 4, 6, 8]
        // total = fold(doubled, 0, (acc, x) => acc + x) = 20

        var mults = new object[] { (BigInteger)1, (BigInteger)2, (BigInteger)3, (BigInteger)4 };

        var mapExpr = new MapExpr
        {
            StateKey = "mults",
            ItemName = "m",
            ItemType = ExprType.Number,
            Body = new BinaryExpr
            {
                Op = BinaryOp.Mul,
                Left = new FieldAccessExpr { Path = ["m"], Target = "state" },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" },
            },
        };

        var state = new Dictionary<string, object?> { ["mults"] = mults };
        var mapped = ExactExpressionEvaluator.Evaluate(mapExpr, StateWith(state));

        Assert.Equal(ExprType.Array, mapped.Kind);
        Assert.Equal(4, mapped.ArrayValue!.Count);

        state["doubled"] = mapped.ToStateObject();

        var sumFold = new FoldExpr
        {
            StateKey = "doubled",
            AccName = "acc",
            ItemName = "x",
            ItemType = ExprType.Number,
            Init = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
            Body = new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = new FieldAccessExpr { Path = ["acc"], Target = "state" },
                Right = new FieldAccessExpr { Path = ["x"], Target = "state" },
            },
        };

        var total = ExactExpressionEvaluator.Evaluate(sumFold, StateWith(state));
        Assert.Equal(ExprType.Number, total.Kind);
        Assert.Equal(new BigInteger(20), total.AsInteger());
    }

    [Fact]
    public void Map_RationalPipeline_FoldSum_ExactFraction()
    {
        // mults = [1, 2, 3]
        // weighted = map(mults, m => m/3)   → [1/3, 2/3, 3/3=1]
        // total = fold(weighted, 0, acc+x) = 1/3 + 2/3 + 1 = 2/1 = 2
        // (verifies rationals survive map → ToStateObject → fold round-trip)

        var mults = new object[] { (BigInteger)1, (BigInteger)2, (BigInteger)3 };

        var mapExpr = new MapExpr
        {
            StateKey = "mults",
            ItemName = "m",
            ItemType = ExprType.Number,
            Body = new BinaryExpr
            {
                Op = BinaryOp.Div,
                Left = new FieldAccessExpr { Path = ["m"], Target = "state" },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "3" },
            },
        };

        var state = new Dictionary<string, object?> { ["mults"] = mults };
        var mapped = ExactExpressionEvaluator.Evaluate(mapExpr, StateWith(state));

        state["weighted"] = mapped.ToStateObject();

        var sumFold = new FoldExpr
        {
            StateKey = "weighted",
            AccName = "acc",
            ItemName = "x",
            ItemType = ExprType.Number,
            Init = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
            Body = new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = new FieldAccessExpr { Path = ["acc"], Target = "state" },
                Right = new FieldAccessExpr { Path = ["x"], Target = "state" },
            },
        };

        var total = ExactExpressionEvaluator.Evaluate(sumFold, StateWith(state));
        // 1/3 + 2/3 + 3/3 = 6/3 = 2
        Assert.Equal(ExprType.Number, total.Kind);
        Assert.Equal(new BigInteger(2), total.AsInteger());
        // Numerator/denominator should be exactly 2/1 after reduction
        Assert.Equal(new BigInteger(2), total.NumberNumerator);
        Assert.Equal(new BigInteger(1), total.NumberDenominator);
    }
}

public class MapFilter_TypeCheckerTests
{
    private static readonly TypeCheckContext Ctx = new()
    {
        StateFields = new[]
        {
            new FieldDescriptor { Name = "nums",    Type = ExprType.Number },
            new FieldDescriptor { Name = "cells",   Type = ExprType.String },
            new FieldDescriptor { Name = "doubled", Type = ExprType.Array  },
        },
    };

    // ── Well-typed map/filter ────────────────────────────────────────────

    [Fact]
    public void MapExpr_WellTyped_NoErrors()
    {
        var expr = new MapExpr
        {
            StateKey = "nums",
            ItemName = "n",
            ItemType = ExprType.Number,
            Body = new BinaryExpr
            {
                Op = BinaryOp.Mul,
                Left = new FieldAccessExpr { Path = ["n"], Target = "state" },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" },
            },
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx);
        Assert.Empty(errors);
    }

    [Fact]
    public void MapExpr_ReturnsArrayType()
    {
        var expr = new MapExpr
        {
            StateKey = "nums",
            ItemName = "n",
            ItemType = ExprType.Number,
            Body = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
        };

        var errors = new List<TypeCheckError>();
        // Check that the returned ExprType is Array via the public API:
        // An array-typed expression used where Number is expected must produce a type-mismatch error.
        var mismatchErrors = ExpressionTypeChecker.Check(expr, Ctx, ExprType.Number);
        Assert.Contains(mismatchErrors, e => e.Message.Contains("Array") || e.Message.Contains("mismatch"));
    }

    [Fact]
    public void FilterExpr_WellTyped_NoErrors()
    {
        var expr = new FilterExpr
        {
            StateKey = "cells",
            ItemName = "c",
            ItemType = ExprType.String,
            Predicate = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = new FieldAccessExpr { Path = ["c"], Target = "state" },
                Right = new ConstantExpr { Kind = ConstantKind.String, Value = "H" },
            },
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx);
        Assert.Empty(errors);
    }

    [Fact]
    public void FilterExpr_NonBooleanPredicate_ProducesError()
    {
        var expr = new FilterExpr
        {
            StateKey = "nums",
            ItemName = "n",
            ItemType = ExprType.Number,
            Predicate = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" }, // Number, not Boolean
        };

        var errors = ExpressionTypeChecker.Check(expr, Ctx);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("Boolean"));
    }

    // ── Nesting guard ────────────────────────────────────────────────────

    [Fact]
    public void MapInsideFold_IsRejected()
    {
        // fold(nums, 0, (acc, x) => map(nums, n => n))  — map inside fold body
        var foldWithMap = new FoldExpr
        {
            StateKey = "nums",
            AccName = "acc",
            ItemName = "x",
            ItemType = ExprType.Number,
            Init = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
            Body = new MapExpr
            {
                StateKey = "nums",
                ItemName = "n",
                Body = new FieldAccessExpr { Path = ["n"], Target = "state" },
            },
        };

        var errors = ExpressionTypeChecker.Check(foldWithMap, Ctx);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("Nested fold is not allowed"));
    }

    [Fact]
    public void FilterInsideFold_IsRejected()
    {
        var foldWithFilter = new FoldExpr
        {
            StateKey = "nums",
            AccName = "acc",
            ItemName = "x",
            ItemType = ExprType.Number,
            Init = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
            Body = new FilterExpr
            {
                StateKey = "nums",
                ItemName = "n",
                Predicate = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" },
            },
        };

        var errors = ExpressionTypeChecker.Check(foldWithFilter, Ctx);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("Nested fold is not allowed"));
    }

    [Fact]
    public void FoldInsideMap_IsRejected()
    {
        var mapWithFold = new MapExpr
        {
            StateKey = "nums",
            ItemName = "n",
            ItemType = ExprType.Number,
            Body = new FoldExpr
            {
                StateKey = "nums",
                AccName = "acc",
                ItemName = "y",
                Init = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
                Body = new FieldAccessExpr { Path = ["acc"], Target = "state" },
            },
        };

        var errors = ExpressionTypeChecker.Check(mapWithFold, Ctx);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("Nested iteration is not allowed in a map body"));
    }

    [Fact]
    public void FoldInsideFilter_IsRejected()
    {
        var filterWithFold = new FilterExpr
        {
            StateKey = "nums",
            ItemName = "n",
            ItemType = ExprType.Number,
            Predicate = new FoldExpr
            {
                StateKey = "nums",
                AccName = "acc",
                ItemName = "y",
                Init = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" },
                Body = new FieldAccessExpr { Path = ["acc"], Target = "state" },
            },
        };

        var errors = ExpressionTypeChecker.Check(filterWithFold, Ctx);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("Nested iteration is not allowed in a filter predicate"));
    }

    [Fact]
    public void MapInsideMap_IsRejected()
    {
        var mapInMap = new MapExpr
        {
            StateKey = "nums",
            ItemName = "n",
            Body = new MapExpr
            {
                StateKey = "nums",
                ItemName = "m",
                Body = new FieldAccessExpr { Path = ["m"], Target = "state" },
            },
        };

        var errors = ExpressionTypeChecker.Check(mapInMap, Ctx);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("Nested iteration"));
    }

    [Fact]
    public void MapInsideIfBranch_InsideFold_IsRejected()
    {
        // fold body = if (true) then map(...) else 0
        var foldWithMapInBranch = new FoldExpr
        {
            StateKey = "nums",
            AccName = "acc",
            ItemName = "x",
            ItemType = ExprType.Number,
            Init = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
            Body = new IfExpr
            {
                Condition = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" },
                ThenExpr = new MapExpr
                {
                    StateKey = "nums",
                    ItemName = "n",
                    Body = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
                },
                ElseExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
            },
        };

        var errors = ExpressionTypeChecker.Check(foldWithMapInBranch, Ctx);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("Nested fold is not allowed"));
    }

    // ── JSON round-trip ─────────────────────────────────────────────────

    [Fact]
    public void MapExpr_RoundTrips_ThroughJson()
    {
        var expr = new MapExpr
        {
            StateKey = "nums",
            ItemName = "n",
            ItemType = ExprType.Number,
            Body = new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = new FieldAccessExpr { Path = ["n"], Target = "state" },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
            },
        };

        var json = System.Text.Json.JsonSerializer.Serialize<Expression>(expr);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<Expression>(json);

        Assert.IsType<MapExpr>(deserialized);
        var m = (MapExpr)deserialized!;
        Assert.Equal("nums", m.StateKey);
        Assert.Equal("n", m.ItemName);
        Assert.Equal(ExprType.Number, m.ItemType);
    }

    [Fact]
    public void FilterExpr_RoundTrips_ThroughJson()
    {
        var expr = new FilterExpr
        {
            StateKey = "cells",
            ItemName = "c",
            ItemType = ExprType.String,
            Predicate = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = new FieldAccessExpr { Path = ["c"], Target = "state" },
                Right = new ConstantExpr { Kind = ConstantKind.String, Value = "H" },
            },
        };

        var json = System.Text.Json.JsonSerializer.Serialize<Expression>(expr);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<Expression>(json);

        Assert.IsType<FilterExpr>(deserialized);
        var fi = (FilterExpr)deserialized!;
        Assert.Equal("cells", fi.StateKey);
        Assert.Equal("c", fi.ItemName);
        Assert.Equal(ExprType.String, fi.ItemType);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Index-aware iteration + array ops (contains/length/append) — the
//  foundation for position/sticky board mechanics as pure subgraphs (inv 2/4).
// ═══════════════════════════════════════════════════════════════════════════

public class IndexedIterationAndArrayOps
{
    private static EvalContext State(Dictionary<string, object?> d) => new() { State = d };

    private static Dictionary<string, object?> Board(params string[] cells) =>
        new() { ["board"] = cells.Cast<object?>().ToArray() };

    [Fact]
    public void ArrayValuedField_Contains_FindsSymbol()
    {
        // contains(state["board"], "S")
        var expr = new CallExpr
        {
            Function = "contains",
            Args =
            [
                new FieldAccessExpr { Target = "state", Path = ["board"] },
                new ConstantExpr { Kind = ConstantKind.String, Value = "S" },
            ],
        };
        Assert.True(ExactExpressionEvaluator.Evaluate(expr, State(Board("A", "S", "B"))).BoolValue);
        Assert.False(ExactExpressionEvaluator.Evaluate(expr, State(Board("A", "B", "C"))).BoolValue);
    }

    [Fact]
    public void ArrayValuedField_Length_CountsElements()
    {
        var expr = new CallExpr
        {
            Function = "length",
            Args = [new FieldAccessExpr { Target = "state", Path = ["board"] }],
        };
        Assert.Equal(new BigInteger(3),
            ExactExpressionEvaluator.Evaluate(expr, State(Board("A", "B", "C"))).AsInteger());
    }

    [Fact]
    public void IndexAwareMap_OverlaysByPosition()
    {
        // map(board, item, idx => if contains(stickyPositions, idx) then "W" else item)
        var expr = new MapExpr
        {
            StateKey = "board",
            ItemName = "item",
            IndexName = "idx",
            Body = new IfExpr
            {
                Condition = new CallExpr
                {
                    Function = "contains",
                    Args =
                    [
                        new FieldAccessExpr { Target = "state", Path = ["sticky"] },
                        new FieldAccessExpr { Target = "state", Path = ["idx"] },
                    ],
                },
                ThenExpr = new ConstantExpr { Kind = ConstantKind.String, Value = "W" },
                ElseExpr = new FieldAccessExpr { Target = "state", Path = ["item"] },
            },
        };

        var state = new Dictionary<string, object?>
        {
            ["board"] = new object?[] { "A", "B", "C", "D" },
            ["sticky"] = new object?[] { new BigInteger(1), new BigInteger(3) },
        };
        var result = ExactExpressionEvaluator.Evaluate(expr, State(state));
        var symbols = result.ArrayValue!.Select(v => v.StringValue).ToArray();
        Assert.Equal(["A", "W", "C", "W"], symbols);
    }

    [Fact]
    public void IndexCall_ReturnsElement_AndErrorsOutOfRange()
    {
        var arr = new FieldAccessExpr { Target = "state", Path = ["refill"] };
        ExprValue Index(int i) => ExactExpressionEvaluator.Evaluate(
            new CallExpr { Function = "index", Args = [arr, new ConstantExpr { Kind = ConstantKind.Integer, Value = i.ToString() }] },
            State(new Dictionary<string, object?> { ["refill"] = new object?[] { "A", "B", "C" } }));

        Assert.Equal("B", Index(1).StringValue);
        var ex = Assert.Throws<ExpressionEvaluationException>(() => Index(5));
        Assert.Equal(EvalErrorCodes.IndexOutOfRange, ex.Code);
    }

    [Fact]
    public void IndexAwareFold_WithAppend_CollectsMatchingPositions()
    {
        // fold(board, [], (acc, item, idx) => if item=="W" && !contains(acc, idx)
        //                                       then append(acc, idx) else acc)
        var idx = new FieldAccessExpr { Target = "state", Path = ["idx"] };
        var acc = new FieldAccessExpr { Target = "state", Path = ["acc"] };
        var expr = new FoldExpr
        {
            StateKey = "board",
            AccName = "acc",
            ItemName = "item",
            IndexName = "idx",
            ItemType = ExprType.String,
            Init = new FieldAccessExpr { Target = "state", Path = ["prev"] },
            Body = new IfExpr
            {
                Condition = new BinaryExpr
                {
                    Op = BinaryOp.And,
                    Left = new CompareExpr
                    {
                        Op = CompareOp.Eq,
                        Left = new FieldAccessExpr { Target = "state", Path = ["item"] },
                        Right = new ConstantExpr { Kind = ConstantKind.String, Value = "W" },
                    },
                    Right = new NotExpr
                    {
                        Expr = new CallExpr { Function = "contains", Args = [acc, idx] },
                    },
                },
                ThenExpr = new CallExpr { Function = "append", Args = [acc, idx] },
                ElseExpr = acc,
            },
        };

        // Board "W _ W _ W"; prior sticky already has index 0.
        var state = new Dictionary<string, object?>
        {
            ["board"] = new object?[] { "W", "x", "W", "x", "W" },
            ["prev"] = new object?[] { new BigInteger(0) },
        };
        var result = ExactExpressionEvaluator.Evaluate(expr, State(state));
        var indices = result.ArrayValue!.Select(v => (int)v.AsInteger()).OrderBy(x => x).ToArray();
        // 0 (prior, not re-added), 2, 4 added — 0 already present so deduped.
        Assert.Equal([0, 2, 4], indices);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Array-call edge cases: contains / length / append / index correctness
//  and interaction with index-aware iteration
// ═══════════════════════════════════════════════════════════════════════════

public class ArrayOpEdgeCases
{
    private static EvalContext State(Dictionary<string, object?> d) =>
        new() { State = d };

    private static ExprValue Eval(Expression e, Dictionary<string, object?> d) =>
        ExactExpressionEvaluator.Evaluate(e, State(d));

    private static CallExpr Call(string fn, params Expression[] args) =>
        new() { Function = fn, Args = args };

    private static ConstantExpr Int(int v) =>
        new() { Kind = ConstantKind.Integer, Value = v.ToString() };

    private static ConstantExpr Str(string v) =>
        new() { Kind = ConstantKind.String, Value = v };

    private static FieldAccessExpr Ref(string name) =>
        new() { Target = "state", Path = [name] };

    [Fact]
    public void Contains_EmptyArray_IsFalse()
    {
        var result = Eval(Call("contains", Ref("arr"), Str("A")),
            new() { ["arr"] = Array.Empty<object?>() });
        Assert.Equal(ExprValue.Bool(false), result);
    }

    [Fact]
    public void Length_EmptyArray_IsZero()
    {
        var result = Eval(Call("length", Ref("arr")),
            new() { ["arr"] = Array.Empty<object?>() });
        Assert.Equal(ExprValue.Number(0), result);
    }

    [Fact]
    public void Length_NonEmptyArray_IsCount()
    {
        var result = Eval(Call("length", Ref("arr")),
            new() { ["arr"] = new object?[] { "A", "B", "C" } });
        Assert.Equal(ExprValue.Number(3), result);
    }

    [Fact]
    public void Append_DoesNotMutateOriginalArray()
    {
        var original = new object?[] { "A", "B" };
        var state = new Dictionary<string, object?> { ["arr"] = original };
        var result = Eval(Call("append", Ref("arr"), Str("C")), state);

        Assert.Equal(3, result.ArrayValue!.Count);
        Assert.Equal(2, original.Length); // original unchanged
    }

    [Fact]
    public void Append_SingleElement_YieldsTwoElements()
    {
        var result = Eval(Call("append", Ref("arr"), Int(42)),
            new() { ["arr"] = new object?[] { (BigInteger)1 } });
        Assert.Equal(2, result.ArrayValue!.Count);
        Assert.Equal(ExprValue.Number(42), result.ArrayValue[1]);
    }

    [Fact]
    public void Index_InsideMap_RefillsByPosition()
    {
        // map(board, (cell, i) => index(refill, i))
        var expr = new MapExpr
        {
            StateKey = "board",
            ItemName = "cell",
            IndexName = "i",
            ItemType = ExprType.String,
            Body = Call("index", Ref("refill"), Ref("i")),
        };
        var state = new Dictionary<string, object?>
        {
            ["board"] = new object?[] { "x", "x", "x" },
            ["refill"] = new object?[] { "A", "B", "C" },
        };
        var result = Eval(expr, state);
        var strs = result.ArrayValue!.Select(v => v.StringValue).ToArray();
        Assert.Equal(["A", "B", "C"], strs);
    }

    [Fact]
    public void FilterWithIndex_KeepsByPosition()
    {
        // filter(board, (cell, i) => contains(keep, i))  keep = [1, 3]
        var expr = new FilterExpr
        {
            StateKey = "board",
            ItemName = "cell",
            IndexName = "i",
            ItemType = ExprType.String,
            Predicate = Call("contains", Ref("keep"), Ref("i")),
        };
        var state = new Dictionary<string, object?>
        {
            ["board"] = new object?[] { "A", "B", "C", "D" },
            ["keep"] = new object?[] { (BigInteger)1, (BigInteger)3 },
        };
        var result = Eval(expr, state);
        var strs = result.ArrayValue!.Select(v => v.StringValue).ToArray();
        Assert.Equal(["B", "D"], strs);
    }

    [Fact]
    public void MapWithIndex_ExposesZeroBasedIndex()
    {
        // map(arr, (x, i) => i)  — just return the index itself
        var expr = new MapExpr
        {
            StateKey = "arr",
            ItemName = "x",
            IndexName = "i",
            ItemType = ExprType.Number,
            Body = Ref("i"),
        };
        var state = new Dictionary<string, object?> { ["arr"] = new object?[] { "a", "b", "c" } };
        var result = Eval(expr, state);
        var nums = result.ArrayValue!.Select(v => (int)v.AsInteger()).ToArray();
        Assert.Equal([0, 1, 2], nums);
    }

    [Fact]
    public void Length_OfString_CountsCharacters()
    {
        var result = Eval(Call("length", Str("hello")), new());
        Assert.Equal(ExprValue.Number(5), result);
    }

    [Fact]
    public void Contains_Substring_StillWorks()
    {
        var result = Eval(
            Call("contains", Str("WILD_bonus"), Str("bonus")), new());
        Assert.Equal(ExprValue.Bool(true), result);
    }

    [Fact]
    public void Index_NegativeIndex_Throws()
    {
        var ex = Assert.Throws<ExpressionEvaluationException>(() =>
            Eval(Call("index", Ref("arr"), Int(-1)),
                new() { ["arr"] = new object?[] { "A" } }));
        Assert.Equal(EvalErrorCodes.IndexOutOfRange, ex.Code);
    }

    [Fact]
    public void Append_ToEmptyArray_YieldsSingleton()
    {
        var result = Eval(Call("append", Ref("arr"), Str("X")),
            new() { ["arr"] = Array.Empty<object?>() });
        Assert.Single(result.ArrayValue!);
        Assert.Equal("X", result.ArrayValue[0].StringValue);
    }

    [Fact]
    public void Contains_NumberInArray_IsTrue()
    {
        var result = Eval(Call("contains", Ref("arr"), Int(2)),
            new() { ["arr"] = new object?[] { (BigInteger)1, (BigInteger)2, (BigInteger)3 } });
        Assert.Equal(ExprValue.Bool(true), result);
    }

    [Fact]
    public void Contains_NumberNotInArray_IsFalse()
    {
        var result = Eval(Call("contains", Ref("arr"), Int(99)),
            new() { ["arr"] = new object?[] { (BigInteger)1, (BigInteger)2 } });
        Assert.Equal(ExprValue.Bool(false), result);
    }

    [Fact]
    public void Fold_OverSingleElement_ReturnsCorrectResult()
    {
        // fold(arr, 0, (acc, x) => acc + x)  where arr = [7]
        var expr = new FoldExpr
        {
            StateKey = "arr",
            AccName = "acc",
            ItemName = "x",
            ItemType = ExprType.Number,
            Init = Int(0),
            Body = new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = Ref("acc"),
                Right = Ref("x"),
            },
        };
        var result = Eval(expr, new() { ["arr"] = new object?[] { (BigInteger)7 } });
        Assert.Equal(ExprValue.Number(7), result);
    }

    [Fact]
    public void Map_OverSingleElement_ReturnsSingletonArray()
    {
        var expr = new MapExpr
        {
            StateKey = "arr",
            ItemName = "x",
            ItemType = ExprType.Number,
            Body = new BinaryExpr
            {
                Op = BinaryOp.Mul,
                Left = Ref("x"),
                Right = Int(10),
            },
        };
        var result = Eval(expr, new() { ["arr"] = new object?[] { (BigInteger)5 } });
        Assert.Equal(ExprType.Array, result.Kind);
        Assert.Single(result.ArrayValue!);
        Assert.Equal(ExprValue.Number(50), result.ArrayValue![0]);
    }
}
