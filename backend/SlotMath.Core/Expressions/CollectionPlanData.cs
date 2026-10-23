using SlotMath.Core.Model;

namespace SlotMath.Core.Expressions;

internal static class CollectionPlanData
{
    private static readonly string[] PlanFields = ["sources", "freeCounts"];

    internal static int CheckScan(ExprValue source, ScanMode mode, int maxItems)
    {
        Bound(maxItems, "scan.maxItems");
        if (mode is not (ScanMode.Inclusive or ScanMode.Exclusive))
        {
            throw Error("EVAL_SCAN_MODE", "Unknown scan mode.", "scan.mode");
        }

        return Length(source, maxItems, "scan.source");
    }

    internal static ExprValue Scan(ExprValue source, ScanMode mode, ExprValue[] output,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ExprValue> items = source.ArrayValue!;
        ExprType? kind = null;
        ExprValue total = ExprValue.Number(0);
        for (var index = 0; index < output.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExprValue item = items[index];
            if (item.Kind is not (ExprType.Number or ExprType.Boolean) ||
                (kind is { } previous && previous != item.Kind))
            {
                throw Error("EVAL_TYPE_ERROR", "Scan requires homogeneous integers or Boolean flags.",
                    $"scan.source[{index}]");
            }

            kind ??= item.Kind;
            if (item.Kind == ExprType.Number && !item.NumberDenominator.IsOne)
            {
                throw Error("EVAL_SCAN_INTEGER", "Scan numeric items must be exact integers.", $"scan.source[{index}]");
            }

            ExprValue value = item.Kind == ExprType.Boolean ? ExprValue.Number(item.BoolValue ? 1 : 0) : item;
            if (mode == ScanMode.Exclusive)
            {
                output[index] = total;
            }

            total = ExprValue.Add(total, value);
            if (mode == ScanMode.Inclusive)
            {
                output[index] = total;
            }
        }

        return ExprValue.ArrayOwned(output, false);
    }

    internal static (int Items, int Segments) CheckCompact(ExprValue keep, ExprValue boundaries,
        SegmentPacking packing, int maxItems, int maxSegments)
    {
        Bound(maxItems, "segmentedCompact.maxItems");
        Bound(maxSegments, "segmentedCompact.maxSegments");
        if (packing is not (SegmentPacking.Start or SegmentPacking.End))
        {
            throw Error("EVAL_COMPACT_PACKING", "Unknown segment packing.", "segmentedCompact.packing");
        }

        var count = Length(keep, maxItems, "segmentedCompact.keep");
        if (boundaries.Kind != ExprType.Array)
        {
            throw Error("EVAL_TYPE_ERROR", "Segment boundaries must be a numeric array.",
                "segmentedCompact.boundaries");
        }

        var length = boundaries.ArrayValue!.Count;
        if (length < 1 || length - 1 > maxSegments)
        {
            throw Error("EVAL_COMPACT_BOUNDARIES", "Boundaries require one offset per segment plus an end offset.",
                "segmentedCompact.boundaries");
        }

        return (count, length - 1);
    }

    internal static ExprValue Compact(ExprValue keep, ExprValue boundaries, SegmentPacking packing,
        ExprValue[] sources, ExprValue[] freeCounts, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = sources.Length;
        Span<byte> flags = stackalloc byte[count];
        Span<int> offsets = stackalloc int[freeCounts.Length + 1];
        IReadOnlyList<ExprValue> items = keep.ArrayValue!;
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExprValue item = items[index];
            if (item.Kind != ExprType.Boolean)
            {
                throw Error("EVAL_TYPE_ERROR", "Keep items must be Boolean flags.", $"segmentedCompact.keep[{index}]");
            }

            flags[index] = item.BoolValue ? (byte)1 : (byte)0;
        }

        IReadOnlyList<ExprValue> limits = boundaries.ArrayValue!;
        for (var index = 0; index < offsets.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExprValue item = limits[index];
            if (item.Kind != ExprType.Number || !item.NumberDenominator.IsOne ||
                item.NumberNumerator < 0 || item.NumberNumerator > count)
            {
                throw Error("EVAL_COMPACT_BOUNDARIES", "Offsets must be integers within the source length.",
                    $"segmentedCompact.boundaries[{index}]");
            }

            var offset = (int)item.NumberNumerator;
            if ((index == 0 && offset != 0) || (index > 0 && offset < offsets[index - 1]) ||
                (index == offsets.Length - 1 && offset != count))
            {
                throw Error("EVAL_COMPACT_BOUNDARIES", "Offsets must cover the source in nondecreasing order.",
                    $"segmentedCompact.boundaries[{index}]");
            }

            offsets[index] = offset;
        }

        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sources[index] = ExprValue.Number(-1);
        }

        for (var segment = 0; segment < freeCounts.Length; segment++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = offsets[segment];
            var end = offsets[segment + 1];
            var cursor = packing == SegmentPacking.Start ? start : end - 1;
            for (var step = 0; step < end - start; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = packing == SegmentPacking.Start ? start + step : end - 1 - step;
                if (flags[index] == 0)
                {
                    continue;
                }

                sources[cursor] = ExprValue.Number(index);
                cursor += packing == SegmentPacking.Start ? 1 : -1;
            }

            freeCounts[segment] = ExprValue.Number(packing == SegmentPacking.Start ? end - cursor : cursor - start + 1);
        }

        return LexicalValues.Record(PlanFields,
            [ExprValue.ArrayOwned(sources, false), ExprValue.ArrayOwned(freeCounts, false)]);
    }

    private static void Bound(int value, string location)
    {
        if (value is < 0 or > CollectionInputs.MaxItems)
        {
            throw Error("EVAL_COLLECTION_BUDGET", "The explicit bound must be in 0..4096.", location);
        }
    }

    private static int Length(ExprValue value, int bound, string location)
    {
        if (value.Kind != ExprType.Array)
        {
            throw Error("EVAL_TYPE_ERROR", "An array is required.", location);
        }

        return CollectionInputs.CheckCount(value.ArrayValue!.Count, bound, location);
    }

    private static ExpressionEvaluationException Error(string code, string message, string location) =>
        new(code, message, location);
}
