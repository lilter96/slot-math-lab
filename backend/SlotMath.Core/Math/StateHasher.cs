using System.Numerics;
using SlotMath.Core.Mechanics;

namespace SlotMath.Core.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  StateHasher — canonical content hash for dictionary-shaped game states
//
//  The exact interpreter memoises on a caller-supplied recurrence hash.  For
//  the graph compiler's Dictionary<string, object?> state, the hash must be
//  a function of the *contents* (Dictionary.GetHashCode() is reference
//  identity, which is both unsound — in-place-mutated states keep their old
//  hash — and useless — equal states never share cache entries).
//
//  The hash is 128 bits wide (two independent FNV-1a lanes folded into one
//  BigInteger), making accidental collisions between distinct reachable
//  states vanishingly unlikely.  Entries are folded in key order so
//  insertion order does not matter.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Canonical, deterministic, content-based hashing for game states held as
/// <c>Dictionary&lt;string, object?&gt;</c> (the graph compiler's state shape).
/// </summary>
public static class StateHasher
{
    private const ulong FnvOffset1 = 14695981039346656037UL;
    private const ulong FnvPrime1 = 1099511628211UL;
    private const ulong FnvOffset2 = 0x9E3779B97F4A7C15UL;
    private const ulong FnvPrime2 = 0x100000001B3UL * 31;

    /// <summary>
    /// Compute a canonical 128-bit content hash of the state dictionary,
    /// independent of insertion order.
    /// </summary>
    public static BigInteger CanonicalHash(IReadOnlyDictionary<string, object?> state)
    {
        // Fold entries order-independently: hash each (key, value) pair into
        // a 128-bit lane pair, then combine commutatively (sum). This avoids
        // sorting keys on every call while keeping the result canonical.
        ulong lane1 = FnvOffset1;
        ulong lane2 = FnvOffset2;

        foreach (var (key, value) in state)
        {
            ulong e1 = FnvOffset1;
            ulong e2 = FnvOffset2;
            HashString(key, ref e1, ref e2);
            HashValue(value, ref e1, ref e2);
            unchecked
            {
                lane1 += e1 * FnvPrime1;
                lane2 += e2 ^ Mix(e1);
            }
        }

        unchecked
        {
            lane1 = Mix(lane1 ^ (ulong)state.Count);
            lane2 = Mix(lane2 + (ulong)state.Count);
        }

        return new BigInteger(lane1) | (new BigInteger(lane2) << 64);
    }

    private static void HashValue(object? value, ref ulong h1, ref ulong h2)
    {
        switch (value)
        {
            case null:
                HashUInt64(0xD1B54A32D192ED03UL, ref h1, ref h2);
                break;
            case bool b:
                HashUInt64(b ? 3UL : 5UL, ref h1, ref h2);
                break;
            case int i:
                HashUInt64(0x10UL, ref h1, ref h2);
                HashUInt64(unchecked((ulong)(long)i), ref h1, ref h2);
                break;
            case long l:
                HashUInt64(0x10UL, ref h1, ref h2);
                HashUInt64(unchecked((ulong)l), ref h1, ref h2);
                break;
            case BigInteger bi:
                HashUInt64(0x20UL, ref h1, ref h2);
                HashBytes(bi.ToByteArray(), ref h1, ref h2);
                break;
            case decimal d:
                HashUInt64(0x30UL, ref h1, ref h2);
                foreach (var part in decimal.GetBits(d))
                    HashUInt64(unchecked((ulong)(long)part), ref h1, ref h2);
                break;
            case double dbl:
                HashUInt64(0x38UL, ref h1, ref h2);
                HashUInt64(unchecked((ulong)BitConverter.DoubleToInt64Bits(dbl)), ref h1, ref h2);
                break;
            case string s:
                HashUInt64(0x40UL, ref h1, ref h2);
                HashString(s, ref h1, ref h2);
                break;
            case Board board:
                // Board has content-based equality and hashing.
                HashUInt64(0x50UL, ref h1, ref h2);
                HashUInt64(unchecked((ulong)(long)board.GetHashCode()), ref h1, ref h2);
                HashUInt64(unchecked((ulong)((long)board.Rows << 32 | (uint)board.Cols)), ref h1, ref h2);
                break;
            case IReadOnlyDictionary<string, object?> nested:
                HashUInt64(0x70UL, ref h1, ref h2);
                var nestedHash = CanonicalHash(nested);
                HashBytes(nestedHash.ToByteArray(), ref h1, ref h2);
                break;
            case System.Collections.IEnumerable seq:
                // Generic, order-significant sequence hashing (D2/D11: arrays are
                // order-significant).  A "board" is just a user-defined array in
                // state S (invariant 4) — string[], object?[], List<object?>,
                // Win[], etc. all hash by content here, so structurally equal
                // states memoise to the same DAG node regardless of identity.
                HashUInt64(0x80UL, ref h1, ref h2);
                ulong len = 0;
                foreach (var item in seq)
                {
                    HashValue(item, ref h1, ref h2);
                    len++;
                }
                HashUInt64(len, ref h1, ref h2);
                break;
            default:
                // Fall back to the type identity + the value's own hash code.
                // Custom state values should implement content-based equality.
                HashUInt64(0xFFUL, ref h1, ref h2);
                HashString(value.GetType().FullName ?? "?", ref h1, ref h2);
                HashUInt64(unchecked((ulong)(long)value.GetHashCode()), ref h1, ref h2);
                break;
        }
    }

    private static void HashString(string s, ref ulong h1, ref ulong h2)
    {
        HashUInt64((ulong)s.Length, ref h1, ref h2);
        foreach (var c in s)
            HashUInt64(c, ref h1, ref h2);
    }

    private static void HashBytes(byte[] bytes, ref ulong h1, ref ulong h2)
    {
        HashUInt64((ulong)bytes.Length, ref h1, ref h2);
        foreach (var b in bytes)
            HashUInt64(b, ref h1, ref h2);
    }

    private static void HashUInt64(ulong value, ref ulong h1, ref ulong h2)
    {
        unchecked
        {
            h1 = (h1 ^ value) * FnvPrime1;
            h2 = (h2 ^ Mix(value)) * FnvPrime2;
        }
    }

    /// <summary>SplitMix64 finalizer — strong 64-bit avalanche.</summary>
    private static ulong Mix(ulong z)
    {
        unchecked
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
