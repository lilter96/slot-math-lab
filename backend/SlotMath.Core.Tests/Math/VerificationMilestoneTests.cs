using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;
using Dict = System.Collections.Generic.Dictionary<string, object?>;
namespace SlotMath.Core.Tests.Math;

public class VerificationMilestoneTests
{
    [Theory]
    [InlineData("nearestEven", 5, 1000, 0, 1)]
    [InlineData("nearestEven", 15, 1000, 2, 100)]
    [InlineData("nearestAway", 5, 1000, 1, 100)]
    [InlineData("floor", 19, 1000, 1, 100)]
    [InlineData("ceiling", 11, 1000, 2, 100)]
    public void SettlementMatchesIntegerCentTieSpecification(string mode, int numerator, int denominator, int expectedN, int expectedD)
    {
        var value = new MonetarySettlement { Quantum = "0.01", Mode = mode }.Apply(ExprValue.Rational(numerator, denominator));
        Assert.Equal(new Rational(expectedN, expectedD), new(value.NumberNumerator, value.NumberDenominator));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompilerPreservesPreRoundingRationalAndCapsAfterRounding(bool optimized)
    {
        var graph = JsonSerializer.Deserialize<GraphConfig>("""
        {"schemaVersion":"1.0.0","nodes":[
          {"nodeType":"modifyState","id":"award","outputKey":"payout","expressionId":"fraction","outputs":{"out":{"name":"out","type":"State"}}},
          {"nodeType":"metricsSink","id":"sink","winCap":1,"winStateKey":"payout","settlement":{"quantum":"0.01","mode":"nearestEven"},"inputs":{"in":{"name":"in","type":"State"}}}],
          "edges":[{"id":"e","sourceNodeId":"award","sourcePort":"out","targetNodeId":"sink","targetPort":"in"}],
          "expressions":{"fraction":{"exprType":"constant","kind":"Rational","value":"201/200"}}}
        """, JsonOptions.Default)!;
        var metric = new MeasurementDefinition { Id = "before", Name = "Exact before", Value = new FieldAccessExpr { Target = "state", Path = ["__settlementBefore"] } };
        var compiled = new GraphCompiler(optimizeSampling: optimized).Compile(graph, [metric]);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors.Select(e => e.Message)));
        var result = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new() { Measurements = [metric], MaxSpins = 5, WinScale = (double)compiled.WinScale });
        Assert.Equal(1, result.Stats.Mean); Assert.Equal(1.005, Assert.Single(result.Measurements).Mean);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void RuinPolicyStopsWithoutZeroFillingUnplayedSlots(int workers)
    {
        var result = SampledInterpreter.Evaluate(Slot.Pure<Dict, BigInteger>(0), new Dict(), new()
        {
            MaxSpins = 20,
            DegreeOfParallelism = workers,
            Execution = new() { Regime = "sessions", SessionLength = 10, SessionStop = "ruin", InitialBankroll = 2, Wager = 1 }
        });
        Assert.Equal(4, result.SpinsCompleted); Assert.False(result.WasCancelled); Assert.Equal(2, result.Execution!.CompletedSessions);
        var metrics = result.Execution.SessionMetrics.ToDictionary(m => m.Id);
        Assert.Equal(2, metrics["session.duration"].Mean); Assert.Equal(2, metrics["session.turnover"].Mean);
        Assert.Equal(-2, metrics["session.profit"].Mean); Assert.Equal(1, metrics["session.stop.ruin"].Mean);
    }
    [Fact]
    public void StratifiedDesignAllowsAnExplicitUnreachableZeroMassAtom()
    {
        var report = FiniteModelAnalysis.SamplingDesign(new([
            new("dead", "100", "0", "0", "all"), new("paid", "2", "1", "1", "all")],
            Mode: "stratified", Samples: 10, Allocation: [new("all", 10)]));
        Assert.Equal(2, report.Reward.Mean); Assert.Equal("0/1", report.ExactEstimatorVariance);
        Assert.Equal(0, report.Likelihoods.Single(a => a.Id == "dead").Observed);
    }
    [Fact]
    public void StoppedSessionsRemainDeterministicAcrossRealParallelChunkBoundaries()
    {
        SampledResult<Dict> Run(int workers) => SampledInterpreter.Evaluate(Slot.Pure<Dict, BigInteger>(0), new Dict(), new()
        {
            MaxSpins = 131100,
            DegreeOfParallelism = workers,
            Execution = new() { Regime = "sessions", SessionLength = 10, SessionStop = "ruin", InitialBankroll = 2, Wager = 1, AuditRandomStreams = true }
        });
        var serial = Run(1); var parallel = Run(4);
        Assert.Equal(26220, serial.SpinsCompleted); Assert.Equal(13110, parallel.Execution!.CompletedSessions);
        Assert.Equal(JsonSerializer.Serialize(serial.Execution.SessionMetrics), JsonSerializer.Serialize(parallel.Execution.SessionMetrics));
        Assert.True(parallel.Execution.RandomStreams!.Complete); Assert.Equal(13110, parallel.Execution.RandomStreams.Streams);
    }
    [Fact]
    public void InitiallyUnfundedStoppedSessionCompletesAtZeroWithoutConsumingPaidRounds()
    {
        var result = SampledInterpreter.Evaluate(Slot.Pure<Dict, BigInteger>(7), new Dict(), new()
        {
            MaxSpins = 20,
            Execution = new() { Regime = "sessions", SessionLength = 10, SessionStop = "ruin", InitialBankroll = .5, Wager = 1 }
        });
        Assert.Equal(0, result.SpinsCompleted); Assert.Equal(2, result.Execution!.CompletedSessions); Assert.Equal(0, result.Execution.AttemptedRounds);
        Assert.Equal(0, result.Execution.SessionMetrics.Single(m => m.Id == "session.duration").Mean);
    }
    [Fact]
    public void GeometricResourceStopMatchesHandSummedProbabilityAndOmittedReward()
    {
        // P(survive 3)=1/8; expected retained visits=1+1/2+1/4=7/4.
        var report = FiniteModelAnalysis.ResourceImpact(new(new([["1/2"]], ["2"]), 3));
        Assert.Equal("1/8", report.StopProbability); Assert.Equal("7/2", report.RetainedReward);
        Assert.Equal("4/1", report.ReferenceReward); Assert.Equal("1/2", report.OmittedReward); Assert.Equal("7/4", report.RetainedDuration);
        var divergent = FiniteModelAnalysis.ResourceImpact(new(new([["1"]], ["2"]), 3));
        Assert.Equal("1/1", divergent.StopProbability); Assert.Null(divergent.OmittedReward); Assert.Equal("6/1", divergent.RetainedReward);
    }
    private static readonly SamplingAtom[] Law = [new("loss", "0", "3/4", "1/2", "loss"), new("win", "4", "1/4", "1/2", "win")];
    [Fact]
    public void ImportanceDesignMatchesIndependentExactVarianceAndExecutedWeightedCounts()
    {
        var report = FiniteModelAnalysis.SamplingDesign(new(Law, Samples: 1000));
        Assert.Equal("1/1000", report.ExactEstimatorVariance); Assert.Equal("5/4", report.ExactWeightSecondMoment);
        Assert.Equal("1/1", report.Reward.ExactReference); Assert.Equal("1/4", report.TailProbability.ExactReference);
        var wins = report.Likelihoods.Single(x => x.Id == "win").Observed;
        Assert.Equal(wins * 2d / 1000, report.Reward.Mean, 12); Assert.Equal(wins * .5 / 1000, report.TailProbability.Mean, 12);
        Assert.Equal(1000, report.Likelihoods.Sum(x => x.Observed));
        Assert.Throws<ArgumentException>(() => FiniteModelAnalysis.SamplingDesign(new([Law[0] with { ProposalProbability = "1" }, Law[1] with { ProposalProbability = "0" }])));
    }
    [Fact]
    public void FixedStrataUseTargetMassInsteadOfPooledCounts()
    {
        var report = FiniteModelAnalysis.SamplingDesign(new(Law, Mode: "stratified", Samples: 1000, Allocation: [new("loss", 100), new("win", 900)]));
        Assert.Equal(1, report.Reward.Mean); Assert.Equal(.25, report.TailProbability.Mean); Assert.Equal("0/1", report.ExactEstimatorVariance);
        Assert.Null(report.Reward.Interval); Assert.Null(report.EffectiveSampleSize);
    }
    [Fact]
    public void OrdinalProfilesAndNestedWitnessesUseOwningEpisodeAndConditionalPairs()
    {
        var options = new MeasurementOptions
        {
            Subject = "episode",
            EntryNodeId = "enter",
            ExitNodeId = "exit",
            OrdinalLimit = 2,
            ExitReason = new ConstantExpr { Kind = ConstantKind.String, Value = "authoredStop" }
        };
        var metric = new MeasurementDefinition { Id = "fs", Name = "FS", NodeId = "reveal", Options = options };
        var collector = new MeasurementCollector([metric]);
        var binding = new MeasurementBinding<int>(x => ExprValue.Number(x), null, ExitReason: _ => ExprValue.String("authoredStop"));
        collector.Begin(7); collector.Point(0, "enter", 0, binding); collector.Point(0, "reveal", 1, binding);
        collector.Point(0, "enter", 0, binding); collector.Point(0, "reveal", 10, binding); collector.Point(0, "exit", 0, binding);
        collector.Point(0, "reveal", 3, binding); collector.Point(0, "exit", 0, binding); collector.Prepare(); collector.Commit();
        var snapshot = Assert.Single(MeasurementCollector.Snapshot([metric], collector.Total)); var profile = snapshot.Analysis!.EpisodeProfile!;
        Assert.Equal(2, profile.IncludedEpisodes); Assert.Equal(2, profile.ExitReasons["authoredStop"]);
        Assert.Equal(1, profile.Ordinals.Single(x => x.Depth == 1 && x.Ordinal == 1).Mean);
        Assert.Equal(3, profile.Ordinals.Single(x => x.Depth == 1 && x.Ordinal == 2).Mean);
        Assert.Equal(10, profile.Ordinals.Single(x => x.Depth == 2).Mean); Assert.Single(profile.CrossOrdinals);
        var witness = snapshot.Witnesses.Single(w => w.Kind == "maximum"); Assert.Equal("7:2", witness.EpisodeId); Assert.Equal("7:1", witness.ParentEpisodeId); Assert.Equal(2, witness.EpisodeDepth);
    }
    [Theory]
    [InlineData(true, "payoutCap")]
    [InlineData(false, "payoutCap")]
    [InlineData(true, "authoredStop")]
    [InlineData(false, "resourceExpiry")]
    public void ConditionalLoopExitsPublishAuthoredClassificationInBothEngines(bool optimized, string reason)
    {
        var graph = JsonSerializer.Deserialize<GraphConfig>("""
        {"schemaVersion":"1.0.0","nodes":[
          {"nodeType":"loop","id":"loop","maxIterations":3,"stopConditionId":"stop","outputs":{"body":{"name":"body","type":"Wins"},"exit":{"name":"exit","type":"Wins"}}},
          {"nodeType":"draw","id":"body","inputs":{"in":{"name":"in","type":"Wins"}},"drawWeights":[{"outcomeId":"zero","weight":1,"value":0}]},
          {"nodeType":"metricsSink","id":"sink","winCap":10,"inputs":{"in":{"name":"in","type":"Wins"}}}],
          "edges":[{"id":"body","sourceNodeId":"loop","sourcePort":"body","targetNodeId":"body","targetPort":"in"},{"id":"exit","sourceNodeId":"loop","sourcePort":"exit","targetNodeId":"sink","targetPort":"in"}],
          "expressions":{"stop":{"exprType":"constant","kind":"Boolean","value":"true"}}}
        """, JsonOptions.Default)!;
        graph = graph with { Nodes = graph.Nodes.Select(n => n is LoopNode l ? l with { ExitReason = new ConstantExpr { Kind = ConstantKind.String, Value = reason } } : n).ToArray() };
        var compiled = new GraphCompiler(optimizeSampling: optimized).Compile(graph); Assert.True(compiled.IsValid, string.Join(';', compiled.Errors.Select(e => e.Message)));
        var result = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new() { MaxSpins = 3 });
        var loop = Assert.Single(result.Execution!.LoopTerminations); Assert.Equal(3, loop.ExitReasons![reason]); Assert.Equal(0, loop.TotalIterations);
    }
    [Fact]
    public void OrdinalCovarianceMergesByEntryCohortWithoutAliasingResetBuffers()
    {
        var options = new MeasurementOptions
        {
            Subject = "episode",
            EntryNodeId = "enter",
            ExitNodeId = "exit",
            OrdinalLimit = 2,
            GroupLimit = 1,
            Group = new ConstantExpr { Kind = ConstantKind.String, Value = "A" },
            ExitReason = new ConstantExpr { Kind = ConstantKind.String, Value = "condition" }
        };
        var metric = new MeasurementDefinition { Id = "fs", Name = "FS", NodeId = "reveal", Options = options };
        var collector = new MeasurementCollector([metric]);
        var binding = new MeasurementBinding<int>(x => ExprValue.Number(x), null, Group: _ => ExprValue.String("A"), ExitReason: _ => ExprValue.String("condition"));
        for (var i = 1; i <= 2; i++) { collector.Begin(i); collector.Point(0, "enter", 0, binding); collector.Point(0, "reveal", i, binding); collector.Point(0, "reveal", 3 * i, binding); collector.Point(0, "exit", 0, binding); collector.Prepare(); collector.Commit(); }
        var profile = Assert.Single(MeasurementCollector.Snapshot([metric], collector.Total)).Analysis!.Groups["A"].EpisodeProfile!;
        Assert.Equal(2, profile.IncludedEpisodes); var pair = Assert.Single(profile.CrossOrdinals); Assert.Equal(1.5, pair.Covariance); Assert.Equal(1, pair.Correlation);
        collector.Begin(3); Assert.Equal(2, profile.IncludedEpisodes); Assert.Equal(2, profile.Ordinals[0].Count);
    }
    [Fact]
    public void RawStreamAuditDoesNotChangeGeneratorAndDetectsDuplicateInitialState()
    {
        var audited = SeededRandom.ForStream(42, 0); var plain = SeededRandom.ForStream(42, 0); var other = SeededRandom.ForStream(42, 1);
        audited.EnableAudit(); other.EnableAudit();
        for (var i = 0; i < 100; i++) { Assert.Equal(plain.NextUInt64(), audited.NextUInt64()); other.NextUInt64(); }
        var first = audited.AuditSnapshot(0)!;
        var report = RandomStreamEvidence.Analyze([first, other.AuditSnapshot(1)!], 2, true);
        Assert.True(report.Complete); Assert.Empty(report.Duplicates); Assert.Equal(0, report.ScheduledInitialWordCollisionProbability);
        var repeated = RandomStreamEvidence.Analyze([first, first with { StreamIndex = 1 }], 2, true); Assert.Equal(1, repeated.ScheduledInitialWordCollisionProbability); Assert.Equal(3, repeated.CollisionCandidatePairs);
        Assert.False(RandomStreamEvidence.Analyze([first], 2, false).Complete);
        audited.EndAudit(); other.EndAudit();
    }
    [Fact]
    public void ResourceExpiryKeepsCensoredFeatureBoundariesAsACancellationSubset()
    {
        var metric = new MeasurementDefinition { Id = "feature", Name = "Feature", NodeId = "reveal", Options = new() { Subject = "episode", EntryNodeId = "enter", ExitNodeId = "exit" } };
        var collector = new MeasurementCollector([metric]); var binding = new MeasurementBinding<int>(_ => ExprValue.Number(100), null);
        collector.Begin(); collector.Point(0, "enter", 0, binding); collector.Point(0, "reveal", 0, binding); collector.Interrupt(PaidRoundInterruption.ResourceExpiry);
        var result = Assert.Single(MeasurementCollector.Snapshot([metric], collector.Total));
        Assert.Equal(0, result.Count); Assert.Null(result.Mean); Assert.Null(result.Analysis!.Normalization);
        Assert.Equal(new FeatureLifecycleCounts(1, 1, 0, 1, true), result.Analysis.InterruptedLifecycle!.ResourceExpiry);
        Assert.Equal(result.Analysis.InterruptedLifecycle.Cancelled, result.Analysis.InterruptedLifecycle.ResourceExpiry);
    }
    [Fact]
    public void EmbeddedExternalEvidenceMustMatchActualBytes()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("Rule v1: 3 scatters award 10 spins.");
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var input = new EvidenceInput("rule", "scatter", "1", digest, ContentBase64: Convert.ToBase64String(bytes));
        EvidenceInput.Validate([input]); Assert.Throws<ArgumentException>(() => EvidenceInput.Validate([input with { Sha256 = new string('0', 64) }]));
    }
}
