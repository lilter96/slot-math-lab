using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
using Dict = System.Collections.Generic.Dictionary<string, object?>;
namespace SlotMath.Core.Tests.Math;

public class DiagnosticEvidenceTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void SparseCohortClusterInferenceIncludesParentsBeforeAndAfterItsFirstObservation(bool split)
    {
        var options = new MeasurementOptions { IndependentParents = true, Group = new ConstantExpr { Kind = ConstantKind.String, Value = "A" } };
        var definition = new MeasurementDefinition { Id = "cohort", Name = "Sparse independent-parent oracle", NodeId = "reveal", Value = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" }, Options = options };
        var first = new MeasurementCollector([definition]); var second = new MeasurementCollector([definition]);
        // Six independent paid parents: (sum,count) = (0,0),(0,0),(10,1),(0,0),(0,0),(20,1).
        // Pooled mean = 15. Residual sum of squares = 50, variance = 10,
        // mean count = 1/3, cluster SE = sqrt(10/6)/(1/3) = sqrt(15).
        for (var round = 0; round < 6; round++)
        {
            var collector = split && round >= 3 ? second : first; collector.Begin(round);
            if (round is 2 or 5) collector.Observe(0, round, new MeasurementBinding<int>(r => ExprValue.Number(r == 2 ? 10 : 20), null, _ => ExprValue.String("A")));
            collector.Commit();
        }
        if (split) MeasurementCollector.Merge(first.Total, second.Total);
        var cohort = first.Total[0].Snapshot("cohort").Analysis!.Groups["A"];
        Assert.Equal(2, cohort.Count); Assert.Equal(15, cohort.Mean); Assert.Equal(2, cohort.DistinctParents);
        var width = StatisticalInference.NormalCritical(0.05) * System.Math.Sqrt(15);
        Assert.Equal(15 - width, cohort.ClusteredMeanInterval!.Lower, 11); Assert.Equal(15 + width, cohort.ClusteredMeanInterval.Upper, 11);
    }
    [Fact]
    public void JointCategoricalTestIncludesUnobservedCellsAndWithholdsUnjustifiedCalibration()
    {
        var independent = new PairDiagnostics(4); var dependent = new PairDiagnostics(4);
        for (var i = 0; i < 20; i++) foreach (var x in new[] { 0d, 1d }) foreach (var y in new[] { 0d, 1d }) independent.Add(x, y);
        for (var i = 0; i < 40; i++) { dependent.Add(0, 0); dependent.Add(1, 1); }
        Assert.Equal(0, independent.Snapshot(80, true).ChiSquare); Assert.Equal(1, independent.Snapshot(80, true).PValue);
        Assert.Equal(80, dependent.Snapshot(80, true).ChiSquare); Assert.True(dependent.Snapshot(80, true).PValue < 1e-15);
        Assert.Null(dependent.Snapshot(80, false).PValue);
        var sparse = new PairDiagnostics(4); sparse.Add(0, 0); sparse.Add(1, 1); Assert.Null(sparse.Snapshot(2, true).PValue);
        var overflow = new PairDiagnostics(1); overflow.Add(0, 0); overflow.Merge(dependent); Assert.False(overflow.Snapshot(81, true).Complete);
    }
    [Fact]
    public void GroupOverflowPreservesOverallExposureAndWithholdsIncompleteCohorts()
    {
        var definition = new MeasurementDefinition { Id = "group", Name = "Bounded groups", Options = new() { GroupLimit = 1, Group = new ConstantExpr { Kind = ConstantKind.String, Value = "unused" } } };
        var collector = new MeasurementCollector([definition]);
        for (var i = 0; i < 3; i++) { collector.Begin(i); collector.Observe(0, i, new MeasurementBinding<int>(_ => ExprValue.Number(2), null, n => ExprValue.String(n.ToString()))); collector.Commit(); }
        var result = collector.Total[0].Snapshot("group"); Assert.Equal(3, result.Count); Assert.Equal(0, result.Errors); Assert.Equal(2, result.Mean);
        Assert.False(result.Analysis!.GroupsComplete); Assert.Empty(result.Analysis.Groups);
    }
    [Fact]
    public void PairedVarianceIncludesCovarianceRatherThanSummingComponentVariances()
    {
        var accumulator = new MeasurementAnalysisAccumulator(new() { IndependentSubjects = true, Pair = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" } });
        foreach (var x in new[] { 0d, 2d, 0d, 2d }) accumulator.Add(x, x, null, null);
        var pair = accumulator.Snapshot(0, true).Pair!;
        Assert.Equal(16d / 3, pair.SampleVarianceSum!.Value, 12); Assert.Equal(0, pair.SampleVarianceDifference);
        Assert.Equal(1, pair.Correlation); Assert.Equal(0, pair.MeanDifference);
    }
    [Fact]
    public void EveryAcceptedLegacyLoopCapRetainsItsAuthoredValue()
    {
        var config = JsonSerializer.Deserialize<GraphConfig>("""
        {"schemaVersion":"1.0.0","name":"Legacy cap oracle","nodes":[
          {"nodeType":"draw","id":"draw","drawWeights":[{"outcomeId":"one","value":1,"weight":1}],"outputs":{"out":{"name":"out","type":"Wins"}}},
          {"nodeType":"loop","id":"repeat","maxIterations":501,"inputs":{"in":{"name":"in","type":"Wins"}},"outputs":{"out":{"name":"out","type":"Wins"}}},
          {"nodeType":"metricsSink","id":"sink","winCap":2000,"inputs":{"in":{"name":"in","type":"Wins"}}}],
          "edges":[{"id":"a","sourceNodeId":"draw","sourcePort":"out","targetNodeId":"repeat","targetPort":"in"},{"id":"b","sourceNodeId":"repeat","sourcePort":"out","targetNodeId":"sink","targetPort":"in"}]}
        """, JsonOptions.Default)!;
        var compiled = new GraphCompiler().Compile(config); Assert.True(compiled.IsValid, string.Join(";", compiled.Errors.Select(e => e.Message)));
        var result = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new() { MaxSpins = 1 }); Assert.Equal(501, result.Stats.Mean);
    }
    [Fact]
    public void OperatorSelectedReferenceEngineRetainsTheSameLogicalStreamsAndCompleteMeasurementEvidence()
    {
        MeasurementDefinition[] plan = [new() { Id = "payout", Name = "Payout law", Options = new() }]; var compiled = new GraphCompiler().Compile(MeasurementTests.Model, plan);
        var a = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new() { MaxSpins = 1000, Seed = 42, Measurements = plan });
        var b = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new() { MaxSpins = 1000, Seed = 42, Measurements = plan, Execution = new() { SamplingEngine = "reference" } });
        Assert.Equal("reference-interpreter", b.Execution!.SamplingEngine); Assert.Equal("compiled-sampling-plan", a.Execution!.SamplingEngine);
        Assert.Equal(a.Stats.Mean, b.Stats.Mean); Assert.Equal(JsonSerializer.Serialize(a.Measurements), JsonSerializer.Serialize(b.Measurements));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void BoundedWitnessesReplayTheSameRoundAcrossLogicalChunkBoundaries(bool native)
    {
        var plan = new MeasurementDefinition[] { new() { Id = "law", Name = "Coin law", Options = new() { ReferenceDistribution = [new(0, 1)] } } };
        var compiler = new GraphCompiler(optimizeSampling: native); var compiled = compiler.Compile(MeasurementTests.Model, plan); Assert.True(compiled.IsValid);
        var full = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new() { MaxSpins = 65540, Seed = 42, DegreeOfParallelism = 2, Measurements = plan });
        Assert.InRange(full.Measurements[0].Witnesses.Length, 1, 6);
        var witness = full.Measurements[0].Witnesses.Single(w => w.Kind == "maximum");
        var replay = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new() { MaxSpins = 65540, Seed = 42, ReplayRoundIndex = witness.RoundIndex, Measurements = plan });
        Assert.Equal(witness.Value, replay.ReplayedRound![0].Max); Assert.Equal(witness.RoundIndex, replay.ReplayedRound[0].Witnesses[0].RoundIndex);
        var boundary = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new() { MaxSpins = 65540, Seed = 42, ReplayRoundIndex = 65539, Measurements = plan });
        Assert.Equal(4, boundary.SpinsCompleted); Assert.All(boundary.ReplayedRound![0].Witnesses, w => Assert.Equal(65539, w.RoundIndex));
        var serial = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new() { MaxSpins = 65540, Seed = 42, Measurements = plan });
        Assert.Equal(JsonSerializer.Serialize(full.Measurements), JsonSerializer.Serialize(serial.Measurements));
    }
}
