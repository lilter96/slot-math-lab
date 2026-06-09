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

        group.MapPost("/light", (EvaluateLightRequest request, PluginHost pluginHost) =>
        {
            var sw = Stopwatch.StartNew();

            // 1. Deserialize config
            GraphConfig config;
            try
            {
                config = ConfigsEndpoints.DeserializeConfig(request.Config);
            }
            catch (Exception) when (true)
            {
                return Results.BadRequest(new EvaluateLightResponse
                {
                    Strategy = "Error",
                    Provenance = "InvalidJson",
                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                });
            }

            // 2. Validate
            var compiler = new GraphCompiler(pluginHost);
            var compileResult = compiler.Compile(config);
            if (!compileResult.IsValid)
            {
                return Results.Ok(new EvaluateLightResponse
                {
                    Strategy = "Error",
                    Provenance = "ValidationFailed",
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
                        Budget = new Budget { MaxBranches = maxBranches },
                        ForceSampled = false,
                    };

                    var result = HybridEvaluator.Evaluate(
                        compileResult.Program!,
                        new Dictionary<string, object?>(),
                        state => new BigInteger(state.GetHashCode()),
                        exactConfig);

                    var report = result.Report;
                    return Results.Ok(new EvaluateLightResponse
                    {
                        Strategy = "Exact",
                        Rtp = report.Rtp.DisplayValue,
                        HitFrequency = report.HitFrequency.DisplayValue,
                        Volatility = report.Volatility.VolatilityIndex,
                        Provenance = result.AggregateProvenance.ToString(),
                        ElapsedMs = sw.Elapsed.TotalMilliseconds,
                    });
                }
                catch (BudgetExceededException)
                {
                    // Fall through to sampled
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
                    SampledSeed = DateTimeOffset.UtcNow.Ticks,
                };

                var result = HybridEvaluator.Evaluate(
                    compileResult.Program!,
                    new Dictionary<string, object?>(),
                    state => new BigInteger(state.GetHashCode()),
                    sampledConfig);

                var report = result.Report;
                var rtp = report.Rtp.DisplayValue;
                var stdDev = report.Volatility.StdDev;
                var stdErr = stdDev / Math.Sqrt(sampleSize);
                return Results.Ok(new EvaluateLightResponse
                {
                    Strategy = "Sampled",
                    Rtp = rtp,
                    HitFrequency = report.HitFrequency.DisplayValue,
                    Volatility = report.Volatility.VolatilityIndex,
                    StdErr = stdErr,
                    Ci95 = $"{rtp - 1.96 * stdErr:F4} - {rtp + 1.96 * stdErr:F4}",
                    Provenance = "Sampled",
                    SampleCount = sampleSize,
                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                });
            }
            catch (Exception)
            {
                // 6. Too heavy — needs full run
                return Results.Ok(new EvaluateLightResponse
                {
                    Strategy = "NeedsFullRun",
                    Provenance = "TooExpensive",
                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                });
            }
        });

        return group;
    }
}
