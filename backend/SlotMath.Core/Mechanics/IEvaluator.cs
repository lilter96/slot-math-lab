namespace SlotMath.Core.Mechanics;

/// <summary>
/// Plugin contract (level c) for pure win evaluation.
///
/// IEvaluator is the interface that user-uploaded plugins implement to supply
/// novel win-scoring logic.  It is a **plugin-only** contract — the standard
/// library ships ZERO implementations of it (Invariant 2, G12).  The canonical
/// mechanics catalog is composed from substrate atoms (subgraphs of
/// Draw/State/Loop + expressions), not from C# classes.
///
/// Optional C# fast-paths (Lines/Ways/Cluster) are NOT plugins and do NOT
/// implement this interface — they implement the internal, trusted
/// <see cref="IFastPathEvaluator"/> contract instead.
///
/// A game that uses a plugin is flagged sampled-regime; the engine never claims
/// Exact for it.  Rules: pure, deterministic, no Draw, no I/O.
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
