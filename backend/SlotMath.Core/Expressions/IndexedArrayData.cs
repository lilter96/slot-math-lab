using SlotMath.Core.Model;

namespace SlotMath.Core.Expressions;

internal static class IndexedArrayData
{
    internal static (int Items, int Updates) CheckScatter(ExprValue target, ExprValue indices, ExprValue values,
        ScatterMode mode, int maxItems, int maxUpdates)
    {
        Bound(maxItems, "scatter.maxItems");
        Bound(maxUpdates, "scatter.maxUpdates");
        if (mode is not (ScatterMode.RejectDuplicates or ScatterMode.LastWriteWins or ScatterMode.Sum))
        {
            throw Error("EVAL_SCATTER_MODE", "Unknown scatter duplicate policy.", "scatter.mode");
        }

        var count = CollectionInputs.Length(target, maxItems, "scatter.target");
        var updates = CollectionInputs.Length(indices, maxUpdates, "scatter.indices");
        var size = CollectionInputs.Length(values, maxUpdates, "scatter.values");
        if (updates != size)
        {
            throw Error("EVAL_SCATTER_LENGTH", "Indices and values require identical lengths.", "scatter.values");
        }

        return (count, updates);
    }

    internal static ExprValue Scatter(ExprValue target, ExprValue indices, ExprValue values, ScatterMode mode,
        ExprValue[] output, ExprValue[] updates, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Span<int> destinations = stackalloc int[updates.Length];
        Span<byte> seen = mode == ScatterMode.RejectDuplicates ? stackalloc byte[output.Length] : [];
        seen.Clear();
        ExprType? kind = mode == ScatterMode.Sum ? ExprType.Number : null;
        var symbols = 0;
        var reused = ReferenceEquals(target.ItemsArray, output);
        for (var index = 0; index < output.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            ExprValue item = target.ArrayValue![index];
            kind = ItemKind(item, kind, "scatter.target", index);
            if (item.ContainsSymbols)
            {
                symbols++;
            }

            if (!reused)
            {
                output[index] = item;
            }
        }

        for (var index = 0; index < updates.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var destination = Index(indices.ArrayValue![index], output.Length, "scatter.indices", index);
            if (mode == ScatterMode.RejectDuplicates && seen[destination] != 0)
            {
                throw Error("EVAL_SCATTER_DUPLICATE", "A destination index occurs more than once.",
                    $"scatter.indices[{index}]");
            }

            if (mode == ScatterMode.RejectDuplicates)
            {
                seen[destination] = 1;
            }

            destinations[index] = destination;
        }

        for (var index = 0; index < updates.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            ExprValue item = values.ArrayValue![index];
            kind = ItemKind(item, kind, "scatter.values", index);
            updates[index] = item;
        }

        for (var index = 0; index < updates.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var destination = destinations[index];
            if (output[destination].ContainsSymbols)
            {
                symbols--;
            }

            ExprValue item = mode == ScatterMode.Sum
                ? ExprValue.Add(output[destination], updates[index])
                : updates[index];
            output[destination] = item;
            if (item.ContainsSymbols)
            {
                symbols++;
            }
        }

        return ExprValue.ArrayOwned(output, symbols != 0);
    }

    internal static int CheckPartition(ExprValue labels, int buckets, int maxItems)
    {
        Bound(buckets, "partitionIndices.bucketCount");
        Bound(maxItems, "partitionIndices.maxItems");
        return CollectionInputs.Length(labels, maxItems, "partitionIndices.labels");
    }

    internal static void PartitionPlan(ExprValue labels, Span<int> keys, Span<int> counts, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        counts.Clear();
        for (var index = 0; index < keys.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var bucket = Index(labels.ArrayValue![index], counts.Length, "partitionIndices.labels", index);
            keys[index] = bucket;
            counts[bucket]++;
        }
    }

    internal static ExprValue Partition(ExprValue[] groups, ReadOnlySpan<int> keys, Span<int> cursors,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        cursors.Clear();
        for (var index = 0; index < keys.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var bucket = keys[index];
            groups[bucket].ItemsArray![cursors[bucket]++] = ExprValue.Number(index);
        }

        return ExprValue.ArrayOwned(groups, false);
    }

    private static ExprType ItemKind(ExprValue value, ExprType? expected, string operand, int index)
    {
        ExprType type = value.Kind == ExprType.Symbol ? ExprType.String : value.Kind;
        if (type is not (ExprType.Number or ExprType.Boolean or ExprType.String or ExprType.Record or ExprType.Array) ||
            (expected is { } known && type != known))
        {
            throw Error("EVAL_TYPE_ERROR", "Scatter requires one readable element type across target and updates.",
                $"{operand}[{index}]");
        }

        return type;
    }

    private static int Index(ExprValue value, int length, string operand, int index)
    {
        if (value.Kind != ExprType.Number || !value.NumberDenominator.IsOne || value.NumberNumerator < 0 ||
            value.NumberNumerator >= length)
        {
            throw Error("EVAL_INDEXED_DOMAIN", "The index must be an exact integer inside the destination domain.",
                $"{operand}[{index}]");
        }

        return (int)value.NumberNumerator;
    }

    private static void Bound(int value, string location)
    {
        if (value is < 0 or > CollectionInputs.MaxItems)
        {
            throw Error("EVAL_COLLECTION_BUDGET", "The explicit bound must be in 0..4096.", location);
        }
    }

    private static ExpressionEvaluationException Error(string code, string message, string location) =>
        new(code, message, location);
}
