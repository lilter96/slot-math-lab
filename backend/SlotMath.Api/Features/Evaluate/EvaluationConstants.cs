namespace SlotMath.Api.Features.Evaluate;

public static class EvaluationConstants
{
    public const int MinSamples = 1;
    public const int MaxSamples = 1_000_000;

    public const int MinDegreeOfParallelism = 1;
    public const int MaxDegreeOfParallelism = 4;

    public const int MinBranches = 1;
    public const int MaxBranches = 100_000;

    public const double MinEpsilon = 0;
    public const double MaxEpsilon = 1;

    public const long MaxAbsoluteSeed = 9_007_199_254_740_991;

    public const int ChunkSize = 4096;
}
