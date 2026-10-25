using System.Numerics;

namespace SlotMath.Core.Math;

public sealed record ExposureLawAtom(Rational Loss, Rational Probability);

/// <summary>
///     A complete immutable law of signed operator loss in currency units. Positive loss consumes
///     reserve; negative loss is operator profit. Quantiles use the left inverse CDF and ES splits
///     boundary probability. Translating the support preserves central moments and tail ordering.
/// </summary>
public sealed class ExactExposureLaw
{
    private readonly Rational _offset;
    private readonly ExactPayoutLaw _translated;

    private ExactExposureLaw(Dist<BigInteger> distribution, BigInteger scale, CancellationToken cancellation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        if (distribution.IsEmpty || distribution.Count > 1000000 || !distribution.IsFullyExact ||
            distribution.Denominator.Sign <= 0 || distribution.TotalNumerator != distribution.Denominator)
        {
            throw new ArgumentException("Exposure requires a complete normalized law of 1..1000000 atoms.");
        }

        cancellation.ThrowIfCancellationRequested();
        Dist<BigInteger>.Entry[] atoms = distribution.Entries.ToArray();
        if (atoms.Any(atom => atom.Numerator.Sign < 0))
        {
            throw new ArgumentException("Negative probability mass.");
        }

        atoms = atoms.Where(atom => atom.Numerator.Sign > 0).OrderBy(atom => atom.Value).ToArray();
        if (atoms.Length == 0)
        {
            throw new ArgumentException("No positive probability mass.");
        }

        BigInteger offset = atoms[0].Value;
        _offset = new Rational(offset, scale);
        var builder = new DistBuilder<BigInteger>();
        var exposed = new ExposureLawAtom[atoms.Length];
        for (var i = 0; i < atoms.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            Dist<BigInteger>.Entry atom = atoms[i];
            builder.Add(atom.Value - offset, atom.Numerator, distribution.Denominator);
            exposed[i] = new ExposureLawAtom(new Rational(atom.Value, scale),
                new Rational(atom.Numerator, distribution.Denominator));
        }

        _translated = ExactPayoutLaw.Create(builder.Build(), scale, cancellation);
        Atoms = Array.AsReadOnly(exposed);
        Mean = _translated.Mean + _offset;
        SecondMoment = _translated.SecondMoment + 2 * _offset * _translated.Mean + _offset * _offset;
        ThirdMoment = _translated.ThirdMoment + 3 * _offset * _translated.SecondMoment
                                              + 3 * _offset * _offset * _translated.Mean + _offset * _offset * _offset;
        FourthMoment = _translated.FourthMoment + 4 * _offset * _translated.ThirdMoment
                                                + 6 * _offset * _offset * _translated.SecondMoment
                                                + 4 * _offset * _offset * _offset * _translated.Mean
                                                + _offset * _offset * _offset * _offset;
    }

    public IReadOnlyList<ExposureLawAtom> Atoms { get; }
    public Rational Mean { get; }
    public Rational SecondMoment { get; }
    public Rational ThirdMoment { get; }
    public Rational FourthMoment { get; }
    public Rational Variance => _translated.Variance;
    public Rational CentralThirdMoment => _translated.CentralThirdMoment;
    public Rational CentralFourthMoment => _translated.CentralFourthMoment;
    public Rational? SkewnessSquared => _translated.SkewnessSquared;
    public int? SkewnessSign => _translated.SkewnessSign;
    public Rational? ExcessKurtosis => _translated.ExcessKurtosis;
    public Rational Minimum => _translated.Minimum + _offset;
    public Rational Maximum => _translated.Maximum + _offset;

    public static ExactExposureLaw Create(Dist<BigInteger> distribution, BigInteger lossScale,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(distribution);
        return new ExactExposureLaw(distribution, lossScale, cancellation);
    }

    public Rational Cdf(Rational loss) => _translated.Cdf(Valid(loss) - _offset);
    public Rational SurvivalInclusive(Rational loss) => _translated.SurvivalInclusive(Valid(loss) - _offset);
    public Rational Quantile(Rational probability) => _translated.Quantile(probability) + _offset;
    public Rational ExpectedShortfall(Rational probability) => _translated.ExpectedShortfall(probability) + _offset;

    private static Rational Valid(Rational value) => value.Denominator.Sign > 0
        ? value
        : throw new ArgumentException("A canonical rational amount is required.");
}
