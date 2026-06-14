using System.Numerics;

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
//  The algorithm is MurmurHash3 x64 128-bit (D11): a fast, well-distributed,
//  non-cryptographic structural hash.  The state is reduced to a stream of
//  64-bit lanes; each (key, value) entry is hashed independently and the
//  per-entry 128-bit digests are combined COMMUTATIVELY (so dict key order is
//  irrelevant — D11 CanonicalStateIdentity), while arrays feed their elements
//  SEQUENTIALLY into one digest (so element order IS significant — D2/D11).
//  The result is 128 bits wide, making collisions between distinct reachable
//  states vanishingly unlikely.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Canonical, deterministic, content-based hashing for game states held as
/// <c>Dictionary&lt;string, object?&gt;</c> (the graph compiler's state shape).
/// </summary>
public static class StateHasher
{
    // Fixed seed so the hash is reproducible on any machine (D24).
    private const ulong EntrySeed = 0x9E3779B97F4A7C15UL;

    // Value-kind discriminator lanes (so e.g. the string "1" and the integer 1
    // hash differently).
    private const ulong TagNull = 0xD1B54A32D192ED03UL;
    private const ulong TagInt = 0x10UL;
    private const ulong TagBig = 0x20UL;
    private const ulong TagDecimal = 0x30UL;
    private const ulong TagDouble = 0x38UL;
    private const ulong TagString = 0x40UL;
    private const ulong TagDict = 0x70UL;
    private const ulong TagSeq = 0x80UL;
    private const ulong TagOther = 0xFFUL;

    /// <summary>
    /// Compute a canonical 128-bit content hash of the state dictionary,
    /// independent of insertion order.
    /// </summary>
    public static BigInteger CanonicalHash(IReadOnlyDictionary<string, object?> state)
    {
        // Fold entries order-independently: hash each (key, value) pair to a
        // 128-bit digest, then combine commutatively (wrapping add).
        ulong sumLo = 0, sumHi = 0;
        foreach (var (key, value) in state)
        {
            var m = new Murmur128(EntrySeed);
            HashString(key, ref m);
            HashValue(value, ref m);
            var lo = m.Finalize(out var hi);
            unchecked
            {
                sumLo += lo;
                sumHi += hi;
            }
        }

        // Avalanche the commutative sums together with the entry count so two
        // states that differ only in count (e.g. a key absent vs present-as-0)
        // cannot collide.
        var fin = new Murmur128(EntrySeed);
        fin.Add(sumLo);
        fin.Add(sumHi);
        fin.Add((ulong)state.Count);
        var rlo = fin.Finalize(out var rhi);

        return new BigInteger(rlo) | (new BigInteger(rhi) << 64);
    }

    private static void HashValue(object? value, ref Murmur128 m)
    {
        switch (value)
        {
            case null:
                m.Add(TagNull);
                break;
            case bool b:
                m.Add(b ? 3UL : 5UL);
                break;
            case int i:
                m.Add(TagInt);
                m.Add(unchecked((ulong)(long)i));
                break;
            case long l:
                m.Add(TagInt);
                m.Add(unchecked((ulong)l));
                break;
            case BigInteger bi:
                m.Add(TagBig);
                HashBytes(bi.ToByteArray(), ref m);
                break;
            case decimal d:
                m.Add(TagDecimal);
                foreach (var part in decimal.GetBits(d))
                    m.Add(unchecked((ulong)(long)part));
                break;
            case double dbl:
                m.Add(TagDouble);
                m.Add(unchecked((ulong)BitConverter.DoubleToInt64Bits(dbl)));
                break;
            case string s:
                m.Add(TagString);
                HashString(s, ref m);
                break;
            case IReadOnlyDictionary<string, object?> nested:
                // Recurse: nested dicts are themselves order-independent.
                m.Add(TagDict);
                HashBytes(CanonicalHash(nested).ToByteArray(), ref m);
                break;
            case System.Collections.IEnumerable seq:
                // Generic, order-significant sequence hashing (D2/D11: arrays are
                // order-significant).  A "board" is just a user-defined array in
                // state S (invariant 4) — string[], object?[], List<object?>,
                // Win[], etc. all hash by content here, so structurally equal
                // states memoise to the same DAG node regardless of identity.
                m.Add(TagSeq);
                ulong len = 0;
                foreach (var item in seq)
                {
                    HashValue(item, ref m);
                    len++;
                }
                m.Add(len);
                break;
            default:
                // Fall back to the type identity + the value's own hash code.
                // Custom state values should implement content-based equality.
                m.Add(TagOther);
                HashString(value.GetType().FullName ?? "?", ref m);
                m.Add(unchecked((ulong)(long)value.GetHashCode()));
                break;
        }
    }

