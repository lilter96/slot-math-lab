using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using SlotMath.Api.Features.Configs;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Model;
using SlotMath.Core.Plugins;

namespace SlotMath.Api.Features.Evaluate;

/// <summary>
/// POST /evaluate/light — fast regime-aware evaluation.
/// Returns exact when cheap, small-N sampled estimate with CI, or needsFullRun.
/// </summary>
public static class EvaluateEndpoints
{
    public static RouteGroupBuilder MapEvaluate(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/evaluate");

        group.MapPost("/light", async (EvaluateLightRequest request, PluginHost pluginHost, IResultCache cache, IWebHostEnvironment environment, HttpContext context) =>
        {
            if (request.SampleSize is <= 0 or > 100_000 || request.MaxBranches is <= 0 or > 100_000)
                return Results.BadRequest(new { error = "Light evaluation limits must be 1..100000." });
            var sw = Stopwatch.StartNew();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));

            // 0. Cache lookup — the debounced UI re-sends identical configs
            //    constantly; identical (config, knobs) returns the cached
            //    response without recompiling or re-evaluating.
            var cacheKey = "light:" + CanonicalHash.Compute(new
            {
                config = request.Config,
                seed = request.Seed,
                maxBranches = request.MaxBranches,
                sampleSize = request.SampleSize,
            });
            var cached = await cache.GetAsync(cacheKey);
            if (cached is not null)
            {
                var hit = JsonSerializer.Deserialize<EvaluateLightResponse>(cached);
                if (hit is not null)
                    return Results.Ok(hit with { ElapsedMs = sw.Elapsed.TotalMilliseconds });
            }

            async Task<IResult> CacheAndReturnAsync(EvaluateLightResponse response)
            {
                await cache.SetAsync(cacheKey, JsonSerializer.Serialize(response with { Seed = request.Seed }),
                    TimeSpan.FromMinutes(2));
                return Results.Ok(response with { Seed = request.Seed });
            }

            // 1. Deserialize config
            GraphConfig config;
            try
            {
                config = ConfigsEndpoints.DeserializeConfig(request.Config);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                return Results.BadRequest(new EvaluateLightResponse
                {
                    Strategy = "Error",
                    Provenance = "InvalidJson",
                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                });
            }

            if (environment.IsProduction() && config.Nodes.OfType<SlotMath.Core.Model.MapNode>().Any(n => n.TransformId?.StartsWith("plugin:") == true))
                return Results.BadRequest(new { error = "Plugin execution is disabled in production." });
            // 2. Validate
            var compiler = new GraphCompiler(pluginHost, allowPlugins: !environment.IsProduction());
            var compileResult = compiler.Compile(config);
            if (!compileResult.IsValid)
            {
                return Results.Ok(new EvaluateLightResponse
                {
                    Strategy = "Error",
                    Provenance = "ValidationFailed",
                    Errors = compileResult.Errors.Select(e => new ValidateErrorItem { Code = e.Code, Message = e.Message, NodeId = e.NodeId, EdgeId = e.EdgeId }).ToArray(),
                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                });
            }

            // 3. Analyze the program
            var analysis = ProgramAnalyzer.Analyze(compileResult.Program!);
            var maxBranches = request.MaxBranches ?? 10_000;
            var containsPlugin = analysis.ContainsPlugin;

            // 4. Try exact first if within budget
            if (!containsPlugin && analysis.EstimatedBranches <= maxBranches)
            {
                try
                {
                    var exactConfig = new RegimeConfig
                    {
                        Budget = new Budget { MaxBranches = maxBranches, MaxTime = TimeSpan.FromSeconds(1) },
                        ForceSampled = false,
                        CancellationToken = deadline.Token, ProgressCallback = _ => { },
                        // If the exact attempt blows the budget mid-flight,
                        // the hybrid evaluator falls back internally — that
                        // fallback must use the light sample size, not the
                        // heavy-run default.
                        SampledSpins = request.SampleSize ?? 10_000,
                        WinScale = compileResult.WinScale,
                    MaxWinCap = config.Nodes.OfType<SlotMath.Core.Model.MetricsSinkNode>().Single().WinCap,
                        SampledSeed = request.Seed,
                    };

                    var result = HybridEvaluator.Evaluate(
                        compileResult.Program!,
                        new Dictionary<string, object?>(),
                        SlotMath.Core.Math.StateHasher.CanonicalHash,
                        exactConfig);

                    var report = result.Report;
                    // The hybrid evaluator may have fallen back to sampled —
                    // the reported strategy must reflect what actually ran.
                    var ranExact = result.OverallStrategy == EvaluationStrategy.Exact;
                    return await CacheAndReturnAsync(new EvaluateLightResponse
                    {
                        Strategy = ranExact ? "Exact" : "Sampled",
                        Rtp = report.Rtp.DisplayValue,
                        Lo = report.Rtp.LoDisplay, Hi = report.Rtp.HiDisplay, PrunedMass = report.Rtp.PrunedMass,
                        HitFrequency = report.HitFrequency.DisplayValue,
                        Volatility = report.Volatility.VolatilityIndex,
                        SampleCount = (int?)report.Rtp.SampleCount,
                        Provenance = result.AggregateProvenance.ToString(),
                        ElapsedMs = sw.Elapsed.TotalMilliseconds,
                    });
                }
                catch (BudgetExceededException)
                {
                    // Fall through to sampled
                }
                catch (SlotMath.Core.Expressions.ExpressionEvaluationException error)
                {
                    return Results.BadRequest(new EvaluateLightResponse { Strategy = "Error", Provenance = "EvaluationFailed",
                        Errors = [new ValidateErrorItem { Code = error.Code, Message = error.Message }] });
                }
            }

            // 5. Small-N sampled estimate
            try
            {
                var sampleSize = request.SampleSize ?? 10_000;
                var sampledConfig = new RegimeConfig
                {
                    SampledSpins = sampleSize,
                    ForceSampled = true,
                    CancellationToken = deadline.Token, ProgressCallback = _ => { },
                    SampledSeed = request.Seed,
                    WinScale = compileResult.WinScale,
                    MaxWinCap = config.Nodes.OfType<SlotMath.Core.Model.MetricsSinkNode>().Single().WinCap,
                };

                var result = HybridEvaluator.Evaluate(
                    compileResult.Program!,
                    new Dictionary<string, object?>(),
                    SlotMath.Core.Math.StateHasher.CanonicalHash,
                    sampledConfig);

                var report = result.Report;
                var rtp = report.Rtp.DisplayValue;
                var stdDev = report.Volatility.StdDev;
                var completed = report.Rtp.SampleCount ?? sampleSize;
                var stdErr = completed > 0 ? stdDev / Math.Sqrt(completed) : 0;
                return await CacheAndReturnAsync(new EvaluateLightResponse
                {
                    Strategy = "Sampled",
                    Rtp = rtp,
                    HitFrequency = report.HitFrequency.DisplayValue,
                    Volatility = report.Volatility.VolatilityIndex,
                    StdErr = stdErr,
                    Ci95 = $"{rtp - 1.96 * stdErr:F4} - {rtp + 1.96 * stdErr:F4}",
                    Provenance = "Sampled",
                    SampleCount = (int)completed,
                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                });
            }
            catch (SlotMath.Core.Expressions.ExpressionEvaluationException error)
            {
                return Results.BadRequest(new EvaluateLightResponse { Strategy = "Error", Provenance = "EvaluationFailed",
                    Errors = [new ValidateErrorItem { Code = error.Code, Message = error.Message }] });
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or FormatException)
            {
                return Results.BadRequest(new EvaluateLightResponse { Strategy = "Error", Provenance = "EvaluationFailed",
                    Errors = [new ValidateErrorItem { Code = "EVALUATION_FAILED", Message = error.Message }] });
            }
            catch (BudgetExceededException)
            {
                // Too heavy — needs full run
                return Results.Ok(new EvaluateLightResponse
                {
                    Strategy = "NeedsFullRun",
                    Provenance = "TooExpensive",
                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                });
            }
        }).Produces<EvaluateLightResponse>();

        return group;
    }
}
