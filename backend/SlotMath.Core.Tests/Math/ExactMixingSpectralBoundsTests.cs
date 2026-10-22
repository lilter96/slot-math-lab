using System.Numerics;
using SlotMath.Core.Math;

namespace SlotMath.Core.Tests.Math;

public sealed class ExactMixingSpectralBoundsTests
{
    private static ExactSparseMatrix Matrix(params Rational[][] rows) => ExactSparseMatrix.Create(rows.Length,
        rows.SelectMany((row, from) => row.Select((value, to) => new ExactSparseEntry(from, to, value))).ToArray());

    [Fact]
    public void OneStepBoundsRecoverPositiveAndNegativeTwoStateEigenvalueMagnitudes()
    {
        ExactSparseMatrix positive = Matrix([new Rational(3, 4), new Rational(1, 4)],
            [new Rational(1, 4), new Rational(3, 4)]);
        ExactSparseMatrix negative = Matrix([0, 1], [new Rational(1, 3), new Rational(2, 3)]);
        foreach ((ExactSparseMatrix kernel, Rational magnitude) in new[]
                 {
                     (positive, new Rational(1, 2)), (negative, new Rational(1, 3))
                 })
        {
            var certificate = ExactMixingCertificate.Create(kernel, 1);
            ExactMixingSpectralBounds spectrum = certificate.BoundSpectrum(1);
            Assert.Same(certificate, spectrum.Certificate);
            Assert.Equal(magnitude, spectrum.ContractionRootLower);
            Assert.Equal(magnitude, spectrum.ContractionRootUpper);
            Assert.Equal(Rational.One - magnitude, spectrum.AbsoluteGapLowerBound);
            Assert.True(spectrum.ProvesPositiveGap);
            Assert.True(spectrum.Work > 0);
        }
    }

    [Fact]
    public void NonreversibleComplexEigenvaluesHaveAUsefulTwoStepCertificate()
    {
        // (I + cyclic permutation)/2 has eigenvalues 1 and (1 +/- i*sqrt(3))/4, each of magnitude 1/2.
        ExactSparseMatrix kernel = Matrix([new Rational(1, 2), new Rational(1, 2), 0],
            [0, new Rational(1, 2), new Rational(1, 2)], [new Rational(1, 2), 0, new Rational(1, 2)]);
        Assert.False(ExactMixingCertificate.Create(kernel, 1).BoundSpectrum().ProvesPositiveGap);
        ExactMixingSpectralBounds exact = ExactMixingCertificate.Create(kernel, 2).BoundSpectrum();
        Assert.Equal(new Rational(1, 4), exact.Certificate.Factor);
        Assert.Equal(new Rational(1, 2), exact.ContractionRootLower);
        Assert.Equal(new Rational(1, 2), exact.ContractionRootUpper);
        Assert.Equal(new Rational(1, 2), exact.AbsoluteGapLowerBound);

        // A different block gives only an upper bound on the true spectral radius. Its root lower
        // endpoint must not be advertised as a lower bound on an actual eigenvalue.
        ExactMixingSpectralBounds bounded = ExactMixingCertificate.Create(kernel, 3).BoundSpectrum(8);
        Assert.Equal(new Rational(1, 4), bounded.Certificate.Factor);
        Assert.Equal(new Rational(161, 256), bounded.ContractionRootLower);
        Assert.Equal(new Rational(81, 128), bounded.ContractionRootUpper);
        Assert.Equal(new Rational(47, 128), bounded.AbsoluteGapLowerBound);
        Assert.True(bounded.ContractionRootLower > new Rational(1, 2));
        Assert.True(bounded.ContractionRootLower * bounded.ContractionRootLower * bounded.ContractionRootLower <
                    bounded.Certificate.Factor);
        Assert.True(bounded.ContractionRootUpper * bounded.ContractionRootUpper * bounded.ContractionRootUpper >
                    bounded.Certificate.Factor);
    }

    [Fact]
    public void AUnitSpectralGapDoesNotEraseNonnormalTransientSteps()
    {
        // Eigenvalues 1,0,0; the zero-mass restriction has a nontrivial nilpotent Jordan block.
        ExactSparseMatrix kernel = Matrix([0, 1, 0], [0, 0, 1], [0, 0, 1]);
        var certificate = ExactMixingCertificate.Create(kernel, 2);
        ExactMixingSpectralBounds spectrum = certificate.BoundSpectrum();
        Assert.Equal(Rational.Zero, spectrum.ContractionRootUpper);
        Assert.Equal(Rational.One, spectrum.AbsoluteGapLowerBound);
        Assert.Equal(Rational.One, certificate.UpperDistance(1));
        Assert.Equal(Rational.Zero, certificate.UpperDistance(2));
        Assert.Equal(2L, certificate.FindSteps(new Rational(1, 100)));
    }

