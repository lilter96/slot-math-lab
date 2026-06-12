using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests;

// ═══════════════════════════════════════════════════════════════════════════
//  EngineFixRegressionTests — pins the fixes for the critical engine issues
//
//  1. Exact probabilities for rational weights: p(i) = w(i) / Σw — a shared
//     weight denominator must cancel (it used to inflate the denominator,
//     producing an improper distribution).
//  2. Memoisation soundness: cache keys include the pending continuation, so
//     a shared Draw node reached in different bind contexts never shares a
//     cached result.
//  3. Loop memoisation: convergent loop states collapse the iteration tree
//     into a DAG (exponential → polynomial).
//  4. Stack safety: deeply sequential draws evaluate without recursion.
//  5. Compiler purity: PutState/ModifyState are copy-on-write — exact-path
//     branches and sampled spins never observe each other's writes.
//  6. StateHasher: canonical content hash, insertion-order independent.
//  7. ProgramAnalyzer: bounded expansion, saturating arithmetic.
//  8. StreamingStats: the no-cap histogram actually counts samples.
//  9. WeightSet: cached alias table, uniform fast path, weight validation.
// 10. Fan-out: parallel downstream paths are summed, not silently dropped.
// ═══════════════════════════════════════════════════════════════════════════

public class ExactInterpreter_RationalWeightFix
{
    private sealed record CounterState(int N)
    {
        public BigInteger Hash() => N;
    }

    [Fact]
    public void RationalWeights_ProduceProperDistribution()
    {
        // Weights 1/3 and 2/3 share denominator 3.  Probabilities must be
        // 1/3 and 2/3 — the shared denominator cancels.
        var ws = WeightSet.FromRationalStrings(new[] { "1/3", "2/3" });
        var program = Slot.Draw<CounterState, BigInteger>(_ => ws, i => new BigInteger(i * 10));

        var result = ExactInterpreter.Evaluate(program, new CounterState(0), s => s.Hash());
        var dist = result.ValueDistribution();

        // Proper distribution: total mass is exactly 1.
        Assert.Equal(dist.Denominator, dist.TotalNumerator);

        // P(0) = 1/3, P(10) = 2/3 exactly.
        foreach (var entry in dist.Entries)
        {
            var (num, den) = Rational.Reduce(entry.Numerator, dist.Denominator);
            if (entry.Value == BigInteger.Zero)
            {
                Assert.Equal(BigInteger.One, num);
                Assert.Equal(new BigInteger(3), den);
            }
            else
            {
                Assert.Equal(new BigInteger(10), entry.Value);
                Assert.Equal(new BigInteger(2), num);
                Assert.Equal(new BigInteger(3), den);
            }
        }

        // EV = 0·(1/3) + 10·(2/3) = 20/3 exactly.
        var (evNum, evDen) = dist.ExpectedBigIntegerValue();
        Assert.Equal(new BigInteger(20), evNum);
        Assert.Equal(new BigInteger(3), evDen);
    }
}

public class ExactInterpreter_MemoSoundness
{
    private sealed record CounterState(int N)
    {
        public BigInteger Hash() => N;
    }

    [Fact]
    public void SharedDrawNode_UnderDifferentContinuations_DoesNotShareCachedResults()
    {
        // One Draw node instance used in two different bind contexts.  The
        // same (node, state) pair is reached twice with different pending
        // continuations — the results must differ.
        var sharedDraw = Slot.Draw<CounterState>(_ => WeightSet.FromIntegers(new[] { 1, 1 }));

        var program =
            from a in sharedDraw.SelectMany(i => Slot.Pure<CounterState, BigInteger>(i * 10))
            from b in sharedDraw.SelectMany(i => Slot.Pure<CounterState, BigInteger>(i * 100))
            select a + b;

        var result = ExactInterpreter.Evaluate(program, new CounterState(0), s => s.Hash());
        var (evNum, evDen) = result.ValueDistribution().ExpectedBigIntegerValue();

        // E = E[i]·10 + E[i]·100 = 5 + 50 = 55.
        Assert.Equal(new BigInteger(55), evNum);
        Assert.Equal(BigInteger.One, evDen);
    }
}

