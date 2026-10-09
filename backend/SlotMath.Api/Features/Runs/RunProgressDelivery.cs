using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using SlotMath.Api.Infrastructure;

namespace SlotMath.Api.Features.Runs;

/// <summary>Latest cumulative snapshot wins. A stalled client cannot retain a
/// worker indefinitely; terminal state is already persisted before delivery.</summary>
public sealed class RunProgressDelivery : IAsyncDisposable
{
    private static readonly Meter Meter = new("SlotMath.Api.Realtime", "1.0.0");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("slotmath.realtime.send.failures");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("slotmath.realtime.send.duration", "ms");
    private readonly Channel<RunProgressMessage> _channel = Channel.CreateBounded<RunProgressMessage>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Task _sender;

    public RunProgressDelivery(Func<RunProgressMessage, CancellationToken, Task> send, Action<Exception> failed,
        TimeSpan? timeout = null)
    {
        _sender = Task.Run(async () =>
        {
            await foreach (var message in _channel.Reader.ReadAllAsync())
            {
                using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(2));
                var started = Stopwatch.GetTimestamp();
                try
                {
                    // WaitAsync bounds even transports that fail to honor cancellation.
                    await send(message, deadline.Token).WaitAsync(deadline.Token);
                    Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, new KeyValuePair<string, object?>("outcome", "delivered"));
                    if (message.Status is not ("completed" or "failed" or "cancelled")) await Task.Delay(80);
                }
                catch (Exception ex)
                {
                    var outcome = deadline.IsCancellationRequested ? "timeout" : "error";
                    Failures.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
                    Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, new KeyValuePair<string, object?>("outcome", outcome));
                    failed(ex);
                }
            }
        });
    }

    public void Publish(RunProgressMessage message) => _channel.Writer.TryWrite(message);
    public async ValueTask DisposeAsync() { _channel.Writer.TryComplete(); await _sender; }
}
