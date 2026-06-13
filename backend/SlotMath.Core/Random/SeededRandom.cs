using System.Numerics;

namespace SlotMath.Core.Random;

// ═══════════════════════════════════════════════════════════════════════════
//  SeededRandom — the pinned PRNG (PRD v3.1, D3).
//
//  Algorithm: xoshiro256** (core generator), seeded via SplitMix64.
//  This combination is the contract: it is fast, has excellent statistical
//  quality, a 2^256 period, and supports cheap, deterministic stream splitting
//  for parallel Monte Carlo (D3).
//
//  Seeding (canonical xoshiro procedure):
//    - A 64-bit "expansion seed" is run through SplitMix64 four times to fill
//      the four 64-bit xoshiro256** state words.
//    - The plain constructor uses (ulong)seed as the expansion seed.
//    - Stream splitting: stream i's expansion seed = SplitMix64(masterSeed, i)
//      = Mix64(masterSeed + (i+1)·GAMMA) — an O(1), pinned mapping.  This gives
//      each parallel chunk an independent, reproducible sub-stream so a given
//      (seed, n) yields bit-identical statistics regardless of thread count.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Deterministic, seedable PRNG: xoshiro256** seeded via SplitMix64 (D3).
/// Guarantees identical sequences for identical seeds across all platforms,
/// and supports deterministic stream splitting for parallel sampling.
/// </summary>
public sealed class SeededRandom
{
    private ulong _s0, _s1, _s2, _s3;
    private readonly ulong _expansionSeed;

    /// <summary>Create a PRNG with the given seed. A seed of 0 is valid.</summary>
    public SeededRandom(long seed) : this((ulong)seed, seed) { }

    private SeededRandom(ulong expansionSeed, long reportedSeed)
    {
        _expansionSeed = expansionSeed;
        Seed = reportedSeed;
        SeedState(expansionSeed);
    }

    /// <summary>
    /// Create the PRNG for stream <paramref name="streamIndex"/> of a parallel
    /// run with the given master seed (D3). Stream <c>i</c> is reproducible from
    /// <c>(masterSeed, i)</c> alone, independent of any other stream.
    /// </summary>
    public static SeededRandom ForStream(ulong masterSeed, long streamIndex)
    {
        if (streamIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(streamIndex), "Stream index must be non-negative.");
        var expansion = SplitMix64Output(masterSeed, (ulong)streamIndex + 1);
        return new SeededRandom(expansion, (long)expansion);
    }

    /// <summary>The original seed value (before mixing).</summary>
    public long Seed { get; }

    /// <summary>Fill the four xoshiro256** state words from an expansion seed via SplitMix64.</summary>
    private void SeedState(ulong expansionSeed)
    {
        var x = expansionSeed;
        _s0 = NextSplitMix(ref x);
        _s1 = NextSplitMix(ref x);
        _s2 = NextSplitMix(ref x);
        _s3 = NextSplitMix(ref x);
        // Guard against the all-zero state (degenerate for xoshiro): perturb deterministically.
        if ((_s0 | _s1 | _s2 | _s3) == 0)
            _s0 = Gamma;
    }

    /// <summary>
    /// Return a uniformly distributed unsigned 64-bit integer (xoshiro256** core).
    /// </summary>
    public ulong NextUInt64()
    {
        var result = Rotl(_s1 * 5UL, 7) * 9UL;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>
    /// Return a uniformly distributed 64-bit signed integer.
    /// The full 64-bit range is covered (including negative values).
    /// </summary>
    public long NextInt64() => (long)NextUInt64();

    /// <summary>
    /// Return a uniformly distributed non-negative integer in [0, <paramref name="maxValue"/>).
    /// </summary>
    public int Next(int maxValue)
    {
        if (maxValue <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be positive.");

        // Unbiased: mask down to the next power of two and reject out-of-range draws.
        var mask = ulong.MaxValue >> BitOperations.LeadingZeroCount((ulong)(maxValue - 1) | 1UL);
        while (true)
        {
            var x = NextUInt64() & mask;
            if (x < (ulong)maxValue)
                return (int)x;
        }
    }

    /// <summary>
    /// Return a uniformly distributed double in [0.0, 1.0).
    /// Uses the high 53 bits of a 64-bit random value for maximum precision.
    /// </summary>
    public double NextDouble()
    {
        const double Unit = 1.0 / (1UL << 53);
        return (NextUInt64() >> 11) * Unit;
    }

    /// <summary>
    /// Create a fresh copy that reproduces the identical sequence from its start.
    /// </summary>
    public SeededRandom Clone() => new(_expansionSeed, Seed);

    // ── SplitMix64 seeding primitive ─────────────────────────────────────

    private const ulong Gamma = 0x9E3779B97F4A7C15UL; // golden ratio φ − 1

    private static ulong NextSplitMix(ref ulong state)
    {
        state += Gamma;
        return Mix64(state);
    }

    /// <summary>SplitMix64 finalizer — hash a 64-bit value to itself.</summary>
    private static ulong Mix64(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>
    /// The <paramref name="index"/>-th output of a SplitMix64 stream seeded with
    /// <paramref name="seed"/> (1-based). O(1) via SplitMix64's additive state.
    /// </summary>
    private static ulong SplitMix64Output(ulong seed, ulong index) => Mix64(seed + index * Gamma);

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));
}
