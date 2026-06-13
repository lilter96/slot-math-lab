using System.Numerics;

namespace SlotMath.Core.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  Dist<T> — Exact discrete probability distribution
//
//  Each outcome carries a value of type T and an exact rational probability
//  (BigInteger numerator over a shared BigInteger denominator).
//
//  Produced by the exact interpreter; consumed by metric reducers (G7) and
//  the regime/budget layer (G8).
//
//  Pruned mass tracks probability that was removed by epsilon pruning,
//  enabling interval estimates [lo, hi] for expected values.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// An immutable discrete probability distribution with exact rational
/// (BigInteger) probabilities.  All entries share a common denominator.
/// </summary>
public sealed class Dist<T>
{
    private readonly Entry[] _entries;

    /// <summary>Outcome entries, each with a value and probability numerator.</summary>
    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>Common probability denominator shared by all entries.</summary>
    public BigInteger Denominator { get; }

    /// <summary>
    /// Sum of all entry numerators.  With no pruning this equals Denominator
    /// (the distribution is a proper probability distribution).  With pruning
    /// it is less than Denominator.
    /// </summary>
    public BigInteger TotalNumerator { get; }

    /// <summary>Numerator of the pruned probability mass (0 when no pruning).</summary>
    public BigInteger PrunedNumerator { get; }

    /// <summary>Denominator of the pruned probability mass.</summary>
    public BigInteger PrunedDenominator { get; }

    /// <summary>True when no pruning was applied.</summary>
    public bool IsFullyExact => PrunedNumerator == 0;

    /// <summary>
    /// True when the distribution is empty (all mass was pruned or the
    /// computation produced no outcomes).
    /// </summary>
    public bool IsEmpty => _entries.Length == 0;

    /// <summary>Number of distinct outcomes.</summary>
    public int Count => _entries.Length;

    internal Dist(Entry[] entries, BigInteger denominator, BigInteger totalNumerator,
                  BigInteger prunedNumerator, BigInteger prunedDenominator)
    {
        _entries = entries;
        Denominator = denominator;
        TotalNumerator = totalNumerator;
        PrunedNumerator = prunedNumerator;
        PrunedDenominator = prunedDenominator;
    }

    /// <summary>A single outcome in the distribution.</summary>
    public readonly record struct Entry(T Value, BigInteger Numerator);

    /// <summary>
    /// Expected value of f(value) over this distribution.
    /// Returns (numerator, denominator) as a reduced rational.
    /// Normalized over the unpruned probability mass.
    /// </summary>
    public (BigInteger Num, BigInteger Den) ExpectedValue(Func<T, BigInteger> f)
    {
        if (_entries.Length == 0 || TotalNumerator == 0)
            return (0, 1);

        var sum = BigInteger.Zero;
        foreach (var e in _entries)
            sum += f(e.Value) * e.Numerator;

        // EV = sum / TotalNumerator — normalized over unpruned mass only.
        return Rational.Reduce(sum, TotalNumerator);
    }

    /// <summary>
    /// Expected value when T itself is BigInteger (win amount).
    /// Returns (numerator, denominator) as a reduced rational.
    /// </summary>
    public (BigInteger Num, BigInteger Den) ExpectedBigIntegerValue()
    {
        if (typeof(T) != typeof(BigInteger))
            throw new InvalidOperationException("ExpectedBigIntegerValue requires T = BigInteger.");

        if (_entries.Length == 0 || TotalNumerator == 0)
            return (0, 1);

        var sum = BigInteger.Zero;
        foreach (var e in _entries)
        {
            var val = (BigInteger)(object)e.Value!;
            sum += val * e.Numerator;
        }

        return Rational.Reduce(sum, TotalNumerator);
    }

    /// <summary>
    /// Raw (unnormalized) sum of f(value) * numerator over all entries.
    /// </summary>
    private BigInteger RawSum(Func<T, BigInteger> f)
    {
        var sum = BigInteger.Zero;
        foreach (var e in _entries)
            sum += f(e.Value) * e.Numerator;
        return sum;
    }

