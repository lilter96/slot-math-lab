using System.Numerics;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;

namespace SlotMath.Core.Tests.Math;

public sealed class ExactPayoutLawTests
{
    private static Dist<BigInteger> Law(params (int Value, int Mass)[] atoms)
    {
        var builder = new DistBuilder<BigInteger>();
        var denominator = atoms.Sum(atom => atom.Mass);
        foreach ((int Value, int Mass) atom in atoms)
        {
            builder.Add(atom.Value, atom.Mass, denominator);
        }

        return builder.Build();
    }

    [Fact]
    public void JackpotLawSeparatesRawCentralMomentsAndOperatorLoss()
    {
        var law = ExactPayoutLaw.Create(Law((0, 9), (100, 1)), 1);
        Assert.Equal(new Rational(10, 1), law.Mean);
        Assert.Equal(new Rational(1000, 1), law.SecondMoment);
        Assert.Equal(new Rational(100000, 1), law.ThirdMoment);
        Assert.Equal(new Rational(10000000, 1), law.FourthMoment);
        Assert.Equal(new Rational(900, 1), law.Variance);
        Assert.Equal(new Rational(72000, 1), law.CentralThirdMoment);
        Assert.Equal(new Rational(6570000, 1), law.CentralFourthMoment);
        Assert.Equal(new Rational(64, 9), law.SkewnessSquared);
        Assert.Equal(1, law.SkewnessSign);
        Assert.Equal(new Rational(46, 9), law.ExcessKurtosis);
        Assert.Equal(new Rational(9, 1), law.CoefficientOfVariationSquared);
        Assert.Equal(new Rational(5, 1), law.Rtp(2));
        Assert.Equal(new Rational(98, 1), law.OperatorLossQuantile(new Rational(95, 100), 2));
        Assert.Equal(new Rational(98, 1), law.OperatorLossExpectedShortfall(new Rational(95, 100), 2));
    }

    [Fact]
    public void TiedTailUsesExactlyTheRequestedMassAndFractionalCreditUnits()
    {
        var law = ExactPayoutLaw.Create(Law((0, 8), (10, 1), (100, 1)), 10);
        Assert.Equal(Rational.Zero, law.Quantile(new Rational(8, 10)));
        Assert.Equal(Rational.One, law.Quantile(new Rational(9, 10)));
        Assert.Equal(new Rational(10, 1), law.Quantile(Rational.One));
        Assert.Equal(new Rational(9, 10), law.Cdf(1));
        Assert.Equal(new Rational(2, 10), law.SurvivalInclusive(1));
        Assert.Equal(new Rational(1, 10), law.SurvivalInclusive(new Rational(11, 10)));
        Assert.Equal(new Rational(11, 2), law.ExpectedShortfall(new Rational(8, 10)));
        Assert.Equal(new Rational(7, 1), law.ExpectedShortfall(new Rational(85, 100)));
        Assert.Equal(new Rational(10, 1), law.ExpectedShortfall(new Rational(95, 100)));
        Assert.Equal(law.Mean, law.ExpectedShortfall(Rational.Zero));
        Assert.Equal(Rational.Zero, law.Cdf(-1));
        Assert.Equal(Rational.One, law.Cdf(100));
    }

    [Fact]
    public void SameMeanAndVarianceDoNotDetermineTheTailLaw()
    {
        var a = ExactPayoutLaw.Create(Law((0, 1), (2, 1)), 1);
        var b = ExactPayoutLaw.Create(Law((0, 2), (1, 3), (3, 1)), 1);
        Assert.Equal(a.Mean, b.Mean);
        Assert.Equal(a.Variance, b.Variance);
        Assert.NotEqual(a.Quantile(new Rational(9, 10)), b.Quantile(new Rational(9, 10)));
        Assert.NotEqual(a.ExpectedShortfall(new Rational(9, 10)), b.ExpectedShortfall(new Rational(9, 10)));
    }

    [Fact]
    public void DegenerateLawsHaveUndefinedStandardizedShapeAndValidZeroVariation()
    {
        var law = ExactPayoutLaw.Create(Law((10, 1), (99999, 0)), 10);
        Assert.Equal(Rational.One, law.Minimum);
        Assert.Equal(Rational.One, law.Maximum);
        Assert.Equal(Rational.Zero, law.Variance);
        Assert.Equal(Rational.Zero, law.CoefficientOfVariationSquared);
        Assert.Null(law.SkewnessSquared);
        Assert.Null(law.ExcessKurtosis);
        var blank = ExactPayoutLaw.Create(Law((0, 1)), 1);
        Assert.Null(blank.CoefficientOfVariationSquared);
    }

    [Fact]
    public void IncompleteLawAndPrunedGraphCannotClaimExactRisk()
    {
        var builder = new DistBuilder<BigInteger>();
        builder.Add(0, 1, 2);
        Assert.Throws<ArgumentException>(() => ExactPayoutLaw.Create(builder.Build(), 1));
        Dist<BigInteger> complete = Law((0, 1), (10, 1));
        var result = new StructuredExactResult
        {
            Succeeded = true,
            Distribution = complete,
            PrunedMass = new Rational(1, 100)
        };
        Assert.Throws<InvalidOperationException>(() => result.AnalyzeRisk(1));
        Assert.Throws<InvalidOperationException>(() => new StructuredExactResult().AnalyzeRisk(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExactPayoutLaw.Create(complete, 0));
        var law = ExactPayoutLaw.Create(complete, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => law.Quantile(Rational.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => law.ExpectedShortfall(Rational.One));
        Assert.Throws<ArgumentException>(() => law.Cdf(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => law.Rtp(0));
        Assert.Throws<OperationCanceledException>(() =>
            ExactPayoutLaw.Create(complete, 1, new CancellationToken(true)));
    }

    [Fact]
    public void ValidatedLawOwnsItsSnapshotAndChecksActualMassRatherThanCachedMetadata()
    {
        Dist<BigInteger> distribution = Law((0, 1), (10, 1));
        var law = ExactPayoutLaw.Create(distribution, 1);
        var escaped = (Dist<BigInteger>.Entry[])distribution.Entries;
        escaped[0] = new Dist<BigInteger>.Entry(1000, 1);
        Assert.Equal(new Rational(5, 1), law.Mean);
        Assert.Equal(new Rational(10, 1), law.Maximum);
        var malformed = new Dist<BigInteger>([new Dist<BigInteger>.Entry(0, 1)], 2, 2, 0, 1);
        Assert.Throws<ArgumentException>(() => ExactPayoutLaw.Create(malformed, 1));
        var negative = new Dist<BigInteger>([new Dist<BigInteger>.Entry(0, -1), new Dist<BigInteger>.Entry(10, 2)], 1,
            1, 0, 1);
        Assert.Throws<ArgumentException>(() => ExactPayoutLaw.Create(negative, 1));
        var pruned = new Dist<BigInteger>([new Dist<BigInteger>.Entry(0, 1)], 1, 1, 1, 100);
        Assert.Throws<ArgumentException>(() => ExactPayoutLaw.Create(pruned, 1));
    }
}
