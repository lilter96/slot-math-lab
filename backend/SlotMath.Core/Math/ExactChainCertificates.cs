namespace SlotMath.Core.Math;

internal static class ExactChainArithmetic
{
    internal static void Validate(ExactSparseMatrix kernel, bool stochastic, ref ExactLinearBudget budget)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        if (kernel.States > budget.Options.MaxStates || kernel.NonzeroEntries > budget.Options.MaxCoefficients)
        {
            throw new ExactLinearLimitException("Finite chain exceeds the state/coefficient allowance.");
        }

        for (var row = 0; row < kernel.States; row++)
        {
            budget.Value(kernel.RowSum(row));
            if (budget.Less(Rational.One, kernel.RowSum(row)) ||
                (stochastic && kernel.RowSum(row) != Rational.One))
            {
                throw new ArgumentException(stochastic
                    ? "Mixing requires a stochastic kernel."
                    : "A killed chain requires a substochastic kernel.");
            }

            foreach (ExactSparseMatrix.Entry entry in kernel.Rows[row])
            {
                budget.Value(entry.Value);
            }
        }
    }

    internal static void Block(int steps)
    {
        if (steps is < 1 or > 1000000)
        {
            throw new ArgumentOutOfRangeException(nameof(steps));
        }
    }

    internal static void Row(ExactSparseMatrix kernel, Rational[] source, Rational[] destination,
        ref ExactLinearBudget budget)
    {
        budget.Spend(2L * kernel.States);
        Array.Fill(destination, Rational.Zero);
        for (var row = 0; row < kernel.States; row++)
        {
            if (source[row].IsZero)
            {
                continue;
            }

            foreach (ExactSparseMatrix.Entry entry in kernel.Rows[row])
            {
                destination[entry.To] = budget.Add(destination[entry.To], budget.Multiply(source[row], entry.Value));
            }
        }
    }

    internal static Rational Power(Rational factor, long exponent, ref ExactLinearBudget budget)
    {
        Rational result = Rational.One;
        while (exponent > 0)
        {
            budget.Spend();
            if ((exponent & 1) != 0)
            {
                result = budget.Multiply(result, factor);
            }

            exponent >>= 1;
            if (exponent > 0)
            {
                factor = budget.Multiply(factor, factor);
            }
        }

        return result;
    }

    internal static Rational Bound(Rational factor, int block, long steps, ref ExactLinearBudget budget)
    {
        if (steps < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(steps));
        }

        budget.Value(factor);
        return Power(factor, steps / block, ref budget);
    }

    internal static long? Find(Rational factor, int block, Rational tolerance, int maxBlocks,
        ref ExactLinearBudget budget)
    {
        budget.Value(factor);
        budget.Value(tolerance);
        if (tolerance <= Rational.Zero || tolerance > Rational.One || maxBlocks is < 1 or > 1000000)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        }

        if (tolerance == Rational.One)
        {
            return 0;
        }

        if (factor == Rational.One)
        {
            return null;
        }

        long low = 0, high = 1;
        while (budget.Less(tolerance, Power(factor, high, ref budget)))
        {
            if (high == maxBlocks)
            {
                return null;
            }

            low = high;
            high = System.Math.Min(maxBlocks, 2 * high);
        }

        while (high - low > 1)
        {
            var middle = low + (high - low) / 2;
            if (budget.Less(tolerance, Power(factor, middle, ref budget)))
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        budget.Check();
        return high * block;
    }

    internal static Rational Divide(Rational numerator, Rational denominator, ref ExactLinearBudget budget)
    {
        Rational reciprocal = Rational.One / denominator;
        budget.Value(reciprocal);
        return budget.Multiply(numerator, reciprocal);
    }
}

/// <summary>Exact Q^k*1 and its uniform block-survival bound for a supplied killed chain.</summary>
public sealed class ExactSurvivalCertificate
{
    private ExactSurvivalCertificate(ExactSparseMatrix kernel, int steps, Rational[] survival,
        Rational factor, long work)
    {
        Kernel = kernel;
        BlockSteps = steps;
        RowSurvival = Array.AsReadOnly(survival);
        Factor = factor;
        Work = work;
    }

    public ExactSparseMatrix Kernel { get; }
    public int BlockSteps { get; }
    public IReadOnlyList<Rational> RowSurvival { get; }
    public Rational Factor { get; }
    public bool Contracts => Factor < Rational.One;
    public long Work { get; }

