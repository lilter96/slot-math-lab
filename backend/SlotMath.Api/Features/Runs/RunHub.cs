using Microsoft.AspNetCore.SignalR;
using SlotMath.Api.Infrastructure;

namespace SlotMath.Api.Features.Runs;

/// <summary>
/// SignalR hub for streaming run progress to connected clients.
///
/// Clients join a group by run ID to receive progress updates.  On connect,
/// if the run is in-flight, the hub sends the current snapshot immediately
/// so reconnecting clients catch up without waiting for the next batch.
/// </summary>
public class RunHub : Hub
{
    private readonly InMemoryRunStore _runStore;

    public RunHub(InMemoryRunStore runStore) => _runStore = runStore;

    /// <summary>
    /// Subscribe to progress updates for a specific run.
    /// Sends the latest snapshot immediately — including for runs that have
    /// already finished, so a client subscribing after a fast run completes
    /// (or reconnecting) still receives the terminal state instead of
    /// waiting forever for a broadcast that already happened.
    /// </summary>
    public async Task SubscribeToRun(string runId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, runId);

        var run = _runStore.Get(runId);
        if (run is not null && run.Status != "pending")
        {
            await Clients.Caller.SendAsync("ProgressUpdate", new RunProgressMessage
            {
                RunId = runId,
                SampleCount = run.SampleCount ?? 0,
                TotalSamples = run.TotalSamples ?? 0,
                RunningRtp = run.RunningRtp ?? 0,
                StdErr = run.StdErr ?? 0,
                Status = run.Status,
                ElapsedMs = run.ElapsedMs ?? 0,
            });
        }
    }

    /// <summary>
    /// Unsubscribe from progress updates for a specific run.
    /// </summary>
    public async Task UnsubscribeFromRun(string runId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, runId);
    }
}
