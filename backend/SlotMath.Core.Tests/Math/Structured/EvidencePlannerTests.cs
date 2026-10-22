using System.Text.Json.Nodes;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;

namespace SlotMath.Core.Tests.Math.Structured;

public sealed partial class MarkovLoopTests
{
    [Fact]
    public void SequentialDeterministicLoopsReleaseMaterializedExitBuffers()
    {
        JsonObject graph = Fixture(64);
        JsonArray nodes = graph["nodes"]!.AsArray();
        JsonArray edges = graph["edges"]!.AsArray();
        foreach (JsonNode? original in nodes.Take(3).ToArray())
        {
            JsonNode clone = original!.DeepClone();
            clone["id"] = clone["id"]!.GetValue<string>() + "2";
            clone["inputs"] = nodes[1]!["inputs"]!.DeepClone();
            nodes.Add(clone);
        }

        foreach (JsonNode? original in edges.ToArray())
        {
            JsonNode clone = original!.DeepClone();
            clone["id"] = clone["id"]!.GetValue<string>() + "2";
            clone["sourceNodeId"] = clone["sourceNodeId"]!.GetValue<string>() + "2";
            var target = clone["targetNodeId"]!.GetValue<string>();
            clone["targetNodeId"] = target == "sink" ? target : target + "2";
            edges.Add(clone);
        }

        edges.Single(edge => edge!["id"]!.GetValue<string>() == "exit")!["targetNodeId"] = "repeat2";
        var options = new StructuredExactOptions
        {
            TrackStorage = true
        };
        StructuredExactResult one = Evaluate(Compile(Fixture(64)), options);
        StructuredExactResult two = Evaluate(Compile(graph), options);
        Assert.Equal(new Rational(256, 1), two.ExpectedWin);

        long Peak(StructuredExactResult result)
        {
            return result.Storage!.Counters.Single(counter =>
                counter.Kind == ExactStorageKind.LoopExitCoefficientSlots).Peak;
        }

        Assert.True(Peak(one) > 0);
        Assert.Equal(Peak(one), Peak(two));
    }

    [Fact]
    public void MomentEvidenceSkipsThePayoutLawAndMatchesTheIndependentClosedForm()
    {
        const int horizon = 256;
        CompileResult compiled = Compile(RandomModel(horizon));
        StructuredExactResult result = Evaluate(compiled, new StructuredExactOptions
        {
            Evidence = StructuredEvidence.Moments,
            DistributionWork = 0,
            MomentOrder = 4,
            TrackStorage = true,
            PruneLoopMassBelow = null,
            MaxTime = TimeSpan.FromSeconds(15)
        });
        Assert.Null(result.Distribution);
        Assert.Null(result.HitFrequency);
        var mean = new Rational(2 * horizon - 1, 2);
        var variance = new Rational(4 * horizon - 3, 4);
        Assert.Equal(mean, result.ExpectedWin);
        Assert.Equal(variance, result.Variance(compiled.WinScale));
        Assert.Equal(mean * mean * mean + 3 * mean * variance, result.ThirdMoment);
        Assert.Equal(mean * mean * mean * mean + 6 * mean * mean * variance + 3 * variance * variance
                     - new Rational(16 * horizon - 15, 8), result.FourthMoment);
        StructuredAttemptResult attempt = Assert.Single(result.Attempts);
        Assert.Equal("Moments", attempt.LoopMode);
        Assert.False(attempt.PayoutLawAttempted);
        Assert.False(attempt.PayoutLawNeededForSettlement);
        Assert.False(attempt.PayoutLawRequested);
        Assert.DoesNotContain(result.SolverNotes, note => note.Contains("matrix law probe", StringComparison.Ordinal));
        Assert.All(result.Storage!.Counters, counter => Assert.Equal(0, counter.Current));
        Assert.True(result.Storage.Counters.Single(counter => counter.Kind == ExactStorageKind.MatrixCoefficientSlots)
            .Peak > 0);
    }

