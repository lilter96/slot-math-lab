using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Random;

public class ExactWeightVectorTests
{
    private static ExprValue Vector(params int[] values) =>
        ExprValue.Array(values.Select(value => ExprValue.Number(value)).ToArray());

    [Fact]
    public void RationalMassesKeepExactDenominatorOrderZerosAndRngWords()
    {
        ExprValue vector = ExprValue.Array([
            ExprValue.Rational(1, 2), ExprValue.Rational(1, 3), ExprValue.Number(0), ExprValue.Rational(5, 6)
        ]);
        var cache = new ExactWeightVectorCache();
        WeightSet weights = cache.Get(vector, 4);
        Assert.Equal(new BigInteger(6), weights.Denominator);
        Assert.Equal(new BigInteger[] { 3, 2, 0, 5 }, weights.Numerators);
        Assert.Equal(new BigInteger(10), weights.NumeratorSum);
        WeightSet oracle = WeightSet.FromRationals(new BigInteger[] { 3, 2, 0, 5 }, 6);
        for (var seed = -32; seed < 64; seed++)
        {
            var actual = new SeededRandom(seed);
            var expected = new SeededRandom(seed);
            for (var draw = 0; draw < 16; draw++)
            {
                Assert.Equal(oracle.AliasTable.Sample(expected), weights.AliasTable.Sample(actual));
            }

            Assert.Equal(expected.NextUInt64(), actual.NextUInt64());
        }
    }

    [Theory]
    [InlineData("scalar", "EVAL_TYPE_ERROR")]
    [InlineData("empty", "EVAL_WEIGHT_LENGTH")]
    [InlineData("length", "EVAL_WEIGHT_LENGTH")]
    [InlineData("negative", "EVAL_WEIGHT_NEGATIVE")]
    [InlineData("zero", "EVAL_WEIGHT_ZERO")]
    [InlineData("text", "EVAL_TYPE_ERROR")]
    [InlineData("boolean", "EVAL_TYPE_ERROR")]
    public void InvalidVectorsHaveCodedErrorsAndNeverEnterTheCache(string kind, string code)
    {
        ExprValue value = kind switch
        {
            "scalar" => ExprValue.Number(1), "empty" => Vector(), "length" => Vector(1),
            "negative" => Vector(-1, 2), "zero" => Vector(0, 0),
            "text" => ExprValue.Array([ExprValue.Number(1), ExprValue.String("2")]),
            _ => ExprValue.Array([ExprValue.Number(1), ExprValue.Bool(true)])
        };
        var cache = new ExactWeightVectorCache();
        var error = Assert.Throws<ExpressionEvaluationException>(() => cache.Get(value, 2));
        Assert.Equal(code, error.Code);
        Assert.StartsWith("draw.weights", error.Location);
        Assert.Equal(0, cache.Count);
    }

    [Theory]
    [InlineData("length")]
    [InlineData("item")]
    [InlineData("input")]
    [InlineData("denominator")]
    [InlineData("coefficients")]
    public void IndependentResourceLimitsRejectBeforeUnboundedExpansion(string kind)
    {
        ExprValue[] items = kind switch
        {
            "length" => Enumerable.Repeat(ExprValue.Number(1), ExactWeightVector.MaxLength + 1).ToArray(),
            "item" => [ExprValue.Number(BigInteger.One << ExactWeightVector.MaxItemBits)],
            "input" => Enumerable.Repeat(ExprValue.Number(BigInteger.One << 1023), 65).ToArray(),
            "denominator" => Enumerable.Range(4090, 5)
                .Select(bits => ExprValue.Rational(1, (BigInteger.One << bits) - 1)).ToArray(),
            _ => [ExprValue.Rational(1, (BigInteger.One << 1000) - 1), .. Enumerable.Repeat(ExprValue.Number(1), 299)]
        };
        var cache = new ExactWeightVectorCache();
        var error = Assert.Throws<ExpressionEvaluationException>(() => cache.Get(ExprValue.Array(items), items.Length));
        Assert.Equal("EVAL_WEIGHT_BUDGET", error.Code);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void RetainedKeysOwnValuesWhenAnInputArrayIsReusedAndChanged()
    {
        ExprValue[] items = [ExprValue.Number(1), ExprValue.Number(2)];
        ExprValue borrowed = ExprValue.Array(items);
        var cache = new ExactWeightVectorCache();
        WeightSet first = cache.Get(borrowed, 2);
        items[0] = ExprValue.Number(9);
        WeightSet changed = cache.Get(borrowed, 2);
        Assert.NotSame(first, changed);
        Assert.Equal(new BigInteger[] { 1, 2 }, first.Numerators);
        Assert.Equal(new BigInteger[] { 9, 2 }, changed.Numerators);
        Assert.Same(first, cache.Get(Vector(1, 2), 2));
    }

    [Fact]
    public void EntryPositionAndNumericRetentionAreBoundedWithoutRejectingValidWeights()
    {
        var cache = new ExactWeightVectorCache(2, 4, 128);
        WeightSet old = cache.Get(Vector(1, 2), 2);
        for (var value = 2; value < 100; value++)
        {
            WeightSet weights = cache.Get(Vector(value, value + 1), 2);
            Assert.Equal(new BigInteger(value), weights[0]);
            Assert.InRange(cache.Count, 0, 2);
            Assert.InRange(cache.RetainedPositions, 0, 4);
            Assert.InRange(cache.RetainedNumericBits, 0, 128);
        }

        Assert.NotSame(old, cache.Get(Vector(1, 2), 2));
        var uncached = new ExactWeightVectorCache(2, 1, 4);
        Assert.Equal(new BigInteger(5), uncached.Get(Vector(2, 3), 2).NumeratorSum);
        Assert.Equal(0, uncached.Count);
    }

    [Fact]
    public void ConcurrentLookupsAndCancellationPreserveOwnedCacheState()
    {
        var cache = new ExactWeightVectorCache();
        Parallel.For(0, 512, index =>
        {
            var value = index % 7 + 1;
            WeightSet weights = cache.Get(Vector(value, 8 - value), 2);
            Assert.Equal(new BigInteger(8), weights.NumeratorSum);
            Assert.Equal(new BigInteger(value), weights[0]);
        });
        Assert.Equal(7, cache.Count);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => cache.Get(Vector(1, 7), 2, cancellation.Token));
        Assert.Equal(7, cache.Count);
    }
}
