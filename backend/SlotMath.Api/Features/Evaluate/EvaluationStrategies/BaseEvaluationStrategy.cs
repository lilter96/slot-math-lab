using System.Diagnostics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
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

        StrategyExecutionOutput context =
            await ExecuteEvaluationAsync(compiled, config, request, budget, cap, deadline.Token, cancellationToken);

        SlotMathReport? report = context.Report;
        var method = context.Method;
        if (context.Structured is { } structured)
        {
            method = "structured";
            if (structured.Distribution is { } distribution)
            {
                report = ExactMetrics.Compute(distribution, cap, winScale: compiled.WinScale);
            }
        }

        RtpMetric rtp = context.Structured is { PrunedMass.IsZero: false }
            ? context.Structured.Rtp(compiled.WinScale)
            : report?.Rtp ?? context.Structured!.Rtp(compiled.WinScale);
        var actual = request.Mode != "Sampled" && method != "sampling"
            ? rtp.Provenance.Provenance.ToString()
            : context.Actual;

        var half = rtp.Ci95Half ?? 0;

        return EvaluationOutputBuilder.Build(
            request, actual, rtp, context.Incomplete,
            method, half, context.Samples,
            report, context.Structured, compiled, timer, hash);
    }

    protected abstract Task<StrategyExecutionOutput> ExecuteEvaluationAsync(
        CompileResult compiled, GraphConfig config,
        GraphEvaluationRequest request, Budget budget,
        long? cap, CancellationToken deadlineToken, CancellationToken cancellationToken);
}