    /// <summary>
    /// Interval [lo, hi] for the expected value accounting for pruned mass.
    /// lo assumes pruned branches contribute minValue each;
    /// hi assumes pruned branches contribute maxValue each.
    ///
    /// Returns ((loNum, loDen), (hiNum, hiDen)) as reduced rationals.
    /// </summary>
    public ((BigInteger Num, BigInteger Den) Lo, (BigInteger Num, BigInteger Den) Hi)
        ExpectedValueInterval(Func<T, BigInteger> f, BigInteger minValue, BigInteger maxValue)
    {
        var sum = RawSum(f);

        if (PrunedNumerator == 0)
        {
            if (TotalNumerator == 0) return ((0, 1), (0, 1));
            var r = Rational.Reduce(sum, TotalNumerator);
            return (r, r);
        }

        // Build the combined expected value as a single rational.
        // Raw EV from unpruned = sum / Denominator
        // Pruned contribution ∈ [PrunedNumerator * min / PrunedDenominator,
        //                       PrunedNumerator * max / PrunedDenominator]
        var commonDen = Rational.Lcm(Denominator, PrunedDenominator);
        var unprunedScaled = sum * (commonDen / Denominator);
        var prunedMin = PrunedNumerator * minValue * (commonDen / PrunedDenominator);
        var prunedMax = PrunedNumerator * maxValue * (commonDen / PrunedDenominator);

        var loNum = unprunedScaled + prunedMin;
        var hiNum = unprunedScaled + prunedMax;

        return (Rational.Reduce(loNum, commonDen), Rational.Reduce(hiNum, commonDen));
    }

    /// <summary>
    /// Interval for BigInteger-valued distributions.
    /// </summary>
    public ((BigInteger Num, BigInteger Den) Lo, (BigInteger Num, BigInteger Den) Hi)
        ExpectedBigIntegerValueInterval(BigInteger minValue, BigInteger maxValue)
    {
        if (typeof(T) != typeof(BigInteger))
            throw new InvalidOperationException("ExpectedBigIntegerValueInterval requires T = BigInteger.");

        if (_entries.Length == 0)
            return ((0, 1), (0, 1));

        var sum = BigInteger.Zero;
        foreach (var e in _entries)
        {
            var val = (BigInteger)(object)e.Value!;
            sum += val * e.Numerator;
        }

        if (PrunedNumerator == 0)
        {
            if (TotalNumerator == 0) return ((0, 1), (0, 1));
            var r = Rational.Reduce(sum, TotalNumerator);
            return (r, r);
        }

        var commonDen = Rational.Lcm(Denominator, PrunedDenominator);
        var unprunedScaled = sum * (commonDen / Denominator);
        var prunedMin = PrunedNumerator * minValue * (commonDen / PrunedDenominator);
        var prunedMax = PrunedNumerator * maxValue * (commonDen / PrunedDenominator);

        var loNum = unprunedScaled + prunedMin;
        var hiNum = unprunedScaled + prunedMax;

        return (Rational.Reduce(loNum, commonDen), Rational.Reduce(hiNum, commonDen));
    }

