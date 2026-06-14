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
    /// Apply the transform to a board and game state, returning a new board
    /// and possibly-modified state.
    ///
    /// The original board is never mutated.  State is passed opaquely; a
    /// transform that does not touch state should return it unchanged.
    /// </summary>
    /// <param name="board">The current board (never null).</param>
    /// <param name="state">The opaque recurrence state (may be null).</param>
    /// <returns>A tuple of the new board and the (possibly modified) state.</returns>
    (Board NewBoard, object? NewState) Apply(Board board, object? state);
}
