using System.Diagnostics;
using System.Numerics;
using SlotMath.Api.Features.Evaluate;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;

namespace SlotMath.Api.Features.Evaluate.EvaluationStrategies;

public abstract class BaseEvaluationStrategy : IEvaluationStrategy
{
    public abstract bool CanEvaluate(EvaluationMode mode);

    public async Task<EvaluationOutput> EvaluateAsync(
        CompileResult compiled, GraphConfig config,
        GraphEvaluationRequest request, Budget budget,
        string? hash, Stopwatch? timer, CancellationToken cancellationToken)
    {
        var cap = config.Nodes.OfType<MetricsSinkNode>().Single().WinCap;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));

        var context = await ExecuteEvaluationAsync(compiled, config, request, budget, cap, deadline.Token, cancellationToken);

        RtpMetric rtp = context.Structured is { PrunedMass.IsZero: false }
            ? context.Structured.Rtp(compiled.WinScale)
            : context.Report?.Rtp ?? context.Structured!.Rtp(compiled.WinScale);

        var half = rtp.Ci95Half ?? 0;

        return EvaluationOutputBuilder.Build(
            request, context.Actual, rtp, context.Incomplete,
            context.Method, half, context.Samples,
            context.Report, context.Structured, compiled, timer, hash);
    }

    protected abstract Task<StrategyExecutionOutput> ExecuteEvaluationAsync(
        CompileResult compiled, GraphConfig config,
        GraphEvaluationRequest request, Budget budget,
        long? cap, CancellationToken deadlineToken, CancellationToken cancellationToken);
}
