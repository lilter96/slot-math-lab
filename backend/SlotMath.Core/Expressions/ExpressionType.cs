using System.Numerics;
using SlotMath.Core.Mechanics;

namespace SlotMath.Core.Expressions;

// ═══════════════════════════════════════════════════════════════════════════
//  Expression type system (level b)
//
//  Every expression evaluates to one of these result types.  The type
//  checker infers this bottom-up and the evaluator produces values of
//  the corresponding runtime type.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// The result type of an expression after type inference.
/// </summary>
public enum ExprType
{
    /// <summary>Arithmetic values: on the exact path these are BigInteger rationals;
    /// on the sampled path they are double.</summary>
    Number,

    /// <summary>Boolean (used in predicates, conditions).</summary>
    Boolean,

    /// <summary>String (symbol ids, labels).</summary>
    String,

    /// <summary>A WeightSet — only valid for weight-valued ports (Draw nodes).</summary>
    Weights,

    /// <summary>A symbol identifier (treated as string internally).</summary>
    Symbol,

    /// <summary>Type error — used internally during inference.</summary>
    Error,
}

// ═══════════════════════════════════════════════════════════════════════════
//  Runtime value for the exact path — rational amounts
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// A runtime value produced by evaluating an expression on the exact path.
/// Amounts carry exact rational (BigInteger) values; booleans and strings
/// round out the set.
/// </summary>
public readonly struct ExprValue : IEquatable<ExprValue>
{
    public ExprType Kind { get; }

    public BigInteger NumberNumerator { get; }
    public BigInteger NumberDenominator { get; }
    public bool BoolValue { get; }
    public string? StringValue { get; }

    // ── Constructors ──────────────────────────────────────────────────────

    private ExprValue(ExprType kind, BigInteger num, BigInteger den, bool b, string? s)
    {
        Kind = kind;
        NumberNumerator = num;
        NumberDenominator = den;
        BoolValue = b;
        StringValue = s;
    }

    public static ExprValue Number(BigInteger value)
        => new(ExprType.Number, value, 1, false, null);

    public static ExprValue Rational(BigInteger num, BigInteger den)
    {
        if (den == 0) throw new DivideByZeroException("Denominator cannot be zero.");
        var reduced = Math.Rational.Reduce(num, den);
        return new ExprValue(ExprType.Number, reduced.Num, reduced.Den, false, null);
    }

    public static ExprValue Bool(bool value)
        => new(ExprType.Boolean, 0, 1, value, null);

    public static ExprValue String(string value)
        => new(ExprType.String, 0, 1, false, value);

    public static ExprValue Symbol(string value)
        => new(ExprType.Symbol, 0, 1, false, value);

    /// <summary>Convert the rational number to a single BigInteger (truncating division).</summary>
    public BigInteger AsInteger() =>
        Kind == ExprType.Number ? NumberNumerator / NumberDenominator : 0;

    /// <summary>Convert to double (for sampled/hybrid).</summary>
    public double AsDouble() =>
        Kind == ExprType.Number
            ? (double)NumberNumerator / (double)NumberDenominator
            : 0.0;

    /// <summary>Arithmetic multiply of two number values.</summary>
    public static ExprValue Mul(ExprValue a, ExprValue b)
    {
        var num = a.NumberNumerator * b.NumberNumerator;
        var den = a.NumberDenominator * b.NumberDenominator;
        return Rational(num, den);
    }

    /// <summary>Arithmetic divide.</summary>
    public static ExprValue Div(ExprValue a, ExprValue b)
    {
        var num = a.NumberNumerator * b.NumberDenominator;
        var den = a.NumberDenominator * b.NumberNumerator;
        return Rational(num, den);
    }

    /// <summary>Arithmetic add.</summary>
    public static ExprValue Add(ExprValue a, ExprValue b)
    {
        var num = a.NumberNumerator * b.NumberDenominator + b.NumberNumerator * a.NumberDenominator;
        var den = a.NumberDenominator * b.NumberDenominator;
        return Rational(num, den);
    }

    /// <summary>Arithmetic subtract.</summary>
    public static ExprValue Sub(ExprValue a, ExprValue b)
    {
        var num = a.NumberNumerator * b.NumberDenominator - b.NumberNumerator * a.NumberDenominator;
        var den = a.NumberDenominator * b.NumberDenominator;
        return Rational(num, den);
    }

    public bool Equals(ExprValue other) =>
        Kind == other.Kind
        && NumberNumerator == other.NumberNumerator
        && NumberDenominator == other.NumberDenominator
        && BoolValue == other.BoolValue
        && StringValue == other.StringValue;

    public override bool Equals(object? obj) => obj is ExprValue v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Kind, NumberNumerator, NumberDenominator, BoolValue, StringValue);

    public override string ToString() => Kind switch
    {
        ExprType.Number => $"{NumberNumerator}/{NumberDenominator}",
        ExprType.Boolean => BoolValue.ToString(),
        ExprType.String => $"\"{StringValue}\"",
        ExprType.Symbol => $"symbol:{StringValue}",
        _ => "?"
    };
}

