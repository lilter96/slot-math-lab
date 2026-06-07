using System.Numerics;

namespace SlotMath.Core.Random;

/// <summary>
/// Represents a set of outcome weights — accepted as integers or rationals.
/// The exact path reads weights directly; the sampled path normalises them
/// into probabilities for alias construction.
/// </summary>
public sealed record WeightSet
{
    private WeightSet(BigInteger[] numerators, BigInteger denominator)
    {
        Numerators = numerators;
        Denominator = denominator;
    }

    /// <summary>Numerators for each outcome.</summary>
    public BigInteger[] Numerators { get; }

    /// <summary>Common denominator. 1 for pure integer weights.</summary>
    public BigInteger Denominator { get; }

    /// <summary>Number of outcomes.</summary>
    public int Count => Numerators.Length;

    /// <summary>Sum of all numerators.</summary>
    public BigInteger NumeratorSum => Numerators.Aggregate(BigInteger.Zero, (a, b) => a + b);

    /// <summary>
    /// Rational probability for outcome i = Numerators[i] / (NumeratorSum * Denominator).
    /// Useful for building alias tables or computing exact distributions.
    /// </summary>
    public BigInteger TotalDenominator => NumeratorSum * Denominator;

    // ── Structural equality (BigInteger[] defaults to reference equality) ──

    public bool Equals(WeightSet? other) =>
        other is not null
        && Denominator == other.Denominator
        && Numerators.Length == other.Numerators.Length
        && Numerators.SequenceEqual(other.Numerators);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Denominator);
        foreach (var n in Numerators)
            hash.Add(n);
        return hash.ToHashCode();
    }

    // ── Factory methods ──────────────────────────────────────────────────

    /// <summary>Create from integer weights.</summary>
    public static WeightSet FromIntegers(int[] weights) =>
        new(weights.Select(w => new BigInteger(w)).ToArray(), BigInteger.One);

    /// <summary>Create from long weights.</summary>
    public static WeightSet FromIntegers(long[] weights) =>
        new(weights.Select(w => new BigInteger(w)).ToArray(), BigInteger.One);

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
