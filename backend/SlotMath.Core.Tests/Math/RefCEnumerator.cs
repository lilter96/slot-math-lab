using System.Numerics;

namespace SlotMath.Core.Tests.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  Independent oracle for REF-C (D9) — imports NOTHING from SlotMath.Core.
//
//  The kernel must never be its own oracle. This file derives the capped
//  REF-C expected win from first principles with plain BigInteger fractions.
//
//  Per cascade level the 3-cell row (cells drawn i.i.d. from P:2, Q:3, R:5
//  over a total weight of 10) yields:
//    P³  with probability (2/10)³ = 8/1000   → pays 5, then cascades again,
//    Q³  with probability (3/10)³ = 27/1000  → pays 2, then cascades again,
//    otherwise (965/1000)                    → pays 0 and the round ends.
//  Capped at 10 cascade levels. Hence the expected total win obeys
//    E(L) = 8/1000·(5 + E(L-1)) + 27/1000·(2 + E(L-1)),  E(0) = 0,
//  i.e. E(L) = 94/1000 + (35/1000)·E(L-1), and the capped value is E(10).
// ═══════════════════════════════════════════════════════════════════════════

internal static class RefCEnumerator
{
    /// <summary>Exact capped expected win after at most <paramref name="levels"/> cascades, as a reduced (num, den).</summary>
    public static (BigInteger Num, BigInteger Den) ExpectedWin(int levels)
    {
        BigInteger num = 0, den = 1; // E(0) = 0/1
        for (var i = 1; i <= levels; i++)
        {
            // E = 8/1000·(5 + E) + 27/1000·(2 + E)
            //   = [8·(5·den + num) + 27·(2·den + num)] / (1000·den)
            var nn = 8 * (5 * den + num) + 27 * (2 * den + num);
            var nd = 1000 * den;
            var g = BigInteger.GreatestCommonDivisor(BigInteger.Abs(nn), nd);
            num = nn / g;
            den = nd / g;
        }
        return (num, den);
    }
}
