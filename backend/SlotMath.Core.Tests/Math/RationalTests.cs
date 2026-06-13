using System.Numerics;
using SlotMath.Core.Math;

namespace SlotMath.Core.Tests.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  D1 — canonical rational form
// ═══════════════════════════════════════════════════════════════════════════

public class RationalTests
{
    [Fact]
    public void ReducesToLowestTerms()
    {
        var r = new Rational(6, 8);
        Assert.Equal(new BigInteger(3), r.Numerator);
        Assert.Equal(new BigInteger(4), r.Denominator);
    }

    [Fact]
    public void NegativeDenominator_MovesSignToNumerator()
    {
        var r = new Rational(1, -2);
        Assert.Equal(new BigInteger(-1), r.Numerator);
        Assert.Equal(new BigInteger(2), r.Denominator);
    }

    [Fact]
    public void CanonicalZero_IsZeroOverOne()
    {
        var r = new Rational(0, 7);
        Assert.Equal(BigInteger.Zero, r.Numerator);
        Assert.Equal(BigInteger.One, r.Denominator);
        Assert.Equal("0/1", r.ToString());
        Assert.True(r.IsZero);
        Assert.Equal(Rational.Zero, r);
    }

    [Fact]
    public void ZeroDenominator_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => new Rational(1, 0));
    }

    [Fact]
    public void Arithmetic_IsExact()
    {
        // 1/3 + 1/6 = 1/2
        Assert.Equal(new Rational(1, 2), new Rational(1, 3) + new Rational(1, 6));
        // 3/4 - 1/4 = 1/2
        Assert.Equal(new Rational(1, 2), new Rational(3, 4) - new Rational(1, 4));
        // 2/3 * 3/4 = 1/2
        Assert.Equal(new Rational(1, 2), new Rational(2, 3) * new Rational(3, 4));
        // (1/2) / (1/4) = 2
        Assert.Equal(new Rational(2, 1), new Rational(1, 2) / new Rational(1, 4));
    }

    [Fact]
    public void RefA_Rtp_Equals_ThreeQuarters_Exactly()
    {
        // REF-A (D9): E[win] = (1*3 + 3*1 + 4*0) / 8 = 6/8 = 3/4 exactly.
        var rtp = (new Rational(1, 8) * 3) + (new Rational(3, 8) * 1) + (new Rational(4, 8) * 0);
        Assert.Equal(new Rational(3, 4), rtp);
        Assert.Equal("3/4", rtp.ToString());
    }

    [Fact]
    public void Comparison_Works()
    {
        Assert.True(new Rational(1, 3) < new Rational(1, 2));
        Assert.True(new Rational(2, 4) == new Rational(1, 2));
        Assert.True(new Rational(-1, 2) < Rational.Zero);
    }

    [Fact]
    public void DivisionByZeroRational_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => new Rational(1, 2) / Rational.Zero);
    }

    [Theory]
    [InlineData("3/4", 3, 4)]
    [InlineData("6/8", 3, 4)]
    [InlineData("-1/2", -1, 2)]
    [InlineData("5", 5, 1)]
    public void Parse_RoundTrips(string input, int expectNum, int expectDen)
    {
        var r = Rational.Parse(input);
        Assert.Equal(new BigInteger(expectNum), r.Numerator);
        Assert.Equal(new BigInteger(expectDen), r.Denominator);
    }

    [Fact]
    public void ImplicitFromIntegers()
    {
        Rational r = new BigInteger(5);
        Assert.Equal(new Rational(5, 1), r);
        Rational q = 7L;
        Assert.Equal(new Rational(7, 1), q);
    }
}
