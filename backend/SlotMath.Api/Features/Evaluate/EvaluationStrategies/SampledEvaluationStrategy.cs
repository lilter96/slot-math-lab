using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Math.Structured;
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
        var result = await Task.Run(() =>
            SampledInterpreter.Evaluate(compiled.Program!, new Dictionary<string, object?>(),
                new SampledConfig
                {
                    Seed = request.Seed,
                    MaxSpins = request.Samples,
                    DegreeOfParallelism = request.DegreeOfParallelism,
                    ChunkSize = EvaluationConstants.ChunkSize,
                    MaxWinCap = cap,
                    WinScale = (double)compiled.WinScale,
                    CancellationToken = deadlineToken,
                    CancellationCheckInterval = 1
                }), cancellationToken);

        var report = SampledMetrics.ComputeFromResult(result);

        return new StrategyExecutionOutput(
            Report: report,
            Samples: result.SpinsCompleted,
            Incomplete: result.WasCancelled,
            Method: "sampling",
            Actual: request.Mode,
            Structured: null
        );
    }
}