    /// <summary>Human-readable representation for debugging.</summary>
    public override string ToString()
    {
        if (_entries.Length == 0)
            return $"Dist<{typeof(T).Name}>[empty, pruned={PrunedNumerator}/{PrunedDenominator}]";

        var entries = string.Join(", ", _entries.Take(5).Select(e =>
            $"{e.Value}:{e.Numerator}/{Denominator}"));
        if (_entries.Length > 5)
            entries += ", ...";

        var pruned = PrunedNumerator > 0
            ? $", pruned={PrunedNumerator}/{PrunedDenominator}"
            : "";
        return $"Dist<{typeof(T).Name}>[{entries}{pruned}]";
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DistBuilder<T> — Mutable builder for constructing Dist<T> incrementally
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Mutable builder that accumulates outcome → probability entries and
/// produces an immutable <see cref="Dist{T}"/> with a shared denominator.
/// </summary>
public sealed class DistBuilder<T> where T : notnull
{
    // value → accumulated numerator (before common denominator)
    private readonly Dictionary<T, BigInteger> _map;
    private BigInteger _denominator = 1;
    private BigInteger _prunedNumerator;
    private BigInteger _prunedDenominator = 1;
    private int _entryCount;
    private bool _frozen;

    public DistBuilder()
    {
        _map = new Dictionary<T, BigInteger>();
    }

    /// <summary>Number of distinct outcomes added so far.</summary>
    public int Count => _map.Count;

    /// <summary>Total number of entry adds (including duplicates).</summary>
    public int EntryCount => _entryCount;

    /// <summary>Current pruned mass numerator.</summary>
    public BigInteger PrunedNumerator => _prunedNumerator;

    /// <summary>True when nothing has been added.</summary>
    public bool IsEmpty => _frozen ? throw new InvalidOperationException("Builder frozen") : _map.Count == 0;

    /// <summary>
    /// Add a single outcome with a given probability (num/den).
    /// Probabilities accumulate: adding the same value twice sums the numerators.
    /// </summary>
    public void Add(T value, BigInteger numerator, BigInteger denominator)
    {
        EnsureNotFrozen();

        if (denominator <= 0)
            throw new ArgumentException("Denominator must be positive.");

        // Bring to common denominator.
        if (denominator != _denominator)
            AlignToDenominator(Rational.Lcm(_denominator, denominator));

        var scale = _denominator / denominator;
        var scaledNum = numerator * scale;

        _map.TryGetValue(value, out var existing);
        _map[value] = existing + scaledNum;
        _entryCount++;
    }

    /// <summary>
    /// Add all entries from another distribution, scaled by scaleNum/scaleDen.
    /// </summary>
    public void Add(Dist<T> other, BigInteger scaleNumerator, BigInteger scaleDenominator)
    {
        EnsureNotFrozen();
        if (other.IsEmpty) return;

        var newDen = other.Denominator * scaleDenominator;
        var commonDen = Rational.Lcm(_denominator, newDen);

        if (commonDen != _denominator)
            AlignToDenominator(commonDen);

        var otherScale = commonDen / newDen;
        foreach (var e in other.Entries)
        {
            var scaledNum = e.Numerator * scaleNumerator * otherScale;
            _map.TryGetValue(e.Value, out var existing);
            _map[e.Value] = existing + scaledNum;
        }
        _entryCount += other.Entries.Count;

        // Also carry over pruned mass from other.
        if (other.PrunedNumerator > 0)
        {
            AddPrunedMass(
                other.PrunedNumerator * scaleNumerator,
                other.PrunedDenominator * scaleDenominator);
        }
    }

    /// <summary>
    /// Add probability mass to the pruned accumulator.
    /// </summary>
    public void AddPrunedMass(BigInteger numerator, BigInteger denominator)
    {
        EnsureNotFrozen();
        if (numerator == 0) return;

        var commonDen = Rational.Lcm(_prunedDenominator, denominator);
        var scaleExisting = commonDen / _prunedDenominator;
        var scaleNew = commonDen / denominator;
        _prunedNumerator = _prunedNumerator * scaleExisting + numerator * scaleNew;
        _prunedDenominator = commonDen;
    }

    /// <summary>
    /// Build an immutable <see cref="Dist{T}"/> from the accumulated entries.
    /// </summary>
    public Dist<T> Build()
    {
        _frozen = true;

        if (_map.Count == 0)
        {
            var (pNum, pDen) = Rational.Reduce(_prunedNumerator, _prunedDenominator);
            return new Dist<T>(Array.Empty<Dist<T>.Entry>(), _denominator, 0, pNum, pDen);
        }

        var entries = new Dist<T>.Entry[_map.Count];
        var idx = 0;
        var totalNum = BigInteger.Zero;
        foreach (var kvp in _map)
        {
            entries[idx++] = new Dist<T>.Entry(kvp.Key, kvp.Value);
            totalNum += kvp.Value;
        }

        // Reduce fraction: find GCD of all numerators and denominator.
        var overallGcd = _denominator;
        foreach (var e in entries)
            overallGcd = Rational.Gcd(overallGcd, e.Numerator);
        if (overallGcd > 1)
        {
            for (var i = 0; i < entries.Length; i++)
                entries[i] = new Dist<T>.Entry(entries[i].Value, entries[i].Numerator / overallGcd);
            _denominator /= overallGcd;
            totalNum /= overallGcd;
        }

        var (finalPrunedNum, finalPrunedDen) = Rational.Reduce(_prunedNumerator, _prunedDenominator);

        return new Dist<T>(entries, _denominator, totalNum, finalPrunedNum, finalPrunedDen);
    }

    private void AlignToDenominator(BigInteger newDenominator)
    {
        if (newDenominator == _denominator) return;

        var scale = newDenominator / _denominator;
        var keys = _map.Keys.ToArray();
        foreach (var key in keys)
            _map[key] *= scale;

        _denominator = newDenominator;
    }

    private void EnsureNotFrozen()
    {
        if (_frozen)
            throw new InvalidOperationException("DistBuilder has been frozen by Build().");
    }
}
