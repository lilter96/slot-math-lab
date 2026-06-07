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
    /// If the run is currently "running", sends the latest progress snapshot
    /// immediately so the client has current state on (re)connect.
    /// </summary>
    public async Task SubscribeToRun(string runId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, runId);

        var run = _runStore.Get(runId);
        if (run is { Status: "running", SampleCount: not null })
        {
            await Clients.Caller.SendAsync("ProgressUpdate", new RunProgressMessage
            {
                RunId = runId,
                SampleCount = run.SampleCount!.Value,
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
