using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using SlotMath.Core.Math;

namespace SlotMath.Core.Expressions;

// ═══════════════════════════════════════════════════════════════════════════
//  Expression type system (level b)
//
//  Every expression evaluates to one of these result types.  The type
//  checker infers this bottom-up and the evaluator produces values of
//  the corresponding runtime type.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
///     The result type of an expression after type inference.
/// </summary>
public enum ExprType
{
    /// <summary>
    ///     Arithmetic values: on the exact path these are BigInteger rationals;
    ///     on the sampled path they are double.
    /// </summary>
    Number,

    /// <summary>Boolean (used in predicates, conditions).</summary>
    Boolean,

    /// <summary>String (symbol ids, labels).</summary>
    String,

    /// <summary>A WeightSet — only valid for weight-valued ports (Draw nodes).</summary>
    Weights,

    /// <summary>A symbol identifier (treated as string internally).</summary>
    Symbol,

    /// <summary>An array of typed values — produced by map/filter expressions.</summary>
    Array,

    /// <summary>Type error — used internally during inference.</summary>
    Error,

    /// <summary>An immutable record with named, typed fields.</summary>
    Record,

    /// <summary>An explicitly absent value inside a collection; never a numeric zero.</summary>
    Null
}

// ═══════════════════════════════════════════════════════════════════════════
//  Runtime value for the exact path — rational amounts
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
///     A runtime value produced by evaluating an expression on the exact path.
///     Amounts carry exact rational (BigInteger) values; booleans and strings
///     round out the set.
/// </summary>
public readonly struct ExprValue : IEquatable<ExprValue>
{
    // Sixteen bytes: a null reference denotes a full-width inline integer.
    // Other values keep their kind/flags in the low 16 payload bits and use
    // a shared sentinel when they have no object payload. Large numbers and
    // proper rationals still use the existing exact BigNumber representation.
    private readonly object? _ref;
    private static readonly object EmptyReference = new();

    private ushort Tag
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _ref is null ? (ushort)0 : (ushort)InlineInteger;
    }

    private object? Reference => ReferenceEquals(_ref, EmptyReference) ? null : _ref;

    private const ushort KindMask = 0x00FF;
    private const ushort SymbolsFlag = 0x0100;
    private const ushort BigFlag = 0x0200;
    private const ushort NumericTextFlag = 0x0400;

    private sealed class BigNumber(BigInteger numerator, BigInteger denominator)
    {
        public readonly BigInteger Denominator = denominator;
        public readonly BigInteger Numerator = numerator;
    }

    public ExprType Kind => (ExprType)(Tag & KindMask);

    public BigInteger NumberNumerator => Tag == 0 ? InlineInteger
        : (Tag & BigFlag) != 0 ? Unsafe.As<BigNumber>(_ref!).Numerator : BigInteger.Zero;

    public BigInteger NumberDenominator =>
        (Tag & BigFlag) != 0 ? Unsafe.As<BigNumber>(_ref!).Denominator : BigInteger.One;

    public bool BoolValue => Tag == (ushort)ExprType.Boolean && InlineInteger >> 16 != 0;

    // Immutable arrays carry this once, so storing/rebinding them need not
    // scan every item to reproduce ToStateObject's symbol normalization.
    internal bool ContainsSymbols => (Tag & SymbolsFlag) != 0;
    public string? StringValue => Kind is ExprType.String or ExprType.Symbol ? Unsafe.As<string>(Reference) : null;

    /// <summary>Array items — non-null only when Kind == Array.</summary>
    public IReadOnlyList<ExprValue>? ArrayValue =>
        Kind == ExprType.Array ? Unsafe.As<IReadOnlyList<ExprValue>>(Reference) : null;

    public IReadOnlyDictionary<string, ExprValue>? RecordValue => Kind == ExprType.Record
        ? Unsafe.As<IReadOnlyDictionary<string, ExprValue>>(Reference)
        : null;

    // ── Fast accessors for the evaluators ─────────────────────────────────

    /// <summary>True when this is an integer stored inline in 64 bits.</summary>
    internal bool IsInlineInteger => Tag == 0;

    /// <summary>The inline integer; meaningful only when <see cref="IsInlineInteger" />.</summary>
    internal long InlineInteger { get; }

    /// <summary>Compiler use only after proving this is an integer in [0, 2^64-1].</summary>
    internal ulong MaskUnchecked
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _ref is null ? unchecked((ulong)InlineInteger) : (ulong)Unsafe.As<BigNumber>(_ref).Numerator;
    }

    internal bool IsBoolean => Tag == (ushort)ExprType.Boolean;
    internal bool IsString => (Tag & KindMask) == (ushort)ExprType.String;
    internal bool IsText => (Tag & KindMask) is (ushort)ExprType.String or (ushort)ExprType.Symbol;
    internal string TextUnchecked => Unsafe.As<string>(Reference!);

    /// <summary>The backing array when the items are stored as one, otherwise null.</summary>
    internal ExprValue[]? ItemsArray => Kind == ExprType.Array ? _ref as ExprValue[] : null;

    /// <summary>Text that is a canonical 64-bit integer literal carries its parsed value.</summary>
    internal bool TryGetNumericText(out long value)
    {
        value = InlineInteger >> 16;
        return (Tag & NumericTextFlag) != 0;
    }

    // ── Constructors ──────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ExprValue(ushort tag, long bits, object? reference)
    {
        InlineInteger = tag == 0 ? bits : unchecked((bits << 16) | tag);
        _ref = tag == 0 ? null : reference ?? EmptyReference;
    }

    private static bool HasSymbols(IReadOnlyList<ExprValue>? items)
    {
        if (items is null)
        {
            return false;
        }

        if (items is ExprValue[] array)
        {
            foreach (ref readonly ExprValue item in array.AsSpan())
            {
                if (item.ContainsSymbols)
                {
                    return true;
                }
            }

            return false;
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].ContainsSymbols)
            {
                return true;
            }
        }

        return false;
    }

    public static ExprValue Number(long value) => new(0, value, null);

    public static ExprValue Number(BigInteger value)
        => value >= long.MinValue && value <= long.MaxValue
            ? new ExprValue(0, (long)value, null)
            : new ExprValue((ushort)ExprType.Number | BigFlag, 0, new BigNumber(value, BigInteger.One));

    public static ExprValue Rational(BigInteger num, BigInteger den)
    {
        if (den == 0)
        {
            throw new DivideByZeroException("Denominator cannot be zero.");
        }

        if (den.IsOne)
        {
            return Number(num);
        }

        (BigInteger Num, BigInteger Den) reduced = Math.Rational.Reduce(num, den);
        return reduced.Den.IsOne
            ? Number(reduced.Num)
            : new ExprValue((ushort)ExprType.Number | BigFlag, 0, new BigNumber(reduced.Num, reduced.Den));
    }

    public static ExprValue Bool(bool value)
        => new((ushort)ExprType.Boolean, value ? 1 : 0, null);

    public static ExprValue String(string value) => Text((ushort)ExprType.String, value);

    public static ExprValue Symbol(string value) => Text((ushort)ExprType.Symbol | SymbolsFlag, value);

    // tonumber of a plain integer literal is a pure function of the text, so
    // the parse is done once, when the value is created.
    private static ExprValue Text(ushort tag, string value)
    {
        if (value is not { Length: >= 1 and <= 19 })
        {
            return new ExprValue(tag, 0, value);
        }

        var start = value[0] is '+' or '-' ? 1 : 0;
        var digits = value.Length - start;
        if (digits is < 1 or > 18)
        {
            return new ExprValue(tag, 0, value);
        }

        long parsed = 0;
        for (var i = start; i < value.Length; i++)
        {
            var digit = (uint)(value[i] - '0');
            if (digit > 9)
            {
                return new ExprValue(tag, 0, value);
            }

            parsed = parsed * 10 + digit;
        }

        parsed = value[0] == '-' ? -parsed : parsed;
        return parsed is >= -(1L << 47) and < 1L << 47
            ? new ExprValue((ushort)(tag | NumericTextFlag), parsed, value)
            : new ExprValue(tag, 0, value);
    }

    public static ExprValue Array(IReadOnlyList<ExprValue> items)
        => new(HasSymbols(items) ? (ushort)((ushort)ExprType.Array | SymbolsFlag) : (ushort)ExprType.Array, 0, items);

    /// <summary>Wrap a freshly built array whose symbol content is already known.</summary>
    internal static ExprValue ArrayOwned(ExprValue[] items, bool containsSymbols)
        => new(containsSymbols ? (ushort)((ushort)ExprType.Array | SymbolsFlag) : (ushort)ExprType.Array, 0, items);

    public static ExprValue Record(IReadOnlyDictionary<string, ExprValue> fields)
    {
        var copy = new ReadOnlyDictionary<string, ExprValue>(
            fields.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
        return new ExprValue(
            copy.Values.Any(v => v.ContainsSymbols)
                ? (ushort)((ushort)ExprType.Record | SymbolsFlag)
                : (ushort)ExprType.Record, 0, copy);
    }

    // Internal immutable ownership is established by the sampler's bounded arena.
    // Public Record continues to copy caller-owned dictionaries.
    internal static ExprValue RecordOwned(IReadOnlyDictionary<string, ExprValue> fields, bool containsSymbols)
        => new(containsSymbols ? (ushort)((ushort)ExprType.Record | SymbolsFlag) : (ushort)ExprType.Record, 0, fields);

    public static ExprValue Null => new((ushort)ExprType.Null, 0, null);

    /// <summary>Convert the rational number to a single BigInteger (truncating division).</summary>
    public BigInteger AsInteger() =>
        Tag == 0 ? InlineInteger
        : Kind == ExprType.Number ? NumberNumerator / NumberDenominator
        : throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "Numeric expression required.");

    /// <summary>Convert to double (for sampled/hybrid).</summary>
    public double AsDouble()
    {
        if (Kind != ExprType.Number)
        {
            throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "Numeric expression required.");
        }

        // Integers of magnitude ≤ 2^53 convert exactly; larger ones keep the
        // BigInteger conversion so results stay bit-identical.
        if (Tag == 0 && InlineInteger is >= -(1L << 53) and <= 1L << 53)
        {
            return InlineInteger;
        }

        var numerator = (double)NumberNumerator;
        if (NumberDenominator.IsOne)
        {
            return numerator;
        }

        var denominator = (double)NumberDenominator;
        return double.IsFinite(numerator) && double.IsFinite(denominator)
            ? numerator / denominator
            : new Rational(NumberNumerator, NumberDenominator).ToDouble();
    }

    /// <summary>Arithmetic multiply of two number values.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Mul(ExprValue a, ExprValue b)
    {
        if ((a.Tag | b.Tag) == 0)
        {
            var high = System.Math.BigMul(a.InlineInteger, b.InlineInteger, out var low);
            if (high == low >> 63)
            {
                return new ExprValue(0, low, null);
            }
        }

        return MulSlow(a, b);
    }

    private static ExprValue MulSlow(ExprValue a, ExprValue b)
    {
        if (a.NumberDenominator.IsOne && b.NumberDenominator.IsOne)
        {
            return Number(a.NumberNumerator * b.NumberNumerator);
        }

        BigInteger num = a.NumberNumerator * b.NumberNumerator;
        BigInteger den = a.NumberDenominator * b.NumberDenominator;
        return Rational(num, den);
    }

    /// <summary>
    ///     Arithmetic divide. Partial (D1): division by zero produces a deterministic,
    ///     located evaluation error on both paths — never a silent value.
    /// </summary>
    public static ExprValue Div(ExprValue a, ExprValue b)
    {
        if ((a.Tag | b.Tag) == 0 && b.InlineInteger > 0 && a.InlineInteger % b.InlineInteger == 0)
        {
            return new ExprValue(0, a.InlineInteger / b.InlineInteger, null);
        }

        if (b.NumberNumerator == 0)
        {
            throw new ExpressionEvaluationException(
                EvalErrorCodes.DivisionByZero, "Division by zero in a level-(b) expression (D1).", "/");
        }

        BigInteger num = a.NumberNumerator * b.NumberDenominator;
        BigInteger den = a.NumberDenominator * b.NumberNumerator;
        return Rational(num, den);
    }

    /// <summary>Arithmetic add.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Add(ExprValue a, ExprValue b)
    {
        if ((a.Tag | b.Tag) == 0)
        {
            var sum = unchecked(a.InlineInteger + b.InlineInteger);
            if (((a.InlineInteger ^ sum) & (b.InlineInteger ^ sum)) >= 0)
            {
                return new ExprValue(0, sum, null);
            }
        }

        return AddSlow(a, b);
    }

    private static ExprValue AddSlow(ExprValue a, ExprValue b)
    {
        if (a.NumberDenominator.IsOne && b.NumberDenominator.IsOne)
        {
            return Number(a.NumberNumerator + b.NumberNumerator);
        }

        BigInteger num = a.NumberNumerator * b.NumberDenominator + b.NumberNumerator * a.NumberDenominator;
        BigInteger den = a.NumberDenominator * b.NumberDenominator;
        return Rational(num, den);
    }

    /// <summary>Arithmetic subtract.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Sub(ExprValue a, ExprValue b)
    {
        if ((a.Tag | b.Tag) == 0)
        {
            var difference = unchecked(a.InlineInteger - b.InlineInteger);
            if (((a.InlineInteger ^ b.InlineInteger) & (a.InlineInteger ^ difference)) >= 0)
            {
                return new ExprValue(0, difference, null);
            }
        }

        return SubSlow(a, b);
    }

    private static ExprValue SubSlow(ExprValue a, ExprValue b)
    {
        if (a.NumberDenominator.IsOne && b.NumberDenominator.IsOne)
        {
            return Number(a.NumberNumerator - b.NumberNumerator);
        }

        BigInteger num = a.NumberNumerator * b.NumberDenominator - b.NumberNumerator * a.NumberDenominator;
        BigInteger den = a.NumberDenominator * b.NumberDenominator;
        return Rational(num, den);
    }

    /// <summary>Order two number values exactly.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CompareNumbers(ExprValue a, ExprValue b)
        => (a.Tag | b.Tag) == 0 ? a.InlineInteger.CompareTo(b.InlineInteger) : CompareNumbersSlow(a, b);

    private static int CompareNumbersSlow(ExprValue a, ExprValue b) =>
        a.NumberDenominator.IsOne && b.NumberDenominator.IsOne
            ? a.NumberNumerator.CompareTo(b.NumberNumerator)
            : (a.NumberNumerator * b.NumberDenominator).CompareTo(b.NumberNumerator * a.NumberDenominator);

    /// <summary>
    ///     Convert to a plain CLR object suitable for storage in the state dictionary.
    ///     Integers → BigInteger; rationals → ExprValue (preserving exactness);
    ///     booleans → bool; strings → string; arrays → object[].
    /// </summary>
    public object? ToStateObject() => Kind switch
    {
        ExprType.Number => NumberDenominator == 1 ? NumberNumerator : this,
        ExprType.Boolean => BoolValue,
        ExprType.String or ExprType.Symbol => StringValue,
        ExprType.Array => ArrayValue?.Select(v => v.ToStateObject()).ToArray(),
        ExprType.Record => RecordValue!.ToDictionary(p => p.Key, p => p.Value.ToStateObject(), StringComparer.Ordinal),
        ExprType.Null => null,
        _ => throw new ExpressionEvaluationException("EVAL_TYPE_ERROR",
            "Unsupported expression value cannot be stored.")
    };

    public bool Equals(ExprValue other)
    {
        if (((Tag ^ other.Tag) & KindMask) != 0)
        {
            return false;
        }

        switch (Kind)
        {
            case ExprType.Number:
                if (((Tag | other.Tag) & BigFlag) == 0)
                {
                    return InlineInteger == other.InlineInteger;
                }

                // Inline integers are canonical: a boxed number never equals one.
                return (Tag & other.Tag & BigFlag) != 0
                       && NumberNumerator == other.NumberNumerator && NumberDenominator == other.NumberDenominator;
            case ExprType.Boolean: return InlineInteger == other.InlineInteger;
            case ExprType.String or ExprType.Symbol:
                return string.Equals(Unsafe.As<string>(Reference), Unsafe.As<string>(other.Reference));
            case ExprType.Array: return ArrayEquality(ArrayValue, other.ArrayValue);
            case ExprType.Record: return RecordEquality(RecordValue, other.RecordValue);
            default: return true;
        }
    }

    public override bool Equals(object? obj) => obj is ExprValue v && Equals(v);

    public override int GetHashCode()
    {
        var h = HashCode.Combine(Kind, NumberNumerator, NumberDenominator, BoolValue, StringValue);
        if (ArrayValue is { } items)
        {
            foreach (ExprValue item in items)
            {
                h = HashCode.Combine(h, item.GetHashCode());
            }
        }

        if (RecordValue is { } fields)
        {
            foreach (KeyValuePair<string, ExprValue> item in fields.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                h = HashCode.Combine(h, item.Key, item.Value.GetHashCode());
            }
        }

        return h;
    }

    public override string ToString() => Kind switch
    {
        ExprType.Number => $"{NumberNumerator}/{NumberDenominator}",
        ExprType.Boolean => BoolValue.ToString(),
        ExprType.String => $"\"{StringValue}\"",
        ExprType.Symbol => $"symbol:{StringValue}",
        ExprType.Array => $"[{string.Join(", ", ArrayValue?.Select(x => x.ToString()) ?? [])}]",
        ExprType.Record =>
            $"{{{string.Join(", ", RecordValue!.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}: {p.Value}"))}}}",
        ExprType.Null => "null",
        _ => "?"
    };

    private static bool ArrayEquality(IReadOnlyList<ExprValue>? a, IReadOnlyList<ExprValue>? b)
    {
        if (a == null && b == null)
        {
            return true;
        }

        if (a == null || b == null)
        {
            return false;
        }

        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RecordEquality(IReadOnlyDictionary<string, ExprValue>? a,
        IReadOnlyDictionary<string, ExprValue>? b) =>
        a is null
            ? b is null
            : b is not null && a.Count == b.Count
                            && a.All(p => b.TryGetValue(p.Key, out ExprValue value) && p.Value.Equals(value));
}

