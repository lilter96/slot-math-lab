using SlotMath.Core.Measurements;
namespace SlotMath.Core.Tests.Math;

public class FiniteLawComparisonTests
{
    [Fact]
    public void EqualRtpDoesNotHideTheIndependentlySpecifiedDifferentLaws()
    {
        var result = FiniteModelAnalysis.Compare(new([new("0", "1/2"), new("1.96", "1/2")], [new("0", "9/10"), new("9.8", "1/10")]));
        Assert.False(result.Equal); Assert.Equal("49/50", result.LeftMean); Assert.Equal(result.LeftMean, result.RightMean);
        Assert.Equal("0/1", result.MeanDifference); Assert.Equal("1/2", result.TotalVariation); Assert.Equal("2/5", result.CdfDistance);
        Assert.Equal(new[] { new RationalLawDifference("0/1", "1/2", "9/10", "-2/5"), new("49/25", "1/2", "0/1", "1/2"), new("49/5", "0/1", "1/10", "-1/10") }, result.Support);
    }
    [Fact]
    public void EquivalentRationalsAtomOrderAndImpossibleValuesPreserveExactEquality()
    {
        var result = FiniteModelAnalysis.Compare(new([new("0", "1/2"), new("1.96", "1/2")], [new("98/50", "2/4"), new("5", "0"), new("0", "2/4")]));
        Assert.True(result.Equal); Assert.Equal("0/1", result.TotalVariation); Assert.Equal("0/1", result.CdfDistance); Assert.Equal(2, result.Support.Length);
    }
    [Fact]
    public void IncompleteMassAndDuplicateEquivalentAtomsCannotAcquireAnEqualityVerdict()
    {
        RationalOutcome[] one = [new("0", "1")];
        Assert.Throws<ArgumentException>(() => FiniteModelAnalysis.Compare(new(one, [new("0", "1/2")])));
        Assert.Throws<ArgumentException>(() => FiniteModelAnalysis.Compare(new(one, [new("1", "1/2"), new("2/2", "1/2")])));
        Assert.Throws<ArgumentException>(() => FiniteModelAnalysis.Compare(new(one, [new("0", "-1"), new("1", "2")])));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => FiniteModelAnalysis.Compare(new(one, one), cancelled.Token));
    }
}
