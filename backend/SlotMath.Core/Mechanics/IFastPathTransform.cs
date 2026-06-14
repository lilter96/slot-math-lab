namespace SlotMath.Core.Mechanics;

/// <summary>
/// Internal, trusted fast-path contract for board+state transformations
/// (Invariant 11).
///
/// This is **NOT** a plugin contract. The plugin escape hatch (level c) uses
/// <see cref="ITransform"/>; that interface is reserved for sandboxed,
/// admin-registered plugins and the standard library ships zero implementations
/// of it (Invariant 2, G12).
///
/// A fast-path transform is an optional C# optimization of a catalog subgraph.
/// Fast-paths are first-party trusted code — they are not sandboxed and do not
/// force the sampled regime, unlike plugins.
///
/// Rules: pure, deterministic, no Draw, no I/O. The original board is never
/// mutated — always return a new Board.
/// </summary>
public interface IFastPathTransform
{
    /// <summary>
    /// Apply the transform to a board and game state, returning a new board
    /// and possibly-modified state.
    ///
    /// The original board is never mutated. State is passed opaquely; a
    /// transform that does not touch state should return it unchanged.
    /// </summary>
    /// <param name="board">The current board (never null).</param>
    /// <param name="state">The opaque recurrence state (may be null).</param>
    /// <returns>A tuple of the new board and the (possibly modified) state.</returns>
    (Board NewBoard, object? NewState) Apply(Board board, object? state);
}
