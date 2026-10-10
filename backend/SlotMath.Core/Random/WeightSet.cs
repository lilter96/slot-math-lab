using System.Numerics;
using System.Runtime.CompilerServices;

namespace SlotMath.Core.Random;

/// <summary>
///     Immutable outcome weight set supporting integers, rationals, and uniform distributions.
///     Fully immutable: all participating state in equality and hash code is strictly readonly.
/// </summary>
public sealed record WeightSet
{
    private readonly BigInteger[]? _numerators;
    private readonly Lock _gate = new();

    private WeightSet(BigInteger[] numerators, BigInteger numeratorSum, BigInteger denominator)
    {
        _numerators = numerators;
        NumeratorSum = numeratorSum;
        Count = numerators.Length;
        Denominator = denominator;
        IsUniform = false;
    }

    private WeightSet(int uniformCount)
    {
        _numerators = null;
        Count = uniformCount;
        NumeratorSum = new BigInteger(uniformCount);
        Denominator = BigInteger.One;
        IsUniform = true;
    }

    /// <summary>Number of outcomes.</summary>
    public int Count { get; }

    /// <summary>Common denominator. Equal to 1 for pure integer weights.</summary>
    public BigInteger Denominator { get; }

    /// <summary>True when every outcome has equal weight 1 (created via <see cref="Uniform" />).</summary>
    public bool IsUniform { get; }

    /// <summary>Sum of all numerators (always positive).</summary>
    public BigInteger NumeratorSum { get; }

    /// <summary>
    ///     Originally-authored total denominator calculation: NumeratorSum * Denominator.
    ///     Used for recovering the exact unscaled rational mass.
    /// </summary>
    public BigInteger TotalDenominator => NumeratorSum * Denominator;

    /// <summary>Total authored weight mass: NumeratorSum / Denominator.</summary>
    public double TotalWeight => (double)NumeratorSum / (double)Denominator;

    /// <summary>Exact rational representation of the total weight mass: (NumeratorSum, Denominator).</summary>
    public (BigInteger Numerator, BigInteger Denominator) TotalWeightRational => (NumeratorSum, Denominator);

