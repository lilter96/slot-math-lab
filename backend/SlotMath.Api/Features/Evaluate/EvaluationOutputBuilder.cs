using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Compiler;
using System.Diagnostics;

namespace SlotMath.Api.Features.Evaluate;
public static class EvaluationOutputBuilder
{
    public static EvaluationOutput Build(GraphEvaluationRequest request, string? actual, RtpMetric rtp,
        bool incomplete, string? method, double half, long samples, SlotMathReport? report, StructuredExactResult? structured,
        CompileResult compiled, Stopwatch? timer, string? hash)
        => new EvaluationOutput
        {
            RequestedMode = request.Mode,
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
            HitFrequency = report?.HitFrequency.DisplayValue ?? structured?.HitFrequency?.ToDouble(),
            RationalHitFrequency =
                            structured?.HitFrequency is { } hits ? $"{hits.Numerator}/{hits.Denominator}" : null,
            Distribution = report is null ? "NotBuilt" : method == "sampling" ? "Sampled" : "Exact",
            StreamScheme = "splitmix64-chunks-4096-v1",
            ChunkSize = 4096,
            SamplingEngine = compiled.SamplingEngine,
            Variance = report?.Volatility.Variance ??
                                   structured?.Variance(compiled.WinScale)?.ToDouble(),
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
            MaxObserved = report?.MaxWin.MaxWin,
            ElapsedMs = timer!.Elapsed.TotalMilliseconds,
            ConfigHash = hash!,
            Note = Note(request.Mode, structured)
        };

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

