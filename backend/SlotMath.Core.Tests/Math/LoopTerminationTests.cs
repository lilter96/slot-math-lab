using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using Dict = System.Collections.Generic.Dictionary<string, object?>;
namespace SlotMath.Core.Tests.Math;

public class LoopTerminationTests
{
    private static GraphConfig Create(bool conditional)
    {
        // One unit award per body invocation: capped model pays 3, condition pays 2.
        var json = """
        {"schemaVersion":"1.0.0","name":"Independent loop stop oracle","nodes":[
          {"nodeType":"loop","id":"feature","maxIterations":3,"outputs":{"body":{"name":"body","type":"State"},"exit":{"name":"exit","type":"Wins"}}},
          {"nodeType":"draw","id":"award","drawWeights":[{"outcomeId":"one","weight":1,"value":1}],"inputs":{"in":{"name":"in","type":"State"}}},
          {"nodeType":"metricsSink","id":"sink","winCap":10,"inputs":{"in":{"name":"in","type":"Wins"}}}],
          "edges":[{"id":"body","sourceNodeId":"feature","sourcePort":"body","targetNodeId":"award","targetPort":"in"},{"id":"exit","sourceNodeId":"feature","sourcePort":"exit","targetNodeId":"sink","targetPort":"in"}],
          "expressions":{"stop":{"exprType":"compare","op":"Gte","left":{"exprType":"fieldAccess","target":"state","path":["__iter_feature__"]},"right":{"exprType":"constant","kind":"Integer","value":"2"}}}}
        """;
        if (conditional) json = json.Replace("\"maxIterations\":3", "\"maxIterations\":3,\"stopConditionId\":\"stop\"");
        return JsonSerializer.Deserialize<GraphConfig>(json, JsonOptions.Default)!;
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompletionReasonAndIterationExposureAgreeAcrossEnginesAndWorkers(bool conditional)
    {
        var results = new List<SampledResult<Dict>>();
        foreach (var native in new[] { true, false })
        {
            var compiled = new GraphCompiler(optimizeSampling: native).Compile(Create(conditional));
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors.Select(e => e.Message)));
            foreach (var workers in new[] { 1, 3 })
            {
                var result = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new() { MaxSpins = 65537, DegreeOfParallelism = workers });
                var loop = Assert.Single(result.Execution!.LoopTerminations); var iterations = conditional ? 2 : 3;
                Assert.Equal(65537, loop.CompletedInvocations); Assert.Equal(65537L * iterations, loop.TotalIterations);
                Assert.Equal(iterations, loop.MinimumIterations); Assert.Equal(iterations, loop.MaximumIterations);
                Assert.Equal(conditional ? 65537 : 0, loop.ConditionCompletions); Assert.Equal(conditional ? 0 : 65537, loop.ModelLimitCompletions);
                Assert.True(result.Execution.LoopTerminationsComplete); results.Add(result);
            }
        }
        Assert.All(results, r => Assert.Equal(JsonSerializer.Serialize(results[0].Execution!.LoopTerminations), JsonSerializer.Serialize(r.Execution!.LoopTerminations)));
    }
    [Fact]
    public void LoopInAnInterruptedRoundNeverLeaksIntoSettledEvidence()
    {
        using var token = new CancellationTokenSource(); var progress = new List<SampledProgress>();
        var program = new LoopCompletionSlot<Dict, BigInteger>("loop", "counter", 3, Slot.Pure<Dict, BigInteger>(3))
            .SelectMany(_ => Slot.Modify<Dict>(_ => { token.Cancel(); throw new OperationCanceledException(token.Token); }).Select(_ => BigInteger.Zero));
        var result = SampledInterpreter.Evaluate(program, new Dict { ["counter"] = 3 }, new() { MaxSpins = 2, CancellationToken = token.Token, ProgressCallback = progress.Add });
        Assert.Equal(0, result.SpinsCompleted); Assert.Empty(result.Execution!.LoopTerminations); Assert.Empty(Assert.Single(progress).Execution!.LoopTerminations);
    }
    [Fact]
    public void BoundedLoopStorageMarksMissingCoverageExplicitly()
    {
        var evidence = new LoopTerminationEvidence(); evidence.Begin();
        for (var i = 0; i < 257; i++) evidence.Observe($"loop-{i}", 1, 3);
        evidence.Commit(); Assert.False(evidence.Complete); Assert.Equal(256, evidence.Snapshot().Length);
    }
}
