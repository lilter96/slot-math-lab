using System.Numerics;

namespace SlotMath.Core.Math;

/// <summary>
///     Immutable risk analysis of a complete discrete settled-payout law. Values and moments are in
///     credits after division by WinScale. Quantiles use the left inverse CDF; expected shortfall
///     includes fractional probability at ties. Exactness describes this supplied law, not an
///     unverified game's equivalence to it. A pruned law must not be silently renormalized.
/// </summary>
public sealed class ExactPayoutLaw
{
    private readonly BigInteger _denominator, _scale;
    private readonly BigInteger[] _values, _prefixMass, _prefixWin;

    private ExactPayoutLaw(Dist<BigInteger> distribution, BigInteger winScale, CancellationToken cancellation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(winScale);
        if (distribution.IsEmpty || distribution.Count > 1000000 || distribution.Denominator.Sign <= 0 ||
            !distribution.PrunedNumerator.IsZero || distribution.TotalNumerator != distribution.Denominator)
        {
            throw new ArgumentException("Risk analysis requires a complete normalized payout law of 1..1000000 atoms.");
        }

        cancellation.ThrowIfCancellationRequested();
        Dist<BigInteger>.Entry[] atoms = distribution.Entries.ToArray();
        foreach (Dist<BigInteger>.Entry atom in atoms)
        {
            cancellation.ThrowIfCancellationRequested();
            if (atom.Numerator.Sign < 0 || (atom.Numerator.Sign > 0 && atom.Value.Sign < 0))
            {
                throw new ArgumentException("Settled payouts and their probability masses must be nonnegative.");
            }
        }

        atoms = atoms.Where(atom => atom.Numerator.Sign > 0).OrderBy(atom => atom.Value).ToArray();
        cancellation.ThrowIfCancellationRequested();
        if (atoms.Length == 0)
        {
            throw new ArgumentException("The payout law has no positive probability.");
        }

        _scale = winScale;
        _denominator = distribution.Denominator;
        _values = new BigInteger[atoms.Length];
        _prefixMass = new BigInteger[atoms.Length];
        _prefixWin = new BigInteger[atoms.Length];
        BigInteger mass = 0, first = 0, second = 0, third = 0, fourth = 0;
        for (var i = 0; i < atoms.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            Dist<BigInteger>.Entry atom = atoms[i];
            BigInteger squared = atom.Value * atom.Value;
            mass += atom.Numerator;
            first += atom.Value * atom.Numerator;
            second += squared * atom.Numerator;
            third += squared * atom.Value * atom.Numerator;
            fourth += squared * squared * atom.Numerator;
            _values[i] = atom.Value;
            _prefixMass[i] = mass;
            _prefixWin[i] = first;
        }

        if (mass != _denominator)
        {
            throw new ArgumentException("The actual payout masses do not sum to the probability denominator.");
        }

        Mean = new Rational(first, _denominator * _scale);
        SecondMoment = new Rational(second, _denominator * _scale * _scale);
        ThirdMoment = new Rational(third, _denominator * BigInteger.Pow(_scale, 3));
        FourthMoment = new Rational(fourth, _denominator * BigInteger.Pow(_scale, 4));
        Variance = SecondMoment - Mean * Mean;
        CentralThirdMoment = ThirdMoment - 3 * Mean * SecondMoment + 2 * Mean * Mean * Mean;
        CentralFourthMoment = FourthMoment - 4 * Mean * ThirdMoment + 6 * Mean * Mean * SecondMoment
                              - 3 * Mean * Mean * Mean * Mean;
    }

    public Rational Mean { get; }
    public Rational SecondMoment { get; }
    public Rational ThirdMoment { get; }
    public Rational FourthMoment { get; }
    public Rational Variance { get; }
    public Rational CentralThirdMoment { get; }
    public Rational CentralFourthMoment { get; }
    public Rational Minimum => new(_values[0], _scale);
    public Rational Maximum => new(_values[^1], _scale);
    public Rational? CoefficientOfVariationSquared => Mean.IsZero ? null : Variance / (Mean * Mean);

    public Rational? SkewnessSquared => Variance.IsZero
        ? null
        : CentralThirdMoment * CentralThirdMoment / (Variance * Variance * Variance);

    public int? SkewnessSign => Variance.IsZero ? null : CentralThirdMoment.Sign;
    public Rational? ExcessKurtosis => Variance.IsZero ? null : CentralFourthMoment / (Variance * Variance) - 3;

    public static ExactPayoutLaw Create(Dist<BigInteger> distribution, BigInteger winScale,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(distribution);
        return new ExactPayoutLaw(distribution, winScale, cancellation);
    }

    /// <summary>P(W &lt;= amount) in original credit units.</summary>
    public Rational Cdf(Rational amount) => ProbabilityBelow(amount, true);

    /// <summary>P(W &gt;= amount), including mass at the threshold.</summary>
    public Rational SurvivalInclusive(Rational amount) => Rational.One - ProbabilityBelow(amount, false);

    public Rational Quantile(Rational probability)
    {
        Validate(probability);
        if (probability <= Rational.Zero || probability > Rational.One)
        {
            throw new ArgumentOutOfRangeException(nameof(probability), "Quantiles require 0 < probability <= 1.");
        }

        return new Rational(_values[QuantileIndex(probability)], _scale);
    }

    /// <summary>Mean of exactly the highest 1-probability mass, splitting the boundary atom when needed.</summary>
    public Rational ExpectedShortfall(Rational probability)
    {
        Validate(probability);
        if (probability < Rational.Zero || probability >= Rational.One)
        {
            throw new ArgumentOutOfRangeException(nameof(probability),
                "Expected shortfall requires 0 <= probability < 1.");
        }

        var index = QuantileIndex(probability);
        Rational tail = Rational.One - probability;
        var above = new Rational(_denominator - _prefixMass[index], _denominator);
        var aboveWin = new Rational(_prefixWin[^1] - _prefixWin[index], _denominator * _scale);
        return (aboveWin + (tail - above) * new Rational(_values[index], _scale)) / tail;
    }

    public Rational Rtp(Rational stake) => Mean / Stake(stake);
    public Rational OperatorLossQuantile(Rational probability, Rational stake) => Quantile(probability) - Stake(stake);

    public Rational OperatorLossExpectedShortfall(Rational probability, Rational stake) =>
        ExpectedShortfall(probability) - Stake(stake);

    private int QuantileIndex(Rational probability)
    {
        var lo = 0;
        var hi = _values.Length - 1;
        BigInteger target = probability.Numerator * _denominator;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (_prefixMass[mid] * probability.Denominator >= target)
            {
                hi = mid;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return lo;
    }

    private Rational ProbabilityBelow(Rational amount, bool inclusive)
    {
        Validate(amount);
        var lo = 0;
        var hi = _values.Length;
        BigInteger target = amount.Numerator * _scale;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            BigInteger value = _values[mid] * amount.Denominator;
            if (value < target || (inclusive && value == target))
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo == 0 ? Rational.Zero : new Rational(_prefixMass[lo - 1], _denominator);
    }

    private static void Validate(Rational value)
    {
        if (value.Denominator.Sign <= 0)
        {
            throw new ArgumentException("A canonical rational value is required.");
        }
    }

    private static Rational Stake(Rational stake)
    {
        Validate(stake);
        if (stake <= Rational.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(stake));
        }

        return stake;
    }
}
