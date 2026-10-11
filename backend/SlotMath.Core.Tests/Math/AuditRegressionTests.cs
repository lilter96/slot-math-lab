using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Math;

public class AuditRegressionTests
{
    [Fact]
    public void HugeRational_DisplayDoesNotBecomeNaN()
    {
        var huge = BigInteger.One << 10000;
        Assert.Equal(0.75, new Rational(huge * 3 + 1, huge * 4 + 1).ToDouble());
        Assert.Equal(double.Epsilon, new Rational(1, BigInteger.One << 1074).ToDouble());
    }

    [Fact]
    public void SharedWild_PolicyIsExplicitAndPaysEachSymbol()
    {
        var paytable = new Paytable
        {
            Id = "pt",
            Entries = [
            new PaytableEntry { SymbolId = "H", Counts = [2], Payouts = ["5"] },
            new PaytableEntry { SymbolId = "L", Counts = [2], Payouts = ["2"] }]
        };
        var state = new Dictionary<string, object?> { ["board"] = new object?[] { "H", "W", "L" }, ["rows"] = 1, ["cols"] = 3 };
        var shared = new SlotMath.Core.Mechanics.Evaluators.ClusterEvaluator(paytable, 2, "W", true).Evaluate(state);
        Assert.Equal(2, shared.Length);
        Assert.Equal(7m, shared.Sum(win => win.Payout));
        var exclusive = new SlotMath.Core.Mechanics.Evaluators.ClusterEvaluator(paytable, 2, "W", false).Evaluate(state);
        Assert.Single(exclusive);
        Assert.Equal(5m, exclusive[0].Payout);
    }

    [Fact]
    public void PrunedRareThousandWin_ContainsIndependentExpectation()
    {
        // Manual PMF: P(0)=99/100, P(1000)=1/100, hence E=10.
        var game = Slot.Draw<int>(_ => WeightSet.FromIntegers([99, 1]))
            .SelectMany(i => Slot.Emit<int>("win", i == 0 ? 0 : 1000));
        var exact = ExactEmitInterpreter.Evaluate(game, 0, _ => 0, winCap: 1000);
        Assert.Equal(new Rational(10, 1), exact.ExpectedWin);
        Assert.Equal(2, exact.TotalWin.Count);
        var pruned = ExactEmitInterpreter.Evaluate(game, 0, _ => 0,
            new ExactConfig { EpsilonNumerator = 2, EpsilonDenominator = 100 }, winCap: 1000);
        Assert.Equal(Provenance.ExactInterval, pruned.Provenance.Provenance);
        Assert.True(pruned.Provenance.Lo <= 10);
        Assert.True(pruned.Provenance.Hi >= 10);
        Assert.Equal(0.01, pruned.Provenance.PrunedMass);
        var unbounded = ExactEmitInterpreter.Evaluate(game, 0, _ => 0,
            new ExactConfig { EpsilonNumerator = 2, EpsilonDenominator = 100 });
        Assert.Equal(Provenance.ExactWithMassLoss, unbounded.Provenance.Provenance);
        Assert.Null(unbounded.Provenance.Hi);
    }

    [Fact]
    public void ValueReducer_UsesDeclaredCapAndUnnormalizedMass()
    {
        var builder = new DistBuilder<BigInteger>();
        builder.Add(2, 99, 100);
        builder.AddPrunedMass(1, 100);
        var metric = ExactMetrics.ComputeRtp(builder.Build(), 1000);
        Assert.Equal(new BigInteger(99), metric.RationalNumerator);
        Assert.Equal(new BigInteger(50), metric.RationalDenominator);
        Assert.True(metric.LoDisplay <= 1.98);
        Assert.True(metric.HiDisplay >= 11.98);
    }

    [Fact]
    public void MissingState_IsAnErrorWithLocation()
    {
        var exception = Assert.Throws<ExpressionEvaluationException>(() =>
            ExactExpressionEvaluator.Evaluate(new FieldAccessExpr { Target = "state", Path = ["missing"] },
                new EvalContext { State = new Dictionary<string, object?>() }));
        Assert.Equal("EVAL_MISSING_STATE", exception.Code);
        Assert.Equal("missing", exception.Location);
    }

    [Fact]
    public void DirectCappedLoop_TerminatesAndTracksMassLoss()
    {
        var game = Slot.Loop<int>(_ => false, Slot.Emit<int>("win", 1), cap: 3);
        var result = ExactEmitInterpreter.Evaluate(game, 0, _ => 0, winCap: 1000);
        Assert.Equal(1, result.Provenance.PrunedMass);
        Assert.True(result.Provenance.Hi >= 3);
    }

    [Fact]
    public void Compiler_CapsTheFullRoundDistribution()
    {
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Nodes = [
                new DrawNode { Id = "draw", DrawWeights = [new DrawWeight { OutcomeId = "zero", Weight = 1, Value = 0 }, new DrawWeight { OutcomeId = "high", Weight = 1, Value = 1000 }],
                    Outputs = new() { ["out"] = new() { Name = "out", Type = PortType.Wins } } },
                new MetricsSinkNode { Id = "sink", WinCap = 10, Inputs = new() { ["in"] = new() { Name = "in", Type = PortType.Wins } } }
            ],
            Edges = [new Edge { Id = "edge", SourceNodeId = "draw", SourcePort = "out", TargetNodeId = "sink", TargetPort = "in" }]
        };
        var result = new GraphCompiler().Compile(config);
        Assert.True(result.IsValid, string.Join(";", result.Errors));
        var dist = ExactInterpreter.Evaluate(result.Program!, new Dictionary<string, object?>(), StateHasher.CanonicalHash).ValueDistribution();
        Assert.Equal(new BigInteger[] { 0, 10 }, dist.Entries.Select(e => e.Value).Order().ToArray());
        Assert.Equal((new BigInteger(5), BigInteger.One), dist.ExpectedBigIntegerValue());
    }
}
