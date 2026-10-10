using SlotMath.Core.Model;

namespace SlotMath.Core.Measurements;

/// <summary>Versioned measurement semantics, independent of dashboard presentation.
/// Node values are sampled before execution. Episode entry/exit are explicit graph points.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record MeasurementOptions
{
    public string Subject { get; init; } = "observation"; // observation, round, episode
    public string Reduction { get; init; } = "sum";
    public string Source { get; init; } = "value"; // value, event, count
    public string? EntryNodeId { get; init; }
    public string? ExitNodeId { get; init; }
    public Expression? EntryFilter { get; init; }
    public Expression? ExitFilter { get; init; }
    public Expression? Group { get; init; }
    public Expression? Pair { get; init; }
    public string PairRole { get; init; } = "value";
    public Expression? Weight { get; init; }
    public Expression? AwardId { get; init; }
    public string Assertion { get; init; } = "none";
    public double[] BinEdges { get; init; } = [0, 1, 2, 5, 10, 20, 50, 100, 500, 1000];
    public double[] Quantiles { get; init; } = [0.5, 0.9, 0.95, 0.99];
    public double[] Thresholds { get; init; } = [1, 10, 100];
    public int SupportLimit { get; init; } = 256;
    public int GroupLimit { get; init; } = 32;
    public int[] Lags { get; init; } = [];
    public double Stake { get; init; } = 1;
    public double? LowerBound { get; init; }
    public double? UpperBound { get; init; }
    public double Confidence { get; init; } = 0.95;
    public int ErrorFamilySize { get; init; } = 1;
    public bool IndependentSubjects { get; init; }
    public bool IndependentParents { get; init; }
    public double? ReferenceMean { get; init; }
    public string ReferenceStatistic { get; init; } = "mean";
    public double? Tolerance { get; init; }
    public ReferenceMass[] ReferenceDistribution { get; init; } = [];
}
public sealed record ReferenceMass(double Value, double Probability);
public sealed record NumericInterval(double Lower, double Upper, string Method, string Assumptions);
public sealed record QuantileEstimate(double Probability, double? Value, double? Lower, double? Upper, string Method);
public sealed record DistributionBin(double? Lower, double? Upper, long Count, double Sum, double SumSquares);
public sealed record ValueFrequency(double Value, long Count, double Sum);
public sealed record TailSummary(double Threshold, long Count, double? Probability, double Sum, double? Mean, double? SecondMoment);
public sealed record UpperTailEstimate(double Quantile, double TailMass, double? Mean, double? LowerMean, double? UpperMean, double? LowerReturnShare, double? UpperReturnShare, string Method);
public sealed record PairSummary(long Count, double SumY, double? MeanY, double? Covariance, double? Correlation, double? Ratio, NumericInterval? RatioInterval, double? MeanDifference)
{ public double? SampleVarianceY { get; init; } public double? SampleVarianceSum { get; init; } public double? SampleVarianceDifference { get; init; } public NumericInterval? DifferenceInterval { get; init; } public JointDiagnostics? Joint { get; init; } }
public sealed record TransitionFrequency(double From, double To, long Count, long FromExposure, double Probability);
public sealed record MomentSummary(double? SecondMoment, double? PopulationVariance, double? SampleVariance, double? CoefficientOfVariation, double? Skewness, double? ExcessKurtosis, double? MeanAbsoluteDeviation);
public sealed record StateDwell(double State, long CompletedRuns, long Observations, long MaximumLength, double MeanLength);
public sealed record SequenceSummary(long Count, long Events, long LongestEventStreak, long LongestDrought, long CompletedGaps, double? MeanGap, IReadOnlyDictionary<int, double?> Autocorrelations, bool Ordered,
    long EqualAdjacentPairs, long AdjacentPairs, StateDwell[] StateDwell, bool StateDwellComplete);
public sealed record VerificationCheck(string Id, string Status, double? Observed, double? Reference, double? Difference, string Detail);
public sealed record AssertionSummary(string Kind, long Checked, long Violations, string Status);
public sealed record ContributionNormalization(long PaidRounds, double? ExternalTurnover, string Basis);
/// <summary>Accepted child exposure before exit filters and subject reduction.
/// Each paid round and each owning episode is counted once, including open
/// episodes in a settled round. Unfinished paid rounds publish no exposure.</summary>
public sealed record ParentExposure(long PaidRoundsWithMatchingChildren, long? EpisodesWithMatchingChildren);
public sealed record DistributionComparison(double TotalVariation, double CdfDistance, double? ChiSquare, int DegreesOfFreedom, bool ExpectedCountsAdequate, long UnexpectedObservations, double? PValue, string Calibration);
public sealed record WeightedSummary(double WeightSum, double WeightSquares, double? EffectiveSampleSize, double? OrdinaryEstimate, double? SelfNormalizedEstimate, double MinWeight, double MaxWeight, NumericInterval? OrdinaryInterval, double? EventEstimate, NumericInterval? EventInterval, double? PairedRatio, NumericInterval? PairedRatioInterval);
public sealed record MeasurementAnalysis(
    long Count, double? Min, double? Max, double? Mean, double? Sum, string Subject, string Reduction, long DistinctParents, long Entries, long Exits, long UnclosedEpisodes,
    long UniqueAwards, long DuplicateAwards, MomentSummary Moments, PairSummary? Pair,
    ValueFrequency[] Support, bool SupportComplete, DistributionBin[] Bins, QuantileEstimate[] Quantiles,
    TailSummary[] Tails, UpperTailEstimate[] UpperTails, NumericInterval? MeanAbsoluteDeviationBounds, NumericInterval? MeanInterval, NumericInterval? ProbabilityInterval, NumericInterval? ClusteredMeanInterval,
    NumericInterval? SequentialMeanInterval, double? MeanStandardError, double? RequiredSampleSize,
    WeightedSummary? Weights, SequenceSummary? Sequence, TransitionFrequency[] Transitions, bool TransitionsComplete, DistributionComparison? Comparison,
    VerificationCheck[] Checks, IReadOnlyDictionary<string, MeasurementAnalysis> Groups)
{ public bool GroupsComplete { get; init; } = true; public AssertionSummary? Assertion { get; init; } public ContributionNormalization? Normalization { get; init; } public ParentExposure? ParentExposure { get; init; } public InterruptedFeatureLifecycle? InterruptedLifecycle { get; init; } }

internal sealed record MeasurementBinding<T>(Func<T, Expressions.ExprValue>? Value,
    Func<T, Expressions.ExprValue>? Filter, Func<T, Expressions.ExprValue>? Group = null,
    Func<T, Expressions.ExprValue>? Pair = null, Func<T, Expressions.ExprValue>? Weight = null,
    Func<T, Expressions.ExprValue>? AwardId = null, Func<T, Expressions.ExprValue>? EntryFilter = null, Func<T, Expressions.ExprValue>? ExitFilter = null);
