namespace SlotMath.Core.Math.Regime;

// ═══════════════════════════════════════════════════════════════════════════
//  RegimeResult — wraps SlotMathReport with regime/strategy metadata
//
//  Returned by the HybridEvaluator.  Carries the full metric report plus
//  per-subgraph strategy records and the aggregate provenance.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// The result of a regime-aware evaluation.  Wraps the standard
/// <see cref="SlotMathReport"/> with additional strategy metadata.
/// </summary>
public sealed record RegimeResult
{
    /// <summary>The full slot-math metric report.</summary>
    public required SlotMathReport Report { get; init; }

    /// <summary>
    /// Per-subgraph strategy records — one entry per identified subgraph.
    /// Includes the strategy chosen, the reason, and the resulting provenance.
    /// </summary>
    public required IReadOnlyList<SubgraphStrategy> SubgraphStrategies { get; init; }

    /// <summary>
    /// Aggregate provenance for the entire evaluation.
    /// - <see cref="Provenance.Exact"/> when all subgraphs ran exactly with no pruning.
    /// - <see cref="Provenance.ExactInterval"/> when all exact with some pruning.
    /// - <see cref="Provenance.Sampled"/> when any subgraph used sampling (incl. plugins).
    /// </summary>
    public Provenance AggregateProvenance { get; init; }

    /// <summary>Overall strategy chosen.</summary>
    public EvaluationStrategy OverallStrategy { get; init; }

    /// <summary>Whether the budget was exceeded, triggering a fallback.</summary>
    public bool BudgetExceeded { get; init; }

    /// <summary>Human-readable summary for debugging.</summary>
    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"RegimeResult [{AggregateProvenance}] — {OverallStrategy}" +
                       (BudgetExceeded ? " (budget exceeded)" : ""));
        foreach (var ss in SubgraphStrategies)
            sb.AppendLine($"  {ss}");
        sb.Append(Report.ToString());
        return sb.ToString();
    }
}