    public static ExactSurvivalCertificate Create(ExactSparseMatrix kernel, int blockSteps,
        ExactLinearOptions? options = null, CancellationToken cancellation = default)
    {
        var budget = new ExactLinearBudget(options, cancellation);
        ExactChainArithmetic.Block(blockSteps);
        ExactChainArithmetic.Validate(kernel, false, ref budget);
        budget.Spend(2L * kernel.States);
        var current = new Rational[kernel.States];
        var next = new Rational[kernel.States];
        Array.Fill(current, Rational.One);
        for (var step = 0; step < blockSteps; step++)
        {
            budget.Spend(2L * kernel.States);
            Array.Fill(next, Rational.Zero);
            for (var row = 0; row < kernel.States; row++)
            {
                foreach (ExactSparseMatrix.Entry entry in kernel.Rows[row])
                {
                    budget.Spend();
                    if (current[entry.To].IsZero)
                    {
                        continue;
                    }

                    next[row] = budget.Add(next[row], budget.Multiply(entry.Value, current[entry.To]));
                }
            }

            (current, next) = (next, current);
        }

        Rational factor = Rational.Zero;
        foreach (Rational value in current)
        {
            if (budget.Less(factor, value))
            {
                factor = value;
            }
        }

        budget.Check();
        return new ExactSurvivalCertificate(kernel, blockSteps, current, factor, budget.Work);
    }

    public Rational UpperSurvival(long steps, ExactLinearOptions? options = null,
        CancellationToken cancellation = default)
    {
        var budget = new ExactLinearBudget(options, cancellation);
        Rational bound = ExactChainArithmetic.Bound(Factor, BlockSteps, steps, ref budget);
        budget.Check();
        return bound;
    }

    /// <summary>Smallest block multiple certified by this bound; null if none within the search cap.</summary>
    public long? FindSteps(Rational tolerance, int maxBlocks = 1000000, ExactLinearOptions? options = null,
        CancellationToken cancellation = default)
    {
        var budget = new ExactLinearBudget(options, cancellation);
        return ExactChainArithmetic.Find(Factor, BlockSteps, tolerance, maxBlocks, ref budget);
    }

    public Rational? ExpectedStepsUpperBound(ExactLinearOptions? options = null,
        CancellationToken cancellation = default)
    {
        var budget = new ExactLinearBudget(options, cancellation);
        budget.Value(Factor);
        if (!Contracts)
        {
            return null;
        }

        Rational bound = ExactChainArithmetic.Divide(BlockSteps, budget.Add(Rational.One, -Factor), ref budget);
        budget.Check();
        return bound;
    }

    /// <summary>Upper bound on absolute rewards after n transitions, given an external per-transient-visit bound.</summary>
    public Rational? FutureAbsoluteRewardUpperBound(long steps, Rational maxPerVisit,
        ExactLinearOptions? options = null, CancellationToken cancellation = default)
    {
        var budget = new ExactLinearBudget(options, cancellation);
        Rational survival = ExactChainArithmetic.Bound(Factor, BlockSteps, steps, ref budget);
        budget.Value(maxPerVisit);
        if (maxPerVisit < Rational.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPerVisit));
        }

        if (maxPerVisit.IsZero)
        {
            return Rational.Zero;
        }

        if (!Contracts)
        {
            return null;
        }

        Rational lifetime = ExactChainArithmetic.Divide(BlockSteps, budget.Add(Rational.One, -Factor), ref budget);
        Rational bound = budget.Multiply(budget.Multiply(maxPerVisit, lifetime), survival);
        budget.Check();
        return bound;
    }
}

public sealed class ExactStationaryResidual
{
    internal ExactStationaryResidual(Rational[] candidate, Rational residual, Rational? upperDistance, long work)
    {
        Candidate = Array.AsReadOnly(candidate);
        Residual = residual;
        UpperDistance = upperDistance;
        Work = work;
    }

    public IReadOnlyList<Rational> Candidate { get; }
    public Rational Residual { get; }
    public Rational? UpperDistance { get; }
    public long Work { get; }
}

/// <summary>Common column minorization of P^k; sufficient, not necessary, for total-variation contraction.</summary>
public sealed class ExactMixingCertificate
{
    private ExactMixingCertificate(ExactSparseMatrix kernel, int steps, Rational[] minorization,
        Rational mass, long work)
    {
        Kernel = kernel;
        BlockSteps = steps;
        Minorization = Array.AsReadOnly(minorization);
        Mass = mass;
        Factor = Rational.One - mass;
        Work = work;
    }

