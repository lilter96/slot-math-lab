using System.Numerics;
using SlotMath.Core.Math;

namespace SlotMath.Core.Tests.Math;

public sealed class MarkovRewardMatrixTests
{
    private static MarkovRewardTransition Edge(int from, int to, Rational probability, params Rational[] rewards)
    {
        var second = new List<Rational>();
        for (var a = 0; a < rewards.Length; a++)
        {
            for (var b = a; b < rewards.Length; b++)
            {
                second.Add(probability * rewards[a] * rewards[b]);
            }
        }

        return new MarkovRewardTransition(from, to, probability,
            rewards.Select(reward => probability * reward).ToArray(), second.ToArray(),
            rewards.Select(reward => reward.Sign < 0 ? (Rational?)null : reward.IsZero ? probability : Rational.Zero)
                .ToArray(),
            rewards.Select(reward => (Rational?)reward).ToArray(),
            rewards.Select(reward => (Rational?)reward).ToArray());
    }

    private static Rational Mass(MarkovRewardMatrix row) => new(
        row.Row(0).Values.Aggregate(BigInteger.Zero, (sum, weight) => sum + weight.Mass), row.Denominator);

    private static Rational First(MarkovRewardMatrix row, int amount = 0) => new(
        row.Row(0).Values.Aggregate(BigInteger.Zero, (sum, weight) => sum + weight.First[amount]), row.Denominator);

    private static Rational Second(MarkovRewardMatrix row, int pair = 0) => new(
        row.Row(0).Values.Aggregate(BigInteger.Zero, (sum, weight) => sum + weight.Second[pair]), row.Denominator);

