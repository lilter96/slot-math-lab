using SlotMath.Core.Measurements;
namespace SlotMath.Core.Tests.Math;

public class FiniteModelAnalysisTests
{
    [Fact]
    public void EqualRtpLawsHaveDifferentFullMoments()
    {
        var a = FiniteModelAnalysis.Distribution(new([new("0", "1/2"), new("1.96", "1/2")]));
        var b = FiniteModelAnalysis.Distribution(new([new("0", "9/10"), new("9.8", "1/10")]));
        Assert.Equal("49/50", a.Rtp.Lower); Assert.Equal(a.Rtp, b.Rtp);
        Assert.Equal("2401/2500", a.Variance.Lower); Assert.Equal("21609/2500", b.Variance.Lower);
        Assert.Equal("1/2", a.HitProbability.Lower); Assert.Equal("1/10", b.HitProbability.Lower);
    }
    [Fact]
    public void PrunedMomentsAreUnnormalizedAndConservativelyBounded()
    {
        var result = FiniteModelAnalysis.Distribution(new([new("0", "1/2"), new("2", "1/4")], PrunedMass: "1/4", ProvenMaximum: "8"));
        Assert.Equal(new("1/2", "5/2"), result.Mean); Assert.Equal(new("1/1", "17/1"), result.SecondMoment);
        Assert.Equal(new("0/1", "67/4"), result.Variance); Assert.Equal(new("1/4", "1/2"), result.HitProbability);
        var unbounded = FiniteModelAnalysis.Distribution(new([new("0", "1/2"), new("2", "1/4")], PrunedMass: "1/4"));
        Assert.Null(unbounded.Mean.Upper); Assert.Null(unbounded.SecondMoment.Upper); Assert.Null(unbounded.Variance.Upper);
    }
    [Fact]
    public void ModeCostChangesReturnWithoutChangingPayoutLaw()
    {
        var result = FiniteModelAnalysis.Distribution(new([new("0", "1/2"), new("2", "1/2")], Cost: "2"));
        Assert.Equal("1/1", result.Mean.Lower); Assert.Equal("1/2", result.Rtp.Lower); Assert.Equal("1/2", result.HouseEdge);
    }
    [Fact]
    public void FundamentalMatrixMatchesHandSolvedVisitsDurationAndReward()
    {
        var result = FiniteModelAnalysis.Markov(new([["1/2", "1/2"], ["0", "0"]], ["1", "4"]));
        Assert.Equal("absorbing", result.Status); Assert.Equal(new[] { "2/1", "1/1" }, result.ExpectedVisits);
        Assert.Equal("3/1", result.ExpectedDuration); Assert.Equal("6/1", result.ExpectedReward);
    }
    [Fact]
    public void ReachableClosedClassesCannotBeSilentlyTruncated()
    {
        var result = FiniteModelAnalysis.Markov(new([["0", "1"], ["0", "1"]], ["0", "1"]));
        Assert.Equal("nonterminating", result.Status); Assert.Null(result.ExpectedDuration); Assert.Null(result.ExpectedReward);
        var unreachable = FiniteModelAnalysis.Markov(new([["0", "0"], ["0", "1"]], ["2", "100"]));
        Assert.Equal("absorbing", unreachable.Status); Assert.Equal("2/1", unreachable.ExpectedReward); Assert.Equal("0/1", unreachable.ExpectedVisits[1]);
    }
    [Fact]
    public void PartialAbsorptionAndDurationMomentsMatchHandSolvedLaws()
    {
        var partial = FiniteModelAnalysis.Markov(new([["0", "1/2"], ["0", "1"]], ["0", "1"]));
        Assert.Equal("nonterminating", partial.Status); Assert.Equal("1/2", partial.AbsorptionProbability); Assert.Null(partial.ExpectedDuration);
        var geometric = FiniteModelAnalysis.Markov(new([["1/2"]], ["1"]));
        Assert.Equal("1/1", geometric.AbsorptionProbability); Assert.Equal("2/1", geometric.ExpectedDuration); Assert.Equal("6/1", geometric.DurationSecondMoment); Assert.Equal("2/1", geometric.DurationVariance);
    }
    [Fact]
    public void StationaryReturnUsesOccupationWeightedCostAndRejectsAmbiguousRecurrentClasses()
    {
        var model = new MarkovModelRequest([["1/2", "1/2"], ["1/4", "3/4"]], ["0", "1.47"], Stationary: true);
        var result = FiniteModelAnalysis.Markov(model); Assert.Equal("stationary", result.Status);
        Assert.Equal(new[] { "1/3", "2/3" }, result.StationaryOccupancy); Assert.Equal("49/50", result.LongRunReturn);
        var mixed = FiniteModelAnalysis.Markov(model with { Costs = ["1", "2"] }); Assert.Equal("147/250", mixed.LongRunReturn);
        var periodic = FiniteModelAnalysis.Markov(new([["0", "1"], ["1", "0"]], ["0", "2"], Stationary: true)); Assert.Equal(new[] { "1/2", "1/2" }, periodic.StationaryOccupancy);
        var multiple = FiniteModelAnalysis.Markov(new([["0", "1/2", "1/2"], ["0", "1", "0"], ["0", "0", "1"]], ["0", "1", "2"], Stationary: true)); Assert.Equal("nonuniqueStationary", multiple.Status); Assert.Null(multiple.LongRunReturn);
        Assert.Throws<ArgumentException>(() => FiniteModelAnalysis.Markov(new([["1/2"]], ["1"], Stationary: true)));
    }
    [Fact]
    public void IndependentSamplePlansDistinguishPrecisionFromRareEventDetection()
    {
        var plan = SamplePlanning.Calculate(new(EventProbability: 1e-7, Variance: 1));
        Assert.Equal(29957322, plan.EventDetectionRounds); Assert.False(plan.DetectionBudgetSufficient); Assert.False(plan.MeanBudgetSufficient);
        Assert.Equal(2.9956874e-5, plan.ZeroObservedEventUpper, 12);
        var bounded = SamplePlanning.Calculate(new(Precision: .1, LowerBound: 0, UpperBound: 1)); Assert.NotNull(bounded.TimeUniformBoundedMeanRounds);
        var n = bounded.TimeUniformBoundedMeanRounds!.Value;
        var at = SlotMath.Core.Measurements.StatisticalInference.SequentialMean(.5, n, 0, 1, .05); Assert.True(at.Upper - .5 <= .1);
        if (n > 1) Assert.True(SlotMath.Core.Measurements.StatisticalInference.SequentialMean(.5, n - 1, 0, 1, .05).Upper - .5 > .1);
        Assert.Null(SamplePlanning.Calculate(new(Variance: 0)).ApproximateMeanRounds);
        Assert.Throws<ArgumentException>(() => SamplePlanning.Calculate(new(EventProbability: 0)));
    }
    [Fact]
    public void InvalidReferenceMassAndTransitionRowsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => FiniteModelAnalysis.Distribution(new([new("1", "1/2")])));
        Assert.Throws<ArgumentException>(() => FiniteModelAnalysis.Distribution(new([new("-1", "1")])));
        Assert.Throws<ArgumentException>(() => FiniteModelAnalysis.Distribution(new([new("2", "1")], ProvenMaximum: "1")));
        Assert.Throws<ArgumentException>(() => FiniteModelAnalysis.Markov(new([["1", "1"], ["0", "0"]], ["0", "0"])));
    }
}
