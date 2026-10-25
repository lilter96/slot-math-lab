namespace SlotMath.Core.Math;

/// <summary>
///     A bounded multivariate raw-moment closure through total degree four. Monomials are indexed
///     by sorted reward-channel multisets; splitting each labelled factor between successive blocks
///     gives the exact multinomial composition, including repeated-index multiplicities.
/// </summary>
internal sealed class RewardMomentLayout
{
    private readonly Dictionary<string, int> _positions;

    private RewardMomentLayout(int amounts, int order)
    {
        Amounts = amounts;
        Order = order;
        BaseCount = 1 + amounts + amounts * (amounts + 1) / 2;
        List<int[]> monomials = [[]];
        for (var degree = 1; degree <= order; degree++)
        {
            var indices = new int[degree];

            void Enumerate(int at, int start)
            {
                if (at == degree)
                {
                    monomials.Add((int[])indices.Clone());
                    return;
                }

                for (var channel = start; channel < amounts; channel++)
                {
                    indices[at] = channel;
                    Enumerate(at + 1, channel);
                }
            }

            Enumerate(0, 0);
        }

        Monomials = monomials.ToArray();
        _positions = Monomials.Select((indices, index) => (Key: Key(indices), Index: index))
            .ToDictionary(pair => pair.Key, pair => pair.Index, StringComparer.Ordinal);
        Splits = new Split[HigherCount][];
        for (var extra = 0; extra < HigherCount; extra++)
        {
            var indices = Monomials[BaseCount + extra];
            var grouped = new Dictionary<(int Left, int Right), int>();
            for (var mask = 0; mask < 1 << indices.Length; mask++)
            {
                var left = new List<int>();
                var right = new List<int>();
                for (var j = 0; j < indices.Length; j++)
                {
                    ((mask & (1 << j)) == 0 ? left : right).Add(indices[j]);
                }

                (int, int) pair = (_positions[Key(left)], _positions[Key(right)]);
                grouped[pair] = grouped.GetValueOrDefault(pair) + 1;
            }

            Splits[extra] = grouped.Select(pair => new Split(pair.Key.Left, pair.Key.Right, pair.Value)).ToArray();
            Products += Splits[extra].Length;
        }
    }

    public int Amounts { get; }
    public int Order { get; }
    public int BaseCount { get; }
    public int HigherCount => Monomials.Length - BaseCount;
    public int Products { get; }
    public int[][] Monomials { get; }
    public Split[][] Splits { get; }

    public static RewardMomentLayout? TryCreate(int amounts, int order, int maximumCoefficients = 8192)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amounts);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(amounts, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(order, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(order, 4);
        if (amounts == 0 || order == 2)
        {
            return null;
        }

        long count = 1;
        for (var k = 1; k <= order; k++)
        {
            count = count * (amounts + k) / k;
            if (count > maximumCoefficients)
            {
                return null;
            }
        }

        return new RewardMomentLayout(amounts, order);
    }

    public int Find(IEnumerable<int> indices) => _positions.TryGetValue(Key(indices.Order()), out var index)
        ? index
        : throw new ArgumentOutOfRangeException(nameof(indices), "The requested joint moment is outside its closure.");

    private static string Key(IEnumerable<int> indices) => string.Join(',', indices);

    internal readonly record struct Split(int Left, int Right, int Multiplicity);
}
