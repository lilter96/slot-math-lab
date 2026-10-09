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
    public async Task<RunResponse> SubscribeToRun(string runId)
    {
        GetRun(runId);
        if (Context.Items.TryGetValue("run", out var previous) && previous is string old && old != runId)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, old, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, runId, Context.ConnectionAborted);
        Context.Items["run"] = runId;
        var snapshot = RunResponse.From(GetRun(runId));
        await Clients.Caller.SendAsync("ProgressUpdate", snapshot.Progress, Context.ConnectionAborted);
        return snapshot;
    }

    /// <summary>Application-level liveness acknowledgement and authoritative resync.</summary>
    public RunResponse GetRunSnapshot(string runId) => RunResponse.From(GetRun(runId));

    private RunEntry GetRun(string runId) => !string.IsNullOrEmpty(runId) && runId.Length <= 128
        ? _runStore.Get(runId) ?? throw new HubException("RUN_NOT_FOUND")
        : throw new HubException("RUN_NOT_FOUND");

    /// <summary>
    /// Unsubscribe from progress updates for a specific run.
    /// </summary>
    public async Task UnsubscribeFromRun(string runId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, runId, Context.ConnectionAborted);
        if (Context.Items.TryGetValue("run", out var current) && current as string == runId) Context.Items.Remove("run");
    }
}
