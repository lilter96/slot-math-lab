using System.Diagnostics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;

namespace SlotMath.Api.Features.Evaluate;

public static class EvaluationOutputBuilder
{
    public static EvaluationOutput Build(GraphEvaluationRequest request, string? actual, RtpMetric rtp,
        bool incomplete, string? method, double half, long samples, SlotMathReport? report,
        StructuredExactResult? structured,
        CompileResult compiled, Stopwatch? timer, string? hash, PayoutAnalysisOutput? analysis = null)
        => new()
        {
            RequestedMode = request.Mode,
            RequestedEvidence = request.Evidence,
            ActualStrategy = actual,
            Provenance = rtp.Provenance.Provenance.ToString(),
            Status = incomplete ? "Partial" : "Complete",
            Method = method,
            Rtp = rtp.DisplayValue,
            Lower = rtp.LoDisplay ?? rtp.DisplayValue - half,
            Upper = rtp.HiDisplay ?? rtp.DisplayValue + half,
            RationalRtp =
                rtp.RationalNumerator.HasValue
                    ? $"{rtp.RationalNumerator}/{rtp.RationalDenominator}"
                    : null,
            PrunedMass = rtp.PrunedMass ?? 0,
            Samples = samples,
            RequestedSamples = request.Samples,
            Seed = request.Seed,
            DegreeOfParallelism = request.DegreeOfParallelism,
            HitFrequency = request.Evidence == "Mean"
                ? null
                : report?.HitFrequency.DisplayValue ?? structured?.HitFrequency?.ToDouble(),
            RationalHitFrequency =
                request.Evidence != "Mean" && structured?.HitFrequency is { } hits
                    ? $"{hits.Numerator}/{hits.Denominator}"
                    : null,
            Distribution = request.Evidence != "Report" ? "NotRequested" :
                report is null ? "NotBuilt" :
                method == "sampling" ? "Sampled" : "Exact",
            StreamScheme = "splitmix64-chunks-4096-v1",
            ChunkSize = 4096,
            SamplingEngine = compiled.SamplingEngine,
            Variance = request.Evidence == "Mean"
                ? null
                : report?.Volatility.Variance ??
                  structured?.Variance(compiled.WinScale)?.ToDouble(),
            Analysis = request.Evidence == "Moments" && analysis?.Status == "ExactLaw"
                ? analysis with
                {
                    Status = "ExactMoments",
                    Tails = null,
                    Thresholds = null
                }
                : analysis,
            Solver = request.Diagnostics && structured is not null
                ? new SolverDiagnosticsOutput(structured.RequestedSolverPlan.ToString(), structured.Work,
                    structured.Attempts.Select(attempt => new SolverAttemptOutput(attempt.LoopMode, attempt.Succeeded,
                            attempt.DeclinedReason, attempt.Work, attempt.Elapsed.TotalMilliseconds,
                            attempt.PayoutLawRequested, attempt.PayoutLawNeededForSettlement,
                            attempt.PayoutLawAttempted))
                        .ToArray(),
                    structured.Diagnostics, structured.Storage?.Counters.Select(counter =>
                        new StorageCounterOutput(counter.Kind.ToString(), counter.Current, counter.Peak)).ToArray())
                : null,
            Loops = structured?.Loops.Select(loop => new LoopEvaluationOutput
            {
                LoopId = loop.LoopId,
                Evaluation = loop.Evaluation,
                IterationCap = loop.IterationCap,
                Runs = loop.Runs,
                States = loop.States,
                ExpectedCapHits = loop.ExpectedCapHits?.ToDouble(),
                PrunedMass = loop.PrunedMass.ToDouble()
            }),
            MaxObserved = request.Evidence == "Report" ? report?.MaxWin.MaxWin : null,
            ElapsedMs = timer!.Elapsed.TotalMilliseconds,
            ConfigHash = hash!,
            Note = Note(request, structured)
        };

    private static string? Note(GraphEvaluationRequest request, StructuredExactResult? structured)
    {
        var selection = request.Mode == "Hybrid"
            ? "Automatic Exact/Sampled selection; no mixed exact/sample estimator is claimed."
            : null;
        if (structured is null)
        {
            return request.Evidence == "Report"
                ? selection
                : "Only the requested population evidence is returned. A complete payout law may still be needed internally to apply settlement caps or preserve exactness. " +
                  selection;
        }

        var exact = "The requested evidence was calculated from the compiled graph using exact rational arithmetic. ";
        exact += request.Evidence != "Report"
            ? "The complete payout law was not requested. Settlement caps or exactness obligations may still require an internal law. Missing moments or hit frequency remain unavailable."
            : structured.Distribution is not null
                ? "A complete payout law is retained."
                : "A complete payout law could not be retained within the applicable limits. Tail quantiles and maximum payout are unavailable; reported moments and hit frequency carry their own evidence.";
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
