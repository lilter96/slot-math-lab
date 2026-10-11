using SlotMath.Core.Random;
using M = System.Math;
namespace SlotMath.Core.Measurements;

public sealed record NullCalibrationRequest(ValueFrequency[] Observed, ReferenceMass[] Reference, string Statistic = "cdf", int Replicates = 2000, long Seed = 42);
public sealed record NullCalibrationReport(string Method, string Statistic, long Subjects, double? ObservedStatistic, double PValue,
    long Evaluations, long ExtremeEvaluations, double? MinimumResolvablePValue, NumericInterval? MonteCarloTailInterval, long Seed, string Assumptions)
{ public string? AuthoredInputSha256 { get; init; } public string? CoreBinarySha256 { get; init; } }

/// <summary>Prespecified discrete one-sample null calibration. Enumerates small multinomial
/// count laws; larger supported jobs use independent null draws with a plus-one Monte Carlo
/// p-value. No continuous-KS critical values or weighted pseudo-counts are accepted.</summary>
public static class DiscreteNullCalibration
{
    public static NullCalibrationReport Calculate(NullCalibrationRequest request, CancellationToken token = default)
    {
        if (request.Observed is not { Length: > 0 and <= 1024 } || request.Reference is not { Length: > 0 and <= 1024 }
            || request.Observed.Any(p => p is null || !double.IsFinite(p.Value) || p.Count < 0) || request.Reference.Any(p => p is null || !double.IsFinite(p.Value) || !double.IsFinite(p.Probability) || p.Probability < 0)
            || request.Observed.Select(p => p.Value).Distinct().Count() != request.Observed.Length || request.Reference.Select(p => p.Value).Distinct().Count() != request.Reference.Length
            || M.Abs(request.Reference.Sum(p => p.Probability) - 1) > 1e-10 || request.Statistic is not ("cdf" or "pearson")
            || request.Replicates is < 100 or > 20000 || request.Seed is < -9007199254740991 or > 9007199254740991) throw new ArgumentException("Supply complete integer counts, a prespecified finite PMF, CDF/Pearson statistic, 100..20000 replicates and a safe seed.");
        var count = request.Observed.Sum(p => p.Count); if (count is < 1 or > 10000000) throw new ArgumentException("Calibration requires 1..10000000 independent unweighted subjects.");
        var reference = request.Reference.Where(p => p.Probability > 0).OrderBy(p => p.Value).ToArray();
        var referenceKeys = reference.Select(p => p.Value).ToHashSet();
        if (request.Observed.Any(p => p.Count > 0 && !referenceKeys.Contains(p.Value)))
            return new("Structural reference-support mismatch", request.Statistic, count, null, 0, 0, 0, 0, null, request.Seed, "At least one observed value has zero probability under the prespecified reference. This is a structural discrepancy, independent of sample-size approximation.");
        var probabilitySum = reference.Sum(p => p.Probability); var probabilities = reference.Select(p => p.Probability / probabilitySum).ToArray();
        var observed = request.Observed.ToDictionary(p => p.Value, p => p.Count); var counts = reference.Select(p => observed.GetValueOrDefault(p.Value)).ToArray();
        double Statistic(long[] sample)
        {
            double chi = 0, cumulative = 0, distance = 0;
            for (var i = 0; i < sample.Length; i++)
            { var expected = count * probabilities[i]; var difference = sample[i] - expected; chi += difference * difference / expected; cumulative += (double)sample[i] / count - probabilities[i]; distance = M.Max(distance, M.Abs(cumulative)); }
            return request.Statistic == "cdf" ? distance : chi;
        }
        if (reference.Length == 1) return new("Enumerated degenerate discrete null", request.Statistic, count, 0, 1, 1, 1, null, null, request.Seed, "Every subject has the sole reference outcome; its fixed count law is deterministic. Structural support was checked before this result. Non-rejection does not independently validate the game specification.");
        var statistic = Statistic(counts);
        if (!double.IsFinite(statistic)) throw new ArithmeticException("The reference statistic exceeds finite numeric range."); var tolerance = 1e-12 * M.Max(1, M.Abs(statistic));
        double combinations = 1;
        for (var i = 1; i < reference.Length && combinations <= 100000; i++) combinations *= (count + (double)i) / i;
        if (count <= 512 && reference.Length <= 16 && combinations <= 100000)
        {
            var logFactorial = new double[count + 1]; for (var i = 2; i < logFactorial.Length; i++) logFactorial[i] = logFactorial[i - 1] + M.Log(i);
            var sample = new long[reference.Length]; long evaluations = 0, extreme = 0; double totalMass = 0, tailMass = 0;
            void Enumerate(int index, long remaining, double logMass)
            {
                token.ThrowIfCancellationRequested();
                if (index == sample.Length - 1)
                {
                    sample[index] = remaining; var mass = M.Exp(logFactorial[count] + logMass + remaining * M.Log(probabilities[index]) - logFactorial[remaining]); totalMass += mass; evaluations++;
                    if (Statistic(sample) + tolerance >= statistic) { tailMass += mass; extreme++; }
                    return;
                }
                for (long c = 0; c <= remaining; c++) { sample[index] = c; Enumerate(index + 1, remaining - c, logMass + c * M.Log(probabilities[index]) - logFactorial[c]); }
            }
            Enumerate(0, count, 0);
            if (M.Abs(totalMass - 1) > 1e-8) throw new ArithmeticException("Enumerated null probability conservation failed.");
            return new("Enumerated finite multinomial null · floating arithmetic", request.Statistic, count, statistic, M.Clamp(tailMass, 0, 1), evaluations, extreme, null, null, request.Seed,
                "Prespecified known discrete probabilities and independent unweighted subjects; inclusive tail and complete count-law enumeration. No fitted parameters or optional-stopping guarantee. Binary probabilities normalized within input tolerance; numerical tolerance 1e-12 on the statistic.");
        }
        if (count * request.Replicates > 10000000) throw new ArgumentException("Null sampling exceeds the 10000000-draw diagnostic budget. Reduce replicates or analyze a separately prespecified smaller subject population.");
        if (probabilities.Any(p => p < 1e-12)) throw new ArgumentException("This null has probabilities below the supported floating-draw resolution budget. Use bounded small-law enumeration or an independent exact rare-event reference.");
        var cumulativeProbabilities = new double[probabilities.Length]; double cumulativeProbability = 0;
        for (var i = 0; i < probabilities.Length; i++) cumulativeProbabilities[i] = cumulativeProbability += probabilities[i]; cumulativeProbabilities[^1] = 1;
        var rng = new SeededRandom(request.Seed); var simulated = new long[counts.Length]; long extremes = 0;
        for (var b = 0; b < request.Replicates; b++)
        {
            token.ThrowIfCancellationRequested(); Array.Clear(simulated);
            for (long j = 0; j < count; j++) { if ((j & 1023) == 0) token.ThrowIfCancellationRequested(); var u = rng.NextDouble(); var i = Array.BinarySearch(cumulativeProbabilities, u); if (i < 0) i = ~i; else while (i < cumulativeProbabilities.Length - 1 && cumulativeProbabilities[i] <= u) i++; simulated[i]++; }
            if (Statistic(simulated) + tolerance >= statistic) extremes++;
        }
        return new("Independent Monte Carlo discrete null · plus-one p-value", request.Statistic, count, statistic, (extremes + 1d) / (request.Replicates + 1d), request.Replicates, extremes,
            1d / (request.Replicates + 1), StatisticalInference.ExactBinomial(extremes, request.Replicates), request.Seed,
            "Prespecified known PMF, independent unweighted subjects and fixed count. Null replicates use a separate pinned xoshiro stream; inclusive statistic tail; (extreme+1)/(replicates+1). The interval describes Monte Carlo tail uncertainty, not game probability or a sequential decision rule. Non-rejection does not prove equivalence.");
    }
}
