using System.Numerics;
using SlotMath.Core.Math;

namespace SlotMath.Core.Tests.Math;

public sealed class ExactChainCertificatesTests
{
    private static ExactSparseMatrix Matrix(params Rational[][] rows)
    {
        var entries = new List<ExactSparseEntry>();
        for (var from = 0; from < rows.Length; from++)
        {
            Assert.Equal(rows.Length, rows[from].Length);
            for (var to = 0; to < rows.Length; to++)
            {
                entries.Add(new ExactSparseEntry(from, to, rows[from][to]));
            }
        }

        return ExactSparseMatrix.Create(rows.Length, entries);
    }

    private static Rational PathProbability(ExactSparseMatrix matrix, int from, int to, int steps)
    {
        if (steps == 0)
        {
            return from == to ? Rational.One : Rational.Zero;
        }

        Rational result = Rational.Zero;
        for (var via = 0; via < matrix.States; via++)
        {
            result += matrix[from, via] * PathProbability(matrix, via, to, steps - 1);
        }

        return result;
    }

    private static Rational Distance(IReadOnlyList<Rational> a, IReadOnlyList<Rational> b)
    {
        Rational result = Rational.Zero;
        for (var index = 0; index < a.Count; index++)
        {
            Rational difference = a[index] - b[index];
            result += difference.Sign < 0 ? -difference : difference;
        }

        return result / 2;
    }