    private static void HashString(string s, ref Murmur128 m)
    {
        m.Add((ulong)s.Length);
        foreach (var c in s)
            m.Add(c);
    }

    private static void HashBytes(byte[] bytes, ref Murmur128 m)
    {
        m.Add((ulong)bytes.Length);
        foreach (var b in bytes)
            m.Add(b);
    }

    // ───────────────────────────────────────────────────────────────────────
    //  MurmurHash3 x64 128-bit — streaming over 64-bit lanes.
    //
    //  Each lane is one 8-byte word; pairs of lanes form the 16-byte blocks of
    //  the canonical algorithm, and a trailing odd lane is the 8-byte tail.
    //  Faithful to the reference implementation for byte lengths that are
    //  multiples of 8 (which is all we ever feed).
    // ───────────────────────────────────────────────────────────────────────
    private struct Murmur128
    {
        private const ulong C1 = 0x87C37B91114253D5UL;
        private const ulong C2 = 0x4CF5AD432745937FUL;

        private ulong _h1;
        private ulong _h2;
        private ulong _pending;   // a buffered lane awaiting its pair
        private bool _hasPending;
        private ulong _byteLen;

        public Murmur128(ulong seed)
        {
            _h1 = seed;
            _h2 = seed;
            _pending = 0;
            _hasPending = false;
            _byteLen = 0;
        }

        private static ulong RotL(ulong x, int r) => (x << r) | (x >> (64 - r));

        public void Add(ulong lane)
        {
            _byteLen += 8;
            if (!_hasPending)
            {
                _pending = lane;
                _hasPending = true;
                return;
            }

            MixBlock(_pending, lane);
            _hasPending = false;
        }

        private void MixBlock(ulong k1, ulong k2)
        {
            unchecked
            {
                k1 *= C1;
                k1 = RotL(k1, 31);
                k1 *= C2;
                _h1 ^= k1;
                _h1 = RotL(_h1, 27);
                _h1 += _h2;
                _h1 = _h1 * 5 + 0x52DCE729UL;

                k2 *= C2;
                k2 = RotL(k2, 33);
                k2 *= C1;
                _h2 ^= k2;
                _h2 = RotL(_h2, 31);
                _h2 += _h1;
                _h2 = _h2 * 5 + 0x38495AB5UL;
            }
        }

        private static ulong FMix64(ulong k)
        {
            unchecked
            {
                k ^= k >> 33;
                k *= 0xFF51AFD7ED558CCDUL;
                k ^= k >> 33;
                k *= 0xC4CEB9FE1A85EC53UL;
                k ^= k >> 33;
                return k;
            }
        }

        public ulong Finalize(out ulong high)
        {
            unchecked
            {
                // Tail: a single leftover 8-byte lane uses the k1 path only.
                if (_hasPending)
                {
                    var k1 = _pending;
                    k1 *= C1;
                    k1 = RotL(k1, 31);
                    k1 *= C2;
                    _h1 ^= k1;
                }

                _h1 ^= _byteLen;
                _h2 ^= _byteLen;
                _h1 += _h2;
                _h2 += _h1;
                _h1 = FMix64(_h1);
                _h2 = FMix64(_h2);
                _h1 += _h2;
                _h2 += _h1;

                high = _h2;
                return _h1;
            }
        }
    }
}
