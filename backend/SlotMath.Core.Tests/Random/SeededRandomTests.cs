using System.Collections.Concurrent;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Random;

public sealed class SeededRandomTests
{
    // ════════════════════════════════════════════════════════════════
    //  1. GLI-19 Golden Test Vectors (Regression & Spec Pinning)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GoldenVectors_FixedSeed_MatchesCanonicalSequence()
    {
        // Pinned Golden Vector: Validates exact xoshiro256** + SplitMix64 math constants.
        // Any unintentional modification to bit shifts, rotations or constants will fail this gate.
        var rng = new SeededRandom(42);

        ulong[] expectedFirst5 =
        [
            0x15780B2E0C2EC716UL,
            0x6104D9866D113A7EUL,
            0xAE17533239E499A1UL,
            0xECB8AD4703B360A1UL,
            0xFDE6DC7FE2EC5E64UL
        ];

        for (var i = 0; i < expectedFirst5.Length; i++)
        {
            Assert.Equal(expectedFirst5[i], rng.NextUInt64());
        }
    }

    [Fact]
    public void DegenerateZeroSeed_PerturbsAndProducesNonZeroSequence()
    {
        // GLI-19 §2.4.1: PRNG must never lock into a degenerate zero-attractor state.
        var rng = new SeededRandom(0);

        Assert.Equal(0, rng.Seed);
        var values = new ulong[100];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = rng.NextUInt64();
            Assert.NotEqual(0UL, values[i]); // Must not emit continuous zeros
        }

