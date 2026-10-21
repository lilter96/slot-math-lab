using System.Diagnostics;
using System.Numerics;

namespace SlotMath.Core.Math;

/// <summary>A hard resource failure. No partial distribution is returned or renormalized.</summary>
public sealed class ExactExposureLimitException(string message) : InvalidOperationException(message);

internal sealed class ExposureBudget
{
    private readonly Stopwatch _clock;

    public ExposureBudget(ExactExposureOptions options, CancellationToken cancellation, Stopwatch? clock = null)
    {
        if (options.MaxWork <= 0 || options.RuinWork < 0 || options.MaxAtoms is < 1 or > 1000000 ||
            options.MaxStates is < 1 or > 128 || options.MaximumBits is < 1 or > 1048576 ||
            (options.MaxTime is { } time && time <= TimeSpan.Zero))
        {
            throw new ArgumentException("Invalid exact exposure resource limits.");
        }

        Options = options;
        Cancellation = cancellation;
        _clock = clock ?? Stopwatch.StartNew();
        Check();
    }

    public ExactExposureOptions Options { get; }
    public CancellationToken Cancellation { get; }
    public long Work { get; private set; }
    public long Convolutions { get; set; }
    public long Transforms { get; set; }
    public int LargestLaw { get; private set; }
    public int LargestOperator { get; private set; }

    public void Check()
    {
        Cancellation.ThrowIfCancellationRequested();
        if (Options.MaxTime is { } time && _clock.Elapsed > time)
        {
            throw new ExactExposureLimitException("Exact exposure exceeded its time allowance.");
        }
    }

    public void Spend(long units = 1)
    {
        Cancellation.ThrowIfCancellationRequested();
        if (units < 0 || units > Options.MaxWork - Work)
        {
            throw new ExactExposureLimitException("Exact exposure exceeded its work allowance.");
        }

        var before = Work;
        Work += units;
        if (before >> 10 != Work >> 10)
        {
            Check();
        }
    }

    public void Integer(BigInteger value)
    {
        if (BigInteger.Abs(value).GetBitLength() > Options.MaximumBits)
        {
            throw new ExactExposureLimitException("Exact exposure exceeded its integer bit allowance.");
        }
    }

    public void Support(int count)
    {
        InputSupport(count);
        LargestLaw = System.Math.Max(LargestLaw, count);
    }

    public void InputSupport(int count)
    {
        if (count > Options.MaxAtoms)
        {
            throw new ExactExposureLimitException("Exact exposure exceeded its support allowance.");
        }
    }

    public void OperatorSupport(int count)
    {
        if (count > Options.MaxAtoms)
        {
            throw new ExactExposureLimitException("Exact exposure exceeded its operator support allowance.");
        }

        LargestOperator = System.Math.Max(LargestOperator, count);
    }

    public ExposureBudget? RuinBudget()
    {
        var work = System.Math.Min(Options.RuinWork, Options.MaxWork - Work);
        return work <= 0
            ? null
            : new ExposureBudget(Options with
            {
                MaxWork = work
            }, Cancellation, _clock);
    }
}

/// <summary>Integer exponents, nonnegative joint masses, and a common denominator. May be substochastic.</summary>
internal sealed class ExposurePolynomial
{
    private ExposurePolynomial(Dictionary<BigInteger, BigInteger> atoms, BigInteger denominator)
    {
        Atoms = atoms;
        Denominator = denominator;
    }

    public Dictionary<BigInteger, BigInteger> Atoms { get; }
    public BigInteger Denominator { get; }

    public static ExposurePolynomial Unit { get; } = new(new Dictionary<BigInteger, BigInteger>
    {
        [0] = 1
    }, 1);

    public static ExposurePolynomial Create(Dictionary<BigInteger, BigInteger> atoms, BigInteger denominator,
        ExposureBudget budget)
    {
        budget.Support(atoms.Count);
        budget.Integer(denominator);
        if (denominator.Sign <= 0 || atoms.Count == 0)
        {
            throw new ArithmeticException("Empty or invalid probability polynomial.");
        }

        BigInteger divisor = denominator, mass = 0;
        foreach ((BigInteger value, BigInteger weight) in atoms)
        {
            budget.Spend(1 + weight.GetBitLength() / 64 + BigInteger.Abs(value).GetBitLength() / 64);
            budget.Integer(value);
            if (weight.Sign <= 0)
            {
                throw new ArithmeticException("Probability coefficients must be positive.");
            }

            mass += weight;
            divisor = BigInteger.GreatestCommonDivisor(divisor, weight);
        }

        if (mass > denominator)
        {
            throw new ArithmeticException("Joint law exceeds unit probability.");
        }

        if (!divisor.IsOne)
        {
            denominator /= divisor;
            foreach (BigInteger value in atoms.Keys.ToArray())
            {
                atoms[value] /= divisor;
            }
        }

        return new ExposurePolynomial(atoms, denominator);
    }

