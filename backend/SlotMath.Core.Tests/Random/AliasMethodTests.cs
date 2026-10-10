using System.Numerics;
using CsCheck;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Random;

public sealed class AliasMethodTests
{
    // ════════════════════════════════════════════════════════════════
    //  Chi-Squared Goodness-of-Fit (Statistical Engine Verification)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Critical values for χ² distribution at α = 0.01. Indexed by df (1..30).
    /// </summary>
    private static readonly double[] Chi2Critical01 =
    [
        0.0, // df=0 (unused)
        6.6348966, // df=1
        9.2103404, // df=2
        11.3448667, // df=3
        13.2767040, // df=4
        15.0862725, // df=5
        16.8118936, // df=6
        18.4753069, // df=7
        20.0902350, // df=8
        21.6659942, // df=9
        23.2092515, // df=10
        24.7249700, // df=11
        26.2169670, // df=12
        27.6882500, // df=13
        29.1412400, // df=14
        30.5779100, // df=15
        31.9999300, // df=16
        33.4086600, // df=17
        34.8053000, // df=18
        36.1908700, // df=19
        37.5662300, // df=20
        38.9321700, // df=21
        40.2893600, // df=22
        41.6384000, // df=23
        42.9798200, // df=24
        44.3141000, // df=25
        45.6416800, // df=26
        46.9629400, // df=27
        48.2782400, // df=28
        49.5878800, // df=29
        50.8921800 // df=30
    ];

    private static double GetChi2Critical(int df)
    {
        if (df < 1 || df >= Chi2Critical01.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(df),
                $"df must be in [1, {Chi2Critical01.Length - 1}], got {df}.");
        }

