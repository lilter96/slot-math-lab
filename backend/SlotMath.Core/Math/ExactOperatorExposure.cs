using System.Numerics;

namespace SlotMath.Core.Math;

/// <summary>Joint next-state/settlement outcome, conditional on From. Wager is received by the operator.</summary>
public sealed record ExposureTransition(int From, int To, Rational Probability, Rational Payout, Rational Wager);

public sealed record OperatorExposureModel(int States, ExposureTransition[] Transitions, int InitialState = 0);

public sealed record ExactExposureOptions
{
    public long MaxWork { get; init; } = 40000000;
    public long RuinWork { get; init; } = 4000000;

    /// <summary>Maximum total nonzero coefficients in any retained polynomial operator or active ruin table.</summary>
    public int MaxAtoms { get; init; } = 100000;

    public int MaxStates { get; init; } = 32;

    /// <summary>Maximum bits of integer loss coordinates, probability coefficients and denominators.</summary>
    public int MaximumBits { get; init; } = 65536;

    public bool UseTransformConvolution { get; init; } = true;
    public TimeSpan? MaxTime { get; init; }
}

public sealed record ExposureDiagnostics(
    long DistributionWork,
    long RuinWork,
    long Convolutions,
    long TransformConvolutions,
    int LargestLawAtoms,
    int LargestOperatorAtoms,
    int ReachableStates);

public sealed record FirstRuinPassage(long Round, Rational Probability);

/// <summary>First post-settlement reserve strictly below zero. Surviving sessions are censored, not zero times.</summary>
public sealed record ExactRuinResult
{
    public required string Status { get; init; }
    public string? Reason { get; init; }
    public Rational? Probability { get; init; }
    public Rational? SurvivalProbability { get; init; }
    public Rational? ConditionalExpectedRound { get; init; }
    public IReadOnlyList<FirstRuinPassage> FirstPassages { get; init; } = [];
}

/// <summary>
///     FinalLoss covers the full declared horizon with all settlements, including paths that recover
///     after insolvency. Ruin is a separate first-passage query, never a loss-CDF shortcut or a stop policy.
///     Exactness refers to the supplied transition model; equivalence to a compiled graph is not inferred.
/// </summary>
public sealed record ExactExposureResult(
    long Rounds,
    ExactExposureLaw FinalLoss,
    ExactRuinResult? Ruin,
    ExposureDiagnostics Diagnostics);

public static class ExactOperatorExposure
{
    public static ExactExposureResult Independent(Dist<BigInteger> payouts, BigInteger winScale, Rational wager,
        long rounds, Rational? initialReserve = null, ExactExposureOptions? options = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(payouts);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(winScale);
        Require(wager);
        if (wager <= Rational.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(wager));
        }

        options ??= new ExactExposureOptions();
        var budget = new ExposureBudget(options, cancellation);
        budget.InputSupport(payouts.Count);
        budget.Integer(winScale);
        budget.Integer(payouts.Denominator);
        if (payouts.IsEmpty || !payouts.IsFullyExact || payouts.TotalNumerator != payouts.Denominator ||
            payouts.Denominator.Sign <= 0)
        {
            throw new ArgumentException("Independent exposure requires a complete unpruned settled payout law.");
        }

        Dist<BigInteger>.Entry[] atoms = payouts.Entries.ToArray();
        var edges = new List<ExposureTransition>();
        BigInteger mass = 0;
        foreach (Dist<BigInteger>.Entry atom in atoms)
        {
            budget.Spend(1 + BigInteger.Abs(atom.Value).GetBitLength() / 64 +
                         BigInteger.Abs(atom.Numerator).GetBitLength() / 64);
            budget.Integer(atom.Numerator);
            budget.Integer(atom.Value);
            if (atom.Numerator.Sign < 0 || (atom.Numerator.Sign > 0 && atom.Value.Sign < 0))
            {
                throw new ArgumentException("Settled payouts and their masses must be nonnegative.");
            }

            mass += atom.Numerator;
            if (atom.Numerator.Sign > 0)
            {
                edges.Add(new ExposureTransition(0, 0,
                    new Rational(atom.Numerator, payouts.Denominator), new Rational(atom.Value, winScale), wager));
            }
        }

