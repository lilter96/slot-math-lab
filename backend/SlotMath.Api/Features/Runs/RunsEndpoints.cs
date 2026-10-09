using System.Numerics;
using System.Text.Json;
using Hangfire;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Model;
using SlotMath.Core.Plugins;

namespace SlotMath.Api.Features.Runs;

/// <summary>
/// POST /runs + GET /runs/{id} + DELETE /runs/{id} — heavy evaluation runs
/// backed by Hangfire with progress streamed via SignalR (G17).
/// </summary>
public sealed record MeasurementSchemaRequest(JsonElement Config, MeasurementInput[]? Measurements = null);

public static class RunsEndpoints
{
    public static RouteGroupBuilder MapRuns(
        this IEndpointRouteBuilder app,
        InMemoryConfigStore configStore,
        InMemoryRunStore runStore,
        PluginHost pluginHost)
    {
        var group = app.MapGroup("/api/runs");
        MeasurementReplay.Map(group, configStore, runStore);
        MeasurementCalibration.Map(group, runStore);
        DiagnosticReferences.Map(group, runStore);
        group.MapPost("/measurements/reference/planning", (SlotMath.Core.Measurements.SamplePlanRequest request) =>
        {
            try { return Results.Ok(SlotMath.Core.Measurements.SamplePlanning.Calculate(request) with { AuthoredInputSha256 = RuntimeProvenance.AuthoredInputHash(request), CoreBinarySha256 = RuntimeProvenance.Current.CoreBinarySha256 }); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).Produces<SlotMath.Core.Measurements.SamplePlanReport>().RequireRateLimiting("compute");

        group.MapPost("/measurements/reference/distribution", (SlotMath.Core.Measurements.FiniteModelRequest request, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(SlotMath.Core.Measurements.FiniteModelAnalysis.Distribution(request, cancellationToken) with { AuthoredInputSha256 = RuntimeProvenance.AuthoredInputHash(request), AlgorithmVersion = "finite-law-rational-v1", CoreBinarySha256 = RuntimeProvenance.Current.CoreBinarySha256 }); }
            catch (Exception ex) when (ex is ArgumentException or ArithmeticException or FormatException) { return Results.BadRequest(new { error = ex.Message }); }
        }).Produces<SlotMath.Core.Measurements.FiniteModelReport>().RequireRateLimiting("compute");
        group.MapPost("/measurements/reference/markov", (SlotMath.Core.Measurements.MarkovModelRequest request, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(SlotMath.Core.Measurements.FiniteModelAnalysis.Markov(request, cancellationToken) with { AuthoredInputSha256 = RuntimeProvenance.AuthoredInputHash(request), AlgorithmVersion = "absorbing-rational-gauss-jordan-v1", CoreBinarySha256 = RuntimeProvenance.Current.CoreBinarySha256 }); }
            catch (Exception ex) when (ex is ArgumentException or ArithmeticException or FormatException) { return Results.BadRequest(new { error = ex.Message }); }
        }).Produces<SlotMath.Core.Measurements.MarkovModelReport>().RequireRateLimiting("compute");

        group.MapGet("/", (int? limit, string? cursor, string? status, string? search) =>
            RunArchive.Query(runStore, configStore, limit, cursor, status, search)).Produces<RunPage>();

        group.MapGet("/{id}/evidence", (string id) =>
        {
            var run = runStore.Get(id);
            if (run is null) return Results.NotFound(new { error = "Run not found." });
            var pinned = configStore.GetVersion(run.ConfigId, run.ConfigVersion);
            var hash = pinned is null ? null : CanonicalHash.Compute(pinned.Config);
            return Results.Ok(new RunEvidence(RunResponse.From(run), RunArchive.Describe(pinned, run.ConfigId),
                pinned is null ? null : JsonSerializer.SerializeToElement(pinned.Config, SlotMath.Core.JsonOptions.Default),
                hash, hash is not null && hash == run.ConfigHash) { Diagnostics = run.Diagnostics });
        }).Produces<RunEvidence>();

        group.MapPost("/{id}/measurements/reference", (string id, SlotMath.Core.Measurements.EnumerationBudget budget, CompiledGraphCache compiledGraphs, IWebHostEnvironment environment, CancellationToken cancellationToken) =>
        {
            var run = runStore.Get(id); if (run is null) return Results.NotFound(new { error = "Run not found." });
            if (run.Execution is { PersistentKeys.Length: > 0 }) return Results.BadRequest(new { error = "This run carries state. An independent-round enumeration cannot establish its session or stationary law; supply an explicit finite-state model." });
            var pinned = configStore.GetVersion(run.ConfigId, run.ConfigVersion); if (pinned is null) return Results.NotFound(new { error = "Pinned graph version is unavailable." });
            if (environment.IsProduction() && (pinned.Config.Plugins.Length > 0 || pinned.Config.Nodes.OfType<MapNode>().Any(n => n.TransformId?.StartsWith("plugin:") == true)))
                return Results.BadRequest(new { error = "Plugin execution is disabled in production." });
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                var report = SlotMath.Core.Measurements.GraphMeasurementEnumeration.Evaluate(compiledGraphs.Compile(pinned.Config, run.Measurements), run.Measurements,
                    new BigInteger(pinned.Config.Nodes.OfType<MetricsSinkNode>().Single().WinCap!.Value), budget, deadline.Token);
                var result = new GraphMeasurementReference(run.ConfigHash!, run.MeasurementHash, RuntimeProvenance.Current, report);
                return Results.Ok(result with { Retention = runStore.RetainDiagnostic(id, "graph-enumeration", budget, result) });
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Results.Json(new { error = "Enumeration exceeded the 20-second budget. Reduce the path or operation limits." }, statusCode: 408); }
            catch (Exception ex) when (ex is ArgumentException or ArithmeticException or InvalidOperationException)
            { return Results.BadRequest(new { error = ex.Message }); }
        }).Produces<GraphMeasurementReference>().RequireRateLimiting("compute");

