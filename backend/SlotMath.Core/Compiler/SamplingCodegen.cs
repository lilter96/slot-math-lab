using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;
using SlotMath.Core.Random;
using Expression = SlotMath.Core.Model.Expression;
using Lx = System.Linq.Expressions.Expression;
using LxParameter = System.Linq.Expressions.ParameterExpression;

namespace SlotMath.Core.Compiler;

/// <summary>
///     How a sampling expression is executed. Every tier has the same
///     observable semantics; the tiers differ only in start-up and steady-state cost.
/// </summary>
public enum SamplingTier
{
    /// <summary>Start interpreted; generate IL once the expression is hot.</summary>
    Tiered,

    /// <summary>Closure tree only. No code generation.</summary>
    Interpreted,

    /// <summary>Generate IL when the expression is bound.</summary>
    Compiled
}

/// <summary>
///     Process-wide default for newly compiled sampling plans. A host may
///     select <see cref="SamplingTier.Interpreted" /> to rule code generation out
///     while diagnosing a result; sampled statistics do not depend on the tier.
/// </summary>
public static class SamplingOptions
{
    public static SamplingTier Tier { get; set; } = SamplingTier.Tiered;

    /// <summary>Evaluations of one expression before IL is generated for it.</summary>
    public static int HotThreshold { get; set; } = 64;
}

/// <summary>
///     Everything in which two expressions of the same shape differ:
///     the fields they read, their constants, and the closures of the forms that
///     stay interpreted. Generated code takes these as an argument, so one method
///     serves every expression of that shape.
/// </summary>
internal sealed class SamplingOperands(
    int[] slots,
    string[] texts,
    string[][] paths,
    long[] numbers,
    ExprValue[] values,
    ExprValue[][] arrays,
    SamplingDraw[] draws,
    FiniteTable[] precomputations,
    ReachabilityData.Prepared[] reachabilities,
    SamplingIntegerTable[] integerTables,
    Func<SamplingFrame, ExprValue>[] closures,
    SamplingExpressions owner)
{
    public readonly ExprValue[][] Arrays = arrays;
    public readonly Func<SamplingFrame, ExprValue>[] Closures = closures;
    public readonly SamplingDraw[] Draws = draws;
    public readonly SamplingIntegerTable[] IntegerTables = integerTables;
    public readonly long[] Numbers = numbers;
    public readonly SamplingExpressions Owner = owner;
    public readonly string[][] Paths = paths;
    public readonly FiniteTable[] Precomputations = precomputations;
    public readonly ReachabilityData.Prepared[] Reachabilities = reachabilities;
    public readonly int[] Slots = slots;
    public readonly string[] Texts = texts;
    public readonly ExprValue[] Values = values;
    internal int PrecomputedExpressions { get; init; }
    internal int ReusedScalarEvaluations { get; init; }
    internal int InvariantScalarValues { get; init; }
    internal int EliminatedStateStores { get; init; }
    internal int ReusedCollectionBuffers { get; init; }
    internal int FusedCollectionPasses { get; init; }
    internal int PromotedMaskFields { get; init; }
}