// ═══════════════════════════════════════════════════════════════════════════
//  Type-check context — carries available field schemas
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
///     Describes the type of a field accessible in an expression context
///     (from board, state, or constants).
/// </summary>
public sealed record FieldDescriptor
{
    public required string Name { get; init; }
    public ExprType Type { get; init; }

    /// <summary>Homogeneous scalar element type, when it can be established from the authored graph.</summary>
    public ExprType? ArrayItemType { get; init; }

    public FieldDescriptor? ArrayItem { get; init; }
    public IReadOnlyList<FieldDescriptor> RecordFields { get; init; } = [];

    /// <summary>Proven empty array identity, used only during element inference.</summary>
    public bool ArrayIsEmpty { get; init; }

    public string? Description { get; init; }
}

/// <summary>
///     Context for type-checking an expression.  Carries the schemas
///     of the board and game state so the type-checker can resolve
///     field accesses.
/// </summary>
public sealed class TypeCheckContext
{
    public IReadOnlyList<FieldDescriptor> LocalFields { get; init; } = [];

    public IReadOnlyList<FieldDescriptor> MeasurementFields { get; init; } = [];

    /// <summary>Expected result type for the expression (Number, Boolean, String, Weights).</summary>
    public ExprType ExpectedType { get; init; } = ExprType.Number;

