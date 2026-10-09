namespace SlotMath.Core.Measurements;

public sealed record JointFrequency(double X, double Y, long Count, double Probability);
public sealed record JointDiagnostics(bool Complete, JointFrequency[] Support, double? ChiSquare, int DegreesOfFreedom,
    bool ExpectedCountsAdequate, double? PValue, string Calibration);

/// <summary>Bounded joint support for categorical mapped draws, paired components and state
/// conditions. The independence diagnostic uses complete integer counts, including unobserved
/// Cartesian cells; it never treats likelihood-weighted counts as multinomial trials.</summary>
internal sealed class PairDiagnostics(int limit)
{
    private bool _complete = true;
    private readonly Dictionary<(double X, double Y), long> _support = new();
    public void Reset() { _complete = true; _support.Clear(); }
    public void Add(double x, double y)
    {
        if (!_complete) return;
        var key = (x, y);
        if (!_support.ContainsKey(key) && _support.Count >= limit) { _complete = false; _support.Clear(); return; }
        _support[key] = _support.GetValueOrDefault(key) + 1;
    }
    public void Merge(PairDiagnostics other)
    {
        if (!_complete || !other._complete) { _complete = false; _support.Clear(); return; }
        foreach (var (key, count) in other._support)
        {
            if (!_support.ContainsKey(key) && _support.Count >= limit) { _complete = false; _support.Clear(); return; }
            _support[key] = _support.GetValueOrDefault(key) + count;
        }
    }
    public JointDiagnostics Snapshot(long count, bool independentUnweighted)
    {
        if (!_complete || count == 0) return new(_complete, [], null, 0, false, null, "Joint support incomplete or empty; no categorical independence test.");
        var x = _support.GroupBy(p => p.Key.X).ToDictionary(g => g.Key, g => g.Sum(p => p.Value));
        var y = _support.GroupBy(p => p.Key.Y).ToDictionary(g => g.Key, g => g.Sum(p => p.Value));
        var df = checked((x.Count - 1) * (y.Count - 1));
        // Equivalent to summing (observed-expected)^2/expected over EVERY Cartesian
        // cell, including zero-observation cells, without quadratic materialization.
        var chi = System.Math.Max(0, _support.Sum(p => (double)p.Value * p.Value * count / ((double)x[p.Key.X] * y[p.Key.Y])) - count);
        var adequate = (double)x.Values.Min() * y.Values.Min() / count >= 5;
        var calibrated = df > 0 && adequate && independentUnweighted;
        return new(true, _support.OrderBy(p => p.Key.X).ThenBy(p => p.Key.Y).Select(p => new JointFrequency(p.Key.X, p.Key.Y, p.Value, (double)p.Value / count)).ToArray(),
            double.IsFinite(chi) ? chi : null, df, adequate, calibrated ? StatisticalInference.ChiSquareSurvival(chi, df) : null,
            calibrated ? "Asymptotic Pearson categorical independence; independent paired subjects; every Cartesian expected count ≥ 5; fixed-count diagnostic, not a randomness proof."
                : "Uncalibrated categorical diagnostic: sparse counts, dependent/weighted subjects or constant marginal.");
    }
}
