namespace SlotMath.Core.Math;

/// <summary>One explicitly authored Kronecker product. Factor order defines mixed-radix state order, last factor fastest.</summary>
public sealed record TensorProductTerm(Rational Coefficient, ExactSparseMatrix[] Factors);

public enum ExactTensorKind
{
    DiscreteTransition,
    ContinuousGenerator,
    UniformizedTransition
}

/// <summary>Caller-owned scratch. Reuse serially; concurrent reuse is rejected. No buffer escapes.</summary>
public sealed class ExactTensorWorkspace
{
    internal readonly Rational[] First, Second;
    private int _busy;

    internal ExactTensorWorkspace(int states)
    {
        First = new Rational[states];
        Second = new Rational[states];
    }

    public int States => First.Length;
    public int ScratchEntries => 2 * States;

    internal void Enter()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw new InvalidOperationException("Tensor workspace is already in use.");
        }
    }

    internal void Exit() => Volatile.Write(ref _busy, 0);
}

/// <summary>
///     Exact matrix-free row-vector action for a supplied factorization. Discrete terms are a convex
///     mixture of products of stochastic matrices. Generator terms are nonnegative event kernels E
///     with diagonal compensation E-diag(E*1). Coupled/synchronizing events are explicit terms.
///     No automatic factorization or game-graph equivalence is inferred, and no tensor rank is truncated.
/// </summary>
public sealed class ExactTensorOperator
{
    private readonly int[] _shape, _stride;
    private readonly TensorProductTerm[] _terms;

    private ExactTensorOperator(TensorProductTerm[] terms, int[] shape, ExactTensorKind kind,
        Rational maximumEventRate, Rational? uniformization, int stored)
    {
        _terms = terms;
        _shape = shape;
        _stride = new int[shape.Length];
        var size = 1;
        for (var axis = shape.Length - 1; axis >= 0; axis--)
        {
            _stride[axis] = size;
            size *= shape[axis];
        }

        States = size;
        Shape = Array.AsReadOnly((int[])shape.Clone());
        Kind = kind;
        MaximumEventRate = maximumEventRate;
        UniformizationRate = uniformization;
        StoredNonzeroEntries = stored;
    }

    public int States { get; }
    public IReadOnlyList<int> Shape { get; }
    public ExactTensorKind Kind { get; }
    public Rational MaximumEventRate { get; }
    public Rational? UniformizationRate { get; }

    public int StoredNonzeroEntries { get; }
    public ExactTensorWorkspace CreateWorkspace() => new(States);

    public static ExactTensorOperator Discrete(TensorProductTerm[] terms, ExactLinearOptions? options = null,
        CancellationToken cancellation = default) =>
        Create(terms, ExactTensorKind.DiscreteTransition, null, options, cancellation);

    public static ExactTensorOperator Generator(TensorProductTerm[] events, ExactLinearOptions? options = null,
        CancellationToken cancellation = default) =>
        Create(events, ExactTensorKind.ContinuousGenerator, null, options, cancellation);

    public static ExactTensorOperator Uniformized(TensorProductTerm[] events, Rational rate,
        ExactLinearOptions? options = null, CancellationToken cancellation = default) =>
        Create(events, ExactTensorKind.UniformizedTransition, rate, options, cancellation);

    private static ExactTensorOperator Create(TensorProductTerm[] authored, ExactTensorKind kind, Rational? rate,
        ExactLinearOptions? options, CancellationToken cancellation)
    {
        var budget = new ExactLinearBudget(options, cancellation);
        if (authored is not { Length: > 0 } || authored.Length > budget.Options.MaxTerms)
        {
            throw new ArgumentException("Supply a bounded nonempty list of product/event terms.");
        }

        int[]? shape = null;
        var snapshots = new TensorProductTerm[authored.Length];
        var matrices = new HashSet<ExactSparseMatrix>(ReferenceEqualityComparer.Instance);
        Rational weight = Rational.Zero, maximumRate = Rational.Zero;
        var stored = 0;
        for (var term = 0; term < authored.Length; term++)
        {
            TensorProductTerm input = authored[term];
            if (input is null || input.Factors is not { Length: > 0 } ||
                input.Factors.Length > budget.Options.MaxFactors)
            {
                throw new ArgumentException("Each term requires bounded, nonempty factors.");
            }

            budget.Value(input.Coefficient);
            if (input.Coefficient.Sign < 0)
            {
                throw new ArgumentException("Term weights/rates must be nonnegative.");
            }

            var factors = (ExactSparseMatrix[])input.Factors.Clone();
            if (factors.Any(factor => factor is null))
            {
                throw new ArgumentException("Factors cannot be null.");
            }

            var dimensions = factors.Select(factor => factor.States).ToArray();
            shape ??= dimensions;
            if (!shape.SequenceEqual(dimensions))
            {
                throw new ArgumentException("All terms must use the same ordered component dimensions.");
            }

            long states = 1;
            Rational eventRate = input.Coefficient;
            foreach (ExactSparseMatrix factor in factors)
            {
                states *= factor.States;
                if (states > budget.Options.MaxStates)
                {
                    throw new ExactLinearLimitException("Tensor state vector exceeds the allowance.");
                }

                if (matrices.Add(factor))
                {
                    stored += factor.NonzeroEntries;
                    if (stored > budget.Options.MaxCoefficients)
                    {
                        throw new ExactLinearLimitException("Stored factor coefficients exceed the allowance.");
                    }

                    foreach (ExactSparseEntry entry in factor.Entries)
                    {
                        budget.Value(entry.Value);
                    }
                }

                if (kind == ExactTensorKind.DiscreteTransition && !factor.IsStochastic)
                {
                    throw new ArgumentException("Discrete product factors must each have unit row sums.");
                }

                eventRate = budget.Multiply(eventRate, factor.MaximumRowSum);
            }

            weight = budget.Add(weight, input.Coefficient);
            maximumRate = budget.Add(maximumRate, eventRate);
            snapshots[term] = new TensorProductTerm(input.Coefficient, factors);
        }

        if (kind == ExactTensorKind.DiscreteTransition && weight != Rational.One)
        {
            throw new ArgumentException("Discrete mixture weights must sum to one exactly.");
        }

        if (rate is { } lambda)
        {
            budget.Value(lambda);
            if (lambda <= Rational.Zero || lambda < maximumRate)
            {
                throw new ArgumentException(
                    "Uniformization requires a positive rate at least the certified event-rate bound.");
            }
        }

        budget.Check();
        return new ExactTensorOperator(snapshots, shape!, kind, maximumRate, rate, stored);
    }