    /// <summary>
    ///     Board-level fields (e.g. "rows", "cols") and cell fields
    ///     accessible inside aggregations.
    /// </summary>
    public IReadOnlyList<FieldDescriptor> BoardFields { get; init; } = Array.Empty<FieldDescriptor>();

    /// <summary>State-level fields (game-specific recurrence state).</summary>
    public IReadOnlyList<FieldDescriptor> StateFields { get; init; } = Array.Empty<FieldDescriptor>();

    /// <summary>
    ///     Cell-level fields accessible inside aggregation predicates
    ///     (e.g. "symbol", "isLocked", decoration keys).
    /// </summary>
    public IReadOnlyList<FieldDescriptor> CellFields { get; init; } = Array.Empty<FieldDescriptor>();

    /// <summary>Decorations known to be present on cells (for type resolution).</summary>
    public IReadOnlyDictionary<string, ExprType> DecorationTypes { get; init; }
        = new Dictionary<string, ExprType>();

    /// <summary>Create a default context for standalone expression checking.</summary>
    public static TypeCheckContext Default { get; } = new()
    {
        BoardFields = new[]
        {
            new FieldDescriptor
            {
                Name = "rows",
                Type = ExprType.Number
            },
            new FieldDescriptor
            {
                Name = "cols",
                Type = ExprType.Number
            }
        },
        CellFields = new[]
        {
            new FieldDescriptor
            {
                Name = "symbol",
                Type = ExprType.Symbol
            },
            new FieldDescriptor
            {
                Name = "isLocked",
                Type = ExprType.Boolean
            },
            new FieldDescriptor
            {
                Name = "isEmpty",
                Type = ExprType.Boolean
            }
        }
    };

