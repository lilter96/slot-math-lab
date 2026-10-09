using System.Diagnostics;
using System.Security.Cryptography;
using SlotMath.Api.Features.Runs;
using SlotMath.Api.Infrastructure;

namespace SlotMath.Api.Tests;

public class RealtimeDurabilityTests
{
    private static RunProgressMessage Progress(long count) => new()
    {
        RunId = "ignored", Status = "running", SampleCount = count, TotalSamples = 1000,
        RunningRtp = .98, StdErr = .1, ElapsedMs = count, NonZeroCount = count / 2, HitFrequency = .5,
        Histogram = [new RunHistogramBin(0, null, count)]
    };

    [Fact]
    public void EveryTransitionHasARevision_AndTerminalStateIsAbsorbing()
    {
        var store = new InMemoryRunStore();
        var run = store.Create("graph", configHash: new string('a', 64), totalSamples: 1000);
        Assert.Equal(32, run.Id.Length);
        Assert.NotEqual(run.Id, new InMemoryRunStore().Create("graph").Id);
        Assert.Equal(0, InMemoryRunStore.Snapshot(run).Sequence);
        store.CreateCancellationToken(run.Id);
        var running = store.PublishProgress(run.Id, Progress(10))!;
        Assert.Equal(run.Id, running.RunId);
        Assert.Equal(run.StreamEpoch, running.StreamEpoch);
        Assert.True(store.Cancel(run.Id));
        var cancelling = InMemoryRunStore.Snapshot(store.Get(run.Id)!);
        Assert.True(cancelling.Sequence > running.Sequence);
        var afterCancel = store.PublishProgress(run.Id, Progress(20))!;
        Assert.Equal("cancelling", afterCancel.Status);
        Assert.True(afterCancel.Sequence > cancelling.Sequence);
        var result = "{\"status\":\"cancelled\",\"sampleCount\":20}";
        var done = store.Update(run.Id, "cancelled", result);
        var final = RunResponse.From(done);
        Assert.True(final.Sequence > afterCancel.Sequence);
        Assert.Equal(final.Sequence, final.Progress!.Sequence);
        Assert.Equal(result, final.Progress.ResultJson);
        Assert.NotNull(final.CompletedAt);
        Assert.Null(store.PublishProgress(run.Id, Progress(1000)));
        Assert.Same(done, store.Update(run.Id, "completed", "{}"));
        Assert.False(store.Cancel(run.Id));
        store.RemoveCancellationToken(run.Id);
    }

    [Fact]
    public void FailureBeforeAnyProgress_HasItsOwnTerminalRevisionAndResult()
    {
        var store = new InMemoryRunStore(); var run = store.Create("graph");
        var final = RunResponse.From(store.Update(run.Id, "failed", "{\"error\":\"invalid model\"}"));
        Assert.Equal(1, final.Sequence); Assert.Equal(1, final.Progress!.Sequence);
        Assert.Equal("failed", final.Progress.Status); Assert.Equal(final.ResultJson, final.Progress.ResultJson);
    }

    [Fact]
    public void CrashRestoresDurableCheckpointInANewEpoch_AndDoesNotResumeOrReinterruptIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "slotmath-realtime-" + Guid.NewGuid());
        try
        {
            var snapshots = new EncryptedSnapshots(directory, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            var clock = new ManualClock(); var store = new InMemoryRunStore(snapshots, clock);
            var run = store.Create("graph", seed: 99, configHash: new string('a', 64), totalSamples: 1000);
            store.PublishProgress(run.Id, Progress(100));
            clock.Advance(TimeSpan.FromSeconds(5));
            var checkpoint = store.PublishProgress(run.Id, Progress(200))!;
            var newer = store.PublishProgress(run.Id, Progress(300))!;
            store.PublishProgress(run.Id, Progress(400));
            var restoredStore = new InMemoryRunStore(snapshots, clock);
            var recovered = restoredStore.Get(run.Id)!;
            var final = RunResponse.From(recovered);
            Assert.Equal("failed", final.Status); Assert.Contains("RUN_INTERRUPTED", final.ResultJson);
            Assert.NotEqual(run.StreamEpoch, final.StreamEpoch);
            Assert.Equal(200, final.Progress!.SampleCount);
            Assert.Equal(checkpoint.Sequence + 1, final.Sequence);
            Assert.True(final.Sequence <= newer.Sequence);
            Assert.Equal(final.StreamEpoch, final.Progress.StreamEpoch);
            Assert.Equal(99, final.Seed); Assert.Equal(run.ConfigHash, final.ConfigHash);
            var again = RunResponse.From(new InMemoryRunStore(snapshots, clock).Get(run.Id)!);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(final), System.Text.Json.JsonSerializer.Serialize(again));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DeliveryCoalescesWhileBlocked_AndFlushesTheTerminalSnapshotLast()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new List<RunProgressMessage>();
        var delivery = new RunProgressDelivery(async (message, token) =>
        {
            sent.Add(message);
            if (sent.Count == 1) { entered.SetResult(); await release.Task.WaitAsync(token); }
        }, _ => Assert.Fail("Unexpected delivery failure"));
        delivery.Publish(Progress(1)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (var i = 2; i <= 100; i++) delivery.Publish(Progress(i));
        delivery.Publish(Progress(100) with { Status = "completed", ResultJson = "{}" });
        release.SetResult(); await delivery.DisposeAsync();
        Assert.Equal(2, sent.Count); Assert.Equal("completed", sent[^1].Status);
        Assert.Equal(100, sent[^1].SampleCount); Assert.Equal("{}", sent[^1].ResultJson);
    }

    [Fact]
    public async Task NonCooperativeTransportCannotHoldWorkerCleanupIndefinitely()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = 0;
        var delivery = new RunProgressDelivery((_, _) => { entered.TrySetResult(); return never.Task; },
            _ => Interlocked.Increment(ref failures), TimeSpan.FromMilliseconds(50));
        delivery.Publish(Progress(1)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        delivery.Publish(Progress(2) with { Status = "cancelled", ResultJson = "{}" });
        var watch = Stopwatch.StartNew(); await delivery.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1)); Assert.Equal(2, failures);
        never.SetResult();
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_timestamp);
        public void Advance(TimeSpan time) => _timestamp += time.Ticks;
    }
}
