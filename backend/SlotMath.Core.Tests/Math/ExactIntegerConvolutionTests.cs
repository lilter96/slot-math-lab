using System.Numerics;
using SlotMath.Core.Math;

namespace SlotMath.Core.Tests.Math;

public sealed class ExactIntegerConvolutionTests
{
    [Fact]
    public void TransformPreflightMatchesActualChargesAtTheExactWorkBoundary()
    {
        BigInteger origin = BigInteger.One << 1023;
        BigInteger[] a = Enumerable.Range(0, 512).Select(i => origin + 11 + 3 * i).ToArray();
        BigInteger[] b = Enumerable.Range(0, 512).Select(i => origin + 23 + 5 * i).ToArray();
        const long allowance = 6995772;
        long used = 0;

        void Spend(long units)
        {
            used = checked(used + units);
            Assert.True(used <= allowance);
        }

        Assert.True(ExactIntegerConvolution.TryTransform(a, b, Spend, out BigInteger[] actual, true, null, allowance));
        Assert.Equal(allowance, used);
        Assert.Equal(Direct(a, b), actual);
        used = 0;
        Assert.False(ExactIntegerConvolution.TryTransform(a, b, Spend, out BigInteger[] withheld, true, null,
            allowance - 1));
        Assert.Empty(withheld);
        Assert.Equal(17408, used);
    }

    [Fact]
    public void ExpensiveTransformCanDeclineBeforeSpendingAStillUsableDirectBudget()
    {
        var a = new BigInteger[4096];
        var b = new BigInteger[4096];
        BigInteger origin = BigInteger.One << 31;
        for (var i = 0; i < 1024; i++)
        {
            a[4 * i] = origin + 11 + 3 * i;
            b[4 * i] = origin + 23 + 5 * i;
        }

        a[1] = origin + 112;
        b[1] = origin + 126;
        Assert.True(ExactIntegerConvolution.TryTransform(a, b, _ => { }, out BigInteger[] expected));
        const long allowance = 1100000;
        long used = 0;

        void Spend(long units)
        {
            used = checked(used + units);
            Assert.True(used <= allowance);
        }

        Assert.False(
            ExactIntegerConvolution.TryTransform(a, b, Spend, out BigInteger[] withheld, true, null, allowance));
        Assert.Empty(withheld);
        var actual = new BigInteger[8191];
        foreach ((BigInteger value, int index) x in a.Select((value, index) => (value, index))
                     .Where(atom => !atom.value.IsZero))
        {
            foreach ((BigInteger value, int index) y in b.Select((value, index) => (value, index))
                         .Where(atom => !atom.value.IsZero))
            {
                Spend(1);
                actual[x.index + y.index] += x.value * y.value;
            }
        }

        Assert.Equal(expected, actual);
        Assert.Equal(1058817, used);
        Assert.Throws<ArgumentOutOfRangeException>(() => ExactIntegerConvolution.TryTransform(a, b, Spend,
            out _, true, null, -1));
    }

