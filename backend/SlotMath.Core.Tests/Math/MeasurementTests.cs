using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using Dict = System.Collections.Generic.Dictionary<string, object?>;
namespace SlotMath.Core.Tests.Math;
public class MeasurementTests
{
    private static ConstantExpr N(int value) => new() { Kind = ConstantKind.Integer, Value = value.ToString() };
    private static FieldAccessExpr F(string field) => new() { Target = "state", Path = [field] };
    private static readonly GraphConfig Model = JsonSerializer.Deserialize<GraphConfig>("""
    {"schemaVersion":"1.0.0","name":"Independent three-spin feature","nodes":[
      {"nodeType":"loop","id":"fs","maxIterations":3,"outputs":{"body":{"name":"body","type":"State"},"exit":{"name":"exit","type":"Wins"}}},
      {"nodeType":"modifyState","id":"type","expressionId":"type","outputKey":"fsType","inputs":{"in":{"name":"in","type":"State"}},"outputs":{"out":{"name":"out","type":"State"}}},
      {"nodeType":"modifyState","id":"win","expressionId":"win","outputKey":"spinWin","inputs":{"in":{"name":"in","type":"State"}},"outputs":{"out":{"name":"out","type":"State"}}},
      {"nodeType":"modifyState","id":"end","expressionId":"zero","outputKey":"end","inputs":{"in":{"name":"in","type":"State"}}},
      {"nodeType":"metricsSink","id":"sink","winCap":10,"inputs":{"in":{"name":"in","type":"Wins"}}}],
      "edges":[{"id":"a","sourceNodeId":"fs","sourcePort":"body","targetNodeId":"type","targetPort":"in"},
        {"id":"b","sourceNodeId":"type","sourcePort":"out","targetNodeId":"win","targetPort":"in"},
        {"id":"c","sourceNodeId":"win","sourcePort":"out","targetNodeId":"end","targetPort":"in"},
        {"id":"d","sourceNodeId":"fs","sourcePort":"exit","targetNodeId":"sink","targetPort":"in"}],
      "expressions":{"zero":{"exprType":"constant","kind":"Integer","value":"0"},
        "type":{"exprType":"if","condition":{"exprType":"compare","op":"Eq","left":{"exprType":"fieldAccess","target":"state","path":["__iter_fs__"]},"right":{"exprType":"constant","kind":"Integer","value":"1"}},"thenExpr":{"exprType":"constant","kind":"String","value":"other"},"elseExpr":{"exprType":"constant","kind":"String","value":"sticky"}},
        "win":{"exprType":"binary","op":"Add","left":{"exprType":"fieldAccess","target":"state","path":["__iter_fs__"]},"right":{"exprType":"constant","kind":"Integer","value":"2"}}}}
    """, JsonOptions.Default)!;
    private static MeasurementDefinition Sticky => new() { Id = "sticky", Name = "Sticky FS", NodeId = "end", Value = F("spinWin"), Filter = new CompareExpr { Op = CompareOp.Eq, Left = F("fsType"), Right = new ConstantExpr { Kind = ConstantKind.String, Value = "sticky" } } };
    private static SampledResult<Dict> Run(MeasurementDefinition[] plan, long n = 5, int workers = 1, bool optimized = true, Action<SampledProgress>? progress = null)
    {
        var compiled = new GraphCompiler(optimizeSampling: optimized).Compile(Model, plan);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors.Select(e => e.Message)));
        return SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new SampledConfig { Seed = 42, MaxSpins = n, DegreeOfParallelism = workers, Measurements = plan, ProgressReportInterval = 16, ProgressCallback = progress });
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void OneFsType_IndependentOracleHasTwoValuesPerRound(bool optimized)
    {
        // Manual specification: iterations 0 and 2 are sticky, payouts i+2 => {2,4}.
        // Five paid rounds therefore yield ten matching values, mean 3, sample variance 10/9.
        var result = Run([Sticky], optimized: optimized); var metric = Assert.Single(result.Measurements);
        Assert.Equal(15, metric.Observations); Assert.Equal(10, metric.Count); Assert.Equal(5, metric.Excluded); Assert.Equal(0, metric.Errors);
        Assert.Equal(2, metric.Min); Assert.Equal(4, metric.Max); Assert.Equal(3, metric.Mean); Assert.Equal(30, metric.Sum);
        Assert.Equal(System.Math.Sqrt(10d / 9), metric.StdDev!.Value, 12); Assert.Equal(5, result.SpinsCompleted);
    }
    [Fact]
    public void UnmatchedFilter_ShortCircuitsMissingValue_AndMissingDataIsNotZero()
    {
        var missing = Sticky with { Id = "missing", Name = "Missing at point", NodeId = "type", Value = F("spinWin"), Filter = null };
        var empty = missing with { Id = "none", Filter = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "false" } };
        var result = Run([empty, missing], n: 1);
        Assert.Equal(0, result.Measurements[0].Count); Assert.Equal(3, result.Measurements[0].Excluded); Assert.Null(result.Measurements[0].Mean); Assert.Equal(0, result.Measurements[0].Errors);
        Assert.Equal(1, result.Measurements[1].Errors); Assert.Equal(2, result.Measurements[1].Count); Assert.Contains("absent", result.Measurements[1].FirstError);
    }
    [Fact]
    public void ChunkReduction_IsBitIdenticalAcrossWorkers_AndObservationsDoNotChangeGame()
    {
        var plan = new[] { Sticky, new MeasurementDefinition { Id = "round", Name = "Settled payout" } };
        var a = Run(plan, 131073, workers: 1); var b = Run(plan, 131073, workers: 4, progress: p => {
            Assert.Equal(p.SpinsCompleted * 2, p.Measurements[0].Count); Assert.Equal(p.SpinsCompleted, p.Measurements[1].Count);
        });
        Assert.Equal(a.Measurements, b.Measurements); Assert.Equal(a.Stats.Mean, b.Stats.Mean);
        var plain = Run([], 131073); Assert.Equal(plain.Stats.Mean, a.Stats.Mean); Assert.Equal(plain.Stats.Histogram.Select(b => (b.LowerBound, b.UpperBound, b.Count)), a.Stats.Histogram.Select(b => (b.LowerBound, b.UpperBound, b.Count)));
    }
    [Fact]
    public void InterruptedRound_DoesNotLeakAnyObservations()
    {
        using var cts = new CancellationTokenSource();
        var definition = new MeasurementDefinition { Id = "node", Name = "Node", NodeId = "point", Value = N(7) };
        var program = new ObservationSlot<Dict, BigInteger>("point", Slot.Modify<Dict>(state => {
            cts.Cancel(); throw new OperationCanceledException(cts.Token);
        }).Select(_ => BigInteger.Zero));
        var result = SampledInterpreter.Evaluate(program, new Dict(), new SampledConfig { MaxSpins = 1, Measurements = [definition], CancellationToken = cts.Token });
        Assert.True(result.WasCancelled); Assert.Equal(0, result.SpinsCompleted); Assert.Equal(0, Assert.Single(result.Measurements).Observations);
    }
    [Fact]
    public void GenericSelectorAndEmptyRun_KeepMeasurementContract()
    {
        var plan = new[] { new MeasurementDefinition { Id = "selected", Name = "Selected payout" } };
        var result = SampledInterpreter.Evaluate(Slot.Pure<Dict, string>("7"), new Dict(), value => BigInteger.Parse(value),
            new SampledConfig { MaxSpins = 3, Measurements = plan, MaxWinCap = 5 });
        Assert.Equal(5, result.Stats.Mean); Assert.Equal(5, Assert.Single(result.Measurements).Mean);
        var empty = SampledInterpreter.Evaluate(Slot.Pure<Dict, BigInteger>(BigInteger.One), new Dict(), new SampledConfig { MaxSpins = 0, Measurements = plan });
        Assert.Equal(0, Assert.Single(empty.Measurements).Count); Assert.Null(empty.Measurements[0].Mean);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void RuntimeDivisionError_IsCountedAndNeverTurnsIntoZero(bool optimized)
    {
        var metric = Sticky with { Filter = null, Value = new BinaryExpr { Op = BinaryOp.Div, Left = F("spinWin"), Right = N(0) } };
        var result = Run([metric], n: 2, optimized: optimized); var m = Assert.Single(result.Measurements);
        Assert.Equal(6, m.Errors); Assert.Equal(0, m.Count); Assert.Null(m.Mean); Assert.Contains("Division by zero", m.FirstError);
        Assert.Equal(2, result.SpinsCompleted);
    }

    [Fact]
    public void InvalidPlans_FailBeforeExecution()
    {
        foreach (var invalid in new[] { Sticky with { NodeId = "missing" }, Sticky with { Value = F("unknown") }, Sticky with { Filter = N(1) }, Sticky with { Value = new ConstantExpr { Kind = ConstantKind.String, Value = "wrong" } } })
            Assert.Contains(new GraphCompiler().Compile(Model, [invalid]).Errors, e => e.Code == "INVALID_MEASUREMENT");
        Assert.False(new GraphCompiler().Compile(Model, Enumerable.Repeat(Sticky, 33).ToArray()).IsValid);
        Assert.False(new GraphCompiler().Compile(Model, [Sticky, Sticky]).IsValid);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void RoundPayout_UsesActualWinScaleCapAndFilter(bool optimized)
    {
        var model = JsonSerializer.Deserialize<GraphConfig>("""
        {"schemaVersion":"1.0.0","name":"Capped","initialState":{"payout":20},"nodes":[{"nodeType":"modifyState","id":"a","outputKey":"payout","expressionId":"p","outputs":{"out":{"name":"out","type":"Wins"}}},{"nodeType":"metricsSink","id":"sink","winCap":10,"winStateKey":"payout","inputs":{"in":{"name":"in","type":"Wins"}}}],"edges":[{"id":"e","sourceNodeId":"a","sourcePort":"out","targetNodeId":"sink","targetPort":"in"}],"expressions":{"p":{"exprType":"constant","kind":"Integer","value":"20"}}}
        """, JsonOptions.Default)!;
        MeasurementDefinition[] plan = [new() { Id = "round", Name = "Capped", Filter = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" } }];
        var compiled = new GraphCompiler(optimizeSampling: optimized).Compile(model, plan); Assert.True(compiled.IsValid);
        var result = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new SampledConfig { MaxSpins = 3, Measurements = plan, WinScale = (double)compiled.WinScale });
        Assert.Equal(10, result.Stats.Mean); Assert.Equal(10, Assert.Single(result.Measurements).Mean);
    }
}
