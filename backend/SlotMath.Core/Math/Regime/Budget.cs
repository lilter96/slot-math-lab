namespace SlotMath.Core.Math.Regime;

// ═══════════════════════════════════════════════════════════════════════════
//  Budget — resource limits for exact evaluation
//
//  Used by the regime selector (G8) to decide when exact evaluation is too
//  expensive and should fall back to sampled or hybrid mode.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Resource budget for computation.  When the exact interpreter exceeds the
/// branch ceiling or time limit, it throws <see cref="BudgetExceededException"/>,
/// and the regime selector falls back to sampled evaluation.
/// </summary>
public sealed class Budget
{
    /// <summary>
    /// Maximum number of branches (Draw outcome evaluations) before falling
    /// back to sampled.  Null means no limit.
    /// </summary>
    public long? MaxBranches { get; init; }

    /// <summary>
    /// Maximum wall-clock time for exact evaluation.  Null means no limit.
    /// </summary>
    public TimeSpan? MaxTime { get; init; }

    /// <summary>
    /// Default budget suitable for MVP-class configs — 100k branches, no time limit.
    /// </summary>
    public static Budget Default { get; } = new() { MaxBranches = 100_000 };

    /// <summary>
    /// Unlimited budget — always try exact.
    /// </summary>
    public static Budget Unlimited { get; } = new();

    /// <summary>
    /// Tight budget for testing fallback — 10 branches.
    /// </summary>
    public static Budget Tight { get; } = new() { MaxBranches = 10 };
}
