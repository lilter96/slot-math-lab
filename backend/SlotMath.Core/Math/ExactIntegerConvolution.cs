using System.Buffers;
using System.Numerics;

namespace SlotMath.Core.Math;

/// <summary>
///     Exact linear convolution of nonnegative integer coefficients by power-of-two transforms over
///     prime fields and incremental unsigned CRT. The modulus product must strictly exceed a proven
///     coefficient bound. False means the direct/sparse caller should retain its existing algorithm;
///     cancellation and budget failures propagate. No floating-point arithmetic or tail truncation.
/// </summary>
internal static class ExactIntegerConvolution
{
    internal const int MaximumLength = 1 << 20;

    // Distinct primes p < 2^31 with 2^20 dividing p-1. The independent regression verifies
    // primality by trial division, and transform root orders by BigInteger modular arithmetic.
    private static readonly uint[] Primes =
    [
        2130706433u, 2114977793u, 2113929217u, 2099249153u, 2095054849u, 2088763393u, 2077229057u, 2070937601u,
        2047868929u, 2035286017u, 2025848833u, 2013265921u, 1998585857u, 1978662913u, 1963982849u, 1953497089u,
        1945108481u, 1931476993u, 1922039809u, 1894776833u, 1893728257u, 1888485377u, 1868562433u, 1866465281u,
        1863319553u, 1835008001u, 1811939329u, 1790967809u, 1724907521u, 1711276033u, 1709178881u, 1699741697u,
        1695547393u, 1654652929u, 1651507201u, 1643118593u, 1638924289u, 1630535681u, 1607467009u, 1601175553u,
        1594884097u, 1572864001u, 1570766849u, 1558183937u, 1555038209u, 1541406721u, 1536163841u, 1513095169u,
        1504706561u, 1492123649u, 1485832193u, 1484783617u, 1479540737u, 1438646273u, 1437597697u, 1431306241u,
        1415577601u, 1412431873u, 1410334721u, 1397751809u, 1380974593u, 1366294529u, 1365245953u, 1327497217u,
        1321205761u, 1315962881u, 1306525697u, 1300234241u, 1286602753u, 1281359873u, 1261436929u, 1224736769u,
        1218445313u, 1214251009u, 1212153857u, 1211105281u, 1205862401u, 1192230913u, 1185939457u, 1161822209u,
        1158676481u, 1151336449u, 1146093569u, 1138753537u, 1133510657u, 1127219201u, 1107296257u, 1102053377u,
        1095761921u, 1092616193u, 1085276161u, 1053818881u, 1051721729u, 1045430273u, 1012924417u, 1007681537u
    ];

    internal static int ModulusCount => Primes.Length;

    internal static uint Modulus(int index) => Primes[index];
    internal static uint TransformRoot(int index) => Root(Primes[index]);

    private static uint Root(uint prime)
    {
        uint candidate = 2;
        while (true)
        {
            var root = Power(candidate, (prime - 1) / MaximumLength, prime);
            if (Power(root, MaximumLength / 2, prime) != 1)
            {
                return root;
            }

            candidate++;
        }
    }

    /// <summary>True only when a complete exact transform is returned. Scratch buffers never escape.</summary>
    public static bool TryTransform(ReadOnlySpan<BigInteger> left, ReadOnlySpan<BigInteger> right,
        Action<long> spend, out BigInteger[] result, bool requireAdvantage = true, ExactStorageTracker? storage = null)
        => TryTransform(left, right, spend, out result, requireAdvantage, storage, null);

    /// <summary>Preflight uses the caller's remaining work before inspection; rejected plans return no partial result.</summary>
    internal static bool TryTransform(ReadOnlySpan<BigInteger> left, ReadOnlySpan<BigInteger> right,
        Action<long> spend, out BigInteger[] result, bool requireAdvantage, ExactStorageTracker? storage,
        long? workAllowance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance ?? 0);
        result = [];
        if (left.Length == 0 || right.Length == 0 || left.Length > MaximumLength ||
            right.Length > MaximumLength || (long)left.Length + right.Length - 1 > MaximumLength)
        {
            return false;
        }

        if (workAllowance is { } minimum && minimum < (long)left.Length + right.Length)
        {
            return false;
        }

        (BigInteger Sum, BigInteger Max, int Nonzero, long Work) a = Inspect(left, spend);
        (BigInteger Sum, BigInteger Max, int Nonzero, long Work) b = Inspect(right, spend);
        var count = left.Length + right.Length - 1;
        if (a.Sum.IsZero || b.Sum.IsZero)
        {
            using ExactStorageTracker.Lease? zeroSlots = storage?.Hold(ExactStorageKind.ConvolutionResultSlots, count);
            result = new BigInteger[count];
            return true;
        }

        // Every convolution coefficient is bounded by both of these sums of nonnegative products.
        BigInteger bound = BigInteger.Min(a.Sum * b.Max, b.Sum * a.Max);
        BigInteger modulus = BigInteger.One;
        var primes = 0;
        while (modulus <= bound && primes < Primes.Length)
        {
            modulus *= Primes[primes++];
        }

        if (modulus <= bound)
        {
            return false;
        }

        var length = (int)BitOperations.RoundUpToPowerOf2((uint)count);
        var levels = BitOperations.Log2((uint)length);
        var words = 1 + bound.GetBitLength() / 64;
        var transformCost = primes * (3L * length * (levels + 1) + (left.Length + right.Length + count) * words);
        // Multiplication depends on both operand widths; addition and output storage depend on the result width.
        // This CPU estimate is deliberately separate from the caller's charged arithmetic-work contract.
        var productWords = (1 + a.Max.GetBitLength() / 64) * (1 + b.Max.GetBitLength() / 64);
        var directCost = (long)a.Nonzero * b.Nonzero * (productWords + words);
        if (requireAdvantage && (a.Nonzero < 64 || b.Nonzero < 64 || transformCost * 2 >= directCost))
        {
            return false;
        }

