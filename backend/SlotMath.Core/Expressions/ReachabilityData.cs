using System.Numerics;

namespace SlotMath.Core.Expressions;

/// <summary>Pure bounded closure of allowed directed adjacency. No geometry or game rules.</summary>
internal static class ReachabilityData
{
    internal const int MaxVertices = 64;

    // Preparation is speculative. Invalid constants keep their original runtime
    // validation, after all three operands have been evaluated.
    internal static Prepared? Prepare(ExprValue adjacency, int vertexCount)
    {
        if (vertexCount is < 0 or > MaxVertices || adjacency.ItemsArray is not { } rows)
        {
            return null;
        }

        try
        {
            Evaluate(adjacency, ExprValue.Number(0), ExprValue.Number(0), vertexCount);
        }
        catch (ExpressionEvaluationException)
        {
            return null;
        }

        var neighbors = new ulong[vertexCount];
        for (var vertex = 0; vertex < vertexCount; vertex++)
        {
            neighbors[vertex] = (ulong)rows[vertex].NumberNumerator;
        }

        return new Prepared(neighbors, vertexCount == 64 ? ulong.MaxValue : (1UL << vertexCount) - 1);
    }

    internal static ExprValue Evaluate(ExprValue adjacency, ExprValue allowed, ExprValue seeds, int vertexCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (vertexCount is < 0 or > MaxVertices)
        {
            throw new ExpressionEvaluationException("EVAL_REACHABLE_BUDGET",
                "Reachable requires an explicit domain of 0..64 vertices.", "reachable.vertexCount");
        }

        if (adjacency.Kind != ExprType.Array)
        {
            throw new ExpressionEvaluationException("EVAL_TYPE_ERROR",
                "Reachable adjacency must be an array of numeric masks.", "reachable.adjacency");
        }

        IReadOnlyList<ExprValue> rows = adjacency.ArrayValue!;
        if (rows.Count != vertexCount)
        {
            throw new ExpressionEvaluationException("EVAL_REACHABLE_LENGTH",
                "Adjacency length must equal vertexCount.", "reachable.adjacency");
        }

        var domain = vertexCount == 64 ? ulong.MaxValue : (1UL << vertexCount) - 1;
        Span<ulong> neighbors = stackalloc ulong[MaxVertices];
        for (var vertex = 0; vertex < vertexCount; vertex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExprValue row = rows[vertex];
            // Successful rows need no per-vertex location string allocation.
            if (row.IsInlineInteger && row.InlineInteger >= 0 && ((ulong)row.InlineInteger & ~domain) == 0)
            {
                neighbors[vertex] = (ulong)row.InlineInteger;
            }
            else
            {
                if (row.Kind == ExprType.Number && row.NumberDenominator.IsOne &&
                    row.NumberNumerator.Sign >= 0 && row.NumberNumerator.GetBitLength() <= 64 &&
                    ((ulong)row.NumberNumerator & ~domain) == 0)
                {
                    neighbors[vertex] = (ulong)row.NumberNumerator;
                }
                else
                {
                    neighbors[vertex] = InDomain(row, domain, $"reachable.adjacency[{vertex}]");
                }
            }
        }

        return Traverse(neighbors, allowed, seeds, domain, cancellationToken);
    }

    private static ExprValue Traverse(ReadOnlySpan<ulong> neighbors, ExprValue allowed, ExprValue seeds, ulong domain,
        CancellationToken cancellationToken)
    {
        var permitted = InDomain(allowed, domain, "reachable.allowed");
        var reached = InDomain(seeds, domain, "reachable.seeds") & permitted;
        var pending = reached;
        while (pending != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vertex = BitOperations.TrailingZeroCount(pending);
            pending &= pending - 1;
            var added = neighbors[vertex] & permitted & ~reached;
            reached |= added;
            pending |= added;
        }

        return BitSetOperations.Value(reached);
    }

    private static ulong InDomain(ExprValue value, ulong domain, string location)
    {
        var mask = BitSetOperations.Mask(value, location);
        if ((mask & ~domain) != 0)
        {
            throw new ExpressionEvaluationException("EVAL_REACHABLE_DOMAIN",
                "A mask contains a vertex outside the declared domain.", location);
        }

        return mask;
    }

    internal sealed class Prepared(ulong[] neighbors, ulong domain)
    {
        internal ExprValue Evaluate(ExprValue allowed, ExprValue seeds, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cancellationToken.CanBeCanceled)
            {
                // Preserve the validation checkpoints of each immutable row.
                for (var vertex = 0; vertex < neighbors.Length; vertex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            return Traverse(neighbors, allowed, seeds, domain, cancellationToken);
        }
    }
}
