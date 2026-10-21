using System.Diagnostics;
using System.Text.Json;
using SlotMath.Api.Features.Configs;
using SlotMath.Api.Features.Evaluate.EvaluationStrategies;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Model;
using static SlotMath.Api.Features.Evaluate.EvaluationConstants;

namespace SlotMath.Api.Features.Evaluate;

public sealed record GraphEvaluationRequest(
    JsonElement Config,
    string Mode = "Sampled",
    long Seed = 42,
    int Samples = 10000,
    int DegreeOfParallelism = 1,
    double Epsilon = 0.00001,
    int MaxBranches = 10000,
    PayoutAnalysisRequest? Analysis = null);

/// <summary>Explicit regimes for any UI-authored graph. Requested mode and actual provenance are separate.</summary>
public static class GraphEvaluationEndpoints
{
    public static void MapGraphEvaluation(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/evaluate/graph",
            async (GraphEvaluationRequest request, CompiledGraphCache compiledGraphs,
                IEnumerable<IEvaluationStrategy> strategies, HttpContext context) =>
            {
                if (!Enum.TryParse(request.Mode, true, out EvaluationMode evaluationMode) ||
                    request.Samples is < MinSamples or > MaxSamples ||
                    request.DegreeOfParallelism is < MinDegreeOfParallelism or > MaxDegreeOfParallelism ||
                    request.MaxBranches is < MinBranches or > MaxBranches ||
                    !double.IsFinite(request.Epsilon) || request.Epsilon is <= MinEpsilon or >= MaxEpsilon ||
                    Math.Abs((double)request.Seed) > MaxAbsoluteSeed ||
                    !PayoutAnalysis.IsValid(request.Analysis))
                {
                    return Results.BadRequest(new
                    {
                        error = "Invalid calculation mode or limits."
                    });
                }

                var timer = Stopwatch.StartNew();
                var hash = CanonicalHash.Compute(request.Config);
                string? declined = null;
                try
                {
                    GraphConfig config = ConfigsEndpoints.DeserializeConfig(request.Config);
                    hash = CanonicalHash.Compute(config);
                    CompileResult compiled = compiledGraphs.Compile(config);

                    if (!compiled.IsValid)
                    {
                        return Results.BadRequest(new
                        {
                            error = "Graph validation failed.",
                            errors = compiled.Errors
                        });
                    }

                    var budget = new Budget
                    {
                        MaxBranches = request.MaxBranches,
                        MaxTime = TimeSpan.FromSeconds(3)
                    };
                    IEvaluationStrategy? strategy = strategies.FirstOrDefault(s => s.CanEvaluate(evaluationMode));

                    if (strategy is null)
                    {
                        return Results.BadRequest(new
                        {
                            error = $"No strategy found for mode {request.Mode}"
                        });
                    }

                    EvaluationOutput output = await strategy.EvaluateAsync(
                        compiled, config, request, budget, hash, timer, context.RequestAborted);

                    return Results.Ok(output);
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
                        note = "Enumerating the full distribution exceeds the branch/time budget" +
                               (declined is null
                                   ? "."
                                   : $", and structured exact evaluation does not apply: {declined}.") +
                               " No exact RTP has been calculated."
                    });
                }
                catch (OperationCanceledException)
                {
                    return Results.Json(new
                    {
                        error = "Calculation cancelled."
                    }, statusCode: 408);
                }
                catch (Exception ex) when (ex is JsonException or ExpressionEvaluationException or NotSupportedException
                                               or InvalidOperationException or ArgumentException or FormatException)
                {
                    return Results.BadRequest(new
                    {
                        error = ex.Message
                    });
                }
            }).Produces<EvaluationOutput>().RequireRateLimiting("compute");
    }
}
