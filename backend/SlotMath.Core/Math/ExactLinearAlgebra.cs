using System.Diagnostics;
using System.Numerics;

namespace SlotMath.Core.Math;

public sealed record ExactLinearOptions
{
    public static ExactLinearOptions Default { get; } = new();
    public int MaxStates { get; init; } = 1048576;
    public int MaxCoefficients { get; init; } = 1048576;
    public int MaxTerms { get; init; } = 1024;
    public int MaxFactors { get; init; } = 32;
    public int MaximumBits { get; init; } = 65536;
    public long MaxWork { get; init; } = 40000000;
    public TimeSpan? MaxTime { get; init; }
}

public sealed class ExactLinearLimitException(string message) : InvalidOperationException(message);

public readonly record struct ExactOperatorApplication(long Work, long Products, int ScratchEntries);

public readonly record struct ExactSparseEntry(int From, int To, Rational Value);

internal struct ExactLinearBudget
{
    private readonly CancellationToken _cancellation;
    private readonly long _start;

    public ExactLinearBudget(ExactLinearOptions? options, CancellationToken cancellation)
    {
        Options = options ?? ExactLinearOptions.Default;
        if (Options.MaxStates is < 1 or > 1048576 || Options.MaxCoefficients is < 1 or > 16777216 ||
            Options.MaxTerms is < 1 or > 65536 || Options.MaxFactors is < 1 or > 64 ||
            Options.MaximumBits is < 1 or > 1048576 || Options.MaxWork <= 0 ||
            (Options.MaxTime is { } time && time <= TimeSpan.Zero))
        {
            throw new ArgumentException("Invalid exact linear-operator limits.");
        }

        _cancellation = cancellation;
        _start = Stopwatch.GetTimestamp();
        Work = 0;
        Products = 0;
        Check();
    }

    public ExactLinearOptions Options { get; }

    public long Work { get; private set; }
    public long Products { get; private set; }

    public readonly void Check()
    {
        _cancellation.ThrowIfCancellationRequested();
        if (Options.MaxTime is { } time && Stopwatch.GetElapsedTime(_start) > time)
        {
            throw new ExactLinearLimitException("Exact linear operator exceeded its time allowance.");
        }
    }

    public void Spend(long units = 1)
    {
        _cancellation.ThrowIfCancellationRequested();
        if (units < 0 || units > Options.MaxWork - Work)
        {
            throw new ExactLinearLimitException("Exact linear operator exceeded its work allowance.");
        }

        var before = Work;
        Work += units;
        if (before >> 10 != Work >> 10)
        {
            Check();
        }
    }

    public void Value(Rational value)
    {
        if (value.Denominator.Sign <= 0)
        {
            throw new ArgumentException("Use canonical rational coefficients/vectors.");
        }

        var bits = System.Math.Max(BigInteger.Abs(value.Numerator).GetBitLength(), value.Denominator.GetBitLength());
        Spend(1 + bits / 64);
        if (bits > Options.MaximumBits)
        {
            throw new ExactLinearLimitException("Exact linear operator exceeded its stored-number bit allowance.");
        }
    }

    public Rational Add(Rational a, Rational b)
    {
        Spend(Words(a) * Words(b));
        if (a.IsZero)
        {
            return b;
        }

        if (b.IsZero)
        {
            return a;
        }

        Rational value = a + b;
        Value(value);
        return value;
    }

    public Rational Multiply(Rational a, Rational b)
    {
        Products++;
        Spend(Words(a) * Words(b));
        if (a.IsZero || b.IsZero)
        {
            return Rational.Zero;
        }

        Rational value = a == Rational.One ? b : b == Rational.One ? a : a * b;
        Value(value);
        return value;
    }

    public bool Less(Rational a, Rational b)
    {
        Spend(Words(a) * Words(b));
        return a < b;
    }

    private static long Words(Rational value) =>
        1 + System.Math.Max(BigInteger.Abs(value.Numerator).GetBitLength(), value.Denominator.GetBitLength()) / 64;
}

/// <summary>Immutable sparse nonnegative matrix. Row sums may be rates, subprobabilities or probabilities.</summary>
public sealed class ExactSparseMatrix
{
    private readonly Rational[] _sums;

    private ExactSparseMatrix(Entry[][] rows, Rational[] sums, int nonzero, Rational maximum, bool stochastic,
        bool identity)
    {
        Rows = rows;
        _sums = sums;
        NonzeroEntries = nonzero;
        MaximumRowSum = maximum;
        IsStochastic = stochastic;
        IsIdentity = identity;
    }

    internal Entry[][] Rows { get; }

    public int States => Rows.Length;
    public int NonzeroEntries { get; }
    public bool IsStochastic { get; }
    public bool IsIdentity { get; }
    public Rational MaximumRowSum { get; }

    public IEnumerable<ExactSparseEntry> Entries
    {
        get
        {
            for (var row = 0; row < States; row++)
            {
                foreach (Entry entry in Rows[row])
                {
                    yield return new ExactSparseEntry(row, entry.To, entry.Value);
                }
            }
        }
    }