    internal TypeCheckContext WithLocal(string name, FieldDescriptor field) => new()
    {
        ExpectedType = ExpectedType,
        BoardFields = BoardFields,
        StateFields = StateFields,
        CellFields = CellFields,
        MeasurementFields = MeasurementFields,
        DecorationTypes = DecorationTypes,
        LocalFields = LocalFields.Where(local => local.Name != name).Append(field with
        {
            Name = name
        }).ToArray()
    };

    /// <summary>Resolve a field path to its type (or null if not found).</summary>
    public ExprType? ResolvePath(string[] path, string? target)
        => ResolveField(path, target)?.Type;

    internal FieldDescriptor? ResolveField(string[] path, string? target)
    {
        if (path.Length == 0)
        {
            return null;
        }

        IReadOnlyList<FieldDescriptor> fields;
        if (target == "state")
        {
            fields = StateFields;
        }
        else if (target == "measurement")
        {
            fields = MeasurementFields;
        }
        else if (target == "board" || target == null)
        {
            fields = BoardFields;
        }
        else
        {
            return null;
        }

        var first = path[0];
        StringComparison comparison = target is "state" or "measurement"
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        FieldDescriptor? fd = fields.LastOrDefault(f => string.Equals(f.Name, first, comparison));
        if (fd == null && path.Length == 1 && target == "board")
        {
            // Try cell-level fields (used as shorthand inside aggregations)
            fd = CellFields.FirstOrDefault(f =>
                string.Equals(f.Name, first, StringComparison.OrdinalIgnoreCase));
        }

        for (var i = 1; i < path.Length && fd is not null; i++)
        {
            if (fd.Type == ExprType.Record)
            {
                fd = fd.RecordFields.SingleOrDefault(f => f.Name == path[i]);
            }
            else if (fd.Type == ExprType.Array && BigInteger.TryParse(path[i],
                         NumberStyles.None, CultureInfo.InvariantCulture,
                         out BigInteger index) && index >= 0)
            {
                fd = fd.ArrayItem ?? (fd.ArrayItemType is { } type
                    ? new FieldDescriptor
                    {
                        Name = "item",
                        Type = type
                    }
                    : null);
            }
            else
            {
                return null;
            }
        }

        return fd;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Type-check error
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
///     A precise type-checking error with location and explanation.
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