// ═══════════════════════════════════════════════════════════════════════════
//  Type-check context — carries available field schemas
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Describes the type of a field accessible in an expression context
/// (from board, state, or constants).
/// </summary>
public sealed record FieldDescriptor
{
    public required string Name { get; init; }
    public ExprType Type { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Context for type-checking an expression.  Carries the schemas
/// of the board and game state so the type-checker can resolve
/// field accesses.
/// </summary>
public sealed class TypeCheckContext
{
    /// <summary>Expected result type for the expression (Number, Boolean, String, Weights).</summary>
    public ExprType ExpectedType { get; init; } = ExprType.Number;

    /// <summary>Board-level fields (e.g. "rows", "cols") and cell fields
    /// accessible inside aggregations.</summary>
    public IReadOnlyList<FieldDescriptor> BoardFields { get; init; } = Array.Empty<FieldDescriptor>();

    /// <summary>State-level fields (game-specific recurrence state).</summary>
    public IReadOnlyList<FieldDescriptor> StateFields { get; init; } = Array.Empty<FieldDescriptor>();

    /// <summary>Cell-level fields accessible inside aggregation predicates
    /// (e.g. "symbol", "isLocked", decoration keys).</summary>
    public IReadOnlyList<FieldDescriptor> CellFields { get; init; } = Array.Empty<FieldDescriptor>();

    /// <summary>Decorations known to be present on cells (for type resolution).</summary>
    public IReadOnlyDictionary<string, ExprType> DecorationTypes { get; init; }
        = new Dictionary<string, ExprType>();

    /// <summary>Create a default context for standalone expression checking.</summary>
    public static TypeCheckContext Default { get; } = new()
    {
        BoardFields = new[]
        {
            new FieldDescriptor { Name = "rows", Type = ExprType.Number },
            new FieldDescriptor { Name = "cols", Type = ExprType.Number },
        },
        CellFields = new[]
        {
            new FieldDescriptor { Name = "symbol", Type = ExprType.Symbol },
            new FieldDescriptor { Name = "isLocked", Type = ExprType.Boolean },
            new FieldDescriptor { Name = "isEmpty", Type = ExprType.Boolean },
        },
    };

    /// <summary>Resolve a field path to its type (or null if not found).</summary>
    public ExprType? ResolvePath(string[] path, string? target)
    {
        if (path.Length == 0) return null;

        IReadOnlyList<FieldDescriptor> fields;
        if (target == "state")
            fields = StateFields;
        else if (target == "board" || target == null)
            fields = BoardFields;
        else
            return null;

        var first = path[0];
        var fd = fields.FirstOrDefault(f =>
            string.Equals(f.Name, first, StringComparison.OrdinalIgnoreCase));
        if (fd == null && path.Length == 1 && target == "board")
        {
            // Try cell-level fields (used as shorthand inside aggregations)
            fd = CellFields.FirstOrDefault(f =>
                string.Equals(f.Name, first, StringComparison.OrdinalIgnoreCase));
        }

        return fd?.Type;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Type-check error
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// A precise type-checking error with location and explanation.
/// </summary>
public sealed record TypeCheckError
{
    public required string Message { get; init; }
    public string? NodeAnnotation { get; init; }
    public string? Code { get; init; }

    public override string ToString() =>
        NodeAnnotation is null
            ? Message
            : $"[{NodeAnnotation}] {Message}";
}