public class ExactInterpreter_LoopMemoisation
{
    private sealed record FreeSpinState(int Left, int Awarded, BigInteger Total)
    {
        public BigInteger Hash() => (new BigInteger(Awarded) * 1024 + Left) * 1_000_003 + Total;
    }

    private static Slot<FreeSpinState, BigInteger> BuildRetriggerGame(int maxAwarded)
    {
        // Each spin: 70% win 0, 20% win 5, 10% retrigger +2 spins (capped).
        var weights = WeightSet.FromIntegers(new[] { 7, 2, 1 });
        var body =
            from outcome in Slot.Draw<FreeSpinState>(_ => weights)
            from _ in Slot.Modify<FreeSpinState>(s =>
            {
                if (outcome == 0) return s with { Left = s.Left - 1 };
                if (outcome == 1) return s with { Left = s.Left - 1, Total = s.Total + 5 };
                var extra = System.Math.Min(2, maxAwarded - s.Awarded);
                return s with { Left = s.Left - 1 + extra, Awarded = s.Awarded + extra };
            })
            select Unit.Value;

        return Slot.Loop<FreeSpinState>(s => s.Left <= 0, body)
            .SelectMany(_ => Slot.GetState<FreeSpinState, BigInteger>(s => s.Total));
    }

    [Fact]
    public void RetriggerLoop_MemoisedResultEqualsNaiveEnumeration()
    {
        var initial = new FreeSpinState(3, 3, BigInteger.Zero);

        // Memoised: canonical state hash lets convergent states share results.
        var memoised = ExactInterpreter.Evaluate(
            BuildRetriggerGame(maxAwarded: 9), initial, s => s.Hash());

        // Naive: a unique hash per visit disables all sharing, forcing full
        // tree enumeration — the ground truth.
        var counter = BigInteger.Zero;
        var naive = ExactInterpreter.Evaluate(
            BuildRetriggerGame(maxAwarded: 9), initial, _ => counter++);

        var (memNum, memDen) = memoised.ValueDistribution().ExpectedBigIntegerValue();
        var (naiveNum, naiveDen) = naive.ValueDistribution().ExpectedBigIntegerValue();

        Assert.Equal(naiveNum, memNum);
        Assert.Equal(naiveDen, memDen);

        // The collapse must be real: strictly fewer branches and actual hits.
        Assert.True(memoised.Stats.CacheHits > 0,
            "expected cache hits across loop iterations");
        Assert.True(memoised.Stats.TotalBranchesEvaluated < naive.Stats.TotalBranchesEvaluated / 2,
            $"memoised {memoised.Stats.TotalBranchesEvaluated} vs naive {naive.Stats.TotalBranchesEvaluated}");
    }

    [Fact]
    public void DeepSequentialDraws_EvaluateWithoutStackOverflow()
    {
        // 20,000 sequential single-outcome draws (one per loop iteration).
        // Every draw nests a pending frame — the frame stack lives on the
        // heap, so no call-stack depth limit applies.
        var single = WeightSet.FromIntegers(new[] { 1 });
        var body =
            from _ in Slot.Draw<FreeSpinState>(_ => single)
            from __ in Slot.Modify<FreeSpinState>(s =>
                s with { Left = s.Left - 1, Total = s.Total + 1 })
            select Unit.Value;

        var program = Slot.Loop<FreeSpinState>(s => s.Left <= 0, body)
            .SelectMany(_ => Slot.GetState<FreeSpinState, BigInteger>(s => s.Total));

        var result = ExactInterpreter.Evaluate(
            program, new FreeSpinState(20_000, 0, BigInteger.Zero), s => s.Hash());

        var dist = result.ValueDistribution();
        Assert.Single(dist.Entries);
        Assert.Equal(new BigInteger(20_000), dist.Entries[0].Value);
    }
}

