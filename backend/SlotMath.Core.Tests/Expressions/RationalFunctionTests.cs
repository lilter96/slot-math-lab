using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Expressions;

public class RationalFunctionTests
{
    private static ConstantExpr Q(string value) => new() { Kind = ConstantKind.Rational, Value = value };
    private static ExprValue Expected(string value)
    {
        var parts = value.Split('/'); return ExprValue.Rational(BigInteger.Parse(parts[0]), parts.Length == 1 ? 1 : BigInteger.Parse(parts[1]));
    }
    private static ExprValue Evaluate(Expression expression, bool compiled, object[]? items = null)
    {
        var state = new Dictionary<string, object?> { ["items"] = items ?? [] };
        if (!compiled) return ExactExpressionEvaluator.Evaluate(expression, new EvalContext { State = state });
        var slots = new Dictionary<string, int> { ["items"] = 0, ["item"] = 1 };
        var frame = new SamplingFrame(slots, new SamplingCell[2], state); frame.Reset(CancellationToken.None);
        return new SamplingExpressions(key => slots[key]).Compile(expression)(frame);
    }
    public static IEnumerable<object[]> ScalarExamples()
    {
        (string Function, string A, string? B, string Expected)[] cases = [
            ("abs", "-1/2", null, "1/2"), ("abs", "2/3", null, "2/3"), ("abs", "0", null, "0"),
            ("min", "1/2", "3/4", "1/2"), ("max", "1/2", "3/4", "3/4"),
            ("min", "-3/4", "-1/2", "-3/4"), ("max", "-3/4", "-1/2", "-1/2"),
            ("floor", "-1/2", null, "-1"), ("floor", "-7/3", null, "-3"), ("floor", "7/3", null, "2"),
            ("ceil", "-7/3", null, "-2"), ("ceil", "7/3", null, "3"),
            ("round", "-1/2", null, "-1"), ("round", "1/2", null, "1"),
            ("round", "-7/3", null, "-2"), ("round", "8/3", null, "3"), ("floor", "-2", null, "-2"), ("ceil", "2", null, "2")];
        foreach (var compiled in new[] { false, true }) foreach (var example in cases)
            yield return [compiled, example.Function, example.A, example.B!, example.Expected];
    }
    [Theory][MemberData(nameof(ScalarExamples))]
    public void NumericFunctionsMatchSpecifiedExactFractionsAndSignedIntegerBoundaries(bool compiled, string function, string a, string? b, string expected)
    {
        var expression = new CallExpr { Function = function, Args = b is null ? [Q(a)] : [Q(a), Q(b)] };
        Assert.Equal(Expected(expected), Evaluate(expression, compiled));
    }
    [Theory]
    [InlineData(false, AggregateFunc.Sum, "17/12")][InlineData(true, AggregateFunc.Sum, "17/12")]
    [InlineData(false, AggregateFunc.Product, "-5/12")][InlineData(true, AggregateFunc.Product, "-5/12")]
    [InlineData(false, AggregateFunc.Min, "-1/2")][InlineData(true, AggregateFunc.Min, "-1/2")]
    [InlineData(false, AggregateFunc.Max, "5/4")][InlineData(true, AggregateFunc.Max, "5/4")]
    public void SelectorAndTypedArrayReductionsPreserveTheFullRationalValue(bool compiled, AggregateFunc function, string expected)
    {
        var expression = new AggregateExpr { StateKey = "items", ItemName = "item", Func = function };
        object[] values = [Expected("-1/2"), Expected("2/3"), Expected("5/4")];
        Assert.Equal(Expected(expected), Evaluate(expression, compiled, values));
        var selector = expression with { ValueExpr = new BinaryExpr { Op = BinaryOp.Div, Left = new FieldAccessExpr { Target = "state", Path = ["item"] }, Right = Q("12") } };
        Assert.Equal(Expected(expected), Evaluate(selector, compiled, [new BigInteger(-6), new BigInteger(8), new BigInteger(15)]));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void WrongNumericTypesAndAritiesCannotAcquireAZeroValue(bool compiled)
    {
        foreach (var function in new[] { "abs", "min", "max", "floor", "ceil", "round" })
        {
            var value = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "false" };
            var expression = new CallExpr { Function = function, Args = function is "min" or "max" ? [value, Q("1")] : [value] };
            Assert.Equal("EVAL_TYPE_ERROR", Assert.Throws<ExpressionEvaluationException>(() => Evaluate(expression, compiled)).Code);
            Assert.Equal("EVAL_ARITY_ERROR", Assert.Throws<ExpressionEvaluationException>(() => Evaluate(expression with { Args = [] }, compiled)).Code);
        }
        var selector = new AggregateExpr { StateKey = "items", ItemName = "item", Func = AggregateFunc.Sum, ValueExpr = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "false" } };
        Assert.Equal("EVAL_TYPE_ERROR", Assert.Throws<ExpressionEvaluationException>(() => Evaluate(selector, compiled, [1])).Code);
    }
}