        var chargedWork = a.Work + b.Work + transformCost + primes * (256L + length);
        if (workAllowance is { } available && chargedWork > available)
        {
            return false;
        }

        using ExactStorageTracker.Lease? outputSlots = storage?.Hold(ExactStorageKind.ConvolutionResultSlots, count);
        var output = new BigInteger[count];
        var first = ArrayPool<uint>.Shared.Rent(length);
        using ExactStorageTracker.Lease? firstCapacity =
            storage?.Hold(ExactStorageKind.TransformResidueCapacity, first.Length);
        uint[]? second = null;
        try
        {
            second = ArrayPool<uint>.Shared.Rent(length);
            using ExactStorageTracker.Lease? secondCapacity =
                storage?.Hold(ExactStorageKind.TransformResidueCapacity, second.Length);
            modulus = BigInteger.One;
            for (var index = 0; index < primes; index++)
            {
                var prime = Primes[index];
                spend(256);
                var root = Power(Root(prime), (uint)(MaximumLength / length), prime);
                Span<uint> x = first.AsSpan(0, length), y = second.AsSpan(0, length);
                x.Clear();
                y.Clear();
                Load(left, x, prime, words, spend);
                Load(right, y, prime, words, spend);
                Transform(x, root, prime, spend);
                Transform(y, root, prime, spend);
                spend(length);
                for (var i = 0; i < length; i++)
                {
                    x[i] = (uint)((ulong)x[i] * y[i] % prime);
                }

                Transform(x, Power(root, prime - 2, prime), prime, spend);
                var inverseLength = Power((uint)length, prime - 2, prime);
                var inverseModulus = Power((uint)(modulus % prime), prime - 2, prime);
                for (var i = 0; i < count; i++)
                {
                    if ((i & 255) == 0)
                    {
                        spend(System.Math.Min(256, count - i) * words);
                    }

                    var residue = (uint)((ulong)x[i] * inverseLength % prime);
                    var existing = (uint)(output[i] % prime);
                    var difference = residue >= existing ? residue - existing : residue + prime - existing;
                    var digit = (uint)((ulong)difference * inverseModulus % prime);
                    output[i] += modulus * digit;
                }

                modulus *= prime;
            }

            result = output;
            return true;
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(first);
            if (second is not null)
            {
                ArrayPool<uint>.Shared.Return(second);
            }
        }
    }

    private static (BigInteger Sum, BigInteger Max, int Nonzero, long Work) Inspect(ReadOnlySpan<BigInteger> values,
        Action<long> spend)
    {
        BigInteger sum = BigInteger.Zero, maximum = BigInteger.Zero;
        var nonzero = 0;
        long inspected = 0;
        for (var i = 0; i < values.Length; i++)
        {
            if ((i & 255) == 0)
            {
                long work = 0;
                for (var j = i; j < System.Math.Min(i + 256, values.Length); j++)
                {
                    work += 1 + BigInteger.Abs(values[j]).GetBitLength() / 64;
                }

                spend(work);
                inspected += work;
            }

            BigInteger value = values[i];
            if (value.Sign < 0)
            {
                throw new ArgumentException("Exact unsigned convolution requires nonnegative coefficients.");
            }

            sum += value;
            maximum = BigInteger.Max(maximum, value);
            nonzero += value.IsZero ? 0 : 1;
        }

        return (sum, maximum, nonzero, inspected);
    }

    private static void Load(ReadOnlySpan<BigInteger> source, Span<uint> into, uint prime, long words,
        Action<long> spend)
    {
        for (var i = 0; i < source.Length; i++)
        {
            if ((i & 255) == 0)
            {
                spend(System.Math.Min(256, source.Length - i) * words);
            }

            into[i] = (uint)(source[i] % prime);
        }
    }

    private static uint Power(uint value, uint exponent, uint prime)
    {
        ulong result = 1, next = value;
        while (exponent > 0)
        {
            if ((exponent & 1) != 0)
            {
                result = result * next % prime;
            }

            next = next * next % prime;
            exponent >>= 1;
        }

        return (uint)result;
    }

    private static void Transform(Span<uint> values, uint root, uint prime, Action<long> spend)
    {
        var length = values.Length;
        spend(length);
        for (int i = 1, reversed = 0; i < length; i++)
        {
            var bit = length >> 1;
            for (; (reversed & bit) != 0; bit >>= 1)
            {
                reversed ^= bit;
            }

            reversed ^= bit;
            if (i < reversed)
            {
                (values[i], values[reversed]) = (values[reversed], values[i]);
            }
        }

        for (var block = 2; block <= length; block <<= 1)
        {
            spend(length);
            var factor = Power(root, (uint)(length / block), prime);
            var half = block / 2;
            for (var start = 0; start < length; start += block)
            {
                ulong rotation = 1;
                for (var i = 0; i < half; i++)
                {
                    var a = values[start + i];
                    var b = (uint)(values[start + i + half] * rotation % prime);
                    var sum = a + b;
                    values[start + i] = sum >= prime ? sum - prime : sum;
                    values[start + i + half] = a >= b ? a - b : a + prime - b;
                    rotation = rotation * factor % prime;
                }
            }
        }
    }
}
