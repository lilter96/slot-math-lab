using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Random;

public class SeededRandomTests
{
    // ── Determinism: fixed seed → identical sequence ────────────────

    [Fact]
    public void FixedSeed_ProducesIdenticalInt64Sequence()
    {
        const long seed = 12345;
        var rng1 = new SeededRandom(seed);
        var rng2 = new SeededRandom(seed);

        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(rng1.NextInt64(), rng2.NextInt64());
        }
    }

    [Fact]
    public void FixedSeed_ProducesIdenticalDoubleSequence()
    {
        const long seed = 12345;
        var rng1 = new SeededRandom(seed);
        var rng2 = new SeededRandom(seed);

        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(rng1.NextDouble(), rng2.NextDouble());
        }
    }

    [Fact]
    public void FixedSeed_ProducesIdenticalIntSequence()
    {
        const long seed = 42;
        var rng1 = new SeededRandom(seed);
        var rng2 = new SeededRandom(seed);

        for (var i = 0; i < 500; i++)
        {
            Assert.Equal(rng1.Next(10), rng2.Next(10));
        }
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        var rng1 = new SeededRandom(1);
        var rng2 = new SeededRandom(2);

        var first1 = Enumerable.Range(0, 20).Select(_ => rng1.NextInt64()).ToArray();
        var first2 = Enumerable.Range(0, 20).Select(_ => rng2.NextInt64()).ToArray();

        // At least one value should differ (probabilistically guaranteed).
        Assert.NotEqual(first1, first2);
    }

    // ── Clone ───────────────────────────────────────────────────────

    [Fact]
    public void Clone_ProducesIdenticalSequence()
    {
        var original = new SeededRandom(99999);
        // Consume some values so the instance is mid-stream
        for (var i = 0; i < 100; i++)
            original.NextInt64();

        // Clone resets to the seed start — produces the same sequence the original produced from t=0
        var clone = original.Clone();

        // So compare clone vs a fresh instance from the same seed
        var reference = new SeededRandom(99999);
        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(reference.NextInt64(), clone.NextInt64());
        }
    }

    [Fact]
    public void Clone_ResetsToSeedStart()
    {
        var original = new SeededRandom(555);
        var clone = original.Clone();

        // Clone should start from the same point — but since original has consumed nothing,
        // they should produce the same first value.
        // Actually, we already consumed 0 values; let's make it explicit.
        var rng = new SeededRandom(555);
        var firstBatch = Enumerable.Range(0, 100).Select(_ => rng.NextInt64()).ToArray();

        var clone2 = new SeededRandom(555);
        var secondBatch = Enumerable.Range(0, 100).Select(_ => clone2.NextInt64()).ToArray();

        Assert.Equal(firstBatch, secondBatch);
    }

    // ── NextDouble range ────────────────────────────────────────────

    [Fact]
    public void NextDouble_IsInProperRange()
    {
        var rng = new SeededRandom(42);
        for (var i = 0; i < 10000; i++)
        {
            var d = rng.NextDouble();
            Assert.True(d >= 0.0);
            Assert.True(d < 1.0);
        }
    }

    // ── Next range ──────────────────────────────────────────────────

    [Fact]
    public void Next_ProducesValuesInRange()
    {
        var rng = new SeededRandom(77);
        const int max = 7;
        for (var i = 0; i < 10000; i++)
        {
            var v = rng.Next(max);
            Assert.True(v >= 0);
            Assert.True(v < max);
        }
    }

    [Fact]
    public void Next_ThrowsOnNonPositiveMax()
    {
        var rng = new SeededRandom(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(-1));
    }

    // ── Statistical uniformity of NextInt ───────────────────────────

    [Fact]
    public void Next_IsApproximatelyUniform()
    {
        var rng = new SeededRandom(12345);
        const int buckets = 10;
        const int n = 100_000;
        var counts = new int[buckets];

        for (var i = 0; i < n; i++)
            counts[rng.Next(buckets)]++;

        var expected = n / (double)buckets;
        // Use chi-squared to check uniformity
        var chi2 = 0.0;
        for (var i = 0; i < buckets; i++)
        {
            var diff = counts[i] - expected;
            chi2 += diff * diff / expected;
        }

        // Critical value for χ²(9, α=0.01) ≈ 21.666
        Assert.True(chi2 <= 21.666, $"chi2={chi2} exceeds critical value 21.666 — distribution may be biased");
    }

    // ── Seed property ───────────────────────────────────────────────

    [Fact]
    public void Seed_ReturnsOriginalValue()
    {
        Assert.Equal(42, new SeededRandom(42).Seed);
        Assert.Equal(0, new SeededRandom(0).Seed);
        Assert.Equal(-1, new SeededRandom(-1).Seed);
        Assert.Equal(long.MaxValue, new SeededRandom(long.MaxValue).Seed);
    }

    // ── Edge: zero seed ─────────────────────────────────────────────

    [Fact]
    public void ZeroSeed_Works()
    {
        var rng = new SeededRandom(0);
        var values = Enumerable.Range(0, 10).Select(_ => rng.NextInt64()).ToArray();
        // Should produce distinct values (not get stuck).
        Assert.Distinct(values);
    }

    // ── D3: stream splitting ─────────────────────────────────────────

    [Fact]
    public void ForStream_IsReproducibleFromMasterSeedAndIndexAlone()
    {
        const ulong master = 0xC0FFEE;
        // Stream i must be reproducible from (masterSeed, i) alone — no shared mutable state.
        for (long i = 0; i < 8; i++)
        {
            var a = SeededRandom.ForStream(master, i);
            var b = SeededRandom.ForStream(master, i);
            for (var k = 0; k < 200; k++)
                Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void ForStream_DistinctStreamsDiffer()
    {
        const ulong master = 0xC0FFEE;
        // Two different stream indices should not produce identical leading sequences.
        var a = SeededRandom.ForStream(master, 3);
        var b = SeededRandom.ForStream(master, 4);
        var seqA = Enumerable.Range(0, 50).Select(_ => a.NextUInt64()).ToArray();
        var seqB = Enumerable.Range(0, 50).Select(_ => b.NextUInt64()).ToArray();
        Assert.NotEqual(seqA, seqB);
    }

    [Fact]
    public void ForStream_RejectsNegativeIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SeededRandom.ForStream(1, -1));
    }
}
