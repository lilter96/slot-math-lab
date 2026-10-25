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
        if (conditional)
        {
            json = json.Replace("\"maxIterations\":3", "\"maxIterations\":3,\"stopConditionId\":\"stop\"");
        }

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
            CompileResult compiled = new GraphCompiler(optimizeSampling: native).Compile(Create(conditional));
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors.Select(e => e.Message)));
            foreach (var workers in new[] { 1, 3 })
            {
                SampledResult<Dict> result = SampledInterpreter.Evaluate(compiled.Program!, new Dict(),
                    new SampledConfig { MaxSpins = 65537, DegreeOfParallelism = workers });
                LoopTerminationSummary loop = Assert.Single(result.Execution!.LoopTerminations);
                var iterations = conditional ? 2 : 3;
                Assert.Equal(65537, loop.CompletedInvocations);
                Assert.Equal(65537L * iterations, loop.TotalIterations);
                Assert.Equal(iterations, loop.MinimumIterations);
                Assert.Equal(iterations, loop.MaximumIterations);
                Assert.Equal(conditional ? 65537 : 0, loop.ConditionCompletions);
                Assert.Equal(conditional ? 0 : 65537, loop.ModelLimitCompletions);
                Assert.True(result.Execution.LoopTerminationsComplete);
                results.Add(result);
            }
        }

        Assert.All(results,
            r => Assert.Equal(JsonSerializer.Serialize(results[0].Execution!.LoopTerminations),
                JsonSerializer.Serialize(r.Execution!.LoopTerminations)));
    }

    [Fact]
    public void UnobservedCountersPublishRawInt32AndIdenticalEvidencePastTheBoxCache()
    {
        GraphConfig graph = Create(false);
        graph = graph with
        {
            Expressions =
            [],
            Nodes = graph.Nodes.Select(node => node is LoopNode loop ? loop with { MaxIterations = 300 } : node)
                .ToArray()
        };
        CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(graph);
        Assert.True(reference.IsValid);
        (BigInteger Value, Dict State) expected = SampledInterpreter.RunSingle(reference.Program!, new Dict(), 42);
        Assert.Equal(300, Assert.IsType<int>(expected.State["__iter_feature__"]));
        foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
        {
            SamplingTier previous = SamplingOptions.Tier;
            try
            {
                SamplingOptions.Tier = tier;
                CompileResult compiled = new GraphCompiler().Compile(graph);
                Assert.True(compiled.IsValid);
                (BigInteger Value, Dict State) actual = SampledInterpreter.RunSingle(compiled.Program!, new Dict(), 42);
                Assert.Equal(expected.Value, actual.Value);
                Assert.Equal(StateHasher.CanonicalHash(expected.State), StateHasher.CanonicalHash(actual.State));
                Assert.Equal(300, Assert.IsType<int>(actual.State["__iter_feature__"]));
                SampledResult<Dict> sampled = SampledInterpreter.Evaluate(compiled.Program!, new Dict(),
                    new SampledConfig { MaxSpins = 7, DegreeOfParallelism = 2 });
                LoopTerminationSummary evidence = Assert.Single(sampled.Execution!.LoopTerminations);
                Assert.Equal(7, evidence.ModelLimitCompletions);
                Assert.Equal(2100, evidence.TotalIterations);
                Assert.Equal(300, evidence.MinimumIterations);
                Assert.Equal(300, evidence.MaximumIterations);
            }
            finally
            {
                SamplingOptions.Tier = previous;
            }
        }
    }

    [Fact]
    public void AuthoredCounterWritesRetainTheirRawIterationSemantics()
    {
        var graph = JsonSerializer.Deserialize<GraphConfig>("""
                                                            {"schemaVersion":"1.0.0","name":"Counter write oracle","initialState":{"turn":0},"nodes":[
                                                            {"nodeType":"loop","id":"feature","maxIterations":10,"stopConditionId":"done","outputs":{"body":{"name":"body","type":"State"},"exit":{"name":"exit","type":"Wins"}}},
                                                            {"nodeType":"modifyState","id":"write","outputKey":"__iter_feature__","expressionId":"eight","inputs":{"state":{"name":"state","type":"State"}},"outputs":{"state":{"name":"state","type":"State"}}},
                                                            {"nodeType":"modifyState","id":"finish","outputKey":"turn","expressionId":"next","inputs":{"state":{"name":"state","type":"State"}}},
                                                            {"nodeType":"metricsSink","id":"sink","winCap":10,"inputs":{"in":{"name":"in","type":"Wins"}}}],
                                                            "edges":[{"id":"body","sourceNodeId":"feature","sourcePort":"body","targetNodeId":"write","targetPort":"state"},
                                                            {"id":"write-finish","sourceNodeId":"write","sourcePort":"state","targetNodeId":"finish","targetPort":"state"},
                                                            {"id":"exit","sourceNodeId":"feature","sourcePort":"exit","targetNodeId":"sink","targetPort":"in"}],
                                                            "expressions":{"done":{"exprType":"compare","op":"Gte","left":{"exprType":"fieldAccess","target":"state","path":["turn"]},"right":{"exprType":"constant","kind":"Integer","value":"3"}},
                                                            "eight":{"exprType":"constant","kind":"Integer","value":"8"},"next":{"exprType":"binary","op":"Add","left":{"exprType":"fieldAccess","target":"state","path":["turn"]},"right":{"exprType":"constant","kind":"Integer","value":"1"}}}}
                                                            """, JsonOptions.Default)!;
        foreach (var optimized in new[] { false, true })
        {
            CompileResult compiled = new GraphCompiler(optimizeSampling: optimized).Compile(graph);
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors.Select(error => error.Message)));
            (BigInteger Value, Dict State) round = SampledInterpreter.RunSingle(compiled.Program!, new Dict(), 42);
            Assert.Equal(1, Assert.IsType<int>(round.State["__iter_feature__"]));
            Assert.Equal("condition", round.State["__exitReason_feature__"]);
        }
    }

    [Fact]
    public void LoopInAnInterruptedRoundNeverLeaksIntoSettledEvidence()
    {
        using var token = new CancellationTokenSource();
        var progress = new List<SampledProgress>();
        Slot<Dict, BigInteger> program =
            new LoopCompletionSlot<Dict, BigInteger>("loop", "counter", 3, Slot.Pure<Dict, BigInteger>(3))
                .SelectMany(_ => Slot.Modify<Dict>(_ =>
                {
                    token.Cancel();
                    throw new OperationCanceledException(token.Token);
                }).Select(_ => BigInteger.Zero));
        SampledResult<Dict> result = SampledInterpreter.Evaluate(program, new Dict { ["counter"] = 3 },
            new SampledConfig { MaxSpins = 2, CancellationToken = token.Token, ProgressCallback = progress.Add });
        Assert.Equal(0, result.SpinsCompleted);
        Assert.Empty(result.Execution!.LoopTerminations);
        Assert.Empty(Assert.Single(progress).Execution!.LoopTerminations);
    }

    [Fact]
    public void BoundedLoopStorageMarksMissingCoverageExplicitly()
    {
        var evidence = new LoopTerminationEvidence();
        evidence.Begin();
        for (var i = 0; i < 257; i++)
        {
            evidence.Observe($"loop-{i}", 1, 3);
        }

        evidence.Commit();
        Assert.False(evidence.Complete);
        Assert.Equal(256, evidence.Snapshot().Length);
    }

    [Fact]
    public void ReportedReasonsRemainIndependentFromLimitClassificationAcrossBoundKeys()
    {
        const string id = "outer/inner/repeated/loop";
        var worker = new LoopTerminationEvidence();
        var receiver = new LoopTerminationEvidence();
        worker.Begin();
        worker.Observe(new LoopTerminationEvidence.Key(id), 1, 5, "modelLimit");
        worker.Observe(new LoopTerminationEvidence.Key(new string(id.ToCharArray())), 5, 5, "condition");
        worker.Observe(id, 2, 5, "ready");
        worker.Commit();
        receiver.Merge(worker);
        LoopTerminationSummary summary = Assert.Single(receiver.Snapshot());
        Assert.Equal(id, summary.NodeId);
        Assert.Equal(3, summary.CompletedInvocations);
        Assert.Equal(1, summary.ModelLimitCompletions);
        Assert.Equal(2, summary.ConditionCompletions);
        Assert.Equal(8, summary.TotalIterations);
        Assert.Equal(new Dictionary<string, long> { ["condition"] = 1, ["modelLimit"] = 1, ["ready"] = 1 },
            summary.ExitReasons);
    }

    [Fact]
    public void ReusedBanksKeepSnapshotsAndMergedEvidenceIndependent()
    {
        var worker = new LoopTerminationEvidence();
        var receiver = new LoopTerminationEvidence();
        worker.Begin();
        worker.Observe("loop", 2, 5, "ready");
        worker.Observe("loop", 5, 5);
        worker.Commit();
        LoopTerminationSummary snapshot = Assert.Single(worker.Snapshot());
        receiver.MergeDelta(worker);
        worker.ClearDelta();
        worker.Begin();
        worker.Observe("different", 1, 5, "cancelled");
        // Discard this interrupted round when the next round begins.
        worker.Begin();
        worker.Observe("loop", 0, 5, "empty");
        worker.Commit();
        receiver.MergeDelta(worker);
        worker.ClearDelta();
        Assert.Equal(2, snapshot.CompletedInvocations);
        Assert.Equal(new Dictionary<string, long> { ["ready"] = 1, ["modelLimit"] = 1 }, snapshot.ExitReasons);
        LoopTerminationSummary merged = Assert.Single(receiver.Snapshot());
        Assert.Equal(3, merged.CompletedInvocations);
        Assert.Equal(7, merged.TotalIterations);
        Assert.Equal(0, merged.MinimumIterations);
        Assert.Equal(5, merged.MaximumIterations);
        Assert.Equal(new Dictionary<string, long> { ["ready"] = 1, ["modelLimit"] = 1, ["empty"] = 1 },
            merged.ExitReasons);
        worker.Begin();
        worker.Observe("new", 3, 4);
        worker.Commit();
        var fresh = new LoopTerminationEvidence();
        fresh.MergeDelta(worker);
        Assert.Equal("new", Assert.Single(fresh.Snapshot()).NodeId);
        Assert.Single(receiver.Snapshot());
    }
}
