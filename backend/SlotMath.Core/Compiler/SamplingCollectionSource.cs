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

    internal static ExprValue ReadIndexedValue(ExprValue source, int bound, string location, out int length)
    {
        length = CollectionInputs.Length(source, bound, location);
        return source;
    }

    internal static SamplingCollectionValue ReadIndexed(SamplingCell source, int bound, string key, string location)
    {
        if (source.HasRaw && source.Raw is IEnumerable &&
            source.Raw is not (IList or ExprValue or string or IDictionary or IReadOnlyDictionary<string, object?>))
        {
            throw new ExpressionEvaluationException("EVAL_TYPE_ERROR",
                "A short-circuit collection requires an indexed array with a known length.", location);
        }

        return new SamplingCollectionValue(source,
            CollectionInputs.CheckCount(source.RequireArrayCount(key), bound, location));
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
