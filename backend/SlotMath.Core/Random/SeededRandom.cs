using System.Numerics;
using System.Runtime.CompilerServices;
using SlotMath.Core.Math;

namespace SlotMath.Core.Random;

/// <summary>
///     Deterministic PRNG: xoshiro256** seeded via SplitMix64 (PRD v3.1, D3).
///     The sequence for a seed is pinned by golden vectors in the test suite.
///     No laboratory certification is claimed here.
/// </summary>
public sealed class SeededRandom
{
    private const ulong Gamma = 0x9E3779B97F4A7C15UL; // Fractional golden ratio expansion (phi - 1)
    private const double DoubleUnit = 1.0 / (1UL << 53); // Exact compile-time 2^-53 IEEE 754 multiplier
    private readonly ulong _expansionSeed;
    private RandomStreamCapture? _capture;

    private ulong _s0, _s1, _s2, _s3;

    /// <summary>Create a PRNG with the given seed. A seed of 0 is valid.</summary>
    public SeededRandom(long seed) : this((ulong)seed, seed)
    {
    }

    private SeededRandom(ulong expansionSeed, long reportedSeed)
    {
        _expansionSeed = expansionSeed;
        Seed = reportedSeed;
        SeedState(expansionSeed);
    }

    /// <summary>The original seed value (before mixing).</summary>
    public long Seed { get; }

    internal void EnableAudit() => _capture ??= new RandomStreamCapture();
    internal RandomStreamIdentity? AuditSnapshot(long index) => _capture?.Snapshot(index, _expansionSeed);

    internal void EndAudit()
    {
        _capture?.Dispose();
        _capture = null;
    }

    /// <summary>
    ///     Stream-splitting constructor (D3). Generates independent, non-overlapping
    ///     sequences for parallel Monte Carlo threads from a single master seed.
    /// </summary>
    public static SeededRandom ForStream(ulong masterSeed, long streamIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
        var expansion = SplitMix64Output(masterSeed, (ulong)streamIndex + 1UL);
        return new SeededRandom(expansion, (long)expansion);
    }

    /// <summary>Initialize the 256-bit state via SplitMix64.</summary>
    private void SeedState(ulong expansionSeed)
    {
        var x = expansionSeed;
        _s0 = NextSplitMix(ref x);
        _s1 = NextSplitMix(ref x);
        _s2 = NextSplitMix(ref x);
        _s3 = NextSplitMix(ref x);

        // xoshiro256** never leaves the all-zero state: replace it deterministically.
        if ((_s0 | _s1 | _s2 | _s3) == 0UL)
        {
            _s0 = Gamma;
        }
    }

    /// <summary>
    ///     Core xoshiro256** engine.
    ///     Inlined into callers with zero register spills and hardware rotation instructions (ROL).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong NextUInt64()
    {
        ulong s0 = _s0, s1 = _s1, s2 = _s2, s3 = _s3;

        var result = BitOperations.RotateLeft(s1 * 5UL, 7) * 9UL;
        var t = s1 << 17;

        s2 ^= s0;
        s3 ^= s1;
        s1 ^= s2;
        s0 ^= s3;
        s2 ^= t;
        s3 = BitOperations.RotateLeft(s3, 45);

        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;

        // Hot/Cold Path Splitting: Keeps the execution pipeline linear when audit is inactive.
        if (_capture is not null)
        {
            RecordAudit(result);
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RecordAudit(ulong result) => _capture!.Add(result);

    /// <summary>Uniformly distributed 64-bit signed integer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long NextInt64() => (long)NextUInt64();

    /// <summary>
    ///     Returns a non-negative integer in [0, maxValue).
    ///     Strictly preserves PRD v3.1 bit-identical sequence via power-of-two bitmask rejection.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Next(int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxValue);

        var max = (ulong)(uint)maxValue;
        var mask = ulong.MaxValue >> BitOperations.LeadingZeroCount((max - 1UL) | 1UL);

        while (true)
        {
            var x = NextUInt64() & mask;
            if (x < max)
            {
                return (int)x;
            }
        }
    }

    /// <summary>
    ///     Returns a uniform double in [0.0, 1.0) using 53-bit mantissa precision.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // After shifting, the value fits in 53 positive bits. Signed conversion
    // is exact and avoids the unsigned-to-double adjustment path.
    public double NextDouble() => (long)(NextUInt64() >> 11) * DoubleUnit;

    // Alias sampling always consumes Next(maxValue), including its rejections,
    // followed by NextDouble(). Keep the intervening state in registers when
    // audit is disabled. Audited streams retain the ordinary per-word path.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int NextWithFraction(int maxValue, ulong mask, out double fraction)
    {
        if (_capture is not null)
        {
            return AuditedPair(maxValue, out fraction);
        }

        ulong s0 = _s0, s1 = _s1, s2 = _s2, s3 = _s3;
        ulong index;
        do
        {
            index = (BitOperations.RotateLeft(s1 * 5UL, 7) * 9UL) & mask;
            var t = s1 << 17;
            s2 ^= s0;
            s3 ^= s1;
            s1 ^= s2;
            s0 ^= s3;
            s2 ^= t;
            s3 = BitOperations.RotateLeft(s3, 45);
        } while (index >= (uint)maxValue);

        var word = BitOperations.RotateLeft(s1 * 5UL, 7) * 9UL;
        var last = s1 << 17;
        s2 ^= s0;
        s3 ^= s1;
        s1 ^= s2;
        s0 ^= s3;
        s2 ^= last;
        s3 = BitOperations.RotateLeft(s3, 45);
        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;
        fraction = (long)(word >> 11) * DoubleUnit;
        return (int)index;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int AuditedPair(int maxValue, out double fraction)
    {
        var index = Next(maxValue);
        fraction = NextDouble();
        return index;
    }

    /// <summary>Clones the PRNG to reproduce the sequence from the start.</summary>
    public SeededRandom Clone() => new(_expansionSeed, Seed);

    // ── SplitMix64 Seeding Primitives ────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong NextSplitMix(ref ulong state)
    {
        state += Gamma;
        return Mix64(state);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Mix64(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong SplitMix64Output(ulong seed, ulong index) => Mix64(seed + index * Gamma);
}
