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
