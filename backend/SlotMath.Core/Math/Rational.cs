using System.Globalization;
using System.Numerics;

namespace SlotMath.Core.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  Rational — exact rational number (PRD v3.1, D1)
//
//  A BigInteger numerator over a BigInteger denominator, ALWAYS reduced to
//  lowest terms with denominator > 0; canonical zero is 0/1.  All exact-path
//  probabilities and amounts are rationals in this form.  Display floats are
//  derived only at the edge (ToDouble) and never re-enter computation.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// An exact rational number in canonical form (D1): lowest terms, denominator
/// &gt; 0, canonical zero = 0/1. Immutable.
/// </summary>
public readonly struct Rational : IEquatable<Rational>, IComparable<Rational>
{
    /// <summary>Numerator (sign lives here).</summary>
    public BigInteger Numerator { get; }

    /// <summary>Denominator, always &gt; 0.</summary>
    public BigInteger Denominator { get; }

    /// <summary>Canonical zero (0/1).</summary>
    public static Rational Zero { get; } = new(BigInteger.Zero, BigInteger.One, alreadyCanonical: true);

    /// <summary>Canonical one (1/1).</summary>
    public static Rational One { get; } = new(BigInteger.One, BigInteger.One, alreadyCanonical: true);

    public Rational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
            throw new DivideByZeroException("Rational denominator must be non-zero.");

        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        if (numerator.IsZero)
        {
            Numerator = BigInteger.Zero;
            Denominator = BigInteger.One;
            return;
        }

        var g = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / g;
        Denominator = denominator / g;
    }

    private Rational(BigInteger numerator, BigInteger denominator, bool alreadyCanonical)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    public static implicit operator Rational(BigInteger value) => new(value, BigInteger.One, alreadyCanonical: true);
    public static implicit operator Rational(long value) => new(value, BigInteger.One, alreadyCanonical: true);
    public static implicit operator Rational(int value) => new(value, BigInteger.One, alreadyCanonical: true);

    public bool IsZero => Numerator.IsZero;
    public int Sign => Numerator.Sign;

    public static Rational operator +(Rational a, Rational b) =>
        new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);

    public static Rational operator -(Rational a, Rational b) =>
        new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);

    public static Rational operator -(Rational a) => new(-a.Numerator, a.Denominator, alreadyCanonical: true);

    public static Rational operator *(Rational a, Rational b) =>
        new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    public static Rational operator /(Rational a, Rational b)
    {
        if (b.IsZero)
            throw new DivideByZeroException("Division by zero rational.");
        return new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    }

    public static bool operator ==(Rational a, Rational b) => a.Equals(b);
    public static bool operator !=(Rational a, Rational b) => !a.Equals(b);
    public static bool operator <(Rational a, Rational b) => a.CompareTo(b) < 0;
    public static bool operator >(Rational a, Rational b) => a.CompareTo(b) > 0;
    public static bool operator <=(Rational a, Rational b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Rational a, Rational b) => a.CompareTo(b) >= 0;

    public int CompareTo(Rational other) =>
        (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public bool Equals(Rational other) =>
        Numerator == other.Numerator && Denominator == other.Denominator;

    public override bool Equals(object? obj) => obj is Rational r && Equals(r);

    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    /// <summary>Display float (D1) — derived at the edge, never fed back into computation.</summary>
    public double ToDouble()
    {
        if (Numerator.IsZero) return 0;
        var numerator = BigInteger.Abs(Numerator);
        var exponentLong = numerator.GetBitLength() - Denominator.GetBitLength();
        if (exponentLong > 1024) return Numerator.Sign * double.PositiveInfinity;
        if (exponentLong < -1075) return Numerator.Sign * 0.0;
        var exponent = (int)exponentLong;
        if (exponent >= 0 ? numerator < (Denominator << exponent) : (numerator << -exponent) < Denominator)
            exponent--;
        var shift = exponent < -1022 ? 1074 : 52 - exponent;
        var scaledNumerator = shift >= 0 ? numerator << shift : numerator;
        var scaledDenominator = shift >= 0 ? Denominator : Denominator << -shift;
        var mantissa = BigInteger.DivRem(scaledNumerator, scaledDenominator, out var remainder);
        var comparison = (remainder * 2).CompareTo(scaledDenominator);
        if (comparison > 0 || comparison == 0 && !mantissa.IsEven) mantissa++;
        return Numerator.Sign * System.Math.ScaleB((double)mantissa, -shift);
    }

    /// <summary>Canonical string form "n/d" (D1, D2).</summary>
    public override string ToString() => $"{Numerator}/{Denominator}";

    /// <summary>Parse the canonical "n/d" string (also accepts a bare integer "n").</summary>
    public static Rational Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var slash = s.IndexOf('/');
        if (slash < 0)
            return new Rational(BigInteger.Parse(s, CultureInfo.InvariantCulture), BigInteger.One);
        var num = BigInteger.Parse(s[..slash], CultureInfo.InvariantCulture);
        var den = BigInteger.Parse(s[(slash + 1)..], CultureInfo.InvariantCulture);
        return new Rational(num, den);
    }

    // ── Shared BigInteger rational-arithmetic helpers ────────────────────
    //   (Used by Dist and ExactInterpreter; kept as static methods on the
    //    Rational type so existing Rational.Reduce/Gcd/Lcm call sites resolve.)

    /// <summary>Reduce a fraction (num/den) to lowest terms (canonical zero = 0/1).</summary>
    public static (BigInteger Num, BigInteger Den) Reduce(BigInteger num, BigInteger den)
    {
        if (den == 0)
            throw new ArgumentException("Denominator cannot be zero.");
        if (num == 0)
            return (0, 1);

        var g = Gcd(BigInteger.Abs(num), BigInteger.Abs(den));
        var sign = den < 0 ? -1 : 1;
        return (sign * num / g, BigInteger.Abs(den) / g);
    }

    /// <summary>Greatest common divisor.</summary>
    public static BigInteger Gcd(BigInteger a, BigInteger b)
    {
        a = BigInteger.Abs(a);
        b = BigInteger.Abs(b);
        while (b != 0)
        {
            var t = b;
            b = a % b;
            a = t;
        }
        return a;
    }

    /// <summary>Least common multiple.</summary>
    public static BigInteger Lcm(BigInteger a, BigInteger b)
    {
        if (a == 0 || b == 0) return 1;
        return a / Gcd(a, b) * b;
    }
}
