using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using SlotMath.Api.Features.Configs;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Model;
using SlotMath.Core.Plugins;

namespace SlotMath.Api.Features.Ai;

// ══════════════════════════════════════════════════════════════════════════════
//  G27 — Auto-tune / inverse design
//
//  POST /api/ai/auto-tune
//  Coordinate/random search over DrawNode weights to converge on a target RTP.
//  Pure optimisation — no AI API key required.
// ══════════════════════════════════════════════════════════════════════════════

public static class AutoTuneEndpoints
{
    public static void MapAutoTune(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai");

        group.MapPost("/auto-tune", async (AutoTuneRequest request, PluginHost pluginHost) =>
        {
            var sw = Stopwatch.StartNew();

            // 1. Deserialise the config
            GraphConfig config;
            try
            {
                config = ConfigsEndpoints.DeserializeConfig(request.Config);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = $"Invalid config JSON: {ex.Message}" });
            }

            // 2. Compile and validate
            var compiler = new GraphCompiler(pluginHost);
            var compileResult = compiler.Compile(config);
            if (!compileResult.IsValid)
            {
                return Results.BadRequest(new
                {
                    error = "Config is invalid: " + string.Join("; ", compileResult.Errors.Select(e => e.Message)),
                });
            }

            // 3. Extract DrawNodes with inline weights (these are tunable)
            var tunableDraws = config.Nodes
                .OfType<DrawNode>()
                .Where(n => n.DrawWeights is { Length: > 0 })
                .ToList();

            if (tunableDraws.Count == 0)
            {
                return Results.BadRequest(new
                {
                    error = "No DrawNodes with inline weights found. The auto-tuner requires at least one DrawNode with DrawWeights.",
                });
            }

            // 4. Coordinate random search
            var targetRtp = request.TargetRtp;
            var maxIter = Math.Min(request.MaxIterations ?? 200, 500); // hard cap
            var rng = new System.Random(42); // fixed seed for reproducibility

            // Working copy of weights (mutable during search)
            var bestConfig = config;
            var bestRtp = EvaluateRtp(bestConfig, pluginHost);
            var bestObjective = Objective(bestRtp, targetRtp, request);

            for (var iter = 0; iter < maxIter; iter++)
            {
                // Pick a random DrawNode and a random weight entry to perturb
                var drawNode = tunableDraws[rng.Next(tunableDraws.Count)];
                var weights = drawNode.DrawWeights!;
                if (weights.Length == 0) continue;

                var idx = rng.Next(weights.Length);
                var entry = weights[idx];

                // Perturb by ±10-20%
                var perturbFactor = 1.0 + (rng.NextDouble() * 0.2 - 0.1);
                var newWeight = Math.Max(1L, (long)System.Math.Round(entry.Weight * perturbFactor));

                // Build candidate config with the perturbed weight
                var candidateWeights = weights.ToArray();
                candidateWeights[idx] = entry with { Weight = newWeight };

                var candidateConfig = ReplaceDrawNodeWeights(bestConfig, drawNode.Id, candidateWeights);

                // Re-compile the candidate
                var candidateCompile = new GraphCompiler(pluginHost).Compile(candidateConfig);
                if (!candidateCompile.IsValid) continue;

                var candidateRtp = EvaluateRtp(candidateConfig, pluginHost);
                var candidateObjective = Objective(candidateRtp, targetRtp, request);

                if (candidateObjective < bestObjective)
                {
                    bestObjective = candidateObjective;
                    bestRtp = candidateRtp;
                    bestConfig = candidateConfig;
                }

                // Early exit if close enough
                if (System.Math.Abs(bestRtp - targetRtp) < 0.001) break;
            }

            await Task.CompletedTask; // async for pipeline consistency

            return Results.Ok(new AutoTuneResponse
            {
                BestConfig = bestConfig,
                AchievedRtp = bestRtp,
                Iterations = maxIter,
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
            });
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluate the RTP of a config using a light sampled run (fast).
    /// Falls back to 0 on any error so the search continues.
    /// </summary>
    private static double EvaluateRtp(GraphConfig config, PluginHost pluginHost)
    {
        try
        {
            var compile = new GraphCompiler(pluginHost).Compile(config);
            if (!compile.IsValid) return 0;

            var regimeConfig = new RegimeConfig
            {
                ForceSampled = true,
                SampledSeed = 42,
                SampledSpins = 5_000,
                WinScale = compile.WinScale,
            };

            var result = HybridEvaluator.Evaluate(
                compile.Program!,
                new Dictionary<string, object?>(),
                SlotMath.Core.Math.StateHasher.CanonicalHash,
                regimeConfig);

            return result.Report.Rtp.DisplayValue;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Objective: |rtp - target| + constraint penalties.</summary>
    private static double Objective(double rtp, double target, AutoTuneRequest req)
    {
        var obj = System.Math.Abs(rtp - target);

        // Volatility and MaxWin penalties are accepted in the request schema
        // for future extension; the light tuner focuses on RTP convergence only.
        _ = req.VolatilityMin;
        _ = req.VolatilityMax;
        _ = req.MaxWinMin;
        _ = req.MaxWinMax;

        return obj;
    }

    /// <summary>
    /// Return a copy of <paramref name="config"/> with the specified DrawNode's
    /// weights replaced.  All other nodes are unchanged.
    /// </summary>
    private static GraphConfig ReplaceDrawNodeWeights(
        GraphConfig config,
        string drawNodeId,
        DrawWeight[] newWeights)
    {
        var newNodes = config.Nodes.Select(n =>
        {
            if (n is DrawNode dn && dn.Id == drawNodeId)
                return dn with { DrawWeights = newWeights };
            return n;
        }).ToArray();

        return config with { Nodes = newNodes };
    }
}

// ── Request / Response DTOs ───────────────────────────────────────────────────

public sealed record AutoTuneRequest
{
    public required object Config { get; init; }
    public double TargetRtp { get; init; } = 0.96;
    public int? MaxIterations { get; init; } = 200;
    public double? VolatilityMin { get; init; }
    public double? VolatilityMax { get; init; }
    public double? MaxWinMin { get; init; }
    public double? MaxWinMax { get; init; }
}

public sealed record AutoTuneResponse
{
    public required GraphConfig BestConfig { get; init; }
    public double AchievedRtp { get; init; }
    public int Iterations { get; init; }
    public double ElapsedMs { get; init; }
}
