using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SlotMath.Core.Random;

/// <summary>
///     Walker–Vose alias method for O(1) weighted random sampling.
///     High-performance, zero-allocation construction with branchless, bounds-check-free sampling.
/// </summary>
public sealed class AliasMethod
{
    /// <summary>
    ///     Contiguous 16-byte aligned entry (power-of-two size: lea rax, [rcx + rdx*16]).
    ///     Both threshold and alias reside in the exact same 64-byte L1 cache line.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct SlotEntry(double threshold, int alias)
    {
        public readonly double Threshold = threshold;
        public readonly int Alias = alias;
    }

    private readonly SlotEntry[]? _table;

    private AliasMethod(SlotEntry[]? table, int n)
    {
        _table = table;
        Count = n;
    }

    public int Count { get; }

    /// <summary>
    ///     Builds the alias table with zero heap allocations for N &lt;= 256
    ///     and normalized BigInteger precision arithmetic.
    /// </summary>
    public static AliasMethod Build(WeightSet weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        var n = weights.Count;
        if (n == 0)
        {
            throw new ArgumentException("Must have at least one outcome.", nameof(weights));
        }

        if (n == 1)
        {
            return new AliasMethod(null, 1);
        }

        if (weights.IsUniform)
        {
            return new AliasMethod(null, n);
        }

        BigInteger totalNum = weights.NumeratorSum;
        if (totalNum <= BigInteger.Zero)
        {
            throw new ArgumentException("At least one weight must be positive.", nameof(weights));
        }

        // ── 1. Scaling ───────────────────────────────────────────────────────────
        // scaled[i] = weight · n / total. See Ratio for totals beyond the double range.
        var divisor = (double)totalNum;
        var bigN = new BigInteger(n);

        // ── 2. Memory Workspace (Zero GC for N <= 256) ───────────────────────────
        Span<double> stackScaled = stackalloc double[n <= 256 ? n : 0];
        Span<int> stackWork = stackalloc int[n <= 256 ? 2 * n : 0];

        double[]? rentedScaled = null;
        int[]? rentedWork = null;

        Span<double> scaled = n <= 256
            ? stackScaled
            : (rentedScaled = ArrayPool<double>.Shared.Rent(n)).AsSpan(0, n);

        Span<int> work = n <= 256
            ? stackWork
            : (rentedWork = ArrayPool<int>.Shared.Rent(2 * n)).AsSpan(0, 2 * n);

        try
        {
            for (var i = 0; i < n; i++)
            {
                BigInteger numer = weights[i];
                if (numer < BigInteger.Zero)
                {
                    throw new ArgumentException($"Weight at index {i} cannot be negative.", nameof(weights));
                }

                if (numer.IsZero)
                {
                    scaled[i] = 0.0;
                }
                else
                {
                    scaled[i] = Ratio(numer * bigN, totalNum, divisor);
                }
            }

            // ── 3. Two FIFO Worklists (replace two Queue<int>) ──────────────────
            // The pairing order decides which outcome a (bucket, fraction) pair
            // maps to, so it is part of the seeded-replay contract (D24): first
            // in, first out, exactly as the Queue-based build paired them.
            // Each ring holds at most n indices.
            Span<int> small = work[..n];
            Span<int> large = work[n..];
            int smallHead = 0, smallCount = 0, largeHead = 0, largeCount = 0;

            for (var i = 0; i < n; i++)
            {
                if (scaled[i] < 1.0)
                {
                    small[smallCount++] = i;
                }
                else
                {
                    large[largeCount++] = i;
                }
            }

            // Allocate table without zeroing memory overhead since all slots are written.
            SlotEntry[] table = GC.AllocateUninitializedArray<SlotEntry>(n);

            // ── 4. Walker–Vose Partitioning ─────────────────────────────────────
            while (smallCount > 0 && largeCount > 0)
            {
                var s = small[smallHead];
                smallHead = smallHead + 1 == n ? 0 : smallHead + 1;
                smallCount--;

                var l = large[largeHead];
                largeHead = largeHead + 1 == n ? 0 : largeHead + 1;
                largeCount--;

                // Rounding can leave a remainder a few ulps below zero. The stored
                // threshold is clamped (a negative one never accepts, like 0.0);
                // the running remainder is not, so later thresholds keep the same
                // bits as before.
                table[s] = new SlotEntry(scaled[s] > 0.0 ? scaled[s] : 0.0, l);
                scaled[l] -= 1.0 - scaled[s];

                if (scaled[l] < 1.0)
                {
                    var tail = smallHead + smallCount;
                    small[tail >= n ? tail - n : tail] = l;
                    smallCount++;
                }
                else
                {
                    var tail = largeHead + largeCount;
                    large[tail >= n ? tail - n : tail] = l;
                    largeCount++;
                }
            }

            // Remaining elements are mathematically ~1.0; force threshold = 1.0.
            for (; largeCount > 0; largeCount--)
            {
                var l = large[largeHead];
                largeHead = largeHead + 1 == n ? 0 : largeHead + 1;
                table[l] = new SlotEntry(1.0, l);
            }

            for (; smallCount > 0; smallCount--)
            {
                var s = small[smallHead];
                smallHead = smallHead + 1 == n ? 0 : smallHead + 1;
                table[s] = new SlotEntry(1.0, s);
            }

            return new AliasMethod(table, n);
        }
        finally
        {
            if (rentedScaled is not null)
            {
                ArrayPool<double>.Shared.Return(rentedScaled);
            }

            if (rentedWork is not null)
            {
                ArrayPool<int>.Shared.Return(rentedWork);
            }
        }
    }

