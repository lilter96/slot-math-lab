namespace SlotMath.Core.Mechanics;

/// <summary>
/// A pure evaluator that scores a board and returns winning combinations.
///
/// IEvaluator is one of the three stable, pluggable interfaces of the mechanic
/// layer.  Evaluators are pure functions over board + state — they must not
/// perform random draws or I/O.
///
/// The standard library implements common evaluators (lines, ways, cluster,
/// scatter, Megaways); plugins can supply novel evaluators implementing this
/// same interface without any engine changes.
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
