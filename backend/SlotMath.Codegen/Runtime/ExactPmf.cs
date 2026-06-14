using System.Numerics;
using SlotMath.Core.Math;

namespace SlotMath.Codegen.Runtime;

// ═══════════════════════════════════════════════════════════════════════════
//  ExactPmf — exact distribution of GENERATED code via re-execution DFS.
//
//  D25 requires proving P_fast(x) = P_canonical(x) for every outcome.  We do
//  not sample the generated code — we ENUMERATE it exactly: the body is run
//  once per draw-path, forking over every outcome at the frontier draw and
//  accumulating exact rational probability from the draw weights.  No
//  approximation, no plugins, no floats — the PMF is a function over the draw
//  tree, identical in kind to the exact interpreter's Dist.
//
//  Each path re-runs the generated body from the same seeded initial state
//  (O(paths × depth)); correct and fast for the bounded games G7 supports.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Enumerates the exact probability mass function of a generated game body by
/// re-executing it over the full draw tree.
/// </summary>
public static class ExactPmf
{
    /// <summary>
    /// Compute the exact win → probability map produced by <paramref name="runSpin"/>.
    /// The callback must run one round deterministically given the supplied
    /// driver (i.e. reset to the same initial state on each call).
    /// </summary>
    public static IReadOnlyDictionary<BigInteger, Rational> Enumerate(Func<IDrawDriver, long> runSpin)
    {
        ArgumentNullException.ThrowIfNull(runSpin);
        var pmf = new Dictionary<BigInteger, Rational>();
        Recurse(runSpin, new List<int>(), Rational.One, pmf);
        return pmf;
    }

    private static void Recurse(
        Func<IDrawDriver, long> runSpin,
        List<int> prefix,
        Rational prob,
        Dictionary<BigInteger, Rational> pmf)
    {
        var driver = new ReplayForkDriver(prefix);
        long win = runSpin(driver);

        // No draw occurred at depth == prefix.Count ⇒ this path is fully
        // determined by the prefix: record its win with the path probability.
        if (driver.DrawCount == prefix.Count)
        {
            var key = (BigInteger)win;
            pmf[key] = pmf.TryGetValue(key, out var existing) ? existing + prob : prob;
            return;
        }

        // A frontier draw exists at depth prefix.Count — fork over its outcomes.
        var weights = driver.FrontierWeights!;
        BigInteger total = 0;
        foreach (var w in weights) total += w;
        if (total <= 0) return; // degenerate; no mass

        for (var i = 0; i < weights.Length; i++)
        {
            if (weights[i] <= 0) continue;
            prefix.Add(i);
            Recurse(runSpin, prefix, prob * new Rational(weights[i], total), pmf);
            prefix.RemoveAt(prefix.Count - 1);
        }
    }

    /// <summary>
    /// Replays a fixed outcome prefix, then captures the weights of the first
    /// draw beyond it (the "frontier") and forces outcome 0 from there on.
    /// </summary>
    private sealed class ReplayForkDriver(List<int> prefix) : IDrawDriver
    {
        private readonly int[] _prefix = prefix.ToArray();

        public int DrawCount { get; private set; }
        public long[]? FrontierWeights { get; private set; }

        public int Draw(ReadOnlySpan<long> weights)
        {
            var depth = DrawCount;
            DrawCount++;

            if (depth < _prefix.Length)
                return _prefix[depth];

            FrontierWeights ??= weights.ToArray();
            return 0;
        }
    }
}