public class Compiler_StatePurity
{
    private static GraphConfig BuildPutStateGraph() => new()
    {
        SchemaVersion = "1.0.0",
        Id = "test-putstate-purity",
        Nodes = new Node[]
        {
            new DrawNode
            {
                Id = "draw",
                Label = "Draw",
                DrawWeights = new[]
                {
                    new DrawWeight { OutcomeId = "hi", Weight = 1, Value = 100 },
                    new DrawWeight { OutcomeId = "lo", Weight = 1, Value = 10 },
                },
                Outputs = new Dictionary<string, Port>
                {
                    ["value"] = new() { Name = "value", Type = PortType.Number }
                }
            },
            new PutStateNode
            {
                Id = "put",
                Label = "Store",
                StateKey = "lastWin",
                Inputs = new Dictionary<string, Port>
                {
                    ["in"] = new() { Name = "in", Type = PortType.Number }
                },
                Outputs = new Dictionary<string, Port>
                {
                    ["out"] = new() { Name = "out", Type = PortType.Number }
                }
            },
            new MetricsSinkNode
            {
                Id = "sink",
                Label = "Sink",
                Inputs = new Dictionary<string, Port>
                {
                    ["in"] = new() { Name = "in", Type = PortType.Number }
                }
            },
        },
        Edges = new[]
        {
            new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "value", TargetNodeId = "put", TargetPort = "in" },
            new Edge { Id = "e2", SourceNodeId = "put", SourcePort = "out", TargetNodeId = "sink", TargetPort = "in" },
        },
    };

    [Fact]
    public void ExactPath_PutStateBranches_AreIsolated()
    {
        var compiled = new GraphCompiler().Compile(BuildPutStateGraph());
        Assert.True(compiled.IsValid, string.Join("; ", compiled.Errors.Select(e => e.Message)));

        var initial = new Dictionary<string, object?>();
        var result = ExactInterpreter.Evaluate(
            compiled.Program!, initial, StateHasher.CanonicalHash);

        // The initial state must never be mutated in place.
        Assert.Empty(initial);

        // Each branch's final state holds its own value — no cross-branch bleed.
        Assert.Equal(2, result.Distribution.Count);
        var finals = result.Distribution.Entries
            .Select(e => (Value: e.Value.Value, State: e.Value.FinalState))
            .ToList();
        foreach (var (value, state) in finals)
            Assert.Equal(value, Assert.IsType<BigInteger>(state["lastWin"]));
        Assert.Contains(finals, f => f.Value == new BigInteger(100));
        Assert.Contains(finals, f => f.Value == new BigInteger(10));
    }

    [Fact]
    public void SampledPath_SpinsShareNoState()
    {
        var compiled = new GraphCompiler().Compile(BuildPutStateGraph());
        Assert.True(compiled.IsValid);

        var initial = new Dictionary<string, object?>();
        var result = SampledInterpreter.Evaluate(
            compiled.Program!, initial,
            new SampledConfig { Seed = 11, MaxSpins = 50 });

        Assert.Equal(50, result.SpinsCompleted);
        // Spins must not leak writes into the shared initial state.
        Assert.Empty(initial);
    }
}

