namespace SlotMath.Core.Math.Regime;

// ═══════════════════════════════════════════════════════════════════════════
//  BudgetExceededException — thrown when exact evaluation exceeds budget
//
//  Caught by the regime selector to trigger fallback to sampled/hybrid.
//  Carries partial stats so the caller knows how far evaluation got.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Thrown by the exact interpreter when a budget limit is exceeded.
/// Carries the current evaluation stats for diagnostic purposes.
/// </summary>
public sealed class BudgetExceededException : Exception
{
    /// <summary>Branches evaluated before the budget was exceeded.</summary>
    public long BranchesEvaluated { get; }

    /// <summary>Draws evaluated before the budget was exceeded.</summary>
    public long DrawsEvaluated { get; }

    /// <summary>The budget that was exceeded.</summary>
    public Budget Budget { get; }

    /// <summary>The reason: "branches" or "time".</summary>
    public string LimitExceeded { get; }

    public BudgetExceededException(
        long branchesEvaluated,
        long drawsEvaluated,
        Budget budget,
        string limitExceeded)
        : base($"Budget exceeded: {limitExceeded} limit reached " +
               $"(branches={branchesEvaluated}, draws={drawsEvaluated}, " +
               $"maxBranches={budget.MaxBranches}, maxTime={budget.MaxTime})")
    {
        BranchesEvaluated = branchesEvaluated;
        DrawsEvaluated = drawsEvaluated;
        Budget = budget;
        LimitExceeded = limitExceeded;
    }
}
