namespace SlotMath.Core.Measurements;
public sealed record SamplePlanRequest(double Confidence = 0.95, int ErrorFamilySize = 1, double Precision = 0.001,
    double? Variance = null, double? LowerBound = null, double? UpperBound = null,
    double? EventProbability = null, double DetectionPower = 0.95, long RoundBudget = 100000);
public sealed record SamplePlanReport(double AllocatedAlpha, double NormalCritical, double? ApproximateMeanRounds,
    long? TimeUniformBoundedMeanRounds, double? EventDetectionRounds, double ZeroObservedEventUpper,
    bool? MeanBudgetSufficient, bool? DetectionBudgetSufficient, string Assumptions)
{ public string? AuthoredInputSha256 { get; init; } public string? CoreBinarySha256 { get; init; } }

public static class SamplePlanning
{
    public static SamplePlanReport Calculate(SamplePlanRequest request)
    {
        if (!(request.Confidence > 0.5 && request.Confidence < 0.999999) || request.ErrorFamilySize is < 1 or > 256
            || !(request.Precision > 0) || !double.IsFinite(request.Precision) || request.RoundBudget is < 1 or > 1000000000000
            || request.Variance is { } v && (!double.IsFinite(v) || v < 0)
            || request.LowerBound is { } l && !double.IsFinite(l) || request.UpperBound is { } h && !double.IsFinite(h)
            || request.LowerBound > request.UpperBound || request.EventProbability is { } p && !(p > 0 && p <= 1)
            || !(request.DetectionPower > 0 && request.DetectionPower < 1)) throw new ArgumentException("Use finite precision, ordered proven bounds, valid confidence/family and an independent-subject budget of 1..10¹².");
        var alpha = (1 - request.Confidence) / request.ErrorFamilySize; var z = StatisticalInference.NormalCritical(alpha);
        double? normal = request.Variance is > 0 ? Finite(System.Math.Ceiling(z * z * request.Variance.Value / (request.Precision * request.Precision))) : null;
        long? bounded = null;
        if (request.LowerBound is { } lower && request.UpperBound is { } upper)
        {
            bool Fits(long n) => (upper - lower) * System.Math.Sqrt(System.Math.Log(System.Math.PI * System.Math.PI * (double)n * n / (3 * alpha)) / (2 * n)) <= request.Precision;
            const long maximum = 1000000000000;
            if (Fits(maximum)) { long left = 1, right = maximum; while (left < right) { var mid = left + (right - left) / 2; if (Fits(mid)) right = mid; else left = mid + 1; } bounded = left; }
        }
        double? detection = request.EventProbability is { } probability ? probability == 1 ? 1 : Finite(System.Math.Ceiling(LogOneMinus(request.DetectionPower) / LogOneMinus(probability))) : null;
        return new(alpha, z, normal, bounded, detection, StatisticalInference.ZeroEventUpperBound(request.RoundBudget, alpha),
            bounded is { } exactBound ? exactBound <= request.RoundBudget : normal is { } approximate ? approximate <= request.RoundBudget : null,
            detection is { } required ? required <= request.RoundBudget : null,
            "Independent subjects with one fixed population. Mean precision uses a supplied variance and normal approximation; a zero pilot variance does not establish sufficiency. The bounded plan matches the time-uniform Hoeffding spending interval and requires proven support. Event detection means seeing at least one event, not estimating its payout contribution precisely. Family alpha is allocated by Bonferroni. Values beyond 10¹² bounded-mean subjects are withheld; API run limits remain separate.");
    }
    private static double? Finite(double value) => double.IsFinite(value) && value <= 9_007_199_254_740_991 ? System.Math.Max(1, value) : null;
    private static double LogOneMinus(double p)
    {
        if (p >= 1e-4) return System.Math.Log(1 - p);
        // Avoid subtracting tiny probabilities from one.
        return -p * (1 + p * (0.5 + p * (1d / 3 + p * 0.25)));
    }
}