    private static Rational Zero(MarkovRewardMatrix row, int amount = 0) => new(
        row.Row(0).Values.Aggregate(BigInteger.Zero, (sum, weight) => sum + weight.Zero[amount]!.Value),
        row.Denominator);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(100000)]
    public void PeriodicClosedChainKeepsTheFiniteHorizonAndExactBounds(int horizon)
    {
        long work = 0;
        var matrix = MarkovRewardMatrix.Create(2, 1,
            [Edge(0, 1, Rational.One, 1), Edge(1, 0, Rational.One, 3)], units => work += units);
        (MarkovRewardMatrix row, var completed) = matrix.Advance(0, horizon);
        var expected = new Rational(4 * (horizon / 2) + horizon % 2, 1);
        Assert.Equal(horizon, completed);
        Assert.Equal(Rational.One, Mass(row));
        Assert.Equal(expected, First(row));
        Assert.Equal(expected * expected, Second(row));
        Assert.Equal(horizon == 0 ? Rational.One : Rational.Zero, Zero(row));
        MarkovRewardMatrix.Weight weight = Assert.Single(row.Row(0)).Value;
        Assert.Equal(expected, weight.Lower[0]);
        Assert.Equal(expected, weight.Upper[0]);
        Assert.Equal(horizon % 2, Assert.Single(row.Row(0)).Key);
        Assert.True(work < 10000, $"work={work}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(9)]
    [InlineData(64)]
    public void RewardsCorrelatedWithTheNextStateRetainConditionalAndCrossMoments(int horizon)
    {
        // Y_t are independent fair bits, S_(t+1)=Y_t, A_t=S_t+Y_t, B_t=Y_t.
        // Total A = 2*(Y_1+...+Y_(n-1)) + Y_n; B = Y_1+...+Y_n.
        var edges = new List<MarkovRewardTransition>();
        for (var state = 0; state < 2; state++)
        {
            for (var next = 0; next < 2; next++)
            {
                edges.Add(Edge(state, next, new Rational(1, 2), state + next, next));
            }
        }

        var matrix = MarkovRewardMatrix.Create(2, 2, edges, _ => { });
        MarkovRewardMatrix row = matrix.Advance(0, horizon).Row;
        var meanA = new Rational(2 * horizon - 1, 2);
        var meanB = new Rational(horizon, 2);
        Assert.Equal(meanA, First(row));
        Assert.Equal(meanB, First(row, 1));
        Assert.Equal(meanA * meanA + new Rational(4 * horizon - 3, 4), Second(row));
        Assert.Equal(meanA * meanB + new Rational(2 * horizon - 1, 4), Second(row, 1));
        Assert.Equal(meanB * meanB + new Rational(horizon, 4), Second(row, 2));
        Assert.Equal(new Rational(1, BigInteger.One << horizon), Zero(row));
        Assert.Equal(Zero(row), Zero(row, 1));
        foreach ((var last, MarkovRewardMatrix.Weight weight) in row.Row(0))
        {
            Assert.Equal(new Rational(horizon - 1 + last, 1), new Rational(weight.First[0], weight.Mass));
        }
    }

    [Fact]
    public void DuplicateDestinationsPreserveTheWholeRewardLawInTheirMoments()
    {
        var matrix = MarkovRewardMatrix.Create(1, 1,
            [Edge(0, 0, new Rational(1, 2), 0), Edge(0, 0, new Rational(1, 2), new Rational(2, 3))], _ => { });
        const int n = 127;
        MarkovRewardMatrix row = matrix.Advance(0, n).Row;
        Assert.Equal(new Rational(n, 3), First(row));
        Assert.Equal(new Rational(n * n + n, 9), Second(row));
        Assert.Equal(new Rational(1, BigInteger.One << n), Zero(row));
        Assert.Equal(Rational.Zero, Assert.Single(row.Row(0)).Value.Lower[0]);
        Assert.Equal(new Rational(2 * n, 3), Assert.Single(row.Row(0)).Value.Upper[0]);
    }

    [Fact]
    public void AbsorbingAndUnreachableClassesDoNotChangeTheInitialStatesReward()
    {
        // In state 0, earn 1, and continue with probability 1/2. State 1 absorbs.
        // A disconnected closed class earns 100 forever and must never contribute.
        var matrix = MarkovRewardMatrix.Create(3, 1,
        [
            Edge(0, 0, new Rational(1, 2), 1), Edge(0, 1, new Rational(1, 2), 1),
            Edge(1, 1, Rational.One, 0), Edge(2, 2, Rational.One, 100)
        ], _ => { });
        const int n = 64;
        MarkovRewardMatrix row = matrix.Advance(0, n).Row;
        var tail = new Rational(1, BigInteger.One << n);
        Assert.Equal(new Rational(2, 1) * (Rational.One - tail), First(row));
        Assert.Equal(new Rational(6, 1) - (4 * n + 6) * tail, Second(row));
        Assert.Equal(tail, new Rational(row.Row(0)[0].Mass, row.Denominator));
        Assert.False(row.Row(0).ContainsKey(2));
    }

    [Fact]
    public void NegativeRewardsNeverClaimZeroProbabilityByMultiplyingZeroEvents()
    {
        var matrix = MarkovRewardMatrix.Create(1, 1,
            [Edge(0, 0, new Rational(1, 2), -1), Edge(0, 0, new Rational(1, 2), 1)], _ => { });
        MarkovRewardMatrix row = matrix.Advance(0, 2).Row;
        Assert.Equal(Rational.Zero, First(row));
        Assert.Equal(new Rational(2, 1), Second(row));
        Assert.Null(Assert.Single(row.Row(0)).Value.Zero[0]);
    }

    [Fact]
    public void EarlyCertificatesReturnTheExactPrefixRatherThanTheInfiniteHorizon()
    {
        var matrix = MarkovRewardMatrix.Create(1, 1, [Edge(0, 0, Rational.One, 2)], _ => { });
        (MarkovRewardMatrix row, var completed) = matrix.Advance(0, 100000, (steps, _) => steps >= 7);
        Assert.Equal(7, completed);
        Assert.Equal(new Rational(14, 1), First(row));
    }

    [Fact]
    public void ArithmeticAndCancellationBudgetsAbortPowering()
    {
        var tiny = MarkovRewardMatrix.Create(1, 1,
            [Edge(0, 0, new Rational(1, 2), 0), Edge(0, 0, new Rational(1, 2), 1)], _ => { }, 8);
        Assert.Throws<MarkovMatrixLimitException>(() => tiny.Advance(0, 64));
        using var cancellation = new CancellationTokenSource();
        var cancelled = MarkovRewardMatrix.Create(1, 1, [Edge(0, 0, Rational.One, 1)],
            _ => cancellation.Token.ThrowIfCancellationRequested());
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => cancelled.Advance(0, 100000));
    }
}
