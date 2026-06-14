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
    /// Apply the transform to the game state, returning a new state.  The board
    /// is read from and written back to state (invariant 4: a board is a
    /// user-defined array in S, conventionally <c>state["board"]</c>); see
    /// <see cref="GridState"/>.  The input state is never mutated (D17).
    /// </summary>
    /// <param name="state">The current game state (never null).</param>
    /// <returns>The new game state.</returns>
    IReadOnlyDictionary<string, object?> Apply(IReadOnlyDictionary<string, object?> state);
}
