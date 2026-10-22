using System.Diagnostics;
using System.Text.Json;
using SlotMath.Api.Features.Evaluate;
using SlotMath.Api.Features.Evaluate.EvaluationStrategies;
using SlotMath.Core;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Model;

namespace SlotMath.Api.Tests;

public sealed class EvaluationEvidenceTests
{
    private static JsonElement Coin(int cap) => JsonSerializer.SerializeToElement(new
    {
        schemaVersion = "1.0.0",
        name = "evidence regression",
        nodes = new object[]
        {
            new
            {
                nodeType = "draw",
                id = "d",
                drawWeights = new[]
                {
                    new
                    {
                        outcomeId = "zero",
                        value = 0,
                        weight = 1
                    },
                    new
                    {
                        outcomeId = "high",
                        value = 20,
                        weight = 1
                    }
                },
                outputs = new
                {
                    @out = new
                    {
                        name = "out",
                        type = "Number"
                    }
                }
            },
            new
            {
                nodeType = "metricsSink",
                id = "sink",
                winCap = cap,
                inputs = new
                {
                    @in = new
                    {
                        name = "in",
                        type = "Number"
                    }
                }
            }
        },
        edges = new[]
        {
            new
            {
                id = "e",
                sourceNodeId = "d",
                sourcePort = "out",
                targetNodeId = "sink",
                targetPort = "in"
            }
        }
    });

    [Theory]
    [InlineData("Exact", "Moments", 10)]
    [InlineData("Hybrid", "Moments", 10)]
    [InlineData("Pruned", "Moments", 100)]
    [InlineData("Exact", "Mean", 10)]
    [InlineData("Hybrid", "Mean", 100)]
    public async Task SelectedEvidenceUsesStructuredPlanAndPreservesSettlement(string mode, string evidence, int cap)
    {
        JsonElement json = Coin(cap);
        var config = json.Deserialize<GraphConfig>(JsonOptions.Default)!;
        CompileResult compiled = new GraphCompiler().Compile(config);
        Assert.True(compiled.IsValid);
        BaseEvaluationStrategy strategy = mode == "Hybrid"
            ? new HybridEvaluationStrategy()
            : new ExactOrPrunedEvaluationStrategy();
        var request = new GraphEvaluationRequest(json, mode, Evidence: evidence, Diagnostics: true,
            Analysis: evidence == "Moments" ? new PayoutAnalysisRequest(TailProbabilities: []) : null);
        Assert.True(EvaluationEvidence.IsValid(request));
        EvaluationOutput output = await strategy.EvaluateAsync(compiled, config, request,
            new Budget
            {
                MaxBranches = 100,
                MaxTime = TimeSpan.FromSeconds(5)
            }, "test",
            Stopwatch.StartNew(), CancellationToken.None);
        var high = Math.Min(20, cap);
        Assert.Equal("structured", output.Method);
        Assert.Equal(evidence, output.RequestedEvidence);
        Assert.Equal("NotRequested", output.Distribution);
        Assert.Equal(high / 2.0, output.Rtp);
        Assert.Equal("Exact", output.Provenance);
        Assert.Null(output.MaxObserved);
        Assert.NotNull(output.Solver);
        Assert.Equal(output.Solver.Work, output.Solver.Attempts.Sum(attempt => attempt.Work));
        Assert.All(output.Solver.Attempts, attempt => Assert.False(attempt.PayoutLawRequested));
        Assert.Contains(output.Solver.Storage!, counter => counter.Kind == "TermObjects" && counter.Peak > 0);
        Assert.Contains("not requested", output.Note);
        if (evidence == "Mean")
        {
            Assert.Null(output.Variance);
            Assert.Null(output.HitFrequency);
            Assert.Null(output.Analysis);
        }
        else
        {
            Assert.Equal(high * high / 4.0, output.Variance);
            Assert.Equal(0.5, output.HitFrequency);
            Assert.Equal("ExactMoments", output.Analysis!.Status);
            Assert.Equal(new Rational(high * high * high * high, 2).ToString(), output.Analysis.FourthMoment!.Rational);
            Assert.Null(output.Analysis.Tails);
        }
    }

    [Theory]
    [InlineData("Report", "Sampled", true)]
    [InlineData("Moments", "Sampled", false)]
    [InlineData("Moments", "sampled", false)]
    [InlineData("Mean", "Exact", true)]
    [InlineData("invalid", "Exact", false)]
    [InlineData("3", "Exact", false)]
    public void EvidenceNamesAndSampledContractAreExplicit(string evidence, string mode, bool valid)
        => Assert.Equal(valid,
            EvaluationEvidence.IsValid(new GraphEvaluationRequest(default, mode, Evidence: evidence)));

    [Theory]
    [InlineData("Exact", true)]
    [InlineData("exact", true)]
    [InlineData(" SAMPLEd ", true)]
    [InlineData("Pruned", true)]
    [InlineData("Hybrid", true)]
    [InlineData("0", false)]
    [InlineData("Exact, Pruned", false)]
    [InlineData("invalid", false)]
    [InlineData(null, false)]
    public void ModesPreserveNamedCaseInsensitiveInputsAndRejectNumericOrCombinedEnums(string? mode, bool valid)
        => Assert.Equal(valid, EvaluationEvidence.TryMode(mode, out _));

    [Fact]
    public void TailAndHorizonRequestsRequireFullReport()
    {
        var moments = new GraphEvaluationRequest(default, "Exact", Evidence: "Moments",
            Analysis: new PayoutAnalysisRequest(TailProbabilities: []));
        Assert.True(EvaluationEvidence.IsValid(moments));
        Assert.False(EvaluationEvidence.IsValid(moments with
        {
            Analysis = new PayoutAnalysisRequest()
        }));
        Assert.False(EvaluationEvidence.IsValid(moments with
        {
            Analysis = new PayoutAnalysisRequest(TailProbabilities: ["19/20"])
        }));
        Assert.False(EvaluationEvidence.IsValid(moments with
        {
            Analysis = new PayoutAnalysisRequest(TailProbabilities: [], PayoutThresholds: ["2"])
        }));
        Assert.False(EvaluationEvidence.IsValid(moments with
        {
            Analysis = new PayoutAnalysisRequest(TailProbabilities: [],
                IndependentRounds: new IndependentRoundExposureRequest())
        }));
        Assert.False(EvaluationEvidence.IsValid(moments with
        {
            Evidence = "Mean"
        }));
        Assert.True(EvaluationEvidence.IsValid(moments with
        {
            Evidence = "Report",
            Analysis = new PayoutAnalysisRequest()
        }));
    }
}