    public Rational this[int from, int to]
    {
        get
        {
            if (from < 0 || from >= States || to < 0 || to >= States)
            {
                throw new ArgumentOutOfRangeException(nameof(from));
            }

            foreach (Entry entry in Rows[from])
            {
                if (entry.To == to)
                {
                    return entry.Value;
                }

                if (entry.To > to)
                {
                    break;
                }
            }

            return Rational.Zero;
        }
    }

    public Rational RowSum(int row) => _sums[row];

    public static ExactSparseMatrix Create(int states, IReadOnlyList<ExactSparseEntry> entries,
        ExactLinearOptions? options = null, CancellationToken cancellation = default)
    {
        var budget = new ExactLinearBudget(options, cancellation);
        ArgumentNullException.ThrowIfNull(entries);
        if (states < 1 || states > budget.Options.MaxStates || entries.Count > budget.Options.MaxCoefficients)
        {
            throw new ExactLinearLimitException(
                "Sparse matrix dimensions or authored coefficient count exceed the allowance.");
        }

        budget.Spend(3L * states);
        var grouped = new Dictionary<(int From, int To), Rational>();
        foreach (ExactSparseEntry entry in entries)
        {
            budget.Value(entry.Value);
            if (entry.From < 0 || entry.From >= states || entry.To < 0 || entry.To >= states || entry.Value.Sign < 0)
            {
                throw new ArgumentException("Use in-range coordinates and nonnegative matrix entries.");
            }

            if (entry.Value.IsZero)
            {
                continue;
            }

            (int From, int To) key = (entry.From, entry.To);
            grouped[key] = grouped.TryGetValue(key, out Rational prior) ? budget.Add(prior, entry.Value) : entry.Value;
        }

        var lists = new List<Entry>?[states];
        var sums = new Rational[states];
        Array.Fill(sums, Rational.Zero);
        foreach (((int From, int To) key, Rational value) in grouped)
        {
            budget.Spend();
            (lists[key.From] ??= []).Add(new Entry(key.To, value));
            sums[key.From] = budget.Add(sums[key.From], value);
        }

        var rows = new Entry[states][];
        Rational maximum = Rational.Zero;
        var stochastic = true;
        var identity = true;
        for (var row = 0; row < states; row++)
        {
            var count = lists[row]?.Count ?? 0;
            budget.Spend(count * (1L + (count == 0 ? 0 : BitOperations.Log2((uint)count))));
            rows[row] = lists[row]?.OrderBy(entry => entry.To).ToArray() ?? [];
            if (budget.Less(maximum, sums[row]))
            {
                maximum = sums[row];
            }

            stochastic &= sums[row] == Rational.One;
            identity &= rows[row].Length == 1 && rows[row][0].To == row && rows[row][0].Value == Rational.One;
        }

        budget.Check();
        var result = new ExactSparseMatrix(rows, sums, grouped.Count, maximum, stochastic, identity);
        budget.Check();
        return result;
    }

    public static ExactSparseMatrix Identity(int states, ExactLinearOptions? options = null,
        CancellationToken cancellation = default)
    {
        var budget = new ExactLinearBudget(options, cancellation);
        ExactLinearOptions limits = budget.Options;
        if (states < 1 || states > limits.MaxStates || states > limits.MaxCoefficients)
        {
            throw new ExactLinearLimitException("Identity matrix exceeds the allowance.");
        }

        budget.Spend(3L * states);
        return Create(states,
            Enumerable.Range(0, states).Select(index => new ExactSparseEntry(index, index, Rational.One)).ToArray(),
            limits, cancellation);
    }

    /// <summary>Row-vector product. Source and destination cannot overlap. Destination is valid only on successful return.</summary>
    public ExactOperatorApplication Apply(ReadOnlySpan<Rational> source, Span<Rational> destination,
        ExactLinearOptions? options = null, CancellationToken cancellation = default)
    {
        var budget = new ExactLinearBudget(options, cancellation);
        if (NonzeroEntries > budget.Options.MaxCoefficients)
        {
            throw new ExactLinearLimitException("Sparse matrix coefficients exceed the application allowance.");
        }

        ValidateVectors(source, destination, States, ref budget);
        destination.Fill(Rational.Zero);
        for (var row = 0; row < States; row++)
        {
            budget.Spend();
            if (source[row].IsZero)
            {
                continue;
            }

            foreach (Entry entry in Rows[row])
            {
                destination[entry.To] = budget.Add(destination[entry.To], budget.Multiply(source[row], entry.Value));
            }
        }

        budget.Check();
        return new ExactOperatorApplication(budget.Work, budget.Products, 0);
    }

    internal static void ValidateVectors(ReadOnlySpan<Rational> source, Span<Rational> destination, int states,
        ref ExactLinearBudget budget)
    {
        if (states > budget.Options.MaxStates)
        {
            throw new ExactLinearLimitException("Vector dimension exceeds the allowance.");
        }

        if (source.Length != states || destination.Length != states || source.Overlaps(destination))
        {
            throw new ArgumentException("Use matching, nonoverlapping source and destination vectors.");
        }

        foreach (Rational value in source)
        {
            budget.Value(value);
        }
    }

    internal readonly record struct Entry(int To, Rational Value);
}
