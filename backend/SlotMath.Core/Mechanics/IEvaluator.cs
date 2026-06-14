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
    /// Evaluate the game state and return all winning combinations.  The plugin
    /// receives the whole state and reads the board from it (invariant 4:
    /// a board is a user-defined array in S, e.g. <c>state["board"]</c>); see
    /// <see cref="GridState"/>.
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <returns>Array of wins (empty if none).</returns>
    Win[] Evaluate(IReadOnlyDictionary<string, object?> state);
}