    [Fact]
    public void LawFirstReferenceAndEvidencePlannerReturnTheSameRequestedFigures()
    {
        CompileResult compiled = Compile(RandomModel(128));
        var options = new StructuredExactOptions
        {
            Evidence = StructuredEvidence.Moments | StructuredEvidence.HitFrequency,
            MomentOrder = 4,
            PruneLoopMassBelow = null
        };
        StructuredExactResult automatic = Evaluate(compiled, options);
        StructuredExactResult reference = Evaluate(compiled, options with
        {
            SolverPlan = StructuredSolverPlan.LawFirst
        });
        Assert.Equal(reference.ExpectedWin, automatic.ExpectedWin);
        Assert.Equal(reference.SecondMoment, automatic.SecondMoment);
        Assert.Equal(reference.ThirdMoment, automatic.ThirdMoment);
        Assert.Equal(reference.FourthMoment, automatic.FourthMoment);
        Assert.Equal(reference.HitFrequency, automatic.HitFrequency);
        Assert.Null(reference.Distribution);
        Assert.Null(automatic.Distribution);
        Assert.True(Assert.Single(reference.Attempts).PayoutLawAttempted);
        Assert.False(Assert.Single(automatic.Attempts).PayoutLawAttempted);
    }

    [Fact]
    public void MeanOnlyEvidenceDoesNotProduceUnrequestedOptionalMetrics()
    {
        StructuredExactResult result = Evaluate(Compile(RandomModel(128)), new StructuredExactOptions
        {
            Evidence = StructuredEvidence.Mean,
            PruneLoopMassBelow = null
        });
        Assert.Equal(new Rational(255, 2), result.ExpectedWin);
        Assert.Null(result.SecondMoment);
        Assert.Null(result.HitFrequency);
        Assert.Null(result.Distribution);
        Assert.Null(result.Storage);
        Assert.False(Assert.Single(result.Attempts).PayoutLawAttempted);
    }

    [Fact]
    public void RequestedMomentsKeepTheLawFallbackWhenTheAuthoredCapRequiresIt()
    {
        JsonObject graph = RandomModel(65);
        graph["nodes"]!.AsArray().Single(node => node!["id"]!.GetValue<string>() == "sink")!["winCap"] = 50;
        CompileResult compiled = Compile(graph);
        StructuredExactResult report = Evaluate(compiled, new StructuredExactOptions
        {
            PruneLoopMassBelow = null
        });
        StructuredExactResult moments = Evaluate(compiled, new StructuredExactOptions
        {
            Evidence = StructuredEvidence.Moments,
            TrackStorage = true,
            PruneLoopMassBelow = null
        });
        Assert.Equal(report.ExpectedWin, moments.ExpectedWin);
        Assert.Equal(report.SecondMoment, moments.SecondMoment);
        Assert.Null(moments.Distribution);
        Assert.Contains(moments.Attempts, attempt => !attempt.Succeeded && attempt.LoopMode == "Moments");
        Assert.Contains(moments.Attempts, attempt => attempt.Succeeded && attempt.PayoutLawNeededForSettlement
                                                                       && attempt.PayoutLawAttempted &&
                                                                       !attempt.PayoutLawRequested);
        Assert.Equal(moments.Work, moments.Attempts.Sum(attempt => attempt.Work));
        Assert.All(moments.Storage!.Counters, counter => Assert.Equal(0, counter.Current));
    }

    [Fact]
    public void ProfilingPreservesDefaultReportEvidenceAndTheCompleteLaw()
    {
        CompileResult compiled = Compile(RandomModel(65));
        StructuredExactResult baseline = Evaluate(compiled, new StructuredExactOptions());
        StructuredExactResult profiled = Evaluate(compiled, new StructuredExactOptions
        {
            TrackStorage = true
        });
        Assert.Equal(baseline.ExpectedWin, profiled.ExpectedWin);
        Assert.Equal(baseline.SecondMoment, profiled.SecondMoment);
        Assert.Equal(baseline.HitFrequency, profiled.HitFrequency);
        Assert.NotNull(profiled.Distribution);
        Assert.Equal(baseline.Distribution!.Denominator, profiled.Distribution.Denominator);
        Assert.Equal(baseline.Distribution.Entries.OrderBy(entry => entry.Value),
            profiled.Distribution.Entries.OrderBy(entry => entry.Value));
        Assert.True(Assert.Single(profiled.Attempts).PayoutLawRequested);
        Assert.All(profiled.Storage!.Counters, counter => Assert.Equal(0, counter.Current));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(32, 2)]
    [InlineData(1, 4)]
    public void UnsupportedEvidenceContractsAreRejected(int evidence, int order)
    {
        CompileResult compiled = Compile(Fixture(65));
        Assert.Throws<ArgumentException>(() => StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions
        {
            Evidence = (StructuredEvidence)evidence,
            MomentOrder = order
        }));
    }
}
