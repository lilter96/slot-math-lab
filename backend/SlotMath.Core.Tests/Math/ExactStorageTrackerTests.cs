using System.Numerics;
using SlotMath.Core.Math;

namespace SlotMath.Core.Tests.Math;

public sealed class ExactStorageTrackerTests
{
    private static ExactStorageCounter Count(ExactStorageTracker tracker, ExactStorageKind kind) =>
        tracker.Snapshot().Counters.Single(counter => counter.Kind == kind);

    [Fact]
    public void OwnershipScopesDeduplicateAliasesAndKeepPeaksAcrossRetries()
    {
        var tracker = new ExactStorageTracker();
        using (tracker.Scope())
        {
            var shared = new BigInteger[7];
            tracker.RetainArray(ExactStorageKind.RandomWeightSlots, shared);
            tracker.RetainArray(ExactStorageKind.RandomWeightSlots, shared);
            Assert.Equal(7, Count(tracker, ExactStorageKind.RandomWeightSlots).Current);
            using (tracker.Scope())
            {
                tracker.Add(ExactStorageKind.TermObjects, 20);
                using ExactStorageTracker.Lease candidate = tracker.Hold(ExactStorageKind.CachedLawAtoms, 3);
                candidate.Keep();
            }

            Assert.Equal(0, Count(tracker, ExactStorageKind.TermObjects).Current);
            Assert.Equal(20, Count(tracker, ExactStorageKind.TermObjects).Peak);
            Assert.Equal(7, Count(tracker, ExactStorageKind.RandomWeightSlots).Current);
        }

        Assert.All(tracker.Snapshot().Counters, counter => Assert.Equal(0, counter.Current));
        using (tracker.Scope())
        {
            tracker.Add(ExactStorageKind.TermObjects, 5);
        }

        Assert.Equal(20, Count(tracker, ExactStorageKind.TermObjects).Peak);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ExactStorageCounter>)tracker.Snapshot().Counters)[0] = null!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransformBuffersAreReleasedOnSuccessAndOnCancellation(bool cancel)
    {
        var tracker = new ExactStorageTracker();
        BigInteger[] input = Enumerable.Repeat(BigInteger.One, 128).ToArray();

        void Spend(long work)
        {
            if (cancel && Count(tracker, ExactStorageKind.TransformResidueCapacity).Current > 0)
            {
                throw new OperationCanceledException();
            }
        }

        if (cancel)
        {
            Assert.Throws<OperationCanceledException>(() => ExactIntegerConvolution.TryTransform(input, input,
                Spend, out _, false, tracker));
        }
        else
        {
            Assert.True(ExactIntegerConvolution.TryTransform(input, input, Spend, out BigInteger[] result, false,
                tracker));
            Assert.Equal(255, result.Length);
            Assert.Equal(new BigInteger(128), result[127]);
        }

        Assert.All(tracker.Snapshot().Counters, counter => Assert.Equal(0, counter.Current));
        Assert.True(Count(tracker, ExactStorageKind.TransformResidueCapacity).Peak >= 512);
        Assert.Equal(255, Count(tracker, ExactStorageKind.ConvolutionResultSlots).Peak);
    }

    [Fact]
    public void MatrixPowerOwnershipReleasesOldRowsAndSharedPowerAliases()
    {
        var tracker = new ExactStorageTracker();

        MarkovRewardTransition Edge(int from, int to, Rational probability)
        {
            return new MarkovRewardTransition(from, to, probability, [], [], [], [], []);
        }

        using (var matrix = MarkovRewardMatrix.Create(2, 0,
                   [Edge(0, 0, new Rational(1, 2)), Edge(0, 1, new Rational(1, 2)), Edge(1, 1, 1)], _ => { },
                   storage: tracker))
        {
            Assert.Equal(4, Count(tracker, ExactStorageKind.MatrixCoefficientSlots).Current);
            (MarkovRewardMatrix row, var steps) = matrix.Advance(0, 1024);
            Assert.Equal(1024, steps);
            Assert.Equal(7, Count(tracker, ExactStorageKind.MatrixCoefficientSlots).Current);
            Assert.True(Count(tracker, ExactStorageKind.MatrixCoefficientSlots).Peak < 100);
            Assert.True(Count(tracker, ExactStorageKind.MatrixCoefficientSlots).Peak > 10);
            row.Dispose();
            Assert.Equal(4, Count(tracker, ExactStorageKind.MatrixCoefficientSlots).Current);
        }

        Assert.Equal(0, Count(tracker, ExactStorageKind.MatrixCoefficientSlots).Current);
    }
}
