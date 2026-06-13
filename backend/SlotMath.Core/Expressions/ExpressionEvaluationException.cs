namespace SlotMath.Core.Expressions;

// ═══════════════════════════════════════════════════════════════════════════
//  Level-(b) expression evaluation errors (PRD v3.1, D1 + D20)
//
//  Partial operations — division/modulo by zero, array index out of range,
//  min/max over an empty array — produce a DETERMINISTIC, LOCATED evaluation
//  error, never undefined behavior and never a silent value. The error is
//  identical on both the exact and sampled paths (the sampled evaluator
//  delegates to the exact one). sum/product/count over an empty array return
//  their identities (0, 1, 0); fold is total (it has an explicit init).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>A located, deterministic level-(b) expression evaluation error (D1, D20).</summary>
public sealed class ExpressionEvaluationException : Exception
{
    /// <summary>Machine-readable error code (see <see cref="EvalErrorCodes"/>).</summary>
    public string Code { get; }

    /// <summary>Location within the expression (operator / aggregate / index) (D20).</summary>
    public string? Location { get; }

    public ExpressionEvaluationException(string code, string message, string? location = null)
        : base(message)
    {
        Code = code;
        Location = location;
    }
}

/// <summary>Stable codes for level-(b) evaluation errors (D20).</summary>
public static class EvalErrorCodes
{
    public const string DivisionByZero = "EVAL_DIVISION_BY_ZERO";
    public const string ModuloByZero = "EVAL_MODULO_BY_ZERO";
    public const string IndexOutOfRange = "EVAL_INDEX_OUT_OF_RANGE";
    public const string EmptyMinMax = "EVAL_EMPTY_MIN_MAX";
}
