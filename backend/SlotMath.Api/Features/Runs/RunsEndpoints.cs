using System.Numerics;
using System.Text.Json;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Model;
using SlotMath.Core.Plugins;

namespace SlotMath.Api.Features.Runs;

/// <summary>
/// POST /runs + GET /runs/{id} — heavy evaluation runs.
/// </summary>
public static class RunsEndpoints
{
    public static RouteGroupBuilder MapRuns(this IEndpointRouteBuilder app, InMemoryConfigStore configStore, InMemoryRunStore runStore, PluginHost pluginHost)
    {
        var group = app.MapGroup("/api/runs");

        group.MapPost("/", (CreateRunRequest request) =>
        {
            var configEntry = configStore.GetLatest(request.ConfigId);
            if (configEntry is null)
                return Results.NotFound(new { error = $"Config '{request.ConfigId}' not found." });

            var run = runStore.Create(request.ConfigId);

            // Fire-and-forget: run evaluation in background
            _ = Task.Run(() => ExecuteRun(run.Id, configEntry.Config, request.SampleSize ?? 100_000, runStore, pluginHost));

            return Results.Accepted($"/api/runs/{run.Id}", new RunResponse
            {
                Id = run.Id,
                ConfigId = run.ConfigId,
                Status = run.Status,
                CreatedAt = run.CreatedAt,
            });
        });

        group.MapGet("/{id}", (string id) =>
        {
            var run = runStore.Get(id);
            if (run is null)
                return Results.NotFound(new { error = $"Run '{id}' not found." });

            return Results.Ok(new RunResponse
            {
                Id = run.Id,
                ConfigId = run.ConfigId,
                Status = run.Status,
                ResultJson = run.ResultJson,
                CreatedAt = run.CreatedAt,
                CompletedAt = run.CompletedAt,
            });
        });

        return group;
    }

    private static void ExecuteRun(string runId, GraphConfig config, int sampleSize,
        InMemoryRunStore runStore, PluginHost pluginHost)
    {
        try
        {
            runStore.Update(runId, "running", null);

            var compiler = new GraphCompiler(pluginHost);
            var compileResult = compiler.Compile(config);

            if (!compileResult.IsValid)
            {
                runStore.Update(runId, "failed",
                    JsonSerializer.Serialize(new { error = "Validation failed", details = compileResult.Errors }));
                return;
            }

            var regimeConfig = new RegimeConfig
            {
                SampledSpins = sampleSize,
                ForceSampled = true,
                SampledSeed = DateTimeOffset.UtcNow.Ticks,
            };

            var result = HybridEvaluator.Evaluate(
                compileResult.Program!,
                new Dictionary<string, object?>(),
                state => new BigInteger(state.GetHashCode()),
                regimeConfig);

            runStore.Update(runId, "completed",
                JsonSerializer.Serialize(new
                {
                    rtp = result.Report.Rtp.DisplayValue,
                    hitFrequency = result.Report.HitFrequency.DisplayValue,
                    volatility = result.Report.Volatility.StdDev,
                    sampleCount = sampleSize,
                    provenance = result.AggregateProvenance.ToString(),
                }));
        }
        catch (Exception ex)
        {
            runStore.Update(runId, "failed",
                JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }
}
