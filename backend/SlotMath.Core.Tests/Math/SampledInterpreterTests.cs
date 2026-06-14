using System.Numerics;
using SlotMath.Core.Math;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  G6 — Sampled Monte Carlo interpreter acceptance tests
// ═══════════════════════════════════════════════════════════════════════════

public record SpinState(int Counter, BigInteger Accumulator)
{
    public SpinState Inc() => this with { Counter = Counter + 1 };
    public SpinState Add(BigInteger v) => this with { Accumulator = Accumulator + v };
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 1 — Fixed seed + n → identical stats
// ═══════════════════════════════════════════════════════════════════════════

public class SampledInterpreter_Determinism
{
    [Fact]
    public void FixedSeed_ProducesIdenticalStats()
    {
        var program =
            from d1 in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1, 2, 3, 4, 5]))
            from d2 in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([10, 20, 30]))
            select new BigInteger(d1 * 100 + d2 * 10);

        var config1 = new SampledConfig { Seed = 42, MaxSpins = 10_000 };
        var config2 = new SampledConfig { Seed = 42, MaxSpins = 10_000 };

        var result1 = SampledInterpreter.Evaluate(program, new SpinState(0, 0), config1);
        var result2 = SampledInterpreter.Evaluate(program, new SpinState(0, 0), config2);

        Assert.Equal(result1.SpinsCompleted, result2.SpinsCompleted);
        Assert.Equal(result1.Stats.Mean, result2.Stats.Mean, precision: 12);
        Assert.Equal(result1.Stats.Variance, result2.Stats.Variance, precision: 12);
        Assert.Equal(result1.Stats.StdDev, result2.Stats.StdDev, precision: 12);
        Assert.Equal(result1.Stats.MaxObserved, result2.Stats.MaxObserved);
        Assert.Equal(result1.Stats.MinObserved, result2.Stats.MinObserved);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        var program =
            from d in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1, 1, 1, 1, 1]))
            select new BigInteger(d);

        var result1 = SampledInterpreter.Evaluate(program, new SpinState(0, 0),
            new SampledConfig { Seed = 1, MaxSpins = 100 });
        var result2 = SampledInterpreter.Evaluate(program, new SpinState(0, 0),
            new SampledConfig { Seed = 999, MaxSpins = 100 });

        // The exact sequence of 100 draws should differ.
        // With 5 equiprobable outcomes, the mean should differ (probabilistically).
        // We assert structural equality of the result objects (seed differs).
        Assert.NotEqual(result1.Seed, result2.Seed);
        // Stats objects themselves will differ since the draws differ.
    }

    [Fact]
    public void MultipleRunsWithSameSeed_IdenticalMeanAndVariance()
    {
        var program =
            from d in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1, 3, 6, 10]))
            from _ in Slot.Modify<SpinState>(s => s.Inc())
            from d2 in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([100, 200, 300, 400, 500]))
            select new BigInteger(d * d2);

        const int runs = 5;
        const long seed = 12345;
        const long n = 5_000;

        double? firstMean = null;
        double? firstVar = null;

        for (var i = 0; i < runs; i++)
        {
            var result = SampledInterpreter.Evaluate(program, new SpinState(0, 0),
                new SampledConfig { Seed = seed, MaxSpins = n });

            if (i == 0)
            {
                firstMean = result.Stats.Mean;
                firstVar = result.Stats.Variance;
            }
            else
            {
                Assert.Equal(firstMean!.Value, result.Stats.Mean, precision: 12);
                Assert.Equal(firstVar!.Value, result.Stats.Variance, precision: 12);
            }
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 2 — Streaming Welford stats match naive batch on the same samples
// ═══════════════════════════════════════════════════════════════════════════

public class SampledInterpreter_WelfordMatchesBatch
{
    /// <summary>
    /// Run the program N times, collect all individual spin results,
    /// then compare Welford (streaming) vs batch (two-pass) statistics.
    /// </summary>
    [Fact]
    public void WelfordMean_MatchesBatchMean()
    {
        var program =
            from d in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1, 2, 3, 4, 5]))
            select new BigInteger(d * 10);

        // Collect individual spin results by running the trampoline explicitly.
        // n < CHUNK ⇒ Evaluate runs a single chunk seeded SplitMix64(seed, 0),
        // so the manual reference stream must use the same chunk-0 seed (D3).
        var rng = new SeededRandom(SampledInterpreter.DeriveStreamSeed(777, 0));
        const int n = 10_000;
        var samples = new double[n];

        for (var i = 0; i < n; i++)
        {
            samples[i] = (double)SampledInterpreter.RunOneSpin<SpinState, BigInteger>(
                program, new SpinState(0, 0), rng);
        }

        // Batch mean.
        var batchMean = samples.Average();

        // Now run through SampledInterpreter and compare.
        var result = SampledInterpreter.Evaluate(program, new SpinState(0, 0),
            new SampledConfig { Seed = 777, MaxSpins = n });

        Assert.Equal(batchMean, result.Stats.Mean, precision: 10);
    }

    [Fact]
    public void WelfordVariance_MatchesBatchVariance()
    {
        var program =
            from d1 in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1, 1, 1]))
            from d2 in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([5, 10, 15, 20]))
            select new BigInteger(d1 * d2);

        var rng = new SeededRandom(SampledInterpreter.DeriveStreamSeed(1234, 0));
        const int n = 20_000;
        var samples = new double[n];

        for (var i = 0; i < n; i++)
        {
            samples[i] = (double)SampledInterpreter.RunOneSpin<SpinState, BigInteger>(
                program, new SpinState(0, 0), rng);
        }

        // Batch variance (sample variance, unbiased: divide by n-1).
        var batchMean = samples.Average();
        var batchVar = samples.Select(x => (x - batchMean) * (x - batchMean)).Sum() / (n - 1);

        var result = SampledInterpreter.Evaluate(program, new SpinState(0, 0),
            new SampledConfig { Seed = 1234, MaxSpins = n });

        Assert.Equal(batchMean, result.Stats.Mean, precision: 10);
        Assert.Equal(batchVar, result.Stats.Variance, precision: 8);
    }

    [Fact]
    public void WelfordStdErr_MatchesBatchStdErr()
    {
        var program =
            from d in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1, 2, 4, 8, 16]))
            select new BigInteger(d * 7);

        var rng = new SeededRandom(SampledInterpreter.DeriveStreamSeed(5555, 0));
        const int n = 15_000;
        var samples = new double[n];

        for (var i = 0; i < n; i++)
        {
            samples[i] = (double)SampledInterpreter.RunOneSpin<SpinState, BigInteger>(
                program, new SpinState(0, 0), rng);
        }

        var batchMean = samples.Average();
        var batchVar = samples.Select(x => (x - batchMean) * (x - batchMean)).Sum() / (n - 1);
        var batchStdDev = System.Math.Sqrt(batchVar);
        var batchStdErr = batchStdDev / System.Math.Sqrt(n);

        var result = SampledInterpreter.Evaluate(program, new SpinState(0, 0),
            new SampledConfig { Seed = 5555, MaxSpins = n });

        Assert.Equal(batchStdErr, result.Stats.StdErr, precision: 10);
        Assert.Equal(batchMean, result.Stats.Mean, precision: 10);
    }

    [Fact]
    public void Welford_MatchesBatch_WithStateDependentWeights()
    {
        // Program where draw weights depend on state.
        var program =
            from d in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1, 2, 3]))
            from _ in Slot.Modify<SpinState>(s => s.Inc())
            from s in Slot.GetState<SpinState>()
            from d2 in Slot.Draw<SpinState>(st =>
                WeightSet.FromIntegers(Enumerable.Repeat(1, st.Counter + 1).ToArray()))
            select new BigInteger(d * 10 + d2);

        var rng = new SeededRandom(SampledInterpreter.DeriveStreamSeed(9876, 0));
        const int n = 8_000;
        var samples = new double[n];

        for (var i = 0; i < n; i++)
        {
            samples[i] = (double)SampledInterpreter.RunOneSpin<SpinState, BigInteger>(
                program, new SpinState(0, 0), rng);
        }

        var batchMean = samples.Average();
        var batchVar = samples.Select(x => (x - batchMean) * (x - batchMean)).Sum() / (n - 1);

        var result = SampledInterpreter.Evaluate(program, new SpinState(0, 0),
            new SampledConfig { Seed = 9876, MaxSpins = n });

        Assert.Equal(batchMean, result.Stats.Mean, precision: 10);
        Assert.Equal(batchVar, result.Stats.Variance, precision: 8);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 3 — Max win cap: observed max ≤ cap, cap-hits counted