    private static BigInteger[] Direct(BigInteger[] a, BigInteger[] b)
    {
        var result = new BigInteger[a.Length + b.Length - 1];
        for (var i = 0; i < a.Length; i++)
        {
            for (var j = 0; j < b.Length; j++)
            {
                result[i + j] += a[i] * b[j];
            }
        }

        return result;
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 3, 31)]
    [InlineData(17, 65, 256)]
    [InlineData(31, 33, 1024)]
    [InlineData(256, 511, 64)]
    [InlineData(512, 1024, 8)]
    public void ModularTransformsReconstructEveryCoefficientAgainstBigIntegerProducts(int n, int m, int bits)
    {
        var random = new System.Random(0x1451);

        BigInteger[] Input(int count)
        {
            return Enumerable.Range(0, count)
                .Select(_ => (BigInteger.One << bits) + random.Next(32768)).ToArray();
        }

        BigInteger[] a = Input(n), b = Input(m);
        BigInteger[] expected = Direct(a, b);
        Assert.True(ExactIntegerConvolution.TryTransform(a, b, _ => { }, out BigInteger[] actual, false));
        Assert.Equal(expected, actual);
        Assert.Equal(a.Aggregate(BigInteger.Add) * b.Aggregate(BigInteger.Add), actual.Aggregate(BigInteger.Add));
    }

    [Fact]
    public void TransformPaddingKeepsLinearEndpointsAndInteriorZeros()
    {
        var a = new BigInteger[257];
        var b = new BigInteger[513];
        a[0] = 7;
        a[^1] = 11;
        b[0] = 13;
        b[^1] = 17;
        Assert.True(ExactIntegerConvolution.TryTransform(a, b, _ => { }, out BigInteger[] actual, false));
        Assert.Equal(Direct(a, b), actual);
        Assert.Equal(new BigInteger(91), actual[0]);
        Assert.Equal(new BigInteger(187), actual[^1]);
    }

    [Fact]
    public void AResultEqualToTheFirstPrimeRequiresAnotherCrtModulus()
    {
        BigInteger prime = ExactIntegerConvolution.Modulus(0);
        Assert.True(ExactIntegerConvolution.TryTransform([prime], [1], _ => { }, out BigInteger[] actual, false));
        Assert.Equal(prime, Assert.Single(actual));
    }

    [Fact]
    public void AnAllOneLawHasTheIndependentTriangularConvolution()
    {
        BigInteger[] values = Enumerable.Repeat(BigInteger.One, 4096).ToArray();
        Assert.True(ExactIntegerConvolution.TryTransform(values, values, _ => { }, out BigInteger[] actual));
        Assert.Equal(8191, actual.Length);
        for (var k = 0; k < actual.Length; k++)
        {
            Assert.Equal(new BigInteger(k < 4096 ? k + 1 : 8191 - k), actual[k]);
        }
    }

    [Fact]
    public void SmallSparseAndUnrepresentableInputsKeepTheCallersFallback()
    {
        Assert.False(ExactIntegerConvolution.TryTransform([1, 2], [3, 4], _ => { }, out _));
        Assert.False(ExactIntegerConvolution.TryTransform([], [1], _ => { }, out _));
        var sparse = new BigInteger[2048];
        sparse[0] = sparse[^1] = 1;
        Assert.False(ExactIntegerConvolution.TryTransform(sparse, sparse, _ => { }, out _));
        BigInteger[] huge = [BigInteger.One << 2000, BigInteger.One << 2000];
        Assert.False(ExactIntegerConvolution.TryTransform(huge, huge, _ => { }, out _, false));
        Assert.True(ExactIntegerConvolution.TryTransform([0, 0], [1, 2], _ => { }, out BigInteger[] zero));
        Assert.Equal(new BigInteger[3], zero);
        Assert.Throws<ArgumentException>(() =>
            ExactIntegerConvolution.TryTransform([1, -1], [2], _ => { }, out _, false));
    }

    [Fact]
    public void EveryModulusIsIndependentlyPrimeAndHasTheRequiredTransformLength()
    {
        var seen = new HashSet<uint>();
        for (var i = 0; i < ExactIntegerConvolution.ModulusCount; i++)
        {
            var prime = ExactIntegerConvolution.Modulus(i);
            Assert.True(seen.Add(prime));
            Assert.True(prime < 1u << 31);
            Assert.Equal(0u, (prime - 1) % ExactIntegerConvolution.MaximumLength);
            var root = ExactIntegerConvolution.TransformRoot(i);
            Assert.Equal(BigInteger.One, BigInteger.ModPow(root, ExactIntegerConvolution.MaximumLength, prime));
            Assert.NotEqual(BigInteger.One, BigInteger.ModPow(root, ExactIntegerConvolution.MaximumLength / 2, prime));
            for (uint divisor = 3; (ulong)divisor * divisor <= prime; divisor += 2)
            {
                Assert.NotEqual(0u, prime % divisor);
            }
        }
    }

    [Fact]
    public void CancellationAndArithmeticBudgetInterruptBeforeReturningPartialCoefficients()
    {
        BigInteger[] values = Enumerable.Repeat(BigInteger.One, 1024).ToArray();
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        Assert.Throws<OperationCanceledException>(() => ExactIntegerConvolution.TryTransform(values, values, _ =>
        {
            if (++calls == 20)
            {
                cancellation.Cancel();
            }

            cancellation.Token.ThrowIfCancellationRequested();
        }, out _));
        Assert.Throws<InvalidOperationException>(() => ExactIntegerConvolution.TryTransform(values, values,
            _ => throw new InvalidOperationException("test arithmetic budget"), out _));
        // A cancelled call returned its pooled scratch; a later operation still produces a complete answer.
        Assert.True(ExactIntegerConvolution.TryTransform(values, values, _ => { }, out BigInteger[] actual));
        Assert.Equal(new BigInteger(1024), actual[1023]);
    }
}
