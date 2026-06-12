namespace SlotMath.Core.Mechanics;

/// <summary>
/// Plugin contract (level c) for pure win evaluation.
///
/// IEvaluator is the interface that user-uploaded plugins implement to supply
/// novel win-scoring logic.  It is NOT a standard-library interface — the
/// canonical mechanics catalog is composed from substrate atoms (subgraphs of
/// Draw/State/Loop + expressions), not from C# classes.
///
/// The implementations in Mechanics/Evaluators/ are OPTIONAL C# fast-paths:
/// each one is accompanied by a passing equivalence test against its canonical
/// atomic subgraph (Invariant 11).  They are optimization details, not the
/// source of truth.
///
/// Rules: pure, deterministic, no Draw, no I/O.
/// </summary>
public interface IEvaluator
{
    /// <summary>
    /// Evaluate the board and return all winning combinations.
    /// </summary>
    /// <param name="board">The current board state.</param>
    /// <param name="state">Opaque recurrence state (may be null).</param>
    /// <returns>Array of wins (empty if none).</returns>
    Win[] Evaluate(Board board, object? state);
}
