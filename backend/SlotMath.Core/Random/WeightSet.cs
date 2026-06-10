using System.Numerics;

namespace SlotMath.Core.Random;

/// <summary>
/// Represents a set of outcome weights — accepted as integers or rationals.
/// The exact path reads weights directly; the sampled path normalises them
/// into probabilities for alias construction.
///
/// The probability of outcome i is always <c>Numerators[i] / NumeratorSum</c>:
/// a shared per-weight denominator cancels out of the normalisation and is
/// kept only so the originally-authored rational weights can be recovered.
///
/// Instances are immutable; the numerator sum and the alias table are
/// computed once and cached, so repeated sampling from the same WeightSet
/// is O(1) per sample with no rebuild cost.
/// </summary>
public sealed record WeightSet
{
    private BigInteger[]? _numerators;
    private readonly int _uniformCount;
    private BigInteger _numeratorSum;
    private bool _sumComputed;
    private AliasMethod? _alias;

    private WeightSet(BigInteger[] numerators, BigInteger denominator)
    {
        foreach (var n in numerators)
        {
            if (n < 0)
                throw new ArgumentException("Weights must be non-negative.", nameof(numerators));
        }

        _numerators = numerators;
        _uniformCount = numerators.Length;
        Denominator = denominator;
    }

    private WeightSet(int uniformCount)
    {
        _uniformCount = uniformCount;
        _numeratorSum = uniformCount;
        _sumComputed = true;
        IsUniform = true;
        Denominator = BigInteger.One;
    }

    /// <summary>Numerators for each outcome (materialised lazily for uniform sets).</summary>
    public BigInteger[] Numerators
    {
        get
        {
            var nums = _numerators;
            if (nums is null)
            {
                nums = new BigInteger[_uniformCount];
                Array.Fill(nums, BigInteger.One);
                _numerators = nums;
            }
            return nums;
        }
    }

    /// <summary>Common denominator. 1 for pure integer weights.</summary>
    public BigInteger Denominator { get; }

    /// <summary>True when every outcome has weight 1 (created via <see cref="Uniform"/>).</summary>
    public bool IsUniform { get; }

    /// <summary>Number of outcomes.</summary>
    public int Count => _uniformCount;

    /// <summary>Sum of all numerators (computed once, then cached).</summary>
    public BigInteger NumeratorSum
    {
        get
        {
            if (!_sumComputed)
            {
                var sum = BigInteger.Zero;
                foreach (var n in Numerators)
                    sum += n;
                _numeratorSum = sum;
                _sumComputed = true;
            }
            return _numeratorSum;
        }
    }

    /// <summary>
    /// The originally-authored weight mass = NumeratorSum / Denominator,
    /// expressed over the common denominator.  Note that probabilities do
    /// NOT use this: p(i) = Numerators[i] / NumeratorSum (the denominator
    /// cancels out of the normalisation).
    /// </summary>
    public BigInteger TotalDenominator => NumeratorSum * Denominator;

    /// <summary>
    /// The Walker–Vose alias table for this weight set, built on first use
    /// and cached.  Sampling through the cached table is deterministic and
    /// identical to sampling through a freshly-built one.
    /// </summary>
    public AliasMethod AliasTable => _alias ??= AliasMethod.Build(this);

    // ── Structural equality (BigInteger[] defaults to reference equality) ──

    public bool Equals(WeightSet? other) =>
        other is not null
        && Denominator == other.Denominator
        && Count == other.Count
        && Numerators.AsSpan().SequenceEqual(other.Numerators);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Denominator);
        foreach (var n in Numerators)
            hash.Add(n);
        return hash.ToHashCode();
    }

    // ── Factory methods ──────────────────────────────────────────────────

    /// <summary>
    /// Create a uniform weight set of <paramref name="count"/> outcomes, each
    /// with weight 1.  Avoids materialising large arrays and alias tables —
    /// sampling is a single bounded uniform draw.
    /// </summary>
    public static WeightSet Uniform(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return new WeightSet(count);
    }

    /// <summary>Create from integer weights.</summary>
    public static WeightSet FromIntegers(int[] weights) =>
        new(Array.ConvertAll(weights, w => new BigInteger(w)), BigInteger.One);

    /// <summary>Create from long weights.</summary>
    public static WeightSet FromIntegers(long[] weights) =>
        new(Array.ConvertAll(weights, w => new BigInteger(w)), BigInteger.One);

    /// <summary>
    /// Create from BigInteger numerators with denominator = 1.
    /// Each weight = numerator / 1.
    /// </summary>
    public static WeightSet FromNumerators(BigInteger[] numerators) =>
        new(numerators, BigInteger.One);

    /// <summary>
    /// Create from explicit rational weights.
    /// Each weight = numerators[i] / denominator.
    /// </summary>
    public static WeightSet FromRationals(BigInteger[] numerators, BigInteger denominator) =>
        new(numerators, denominator);

    /// <summary>
    /// Create from weights specified as rational strings (e.g. "3/2", "5", "1/4").
    /// Mixed denominators are normalised to their least common multiple.
    /// </summary>
    public static WeightSet FromRationalStrings(string[] weights)
    {
        // First pass: parse all entries, collecting per-outcome numerators and denominators
        var rawNums = new BigInteger[weights.Length];
        var rawDenoms = new BigInteger[weights.Length]; // 1 for integer entries

        for (var i = 0; i < weights.Length; i++)
        {
            var parts = weights[i].Split('/');
            if (parts.Length == 1)
            {
                rawNums[i] = BigInteger.Parse(parts[0].Trim());
                rawDenoms[i] = BigInteger.One;
            }
            else if (parts.Length == 2)
            {
                rawNums[i] = BigInteger.Parse(parts[0].Trim());
                var denom = BigInteger.Parse(parts[1].Trim());
                if (denom <= 0)
                    throw new ArgumentException($"Denominator must be positive: {weights[i]}");
                rawDenoms[i] = denom;
            }
            else
            {
                throw new ArgumentException($"Invalid rational format: {weights[i]}");
            }
        }

        // Compute LCM of all denominators
        var commonDenom = BigInteger.One;
        for (var i = 0; i < weights.Length; i++)
        {
            commonDenom = Lcm(commonDenom, rawDenoms[i]);
        }

        // Scale numerators to the common denominator
        var numerators = new BigInteger[weights.Length];
        for (var i = 0; i < weights.Length; i++)
        {
            var scale = commonDenom / rawDenoms[i];
            numerators[i] = rawNums[i] * scale;
        }

        return new WeightSet(numerators, commonDenom);
    }

    private static BigInteger Gcd(BigInteger a, BigInteger b)
    {
        a = BigInteger.Abs(a);
        b = BigInteger.Abs(b);
        while (b != 0)
        {
            var t = b;
            b = a % b;
            a = t;
        }
        return a;
    }

    private static BigInteger Lcm(BigInteger a, BigInteger b) =>
        a / Gcd(a, b) * b;
}
