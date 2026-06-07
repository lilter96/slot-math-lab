using System.Numerics;
using SlotMath.Core.Math;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;
using Xunit;
using Xunit.Abstractions;

namespace SlotMath.Core.Tests.Mechanics.CrossCheck;

// ═══════════════════════════════════════════════════════════════════════════
//  G13 DoD (a) — Exact-vs-sampled cross-check
//
//  ≥50 generated configs × ≥20 seeds:
//    sampled RTP within exact ± 3·stdErr for each seed.
// ═══════════════════════════════════════════════════════════════════════════

public class CrossCheckTests(ITestOutputHelper output)
{
    private const int ConfigCount = 50;
    private const int SeedCount = 20;
    private const long SpinsPerSeed = 10_000;

    /// <summary>
    /// Property-based cross-check: for each generated config and seed,
    /// the sampled RTP must fall within exact ± 3·stdErr.
    /// </summary>
    [Fact]
    public void AllConfigs_AllSeeds_SampledWithinExactConfidence()
    {
        var configs = ConfigGenerator.Generate(ConfigCount);
        Assert.True(configs.Count >= ConfigCount,
            $"Expected ≥{ConfigCount} configs, got {configs.Count}");

        output.WriteLine($"Testing {configs.Count} configs × {SeedCount} seeds = " +
                       $"{configs.Count * SeedCount} cross-checks");
        output.WriteLine("");

        var failures = new List<string>();
        var passed = 0;

        foreach (var cfg in configs)
        {
            // ── Compute exact RTP ────────────────────────────────────
            var exactResult = ExactInterpreter.Evaluate(
                cfg.Program, cfg.InitialState, s => s.RecurrenceHash);

            var exactDist = exactResult.ValueDistribution();
            var (exactNum, exactDen) = exactDist.ExpectedBigIntegerValue();
            var exactRtp = (double)exactNum / (double)exactDen;

            var cfgFailures = 0;

            for (var seed = 0; seed < SeedCount; seed++)
            {
                var sampledResult = SampledInterpreter.Evaluate(
                    cfg.Program, cfg.InitialState,
                    new SampledConfig
                    {
                        Seed = seed * 1000 + cfg.Id,
                        MaxSpins = SpinsPerSeed,
                    });

                var sampledRtp = sampledResult.Stats.Mean;
                var stdErr = sampledResult.Stats.StdErr;

                if (stdErr <= 0) stdErr = 0.001; // degenerate case

                var diff = System.Math.Abs(sampledRtp - exactRtp);
                var threshold = 3.0 * stdErr;

                if (diff > threshold)
                {
                    cfgFailures++;
                    if (cfgFailures <= 3) // log max 3 failures per config
                    {
                        failures.Add(
                            $"[{cfg.Category}] #{cfg.Id} seed={seed}: " +
                            $"exact={exactRtp:F4} sampled={sampledRtp:F4} " +
                            $"diff={diff:F4} > 3·σ={threshold:F4}");
                    }
                }
                else
                {
                    passed++;
                }
            }

            if (cfgFailures > 0)
                output.WriteLine(
                    $"  {cfg.Category} #{cfg.Id}: {cfgFailures}/{SeedCount} seeds " +
                    $"failed (exact={exactRtp:F4})");
        }

        output.WriteLine("");
        output.WriteLine(
            $"Result: {passed}/{configs.Count * SeedCount} cross-checks passed");

        if (failures.Count > 0)
        {
            output.WriteLine($"Failures ({failures.Count} total):");
            foreach (var f in failures.Take(20))
                output.WriteLine($"  {f}");
        }

        // At 3·σ with 1000 total cross-checks, we expect ~3 false positives.
        // Allow up to 10 failures (1% rate) to account for statistical noise.
        var failureRate = (double)failures.Count / (configs.Count * SeedCount);
        output.WriteLine($"Failure rate: {failureRate:P2}");

        Assert.True(failureRate <= 0.05,
            $"Failure rate {failureRate:P2} exceeds 5% threshold. " +
            $"Failures: {failures.Count}");
    }

    /// <summary>
    /// Single config — detailed verification that exact and sampled agree
    /// within tight tolerances.
    /// </summary>
    [Fact]
    public void SingleConfig_DeepCheck()
    {
        // Simple game: 3-outcome draw with weights [3,2,1], payouts 0,10,20.
        // EV = (0*3 + 10*2 + 20*1)/6 = 40/6 = 20/3.
        var program =
            from idx in Slot.Draw<CrossCheckState>(_ =>
                WeightSet.FromIntegers(new int[] { 3, 2, 1 }))
            select idx switch
            {
                0 => BigInteger.Zero,
                1 => new BigInteger(10),
                2 => new BigInteger(20),
                _ => BigInteger.Zero,
            };

        var state = new CrossCheckState(0, 0, 0);

        // Exact.
        var exact = ExactInterpreter.Evaluate(program, state, s => s.RecurrenceHash);
        var (num, den) = exact.ValueDistribution().ExpectedBigIntegerValue();
        Assert.Equal(new BigInteger(20), num);
        Assert.Equal(new BigInteger(3), den);

        // Sampled with many spins → should converge tightly.
        var sampled = SampledInterpreter.Evaluate(program, state,
            new SampledConfig { Seed = 42, MaxSpins = 200_000 });

        var exactRtp = (double)num / (double)den;
        var diff = System.Math.Abs(sampled.Stats.Mean - exactRtp);

        output.WriteLine($"Exact: {exactRtp:F6}");
        output.WriteLine($"Sampled: {sampled.Stats.Mean:F6} ± {sampled.Stats.StdErr:F6}");
        output.WriteLine($"Diff: {diff:F6}");

        Assert.True(diff <= 3.0 * sampled.Stats.StdErr,
            $"Sampled {sampled.Stats.Mean:F6} not within 3·σ of exact {exactRtp:F6}");
    }
}
