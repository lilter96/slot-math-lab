namespace SlotMath.Core.Mechanics;

/// <summary>
/// Internal, trusted fast-path contract for win evaluation (Invariant 11, D25).
///
/// This is **NOT** a plugin contract. The plugin escape hatch (level c) uses
/// <see cref="IEvaluator"/>; that interface is reserved for sandboxed,
/// admin-registered plugins and the standard library ships zero implementations
/// of it (Invariant 2, G12).
///
/// A fast-path is an optional C# optimization of a catalog subgraph (the
/// sanctioned set is Lines / Ways / Cluster). Each fast-path must be paired with
/// a passing equivalence test proving it yields the identical probability mass
/// function as its canonical atomic subgraph (D25, see FastPathEquivalenceTests).
/// Fast-paths are first-party trusted code — they are not sandboxed and do not
/// force the sampled regime, unlike plugins.
///
/// Rules: pure, deterministic, no Draw, no I/O.
/// </summary>
public interface IFastPathEvaluator
{
    /// <summary>
    /// Evaluate the game state and return all winning combinations.  The board
    /// is read from state (invariant 4: a board is a user-defined array in S,
    /// conventionally <c>state["board"]</c> with <c>state["rows"]/["cols"]</c>);
    /// see <see cref="GridState"/>.
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <returns>Array of wins (empty if none).</returns>
    Win[] Evaluate(IReadOnlyDictionary<string, object?> state);
}
