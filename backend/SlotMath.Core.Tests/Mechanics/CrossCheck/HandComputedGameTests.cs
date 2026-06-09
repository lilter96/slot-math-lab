using System.Numerics;
using SlotMath.Core.Math;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;
using Xunit;
using Xunit.Abstractions;

namespace SlotMath.Core.Tests.Mechanics.CrossCheck;

// ═══════════════════════════════════════════════════════════════════════════
//  G13 DoD (b) — Hand-computed full games
//
//  Each game uses a bounded model so the exact interpreter can compute
//  the full distribution without stack overflow.  Every result is
//  compared against a hand-computed closed-form fraction.
// ═══════════════════════════════════════════════════════════════════════════

// ── Simple state — no loops needed for bounded-depth games ──────────────

public sealed record SimpleState(
    int Counter,
    int Multiplier,
    BigInteger Accumulated)
{
    public BigInteger RecurrenceHash => Counter * 100 + Multiplier;
    public SimpleState Inc() => this with { Counter = Counter + 1 };
    public SimpleState SetMult(int m) => this with { Multiplier = m };
    public SimpleState Add(BigInteger w) => this with { Accumulated = Accumulated + w };
}

// ═══════════════════════════════════════════════════════════════════════════
//  Game 1 — Single-level cascade (two sequential draws)
//
//  Draw 1: [3, 2, 1] → 0, 10, 25.
//  If win (idx>0), Draw 2: [1, 1] → multiplier 1x or 2x.
//
//  EV = (0*3 + 10*2*EV_mult2 + 25*1*EV_mult2) / 6
//  where EV_mult2 = (1+2)/2 = 1.5
//  = (0 + 20*1.5 + 25*1.5) / 6 = (0 + 30 + 37.5) / 6 = 67.5/6 = 45/4 = 11.25
//
//  Exact: (20*3/2 + 25*3/2) / 6 = (30 + 75/2) / 6 = (60/2 + 75/2) / 6
//  = 135/2 / 6 = 135/12 = 45/4
// ═══════════════════════════════════════════════════════════════════════════

