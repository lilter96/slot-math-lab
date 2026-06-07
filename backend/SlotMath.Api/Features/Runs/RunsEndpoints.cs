using System.Numerics;
using System.Text.Json;
using Hangfire;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Model;
using SlotMath.Core.Plugins;

namespace SlotMath.Api.Features.Runs;

/// <summary>
/// POST /runs + GET /runs/{id} + DELETE /runs/{id} — heavy evaluation runs
/// backed by Hangfire with progress streamed via SignalR (G17).
/// </summary>
public static class RunsEndpoints
{
    public static RouteGroupBuilder MapRuns(
        this IEndpointRouteBuilder app,
        InMemoryConfigStore configStore,
        InMemoryRunStore runStore,
        PluginHost pluginHost)
    {
        var group = app.MapGroup("/api/runs");

        // ── POST: create & enqueue a run ─────────────────────────────────
        group.MapPost("/", (
            CreateRunRequest request,
            IBackgroundJobClient jobClient) =>
        {
            var configEntry = configStore.GetLatest(request.ConfigId);
            if (configEntry is null)
                return Results.NotFound(new { error = $"Config '{request.ConfigId}' not found." });

            var run = runStore.Create(request.ConfigId);
            var sampleSize = request.SampleSize ?? 100_000;
            var batchSize = request.ProgressBatchSize ?? Math.Max(100, sampleSize / 100);

            // Pre-create the CTS so cancellation works even before the job starts.
            runStore.CreateCancellationToken(run.Id);

            // Enqueue via Hangfire — the job uses the pre-created CTS.
            jobClient.Enqueue<RunJobService>(s =>
                s.ExecuteRunAsync(run.Id, sampleSize, batchSize));

            return Results.Accepted($"/api/runs/{run.Id}", new RunResponse
            {
                Id = run.Id,
                ConfigId = run.ConfigId,
                Status = run.Status,
                CreatedAt = run.CreatedAt,
            });
        });

        // ── GET: poll run status + progress ──────────────────────────────
        group.MapGet("/{id}", (string id) =>
        {
            var run = runStore.Get(id);
            if (run is null)
                return Results.NotFound(new { error = $"Run '{id}' not found." });

            RunProgressMessage? progress = null;
            if (run.Status == "running" && run.SampleCount.HasValue)
            {
                progress = new RunProgressMessage
                {
                    RunId = run.Id,
                    SampleCount = run.SampleCount.Value,
                    TotalSamples = run.TotalSamples ?? 0,
                    RunningRtp = run.RunningRtp ?? 0,
                    StdErr = run.StdErr ?? 0,
                    Status = run.Status,
                    ElapsedMs = run.ElapsedMs ?? 0,
                };
            }

            return Results.Ok(new RunResponse
            {
                Id = run.Id,
                ConfigId = run.ConfigId,
                Status = run.Status,
                ResultJson = run.ResultJson,
                CreatedAt = run.CreatedAt,
                CompletedAt = run.CompletedAt,
                Progress = progress,
            });
        });

        // ── DELETE: cancel a running job ─────────────────────────────────
        group.MapDelete("/{id}", (string id) =>
        {
            var run = runStore.Get(id);
            if (run is null)
                return Results.NotFound(new { error = $"Run '{id}' not found." });

            if (run.Status is "completed" or "failed" or "cancelled")
            {
                return Results.Conflict(new
                {
                    error = $"Run '{id}' is already {run.Status} and cannot be cancelled.",
                });
            }

            var cancelled = runStore.Cancel(id);
            if (!cancelled)
            {
                return Results.Conflict(new
                {
                    error = $"Run '{id}' has no active cancellation token.",
                });
            }

            return Results.Ok(new { id, status = "cancelling" });
        });

        return group;
    }
}
