using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
namespace SlotMath.Core.Tests.Math;
public class GraphMeasurementEnumerationTests
{
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