    public ExactSparseMatrix Kernel { get; }
    public int BlockSteps { get; }
    public IReadOnlyList<Rational> Minorization { get; }
    public Rational Mass { get; }
    public Rational Factor { get; }
    public bool Contracts => Mass > Rational.Zero;
    public long Work { get; }

    public static ExactMixingCertificate Create(ExactSparseMatrix kernel, int blockSteps,
        ExactLinearOptions? options = null, CancellationToken cancellation = default)
    {
        var budget = new ExactLinearBudget(options, cancellation);
        ExactChainArithmetic.Block(blockSteps);
        ExactChainArithmetic.Validate(kernel, true, ref budget);
        budget.Spend(3L * kernel.States);
        var current = new Rational[kernel.States];
        var next = new Rational[kernel.States];
        var minorization = new Rational[kernel.States];
        Array.Fill(minorization, Rational.One);
        for (var initial = 0; initial < kernel.States; initial++)
        {
            budget.Spend(kernel.States);
            Array.Fill(current, Rational.Zero);
            current[initial] = Rational.One;
            for (var step = 0; step < blockSteps; step++)
            {
                ExactChainArithmetic.Row(kernel, current, next, ref budget);
                (current, next) = (next, current);
            }

            for (var column = 0; column < kernel.States; column++)
            {
                if (budget.Less(current[column], minorization[column]))
                {
                    minorization[column] = current[column];
                }
            }
        }

        Rational mass = Rational.Zero;
        foreach (Rational value in minorization)
        {
            mass = budget.Add(mass, value);
        }

        budget.Check();
        return new ExactMixingCertificate(kernel, blockSteps, minorization, mass, budget.Work);
    }

    /// <summary>Distance to the unique stationary law, when Contracts; otherwise the vacuous bound one.</summary>
    public Rational UpperDistance(long steps, ExactLinearOptions? options = null,
        CancellationToken cancellation = default)
    {
        var budget = new ExactLinearBudget(options, cancellation);
        Rational bound = ExactChainArithmetic.Bound(Factor, BlockSteps, steps, ref budget);
        budget.Check();
        return bound;
    }

    public long? FindSteps(Rational tolerance, int maxBlocks = 1000000, ExactLinearOptions? options = null,
        CancellationToken cancellation = default)
    {
        var budget = new ExactLinearBudget(options, cancellation);
        return ExactChainArithmetic.Find(Factor, BlockSteps, tolerance, maxBlocks, ref budget);
    }

    public ExactStationaryResidual VerifyCandidate(IReadOnlyList<Rational> candidate,
        ExactLinearOptions? options = null, CancellationToken cancellation = default)
    {
        var budget = new ExactLinearBudget(options, cancellation);
        ExactChainArithmetic.Validate(Kernel, true, ref budget);
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Count != Kernel.States)
        {
            throw new ArgumentException("Candidate dimensions differ.");
        }

        budget.Spend(3L * Kernel.States);
        var snapshot = new Rational[candidate.Count];
        var current = new Rational[candidate.Count];
        var next = new Rational[candidate.Count];
        Rational mass = Rational.Zero;
        for (var state = 0; state < candidate.Count; state++)
        {
            Rational value = candidate[state];
            budget.Value(value);
            if (value < Rational.Zero)
            {
                throw new ArgumentException("Candidate must be a probability law.");
            }

            snapshot[state] = current[state] = value;
            mass = budget.Add(mass, value);
        }

        if (mass != Rational.One)
        {
            throw new ArgumentException("Candidate mass must be exactly one.");
        }

        for (var step = 0; step < BlockSteps; step++)
        {
            ExactChainArithmetic.Row(Kernel, current, next, ref budget);
            (current, next) = (next, current);
        }

        Rational norm = Rational.Zero;
        for (var state = 0; state < current.Length; state++)
        {
            Rational difference = budget.Add(current[state], -snapshot[state]);
            norm = budget.Add(norm, difference.Sign < 0 ? -difference : difference);
        }

        Rational residual = budget.Multiply(norm, new Rational(1, 2));
        Rational? bound = Contracts ? ExactChainArithmetic.Divide(residual, Mass, ref budget) : null;
        if (bound is { } valueBound && budget.Less(Rational.One, valueBound))
        {
            bound = Rational.One;
        }

        budget.Check();
        return new ExactStationaryResidual(snapshot, residual, bound, budget.Work);
    }
}
