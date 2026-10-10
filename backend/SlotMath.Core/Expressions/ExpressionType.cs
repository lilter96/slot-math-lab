using System.Numerics;
using System.Runtime.CompilerServices;
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

    /// <summary>An array of typed values — produced by map/filter expressions.</summary>
    Array,

    /// <summary>Type error — used internally during inference.</summary>
    Error,

    /// <summary>An immutable record with named, typed fields.</summary>
    Record,

    /// <summary>An explicitly absent value inside a collection; never a numeric zero.</summary>
    Null,
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
    // Compact tagged representation (24 bytes, one object reference). Integers
    // that fit in 64 bits are stored inline; larger integers and proper
    // rationals are boxed, so every value remains an exact rational (D1).
    // A value is an inline integer exactly when Tag == 0.
    private readonly object? _ref;
    private readonly long _bits;
    private readonly ushort _tag;

    private const ushort KindMask = 0x00FF;
    private const ushort SymbolsFlag = 0x0100;
    private const ushort BigFlag = 0x0200;
    private const ushort NumericTextFlag = 0x0400;

    private sealed class BigNumber(BigInteger numerator, BigInteger denominator)
    {
        public readonly BigInteger Numerator = numerator;
        public readonly BigInteger Denominator = denominator;
    }

    public ExprType Kind => (ExprType)(_tag & KindMask);

    public BigInteger NumberNumerator => _tag == 0 ? _bits
        : (_tag & BigFlag) != 0 ? Unsafe.As<BigNumber>(_ref!).Numerator : BigInteger.Zero;
    public BigInteger NumberDenominator => (_tag & BigFlag) != 0 ? Unsafe.As<BigNumber>(_ref!).Denominator : BigInteger.One;
    public bool BoolValue => _tag == (ushort)ExprType.Boolean && _bits != 0;
    // Immutable arrays carry this once, so storing/rebinding them need not
    // scan every item to reproduce ToStateObject's symbol normalization.
    internal bool ContainsSymbols => (_tag & SymbolsFlag) != 0;
    public string? StringValue => Kind is ExprType.String or ExprType.Symbol ? Unsafe.As<string>(_ref) : null;

    /// <summary>Array items — non-null only when Kind == Array.</summary>
    public IReadOnlyList<ExprValue>? ArrayValue => Kind == ExprType.Array ? Unsafe.As<IReadOnlyList<ExprValue>>(_ref) : null;
    public IReadOnlyDictionary<string, ExprValue>? RecordValue => Kind == ExprType.Record ? Unsafe.As<IReadOnlyDictionary<string, ExprValue>>(_ref) : null;

    // ── Fast accessors for the evaluators ─────────────────────────────────

    /// <summary>True when this is an integer stored inline in 64 bits.</summary>
    internal bool IsInlineInteger => _tag == 0;
    /// <summary>The inline integer; meaningful only when <see cref="IsInlineInteger"/>.</summary>
    internal long InlineInteger => _bits;
    internal bool IsBoolean => _tag == (ushort)ExprType.Boolean;
    internal bool IsString => (_tag & KindMask) == (ushort)ExprType.String;
    internal bool IsText => (_tag & KindMask) is (ushort)ExprType.String or (ushort)ExprType.Symbol;
    internal string TextUnchecked => Unsafe.As<string>(_ref!);
    /// <summary>The backing array when the items are stored as one, otherwise null.</summary>
    internal ExprValue[]? ItemsArray => Kind == ExprType.Array ? _ref as ExprValue[] : null;
    /// <summary>Text that is a canonical 64-bit integer literal carries its parsed value.</summary>
    internal bool TryGetNumericText(out long value) { value = _bits; return (_tag & NumericTextFlag) != 0; }

    // ── Constructors ──────────────────────────────────────────────────────

    private ExprValue(ushort tag, long bits, object? reference)
    {
        _tag = tag;
        _bits = bits;
        _ref = reference;
    }

    private static bool HasSymbols(IReadOnlyList<ExprValue>? items)
    {
        if (items is null) return false;
        if (items is ExprValue[] array)
        {
            foreach (ref readonly var item in array.AsSpan()) if (item.ContainsSymbols) return true;
            return false;
        }
        for (var i = 0; i < items.Count; i++) if (items[i].ContainsSymbols) return true;
        return false;
    }

    public static ExprValue Number(long value) => new(0, value, null);

    public static ExprValue Number(BigInteger value)
        => value >= long.MinValue && value <= long.MaxValue
            ? new(0, (long)value, null)
            : new((ushort)ExprType.Number | BigFlag, 0, new BigNumber(value, BigInteger.One));

    public static ExprValue Rational(BigInteger num, BigInteger den)
    {
        if (den == 0) throw new DivideByZeroException("Denominator cannot be zero.");
        if (den.IsOne) return Number(num);
        var reduced = Math.Rational.Reduce(num, den);
        return reduced.Den.IsOne ? Number(reduced.Num)
            : new((ushort)ExprType.Number | BigFlag, 0, new BigNumber(reduced.Num, reduced.Den));
    }

    public static ExprValue Bool(bool value)
        => new((ushort)ExprType.Boolean, value ? 1 : 0, null);

    public static ExprValue String(string value) => Text((ushort)ExprType.String, value);

    public static ExprValue Symbol(string value) => Text((ushort)ExprType.Symbol | SymbolsFlag, value);

    // tonumber of a plain integer literal is a pure function of the text, so
    // the parse is done once, when the value is created.
    private static ExprValue Text(ushort tag, string value)
    {
        if (value is not { Length: >= 1 and <= 19 }) return new(tag, 0, value);
        var start = value[0] is '+' or '-' ? 1 : 0;
        var digits = value.Length - start;
        if (digits is < 1 or > 18) return new(tag, 0, value);
        long parsed = 0;
        for (var i = start; i < value.Length; i++)
        {
            var digit = (uint)(value[i] - '0');
            if (digit > 9) return new(tag, 0, value);
            parsed = parsed * 10 + digit;
        }
        return new((ushort)(tag | NumericTextFlag), value[0] == '-' ? -parsed : parsed, value);
    }

    public static ExprValue Array(IReadOnlyList<ExprValue> items)
        => new(HasSymbols(items) ? (ushort)((ushort)ExprType.Array | SymbolsFlag) : (ushort)ExprType.Array, 0, items);

    /// <summary>Wrap a freshly built array whose symbol content is already known.</summary>
    internal static ExprValue ArrayOwned(ExprValue[] items, bool containsSymbols)
        => new(containsSymbols ? (ushort)((ushort)ExprType.Array | SymbolsFlag) : (ushort)ExprType.Array, 0, items);

    public static ExprValue Record(IReadOnlyDictionary<string, ExprValue> fields)
    {
        var copy = new System.Collections.ObjectModel.ReadOnlyDictionary<string, ExprValue>(
            fields.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
        return new(copy.Values.Any(v => v.ContainsSymbols) ? (ushort)((ushort)ExprType.Record | SymbolsFlag) : (ushort)ExprType.Record, 0, copy);
    }

    public static ExprValue Null => new((ushort)ExprType.Null, 0, null);

    /// <summary>Convert the rational number to a single BigInteger (truncating division).</summary>
    public BigInteger AsInteger() =>
        _tag == 0 ? _bits
            : Kind == ExprType.Number ? NumberNumerator / NumberDenominator
            : throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "Numeric expression required.");

    /// <summary>Convert to double (for sampled/hybrid).</summary>
    public double AsDouble()
    {
        if (Kind != ExprType.Number) throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "Numeric expression required.");
        // Integers of magnitude ≤ 2^53 convert exactly; larger ones keep the
        // BigInteger conversion so results stay bit-identical.
        if (_tag == 0 && _bits is >= -(1L << 53) and <= 1L << 53) return _bits;
        var numerator = (double)NumberNumerator;
        if (NumberDenominator.IsOne) return numerator;
        var denominator = (double)NumberDenominator;
        return double.IsFinite(numerator) && double.IsFinite(denominator) ? numerator / denominator
            : new Math.Rational(NumberNumerator, NumberDenominator).ToDouble();
    }

    /// <summary>Arithmetic multiply of two number values.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Mul(ExprValue a, ExprValue b)
    {
        if ((a._tag | b._tag) == 0)
        {
            var high = System.Math.BigMul(a._bits, b._bits, out long low);
            if (high == low >> 63) return new(0, low, null);
        }
        return MulSlow(a, b);
    }

    private static ExprValue MulSlow(ExprValue a, ExprValue b)
    {
        if (a.NumberDenominator.IsOne && b.NumberDenominator.IsOne)
            return Number(a.NumberNumerator * b.NumberNumerator);
        var num = a.NumberNumerator * b.NumberNumerator;
        var den = a.NumberDenominator * b.NumberDenominator;
        return Rational(num, den);
    }

    /// <summary>
    /// Arithmetic divide. Partial (D1): division by zero produces a deterministic,
    /// located evaluation error on both paths — never a silent value.
    /// </summary>
    public static ExprValue Div(ExprValue a, ExprValue b)
    {
        if ((a._tag | b._tag) == 0 && b._bits > 0 && a._bits % b._bits == 0)
            return new(0, a._bits / b._bits, null);
        if (b.NumberNumerator == 0)
            throw new ExpressionEvaluationException(
                EvalErrorCodes.DivisionByZero, "Division by zero in a level-(b) expression (D1).", "/");

        var num = a.NumberNumerator * b.NumberDenominator;
        var den = a.NumberDenominator * b.NumberNumerator;
        return Rational(num, den);
    }

    /// <summary>Arithmetic add.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Add(ExprValue a, ExprValue b)
    {
        if ((a._tag | b._tag) == 0)
        {
            var sum = unchecked(a._bits + b._bits);
            if (((a._bits ^ sum) & (b._bits ^ sum)) >= 0) return new(0, sum, null);
        }
        return AddSlow(a, b);
    }

    private static ExprValue AddSlow(ExprValue a, ExprValue b)
    {
        if (a.NumberDenominator.IsOne && b.NumberDenominator.IsOne)
            return Number(a.NumberNumerator + b.NumberNumerator);
        var num = a.NumberNumerator * b.NumberDenominator + b.NumberNumerator * a.NumberDenominator;
        var den = a.NumberDenominator * b.NumberDenominator;
        return Rational(num, den);
    }

    /// <summary>Arithmetic subtract.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Sub(ExprValue a, ExprValue b)
    {
        if ((a._tag | b._tag) == 0)
        {
            var difference = unchecked(a._bits - b._bits);
            if (((a._bits ^ b._bits) & (a._bits ^ difference)) >= 0) return new(0, difference, null);
        }
        return SubSlow(a, b);
    }

    private static ExprValue SubSlow(ExprValue a, ExprValue b)
    {
        if (a.NumberDenominator.IsOne && b.NumberDenominator.IsOne)
            return Number(a.NumberNumerator - b.NumberNumerator);
        var num = a.NumberNumerator * b.NumberDenominator - b.NumberNumerator * a.NumberDenominator;
        var den = a.NumberDenominator * b.NumberDenominator;
        return Rational(num, den);
    }

    /// <summary>Order two number values exactly.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CompareNumbers(ExprValue a, ExprValue b)
        => (a._tag | b._tag) == 0 ? a._bits.CompareTo(b._bits) : CompareNumbersSlow(a, b);

    private static int CompareNumbersSlow(ExprValue a, ExprValue b) => a.NumberDenominator.IsOne && b.NumberDenominator.IsOne
        ? a.NumberNumerator.CompareTo(b.NumberNumerator)
        : (a.NumberNumerator * b.NumberDenominator).CompareTo(b.NumberNumerator * a.NumberDenominator);

    /// <summary>
    /// Convert to a plain CLR object suitable for storage in the state dictionary.
    /// Integers → BigInteger; rationals → ExprValue (preserving exactness);
    /// booleans → bool; strings → string; arrays → object[].
    /// </summary>
    public object? ToStateObject() => Kind switch
    {
        ExprType.Number => NumberDenominator == 1 ? (object?)NumberNumerator : this,
        ExprType.Boolean => BoolValue,
        ExprType.String or ExprType.Symbol => StringValue,
        ExprType.Array => ArrayValue?.Select(v => v.ToStateObject()).ToArray(),
        ExprType.Record => RecordValue!.ToDictionary(p => p.Key, p => p.Value.ToStateObject(), StringComparer.Ordinal),
        ExprType.Null => null,
        _ => throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "Unsupported expression value cannot be stored."),
    };

    public bool Equals(ExprValue other)
    {
        if (((_tag ^ other._tag) & KindMask) != 0) return false;
        switch (Kind)
        {
            case ExprType.Number:
                if (((_tag | other._tag) & BigFlag) == 0) return _bits == other._bits;
                // Inline integers are canonical: a boxed number never equals one.
                return ((_tag & other._tag) & BigFlag) != 0
                    && NumberNumerator == other.NumberNumerator && NumberDenominator == other.NumberDenominator;
            case ExprType.Boolean: return _bits == other._bits;
            case ExprType.String or ExprType.Symbol: return string.Equals(Unsafe.As<string>(_ref), Unsafe.As<string>(other._ref));
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
            foreach (var item in items)
                h = HashCode.Combine(h, item.GetHashCode());
        if (RecordValue is { } fields)
            foreach (var item in fields.OrderBy(p => p.Key, StringComparer.Ordinal))
                h = HashCode.Combine(h, item.Key, item.Value.GetHashCode());
        return h;
    }

    public override string ToString() => Kind switch
    {
        ExprType.Number => $"{NumberNumerator}/{NumberDenominator}",
        ExprType.Boolean => BoolValue.ToString(),
        ExprType.String => $"\"{StringValue}\"",
        ExprType.Symbol => $"symbol:{StringValue}",
        ExprType.Array => $"[{string.Join(", ", ArrayValue?.Select(x => x.ToString()) ?? [])}]",
        ExprType.Record => $"{{{string.Join(", ", RecordValue!.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}: {p.Value}"))}}}",
        ExprType.Null => "null",
        _ => "?"
    };

    private static bool ArrayEquality(IReadOnlyList<ExprValue>? a, IReadOnlyList<ExprValue>? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (!a[i].Equals(b[i])) return false;
        return true;
    }

    private static bool RecordEquality(IReadOnlyDictionary<string, ExprValue>? a, IReadOnlyDictionary<string, ExprValue>? b) =>
        a is null ? b is null : b is not null && a.Count == b.Count
            && a.All(p => b.TryGetValue(p.Key, out var value) && p.Value.Equals(value));
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
    /// <summary>Homogeneous scalar element type, when it can be established from the authored graph.</summary>
    public ExprType? ArrayItemType { get; init; }
    public FieldDescriptor? ArrayItem { get; init; }
    public IReadOnlyList<FieldDescriptor> RecordFields { get; init; } = [];
    /// <summary>Proven empty array identity, used only during element inference.</summary>
    public bool ArrayIsEmpty { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Context for type-checking an expression.  Carries the schemas
/// of the board and game state so the type-checker can resolve
/// field accesses.
/// </summary>
public sealed class TypeCheckContext
{
    public IReadOnlyList<FieldDescriptor> MeasurementFields { get; init; } = [];
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
        => ResolveField(path, target)?.Type;

    internal FieldDescriptor? ResolveField(string[] path, string? target)
    {
        if (path.Length == 0) return null;

        IReadOnlyList<FieldDescriptor> fields;
        if (target == "state")
            fields = StateFields;
        else if (target == "measurement") fields = MeasurementFields;
        else if (target == "board" || target == null)
            fields = BoardFields;
        else
            return null;

        var first = path[0];
        var comparison = target is "state" or "measurement" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var fd = fields.LastOrDefault(f => string.Equals(f.Name, first, comparison));
        if (fd == null && path.Length == 1 && target == "board")
        {
            // Try cell-level fields (used as shorthand inside aggregations)
            fd = CellFields.FirstOrDefault(f =>
                string.Equals(f.Name, first, StringComparison.OrdinalIgnoreCase));
        }

        for (var i = 1; i < path.Length && fd is not null; i++)
        {
            if (fd.Type == ExprType.Record)
                fd = fd.RecordFields.SingleOrDefault(f => f.Name == path[i]);
            else if (fd.Type == ExprType.Array && BigInteger.TryParse(path[i],
                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index) && index >= 0)
                fd = fd.ArrayItem ?? (fd.ArrayItemType is { } type ? new() { Name = "item", Type = type } : null);
            else return null;
        }
        return fd;
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
