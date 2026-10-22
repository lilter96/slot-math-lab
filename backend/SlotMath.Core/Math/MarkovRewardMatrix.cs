using System.Numerics;

namespace SlotMath.Core.Math;

/// <summary>
///     Unnormalized reward moments on a transition: First[a] = E[R_a; transition],
///     Second[a,b] = E[R_a R_b; transition]. Zero is supplied only for nonnegative rewards.
///     Bounds describe the reward conditional on taking the transition.
/// </summary>
internal sealed record MarkovRewardTransition(
    int From,
    int To,
    Rational Probability,
    Rational[] First,
    Rational[] Second,
    Rational?[] Zero,
    Rational?[] Lower,
    Rational?[] Upper,
    Rational[]? Higher = null);

internal sealed class MarkovMatrixLimitException(string message) : Exception(message);

/// <summary>
///     A sparse finite-horizon Markov reward operator. Multiplication composes successive independent
///     blocks conditional on their intermediate state, including cross moments and zero-reward events.
///     All coefficients share a reduced integer denominator; no floating-point arithmetic is used.
/// </summary>
internal sealed class MarkovRewardMatrix : IDisposable
{
    private readonly int _columns, _amounts, _pairs, _maximumBits;
    private readonly RewardMomentLayout? _higher;
    private readonly Dictionary<int, Weight>[] _rows;
    private readonly ExactStorageTracker.Lease? _slots;
    private readonly Action<long> _spend;
    private readonly ExactStorageTracker? _storage;

    private MarkovRewardMatrix(int rows, int columns, int amounts, Action<long> spend, int maximumBits,
        RewardMomentLayout? higher = null, ExactStorageTracker? storage = null)
    {
        _rows = Enumerable.Range(0, rows).Select(_ => new Dictionary<int, Weight>()).ToArray();
        _columns = columns;
        _amounts = amounts;
        _pairs = amounts * (amounts + 1) / 2;
        _spend = spend;
        _maximumBits = maximumBits;
        _higher = higher;
        _storage = storage;
        _slots = storage?.Hold(ExactStorageKind.MatrixCoefficientSlots, 1);
    }

    private long WeightSlots => 1 + 4L * _amounts + _pairs + (_higher?.HigherCount ?? 0);

    public BigInteger Denominator { get; private set; } = BigInteger.One;
    public long MaximumBits { get; private set; } = 1;

    public void Dispose() => _slots?.Dispose();

    private Weight NewWeight()
    {
        if (_slots is not null)
        {
            _slots.Resize(_slots.Count + WeightSlots);
        }

        return new Weight(_amounts, _pairs, _higher?.HigherCount ?? 0);
    }

    public IReadOnlyDictionary<int, Weight> Row(int index) => _rows[index];

    public static MarkovRewardMatrix Create(int states, int amounts,
        IEnumerable<MarkovRewardTransition> transitions, Action<long> spend, int maximumBits = 65_536,
        RewardMomentLayout? higher = null, ExactStorageTracker? storage = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(states);
        ArgumentOutOfRangeException.ThrowIfNegative(amounts);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(amounts, 64);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBits);
        if (higher is not null && higher.Amounts != amounts)
        {
            throw new ArgumentException("Moment channels differ from the transition operator.");
        }

