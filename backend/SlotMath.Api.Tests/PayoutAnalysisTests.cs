using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using SlotMath.Api.Features.Evaluate;
using SlotMath.Api.Features.Evaluate.EvaluationStrategies;
using SlotMath.Core;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;

namespace SlotMath.Api.Tests;

public sealed class PayoutAnalysisTests
{
    private static Dist<BigInteger> Law(BigInteger high = default)
    {
        var builder = new DistBuilder<BigInteger>();
        builder.Add(0, 1, 2);
        builder.Add(high.IsZero ? 20 : high, 1, 2);
        return builder.Build();
    }

    [Fact]
    public void ExactLawReportsPopulationRiskAndDistinctPlanningAssumptions()
    {
        var request = new PayoutAnalysisRequest(TailProbabilities: ["1/2", "3/4"],
            PayoutThresholds: ["0", "20"], Stake: "2", RtpHalfWidth: "1/100");
        PayoutAnalysisOutput result = PayoutAnalysis.Build(request, Law(), null, 1)!;
        Assert.Equal("ExactLaw", result.Status);
        Assert.Equal("10/1", result.Mean!.Rational);
        Assert.Equal("5/1", result.RtpForStake!.Rational);
        Assert.Equal("100/1", result.Variance!.Rational);
        Assert.Equal("0/1", result.CentralThirdMoment!.Rational);
        Assert.Equal("10000/1", result.CentralFourthMoment!.Rational);
        Assert.Equal(1, result.CoefficientOfVariation);
        Assert.Equal(0, result.Skewness);
        Assert.Equal("-2/1", result.ExcessKurtosis!.Rational);
        Assert.Equal("0/1", result.Tails![0].PayoutQuantile.Rational);
        Assert.Equal("20/1", result.Tails[0].PayoutExpectedShortfall.Rational);
        Assert.Equal("-2/1", result.Tails[0].OperatorLossQuantile.Rational);
        Assert.Equal("18/1", result.Tails[1].OperatorLossExpectedShortfall.Rational);
        Assert.Equal("1/2", result.Thresholds![0].Cdf.Rational);
        Assert.Equal("1/1", result.Thresholds[0].SurvivalInclusive.Rational);
        Assert.Equal("1/2", result.Thresholds[1].SurvivalInclusive.Rational);
        Assert.Equal("960400", result.Planning!.Normal95Rounds);
        Assert.Equal("5000000", result.Planning.Chebyshev95Rounds);
        Assert.Contains("approximation", result.Planning.Assumptions);
    }

    [Fact]
    public void CompleteMomentsWithoutALawNeverInventTailRisk()
    {
        var structured = new StructuredExactResult
        {
            Succeeded = true,
            ExpectedWin = 100,
            SecondMoment = 20000,
            ThirdMoment = 4000000,
            FourthMoment = 800000000
        };
        PayoutAnalysisOutput result =
            PayoutAnalysis.Build(new PayoutAnalysisRequest(RtpHalfWidth: "1/100"), null, structured, 10)!;
        Assert.Equal("ExactMoments", result.Status);
        Assert.Equal("10/1", result.Mean!.Rational);
        Assert.Equal("80000/1", result.FourthMoment!.Rational);
        Assert.Equal("3841600", result.Planning!.Normal95Rounds);
        Assert.Null(result.Tails);
        Assert.Null(result.Thresholds);
        PayoutAnalysisOutput lowerOrder =
            PayoutAnalysis.Build(new PayoutAnalysisRequest(2), null, structured, 10)!;
        Assert.Null(lowerOrder.ThirdMoment);
        Assert.Null(lowerOrder.FourthMoment);
        Assert.Null(lowerOrder.Skewness);
        Assert.Null(lowerOrder.ExcessKurtosis);
    }

    [Fact]
    public void PrunedAndSampledResultsCannotClaimPopulationRisk()
    {
        var request = new PayoutAnalysisRequest();
        Assert.Equal("Unavailable", PayoutAnalysis.Build(request, Law(), new StructuredExactResult
        {
            Succeeded = true,
            PrunedMass = new Rational(1, 100)
        }, 1)!.Status);
        var pruned = new DistBuilder<BigInteger>();
        pruned.Add(20, 1, 2);
        pruned.AddPrunedMass(1, 2);
        Assert.Equal("Unavailable", PayoutAnalysis.Build(request, pruned.Build(), null, 1)!.Status);
        Assert.Equal("Unavailable", PayoutAnalysis.Build(request, null, null, 1)!.Status);
        Assert.Null(PayoutAnalysis.Build(null, null, null, 1));
        Assert.Throws<OperationCanceledException>(() =>
            PayoutAnalysis.Build(request, Law(), null, 1, new CancellationToken(true)));
    }

    [Fact]
    public void DegenerateAndVeryLargeLawsKeepUndefinedDisplaysAndExactRationals()
    {
        var constant = new DistBuilder<BigInteger>();
        constant.Add(10, 1, 1);
        PayoutAnalysisOutput result =
            PayoutAnalysis.Build(new PayoutAnalysisRequest(RtpHalfWidth: "1/100"), constant.Build(), null, 10)!;
        Assert.Equal("0/1", result.Variance!.Rational);
        Assert.Equal(0, result.CoefficientOfVariation);
        Assert.Null(result.Skewness);
        Assert.Null(result.ExcessKurtosis);
        Assert.Equal("1", result.Planning!.Normal95Rounds);
        PayoutAnalysisOutput huge =
            PayoutAnalysis.Build(new PayoutAnalysisRequest(), Law(BigInteger.One << 2048), null, 1)!;
        Assert.Null(huge.Mean!.Display);
        Assert.NotEmpty(huge.Mean.Rational);
        JsonSerializer.Serialize(huge); // Non-finite display values must not break the response.
    }

