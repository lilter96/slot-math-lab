using System.Numerics;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;

namespace SlotMath.Api.Features.Evaluate;

/// <summary>Optional exact population analysis. Rational inputs accept integers or n/d, never display floats.</summary>
public sealed record PayoutAnalysisRequest(
    int MomentOrder = 4,
    string[]? TailProbabilities = null,
    string[]? PayoutThresholds = null,
    string Stake = "1",
    string? RtpHalfWidth = null);

public sealed record ExactNumberOutput(string Rational, double? Display);

public sealed record TailRiskOutput(
    string Probability,
    ExactNumberOutput PayoutQuantile,
    ExactNumberOutput PayoutExpectedShortfall,
    ExactNumberOutput OperatorLossQuantile,
    ExactNumberOutput OperatorLossExpectedShortfall);

public sealed record PayoutThresholdOutput(
    string Threshold,
    ExactNumberOutput Cdf,
    ExactNumberOutput SurvivalInclusive);

public sealed record RtpPlanningOutput(
    string Stake,
    string RtpHalfWidth,
    string Normal95Rounds,
    string Chebyshev95Rounds,
    string Assumptions);

public sealed record PayoutAnalysisOutput
{
    public required string Status { get; init; }
    public string? Reason { get; init; }
    public string Units { get; init; } = "credits";
    public string? Stake { get; init; }
    public ExactNumberOutput? RtpForStake { get; init; }
    public ExactNumberOutput? Mean { get; init; }
    public ExactNumberOutput? SecondMoment { get; init; }
    public ExactNumberOutput? ThirdMoment { get; init; }
    public ExactNumberOutput? FourthMoment { get; init; }
    public ExactNumberOutput? Variance { get; init; }
    public ExactNumberOutput? CentralThirdMoment { get; init; }
    public ExactNumberOutput? CentralFourthMoment { get; init; }
    public ExactNumberOutput? CoefficientOfVariationSquared { get; init; }
    public double? CoefficientOfVariation { get; init; }
    public double? Skewness { get; init; }
    public ExactNumberOutput? SkewnessSquared { get; init; }
    public ExactNumberOutput? ExcessKurtosis { get; init; }
    public TailRiskOutput[]? Tails { get; init; }
    public PayoutThresholdOutput[]? Thresholds { get; init; }
    public RtpPlanningOutput? Planning { get; init; }
}

public static class PayoutAnalysis
{
    private static readonly string[] DefaultProbabilities = ["19/20", "99/100"];

    public static bool IsValid(PayoutAnalysisRequest? request)
    {
        if (request is null)
        {
            return true;
        }

        return request.MomentOrder is >= 2 and <= 4 &&
               TryRational(request.Stake, out Rational stake) && stake > Rational.Zero &&
               (request.RtpHalfWidth is null ||
                (TryRational(request.RtpHalfWidth, out Rational width) && width > Rational.Zero)) &&
               ValidValues(request.TailProbabilities, value => value > Rational.Zero && value < Rational.One) &&
               ValidValues(request.PayoutThresholds, _ => true);
    }

    private static bool ValidValues(string[]? values, Func<Rational, bool> predicate) => values is null ||
        (values.Length <= 16 && values.All(text => TryRational(text, out Rational value) && predicate(value)));

    private static bool TryRational(string? text, out Rational value)
    {
        value = Rational.Zero;
        if (text is null || text.Length is 0 or > 160)
        {
            return false;
        }

        try
        {
            value = Rational.Parse(text);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or DivideByZeroException or OverflowException)
        {
            return false;
        }
    }

