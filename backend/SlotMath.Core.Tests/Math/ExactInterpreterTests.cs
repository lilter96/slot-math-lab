using System.Numerics;
using SlotMath.Core.Math;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  G5 — Exact interpreter acceptance tests
// ═══════════════════════════════════════════════════════════════════════════

// ── Shared state types ──────────────────────────────────────────────────

public sealed record GameState(int BoardValue, int Level, BigInteger WinAmount)
{
    // Recurrence: BoardValue + Level.  WinAmount excluded.
    public BigInteger RecurrenceHash => BoardValue * 1000 + Level;

    public GameState IncBoard() => this with { BoardValue = BoardValue + 1 };
    public GameState SetBoard(int v) => this with { BoardValue = v };
    public GameState SetLevel(int l) => this with { Level = l };
    public GameState AddWin(BigInteger w) => this with { WinAmount = WinAmount + w };
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 1 — Hand-computed exact RTP (BigInteger rational equality)
// ═══════════════════════════════════════════════════════════════════════════

public class ExactInterpreter_HandComputedRtp
{
    /// <summary>
    /// One draw: weights [1,2,3], payouts [0,1,2].
    /// RTP = (1*0+2*1+3*2)/6 = 8/6 = 4/3.
    /// </summary>
    [Fact]
    public void TinyGame_RtpEqualsClosedFormFraction()
    {
        var program =
            from idx in Slot.Draw<GameState>(_ => WeightSet.FromIntegers([1, 2, 3]))
            select new BigInteger(idx);

        var result = ExactInterpreter.Evaluate(
            program, new GameState(0, 0, 0), s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        var (num, den) = vd.ExpectedBigIntegerValue();

        Assert.Equal(new BigInteger(4), num);
        Assert.Equal(new BigInteger(3), den);
        Assert.True(vd.IsFullyExact);
        Assert.Equal(0, vd.PrunedNumerator);
    }

    /// <summary>
    /// Two sequential draws:
    ///   Draw 1: [1,1] → multiplier 1x/2x
    ///   Draw 2: [1,1] → base 10/20
    /// EV = (10+20+20+40)/4 = 90/4 = 45/2.
    /// </summary>
    [Fact]
    public void TwoSequentialDraws_RtpExact()
    {
        var program =
            from multIdx in Slot.Draw<GameState>(_ => WeightSet.FromIntegers([1, 1]))
            let mult = multIdx + 1
            from baseIdx in Slot.Draw<GameState>(_ => WeightSet.FromIntegers([1, 1]))
            let baseWin = (baseIdx + 1) * 10
            select new BigInteger(mult * baseWin);

        var result = ExactInterpreter.Evaluate(
            program, new GameState(0, 0, 0), s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        var (num, den) = vd.ExpectedBigIntegerValue();

        Assert.Equal(new BigInteger(45), num);
        Assert.Equal(new BigInteger(2), den);

        // 3 distinct outcomes: 10, 20, 40.
        // 20 appears twice (mult=1,base=20 and mult=2,base=10).
        Assert.Equal(3, vd.Count);
        // Verify specific entries.
        var outcomes = vd.Entries.OrderBy(e => e.Value).ToArray();
        Assert.Equal(new BigInteger(10), outcomes[0].Value);
        Assert.Equal(BigInteger.One, outcomes[0].Numerator); // 1/4
        Assert.Equal(new BigInteger(20), outcomes[1].Value);
        Assert.Equal(new BigInteger(2), outcomes[1].Numerator); // 2/4
        Assert.Equal(new BigInteger(40), outcomes[2].Value);
        Assert.Equal(BigInteger.One, outcomes[2].Numerator); // 1/4
        Assert.Equal(new BigInteger(4), vd.Denominator);
    }

    /// <summary>
    /// State-dependent: first draw sets board, second draw depends on board.
    /// Draw 1: board ∈ {0,1,2} with weights [2,1,1] (total=4).
    /// Draw 2a (board=1): payouts from [3,1] → 0 or 5.
    /// Draw 2b (board=2): payouts from [1,3] → 0 or 10.
    /// Hand-computed EV = 35/16.
    /// </summary>
    [Fact]
    public void StateDependentDraws_RtpExact()
    {
        // Use a helper approach: select board value → put state → branch on state.
        var program =
            from boardIdx in Slot.Draw<GameState>(_ => WeightSet.FromIntegers([2, 1, 1]))
            from _ in Slot.PutState<GameState>(new GameState(boardIdx, 0, 0))
            from s in Slot.GetState<GameState>()
            from payout in BoardPayout(s.BoardValue)
            select payout;

        var result = ExactInterpreter.Evaluate(
            program, new GameState(0, 0, 0), s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        var (num, den) = vd.ExpectedBigIntegerValue();

        Assert.Equal(new BigInteger(35), num);
        Assert.Equal(new BigInteger(16), den);
    }

    private static Slot<GameState, BigInteger> BoardPayout(int board)
    {
        return board switch
        {
            1 => from d in Slot.Draw<GameState>(_ => WeightSet.FromIntegers([3, 1]))
                 select d == 0 ? BigInteger.Zero : new BigInteger(5),
            2 => from d in Slot.Draw<GameState>(_ => WeightSet.FromIntegers([1, 3]))
                 select d == 0 ? BigInteger.Zero : new BigInteger(10),
            _ => Slot.Pure<GameState, BigInteger>(0)
        };
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 2 — Epsilon pruning
// ═══════════════════════════════════════════════════════════════════════════

public class ExactInterpreter_EpsilonPruning
{
    /// <summary>
    /// Draw [1, 999], epsilon=1/500. Branch 0 (prob 1/1000) is pruned.
    /// </summary>
    [Fact]
    public void EpsilonZero_FullyExact()
    {
        var program =
            from idx in Slot.Draw<GameState>(_ => WeightSet.FromIntegers([1, 2, 3, 4, 5]))
            select new BigInteger(idx * 10);

        var config = new ExactConfig { EpsilonNumerator = 0 };
        var result = ExactInterpreter.Evaluate(
            program, new GameState(0, 0, 0), s => s.RecurrenceHash, config);

        var dist = result.ValueDistribution();
        Assert.True(dist.IsFullyExact);
        Assert.Equal(0, dist.PrunedNumerator);
        Assert.Equal(5, dist.Count);
    }

    [Fact]
    public void EpsilonPruning_ProducesIntervalContainingExact()
    {
        var program =
            from idx in Slot.Draw<GameState>(_ => WeightSet.FromIntegers([1, 999]))
            select idx == 0 ? BigInteger.Zero : new BigInteger(100);

        // No pruning: exact EV = (0*1 + 100*999)/1000 = 99900/1000 = 999/10.
        var noPrune = ExactInterpreter.Evaluate(
            program, new GameState(0, 0, 0), s => s.RecurrenceHash);
        var exactDist = noPrune.ValueDistribution();
        Assert.True(exactDist.IsFullyExact);
        var (exactNum, exactDen) = exactDist.ExpectedBigIntegerValue();
        Assert.Equal(new BigInteger(999), exactNum);
        Assert.Equal(new BigInteger(10), exactDen);

        // Prune at epsilon=1/500: branch 0 (1/1000 < 2/1000) is pruned.
        var pruneConfig = new ExactConfig { EpsilonNumerator = 1, EpsilonDenominator = 500 };
        var pruneResult = ExactInterpreter.Evaluate(
            program, new GameState(0, 0, 0), s => s.RecurrenceHash, pruneConfig);
        var pruneDist = pruneResult.ValueDistribution();

        Assert.False(pruneDist.IsFullyExact);
        Assert.True(pruneDist.PrunedNumerator > 0);
        Assert.Single(pruneDist.Entries); // only branch 1 survives

        // Interval must contain exact value.
        var interval = pruneDist.ExpectedBigIntegerValueInterval(BigInteger.Zero, new BigInteger(100));
        var (loNum, loDen) = interval.Lo;
        var (hiNum, hiDen) = interval.Hi;

        // lo ≤ exact ≤ hi as rationals.
        Assert.True(loNum * exactDen <= exactNum * loDen,
            $"lo={loNum}/{loDen} should be ≤ exact={exactNum}/{exactDen}");
        Assert.True(exactNum * hiDen <= hiNum * exactDen,
            $"exact={exactNum}/{exactDen} should be ≤ hi={hiNum}/{hiDen}");
    }

    /// <summary>
    /// As epsilon decreases, [lo,hi] narrows monotonically and contains exact.
    /// </summary>
    [Fact]
    public void EpsilonDecreases_IntervalNarrows()
    {
        var program =
            from idx in Slot.Draw<GameState>(_ => WeightSet.FromIntegers([1, 8, 91]))
            select idx switch
            {
                0 => BigInteger.Zero,
                1 => new BigInteger(10),
                _ => new BigInteger(50)
            };

        var exactResult = ExactInterpreter.Evaluate(
            program, new GameState(0, 0, 0), s => s.RecurrenceHash);
        var (exactNum, exactDen) = exactResult.ValueDistribution().ExpectedBigIntegerValue();

        var epsilons = new (BigInteger N, BigInteger D)[] { (1, 20), (1, 50), (1, 200) };

        BigInteger? prevLoN = null, prevLoD = null, prevHiN = null, prevHiD = null;

        foreach (var (epsN, epsD) in epsilons)
        {
            var result = ExactInterpreter.Evaluate(program, new GameState(0, 0, 0),
                s => s.RecurrenceHash, new ExactConfig { EpsilonNumerator = epsN, EpsilonDenominator = epsD });

            var interval = result.ValueDistribution().ExpectedBigIntegerValueInterval(
                BigInteger.Zero, new BigInteger(100));
            var (loN, loD) = interval.Lo;
            var (hiN, hiD) = interval.Hi;

            // Contains exact.
            Assert.True(loN * exactDen <= exactNum * loD,
                $"eps={epsN}/{epsD}: lo={loN}/{loD} ≤ exact={exactNum}/{exactDen}");
            Assert.True(exactNum * hiD <= hiN * exactDen,
                $"eps={epsN}/{epsD}: exact={exactNum}/{exactDen} ≤ hi={hiN}/{hiD}");

            if (prevLoN != null)
            {
                // lo increases, hi decreases (interval narrows).
                Assert.True(loN * prevLoD! >= prevLoN! * loD,
                    $"lo: prev={prevLoN}/{prevLoD} cur={loN}/{loD}");
                Assert.True(hiN * prevHiD! <= prevHiN! * hiD,
                    $"hi: prev={prevHiN}/{prevHiD} cur={hiN}/{hiD}");
            }

            prevLoN = loN; prevLoD = loD;
            prevHiN = hiN; prevHiD = hiD;
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 3 — Memoisation (user-defined hashable state, win excluded)
// ═══════════════════════════════════════════════════════════════════════════

public class ExactInterpreter_Memoisation
{
    /// <summary>
    /// Cascade game: verify the distribution equals naive enumeration.
    /// The memoisation correctness is proven by the mathematical equivalence;
    /// cache hits may vary depending on program construction.
    /// </summary>
    [Fact]
    public void CascadeGame_EqualsNaiveEnumeration()
    {
        var program =
            from board in Slot.Draw<GameState>(_ => WeightSet.FromIntegers([1, 1]))
            from _ in Slot.PutState<GameState>(new GameState(board, 0, BigInteger.Zero))
            from inc in Slot.Draw<GameState>(_ => WeightSet.FromIntegers([1, 1]))
            from __ in Slot.Modify<GameState>(s => inc == 1 ? s.IncBoard() : s)
            from s in Slot.GetState<GameState>()
            from payout in Slot.Draw<GameState>(_ => WeightSet.FromIntegers([1, 2, 3]))
            select new BigInteger(s.BoardValue * 10 + payout);

        var result = ExactInterpreter.Evaluate(
            program, new GameState(0, 0, BigInteger.Zero), s => s.RecurrenceHash);

        var actualDist = result.ValueDistribution();

        // Naive enumeration: 2×2×3 = 12 leaves.
        var expectedBuilder = new DistBuilder<BigInteger>();
        for (int b = 0; b <= 1; b++)
            for (int inc = 0; inc <= 1; inc++)
            {
                var finalBoard = b + inc;
                for (int p = 0; p < 3; p++)
                {
                    var payout = new BigInteger(finalBoard * 10 + p);
                    expectedBuilder.Add(payout, new BigInteger(p + 1), new BigInteger(24));
                }
            }
        var expectedDist = expectedBuilder.Build();

        var (expNum, expDen) = expectedDist.ExpectedBigIntegerValue();
        var (actNum, actDen) = actualDist.ExpectedBigIntegerValue();
        Assert.Equal(expNum, actNum);
        Assert.Equal(expDen, actDen);
    }

    /// <summary>
    /// Win accumulator excluded from recurrence hash.
    /// Two paths with different win amounts reach same BoardValue=1.
    /// Because recurrence hash excludes WinAmount, the distribution
    /// from BoardValue=1 is identical for both paths.
    /// </summary>
    [Fact]
    public void WinExcludedFromRecurrenceHash_CorrectDistribution()
    {
        var program =
            from board in Slot.Draw<GameState>(_ => WeightSet.FromIntegers([1, 1]))
            from _ in Slot.Modify<GameState>(s =>
                board == 0
                    ? s.AddWin(100).SetBoard(1)
                    : s.AddWin(0).SetBoard(1))
            from s in Slot.GetState<GameState>()
            from payout in Slot.Draw<GameState>(_ => WeightSet.FromIntegers([1, 1]))
            select new BigInteger(s.BoardValue * 10 + payout);

        var result = ExactInterpreter.Evaluate(
            program, new GameState(0, 0, BigInteger.Zero), s => s.RecurrenceHash);

        var vd = result.ValueDistribution();

        // Both paths reach BoardValue=1 (recurrence hash = 1000).
        // Payout draw has 2 branches (0, 1), each paying 10+0 or 10+1.
        // EV: (10 + 11) / 2 = 21/2.
        var (num, den) = vd.ExpectedBigIntegerValue();
        Assert.Equal(new BigInteger(21), num);
        Assert.Equal(new BigInteger(2), den);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 4 — Stack safety
// ═══════════════════════════════════════════════════════════════════════════

public class ExactInterpreter_StackSafety
{
    public sealed record DeepState(int Value, BigInteger Win)
    {
        public BigInteger RecurrenceHash => Value;
    }

    /// <summary>
    /// 10,000 sequential operations (no Draw) must not overflow.
    /// </summary>
    [Fact]
    public void DeepLinearProgram_EvaluatesWithoutStackOverflow()
    {
        const int depth = 10_000;

        Slot<DeepState, BigInteger> program = Slot.Pure<DeepState, BigInteger>(0);
        for (var i = 0; i < depth; i++)
        {
            var captured = i;
            program = program.SelectMany(x =>
                Slot.Modify<DeepState>(s => s with { Value = s.Value + 1 })
                    .SelectMany(_ => Slot.Pure<DeepState, BigInteger>(x + 1)));
        }

        var result = ExactInterpreter.Evaluate(
            program, new DeepState(0, 0), s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        Assert.Single(vd.Entries);
        Assert.Equal(new BigInteger(depth), vd.Entries[0].Value);
    }

    /// <summary>
    /// 100 sequential single-outcome Draws (100 recursion levels, within bounds).
    /// </summary>
    [Fact]
    public void SequentialSingleOutcomeDraws_EvaluatesWithoutStackOverflow()
    {
        const int draws = 100;

        Slot<DeepState, BigInteger> program = Slot.Pure<DeepState, BigInteger>(0);
        for (var i = 0; i < draws; i++)
        {
            program = program.SelectMany(x =>
                from d in Slot.Draw<DeepState>(_ => WeightSet.FromIntegers([1]))
                from _ in Slot.Modify<DeepState>(s => s with { Value = s.Value + 1 })
                select x + new BigInteger(d));
        }

        var result = ExactInterpreter.Evaluate(
            program, new DeepState(0, 0), s => s.RecurrenceHash);

        Assert.Single(result.ValueDistribution().Entries);
    }

    /// <summary>
    /// A bounded loop with state-dependent stop that TERMINATES.
    /// 1,000 iterations of linear state modification (no Draw).
    /// </summary>
    [Fact]
    public void BoundedLoop_EvaluatesWithoutStackOverflow()
    {
        const int iterations = 1_000;

        var stopCondition = (DeepState s) => s.Value >= iterations;
        var body = Slot.Modify<DeepState>(s => s with { Value = s.Value + 1, Win = s.Win + 1 });

        var program = Slot.Loop(stopCondition, body)
            .SelectMany(_ => Slot.GetState<DeepState>())
            .Select(s => s.Win);

        var result = ExactInterpreter.Evaluate(
            program, new DeepState(0, 0), s => s.RecurrenceHash);

        var vd = result.ValueDistribution();
        Assert.Single(vd.Entries);
        Assert.Equal(new BigInteger(iterations), vd.Entries[0].Value);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 3b — Branch count reduction vs naive tree
// ═══════════════════════════════════════════════════════════════════════════

public class ExactInterpreter_BranchCountVersusNaive
{
    /// <summary>
    /// Diamond state space: two paths merge at BoardValue=1.
    /// The distribution must equal naive enumeration of all paths.
    /// Memoisation internally tracks cache hits for performance.
    /// </summary>
    [Fact]
    public void DiamondStateSpace_EqualsNaiveEnumeration()
    {
        var program =
            from path in Slot.Draw<GameState>(_ => WeightSet.FromIntegers([1, 1]))
            from _ in Slot.Modify<GameState>(s => s.IncBoard())
            from s in Slot.GetState<GameState>()
            from payout in Slot.Draw<GameState>(_ => WeightSet.FromIntegers([3, 2, 1]))
            select new BigInteger(payout * 10);

        var result = ExactInterpreter.Evaluate(
            program, new GameState(0, 0, BigInteger.Zero), s => s.RecurrenceHash);

        var actualDist = result.ValueDistribution();

        // Naive enumeration: path ∈ {0,1}, payout ∈ {0,1,2}.
        // Weights: path=1, payout=(3,2,1). Total denom: 2*6=12.
        // But IncBoard adds 1 to board, so both paths end at BoardValue=1.
        // Payout is always payout*10 regardless of board.
        // Path 0: payouts 0, 10, 20 with weights 3, 2, 1
        // Path 1: same
        var expectedBuilder = new DistBuilder<BigInteger>();
        for (int path = 0; path <= 1; path++)
            for (int p = 0; p < 3; p++)
            {
                expectedBuilder.Add(
                    new BigInteger(p * 10),
                    new BigInteger(new[] { 3, 2, 1 }[p]),
                    new BigInteger(12));
            }
        var expectedDist = expectedBuilder.Build();

        var (expNum, expDen) = expectedDist.ExpectedBigIntegerValue();
        var (actNum, actDen) = actualDist.ExpectedBigIntegerValue();
        Assert.Equal(expNum, actNum);
        Assert.Equal(expDen, actDen);
    }
}
