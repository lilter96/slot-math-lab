using SlotMath.Api.Infrastructure;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

namespace SlotMath.Api.Features.Runs;

public sealed record MeasurementReplayRequest(long RoundIndex);
public sealed record MeasurementReplayReport(string RunId, long RoundIndex, string ConfigHash, string? MeasurementHash,
    RuntimeProvenance RuntimeProvenance, IReadOnlyList<MeasurementSnapshot> Measurements, string Reconstruction)
{ public DiagnosticRetention? Retention { get; init; } }

public static class MeasurementReplay
{
    public static void Map(RouteGroupBuilder group, InMemoryConfigStore configs, InMemoryRunStore runs)
    {
        group.MapPost("/{id}/measurements/replay", async (string id, MeasurementReplayRequest request,
            CompiledGraphCache compiler, IWebHostEnvironment environment, CancellationToken cancellationToken) =>
        {
            var run = runs.Get(id); if (run is null) return Results.NotFound(new { error = "Run not found." });
            if (request.RoundIndex < 0 || request.RoundIndex >= (run.TotalSamples ?? 0)) return Results.BadRequest(new { error = "Choose an absolute zero-based round in this run's pinned budget." });
            var config = configs.GetVersion(run.ConfigId, run.ConfigVersion)?.Config;
            if (config is null) return Results.NotFound(new { error = "Pinned graph version is unavailable." });
            if (run.RuntimeProvenance?.CoreBinarySha256 is null || RuntimeProvenance.Current.CoreBinarySha256 is null || run.RuntimeProvenance.CoreBinarySha256 != RuntimeProvenance.Current.CoreBinarySha256)
                return Results.Conflict(new { error = "The saved run used a different or unidentified Core binary. Use that artifact for a verifiable replay." });
            if (environment.IsProduction() && (config.Plugins.Length > 0 || config.Nodes.OfType<MapNode>().Any(n => n.TransformId?.StartsWith("plugin:") == true)))
                return Results.BadRequest(new { error = "Plugin execution is disabled in production." });
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                var compiled = compiler.Compile(config, run.Measurements);
                if (!compiled.IsValid) return Results.BadRequest(new { error = "Pinned graph or measurement plan is invalid.", errors = compiled.Errors });
                var result = await Task.Run(() => SampledInterpreter.Evaluate(compiled.Program!, new Dictionary<string, object?>(), new()
                {
                    ReplayRoundIndex = request.RoundIndex,
                    MaxSpins = run.TotalSamples ?? 0,
                    Seed = run.Seed,
                    Measurements = run.Measurements,
                    Execution = run.Execution,
                    DegreeOfParallelism = 1,
                    WinScale = (double)compiled.WinScale,
                    MaxWinCap = config.Nodes.OfType<MetricsSinkNode>().Single().WinCap,
                    CancellationToken = deadline.Token,
                    CancellationCheckInterval = 1
                }), deadline.Token);
                if (result.ReplayedRound is null) return result.WasCancelled ? Results.Json(new { error = "Replay was cancelled before its selected round." }, statusCode: 408) : Results.BadRequest(new { error = "This round slot was not played under the pinned session stopping policy." });
                var report = new MeasurementReplayReport(id, request.RoundIndex, run.ConfigHash!, run.MeasurementHash,
                    RuntimeProvenance.Current, result.ReplayedRound, "Reconstructed the selected logical stream prefix, including retained state; other worker streams are omitted. Instrumentation does not consume game RNG.");
                return Results.Ok(report with { Retention = runs.RetainDiagnostic(id, "witness-replay", request, report) });
            }
            catch (OperationCanceledException) { return Results.Json(new { error = "Replay was cancelled or exceeded its 20-second budget." }, statusCode: 408); }
            catch (Exception ex) when (ex is ArgumentException or ArithmeticException or InvalidOperationException)
            { return Results.BadRequest(new { error = ex.Message }); }
        }).Produces<MeasurementReplayReport>().RequireRateLimiting("compute");
    }
}
