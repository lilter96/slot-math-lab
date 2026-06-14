namespace SlotMath.Codegen.Runtime;

// ═══════════════════════════════════════════════════════════════════════════
//  Runtime contracts consumed by GENERATED game code (G7).
//
//  A compiled game body makes its weighted random choices through an
//  IDrawDriver.  The SAME generated body serves two masters:
//    • the zero-allocation sampled hot path (SampledDrawDriver), and
//    • the exact-PMF enumerator (ExactPmf) used to PROVE the generated code is
//      distribution-equivalent to the interpreter (invariant 11 / D25).
//  This is the seam that lets one emitted method be both fast AND verifiable.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Supplies a weighted random outcome index to generated game code.  The
/// generated body calls <see cref="Draw"/> at each draw point, in deterministic
/// order, passing the (static, pre-allocated) integer weight table.
/// </summary>
public interface IDrawDriver
{
    /// <summary>Choose an outcome index in [0, weights.Length) by weight.</summary>
    int Draw(ReadOnlySpan<long> weights);
}

/// <summary>
/// Contract implemented by every generated game class.  State is held on the
/// instance; <see cref="SetInitial"/> seeds the recurrence state from a dict
/// (used by verification to inject random initial states), and
/// <see cref="RunSpin"/> executes one round, reading draws from the driver and
/// returning the round win in scaled credit units.
/// </summary>
public interface ICompiledGame
{
    /// <summary>Seed the initial recurrence state for subsequent spins.</summary>
    void SetInitial(IReadOnlyDictionary<string, object?> initialState);

    /// <summary>Run one round from the seeded initial state; return the win.</summary>
    long RunSpin(IDrawDriver driver);
}
