using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math.Structured;

namespace SlotMath.Core.Tests.Math.Structured;

public sealed class TransformConvolutionTests
{
    private static (Law Law, InferenceStats Stats) Sum(int count, int scale, int gap, bool transform,
        bool error = false)
    {
        var budget = new WorkBudget(100000000, TimeSpan.FromSeconds(15), CancellationToken.None);
        var factory = new TermFactory(budget);
        var inference = new Inference(factory, transform);
        BigInteger[] weights = Enumerable.Range(0, count).Select(i => new BigInteger(1 + i % 7)).ToArray();
        Rv a = factory.NewRv("a", weights), b = factory.NewRv("b", weights.Reverse().ToArray());
        ExprValue[] first = Enumerable.Range(0, count).Select(i => ExprValue.Rational(-17 + i * gap, scale)).ToArray();
        ExprValue[] second = Enumerable.Range(0, count).Select(i => ExprValue.Rational(8 + i * gap, scale)).ToArray();
        if (error)
        {
            first[0] = V.Err;
        }

        Term term = factory.Lin(V.Zero, [(V.One, factory.Table(a, first)), (V.One, factory.Table(b, second))]);
        return (inference.LawOf(term), inference.Stats);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(7, 5)]
    public void DenseIntegerAndFractionalLatticesPreserveTheCanonicalLaw(int scale, int gap)
    {
        (Law expected, _) = Sum(1024, scale, gap, false);
        (Law actual, InferenceStats stats) = Sum(1024, scale, gap, true);
        Assert.True(stats.TransformConvolutions > 0);
        Assert.Equal(expected.Total, actual.Total);
        Assert.Equal(expected.Values, actual.Values);
        Assert.Equal(expected.Mass, actual.Mass);
        Assert.True(stats.DirectConvolutionPairs < 10000);
    }

    [Fact]
    public void ErrorMassAndSmallLawsRetainDirectSemantics()
    {
        foreach (var (count, error) in new[] { (32, false), (512, true) })
        {
            (Law expected, _) = Sum(count, 3, 1, false, error);
            (Law actual, InferenceStats stats) = Sum(count, 3, 1, true, error);
            Assert.Equal(0, stats.TransformConvolutions);
            Assert.Equal(expected.Total, actual.Total);
            Assert.Equal(expected.Values, actual.Values);
            Assert.Equal(expected.Mass, actual.Mass);
            Assert.Equal(expected.ErrMass, actual.ErrMass);
        }
    }
}
