using System.Runtime.CompilerServices;
using SlotMath.Core.Expressions;

namespace SlotMath.Core.Compiler;

/// <summary>Owned numeric operands for an immutable integer array; no per-read value construction.</summary>
internal sealed class SamplingIntegerTable
{
    private const int MaxEntries = 65536;
    private readonly long[] _values;

    private SamplingIntegerTable(long[] values, SamplingIntegerRange range)
    {
        _values = values;
        Range = range;
    }

    internal SamplingIntegerRange Range { get; }

    internal static SamplingIntegerTable? Prepare(ExprValue source)
    {
        if (source.ItemsArray is not { Length: <= MaxEntries } items || items.Any(value => !value.IsInlineInteger))
        {
            return null;
        }

        var values = new long[items.Length];
        var minimum = long.MaxValue;
        var maximum = long.MinValue;
        for (var index = 0; index < items.Length; index++)
        {
            var value = items[index].InlineInteger;
            values[index] = value;
            minimum = System.Math.Min(minimum, value);
            maximum = System.Math.Max(maximum, value);
        }

        // An empty table has no successful reads; its ordinary bounds error is retained.
        return new SamplingIntegerTable(values, values.Length == 0
            ? new SamplingIntegerRange(0, 0)
            : new SamplingIntegerRange(minimum, maximum));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal long Read(long position) => (ulong)position < (ulong)_values.Length
        ? _values[position]
        : Read(ExprValue.Number(position));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal long Read(ExprValue position) => position.IsInlineInteger &&
                                              (ulong)position.InlineInteger < (ulong)_values.Length
        ? _values[position.InlineInteger]
        : _values[CollectionFunctions.IndexPosition(position, _values.Length)];
}