    [Theory]
    [InlineData("1/0")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("1.5")]
    public void InvalidStakeOrPlanningWidthIsRejected(string text)
    {
        Assert.False(PayoutAnalysis.IsValid(new PayoutAnalysisRequest(Stake: text)));
        Assert.False(PayoutAnalysis.IsValid(new PayoutAnalysisRequest(RtpHalfWidth: text)));
    }

    [Fact]
    public void QueryCountsPrecisionAndProbabilityRangesAreBounded()
    {
        Assert.False(PayoutAnalysis.IsValid(new PayoutAnalysisRequest(TailProbabilities: ["0"])));
        Assert.False(PayoutAnalysis.IsValid(new PayoutAnalysisRequest(TailProbabilities: ["1"])));
        Assert.False(PayoutAnalysis.IsValid(new PayoutAnalysisRequest(PayoutThresholds: new string[17])));
        Assert.False(PayoutAnalysis.IsValid(new PayoutAnalysisRequest(Stake: new string('9', 161))));
        Assert.False(PayoutAnalysis.IsValid(new PayoutAnalysisRequest(5)));
        Assert.True(PayoutAnalysis.IsValid(new PayoutAnalysisRequest(PayoutThresholds: ["-5/2"])));
    }

    [Fact]
    public void RepresentableTinyShapeDisplaysSurviveUnderflowOfTheirSquares()
    {
        var tiny = new Rational(1, BigInteger.Pow(10, 200));
        var smallVariance = new StructuredExactResult
        {
            Succeeded = true,
            ExpectedWin = 1,
            SecondMoment = Rational.One + tiny * tiny
        };
        PayoutAnalysisOutput cv = PayoutAnalysis.Build(new PayoutAnalysisRequest(2), null, smallVariance, 1)!;
        Assert.Equal(0, cv.CoefficientOfVariationSquared!.Display);
        Assert.InRange(cv.CoefficientOfVariation!.Value / 1e-200, 1 - 1e-14, 1 + 1e-14);
        var smallSkew = new StructuredExactResult
        {
            Succeeded = true,
            ExpectedWin = 1,
            SecondMoment = 2,
            ThirdMoment = 4 + tiny
        };
        PayoutAnalysisOutput skew = PayoutAnalysis.Build(new PayoutAnalysisRequest(3), null, smallSkew, 1)!;
        Assert.Equal((tiny * tiny).ToString(), skew.SkewnessSquared!.Rational);
        Assert.InRange(skew.Skewness!.Value / 1e-200, 1 - 1e-14, 1 + 1e-14);
    }

    [Fact]
    public void RepresentableLargeShapeDisplaySurvivesOverflowOfItsSquare()
    {
        BigInteger huge = BigInteger.Pow(10, 400);
        var structured = new StructuredExactResult
        {
            Succeeded = true,
            ExpectedWin = 1,
            SecondMoment = huge + 1
        };
        PayoutAnalysisOutput result = PayoutAnalysis.Build(new PayoutAnalysisRequest(2), null, structured, 1)!;
        Assert.Null(result.CoefficientOfVariationSquared!.Display);
        Assert.Equal(new Rational(huge, 1).ToString(), result.CoefficientOfVariationSquared.Rational);
        Assert.InRange(result.CoefficientOfVariation!.Value / 1e200, 1 - 1e-14, 1 + 1e-14);
    }

    [Theory]
    [InlineData("Exact", 100, 10)]
    [InlineData("Hybrid", 100, 10)]
    [InlineData("Hybrid", 100, 5)]
    public async Task StrategiesRetainAndAnalyzeTheActualSettledLaw(string mode, int branches, int cap)
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""
                                                           {"schemaVersion":"1.0.0","name":"risk contract","nodes":[
                                                           {"nodeType":"draw","id":"d","drawWeights":[{"outcomeId":"a","value":0,"weight":1},{"outcomeId":"b","value":20,"weight":1}],
                                                            "outputs":{"out":{"name":"out","type":"Number"}}},
                                                           {"nodeType":"metricsSink","id":"sink","winCap":10,"inputs":{"in":{"name":"in","type":"Number"}}}],
                                                            "edges":[{"id":"e","sourceNodeId":"d","sourcePort":"out","targetNodeId":"sink","targetPort":"in"}]}
                                                           """);
        var config = json.Deserialize<GraphConfig>(JsonOptions.Default)!;
        config = config with
        {
            Nodes = config.Nodes.Select(node => node is MetricsSinkNode sink
                ? sink with
                {
                    WinCap = cap
                }
                : node).ToArray()
        };
        CompileResult compiled = new GraphCompiler().Compile(config);
        Assert.True(compiled.IsValid, string.Join("; ", compiled.Errors.Select(error => error.Message)));
        BaseEvaluationStrategy strategy =
            mode == "Exact" ? new ExactOrPrunedEvaluationStrategy() : new HybridEvaluationStrategy();
        var request = new GraphEvaluationRequest(json, mode, Analysis: new PayoutAnalysisRequest());
        EvaluationOutput output = await strategy.EvaluateAsync(compiled, config, request,
            new Budget
            {
                MaxBranches = branches,
                MaxTime = TimeSpan.FromSeconds(5)
            }, "test", Stopwatch.StartNew(), CancellationToken.None);
        Assert.Equal("ExactLaw", output.Analysis!.Status);
        Assert.Equal(new Rational(cap, 2).ToString(), output.Analysis.Mean!.Rational);
        Assert.Equal(new Rational(cap, 1).ToString(), output.Analysis.Tails![0].PayoutQuantile.Rational);
    }
}