        return Chi2Critical01[df];
    }

    private static bool ChiSquaredDoesNotReject(int[] observed, WeightSet weights, int df)
    {
        BigInteger totalNum = weights.NumeratorSum;
        var n = observed.Sum();
        var chi2 = 0.0;

        for (var i = 0; i < weights.Count; i++)
        {
            var expectedProb = (double)weights[i] / (double)totalNum;
            var expected = expectedProb * n;

            if (expected < 5.0)
            {
                continue; // Skip bins with low frequency (standard NIST / GLI testing protocol)
            }

            var diff = observed[i] - expected;
            chi2 += diff * diff / expected;
        }

        return chi2 <= GetChi2Critical(df);
    }

    public record WeightProfile(string Name, WeightSet Weights, int Df);

    public static TheoryData<WeightProfile> ChiSquaredProfiles()
    {
        var data = new TheoryData<WeightProfile>();

        // 1. Uniform lazy distribution (tests null table fast-path)
        data.Add(new WeightProfile("uniform-lazy-2", WeightSet.Uniform(2), 1));
        data.Add(new WeightProfile("uniform-lazy-5", WeightSet.Uniform(5), 4));

        // 2. Uniform integer arrays
        data.Add(new WeightProfile("uniform-int-2", WeightSet.FromIntegers([1, 1]), 1));
        data.Add(new WeightProfile("uniform-int-5", WeightSet.FromIntegers([3, 3, 3, 3, 3]), 4));

        // 3. Skewed integer distributions
        data.Add(new WeightProfile("skewed-3", WeightSet.FromIntegers([1, 2, 3]), 2));
        data.Add(new WeightProfile("skewed-6", WeightSet.FromIntegers([1, 2, 4, 8, 16, 32]), 5));
        data.Add(new WeightProfile("large-int-10",
            WeightSet.FromIntegers([100, 200, 300, 400, 500, 600, 700, 800, 900, 1000]), 9));
        data.Add(new WeightProfile("very-skewed-5", WeightSet.FromIntegers([1, 1, 1, 1, 96]), 4));
        data.Add(new WeightProfile("primes-7", WeightSet.FromIntegers([2, 3, 5, 7, 11, 13, 17]), 6));
        data.Add(new WeightProfile("powers-of-two-8", WeightSet.FromIntegers([1, 2, 4, 8, 16, 32, 64, 128]), 7));

        // 4. Rational distributions
        data.Add(new WeightProfile("rational-mixed", WeightSet.FromRationalStrings(["1/2", "1/3", "1/6"]), 2));
        data.Add(new WeightProfile("rational-simple", WeightSet.FromRationalStrings(["3/4", "1/4"]), 1));
        data.Add(new WeightProfile("rational-5", WeightSet.FromRationalStrings(["1/3", "1/6", "1/6", "1/6", "1/6"]),
            4));

        // 5. Broad distributions
        data.Add(new WeightProfile("wide-20",
            WeightSet.FromIntegers([5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100]),
            19));

        return data;
    }

    [Theory]
    [MemberData(nameof(ChiSquaredProfiles))]
    public void AliasSampler_PassesChiSquared(WeightProfile profile)
    {
        const int n = 150_000;
        var alias = AliasMethod.Build(profile.Weights);
        SeededRandom rng = SeededRandom.ForStream(SlotMathConstants.Seeds.Main,
            StableStreamIndex(profile.Name) + ChiSquaredPinnedSalt);

        var observed = new int[profile.Weights.Count];
        for (var i = 0; i < n; i++)
        {
            observed[alias.Sample(rng)]++;
        }

        var pass = ChiSquaredDoesNotReject(observed, profile.Weights, profile.Df);
        Assert.True(pass,
            $"Profile '{profile.Name}' failed chi-squared at α=0.01. Observed: [{string.Join(", ", observed)}]");
    }

    private const long ChiSquaredPinnedSalt = 1;

    [Fact]
    public void AliasSampler_ChiSquared_NegativeControl_RejectsPerturbedWeights()
    {
        const int n = 150_000;
        WeightSet trueWeights = WeightSet.FromIntegers([1, 2, 4, 8, 16, 32]);
        var alias = AliasMethod.Build(trueWeights);
        SeededRandom rng = SeededRandom.ForStream(SlotMathConstants.Seeds.Main, StableStreamIndex("negative-control"));

        var observed = new int[trueWeights.Count];
        for (var i = 0; i < n; i++)
        {
            observed[alias.Sample(rng)]++;
        }

        Assert.True(ChiSquaredDoesNotReject(observed, trueWeights, 5), "Samples from true distribution must pass.");

        // Swapping weights must trigger rejection
        WeightSet perturbed = WeightSet.FromIntegers([1, 2, 4, 8, 32, 16]);
        Assert.False(ChiSquaredDoesNotReject(observed, perturbed, 5),
            "Negative control must reject perturbed distribution.");
    }

    private static long StableStreamIndex(string name)
    {
        var h = 1469598103934665603UL;
        foreach (var c in name)
        {
            h ^= c;
            h *= 1099511628211UL;
        }

        return (long)(h % int.MaxValue);
    }

    // ════════════════════════════════════════════════════════════════
    //  GLI-19 Certification & Invariants
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void AliasSampler_GuaranteesRngDrawParityAcrossAllRepresentations()
    {
        // GLI-19 Invariant: All execution paths (Uniform, Non-uniform, Single-outcome)
        // MUST consume the exact same sequence of RNG draws.
        var rngA = new SeededRandom(12345);
        var rngB = new SeededRandom(12345);

        WeightSet uniform = WeightSet.Uniform(5);
        WeightSet explicitWeights = WeightSet.FromIntegers([1, 1, 1, 1, 1]);

        var aliasUniform = AliasMethod.Build(uniform);
        var aliasExplicit = AliasMethod.Build(explicitWeights);

        for (var i = 0; i < 500; i++)
        {
            var sampleA = aliasUniform.Sample(rngA);
            var sampleB = aliasExplicit.Sample(rngB);
            Assert.Equal(sampleA, sampleB);
        }

        // Both RNG instances must remain strictly synchronized
        Assert.Equal(rngA.NextUInt64(), rngB.NextUInt64());
    }

    [Fact]
    public void AliasMethod_HandlesExtremeBigIntegerWeightsWithoutOverflowOrNaN()
    {
        // 2^1050 exceeds double.MaxValue (~1.79e308).
        // Must normalize through bit-shifting and avoid NaN.
        BigInteger massive1 = BigInteger.One << 1050;
        BigInteger massive2 = BigInteger.One << 1051;

        WeightSet weights = WeightSet.FromNumerators([massive1, massive2]);
        var alias = AliasMethod.Build(weights);

        var (prob, _) = alias.GetTables();
        foreach (var p in prob)
        {
            Assert.False(double.IsNaN(p), "Probability threshold cannot be NaN");
            Assert.False(double.IsInfinity(p), "Probability threshold cannot be Infinity");
        }

        var rng = new SeededRandom(42);
        for (var i = 0; i < 1000; i++)
        {
            var sample = alias.Sample(rng);
            Assert.InRange(sample, 0, 1);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Degenerate Cases & Bounds
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleOutcome_AlwaysReturnsZero()
    {
        WeightSet weights = WeightSet.FromIntegers([42]);
        var alias = AliasMethod.Build(weights);
        var rng = new SeededRandom(1);

        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(0, alias.Sample(rng));
        }
    }

    [Fact]
    public void ZeroWeights_Throws() =>
        Assert.Throws<ArgumentException>(() => AliasMethod.Build(WeightSet.FromIntegers([0, 0, 0])));

    [Fact]
    public void EmptyWeights_Throws() =>
        Assert.Throws<ArgumentException>(() => AliasMethod.Build(WeightSet.FromIntegers(Array.Empty<int>())));

    [Fact]
    public void SingleZeroWeight_WithPositiveOthers_IsNeverSampled()
    {
        WeightSet weights = WeightSet.FromIntegers([0, 10, 5]);
        var alias = AliasMethod.Build(weights);
        var rng = new SeededRandom(99);

        for (var i = 0; i < 10_000; i++)
        {
            Assert.NotEqual(0, alias.Sample(rng));
        }
    }

    [Fact]
    public void AliasTable_HasValidStructure()
    {
        WeightSet weights = WeightSet.FromIntegers([1, 2, 3, 4, 5]);
        var alias = AliasMethod.Build(weights);
        var (prob, aliasIdx) = alias.GetTables();

        Assert.Equal(5, prob.Length);
        Assert.Equal(5, aliasIdx.Length);

        for (var i = 0; i < prob.Length; i++)
        {
            Assert.InRange(prob[i], 0.0, 1.0);
            Assert.InRange(aliasIdx[i], 0, 4);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WeightSet Rational & Structural Correctness
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void WeightSet_PreservesExactRationalValues()
    {
        WeightSet ws = WeightSet.FromRationalStrings(["1/3", "2/3"]);

        Assert.Equal(2, ws.Count);
        Assert.Equal(new BigInteger(1), ws.Numerators[0]);
        Assert.Equal(new BigInteger(2), ws.Numerators[1]);
        Assert.Equal(new BigInteger(3), ws.Denominator);
        Assert.Equal(new BigInteger(3), ws.NumeratorSum);
        Assert.Equal(new BigInteger(9), ws.TotalDenominator);
    }

    [Fact]
    public void WeightSet_RationalStrings_ParsesMixedIntegersAndFractions()
    {
        WeightSet ws = WeightSet.FromRationalStrings(["1/2", "3/4", "5"]);
        Assert.Equal(3, ws.Count);
        Assert.True(ws.Numerators.All(n => n > 0));
        Assert.Equal(new BigInteger(4), ws.Denominator); // LCM(2, 4, 1) = 4
    }

    // ═════════════════════════════════════════════════════
    //  Seeded-Replay Contract (D24): tables and outcomes are pinned
    // ═════════════════════════════════════════════════════

    /// <summary>
    ///     The Walker–Vose build as first released: two FIFO queues. Which outcome a
    ///     (bucket, fraction) pair maps to depends on the pairing order, so any other
    ///     order changes every seeded weighted draw while leaving the distribution intact.
    /// </summary>
    private static (double[] Prob, int[] Alias) QueueReference(BigInteger[] weights)
    {
        var n = weights.Length;
        BigInteger total = weights.Aggregate(BigInteger.Zero, (sum, w) => sum + w);
        var scaled = new double[n];
        for (var i = 0; i < n; i++)
        {
            scaled[i] = (double)(weights[i] * n) / (double)total;
        }

        var prob = new double[n];
        var alias = new int[n];
        var small = new Queue<int>();
        var large = new Queue<int>();
        for (var i = 0; i < n; i++)
        {
            (scaled[i] < 1.0 ? small : large).Enqueue(i);
        }

        while (small.Count > 0 && large.Count > 0)
        {
            var s = small.Dequeue();
            var l = large.Dequeue();
            prob[s] = scaled[s];
            alias[s] = l;
            scaled[l] -= 1.0 - scaled[s];
            (scaled[l] < 1.0 ? small : large).Enqueue(l);
        }

        foreach (var i in large.Concat(small))
        {
            prob[i] = 1.0;
            alias[i] = i;
        }

        return (prob, alias);
    }

    private static readonly Gen<BigInteger> Weight = Gen.OneOf(
        Gen.Int[0, 20].Select(w => new BigInteger(w)),
        Gen.Int[0, 1_000_000].Select(w => new BigInteger(w)),
        Gen.Const(BigInteger.Zero),
        Gen.Const(BigInteger.One),
        // Far apart magnitudes: remainders that round to just below zero.
        Gen.Int[0, 800].Select(bits => BigInteger.One << bits),
        Gen.Select(Gen.Int[60, 800], Gen.Long[1, long.MaxValue]).Select((bits, low) => (BigInteger.One << bits) + low),
        // Close to the double range: 400 of these, scaled by the outcome count, stay below 2^1024.
        Gen.Int[801, 1010].Select(bits => BigInteger.One << bits),
        Gen.Select(Gen.Int[801, 1010], Gen.Long[1, long.MaxValue]).Select((bits, low) => (BigInteger.One << bits) + low));

    private static readonly Gen<BigInteger[]> Weights = Gen.OneOf(
        Weight.Array[2, 12],
        Weight.Array[2, 64],
        // Above the stack workspace limit: the pooled path.
        Weight.Array[257, 400],
        // Equal weights given explicitly, and one dominant outcome.
        Gen.Select(Gen.Int[2, 40], Gen.Int[1, 1000]).Select((n, w) => Enumerable.Repeat(new BigInteger(w), n).ToArray()),
        Gen.Select(Gen.Int[2, 40], Gen.Int[0, 39]).Select((n, at) => Enumerable.Range(0, n).Select(i => i == at % n ? BigInteger.One << 200 : BigInteger.One).ToArray()))
        .Where(w => w.Any(x => !x.IsZero));

    [Fact]
    public void AliasTable_Pbt_10000_MatchesQueueBuiltReferenceBitForBit()
    {
        Weights.Sample(weights =>
        {
            var (expectedProb, expectedAlias) = QueueReference(weights);
            var (prob, alias) = AliasMethod.Build(WeightSet.FromNumerators(weights)).GetTables();
            for (var i = 0; i < weights.Length; i++)
            {
                // A threshold below zero never accepts; it is stored as 0.0.
                if (BitConverter.DoubleToInt64Bits(System.Math.Max(expectedProb[i], 0.0)) != BitConverter.DoubleToInt64Bits(prob[i])
                    || expectedAlias[i] != alias[i])
                {
                    return false;
                }
            }

            return true;
        }, seed: "alias-table-replay-v1", iter: 10000, print: w => string.Join(",", w));
    }

    [Fact]
    public void SeededWeightedDraws_ArePinned()
    {
        // Recorded from the first released build. A change here invalidates every
        // stored seed of every model with a weighted draw.
        Assert.Equal([2, 2, 1, 2, 2, 1, 2, 2, 2, 2, 2, 1, 1, 2, 2, 1, 2, 1, 2, 2, 2, 2, 0, 0],
            Draw([1, 3, 4], 24));
        Assert.Equal([6, 5, 7, 6, 1, 6, 10, 7, 10, 10, 7, 3, 8, 10, 10, 2, 5, 1, 10, 6, 1, 7, 8, 12],
            Draw([1, 3, 4, 7, 2, 9, 5, 11, 6, 2, 8, 1, 3], 24));

        static int[] Draw(int[] weights, int count)
        {
            var alias = WeightSet.FromIntegers(weights).AliasTable;
            var rng = new SeededRandom(42);
            return Enumerable.Range(0, count).Select(_ => alias.Sample(rng)).ToArray();
        }
    }

    [Theory]
    [InlineData(961)]
    [InlineData(995)]
    [InlineData(1000)]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(1100)]
    [InlineData(5000)]
    public void AliasMethod_NearDoubleRange_KeepsFiniteThresholdsAndExactRatio(int bits)
    {
        // Three outcomes weighted 1:1:2 with a total around 2^bits. Scaling a numerator
        // by the outcome count must not overflow before the total is normalized.
        BigInteger unit = BigInteger.One << (bits - 2);
        var (prob, alias) = AliasMethod.Build(WeightSet.FromNumerators([unit, unit, unit * 2])).GetTables();

        Assert.Equal([0.75, 0.75, 1.0], prob);
        Assert.Equal([2, 2, 2], alias);
    }

    [Theory]
    [InlineData(961)]
    [InlineData(1000)]
    [InlineData(1022)]
    public void AliasMethod_TinyWeightBesideHugeOne_KeepsThePlainQuotient(int bits)
    {
        // Both operands still convert to a finite double: the threshold is the
        // plain quotient, as it was before the table was rewritten.
        BigInteger huge = BigInteger.One << bits;
        var (prob, alias) = AliasMethod.Build(WeightSet.FromNumerators([BigInteger.One, huge])).GetTables();

        Assert.Equal(2.0 / (double)(huge + 1), prob[0]);
        Assert.True(prob[0] > 0.0);
        Assert.Equal(1, alias[0]);
        Assert.Equal(1.0, prob[1]);
    }

    [Fact]
    public void AliasMethod_TinyWeightBeyondDoubleRange_StaysPositiveWhileRepresentable()
    {
        // 2 / (2^1050 + 1): the total is not a finite double, the share is a subnormal one.
        var (prob, alias) = AliasMethod.Build(WeightSet.FromNumerators([BigInteger.One, BigInteger.One << 1050])).GetTables();

        Assert.Equal(System.Math.ScaleB(1.0, -1049), prob[0]);
        Assert.Equal(1, alias[0]);
        Assert.Equal(1.0, prob[1]);

        // Below the smallest double the share is zero, and nothing is NaN or infinite.
        var (underflow, _) = AliasMethod.Build(WeightSet.FromNumerators([BigInteger.One, BigInteger.One << 1200])).GetTables();
        Assert.Equal([0.0, 1.0], underflow);
    }

    [Fact]
    public void WeightSet_UniformAndExplicitOnes_AreEqualAndHashAlike()
    {
        WeightSet uniform = WeightSet.Uniform(5);
        WeightSet explicitOnes = WeightSet.FromIntegers([1, 1, 1, 1, 1]);

        Assert.Equal(uniform, explicitOnes);
        Assert.Equal(explicitOnes, uniform);
        Assert.Equal(uniform.GetHashCode(), explicitOnes.GetHashCode());
        Assert.Single(new HashSet<WeightSet> { uniform, explicitOnes });

        Assert.NotEqual(uniform, WeightSet.FromIntegers([1, 1, 1, 2, 0]));
        Assert.NotEqual(WeightSet.FromIntegers([1, 2, 3]), WeightSet.FromIntegers([3, 2, 1]));
        Assert.Equal(WeightSet.FromIntegers([1, 2, 3]).GetHashCode(), WeightSet.FromIntegers([1, 2, 3]).GetHashCode());
    }

    [Fact]
    public void WeightSet_UniformNumerators_AreMaterializedOnce()
    {
        // The exact interpreters read Numerators once per outcome.
        WeightSet uniform = WeightSet.Uniform(1000);
        Assert.Same(uniform.Numerators, uniform.Numerators);
        Assert.All(uniform.Numerators, n => Assert.Equal(BigInteger.One, n));
        Assert.Equal(BigInteger.One, uniform[999]);
    }

    [Theory]
    [InlineData("1/2/3")]
    [InlineData("1//2")]
    public void WeightSet_RationalStrings_RejectsMalformedFractions(string weight) =>
        Assert.Throws<ArgumentException>(() => WeightSet.FromRationalStrings(["1", weight]));

    [Fact]
    public void SeededRandom_Determinism()
    {
        var r1 = new SeededRandom(42);
        var r2 = new SeededRandom(42);

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(r1.NextInt64(), r2.NextInt64());
        }
    }
}