    private static void AddWeight(Dictionary<BigInteger, BigInteger> into, BigInteger value, BigInteger weight,
        ExposureBudget budget)
    {
        if (!into.ContainsKey(value))
        {
            budget.Support(into.Count + 1);
        }

        into[value] = into.GetValueOrDefault(value) + weight;
    }

    public static ExposurePolynomial Add(ExposurePolynomial left, ExposurePolynomial right, ExposureBudget budget)
    {
        budget.Check();
        BigInteger denominator = Rational.Lcm(left.Denominator, right.Denominator);
        budget.Integer(denominator);
        BigInteger a = denominator / left.Denominator, b = denominator / right.Denominator;
        var atoms = new Dictionary<BigInteger, BigInteger>(left.Atoms.Count);
        var words = 1 + denominator.GetBitLength() / 64;
        foreach ((BigInteger value, BigInteger weight) in left.Atoms)
        {
            budget.Spend(words);
            AddWeight(atoms, value, weight * a, budget);
        }

        foreach ((BigInteger value, BigInteger weight) in right.Atoms)
        {
            budget.Spend(words);
            AddWeight(atoms, value, weight * b, budget);
        }

        return Create(atoms, denominator, budget);
    }

    public static ExposurePolynomial Convolve(ExposurePolynomial left, ExposurePolynomial right, ExposureBudget budget)
    {
        budget.Check();
        budget.Convolutions++;
        BigInteger denominator = left.Denominator * right.Denominator;
        budget.Integer(denominator);
        var atoms = new Dictionary<BigInteger, BigInteger>();
        if (budget.Options.UseTransformConvolution && Dense(left, right, denominator, budget) is { } transformed)
        {
            return transformed;
        }

        var words = (1 + left.Denominator.GetBitLength() / 64) * (1 + right.Denominator.GetBitLength() / 64);
        foreach ((BigInteger x, BigInteger p) in left.Atoms)
        {
            foreach ((BigInteger y, BigInteger q) in right.Atoms)
            {
                budget.Spend(words);
                AddWeight(atoms, x + y, p * q, budget);
            }
        }

        return Create(atoms, denominator, budget);
    }

    private static ExposurePolynomial? Dense(ExposurePolynomial left, ExposurePolynomial right, BigInteger denominator,
        ExposureBudget budget)
    {
        if (left.Atoms.Count < 64 || right.Atoms.Count < 64)
        {
            return null;
        }

        BigInteger lo = left.Atoms.Keys.Min(), ro = right.Atoms.Keys.Min(), step = 0;
        foreach (BigInteger value in left.Atoms.Keys)
        {
            budget.Spend();
            step = BigInteger.GreatestCommonDivisor(step, value - lo);
        }

        foreach (BigInteger value in right.Atoms.Keys)
        {
            budget.Spend();
            step = BigInteger.GreatestCommonDivisor(step, value - ro);
        }

        if (step.IsZero)
        {
            return null;
        }

        BigInteger a = (left.Atoms.Keys.Max() - lo) / step + 1, b = (right.Atoms.Keys.Max() - ro) / step + 1;
        if (a + b - 1 > System.Math.Min(budget.Options.MaxAtoms, ExactIntegerConvolution.MaximumLength) ||
            a > 8L * left.Atoms.Count || b > 8L * right.Atoms.Count)
        {
            return null;
        }

        var x = new BigInteger[(int)a];
        var y = new BigInteger[(int)b];
        foreach ((BigInteger value, BigInteger weight) in left.Atoms)
        {
            x[(int)((value - lo) / step)] = weight;
        }

        foreach ((BigInteger value, BigInteger weight) in right.Atoms)
        {
            y[(int)((value - ro) / step)] = weight;
        }

        if (!ExactIntegerConvolution.TryTransform(x, y, budget.Spend, out BigInteger[] output))
        {
            return null;
        }

        budget.Transforms++;
        var atoms = new Dictionary<BigInteger, BigInteger>();
        for (var i = 0; i < output.Length; i++)
        {
            if (!output[i].IsZero)
            {
                atoms.Add(lo + ro + step * i, output[i]);
            }
        }

        return Create(atoms, denominator, budget);
    }

    public ExactExposureLaw Analyze(BigInteger scale, ExposureBudget budget)
    {
        var builder = new DistBuilder<BigInteger>();
        foreach ((BigInteger value, BigInteger weight) in Atoms)
        {
            budget.Spend(8 * (1 + weight.GetBitLength() / 64 + 4 * BigInteger.Abs(value).GetBitLength() / 64));
            builder.Add(value, weight, Denominator);
        }

        budget.Check();
        var result = ExactExposureLaw.Create(builder.Build(), scale, budget.Cancellation);
        budget.Check();
        return result;
    }
}
