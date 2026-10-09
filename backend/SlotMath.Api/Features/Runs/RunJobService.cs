using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Math;

namespace SlotMath.Api.Features.Runs;

public class RunJobService(InMemoryConfigStore configStore, InMemoryRunStore runStore,
    IHubContext<RunHub> hubContext, ILogger<RunJobService> logger, CompiledGraphCache compiledGraphs, IHostApplicationLifetime lifetime)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteRunAsync(string runId, int sampleSize, int progressBatchSize)
    {
        var cts = runStore.CreateCancellationToken(runId);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, lifetime.ApplicationStopping);
        operation.CancelAfter(TimeSpan.FromMinutes(5));
        // One sender per run preserves ordering and applies backpressure. Slow
        // clients receive the latest snapshot, never an unbounded message queue.
        var delivery = new RunProgressDelivery(
            (message, token) => hubContext.Clients.Group(runId).SendAsync("ProgressUpdate", message, token),
            ex => logger.LogWarning(ex, "Run {RunId}: progress delivery failed; snapshots remain available", runId));
        void Publish(SampledProgress progress)
        {
            var s = progress.Stats;
            var message = runStore.PublishProgress(runId, new RunProgressMessage
            {
                MeasurementHash = runStore.Get(runId)?.MeasurementHash, Measurements = progress.Measurements,
                RunId = runId, Status = "running", SampleCount = s.Count, TotalSamples = sampleSize,
                RunningRtp = s.Mean, StdErr = s.StdErr, ElapsedMs = (long)progress.Elapsed.TotalMilliseconds,
                HitFrequency = s.HitFrequency, NonZeroCount = s.NonZeroCount, Volatility = s.StdDev,
                MaxWin = s.Count > 0 && double.IsFinite(s.MaxObserved) ? s.MaxObserved : 0, CapHits = s.CapHits,
                Histogram = (s.AdaptiveHistogram ?? Array.Empty<AdaptiveHistogramBin>())
                    .Select(b => new RunHistogramBin(b.LowerBound, double.IsFinite(b.UpperBound) ? b.UpperBound : null, b.Count)).ToArray(),
            });
            if (message is not null) delivery.Publish(message);
        }
        try
        {
            var run = runStore.Get(runId) ?? throw new InvalidOperationException("Run not found.");
            if (run.Status is "completed" or "cancelled" or "failed") return;
            operation.Token.ThrowIfCancellationRequested();
            var entry = configStore.GetVersion(run.ConfigId, run.ConfigVersion)
                ?? throw new InvalidOperationException("Pinned config version not found.");
            if (run.MeasurementHash != (run.Measurements.Length > 0 ? MeasurementHash.Compute(run.Measurements) : null))
                throw new InvalidOperationException("Pinned measurement plan fingerprint mismatch.");
            var compiled = compiledGraphs.Compile(entry.Config, run.Measurements);
            if (!compiled.IsValid) throw new InvalidOperationException(string.Join("; ", compiled.Errors.Select(e => $"{e.Code}: {e.Message}")));
            operation.Token.ThrowIfCancellationRequested();
            var started = runStore.PublishProgress(runId, InMemoryRunStore.Snapshot(run) with { Status = "running" });
            if (started is not null) delivery.Publish(started);
            var sampled = SampledInterpreter.Evaluate(compiled.Program!, new Dictionary<string, object?>(), new SampledConfig
            {
                Measurements = run.Measurements, Seed = run.Seed, MaxSpins = sampleSize, DegreeOfParallelism = run.DegreeOfParallelism,
                WinScale = (double)compiled.WinScale,
                MaxWinCap = entry.Config.Nodes.OfType<SlotMath.Core.Model.MetricsSinkNode>().Single().WinCap,
                CancellationToken = operation.Token, CancellationCheckInterval = 1,
                ProgressReportInterval = Math.Clamp(progressBatchSize, 16, 1000), ProgressCallback = Publish,
            });
            if (sampled.WasCancelled && sampled.SpinsCompleted == 0) throw new OperationCanceledException(operation.Token);
            // Final statistics use deterministic chunk-order reduction, including
            // partial results. A cancelled run is never reported as completed.
            Publish(new SampledProgress { SpinsCompleted = sampled.SpinsCompleted, TotalSpins = sampleSize,
                Stats = sampled.Stats.Snapshot(), Measurements = sampled.Measurements, Elapsed = sampled.Elapsed });
            var report = SampledMetrics.ComputeFromResult(sampled);
            var status = sampled.WasCancelled || sampled.SpinsCompleted < sampleSize ? "cancelled" : "completed";
            var result = JsonSerializer.Serialize(new
            {
                seed = run.Seed, configHash = run.ConfigHash, configVersion = run.ConfigVersion,
                degreeOfParallelism = run.DegreeOfParallelism, streamScheme = "splitmix64-chunk-65536",
                measurementHash = run.MeasurementHash, measurements = sampled.Measurements,
                samplingEngine = compiled.SamplingEngine,
                rtp = report.Rtp.DisplayValue, runningRtp = report.Rtp.DisplayValue,
                stdErr = sampled.Stats.StdErr, ci95 = sampled.SpinsCompleted > 1 ? new[] { sampled.Stats.Mean - sampled.Stats.Ci95Half, sampled.Stats.Mean + sampled.Stats.Ci95Half } : null,
                hitFrequency = report.HitFrequency.DisplayValue, volatility = sampled.SpinsCompleted > 1 ? report.Volatility.StdDev : (double?)null,
                volatilityIndex = report.Volatility.VolatilityIndex, maxWin = report.MaxWin.MaxWin,
                sampleCount = sampled.SpinsCompleted, totalSamples = sampleSize, status,
                provenance = "Sampled", elapsedMs = (long)sampled.Elapsed.TotalMilliseconds,
                histogram = sampled.Stats.Histogram.Select(b => new { lo = b.LowerBound, hi = b.UpperBound, count = b.Count }),
                adaptiveHistogram = runStore.Get(runId)?.Progress?.Histogram.Select(b => new { lo = b.Lo, hi = b.Hi, count = b.Count }),
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            runStore.Update(runId, status, result);
        }
        catch (OperationCanceledException)
        {
            var run = runStore.Get(runId);
            runStore.Update(runId, "cancelled", JsonSerializer.Serialize(new { sampleCount = run?.SampleCount ?? 0,
                rtp = run?.RunningRtp ?? 0, stdErr = run?.StdErr ?? 0, status = "cancelled" }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {RunId}: failed", runId);
            runStore.Update(runId, "failed", JsonSerializer.Serialize(new { error = ex.Message }));
        }
        finally
        {
            try
            {
                var final = runStore.Get(runId);
                if (final is not null) delivery.Publish(InMemoryRunStore.Snapshot(final));
                await delivery.DisposeAsync();
            }
            finally { runStore.RemoveCancellationToken(runId); }
        }
    }
}
