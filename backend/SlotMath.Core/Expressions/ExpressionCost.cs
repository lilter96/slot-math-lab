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
    /// Weight applied to a bounded fold/map/filter body to reflect that it runs
    /// once per array element (D16: fold cost = arrayLength · bodyCost). A static,
    /// conservative bound on array length for the static op-count estimate.
    /// </summary>
    private const int BoundedIterationFactor = 64;

    /// <summary>Total static operation count of the expression tree (D15).</summary>
    public static long Compute(Expression expr) => expr switch
    {
        ConstantExpr => 1,
        FieldAccessExpr => 1,
        NotExpr n => 1 + Compute(n.Expr),
        BinaryExpr b => 1 + Compute(b.Left) + Compute(b.Right),
        CompareExpr c => 1 + Compute(c.Left) + Compute(c.Right),
        IfExpr i => 1 + Compute(i.Condition) + Compute(i.ThenExpr) + Compute(i.ElseExpr),
        AggregateExpr a => 1 + BoundedIterationFactor * (
            (a.Predicate is null ? 0 : Compute(a.Predicate)) +
            (a.ValueExpr is null ? 0 : Compute(a.ValueExpr))),
        CallExpr call => 1 + call.Args.Sum(Compute),
        MapExpr m => 1 + BoundedIterationFactor * Compute(m.Body),
        FilterExpr f => 1 + BoundedIterationFactor * Compute(f.Predicate),
        FoldExpr fold => 1 + Compute(fold.Init) + BoundedIterationFactor * Compute(fold.Body),
        _ => 1,
    };

    /// <summary>True when the expression's static cost is within the D16 budget.</summary>
    public static bool WithinBudget(Expression expr, out long cost)
    {
        cost = Compute(expr);
        return cost <= SlotMathConstants.Expression.MaxOps;
    }
}
