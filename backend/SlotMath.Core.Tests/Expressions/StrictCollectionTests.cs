using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Expressions;

public class StrictCollectionTests
{
    private static ConstantExpr N(string n) => new() { Kind = ConstantKind.Rational, Value = n };
    private static FieldAccessExpr Item => new() { Target = "state", Path = ["item"] };
    private static Expression Operation(string name) => name switch
    {
        "map" => new MapExpr { StateKey = "items", ItemName = "item", Body = Item },
        "filter" => new FilterExpr { StateKey = "items", ItemName = "item", Predicate = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" } },
        "fold" => new FoldExpr { StateKey = "items", ItemName = "item", AccName = "acc", Init = N("1/3"), Body = new FieldAccessExpr { Target = "state", Path = ["acc"] } },
        "index" => new FieldAccessExpr { Target = "state", Path = ["items", "0"] },
        _ => new AggregateExpr { StateKey = "items", ItemName = "item", Func = Enum.Parse<AggregateFunc>(name, true) }
    };
    private static ExprValue Evaluate(Expression expression, bool compiled, Dictionary<string, object?> state)
    {
        if (!compiled) return ExactExpressionEvaluator.Evaluate(expression, new EvalContext { State = state });
        var slots = new Dictionary<string, int> { ["items"] = 0, ["item"] = 1, ["acc"] = 2 };
        var frame = new SamplingFrame(slots, new SamplingCell[3], state); frame.Reset(CancellationToken.None);
        return new SamplingExpressions(key => slots[key]).Compile(expression)(frame);
    }
    public static IEnumerable<object[]> InvalidSources()
    {
        foreach (var compiled in new[] { false, true }) foreach (var name in new[] { "map", "filter", "fold", "index", "sum", "count", "min" })
            foreach (var kind in new[] { "missing", "null", "number", "string" }) yield return [compiled, name, kind];
    }
    [Theory][MemberData(nameof(InvalidSources))]
    public void AbsentAndWronglyTypedArraysCannotBeMistakenForAnEmptyFeature(bool compiled, string name, string kind)
    {
        Dictionary<string, object?> state = kind == "missing" ? new() : new() { ["items"] = kind switch { "null" => null, "number" => 0, _ => "" } };
        var error = Assert.Throws<ExpressionEvaluationException>(() => Evaluate(Operation(name), compiled, state));
        Assert.Equal(kind == "missing" ? "EVAL_MISSING_STATE" : kind == "null" ? "EVAL_NULL_VALUE" : "EVAL_TYPE_ERROR", error.Code);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void ExplicitEmptyArraysKeepTheirDefinedIdentitiesAndUndefinedExtrema(bool compiled)
    {
        var state = new Dictionary<string, object?> { ["items"] = Array.Empty<object>() };
        Assert.Equal(ExprValue.Number(0), Evaluate(Operation("sum"), compiled, state));
        Assert.Equal(ExprValue.Number(1), Evaluate(Operation("product"), compiled, state));
        Assert.Equal(ExprValue.Number(0), Evaluate(Operation("count"), compiled, state));
        Assert.Equal(ExprValue.Rational(1, 3), Evaluate(Operation("fold"), compiled, state));
        Assert.Empty(Evaluate(Operation("map"), compiled, state).ArrayValue!);
        Assert.Empty(Evaluate(Operation("filter"), compiled, state).ArrayValue!);
        foreach (var name in new[] { "min", "max" }) Assert.Equal(EvalErrorCodes.EmptyMinMax, Assert.Throws<ExpressionEvaluationException>(() => Evaluate(Operation(name), compiled, state)).Code);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void FractionalArrayElementsSurviveIndexingIterationAndFilter(bool compiled)
    {
        var expected = ExprValue.Rational(3, 4);
        foreach (var value in new object[] { new object[] { expected }, ExprValue.Array([expected]), new List<object> { expected } })
        {
            var state = new Dictionary<string, object?> { ["items"] = value };
            Assert.Equal(expected, Evaluate(Operation("index"), compiled, state));
            Assert.Equal(expected, Evaluate(Operation("sum"), compiled, state));
            Assert.Equal(expected, Assert.Single(Evaluate(Operation("map"), compiled, state).ArrayValue!));
            Assert.Equal(expected, Assert.Single(Evaluate(Operation("filter"), compiled, state).ArrayValue!));
        }
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void WronglyTypedPredicatesCannotManufactureAZeroCount(bool compiled)
    {
        var state = new Dictionary<string, object?> { ["items"] = new object[] { 1 } };
        Expression[] expressions = [new AggregateExpr { StateKey = "items", ItemName = "item", Func = AggregateFunc.Count, Predicate = N("0") },
            new FilterExpr { StateKey = "items", ItemName = "item", Predicate = N("0") }];
        foreach (var expression in expressions) Assert.Equal("EVAL_TYPE_ERROR", Assert.Throws<ExpressionEvaluationException>(() => Evaluate(expression, compiled, state)).Code);
    }
}
