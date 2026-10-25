using System.Numerics;
using SlotMath.Core.Expressions;

namespace SlotMath.Core.Random;

/// <summary>Bounded exact categorical weights. Order and zero positions never change.</summary>
internal static class ExactWeightVector
{
    internal const int MaxLength = 4096;
    internal const int MaxItemBits = 8192;
    internal const int MaxInputBits = 65_536;
    internal const int MaxDenominatorBits = 16_384;
    internal const int MaxScaledBits = 262_144;
    internal const string Location = "draw.weights";

    internal static IReadOnlyList<ExprValue> Validate(ExprValue vector, int expected,
        out long inputBits, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (vector.Kind != ExprType.Array)
        {
            throw Error("EVAL_TYPE_ERROR", "A weight vector must be an array of Numbers.");
        }

        IReadOnlyList<ExprValue> items = vector.ArrayValue!;
        if (items.Count != expected || items.Count == 0)
        {
            throw Error("EVAL_WEIGHT_LENGTH", $"A weight vector requires exactly {expected} entries.");
        }

        if (items.Count > MaxLength)
        {
            throw Budget();
        }

        inputBits = 0;
        var positive = false;
        for (var index = 0; index < items.Count; index++)
        {
            if ((index & 63) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }

            ExprValue item = items[index];
            if (item.Kind != ExprType.Number)
            {
                throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "A weight must be a Number.",
                    $"{Location}[{index}]");
            }

            if (item.NumberNumerator.Sign < 0)
            {
                throw new ExpressionEvaluationException("EVAL_WEIGHT_NEGATIVE", "Weights must be nonnegative.",
                    $"{Location}[{index}]");
            }

            var bits = item.NumberNumerator.GetBitLength() + item.NumberDenominator.GetBitLength();
            inputBits += bits;
            if (bits > MaxItemBits || inputBits > MaxInputBits)
            {
                throw Budget();
            }

            positive |= item.NumberNumerator.Sign > 0;
        }

        if (!positive)
        {
            throw Error("EVAL_WEIGHT_ZERO", "At least one weight must be positive.");
        }

        return items;
    }

    internal static WeightSet Build(IReadOnlyList<ExprValue> items, out long numericBits,
        CancellationToken cancellation = default)
    {
        BigInteger denominator = BigInteger.One;
        for (var index = 0; index < items.Count; index++)
        {
            if ((index & 63) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }

            BigInteger next = items[index].NumberDenominator;
            BigInteger factor = next / BigInteger.GreatestCommonDivisor(denominator, next);
            if (denominator.GetBitLength() + factor.GetBitLength() - 1 > MaxDenominatorBits)
            {
                throw Budget();
            }

            denominator *= factor;
            if (denominator.GetBitLength() > MaxDenominatorBits)
            {
                throw Budget();
            }
        }

        long projectedBits = 0;
        foreach (ExprValue item in items)
        {
            projectedBits += item.NumberNumerator.IsZero
                ? 1
                : item.NumberNumerator.GetBitLength()
                + denominator.GetBitLength() - item.NumberDenominator.GetBitLength() + 1;
            if (projectedBits > MaxScaledBits)
            {
                throw Budget();
            }
        }

        var numerators = new BigInteger[items.Count];
        numericBits = denominator.GetBitLength();
        for (var index = 0; index < items.Count; index++)
        {
            if ((index & 63) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }

            ExprValue item = items[index];
            numerators[index] = item.NumberNumerator * (denominator / item.NumberDenominator);
            numericBits += numerators[index].GetBitLength();
        }

        return WeightSet.FromRationals(numerators, denominator);
    }

    private static ExpressionEvaluationException Error(string code, string message) => new(code, message, Location);

    private static ExpressionEvaluationException Budget() => Error("EVAL_WEIGHT_BUDGET",
        "The weight vector exceeds its exact arithmetic budget.");
}

/// <summary>
///     Program-owned bounded LRU. Lookups compare values; retained keys own scalar snapshots,
///     so a caller changing a reused array cannot mutate a dictionary key or alias table.
/// </summary>
internal sealed class ExactWeightVectorCache(
    int capacity = 64,
    int maxPositions = 8192,
    long maxNumericBits = 1_048_576)
{
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = new(new KeyComparer());
    private readonly Lock _gate = new();
    private readonly LinkedList<Entry> _lru = new();
    private long _bits;
    private int _positions;

    internal int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    internal int RetainedPositions
    {
        get
        {
            lock (_gate)
            {
                return _positions;
            }
        }
    }

    internal long RetainedNumericBits
    {
        get
        {
            lock (_gate)
            {
                return _bits;
            }
        }
    }

    internal WeightSet Get(ExprValue vector, int expected, CancellationToken cancellation = default)
    {
        IReadOnlyList<ExprValue> items = ExactWeightVector.Validate(vector, expected, out var inputBits, cancellation);
        var borrowed = new Key(items);
        lock (_gate)
        {
            if (_entries.TryGetValue(borrowed, out LinkedListNode<Entry>? hit))
            {
                _lru.Remove(hit);
                _lru.AddFirst(hit);
                return hit.Value.Weights;
            }

            WeightSet weights = ExactWeightVector.Build(items, out var coefficientBits, cancellation);
            var charge = inputBits + coefficientBits;
            if (capacity <= 0 || items.Count > maxPositions || charge > maxNumericBits)
            {
                return weights;
            }

            while (_entries.Count >= capacity || _positions + items.Count > maxPositions ||
                   _bits + charge > maxNumericBits)
            {
                LinkedListNode<Entry> last = _lru.Last!;
                _entries.Remove(last.Value.Key);
                _lru.RemoveLast();
                _positions -= last.Value.Key.Items.Count;
                _bits -= last.Value.Bits;
            }

            var owned = new Key(items.ToArray());
            var entry = new Entry(owned, weights, charge);
            _entries.Add(owned, _lru.AddFirst(entry));
            _positions += owned.Items.Count;
            _bits += charge;
            return weights;
        }
    }

    private readonly record struct Key(IReadOnlyList<ExprValue> Items);

    private sealed record Entry(Key Key, WeightSet Weights, long Bits);

    private sealed class KeyComparer : IEqualityComparer<Key>
    {
        public bool Equals(Key left, Key right) => left.Items.Count == right.Items.Count
                                                   && left.Items.SequenceEqual(right.Items);

        public int GetHashCode(Key key)
        {
            var hash = new HashCode();
            hash.Add(key.Items.Count);
            foreach (ExprValue item in key.Items)
            {
                hash.Add(item.NumberNumerator);
                hash.Add(item.NumberDenominator);
            }

            return hash.ToHashCode();
        }
    }
}
