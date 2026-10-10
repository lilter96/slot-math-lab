using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Expressions;

public sealed class StrictCallTests
{
    private static ConstantExpr Text(string value) => new() { Kind = ConstantKind.String, Value = value };
    private static ConstantExpr Number(string value) => new() { Kind = ConstantKind.Rational, Value = value };
    private static FieldAccessExpr Array => new() { Target = "state", Path = ["values"] };
    private static CallExpr Call(string function, params Expression[] args) => new() { Function = function, Args = args };
    private static ExprValue Evaluate(Expression expression, bool compiled)
    {
        var state = new Dictionary<string, object?> { ["values"] = new object[] { ExprValue.Rational(1, 2), ExprValue.Rational(1, 4) } };
        if (!compiled) return ExactExpressionEvaluator.Evaluate(expression, new() { State = state });
        var frame = new SamplingFrame(new Dictionary<string, int> { ["values"] = 0 }, new SamplingCell[1], state);
        frame.Reset(CancellationToken.None);
        return new SamplingExpressions(_ => 0).Compile(expression)(frame);
    }

    [Theory]
    [InlineData(false, "0.1", "1", "10")][InlineData(true, "0.1", "1", "10")]
    [InlineData(false, "-2.5e-7", "-1", "4000000")][InlineData(true, "-2.5e-7", "-1", "4000000")]
    [InlineData(false, " 6/-8 ", "-3", "4")][InlineData(true, " 6/-8 ", "-3", "4")]
    [InlineData(false, "+0042", "42", "1")][InlineData(true, "+0042", "42", "1")]
    public void ExplicitTextConversionRetainsItsExactValue(bool compiled, string text, string numerator, string denominator)
    {
        var expected = ExprValue.Rational(BigInteger.Parse(numerator), BigInteger.Parse(denominator));
        Assert.Equal(expected, Evaluate(Call("tonumber", Text(text)), compiled));
        Assert.Equal(expected, Evaluate(Call("tonumber", Call("tostring", Call("tonumber", Text(text)))), compiled));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void InvalidConversionsDoNotManufactureAZeroObservation(bool compiled)
    {
        foreach (var value in new[] { "H", "", "NaN", "Infinity", "1/0", "1/2/3", "1,25", "1.2.3", "0x10", "1e", "1/" })
            Assert.Equal("EVAL_INVALID_NUMBER", Assert.Throws<ExpressionEvaluationException>(() => Evaluate(Call("tonumber", Text(value)), compiled)).Code);
        Assert.Equal("EVAL_NUMERIC_BUDGET", Assert.Throws<ExpressionEvaluationException>(() => Evaluate(Call("tonumber", Text("1e4097")), compiled)).Code);
        Assert.Equal("EVAL_NUMERIC_BUDGET", Assert.Throws<ExpressionEvaluationException>(() => Evaluate(Call("tonumber", Text(new string('1', 4097))), compiled)).Code);
        // An invalid conversion in an unevaluated branch remains unevaluated.
        Assert.Equal(ExprValue.Number(7), Evaluate(new IfExpr { Condition = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "false" },
            ThenExpr = Call("tonumber", Text("H")), ElseExpr = Number("7") }, compiled));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void BuiltinsEnforceArityAndRuntimeTypesOnBothEngines(bool compiled)
    {
        foreach (var name in new[] { "length", "contains", "append", "index", "tonumber", "tostring" })
            Assert.Equal("EVAL_ARITY_ERROR", Assert.Throws<ExpressionEvaluationException>(() => Evaluate(Call(name), compiled)).Code);
        Expression[] invalid = [Call("length", Number("0")), Call("contains", Number("0"), Text("")),
            Call("contains", Text("AB"), Number("0")), Call("append", Number("0"), Number("1")),
            Call("index", Text("AB"), Number("0")), Call("index", Array, Text("0")),
            Call("tonumber", Number("1")), Call("tostring", Text("42"))];
        foreach (var expression in invalid) Assert.Equal("EVAL_TYPE_ERROR", Assert.Throws<ExpressionEvaluationException>(() => Evaluate(expression, compiled)).Code);
        Assert.Equal("EVAL_UNKNOWN_FUNCTION", Assert.Throws<ExpressionEvaluationException>(() => Evaluate(Call("unknown", Number("1")), compiled)).Code);
        Assert.Equal("EVAL_INVALID_INDEX", Assert.Throws<ExpressionEvaluationException>(() => Evaluate(Call("index", Array, Number("1/2")), compiled)).Code);
        foreach (var index in new[] { "-1", "2", "999999999999999999999999999999" })
            Assert.Equal(EvalErrorCodes.IndexOutOfRange, Assert.Throws<ExpressionEvaluationException>(() => Evaluate(Call("index", Array, Number(index)), compiled)).Code);
        Assert.Equal(ExprValue.Rational(1, 4), Evaluate(Call("index", Array, Number("2/2")), compiled));
    }

    [Fact]
    public void CollectionCallsRequireTheirActualShapesAndInferNumericIndexes()
    {
        var context = new TypeCheckContext { StateFields = [new() { Name = "values", Type = ExprType.Array, ArrayItemType = ExprType.Number }] };
        var numeric = new BinaryExpr { Op = BinaryOp.Add, Left = Call("index", Array, Number("0")), Right = Number("1/3") };
        Assert.Empty(ExpressionTypeChecker.Check(numeric, context, ExprType.Number));
        Assert.Empty(ExpressionTypeChecker.Check(Call("contains", Array, Number("1/2")), context, ExprType.Boolean));
        Expression[] invalid = [Call("length", Number("1")), Call("index", Array), Call("index", Array, Text("0")),
            Call("contains", Text("AB"), Number("1")), Call("append", Text("AB"), Number("1")), Call("tonumber", Call("index", Array, Number("0")))];
        foreach (var expression in invalid) Assert.NotEmpty(ExpressionTypeChecker.Check(expression, context));
        Assert.NotEmpty(ExpressionTypeChecker.Check(Call("index", Array, Number("0")), new() { StateFields = [new() { Name = "values", Type = ExprType.Array }] }));
    }

    [Fact]
    public void EmptyAccumulatorAndTypedAppendPropagateThroughConditionalFoldAndNumericWriter()
    {
        var accumulator = new FieldAccessExpr { Target = "state", Path = ["acc"] };
        var fold = new FoldExpr { StateKey = "input", ItemName = "item", ItemType = ExprType.Number, AccName = "acc",
            Init = new FieldAccessExpr { Target = "state", Path = ["empty"] }, Body = new IfExpr
            {
                Condition = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" },
                ThenExpr = Call("append", accumulator, new FieldAccessExpr { Target = "state", Path = ["item"] }), ElseExpr = accumulator,
            } };
        var graph = new GraphConfig { SchemaVersion = "1.0.0", InitialState = new()
            { ["empty"] = JsonSerializer.Deserialize<JsonElement>("[]"), ["input"] = JsonSerializer.Deserialize<JsonElement>("[1,2]") },
            Nodes = [new ModifyStateNode { Id = "fold", OutputKey = "values", ExpressionId = "fold" }, new ModifyStateNode { Id = "first", OutputKey = "first", ExpressionId = "first" }],
            Expressions = new() { ["fold"] = fold, ["first"] = Call("index", Array, Number("0")) } };
        var fields = StateSchemaDeriver.Derive(graph);
        Assert.Equal(ExprType.Number, Assert.Single(fields, field => field.Name == "values").ArrayItemType);
        Assert.Equal(ExprType.Number, Assert.Single(fields, field => field.Name == "first").Type);
        Assert.Empty(ExpressionTypeChecker.Check(Call("index", Array, Number("0")), new() { StateFields = fields }, ExprType.Number));
    }
}
