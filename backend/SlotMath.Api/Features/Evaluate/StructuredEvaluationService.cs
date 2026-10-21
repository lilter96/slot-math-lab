using SlotMath.Core.Compiler;
using SlotMath.Core.Math.Structured;

namespace SlotMath.Api.Features.Evaluate;

public static class StructuredEvaluationService
{
    // A game with too many outcomes to enumerate still has an exact expectation: the structured
    // evaluator computes it from the same compiled graph, or declines and leaves the answer as it was.
    public static async Task<(StructuredExactResult? Result, string? DeclinedReason)> Structured(CompileResult compiled,
        CancellationToken deadlineToken,
        CancellationToken requestAbortedToken,
        int momentOrder = 2)
    {
        StructuredExactResult result = await Task.Run(() => StructuredExactEvaluator.Evaluate(compiled,
                new StructuredExactOptions
                {
                    MaxTime = TimeSpan.FromSeconds(12),
                    MomentOrder = momentOrder
                }, deadlineToken),
            requestAbortedToken);

        var declined = result.DeclinedReason;
        StructuredExactResult? rez = result.Succeeded ? result : null;

        return (rez, declined);
    }
}
