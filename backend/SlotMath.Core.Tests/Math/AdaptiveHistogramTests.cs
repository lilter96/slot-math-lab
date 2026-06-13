using SlotMath.Core;
using SlotMath.Core.Math;
using SlotMath.Core.Monad;

namespace SlotMath.Core.Tests.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  D14 — decade-based adaptive sampled histogram
//
//  0..100x in 1x bins, 100..1000x in 10x bins, 1000..10000x in 100x bins,
//  and a single ≥10000x tail bucket. Bins sum on merge, so the histogram is
//  deterministic for a given (seed, n) at any thread count (D3).
// ═══════════════════════════════════════════════════════════════════════════

public class AdaptiveHistogramTests
{
    [Fact]
    public void BinsValuesByDecade()
    {
        var s = new StreamingStats();
        s.Add(0.0);      // [0, 1)
        s.Add(50.0);     // [50, 51)
        s.Add(99.0);     // [99, 100)
        s.Add(150.0);    // [150, 160)  — 10x decade
        s.Add(1500.0);   // [1500, 1600) — 100x decade
        s.Add(50_000.0); // ≥10000x tail

        var bins = s.BuildAdaptiveHistogram();

        Assert.Contains(bins, b => b.LowerBound == 0 && b.UpperBound == 1 && b.Count == 1);
        Assert.Contains(bins, b => b.LowerBound == 50 && b.UpperBound == 51 && b.Count == 1);
        Assert.Contains(bins, b => b.LowerBound == 99 && b.UpperBound == 100 && b.Count == 1);
        Assert.Contains(bins, b => b.LowerBound == 150 && b.UpperBound == 160 && b.Count == 1);
        Assert.Contains(bins, b => b.LowerBound == 1500 && b.UpperBound == 1600 && b.Count == 1);
        Assert.Contains(bins, b => b.LowerBound == 10_000 && double.IsPositiveInfinity(b.UpperBound) && b.Count == 1);
    }

    [Fact]
    public void EmptyBinsAreOmitted_AndCountsAccumulate()
    {
        var s = new StreamingStats();
        s.Add(5.0);
        s.Add(5.0);
        s.Add(5.0);

        var bins = s.BuildAdaptiveHistogram();
        var bin = Assert.Single(bins);
        Assert.Equal(5, bin.LowerBound);
        Assert.Equal(6, bin.UpperBound);
        Assert.Equal(3, bin.Count);
    }

    [Fact]
    public void Merge_IsOrderIndependent_ForAdaptiveBins()
    {
        StreamingStats Build(params double[] values)
        {
            var s = new StreamingStats();
            foreach (var v in values) s.Add(v);
            return s;
        }

        var a = Build(5, 5, 250);
        var b = Build(250, 9999, 99_999);

        var m1 = new StreamingStats();
        m1.Merge(a);
        m1.Merge(b);

        var m2 = new StreamingStats();
        m2.Merge(b);
        m2.Merge(a);

        var h1 = m1.BuildAdaptiveHistogram().Select(x => (x.LowerBound, x.Count)).ToArray();
        var h2 = m2.BuildAdaptiveHistogram().Select(x => (x.LowerBound, x.Count)).ToArray();
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void RefD_Sampled_AdaptiveHistogram_HasZeroAndTailMass()
    {
        // REF-D pays 0 (huge mass) or 5000 (1/10000). 5000 lands in [5000, 5100);
        // 0 lands in [0, 1).
        var config = new SampledConfig { Seed = (long)SlotMathConstants.Seeds.Main, MaxSpins = 200_000 };
        var result = SampledInterpreter.EvaluateEmit(ReferenceFixtureTests.RefD(), Unit.Value, config);

        var bins = result.Stats.BuildAdaptiveHistogram();
        Assert.Contains(bins, b => b.LowerBound == 0 && b.Count > 0);
        Assert.Contains(bins, b => b.LowerBound == 5000 && b.UpperBound == 5100 && b.Count > 0);
    }
}
