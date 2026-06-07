using System.Numerics;

namespace SlotMath.Core.Random;

/// <summary>
/// Deterministic, seedable PRNG based on SplitMix64.
/// Guarantees identical sequences for identical seeds across all platforms.
/// </summary>
/// <remarks>
/// SplitMix64 is a simple, fast 64-bit generator with excellent statistical properties.
/// It is used as the seeding generator for Java's SplittableRandom and is well-suited
/// for reproducible Monte Carlo work.
/// </remarks>
public sealed class SeededRandom
{
    private ulong _state;

    /// <summary>Create a PRNG with the given seed. A seed of 0 is valid.</summary>
    public SeededRandom(long seed)
    {
        Seed = seed;
        // Apply a finalizer mix so that small or patterned seeds produce well-distributed initial states.
        _state = Mix64((ulong)seed);
    }

    /// <summary>The original seed value (before mixing).</summary>
    public long Seed { get; }

    /// <summary>
    /// Return a uniformly distributed 64-bit signed integer.
    /// The full 64-bit range is covered (including negative values).
    /// </summary>
    public long NextInt64()
    {
        _state += 0x9e3779b97f4a7c15; // golden ratio φ − 1
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9;
        z = (z ^ (z >> 27)) * 0x94d049bb133111eb;
        return (long)(z ^ (z >> 31));
    }

    /// <summary>
    /// Return a uniformly distributed non-negative integer in [0, <paramref name="maxValue"/>).
    /// </summary>
    public int Next(int maxValue)
    {
        if (maxValue <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be positive.");

        // Fast remainder with rejection to avoid bias.
        // For small maxValue the bias from plain remainder is negligible,
        // but we use a rejection loop for correctness.
        // Use the unbiased technique: find 2^k >= maxValue, mask, reject if >= maxValue.

        // For the common case, use a fast path with rejection from full 64 bits.
        ulong mask = ulong.MaxValue >> (int)(BitOperations.LeadingZeroCount((ulong)(maxValue - 1) | 1u));
        while (true)
        {
            ulong x = (ulong)NextInt64() & mask;
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
        // Take the high 53 bits for a uniform [0, 1) double.
        // IEEE 754 double has 53 bits of mantissa precision.
        const double Unit = 1.0 / (1ul << 53);
        return ((ulong)NextInt64() >> 11) * Unit;
    }

    /// <summary>
    /// Create a fresh copy with the same starting seed.
    /// The copy produces the identical sequence that this instance produced from its start.
    /// </summary>
    public SeededRandom Clone()
    {
        return new SeededRandom(Seed);
    }

    /// <summary>SplitMix64 finalizer — hash a 64-bit value to itself.</summary>
    private static ulong Mix64(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9;
        z = (z ^ (z >> 27)) * 0x94d049bb133111eb;
        return z ^ (z >> 31);
    }
}