    [Fact]
    public void PeriodicAndMultipleStationaryModesRetainOnlyTheVacuousGapBound()
    {
        foreach (ExactSparseMatrix kernel in new[] { Matrix([0, 1], [1, 0]), ExactSparseMatrix.Identity(2) })
        {
            foreach (var block in new[] { 1, 2, 3 })
            {
                ExactMixingSpectralBounds spectrum = ExactMixingCertificate.Create(kernel, block).BoundSpectrum();
                Assert.Equal(Rational.One, spectrum.ContractionRootLower);
                Assert.Equal(Rational.One, spectrum.ContractionRootUpper);
                Assert.Equal(Rational.Zero, spectrum.AbsoluteGapLowerBound);
                Assert.False(spectrum.ProvesPositiveGap);
            }
        }

        ExactMixingSpectralBounds singleton = ExactMixingCertificate.Create(ExactSparseMatrix.Identity(1), 7)
            .BoundSpectrum();
        Assert.Equal(Rational.Zero, singleton.ContractionRootUpper);
        Assert.Equal(Rational.One, singleton.AbsoluteGapLowerBound);
    }

    [Fact]
    public void CoarsePrecisionCanBeInconclusiveForAChainWithAKnownPositiveGap()
    {
        var epsilon = new Rational(1, BigInteger.One << 40);
        var certificate = ExactMixingCertificate.Create(Matrix([1, 0], [epsilon, Rational.One - epsilon]), 2);
        Assert.True(certificate.Contracts);
        ExactMixingSpectralBounds coarse = certificate.BoundSpectrum(8);
        Assert.Equal(Rational.One, coarse.ContractionRootUpper);
        Assert.Equal(Rational.Zero, coarse.AbsoluteGapLowerBound);
        Assert.False(coarse.ProvesPositiveGap);
        ExactMixingSpectralBounds fine = certificate.BoundSpectrum(48);
        Assert.Equal(Rational.One - epsilon, fine.ContractionRootLower);
        Assert.Equal(Rational.One - epsilon, fine.ContractionRootUpper);
        Assert.Equal(epsilon, fine.AbsoluteGapLowerBound);
    }

    [Fact]
    public void PinnedTwoStateCharacteristicRootsMatchCertifiedBrackets()
    {
        var random = new System.Random(8273);
        for (var sample = 0; sample < 24; sample++)
        {
            var p = new Rational(random.Next(1, 20), 20);
            var q = new Rational(random.Next(1, 20), 20);
            Rational difference = p - q;
            Rational magnitude = difference.Sign < 0 ? -difference : difference;
            ExactSparseMatrix kernel = Matrix([p, Rational.One - p], [q, Rational.One - q]);
            foreach (var block in new[] { 1, 2, 3, 5 })
            {
                ExactMixingSpectralBounds spectrum = ExactMixingCertificate.Create(kernel, block).BoundSpectrum(8);
                Assert.Equal(new Rational(BigInteger.Pow(magnitude.Numerator, block),
                    BigInteger.Pow(magnitude.Denominator, block)), spectrum.Certificate.Factor);
                Assert.True(spectrum.ContractionRootLower <= magnitude);
                Assert.True(magnitude <= spectrum.ContractionRootUpper);
                Assert.True(spectrum.ContractionRootUpper - spectrum.ContractionRootLower <= new Rational(1, 256));
                Assert.True(spectrum.AbsoluteGapLowerBound <= Rational.One - magnitude);
            }
        }
    }

    [Fact]
    public void ScalarRootEnclosuresRespectPrecisionArithmeticAndCancellationLimits()
    {
        ExactSparseMatrix kernel = Matrix([new Rational(1, 2), new Rational(1, 2), 0],
            [0, new Rational(1, 2), new Rational(1, 2)], [new Rational(1, 2), 0, new Rational(1, 2)]);
        var certificate = ExactMixingCertificate.Create(kernel, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => certificate.BoundSpectrum(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => certificate.BoundSpectrum(1025));
        Assert.Throws<ExactLinearLimitException>(() => certificate.BoundSpectrum(options: new ExactLinearOptions
        {
            MaxWork = 1
        }));
        Assert.Throws<ExactLinearLimitException>(() => certificate.BoundSpectrum(8, new ExactLinearOptions
        {
            MaximumBits = 4
        }));
        Assert.Throws<OperationCanceledException>(() => certificate.BoundSpectrum(cancellation:
            new CancellationToken(true)));
        Assert.True(certificate.BoundSpectrum(8).ProvesPositiveGap);
    }
}