    public static PayoutAnalysisOutput? Build(PayoutAnalysisRequest? request, Dist<BigInteger>? distribution,
        StructuredExactResult? structured, BigInteger scale, CancellationToken cancellation = default)
    {
        if (request is null)
        {
            return null;
        }

        if (!IsValid(request))
        {
            throw new ArgumentException("Invalid payout analysis inputs.", nameof(request));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        cancellation.ThrowIfCancellationRequested();
        if (structured is { PrunedMass.IsZero: false } || distribution is { IsFullyExact: false })
        {
            return Unavailable("Pruned probability mass prevents exact population risk analysis.");
        }

        ExactPayoutLaw? law = null;
        if (distribution is not null)
        {
            if (distribution.Count > 1000000)
            {
                return Unavailable("The retained law exceeds the risk analysis atom limit.");
            }

            law = ExactPayoutLaw.Create(distribution, scale, cancellation);
        }

        if (law is null && structured is not { Succeeded: true })
        {
            return Unavailable(
                "No complete exact payout law or exact graph moments are retained; sampled reports are not population risk.");
        }

        Rational InCredits(Rational value, int order)
        {
            return value / new Rational(BigInteger.Pow(scale, order), 1);
        }

        Rational mean = law?.Mean ?? InCredits(structured!.ExpectedWin, 1);
        Rational? second = law?.SecondMoment ?? (structured!.SecondMoment is { } m2 ? InCredits(m2, 2) : null);
        Rational? third = request.MomentOrder >= 3
            ? law?.ThirdMoment ?? (structured!.ThirdMoment is { } m3 ? InCredits(m3, 3) : null)
            : null;
        Rational? fourth = request.MomentOrder >= 4
            ? law?.FourthMoment ?? (structured!.FourthMoment is { } m4 ? InCredits(m4, 4) : null)
            : null;
        Rational? variance = second - mean * mean;
        Rational? central3 = third - 3 * mean * second + 2 * mean * mean * mean;
        Rational? central4 = fourth - 4 * mean * third + 6 * mean * mean * second - 3 * mean * mean * mean * mean;
        Rational? cvSquared = mean.IsZero ? null : variance / (mean * mean);
        Rational? skewSquared = variance is { IsZero: false }
            ? central3 * central3 / (variance * variance * variance)
            : null;
        Rational? excess = variance is { IsZero: false } ? central4 / (variance * variance) - 3 : null;
        Rational stake = Rational.Parse(request.Stake);
        TailRiskOutput[]? tails = law is null
            ? null
            : (request.TailProbabilities ?? DefaultProbabilities).Select(text =>
            {
                cancellation.ThrowIfCancellationRequested();
                Rational probability = Rational.Parse(text);
                return new TailRiskOutput(probability.ToString(), Number(law.Quantile(probability))!,
                    Number(law.ExpectedShortfall(probability))!, Number(law.OperatorLossQuantile(probability, stake))!,
                    Number(law.OperatorLossExpectedShortfall(probability, stake))!);
            }).ToArray();
        PayoutThresholdOutput[]? thresholds = law is null
            ? null
            : (request.PayoutThresholds ?? []).Select(text =>
            {
                cancellation.ThrowIfCancellationRequested();
                Rational threshold = Rational.Parse(text);
                return new PayoutThresholdOutput(threshold.ToString(), Number(law.Cdf(threshold))!,
                    Number(law.SurvivalInclusive(threshold))!);
            }).ToArray();
        RtpPlanningOutput? planning = null;
        if (request.RtpHalfWidth is { } halfWidth && variance is { } knownVariance)
        {
            Rational width = Rational.Parse(halfWidth);
            Rational ratio = knownVariance / (stake * stake * width * width);
            planning = new RtpPlanningOutput(stake.ToString(), width.ToString(),
                Ceiling(ratio * new Rational(2401, 625)),
                Ceiling(ratio * 20),
                "Independent identically distributed rounds, fixed positive stake, fixed sample count and known population variance. " +
                "Normal95 uses z=1.96 and is an approximation. Chebyshev95 is a conservative finite-sample bound. " +
                "Neither count supports repeated optional monitoring or correlated rounds.");
        }

        return new PayoutAnalysisOutput
        {
            Status = law is null ? "ExactMoments" : "ExactLaw",
            Reason = law is null
                ? "The complete payout law was not built; moments do not determine quantiles or expected shortfall."
                : null,
            Stake = stake.ToString(),
            RtpForStake = Number(mean / stake),
            Mean = Number(mean),
            SecondMoment = Number(second),
            ThirdMoment = Number(third),
            FourthMoment = Number(fourth),
            Variance = Number(variance),
            CentralThirdMoment = Number(central3),
            CentralFourthMoment = Number(central4),
            CoefficientOfVariationSquared = Number(cvSquared),
            CoefficientOfVariation = SquareRoot(cvSquared),
            Skewness = SquareRoot(skewSquared) is { } absolute ? central3!.Value.Sign * absolute : null,
            SkewnessSquared = Number(skewSquared),
            ExcessKurtosis = Number(excess),
            Tails = tails,
            Thresholds = thresholds,
            Planning = planning
        };
    }

    private static PayoutAnalysisOutput Unavailable(string reason) => new()
    {
        Status = "Unavailable",
        Reason = reason
    };

    private static string Ceiling(Rational value) => BigInteger.Max(BigInteger.One,
            BigInteger.DivRem(value.Numerator, value.Denominator, out BigInteger remainder) +
            (remainder.IsZero ? 0 : 1))
        .ToString();

    private static double? SquareRoot(Rational? value)
    {
        if (value is not { } rational || rational.Sign < 0)
        {
            return null;
        }

        if (rational.IsZero)
        {
            return 0;
        }

        // Converting the square to binary64 first can lose a representable root:
        // sqrt(10^-400) is 10^-200, and sqrt(10^400) is 10^200.
        var exponent = rational.Numerator.GetBitLength() - rational.Denominator.GetBitLength();
        if (exponent > 2048)
        {
            return null;
        }

        if (exponent < -2150)
        {
            return 0;
        }

        var shift = (int)exponent >> 1;
        Rational normalized = shift >= 0
            ? new Rational(rational.Numerator, rational.Denominator << (2 * shift))
            : new Rational(rational.Numerator << (-2 * shift), rational.Denominator);
        return Finite(Math.ScaleB(Math.Sqrt(normalized.ToDouble()), shift));
    }

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;

    private static ExactNumberOutput? Number(Rational? value) => value is { } rational
        ? new ExactNumberOutput(rational.ToString(), Finite(rational.ToDouble()))
        : null;
}
