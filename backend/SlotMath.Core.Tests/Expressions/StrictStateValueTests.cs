using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Expressions;

public sealed class StrictStateValueTests
{
    private static FieldAccessExpr Field(params string[] path) => new() { Target = "state", Path = path };
    private static ExprValue Evaluate(Expression expression, Dict state, bool compiled)
    {
        if (!compiled) return ExactExpressionEvaluator.Evaluate(expression, new() { State = state });
        var slots = state.Keys.Select((key, index) => (key, index)).ToDictionary(p => p.key, p => p.index);
        int Slot(string key) { if (!slots.TryGetValue(key, out var index)) slots[key] = index = slots.Count; return index; }
        var program = new SamplingExpressions(Slot).Compile(expression);
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], state); frame.Reset(default);
        return program(frame);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void EverySegmentMustResolveAndFailuresNeverProduceZero(bool compiled)
    {
        var state = new Dict { ["scalar"] = 9, ["record"] = new Dict { ["value"] = 3, ["0"] = 7, ["empty"] = null },
            ["items"] = new object?[] { new Dict { ["fraction"] = ExprValue.Rational(1, 4) }, null, new object() }, ["unsupported"] = new object() };
        (string[] Path, string Code)[] invalid = [([], "EVAL_INVALID_PATH"), (["scalar", "ignored"], "EVAL_TYPE_ERROR"),
            (["scalar", "0", "ignored"], "EVAL_TYPE_ERROR"), (["record", "missing"], "EVAL_MISSING_STATE"),
            (["record", "empty"], "EVAL_NULL_VALUE"), (["items", "1"], "EVAL_NULL_VALUE"),
            (["items", "2"], "EVAL_TYPE_ERROR"),
            (["items", "0", "missing"], "EVAL_MISSING_STATE"), (["items", "9"], EvalErrorCodes.IndexOutOfRange),
            (["items", "1.5"], "EVAL_INVALID_INDEX"), (["items", "99999999999999999999"], EvalErrorCodes.IndexOutOfRange),
            (["unsupported"], "EVAL_TYPE_ERROR"), (["Scalar"], "EVAL_MISSING_STATE"), (["record", ""], "EVAL_INVALID_PATH")];
        foreach (var (path, code) in invalid)
        {
            var error = Assert.Throws<ExpressionEvaluationException>(() => Evaluate(Field(path), state, compiled));
            Assert.Equal(code, error.Code); Assert.Equal(string.Join('.', path), error.Location);
        }
        Assert.Equal(ExprValue.Number(7), Evaluate(Field("record", "0"), state, compiled));
        Assert.Equal(ExprValue.Rational(1, 4), Evaluate(Field("items", "0", "fraction"), state, compiled));
        Assert.Equal("EVAL_INVALID_TARGET", Assert.Throws<ExpressionEvaluationException>(() =>
            Evaluate(new FieldAccessExpr { Target = "other", Path = ["scalar"] }, state, compiled)).Code);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void FilteringRetainsRecordsNestedCollectionsAndExplicitNulls(bool compiled)
    {
        var state = new Dict { ["items"] = new object?[] { new Dict { ["award"] = ExprValue.Rational(1, 4),
            ["nested"] = new object?[] { new object?[] { 3, null } } } } };
        var filtered = Evaluate(new FilterExpr { StateKey = "items", ItemName = "item", ItemType = ExprType.Record,
            Predicate = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" } }, state, compiled);
        var record = Assert.Single(filtered.ArrayValue!); Assert.Equal(ExprType.Record, record.Kind);
        Assert.Equal(ExprValue.Rational(1, 4), record.RecordValue!["award"]);
        Assert.Equal(ExprType.Null, record.RecordValue["nested"].ArrayValue![0].ArrayValue![1].Kind);
        state["filtered"] = filtered.ToStateObject();
        Assert.Equal(ExprValue.Rational(1, 4), Evaluate(Field("filtered", "0", "award"), state, compiled));
        Assert.Equal(ExprValue.Number(3), Evaluate(Field("filtered", "0", "nested", "0", "0"), state, compiled));
        Assert.Equal("EVAL_NULL_VALUE", Assert.Throws<ExpressionEvaluationException>(() => Evaluate(Field("filtered", "0", "nested", "0", "1"), state, compiled)).Code);
        ((Dict)((object?[])state["filtered"]!)[0]!)["award"] = 100;
        Assert.Equal(ExprValue.Rational(1, 4), record.RecordValue["award"]); // Immutable expression snapshot.
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void AnUnusedInvalidValueDoesNotPoisonALazyBranchOrARecordSibling(bool compiled)
    {
        var state = new Dict { ["bad"] = new object(), ["record"] = new Dict { ["bad"] = new object(), ["good"] = 7 } };
        Assert.Equal(ExprValue.Number(7), Evaluate(Field("record", "good"), state, compiled));
        Assert.Equal(ExprValue.Number(7), Evaluate(new IfExpr { Condition = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "false" },
            ThenExpr = Field("bad"), ElseExpr = Field("record", "good") }, state, compiled));
    }

    [Fact]
    public void ReflectedPathsTraversePropertiesInsteadOfConcatenatingOrIgnoringSegments()
    {
        var context = new EvalContext { State = new { Small = .25m, SmallAmount = 99, Child = new { Amount = .75m }, Missing = (object?)null } };
        Assert.Equal(ExprValue.Rational(3, 4), ExactExpressionEvaluator.Evaluate(Field("child", "amount"), context));
        foreach (var path in new[] { new[] { "small", "amount" }, new[] { "child", "noSuchField" }, new[] { "Missing" } })
            Assert.Throws<ExpressionEvaluationException>(() => ExactExpressionEvaluator.Evaluate(Field(path), context));
        Assert.Throws<ExpressionEvaluationException>(() => SampledExpressionEvaluator.EvaluateAsBool(new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" }, EvalContext.Empty));
        Assert.Throws<ExpressionEvaluationException>(() => ExactExpressionEvaluator.EvaluateAsInteger(new ConstantExpr { Kind = ConstantKind.String, Value = "0" }, EvalContext.Empty));
    }

    public static GraphConfig Model() => JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "record-model.json")), JsonOptions.Default)!;

    [Fact]
    public void DerivedShapesFollowSelectionAndRejectFalseScalarOrNullPaths()
    {
        var graph = Model(); var context = new TypeCheckContext { StateFields = StateSchemaDeriver.Derive(graph) };
        Assert.Equal(ExprType.Record, context.ResolvePath(["selected"], "state"));
        Assert.Equal(ExprType.Number, context.ResolvePath(["selected", "money", "award"], "state"));
        Assert.Equal(ExprType.Number, context.ResolvePath(["selected", "history", "0", "0"], "state"));
        foreach (var path in new[] { new[] { "payout", "suffix" }, new[] { "selected", "money", "award", "suffix" }, new[] { "Selected", "money", "award" }, new[] { "selected", "money", "unused" } })
            Assert.NotEmpty(ExpressionTypeChecker.Check(Field(path), context));
        var incorrect = graph with { Expressions = new(graph.Expressions!) { ["filter"] = ((FilterExpr)graph.Expressions!["filter"]) with { ItemType = ExprType.Number } } };
        Assert.False(new GraphCompiler().Compile(incorrect).IsValid);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void RecordSelectionHasTheIndependentlySpecifiedFullPayoutLaw(bool compiled)
    {
        var result = new GraphCompiler(optimizeSampling: compiled).Compile(Model());
        Assert.True(result.IsValid, string.Join(';', result.Errors.Select(e => e.Message)));
        Assert.Equal(new BigInteger(100), result.WinScale);
        var exact = ExactInterpreter.Evaluate(result.Program!, new Dict(), StateHasher.CanonicalHash).ValueDistribution();
        var law = exact.Entries.ToDictionary(e => e.Value, e => new Rational(e.Numerator, exact.Denominator));
        // One low outcome of four pays 1/4; the other three pay 7/4.
        Assert.Equal(2, law.Count); Assert.Equal(new Rational(1, 4), law[25]); Assert.Equal(new Rational(3, 4), law[175]);
        var expected = new Rational(11, 8);
        var (meanNumerator, meanDenominator) = exact.ExpectedBigIntegerValue();
        Assert.Equal(expected, new Rational(meanNumerator, meanDenominator * 100));
        for (var seed = 1; seed <= 16; seed++)
        {
            var round = SampledInterpreter.RunSingle(result.Program!, new Dict(), seed);
            Assert.True(round.Value == 25 || round.Value == 175);
            var selected = (Dict)round.State["selected"]!;
            Assert.Equal(round.Value == 25 ? "low" : "high", selected["tag"]);
        }
    }
}