// Bound data for an ordinary scalar Draw. Sampling preserves its two RNG
// operations and publishes the same typed outcome and boxed value as the
// interpreted plan. Keeping this body shared avoids copying the PRNG into
// every instruction of a generated straight run.
internal sealed class SamplingDraw(AliasMethod? alias, ExprValue[] names, object[] values)
{
    private readonly SamplingCell[] _outcomes = names.Select(SamplingCell.Typed).ToArray();
    internal IReadOnlyList<ExprValue> StateValues => names;
    internal SamplingDraw WithAlias(AliasMethod prepared) => new(prepared, names, values);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal object Pick(SeededRandom rng, out ExprValue data)
    {
        var index = alias!.Sample(rng);
        data = names[index];
        return values[index];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public object Run(SamplingFrame frame, SeededRandom rng, int destination)
    {
        var index = alias!.Sample(rng);
        if (destination >= 0)
        {
            frame.Cells[destination] = _outcomes[index];
        }

        return values[index];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal object RunWeighted(SamplingFrame frame, SeededRandom rng, int destination, AliasMethod selected)
    {
        var index = selected.Sample(rng);
        if (destination >= 0)
        {
            frame.Cells[destination] = _outcomes[index];
        }

        return values[index];
    }
}

internal sealed class SamplingRunDelegate
{
    private readonly Func<(Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?>
        Code, SamplingOperands Operands)> _generate;

    private readonly int _threshold = System.Math.Max(1, SamplingOptions.HotThreshold);
    private int _calls;

    private Binding _current;

    public SamplingRunDelegate(Func<SamplingFrame, SeededRandom, object?, object?> interpreted,
        Func<(Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code,
            SamplingOperands Operands)> generate, SamplingTier tier)
    {
        _generate = generate;
        var cold = new Binding((frame, _, rng, input) => interpreted(frame, rng, input), null);
        _current = cold;
        if (tier == SamplingTier.Compiled)
        {
            Generate();
        }
        else if (tier == SamplingTier.Tiered)
        {
            _current = new Binding((frame, _, rng, input) =>
            {
                if (Interlocked.Increment(ref _calls) == _threshold)
                {
                    try
                    {
                        Generate();
                    }
                    catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException
                                                   or ArgumentException or PlatformNotSupportedException)
                    {
                        Volatile.Write(ref _current, cold);
                    }
                }

                return interpreted(frame, rng, input);
            }, null);
        }
    }

    private void Generate()
    {
        (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> code, SamplingOperands operands) =
            _generate();
        Volatile.Write(ref _current, new Binding(code, operands));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? Invoke(SamplingFrame frame, SeededRandom rng, object? input)
    {
        Binding current = _current;
        return current.Code(frame, current.Operands, rng, input);
    }

    private sealed class Binding(
        Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> code,
        SamplingOperands? operands)
    {
        public readonly Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?>
            Code = code;

        public readonly SamplingOperands? Operands = operands;
    }
}

/// <summary>
///     Generated methods by expression shape, shared by a compiled program
///     and its replicas. A slot game repeats a few shapes many times (one per
///     payline, reel or cell); sharing keeps the hot code small enough to stay in
///     the instruction cache and is generated once.
/// </summary>
internal sealed class SamplingShapes
{
    private readonly ConcurrentDictionary<string, Lazy<Delegate>> _code =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, Lazy<SamplingDraw>> _draws = new(StringComparer.Ordinal);
    internal ExactWeightVectorCache WeightVectors { get; } = new();
    internal FinitePrecomputationCache Precomputations { get; } = new();

    public int Count => _code.Count;
    internal int BoundDrawCount => _draws.Count;

    internal SamplingDraw BindDraw(DrawWeight[] outcomes, BigInteger scale, bool dynamicWeights = false)
    {
        // Intern bound immutable operands as well as generated code. The complete
        // ordered payload distinguishes equal probabilities with different values.
        var key = JsonSerializer.Serialize(new
        {
            Scale = scale.ToString(CultureInfo.InvariantCulture),
            DynamicWeights = dynamicWeights,
            Outcomes = outcomes
        }, JsonOptions.Default);
        return _draws.GetOrAdd(key, _ => new Lazy<SamplingDraw>(() => new SamplingDraw(
            dynamicWeights
                ? null
                : WeightSet.FromIntegers(outcomes.Select(outcome => outcome.Weight).ToArray()).AliasTable,
            outcomes.Select(DrawData.StateValue).ToArray(),
            outcomes.Select(outcome => (object)(new BigInteger(outcome.Value) * scale)).ToArray()))).Value;
    }

    public T Get<T>(string shape, Func<T> compile) where T : Delegate =>
        (T)_code.GetOrAdd(shape, _ => new Lazy<Delegate>(compile)).Value;
}

/// <summary>
///     One bound expression. Its implementation is replaced by generated
///     code once the expression is hot; <see cref="Entry" /> is a stable delegate for
///     callers that keep one.
/// </summary>
internal sealed class SamplingDelegate<T>
{
    public readonly Func<SamplingFrame, T> Entry;
    private readonly Func<(Func<SamplingFrame, SamplingOperands?, T> Code, SamplingOperands Operands)>? _generate;
    private readonly Func<SamplingFrame, T> _interpreted;
    private readonly int _threshold = System.Math.Max(1, SamplingOptions.HotThreshold);
    private int _calls;

    private Binding _current;

    public SamplingDelegate(Func<SamplingFrame, T> interpreted,
        Func<(Func<SamplingFrame, SamplingOperands?, T> Code, SamplingOperands Operands)>? generate, SamplingTier tier)
    {
        _interpreted = interpreted;
        if (generate is null || tier == SamplingTier.Interpreted)
        {
            _current = new Binding((frame, _) => interpreted(frame), null);
            Entry = interpreted;
            return;
        }

        Entry = Invoke;
        if (tier == SamplingTier.Compiled)
        {
            (Func<SamplingFrame, SamplingOperands?, T> code, SamplingOperands operands) = generate();
            _current = new Binding(code, operands);
        }
        else
        {
            _generate = generate;
            _current = new Binding((frame, _) => Cold(frame), null);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Invoke(SamplingFrame frame)
    {
        Binding current = _current;
        return current.Code(frame, current.Operands);
    }

    private T Cold(SamplingFrame frame)
    {
        if (Interlocked.Increment(ref _calls) == _threshold)
        {
            // Generation is an optimization: if it is unavailable, the
            // interpreted closure remains the implementation.
            try
            {
                (Func<SamplingFrame, SamplingOperands?, T> code, SamplingOperands operands) = _generate!();
                _current = new Binding(code, operands);
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException
                                           or PlatformNotSupportedException)
            {
                _current = new Binding((next, _) => _interpreted(next), null);
            }
        }

        return _interpreted(frame);
    }

    // Code and the operands it expects are published together.
    private sealed class Binding(Func<SamplingFrame, SamplingOperands?, T> code, SamplingOperands? operands)
    {
        public readonly Func<SamplingFrame, SamplingOperands?, T> Code = code;
        public readonly SamplingOperands? Operands = operands;
    }
}

/// <summary>
///     Operators emitted by <see cref="SamplingCodegen" />. Each has an
///     inlined path for 64-bit integers and plain strings, and otherwise defers to
///     the shared exact implementation, so results and located errors are identical
///     to the interpreted tier.
/// </summary>
internal static class SamplingOps
{
    public static ExprValue DefaultAggregateValue(ExprValue value) => value.Kind == ExprType.String
        ? ExprValue.Number(BigInteger.TryParse(value.StringValue!, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out BigInteger number)
            ? number
            : 0)
        : value.Kind == ExprType.Number
            ? value
            : ExprValue.Number(0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Truth(ExprValue value) => value.IsBoolean ? value.BoolValue : NotBoolean();

    private static bool NotBoolean() =>
        throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "Boolean expression required.");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Add(ExprValue l, ExprValue r)
    {
        if (!(l.IsInlineInteger && r.IsInlineInteger))
        {
            RequireNumbers(BinaryOp.Add, l, r);
        }

        return ExprValue.Add(l, r);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Sub(ExprValue l, ExprValue r)
    {
        if (!(l.IsInlineInteger && r.IsInlineInteger))
        {
            RequireNumbers(BinaryOp.Sub, l, r);
        }

        return ExprValue.Sub(l, r);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Mul(ExprValue l, ExprValue r)
    {
        if (!(l.IsInlineInteger && r.IsInlineInteger))
        {
            RequireNumbers(BinaryOp.Mul, l, r);
        }

        return ExprValue.Mul(l, r);
    }

    public static ExprValue Div(ExprValue l, ExprValue r)
    {
        if (!(l.IsInlineInteger && r.IsInlineInteger))
        {
            RequireNumbers(BinaryOp.Div, l, r);
        }

        return ExprValue.Div(l, r);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue FloorDivide(ExprValue l, ExprValue r)
    {
        if (l.IsInlineInteger && r.IsInlineInteger)
        {
            long numerator = l.InlineInteger, denominator = r.InlineInteger;
            if (denominator != 0 && !(numerator == long.MinValue && denominator == -1))
            {
                var quotient = numerator / denominator;
                if ((numerator ^ denominator) < 0 && numerator % denominator != 0)
                {
                    quotient--;
                }

                return ExprValue.Number(quotient);
            }
        }

        // Keep exact wide/rational arithmetic and the original located errors.
        return NumericFunctions.Call("floor", Div(l, r), default, 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long FloorDivideInteger(long numerator, long denominator)
    {
        var quotient = numerator / denominator;
        return (numerator ^ denominator) < 0 && numerator % denominator != 0 ? quotient - 1 : quotient;
    }

    private static void RequireNumbers(BinaryOp op, ExprValue l, ExprValue r)
    {
        if (l.Kind != ExprType.Number || r.Kind != ExprType.Number)
        {
            throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", $"Operator {op} requires numeric operands.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Eq(ExprValue l, ExprValue r) =>
        l.IsInlineInteger && r.IsInlineInteger ? l.InlineInteger == r.InlineInteger
        : l.IsString && r.IsString ? string.Equals(l.TextUnchecked, r.TextUnchecked)
        : SamplingExpressions.CompareValues(CompareOp.Eq, l, r);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Neq(ExprValue l, ExprValue r) =>
        l.IsInlineInteger && r.IsInlineInteger ? l.InlineInteger != r.InlineInteger
        : l.IsString && r.IsString ? !string.Equals(l.TextUnchecked, r.TextUnchecked)
        : SamplingExpressions.CompareValues(CompareOp.Neq, l, r);

    // The other operand is a known String constant: no value is materialized
    // unless the comparison leaves the plain-string path.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool EqText(ExprValue l, string text) =>
        l.IsString
            ? string.Equals(l.TextUnchecked, text)
            : SamplingExpressions.CompareValues(CompareOp.Eq, l, ExprValue.String(text));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool NeqText(ExprValue l, string text) =>
        l.IsString
            ? !string.Equals(l.TextUnchecked, text)
            : SamplingExpressions.CompareValues(CompareOp.Neq, l, ExprValue.String(text));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Lt(ExprValue l, ExprValue r) =>
        l.IsInlineInteger && r.IsInlineInteger
            ? l.InlineInteger < r.InlineInteger
            : SamplingExpressions.CompareValues(CompareOp.Lt, l, r);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Gt(ExprValue l, ExprValue r) =>
        l.IsInlineInteger && r.IsInlineInteger
            ? l.InlineInteger > r.InlineInteger
            : SamplingExpressions.CompareValues(CompareOp.Gt, l, r);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Lte(ExprValue l, ExprValue r) =>
        l.IsInlineInteger && r.IsInlineInteger
            ? l.InlineInteger <= r.InlineInteger
            : SamplingExpressions.CompareValues(CompareOp.Lte, l, r);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Gte(ExprValue l, ExprValue r) =>
        l.IsInlineInteger && r.IsInlineInteger
            ? l.InlineInteger >= r.InlineInteger
            : SamplingExpressions.CompareValues(CompareOp.Gte, l, r);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Index(ExprValue array, ExprValue position)
    {
        if (position.IsInlineInteger && array.ItemsArray is { } items &&
            (ulong)position.InlineInteger < (ulong)items.Length)
        {
            return items[position.InlineInteger];
        }

        return CollectionFunctions.Index(array, position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue IndexItems(ExprValue[] items, ExprValue position)
    {
        if (position.IsInlineInteger && (ulong)position.InlineInteger < (ulong)items.Length)
        {
            return items[position.InlineInteger];
        }

        return CollectionFunctions.Index(ExprValue.Array(items), position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue IndexInteger(ExprValue array, long position)
    {
        return array.ItemsArray is { } items && (ulong)position < (ulong)items.Length
            ? items[position]
            : CollectionFunctions.Index(array, ExprValue.Number(position));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue IndexItemsInteger(ExprValue[] items, long position)
    {
        return (ulong)position < (ulong)items.Length
            ? items[position]
            : CollectionFunctions.Index(ExprValue.Array(items), ExprValue.Number(position));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Max(ExprValue l, ExprValue r) =>
        l.IsInlineInteger && r.IsInlineInteger
            ? l.InlineInteger >= r.InlineInteger ? l : r
            : NumericFunctions.Call("max", l, r, 2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Min(ExprValue l, ExprValue r) =>
        l.IsInlineInteger && r.IsInlineInteger
            ? l.InlineInteger <= r.InlineInteger ? l : r
            : NumericFunctions.Call("min", l, r, 2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasNumericText(ExprValue value) => value.TryGetNumericText(out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue NumericText(ExprValue value)
    {
        value.TryGetNumericText(out var number);
        return ExprValue.Number(number);
    }

    /// <summary>
    ///     First step of a fused append chain: checks the base exactly as a
    ///     single append would, then reserves room for every appended item.
    /// </summary>
    public static ExprValue[] AppendBegin(SamplingFrame frame, ExprValue first, int extra)
    {
        if (first.Kind != ExprType.Array)
        {
            throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "append requires an array.", "append");
        }

        if (first.ItemsArray is { } items)
        {
            ExprValue[] copy = frame.NewArray(items.Length + extra);
            items.CopyTo(copy, 0);
            return copy;
        }

        IReadOnlyList<ExprValue>? list = first.ArrayValue!;
        ExprValue[] result = frame.NewArray(list.Count + extra);
        for (var i = 0; i < list.Count; i++)
        {
            result[i] = list[i];
        }

        return result;
    }

    public static ExprValue AppendEnd(ExprValue[] items)
    {
        var symbols = false;
        foreach (ref readonly ExprValue item in items.AsSpan())
        {
            if (item.ContainsSymbols)
            {
                symbols = true;
                break;
            }
        }

        return ExprValue.ArrayOwned(items, symbols);
    }
}

/// <summary>
///     Translates one expression AST into a LINQ expression tree. Scalar
///     operators become straight-line IL with statically typed conditions; forms
///     for map, filter, fold and count become loops with scoped bindings. Other iteration
///     forms and uncommon paths reuse interpreted closures with compiled bodies.
///     <para>
///         While translating, the generator writes down the expression's shape:
///         every decision that selects what is emitted. Slots, constants and closures
///         are not part of it; they become <see cref="SamplingOperands" />. Two
///         expressions with equal shapes therefore produce the same method.
///     </para>
/// </summary>
internal sealed class SamplingCodegen
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                                     BindingFlags.Instance;

    private static readonly MethodInfo
        NumberOf = typeof(ExprValue).GetMethod(nameof(ExprValue.Number), [typeof(long)])!;

    private static readonly MethodInfo BoolOf = typeof(ExprValue).GetMethod(nameof(ExprValue.Bool), [typeof(bool)])!;
    private static readonly MethodInfo ReadField = typeof(SamplingFrame).GetMethod(nameof(SamplingFrame.Read), Any)!;
    private static readonly MethodInfo TruthOp = Op(nameof(SamplingOps.Truth));
    private static readonly MethodInfo HasNumericText = Op(nameof(SamplingOps.HasNumericText));
    private static readonly MethodInfo NumericText = Op(nameof(SamplingOps.NumericText));
    private static readonly MethodInfo AppendBegin = Op(nameof(SamplingOps.AppendBegin));
    private static readonly MethodInfo AppendEnd = Op(nameof(SamplingOps.AppendEnd));

    private static readonly MethodInfo ToNumberSlow =
        typeof(SamplingExpressions).GetMethod(nameof(SamplingExpressions.ToNumber), Any)!;

    private static readonly MethodInfo CallGeneric =
        typeof(SamplingExpressions).GetMethod(nameof(SamplingExpressions.Call), Any)!;

    private readonly List<ExprValue[]> _arrays = [];
    private readonly Dictionary<int, LxParameter> _availableScalars = [];

    private readonly Dictionary<string, IterationBinding> _bindings = new(StringComparer.Ordinal);
    private readonly List<Func<SamplingFrame, ExprValue>> _closures = [];
    private readonly Dictionary<int, (LxParameter Value, LxParameter Present)> _deferredStores = [];
    private readonly List<SamplingDraw> _draws = [];
    private readonly LxParameter _frame = Lx.Parameter(typeof(SamplingFrame), "frame");
    private readonly List<SamplingIntegerTable> _integerTables = [];
    private readonly Dictionary<string, (LxParameter Value, int Id)> _lexical = new(StringComparer.Ordinal);
    private readonly bool _literalOperands;
    private readonly List<LxParameter> _locals = [];

    private readonly Dictionary<int, (LxParameter Value, LxParameter Dirty, bool Inline, bool Initial)>
        _maskLocals = [];

    private readonly HashSet<int> _normalizedWrites = [];
    private readonly List<long> _numbers = [];
    private readonly LxParameter _operands = Lx.Parameter(typeof(SamplingOperands), "operands");

    private readonly SamplingExpressions _owner;
    private readonly List<string[]> _paths = [];
    private readonly List<FiniteTable> _precomputations = [];
    private readonly List<ReachabilityData.Prepared> _reachabilities = [];
    private readonly HashSet<int> _readableWrites = [];
    private readonly Dictionary<string, ScalarRecordAccumulator> _scalarAccumulators = new(StringComparer.Ordinal);
    private readonly StringBuilder _shape = new();
    private readonly Func<string, int> _slot;
    private readonly List<int> _slots = [];
    private readonly List<string> _texts = [];
    private readonly List<ExprValue> _values = [];
    private bool _blockedMaskLocals;
    private CollectionBufferAnalysis? _buffers;
    private int _eliminatedStateStores;
    private int _fusedCollectionPasses;
    private int _invariantScalarValues;
    private int _iteration;
    private int _lexicalFrameUses;
    private int _lexicalId;
    private int _precomputedExpressions;
    private int _reusedCollectionBuffers;
    private int _reusedScalarEvaluations;

    private SamplingSubexpressions? _subexpressions;

    private SamplingCodegen(SamplingExpressions owner, Func<string, int> slot, bool literalOperands = false)
    {
        _owner = owner;
        _slot = slot;
        _literalOperands = literalOperands;
    }

    private static MethodInfo Op(string name) => typeof(SamplingOps).GetMethod(name, Any) ??
                                                 throw new MissingMethodException(nameof(SamplingOps), name);

    private static MethodInfo Collection(string name) => typeof(CollectionFunctions).GetMethod(name, Any)!;

    public static (Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) CompileValue(
        SamplingExpressions owner, Func<string, int> slot, Expression expression)
    {
        var generator = new SamplingCodegen(owner, slot)
        {
            _buffers = owner.Buffers(expression)
        };
        return generator.Finish<ExprValue>("value:", generator.Value(expression), expression);
    }

    public static (Func<SamplingFrame, SamplingOperands?, bool> Code, SamplingOperands Operands) CompileCondition(
        SamplingExpressions owner, Func<string, int> slot, Expression expression)
    {
        var generator = new SamplingCodegen(owner, slot)
        {
            _buffers = owner.Buffers(expression)
        };
        return generator.Finish<bool>("condition:", generator.Condition(expression), expression);
    }

    public static (Func<SamplingFrame, SamplingOperands?, bool> Code, SamplingOperands Operands) CompileWrite(
        SamplingExpressions owner, Func<string, int> slot, Expression expression, int destination, bool integer,
        bool inlineMask = false)
    {
        var generator = new SamplingCodegen(owner, slot)
        {
            _buffers = owner.Buffers(expression)
        };
        return generator.Finish<bool>(integer ? "write-integer:" : "write:",
            Lx.Block(generator.Write(expression, destination, integer, inlineMask), Lx.Constant(true)), expression);
    }

    private Lx Write(Expression expression, int destination, bool integer, bool inlineMask = false,
        int? reuseGroup = null, bool deferStore = false)
    {
        if (_maskLocals.TryGetValue(destination,
                out (LxParameter Value, LxParameter Dirty, bool Inline, bool Initial) promoted))
        {
            if (!integer && KnownMask(expression))
            {
                _shape.Append("local-mask-write:").Append(destination).Append(';');
                return Lx.Block(Lx.Assign(promoted.Value, KnownMaskValue(expression)),
                    Lx.Assign(promoted.Dirty, Lx.Constant(true)), Lx.Empty());
            }

            _blockedMaskLocals = true;
        }

        inlineMask = inlineMask && !integer && KnownMask(expression);
        _shape.Append(inlineMask ? "write-inline-mask(" : integer ? "write-integer(" : "write(");
        LxParameter result = Local(typeof(ExprValue));
        Lx value;
        if (reuseGroup is { } existing && _availableScalars.TryGetValue(existing, out LxParameter? cached))
        {
            _shape.Append("available-scalar:").Append(existing).Append(';');
            _reusedScalarEvaluations++;
            value = cached;
        }
        else
        {
            value = inlineMask
                ? Lx.Call(NumberOf, Lx.Convert(KnownMaskValue(expression), typeof(long)))
                : Value(expression);
            if (reuseGroup is { } group)
            {
                _shape.Append("capture-scalar:").Append(group).Append(';');
                LxParameter captured = Local(typeof(ExprValue));
                _availableScalars.Add(group, captured);
                value = Lx.Assign(captured, value);
            }
        }

        BinaryExpression evaluate = Lx.Assign(result, value);
        Lx stored = result;
        if (integer)
        {
            stored = Lx.Call(typeof(ExprValue).GetMethod(nameof(ExprValue.Number), [typeof(BigInteger)])!,
                Lx.Call(result, typeof(ExprValue).GetMethod(nameof(ExprValue.AsInteger))!));
        }

        Lx write;
        if (deferStore)
        {
            if (!_deferredStores.TryGetValue(destination, out (LxParameter Value, LxParameter Present) pending))
            {
                pending = (Local(typeof(ExprValue)), Local(typeof(bool)));
                _deferredStores.Add(destination, pending);
            }

            _shape.Append("deferred-write:").Append(destination).Append(';');
            write = Lx.Block(Lx.Assign(pending.Value, stored), Lx.Assign(pending.Present, Lx.Constant(true)),
                Lx.Empty());
        }
        else
        {
            IndexExpression cell = Lx.ArrayAccess(Lx.Field(_frame, nameof(SamplingFrame.Cells)),
                Operand(nameof(SamplingOperands.Slots), _slots, destination));
            write = Lx.Call(cell, typeof(SamplingCell).GetMethod(nameof(SamplingCell.SetTyped))!, stored);
        }

        if (integer || ReadableResult(expression))
        {
            _readableWrites.Add(destination);
        }
        else
        {
            _readableWrites.Remove(destination);
        }

        _normalizedWrites.Add(destination);
        _shape.Append(')');
        return Lx.Block(evaluate, write);
    }

    public static (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code,
        SamplingOperands Operands) CompileRun(
            SamplingExpressions owner, Func<string, int> slot, IReadOnlyList<Node> nodes,
            IReadOnlyDictionary<string, Expression> expressions, BigInteger scale, bool discardOutput = false,
            Func<DrawNode, SamplingDraw>? bindDraw = null, bool promoteMasks = true)
    {
        var generator = new SamplingCodegen(owner, slot, true);
        if (promoteMasks)
        {
            foreach (var (field, initial) in SamplingMaskLocals.Find(nodes, owner.Ir, owner.IsMaskField))
            {
                var destination = slot(field);
                var inline = owner.IsInlineMaskField(field);
                generator._maskLocals.Add(destination,
                    (generator.Local(typeof(ulong)), generator.Local(typeof(bool)), inline, initial));
                generator._shape.Append("promote-mask:").Append(destination).Append(':')
                    .Append(inline).Append(':').Append(initial).Append(';');
            }
        }

        var reuse = ScalarReusePlan.Build(nodes, expressions, owner.Ir,
            admit: node => node.OutputKey is null || !owner.IsInlineMaskField(node.OutputKey));
        var deferred = DeferredStorePlan.Build(nodes, expressions, owner.Ir);
        generator._eliminatedStateStores = deferred.EliminatedStores;
        ParameterExpression rng = Lx.Parameter(typeof(SeededRandom), "rng");
        ParameterExpression input = Lx.Parameter(typeof(object), "input");
        var body = new List<Lx>();
        foreach (Node node in nodes)
        {
            body.Add(Lx.Call(generator._frame,
                typeof(SamplingFrame).GetMethod(nameof(SamplingFrame.CheckCancellation))!));
            switch (node)
            {
                case ModifyStateNode modify:
                    if (modify.ExpressionId is null ||
                        !expressions.TryGetValue(modify.ExpressionId, out Expression? expression))
                    {
                        generator._shape.Append("noop;");
                        break;
                    }

                    var destination = slot(modify.OutputKey ?? "__modified__");
                    if (owner.Reduce(expression) is FieldAccessExpr
                        {
                            Target: null or "state" or "board", Path.Length: 1
                        } field && field.Path[0] == modify.OutputKey)
                    {
                        generator._shape.Append("touch;");
                        body.Add(generator._maskLocals.TryGetValue(destination,
                            out (LxParameter Value, LxParameter Dirty, bool Inline, bool Initial) promoted)
                            ? Lx.Block(Lx.Assign(promoted.Dirty, Lx.Constant(true)), Lx.Empty())
                            : Lx.Call(generator._frame,
                                typeof(SamplingFrame).GetMethod(nameof(SamplingFrame.Touch))!,
                                generator.Operand(nameof(SamplingOperands.Slots), generator._slots, destination),
                                generator.Text(field.Path[0])));
                        generator._readableWrites.Add(destination);
                        generator._normalizedWrites.Add(destination);
                    }
                    else
                    {
                        generator._buffers = owner.Buffers(expression);
                        body.Add(generator.Write(expression, destination, modify.OutputKey is null,
                            modify.OutputKey is { } outputKey && owner.IsInlineMaskField(outputKey),
                            reuse.Groups.TryGetValue(modify.Id, out var group) ? group : null,
                            deferred.Fields.Contains(modify.OutputKey ?? "__modified__")));
                    }

                    break;
                case DrawNode draw:
                    generator._shape.Append(draw.StateWriteKey is null ? "draw-value;" : "draw-state;");
                    DrawWeight[] weights = draw.DrawWeights!;
                    SamplingDraw data = bindDraw?.Invoke(draw) ?? owner.Shapes.BindDraw(weights, scale);
                    body.Add(Lx.Assign(input,
                        Lx.Call(generator.Operand(nameof(SamplingOperands.Draws), generator._draws, data),
                            typeof(SamplingDraw).GetMethod(nameof(SamplingDraw.Run))!, generator._frame, rng,
                            draw.StateWriteKey is null
                                ? Lx.Constant(-1)
                                : generator.Operand(nameof(SamplingOperands.Slots), generator._slots,
                                    slot(draw.StateWriteKey)))));
                    if (draw.StateWriteKey is { } key)
                    {
                        generator._readableWrites.Add(slot(key));
                        generator._normalizedWrites.Add(slot(key));
                    }

                    break;
                default: throw new NotSupportedException($"Cannot emit {node.GetType().Name} in a straight run.");
            }
        }

        // Opaque evaluators and finite-table probes observe frame state directly.
        // Retain ordinary stores for such runs rather than publish speculatively.
        if (generator._maskLocals.Count != 0 && (generator._blockedMaskLocals ||
                                                 generator._closures.Count != 0 ||
                                                 generator._precomputations.Count != 0))
        {
            return CompileRun(owner, slot, nodes, expressions, scale, discardOutput, bindDraw, false);
        }

        body.Add(discardOutput ? Lx.Constant(null, typeof(object)) : input);
        Lx execute = Lx.Block(body);
        if (generator._deferredStores.Count != 0)
        {
            var initialize = new List<Lx>();
            var publish = new List<Lx>();
            foreach ((var destination, (LxParameter Value, LxParameter Present) pending) in generator._deferredStores)
            {
                initialize.Add(Lx.Assign(pending.Present, Lx.Constant(false)));
                IndexExpression cell = Lx.ArrayAccess(Lx.Field(generator._frame, nameof(SamplingFrame.Cells)),
                    generator.Operand(nameof(SamplingOperands.Slots), generator._slots, destination));
                publish.Add(Lx.IfThen(pending.Present,
                    Lx.Call(cell, typeof(SamplingCell).GetMethod(nameof(SamplingCell.SetTyped))!, pending.Value)));
            }

            initialize.Add(Lx.TryFinally(execute, Lx.Block(publish)));
            execute = Lx.Block(initialize);
        }

        if (generator._maskLocals.Count != 0)
        {
            var initialize = new List<Lx>();
            var publish = new List<Lx>();
            foreach ((var destination, (LxParameter Value, LxParameter Dirty, bool Inline, bool Initial) promoted) in
                     generator._maskLocals)
            {
                IndexExpression cell = Lx.ArrayAccess(Lx.Field(generator._frame, nameof(SamplingFrame.Cells)),
                    generator.Operand(nameof(SamplingOperands.Slots), generator._slots, destination));
                initialize.Add(Lx.Assign(promoted.Dirty, Lx.Constant(false)));
                if (promoted.Initial)
                {
                    initialize.Add(Lx.Assign(promoted.Value, Lx.Property(cell, nameof(SamplingCell.StoredMask))));
                }

                publish.Add(Lx.IfThen(promoted.Dirty, Lx.Call(cell,
                    typeof(SamplingCell).GetMethod(nameof(SamplingCell.SetTyped))!,
                    generator.MaskLocalValue(promoted.Value, promoted.Inline))));
            }

            initialize.Add(Lx.TryFinally(execute, Lx.Block(publish)));
            execute = Lx.Block(initialize);
        }

        var cacheKey = (discardOutput ? "run-null:" : "run:") + generator._shape;
        var methodName = SamplingNativeProfile.Register("run", cacheKey, nodes);
        Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> code = owner.Shapes.Get(
            cacheKey, () => Lx
                .Lambda<Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?>>(
                    Lx.Block(generator._locals, execute), methodName, false,
                    [generator._frame, generator._operands, rng, input]).Compile());
        return (code, generator.Operands());
    }

    private (Func<SamplingFrame, SamplingOperands?, T> Code, SamplingOperands Operands) Finish<T>(string kind, Lx body,
        Expression source)
    {
        var key = kind + _shape;
        var methodName = SamplingNativeProfile.Register(kind, key, expression: source);
        Func<SamplingFrame, SamplingOperands?, T> code = _owner.Shapes.Get(key,
            () => Lx.Lambda<Func<SamplingFrame, SamplingOperands?, T>>(Lx.Block(_locals, body), methodName, false,
                    [_frame, _operands])
                .Compile());
        return (code, Operands());
    }

    private SamplingOperands Operands() => new([.. _slots], [.. _texts], [.. _paths], [.. _numbers], [.. _values],
        [.. _arrays],
        [.. _draws], [.. _precomputations], [.. _reachabilities], [.. _integerTables], [.. _closures], _owner)
    {
        PrecomputedExpressions = _precomputedExpressions,
        ReusedScalarEvaluations = _reusedScalarEvaluations,
        InvariantScalarValues = _invariantScalarValues,
        EliminatedStateStores = _eliminatedStateStores,
        ReusedCollectionBuffers = _reusedCollectionBuffers,
        FusedCollectionPasses = _fusedCollectionPasses,
        PromotedMaskFields = _maskLocals.Count
    };

    private Lx MaskLocalValue(Lx value, bool inline) => inline
        ? Lx.Call(NumberOf, Lx.Convert(value, typeof(long)))
        : Lx.Call(SetOp(nameof(BitSetOperations.Value)), value);

    private LxParameter Local(Type type)
    {
        ParameterExpression local = Lx.Variable(type);
        _locals.Add(local);
        return local;
    }

    // An operand: element `list.Count` of the named array, in emission order.
    private Lx Operand<T>(string array, List<T> list, T value)
    {
        // A whole straight run is shared between worker replicas with the
        // same bound literals/layout. Embedding them lets the JIT fold numeric
        // tags and address cells directly. Individual expression methods still
        // share code across differently bound fields and constants.
        if (_literalOperands && value is int or long or string)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture)!;
            _shape.Append(array).Append(':').Append(text.Length).Append(':').Append(text).Append(';');
            return Lx.Constant(value, typeof(T));
        }

        list.Add(value);
        return Lx.ArrayIndex(Lx.Field(_operands, array), Lx.Constant(list.Count - 1));
    }

    private Lx Text(string value) => Operand(nameof(SamplingOperands.Texts), _texts, value);

    private Lx Interpreted(Expression expression)
    {
        _lexicalFrameUses++;
        foreach (IterationBinding binding in _bindings.Values)
        {
            binding.NeedsFrame = true;
        }

        _shape.Append("closure;");
        return Lx.Invoke(Operand(nameof(SamplingOperands.Closures), _closures, _owner.Interpret(expression, Compiled)),
            _frame);
    }

    private Func<SamplingFrame, ExprValue> Compiled(Expression expression)
    {
        (Func<SamplingFrame, SamplingOperands?, ExprValue> code, SamplingOperands operands) =
            CompileValue(_owner, _slot, expression);
        return frame => code(frame, operands);
    }

    private static bool TryConstant(Expression expression, out ExprValue value)
    {
        value = default;
        if (expression is not ConstantExpr constant)
        {
            return false;
        }

        try
        {
            value = ExactExpressionEvaluator.Evaluate(constant, EvalContext.Empty);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or DivideByZeroException or ExpressionEvaluationException)
        {
            return false;
        }
    }

    private Lx Literal(ExprValue constant)
    {
        if (constant.IsInlineInteger)
        {
            if (constant.InlineInteger == 0)
            {
                _shape.Append("zero-integer;");
                return Lx.Default(typeof(ExprValue));
            }

            _shape.Append("integer;");
            return Lx.Call(NumberOf, Operand(nameof(SamplingOperands.Numbers), _numbers, constant.InlineInteger));
        }

        if (constant.IsBoolean)
        {
            return Lx.Call(BoolOf, Truth(constant.BoolValue));
        }

        _shape.Append("constant;");
        return Operand(nameof(SamplingOperands.Values), _values, constant);
    }

    private Lx Truth(bool value)
    {
        _shape.Append(value ? "true;" : "false;");
        return Lx.Constant(value);
    }

    // Writes `name(` before and `)` after the operands, so nesting is part of the shape.
    private Lx Form(string name, Func<Lx> emit)
    {
        _shape.Append(name).Append('(');
        Lx result = emit();
        _shape.Append(')');
        return result;
    }

    private Lx Value(Expression expression)
    {
        expression = _owner.Reduce(expression);
        return _subexpressions?.Emit(expression, () => ValueCore(expression), text => _shape.Append(text))
               ?? ValueCore(expression);
    }

    private Lx ValueCore(Expression expression)
    {
        if (expression.Precompute is not null && _owner.Precomputation(expression).Table is { } table)
        {
            return Precomputed(expression, table);
        }

        expression = _owner.Reduce(expression);
        if (TryBoundValue(expression, out ExprValue unchanging))
        {
            return Literal(unchanging);
        }

        if (TryScalarProjection(expression, out Lx? scalarProjection))
        {
            return scalarProjection;
        }

        switch (expression)
        {
            case LetExpr let:
                return Form("let", () =>
                {
                    Lx initializer = Value(let.Value);
                    LxParameter value = Local(typeof(ExprValue));
                    LxParameter previousFrame = Local(typeof(LexicalBinding));
                    var hadPrevious = _lexical.TryGetValue(let.Name, out (LxParameter Value, int Id) previous);
                    _lexical[let.Name] = (value, ++_lexicalId);
                    Lx body;
                    var beforeFrameUses = _lexicalFrameUses;
                    try
                    {
                        body = Value(let.Body);
                    }
                    finally
                    {
                        if (hadPrevious)
                        {
                            _lexical[let.Name] = previous;
                        }
                        else
                        {
                            _lexical.Remove(let.Name);
                        }
                    }

                    if (beforeFrameUses == _lexicalFrameUses)
                    {
                        _shape.Append("lexical-local;");
                        return Lx.Block(Lx.Assign(value, initializer), body);
                    }

                    _shape.Append("lexical-frame;");

                    // Fallback closures compile separately and read locals through the frame.
                    // The IL local still gives native children direct access; finally restores
                    // the outer lexical scope even when a child fails.
                    Lx bindings = Lx.Field(_frame, nameof(SamplingFrame.Locals));
                    return Lx.Block(
                        Lx.Assign(value, initializer),
                        Lx.Assign(previousFrame, bindings),
                        Lx.Assign(bindings,
                            Lx.New(typeof(LexicalBinding).GetConstructors()[0], Text(let.Name), value, previousFrame)),
                        Lx.TryFinally(body, Lx.Block(Lx.Assign(bindings, previousFrame), Lx.Empty())));
                });
            case LocalExpr local:
                if (_lexical.TryGetValue(local.Name, out (LxParameter Value, int Id) lexical))
                {
                    _shape.Append("local:").Append(lexical.Id).Append(';');
                    return ProjectScalar(lexical.Value, local.Path, "local." + local.Name);
                }

                _lexicalFrameUses++;
                _shape.Append("local-frame;");
                return Lx.Call(typeof(LexicalValues).GetMethod(nameof(LexicalValues.Read), Any)!,
                    Lx.Field(_frame, nameof(SamplingFrame.Locals)), Text(local.Name),
                    Operand(nameof(SamplingOperands.Paths), _paths, local.Path));
            case ProjectExpr project:
                return Form("project", () =>
                    Lx.Call(typeof(LexicalValues).GetMethod(nameof(LexicalValues.Project), Any)!,
                        Value(project.Value), Operand(nameof(SamplingOperands.Paths), _paths, project.Path),
                        Text("project")));
            case ReachableExpr reachable:
                return Form("reachable:" + reachable.VertexCount, () =>
                {
                    if (reachable.Adjacency is FieldAccessExpr { Path.Length: 1 } field &&
                        !_bindings.ContainsKey(field.Path[0]) && _owner.Reachability(reachable) is { } prepared)
                    {
                        _shape.Append("prepared;");
                        return Lx.Call(Operand(nameof(SamplingOperands.Reachabilities), _reachabilities, prepared),
                            typeof(ReachabilityData.Prepared).GetMethod(nameof(ReachabilityData.Prepared.Evaluate),
                                Any)!,
                            Value(reachable.Allowed), Value(reachable.Seeds),
                            Lx.Field(_frame, nameof(SamplingFrame.CancellationToken)));
                    }

                    _shape.Append("checked;");
                    return Lx.Call(typeof(ReachabilityData).GetMethod(nameof(ReachabilityData.Evaluate), Any)!,
                        Value(reachable.Adjacency), Value(reachable.Allowed), Value(reachable.Seeds),
                        Lx.Constant(reachable.VertexCount), Lx.Field(_frame, nameof(SamplingFrame.CancellationToken)));
                });
            case ScatterExpr scatter:
                return Form("scatter:" + scatter.Mode + ":" + scatter.MaxItems + ":" + scatter.MaxUpdates, () =>
                {
                    var reuse = _buffers?.CanReuseSource(scatter) == true;
                    _shape.Append(reuse ? "transfer-scatter;" : "copy-scatter;");
                    if (reuse)
                    {
                        _reusedCollectionBuffers++;
                    }

                    return Lx.Call(_frame, typeof(SamplingFrame).GetMethod(nameof(SamplingFrame.Scatter), Any)!,
                        Value(scatter.Target), Value(scatter.Indices), Value(scatter.Values), Lx.Constant(scatter.Mode),
                        Lx.Constant(scatter.MaxItems), Lx.Constant(scatter.MaxUpdates), Lx.Constant(reuse));
                });
            case PartitionIndicesExpr partition:
                return Form("partition:" + partition.BucketCount + ":" + partition.MaxItems, () =>
                    Lx.Call(_frame, typeof(SamplingFrame).GetMethod(nameof(SamplingFrame.Partition), Any)!,
                        Value(partition.Labels), Lx.Constant(partition.BucketCount), Lx.Constant(partition.MaxItems)));
            case ScanExpr scan:
                return Form("scan:" + scan.Mode + ":" + scan.MaxItems, () =>
                {
                    var reuse = _buffers?.CanReuseSource(scan) == true;
                    _shape.Append(reuse ? "transfer-scan;" : "copy-scan;");
                    if (reuse)
                    {
                        _reusedCollectionBuffers++;
                    }

                    return Lx.Call(_frame, typeof(SamplingFrame).GetMethod(nameof(SamplingFrame.Scan), Any)!,
                        Value(scan.Source), Lx.Constant(scan.Mode), Lx.Constant(scan.MaxItems), Lx.Constant(reuse));
                });
            case SegmentedCompactExpr compact:
                return Form("compact:" + compact.Packing + ":" + compact.MaxItems + ":" + compact.MaxSegments,
                    () => Lx.Call(_frame, typeof(SamplingFrame).GetMethod(nameof(SamplingFrame.Compact), Any)!,
                        Value(compact.Keep), Value(compact.Boundaries), Lx.Constant(compact.Packing),
                        Lx.Constant(compact.MaxItems), Lx.Constant(compact.MaxSegments)));
            case RecordExpr record:
                return RecordValue(record.Fields.Select(field => field.Name).ToArray(),
                    record.Fields.Select(field => field.Value).ToArray(), expression);
            case TupleExpr tuple:
                return RecordValue(LexicalValues.TupleNames(tuple.Items.Length), tuple.Items, expression);
            case ConstantExpr:
                return TryConstant(expression, out ExprValue constant) ? Literal(constant) : Interpreted(expression);
            case FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } field
                when !string.IsNullOrEmpty(field.Path[0]):
                if (_bindings.TryGetValue(field.Path[0], out IterationBinding? binding))
                {
                    _shape.Append(binding.Shape);
                    return binding.Read(field.Path[0]);
                }

                if (_maskLocals.TryGetValue(_slot(field.Path[0]),
                        out (LxParameter Value, LxParameter Dirty, bool Inline, bool Initial) promoted))
                {
                    _shape.Append("local-mask-value:").Append(_slot(field.Path[0])).Append(';');
                    return MaskLocalValue(promoted.Value, promoted.Inline);
                }

                _shape.Append("field;");
                if (_readableWrites.Contains(_slot(field.Path[0])))
                {
                    _shape.Append("readable;");
                    return Lx.Property(Lx.ArrayAccess(Lx.Field(_frame, nameof(SamplingFrame.Cells)),
                            Operand(nameof(SamplingOperands.Slots), _slots, _slot(field.Path[0]))),
                        typeof(SamplingCell).GetProperty(nameof(SamplingCell.StoredValue), Any)!);
                }

                return Lx.Call(_frame, ReadField, Operand(nameof(SamplingOperands.Slots), _slots, _slot(field.Path[0])),
                    Text(field.Path[0]));
            case AggregateExpr { Func: AggregateFunc.Count } count:
                return Iterate(CollectionInputs.Of(count), count.ItemName, null,
                    count.Predicate ?? new ConstantExpr
                    {
                        Kind = ConstantKind.Boolean,
                        Value = "true"
                    }, true,
                    counting: true, selectedValue: count.ValueExpr);
            case AggregateExpr
            {
                Func: AggregateFunc.Sum or AggregateFunc.Product or AggregateFunc.Min or AggregateFunc.Max
            } aggregate:
                return Iterate(CollectionInputs.Of(aggregate), aggregate.ItemName, null,
                    aggregate.Predicate ?? new ConstantExpr
                    {
                        Kind = ConstantKind.Boolean,
                        Value = "true"
                    }, true,
                    counting: true, selectedValue: aggregate.ValueExpr, aggregation: aggregate.Func);
            case FoldExpr fold:
                return Iterate(CollectionInputs.Of(fold), fold.ItemName, fold.IndexName, fold.Body, false, fold.Init,
                    fold.AccName, whileCondition: fold.WhileCondition);
            case MapExpr map:
                if (_buffers?.TryConstantProducer(map, out ExprValue repeated) == true &&
                    map.Source is MapExpr producer)
                {
                    _shape.Append("virtual-map-source;");
                    _fusedCollectionPasses++;
                    LxParameter producedLength = Local(typeof(int));
                    Lx preflight = Iterate(CollectionInputs.Of(producer), producer.ItemName, producer.IndexName,
                        producer.Body, false, discardValues: true, captureLength: producedLength);
                    Lx consumer = Iterate(CollectionInputs.Of(map), map.ItemName, map.IndexName, map.Body, false,
                        virtualItem: repeated, virtualLength: producedLength);
                    return Lx.Block(preflight, consumer);
                }

                return Iterate(CollectionInputs.Of(map), map.ItemName, map.IndexName, map.Body, false,
                    ownerExpression: map);
            case FilterExpr filter:
                return Iterate(CollectionInputs.Of(filter), filter.ItemName, filter.IndexName, filter.Predicate, true,
                    ownerExpression: filter);
            case IfExpr branch:
                return Form("if", () =>
                {
                    if (TryBoundBoolean(branch.Condition, out var selected))
                    {
                        _shape.Append(selected ? "true;" : "false;");
                        return Value(selected ? branch.ThenExpr : branch.ElseExpr);
                    }

                    Lx test = Condition(branch.Condition);
                    Lx yes = Value(branch.ThenExpr);
                    return Lx.Condition(test, yes, Value(branch.ElseExpr));
                });
            case NotExpr:
            case BinaryExpr { Op: BinaryOp.And or BinaryOp.Or }:
            case CompareExpr
            {
                Op: CompareOp.Eq or CompareOp.Neq or CompareOp.Lt or CompareOp.Gt or CompareOp.Lte or CompareOp.Gte
            }:
                return Form("bool", () => Lx.Call(BoolOf, Condition(expression)));
            case BinaryExpr { Op: BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul or BinaryOp.Div } binary:
                if (KnownInteger(binary))
                {
                    return Form("integer-arithmetic-result", () => Lx.Call(NumberOf, IntegerValueCore(binary)));
                }

                return Form(binary.Op.ToString(), () =>
                {
                    Lx left = Value(binary.Left);
                    return Lx.Call(Op(binary.Op.ToString()), left, Value(binary.Right));
                });
            case CallExpr call:
                return Call(call);
            case BitSetExpr set when BitSetOperations.Arity(set.Op) is > 0 and var arity && set.Args.Length == arity:
                return BitSetValue(set);
            default:
                return Interpreted(expression);
        }
    }

    private Lx Precomputed(Expression expression, FiniteTable table) => Form("precompute:" + table.Domains.Length, () =>
    {
        _precomputedExpressions++;
        Lx bound = Operand(nameof(SamplingOperands.Precomputations), _precomputations, table);
        LxParameter row = Local(typeof(int)), value = Local(typeof(ExprValue)), contribution = Local(typeof(int));
        Lx all = Lx.Constant(true);
        for (var index = 0; index < table.Domains.Length; index++)
        {
            FiniteDomain domain = table.Domains[index];
            Lx probe;
            if (domain.Scope == PrecomputationScope.Local &&
                _lexical.TryGetValue(domain.Name, out (LxParameter Value, int Id) local))
            {
                _shape.Append("probe-local:").Append(local.Id).Append(';');
                probe = Lx.Block(Lx.Assign(value, local.Value), Lx.Constant(true));
            }
            else if (domain.Scope == PrecomputationScope.Local)
            {
                _lexicalFrameUses++;
                _shape.Append("probe-local-frame;");
                probe = Lx.Call(typeof(LexicalValues).GetMethod(nameof(LexicalValues.TryRead), Any)!,
                    Lx.Field(_frame, nameof(SamplingFrame.Locals)), Text(domain.Name), value);
            }
            else if (_bindings.TryGetValue(domain.Name, out IterationBinding? binding))
            {
                _shape.Append("probe-iteration:").Append(binding.Shape);
                probe = binding.Probe is { } safe ? safe(value) :
                    binding.Constant is { } literal ? Lx.Block(Lx.Assign(value, Literal(literal)), Lx.Constant(true)) :
                    Lx.Constant(false);
            }
            else
            {
                _shape.Append("probe-state;");
                IndexExpression cell = Lx.ArrayAccess(Lx.Field(_frame, nameof(SamplingFrame.Cells)),
                    Operand(nameof(SamplingOperands.Slots), _slots, _slot(domain.Name)));
                probe = Lx.AndAlso(Lx.Field(cell, nameof(SamplingCell.Present)),
                    Lx.Call(cell, typeof(SamplingCell).GetMethod(nameof(SamplingCell.TryCachedValue), Any)!, value));
            }

            Lx found = Lx.AndAlso(probe,
                Lx.Call(bound, typeof(FiniteTable).GetMethod(nameof(FiniteTable.TryArgument), Any)!, Lx.Constant(index),
                    value, contribution));
            all = Lx.AndAlso(all, Lx.AndAlso(found, Lx.Block(Lx.AddAssign(row, contribution), Lx.Constant(true))));
        }

        Lx fallback = Value(expression with
        {
            Precompute = null
        });
        return Lx.Block(Lx.Assign(row, Lx.Constant(0)),
            Lx.Condition(all, Lx.Call(bound, typeof(FiniteTable).GetMethod(nameof(FiniteTable.Result), Any)!, row),
                fallback));
    });

    private Lx RecordValue(string[] names, Expression[] fields, Expression original)
    {
        if (fields.Length > LexicalValues.MaxFields)
        {
            return Interpreted(original);
        }

        return Form("record", () => ValidRecordNames(names)
            ? OwnedRecord(names, fields.Select(Value).ToArray())
            : Lx.Call(typeof(LexicalValues).GetMethod(nameof(LexicalValues.Record), Any)!,
                Operand(nameof(SamplingOperands.Paths), _paths, names),
                Lx.NewArrayInit(typeof(ExprValue), fields.Select(Value))));
    }

    private bool TryScalarProjection(Expression expression, out Lx value)
    {
        Expression? source = null;
        string[] path = [];
        var location = "project";
        if (expression is ProjectExpr project)
        {
            source = project.Value;
            path = project.Path;
        }
        else if (expression is FieldAccessExpr { Target: null or "state" or "board", Path.Length: >= 2 } field)
        {
            source = new FieldAccessExpr
            {
                Target = field.Target,
                Path = [field.Path[0]]
            };
            path = field.Path[1..];
            location = string.Join('.', field.Path);
        }

        if (source is FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } stateField &&
            _scalarAccumulators.TryGetValue(stateField.Path[0], out ScalarRecordAccumulator? accumulator) &&
            _bindings.TryGetValue(stateField.Path[0], out IterationBinding? activeBinding) &&
            ReferenceEquals(activeBinding, accumulator.Owner) &&
            path.Length > 0 && accumulator.Fields.TryGetValue(path[0], out LxParameter? fieldValue))
        {
            var fieldIndex = Array.IndexOf(accumulator.Names, path[0]);
            _shape.Append("scalar-acc-field:").Append(accumulator.Owner.Shape).Append(fieldIndex).Append(';');
            value = ProjectScalar(fieldValue, path[1..], location);
            return true;
        }

        value = null!;
        return false;
    }

    private Lx ProjectScalar(Lx value, string[] path, string location) => path.Length == 0
        ? Lx.Call(typeof(StateValues).GetMethod(nameof(StateValues.RequireReadable), Any)!, value, Text(location))
        : Lx.Call(typeof(LexicalValues).GetMethod(nameof(LexicalValues.Project), Any)!, value,
            Operand(nameof(SamplingOperands.Paths), _paths, path), Text(location));

    private static bool TryRecordParts(Expression expression, out string[] names, out Expression[] values)
    {
        switch (expression)
        {
            case RecordExpr record:
                names = record.Fields.Select(field => field.Name).ToArray();
                values = record.Fields.Select(field => field.Value).ToArray();
                return true;
            case TupleExpr tuple:
                names = LexicalValues.TupleNames(tuple.Items.Length);
                values = tuple.Items;
                return true;
            default:
                names = [];
                values = [];
                return false;
        }
    }

    private static bool TryRecordBody(Expression expression, out List<LetExpr> lets, out string[] names,
        out Expression[] values)
    {
        lets = [];
        while (expression is LetExpr let)
        {
            lets.Add(let);
            expression = let.Body;
        }

        return TryRecordParts(expression, out names, out values);
    }

    private static bool ValidRecordNames(string[] names) => names.Length <= LexicalValues.MaxFields &&
                                                            names.All(name => !string.IsNullOrWhiteSpace(name)) &&
                                                            names.Distinct(StringComparer.Ordinal).Count() ==
                                                            names.Length;

    private static bool ContainsWholeAccumulatorRead(Expression expression, string accumulatorName)
    {
        if (expression is FieldAccessExpr { Target: null or "state" or "board" } field &&
            field.Path.Length > 0 && field.Path[0] == accumulatorName)
        {
            return field.Path.Length == 1;
        }

        if (expression is ProjectExpr { Path.Length: > 0 } project &&
            project.Value is FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } source &&
            source.Path[0] == accumulatorName)
        {
            return false;
        }

        foreach (PropertyInfo property in expression.GetType().GetProperties(
                     BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.PropertyType == typeof(string) || property.PropertyType.IsPrimitive ||
                property.PropertyType.IsEnum)
            {
                continue;
            }

            var child = property.GetValue(expression);
            if (child is Expression childExpression && ContainsWholeAccumulatorRead(childExpression, accumulatorName))
            {
                return true;
            }

            if (child is IEnumerable children)
            {
                foreach (var item in children)
                {
                    if (item is Expression nested && ContainsWholeAccumulatorRead(nested, accumulatorName))
                    {
                        return true;
                    }

                    if (item is RecordFieldExpr recordField &&
                        ContainsWholeAccumulatorRead(recordField.Value, accumulatorName))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private Lx MaterializeRecord(string[] names, IReadOnlyList<LxParameter> values) =>
        OwnedRecord(names, values.Cast<Lx>().ToArray());

    private Lx OwnedRecord(string[] names, IReadOnlyList<Lx> values)
    {
        _shape.Append("owned-record:").Append(values.Count).Append(';');
        LxParameter record = Local(typeof(SamplingRecord));
        Lx fields = Lx.Field(record, nameof(SamplingRecord.Fields));
        var steps = new List<Lx>
        {
            Lx.Assign(record, Lx.Call(_frame, typeof(SamplingFrame).GetMethod(nameof(SamplingFrame.NewRecord), Any)!,
                Operand(nameof(SamplingOperands.Paths), _paths, names)))
        };
        for (var index = 0; index < values.Count; index++)
        {
            steps.Add(Lx.Assign(Lx.ArrayAccess(fields, Lx.Constant(index)), values[index]));
        }

        steps.Add(Lx.Call(record, typeof(SamplingRecord).GetMethod(nameof(SamplingRecord.Finish), Any)!));
        return Lx.Block(steps);
    }

    private Lx CompileScalarRecordBody(IReadOnlyList<LetExpr> lets, Expression[] fields,
        LxParameter[] stagedFields)
    {
        Lx Compile(int letIndex)
        {
            if (letIndex == lets.Count)
            {
                return Lx.Block(fields.Select((field, index) =>
                    (Lx)Lx.Assign(stagedFields[index], Value(field))));
            }

            LetExpr let = lets[letIndex];
            Lx initializer = Value(let.Value);
            LxParameter lexicalValue = Local(typeof(ExprValue));
            var hadPrevious = _lexical.TryGetValue(let.Name, out (LxParameter Value, int Id) previous);
            var frameUsesBeforeBody = _lexicalFrameUses;
            _lexical[let.Name] = (lexicalValue, ++_lexicalId);
            Lx body;
            try
            {
                body = Compile(letIndex + 1);
            }
            finally
            {
                if (hadPrevious)
                {
                    _lexical[let.Name] = previous;
                }
                else
                {
                    _lexical.Remove(let.Name);
                }
            }

            if (frameUsesBeforeBody == _lexicalFrameUses)
            {
                _shape.Append("scalar-lexical-local;");
                return Lx.Block(Lx.Assign(lexicalValue, initializer), body);
            }

            _shape.Append("scalar-lexical-frame;");
            LxParameter previousFrame = Local(typeof(LexicalBinding));
            Lx bindings = Lx.Field(_frame, nameof(SamplingFrame.Locals));
            return Lx.Block(
                Lx.Assign(lexicalValue, initializer),
                Lx.Assign(previousFrame, bindings),
                Lx.Assign(bindings,
                    Lx.New(typeof(LexicalBinding).GetConstructors()[0], Text(let.Name), lexicalValue, previousFrame)),
                Lx.TryFinally(body, Lx.Block(Lx.Assign(bindings, previousFrame), Lx.Empty())));
        }

        return Compile(0);
    }

    // Raw null bindings report null-value errors, whereas typed null state
    // can report an array-type error. Keep nullable sources on the raw path.
    private static bool HasNull(ExprValue value) => value.Kind == ExprType.Null
                                                    || value.ArrayValue?.Any(HasNull) == true
                                                    || value.RecordValue?.Values.Any(HasNull) == true;

    private static FieldAccessExpr StateField(string key) => new()
    {
        Target = "state",
        Path = [key]
    };

    // Existing iteration semantics emitted as one loop. Scoped aliases let
    // the generated body read its element/index without repeated frame lookups.
    // Frame bindings are still published for interpreted nested forms, and
    // restored even on errors or cancellation.
    private Lx Iterate(CollectionInput input, string itemName, string? indexName, Expression body, bool filter,
        Expression? init = null, string? accName = null, bool counting = false, Expression? selectedValue = null,
        Expression? ownerExpression = null, bool discardValues = false, LxParameter? captureLength = null,
        ExprValue? virtualItem = null, LxParameter? virtualLength = null, Expression? whileCondition = null,
        AggregateFunc? aggregation = null) =>
        Form(
            (aggregation?.ToString() ?? (counting ? "count" : init is not null ? "fold" : filter ? "filter" : "map")) +
            (indexName is null ? "-item" : "-indexed") + (whileCondition is null ? "" : "-while"), () =>
            {
                var key = input.Key;
                Expression sourceExpression = input.Source ?? StateField(key);
                var shadowedSource = sourceExpression is FieldAccessExpr { Path.Length: 1 } sourceField &&
                                     _bindings.ContainsKey(sourceField.Path[0]);
                if (!filter && !counting && !discardValues && virtualItem is null && init is null && !shadowedSource
                    && _owner.TryConstant(sourceExpression,
                        out ExprValue unchanging)
                    && unchanging.ItemsArray is { Length: <= 32 } entries && !unchanging.ContainsSymbols &&
                    !HasNull(unchanging) && (input.MaxItems is null ||
                                             (input.MaxItems is >= 0 and <= CollectionInputs.MaxItems &&
                                              entries.Length <= input.MaxItems)))
                {
                    return ConstantMap(entries, itemName, indexName, body);
                }

                // Range propagation through a Fold phi value: every successful body
                // path must produce a finite mask, assuming its accumulator is one.
                var maskFold = init is not null && KnownMask(init) && KnownMask(body, accName, itemName, indexName);
                string[] recordNames = [];
                Expression[] initialFields = [];
                List<LetExpr> recordLets = [];
                string[] bodyNames = [];
                Expression[] bodyFields = [];
                var scalarRecordFold = init is not null && accName is not null &&
                                       TryRecordParts(init, out recordNames, out initialFields) &&
                                       TryRecordBody(body, out recordLets, out bodyNames,
                                           out bodyFields) &&
                                       recordNames.SequenceEqual(bodyNames, StringComparer.Ordinal) &&
                                       ValidRecordNames(recordNames) &&
                                       !ContainsWholeAccumulatorRead(body, accName!);
                if (!scalarRecordFold)
                {
                    recordNames = [];
                    initialFields = [];
                    recordLets = [];
                }

                _shape.Append(maskFold ? "mask-accumulator;" : "value-accumulator;");
                Lx? initial = init is null ? null :
                    maskFold ? KnownMaskValue(init) :
                    scalarRecordFold ? null : Value(init);
                LxParameter[]? scalarValues = scalarRecordFold
                    ? initialFields.Select(_ => Local(typeof(ExprValue))).ToArray()
                    : null;
                LxParameter[]? stagedScalarValues = scalarRecordFold
                    ? bodyFields.Select(_ => Local(typeof(ExprValue))).ToArray()
                    : null;
                Lx[]? initialScalarExpressions = scalarRecordFold
                    ? initialFields.Select(Value).ToArray()
                    : null;
                Lx? expressionSource = input.Source is null || virtualItem is not null ? null : Value(input.Source);
                LxParameter source = Local(typeof(SamplingCell));
                LxParameter typedItems = Local(typeof(ExprValue[]));
                LxParameter element = Local(typeof(ExprValue));
                LxParameter item = Local(typeof(SamplingCell));
                LxParameter savedItem = Local(typeof(SamplingCell));
                LxParameter? savedIndex = indexName is null ? null : Local(typeof(SamplingCell));
                LxParameter? accumulator = accName is null ? null : Local(maskFold ? typeof(ulong) : typeof(ExprValue));
                LxParameter? savedAcc = accName is null ? null : Local(typeof(SamplingCell));
                LxParameter length = Local(typeof(int));
                LxParameter cursor = Local(typeof(int));
                LxParameter count = Local(typeof(int));
                LxParameter values = Local(typeof(ExprValue[]));
                LxParameter result = Local(maskFold ? typeof(ulong) : typeof(ExprValue));
                LxParameter symbols = Local(typeof(bool));
                MemberExpression cells = Lx.Field(_frame, nameof(SamplingFrame.Cells));

                Lx Cell(int slot)
                {
                    return Lx.ArrayAccess(cells, Operand(nameof(SamplingOperands.Slots), _slots, slot));
                }

                if (virtualItem is null && input.Source is null &&
                    _bindings.TryGetValue(key, out IterationBinding? sourceBinding))
                {
                    sourceBinding.NeedsFrame = true;
                }

                Lx sourceCell;
                Lx? constantItems = null;
                var constantLength = -1;
                if (virtualItem is not null)
                {
                    _shape.Append("virtual-source;");
                    sourceCell = Lx.Default(typeof(SamplingCell));
                }
                else if (expressionSource is not null)
                {
                    _shape.Append("expression-source;");
                    sourceCell = Lx.Call(typeof(SamplingCell).GetMethod(nameof(SamplingCell.Typed), Any)!,
                        expressionSource);
                }
                else if (!_bindings.ContainsKey(key) && _owner.TryConstant(StateField(key),
                                                         out ExprValue constantSource)
                                                     && constantSource.ItemsArray is { } sourceItems &&
                                                     !constantSource.ContainsSymbols &&
                                                     !HasNull(constantSource))
                {
                    _shape.Append("constant-source:").Append(sourceItems.Length).Append(';');
                    constantLength = sourceItems.Length;
                    constantItems = Operand(nameof(SamplingOperands.Arrays), _arrays, sourceItems);
                    sourceCell = Lx.Default(typeof(SamplingCell));
                }
                else
                {
                    _shape.Append("state-source;");
                    sourceCell = Cell(_slot(key));
                }

                Lx? accCell = accName is null ? null : Cell(_owner.Variable(accName));
                Lx itemCell = Cell(_owner.Variable(itemName));
                Lx? indexCell = indexName is null ? null : Cell(_owner.Variable(indexName));
                MethodInfo readable = typeof(StateValues).GetMethod(nameof(StateValues.RequireReadable), Any)!;
                MethodInfo readValue = typeof(SamplingCell).GetMethod(nameof(SamplingCell.ReadValue), Any)!;
                MethodInfo typed = typeof(SamplingCell).GetMethod(nameof(SamplingCell.Typed), Any)!;
                MethodInfo requireArray = typeof(SamplingCell).GetMethod(nameof(SamplingCell.RequireArrayCount), Any)!;
                MethodInfo getItem = typeof(SamplingCell).GetMethod(nameof(SamplingCell.Item), Any)!;
                MethodInfo checkCancellation =
                    typeof(SamplingFrame).GetMethod(nameof(SamplingFrame.CheckCancellation), Any)!;
                MethodInfo owned = typeof(ExprValue).GetMethod(nameof(ExprValue.ArrayOwned), Any)!;
                MethodInfo resize = typeof(SamplingFrame).GetMethod(nameof(SamplingFrame.ResizeArray), Any)!;
                IterationBinding? parentAcc =
                    accName is not null && _bindings.TryGetValue(accName, out IterationBinding? previousAcc)
                        ? previousAcc
                        : null;
                _scalarAccumulators.TryGetValue(accName ?? string.Empty, out ScalarRecordAccumulator? parentScalarAcc);
                _bindings.TryGetValue(itemName, out IterationBinding? parentItem);
                IterationBinding? parentIndex =
                    indexName is not null && _bindings.TryGetValue(indexName, out IterationBinding? found)
                        ? found
                        : null;
                var iteration = _iteration++;

                Lx AccumulatorValue()
                {
                    if (scalarRecordFold)
                    {
                        return MaterializeRecord(recordNames, scalarValues!);
                    }

                    return maskFold ? Lx.Call(SetOp(nameof(BitSetOperations.Value)), accumulator!) : accumulator!;
                }

                IterationBinding? accBinding = accName is null
                    ? null
                    : new IterationBinding($"acc:{iteration};", name => scalarRecordFold
                        ? Lx.Call(readable, AccumulatorValue(), Text(name))
                        : maskFold
                            ? AccumulatorValue()
                            : Lx.Call(readable, accumulator!, Text(name)), maskFold ? accumulator : null)
                    {
                        Probe = destination => Lx.Block(Lx.Assign(destination, AccumulatorValue()), Lx.Constant(true))
                    };
                Lx hasTypedItems = constantItems is not null || virtualItem is not null
                    ? Lx.Constant(true)
                    : Lx.NotEqual(typedItems, Lx.Constant(null, typeof(ExprValue[])));

                Lx ItemValue()
                {
                    return Lx.Condition(hasTypedItems, element,
                        Lx.Property(item, nameof(SamplingCell.FilterValue)));
                }

                var itemBinding = new IterationBinding($"item:{iteration};", name =>
                    constantItems is not null || virtualItem is not null
                        ? element
                        : Lx.Call(readable,
                            Lx.Condition(hasTypedItems, element, Lx.Call(item, readValue, Text(name))), Text(name)))
                {
                    Probe = destination => Lx.Condition(hasTypedItems,
                        Lx.Block(Lx.Assign(destination, element), Lx.Constant(true)),
                        Lx.Call(item, typeof(SamplingCell).GetMethod(nameof(SamplingCell.TryCachedValue), Any)!,
                            destination))
                };
                IterationBinding? indexBinding = indexName is null
                    ? null
                    : new IterationBinding($"index:{iteration};",
                        _ => Lx.Call(NumberOf, Lx.Convert(cursor, typeof(long))))
                    {
                        Probe = destination =>
                            Lx.Block(Lx.Assign(destination, Lx.Call(NumberOf, Lx.Convert(cursor, typeof(long)))),
                                Lx.Constant(true)),
                        Integer = Lx.Convert(cursor, typeof(long)),
                        ShiftDistance = constantLength is >= 0 and <= 64 ? cursor : null
                    };
                if (accName is not null)
                {
                    _bindings[accName] = accBinding!;
                    if (scalarRecordFold)
                    {
                        _scalarAccumulators[accName] = new ScalarRecordAccumulator(recordNames, accBinding!,
                            recordNames.Select((name, fieldIndex) => (name, Value: scalarValues![fieldIndex]))
                                .ToDictionary(pair => pair.name, pair => pair.Value, StringComparer.Ordinal));
                    }
                }

                _bindings[itemName] = itemBinding;
                if (indexName is not null)
                {
                    _bindings[indexName] = indexBinding!;
                }

                Lx? keepGoing = null;
                if (whileCondition is not null)
                {
                    SamplingSubexpressions? previous = _subexpressions;
                    _subexpressions = null;
                    var unavailableItem = new IterationBinding("unavailable-item;", name => Lx.Throw(
                        Lx.New(
                            typeof(ExpressionEvaluationException).GetConstructor([
                                typeof(string), typeof(string), typeof(string)
                            ])!,
                            Lx.Constant("EVAL_MISSING_STATE"),
                            Lx.Constant("The current item is unavailable before the guard."), Text(name)),
                        typeof(ExprValue)));
                    _bindings[itemName] = unavailableItem;
                    try
                    {
                        keepGoing = Condition(whileCondition);
                    }
                    finally
                    {
                        itemBinding.NeedsFrame |= unavailableItem.NeedsFrame;
                        _bindings[itemName] = itemBinding;
                        _subexpressions = previous;
                    }
                }

                Lx selector;
                Lx? selected = null;
                SamplingSubexpressions? parentSubexpressions = _subexpressions;
                var selectorSubexpressions = SamplingSubexpressions.Create(body, Local,
                    new[] { itemName, indexName, accName }.OfType<string>());
                _subexpressions = selectorSubexpressions;
                try
                {
                    if (scalarRecordFold)
                    {
                        _shape.Append("scalar-record-fold;");
                        selector = CompileScalarRecordBody(recordLets, bodyFields, stagedScalarValues!);
                    }
                    else
                    {
                        selector = filter ? Condition(body) : maskFold ? KnownMaskValue(body) : Value(body);
                    }

                    if (counting)
                    {
                        // Count still evaluates its selector (or validates the raw
                        // item). Parsing an unused numeric string cannot affect it.
                        if (selectedValue is null)
                        {
                            _shape.Append("default-selection;");
                            selected = aggregation is null
                                ? ItemValue()
                                : Lx.Call(Op(nameof(SamplingOps.DefaultAggregateValue)), ItemValue());
                        }
                        else
                        {
                            selected = Value(selectedValue);
                        }
                    }
                }
                finally
                {
                    _subexpressions = parentSubexpressions;
                    if (accName is not null)
                    {
                        if (parentScalarAcc is null)
                        {
                            _scalarAccumulators.Remove(accName);
                        }
                        else
                        {
                            _scalarAccumulators[accName] = parentScalarAcc;
                        }

                        if (parentAcc is null)
                        {
                            _bindings.Remove(accName);
                        }
                        else
                        {
                            _bindings[accName] = parentAcc;
                        }
                    }

                    if (parentItem is null)
                    {
                        _bindings.Remove(itemName);
                    }
                    else
                    {
                        _bindings[itemName] = parentItem;
                    }

                    if (indexName is not null)
                    {
                        if (parentIndex is null)
                        {
                            _bindings.Remove(indexName);
                        }
                        else
                        {
                            _bindings[indexName] = parentIndex;
                        }
                    }
                }

                if (whileCondition is not null && !maskFold)
                {
                    if (!scalarRecordFold)
                    {
                        selector = Lx.Call(
                            typeof(CollectionInputs).GetMethod(nameof(CollectionInputs.CheckAccumulator), Any)!,
                            selector, Lx.Property(result, nameof(ExprValue.Kind)));
                    }
                }

                var publish = itemBinding.NeedsFrame || accBinding?.NeedsFrame == true ||
                              indexBinding?.NeedsFrame == true;
                _shape.Append(publish ? "published;" : "local;");
                BlockExpression take = discardValues ? Lx.Block(selector, Lx.Empty()) :
                    aggregation is { } operation ? Lx.Block(
                        Lx.Assign(result,
                            Lx.Call(typeof(NumericFunctions).GetMethod(nameof(NumericFunctions.Aggregate), Any)!,
                                Lx.Constant(operation), result, selected!, Lx.Equal(count, Lx.Constant(0)))),
                        Lx.PostIncrementAssign(count), Lx.Empty()) :
                    counting ? Lx.Block(selected!, Lx.PostIncrementAssign(count), Lx.Empty()) :
                    scalarRecordFold ? Lx.Block(selector, Lx.Empty()) :
                    init is not null ? Lx.Block(Lx.Assign(result, selector), Lx.Empty()) : Lx.Block(
                        Lx.Assign(result, filter ? ItemValue() : selector),
                        Lx.Assign(Lx.ArrayAccess(values, count), result),
                        Lx.Assign(symbols, Lx.Or(symbols, Lx.Property(result, nameof(ExprValue.ContainsSymbols)))),
                        Lx.PostIncrementAssign(count), Lx.Empty());
                Lx readItem = virtualItem is { } virtualValue
                    ? Lx.Block(Lx.Assign(element, Literal(virtualValue)),
                        publish ? Lx.Block(Lx.Assign(item, Lx.Call(typed, element)), Lx.Empty()) : Lx.Empty())
                    : Lx.IfThenElse(hasTypedItems,
                        publish
                            ? Lx.Block(Lx.Assign(element, Lx.ArrayIndex(typedItems, cursor)),
                                Lx.Assign(item, Lx.Call(typed, element)), Lx.Empty())
                            : Lx.Block(Lx.Assign(element, Lx.ArrayIndex(typedItems, cursor)), Lx.Empty()),
                        Lx.Block(Lx.Assign(item, Lx.Call(source, getItem, cursor)), Lx.Empty()));
                if (whileCondition is not null && expressionSource is not null)
                {
                    readItem = Lx.Block(readItem, Lx.IfThen(hasTypedItems,
                        Lx.Assign(element, Lx.Property(Lx.Call(typed, element), nameof(SamplingCell.Value)))));
                }

                LabelTarget end = Lx.Label();
                var steps = new List<Lx>
                {
                    Lx.Call(_frame, checkCancellation)
                };
                if (whileCondition is null)
                {
                    steps.Add(readItem);
                }
                else
                {
                    steps.Add(Lx.Call(Lx.Field(_frame, nameof(SamplingFrame.CancellationToken)),
                        typeof(CancellationToken).GetMethod(nameof(CancellationToken.ThrowIfCancellationRequested))!));
                }

                if (accCell is not null)
                {
                    if (scalarRecordFold)
                    {
                        for (var fieldIndex = 0; fieldIndex < scalarValues!.Length; fieldIndex++)
                        {
                            steps.Add(Lx.Assign(scalarValues[fieldIndex],
                                Lx.Call(typeof(SamplingCell).GetMethod(nameof(SamplingCell.NormalizeValue), Any)!,
                                    stagedScalarValues![fieldIndex])));
                        }
                    }
                    else
                    {
                        steps.Add(Lx.Assign(accumulator!, maskFold
                            ? result
                            : Lx.Condition(Lx.Property(result, nameof(ExprValue.ContainsSymbols)),
                                Lx.Property(Lx.Call(typed, result), nameof(SamplingCell.Value)), result)));
                    }

                    if (publish)
                    {
                        steps.Add(Lx.Assign(accCell, Lx.Call(typed, AccumulatorValue())));
                    }
                }

                if (publish && whileCondition is null)
                {
                    steps.Add(Lx.Assign(itemCell, item));
                }

                if (publish && indexCell is not null)
                {
                    steps.Add(Lx.Assign(indexCell,
                        Lx.Call(typed, Lx.Call(NumberOf, Lx.Convert(cursor, typeof(long))))));
                }

                if (keepGoing is not null)
                {
                    if (publish)
                    {
                        steps.Add(Lx.Assign(itemCell, Lx.Default(typeof(SamplingCell))));
                    }

                    steps.Add(Lx.IfThen(Lx.Not(keepGoing), Lx.Break(end)));
                    steps.Add(readItem);
                    if (publish)
                    {
                        steps.Add(Lx.Assign(itemCell, item));
                    }
                }

                if (selectorSubexpressions is not null)
                {
                    steps.AddRange(selectorSubexpressions.Reset());
                }

                steps.Add(filter ? Lx.IfThen(selector, take) : take);
                steps.Add(Lx.PostIncrementAssign(cursor));
                steps.Add(Lx.Empty());
                LoopExpression loop =
                    Lx.Loop(Lx.IfThenElse(Lx.LessThan(cursor, length), Lx.Block(steps), Lx.Break(end)), end);
                var restore = new List<Lx>();
                if (accCell is not null)
                {
                    restore.Add(Lx.Assign(accCell, savedAcc!));
                }

                restore.Add(Lx.Assign(itemCell, savedItem));
                if (indexCell is not null)
                {
                    restore.Add(Lx.Assign(indexCell, savedIndex!));
                }

                restore.Add(Lx.Empty());
                var initialize = new List<Lx>();
                if (selectorSubexpressions is not null)
                {
                    initialize.AddRange(selectorSubexpressions.ResetInvariants());
                    _invariantScalarValues += selectorSubexpressions.InvariantCount;
                }

                if (initial is not null)
                {
                    initialize.Add(Lx.Assign(result, initial));
                }

                if (scalarRecordFold)
                {
                    for (var fieldIndex = 0; fieldIndex < initialScalarExpressions!.Length; fieldIndex++)
                    {
                        initialize.Add(Lx.Assign(stagedScalarValues![fieldIndex],
                            initialScalarExpressions[fieldIndex]));
                    }
                }

                if (virtualItem is not null)
                {
                    initialize.Add(Lx.Assign(length, Lx.Call(
                        typeof(CollectionInputs).GetMethod(nameof(CollectionInputs.CheckCount), Any)!, virtualLength!,
                        Lx.Convert(Operand(nameof(SamplingOperands.Numbers), _numbers, input.MaxItems ?? -1),
                            typeof(int)),
                        Text(input.Location))));
                }
                else if (constantItems is not null)
                {
                    initialize.Add(Lx.Assign(typedItems, constantItems));
                    initialize.Add(Lx.Assign(length, Lx.Constant(constantLength)));
                    if (input.MaxItems is not null || whileCondition is not null)
                    {
                        var bound = input.MaxItems ?? -1;
                        _shape.Append("bounded-constant;");
                        initialize.Add(Lx.Call(
                            typeof(CollectionInputs).GetMethod(nameof(CollectionInputs.CheckCount), Any)!, length,
                            Lx.Convert(Operand(nameof(SamplingOperands.Numbers), _numbers, bound), typeof(int)),
                            Text(input.Location)));
                    }
                }
                else if (expressionSource is not null)
                {
                    // An expression already returns a typed value. Keep it in that
                    // representation instead of returning a large state-cell pair.
                    _shape.Append("bounded-value-source;");
                    LxParameter valueSource = Local(typeof(ExprValue));
                    initialize.Add(Lx.Assign(valueSource, Lx.Call(
                        typeof(SamplingCollectionSource).GetMethod(whileCondition is null
                            ? nameof(SamplingCollectionSource.ReadValue)
                            : nameof(SamplingCollectionSource.ReadIndexedValue), Any)!,
                        expressionSource,
                        Lx.Convert(Operand(nameof(SamplingOperands.Numbers), _numbers, input.MaxItems ?? -1),
                            typeof(int)), Text(input.Location), length)));
                    initialize.Add(Lx.Assign(typedItems,
                        Lx.Property(valueSource, typeof(ExprValue).GetProperty(nameof(ExprValue.ItemsArray), Any)!)));
                    initialize.Add(Lx.IfThen(Lx.Equal(typedItems, Lx.Constant(null, typeof(ExprValue[]))),
                        Lx.Block(Lx.Assign(source, Lx.Call(whileCondition is null
                                ? typed
                                : typeof(SamplingCell).GetMethod(nameof(SamplingCell.ArraySource), Any)!, valueSource)),
                            Lx.Empty())));
                }
                else if (input.MaxItems is not null || whileCondition is not null)
                {
                    _shape.Append("bounded-source;");
                    LxParameter collection = Local(typeof(SamplingCollectionValue));
                    initialize.Add(Lx.Assign(collection, Lx.Call(
                        typeof(SamplingCollectionSource).GetMethod(whileCondition is null
                            ? nameof(SamplingCollectionSource.Read)
                            : nameof(SamplingCollectionSource.ReadIndexed), Any)!,
                        sourceCell,
                        Lx.Convert(Operand(nameof(SamplingOperands.Numbers), _numbers, input.MaxItems ?? -1),
                            typeof(int)), Text(key), Text(input.Location))));
                    initialize.Add(Lx.Assign(source, Lx.Property(collection, nameof(SamplingCollectionValue.Cell))));
                    initialize.Add(Lx.Assign(length, Lx.Property(collection, nameof(SamplingCollectionValue.Length))));
                    initialize.Add(Lx.Assign(typedItems, Lx.Condition(Lx.Field(source, nameof(SamplingCell.HasRaw)),
                        Lx.Constant(null, typeof(ExprValue[])), Lx.Property(
                            Lx.Property(source, nameof(SamplingCell.Value)),
                            typeof(ExprValue).GetProperty(nameof(ExprValue.ItemsArray), Any)!))));
                }
                else
                {
                    initialize.AddRange([
                        Lx.Assign(source, sourceCell),
                        Lx.Assign(length, Lx.Call(source, requireArray, Text(key), Lx.Constant(null, typeof(string)))),
                        // Raw sources retain lazy conversion and their distinct null/symbol semantics.
                        Lx.Assign(typedItems, Lx.Condition(Lx.Field(source, nameof(SamplingCell.HasRaw)),
                            Lx.Constant(null, typeof(ExprValue[])),
                            Lx.Property(Lx.Property(source, nameof(SamplingCell.Value)),
                                typeof(ExprValue).GetProperty(nameof(ExprValue.ItemsArray), Any)!)))
                    ]);
                }

                if (whileCondition is not null)
                {
                    initialize.Add(Lx.Call(Lx.Field(_frame, nameof(SamplingFrame.CancellationToken)),
                        typeof(CancellationToken).GetMethod(nameof(CancellationToken.ThrowIfCancellationRequested))!));
                }

                if (publish)
                {
                    if (accCell is not null)
                    {
                        initialize.Add(Lx.Assign(savedAcc!, accCell));
                    }

                    initialize.Add(Lx.Assign(savedItem, itemCell));
                    if (indexCell is not null)
                    {
                        initialize.Add(Lx.Assign(savedIndex!, indexCell));
                    }
                }

                Lx execute = publish ? Lx.TryFinally(loop, Lx.Block(restore)) : loop;
                if (captureLength is not null)
                {
                    initialize.Add(Lx.Assign(captureLength, length));
                }

                if (discardValues)
                {
                    _shape.Append("producer-preflight;");
                    initialize.Add(Lx.Call(typeof(SamplingFrame).GetMethod(
                        nameof(SamplingFrame.ValidateArrayAllocationLength), Any)!, length));
                    Lx maySkip = Lx.AndAlso(Lx.NotEqual(typedItems, Lx.Constant(null, typeof(ExprValue[]))),
                        Lx.AndAlso(Lx.Equal(Lx.ArrayLength(typedItems), length), Lx.Call(_frame,
                            typeof(SamplingFrame).GetMethod(nameof(SamplingFrame.TryAdvanceCancellationCheckpoints),
                                Any)!, length)));
                    initialize.Add(Lx.Assign(cursor, Lx.Constant(0)));
                    initialize.Add(Lx.IfThen(Lx.Not(maySkip), execute));
                    initialize.Add(Lx.Call(NumberOf, Lx.Constant(0L)));
                    return Lx.Block(initialize);
                }

                if (counting)
                {
                    if (aggregation is { } function)
                    {
                        initialize.AddRange([
                            Lx.Assign(cursor, Lx.Constant(0)), Lx.Assign(count, Lx.Constant(0)),
                            Lx.Assign(result, Literal(ExprValue.Number(function == AggregateFunc.Product ? 1 : 0))),
                            execute
                        ]);
                        if (function is AggregateFunc.Min or AggregateFunc.Max)
                        {
                            initialize.Add(Lx.IfThen(Lx.Equal(count, Lx.Constant(0)), Lx.Throw(Lx.New(
                                typeof(ExpressionEvaluationException).GetConstructor([
                                    typeof(string), typeof(string), typeof(string)
                                ])!,
                                Text(EvalErrorCodes.EmptyMinMax),
                                Text($"{function} over an empty array is undefined (D1)."),
                                Text(function.ToString().ToLowerInvariant())))));
                        }

                        initialize.Add(result);
                        return Lx.Block(initialize);
                    }

                    initialize.AddRange([
                        Lx.Assign(cursor, Lx.Constant(0)), Lx.Assign(count, Lx.Constant(0)), execute,
                        Lx.Call(NumberOf, Lx.Convert(count, typeof(long)))
                    ]);
                    return Lx.Block(initialize);
                }

                if (init is not null)
                {
                    initialize.AddRange([
                        Lx.Assign(cursor, Lx.Constant(0)), execute,
                        scalarRecordFold
                            ? MaterializeRecord(recordNames, stagedScalarValues!)
                            : maskFold
                                ? Lx.Call(SetOp(nameof(BitSetOperations.Value)), result)
                                : result
                    ]);
                    return Lx.Block(initialize);
                }

                var reuseBuffer = virtualItem is null && ownerExpression is not null &&
                                  _buffers?.CanReuseSource(ownerExpression) == true;
                Lx allocate = Lx.Call(_frame, typeof(SamplingFrame).GetMethod(nameof(SamplingFrame.NewArray), Any)!,
                    length);
                if (reuseBuffer)
                {
                    _shape.Append("reuse-private-buffer;");
                    _reusedCollectionBuffers++;
                    allocate = Lx.Condition(Lx.NotEqual(typedItems, Lx.Constant(null, typeof(ExprValue[]))), typedItems,
                        allocate);
                }

                initialize.AddRange([
                    Lx.Assign(cursor, Lx.Constant(0)), Lx.Assign(count, Lx.Constant(0)),
                    Lx.Assign(symbols, Lx.Constant(false)),
                    Lx.Assign(values, allocate),
                    execute,
                    Lx.IfThen(Lx.NotEqual(count, length), Lx.Assign(values, Lx.Call(_frame, resize, values, count))),
                    Lx.Call(owned, values, symbols)
                ]);
                return Lx.Block(initialize);
            });

    private static MethodInfo SetOp(string name) => typeof(BitSetOperations).GetMethod(name, Any)!;

    // Small immutable sources can be partially evaluated. Item/index aliases
    // are constants in each iteration; only valid constant comparisons fold,
    // so untaken branches and located failures keep their original timing.
    private Lx ConstantMap(ExprValue[] entries, string itemName, string? indexName, Expression body)
    {
        _shape.Append("unrolled:").Append(entries.Length).Append(';');
        LxParameter values = Local(typeof(ExprValue[]));
        LxParameter value = Local(typeof(ExprValue));
        LxParameter symbols = Local(typeof(bool));
        IndexExpression itemCell = Lx.ArrayAccess(Lx.Field(_frame, nameof(SamplingFrame.Cells)),
            Operand(nameof(SamplingOperands.Slots), _slots, _owner.Variable(itemName)));
        IndexExpression? indexCell = indexName is null
            ? null
            : Lx.ArrayAccess(Lx.Field(_frame, nameof(SamplingFrame.Cells)),
                Operand(nameof(SamplingOperands.Slots), _slots, _owner.Variable(indexName)));
        LxParameter savedItem = Local(typeof(SamplingCell));
        LxParameter? savedIndex = indexName is null ? null : Local(typeof(SamplingCell));
        var statements = new List<Lx>();
        var publish = false;
        _bindings.TryGetValue(itemName, out IterationBinding? previousItem);
        IterationBinding? previousIndex =
            indexName is not null && _bindings.TryGetValue(indexName, out IterationBinding? binding) ? binding : null;
        try
        {
            for (var i = 0; i < entries.Length; i++)
            {
                ExprValue element = entries[i];
                ExprValue index = ExprValue.Number(i);
                var itemBinding = new IterationBinding("constant-item;", _ => Literal(element))
                {
                    Constant = element
                };
                IterationBinding? indexBinding = indexName is null
                    ? null
                    : new IterationBinding("constant-index;", _ => Literal(index))
                    {
                        Constant = index
                    };
                _bindings[itemName] = itemBinding;
                if (indexName is not null)
                {
                    _bindings[indexName] = indexBinding!;
                }

                Lx selector = Value(body);
                publish |= itemBinding.NeedsFrame || indexBinding?.NeedsFrame == true;
                statements.Add(Lx.Call(_frame,
                    typeof(SamplingFrame).GetMethod(nameof(SamplingFrame.CheckCancellation))!));
                if (itemBinding.NeedsFrame || indexBinding?.NeedsFrame == true)
                {
                    MethodInfo typed = typeof(SamplingCell).GetMethod(nameof(SamplingCell.Typed))!;
                    statements.Add(Lx.Assign(itemCell, Lx.Call(typed, Literal(element))));
                    if (indexCell is not null)
                    {
                        statements.Add(Lx.Assign(indexCell, Lx.Call(typed, Literal(index))));
                    }
                }

                statements.Add(Lx.Assign(value, selector));
                statements.Add(Lx.Assign(Lx.ArrayAccess(values, Lx.Constant(i)), value));
                var normalized = NormalizedResult(body);
                _shape.Append(normalized ? "normalized;" : "symbols;");
                if (!normalized)
                {
                    statements.Add(Lx.OrAssign(symbols, Lx.Property(value, nameof(ExprValue.ContainsSymbols))));
                }
            }
        }
        finally
        {
            if (previousItem is null)
            {
                _bindings.Remove(itemName);
            }
            else
            {
                _bindings[itemName] = previousItem;
            }

            if (indexName is not null)
            {
                if (previousIndex is null)
                {
                    _bindings.Remove(indexName);
                }
                else
                {
                    _bindings[indexName] = previousIndex;
                }
            }
        }

        var initialize = new List<Lx>
        {
            Lx.Assign(values,
                Lx.Call(_frame, typeof(SamplingFrame).GetMethod(nameof(SamplingFrame.NewArray), Any)!,
                    Lx.Constant(entries.Length))),
            Lx.Assign(symbols, Lx.Constant(false))
        };
        Lx execute = Lx.Block(statements.Count == 0 ? [Lx.Empty()] : statements);
        if (publish)
        {
            initialize.Add(Lx.Assign(savedItem, itemCell));
            if (indexCell is not null)
            {
                initialize.Add(Lx.Assign(savedIndex!, indexCell));
            }

            var restore = new List<Lx>
            {
                Lx.Assign(itemCell, savedItem)
            };
            if (indexCell is not null)
            {
                restore.Add(Lx.Assign(indexCell, savedIndex!));
            }

            restore.Add(Lx.Empty());
            execute = Lx.TryFinally(execute, Lx.Block(restore));
        }

        initialize.Add(execute);
        initialize.Add(Lx.Call(typeof(ExprValue).GetMethod(nameof(ExprValue.ArrayOwned), Any)!, values, symbols));
        return Lx.Block(initialize);
    }

    private bool TryBoundValue(Expression expression, out ExprValue value)
    {
        expression = _owner.Reduce(expression);
        if (expression is FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } field
            && _bindings.TryGetValue(field.Path[0], out IterationBinding? binding) && binding.Constant is { } constant)
        {
            value = constant;
            return true;
        }

        if (_owner.TryConstant(expression, out value) || TryConstant(expression, out value))
        {
            return true;
        }

        if (expression is IfExpr branch)
        {
            return TryBoundBoolean(branch.Condition, out var selected)
                   && TryBoundValue(selected ? branch.ThenExpr : branch.ElseExpr, out value);
        }

        if (expression is not (BinaryExpr or CompareExpr or NotExpr or CallExpr or BitSetExpr))
        {
            return false;
        }

        // Only pure forms with entirely immutable operands are evaluated here.
        // Reuse the existing semantics, and retain runtime evaluation for errors
        // or state-dependent operands, including arguments of unknown calls.
        var known = true;
        Func<SamplingFrame, ExprValue> evaluate = _owner.Interpret(expression, child =>
        {
            if (TryBoundValue(child, out ExprValue operand))
            {
                return _ => operand;
            }

            known = false;
            return _ => default;
        });
        if (!known)
        {
            return false;
        }

        try
        {
            value = evaluate(null!);
            return true;
        }
        catch (Exception ex) when (ex is ExpressionEvaluationException or FormatException or DivideByZeroException
                                       or OverflowException)
        {
            return false;
        }
    }

    // Facts hold only within a straight run. Every typed state write normalizes
    // nested symbols; a successful checked field read cannot produce Null.
    // Temporary iteration aliases take precedence over these state facts.
    private bool ReadableResult(Expression expression)
    {
        expression = _owner.Reduce(expression);
        if (TryBoundValue(expression, out ExprValue constant))
        {
            return constant.Kind != ExprType.Null;
        }

        return expression switch
        {
            FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } => true,
            BitSetExpr or ReachableExpr or ScanExpr or SegmentedCompactExpr or ScatterExpr or PartitionIndicesExpr
                or BinaryExpr or CompareExpr or NotExpr
                or AggregateExpr { Func: AggregateFunc.Count } or MapExpr
                or FilterExpr => true,
            IfExpr branch => ReadableResult(branch.ThenExpr) && ReadableResult(branch.ElseExpr),
            FoldExpr fold => ReadableResult(fold.Init) && ReadableResult(fold.Body),
            _ => false
        };
    }

    private bool NormalizedResult(Expression expression)
    {
        expression = _owner.Reduce(expression);
        if (TryBoundValue(expression, out ExprValue constant))
        {
            return !constant.ContainsSymbols;
        }

        return expression switch
        {
            FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } field =>
                !_bindings.ContainsKey(field.Path[0]) && _normalizedWrites.Contains(_slot(field.Path[0])),
            BitSetExpr or ReachableExpr or ScanExpr or SegmentedCompactExpr or PartitionIndicesExpr
                or ScatterExpr { Mode: ScatterMode.Sum } or BinaryExpr or CompareExpr
                or NotExpr => true,
            IfExpr branch => NormalizedResult(branch.ThenExpr) && NormalizedResult(branch.ElseExpr),
            _ => false
        };
    }

    private bool TryBoundBoolean(Expression expression, out bool value)
    {
        if (TryBoundValue(expression, out ExprValue constant) && constant.IsBoolean)
        {
            value = constant.BoolValue;
            return true;
        }

        if (expression is CompareExpr compare && TryBoundValue(compare.Left, out ExprValue left) &&
            TryBoundValue(compare.Right, out ExprValue right))
        {
            try
            {
                value = SamplingExpressions.CompareValues(compare.Op, left, right);
                return true;
            }
            catch (ExpressionEvaluationException)
            {
            }
        }

        value = false;
        return false;
    }

    // Lower the finite-set expression tree to unsigned machine words. Only a
    // boundary with an arbitrary numeric expression needs range validation;
    // intermediate set results are already valid masks, including bit 63.
    private Lx BitSetValue(BitSetExpr set)
    {
        if (set.Op is not (BitSetOp.Count or BitSetOp.First))
        {
            return Lx.Call(SetOp(nameof(BitSetOperations.Value)), BitMask(set));
        }

        return Form($"setnumber:{set.Op}", () => Lx.Call(NumberOf, BitSetInteger(set)));
    }

    private Lx BitSetInteger(BitSetExpr set)
    {
        LxParameter mask = Local(typeof(ulong));
        BinaryExpression assign = Lx.Assign(mask, MaskOperand(set.Args[0], 0));
        MethodInfo method = typeof(BitOperations).GetMethod(
            set.Op == BitSetOp.Count ? nameof(BitOperations.PopCount) : nameof(BitOperations.TrailingZeroCount),
            [typeof(ulong)])!;
        Lx count = Lx.Call(method, mask);
        if (set.Op == BitSetOp.First)
        {
            count = Lx.Condition(Lx.Equal(mask, Lx.Constant(0UL)), Lx.Constant(-1), count);
        }

        return Lx.Block(assign, Lx.Convert(count, typeof(long)));
    }

    private static bool IsMask(Expression expression) => expression is BitSetExpr
    {
        Args.Length: 2, Op: BitSetOp.Union or BitSetOp.Intersect or BitSetOp.Except or BitSetOp.Shift
    };

    // A conservative numeric range proof, independent of any authored graph.
    // Unknown forms use the ordinary exact-value representation.
    private bool KnownMask(Expression expression, string? accumulator = null, string? itemName = null,
        string? indexName = null)
    {
        expression = _owner.Reduce(expression);
        // Item and index assignments take precedence over the accumulator,
        // including an outer scoped binding with the same name.
        if (expression is FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } scoped
            && (scoped.Path[0] == itemName || scoped.Path[0] == indexName))
        {
            return false;
        }

        if (_owner.TryConstant(expression, out ExprValue constant) || TryConstant(expression, out constant))
        {
            return constant.Kind == ExprType.Number && constant.NumberDenominator.IsOne
                                                    && constant.NumberNumerator >= 0 &&
                                                    constant.NumberNumerator <= ulong.MaxValue;
        }

        return expression switch
        {
            FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } field => field.Path[0] == accumulator
                || (_bindings.TryGetValue(field.Path[0], out IterationBinding? binding)
                    ? binding.Mask is not null
                    : _owner.IsMaskField(field.Path[0])),
            IfExpr branch => KnownMask(branch.ThenExpr, accumulator, itemName, indexName) &&
                             KnownMask(branch.ElseExpr, accumulator, itemName, indexName),
            BitSetExpr { Op: BitSetOp.Count, Args.Length: 1 } => true,
            _ => IsMask(expression)
        };
    }

    private Lx KnownMaskValue(Expression expression)
    {
        expression = _owner.Reduce(expression);
        if (TrySetConstant(expression, false, out Lx constant))
        {
            return constant;
        }

        if (expression is FieldAccessExpr field &&
            _bindings.TryGetValue(field.Path[0], out IterationBinding? binding) &&
            binding.Mask is { } mask)
        {
            _shape.Append("mask-").Append(binding.Shape);
            return mask;
        }

        if (expression is FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } stored
            && !_bindings.ContainsKey(stored.Path[0]) && _owner.IsMaskField(stored.Path[0]))
        {
            if (_maskLocals.TryGetValue(_slot(stored.Path[0]),
                    out (LxParameter Value, LxParameter Dirty, bool Inline, bool Initial) promoted))
            {
                _shape.Append("local-mask-read:").Append(_slot(stored.Path[0])).Append(';');
                return promoted.Value;
            }

            var inline = _owner.IsInlineMaskField(stored.Path[0]);
            return Form(inline ? "inline-mask-state" : "mask-state", () =>
            {
                IndexExpression cell = Lx.ArrayAccess(Lx.Field(_frame, nameof(SamplingFrame.Cells)),
                    Operand(nameof(SamplingOperands.Slots), _slots, _slot(stored.Path[0])));
                return inline
                    ? Lx.Convert(
                        Lx.Property(Lx.Property(cell, nameof(SamplingCell.StoredValue)),
                            nameof(ExprValue.InlineInteger)), typeof(ulong))
                    : Lx.Property(cell, nameof(SamplingCell.StoredMask));
            });
        }

        if (expression is IfExpr branch)
        {
            return Form("mask-if",
                () => Lx.Condition(Condition(branch.Condition), KnownMaskValue(branch.ThenExpr),
                    KnownMaskValue(branch.ElseExpr)));
        }

        if (expression is BitSetExpr { Op: BitSetOp.Count } count)
        {
            return Form("mask-count",
                () => Lx.Convert(
                    Lx.Call(typeof(BitOperations).GetMethod(nameof(BitOperations.PopCount), [typeof(ulong)])!,
                        MaskOperand(count.Args[0], 0)), typeof(ulong)));
        }

        return BitMask((BitSetExpr)expression);
    }

    private bool TrySetConstant(Expression expression, bool distance, out Lx value)
    {
        expression = _owner.Reduce(expression);
        value = null!;
        if (!_owner.TryConstant(expression, out ExprValue constant) && !TryConstant(expression, out constant))
        {
            return false;
        }

        if (constant.Kind != ExprType.Number || !constant.NumberDenominator.IsOne)
        {
            return false;
        }

        BigInteger number = constant.NumberNumerator;
        if (distance ? number < -63 || number > 63 : number < 0 || number > ulong.MaxValue)
        {
            return false;
        }

        _shape.Append(distance ? "distance-literal;" : "mask-literal;");
        value = Lx.Convert(Operand(nameof(SamplingOperands.Numbers), _numbers,
            distance ? (long)number : unchecked((long)(ulong)number)), distance ? typeof(int) : typeof(ulong));
        return true;
    }

    private Lx MaskOperand(Expression expression, int index)
    {
        return KnownMask(expression)
            ? KnownMaskValue(expression)
            : Lx.Call(SetOp(nameof(BitSetOperations.Mask)), Value(expression), Lx.Constant($"bitset.args[{index}]"));
    }

    private Lx BitMask(BitSetExpr set) => Form($"setmask:{set.Op}", () =>
    {
        var statements = new List<Lx>();

        Lx Capture(Expression expression, int index, bool distance = false)
        {
            // Valid immutable operands cannot throw. Invalid literals remain on
            // the checked path so argument evaluation and error order stay intact.
            if (TrySetConstant(expression, distance, out Lx constant))
            {
                return constant;
            }

            Expression reduced = _owner.Reduce(expression);
            if (distance && reduced is FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } field
                         && _bindings.TryGetValue(field.Path[0], out IterationBinding? binding) &&
                         binding.ShiftDistance is { } cursor)
            {
                _shape.Append("bounded-distance;").Append(binding.Shape);
                return cursor;
            }

            var known = !distance && KnownMask(expression);
            _shape.Append(known ? "mask;" : "checked;");
            LxParameter value = Local(known ? typeof(ulong) : typeof(ExprValue));
            statements.Add(Lx.Assign(value, known ? KnownMaskValue(expression) : Value(expression)));
            return known ? value
                : distance ? Lx.Call(SetOp(nameof(BitSetOperations.Distance)), value)
                : Lx.Call(SetOp(nameof(BitSetOperations.Mask)), value, Lx.Constant($"bitset.args[{index}]"));
        }

        // Both operand expressions run before this operation validates them.
        Lx first = Capture(set.Args[0], 0);
        Lx second = Capture(set.Args[1], 1, set.Op == BitSetOp.Shift);
        if (set.Op == BitSetOp.Shift)
        {
            LxParameter mask = Local(typeof(ulong));
            LxParameter distance = Local(typeof(int));
            statements.Add(Lx.Assign(mask, first));
            statements.Add(Lx.Assign(distance, second));
            statements.Add(Lx.Condition(Lx.GreaterThanOrEqual(distance, Lx.Constant(0)), Lx.LeftShift(mask, distance),
                Lx.RightShift(mask, Lx.Negate(distance))));
        }
        else
        {
            statements.Add(set.Op switch
            {
                BitSetOp.Union => Lx.Or(first, second),
                BitSetOp.Intersect => Lx.And(first, second),
                BitSetOp.Except => Lx.And(first, Lx.Not(second)),
                _ => throw new InvalidOperationException()
            });
        }

        return Lx.Block(statements);
    });

    private Lx Condition(Expression expression)
    {
        if (expression.Precompute is not null && _owner.Precomputation(expression).Table is { } table)
        {
            return Lx.Call(typeof(SamplingExpressions).GetMethod(nameof(SamplingExpressions.RequiredBoolean))!,
                Precomputed(expression, table));
        }

        expression = _owner.Reduce(expression);
        if (_owner.TryConstantBool(expression, out var decided))
        {
            return Truth(decided);
        }

        if (TryBoundBoolean(expression, out decided))
        {
            return Truth(decided);
        }

        switch (expression)
        {
            case ConstantExpr when TryConstant(expression, out ExprValue constant) && constant.IsBoolean:
                return Truth(constant.BoolValue);
            case NotExpr not:
                return Form("not", () => Lx.Not(Condition(not.Expr)));
            case BinaryExpr { Op: BinaryOp.And } and:
                return Form("and", () =>
                {
                    Lx left = Condition(and.Left);
                    return Lx.AndAlso(left, Condition(and.Right));
                });
            case BinaryExpr { Op: BinaryOp.Or } or:
                return Form("or", () =>
                {
                    Lx left = Condition(or.Left);
                    return Lx.OrElse(left, Condition(or.Right));
                });
            case IfExpr branch:
                return Form("when", () =>
                {
                    Lx test = Condition(branch.Condition);
                    Lx yes = Condition(branch.ThenExpr);
                    return Lx.Condition(test, yes, Condition(branch.ElseExpr));
                });
            case CompareExpr
            {
                Op: CompareOp.Eq or CompareOp.Neq or CompareOp.Lt or CompareOp.Gt or CompareOp.Lte or CompareOp.Gte
            } compare:
                // Both operands are evaluated left to right before comparing.
                if (KnownInteger(compare.Left) && KnownInteger(compare.Right))
                {
                    return Form("integer-compare:" + compare.Op, () => Lx.MakeBinary(compare.Op switch
                    {
                        CompareOp.Eq => ExpressionType.Equal,
                        CompareOp.Neq => ExpressionType.NotEqual,
                        CompareOp.Lt => ExpressionType.LessThan,
                        CompareOp.Gt => ExpressionType.GreaterThan,
                        CompareOp.Lte => ExpressionType.LessThanOrEqual,
                        _ => ExpressionType.GreaterThanOrEqual
                    }, IntegerValue(compare.Left), IntegerValue(compare.Right)));
                }

                if (compare.Op is CompareOp.Eq or CompareOp.Neq && TryConstant(compare.Right, out ExprValue text) &&
                    text.IsString && text.StringValue is { } literal)
                {
                    return Form(compare.Op + "Text", () =>
                        Lx.Call(
                            Op(compare.Op == CompareOp.Eq ? nameof(SamplingOps.EqText) : nameof(SamplingOps.NeqText)),
                            Value(compare.Left), Text(literal)));
                }

                return Form(compare.Op.ToString(), () =>
                {
                    Lx left = Value(compare.Left);
                    return Lx.Call(Op(compare.Op.ToString()), left, Value(compare.Right));
                });
            default:
                return Form("truth", () => Lx.Call(TruthOp, Value(expression)));
        }
    }

    private bool KnownInteger(Expression expression) => TryIntegerRange(expression, out _);

    private bool TryIntegerRange(Expression expression, out SamplingIntegerRange range, int depth = 0)
    {
        range = default;
        if (depth > 128)
        {
            return false;
        }

        expression = _owner.Reduce(expression);
        if (expression.Precompute is not null)
        {
            return false;
        }

        if (TryBoundValue(expression, out ExprValue literal))
        {
            if (!literal.IsInlineInteger)
            {
                return false;
            }

            range = new SamplingIntegerRange(literal.InlineInteger, literal.InlineInteger);
            return true;
        }

        switch (expression)
        {
            case FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } field:
                if (_bindings.TryGetValue(field.Path[0], out IterationBinding? binding))
                {
                    range = new SamplingIntegerRange(0, int.MaxValue);
                    return binding.Integer is not null;
                }

                range = new SamplingIntegerRange(0, long.MaxValue);
                return _owner.IsInlineMaskField(field.Path[0]);
            case BitSetExpr { Args.Length: 1, Op: BitSetOp.Count }:
                range = new SamplingIntegerRange(0, 64);
                return true;
            case BitSetExpr { Args.Length: 1, Op: BitSetOp.First }:
                range = new SamplingIntegerRange(-1, 63);
                return true;
            case BinaryExpr { Op: BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul } binary:
                return TryIntegerRange(binary.Left, out SamplingIntegerRange left, depth + 1) &&
                       TryIntegerRange(binary.Right, out SamplingIntegerRange right, depth + 1) &&
                       SamplingIntegerRange.TryBinary(binary.Op, left, right, out range);
            case CallExpr { Args.Length: 1 } call
                when string.Equals(call.Function, "floor", StringComparison.OrdinalIgnoreCase)
                     && call.Args[0] is BinaryExpr { Op: BinaryOp.Div } division:
                return TryIntegerRange(division.Left, out SamplingIntegerRange numerator, depth + 1) &&
                       TryIntegerRange(division.Right, out SamplingIntegerRange denominator, depth + 1) &&
                       SamplingIntegerRange.TryFloorDivide(numerator, denominator, out range);
            case CallExpr call when TryIntegerTable(call, out SamplingIntegerTable table):
                range = table.Range;
                return true;
            case IfExpr branch:
                if (TryIntegerRange(branch.ThenExpr, out SamplingIntegerRange yes, depth + 1) &&
                    TryIntegerRange(branch.ElseExpr, out SamplingIntegerRange no, depth + 1))
                {
                    range = new SamplingIntegerRange(System.Math.Min(yes.Minimum, no.Minimum),
                        System.Math.Max(yes.Maximum, no.Maximum));
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private Lx IntegerValue(Expression expression)
    {
        expression = _owner.Reduce(expression);
        return _subexpressions?.EmitInteger(expression, () => IntegerValueCore(expression), text => _shape.Append(text))
               ?? IntegerValueCore(expression);
    }

    private Lx IntegerValueCore(Expression expression)
    {
        expression = _owner.Reduce(expression);
        if (TryBoundValue(expression, out ExprValue literal))
        {
            if (literal.InlineInteger == 0)
            {
                return Form("integer-zero", () => Lx.Constant(0L));
            }

            return Form("integer-literal",
                () => Operand(nameof(SamplingOperands.Numbers), _numbers, literal.InlineInteger));
        }

        if (expression is FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } field)
        {
            if (_bindings.TryGetValue(field.Path[0], out IterationBinding? binding) && binding.Integer is { } integer)
            {
                _shape.Append("integer-").Append(binding.Shape);
                return integer;
            }

            if (!_bindings.ContainsKey(field.Path[0]) &&
                _maskLocals.TryGetValue(_slot(field.Path[0]),
                    out (LxParameter Value, LxParameter Dirty, bool Inline, bool Initial) promoted))
            {
                _shape.Append("local-mask-integer:").Append(_slot(field.Path[0])).Append(';');
                return Lx.Convert(promoted.Value, typeof(long));
            }

            return Form("integer-state", () => Lx.Property(Lx.Property(Lx.ArrayAccess(
                    Lx.Field(_frame, nameof(SamplingFrame.Cells)),
                    Operand(nameof(SamplingOperands.Slots), _slots, _slot(field.Path[0]))),
                nameof(SamplingCell.StoredValue)), nameof(ExprValue.InlineInteger)));
        }

        if (expression is BinaryExpr binary)
        {
            return Form("integer-" + binary.Op, () => binary.Op switch
            {
                BinaryOp.Add => Lx.Add(IntegerValue(binary.Left), IntegerValue(binary.Right)),
                BinaryOp.Sub => Lx.Subtract(IntegerValue(binary.Left), IntegerValue(binary.Right)),
                BinaryOp.Mul => Lx.Multiply(IntegerValue(binary.Left), IntegerValue(binary.Right)),
                _ => throw new InvalidOperationException("An unproven integer operation reached code generation.")
            });
        }

        if (expression is CallExpr call && TryIntegerTable(call, out SamplingIntegerTable table))
        {
            return IntegerTableRead(call, table);
        }

        if (expression is CallExpr { Args: [BinaryExpr division] })
        {
            return Form("integer-floor-divide", () => Lx.Call(Op(nameof(SamplingOps.FloorDivideInteger)),
                IntegerValue(division.Left), IntegerValue(division.Right)));
        }

        if (expression is IfExpr branch)
        {
            return Form("integer-if", () => Lx.Condition(Condition(branch.Condition),
                IntegerValue(branch.ThenExpr), IntegerValue(branch.ElseExpr)));
        }

        return Form("integer-set", () => BitSetInteger((BitSetExpr)expression));
    }

    private Lx Call(CallExpr call)
    {
        var function = call.Function.ToLowerInvariant();
        Expression[] args = call.Args;

        Lx Binary(MethodInfo method)
        {
            return Form(function, () =>
            {
                Lx first = Value(args[0]);
                return Lx.Call(method, first, Value(args[1]));
            });
        }

        switch (function, args.Length)
        {
            case ("floor", 1) when args[0] is BinaryExpr { Op: BinaryOp.Div } quotient:
                if (KnownInteger(call))
                {
                    return Form("integer-floor-result", () => Lx.Call(NumberOf, IntegerValueCore(call)));
                }

                return Form("floor-divide", () => Lx.Call(Op(nameof(SamplingOps.FloorDivide)),
                    Value(quotient.Left), Value(quotient.Right)));
            case ("tonumber", 1):
                return Form(function, () =>
                {
                    LxParameter text = Local(typeof(ExprValue));
                    return Lx.Block(Lx.Assign(text, Value(args[0])),
                        Lx.Condition(Lx.Call(HasNumericText, text), Lx.Call(NumericText, text),
                            Lx.Call(Lx.Field(_operands, nameof(SamplingOperands.Owner)), ToNumberSlow, text)));
                });
            case ("tostring", 1):
                return Form(function, () => Lx.Call(Collection(nameof(CollectionFunctions.ToText)), Value(args[0])));
            case ("length", 1):
                return Form(function, () => Lx.Call(Collection(nameof(CollectionFunctions.Length)), Value(args[0])));
            case ("contains", 2): return Binary(Collection(nameof(CollectionFunctions.Contains)));
            case ("index", 2):
                if (_owner.Reduce(args[0]) is FieldAccessExpr { Path.Length: 1 } sourceField &&
                    !_bindings.ContainsKey(sourceField.Path[0]) &&
                    _owner.TryConstant(sourceField, out ExprValue source) && source.ItemsArray is { } items)
                {
                    if (TryBoundValue(args[1], out ExprValue position) && position.IsInlineInteger
                                                                       && (ulong)position.InlineInteger <
                                                                       (ulong)items.Length)
                    {
                        return Form("index-literal", () => Literal(items[position.InlineInteger]));
                    }

                    if (KnownInteger(args[1]))
                    {
                        return Form("index-items-integer", () => Lx.Call(Op(nameof(SamplingOps.IndexItemsInteger)),
                            Operand(nameof(SamplingOperands.Arrays), _arrays, items), IntegerValue(args[1])));
                    }

                    return Form("index-items", () => Lx.Call(Op(nameof(SamplingOps.IndexItems)),
                        Operand(nameof(SamplingOperands.Arrays), _arrays, items), Value(args[1])));
                }

                if (KnownInteger(args[1]))
                {
                    return Form("index-integer", () => Lx.Call(Op(nameof(SamplingOps.IndexInteger)),
                        Value(args[0]), IntegerValue(args[1])));
                }

                return Binary(Op(nameof(SamplingOps.Index)));
            case ("max", 2): return Binary(Op(nameof(SamplingOps.Max)));
            case ("min", 2): return Binary(Op(nameof(SamplingOps.Min)));
            case ("append", 2): return Append(call);
        }

        // Any other name or arity: evaluate every argument in order, then let
        // the shared implementation report arity and type errors.
        return Form($"call:{function.Length}:{function}:{args.Length}", () =>
        {
            var statements = new List<Lx>();
            LxParameter first = Local(typeof(ExprValue));
            LxParameter second = Local(typeof(ExprValue));
            statements.Add(Lx.Assign(first, args.Length > 0 ? Value(args[0]) : Lx.Call(NumberOf, Lx.Constant(0L))));
            statements.Add(Lx.Assign(second, args.Length > 1 ? Value(args[1]) : Lx.Call(NumberOf, Lx.Constant(0L))));
            for (var i = 2; i < args.Length; i++)
            {
                statements.Add(Value(args[i]));
            }

            statements.Add(Lx.Call(Lx.Field(_operands, nameof(SamplingOperands.Owner)), CallGeneric,
                Lx.Constant(function), first, second, Lx.Constant(args.Length)));
            return Lx.Block(typeof(ExprValue), statements);
        });
    }

    private bool TryIntegerTable(CallExpr call, out SamplingIntegerTable table)
    {
        table = null!;
        if (call.Args.Length != 2 || !string.Equals(call.Function, "index", StringComparison.OrdinalIgnoreCase) ||
            _owner.Reduce(call.Args[0]) is not FieldAccessExpr
            {
                Target: null or "state" or "board", Path.Length: 1
            } field ||
            _bindings.ContainsKey(field.Path[0]) || _owner.IntegerTable(field.Path[0]) is not { } prepared)
        {
            return false;
        }

        table = prepared;
        return true;
    }

    private Lx IntegerTableRead(CallExpr call, SamplingIntegerTable table)
    {
        var integer = KnownInteger(call.Args[1]);
        return Form(integer ? "integer-table-index" : "integer-table-checked-index", () =>
            Lx.Call(Operand(nameof(SamplingOperands.IntegerTables), _integerTables, table),
                typeof(SamplingIntegerTable).GetMethod(nameof(SamplingIntegerTable.Read), Any,
                    [integer ? typeof(long) : typeof(ExprValue)])!,
                integer ? IntegerValue(call.Args[1]) : Value(call.Args[1])));
    }

    // append(append(append(base, a), b), c) builds one array instead of three.
    // The base is checked after `a` is evaluated, as the innermost append would.
    private Lx Append(CallExpr outer)
    {
        var appended = new List<Expression>();
        Expression source = outer;
        while (source is CallExpr { Args.Length: 2 } inner && inner.Function.ToLowerInvariant() == "append")
        {
            appended.Add(inner.Args[1]);
            source = inner.Args[0];
        }

        appended.Reverse();

        return Form($"append:{appended.Count}", () =>
        {
            LxParameter first = Local(typeof(ExprValue));
            LxParameter head = Local(typeof(ExprValue));
            LxParameter items = Local(typeof(ExprValue[]));
            var statements = new List<Lx>
            {
                Lx.Assign(first, Value(source)),
                Lx.Assign(head, Value(appended[0])),
                Lx.Assign(items, Lx.Call(AppendBegin, _frame, first, Lx.Constant(appended.Count)))
            };

            Lx Slot(int i)
            {
                return Lx.ArrayAccess(items, Lx.Subtract(Lx.ArrayLength(items), Lx.Constant(appended.Count - i)));
            }

            statements.Add(Lx.Assign(Slot(0), head));
            for (var i = 1; i < appended.Count; i++)
            {
                statements.Add(Lx.Assign(Slot(i), Value(appended[i])));
            }

            statements.Add(Lx.Call(AppendEnd, items));
            return Lx.Block(typeof(ExprValue), statements);
        });
    }

    private sealed record IterationBinding(string Shape, Func<string, Lx> Read, Lx? Mask = null)
    {
        public bool NeedsFrame { get; set; }
        public Func<LxParameter, Lx>? Probe { get; init; }
        public Lx? Integer { get; init; }
        public Lx? ShiftDistance { get; init; }
        public ExprValue? Constant { get; init; }
    }

    private sealed record ScalarRecordAccumulator(
        string[] Names,
        IterationBinding Owner,
        Dictionary<string, LxParameter> Fields);
}
