using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Expressions;

public sealed class NumericInputTests
{
    private static FieldAccessExpr Field(params string[] path) => new() { Target = "state", Path = path };
    private static ExprValue Evaluate(Expression expression, Dict state, bool optimized)
    {
        if (!optimized) return ExactExpressionEvaluator.Evaluate(expression, new EvalContext { State = state });
        var layout = state.Keys.Select((key, i) => (key, i)).ToDictionary(p => p.key, p => p.i);
        int Slot(string key) { if (!layout.TryGetValue(key, out var i)) layout[key] = i = layout.Count; return i; }
        var program = new SamplingExpressions(Slot).Compile(expression);
        var frame = new SamplingFrame(layout, new SamplingCell[layout.Count], state); frame.Reset(default);
        return program(frame);
    }

    public static IEnumerable<object[]> Inputs()
    {
        // Independent, hand-specified fractions for the IEEE754 encodings of
        // 0.1 (binary64 and binary32), and the decimal encoding of 0.1.
        foreach (var optimized in new[] { false, true })
        {
            yield return [optimized, .1d, "3602879701896397", "36028797018963968"];
            yield return [optimized, .1f, "13421773", "134217728"];
            yield return [optimized, .1m, "1", "10"];
            yield return [optimized, -1.25m, "-5", "4"];
            yield return [optimized, decimal.MaxValue, "79228162514264337593543950335", "1"];
        }
    }
    [Theory]
    [MemberData(nameof(Inputs))]
    public void StateRecordsArraysAndSelectorsRetainSpecifiedFractions(bool optimized, object input, string numerator, string denominator)
    {
        var expected = ExprValue.Rational(BigInteger.Parse(numerator), BigInteger.Parse(denominator));
        var state = new Dict { ["value"] = input, ["record"] = new Dict { ["value"] = input }, ["items"] = new object[] { input } };
        Assert.Equal(expected, Evaluate(Field("value"), state, optimized));
        Assert.Equal(expected, Evaluate(Field("record", "value"), state, optimized));
        Assert.Equal(expected, Evaluate(Field("items", "0"), state, optimized));
        var filtered = Evaluate(new FilterExpr { StateKey = "items", ItemName = "item", Predicate = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" } }, state, optimized);
        Assert.Equal(expected, Assert.Single(filtered.ArrayValue!));
        Assert.Equal(expected, Evaluate(new AggregateExpr { StateKey = "items", Func = AggregateFunc.Sum }, state, optimized));
        Assert.Equal(expected, Evaluate(new MapExpr { StateKey = "items", ItemName = "item", Body = Field("item") }, state, optimized).ArrayValue![0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReflectedValuesAndSubnormalNumbersAreNeverTruncatedToZero(bool optimized)
    {
        var state = new Dict { ["small"] = 1e-9, ["smallest"] = double.Epsilon };
        Assert.Equal(1e-9, Evaluate(Field("small"), state, optimized).AsDouble());
        var smallest = Evaluate(Field("smallest"), state, optimized);
        Assert.Equal(BigInteger.One, smallest.NumberNumerator); Assert.Equal(BigInteger.One << 1074, smallest.NumberDenominator);
        Assert.Equal(double.Epsilon, smallest.AsDouble());
        Assert.Equal(1.5, ExprValue.Rational((BigInteger.One << 2048) * 3 + 1, BigInteger.One << 2049).AsDouble());
    }

    [Fact]
    public void ReflectedRootNumericPropertiesRetainTheirFiniteValue()
    {
        var context = new EvalContext { State = new { Small = 1e-9, Decimal = .1m } };
        Assert.Equal(1e-9, ExactExpressionEvaluator.Evaluate(Field("Small"), context).AsDouble());
        Assert.Equal(ExprValue.Rational(1, 10), ExactExpressionEvaluator.Evaluate(Field("Decimal"), context));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonfiniteInputsFailExplicitlyBeforeAcquiringAnObservation(bool optimized)
    {
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.Equal("EVAL_NONFINITE_NUMBER", Assert.Throws<ExpressionEvaluationException>(() => Evaluate(Field("value"), new Dict { ["value"] = value }, optimized)).Code);
    }

    private static GraphConfig Model(string signal = "1e-9", string payout = "1") => JsonSerializer.Deserialize<GraphConfig>("""
    {"schemaVersion":"1.0.0","name":"Independent decimal input oracle","initialState":{"signal":SIGNAL,"payout":PAYOUT,"record":{"values":[0.1,-2.5e-7]}},"nodes":[
      {"nodeType":"draw","id":"draw","drawWeights":[{"outcomeId":"fixed","value":0,"weight":1}],"outputs":{"out":{"name":"out","type":"Wins"}}},
      {"nodeType":"metricsSink","id":"sink","winCap":10,"winStateKey":"payout","inputs":{"in":{"name":"in","type":"Wins"}}}],
      "edges":[{"id":"e","sourceNodeId":"draw","sourcePort":"out","targetNodeId":"sink","targetPort":"in"}]}
    """.Replace("SIGNAL", signal).Replace("PAYOUT", payout), JsonOptions.Default)!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AuthoredJsonDecimalAndScientificStateHaveExactResidualsInSamplingAndEnumeration(bool optimized)
    {
        MeasurementDefinition[] plan = [new() { Id = "signal", Name = "Tiny signal", NodeId = "sink", Value = Field("signal"),
            Filter = new CompareExpr { Op = CompareOp.Gt, Left = Field("signal"), Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" } } },
            new() { Id = "residual", Name = "Exact input residual", NodeId = "sink", Value = new BinaryExpr { Op = BinaryOp.Sub, Left = Field("signal"), Right = new ConstantExpr { Kind = ConstantKind.Rational, Value = "1/1000000000" } }, Options = new() { Assertion = "zero" } }];
        var graph = new GraphCompiler(optimizeSampling: optimized).Compile(Model(), plan);
        Assert.True(graph.IsValid, string.Join(';', graph.Errors.Select(e => e.Message))); Assert.Equal(BigInteger.One, graph.WinScale);
        var sampled = SampledInterpreter.Evaluate(graph.Program!, new Dict(), new SampledConfig { Seed = 817, MaxSpins = 100, Measurements = plan });
        Assert.Equal(1, sampled.Stats.Mean); var signal = sampled.Measurements[0];
        Assert.Equal(100, signal.Count); Assert.Equal(0, signal.Errors); Assert.Equal(1e-9, signal.Min); Assert.Equal(1e-9, signal.Max); Assert.Equal(1e-9, signal.Mean);
        Assert.Equal("noObservedViolations", sampled.Measurements[1].Analysis!.Assertion!.Status);
        var exact = GraphMeasurementEnumeration.Evaluate(graph, plan, 1);
        // Enumeration's declared metric contract retains the production
        // collector's binary64 observations. Exact residuals are checked before
        // that reporting conversion; the numeric law has this independent
        // binary64 fraction, not the authored decimal fraction.
        Assert.Equal("4835703278458517/4835703278458516698824704", exact.Measurements[0].ConditionalMean);
        Assert.Equal("0/1", exact.Measurements[1].ConditionalMean);
        var first = SampledInterpreter.RunSingle(graph.Program!, new Dict(), 817);
        var record = (Dict)first.State["record"]!; var values = (object?[])record["values"]!;
        Assert.Equal(ExprValue.Rational(1, 10), values[0]); Assert.Equal(ExprValue.Rational(-1, 4000000), values[1]); values[0] = "corrupted";
        var next = SampledInterpreter.RunSingle(graph.Program!, new Dict(), 817);
        Assert.Equal(ExprValue.Rational(1, 10), ((object?[])((Dict)next.State["record"]!)["values"]!)[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeasurementPrecisionCannotSilentlyChangeTheDeclaredPayoutQuantum(bool optimized)
    {
        var graph = new GraphCompiler(optimizeSampling: optimized).Compile(Model(payout: "0.1")); Assert.True(graph.IsValid); Assert.Equal(BigInteger.One, graph.WinScale);
        Assert.Equal("EVAL_PAYOUT_PRECISION", Assert.Throws<ExpressionEvaluationException>(() => SampledInterpreter.RunSingle(graph.Program!, new Dict(), 817)).Code);
    }

    [Theory]
    [InlineData("1e4097")]
    [InlineData("1e-4097")]
    [InlineData("1e9999999999999")]
    public void NumericStateResourceBudgetsFailCompilationBeforeAJobCanStart(string literal)
    {
        foreach (var optimized in new[] { false, true })
        { var graph = new GraphCompiler(optimizeSampling: optimized).Compile(Model(literal)); Assert.False(graph.IsValid); Assert.Contains(graph.Errors, e => e.Code == "EVAL_NUMERIC_BUDGET"); }
    }
}
