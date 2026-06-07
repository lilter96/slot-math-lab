namespace SlotMath.Core.Random;

/// <summary>
/// Walker–Vose alias method for O(1) weighted random sampling.
/// Precomputes two tables — probability thresholds and alias indices —
/// so each sample requires one uniform integer, one uniform double, and at most one comparison.
/// </summary>
/// <remarks>
/// Build accepts integer or rational weights via <see cref="WeightSet"/>.
/// Sampling is deterministic given a <see cref="SeededRandom"/>.
/// Degenerate inputs (single outcome, zero weights, non-normalised weights) are handled.
/// </remarks>
public sealed class AliasMethod
{
    private readonly double[] _prob;  // threshold for each bucket
    private readonly int[] _alias;    // alias index for each bucket
    private readonly int _n;          // number of outcomes

    private AliasMethod(double[] prob, int[] alias, int n)
    {
        _prob = prob;
        _alias = alias;
        _n = n;
    }

    /// <summary>Number of outcomes.</summary>
    public int Count => _n;

    /// <summary>
    /// Build an alias table from the given weight set.
    /// </summary>
    public static AliasMethod Build(WeightSet weights)
    {
        // ── Degenerate cases ──────────────────────────────────────────
        if (weights.Count == 0)
            throw new ArgumentException("Must have at least one outcome.", nameof(weights));

        if (weights.Count == 1)
        {
            // Single outcome always selected.
            return new AliasMethod([1.0], [0], 1);
        }

        var totalNum = weights.NumeratorSum;
        if (totalNum == 0)
            throw new ArgumentException("At least one weight must be positive.", nameof(weights));

        var n = weights.Count;

        // Probability of outcome i = Numerators[i] / NumeratorSum.
        // The Denominator cancels — it is already reflected in the numerators
        // (for integer weights Denominator=1; for rational weights numerators
        // are scaled to the common denominator).
        // Scaled probability for alias table: p_i * n = Numer[i] * n / totalNum.
        var scaled = new double[n];
        for (var i = 0; i < n; i++)
        {
            var numer = weights.Numerators[i] * new System.Numerics.BigInteger(n);
            scaled[i] = (double)numer / (double)totalNum;
        }

        // ── Walker-Vose construction ──────────────────────────────────
        var prob = new double[n];
        var alias = new int[n];

        // Partition into small (< 1.0) and large (>= 1.0)
        var small = new Queue<int>();
        var large = new Queue<int>();

        for (var i = 0; i < n; i++)
        {
            if (scaled[i] < 1.0)
                small.Enqueue(i);
            else
                large.Enqueue(i);
        }

        while (small.Count > 0 && large.Count > 0)
        {
            var s = small.Dequeue();
            var l = large.Dequeue();

            prob[s] = scaled[s];
            alias[s] = l;

            scaled[l] = scaled[l] - (1.0 - scaled[s]);

            if (scaled[l] < 1.0)
                small.Enqueue(l);
            else
                large.Enqueue(l);
        }

        // Handle remaining entries (floating-point rounding may leave some in either queue).
        // They should all be ~1.0.
        while (large.Count > 0)
        {
            var l = large.Dequeue();
            prob[l] = 1.0;
            alias[l] = l;
        }

        while (small.Count > 0)
        {
            var s = small.Dequeue();
            prob[s] = 1.0;
            alias[s] = s;
        }

        return new AliasMethod(prob, alias, n);
    }

    /// <summary>
    /// Sample an outcome index in [0, n).
    /// O(1): one uniform integer, one uniform double, one comparison.
    /// </summary>
    public int Sample(SeededRandom rng)
    {
        var i = rng.Next(_n);
        return rng.NextDouble() < _prob[i] ? i : _alias[i];
    }

    /// <summary>
    /// Return the alias table for inspection/testing.
    /// prob[i] is the acceptance threshold, alias[i] is the fallback index.
    /// </summary>
    public (double[] prob, int[] alias) GetTables() => (_prob.ToArray(), _alias.ToArray());
}