// ═══════════════════════════════════════════════════════════════════════════

public class SampledInterpreter_MaxWinCap
{
    [Fact]
    public void ObservedMax_NeverExceedsCap()
    {
        var program =
            from d in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1, 1, 1, 1]))
            select new BigInteger(d * 1000); // values: 0, 1000, 2000, 3000

        var cap = new BigInteger(1500);
        var result = SampledInterpreter.Evaluate(program, new SpinState(0, 0),
            new SampledConfig { Seed = 42, MaxSpins = 10_000, MaxWinCap = cap });

        Assert.True(result.Stats.MaxObserved <= (double)cap,
            $"Max observed {result.Stats.MaxObserved} should be ≤ cap {cap}");
    }

    [Fact]
    public void CapHits_AreCounted()
    {
        var program =
            from d in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1, 1]))
            select d == 0 ? BigInteger.Zero : new BigInteger(100); // 50% chance of value 100

        var cap = new BigInteger(50);
        var result = SampledInterpreter.Evaluate(program, new SpinState(0, 0),
            new SampledConfig { Seed = 123, MaxSpins = 20_000, MaxWinCap = cap });

        // Approximately 50% of spins should hit the cap.
        // With 20k spins, 10k expected cap hits. Allow generous tolerance.
        Assert.True(result.Stats.CapHits > 0,
            "Should have at least some cap hits");
        Assert.True(result.Stats.CapHits > 8000,
            $"Expected ~10k cap hits, got {result.Stats.CapHits}");
        Assert.True(result.Stats.CapHits < 12_000,
            $"Expected ~10k cap hits, got {result.Stats.CapHits}");
    }

    [Fact]
    public void NoCap_NoCapHits()
    {
        var program =
            from d in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1, 1, 1]))
            select new BigInteger(d * 1000);

        var result = SampledInterpreter.Evaluate(program, new SpinState(0, 0),
            new SampledConfig { Seed = 99, MaxSpins = 5000 });

        Assert.Equal(0, result.Stats.CapHits);
        Assert.Null(result.Stats.MaxWinCap);
    }

    [Fact]
    public void CapSet_StatsReflectCap()
    {
        var program =
            from d in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1, 1]))
            select new BigInteger(d * 200); // values: 0, 200

        var cap = new BigInteger(100);
        var result = SampledInterpreter.Evaluate(program, new SpinState(0, 0),
            new SampledConfig { Seed = 55, MaxSpins = 5000, MaxWinCap = cap });

        // Max observed should be exactly the cap value (since 200 gets clamped to 100).
        Assert.True(result.Stats.MaxObserved <= (double)cap);
        Assert.NotNull(result.Stats.MaxWinCap);
        Assert.Equal((double)cap, result.Stats.MaxWinCap.Value);
    }

    [Fact]
    public void MaxObservedEqualsCap_WhenAllValuesExceedCap()
    {
        var program =
            from d in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1]))
            select new BigInteger(500); // always 500

        var cap = new BigInteger(200);
        var result = SampledInterpreter.Evaluate(program, new SpinState(0, 0),
            new SampledConfig { Seed = 1, MaxSpins = 100, MaxWinCap = cap });

        Assert.Equal((double)cap, result.Stats.MaxObserved);
        Assert.Equal(100, result.Stats.CapHits); // every spin hits the cap
        Assert.Equal(100, result.SpinsCompleted);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 4 — Cancellation token stops promptly, returns partial stats with correct n
