using SlotMath.Core.Measurements;
namespace SlotMath.Core.Tests.Math;
public class DiscreteCalibrationTests
{
    [Theory][InlineData("cdf")][InlineData("pearson")]
    public void InclusiveEnumeratedCoinTailEqualsTheIndependentHandCalculation(string statistic)
    {
        // Two tosses: extreme all-zero/all-one laws each have mass 1/4.
        var report = DiscreteNullCalibration.Calculate(new([new(0, 0, 0), new(1, 2, 2)], [new(0, .5), new(1, .5)], statistic));
        Assert.Equal(.5, report.PValue, 12); Assert.Equal(3, report.Evaluations); Assert.Equal(2, report.ExtremeEvaluations); Assert.Null(report.MonteCarloTailInterval);
    }
    [Fact]
    public void SparseDiscreteNullNeedsNoLargeSampleCriticalValue()
    {
        var report = DiscreteNullCalibration.Calculate(new([new(0, 1, 0)], [new(0, .999), new(1000, .001)]));
        Assert.Equal(1, report.PValue, 12); Assert.Equal(2, report.Evaluations);
        var mismatch = DiscreteNullCalibration.Calculate(new([new(7, 1, 7)], [new(0, 1)])); Assert.Equal(0, mismatch.PValue); Assert.Null(mismatch.ObservedStatistic);
    }
    [Fact]
    public void MonteCarloUsesPlusOneResolutionAndAnIndependentReproducibleStream()
    {
        var input = new NullCalibrationRequest([new(0, 750, 0), new(1, 250, 250)], [new(0, .5), new(1, .5)], Replicates: 1000);
        var a = DiscreteNullCalibration.Calculate(input); var b = DiscreteNullCalibration.Calculate(input);
        Assert.Equal(a, b); Assert.Equal(1d / 1001, a.PValue); Assert.Equal(a.MinimumResolvablePValue, a.PValue); Assert.NotNull(a.MonteCarloTailInterval);
        Assert.Throws<ArgumentException>(() => DiscreteNullCalibration.Calculate(input with { Replicates = 20000 }));
        using var source = new CancellationTokenSource(); source.Cancel(); Assert.Throws<OperationCanceledException>(() => DiscreteNullCalibration.Calculate(input, source.Token));
    }
    [Fact]
    public void InvalidCountsProbabilitiesAndDuplicateSupportAreRejected()
    {
        Assert.Throws<ArgumentException>(() => DiscreteNullCalibration.Calculate(new([new(0, -1, 0)], [new(0, 1)])));
        Assert.Throws<ArgumentException>(() => DiscreteNullCalibration.Calculate(new([new(0, 1, 0), new(0, 1, 0)], [new(0, 1)])));
        Assert.Throws<ArgumentException>(() => DiscreteNullCalibration.Calculate(new([new(0, 1, 0)], [new(0, .5)])));
    }
}