public class Compiler_FanOut
{
    private static GraphConfig BuildFanOutGraph() => new()
    {
        SchemaVersion = "1.0.0",
        Id = "test-fanout-sum",
        Nodes = new Node[]
        {
            new DrawNode
            {
                Id = "draw",
                Label = "Draw",
                DrawWeights = new[]
                {
                    new DrawWeight { OutcomeId = "only", Weight = 1, Value = 5 },
                },
                Outputs = new Dictionary<string, Port>
                {
                    ["value"] = new() { Name = "value", Type = PortType.Number }
                }
            },
            new GetStateNode
            {
                Id = "pathA",
                Label = "Path A",
                StateKey = "unused-a",
                Inputs = new Dictionary<string, Port>
                {
                    ["in"] = new() { Name = "in", Type = PortType.Number }
                },
                Outputs = new Dictionary<string, Port>
                {
                    ["out"] = new() { Name = "out", Type = PortType.Number }
                }
            },
            new GetStateNode
            {
                Id = "pathB",
                Label = "Path B",
                StateKey = "unused-b",
                Inputs = new Dictionary<string, Port>
                {
                    ["in"] = new() { Name = "in", Type = PortType.Number }
                },
                Outputs = new Dictionary<string, Port>
                {
                    ["out"] = new() { Name = "out", Type = PortType.Number }
                }
            },
            new MetricsSinkNode
            {
                Id = "sink",
                Label = "Sink",
                Inputs = new Dictionary<string, Port>
                {
                    ["in"] = new() { Name = "in", Type = PortType.Number }
                }
            },
        },
        Edges = new[]
        {
            new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "value", TargetNodeId = "pathA", TargetPort = "in" },
            new Edge { Id = "e2", SourceNodeId = "draw", SourcePort = "value", TargetNodeId = "pathB", TargetPort = "in" },
            new Edge { Id = "e3", SourceNodeId = "pathA", SourcePort = "out", TargetNodeId = "sink", TargetPort = "in" },
            new Edge { Id = "e4", SourceNodeId = "pathB", SourcePort = "out", TargetNodeId = "sink", TargetPort = "in" },
        },
    };

    [Fact]
    public void FanOutPaths_AreSummed_NotDropped()
    {
        var compiled = new GraphCompiler().Compile(BuildFanOutGraph());
        Assert.True(compiled.IsValid, string.Join("; ", compiled.Errors.Select(e => e.Message)));

        var result = ExactInterpreter.Evaluate(
            compiled.Program!, new Dictionary<string, object?>(), StateHasher.CanonicalHash);

        // Both paths carry the drawn value 5 to the sink: total 10.
        // (The old compiler silently dropped every edge after the first.)
        var (evNum, evDen) = result.ValueDistribution().ExpectedBigIntegerValue();
        Assert.Equal(new BigInteger(10), evNum);
        Assert.Equal(BigInteger.One, evDen);
    }
}

public class StateHasherTests
{
    [Fact]
    public void EqualContent_DifferentInsertionOrder_SameHash()
    {
        var a = new Dictionary<string, object?>
        {
            ["x"] = new BigInteger(42),
            ["y"] = "red",
            ["z"] = true,
        };
        var b = new Dictionary<string, object?>
        {
            ["z"] = true,
            ["x"] = new BigInteger(42),
            ["y"] = "red",
        };

        Assert.Equal(StateHasher.CanonicalHash(a), StateHasher.CanonicalHash(b));
    }

    [Fact]
    public void DifferentContent_DifferentHash()
    {
        var a = new Dictionary<string, object?> { ["x"] = new BigInteger(1) };
        var b = new Dictionary<string, object?> { ["x"] = new BigInteger(2) };
        var c = new Dictionary<string, object?> { ["y"] = new BigInteger(1) };
        var d = new Dictionary<string, object?> { ["x"] = 1 };  // int vs BigInteger may differ
        var empty = new Dictionary<string, object?>();

        var hashes = new[] { a, b, c, empty }
            .Select(StateHasher.CanonicalHash)
            .ToList();
        Assert.Equal(hashes.Count, hashes.Distinct().Count());

        // Null values are representable and distinct from missing keys.
        var withNull = new Dictionary<string, object?> { ["x"] = null };
        Assert.NotEqual(StateHasher.CanonicalHash(empty), StateHasher.CanonicalHash(withNull));
        _ = d; // type-tagged, no assertion needed on exact value
    }

    [Fact]
    public void SameReferenceMutated_HashChanges()
    {
        // The exact failure mode of reference-identity hashing: a mutated
        // dictionary kept its old hash.  Content hashing must track content.
        var state = new Dictionary<string, object?> { ["spins"] = 5 };
        var before = StateHasher.CanonicalHash(state);
        state["spins"] = 4;
        var after = StateHasher.CanonicalHash(state);
        Assert.NotEqual(before, after);
    }
}

public class ProgramAnalyzer_Bounds
{
    private sealed record S0(int N);

    [Fact]
    public void HugeUniformDraw_AnalyzesQuicklyWithFullEstimate()
    {
        var program = Slot.Draw<S0, BigInteger>(
            _ => WeightSet.Uniform(1_000_000), i => new BigInteger(i));

        var analysis = ProgramAnalyzer.Analyze(program);

        Assert.Equal(1_000_000, analysis.EstimatedBranches);
        Assert.Equal(1, analysis.DrawCount);
    }

