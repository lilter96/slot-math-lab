using SlotMath.Api.Infrastructure;

namespace SlotMath.Api.Tests;

public class LiveRunStoreTests
{
    [Fact]
    public void EarlyCancellation_IsRetainedUntilWorkerStarts()
    {
        var store = new InMemoryRunStore();
        var run = store.Create("graph", totalSamples: 100000);
        var token = store.CreateCancellationToken(run.Id);
        Assert.True(store.Cancel(run.Id));
        Assert.Same(token, store.CreateCancellationToken(run.Id));
        Assert.True(token.IsCancellationRequested);
        Assert.Equal("cancelling", InMemoryRunStore.Snapshot(store.Get(run.Id)!).Status);
        store.RemoveCancellationToken(run.Id);
    }

    [Fact]
    public void Snapshots_RejectOlderCounts_AndPreserveTerminalStatistics()
    {
        var store = new InMemoryRunStore();
        var run = store.Create("graph", totalSamples: 1000);
        var first = store.PublishProgress(run.Id, new RunProgressMessage
        { RunId = run.Id, SampleCount = 500, TotalSamples = 1000, RunningRtp = 0.98,
            StdErr = .01, ElapsedMs = 250, Status = "running", NonZeroCount = 200,
            Histogram = [new RunHistogramBin(0, 1, 400), new RunHistogramBin(1, 5, 100)] })!;
        Assert.Null(store.PublishProgress(run.Id, first with { SampleCount = 499 }));
        var cancelled = store.Update(run.Id, "cancelled", "{\"sampleCount\":500}");
        Assert.Equal("cancelled", InMemoryRunStore.Snapshot(cancelled).Status);
        Assert.True(InMemoryRunStore.Snapshot(cancelled).Sequence > first.Sequence);
        Assert.Equal(500, InMemoryRunStore.Snapshot(cancelled).Histogram.Sum(b => b.Count));
        Assert.Null(store.PublishProgress(run.Id, first with { SampleCount = 1000 }));
        Assert.Equal("cancelled", store.Get(run.Id)!.Status);
    }
}
