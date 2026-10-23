using System.Collections;
using System.Runtime.CompilerServices;
using SlotMath.Core.Expressions;

namespace SlotMath.Core.Compiler;

internal readonly record struct SamplingCollectionValue(SamplingCell Cell, int Length);

internal static class SamplingCollectionSource
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ExprValue ReadValue(ExprValue source, int bound, string location, out int length)
    {
        length = CollectionInputs.Length(source, bound, location);
        return source.ContainsSymbols ? SamplingCell.Typed(source).Value : source;
    }

    internal static SamplingCollectionValue Read(SamplingCell source, int bound, string key, string location)
    {
        // Bound streaming state inputs before counting them, and enumerate once.
        if (source.HasRaw && source.Raw is IEnumerable values
                          && source.Raw is not (IList or ExprValue or string or IDictionary
                              or IReadOnlyDictionary<string, object?>))
        {
            source = SamplingCell.FromRaw(CollectionInputs.BoundedItems(values.Cast<object?>(), bound, location));
        }

        var length = CollectionInputs.CheckCount(source.RequireArrayCount(key), bound, location);
        return new SamplingCollectionValue(source, length);
    }
}