    /// <summary>
    ///     O(1) indexed outcome lookup. Zero allocations even for massive uniform sets.
    /// </summary>
    public BigInteger this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Count);
            return IsUniform ? BigInteger.One : _numerators![index];
        }
    }

    /// <summary>
    ///     Numerators for each outcome. Materialized once, on demand, for uniform sets:
    ///     the exact interpreters read this property for every outcome of a draw.
    ///     For single lookups, prefer indexer this[i], which never materializes.
    /// </summary>
    public BigInteger[] Numerators
    {
        get
        {
            if (_numerators is not null)
            {
                return _numerators;
            }

            BigInteger[]? cached = Volatile.Read(ref field);
            if (cached is not null)
            {
                return cached;
            }

            var materialized = new BigInteger[Count];
            Array.Fill(materialized, BigInteger.One);
            return Interlocked.CompareExchange(ref field, materialized, null) ?? materialized;
        }
    }

    /// <summary>
    ///     Cached Walker–Vose alias table.
    ///     Lock-free volatile read on the hot path after first build.
    /// </summary>
    public AliasMethod AliasTable
    {
        get
        {
            AliasMethod? alias = Volatile.Read(ref field);
            if (alias is not null)
            {
                return alias;
            }

            lock (_gate)
            {
                return field ??= AliasMethod.Build(this);
            }
        }
    }

    // ── Structural Equality (Pure & Allocation-Free) ──────────────────────

    public bool Equals(WeightSet? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || Count != other.Count || Denominator != other.Denominator)
        {
            return false;
        }

        if (IsUniform && other.IsUniform)
        {
            return true;
        }

        if (NumeratorSum != other.NumeratorSum)
        {
            return false;
        }

        // If one is uniform and the other is not:
        if (IsUniform != other.IsUniform)
        {
            BigInteger[] nonUniform = _numerators ?? other._numerators!;
            foreach (BigInteger n in nonUniform)
            {
                if (n != BigInteger.One)
                {
                    return false;
                }
            }

            return true;
        }

        return _numerators.AsSpan().SequenceEqual(other._numerators);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Count);
        hash.Add(Denominator);
        hash.Add(NumeratorSum);

        // Uniform(n) equals an explicit set of n ones, so the two must hash alike:
        // neither contributes its elements.
        if (_numerators is not null && NumeratorSum != Count)
        {
            foreach (BigInteger n in _numerators)
            {
                hash.Add(n);
            }
        }

        return hash.ToHashCode();
    }

    // ── Factory Methods ──────────────────────────────────────────────────

    public static WeightSet Uniform(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return new WeightSet(count);
    }

    public static WeightSet FromIntegers(ReadOnlySpan<int> weights)
    {
        if (weights.IsEmpty)
        {
            throw new ArgumentException("Weights collection cannot be empty.", nameof(weights));
        }

        BigInteger[] numerators = GC.AllocateUninitializedArray<BigInteger>(weights.Length);
        BigInteger sum = BigInteger.Zero;

        for (var i = 0; i < weights.Length; i++)
        {
            var w = weights[i];
            if (w < 0)
            {
                throw new ArgumentException($"Weight at index {i} cannot be negative.", nameof(weights));
            }

            var bigW = new BigInteger(w);
            numerators[i] = bigW;
            sum += bigW;
        }

        return new WeightSet(numerators, sum, BigInteger.One);
    }

    public static WeightSet FromIntegers(ReadOnlySpan<long> weights)
    {
        if (weights.IsEmpty)
        {
            throw new ArgumentException("Weights collection cannot be empty.", nameof(weights));
        }

        BigInteger[] numerators = GC.AllocateUninitializedArray<BigInteger>(weights.Length);
        BigInteger sum = BigInteger.Zero;

        for (var i = 0; i < weights.Length; i++)
        {
            var w = weights[i];
            if (w < 0)
            {
                throw new ArgumentException($"Weight at index {i} cannot be negative.", nameof(weights));
            }

            var bigW = new BigInteger(w);
            numerators[i] = bigW;
            sum += bigW;
        }

        return new WeightSet(numerators, sum, BigInteger.One);
    }

    public static WeightSet FromNumerators(ReadOnlySpan<BigInteger> numerators) =>
        FromRationals(numerators, BigInteger.One);

    public static WeightSet FromRationals(ReadOnlySpan<BigInteger> numerators, BigInteger denominator)
    {
        if (numerators.IsEmpty)
        {
            throw new ArgumentException("Numerators collection cannot be empty.", nameof(numerators));
        }

        if (denominator <= BigInteger.Zero)
        {
            throw new ArgumentException("Denominator must be strictly positive.", nameof(denominator));
        }

        BigInteger[] copy = GC.AllocateUninitializedArray<BigInteger>(numerators.Length);
        BigInteger sum = BigInteger.Zero;

        for (var i = 0; i < numerators.Length; i++)
        {
            BigInteger n = numerators[i];
            if (n < BigInteger.Zero)
            {
                throw new ArgumentException($"Weight at index {i} cannot be negative.", nameof(numerators));
            }

            copy[i] = n;
            sum += n;
        }

        return new WeightSet(copy, sum, denominator);
    }

    public static WeightSet FromRationalStrings(string[] weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Length == 0)
        {
            throw new ArgumentException("Weights collection cannot be empty.", nameof(weights));
        }

        var rawNums = new BigInteger[weights.Length];
        var rawDenoms = new BigInteger[weights.Length];
        BigInteger commonDenom = BigInteger.One;

        for (var i = 0; i < weights.Length; i++)
        {
            ReadOnlySpan<char> span = weights[i].AsSpan().Trim();
            var slashIndex = span.IndexOf('/');

            if (slashIndex < 0)
            {
                rawNums[i] = BigInteger.Parse(span);
                rawDenoms[i] = BigInteger.One;
            }
            else
            {
                if (span[(slashIndex + 1)..].Contains('/'))
                {
                    throw new ArgumentException($"Invalid rational format at index {i}: '{weights[i]}'");
                }

                rawNums[i] = BigInteger.Parse(span[..slashIndex].Trim());
                BigInteger denom = BigInteger.Parse(span[(slashIndex + 1)..].Trim());
                if (denom <= BigInteger.Zero)
                {
                    throw new ArgumentException($"Denominator must be positive at index {i}: '{weights[i]}'");
                }

                rawDenoms[i] = denom;
                commonDenom = Lcm(commonDenom, denom);
            }

            if (rawNums[i] < BigInteger.Zero)
            {
                throw new ArgumentException($"Numerator cannot be negative at index {i}: '{weights[i]}'");
            }
        }

        BigInteger[] scaledNums = GC.AllocateUninitializedArray<BigInteger>(weights.Length);
        BigInteger sum = BigInteger.Zero;

        for (var i = 0; i < weights.Length; i++)
        {
            BigInteger scale = commonDenom / rawDenoms[i];
            BigInteger val = rawNums[i] * scale;
            scaledNums[i] = val;
            sum += val;
        }

        return new WeightSet(scaledNums, sum, commonDenom);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BigInteger Lcm(BigInteger a, BigInteger b)
    {
        if (a.IsZero || b.IsZero)
        {
            return BigInteger.Zero;
        }

        return a / BigInteger.GreatestCommonDivisor(a, b) * b;
    }
}
