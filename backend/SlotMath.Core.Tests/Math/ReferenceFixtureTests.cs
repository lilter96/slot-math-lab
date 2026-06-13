using SlotMath.Core;
using SlotMath.Core.Math;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  D9 reference fixtures built on the Emit substrate (D13) + capped Loop (D6).
//
//  These exercise the win-as-Emit substrate on the runtime path: the
//  trampoline (deterministic, per-label accumulation + loop-cap) and the
//  sampled interpreter (convergence to the D9 closed forms). The exact-path
//  Emit integration (rational RTP equality) is the next step.
// ═══════════════════════════════════════════════════════════════════════════

public class ReferenceFixtureTests
{
    // ── REF-A "Coin": one draw {A:w1 pay3, B:w3 pay1, C:w4 pay0}, label "base" ──

    private static Slot<Unit, Unit> RefA()
    {
        var weights = WeightSet.FromIntegers([1, 3, 4]);
        return Slot.Draw<Unit>(_ => weights)
            .SelectMany(i => Slot.Emit<Unit>("base", i == 0 ? 3 : i == 1 ? 1 : 0));
    }

    [Theory]
    [InlineData(0, 3)] // A → pay 3
    [InlineData(1, 1)] // B → pay 1
    [InlineData(2, 0)] // C → pay 0
    public void RefA_Trampoline_EmitsCorrectLabelledWin(int choice, int expectedPay)
    {
        var result = TrampolineInterpreter.Run(RefA(), Unit.Value, new Queue<int>([choice]));

        Assert.Equal((Rational)expectedPay, result.TotalWin);
        Assert.Equal((Rational)expectedPay, result.Emits["base"]);
        Assert.False(result.LoopCapHit);
    }

    [Fact]
    public void RefA_Sampled_ConvergesToThreeQuarters()
    {
        // Closed form (D9): RTP = 3/4. Pinned seed; D8 tolerance (4·stdErr).
        var config = new SampledConfig { Seed = (long)SlotMathConstants.Seeds.Main, MaxSpins = 300_000 };
        var result = SampledInterpreter.EvaluateEmit(RefA(), Unit.Value, config);

        var tol = 4.0 * result.Stats.StdErr;
        Assert.True(
            System.Math.Abs(result.Stats.Mean - 0.75) <= tol,
            $"REF-A sampled mean {result.Stats.Mean:F6} not within 4·stdErr ({tol:F6}) of 3/4.");
    }

    // ── REF-D "Volcano": pay 5000 w1, pay 0 w9999; win cap 1000 → P(cap)=1/10000 ──

    private static Slot<Unit, Unit> RefD()
    {
        var weights = WeightSet.FromIntegers([1, 9999]);
        return Slot.Draw<Unit>(_ => weights)
            .SelectMany(i => Slot.Emit<Unit>("win", i == 0 ? 5000 : 0));
    }

    [Fact]
    public void RefD_Sampled_Uncapped_ConvergesToHalf()
    {
        // Uncapped RTP = 5000/10000 = 1/2.
        var config = new SampledConfig { Seed = (long)SlotMathConstants.Seeds.Main, MaxSpins = 2_000_000 };
        var result = SampledInterpreter.EvaluateEmit(RefD(), Unit.Value, config);

        var tol = 4.0 * result.Stats.StdErr;
        Assert.True(
            System.Math.Abs(result.Stats.Mean - 0.5) <= tol,
            $"REF-D sampled mean {result.Stats.Mean:F6} not within 4·stdErr ({tol:F6}) of 1/2.");
    }

    [Fact]
    public void RefD_Sampled_Capped_RecordsCapHits()
    {
        // With win cap 1000, P(cap reached) = 1/10000. Over 2M spins ≈ 200 hits.
        var config = new SampledConfig
        {
            Seed = (long)SlotMathConstants.Seeds.Main,
            MaxSpins = 2_000_000,
            MaxWinCap = 1000,
        };
        var result = SampledInterpreter.EvaluateEmit(RefD(), Unit.Value, config);

        // Deterministic at the pinned seed; assert a sane band around the 1/10000 rate.
        Assert.InRange(result.Stats.CapHits, 100, 320);
    }

