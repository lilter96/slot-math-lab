using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using SlotMath.Api.Features.Configs;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;

namespace SlotMath.Api.Features.Evaluate;

public sealed record GraphEvaluationRequest(
    JsonElement Config,
    string Mode = "Sampled",
    long Seed = 42,
    int Samples = 10000,
    int DegreeOfParallelism = 1,
    double Epsilon = 0.00001,
    int MaxBranches = 10000);

/// <summary>Explicit regimes for any UI-authored graph. Requested mode and actual provenance are separate.</summary>
public static class GraphEvaluationEndpoints
{
    public static void MapGraphEvaluation(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/evaluate/graph",
            async (GraphEvaluationRequest request, CompiledGraphCache compiledGraphs, HttpContext context) =>
            {
                if (request.Mode is not ("Exact" or "Pruned" or "Sampled" or "Hybrid") ||
                    request.Samples is < 1 or > 1_000_000 ||
                    request.DegreeOfParallelism is < 1 or > 4 || request.MaxBranches is < 1 or > 100_000 ||
                    !double.IsFinite(request.Epsilon) || request.Epsilon is <= 0 or >= 1 ||
                    Math.Abs((double)request.Seed) > 9_007_199_254_740_991)
                {
                    return Results.BadRequest(new { error = "Invalid calculation mode or limits." });
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
                        return Results.BadRequest(new { error = "Graph validation failed.", errors = compiled.Errors });
                    }

                    var cap = config.Nodes.OfType<MetricsSinkNode>().Single().WinCap;
                    var budget = new Budget { MaxBranches = request.MaxBranches, MaxTime = TimeSpan.FromSeconds(3) };
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                    deadline.CancelAfter(TimeSpan.FromSeconds(45));
                    SlotMathReport? report = null;
                    StructuredExactResult? structured = null;
                    var actual = request.Mode;
                    var method = "enumeration";
                    long samples = 0;
                    var incomplete = false;

                    // A game with too many outcomes to enumerate still has an exact expectation: the structured
                    // evaluator computes it from the same compiled graph, or declines and leaves the answer as it was.
                    async Task<StructuredExactResult?> Structured()
                    {
                        StructuredExactResult result = await Task.Run(() => StructuredExactEvaluator.Evaluate(compiled,
                                new StructuredExactOptions { MaxTime = TimeSpan.FromSeconds(12) }, deadline.Token),
                            context.RequestAborted);
                        declined = result.DeclinedReason;
                        return result.Succeeded ? result : null;
                    }

                    if (request.Mode is "Exact" or "Pruned")
                    {
                        var epsilon = new Rational((long)Math.Round(request.Epsilon * 1_000_000_000_000),
                            1_000_000_000_000);
                        try
                        {
                            ExactResult<Dictionary<string, object?>, BigInteger> result = await Task.Run(() =>
                                ExactInterpreter.Evaluate(compiled.Program!, new Dictionary<string, object?>(),
                                    StateHasher.CanonicalHash,
                                    new ExactConfig
                                    {
                                        Budget = budget,
                                        EpsilonNumerator = request.Mode == "Pruned" ? epsilon.Numerator : 0,
                                        EpsilonDenominator = epsilon.Denominator
                                    }), context.RequestAborted);
                            report = ExactMetrics.Compute(result.ValueDistribution(), cap, winScale: compiled.WinScale);
                        }
                        catch (BudgetExceededException)
                        {
                            structured = await Structured();
                            if (structured is null)
                            {
                                throw;
                            }
                        }
                    }
                    else if (request.Mode == "Hybrid")
                    {
                        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(compiled.Program!);
                        if (!analysis.ContainsPlugin && analysis.EstimatedBranches > request.MaxBranches)
                        {
                            structured = await Structured();
                        }

                        if (structured is null)
                        {
                            RegimeResult result = await Task.Run(() => HybridEvaluator.Evaluate(compiled.Program!,
                                new Dictionary<string, object?>(), StateHasher.CanonicalHash,
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
                            if (result.BudgetExceeded && !analysis.ContainsPlugin)
                            {
                                structured = await Structured();
                            }

                            if (structured is null)
                            {
                                report = result.Report;
                                actual = result.OverallStrategy.ToString();
                                samples = report.Rtp.SampleCount ?? 0;
                                incomplete = actual == "Sampled" && samples < request.Samples;
                                if (actual == "Sampled")
                                {
                                    method = "sampling";
                                }
                            }
                        }
                    }
                    else
                    {
                        SampledResult<Dictionary<string, object?>> result = await Task.Run(() =>
                            SampledInterpreter.Evaluate(compiled.Program!, new Dictionary<string, object?>(),
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
                        method = "sampling";
                    }

                    if (structured is not null)
                    {
                        method = "structured";
                        // Small enough to build: the full exact distribution, and every metric with it.
                        if (structured.Distribution is { } distribution)
                        {
                            report = ExactMetrics.Compute(distribution, cap, winScale: compiled.WinScale);
                        }
                    }

                    // A loop cut short of its cap makes every figure an enclosure, whatever it was computed from.
                    RtpMetric rtp = structured is { PrunedMass.IsZero: false }
                        ? structured.Rtp(compiled.WinScale)
                        : report?.Rtp ?? structured!.Rtp(compiled.WinScale);
                    if (request.Mode != "Sampled" && method != "sampling")
                    {
                        actual = rtp.Provenance.Provenance.ToString();
                    }

                    if (actual == "Sampled" && samples == 0)
                    {
                        return Results.Json(new { error = "No samples completed before cancellation." },
                            statusCode: 408);
                    }

                    var half = rtp.Ci95Half ?? 0;
                    return Results.Ok(new
                    {
                        requestedMode = request.Mode,
                        actualStrategy = actual,
                        provenance = rtp.Provenance.Provenance.ToString(),
                        status = incomplete ? "Partial" : "Complete",
                        method,
                        rtp = rtp.DisplayValue,
                        lower = rtp.LoDisplay ?? rtp.DisplayValue - half,
                        upper = rtp.HiDisplay ?? rtp.DisplayValue + half,
                        rationalRtp =
                            rtp.RationalNumerator.HasValue
                                ? $"{rtp.RationalNumerator}/{rtp.RationalDenominator}"
                                : null,
                        prunedMass = rtp.PrunedMass ?? 0,
                        samples,
                        requestedSamples = request.Samples,
                        seed = request.Seed,
                        degreeOfParallelism = request.DegreeOfParallelism,
                        hitFrequency = report?.HitFrequency.DisplayValue ?? structured?.HitFrequency?.ToDouble(),
                        rationalHitFrequency =
                            structured?.HitFrequency is { } hits ? $"{hits.Numerator}/{hits.Denominator}" : null,
                        distribution = report is null ? "NotBuilt" : method == "sampling" ? "Sampled" : "Exact",
                        streamScheme = "splitmix64-chunks-4096-v1",
                        chunkSize = 4096,
                        samplingEngine = compiled.SamplingEngine,
                        variance = report?.Volatility.Variance ??
                                   structured?.Variance(compiled.WinScale)?.ToDouble(),
                        loops = structured?.Loops.Select(loop => new
                        {
                            loopId = loop.LoopId,
                            evaluation = loop.Evaluation,
                            iterationCap = loop.IterationCap,
                            runs = loop.Runs,
                            states = loop.States,
                            expectedCapHits = loop.ExpectedCapHits?.ToDouble(),
                            prunedMass = loop.PrunedMass.ToDouble()
                        }),
                        maxObserved = report?.MaxWin.MaxWin,
                        elapsedMs = timer.Elapsed.TotalMilliseconds,
                        configHash = hash,
                        note = Note(request.Mode, structured)
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
                        note = "Enumerating the full distribution exceeds the branch/time budget" +
                               (declined is null
                                   ? "."
                                   : $", and structured exact evaluation does not apply: {declined}.") +
                               " No exact RTP has been calculated."
                    });
                }
                catch (OperationCanceledException)
                {
                    return Results.Json(new { error = "Calculation cancelled." }, statusCode: 408);
                }
                catch (Exception ex) when (ex is JsonException or ExpressionEvaluationException or NotSupportedException
                                               or InvalidOperationException or ArgumentException or FormatException)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            }).RequireRateLimiting("compute");
    }

    private static string? Note(string mode, StructuredExactResult? structured)
    {
        var selection = mode == "Hybrid"
            ? "Automatic Exact/Sampled selection; no mixed exact/sample estimator is claimed."
            : null;
        if (structured is null)
        {
            return selection;
        }

        var exact = structured.Distribution is not null
            ? "Outcomes are too many to enumerate. The exact payout distribution was computed from the graph's structure: independent draws are factored, sub-models with one law are computed once."
            : "Outcomes are too many to enumerate. The exact rational RTP was computed from the graph's structure by linearity of expectation over independent sub-models. " +
              (structured.HitFrequency is null
                  ? ""
                  : "The hit frequency is exact as well, as the probability that some part of the payout is positive. ") +
              (structured.SecondMoment is null
                  ? ""
                  : "The variance is exact as well, from the second moment of the payout by bilinearity over its parts. ") +
              "The full payout distribution is too large to build, so " +
              string.Join(", ",
                  new[]
                  {
                      structured.HitFrequency is null ? "hit frequency" : null,
                      structured.SecondMoment is null ? "variance" : null, "maximum win"
                  }.Where(metric => metric is not null)) + " are not reported; use Sampled for them.";
        if (structured.Loops.Any(loop => loop.Evaluation != "unrolled"))
        {
            exact +=
                " Loops with long iteration caps were solved as Markov reward chains over their state, not unrolled.";
        }

        if (!structured.PrunedMass.IsZero)
        {
            exact +=
                $" A loop was cut short of its iteration cap where the probability that it is still running is below {structured.PrunedMass.ToDouble():E2}: the RTP is an enclosure of width at most that mass times the win cap, and every other figure is that of the game with those rounds ended early.";
        }

        return selection is null ? exact : selection + " " + exact;
    }
}