        Assert.Equal(values.Length, values.Distinct().Count());
    }

    // ════════════════════════════════════════════════════════════════
    //  2. Determinism & Stream Identity
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void FixedSeed_ProducesIdenticalSequences(long seed)
    {
        var rng1 = new SeededRandom(seed);
        var rng2 = new SeededRandom(seed);

        Assert.Equal(seed, rng1.Seed);
        Assert.Equal(seed, rng2.Seed);

        for (var i = 0; i < 500; i++)
        {
            Assert.Equal(rng1.NextUInt64(), rng2.NextUInt64());
            Assert.Equal(rng1.NextInt64(), rng2.NextInt64());
            Assert.Equal(rng1.NextDouble(), rng2.NextDouble());
            Assert.Equal(rng1.Next(37), rng2.Next(37));
        }
    }

    [Fact]
    public void DifferentSeeds_ProduceStatisticallyDivergentSequences()
    {
        var rng1 = new SeededRandom(100);
        var rng2 = new SeededRandom(101);

        var seq1 = new ulong[50];
        var seq2 = new ulong[50];

        for (var i = 0; i < 50; i++)
        {
            seq1[i] = rng1.NextUInt64();
            seq2[i] = rng2.NextUInt64();
        }

        Assert.NotEqual(seq1, seq2);
    }

    // ════════════════════════════════════════════════════════════════
    //  3. Clone Invariants
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Clone_ResetsToSeedStart_RegardlessOfConsumption()
    {
        var original = new SeededRandom(777);

        // Record the first 100 values from t=0
        var initialBatch = new ulong[100];
        for (var i = 0; i < initialBatch.Length; i++)
        {
            initialBatch[i] = original.NextUInt64();
        }

        // Consume further 500 draws so the original stream moves deep into sequence
        for (var i = 0; i < 500; i++)
        {
            original.NextUInt64();
        }

        // Clone should reset back to initial expansion seed (t=0)
        SeededRandom clone = original.Clone();
        Assert.Equal(original.Seed, clone.Seed);

        for (var i = 0; i < initialBatch.Length; i++)
        {
            Assert.Equal(initialBatch[i], clone.NextUInt64());
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  4. Statistical Range & Precision (IEEE 754 & Modulo Bounds)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void NextDouble_AdheresStrictlyTo53BitPrecisionMantissa()
    {
        var rng = new SeededRandom(42);
        const double twoTo53 = 9007199254740992.0; // 2^53

        for (var i = 0; i < 20_000; i++)
        {
            var d = rng.NextDouble();

            // Interval requirement: [0.0, 1.0)
            Assert.InRange(d, 0.0, 0.9999999999999999);
            Assert.True(d < 1.0, "NextDouble must never produce 1.0");

            // Precision requirement: Must strictly be an integer multiple of 2^-53
            var mantissa = d * twoTo53;
            Assert.Equal(mantissa, System.Math.Floor(mantissa));
        }
    }

    [Fact]
    public void Next_BoundaryCases_ProducesStrictlyBoundedValues()
    {
        var rng = new SeededRandom(1337);

        // Boundary 1: Smallest possible valid range [0, 1) -> must always return 0
        for (var i = 0; i < 1_000; i++)
        {
            Assert.Equal(0, rng.Next(1));
        }

        // Boundary 2: Power-of-two minus one, exact power-of-two, power-of-two plus one
        int[] testBounds = [2, 3, 4, 31, 32, 33, 63, 64, 65, 1023, 1024, int.MaxValue];

        foreach (var bound in testBounds)
        {
            for (var i = 0; i < 500; i++)
            {
                var val = rng.Next(bound);
                Assert.InRange(val, 0, bound - 1);
            }
        }
    }

    [Fact]
    public void Next_ThrowsOnNonPositiveBoundaries()
    {
        var rng = new SeededRandom(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(int.MinValue));
    }

    [Fact]
    public void Next_IsUnbiasedUniform_ChiSquaredTest()
    {
        var rng = new SeededRandom(12345);
        const int buckets = 10;
        const int n = 150_000;
        var counts = new int[buckets];

        for (var i = 0; i < n; i++)
        {
            counts[rng.Next(buckets)]++;
        }

        const double expected = n / (double)buckets;
        var chi2 = 0.0;
        for (var i = 0; i < buckets; i++)
        {
            var diff = counts[i] - expected;
            chi2 += diff * diff / expected;
        }

        // Critical value for χ²(df=9, α=0.01) = 21.666
        Assert.True(chi2 <= 21.666, $"Chi-squared uniformity test failed: chi2={chi2}");
    }

    // ════════════════════════════════════════════════════════════════
    //  5. Stream Splitting (PRD v3.1 / D3 Parallel Monte Carlo)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ForStream_ProducesBitIdenticalSequencesIndependently()
    {
        const ulong masterSeed = 0xDEADBEEFCAFEUL;

        for (long streamId = 0; streamId < 16; streamId++)
        {
            SeededRandom streamA = SeededRandom.ForStream(masterSeed, streamId);
            SeededRandom streamB = SeededRandom.ForStream(masterSeed, streamId);

            for (var k = 0; k < 100; k++)
            {
                Assert.Equal(streamA.NextUInt64(), streamB.NextUInt64());
            }
        }
    }

    [Fact]
    public void ForStream_ParallelExecution_IsThreadSafeAndNonOverlapping()
    {
        const ulong masterSeed = 0xAA55AA5511223344UL;
        const int streamCount = 64;
        var streamResults = new ConcurrentDictionary<int, ulong[]>();

        // Emulates multi-threaded Monte Carlo worker threads
        Parallel.For(0, streamCount, i =>
        {
            SeededRandom stream = SeededRandom.ForStream(masterSeed, i);
            var samples = new ulong[20];
            for (var k = 0; k < samples.Length; k++)
            {
                samples[k] = stream.NextUInt64();
            }

            streamResults[i] = samples;
        });

        // Ensure distinct streams did not collide or corrupt each other
        for (var i = 0; i < streamCount; i++)
        {
            for (var j = i + 1; j < streamCount; j++)
            {
                Assert.NotEqual(streamResults[i], streamResults[j]);
            }
        }
    }

    [Fact]
    public void ForStream_RejectsNegativeStreamIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SeededRandom.ForStream(42UL, -1L));
        Assert.Throws<ArgumentOutOfRangeException>(() => SeededRandom.ForStream(42UL, long.MinValue));
    }

    // ════════════════════════════════════════════════════════════════
    //  6. Regulatory Audit Trail Integrity
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void EnableAudit_DoesNotAlterRngOutputSequence()
    {
        // GLI-19 Invariant: Observability must have zero side-effects on stream draws.
        var rngStandard = new SeededRandom(999);
        var rngAudited = new SeededRandom(999);

        rngAudited.EnableAudit();

        for (var i = 0; i < 1_000; i++)
        {
            Assert.Equal(rngStandard.NextUInt64(), rngAudited.NextUInt64());
        }

        rngAudited.EndAudit();
    }
}
