using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
namespace SlotMath.Core.Tests.Math;
public class GraphMeasurementEnumerationTests
{
    private static MeasurementDefinition PairedCoin(int supportLimit = 256, int groupLimit = 32) => new() { Id = "paired", Name = "Paid payout and raw payout",
        Options = new() { SupportLimit = supportLimit, GroupLimit = groupLimit,
            Group = new CompareExpr { Op = CompareOp.Gt, Left = new FieldAccessExpr { Target = "measurement", Path = ["payout"] }, Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" } },
            Pair = new FieldAccessExpr { Target = "measurement", Path = ["rawPayout"] } } };
    [Fact]
    public void GroupedJointLawMatchesIndependentTwoOutcomeCovarianceAndVarianceIdentity()
    {
        MeasurementDefinition[] plans = [PairedCoin()];
        var report = GraphMeasurementEnumeration.Evaluate(new GraphCompiler().Compile(Coin, plans), plans, 1);
        var law = Assert.Single(report.Measurements); Assert.True(law.GroupsComplete);
        Assert.Equal(new[] { "false", "true" }, law.Groups.Select(g => g.Key));
        Assert.All(law.Groups, g => { Assert.True(g.SupportComplete); Assert.Equal("1/2", g.ValidPerRound); });
        Assert.Equal("0/1", law.Groups[0].ConditionalMean); Assert.Equal("1/1", law.Groups[1].ConditionalMean);
        var pair = law.Pair!; Assert.True(pair.Complete); Assert.Equal("1/1", pair.PairedPerRound);
        Assert.Equal(new[] { new EnumeratedJointValue(0, 0, "1/2"), new EnumeratedJointValue(1, 2, "1/2") }, pair.Support);
        Assert.Equal("1/2", pair.MeanX); Assert.Equal("1/1", pair.MeanY);
        Assert.Equal("1/4", pair.VarianceX); Assert.Equal("1/1", pair.VarianceY); Assert.Equal("1/2", pair.Covariance);
        Assert.Equal("9/4", pair.VarianceSum); Assert.Equal("1/4", pair.VarianceDifference);
        Assert.All(law.Groups, g => Assert.Equal("0/1", g.Pair!.Covariance));
        Assert.Equal(new EnumeratedParentExposure("1/1", null, true), law.ParentExposure);
        Assert.All(law.Groups, g => Assert.Equal(new EnumeratedParentExposure("1/2", null, true), g.ParentExposure));
    }
    [Fact]
    public void IncompletePathsAndSupportBudgetsWithholdConditionalJointAndCohortGuarantees()
    {
        MeasurementDefinition[] plans = [PairedCoin()];
        var cut = GraphMeasurementEnumeration.Evaluate(new GraphCompiler().Compile(Coin, plans), plans, 1, new(MaximumPaths: 1)).Measurements[0];
        Assert.False(cut.GroupsComplete); Assert.False(cut.Pair!.Complete); Assert.Null(cut.Pair.Covariance);
        Assert.Null(Assert.Single(cut.Groups).ConditionalMean);
        Assert.Equal(new EnumeratedParentExposure("1/2", null, false), cut.ParentExposure);
        Assert.Equal(new EnumeratedParentExposure("1/2", null, false), Assert.Single(cut.Groups).ParentExposure);
        plans = [PairedCoin(supportLimit: 1, groupLimit: 1)];
        var bounded = GraphMeasurementEnumeration.Evaluate(new GraphCompiler().Compile(Coin, plans), plans, 1).Measurements[0];
        Assert.False(bounded.GroupsComplete); Assert.Empty(bounded.Groups); Assert.False(bounded.Pair!.Complete);
        Assert.Empty(bounded.Pair.Support); Assert.Null(bounded.Pair.MeanX); Assert.Null(bounded.ConditionalMean);
    }
    private static readonly GraphConfig Coin = JsonSerializer.Deserialize<GraphConfig>("""
    {"schemaVersion":"1.0.0","name":"Enumeration capped coin","nodes":[{"nodeType":"draw","id":"draw","drawWeights":[{"outcomeId":"zero","value":0,"weight":1},{"outcomeId":"two","value":2,"weight":1}],"outputs":{"out":{"name":"out","type":"Wins"}}},{"nodeType":"metricsSink","id":"sink","winCap":1,"inputs":{"in":{"name":"in","type":"Wins"}}}],"edges":[{"id":"e","sourceNodeId":"draw","sourcePort":"out","targetNodeId":"sink","targetPort":"in"}]}
    """, SlotMath.Core.JsonOptions.Default)!;
    [Fact]
    public void FullCanonicalLawIncludesRawSettlementAndCapDeduction_WithoutSampling()
    {
        MeasurementDefinition[] plans = [new() { Id = "settled", Name = "Settled" }, new() { Id = "raw", Name = "Raw", Options = new() { Source = "rawPayout" } }, new() { Id = "deduction", Name = "Removed", Options = new() { Source = "capDeduction" } }];
        var report = GraphMeasurementEnumeration.Evaluate(new GraphCompiler().Compile(Coin, plans), plans, 1);
        Assert.Equal("Enumerated", report.Status); Assert.Equal("1/1", report.RetainedRoundMass); Assert.Equal("1/2", report.RoundMean.Lower); Assert.Equal(report.RoundMean.Lower, report.RoundMean.Upper);
        Assert.Equal(new[] { new EnumeratedValue(0, "1/2"), new EnumeratedValue(1, "1/2") }, report.Measurements[0].Support);
        Assert.Equal("1/1", report.Measurements[1].KnownSumPerRound); Assert.Equal("1/2", report.Measurements[2].ConditionalMean);
    }
    [Fact]
    public void CutProbabilityIsNotRenormalized_AndConditionalMeasurementProofIsWithheld()
    {
        MeasurementDefinition[] plans = [new() { Id = "law", Name = "Payout" }];
        var report = GraphMeasurementEnumeration.Evaluate(new GraphCompiler().Compile(Coin, plans), plans, 1, new(MaximumPaths: 1));
        Assert.Equal("BoundedEnumeration", report.Status); Assert.Equal("1/2", report.UnresolvedRoundMass);
        Assert.Equal("0/1", report.RoundMean.Lower); Assert.Equal("1/2", report.RoundMean.Upper);
        Assert.False(report.Measurements[0].SupportComplete); Assert.Null(report.Measurements[0].ConditionalMean);
    }
    [Fact]
    public void RepeatedRevealLawAndEpisodeTotalsKeepTheirOwnExposureDenominators()
    {
        MeasurementDefinition[] plans = [new() { Id = "reveal", Name = "Reveal", NodeId = "end", Value = new FieldAccessExpr { Target = "state", Path = ["spinWin"] } },
            new() { Id = "episode", Name = "Episode", NodeId = "end", Value = new FieldAccessExpr { Target = "state", Path = ["spinWin"] }, Options = new() { Subject = "episode", EntryNodeId = "fs", ExitNodeId = "sink" } }];
        var report = GraphMeasurementEnumeration.Evaluate(new GraphCompiler().Compile(MeasurementTests.Model, plans), plans, 10);
        Assert.Equal("3/1", report.Measurements[0].ValidPerRound); Assert.Equal("3/1", report.Measurements[0].ConditionalMean);
        Assert.Equal("1/1", report.Measurements[1].ValidPerRound); Assert.Equal("9/1", report.Measurements[1].ConditionalMean);
    }
}