public class Cascade_HandComputed(ITestOutputHelper output)
{
    [Fact]
    public void Cascade_ExactRtpMatchesHandComputation()
    {
        // Draw 1: base outcome. Draw 2: multiplier (only on win).
        var program =
            from idx in Slot.Draw<SimpleState>(
                _ => WeightSet.FromIntegers(new int[] { 3, 2, 1 }))
            let baseWin = idx switch
            {
                0 => BigInteger.Zero,
                1 => new BigInteger(10),
                2 => new BigInteger(25),
                _ => BigInteger.Zero,
            }
            from multIdx in idx == 0
                ? Slot.Pure<SimpleState, int>(1) // no cascade, mult=1
                : Slot.Draw<SimpleState, int>(
                    _ => WeightSet.FromIntegers(new int[] { 1, 1 }),
                    m => m + 1) // 1→1x, 0→2x
            select baseWin * multIdx;

        var result = ExactInterpreter.Evaluate(
            program,
            new SimpleState(0, 1, 0),
            s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        var (num, den) = vd.ExpectedBigIntegerValue();

        output.WriteLine($"Cascade EV = {num}/{den}");

        // EV = 45/4
        Assert.Equal(new BigInteger(45), num);
        Assert.Equal(new BigInteger(4), den);

        // Verify distribution has correct entries.
        // Non-win (idx=0): prob 3/6, payout 0 → contributes 0
        // idx=1, mult=1: prob 2/6 * 1/2 = 2/12, payout 10*2=20
        // idx=1, mult=2: prob 2/6 * 1/2 = 2/12, payout 10*1=10
        // idx=2, mult=1: prob 1/6 * 1/2 = 1/12, payout 25*2=50
        // idx=2, mult=2: prob 1/6 * 1/2 = 1/12, payout 25*1=25
        // Outcomes: 0(w=6/12), 10(w=2/12), 20(w=2/12), 25(w=1/12), 50(w=1/12)
        // Sum above = 12/12 ✓
        Assert.Equal(5, vd.Count); // 5 distinct outcomes
        Assert.True(vd.IsFullyExact);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Game 2 — Free-spins (bounded loop, no retrigger)
//
//  3 spins. Each: draw [1, 1, 1] → pay 0, 10, 20.
//  EV per spin = 30/3 = 10. Total EV = 30.
// ═══════════════════════════════════════════════════════════════════════════

public class FreeSpins_HandComputed(ITestOutputHelper output)
{
    /// <summary>
    /// 3 independent draws, accumulate wins.
    /// </summary>
    [Fact]
    public void FreeSpins_ExactRtpMatchesHandComputation()
    {
        var program =
            from a in Slot.Draw<SimpleState>(
                _ => WeightSet.FromIntegers(new int[] { 1, 1, 1 }))
            from b in Slot.Draw<SimpleState>(
                _ => WeightSet.FromIntegers(new int[] { 1, 1, 1 }))
            from c in Slot.Draw<SimpleState>(
                _ => WeightSet.FromIntegers(new int[] { 1, 1, 1 }))
            select new BigInteger(
                (a == 0 ? 0 : a == 1 ? 10 : 20) +
                (b == 0 ? 0 : b == 1 ? 10 : 20) +
                (c == 0 ? 0 : c == 1 ? 10 : 20));

        var result = ExactInterpreter.Evaluate(
            program, new SimpleState(0, 1, 0), s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        var (num, den) = vd.ExpectedBigIntegerValue();

        output.WriteLine($"Free-spins EV = {num}/{den}");
        Assert.Equal(new BigInteger(30), num);
        Assert.Equal(new BigInteger(1), den);
    }

    /// <summary>
    /// Free-spins with retrigger: 3 initial, retrigger on outcome 2.
    /// Use bounded recursion: explicitly model 1 retrigger level.
    ///
    /// Each spin: [1,1,1] → 0, 10, 20+retrigger.
    /// Retrigger adds 1 extra spin.
    ///
    /// EV = 3 base spins × 10 + P(retrigger=1) × EV_extra_spin
    /// P(retrigger per spin) = 1/3
    /// Expected retriggers from 3 spins = 3 × 1/3 = 1
    /// Extra spin EV = 10
    /// Total = 30 + 10 = 40
    /// </summary>
    [Fact]
    public void FreeSpinsWithRetrigger_ExactRtpMatchesHandComputation()
    {
        // 3 base spins, each can trigger 1 retrigger.
        // Model: 3 draws → for each win on outcome 2, add 1 extra draw.
        var program =
            from a in Slot.Draw<SimpleState>(
                _ => WeightSet.FromIntegers(new int[] { 1, 1, 1 }))
            from b in Slot.Draw<SimpleState>(
                _ => WeightSet.FromIntegers(new int[] { 1, 1, 1 }))
            from c in Slot.Draw<SimpleState>(
                _ => WeightSet.FromIntegers(new int[] { 1, 1, 1 }))
                // Count retriggers from base spins.
            let retriggerCount = (a == 2 ? 1 : 0) + (b == 2 ? 1 : 0) + (c == 2 ? 1 : 0)
            // Extra spin (if any retrigger) — model just 1 extra for boundedness.
            from extra in retriggerCount > 0
                ? Slot.Draw<SimpleState, int>(
                    _ => WeightSet.FromIntegers(new int[] { 1, 1, 1 }),
                    x => x == 0 ? 0 : x == 1 ? 10 : 20)
                : Slot.Pure<SimpleState, int>(0)
            select new BigInteger(
                (a == 0 ? 0 : a == 1 ? 10 : 20) +
                (b == 0 ? 0 : b == 1 ? 10 : 20) +
                (c == 0 ? 0 : c == 1 ? 10 : 20) +
                extra);

        var result = ExactInterpreter.Evaluate(
            program, new SimpleState(0, 1, 0), s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        var (num, den) = vd.ExpectedBigIntegerValue();

        output.WriteLine($"Free-spins+retrigger EV = {num}/{den}");

        // Hand-computed: 3 base × 10 + P(≥1 retrigger) × 10
        // P(0 retriggers) = (2/3)^3 = 8/27
        // P(≥1 retrigger) = 19/27
        // EV = 30 + (19/27)*10 = 30 + 190/27 = (810+190)/27 = 1000/27
        Assert.Equal(new BigInteger(1000), num);
        Assert.Equal(new BigInteger(27), den);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Game 3 — Hold & Win (simplified: 2 respins, no reset)
//
//  2 respins. Each: draw [1,1] → miss(0)/hit(collect 5).
//  EV per respin = 5/2 = 2.5. Total EV = 5.
// ═══════════════════════════════════════════════════════════════════════════

public class HoldAndWin_HandComputed(ITestOutputHelper output)
{
    [Fact]
    public void HoldAndWin_ExactRtpMatchesHandComputation()
    {
        // 2 independent respins, each hit collects 5.
        var program =
            from r1 in Slot.Draw<SimpleState>(
                _ => WeightSet.FromIntegers(new int[] { 1, 1 }))
            from r2 in Slot.Draw<SimpleState>(
                _ => WeightSet.FromIntegers(new int[] { 1, 1 }))
            let w1 = r1 == 1 ? new BigInteger(5) : BigInteger.Zero
            let w2 = r2 == 1 ? new BigInteger(5) : BigInteger.Zero
            select w1 + w2;

        var result = ExactInterpreter.Evaluate(
            program, new SimpleState(0, 1, 0), s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        var (num, den) = vd.ExpectedBigIntegerValue();

        output.WriteLine($"Hold & Win EV = {num}/{den}");
        Assert.Equal(new BigInteger(5), num);
        Assert.Equal(new BigInteger(1), den);
    }

    /// <summary>
    /// Hold & Win with reset: 3 respins. On hit → collect 10, reset to 3.
    /// Bounded: treat it as a simple game — 1 initial spin + up to 1 reset.
    ///
    /// Draw [1,1]: miss(SpinsRemaining→2) or hit(collect 10, reset→3).
    /// But reset creates unbounded recursion. Bound to at most 1 reset.
    ///
    /// Model: draw 1 → on hit → draw 2 (the "reset" spin) → stop.
    /// P(hit on draw 1) = 1/2 → collect 10, then one more draw with [1,1].
    /// EV = 0*1/2 + (10 + 5)*1/2 = 7.5 = 15/2
    /// </summary>
    [Fact]
    public void HoldAndWinWithReset_OneLevel_ExactRtpMatchesHandComputation()
    {
        var program =
            from r1 in Slot.Draw<SimpleState>(
                _ => WeightSet.FromIntegers(new int[] { 1, 1 }))
            from r2 in r1 == 1
                ? from x in Slot.Draw<SimpleState>(
                    _ => WeightSet.FromIntegers(new int[] { 1, 1 }))
                  select x == 1 ? new BigInteger(5) : BigInteger.Zero
                : Slot.Pure<SimpleState, BigInteger>(0)
            let w1 = r1 == 1 ? new BigInteger(10) : BigInteger.Zero
            select w1 + r2;

        var result = ExactInterpreter.Evaluate(
            program, new SimpleState(0, 1, 0), s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        var (num, den) = vd.ExpectedBigIntegerValue();

        output.WriteLine($"Hold & Win + reset EV = {num}/{den}");

        // EV: miss(miss on reset) = 0, miss(hit on reset)=0, hit(miss on reset)=10, hit(hit on reset)=15
        // Prob: miss=1/2, hit+miss=1/4, hit+hit=1/4
        // EV = 0*1/2 + 10*1/4 + 15*1/4 = 0 + 2.5 + 3.75 = 6.25 = 25/4
        Assert.Equal(new BigInteger(25), num);
        Assert.Equal(new BigInteger(4), den);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Game 4 — Cluster + tumble + rising multiplier
//
//  Draw 1 (base): [2, 1, 1] → 0(no cluster), 10(small), 30(big).
//  If cluster hit (idx>0), Draw 2 with multiplier=2.
//
//  EV₁ = (0*2 + 10*1 + 30*1)/4 = 40/4 = 10
//  EV₂ with mult=2 and draw [2,1,1]: EV per step at mult=2 is 10*2 = 20
//  Total = 10 + P(hit)*20 = 10 + (2/4)*20 = 10 + 10 = 20
//  10 + 10 = 20
// ═══════════════════════════════════════════════════════════════════════════

public class ClusterTumbleRisingMultiplier_HandComputed(ITestOutputHelper output)
{
    [Fact]
    public void ClusterTumbleRisingMultiplier_ExactRtpMatchesHandComputation()
    {
        // Draw 1: cluster hit? → if yes, Draw 2 with ×2 multiplier.
        var program =
            from idx1 in Slot.Draw<SimpleState>(
                _ => WeightSet.FromIntegers(new int[] { 2, 1, 1 }))
            let baseWin = idx1 switch
            {
                0 => BigInteger.Zero,
                1 => new BigInteger(10),
                2 => new BigInteger(30),
                _ => BigInteger.Zero,
            }
            from cascadeWin in idx1 == 0
                ? Slot.Pure<SimpleState, BigInteger>(0)
                : from idx2 in Slot.Draw<SimpleState>(
                    _ => WeightSet.FromIntegers(new int[] { 2, 1, 1 }))
                  select idx2 switch
                  {
                      0 => BigInteger.Zero,
                      1 => new BigInteger(10 * 2), // multiplier ×2
                      2 => new BigInteger(30 * 2),
                      _ => BigInteger.Zero,
                  }
            select baseWin + cascadeWin;

        var result = ExactInterpreter.Evaluate(
            program, new SimpleState(0, 1, 0), s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        var (num, den) = vd.ExpectedBigIntegerValue();

        output.WriteLine($"Cluster+tumble+rising EV = {num}/{den}");

        // EV = base EV + P(hit)*cascade EV
        // Base: EV = (0*2 + 10*1 + 30*1)/4 = 10
        // P(hit) = 2/4 = 1/2
        // Cascade (mult×2): EV = (0*2 + 20*1 + 60*1)/4 = 20
        // Total = 10 + (1/2)*20 = 20
        Assert.Equal(new BigInteger(20), num);
        Assert.Equal(new BigInteger(1), den);
    }
}
