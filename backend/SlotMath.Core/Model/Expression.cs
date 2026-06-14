using System.Text.Json.Serialization;
using SlotMath.Core.Expressions;

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
[JsonDerivedType(typeof(FoldExpr), "fold")]
[JsonDerivedType(typeof(MapExpr), "map")]
[JsonDerivedType(typeof(FilterExpr), "filter")]
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

// ── State-array aggregation (sum/product/count/min/max with a predicate) ──
//
//  aggregate_Func(state[StateKey], ItemName => Predicate, ItemName => ValueExpr)
//
//  Harmonised with FoldExpr/MapExpr/FilterExpr: iterates the array stored at
//  state[StateKey], binding each element under ItemName so both the optional
//  Predicate (filter) and the optional ValueExpr (selector) can reference it.
//  ValueExpr extracts the numeric value to aggregate from each element — e.g.
//  `item.multiplier` for an array of cell records; if absent, the element
//  itself is aggregated (when numeric).  This carries no engine Board type
//  (invariant 4): elements may be plain symbols or cell records.

public sealed record AggregateExpr : Expression
{
    public required AggregateFunc Func { get; init; }

    /// <summary>
    /// Key in the state dictionary whose value is the array to aggregate over
    /// (invariant 4: a "board" is just a user-defined array in state S).
    /// </summary>
    public required string StateKey { get; init; }

    /// <summary>Name bound to the current array element inside Predicate and ValueExpr.</summary>
    public string ItemName { get; init; } = "item";

    /// <summary>Expected type of array items (defaults to String).</summary>
    public ExprType ItemType { get; init; } = ExprType.String;

    /// <summary>Optional filter: only elements where this evaluates true are aggregated.</summary>
    public Expression? Predicate { get; init; }

    /// <summary>
    /// Optional selector extracting the numeric value to aggregate from each
    /// element (e.g. <c>item.multiplier</c>). If absent, the element itself is
    /// aggregated (parsed to a number when it is a numeric string/value).
    /// </summary>
    public Expression? ValueExpr { get; init; }
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

// ── Bounded map over a state array (level-b iteration atom) ───────────
//
//  map(state[StateKey], ItemName => Body)
//
//  Transforms every element of the array stored at state[StateKey] via
//  the Body expression.  Returns ExprType.Array (a new array of
//  transformed values).  No nested iteration allowed in Body.

public sealed record MapExpr : Expression
{
    /// <summary>Key in the state dictionary whose value is the array to map over.</summary>
    public required string StateKey { get; init; }

    /// <summary>Name bound to the current array element inside the lambda body.</summary>
    public required string ItemName { get; init; }

    /// <summary>Optional name bound to the current element's 0-based index (invariant 4: enables position-aware board mechanics).</summary>
    public string? IndexName { get; init; }

    /// <summary>Lambda body — evaluated once per element; may not contain fold/map/filter.</summary>
    public required Expression Body { get; init; }

    /// <summary>Expected type of array items (defaults to String).</summary>
    public ExprType ItemType { get; init; } = ExprType.String;
}

// ── Bounded filter over a state array (level-b iteration atom) ─────────
//
//  filter(state[StateKey], ItemName => Predicate)
//
//  Keeps only elements for which Predicate is true.  Returns
//  ExprType.Array (a new array of the same element type).  No nested
//  iteration allowed in Predicate.

public sealed record FilterExpr : Expression
{
    /// <summary>Key in the state dictionary whose value is the array to filter.</summary>
    public required string StateKey { get; init; }

    /// <summary>Name bound to the current array element inside the predicate.</summary>
    public required string ItemName { get; init; }

    /// <summary>Optional name bound to the current element's 0-based index (invariant 4: enables position-aware board mechanics).</summary>
    public string? IndexName { get; init; }

    /// <summary>Predicate — must return Boolean; may not contain fold/map/filter.</summary>
    public required Expression Predicate { get; init; }

    /// <summary>Expected type of array items (defaults to String).</summary>
    public ExprType ItemType { get; init; } = ExprType.String;
}

// ── Bounded fold over a state array (level-b iteration atom) ──────────
//
//  fold(state[StateKey], Init, (AccName, ItemName) => Body)
//
//  Iterates over the array stored at state[StateKey].  The accumulator
//  (AccName) and current item (ItemName) are bound into the state dict
//  for each iteration; the Body expression accesses them as
//  state.AccName and state.ItemName.  No nested fold allowed (one level
//  of bounded iteration keeps the grammar exact-analysable).

public sealed record FoldExpr : Expression
{
    /// <summary>Key in the state dictionary whose value is the array to fold over.</summary>
    public required string StateKey { get; init; }

    /// <summary>Name bound to the accumulator inside the lambda body.</summary>
    public required string AccName { get; init; }

    /// <summary>Name bound to the current array element inside the lambda body.</summary>
    public required string ItemName { get; init; }

    /// <summary>Optional name bound to the current element's 0-based index (invariant 4: enables position-aware board mechanics).</summary>
    public string? IndexName { get; init; }

    /// <summary>Initial accumulator value.</summary>
    public required Expression Init { get; init; }

    /// <summary>Lambda body — must return the same type as Init.</summary>
    public required Expression Body { get; init; }

    /// <summary>Expected type of array items (defaults to String).</summary>
    public ExprType ItemType { get; init; } = ExprType.String;
}