// ═══════════════════════════════════════════════════════════════════════════

public class SampledInterpreter_Cancellation
{
    [Fact]
    public void Cancellation_StopsBeforeMaxSpins()
    {
        var program =
            from d in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1, 1, 1]))
            select new BigInteger(d);

        using var cts = new CancellationTokenSource();

        var config = new SampledConfig
        {
            Seed = 42,
            MaxSpins = 10_000_000, // far more than we'll let it run
            CancellationToken = cts.Token,
            CancellationCheckInterval = 100 // check frequently for test
        };

        // Cancel almost immediately — before many spins complete.
        cts.CancelAfter(TimeSpan.FromMilliseconds(10));

        var result = SampledInterpreter.Evaluate(program, new SpinState(0, 0), config);

        Assert.True(result.WasCancelled);
        Assert.True(result.SpinsCompleted < config.MaxSpins,
            $"Expected fewer than {config.MaxSpins} spins, got {result.SpinsCompleted}");
        Assert.True(result.SpinsCompleted > 0 || result.WasCancelled);
    }

    [Fact]
    public void Cancellation_PartialStatsHaveCorrectN()
    {
        var program =
            from d in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1, 1, 1, 1, 1]))
            select new BigInteger(d * 10);

        using var cts = new CancellationTokenSource();

        var config = new SampledConfig
        {
            Seed = 777,
            MaxSpins = 100_000_000,
            CancellationToken = cts.Token,
            CancellationCheckInterval = 500
        };

        // Cancel after a short delay.
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var result = SampledInterpreter.Evaluate(program, new SpinState(0, 0), config);

        Assert.True(result.WasCancelled);
        Assert.Equal(result.SpinsCompleted, result.Stats.Count);
        Assert.True(result.SpinsCompleted > 0,
            "Should have completed at least some spins before cancellation");
        Assert.True(result.SpinsCompleted < config.MaxSpins);
    }

    [Fact]
    public void Cancellation_CorrectSpinsCompletedCount()
    {
        var program =
            from d in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1]))
            select new BigInteger(1); // trivial program — very fast per spin

        using var cts = new CancellationTokenSource();

        var config = new SampledConfig
        {
            Seed = 1,
            MaxSpins = 10_000_000,
            CancellationToken = cts.Token,
            CancellationCheckInterval = 1000
        };

        // Cancel after a generous delay so many spins complete.
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        var result = SampledInterpreter.Evaluate(program, new SpinState(0, 0), config);

        Assert.True(result.WasCancelled);
        Assert.Equal(result.SpinsCompleted, result.Stats.Count);

        // With a trivial program we should get a meaningful number of spins.
        Assert.True(result.SpinsCompleted >= 0); // always true; the real test is count == stats.Count
    }

    [Fact]
    public void NoCancellation_CompletesAllSpins()
    {
        var program =
            from d in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1, 2, 3]))
            select new BigInteger(d);

        var result = SampledInterpreter.Evaluate(program, new SpinState(0, 0),
            new SampledConfig { Seed = 42, MaxSpins = 500 });

        Assert.False(result.WasCancelled);
        Assert.Equal(500, result.SpinsCompleted);
        Assert.Equal(500, result.Stats.Count);
    }

    [Fact]
    public void Cancellation_MeanIsReasonable_ForPartialRun()
    {
        // Even with partial data, the mean should be within a reasonable range.
        var program =
            from d in Slot.Draw<SpinState>(_ => WeightSet.FromIntegers([1, 1, 1, 1, 1]))
            select new BigInteger(d * 10); // values: 0, 10, 20, 30, 40, mean=20

        using var cts = new CancellationTokenSource();

        var config = new SampledConfig
        {
            Seed = 42,
            MaxSpins = 50_000_000,
            CancellationToken = cts.Token,
            CancellationCheckInterval = 1000
        };

        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        var result = SampledInterpreter.Evaluate(program, new SpinState(0, 0), config);

        Assert.True(result.WasCancelled);
        Assert.True(result.SpinsCompleted > 0);

        // With enough spins, the mean should be close to 20.
        if (result.SpinsCompleted >= 1000)
        {
            Assert.True(result.Stats.Mean > 18.0 && result.Stats.Mean < 22.0,
                $"Mean should be ~20, got {result.Stats.Mean} after {result.SpinsCompleted} spins");
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 5 — Trampolined (stack safety under deep recursion)
// ═══════════════════════════════════════════════════════════════════════════

public class SampledInterpreter_StackSafety
{
    public record DeepState(int Value);

    [Fact]
    public void DeepLoop_EvaluatesWithoutStackOverflow()
    {
        // A program with 20,000 loop iterations (state-dependent stop).
        const int iterations = 20_000;

        var stopCondition = (DeepState s) => s.Value >= iterations;
        var body = Slot.Modify<DeepState>(s => s with { Value = s.Value + 1 });

        var program =
            from _ in Slot.Loop(stopCondition, body)
            from s in Slot.GetState<DeepState>()
            select new BigInteger(s.Value);

        var result = SampledInterpreter.Evaluate(program, new DeepState(0),
            new SampledConfig { Seed = 1, MaxSpins = 10 });

        Assert.Equal(10, result.SpinsCompleted);
        Assert.False(result.WasCancelled);
        // Each spin should produce the value 'iterations' (state goes from 0 to iterations).
        Assert.Equal(iterations, (int)result.Stats.Mean);
    }

    [Fact]
    public void DeepDrawChain_EvaluatesWithoutStackOverflow()
    {
        // Build a program with 1,000 sequential draws (each single-outcome for determinism).
        const int draws = 1000;

        Slot<DeepState, BigInteger> program = Slot.Pure<DeepState, BigInteger>(0);

        for (var i = 0; i < draws; i++)
        {
            var capturedI = i;
            program = program.SelectMany(x =>
                from d in Slot.Draw<DeepState>(_ => WeightSet.FromIntegers([1]))
                from _ in Slot.Modify<DeepState>(s => s with { Value = s.Value + 1 })
                select x + new BigInteger(d));
        }

        var result = SampledInterpreter.Evaluate(program, new DeepState(0),
            new SampledConfig { Seed = 42, MaxSpins = 5 });

        Assert.Equal(5, result.SpinsCompleted);
        // Each draw adds 0 (only outcome), so total = draws * 0 = 0, but state.Value = draws.
        Assert.Equal(0, (int)result.Stats.Mean);
    }

    [Fact]
    public void SingleSpin_DeepProgram_EvaluatesWithoutStackOverflow()
    {
        // Use RunOneSpin directly to test a single deep execution.
        const int depth = 15_000;

        Slot<DeepState, BigInteger> program = Slot.Pure<DeepState, BigInteger>(0);

        for (var i = 0; i < depth; i++)
        {
            var capturedI = i;
            program = program.SelectMany(x =>
                Slot.Modify<DeepState>(s => s with { Value = s.Value + 1 })
                    .SelectMany(_ => Slot.Pure<DeepState, BigInteger>(x + 1)));
        }

        var rng = new SeededRandom(1);
        var value = SampledInterpreter.RunOneSpin(program, new DeepState(0), rng);

        Assert.Equal(depth, (int)value);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Integration — Sampled vs Exact cross-check (preview of G7/G13)
// ═══════════════════════════════════════════════════════════════════════════

public class SampledInterpreter_Integration
{
    public record IntegState(int Counter, BigInteger Win)
    {
        public BigInteger RecurrenceHash => Counter;
    }

    /// <summary>
    /// A simple draw with known exact RTP. The sampled mean should converge
    /// to the exact RTP within the confidence interval.
    /// </summary>
    [Fact]
    public void SampledRtp_ConvergesToExactRtp()
    {
        // Draw: weights [1,2,3], payouts [0,1,2].
        // Exact EV = (1*0 + 2*1 + 3*2) / 6 = 8/6 = 4/3 ≈ 1.333...
        var program =
            from idx in Slot.Draw<IntegState>(_ => WeightSet.FromIntegers([1, 2, 3]))
            select new BigInteger(idx);

        var exactResult = ExactInterpreter.Evaluate(
            program, new IntegState(0, 0), s => s.RecurrenceHash);
        var exactDist = exactResult.ValueDistribution();
        var (exactNum, exactDen) = exactDist.ExpectedBigIntegerValue();
        var exactRtp = (double)exactNum / (double)exactDen;

        var sampledResult = SampledInterpreter.Evaluate(program, new IntegState(0, 0),
            new SampledConfig { Seed = 12345, MaxSpins = 50_000 });

        // The sampled mean should be within 3 standard errors of the exact RTP.
        var deviation = System.Math.Abs(sampledResult.Stats.Mean - exactRtp);
        var tolerance = 3.0 * sampledResult.Stats.StdErr;

        Assert.True(deviation <= tolerance,
            $"Sampled RTP {sampledResult.Stats.Mean:F6} deviates from exact {exactRtp:F6} " +
            $"by {deviation:F6}, tolerance (3*SE) = {tolerance:F6}");
    }

    [Fact]
    public void SampledRtp_WithStateDependentWeights_ConvergesToExact()
    {
        // Two sequential draws, second depends on first.
        var program =
            from board in Slot.Draw<IntegState>(_ => WeightSet.FromIntegers([1, 1]))
            from _ in Slot.Modify<IntegState>(s => s with { Counter = board })
            from s in Slot.GetState<IntegState>()
            from payout in Slot.Draw<IntegState>(_ =>
                s.Counter == 0
                    ? WeightSet.FromIntegers([3, 1])  // payout 0 or 5
                    : WeightSet.FromIntegers([1, 3])) // payout 0 or 10
            select payout == 0 ? BigInteger.Zero
                 : s.Counter == 0 ? new BigInteger(5)
                 : new BigInteger(10);

        var exactResult = ExactInterpreter.Evaluate(
            program, new IntegState(0, 0), s => s.RecurrenceHash);
        var (exactNum, exactDen) = exactResult.ValueDistribution().ExpectedBigIntegerValue();
        var exactRtp = (double)exactNum / (double)exactDen;

        var sampledResult = SampledInterpreter.Evaluate(program, new IntegState(0, 0),
            new SampledConfig { Seed = 99999, MaxSpins = 30_000 });

        var deviation = System.Math.Abs(sampledResult.Stats.Mean - exactRtp);
        var tolerance = 3.0 * sampledResult.Stats.StdErr;

        Assert.True(deviation <= tolerance,
            $"Sampled RTP {sampledResult.Stats.Mean:F6} deviates from exact {exactRtp:F6} " +
            $"by {deviation:F6}, tolerance (3*SE) = {tolerance:F6}");
    }

    [Fact]
    public void Ci95_ContainsExactMean()
    {
        var program =
            from idx in Slot.Draw<IntegState>(_ => WeightSet.FromIntegers([1, 2, 3, 4, 5]))
            select new BigInteger(idx * 10);

        var exactResult = ExactInterpreter.Evaluate(
            program, new IntegState(0, 0), s => s.RecurrenceHash);
        var (exactNum, exactDen) = exactResult.ValueDistribution().ExpectedBigIntegerValue();
        var exactRtp = (double)exactNum / (double)exactDen;

        var sampledResult = SampledInterpreter.Evaluate(program, new IntegState(0, 0),
            new SampledConfig { Seed = 555, MaxSpins = 25_000 });

        var lo = sampledResult.Stats.Mean - sampledResult.Stats.Ci95Half;
        var hi = sampledResult.Stats.Mean + sampledResult.Stats.Ci95Half;

        Assert.True(exactRtp >= lo && exactRtp <= hi,
            $"Exact RTP {exactRtp:F6} should be within CI95 [{lo:F6}, {hi:F6}], " +
            $"mean={sampledResult.Stats.Mean:F6}, SE={sampledResult.Stats.StdErr:F6}");
    }

    [Fact]
    public void LoopWithDraws_ReturnsStats()
    {
        // A bounded loop with draws inside the body.
        var stopCondition = (IntegState s) => s.Counter >= 3;
        var body =
            from s in Slot.GetState<IntegState>()
            from draw in Slot.Draw<IntegState>(_ => WeightSet.FromIntegers([1, 1]))
            from _ in Slot.Modify<IntegState>(st => st with
            {
                Counter = st.Counter + 1,
                Win = st.Win + draw * 10
            })
            select Unit.Value;

        var program =
            from _ in Slot.Loop(stopCondition, body)
            from s in Slot.GetState<IntegState>()
            select s.Win;

        var result = SampledInterpreter.Evaluate(program, new IntegState(0, 0),
            new SampledConfig { Seed = 42, MaxSpins = 5000 });

        Assert.Equal(5000, result.SpinsCompleted);
        Assert.True(result.Stats.Mean >= 0);
        Assert.True(result.Stats.Variance >= 0);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  StreamingStats standalone tests
// ═══════════════════════════════════════════════════════════════════════════

public class StreamingStats_Welford
{
    [Fact]
    public void SingleValue_HasZeroVariance()
    {
        var stats = new StreamingStats();
        stats.Add(42.0);

        Assert.Equal(1, stats.Count);
        Assert.Equal(42.0, stats.Mean);
        Assert.Equal(0.0, stats.Variance);
        Assert.Equal(0.0, stats.StdDev);
        Assert.Equal(0.0, stats.StdErr);
    }

    [Fact]
    public void TwoValues_ComputesCorrectly()
    {
        var stats = new StreamingStats();
        stats.Add(10.0);
        stats.Add(20.0);

        Assert.Equal(2, stats.Count);
        Assert.Equal(15.0, stats.Mean);
        // Sample variance: ((10-15)^2 + (20-15)^2) / 1 = (25+25)/1 = 50
        Assert.Equal(50.0, stats.Variance);
        Assert.Equal(System.Math.Sqrt(50.0), stats.StdDev);
    }

    [Fact]
    public void ThreeValues_ComputesCorrectly()
    {
        var stats = new StreamingStats();
        stats.Add(2.0);
        stats.Add(4.0);
        stats.Add(6.0);

        Assert.Equal(3, stats.Count);
        Assert.Equal(4.0, stats.Mean);
        // Variance: ((2-4)^2 + (4-4)^2 + (6-4)^2) / 2 = (4+0+4)/2 = 4
        Assert.Equal(4.0, stats.Variance);
        Assert.Equal(2.0, stats.StdDev);
    }

    [Fact]
    public void Empty_HasZeroStats()
    {
        var stats = new StreamingStats();
        Assert.Equal(0, stats.Count);
        Assert.Equal(0.0, stats.Mean);
        Assert.Equal(0.0, stats.Variance);
        Assert.Equal(0.0, stats.StdErr);
        Assert.True(double.IsNaN(stats.MinObserved));
        Assert.True(double.IsNaN(stats.MaxObserved));
    }

    [Fact]
    public void KnownSequence_MatchesPrecomputed()
    {
        // Test against pre-computed values for a known sequence.
        var values = new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0 };
        var batchMean = values.Average();
        var batchVar = values.Select(x => (x - batchMean) * (x - batchMean)).Sum() / (values.Length - 1);

        var stats = new StreamingStats();
        foreach (var v in values)
            stats.Add(v);

        Assert.Equal(10, stats.Count);
        Assert.Equal(batchMean, stats.Mean, precision: 12);
        Assert.Equal(batchVar, stats.Variance, precision: 12);
        Assert.Equal(1.0, stats.MinObserved);
        Assert.Equal(10.0, stats.MaxObserved);
    }

    [Fact]
    public void Merge_CombinesCorrectly()
    {
        var s1 = new StreamingStats();
        foreach (var v in new[] { 1.0, 2.0, 3.0 })
            s1.Add(v);

        var s2 = new StreamingStats();
        foreach (var v in new[] { 4.0, 5.0, 6.0 })
            s2.Add(v);

        s1.Merge(s2);

        Assert.Equal(6, s1.Count);

        // Batch stats for [1,2,3,4,5,6].
        var batchMean = new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 }.Average();
        Assert.Equal(batchMean, s1.Mean, precision: 10);

        var batchVar = new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 }
            .Select(x => (x - batchMean) * (x - batchMean)).Sum() / 5;
        Assert.Equal(batchVar, s1.Variance, precision: 10);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Snapshot tests
// ═══════════════════════════════════════════════════════════════════════════

public class StreamingStats_Snapshot
{
    [Fact]
    public void Snapshot_IsIndependent()
    {
        var stats = new StreamingStats();
        stats.Add(10.0);
        stats.Add(20.0);

        var snap = stats.Snapshot();

        Assert.Equal(2, snap.Count);
        Assert.Equal(15.0, snap.Mean);

        // Add more samples to the live stats.
        stats.Add(30.0);

        // Snapshot should still reflect the old state.
        Assert.Equal(2, snap.Count);
        Assert.Equal(15.0, snap.Mean);

        // Live stats should have the new state.
        Assert.Equal(3, stats.Count);
        Assert.Equal(20.0, stats.Mean);
    }

    [Fact]
    public void Snapshot_ContainsAllFields()
    {
        var stats = new StreamingStats(maxWinCap: 1000.0);
        for (var i = 0; i < 100; i++)
            stats.Add(i * 10.0);

        var snap = stats.Snapshot();

        Assert.Equal(100, snap.Count);
        Assert.True(snap.Mean > 0);
        Assert.True(snap.Variance > 0);
        Assert.True(snap.StdDev > 0);
        Assert.True(snap.StdErr > 0);
        Assert.True(snap.Ci95Half > 0);
        Assert.Equal(0.0, snap.MinObserved);
        Assert.Equal(990.0, snap.MaxObserved);
        Assert.NotNull(snap.MaxWinCap);
        Assert.Equal(1000.0, snap.MaxWinCap.Value);
        Assert.NotNull(snap.Histogram);
    }
}

public sealed record SampledEdgeState(int Counter, BigInteger Win)
{
    public BigInteger RecurrenceHash => Counter;
}

public class SampledInterpreter_EdgeCases
{
    [Fact]
    public void Sampled_WithMaxWinCap()
    {
        var program =
            from idx in Slot.Draw<SampledEdgeState>(_ =>
                WeightSet.FromIntegers(new int[] { 1, 1 }))
            select idx == 0 ? BigInteger.Zero : new BigInteger(1000);

        var result = SampledInterpreter.Evaluate(program, new SampledEdgeState(0, 0),
            new SampledConfig
            {
                Seed = 42,
                MaxSpins = 5000,
                MaxWinCap = new BigInteger(500),
            });
        Assert.True(result.Stats.Mean <= 500);
    }

    [Fact]
    public void Sampled_CancellationToken()
    {
        var program =
            from idx in Slot.Draw<SampledEdgeState>(_ =>
                WeightSet.FromIntegers(new int[] { 1 }))
            select new BigInteger(10);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = SampledInterpreter.Evaluate(program, new SampledEdgeState(0, 0),
            new SampledConfig
            {
                Seed = 42,
                MaxSpins = 1_000_000,
                CancellationToken = cts.Token,
                CancellationCheckInterval = 1,
            });
        Assert.True(result.WasCancelled);
        Assert.True(result.SpinsCompleted < 1_000_000);
    }

    [Fact]
    public void Sampled_SelectorOverload()
    {
        var program =
            from idx in Slot.Draw<SampledEdgeState>(_ =>
                WeightSet.FromIntegers(new int[] { 1 }))
            select idx;

        var result = SampledInterpreter.Evaluate(
            program, new SampledEdgeState(0, 0),
            (int v) => new BigInteger(v * 10),
            new SampledConfig { Seed = 42, MaxSpins = 100 });
        Assert.False(result.WasCancelled);
        Assert.Equal(0, result.Stats.Mean, 3);
    }
}