        group.MapPost("/measurements/schema", (MeasurementSchemaRequest request, CompiledGraphCache compiledGraphs) =>
        {
            try
            {
                if (request.Measurements?.Any(m => m is null) == true) return Results.BadRequest(new { error = "Measurement entries cannot be null." });
                var compiled = compiledGraphs.Compile(SlotMath.Api.Features.Configs.ConfigsEndpoints.DeserializeConfig(request.Config), request.Measurements?.Select(m => m.ToCore()).ToArray());
                return compiled.IsValid ? Results.Ok(compiled.MeasurementSchema) : Results.BadRequest(new { error = "Graph validation failed.", errors = compiled.Errors });
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException or InvalidOperationException)
            { return Results.BadRequest(new { error = ex.Message }); }
        }).Produces<SlotMath.Core.Measurements.MeasurementSchema>().RequireRateLimiting("compute");

        // ── POST: create & enqueue a run ─────────────────────────────────
        group.MapPost("/", (
            CreateRunRequest request,
            IBackgroundJobClient jobClient, IWebHostEnvironment environment, CompiledGraphCache compiledGraphs) =>
        {
            if (request.ConfigVersion is <= 0) return Results.BadRequest(new { error = "Config version must be positive." });
            var configEntry = request.ConfigVersion is { } requestedVersion
                ? configStore.GetVersion(request.ConfigId, requestedVersion) : configStore.GetLatest(request.ConfigId);
            if (configEntry is null)
                return Results.NotFound(new { error = $"Config '{request.ConfigId}' not found." });

            if (request.SampleSize is <= 0 or > 10_000_000 || request.ProgressBatchSize is <= 0)
                return Results.BadRequest(new { error = "Sample size must be 1..10000000; progress batch must be positive." });
            if (environment.IsProduction() && (configEntry.Config.Plugins.Length > 0 || configEntry.Config.Nodes.OfType<SlotMath.Core.Model.MapNode>().Any(n => n.TransformId?.StartsWith("plugin:") == true)))
                return Results.BadRequest(new { error = "Plugin execution is disabled in production." });
            if (request.Seed is < -9_007_199_254_740_991 or > 9_007_199_254_740_991 || request.DegreeOfParallelism is < 1 or > 4)
                return Results.BadRequest(new { error = "Seed must be a safe integer; workers must be 1..4." });
            if (request.Measurements is null || request.Measurements.Length > 32 || request.Measurements.Any(m => m is null))
                return Results.BadRequest(new { error = "Use at most 32 measurements." });
            SlotMath.Core.Measurements.MeasurementDefinition[] measurements;
            try { measurements = request.Measurements.Select(m => m.ToCore()).ToArray(); }
            catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException)
            { return Results.BadRequest(new { error = "Invalid measurement AST: " + ex.Message }); }
            try
            {
                request.Execution?.Validate(request.SampleSize ?? 100_000, request.DegreeOfParallelism);
                if (request.Execution?.FeatureMetricId is { } featureId)
                {
                    var feature = measurements.FirstOrDefault(d => d.Id == featureId);
                    if (feature?.Options is not { Source: "event" or "count" } options || !(options.Subject == "round" && options.Reduction == "any" || feature.NodeId is null && options.Subject == "observation" && options.Source == "event"))
                        return Results.BadRequest(new { error = "Session feature waits require a pinned complete-round Boolean event or an any-event round reduction." });
                }
                if (request.Execution is { PersistentKeys.Length: > 0 } execution)
                {
                    var schema = compiledGraphs.Compile(configEntry.Config, measurements).MeasurementSchema;
                    if (schema is null || execution.PersistentKeys.Any(k => !schema.Fields.Any(f => f.Name == k)))
                        return Results.BadRequest(new { error = "Every persistent key must be declared by this graph." });
                    if (measurements.Any(d => d.Options is { IndependentSubjects: true } or { IndependentParents: true }))
                        return Results.BadRequest(new { error = "Retained state creates dependent paid rounds; remove independent-round inference assumptions." });
                }
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            if (measurements.Length > 0)
            {
                var validation = compiledGraphs.Compile(configEntry.Config, measurements);
                if (!validation.IsValid) return Results.BadRequest(new { error = "Measurement plan validation failed.", errors = validation.Errors });
            }
            var run = configStore.UseVersion(request.ConfigId, configEntry.Version, pinned =>
                runStore.Create(request.ConfigId, request.Seed, pinned.Version, request.SampleSize ?? 100_000,
                    CanonicalHash.Compute(pinned.Config), request.DegreeOfParallelism, measurements, request.Execution));
            if (run is null) return Results.NotFound(new { error = "Config was removed before the run could be pinned." });
            var sampleSize = request.SampleSize ?? 100_000;
            var batchSize = request.ProgressBatchSize ?? Math.Max(100, sampleSize / 100);

            // Pre-create the CTS so cancellation works even before the job starts.
            runStore.CreateCancellationToken(run.Id);

            // Enqueue via Hangfire — the job uses the pre-created CTS.
            jobClient.Enqueue<RunJobService>(s =>
                s.ExecuteRunAsync(run.Id, sampleSize, batchSize));

            return Results.Accepted($"/api/runs/{run.Id}", RunResponse.From(run));
        }).Produces<RunResponse>(202);

        // ── GET: poll run status + progress ──────────────────────────────
        group.MapGet("/{id}", (string id) =>
        {
            var run = runStore.Get(id);
            if (run is null)
                return Results.NotFound(new { error = $"Run '{id}' not found." });

            return Results.Ok(RunResponse.From(run));
        }).Produces<RunResponse>();

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

            return Results.Ok(RunResponse.From(runStore.Get(id)!));
        });

        return group;
    }
}
