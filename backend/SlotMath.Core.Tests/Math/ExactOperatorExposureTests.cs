using System.Numerics;
using SlotMath.Core.Math;

namespace SlotMath.Core.Tests.Math;

public sealed class ExactOperatorExposureTests
{
    private static Dist<BigInteger> Law(params (int Payout, int Weight)[] atoms)
    {
        var builder = new DistBuilder<BigInteger>();
        var denominator = atoms.Sum(atom => atom.Weight);
        foreach ((int Payout, int Weight) atom in atoms)
        {
            builder.Add(atom.Payout, atom.Weight, denominator);
        }

        return builder.Build();
    }

    [Fact]
    public void IndependentFairRoundsHaveTheBinomialLossLawAndSignedMoments()
    {
        ExactExposureResult result = ExactOperatorExposure.Independent(Law((0, 1), (2, 1)), 1, 1, 3, 0);
        Assert.Equal(new[] { -3, -1, 1, 3 }.Select(value => new Rational(value, 1)),
            result.FinalLoss.Atoms.Select(atom => atom.Loss));
        Assert.Equal(new[] { 1, 3, 3, 1 }.Select(value => new Rational(value, 8)),
            result.FinalLoss.Atoms.Select(atom => atom.Probability));
        Assert.Equal(Rational.Zero, result.FinalLoss.Mean);
        Assert.Equal(new Rational(3, 1), result.FinalLoss.Variance);
        Assert.Equal(Rational.Zero, result.FinalLoss.ThirdMoment);
        Assert.Equal(new Rational(21, 1), result.FinalLoss.FourthMoment);
        Assert.Equal(new Rational(1, 2), result.FinalLoss.Cdf(0));
        Assert.Equal(new Rational(1, 2), result.FinalLoss.SurvivalInclusive(1));
        Assert.Equal(new Rational(1, 1), result.FinalLoss.Quantile(new Rational(3, 4)));
        Assert.Equal(new Rational(2, 1), result.FinalLoss.ExpectedShortfall(new Rational(3, 4)));
        Assert.Equal("Exact", result.Ruin!.Status);
        Assert.Equal(new Rational(5, 8), result.Ruin.Probability);
        Assert.Equal(new Rational(3, 8), result.Ruin.SurvivalProbability);
        Assert.Equal(new Rational(7, 5), result.Ruin.ConditionalExpectedRound);
        Assert.Equal(new[] { new FirstRuinPassage(1, new Rational(1, 2)), new FirstRuinPassage(3, new Rational(1, 8)) },
            result.Ruin.FirstPassages);
        Assert.NotEqual(Rational.One - result.FinalLoss.Cdf(0), result.Ruin.Probability);
    }

