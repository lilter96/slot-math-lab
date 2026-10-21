using System.Numerics;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;

namespace SlotMath.Api.Features.Evaluate;

public class EvaluationOutput
{
    public string RequestedMode { get; init; }
    public string ActualStrategy { get; init; }
    public string Provenance { get; init; }
    public string Status { get; init; }
    public string Method { get; init; }
    public double Rtp { get; init; }
    public double Lower { get; init; }
    public double Upper { get; init; }
    public string? RationalRtp { get; init; }
    public double PrunedMass { get; init; }
    public long Samples { get; init; }
    public long RequestedSamples { get; init; }
    public long Seed { get; init; }
    public int DegreeOfParallelism { get; init; }
    public double? HitFrequency { get; init; }
    public string? RationalHitFrequency { get; init; }
    public string Distribution { get; init; }
    public string StreamScheme { get; init; } = "splitmix64-chunks-4096-v1";
    public int ChunkSize { get; init; } = 4096;
    public string SamplingEngine { get; init; }
    public double? Variance { get; init; }
    public PayoutAnalysisOutput? Analysis { get; init; }
    public IEnumerable<LoopEvaluationOutput>? Loops { get; init; }
    public double? MaxObserved { get; init; }
    public double ElapsedMs { get; init; }
    public string ConfigHash { get; init; }
    public string? Note { get; init; }
}

// loops
public class LoopEvaluationOutput
{
    public string LoopId { get; init; }
    public string Evaluation { get; init; }
    public long IterationCap { get; init; }
    public long Runs { get; init; }
    public long States { get; init; }
    public double? ExpectedCapHits { get; init; }
    public double PrunedMass { get; init; }
}

public record StrategyExecutionOutput(
    SlotMathReport? Report,
    long Samples,
    bool Incomplete,
    string Method,
    string Actual,
    StructuredExactResult? Structured,
    Dist<BigInteger>? ExactDistribution = null
);