        if (mass != payouts.Denominator)
        {
            throw new ArgumentException("Actual payout probability mass is incomplete.");
        }

        return Evaluate(new OperatorExposureModel(1, edges.ToArray()), rounds, initialReserve, budget);
    }

    public static ExactExposureResult Markov(OperatorExposureModel model, long rounds, Rational? initialReserve = null,
        ExactExposureOptions? options = null, CancellationToken cancellation = default) =>
        Evaluate(model, rounds, initialReserve,
            new ExposureBudget(options ?? new ExactExposureOptions(), cancellation));

    private static ExactExposureResult Evaluate(OperatorExposureModel model, long rounds, Rational? initialReserve,
        ExposureBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentOutOfRangeException.ThrowIfNegative(rounds);
        if (initialReserve is { } reserve)
        {
            Require(reserve);
            if (reserve.Sign < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialReserve));
            }

            budget.Integer(reserve.Numerator);
            budget.Integer(reserve.Denominator);
        }

        Kernel kernel = Validate(model, budget);
        var row = new PolynomialMatrix(1, model.States);
        row.Add(0, model.InitialState, ExposurePolynomial.Unit, budget);
        PolynomialMatrix power = kernel.Matrix;
        var remaining = rounds;
        while (remaining > 0)
        {
            budget.Check();
            if ((remaining & 1) != 0)
            {
                row = row.Multiply(power, budget);
            }

            remaining >>= 1;
            if (remaining > 0)
            {
                power = power.Multiply(power, budget);
            }
        }

        ExposurePolynomial? total = null;
        foreach (ExposurePolynomial law in row.Rows[0].Values)
        {
            total = total is null ? law : ExposurePolynomial.Add(total, law, budget);
        }

        ExactExposureLaw final = total!.Analyze(kernel.Scale, budget);
        ExactRuinResult? ruin = null;
        ExposureBudget? ruinBudget = null;
        if (initialReserve is { } bankroll)
        {
            try
            {
                BigInteger maximumStepLoss = kernel.Edges.SelectMany(edges => edges)
                    .Aggregate(BigInteger.Zero, (maximum, edge) => BigInteger.Max(maximum, edge.Loss));
                // A per-step upper bound controls every prefix; the terminal loss bound alone does not.
                if (new Rational(maximumStepLoss * rounds, kernel.Scale) <= bankroll)
                {
                    ruin = NoRuin();
                }
                else
                {
                    ruinBudget = budget.RuinBudget();
                    ruin = ruinBudget is null
                        ? UnknownRuin("No work allowance remains for the first-passage calculation.")
                        : FirstPassage(kernel, model.InitialState, rounds, bankroll, ruinBudget);
                }
            }
            catch (ExactExposureLimitException exception)
            {
                ruin = UnknownRuin(exception.Message);
            }
        }

        budget.Cancellation.ThrowIfCancellationRequested();
        return new ExactExposureResult(rounds, final, ruin, new ExposureDiagnostics(budget.Work,
            ruinBudget?.Work ?? 0, budget.Convolutions, budget.Transforms, budget.LargestLaw,
            budget.LargestOperator, kernel.Reachable));
    }

    private static Kernel Validate(OperatorExposureModel model, ExposureBudget budget)
    {
        if (model.States < 1 || model.States > budget.Options.MaxStates || model.InitialState < 0 ||
            model.InitialState >= model.States ||
            model.Transitions is not { Length: > 0 })
        {
            throw new ArgumentException("Use a valid bounded finite settlement model.");
        }

        budget.InputSupport(model.Transitions.Length);
        ExposureTransition[] input = model.Transitions.ToArray();
        var mass = new Rational[model.States];
        Array.Fill(mass, Rational.Zero);
        List<ExposureTransition>[] outgoing =
            Enumerable.Range(0, model.States).Select(_ => new List<ExposureTransition>()).ToArray();
        foreach (ExposureTransition edge in input)
        {
            budget.Spend();
            if (edge is null || edge.From < 0 || edge.From >= model.States || edge.To < 0 || edge.To >= model.States)
            {
                throw new ArgumentException("Invalid settlement transition state.");
            }

            foreach (Rational number in new[] { edge.Probability, edge.Payout, edge.Wager })
            {
                Require(number);
                budget.Integer(number.Numerator);
                budget.Integer(number.Denominator);
                budget.Spend(1 + number.Numerator.GetBitLength() / 64 + number.Denominator.GetBitLength() / 64);
            }

            if (edge.Probability < Rational.Zero || edge.Probability > Rational.One || edge.Payout < Rational.Zero ||
                edge.Wager < Rational.Zero)
            {
                throw new ArgumentException("Use valid probabilities and nonnegative settled payouts/wagers.");
            }

            mass[edge.From] += edge.Probability;
            budget.Integer(mass[edge.From].Denominator);
            budget.Integer(mass[edge.From].Numerator);
            if (!edge.Probability.IsZero)
            {
                outgoing[edge.From].Add(edge);
            }
        }

        if (mass.Any(sum => sum != Rational.One))
        {
            throw new ArgumentException("Every conditional transition row must sum to one exactly.");
        }

        var reachable = new HashSet<int>
        {
            model.InitialState
        };
        var pending = new Queue<int>(reachable);
        while (pending.TryDequeue(out var state))
        {
            foreach (ExposureTransition edge in outgoing[state])
            {
                budget.Spend();
                if (reachable.Add(edge.To))
                {
                    pending.Enqueue(edge.To);
                }
            }
        }

        BigInteger scale = 1, denominator = 1;
        foreach (var state in reachable)
        {
            foreach (ExposureTransition edge in outgoing[state])
            {
                Rational loss = edge.Payout - edge.Wager;
                scale = Rational.Lcm(scale, loss.Denominator);
                denominator = Rational.Lcm(denominator, edge.Probability.Denominator);
                budget.Integer(scale);
                budget.Integer(denominator);
            }
        }

        var matrix = new PolynomialMatrix(model.States, model.States);
        List<KernelEdge>[] transitions =
            Enumerable.Range(0, model.States).Select(_ => new List<KernelEdge>()).ToArray();
        foreach (var state in reachable.Order())
        {
            var grouped = new Dictionary<int, Dictionary<BigInteger, BigInteger>>();
            foreach (ExposureTransition edge in outgoing[state])
            {
                Rational loss = edge.Payout - edge.Wager;
                BigInteger value = loss.Numerator * (scale / loss.Denominator);
                BigInteger weight = edge.Probability.Numerator * (denominator / edge.Probability.Denominator);
                budget.Spend(1 + denominator.GetBitLength() / 64 + BigInteger.Abs(value).GetBitLength() / 64);
                if (!grouped.TryGetValue(edge.To, out Dictionary<BigInteger, BigInteger>? atoms))
                {
                    grouped[edge.To] = atoms = new Dictionary<BigInteger, BigInteger>();
                }

                atoms[value] = atoms.GetValueOrDefault(value) + weight;
            }

            foreach ((var to, Dictionary<BigInteger, BigInteger> atoms) in grouped)
            {
                foreach ((BigInteger value, BigInteger weight) in atoms)
                {
                    transitions[state].Add(new KernelEdge(to, value, weight));
                }

                matrix.Add(state, to, ExposurePolynomial.Create(atoms, denominator, budget), budget);
            }
        }

        return new Kernel(matrix, transitions, denominator, scale, reachable.Count);
    }

    private static ExactRuinResult FirstPassage(Kernel kernel, int initial, long rounds, Rational reserve,
        ExposureBudget budget)
    {
        var running = new Dictionary<(int State, BigInteger Loss), BigInteger>
        {
            [(initial, 0)] = 1
        };
        var passages = new List<FirstRuinPassage>();
        BigInteger denominator = 1;
        Rational ruined = Rational.Zero, time = Rational.Zero;
        BigInteger threshold = reserve.Numerator * kernel.Scale;
        budget.Integer(threshold);
        for (long round = 0; round < rounds && running.Count > 0; round++)
        {
            budget.Check();
            denominator *= kernel.Denominator;
            budget.Integer(denominator);
            var next = new Dictionary<(int State, BigInteger Loss), BigInteger>();
            BigInteger hit = 0;
            foreach (((int State, BigInteger Loss) key, BigInteger weight) in running)
            {
                foreach (KernelEdge edge in kernel.Edges[key.State])
                {
                    budget.Spend((1 + weight.GetBitLength() / 64) * (1 + edge.Weight.GetBitLength() / 64));
                    BigInteger loss = key.Loss + edge.Loss;
                    budget.Integer(loss);
                    BigInteger probability = weight * edge.Weight;
                    if (loss * reserve.Denominator > threshold)
                    {
                        hit += probability;
                    }
                    else
                    {
                        (int To, BigInteger loss) target = (edge.To, loss);
                        if (!next.ContainsKey(target))
                        {
                            budget.Support(next.Count + 1 + passages.Count);
                        }

                        next[target] = next.GetValueOrDefault(target) + probability;
                    }
                }
            }

            if (!hit.IsZero)
            {
                budget.Support(next.Count + passages.Count + 1);
                var probability = new Rational(hit, denominator);
                passages.Add(new FirstRuinPassage(round + 1, probability));
                ruined += probability;
                time += (round + 1) * probability;
                budget.Integer(ruined.Denominator);
                budget.Integer(time.Numerator);
                budget.Integer(time.Denominator);
            }

            BigInteger divisor = denominator;
            foreach (BigInteger value in next.Values)
            {
                budget.Spend(1 + value.GetBitLength() / 64);
                divisor = BigInteger.GreatestCommonDivisor(divisor, value);
            }

            if (!divisor.IsOne)
            {
                denominator /= divisor;
                foreach ((int State, BigInteger Loss) key in next.Keys.ToArray())
                {
                    next[key] /= divisor;
                }
            }

            running = next;
        }

        Rational survival = new(running.Values.Aggregate(BigInteger.Zero, (sum, value) => sum + value), denominator);
        if (ruined + survival != Rational.One)
        {
            throw new ArithmeticException("First-passage mass was not conserved.");
        }

        return new ExactRuinResult
        {
            Status = "Exact",
            Probability = ruined,
            SurvivalProbability = survival,
            ConditionalExpectedRound = ruined.IsZero ? null : time / ruined,
            FirstPassages = Array.AsReadOnly(passages.ToArray())
        };
    }

    private static ExactRuinResult NoRuin() => new()
    {
        Status = "Exact",
        Probability = Rational.Zero,
        SurvivalProbability = Rational.One
    };

    private static ExactRuinResult UnknownRuin(string reason) => new()
    {
        Status = "Unavailable",
        Reason = reason
    };

    private static void Require(Rational value)
    {
        if (value.Denominator.Sign <= 0)
        {
            throw new ArgumentException("Use canonical rational inputs.");
        }
    }

    private sealed record KernelEdge(int To, BigInteger Loss, BigInteger Weight);

    private sealed record Kernel(
        PolynomialMatrix Matrix,
        List<KernelEdge>[] Edges,
        BigInteger Denominator,
        BigInteger Scale,
        int Reachable);

    private sealed class PolynomialMatrix(int rows, int columns)
    {
        private readonly int _columns = columns;
        private int _atoms;

        public Dictionary<int, ExposurePolynomial>[] Rows { get; } = Enumerable.Range(0, rows)
            .Select(_ => new Dictionary<int, ExposurePolynomial>()).ToArray();

        public void Add(int row, int column, ExposurePolynomial law, ExposureBudget budget)
        {
            Rows[row].TryGetValue(column, out ExposurePolynomial? prior);
            ExposurePolynomial result = prior is null ? law : ExposurePolynomial.Add(prior, law, budget);
            var count = _atoms + result.Atoms.Count - (prior?.Atoms.Count ?? 0);
            budget.OperatorSupport(count);
            _atoms = count;
            Rows[row][column] = result;
        }

        public PolynomialMatrix Multiply(PolynomialMatrix right, ExposureBudget budget)
        {
            if (_columns != right.Rows.Length)
            {
                throw new ArithmeticException("Operator dimensions differ.");
            }

            var result = new PolynomialMatrix(Rows.Length, right._columns);
            for (var row = 0; row < Rows.Length; row++)
            {
                foreach ((var middle, ExposurePolynomial left) in Rows[row])
                {
                    foreach ((var column, ExposurePolynomial next) in right.Rows[middle])
                    {
                        result.Add(row, column, ExposurePolynomial.Convolve(left, next, budget), budget);
                    }
                }
            }

            return result;
        }
    }
}
