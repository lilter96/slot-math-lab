using M = System.Math;

namespace SlotMath.Core.Measurements;

/// <summary>Inference for declared independent subjects. Methods carry their assumptions;
/// a descriptive event stream is never silently treated as independent Bernoulli trials.</summary>
public static class StatisticalInference
{
    /// <summary>Upper tail of a chi-square law, Q(df/2, statistic/2).
    /// Positive-term lower series and upper continued fraction (NIST DLMF 8.7/8.9).
    /// This evaluates the distribution, not the validity of a goodness-of-fit model.</summary>
    public static double ChiSquareSurvival(double statistic, int degreesOfFreedom)
    {
        if (!double.IsFinite(statistic) || statistic < 0 || degreesOfFreedom < 1) throw new ArgumentOutOfRangeException(nameof(statistic));
        if (statistic == 0) return 1;
        var a = degreesOfFreedom / 2d; var x = statistic / 2;
        var factor = M.Exp(a * M.Log(x) - x - LogGamma(a));
        if (x < a + 1)
        {
            var term = 1 / a; var sum = term;
            for (var i = 1; i <= 10000; i++)
            { term *= x / (a + i); sum += term; if (term < sum * 2e-15) return M.Clamp(1 - factor * sum, 0, 1); }
        }
        else
        {
            const double tiny = 1e-300; var b = x + 1 - a; var c = 1 / tiny; var d = 1 / b; var h = d;
            for (var i = 1; i <= 10000; i++)
            {
                var numerator = -i * (i - a); b += 2;
                d = numerator * d + b; if (M.Abs(d) < tiny) d = tiny;
                c = b + numerator / c; if (M.Abs(c) < tiny) c = tiny;
                d = 1 / d; var delta = d * c; h *= delta;
                if (M.Abs(delta - 1) < 2e-15) return M.Clamp(factor * h, 0, 1);
            }
        }
        throw new ArithmeticException("Chi-square tail did not converge within its bounded budget.");
    }
    public static NumericInterval ExactBinomial(long successes, long trials, double alpha = 0.05)
    {
        if (trials <= 0 || successes < 0 || successes > trials || !(alpha > 0 && alpha < 1))
            throw new ArgumentOutOfRangeException(nameof(trials));
        var lower = successes == 0 ? 0 : successes == trials ? M.Exp(M.Log(alpha / 2) / trials) : BetaQuantile(alpha / 2, successes, trials - successes + 1);
        var upper = successes == trials ? 1 : successes == 0 ? ZeroEventUpperBound(trials, alpha / 2) : BetaQuantile(1 - alpha / 2, successes + 1, trials - successes);
        return new(lower, upper, "Clopper–Pearson, two-sided", "Independent Bernoulli subjects; fixed trial count; family-adjusted alpha.");
    }

    public static double ZeroEventUpperBound(long trials, double alpha = 0.05)
    {
        if (trials <= 0 || !(alpha > 0 && alpha < 1)) throw new ArgumentOutOfRangeException(nameof(trials));
        // Stable when n is large and the upper bound is tiny.
        var x = M.Log(alpha) / trials;
        return M.Abs(x) < 1e-5 ? -(x + x * x / 2 + x * x * x / 6) : 1 - M.Exp(x);
    }

    public static NumericInterval SequentialMean(double mean, long count, double lower, double upper, double alpha)
    {
        if (count <= 0 || !double.IsFinite(lower) || !double.IsFinite(upper) || upper < lower || !(alpha > 0 && alpha < 1))
            throw new ArgumentOutOfRangeException(nameof(count));
        // Hoeffding at time n with alpha_n=6*alpha/(pi²*n²); union bound over ALL n.
        // This deliberately conservative confidence sequence needs a proven bounded value.
        var radius = (upper - lower) * M.Sqrt(M.Log(M.PI * M.PI * (double)count * count / (3 * alpha)) / (2 * count));
        return new(M.Max(lower, mean - radius), M.Min(upper, mean + radius), "Time-uniform bounded Hoeffding / alpha spending",
            "Independent subjects; authored bounds hold for every possible value; valid at repeated looks.");
    }

    public static double NormalCritical(double alpha)
    {
        if (!(alpha > 0 && alpha < 1)) throw new ArgumentOutOfRangeException(nameof(alpha));
        // Invert the normal survival function with a bounded bisection; no extreme-tail cancellation.
        var target = alpha / 2; double lo = 0, hi = 12;
        for (var i = 0; i < 70; i++) { var mid = (lo + hi) / 2; if (NormalSurvival(mid) > target) lo = mid; else hi = mid; }
        return (lo + hi) / 2;
    }
    private static double NormalSurvival(double x)
    {
        // Abramowitz–Stegun 26.2.17; max absolute error < 7.5e-8.
        var t = 1 / (1 + 0.2316419 * x);
        return M.Exp(-x * x / 2) / M.Sqrt(2 * M.PI) * t * (0.319381530 + t * (-0.356563782 + t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
    }
    private static double BetaQuantile(double p, double a, double b)
    {
        double lo = 0, hi = 1;
        for (var i = 0; i < 80; i++) { var mid = (lo + hi) / 2; if (BetaCdf(mid, a, b) < p) lo = mid; else hi = mid; }
        return (lo + hi) / 2;
    }
    private static double BetaCdf(double x, double a, double b)
    {
        if (x <= 0) return 0; if (x >= 1) return 1;
        var front = M.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) + a * M.Log(x) + b * M.Log(1 - x));
        return x < (a + 1) / (a + b + 2) ? front * BetaFraction(x, a, b) / a : 1 - front * BetaFraction(1 - x, b, a) / b;
    }
    private static double BetaFraction(double x, double a, double b)
    {
        const double tiny = 1e-300; var qab = a + b; var qap = a + 1; var qam = a - 1;
        var c = 1d; var d = 1 - qab * x / qap; if (M.Abs(d) < tiny) d = tiny; d = 1 / d; var h = d;
        for (var m = 1; m <= 10000; m++)
        {
            var aa = m * (b - m) * x / ((qam + 2 * m) * (a + 2 * m));
            d = 1 + aa * d; if (M.Abs(d) < tiny) d = tiny; c = 1 + aa / c; if (M.Abs(c) < tiny) c = tiny; d = 1 / d; h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + 2 * m) * (qap + 2 * m));
            d = 1 + aa * d; if (M.Abs(d) < tiny) d = tiny; c = 1 + aa / c; if (M.Abs(c) < tiny) c = tiny; d = 1 / d;
            var delta = d * c; h *= delta; if (M.Abs(delta - 1) < 3e-14) return h;
        }
        throw new ArithmeticException("Exact binomial interval did not converge.");
    }
    private static double LogGamma(double z)
    {
        ReadOnlySpan<double> coefficients = [676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7];
        z -= 1; var x = 0.99999999999980993;
        for (var i = 0; i < coefficients.Length; i++) x += coefficients[i] / (z + i + 1);
        var t = z + 7.5; return 0.9189385332046727 + (z + 0.5) * M.Log(t) - t + M.Log(x);
    }
}
