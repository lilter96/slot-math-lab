using System.Numerics;
using SlotMath.Core.Math;

namespace SlotMath.Core.Tests.Math;

public sealed class RewardMomentLayoutTests
{
    [Fact]
    public void ClosureIncludesAllMixedMomentsAndTheirBinomialMultiplicities()
    {
        var layout = RewardMomentLayout.TryCreate(2, 4)!;
        Assert.Equal(15, layout.Monomials.Length);
        Assert.Equal(6, layout.BaseCount);
        Assert.Equal(9, layout.HigherCount);
        var fourth = layout.Find([0, 0, 0, 0]) - layout.BaseCount;
        Assert.Equal(new[] { 1, 4, 6, 4, 1 }, layout.Splits[fourth]
            .OrderBy(split => layout.Monomials[split.Right].Length).Select(split => split.Multiplicity));
        Assert.Equal(16, layout.Splits[layout.Find([0, 0, 1, 1]) - layout.BaseCount].Sum(split => split.Multiplicity));
        Assert.Equal(layout.Find([0, 0, 1]), layout.Find([1, 0, 0]));
        Assert.Null(RewardMomentLayout.TryCreate(32, 4));
        Assert.Null(RewardMomentLayout.TryCreate(2, 2));
        Assert.NotNull(RewardMomentLayout.TryCreate(16, 4));
    }

    [Fact]
    public void MatrixMixedMomentsMatchAnIndependentEnumerationOfAllPaths()
    {
        var layout = RewardMomentLayout.TryCreate(2, 4)!;
        (int From, int To, Rational Probability, int A, int B)[] edges =
        [
            (0, 0, new Rational(1, 3), 1, 0), (0, 1, new Rational(2, 3), 0, 2),
            (1, 0, new Rational(1, 2), 3, 1), (1, 1, new Rational(1, 2), 1, 2)
        ];

        Rational Moment(int[] indices, int a, int b)
        {
            return indices.Aggregate(Rational.One,
                (product, index) => product * (index == 0 ? a : b));
        }

        MarkovRewardTransition Transition((int From, int To, Rational Probability, int A, int B) edge)
        {
            return new MarkovRewardTransition(
                edge.From, edge.To, edge.Probability,
                [edge.Probability * edge.A, edge.Probability * edge.B],
                [
                    edge.Probability * edge.A * edge.A, edge.Probability * edge.A * edge.B,
                    edge.Probability * edge.B * edge.B
                ],
                [edge.A == 0 ? edge.Probability : Rational.Zero, edge.B == 0 ? edge.Probability : Rational.Zero],
                [edge.A, edge.B], [edge.A, edge.B], layout.Monomials.Skip(layout.BaseCount)
                    .Select(indices => edge.Probability * Moment(indices, edge.A, edge.B)).ToArray());
        }

        var matrix = MarkovRewardMatrix.Create(2, 2, edges.Select(Transition), _ => { }, higher: layout);
        MarkovRewardMatrix row = matrix.Advance(0, 6).Row;
        var expected = new Rational[2, layout.Monomials.Length];
        for (var state = 0; state < 2; state++)
        {
            for (var index = 0; index < layout.Monomials.Length; index++)
            {
                expected[state, index] = Rational.Zero;
            }
        }

        void Enumerate(int depth, int state, int a, int b, Rational probability)
        {
            if (depth == 6)
            {
                for (var index = 0; index < layout.Monomials.Length; index++)
                {
                    expected[state, index] += probability * Moment(layout.Monomials[index], a, b);
                }

                return;
            }

            foreach ((int From, int To, Rational Probability, int A, int B) edge in edges.Where(edge =>
                         edge.From == state))
            {
                Enumerate(depth + 1, edge.To, a + edge.A, b + edge.B, probability * edge.Probability);
            }
        }

        Enumerate(0, 0, 0, 0, Rational.One);
        foreach ((var state, MarkovRewardMatrix.Weight weight) in row.Row(0))
        {
            BigInteger[] actual = [weight.Mass, .. weight.First, .. weight.Second, .. weight.Higher];
            for (var index = 0; index < actual.Length; index++)
            {
                Assert.Equal(expected[state, index], new Rational(actual[index], row.Denominator));
            }
        }
    }
}