    [Fact]
    public void SolvencyIncludesZeroReserveAndRuinMayBeFollowedByRecovery()
    {
        var model = new OperatorExposureModel(2,
        [
            new ExposureTransition(0, 1, new Rational(1, 2), 3, 1),
            new ExposureTransition(0, 0, new Rational(1, 2), 0, 1),
            new ExposureTransition(1, 0, 1, 0, 2)
        ]);
        ExactExposureResult result = ExactOperatorExposure.Markov(model, 2, 1);
        Assert.Equal(
            new[]
            {
                new ExposureLawAtom(-2, new Rational(1, 4)), new ExposureLawAtom(0, new Rational(1, 2)),
                new ExposureLawAtom(1, new Rational(1, 4))
            }, result.FinalLoss.Atoms);
        Assert.Equal(new Rational(-1, 4), result.FinalLoss.Mean);
        Assert.Equal(new Rational(19, 16), result.FinalLoss.Variance);
        Assert.Equal(new Rational(-7, 4), result.FinalLoss.ThirdMoment);
        Assert.Equal(new Rational(17, 4), result.FinalLoss.FourthMoment);
        Assert.Equal(Rational.One, result.FinalLoss.Cdf(1));
        Assert.Equal(new Rational(1, 2), result.Ruin!.Probability);
        Assert.Equal(new FirstRuinPassage(1, new Rational(1, 2)), Assert.Single(result.Ruin.FirstPassages));
        Assert.DoesNotContain(result.FinalLoss.Atoms,
            atom => atom.Loss == 4); // An IID product of the initial marginal invents this outcome.
        ExactExposureResult withUnreachable = ExactOperatorExposure.Markov(model with
        {
            States = 3,
            Transitions = [.. model.Transitions, new ExposureTransition(2, 2, 1, 999, 0)]
        }, 2, 1);
        Assert.Equal(2, withUnreachable.Diagnostics.ReachableStates);
        Assert.Equal(result.FinalLoss.Atoms, withUnreachable.FinalLoss.Atoms);
        ExactExposureResult equality = ExactOperatorExposure.Independent(Law((0, 1), (2, 1)), 1, 1, 1, 1);
        Assert.Equal(Rational.Zero, equality.Ruin!.Probability);
        ExactExposureResult two = ExactOperatorExposure.Independent(Law((0, 1), (2, 1)), 1, 1, 2, 1);
        Assert.Equal(new Rational(1, 4), two.Ruin!.Probability);
        Assert.Equal(2, Assert.Single(two.Ruin.FirstPassages).Round);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void DependentFractionalCostsAndRewardsMatchEnumerationOfEveryPath(int horizon)
    {
        var model = new OperatorExposureModel(2,
        [
            new ExposureTransition(0, 0, new Rational(1, 3), new Rational(1, 2), new Rational(1, 3)),
            new ExposureTransition(0, 1, new Rational(2, 3), 0, new Rational(1, 5)),
            new ExposureTransition(1, 0, new Rational(1, 2), 2, new Rational(1, 3)),
            new ExposureTransition(1, 1, new Rational(1, 2), new Rational(1, 2), 0)
        ], 1);
        var reserve = new Rational(4, 5);
        var terminal = new Dictionary<Rational, Rational>();
        var hits = new Dictionary<long, Rational>();

        void Enumerate(int depth, int state, Rational loss, Rational probability, long? first)
        {
            if (depth == horizon)
            {
                terminal[loss] = (terminal.TryGetValue(loss, out Rational mass) ? mass : Rational.Zero) + probability;
                if (first is { } at)
                {
                    hits[at] = (hits.TryGetValue(at, out Rational hit) ? hit : Rational.Zero) + probability;
                }

                return;
            }

            foreach (ExposureTransition edge in model.Transitions.Where(edge => edge.From == state))
            {
                Rational next = loss + edge.Payout - edge.Wager;
                Enumerate(depth + 1, edge.To, next, probability * edge.Probability,
                    first ?? (next > reserve ? depth + 1 : null));
            }
        }

        Enumerate(0, model.InitialState, Rational.Zero, Rational.One, null);
        ExactExposureResult actual = ExactOperatorExposure.Markov(model, horizon, reserve);
        Assert.Equal(terminal.OrderBy(pair => pair.Key).Select(pair => new ExposureLawAtom(pair.Key, pair.Value)),
            actual.FinalLoss.Atoms);
        Assert.Equal(hits.OrderBy(pair => pair.Key).Select(pair => new FirstRuinPassage(pair.Key, pair.Value)),
            actual.Ruin!.FirstPassages);
        Rational ruin = hits.Values.Aggregate(Rational.Zero, (sum, value) => sum + value);
        Assert.Equal(ruin, actual.Ruin.Probability);
        Assert.Equal(Rational.One - ruin, actual.Ruin.SurvivalProbability);
        Assert.Equal(terminal.Aggregate(Rational.Zero, (sum, pair) => sum + pair.Key * pair.Value),
            actual.FinalLoss.Mean);
    }

    [Fact]
    public void DenseIndependentAggregationUsesExactTransformsWithTheSameCompleteLaw()
    {
        var builder = new DistBuilder<BigInteger>();
        const int count = 1024;
        var denominator = Enumerable.Range(0, count).Sum(index => 1 + index % 7);
        for (var index = 0; index < count; index++)
        {
            builder.Add(index, 1 + index % 7, denominator);
        }

        Dist<BigInteger> law = builder.Build();
        ExactExposureResult direct = ExactOperatorExposure.Independent(law, 3, new Rational(1, 3), 2,
            options: new ExactExposureOptions
            {
                UseTransformConvolution = false
            });
        ExactExposureResult transform = ExactOperatorExposure.Independent(law, 3, new Rational(1, 3), 2);
        Assert.True(transform.Diagnostics.TransformConvolutions > 0);
        Assert.Equal(direct.FinalLoss.Atoms, transform.FinalLoss.Atoms);
        Assert.Equal(direct.FinalLoss.FourthMoment, transform.FinalLoss.FourthMoment);
        Assert.True(transform.Diagnostics.DistributionWork < direct.Diagnostics.DistributionWork);
    }

    [Fact]
    public void HugeSparseGapsDoNotForceADenseAllocation()
    {
        ExactExposureResult result = ExactOperatorExposure.Independent(Law((0, 1), (1, 2), (1000000, 1)), 1, 2, 2);
        Assert.Equal(0, result.Diagnostics.TransformConvolutions);
        Assert.Equal(new Rational(500001, 1) - 4, result.FinalLoss.Mean);
        Assert.Equal(6, result.FinalLoss.Atoms.Count);
    }

    [Fact]
    public void AbsorbingStatesDoNotKeepChargingWagersAfterSettlementEnds()
    {
        const int horizon = 5;
        var model = new OperatorExposureModel(3,
        [
            new ExposureTransition(0, 0, new Rational(1, 2), 0, 1),
            new ExposureTransition(0, 1, new Rational(1, 2), 3, 1),
            new ExposureTransition(1, 2, 1, 0, 0), new ExposureTransition(2, 2, 1, 0, 0)
        ]);
        var expected = new Dictionary<Rational, Rational>
        {
            [new Rational(-horizon, 1)] = new(1, 1 << horizon)
        };
        // First settlement success at k: payout 3, exactly k wagers, then zero-cost absorption.
        for (var k = 1; k <= horizon; k++)
        {
            expected.Add(new Rational(3 - k, 1), new Rational(1, 1 << k));
        }

        ExactExposureResult result = ExactOperatorExposure.Markov(model, horizon, 0);
        Assert.Equal(expected.OrderBy(pair => pair.Key).Select(pair => new ExposureLawAtom(pair.Key, pair.Value)),
            result.FinalLoss.Atoms);
        Assert.Equal(new Rational(3, 4), result.Ruin!.Probability);
    }

    [Fact]
    public void ZeroHorizonAndHugeDeterministicHorizonsPreserveUnitsAndLimits()
    {
        const long rounds = 1000000000000;
        Dist<BigInteger> law = Law((2, 1));
        ExactExposureResult result = ExactOperatorExposure.Independent(law, 3, new Rational(1, 3), rounds, 0);
        Assert.Equal(new Rational(rounds, 3), result.FinalLoss.Mean);
        Assert.Equal(Rational.Zero, result.FinalLoss.Variance);
        Assert.Equal(Rational.One, result.Ruin!.Probability);
        Assert.Equal(new FirstRuinPassage(1, Rational.One), Assert.Single(result.Ruin.FirstPassages));
        Assert.True(result.Diagnostics.DistributionWork < 20000);
        ExactExposureResult zero = ExactOperatorExposure.Independent(law, 3, new Rational(1, 3), 0, 0);
        Assert.Equal(new ExposureLawAtom(Rational.Zero, Rational.One), Assert.Single(zero.FinalLoss.Atoms));
        Assert.Equal(Rational.Zero, zero.Ruin!.Probability);
        Assert.Null(zero.Ruin.ConditionalExpectedRound);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void OptionalRuinExhaustionDoesNotDiscardTheExactTerminalLaw(long ruinWork)
    {
        var options = new ExactExposureOptions
        {
            RuinWork = ruinWork
        };
        ExactExposureResult result = ExactOperatorExposure.Independent(Law((0, 1), (2, 1)), 1, 1, 3, 0, options);
        Assert.Equal("Unavailable", result.Ruin!.Status);
        Assert.Null(result.Ruin.Probability);
        Assert.Empty(result.Ruin.FirstPassages);
        Assert.Equal(4, result.FinalLoss.Atoms.Count);
        // A uniform bound on every prefix can prove no ruin without a first-passage table.
        ExactExposureResult bounded = ExactOperatorExposure.Independent(Law((0, 1), (2, 1)), 1, 1, 3, 3, options);
        Assert.Equal("Exact", bounded.Ruin!.Status);
        Assert.Equal(Rational.Zero, bounded.Ruin.Probability);
        ExactExposureResult unspecified = ExactOperatorExposure.Independent(Law((0, 1), (2, 1)), 1, 1, 3);
        Assert.Null(unspecified.Ruin);
    }

    [Theory]
    [InlineData(1, 100, 65536)]
    [InlineData(40000000, 3, 65536)]
    [InlineData(40000000, 100, 4)]
    public void ResourceFailureCannotReturnARenormalizedOrPartialLaw(long work, int atoms, int bits)
    {
        Assert.Throws<ExactExposureLimitException>(() => ExactOperatorExposure.Independent(Law((0, 1), (2, 1)), 1, 1,
            20,
            options: new ExactExposureOptions
            {
                MaxWork = work,
                MaxAtoms = atoms,
                MaximumBits = bits
            }));
    }

    [Fact]
    public void IncompleteMalformedOrInvalidModelsAndCancellationAreRejected()
    {
        var incomplete = new DistBuilder<BigInteger>();
        incomplete.Add(0, 1, 2);
        Assert.Throws<ArgumentException>(() => ExactOperatorExposure.Independent(incomplete.Build(), 1, 1, 3));
        var pruned = new DistBuilder<BigInteger>();
        pruned.Add(0, 1, 2);
        pruned.AddPrunedMass(1, 2);
        Assert.Throws<ArgumentException>(() => ExactOperatorExposure.Independent(pruned.Build(), 1, 1, 3));
        var malformed = new Dist<BigInteger>([new Dist<BigInteger>.Entry(0, 1)], 2, 2, 0, 1);
        Assert.Throws<ArgumentException>(() => ExactOperatorExposure.Independent(malformed, 1, 1, 3));
        Dist<BigInteger> law = Law((0, 1), (2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExactOperatorExposure.Independent(law, 0, 1, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExactOperatorExposure.Independent(law, 1, 0, 3));
        Assert.Throws<ArgumentException>(() => ExactOperatorExposure.Independent(law, 1, default, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExactOperatorExposure.Independent(law, 1, 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExactOperatorExposure.Independent(law, 1, 1, 3, -1));
        Assert.Throws<ArgumentException>(() =>
            ExactOperatorExposure.Markov(
                new OperatorExposureModel(1, [new ExposureTransition(0, 0, new Rational(1, 2), 1, 1)]), 3));
        Assert.Throws<ArgumentException>(() =>
            ExactOperatorExposure.Markov(new OperatorExposureModel(1, [new ExposureTransition(0, 2, 1, 1, 1)]), 3));
        Assert.Throws<ArgumentException>(() =>
            ExactOperatorExposure.Markov(new OperatorExposureModel(1, [new ExposureTransition(0, 0, 1, -1, 1)]), 3));
        Assert.Throws<OperationCanceledException>(() =>
            ExactOperatorExposure.Independent(law, 1, 1, 3, cancellation: new CancellationToken(true)));
    }

    [Fact]
    public void SignedLawOwnsItsSnapshotAndReportsNegativeProfitQuantiles()
    {
        Dist<BigInteger> law = Law((-3, 1), (-1, 1));
        var analysis = ExactExposureLaw.Create(law, 2);
        Assert.Equal(new Rational(-1, 1), analysis.Mean);
        Assert.Equal(new Rational(5, 4), analysis.SecondMoment);
        Assert.Equal(new Rational(-7, 4), analysis.ThirdMoment);
        Assert.Equal(new Rational(41, 16), analysis.FourthMoment);
        Assert.Equal(new Rational(-1, 2), analysis.Quantile(new Rational(9, 10)));
        Assert.Equal(new Rational(-1, 2), analysis.ExpectedShortfall(new Rational(9, 10)));
        ((Dist<BigInteger>.Entry[])law.Entries)[0] = new Dist<BigInteger>.Entry(999, 1);
        Assert.Equal(new Rational(-1, 1), analysis.Mean);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ExposureLawAtom>)analysis.Atoms)[0] = new ExposureLawAtom(999, 1));
        Assert.Throws<ArgumentException>(() => analysis.Cdf(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => analysis.Quantile(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => analysis.ExpectedShortfall(1));
    }
}