    /// <summary>
    ///     <paramref name="numerator" /> / <paramref name="denominator" /> as a double.
    ///     While both operands convert to a finite double this is the plain quotient,
    ///     so thresholds keep the bits seeded replay depends on (D24). Beyond that
    ///     range the quotient of the leading 64 bits is rescaled by the difference in
    ///     magnitude: a positive weight keeps a positive share as long as a double
    ///     can represent it, and nothing overflows.
    /// </summary>
    private static double Ratio(BigInteger numerator, BigInteger denominator, double denominatorAsDouble)
    {
        var top = (double)numerator;
        if (double.IsFinite(top) && double.IsFinite(denominatorAsDouble))
        {
            return top / denominatorAsDouble;
        }

        var numeratorShift = (int)System.Math.Max(0, numerator.GetBitLength() - 64);
        var denominatorShift = (int)System.Math.Max(0, denominator.GetBitLength() - 64);
        return System.Math.ScaleB((double)(numerator >> numeratorShift) / (double)(denominator >> denominatorShift),
            numeratorShift - denominatorShift);
    }

    /// <summary>
    ///     Samples an outcome index in [0, n).
    ///     Bound-check-free, branchless selection, exact RNG sequence preservation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Sample(SeededRandom rng)
    {
        var i = rng.Next(Count);
        if (_table is null)
        {
            // Preserves strict RNG sequence parity with table path.
            rng.NextDouble();
            return i;
        }

        // Direct memory reference via base pointer: eliminates both bounds checks.
        ref readonly SlotEntry entry = ref Unsafe.Add(
            ref MemoryMarshal.GetArrayDataReference(_table), i);

        // Compiles down to ucomisd + cmov (branchless conditional move).
        return rng.NextDouble() < entry.Threshold ? i : entry.Alias;
    }

    /// <summary>
    ///     Returns independent arrays for testing and inspection.
    /// </summary>
    public (double[] prob, int[] alias) GetTables()
    {
        var prob = new double[Count];
        var alias = new int[Count];

        if (_table is null)
        {
            Array.Fill(prob, 1.0);
            for (var i = 0; i < Count; i++)
            {
                alias[i] = i;
            }

            return (prob, alias);
        }

        for (var i = 0; i < Count; i++)
        {
            prob[i] = _table[i].Threshold;
            alias[i] = _table[i].Alias;
        }

        return (prob, alias);
    }
}
