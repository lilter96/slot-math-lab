using System.Numerics;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Plugins;

namespace SlotMath.Api.Features.Runs;

/// <summary>
/// Hangfire job that executes a heavy evaluation run with progress streaming.
///
/// Runs the full Monte Carlo evaluation via <see cref="HybridEvaluator"/>,
/// pushing batched progress updates to connected SignalR clients via
/// <see cref="IHubContext{RunHub}"/>.  Supports cancellation via a
/// <see cref="CancellationTokenSource"/> stored in the run store.
///
/// Automatic retries are disabled — math jobs are deterministic given a seed
/// and a failed run should be resubmitted explicitly.
/// </summary>
public class RunJobService
{
    private readonly InMemoryConfigStore _configStore;
    private readonly InMemoryRunStore _runStore;
    private readonly PluginHost _pluginHost;
    private readonly IHubContext<RunHub> _hubContext;
    private readonly ILogger<RunJobService> _logger;

    public RunJobService(
        InMemoryConfigStore configStore,
        InMemoryRunStore runStore,
        PluginHost pluginHost,
        IHubContext<RunHub> hubContext,
        ILogger<RunJobService> logger)
    {
        _configStore = configStore;
        _runStore = runStore;
        _pluginHost = pluginHost;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Execute a full evaluation run.  Called by Hangfire.
    ///
    /// Note: does not take a CancellationToken parameter because Hangfire
    /// cannot serialize it.  Cancellation is handled via the CTS stored in
    /// the run store (created here, cancelled by the DELETE endpoint).
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteRunAsync(
        string runId,
        int sampleSize,
        int progressBatchSize)
    {
        _logger.LogInformation("Run {RunId}: starting ({SampleSize} spins, batch={Batch})",
            runId, sampleSize, progressBatchSize);

        // ── Get or create run-scoped cancellation token ──────────────────
        // (may have been pre-created by the POST endpoint for early cancel support)
        var runCts = _runStore.CreateCancellationToken(runId);

        try
        {
            // ── Lookup config ──────────────────────────────────────────
            var run = _runStore.Get(runId);
            if (run is null)
            {
                _logger.LogError("Run {RunId}: run entry not found", runId);
                return;
            }

            var configEntry = _configStore.GetLatest(run.ConfigId);
            if (configEntry is null)
            {
                _runStore.Update(runId, "failed",
                    JsonSerializer.Serialize(new { error = $"Config '{run.ConfigId}' not found." }));
                await PushProgress(runId, 0, sampleSize, 0, 0, 0, "failed");
                return;
            }

            // ── Compile ────────────────────────────────────────────────
            var compiler = new GraphCompiler(_pluginHost);
            var compileResult = compiler.Compile(configEntry.Config);

            if (!compileResult.IsValid)
            {
                var errorJson = JsonSerializer.Serialize(new
                {
                    error = "Validation failed",
                    details = compileResult.Errors.Select(e => new { e.Code, e.Message, e.NodeId })
                });
                _runStore.Update(runId, "failed", errorJson);
                await PushProgress(runId, 0, sampleSize, 0, 0, 0, "failed");
                return;
            }

            // ── Evaluate ───────────────────────────────────────────────
            var regimeConfig = new RegimeConfig
            {
                SampledSpins = sampleSize,
                ForceSampled = true,
                SampledSeed = DateTimeOffset.UtcNow.Ticks,
                CancellationToken = runCts.Token,
                // Heavy runs use every core; per-seed determinism is
                // unaffected (fixed logical stream count).
                DegreeOfParallelism = Environment.ProcessorCount,
                WinScale = compileResult.WinScale,
                ProgressReportInterval = progressBatchSize,
                ProgressCallback = progress =>
                {
                    // Synchronous: update the store immediately.
                    _runStore.UpdateProgress(runId,
                        progress.SpinsCompleted, progress.TotalSpins,
                        progress.Stats.Mean, progress.Stats.StdErr,
                        (long)progress.Elapsed.TotalMilliseconds);

                    // Fire-and-forget push to SignalR clients.
                    // IHubContext is a singleton — this is safe and will complete
                    // independently of the spin loop.
                    var msg = new RunProgressMessage
                    {
                        RunId = runId,
                        SampleCount = progress.SpinsCompleted,
                        TotalSamples = progress.TotalSpins,
                        RunningRtp = progress.Stats.Mean,
                        StdErr = progress.Stats.StdErr,
                        Status = "running",
                        ElapsedMs = (long)progress.Elapsed.TotalMilliseconds,
                    };
                    _ = _hubContext.Clients.Group(runId).SendAsync("ProgressUpdate", msg);
                },
            };

            var result = HybridEvaluator.Evaluate(
                compileResult.Program!,
                new Dictionary<string, object?>(),
                SlotMath.Core.Math.StateHasher.CanonicalHash,
                regimeConfig);

            var report = result.Report;

            // ── Persist result ──────────────────────────────────────────
            var resultJson = JsonSerializer.Serialize(new
            {
                rtp = report.Rtp.DisplayValue,
                hitFrequency = report.HitFrequency.DisplayValue,
                volatility = report.Volatility.StdDev,
                volatilityIndex = report.Volatility.VolatilityIndex,
                maxWin = report.MaxWin.MaxWin,
                sampleCount = sampleSize,
                provenance = result.AggregateProvenance.ToString(),
                elapsedMs = (long)(DateTimeOffset.UtcNow - run.CreatedAt).TotalMilliseconds,
                // Real per-spin win histogram (in credits) for the UI.
                histogram = report.Histogram.Bins
                    .Select(b => new { lo = b.LowerBound, hi = b.UpperBound, count = b.Count })
                    .ToArray(),
            });

            _runStore.Update(runId, "completed", resultJson);

            await PushProgress(runId, sampleSize, sampleSize,
                report.Rtp.DisplayValue, report.Volatility.StdDev / Math.Sqrt(sampleSize),
                (long)(DateTimeOffset.UtcNow - run.CreatedAt).TotalMilliseconds,
                "completed");

            _logger.LogInformation("Run {RunId}: completed. RTP={Rtp:F4}",
                runId, report.Rtp.DisplayValue);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Run {RunId}: cancelled", runId);

            // Persist partial stats from the run store progress fields
            var run = _runStore.Get(runId);
            var partialJson = run?.ResultJson ?? JsonSerializer.Serialize(new
            {
                sampleCount = run?.SampleCount ?? 0,
                runningRtp = run?.RunningRtp ?? 0,
                stdErr = run?.StdErr ?? 0,
                status = "cancelled",
            });

            _runStore.Update(runId, "cancelled", partialJson);

            await PushProgress(runId,
                run?.SampleCount ?? 0, sampleSize,
                run?.RunningRtp ?? 0, run?.StdErr ?? 0,
                run?.ElapsedMs ?? 0,
                "cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run {RunId}: failed", runId);

            _runStore.Update(runId, "failed",
                JsonSerializer.Serialize(new { error = ex.Message }));

            await PushProgress(runId, 0, sampleSize, 0, 0, 0, "failed");
        }
        finally
        {
            _runStore.RemoveCancellationToken(runId);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private async Task PushProgress(
        string runId, long sampleCount, long totalSamples,
        double runningRtp, double stdErr, long elapsedMs, string status)
    {
        try
        {
            await _hubContext.Clients.Group(runId).SendAsync("ProgressUpdate",
                new RunProgressMessage
                {
                    RunId = runId,
                    SampleCount = sampleCount,
                    TotalSamples = totalSamples,
                    RunningRtp = runningRtp,
                    StdErr = stdErr,
                    Status = status,
                    ElapsedMs = elapsedMs,
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Run {RunId}: failed to push final progress", runId);
        }
    }
}