    [Fact]
    public void NestedHugeDraws_SaturateInsteadOfOverflowing()
    {
        var huge = WeightSet.Uniform(1_000_000);
        var program =
            from a in Slot.Draw<S0>(_ => huge)
            from b in Slot.Draw<S0>(_ => huge)
            from c in Slot.Draw<S0>(_ => huge)
            from d in Slot.Draw<S0>(_ => huge)
            select new BigInteger(a + b + c + d);

        var analysis = ProgramAnalyzer.Analyze(program);

        // 10^6 + 10^12 + 10^18 + (saturated) — must never go negative.
        Assert.True(analysis.EstimatedBranches > 0);
    }
}

public class StreamingStats_DynamicHistogram
{
    [Fact]
    public void NoCap_HistogramCountsAllSamples()
    {
        var stats = new StreamingStats(histogramBins: 10);
        for (var i = 0; i < 1000; i++)
            stats.Add((double)(i % 100));

        var bins = stats.BuildHistogram();
        Assert.Equal(1000, bins.Sum(b => b.Count));
        Assert.Equal(0.0, bins[0].LowerBound, 10);
        Assert.Equal(99.0, bins[^1].UpperBound, 5);
    }

    [Fact]
    public void NoCap_BeyondBufferSize_KeepsCounting()
    {
        var stats = new StreamingStats(histogramBins: 8);
        var n = StreamingStats.DynamicHistogramBufferSize + 5_000;
        var rng = new SeededRandom(3);
        for (var i = 0; i < n; i++)
            stats.Add((double)rng.Next(1000));

        var bins = stats.BuildHistogram();
        Assert.Equal(n, bins.Sum(b => b.Count));
    }
}

public class WeightSet_UniformAndCaching
{
    [Fact]
    public void Uniform_SamplesIdenticallyToExplicitOnes()
    {
        var uniform = WeightSet.Uniform(5);
        var explicitOnes = WeightSet.FromIntegers(new[] { 1, 1, 1, 1, 1 });

        var rngA = new SeededRandom(99);
        var rngB = new SeededRandom(99);
        for (var i = 0; i < 2_000; i++)
        {
            Assert.Equal(
                explicitOnes.AliasTable.Sample(rngA),
                uniform.AliasTable.Sample(rngB));
        }
    }

    [Fact]
    public void Uniform_ExposesOnesAndCorrectSum()
    {
        var uniform = WeightSet.Uniform(4);
        Assert.Equal(4, uniform.Count);
        Assert.Equal(new BigInteger(4), uniform.NumeratorSum);
        Assert.All(uniform.Numerators, n => Assert.Equal(BigInteger.One, n));
        Assert.Equal(WeightSet.FromIntegers(new[] { 1, 1, 1, 1 }), uniform);
    }

    [Fact]
    public void AliasTable_IsBuiltOnceAndCached()
    {
        var ws = WeightSet.FromIntegers(new[] { 3, 1, 4 });
        Assert.Same(ws.AliasTable, ws.AliasTable);
    }

    [Fact]
    public void NegativeWeights_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => WeightSet.FromIntegers(new[] { 1, -2, 3 }));
        Assert.Throws<ArgumentException>(
            () => WeightSet.FromRationalStrings(new[] { "1/2", "-1/2" }));
    }
}

public class SampledInterpreter_Parallel
{
    private sealed record PState(int N);

    private static Slot<PState, BigInteger> BuildGame()
    {
        var weights = WeightSet.FromIntegers(new[] { 3, 2, 1 });
        return
            from a in Slot.Draw<PState>(_ => weights)
            from b in Slot.Draw<PState>(_ => weights)
            select new BigInteger(a * 10 + b);
    }

