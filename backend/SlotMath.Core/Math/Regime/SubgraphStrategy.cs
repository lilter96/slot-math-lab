namespace SlotMath.Core.Math.Regime;

// ═══════════════════════════════════════════════════════════════════════════
//  EvaluationStrategy — which interpreter was used for a subgraph
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// The evaluation strategy chosen for a subgraph (or the whole game).
/// </summary>
public enum EvaluationStrategy
{
    /// <summary>Full exact evaluation via the rational interpreter.</summary>
    Exact,

    /// <summary>Monte Carlo sampling.</summary>
    Sampled,

    /// <summary>
    /// Hybrid: some subgraphs evaluated exactly, others sampled,
    /// combined by the hybrid evaluator.
    /// </summary>
    Hybrid
}

// ═══════════════════════════════════════════════════════════════════════════
//  SubgraphStrategy — records strategy chosen for one subgraph
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Records the evaluation strategy selected for a named subgraph,
/// along with the reason for the selection.
/// </summary>
public sealed record SubgraphStrategy
{
    /// <summary>Identifier for this subgraph (e.g. "BaseGame", "FreeSpins", "Bonus").</summary>
    public required string SubgraphId { get; init; }

    /// <summary>The strategy chosen.</summary>
    public EvaluationStrategy Strategy { get; init; }

    /// <summary>
    /// Human-readable reason for the choice (e.g. "within_budget",
    /// "plugin_present", "branch_estimate_exceeded", "contains_loop").
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>Estimated branch count for this subgraph, if computed.</summary>
    public long? EstimatedBranches { get; init; }

    /// <summary>Whether this subgraph contains a plugin reference.</summary>
    public bool ContainsPlugin { get; init; }

    /// <summary>Provenance of the metrics produced by this subgraph.</summary>
    public Provenance? Provenance { get; init; }

    public override string ToString() =>
        $"[{SubgraphId}] {Strategy} — {Reason}" +
        (EstimatedBranches.HasValue ? $" (est. {EstimatedBranches} branches)" : "") +
        (ContainsPlugin ? " [plugin]" : "");
}
