using System.Diagnostics;
using System.Text.Json;
using SlotMath.Api.Features.Configs;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Model;

namespace SlotMath.Api.Features.Evaluate;

public sealed record GraphEvaluationRequest(JsonElement Config, string Mode = "Sampled", long Seed = 42,
    int Samples = 10000, int DegreeOfParallelism = 1, double Epsilon = 0.00001, int MaxBranches = 10000);

/// <summary>Explicit regimes for any UI-authored graph. Requested mode and actual provenance are separate.</summary>
public static class GraphEvaluationEndpoints
{
    public static void MapGraphEvaluation(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/evaluate/graph", async (GraphEvaluationRequest request, CompiledGraphCache compiledGraphs, HttpContext context) =>
        {
            if (request.Mode is not ("Exact" or "Pruned" or "Sampled" or "Hybrid") || request.Samples is < 1 or > 1_000_000 ||
                request.DegreeOfParallelism is < 1 or > 4 || request.MaxBranches is < 1 or > 100_000 ||
                !double.IsFinite(request.Epsilon) || request.Epsilon is <= 0 or >= 1 || Math.Abs((double)request.Seed) > 9_007_199_254_740_991)
                return Results.BadRequest(new { error = "Invalid calculation mode or limits." });
            var timer = Stopwatch.StartNew();
            var hash = CanonicalHash.Compute(request.Config);
            try
            {
                var config = ConfigsEndpoints.DeserializeConfig(request.Config);
                hash = CanonicalHash.Compute(config);
                var compiled = compiledGraphs.Compile(config);
                if (!compiled.IsValid) return Results.BadRequest(new { error = "Graph validation failed.", errors = compiled.Errors });
                var cap = config.Nodes.OfType<MetricsSinkNode>().Single().WinCap;
                var budget = new Budget { MaxBranches = request.MaxBranches, MaxTime = TimeSpan.FromSeconds(3) };
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                deadline.CancelAfter(TimeSpan.FromSeconds(45));
                SlotMathReport report;
                var actual = request.Mode;
                long samples = 0;
                bool incomplete = false;
                if (request.Mode is "Exact" or "Pruned")
                {
                    var epsilon = new Rational((long)Math.Round(request.Epsilon * 1_000_000_000_000), 1_000_000_000_000);
                    var result = await Task.Run(() => ExactInterpreter.Evaluate(compiled.Program!, new Dictionary<string, object?>(),
                        StateHasher.CanonicalHash, new ExactConfig
                        {
                            Budget = budget,
                            EpsilonNumerator = request.Mode == "Pruned" ? epsilon.Numerator : 0,
                            EpsilonDenominator = epsilon.Denominator
                        }), context.RequestAborted);
                    report = ExactMetrics.Compute(result.ValueDistribution(), cap, winScale: compiled.WinScale);
                    actual = report.Rtp.Provenance.Provenance.ToString();
                }
                else if (request.Mode == "Hybrid")
                {
                    var result = await Task.Run(() => HybridEvaluator.Evaluate(compiled.Program!, new Dictionary<string, object?>(), StateHasher.CanonicalHash,
                        new RegimeConfig
                        {
                            Budget = budget,
                            SampledSeed = request.Seed,
                            SampledSpins = request.Samples,
                            ChunkSize = 4096,
                            DegreeOfParallelism = request.DegreeOfParallelism,
                            MaxWinCap = cap,
                            WinScale = compiled.WinScale,
                            CancellationToken = deadline.Token,
                            ProgressCallback = _ => { }
                        }), context.RequestAborted);
                    report = result.Report;
                    actual = result.OverallStrategy.ToString();
                    samples = report.Rtp.SampleCount ?? 0;
                    incomplete = actual == "Sampled" && samples < request.Samples;
                }
                else
                {
                    var result = await Task.Run(() => SampledInterpreter.Evaluate(compiled.Program!, new Dictionary<string, object?>(),
                        new SampledConfig
                        {
                            Seed = request.Seed,
                            MaxSpins = request.Samples,
                            DegreeOfParallelism = request.DegreeOfParallelism,
                            ChunkSize = 4096,
                            MaxWinCap = cap,
                            WinScale = (double)compiled.WinScale,
                            CancellationToken = deadline.Token,
                            CancellationCheckInterval = 1
                        }), context.RequestAborted);
                    report = SampledMetrics.ComputeFromResult(result);
                    samples = result.SpinsCompleted;
                    incomplete = result.WasCancelled;
                }
                var rtp = report.Rtp;
                if (actual == "Sampled" && samples == 0) return Results.Json(new { error = "No samples completed before cancellation." }, statusCode: 408);
                var half = rtp.Ci95Half ?? 0;
                return Results.Ok(new
                {
                    requestedMode = request.Mode,
                    actualStrategy = actual,
                    provenance = rtp.Provenance.Provenance.ToString(),
                    status = incomplete ? "Partial" : "Complete",
                    rtp = rtp.DisplayValue,
                    lower = rtp.LoDisplay ?? rtp.DisplayValue - half,
                    upper = rtp.HiDisplay ?? rtp.DisplayValue + half,
                    rationalRtp = rtp.RationalNumerator.HasValue ? $"{rtp.RationalNumerator}/{rtp.RationalDenominator}" : null,
                    prunedMass = rtp.PrunedMass ?? 0,
                    samples,
                    requestedSamples = request.Samples,
                    seed = request.Seed,
                    degreeOfParallelism = request.DegreeOfParallelism,
                    hitFrequency = report.HitFrequency.DisplayValue,
                    streamScheme = "splitmix64-chunks-4096-v1",
                    chunkSize = 4096,
                    samplingEngine = compiled.SamplingEngine,
                    variance = report.Volatility.Variance,
                    maxObserved = report.MaxWin.MaxWin,
                    elapsedMs = timer.Elapsed.TotalMilliseconds,
                    configHash = hash,
                    note = request.Mode == "Hybrid" ? "Automatic Exact/Sampled selection; no mixed exact/sample estimator is claimed." : null
                });
            }
            catch (BudgetExceededException)
            {
                return Results.Ok(new
                {
                    requestedMode = request.Mode,
                    actualStrategy = "Unavailable",
                    provenance = "BudgetExceeded",
                    status = "BudgetExceeded",
                    configHash = hash,
                    elapsedMs = timer.Elapsed.TotalMilliseconds,
                    note = "Full distribution exceeds the branch/time budget. No exact RTP has been calculated."
                });
            }
            catch (OperationCanceledException)
            {
                return Results.Json(new { error = "Calculation cancelled." }, statusCode: 408);
            }
            catch (Exception ex) when (ex is JsonException or ExpressionEvaluationException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireRateLimiting("compute");
    }
}