        var matrix = new MarkovRewardMatrix(states, states, amounts, spend, maximumBits, higher, storage);
        var success = false;
        try
        {
            MarkovRewardTransition[] edges = transitions.ToArray();
            foreach (MarkovRewardTransition edge in edges)
            {
                spend(1 + amounts + matrix._pairs + (higher?.HigherCount ?? 0));
                if (edge.From < 0 || edge.From >= states || edge.To < 0 || edge.To >= states ||
                    edge.Probability < Rational.Zero || edge.Probability > Rational.One ||
                    edge.First.Length != amounts || edge.Second.Length != matrix._pairs ||
                    edge.Zero.Length != amounts || edge.Lower.Length != amounts || edge.Upper.Length != amounts ||
                    (edge.Higher?.Length ?? 0) != (higher?.HigherCount ?? 0))
                {
                    throw new ArgumentException("Invalid finite-state reward transition.");
                }

                if (edge.Probability.IsZero)
                {
                    continue;
                }

                foreach (Rational value in edge.First.Concat(edge.Second).Concat(edge.Higher ?? [])
                             .Append(edge.Probability)
                             .Concat(edge.Zero.OfType<Rational>()))
                {
                    if (value.Denominator.Sign <= 0)
                    {
                        throw new ArgumentException("Reward coefficients must be canonical rational numbers.");
                    }

                    matrix.Denominator = Rational.Lcm(matrix.Denominator, value.Denominator);
                    if (matrix.Denominator.GetBitLength() > maximumBits)
                    {
                        throw new MarkovMatrixLimitException("transition denominators exceed the matrix bit budget");
                    }
                }
            }

            foreach (MarkovRewardTransition edge in edges.Where(edge => !edge.Probability.IsZero))
            {
                Weight weight = matrix.NewWeight();
                weight.Mass = matrix.Scale(edge.Probability);
                weight.Ranged = true;
                for (var a = 0; a < amounts; a++)
                {
                    weight.First[a] = matrix.Scale(edge.First[a]);
                    weight.Zero[a] = edge.Zero[a] is { } zero ? matrix.Scale(zero) : null;
                    weight.Lower[a] = edge.Lower[a];
                    weight.Upper[a] = edge.Upper[a];
                }

                for (var pair = 0; pair < matrix._pairs; pair++)
                {
                    weight.Second[pair] = matrix.Scale(edge.Second[pair]);
                }

                for (var extra = 0; extra < weight.Higher.Length; extra++)
                {
                    weight.Higher[extra] = matrix.Scale(edge.Higher![extra]);
                }

                matrix.Add(edge.From, edge.To, weight);
            }

            foreach (Dictionary<int, Weight> row in matrix._rows)
            {
                if (row.Values.Aggregate(BigInteger.Zero, (sum, weight) => sum + weight.Mass) > matrix.Denominator)
                {
                    throw new ArgumentException("Transition row mass exceeds one.");
                }
            }

            matrix.Reduce();
            success = true;
            return matrix;
        }
        finally
        {
            if (!success)
            {
                matrix.Dispose();
            }
        }
    }

    /// <summary>
    ///     Advance exactly steps transitions, unless the caller certifies an earlier stopping point.
    ///     Increasing power-of-two blocks allow that certificate to be checked before building huge powers.
    ///     The returned row retains the joint reward moments for every destination state.
    /// </summary>
    public (MarkovRewardMatrix Row, int Steps) Advance(int initial, int steps,
        Func<int, MarkovRewardMatrix, bool>? stop = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(steps);
        if (_rows.Length != _columns || initial < 0 || initial >= _rows.Length)
        {
            throw new ArgumentException("Use a square transition operator and a valid initial state.");
        }

        var row = new MarkovRewardMatrix(1, _columns, _amounts, _spend, _maximumBits, _higher, _storage);
        Weight unit = row.NewWeight();
        unit.Mass = BigInteger.One;
        unit.Ranged = true;
        for (var a = 0; a < _amounts; a++)
        {
            unit.Zero[a] = BigInteger.One;
            unit.Lower[a] = unit.Upper[a] = Rational.Zero;
        }

        row._rows[0][initial] = unit;
        var powers = new List<MarkovRewardMatrix>
        {
            this
        };
        var completed = 0;
        var level = 0;
        var success = false;
        try
        {
            while (completed < steps)
            {
                var block = 1L << level;
                if (block > steps - completed)
                {
                    level--;
                    continue;
                }

                if (level == powers.Count)
                {
                    powers.Add(powers[^1].Multiply(powers[^1]));
                }

                MarkovRewardMatrix next = row.Multiply(powers[level]);
                row.Dispose();
                row = next;
                completed += (int)block;
                if (completed < steps && stop?.Invoke(completed, row) == true)
                {
                    break;
                }

                if (block << 1 <= steps - completed)
                {
                    level++;
                }
            }

            success = true;
            return (row, completed);
        }
        finally
        {
            foreach (MarkovRewardMatrix power in powers)
            {
                if (!ReferenceEquals(power, this))
                {
                    power.Dispose();
                }
            }

            if (!success)
            {
                row.Dispose();
            }
        }
    }

    private MarkovRewardMatrix Multiply(MarkovRewardMatrix right)
    {
        var result =
            new MarkovRewardMatrix(_rows.Length, right._columns, _amounts, _spend, _maximumBits, _higher, _storage)
            {
                Denominator = Denominator * right.Denominator
            };
        var success = false;
        try
        {
            var cost = (1 + MaximumBits / 256) * (1 + right.MaximumBits / 256) *
                       (1 + 3L * _amounts + 4L * _pairs + (_higher?.Products ?? 0));
            _spend(1);
            for (var row = 0; row < _rows.Length; row++)
            {
                foreach ((var middle, Weight leftWeight) in _rows[row])
                {
                    foreach ((var column, Weight rightWeight) in right._rows[middle])
                    {
                        _spend(cost);
                        if (!result._rows[row].TryGetValue(column, out Weight? into))
                        {
                            result._rows[row][column] = into = result.NewWeight();
                        }

                        if (_higher is { } layout)
                        {
                            BigInteger Value(Weight weight, int index)
                            {
                                return index == 0 ? weight.Mass :
                                    index <= _amounts ? weight.First[index - 1] :
                                    index < layout.BaseCount ? weight.Second[index - 1 - _amounts] :
                                    weight.Higher[index - layout.BaseCount];
                            }

                            for (var extra = 0; extra < layout.HigherCount; extra++)
                            {
                                foreach (RewardMomentLayout.Split split in layout.Splits[extra])
                                {
                                    into.Higher[extra] += split.Multiplicity * Value(leftWeight, split.Left) *
                                                          Value(rightWeight, split.Right);
                                }
                            }
                        }

                        into.Mass += leftWeight.Mass * rightWeight.Mass;
                        var pair = 0;
                        for (var a = 0; a < _amounts; a++)
                        {
                            into.First[a] += leftWeight.First[a] * rightWeight.Mass +
                                             leftWeight.Mass * rightWeight.First[a];
                            into.Zero[a] = into.Zero[a] is { } current && leftWeight.Zero[a] is { } leftZero &&
                                           rightWeight.Zero[a] is { } rightZero
                                ? current + leftZero * rightZero
                                : null;
                            Rational? lower = leftWeight.Lower[a] is { } ll && rightWeight.Lower[a] is { } rl
                                ? ll + rl
                                : null;
                            Rational? upper = leftWeight.Upper[a] is { } lu && rightWeight.Upper[a] is { } ru
                                ? lu + ru
                                : null;
                            into.Lower[a] = Hull(into.Lower[a], lower, into.Ranged, true);
                            into.Upper[a] = Hull(into.Upper[a], upper, into.Ranged, false);
                            for (var b = a; b < _amounts; b++, pair++)
                            {
                                into.Second[pair] += leftWeight.Second[pair] * rightWeight.Mass +
                                                     leftWeight.Mass * rightWeight.Second[pair] +
                                                     leftWeight.First[a] * rightWeight.First[b] +
                                                     leftWeight.First[b] * rightWeight.First[a];
                            }
                        }

                        into.Ranged = true;
                    }
                }
            }

            result.Reduce();
            success = true;
            return result;
        }
        finally
        {
            if (!success)
            {
                result.Dispose();
            }
        }
    }

    private static Rational? Hull(Rational? previous, Rational? next, bool ranged, bool minimum)
        => !ranged ? next
            : previous is not { } p || next is not { } n ? null
            : minimum ? p < n ? p : n
            : p > n ? p : n;

    private BigInteger Scale(Rational value) => value.Numerator * (Denominator / value.Denominator);

    private void Add(int row, int column, Weight weight)
    {
        if (!_rows[row].TryGetValue(column, out Weight? into))
        {
            _rows[row][column] = weight;
            return;
        }

        if (_slots is not null)
        {
            _slots.Resize(_slots.Count - WeightSlots);
        }

        into.Mass += weight.Mass;
        for (var a = 0; a < _amounts; a++)
        {
            into.First[a] += weight.First[a];
            into.Zero[a] = into.Zero[a] is { } x && weight.Zero[a] is { } y ? x + y : null;
            into.Lower[a] = Hull(into.Lower[a], weight.Lower[a], true, true);
            into.Upper[a] = Hull(into.Upper[a], weight.Upper[a], true, false);
        }

        for (var pair = 0; pair < _pairs; pair++)
        {
            into.Second[pair] += weight.Second[pair];
        }

        for (var extra = 0; extra < into.Higher.Length; extra++)
        {
            into.Higher[extra] += weight.Higher[extra];
        }
    }

    private void Reduce()
    {
        BigInteger divisor = Denominator;
        foreach (Weight weight in _rows.SelectMany(row => row.Values))
        {
            divisor = BigInteger.GreatestCommonDivisor(divisor, weight.Mass);
            foreach (BigInteger value in weight.First.Concat(weight.Second).Concat(weight.Higher)
                         .Concat(weight.Zero.OfType<BigInteger>()))
            {
                divisor = BigInteger.GreatestCommonDivisor(divisor, value);
            }
        }

        Denominator /= divisor;
        MaximumBits = Denominator.GetBitLength();
        foreach (Weight weight in _rows.SelectMany(row => row.Values))
        {
            weight.Mass /= divisor;
            MaximumBits = System.Math.Max(MaximumBits, weight.Mass.GetBitLength());
            for (var a = 0; a < _amounts; a++)
            {
                weight.First[a] /= divisor;
                weight.Zero[a] /= divisor;
            }

            for (var pair = 0; pair < _pairs; pair++)
            {
                weight.Second[pair] /= divisor;
            }

            for (var extra = 0; extra < weight.Higher.Length; extra++)
            {
                weight.Higher[extra] /= divisor;
            }

            foreach (BigInteger value in weight.First.Concat(weight.Second).Concat(weight.Higher)
                         .Concat(weight.Zero.OfType<BigInteger>()))
            {
                MaximumBits = System.Math.Max(MaximumBits, BigInteger.Abs(value).GetBitLength());
            }
        }

        _spend((1 + MaximumBits / 256) * (1 + _rows.Sum(row => row.Count)));
        if (MaximumBits > _maximumBits)
        {
            throw new MarkovMatrixLimitException("finite-horizon coefficients exceed the matrix bit budget");
        }
    }

    internal sealed class Weight(int amounts, int pairs, int higher = 0)
    {
        public BigInteger Mass;
        public bool Ranged;
        public BigInteger[] First { get; } = new BigInteger[amounts];
        public BigInteger[] Second { get; } = new BigInteger[pairs];
        public BigInteger[] Higher { get; } = higher == 0 ? [] : new BigInteger[higher];
        public BigInteger?[] Zero { get; } = Enumerable.Repeat<BigInteger?>(BigInteger.Zero, amounts).ToArray();
        public Rational?[] Lower { get; } = new Rational?[amounts];
        public Rational?[] Upper { get; } = new Rational?[amounts];
    }
}