    [Fact]
    public void ParallelResults_AreIdenticalAcrossThreadCounts()
    {
        var program = BuildGame();

        SampledResult<PState> Run(int dop) => SampledInterpreter.Evaluate(
            program, new PState(0),
            new SampledConfig { Seed = 1234, MaxSpins = 40_000, DegreeOfParallelism = dop });

        var two = Run(2);
        var eight = Run(8);

        // Fixed logical stream count ⇒ stats are a pure function of
        // (seed, spins), not of how many workers happened to run them.
        Assert.Equal(two.SpinsCompleted, eight.SpinsCompleted);
        Assert.Equal(two.Stats.Mean, eight.Stats.Mean);
        Assert.Equal(two.Stats.Variance, eight.Stats.Variance);
        Assert.Equal(two.Stats.NonZeroCount, eight.Stats.NonZeroCount);
        Assert.Equal(two.Stats.MinObserved, eight.Stats.MinObserved);
        Assert.Equal(two.Stats.MaxObserved, eight.Stats.MaxObserved);
    }

    [Fact]
    public void ParallelMean_AgreesWithSequentialWithinError()
    {
        var program = BuildGame();

        var sequential = SampledInterpreter.Evaluate(
            program, new PState(0),
            new SampledConfig { Seed = 77, MaxSpins = 60_000 });
        var parallel = SampledInterpreter.Evaluate(
            program, new PState(0),
            new SampledConfig { Seed = 77, MaxSpins = 60_000, DegreeOfParallelism = 4 });

        // Different stream layout ⇒ different sample sequence, but the same
        // distribution: means agree within combined standard error.
        var tolerance = 4 * (sequential.Stats.StdErr + parallel.Stats.StdErr);
        Assert.InRange(parallel.Stats.Mean,
            sequential.Stats.Mean - tolerance, sequential.Stats.Mean + tolerance);
        Assert.Equal(60_000, parallel.SpinsCompleted);
    }

    [Fact]
    public void ParallelHistogram_WithCap_CountsAllSamples()
    {
        var program = BuildGame();
        var result = SampledInterpreter.Evaluate(
            program, new PState(0),
            new SampledConfig
            {
                Seed = 5,
                MaxSpins = 20_000,
                DegreeOfParallelism = 4,
                MaxWinCap = new BigInteger(25),
            });

        Assert.Equal(20_000, result.Stats.Histogram.Sum(b => b.Count));
    }
}

public class ExactInterpreter_AccumulatorConvolution
{
    // The Dog-House pattern from the bug report: a base spin banks a win
    // BEFORE a free-spins loop runs.  Two paths then reach the same
    // recurrence state (spins left) with different banked totals.  Per
    // invariant #4 the accumulator is excluded from the recurrence hash and
    // declared via AccumulatorSpec — cached distributions are deltas,
    // shifted by the caller's banked total on reuse (convolution).
    private sealed record DhState(int Left, BigInteger Total);

    private static readonly AccumulatorSpec<DhState, BigInteger> Spec = new()
    {
        Get = s => s.Total,
        Set = (s, v) => s with { Total = v },
        ShiftValue = (v, d) => v + d,
    };

    private static Slot<DhState, BigInteger> BuildBaseThenLoopGame(
        int[] baseWeights, int[] loopWeights)
    {
        var baseWs = WeightSet.FromIntegers(baseWeights);
        var loopWs = WeightSet.FromIntegers(loopWeights);

        var baseSpin =
            from i in Slot.Draw<DhState>(_ => baseWs)
            from _ in Slot.Modify<DhState>(s => s with { Total = s.Total + i * 10 })
            select Unit.Value;

        var loopBody =
            from i in Slot.Draw<DhState>(_ => loopWs)
            from _ in Slot.Modify<DhState>(s =>
                s with { Left = s.Left - 1, Total = s.Total + i * 10 })
            select Unit.Value;

        return baseSpin
            .SelectMany(_ => Slot.Loop<DhState>(s => s.Left <= 0, loopBody))
            .SelectMany(_ => Slot.GetState<DhState, BigInteger>(s => s.Total));
    }

