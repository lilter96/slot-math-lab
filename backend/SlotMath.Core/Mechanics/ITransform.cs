namespace SlotMath.Core.Mechanics;

/// <summary>
/// Plugin contract (level c) for pure board+state transformations.
///
/// ITransform is the interface that user-uploaded plugins implement to supply
/// novel board-mutation logic.  It is a **plugin-only** contract — the standard
/// library ships ZERO implementations of it (Invariant 2, G12).  The canonical
/// mechanics catalog is composed from substrate atoms (subgraphs of
/// Draw/State/Loop + expressions), not from C# classes.
///
/// Optional C# fast-paths are NOT plugins and do NOT implement this interface —
/// they implement the internal, trusted <see cref="IFastPathTransform"/>
/// contract instead.
///
/// A game that uses a plugin is flagged sampled-regime.  Rules: pure,
/// deterministic, no Draw, no I/O.  Original board is never mutated — always
/// return a new Board.
/// </summary>
public interface ITransform
{
    /// <summary>
    /// Apply the transform to the game state, returning a new state.  The plugin
    /// receives the whole state, reads the board from it, mutates, and writes it
    /// back (invariant 4: a board is a user-defined array in S, e.g.
    /// <c>state["board"]</c>); see <see cref="GridState"/>.  The input state is
    /// never mutated (D17).
    /// </summary>
    /// <param name="state">The current game state (never null).</param>
    /// <returns>The new game state.</returns>
    IReadOnlyDictionary<string, object?> Apply(IReadOnlyDictionary<string, object?> state);
}