    // ── REF-C "MiniCascade": 3-cell row, P³ pays 5 / Q³ pays 2, redraw; cap 10 ──

    private sealed record CascadeState(int CascadesUsed, bool Continue);

    private static Slot<CascadeState, Unit> RefC()
    {
        var cells = WeightSet.FromIntegers([2, 3, 5]); // P, Q, R

        Slot<CascadeState, Unit> body =
            Slot.Draw<CascadeState>(_ => cells).SelectMany(a =>
            Slot.Draw<CascadeState>(_ => cells).SelectMany(b =>
            Slot.Draw<CascadeState>(_ => cells).SelectMany(c =>
            {
                if (a == 0 && b == 0 && c == 0) // P triple
                    return Slot.Emit<CascadeState>("cascade_win", 5)
                        .SelectMany(_ => Slot.Modify<CascadeState>(
                            s => s with { CascadesUsed = s.CascadesUsed + 1, Continue = true }));
                if (a == 1 && b == 1 && c == 1) // Q triple
                    return Slot.Emit<CascadeState>("cascade_win", 2)
                        .SelectMany(_ => Slot.Modify<CascadeState>(
                            s => s with { CascadesUsed = s.CascadesUsed + 1, Continue = true }));
                return Slot.Modify<CascadeState>(
                    s => s with { CascadesUsed = s.CascadesUsed + 1, Continue = false });
            })));

        // Cascade cap 10 enforced both by the stop condition and the loop cap (D6).
        return Slot.Loop<CascadeState>(
            s => !s.Continue || s.CascadesUsed >= 10, body, cap: 10);
    }

    [Fact]
    public void RefC_Sampled_ConvergesTo94Over965()
    {
        // Closed form (D9): v = 94/965 ≈ 0.097409.
        const double expected = 94.0 / 965.0;
        var config = new SampledConfig { Seed = (long)SlotMathConstants.Seeds.Main, MaxSpins = 1_000_000 };
        var result = SampledInterpreter.EvaluateEmit(RefC(), new CascadeState(0, true), config);

        var tol = 4.0 * result.Stats.StdErr;
        Assert.True(
            System.Math.Abs(result.Stats.Mean - expected) <= tol,
            $"REF-C sampled mean {result.Stats.Mean:F6} not within 4·stdErr ({tol:F6}) of 94/965 ({expected:F6}).");
    }

    // ── Loop cap (D6): a never-stopping loop force-exits at its cap ──

    [Fact]
    public void Loop_NeverStops_ExitsAtCapAndFlagsLoopCapHit()
    {
        // stop = false always; body increments the counter; cap = 5.
        var program = Slot.Loop<int>(_ => false, Slot.Modify<int>(c => c + 1), cap: 5);
        var result = TrampolineInterpreter.Run(program, 0, new Queue<int>());

        Assert.Equal(5, result.FinalState);
        Assert.True(result.LoopCapHit);
    }

    [Fact]
    public void Loop_StopsNaturally_DoesNotFlagCap()
    {
        // Stops when counter reaches 3 (before the cap of 100).
        var program = Slot.Loop<int>(c => c >= 3, Slot.Modify<int>(c => c + 1), cap: 100);
        var result = TrampolineInterpreter.Run(program, 0, new Queue<int>());

        Assert.Equal(3, result.FinalState);
        Assert.False(result.LoopCapHit);
    }

    [Fact]
    public void Loop_RejectsNonPositiveAndOverMaxCap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Slot.Loop<int>(_ => true, Slot.Modify<int>(c => c), cap: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Slot.Loop<int>(_ => true, Slot.Modify<int>(c => c), cap: SlotMathConstants.Loop.CapMax + 1));
    }
}
