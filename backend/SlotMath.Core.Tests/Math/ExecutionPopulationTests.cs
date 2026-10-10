using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using Dict = System.Collections.Generic.Dictionary<string, object?>;
namespace SlotMath.Core.Tests.Math;

public class ExecutionPopulationTests
{
    [Fact]
    public void DecimalBankrollFundsEveryEqualWagerBeforeRuinAndReportsExactLoss()
    {
        var result = SampledInterpreter.Evaluate(Slot.Pure<Dict, BigInteger>(0), new Dict(), new()
        {
            MaxSpins = 6,
            Execution = new() { Regime = "sessions", SessionLength = 3, InitialBankroll = .3, Wager = .1 }
        });
        var metrics = result.Execution!.SessionMetrics.ToDictionary(m => m.Id);
        Assert.Equal("decimal-roundtrip-v1", result.Execution.MonetaryAccounting);
        Assert.Equal(3, metrics["session.ruinTime"].Mean);
        Assert.Equal(0, metrics["session.endingBankroll"].Mean);
        Assert.Equal(-.3, metrics["session.profit"].Mean);
        Assert.Equal(.3, metrics["session.drawdown"].Mean);
    }
    [Fact]
    public void DecimalLedgerMatchesIndependentIntegerCentSpecificationAcrossPeakAndScaleChanges()
    {
        // Bank 30 cents, wager 10 cents; payouts 0, 25, 5, 0, 10 cents.
        // Balances 20, 35, 30, 20, 20; peak 35, drawdown 15, net -10, return 40/50.
        var money = new SessionMoney(.3, .1);
        foreach (var payout in new[] { 0, .25, .05, 0, .1 }) money.Add(payout);
        Assert.Equal(-.1, money.Profit); Assert.Equal(.2, money.EndingBankroll);
        Assert.Equal(.15, money.Drawdown); Assert.Equal(.8, money.Return);
        Assert.True(money.CanFundNextWager); Assert.False(money.Profitable);
        // A finer payout unit expands the existing ledger without discarding it.
        money.Add(.0001);
        Assert.Equal(-.1999, money.Profit); Assert.Equal(.1001, money.EndingBankroll);
        Assert.Equal(.2499, money.Drawdown);
    }
    [Fact]
    public void DecimalLedgerPreservesTinyLossesAndSubnormalInputsWithoutEpsilonRules()
    {
        var large = new SessionMoney(1e15, .1); large.Add(0); large.Add(0);
        Assert.Equal(-.2, large.Profit); Assert.Equal(.2, large.Drawdown);
        var tiny = new SessionMoney(1e-300, 5e-301); tiny.Add(0);
        Assert.True(tiny.CanFundNextWager); tiny.Add(0); Assert.False(tiny.CanFundNextWager);
        Assert.Equal(-1e-300, tiny.Profit); Assert.Equal(0, tiny.EndingBankroll);
        Assert.Throws<ArithmeticException>(() => large.Add(double.NaN));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SessionFeatureWaitingUsesPinnedRoundActivationAndCensorsAbsentFeatures(bool native)
    {
        var activation = new SlotMath.Core.Measurements.MeasurementDefinition
        {
            Id = "feature",
            Name = "Counter feature",
            Value = new CompareExpr
            {
                Op = CompareOp.Gte,
                Left = new FieldAccessExpr { Target = "measurement", Path = ["payout"] },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" }
            },
            Options = new() { Source = "event" }
        };
        var compiled = new GraphCompiler(optimizeSampling: native).Compile(Counter, [activation]); Assert.True(compiled.IsValid);
        var result = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new() { MaxSpins = 6, Measurements = [activation], Execution = new() { Regime = "sessions", SessionLength = 2, PersistentKeys = ["counter"], FeatureMetricId = "feature" } });
        Assert.Equal(2, result.Execution!.SessionMetrics.Single(m => m.Id == "session.featureWait").Mean);
        Assert.Equal(1, result.Execution.SessionMetrics.Single(m => m.Id == "session.featureSeen").Mean);
        var absent = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new() { MaxSpins = 6, Measurements = [activation], Execution = new() { Regime = "sessions", SessionLength = 2, FeatureMetricId = "feature" } });
        var censored = absent.Execution!.SessionMetrics.Single(m => m.Id == "session.featureWait"); Assert.Equal(3, censored.Excluded); Assert.Equal(0, censored.Count); Assert.Null(censored.Mean);
        Assert.Equal(0, absent.Execution.SessionMetrics.Single(m => m.Id == "session.featureSeen").Mean);
    }
    private static readonly GraphConfig Counter = JsonSerializer.Deserialize<GraphConfig>("""
    {"schemaVersion":"1.0.0","name":"Retained counter oracle","initialState":{"counter":0},"nodes":[
      {"nodeType":"modifyState","id":"advance","expressionId":"next","outputKey":"counter","outputs":{"out":{"name":"out","type":"State"}}},
      {"nodeType":"metricsSink","id":"sink","winCap":1000000,"winStateKey":"counter","inputs":{"in":{"name":"in","type":"State"}}}],
      "edges":[{"id":"e","sourceNodeId":"advance","sourcePort":"out","targetNodeId":"sink","targetPort":"in"}],
      "expressions":{"next":{"exprType":"binary","op":"Add","left":{"exprType":"fieldAccess","target":"state","path":["counter"]},"right":{"exprType":"constant","kind":"Integer","value":"1"}}}}
    """, JsonOptions.Default)!;
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PersistentTrajectoryRetainsOnlyDeclaredState_AndSessionsResetAtTheirBoundary(bool native)
    {
        var compiled = new GraphCompiler(optimizeSampling: native).Compile(Counter); Assert.True(compiled.IsValid, string.Join(';', compiled.Errors.Select(e => e.Message)));
        SampledResult<Dict> Run(ExecutionOptions? options, int n) => SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new() { MaxSpins = n, Execution = options });
        var reset = Run(null, 5); Assert.Equal(1, reset.Stats.Mean);
        var persistent = Run(new() { Regime = "persistent", PersistentKeys = ["counter"] }, 5); Assert.Equal(3, persistent.Stats.Mean); Assert.Equal(5, persistent.Stats.MaxObserved);
        var sessions = Run(new() { Regime = "sessions", PersistentKeys = ["counter"], SessionLength = 2 }, 6); Assert.Equal(1.5, sessions.Stats.Mean);
        Assert.Equal(3, sessions.Execution!.CompletedSessions); Assert.Equal(1.5, sessions.Execution.SessionMetrics.Single(m => m.Id == "session.return").Mean);
        Assert.Equal(6, sessions.Execution.AttemptedRounds); Assert.Equal(0, sessions.Execution.InterruptedRounds);
    }
    [Fact]
    public void IndependentSessionStreamsAreBitIdenticalAtAnyWorkerCount_WithoutSplittingSessions()
    {
        var compiled = new GraphCompiler().Compile(Counter); Assert.True(compiled.IsValid);
        SampledResult<Dict> Run(int workers) => SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new() { MaxSpins = 131072, DegreeOfParallelism = workers, Execution = new() { Regime = "sessions", PersistentKeys = ["counter"], SessionLength = 2 } });
        var a = Run(1); var b = Run(4); Assert.Equal(a.Stats.Mean, b.Stats.Mean); Assert.Equal(JsonSerializer.Serialize(a.Execution), JsonSerializer.Serialize(b.Execution));
        Assert.Equal(65536, a.Execution!.CompletedSessions);
    }
    [Fact]
    public void RuinIsFirstInabilityToFundNextWager_AndLosingSessionsStayInThePopulation()
    {
        var result = SampledInterpreter.Evaluate(Slot.Pure<Dict, BigInteger>(0), new Dict(), new() { MaxSpins = 6, Execution = new() { Regime = "sessions", SessionLength = 3, InitialBankroll = 2, Wager = 1 } });
        var metrics = result.Execution!.SessionMetrics.ToDictionary(m => m.Id);
        Assert.Equal(2, result.Execution.CompletedSessions); Assert.Equal(6, result.SpinsCompleted);
        Assert.Equal(1, metrics["session.ruin"].Mean); Assert.Equal(2, metrics["session.ruinTime"].Mean);
        Assert.Equal(-3, metrics["session.profit"].Mean); Assert.Equal(3, metrics["session.drawdown"].Mean); Assert.Equal(3, metrics["session.drought"].Mean);
        Assert.Equal(0, metrics["session.return"].Mean);
    }
    [Fact]
    public void InterruptedRoundIsCountedButPublishesNoPayoutOrPartialSession()
    {
        using var token = new CancellationTokenSource();
        var program = Slot.Modify<Dict>(_ => { token.Cancel(); throw new OperationCanceledException(token.Token); }).Select(_ => BigInteger.One);
        var progress = new List<SampledProgress>();
        var result = SampledInterpreter.Evaluate(program, new Dict(), new() { MaxSpins = 2, CancellationToken = token.Token, ProgressCallback = progress.Add, Execution = new() { Regime = "sessions", SessionLength = 2 } });
        Assert.True(result.WasCancelled); Assert.Equal(0, result.SpinsCompleted); Assert.Equal(1, result.Execution!.AttemptedRounds);
        Assert.Equal(1, result.Execution.CancelledRounds); Assert.Equal(1, result.Execution.InterruptedSessions); Assert.Empty(result.Execution.SessionMetrics);
        Assert.Equal(1, Assert.Single(progress).Execution!.CancelledRounds);
    }
    [Fact]
    public void RuntimeFailurePublishesItsCompletePrefix_AndClassifiesFailedAttempt()
    {
        var progress = new List<SampledProgress>(); var program = Slot.Modify<Dict>(_ => throw new InvalidOperationException("oracle failure")).Select(_ => BigInteger.One);
        Assert.Throws<InvalidOperationException>(() => SampledInterpreter.Evaluate(program, new Dict(), new() { MaxSpins = 5, ProgressCallback = progress.Add }));
        Assert.Equal(0, Assert.Single(progress).SpinsCompleted); Assert.Equal(1, progress[0].Execution!.FailedRounds);
    }
    [Fact]
    public void LegacyEmitRejectsExecutionContractsItCannotObserve()
    {
        var program = Slot.Pure<Dict, Unit>(Unit.Value);
        Assert.Throws<ArgumentException>(() => SampledInterpreter.EvaluateEmit(program, new Dict(), new() { MaxSpins = 2, Execution = new() { Regime = "sessions", SessionLength = 2 } }));
        Assert.Throws<ArgumentException>(() => SampledInterpreter.EvaluateEmit(program, new Dict(), new() { MaxSpins = 2, Measurements = [new() { Id = "unsupported", Name = "Explicit rejection" }] }));
    }
    [Fact]
    public void ExecutionRejectsIncompleteSessionsAndFalseParallelPersistentTrajectories()
    {
        Assert.Throws<ArgumentException>(() => new ExecutionOptions { Regime = "persistent" }.Validate(5, 2));
        Assert.Throws<ArgumentException>(() => new ExecutionOptions { Regime = "sessions", SessionLength = 2 }.Validate(5, 1));
        Assert.Throws<ArgumentException>(() => new ExecutionOptions { PersistentKeys = ["counter"] }.Validate(5, 1));
    }
}
