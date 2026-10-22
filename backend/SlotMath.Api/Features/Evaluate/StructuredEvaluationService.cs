using SlotMath.Core.Compiler;
using SlotMath.Core.Math.Structured;

namespace SlotMath.Api.Features.Evaluate;

public static class StructuredEvaluationService
{
    public static Task<(StructuredExactResult? Result, string? DeclinedReason)> Structured(CompileResult compiled,
        CancellationToken deadlineToken, CancellationToken requestAbortedToken, GraphEvaluationRequest request) =>
        Structured(compiled, deadlineToken, requestAbortedToken, request.Analysis?.MomentOrder ?? 2,
            EvaluationEvidence.Parse(request.Evidence), request.Diagnostics);

    // A game with too many outcomes to enumerate still has an exact expectation: the structured
    // evaluator computes it from the same compiled graph, or declines and leaves the answer as it was.
    public static async Task<(StructuredExactResult? Result, string? DeclinedReason)> Structured(CompileResult compiled,
        CancellationToken deadlineToken,
        CancellationToken requestAbortedToken,
        int momentOrder = 2, StructuredEvidence evidence = StructuredEvidence.Report, bool trackStorage = false)
    {
        StructuredExactResult result = await Task.Run(() => StructuredExactEvaluator.Evaluate(compiled,
                new StructuredExactOptions
                {
                    MaxTime = TimeSpan.FromSeconds(12),
                    MomentOrder = momentOrder,
                    Evidence = evidence,
                    TrackStorage = trackStorage
                }, deadlineToken),
            requestAbortedToken);

        var declined = result.DeclinedReason;
        StructuredExactResult? rez = result.Succeeded ? result : null;

        return (rez, declined);
    }
}