    [Fact]
    public void ReachabilitySeparatesTransientCyclesAndUnreachableClosedClasses()
    {
        ExactSparseMatrix matrix = Matrix([0, new Rational(1, 2), new Rational(1, 2), 0, 0],
            [1, 0, 0, 0, 0], [0, 0, 1, 0, 0], [0, 0, 0, 0, 1], [0, 0, 0, 1, 0]);
        FiniteChainStructure report = FiniteChainStructure.Analyze(matrix, [0]);
        Assert.False(report.AbsorptionCertain);
        Assert.Equal(2, report.Classes.Count);
        Assert.Equal(report.ComponentOf[0], report.ComponentOf[1]);
        Assert.NotEqual(report.ComponentOf[0], report.ComponentOf[2]);
        Assert.Equal(-1, report.ComponentOf[3]);
        Assert.Equal(-1, report.ComponentOf[4]);
        FiniteCommunicatingClass cycle = report.Classes[report.ComponentOf[0]];
        Assert.False(cycle.IsClosed);
        Assert.Equal(2, cycle.Period);
        Assert.True(report.Classes[report.ComponentOf[2]].IsClosed);
        Assert.Equal(1, report.Classes[report.ComponentOf[2]].Period);
        FiniteChainStructure other = FiniteChainStructure.Analyze(matrix, [3]);
        Assert.Single(other.Classes);
        Assert.True(other.Classes[0].IsClosed);
        Assert.Equal(2, other.Classes[0].Period);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)report.ComponentOf)[0] = 9);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)cycle.States)[0] = 9);
    }

    [Fact]
    public void LeakyPeriodicCyclesHaveCertainAbsorptionAndAUsefulTwoStepBound()
    {
        ExactSparseMatrix q = Matrix([0, 1], [new Rational(1, 2), 0]);
        FiniteChainStructure structure = FiniteChainStructure.Analyze(q, [0]);
        Assert.True(structure.AbsorptionCertain);
        Assert.False(Assert.Single(structure.Classes).IsClosed);
        Assert.Equal(2, structure.Classes[0].Period);
        Assert.False(ExactSurvivalCertificate.Create(q, 1).Contracts);
        var certificate = ExactSurvivalCertificate.Create(q, 2);
        Assert.True(certificate.Contracts);
        Assert.Equal(new Rational(1, 2), certificate.Factor);
        Assert.Equal(new Rational(4, 1), certificate.ExpectedStepsUpperBound());
        for (var steps = 0; steps <= 8; steps++)
        {
            Rational actual = PathProbability(q, 0, 0, steps) + PathProbability(q, 0, 1, steps);
            Assert.Equal(actual, certificate.UpperSurvival(steps));
        }

        Assert.Equal(14L, certificate.FindSteps(new Rational(1, 100)));
    }

    [Fact]
    public void ZeroEigenvaluesDoNotImplyImmediateAbsorption()
    {
        ExactSparseMatrix q = Matrix([0, 1, 0, 0, 0], [0, 0, 1, 0, 0], [0, 0, 0, 1, 0],
            [0, 0, 0, 0, 1], [0, 0, 0, 0, 0]);
        // Strictly upper triangular: every eigenvalue is zero, but mass survives four transitions.
        Assert.True(FiniteChainStructure.Analyze(q, [0]).AbsorptionCertain);
        Assert.Equal(1, ExactSurvivalCertificate.Create(q, 4).Factor);
        var certificate = ExactSurvivalCertificate.Create(q, 5);
        Assert.Equal(Rational.Zero, certificate.Factor);
        Assert.Equal(Rational.One, certificate.UpperSurvival(4));
        Assert.Equal(Rational.Zero, certificate.UpperSurvival(5));
        Assert.Equal(5L, certificate.FindSteps(new Rational(1, 1000)));
        Assert.Equal(new Rational(5, 1), certificate.ExpectedStepsUpperBound());
    }

    [Fact]
    public void GeometricSurvivalAndRewardTailsHaveIndependentClosedForms()
    {
        var certificate = ExactSurvivalCertificate.Create(Matrix([new Rational(1, 2)]), 1);
        Assert.Equal(new Rational(1, 256), certificate.UpperSurvival(8));
        Assert.Equal(new Rational(2, 1), certificate.ExpectedStepsUpperBound());
        Assert.Equal(new Rational(3, 4), certificate.FutureAbsoluteRewardUpperBound(3, 3));
        Assert.Equal(7L, certificate.FindSteps(new Rational(1, 100)));
        Assert.Null(certificate.FindSteps(new Rational(1, 100), 6));
        Assert.Equal(0L, certificate.FindSteps(1));
        Assert.Throws<NotSupportedException>(() => ((IList<Rational>)certificate.RowSurvival)[0] = 1);
        var immortal = ExactSurvivalCertificate.Create(ExactSparseMatrix.Identity(1), 2);
        Assert.Null(immortal.ExpectedStepsUpperBound());
        Assert.Null(immortal.FutureAbsoluteRewardUpperBound(3, 1));
        Assert.Equal(Rational.Zero, immortal.FutureAbsoluteRewardUpperBound(3, 0));
        Assert.Null(immortal.FindSteps(new Rational(1, 2)));
    }

    [Fact]
    public void NonnormalTransientBoundsIncludeTheFiniteTimePrefactor()
    {
        ExactSparseMatrix q = Matrix([new Rational(1, 2), new Rational(1, 4)], [0, new Rational(1, 2)]);
        var certificate = ExactSurvivalCertificate.Create(q, 1);
        Assert.Equal(new Rational(3, 4), certificate.Factor);
        for (var steps = 0; steps <= 8; steps++)
        {
            Rational actual = PathProbability(q, 0, 0, steps) + PathProbability(q, 0, 1, steps);
            // Jordan block: (1+n/2)*2^-n rather than just the eigenvalue power 2^-n.
            Assert.Equal(new Rational(2 + steps, 2 * (1 << steps)), actual);
            Assert.True(actual <= certificate.UpperSurvival(steps));
        }
    }

    [Fact]
    public void NonreversibleStationaryResidualBoundsEncloseTheKnownExactLaw()
    {
        ExactSparseMatrix p = Matrix([new Rational(1, 2), new Rational(1, 2), 0],
            [new Rational(1, 2), 0, new Rational(1, 2)], [1, 0, 0]);
        var certificate = ExactMixingCertificate.Create(p, 1);
        Assert.Equal(new Rational(1, 2), certificate.Mass);
        Rational[] stationary = [new(4, 7), new(2, 7), new(1, 7)];
        Rational[] candidate = [new(1, 2), new(1, 3), new(1, 6)];
        ExactStationaryResidual checkedCandidate = certificate.VerifyCandidate(candidate);
        Assert.Equal(new Rational(1, 12), checkedCandidate.Residual);
        Assert.Equal(new Rational(1, 6), checkedCandidate.UpperDistance);
        Assert.True(Distance(candidate, stationary) <= checkedCandidate.UpperDistance);
        candidate[0] = 1;
        Assert.Equal(new Rational(1, 2), checkedCandidate.Candidate[0]);
        ExactStationaryResidual exact = certificate.VerifyCandidate(stationary);
        Assert.Equal(Rational.Zero, exact.Residual);
        Assert.Equal(Rational.Zero, exact.UpperDistance);
        Assert.Equal(7L, certificate.FindSteps(new Rational(1, 100)));
        for (var initial = 0; initial < p.States; initial++)
        {
            for (var steps = 0; steps <= 5; steps++)
            {
                Rational[] actual = Enumerable.Range(0, p.States)
                    .Select(to => PathProbability(p, initial, to, steps)).ToArray();
                Assert.True(Distance(actual, stationary) <= certificate.UpperDistance(steps));
            }
        }
    }

    [Fact]
    public void MinorizationCanNeedMoreThanOneStepWithoutProvingNonmixingAtOneStep()
    {
        ExactSparseMatrix p = Matrix([new Rational(1, 2), new Rational(1, 2), 0],
            [0, new Rational(1, 2), new Rational(1, 2)], [new Rational(1, 2), 0, new Rational(1, 2)]);
        Assert.False(ExactMixingCertificate.Create(p, 1).Contracts);
        var certificate = ExactMixingCertificate.Create(p, 2);
        Assert.True(certificate.Contracts);
        Assert.Equal(new Rational(3, 4), certificate.Mass);
        Assert.All(certificate.Minorization, value => Assert.Equal(new Rational(1, 4), value));
        Assert.Equal(new Rational(1, 16), certificate.UpperDistance(4));
        Assert.Equal(8L, certificate.FindSteps(new Rational(1, 100)));
    }

    [Fact]
    public void PeriodicityAndMultipleClosedClassesCannotReceiveAContractionClaim()
    {
        ExactSparseMatrix flip = Matrix([0, 1], [1, 0]);
        foreach (var steps in new[] { 1, 2, 3, 4 })
        {
            var periodic = ExactMixingCertificate.Create(flip, steps);
            Assert.False(periodic.Contracts);
            Assert.Equal(Rational.One, periodic.UpperDistance(100));
            Assert.Null(periodic.FindSteps(new Rational(1, 10)));
            // Even zero residual does not select a unique stationary law without contraction.
            ExactStationaryResidual ambiguous = ExactMixingCertificate.Create(ExactSparseMatrix.Identity(2), steps)
                .VerifyCandidate([new Rational(1, 2), new Rational(1, 2)]);
            Assert.Equal(Rational.Zero, ambiguous.Residual);
            Assert.Null(ambiguous.UpperDistance);
        }

        // Reducibility itself is not an obstruction: both states feed one absorbing class.
        var reducible = ExactMixingCertificate.Create(Matrix([0, 1], [0, 1]), 1);
        Assert.True(reducible.Contracts);
        Assert.Equal(Rational.Zero, reducible.UpperDistance(1));
    }

    [Fact]
    public void PinnedFinitePowersMatchIndependentPathEnumeration()
    {
        var random = new System.Random(9137);
        for (var sample = 0; sample < 20; sample++)
        {
            var n = random.Next(2, 5);
            var stochastic = new Rational[n][];
            var killed = new Rational[n][];
            for (var row = 0; row < n; row++)
            {
                var weights = Enumerable.Range(0, n).Select(_ => random.Next(1, 5)).ToArray();
                stochastic[row] = weights.Select(value => new Rational(value, weights.Sum())).ToArray();
                killed[row] = weights.Select(value => new Rational(value, weights.Sum() + 1)).ToArray();
            }

            ExactSparseMatrix p = Matrix(stochastic), q = Matrix(killed);
            var steps = random.Next(1, 4);
            var mixing = ExactMixingCertificate.Create(p, steps);
            var survival = ExactSurvivalCertificate.Create(q, steps);
            for (var to = 0; to < n; to++)
            {
                Rational expected = Enumerable.Range(0, n).Select(from => PathProbability(p, from, to, steps)).Min();
                Assert.Equal(expected, mixing.Minorization[to]);
            }

            for (var from = 0; from < n; from++)
            {
                Rational expected = Enumerable.Range(0, n).Select(to => PathProbability(q, from, to, steps))
                    .Aggregate(Rational.Zero, (sum, value) => sum + value);
                Assert.Equal(expected, survival.RowSurvival[from]);
            }
        }
    }

    [Fact]
    public void PinnedSccsMatchBooleanReachabilityAndIndependentCycleEnumeration()
    {
        var random = new System.Random(1103);
        for (var sample = 0; sample < 24; sample++)
        {
            const int n = 5;
            var rows = new Rational[n][];
            var reach = new bool[n, n];
            for (var from = 0; from < n; from++)
            {
                var weights = Enumerable.Range(0, n).Select(_ => random.Next(0, 3) == 0 ? random.Next(1, 4) : 0)
                    .ToArray();
                var denominator = System.Math.Max(1, weights.Sum() + random.Next(0, 2));
                rows[from] = weights.Select(weight => new Rational(weight, denominator)).ToArray();
                reach[from, from] = true;
                for (var to = 0; to < n; to++)
                {
                    reach[from, to] |= weights[to] > 0;
                }
            }

            for (var via = 0; via < n; via++)
            {
                for (var from = 0; from < n; from++)
                {
                    for (var to = 0; to < n; to++)
                    {
                        reach[from, to] |= reach[from, via] && reach[via, to];
                    }
                }
            }

            ExactSparseMatrix matrix = Matrix(rows);
            FiniteChainStructure structure = FiniteChainStructure.Analyze(matrix, [0]);
            for (var from = 0; from < n; from++)
            {
                Assert.Equal(reach[0, from], structure.ComponentOf[from] >= 0);
                if (!reach[0, from])
                {
                    continue;
                }

                for (var to = 0; to < n; to++)
                {
                    if (!reach[0, to])
                    {
                        continue;
                    }

                    Assert.Equal(reach[from, to] && reach[to, from],
                        structure.ComponentOf[from] == structure.ComponentOf[to]);
                }
            }

            foreach (FiniteCommunicatingClass component in structure.Classes)
            {
                var closed = component.States.All(from => matrix.RowSum(from) == Rational.One &&
                                                          Enumerable.Range(0, n).All(to =>
                                                              rows[from][to].IsZero || component.States.Contains(to)));
                Assert.Equal(closed, component.IsClosed);
                var lengths = new HashSet<int>();
                foreach (var root in component.States)
                {
                    void Visit(int from, HashSet<int> seen)
                    {
                        foreach (var to in component.States)
                        {
                            if (rows[from][to].IsZero)
                            {
                                continue;
                            }

                            if (to == root)
                            {
                                lengths.Add(seen.Count);
                            }
                            else if (seen.Add(to))
                            {
                                Visit(to, seen);
                                seen.Remove(to);
                            }
                        }
                    }

                    Visit(root, [root]);
                }

                var expectedPeriod = lengths.Aggregate(0,
                    (a, b) => (int)BigInteger.GreatestCommonDivisor(a, b));
                Assert.Equal(expectedPeriod, component.Period);
            }

            Assert.Equal(structure.Classes.All(component => !component.IsClosed), structure.AbsorptionCertain);
        }
    }

    [Fact]
    public void LongChainsUseBoundedIterativeTraversal()
    {
        const int states = 10000;
        var matrix = ExactSparseMatrix.Create(states,
            Enumerable.Range(0, states - 1).Select(from => new ExactSparseEntry(from, from + 1, 1)).ToArray());
        FiniteChainStructure structure = FiniteChainStructure.Analyze(matrix, [0]);
        Assert.Equal(states, structure.Classes.Count);
        Assert.True(structure.AbsorptionCertain);
        Assert.All(structure.Classes, component => Assert.Equal(0, component.Period));
    }

    [Fact]
    public void InvalidModelsCandidatesAndExhaustedBudgetsCannotCreateProofs()
    {
        ExactSparseMatrix half = Matrix([new Rational(1, 2)]), twice = Matrix([2]);
        Assert.Throws<ArgumentException>(() => ExactMixingCertificate.Create(half, 1));
        Assert.Throws<ArgumentException>(() => ExactSurvivalCertificate.Create(twice, 1));
        Assert.Throws<ArgumentException>(() => FiniteChainStructure.Analyze(twice, [0]));
        Assert.Throws<ArgumentException>(() => FiniteChainStructure.Analyze(half, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => FiniteChainStructure.Analyze(half, [-1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExactSurvivalCertificate.Create(half, 0));
        var mixing = ExactMixingCertificate.Create(ExactSparseMatrix.Identity(2), 1);
        Assert.Throws<ArgumentException>(() => mixing.VerifyCandidate([1]));
        Assert.Throws<ArgumentException>(() => mixing.VerifyCandidate([default, 1]));
        Assert.Throws<ArgumentException>(() => mixing.VerifyCandidate([-1, 2]));
        Assert.Throws<ArgumentException>(() => mixing.VerifyCandidate([new Rational(1, 2), 0]));
        var survival = ExactSurvivalCertificate.Create(half, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => survival.UpperSurvival(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => survival.FindSteps(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => survival.FindSteps(new Rational(1, 2), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => survival.FutureAbsoluteRewardUpperBound(1, -1));
        var scarce = new ExactLinearOptions
        {
            MaxWork = 1
        };
        Assert.Throws<ExactLinearLimitException>(() => ExactSurvivalCertificate.Create(half, 1, scarce));
        Assert.Throws<ExactLinearLimitException>(() => ExactMixingCertificate.Create(mixing.Kernel, 1, scarce));
        Assert.Throws<ExactLinearLimitException>(() => FiniteChainStructure.Analyze(half, [0], scarce));
        Assert.Throws<ExactLinearLimitException>(() => survival.UpperSurvival(64,
            new ExactLinearOptions
            {
                MaximumBits = 8
            }));
        Assert.Throws<ExactLinearLimitException>(() => survival.FindSteps(new Rational(1, 100),
            options: new ExactLinearOptions
            {
                MaximumBits = 4
            }));
        Assert.Throws<ExactLinearLimitException>(() => ExactMixingCertificate.Create(mixing.Kernel, 1,
            new ExactLinearOptions
            {
                MaxStates = 1
            }));
        Assert.Throws<ExactLinearLimitException>(() => ExactMixingCertificate.Create(mixing.Kernel, 1,
            new ExactLinearOptions
            {
                MaxCoefficients = 1
            }));
        var canceled = new CancellationToken(true);
        Assert.Throws<OperationCanceledException>(() =>
            FiniteChainStructure.Analyze(half, [0], cancellation: canceled));
        Assert.Throws<OperationCanceledException>(() =>
            ExactMixingCertificate.Create(mixing.Kernel, 1, cancellation: canceled));
        Assert.Throws<OperationCanceledException>(() =>
            ExactSurvivalCertificate.Create(half, 1, cancellation: canceled));
        Assert.Throws<OperationCanceledException>(() => survival.UpperSurvival(1, cancellation: canceled));
        Assert.Throws<OperationCanceledException>(() => mixing.VerifyCandidate([1, 0], cancellation: canceled));
    }
}
