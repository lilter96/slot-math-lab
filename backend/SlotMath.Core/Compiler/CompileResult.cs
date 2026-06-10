using System.Numerics;
using SlotMath.Core.Monad;

namespace SlotMath.Core.Compiler;

// ═══════════════════════════════════════════════════════════════════════════
//  CompileResult — the output of the graph compiler
//
//  Either contains a valid Slot program ready for evaluation, or a list of
//  errors describing why compilation failed.  The errors list is never both
//  empty and the program null.
// ═══════════════════════════════════════════════════════════════════════════

public sealed record CompileResult
{
    /// <summary>The compiled Slot program, or null if validation failed.</summary>
    public Slot<Dictionary<string, object?>, BigInteger>? Program { get; init; }

    /// <summary>
    /// Sub-credit scale of the program's win amounts.  1 when every payout
    /// is integral.  When the config contains fractional paytable payouts
    /// (e.g. "2.5"), the compiler multiplies all win sources by this power
    /// of ten so the value channel stays exact integers; evaluators must
    /// divide it back out (HybridEvaluator does this via RegimeConfig.WinScale).
    /// </summary>
    public BigInteger WinScale { get; init; } = BigInteger.One;

    /// <summary>Validation/compilation errors. Empty means valid.</summary>
    public IReadOnlyList<CompileError> Errors { get; init; } = Array.Empty<CompileError>();

    /// <summary>True when the graph compiled successfully.</summary>
    public bool IsValid => Errors.Count == 0 && Program != null;

    /// <summary>Create a successful result with a compiled program.</summary>
    public static CompileResult Success(Slot<Dictionary<string, object?>, BigInteger> program) =>
        new() { Program = program, Errors = Array.Empty<CompileError>() };

    /// <summary>Create a failure result with a list of errors.</summary>
    public static CompileResult Failure(IReadOnlyList<CompileError> errors) =>
        new() { Program = null, Errors = errors };

    /// <summary>Create a failure result with a single error.</summary>
    public static CompileResult Failure(CompileError error) =>
        Failure(new[] { error });
}