    private static void AssertSameValueDistribution(
        ExactResult<DhState, BigInteger> actual,
        ExactResult<DhState, BigInteger> expected)
    {
        var actualDist = actual.ValueDistribution();
        var expectedDist = expected.ValueDistribution();

        var actualMap = actualDist.Entries.ToDictionary(
            e => e.Value,
            e => Rational.Reduce(e.Numerator, actualDist.Denominator));
        var expectedMap = expectedDist.Entries.ToDictionary(
            e => e.Value,
            e => Rational.Reduce(e.Numerator, expectedDist.Denominator));

        Assert.Equal(expectedMap.Count, actualMap.Count);
        foreach (var (value, prob) in expectedMap)
        {
            Assert.True(actualMap.TryGetValue(value, out var actualProb),
                $"missing outcome {value}");
            Assert.Equal(prob, actualProb);
        }
    }

    [Fact]
    public void BaseSpinThenLoop_AccumulatorExcludedFromHash_MatchesNaive()
    {
        // Recurrence hash EXCLUDES Total — without the spec this pattern
        // returned half the EV (the first path's absolute totals were reused
        // for paths that had banked a different base win).
        var withSpec = ExactInterpreter.Evaluate(
            BuildBaseThenLoopGame(new[] { 1, 1 }, new[] { 1, 1 }),
            new DhState(2, BigInteger.Zero),
            s => s.Left,
            Spec);

        // Ground truth: unique hash per visit forces full tree enumeration.
        var counter = BigInteger.Zero;
        var naive = ExactInterpreter.Evaluate(
            BuildBaseThenLoopGame(new[] { 1, 1 }, new[] { 1, 1 }),
            new DhState(2, BigInteger.Zero),
            _ => counter++);

        AssertSameValueDistribution(withSpec, naive);

        // EV = base 5 + 2 iterations × 5 = 15 exactly.
        var (num, den) = withSpec.ValueDistribution().ExpectedBigIntegerValue();
        Assert.Equal(new BigInteger(15), num);
        Assert.Equal(BigInteger.One, den);

        // The sharing must be real: states differing only in banked total
        // hit the delta cache.
        Assert.True(withSpec.Stats.CacheHits > 0, "expected delta-cache hits");
    }

    [Fact]
    public void DifferentBaseAndLoopBodies_AccumulatorSeparated_MatchesNaive()
    {
        // Test-3 shape from the report: distinct body programs.
        var withSpec = ExactInterpreter.Evaluate(
            BuildBaseThenLoopGame(new[] { 3, 1 }, new[] { 1, 2 }),
            new DhState(3, BigInteger.Zero),
            s => s.Left,
            Spec);

        var counter = BigInteger.Zero;
        var naive = ExactInterpreter.Evaluate(
            BuildBaseThenLoopGame(new[] { 3, 1 }, new[] { 1, 2 }),
            new DhState(3, BigInteger.Zero),
            _ => counter++);

        AssertSameValueDistribution(withSpec, naive);
    }

    private sealed record RtState(int Left, int Awarded, BigInteger Total);

    [Fact]
    public void RetriggerLoop_WithAccumulatorSpec_CollapsesFurtherThanTotalInHash()
    {
        // Free spins with a capped retrigger: with the accumulator separated,
        // states collapse on (Left, Awarded) alone — strictly fewer branches
        // than hashing the banked total into the key, identical results.
        var weights = WeightSet.FromIntegers(new[] { 7, 2, 1 });
        var spec = new AccumulatorSpec<RtState, BigInteger>
        {
            Get = s => s.Total,
            Set = (s, v) => s with { Total = v },
            ShiftValue = (v, d) => v + d,
        };

        Slot<RtState, BigInteger> Build() =>
            Slot.Loop<RtState>(s => s.Left <= 0,
                from outcome in Slot.Draw<RtState>(_ => weights)
                from _ in Slot.Modify<RtState>(s =>
                {
                    if (outcome == 0) return s with { Left = s.Left - 1 };
                    if (outcome == 1) return s with { Left = s.Left - 1, Total = s.Total + 5 };
                    var extra = System.Math.Min(2, 16 - s.Awarded);
                    return s with { Left = s.Left - 1 + extra, Awarded = s.Awarded + extra };
                })
                select Unit.Value)
            .SelectMany(_ => Slot.GetState<RtState, BigInteger>(s => s.Total));

        var separated = ExactInterpreter.Evaluate(
            Build(), new RtState(6, 6, BigInteger.Zero),
            s => new BigInteger(s.Awarded) * 1024 + s.Left,
            spec);

        var totalInHash = ExactInterpreter.Evaluate(
            Build(), new RtState(6, 6, BigInteger.Zero),
            s => (new BigInteger(s.Awarded) * 1024 + s.Left) * 1_000_003 + s.Total);

        var (sepNum, sepDen) = separated.ValueDistribution().ExpectedBigIntegerValue();
        var (refNum, refDen) = totalInHash.ValueDistribution().ExpectedBigIntegerValue();
        Assert.Equal(refNum, sepNum);
        Assert.Equal(refDen, sepDen);

        Assert.True(
            separated.Stats.TotalBranchesEvaluated < totalInHash.Stats.TotalBranchesEvaluated,
            $"separated {separated.Stats.TotalBranchesEvaluated} should beat " +
            $"total-in-hash {totalInHash.Stats.TotalBranchesEvaluated}");
    }
}

