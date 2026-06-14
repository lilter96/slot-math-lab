using SlotMath.Core.Random;

namespace SlotMath.Codegen.Runtime;

// ═══════════════════════════════════════════════════════════════════════════
//  SampledDrawDriver — the zero-allocation hot-path driver (G7 DoD).
//
//  Weighted O(weights) pick from the pinned xoshiro256** PRNG (D3).  No heap
//  allocation per draw or per spin: the weight tables are static arrays owned
//  by the generated class, and the only state is the reused PRNG instance.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Production sampled draw driver: deterministic weighted choice from a seeded
/// xoshiro256** stream with no per-spin allocation.
/// </summary>
public sealed class SampledDrawDriver(SeededRandom rng) : IDrawDriver
{
    private readonly SeededRandom _rng = rng;

    public SampledDrawDriver(long seed) : this(new SeededRandom(seed)) { }

    public int Draw(ReadOnlySpan<long> weights)
    {
        long total = 0;
        for (var i = 0; i < weights.Length; i++)
            total += weights[i];

        if (total <= 0)
            return 0;

        // Uniform in [0, total) from the 64-bit stream, then cumulative pick.
        var roll = (long)(_rng.NextUInt64() % (ulong)total);
        long cumulative = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
                return i;
        }

        return weights.Length - 1;
    }
}
