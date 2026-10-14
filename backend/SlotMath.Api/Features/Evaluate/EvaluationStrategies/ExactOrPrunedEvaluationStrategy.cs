using System.Composition;
using System.Diagnostics;
using System.Numerics;
using System.Timers;
using Microsoft.EntityFrameworkCore;
using SlotMath.Api.Features.Evaluate;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;
using static System.Net.WebRequestMethods;
using static SlotMath.Core.SlotMathConstants;

namespace SlotMath.Api.Features.Evaluate.EvaluationStrategies;

public class ExactOrPrunedEvaluationStrategy : BaseEvaluationStrategy
{
    private const long MaxEpsilonFactor = 1_000_000_000_000;
    public override bool CanEvaluate(EvaluationMode mode)
        => mode is EvaluationMode.Exact || mode is EvaluationMode.Pruned;

    protected override async Task<StrategyExecutionOutput> ExecuteEvaluationAsync(
        CompileResult compiled, GraphConfig config,
        GraphEvaluationRequest request, SlotMath.Core.Math.Regime.Budget budget,
        long? cap, CancellationToken deadlineToken, CancellationToken cancellationToken)
    {
        // epsilon
        var epsilon = new Rational((long)Math.Round(request.Epsilon * MaxEpsilonFactor), MaxEpsilonFactor);

        SlotMathReport? report = null;
        StructuredExactResult? structured = null;

        string? actual = null;
        var mode = Enum.Parse<EvaluationMode>(request.Mode); // pruned or exact
        string method = "enumeration";

        try
        {
            var result = await Task.Run(() =>
                ExactInterpreter.Evaluate(compiled.Program!, new Dictionary<string, object?>(),
                    StateHasher.CanonicalHash,
                    new ExactConfig
                    {
                        Budget = budget,
                        EpsilonNumerator = mode is EvaluationMode.Pruned ? epsilon.Numerator : 0,
                        EpsilonDenominator = epsilon.Denominator
                    }), cancellationToken);

            report = ExactMetrics.Compute(result.ValueDistribution(), (BigInteger)cap, winScale: compiled.WinScale);
            actual = report.Rtp.Provenance.Provenance.ToString();
        }
        catch (BudgetExceededException ex)
        {
            var (sd, d) = await StructuredEvaluationService.Structured(compiled, cancellationToken, cancellationToken);
            structured = sd;
            if (structured is null)
            {
                throw new InvalidOperationException($"Structured evaluation declined. Reason: {d}", ex);
            }

            if (structured?.Distribution is { } dist)
            {
                report = ExactMetrics.Compute(dist, structured.WinCap, winScale: compiled.WinScale);
            }

            actual = structured.Rtp(compiled.WinScale).Provenance.Label;
            method = "structured";
        }

        return new StrategyExecutionOutput(
            Report: report,
            Samples: 0,
            Incomplete: false,
            Method: method,
            Actual: actual, 
            Structured: structured
        );
    }
}