[Collection("Registry")]
public class Compiler_FractionalPayouts : IDisposable
{
    public void Dispose() => SlotMath.Core.Mechanics.EvaluatorRegistry.Clear();

    [Fact]
    public void FractionalPaytablePayout_YieldsExactRationalRtp()
    {
        // Every spin lands sym-a on a single-row board and pays 2.5 — the
        // exact RTP must be exactly 5/2.  Before win scaling, the decimal
        // total was silently truncated to 2 at the BigInteger boundary.
        SlotMath.Core.Mechanics.EvaluatorRegistry.Register("lines", new SlotMath.Core.Mechanics.Evaluators.LinesEvaluator(
            new Paytable
            {
                Id = "pt",
                Entries = new[]
                {
                    new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "2.5" } },
                },
            },
            new PaylineSet { Id = "ps", Paylines = new[] { new Payline { Positions = new[] { 0, 0, 0 } } } }));

        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "fractional-payout",
            Symbols = new[] { new Symbol { Id = "sym-a", Name = "A", Kind = SymbolKind.Standard } },
            Paytables = new[]
            {
                new Paytable
                {
                    Id = "pt",
                    Entries = new[]
                    {
                        new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "2.5" } },
                    },
                },
            },
            ReelStrips = new[]
            {
                new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "sym-a", "sym-a" } },
                new ReelStrip { Id = "r2", Name = "R2", Symbols = new[] { "sym-a", "sym-a" } },
                new ReelStrip { Id = "r3", Name = "R3", Symbols = new[] { "sym-a", "sym-a" } },
            },
            ReelSets = new[] { new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1", "r2", "r3" } } },
            BoardConfig = new BoardConfig { Rows = 1, Columns = 3 },
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board } },
                },
                new MapNode
                {
                    Id = "eval",
                    Label = "Lines",
                    TransformId = "lines",
                    Inputs = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board } },
                    Outputs = new Dictionary<string, Port> { ["wins"] = new() { Name = "wins", Type = PortType.Wins } },
                },
                new MetricsSinkNode
                {
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port> { ["wins"] = new() { Name = "wins", Type = PortType.Wins } },
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "eval", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
        };

        var compiled = new GraphCompiler().Compile(config);
        Assert.True(compiled.IsValid, string.Join("; ", compiled.Errors.Select(e => e.Message)));
        Assert.Equal(new BigInteger(10), compiled.WinScale);

        var result = HybridEvaluator.Evaluate(
            compiled.Program!,
            new Dictionary<string, object?>(),
            StateHasher.CanonicalHash,
            new RegimeConfig { WinScale = compiled.WinScale });

        // Exact rational RTP = 5/2 — no truncation, no floats in the ratio.
        Assert.Equal(EvaluationStrategy.Exact, result.OverallStrategy);
        Assert.Equal(new BigInteger(5), result.Report.Rtp.RationalNumerator);
        Assert.Equal(new BigInteger(2), result.Report.Rtp.RationalDenominator);
        Assert.Equal(2.5, result.Report.Rtp.DisplayValue, 12);
    }
}
