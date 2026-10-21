using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;
using static SlotMath.Api.Features.Evaluate.EvaluationConstants;

namespace SlotMath.Api.Features.Evaluate.EvaluationStrategies;

public class HybridEvaluationStrategy : BaseEvaluationStrategy
{
    public override bool CanEvaluate(EvaluationMode mode)
        => mode is EvaluationMode.Hybrid;

    protected override async Task<StrategyExecutionOutput> ExecuteEvaluationAsync(
        CompileResult compiled, GraphConfig config,
        GraphEvaluationRequest request, Budget budget,
        long? cap, CancellationToken deadlineToken, CancellationToken cancellationToken)
    {
        SlotMathReport? report = null;
        StructuredExactResult? structured = null;
        Dist<BigInteger>? distribution = null;
        string? declined = null;

        var actual = request.Mode;
        var method = "enumeration";
        long samples = 0;
        var incomplete = false;

        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(compiled.Program!);
        if (!analysis.ContainsPlugin && analysis.EstimatedBranches > request.MaxBranches)
        {
            (StructuredExactResult? s, var d) =
                await StructuredEvaluationService.Structured(compiled, deadlineToken, cancellationToken,
                    request.Analysis?.MomentOrder ?? 2);
            structured = s;
            declined = d;
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
                    ChunkSize = ChunkSize,
                    DegreeOfParallelism = request.DegreeOfParallelism,
                    MaxWinCap = cap,
                    WinScale = compiled.WinScale,
                    RetainExactDistribution = request.Analysis is not null,
                    CancellationToken = deadlineToken,
                    ProgressCallback = _ => { }
                }), cancellationToken);

            if (result.BudgetExceeded && !analysis.ContainsPlugin)
            {
                (StructuredExactResult? s, var d) =
                    await StructuredEvaluationService.Structured(compiled, deadlineToken, cancellationToken,
                        request.Analysis?.MomentOrder ?? 2);
                structured = s;
                declined = d;
            }

            if (structured is null)
            {
                report = result.Report;
                distribution = result.ExactDistribution;
                actual = result.OverallStrategy.ToString();
                samples = report.Rtp.SampleCount ?? 0;
                incomplete = actual == "Sampled" && samples < request.Samples;
                if (actual == "Sampled")
                {
                    method = "sampling";
                }
            }
        }

        return new StrategyExecutionOutput(
            report,
            samples,
            incomplete,
            method,
            actual,
            structured,
            distribution
        );
    }
}