    /// <summary>
    ///     Apply a row vector. Caller-owned vectors cannot overlap. A failed application can leave partial
    ///     destination data; it must not be consumed. Scratch can be reused after failure. Rational/BigInteger
    ///     arithmetic may allocate even though the vector buffers and control state are reused.
    /// </summary>
    public ExactOperatorApplication Apply(ReadOnlySpan<Rational> source, Span<Rational> destination,
        ExactTensorWorkspace workspace, ExactLinearOptions? options = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var budget = new ExactLinearBudget(options, cancellation);
        if (StoredNonzeroEntries > budget.Options.MaxCoefficients || _terms.Length > budget.Options.MaxTerms ||
            _shape.Length > budget.Options.MaxFactors)
        {
            throw new ExactLinearLimitException("Tensor factors exceed the application allowance.");
        }

        ExactSparseMatrix.ValidateVectors(source, destination, States, ref budget);
        if (workspace.States != States)
        {
            throw new ArgumentException("Workspace dimension differs from the operator.");
        }

        workspace.Enter();
        try
        {
            destination.Fill(Rational.Zero);
            foreach (TensorProductTerm term in _terms)
            {
                budget.Spend();
                if (term.Coefficient.IsZero)
                {
                    continue;
                }

                ReadOnlySpan<Rational> current = source;
                var buffer = 0;
                for (var axis = 0; axis < _shape.Length; axis++)
                {
                    ExactSparseMatrix factor = term.Factors[axis];
                    if (factor.IsIdentity)
                    {
                        continue;
                    }

                    Span<Rational> next = buffer == 0 ? workspace.First : workspace.Second;
                    ApplyAxis(current, next, factor, axis, ref budget);
                    current = next;
                    buffer ^= 1;
                }

                for (var index = 0; index < States; index++)
                {
                    budget.Spend();
                    Rational value = current[index];
                    if (Kind != ExactTensorKind.DiscreteTransition && !source[index].IsZero)
                    {
                        Rational rowRate = Rational.One;
                        for (var axis = 0; axis < _shape.Length; axis++)
                        {
                            rowRate = budget.Multiply(rowRate,
                                term.Factors[axis].RowSum(index / _stride[axis] % _shape[axis]));
                        }

                        value = budget.Add(value, -budget.Multiply(source[index], rowRate));
                    }

                    if (!value.IsZero)
                    {
                        destination[index] = budget.Add(destination[index], budget.Multiply(term.Coefficient, value));
                    }
                }
            }

            if (UniformizationRate is { } rate)
            {
                Rational inverse = Rational.One / rate;
                budget.Value(inverse);
                for (var index = 0; index < States; index++)
                {
                    destination[index] = budget.Add(source[index], budget.Multiply(destination[index], inverse));
                }
            }

            budget.Check();
            return new ExactOperatorApplication(budget.Work, budget.Products, workspace.ScratchEntries);
        }
        finally
        {
            workspace.Exit();
        }
    }

    private void ApplyAxis(ReadOnlySpan<Rational> source, Span<Rational> destination, ExactSparseMatrix factor,
        int axis, ref ExactLinearBudget budget)
    {
        destination.Fill(Rational.Zero);
        var stride = _stride[axis];
        var width = stride * factor.States;
        for (var block = 0; block < States; block += width)
        {
            for (var offset = 0; offset < stride; offset++)
            {
                for (var row = 0; row < factor.States; row++)
                {
                    budget.Spend();
                    Rational mass = source[block + row * stride + offset];
                    if (mass.IsZero)
                    {
                        continue;
                    }

                    foreach (ExactSparseMatrix.Entry entry in factor.Rows[row])
                    {
                        var target = block + entry.To * stride + offset;
                        destination[target] = budget.Add(destination[target], budget.Multiply(mass, entry.Value));
                    }
                }
            }
        }
    }
}
