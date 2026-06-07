using System.Text.Json.Serialization;

namespace SlotMath.Core.Model;

// ── Expression AST (level b) ───────────────────────────────────────────

[JsonPolymorphic(TypeDiscriminatorPropertyName = "exprType")]
[JsonDerivedType(typeof(ConstantExpr), "constant")]
[JsonDerivedType(typeof(FieldAccessExpr), "fieldAccess")]
[JsonDerivedType(typeof(BinaryExpr), "binary")]
[JsonDerivedType(typeof(CompareExpr), "compare")]
[JsonDerivedType(typeof(IfExpr), "if")]
[JsonDerivedType(typeof(AggregateExpr), "aggregate")]
[JsonDerivedType(typeof(NotExpr), "not")]
[JsonDerivedType(typeof(CallExpr), "call")]
public abstract record Expression
{
    public string? Annotation { get; init; }
}

// ── Constant ───────────────────────────────────────────────────────────

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConstantKind
{
    Integer,
    Rational,
    Boolean,
    String,
}

public sealed record ConstantExpr : Expression
{
    public required ConstantKind Kind { get; init; }
    public required string Value { get; init; } // serialised as string for exact rationals
}

// ── Field / index access ───────────────────────────────────────────────

public sealed record FieldAccessExpr : Expression
{
    public required string[] Path { get; init; } // e.g. ["board", "cells", "0", "symbol"]
    public string? Target { get; init; } // "board" | "state"
}

// ── Binary operation ───────────────────────────────────────────────────

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BinaryOp
{
    Add,        // +
    Sub,        // -
    Mul,        // *
    Div,        // /
    And,        // &&
    Or,         // ||
}

public sealed record BinaryExpr : Expression
{
    public required BinaryOp Op { get; init; }
    public required Expression Left { get; init; }
    public required Expression Right { get; init; }
}

// ── Comparison ─────────────────────────────────────────────────────────

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CompareOp
{
    Eq,         // ==
    Neq,        // !=
    Lt,         // <
    Gt,         // >
    Lte,        // <=
    Gte,        // >=
}

public sealed record CompareExpr : Expression
{
    public required CompareOp Op { get; init; }
    public required Expression Left { get; init; }
    public required Expression Right { get; init; }
}

// ── Conditional ────────────────────────────────────────────────────────

public sealed record IfExpr : Expression
{
    public required Expression Condition { get; init; }
    public required Expression ThenExpr { get; init; }
    public required Expression ElseExpr { get; init; }
}

// ── Board aggregation ──────────────────────────────────────────────────

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AggregateFunc
{
    Sum,
    Product,
    Count,
    Min,
    Max,
}

public sealed record AggregateExpr : Expression
{
    public required AggregateFunc Func { get; init; }
    public Expression? Predicate { get; init; }
    public required string Target { get; init; } // "board" | field path prefix
}

// ── Logical not ────────────────────────────────────────────────────────

public sealed record NotExpr : Expression
{
    public required Expression Expr { get; init; }
}

// ── Named function call ────────────────────────────────────────────────

public sealed record CallExpr : Expression
{
    public required string Function { get; init; }
    public Expression[] Args { get; init; } = Array.Empty<Expression>();
}
