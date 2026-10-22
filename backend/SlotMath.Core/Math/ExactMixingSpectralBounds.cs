namespace SlotMath.Core.Math;

/// <summary>
///     Spectral bounds for the zero-mass restriction of a supplied stochastic kernel.
///     The root bracket encloses Factor^(1/BlockSteps), not an actual eigenvalue.
///     Eigenvalue magnitudes are at most ContractionRootUpper; the absolute spectral gap is at least
///     AbsoluteGapLowerBound.
///     These diagnostics do not replace the certificate's finite-time mixing bounds.
/// </summary>
public sealed class ExactMixingSpectralBounds
{
    private ExactMixingSpectralBounds(ExactMixingCertificate certificate, Rational lower, Rational upper,
        Rational gap, long work)
    {
        Certificate = certificate;
        ContractionRootLower = lower;
        ContractionRootUpper = upper;
        AbsoluteGapLowerBound = gap;
        Work = work;
    }

    public ExactMixingCertificate Certificate { get; }
    public Rational ContractionRootLower { get; }
    public Rational ContractionRootUpper { get; }
    public Rational AbsoluteGapLowerBound { get; }
    public bool ProvesPositiveGap => AbsoluteGapLowerBound > Rational.Zero;

    /// <summary>Scalar enclosure work only; certificate construction has its own Work.</summary>
    public long Work { get; }

    internal static ExactMixingSpectralBounds Create(ExactMixingCertificate certificate, int precisionBits,
        ExactLinearOptions? options, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (precisionBits is < 1 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(precisionBits));
        }

        var budget = new ExactLinearBudget(options, cancellation);
        Rational factor = certificate.Factor;
        budget.Value(factor);
        Rational lower = factor, upper = factor;
        if (certificate.BlockSteps != 1 && factor != Rational.Zero && factor != Rational.One)
        {
            lower = Rational.Zero;
            upper = Rational.One;
            var half = new Rational(1, 2);
            budget.Value(half);
            for (var bit = 0; bit < precisionBits; bit++)
            {
                Rational middle = budget.Multiply(budget.Add(lower, upper), half);
                Rational power = ExactChainArithmetic.Power(middle, certificate.BlockSteps, ref budget);
                if (power == factor)
                {
                    lower = upper = middle;
                    break;
                }

                if (budget.Less(power, factor))
                {
                    lower = middle;
                }
                else
                {
                    upper = middle;
                }
            }
        }

        Rational gap = budget.Add(Rational.One, -upper);
        budget.Check();
        return new ExactMixingSpectralBounds(certificate, lower, upper, gap, budget.Work);
    }
}
