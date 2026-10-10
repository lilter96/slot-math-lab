using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
namespace SlotMath.Core.Tests.Expressions;

public class StrictOperatorTests
{
    private static ConstantExpr N(int n) => new() { Kind = ConstantKind.Integer, Value = n.ToString() };
    private static ConstantExpr B(bool b) => new() { Kind = ConstantKind.Boolean, Value = b ? "true" : "false" };
    private static BinaryExpr WrongNumber => new() { Op = BinaryOp.Add, Left = B(true), Right = N(1) };
    public static IEnumerable<object[]> InvalidOperators()
    {
        Expression[] cases = [WrongNumber, new BinaryExpr { Op = BinaryOp.And, Left = N(0), Right = B(true) },
            new BinaryExpr { Op = BinaryOp.Or, Left = B(false), Right = N(0) }, new NotExpr { Expr = N(0) },
            new IfExpr { Condition = N(0), ThenExpr = N(1), ElseExpr = N(0) }];
        foreach (var native in new[] { false, true }) foreach (var expression in cases) yield return [native, expression];
    }
    private static ExprValue Evaluate(Expression expression, bool native)
    {
        if (!native) return ExactExpressionEvaluator.Evaluate(expression, EvalContext.Empty);
        var frame = new SamplingFrame(new Dictionary<string, int>(), [], new()); frame.Reset(CancellationToken.None);
        return new SamplingExpressions(_ => throw new InvalidOperationException("No field binding in this oracle.")).Compile(expression)(frame);
    }
    [Theory]
    [MemberData(nameof(InvalidOperators))]
    public void WrongOperandTypesAreErrorsRatherThanZeroOrFalse(bool native, Expression expression)
    {
        var error = Assert.Throws<ExpressionEvaluationException>(() => Evaluate(expression, native)); Assert.Equal("EVAL_TYPE_ERROR", error.Code);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownNumericOperatorCannotManufactureAZeroResult(bool native)
    {
        var expression = new BinaryExpr { Op = (BinaryOp)999, Left = N(1), Right = N(2) };
        Assert.Equal("EVAL_INVALID_OPERATOR", Assert.Throws<ExpressionEvaluationException>(() => Evaluate(expression, native)).Code);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShortCircuitDoesNotEvaluateAnUnneededInvalidOperandOrBranch(bool native)
    {
        Assert.False(Evaluate(new BinaryExpr { Op = BinaryOp.And, Left = B(false), Right = WrongNumber }, native).BoolValue);
        Assert.True(Evaluate(new BinaryExpr { Op = BinaryOp.Or, Left = B(true), Right = WrongNumber }, native).BoolValue);
        Assert.Equal(1, Evaluate(new IfExpr { Condition = B(true), ThenExpr = N(1), ElseExpr = WrongNumber }, native).AsInteger());
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnInvalidNestedOperatorCannotPassAnExactZeroAssertion(bool native)
    {
        var collector = new MeasurementCollector([new() { Id = "rule", Name = "Typed rule", Options = new() { Assertion = "zero" } }]);
        collector.Begin(); collector.Observe(0, 0, _ => Evaluate(WrongNumber, native), null); collector.Commit();
        var result = collector.Total[0].Snapshot("rule"); Assert.Equal(0, result.Count); Assert.Equal(1, result.Errors);
        Assert.Equal("invalid", result.Analysis!.Assertion!.Status); Assert.Contains(result.Witnesses, w => w.Kind == "invalid");
    }
}
