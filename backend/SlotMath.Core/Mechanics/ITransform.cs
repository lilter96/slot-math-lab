namespace SlotMath.Core.Mechanics;

/// <summary>
/// A pure transformation on a board and opaque game state.
///
/// ITransform is one of the three stable, pluggable interfaces of the mechanic
/// layer.  Transforms mutate the board (and optionally the recurrence state)
/// without randomness or I/O — they are pure functions.
///
/// Implementations belong to the standard library or are loaded as plugins
/// (level c).  The interpreters and compiler are agnostic to the specific
/// transform: they query the <see cref="TransformRegistry"/> by name.
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
