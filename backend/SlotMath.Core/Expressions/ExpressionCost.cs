using SlotMath.Core.Model;

namespace SlotMath.Core.Expressions;

// ═══════════════════════════════════════════════════════════════════════════
//  ExpressionCost — static operation count of a level-(b) expression AST
//  (PRD v3.1, D15/D16).
//
//  The compiler computes ExpressionCost statically (the total static operation
//  count of the AST tree). The default per-leaf limit is 10,000 operations
//  (SlotMathConstants.Expression.MaxOps); an expression exceeding it is rejected
//  at validation with a coded error (D19/G14). fold/map/filter are O(n) over
//  their array, so their body cost is weighted to reflect bounded iteration.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Static operation-count analyzer for level-(b) expressions (D15/D16).</summary>
public static class ExpressionCost
{
    /// <summary>
    ///     Historical estimate for state-key forms without explicit bounds.
    ///     New bounded forms charge their declared MaxItems, including nested work.
    /// </summary>
    private const int BoundedIterationFactor = 64;

    /// <summary>Total static operation count of the expression tree (D15).</summary>
    public static long Compute(Expression expr) => Compute(expr, 0);

    private static long Add(long left, long right) => left > long.MaxValue - right ? long.MaxValue : left + right;

    private static long Multiply(long value, int count) =>
        count < 0 || value == long.MaxValue || (count != 0 && value > long.MaxValue / count)
            ? long.MaxValue
            : value * count;

    private static long Compute(Expression expr, int depth)
    {
        if (depth > 128)
        {
            return long.MaxValue;
        }

        long Child(Expression? value)
        {
            return value is null ? 0 : Compute(value, depth + 1);
        }

        long Sum(IEnumerable<Expression> values)
        {
            long result = 0;
            foreach (Expression value in values)
            {
                result = Add(result, Child(value));
            }

            return result;
        }

        long Iteration(Expression collection, long body, long init = 0)
        {
            CollectionInput input = CollectionInputs.Of(collection);
            var bound = input.MaxItems ?? BoundedIterationFactor;
            return Add(Add(1, Add(Child(input.Source), init)),
                Multiply(input.MaxItems is null ? body : Add(1, body), bound));
        }

        return expr switch
        {
            ConstantExpr or FieldAccessExpr or LocalExpr => 1,
            LetExpr e => Add(1, Add(Child(e.Value), Child(e.Body))),
            RecordExpr e => Add(1, Sum(e.Fields.Select(field => field.Value))),
            TupleExpr e => Add(1, Sum(e.Items)),
            ProjectExpr e => Add(1, Child(e.Value)),
            ReachableExpr e => Add(Add(Add(Child(e.Adjacency), Child(e.Allowed)), Child(e.Seeds)),
                e.VertexCount is >= 0 and <= ReachabilityData.MaxVertices ? 1 + 2L * e.VertexCount : long.MaxValue),
            ScanExpr e => Add(Child(e.Source), e.MaxItems is >= 0 and <= CollectionInputs.MaxItems
                ? 1L + e.MaxItems
                : long.MaxValue),
            SegmentedCompactExpr e => Add(Add(Child(e.Keep), Child(e.Boundaries)),
                e.MaxItems is >= 0 and <= CollectionInputs.MaxItems &&
                e.MaxSegments is >= 0 and <= CollectionInputs.MaxItems
                    ? 2L + 3L * e.MaxItems + 2L * e.MaxSegments
                    : long.MaxValue),
            NotExpr e => Add(1, Child(e.Expr)),
            BinaryExpr e => Add(1, Add(Child(e.Left), Child(e.Right))),
            CompareExpr e => Add(1, Add(Child(e.Left), Child(e.Right))),
            IfExpr e => Add(1, Add(Child(e.Condition), Add(Child(e.ThenExpr), Child(e.ElseExpr)))),
            AggregateExpr e => Iteration(e, Add(Child(e.Predicate), Child(e.ValueExpr))),
            BitSetExpr e => Add(1, Sum(e.Args)),
            CallExpr e => Add(1, Sum(e.Args)),
            MapExpr e => Iteration(e, Child(e.Body)),
            FilterExpr e => Iteration(e, Child(e.Predicate)),
            FoldExpr e => Iteration(e, Child(e.Body), Child(e.Init)),
            _ => 1
        };
    }

    /// <summary>True when the expression's static cost is within the D16 budget.</summary>
    public static bool WithinBudget(Expression expr, out long cost)
    {
        cost = Compute(expr);
        return cost <= SlotMathConstants.Expression.MaxOps;
    }
}
