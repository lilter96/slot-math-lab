using System.Numerics;
using SlotMath.Core.Math;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Math;

public class LiveProgressTests
{
    private static readonly Slot<int, BigInteger> Program = Slot.Draw<int>(_ => WeightSet.FromIntegers([5, 3, 1]))
        .Select(outcome => new BigInteger(outcome * 7));

    [Theory]
    [InlineData(1, 127)]
    [InlineData(4, 1000)]
    public void LiveSnapshots_ArriveInsideChunks_AndLeaveFinalDistributionBitIdentical(int workers, int interval)
    {
        var snapshots = new List<SampledProgress>();
        var live = SampledInterpreter.Evaluate(Program, 0, new SampledConfig
        {
            Seed = 42,
            MaxSpins = 150000,
            MaxWinCap = 100,
            DegreeOfParallelism = workers,
            ProgressReportInterval = interval,
            ProgressCallback = snapshots.Add
        });
        var baseline = SampledInterpreter.Evaluate(Program, 0, new SampledConfig
        { Seed = 42, MaxSpins = 150000, MaxWinCap = 100, DegreeOfParallelism = 1 });
        Assert.Contains(snapshots, p => p.SpinsCompleted > 0 && p.SpinsCompleted < 65536);
        Assert.Equal(baseline.Stats.Mean, live.Stats.Mean);
        Assert.Equal(baseline.Stats.Variance, live.Stats.Variance);
        Assert.Equal(baseline.Stats.BuildAdaptiveHistogram(), live.Stats.BuildAdaptiveHistogram());
        long previous = 0;
        foreach (var snapshot in snapshots)
        {
            Assert.True(snapshot.SpinsCompleted >= previous);
            Assert.Equal(snapshot.SpinsCompleted, snapshot.Stats.AdaptiveHistogram!.Sum(bin => bin.Count));
            Assert.Equal(snapshot.SpinsCompleted, snapshot.Stats.Histogram.Sum(bin => bin.Count));
            Assert.Equal((double)snapshot.Stats.NonZeroCount / snapshot.SpinsCompleted, snapshot.Stats.HitFrequency);
            previous = snapshot.SpinsCompleted;
        }
        Assert.Equal(150000, snapshots[^1].SpinsCompleted);
    }

    [Fact]
    public void CancellationDuringFirstChunk_PreservesPartialCountsAndHistogram()
    {
        using var cancellation = new CancellationTokenSource();
        var result = SampledInterpreter.Evaluate(Program, 0, new SampledConfig
        {
            Seed = 42,
            MaxSpins = 100000,
            CancellationToken = cancellation.Token,
            CancellationCheckInterval = 1,
            ProgressReportInterval = 1000,
            ProgressCallback = _ => cancellation.Cancel()
        });
        Assert.True(result.WasCancelled);
        Assert.Equal(1000, result.SpinsCompleted);
        Assert.Equal(1000, result.Stats.BuildAdaptiveHistogram().Sum(bin => bin.Count));
    }

    [Fact]
    public void AlreadyCancelled_RunExecutesNoRounds()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var result = SampledInterpreter.Evaluate(Program, 0, new SampledConfig
        { Seed = 42, MaxSpins = 100, CancellationToken = cancellation.Token });
        Assert.True(result.WasCancelled);
        Assert.Equal(0, result.SpinsCompleted);
    }
}
