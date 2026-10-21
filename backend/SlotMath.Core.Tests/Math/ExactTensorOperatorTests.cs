using SlotMath.Core.Math;

namespace SlotMath.Core.Tests.Math;

public sealed class ExactTensorOperatorTests
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

    private static Rational[] Point(int states, int at = 0)
    {
        var values = new Rational[states];
        Array.Fill(values, Rational.Zero);
        values[at] = Rational.One;
        return values;
    }

    private static Rational[,] Expand(TensorProductTerm[] terms, bool generator)
    {
        var shape = terms[0].Factors.Select(factor => factor.States).ToArray();
        var states = shape.Aggregate(1, (size, dimension) => size * dimension);
        var output = new Rational[states, states];
        // Independent definition of Kronecker entries, without the tensor-vector shuffle.
        for (var from = 0; from < states; from++)
        {
            Rational outgoing = Rational.Zero;
            for (var to = 0; to < states; to++)
            {
                Rational value = Rational.Zero;
                if (!generator || from != to)
                {
                    foreach (TensorProductTerm term in terms)
                    {
                        Rational product = term.Coefficient;
                        int a = from, b = to;
                        for (var axis = shape.Length - 1; axis >= 0; axis--)
                        {
                            product *= term.Factors[axis][a % shape[axis], b % shape[axis]];
                            a /= shape[axis];
                            b /= shape[axis];
                        }

                        value += product;
                    }
                }

                output[from, to] = value;
                outgoing += value;
            }

            // A generator's diagonal is minus its off-diagonal rate sum, including self-event cancellation.
            if (generator)
            {
                output[from, from] = -outgoing;
            }
        }

        return output;
    }

    private static Rational[] Multiply(Rational[] source, Rational[,] matrix)
    {
        var result = new Rational[source.Length];
        Array.Fill(result, Rational.Zero);
        for (var from = 0; from < source.Length; from++)
        {
            for (var to = 0; to < source.Length; to++)
            {
                result[to] += source[from] * matrix[from, to];
            }
        }

        return result;
    }

    [Fact]
    public void UnequalComponentDimensionsAndSignedVectorsMatchExplicitKroneckerEntries()
    {
        ExactSparseMatrix a = Matrix([new Rational(1, 2), new Rational(1, 3), new Rational(1, 6)],
            [0, new Rational(1, 4), new Rational(3, 4)], [new Rational(2, 3), new Rational(1, 3), 0]);
        ExactSparseMatrix b = Matrix([new Rational(1, 5), new Rational(4, 5)],
            [new Rational(3, 7), new Rational(4, 7)]);
        TensorProductTerm[] terms = [new(1, [a, b, ExactSparseMatrix.Identity(2)])];
        ExactTensorOperator op = ExactTensorOperator.Discrete(terms);
        Rational[] source = Enumerable.Range(0, 12).Select(index => new Rational(index - 3, 11)).ToArray();
        var snapshot = (Rational[])source.Clone();
        var result = new Rational[12];
        op.Apply(source, result, op.CreateWorkspace());
        Assert.Equal(Multiply(source, Expand(terms, false)), result);
        Assert.Equal(snapshot, source);
        Assert.Equal(new[] { 3, 2, 2 }, op.Shape);
    }

    [Fact]
    public void MixtureOfSynchronousEventsDoesNotPretendTheComponentsRemainIndependent()
    {
        ExactSparseMatrix identity = ExactSparseMatrix.Identity(2);
        ExactSparseMatrix flip = Matrix([0, 1], [1, 0]);
        TensorProductTerm[] terms =
            [new(new Rational(1, 2), [identity, identity]), new(new Rational(1, 2), [flip, flip])];
        ExactTensorOperator op = ExactTensorOperator.Discrete(terms);
        var result = new Rational[4];
        op.Apply(Point(4), result, op.CreateWorkspace());
        Assert.Equal(new Rational[] { new(1, 2), 0, 0, new(1, 2) }, result);
        Assert.NotEqual(Enumerable.Repeat(new Rational(1, 4), 4), result);
        Assert.Equal(4, op.StoredNonzeroEntries); // Shared matrices are stored once.
    }

    [Fact]
    public void AsynchronousAndGuardedSynchronizingRatesMatchAnExplicitGenerator()
    {
        ExactSparseMatrix ratesA = Matrix([0, 2], [3, 0]);
        ExactSparseMatrix ratesB = Matrix([0, 1, 0], [0, 0, 1], [1, 0, 0]);
        ExactSparseMatrix guardA = Matrix([0, 1], [0, 0]);
        ExactSparseMatrix guardB = Matrix([0, 0, 0], [1, 0, 0], [0, 0, 0]);
        TensorProductTerm[] events =
        [
            new(1, [ratesA, ExactSparseMatrix.Identity(3)]),
            new(new Rational(5, 2), [ExactSparseMatrix.Identity(2), ratesB]),
            new(new Rational(7, 3), [guardA, guardB])
        ];
        ExactTensorOperator generator = ExactTensorOperator.Generator(events);
        Rational[,] explicitQ = Expand(events, true);
        Rational[] source = [new(3, 25), new(9, 50), new(3, 10), new(2, 25), new(3, 25), new(1, 5)];
        var result = new Rational[6];
        generator.Apply(source, result, generator.CreateWorkspace());
        Assert.Equal(Multiply(source, explicitQ), result);
        Assert.Equal(Rational.Zero, result.Aggregate(Rational.Zero, (sum, value) => sum + value));
        Assert.Equal(new Rational(7, 3), explicitQ[1, 3]); // Both components change in this single event.
        Assert.Equal(new Rational(47, 6), generator.MaximumEventRate);
        ExactTensorOperator uniformized = ExactTensorOperator.Uniformized(events, 8);
        var transition = new Rational[6, 6];
        for (var row = 0; row < 6; row++)
        {
            Rational sum = Rational.Zero;
            for (var column = 0; column < 6; column++)
            {
                transition[row, column] = (row == column ? Rational.One : Rational.Zero) + explicitQ[row, column] / 8;
                Assert.True(transition[row, column] >= Rational.Zero);
                sum += transition[row, column];
            }

            Assert.Equal(Rational.One, sum);
        }

        uniformized.Apply(source, result, uniformized.CreateWorkspace());
        Assert.Equal(Multiply(source, transition), result);
        Assert.Throws<ArgumentException>(() => ExactTensorOperator.Uniformized(events, 1));
    }

    [Fact]
    public void PureSelfEventsCancelAndUniformizeToIdentity()
    {
        TensorProductTerm[] events = [new(1, [Matrix([2, 0], [0, 5]), Matrix([3, 0], [0, 7])])];
        ExactTensorOperator generator = ExactTensorOperator.Generator(events);
        var result = new Rational[4];
        generator.Apply(Point(4, 2), result, generator.CreateWorkspace());
        Assert.All(result, value => Assert.Equal(Rational.Zero, value));
        ExactTensorOperator uniformized = ExactTensorOperator.Uniformized(events, 35);
        uniformized.Apply(Point(4, 2), result, uniformized.CreateWorkspace());
        Assert.Equal(Point(4, 2), result);
    }

    [Fact]
    public void PinnedSmallStochasticModelsAgreeWithExplicitMatrices()
    {
        var random = new System.Random(731);
        for (var sample = 0; sample < 40; sample++)
        {
            var shape = Enumerable.Range(0, 3).Select(_ => random.Next(1, 4)).ToArray();
            var terms = new TensorProductTerm[3];
            for (var term = 0; term < terms.Length; term++)
            {
                ExactSparseMatrix[] factors = shape.Select(dimension =>
                {
                    var rows = new Rational[dimension][];
                    for (var row = 0; row < dimension; row++)
                    {
                        var weights = Enumerable.Range(0, dimension).Select(_ => random.Next(1, 6)).ToArray();
                        rows[row] = weights.Select(weight => new Rational(weight, weights.Sum())).ToArray();
                    }

                    return Matrix(rows);
                }).ToArray();
                terms[term] = new TensorProductTerm(new Rational(term + 1, 6), factors);
            }

            ExactTensorOperator op = ExactTensorOperator.Discrete(terms);
            Rational[] source = Enumerable.Range(0, op.States).Select(_ => new Rational(random.Next(-4, 5), 7))
                .ToArray();
            var actual = new Rational[op.States];
            op.Apply(source, actual, op.CreateWorkspace());
            Assert.Equal(Multiply(source, Expand(terms, false)), actual);
        }
    }

    [Fact]
    public void SnapshotsCannotBeChangedThroughTheAuthoredArrays()
    {
        ExactSparseEntry[] entries = [new(0, 1, 1), new(1, 0, 1)];
        var flip = ExactSparseMatrix.Create(2, entries);
        ExactSparseMatrix[] factors = [flip, flip];
        TensorProductTerm[] terms = [new(1, factors)];
        ExactTensorOperator op = ExactTensorOperator.Discrete(terms);
        entries[0] = new ExactSparseEntry(0, 0, 99);
        factors[0] = ExactSparseMatrix.Identity(2);
        terms[0] = new TensorProductTerm(0, []);
        var result = new Rational[4];
        op.Apply(Point(4), result, op.CreateWorkspace());
        Assert.Equal(Point(4, 3), result);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)op.Shape)[0] = 9);
    }

    [Fact]
    public void InvalidFactorizationsAndResourceOverrunsCannotReturnSuccessfulResults()
    {
        ExactSparseMatrix flip = Matrix([0, 1], [1, 0]);
        ExactTensorOperator op = ExactTensorOperator.Discrete([new TensorProductTerm(1, [flip, flip])]);
        ExactTensorWorkspace workspace = op.CreateWorkspace();
        var result = new Rational[4];
        Assert.Throws<ExactLinearLimitException>(() => op.Apply(Point(4), result, workspace, new ExactLinearOptions
        {
            MaxWork = 8
        }));
        op.Apply(Point(4), result, workspace); // Scratch is reusable even after a partial application.
        Assert.Equal(Point(4, 3), result);
        Assert.Throws<ArgumentException>(() => op.Apply(result, result, workspace));
        Assert.Throws<ArgumentException>(() => op.Apply(new Rational[4], result, workspace));
        Assert.Throws<OperationCanceledException>(() =>
            op.Apply(Point(4), result, workspace, cancellation: new CancellationToken(true)));
        Assert.Throws<ExactLinearLimitException>(() => ExactTensorOperator.Discrete(
            [new TensorProductTerm(1, [flip, flip])],
            new ExactLinearOptions
            {
                MaxStates = 3
            }));
        Assert.Throws<ArgumentException>(() =>
            ExactTensorOperator.Discrete([new TensorProductTerm(new Rational(1, 2), [flip])]));
        Assert.Throws<ArgumentException>(() =>
            ExactTensorOperator.Discrete([new TensorProductTerm(1, [Matrix([0, 0], [0, 1])])]));
        Assert.Throws<ArgumentException>(() =>
            ExactTensorOperator.Discrete([
                new TensorProductTerm(1, [flip]), new TensorProductTerm(0, [ExactSparseMatrix.Identity(3)])
            ]));
        Assert.Throws<ArgumentException>(() => ExactTensorOperator.Generator([new TensorProductTerm(-1, [flip])]));
        Assert.Throws<ArgumentException>(() => ExactSparseMatrix.Create(2, [new ExactSparseEntry(0, 0, default)]));
        Assert.Throws<ArgumentException>(() => ExactSparseMatrix.Create(2, [new ExactSparseEntry(0, 0, -1)]));
        Assert.Throws<ArgumentException>(() => ExactSparseMatrix.Identity(int.MaxValue, new ExactLinearOptions
        {
            MaxStates = int.MaxValue,
            MaxCoefficients = 8
        }));
        Assert.Throws<ExactLinearLimitException>(() => ExactSparseMatrix.Identity(2, new ExactLinearOptions
        {
            MaxWork = 1
        }));
        ExactSparseMatrix precise = Matrix([new Rational(1, 128), new Rational(127, 128)], [1, 0]);
        ExactTensorOperator exact = ExactTensorOperator.Discrete([new TensorProductTerm(1, [precise])]);
        Assert.Throws<ExactLinearLimitException>(() => exact.Apply(Point(2), new Rational[2], exact.CreateWorkspace(),
            new ExactLinearOptions
            {
                MaximumBits = 4
            }));
    }

    [Fact]
    public void ReusedPermutationWorkspaceHasNoManagedAllocationOnTheMeasuredPath()
    {
        ExactSparseMatrix flip = Matrix([0, 1], [1, 0]);
        ExactTensorOperator op = ExactTensorOperator.Discrete([new TensorProductTerm(1, [flip, flip])]);
        ExactTensorWorkspace workspace = op.CreateWorkspace();
        Rational[] source = Point(4), result = new Rational[4];
        for (var warmup = 0; warmup < 32; warmup++)
        {
            op.Apply(source, result, workspace);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var repeat = 0; repeat < 100; repeat++)
        {
            op.Apply(source, result, workspace);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(Point(4, 3), result);
    }
}
