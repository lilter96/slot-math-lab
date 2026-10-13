using System.Diagnostics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;

namespace SlotMath.Api.Features.Evaluate.EvaluationStrategies;

public interface IEvaluationStrategy
{
    bool CanEvaluate(EvaluationMode mode);
    Task<EvaluationOutput> EvaluateAsync(CompileResult compiled, GraphConfig config,
        GraphEvaluationRequest request, Budget budget,
        string? hash, Stopwatch? timer, CancellationToken cancellationToken);
};
