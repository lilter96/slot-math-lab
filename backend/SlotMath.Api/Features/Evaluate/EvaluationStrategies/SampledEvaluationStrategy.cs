using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Model;
using static SlotMath.Api.Features.Evaluate.EvaluationConstants;

namespace SlotMath.Api.Features.Evaluate.EvaluationStrategies;

public class SampledEvaluationStrategy : BaseEvaluationStrategy
{
    public override bool CanEvaluate(EvaluationMode mode)
        => mode is EvaluationMode.Sampled;

    protected override async Task<StrategyExecutionOutput> ExecuteEvaluationAsync(
        CompileResult compiled, GraphConfig config,
        GraphEvaluationRequest request, Budget budget,
        long? cap, CancellationToken deadlineToken, CancellationToken cancellationToken)
    {
        SampledResult<Dictionary<string, object?>> result = await Task.Run(() =>
            SampledInterpreter.Evaluate(compiled.Program!, new Dictionary<string, object?>(),
                new SampledConfig
                {
                    Seed = request.Seed,
                    MaxSpins = request.Samples,
                    DegreeOfParallelism = request.DegreeOfParallelism,
                    ChunkSize = ChunkSize,
                    MaxWinCap = cap,
                    WinScale = (double)compiled.WinScale,
                    CancellationToken = deadlineToken,
                    CancellationCheckInterval = 1
                }), cancellationToken);

        if (result.SpinsCompleted == 0)
        {
            throw new TimeoutException("Evaluation timed out before completing any spins.");
        }

        SlotMathReport report = SampledMetrics.ComputeFromResult(result);

        return new StrategyExecutionOutput(
            report,
            result.SpinsCompleted,
            result.WasCancelled,
            "sampling",
            request.Mode,
            null
        );
    }
}
