using System.Reflection;
using System.Runtime.CompilerServices;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;
using Lx = System.Linq.Expressions.Expression;
using LxParameter = System.Linq.Expressions.ParameterExpression;

namespace SlotMath.Core.Compiler;

/// <summary>How a sampling expression is executed. Every tier has the same
/// observable semantics; the tiers differ only in start-up and steady-state cost.</summary>
public enum SamplingTier
{
    /// <summary>Start interpreted; generate IL once the expression is hot.</summary>
    Tiered,
    /// <summary>Closure tree only. No code generation.</summary>
    Interpreted,
    /// <summary>Generate IL when the expression is bound.</summary>
    Compiled,
}

/// <summary>Process-wide default for newly compiled sampling plans. A host may
/// select <see cref="SamplingTier.Interpreted"/> to rule code generation out
/// while diagnosing a result; sampled statistics do not depend on the tier.</summary>
public static class SamplingOptions
{
    public static SamplingTier Tier { get; set; } = SamplingTier.Tiered;
    /// <summary>Evaluations of one expression before IL is generated for it.</summary>
    public static int HotThreshold { get; set; } = 64;
}

/// <summary>Everything in which two expressions of the same shape differ:
/// the fields they read, their constants, and the closures of the forms that
/// stay interpreted. Generated code takes these as an argument, so one method
/// serves every expression of that shape.</summary>
internal sealed class SamplingOperands(int[] slots, string[] texts, long[] numbers, ExprValue[] values, Func<SamplingFrame, ExprValue>[] closures, SamplingExpressions owner)
{
    public readonly int[] Slots = slots;
    public readonly string[] Texts = texts;
    public readonly long[] Numbers = numbers;
    public readonly ExprValue[] Values = values;
    public readonly Func<SamplingFrame, ExprValue>[] Closures = closures;
    public readonly SamplingExpressions Owner = owner;
}

/// <summary>Generated methods by expression shape, shared by a compiled program
/// and its replicas. A slot game repeats a few shapes many times (one per
/// payline, reel or cell); sharing keeps the hot code small enough to stay in
/// the instruction cache and is generated once.</summary>
internal sealed class SamplingShapes
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Delegate>> _code = new(StringComparer.Ordinal);

    public T Get<T>(string shape, Func<T> compile) where T : Delegate => (T)_code.GetOrAdd(shape, _ => new Lazy<Delegate>(compile)).Value;

    public int Count => _code.Count;
}

/// <summary>One bound expression. Its implementation is replaced by generated
/// code once the expression is hot; <see cref="Entry"/> is a stable delegate for
/// callers that keep one.</summary>
internal sealed class SamplingDelegate<T>
{
    // Code and the operands it expects are published together.
    private sealed class Binding(Func<SamplingFrame, SamplingOperands?, T> code, SamplingOperands? operands)
    {
        public readonly Func<SamplingFrame, SamplingOperands?, T> Code = code;
        public readonly SamplingOperands? Operands = operands;
    }

    private Binding _current;
    public readonly Func<SamplingFrame, T> Entry;
    private readonly Func<SamplingFrame, T> _interpreted;
    private readonly Func<(Func<SamplingFrame, SamplingOperands?, T> Code, SamplingOperands Operands)>? _generate;
    private readonly int _threshold = System.Math.Max(1, SamplingOptions.HotThreshold);
    private int _calls;

    public SamplingDelegate(Func<SamplingFrame, T> interpreted, Func<(Func<SamplingFrame, SamplingOperands?, T> Code, SamplingOperands Operands)>? generate, SamplingTier tier)
    {
        _interpreted = interpreted;
        if (generate is null || tier == SamplingTier.Interpreted)
        {
            _current = new((frame, _) => interpreted(frame), null); Entry = interpreted;
            return;
        }
        Entry = Invoke;
        if (tier == SamplingTier.Compiled) { var (code, operands) = generate(); _current = new(code, operands); }
        else { _generate = generate; _current = new((frame, _) => Cold(frame), null); }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Invoke(SamplingFrame frame)
    {
        var current = _current;
        return current.Code(frame, current.Operands);
    }

    private T Cold(SamplingFrame frame)
    {
        if (Interlocked.Increment(ref _calls) == _threshold)
        {
            // Generation is an optimization: if it is unavailable, the
            // interpreted closure remains the implementation.
            try { var (code, operands) = _generate!(); _current = new(code, operands); }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or PlatformNotSupportedException)
            { _current = new((next, _) => _interpreted(next), null); }
        }
        return _interpreted(frame);
    }
}

/// <summary>Operators emitted by <see cref="SamplingCodegen"/>. Each has an
/// inlined path for 64-bit integers and plain strings, and otherwise defers to
/// the shared exact implementation, so results and located errors are identical
/// to the interpreted tier.</summary>
internal static class SamplingOps
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Truth(ExprValue value) => value.IsBoolean ? value.InlineInteger != 0 : NotBoolean();
    private static bool NotBoolean() => throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "Boolean expression required.");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Add(ExprValue l, ExprValue r)
    {
        if (!(l.IsInlineInteger && r.IsInlineInteger)) RequireNumbers(BinaryOp.Add, l, r);
        return ExprValue.Add(l, r);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Sub(ExprValue l, ExprValue r)
    {
        if (!(l.IsInlineInteger && r.IsInlineInteger)) RequireNumbers(BinaryOp.Sub, l, r);
        return ExprValue.Sub(l, r);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Mul(ExprValue l, ExprValue r)
    {
        if (!(l.IsInlineInteger && r.IsInlineInteger)) RequireNumbers(BinaryOp.Mul, l, r);
        return ExprValue.Mul(l, r);
    }

    public static ExprValue Div(ExprValue l, ExprValue r)
    {
        if (!(l.IsInlineInteger && r.IsInlineInteger)) RequireNumbers(BinaryOp.Div, l, r);
        return ExprValue.Div(l, r);
    }

    private static void RequireNumbers(BinaryOp op, ExprValue l, ExprValue r)
    {
        if (l.Kind != ExprType.Number || r.Kind != ExprType.Number)
            throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", $"Operator {op} requires numeric operands.");
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
        l.IsString ? string.Equals(l.TextUnchecked, text) : SamplingExpressions.CompareValues(CompareOp.Eq, l, ExprValue.String(text));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool NeqText(ExprValue l, string text) =>
        l.IsString ? !string.Equals(l.TextUnchecked, text) : SamplingExpressions.CompareValues(CompareOp.Neq, l, ExprValue.String(text));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Lt(ExprValue l, ExprValue r) =>
        l.IsInlineInteger && r.IsInlineInteger ? l.InlineInteger < r.InlineInteger : SamplingExpressions.CompareValues(CompareOp.Lt, l, r);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Gt(ExprValue l, ExprValue r) =>
        l.IsInlineInteger && r.IsInlineInteger ? l.InlineInteger > r.InlineInteger : SamplingExpressions.CompareValues(CompareOp.Gt, l, r);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Lte(ExprValue l, ExprValue r) =>
        l.IsInlineInteger && r.IsInlineInteger ? l.InlineInteger <= r.InlineInteger : SamplingExpressions.CompareValues(CompareOp.Lte, l, r);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Gte(ExprValue l, ExprValue r) =>
        l.IsInlineInteger && r.IsInlineInteger ? l.InlineInteger >= r.InlineInteger : SamplingExpressions.CompareValues(CompareOp.Gte, l, r);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Index(ExprValue array, ExprValue position)
    {
        if (position.IsInlineInteger && array.ItemsArray is { } items && (ulong)position.InlineInteger < (ulong)items.Length)
            return items[position.InlineInteger];
        return CollectionFunctions.Index(array, position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Max(ExprValue l, ExprValue r) =>
        l.IsInlineInteger && r.IsInlineInteger ? (l.InlineInteger >= r.InlineInteger ? l : r) : NumericFunctions.Call("max", l, r, 2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Min(ExprValue l, ExprValue r) =>
        l.IsInlineInteger && r.IsInlineInteger ? (l.InlineInteger <= r.InlineInteger ? l : r) : NumericFunctions.Call("min", l, r, 2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasNumericText(ExprValue value) => value.TryGetNumericText(out _);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue NumericText(ExprValue value) { value.TryGetNumericText(out var number); return ExprValue.Number(number); }

    /// <summary>First step of a fused append chain: checks the base exactly as a
    /// single append would, then reserves room for every appended item.</summary>
    public static ExprValue[] AppendBegin(ExprValue first, int extra)
    {
        if (first.Kind != ExprType.Array) throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "append requires an array.", "append");
        if (first.ItemsArray is { } items)
        {
            var copy = new ExprValue[items.Length + extra];
            items.CopyTo(copy, 0);
            return copy;
        }
        var list = first.ArrayValue!;
        var result = new ExprValue[list.Count + extra];
        for (var i = 0; i < list.Count; i++) result[i] = list[i];
        return result;
    }

    public static ExprValue AppendEnd(ExprValue[] items)
    {
        var symbols = false;
        foreach (ref readonly var item in items.AsSpan()) if (item.ContainsSymbols) { symbols = true; break; }
        return ExprValue.ArrayOwned(items, symbols);
    }
}

/// <summary>Translates one expression AST into a LINQ expression tree. Scalar
/// operators become straight-line IL with statically typed conditions; forms
/// that bind iteration variables or take an uncommon path reuse the
/// interpreted closures, with their bodies compiled.
/// <para>While translating, the generator writes down the expression's shape:
/// every decision that selects what is emitted. Slots, constants and closures
/// are not part of it; they become <see cref="SamplingOperands"/>. Two
/// expressions with equal shapes therefore produce the same method.</para></summary>
internal sealed class SamplingCodegen
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static MethodInfo Op(string name) => typeof(SamplingOps).GetMethod(name, Any) ?? throw new MissingMethodException(nameof(SamplingOps), name);

    private static readonly MethodInfo NumberOf = typeof(ExprValue).GetMethod(nameof(ExprValue.Number), [typeof(long)])!;
    private static readonly MethodInfo BoolOf = typeof(ExprValue).GetMethod(nameof(ExprValue.Bool), [typeof(bool)])!;
    private static readonly MethodInfo ReadField = typeof(SamplingFrame).GetMethod(nameof(SamplingFrame.Read), Any)!;
    private static readonly MethodInfo TruthOp = Op(nameof(SamplingOps.Truth));
    private static readonly MethodInfo HasNumericText = Op(nameof(SamplingOps.HasNumericText));
    private static readonly MethodInfo NumericText = Op(nameof(SamplingOps.NumericText));
    private static readonly MethodInfo AppendBegin = Op(nameof(SamplingOps.AppendBegin));
    private static readonly MethodInfo AppendEnd = Op(nameof(SamplingOps.AppendEnd));
    private static readonly MethodInfo ToNumberSlow = typeof(SamplingExpressions).GetMethod(nameof(SamplingExpressions.ToNumber), Any)!;
    private static readonly MethodInfo CallGeneric = typeof(SamplingExpressions).GetMethod(nameof(SamplingExpressions.Call), Any)!;
    private static MethodInfo Collection(string name) => typeof(CollectionFunctions).GetMethod(name, Any)!;

    private readonly SamplingExpressions _owner;
    private readonly Func<string, int> _slot;
    private readonly LxParameter _frame = Lx.Parameter(typeof(SamplingFrame), "frame");
    private readonly LxParameter _operands = Lx.Parameter(typeof(SamplingOperands), "operands");
    private readonly List<LxParameter> _locals = [];
    private readonly System.Text.StringBuilder _shape = new();
    private readonly List<int> _slots = [];
    private readonly List<string> _texts = [];
    private readonly List<long> _numbers = [];
    private readonly List<ExprValue> _values = [];
    private readonly List<Func<SamplingFrame, ExprValue>> _closures = [];

    private SamplingCodegen(SamplingExpressions owner, Func<string, int> slot) { _owner = owner; _slot = slot; }

    public static (Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) CompileValue(SamplingExpressions owner, Func<string, int> slot, Expression expression)
    {
        var generator = new SamplingCodegen(owner, slot);
        return generator.Finish<ExprValue>("value:", generator.Value(expression));
    }

    public static (Func<SamplingFrame, SamplingOperands?, bool> Code, SamplingOperands Operands) CompileCondition(SamplingExpressions owner, Func<string, int> slot, Expression expression)
    {
        var generator = new SamplingCodegen(owner, slot);
        return generator.Finish<bool>("condition:", generator.Condition(expression));
    }

    private (Func<SamplingFrame, SamplingOperands?, T> Code, SamplingOperands Operands) Finish<T>(string kind, Lx body)
    {
        var code = _owner.Shapes.Get(kind + _shape, () => Lx.Lambda<Func<SamplingFrame, SamplingOperands?, T>>(Lx.Block(_locals, body), _frame, _operands).Compile());
        return (code, new SamplingOperands([.. _slots], [.. _texts], [.. _numbers], [.. _values], [.. _closures], _owner));
    }

    private LxParameter Local(Type type)
    {
        var local = Lx.Variable(type);
        _locals.Add(local);
        return local;
    }

    // An operand: element `list.Count` of the named array, in emission order.
    private Lx Operand<T>(string array, List<T> list, T value)
    {
        list.Add(value);
        return Lx.ArrayIndex(Lx.Field(_operands, array), Lx.Constant(list.Count - 1));
    }

    private Lx Text(string value) => Operand(nameof(SamplingOperands.Texts), _texts, value);

    private Lx Interpreted(Expression expression)
    {
        _shape.Append("closure;");
        return Lx.Invoke(Operand(nameof(SamplingOperands.Closures), _closures, _owner.Interpret(expression, Compiled)), _frame);
    }

    private Func<SamplingFrame, ExprValue> Compiled(Expression expression)
    {
        var (code, operands) = CompileValue(_owner, _slot, expression);
        return frame => code(frame, operands);
    }

    private static bool TryConstant(Expression expression, out ExprValue value)
    {
        value = default;
        if (expression is not ConstantExpr constant) return false;
        try { value = ExactExpressionEvaluator.Evaluate(constant, EvalContext.Empty); return true; }
        catch (Exception ex) when (ex is FormatException or DivideByZeroException or ExpressionEvaluationException) { return false; }
    }

    private Lx Literal(ExprValue constant)
    {
        if (constant.IsInlineInteger) { _shape.Append("integer;"); return Lx.Call(NumberOf, Operand(nameof(SamplingOperands.Numbers), _numbers, constant.InlineInteger)); }
        if (constant.IsBoolean) return Lx.Call(BoolOf, Truth(constant.BoolValue));
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
        var result = emit();
        _shape.Append(')');
        return result;
    }

    private Lx Value(Expression expression)
    {
        expression = _owner.Reduce(expression);
        if (_owner.TryConstant(expression, out var unchanging)) return Literal(unchanging);
        switch (expression)
        {
            case ConstantExpr:
                return TryConstant(expression, out var constant) ? Literal(constant) : Interpreted(expression);
            case FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } field when !string.IsNullOrEmpty(field.Path[0]):
                _shape.Append("field;");
                return Lx.Call(_frame, ReadField, Operand(nameof(SamplingOperands.Slots), _slots, _slot(field.Path[0])), Text(field.Path[0]));
            case IfExpr branch:
                return Form("if", () =>
                {
                    var test = Condition(branch.Condition); var yes = Value(branch.ThenExpr);
                    return Lx.Condition(test, yes, Value(branch.ElseExpr));
                });
            case NotExpr or BinaryExpr { Op: BinaryOp.And or BinaryOp.Or }
                or CompareExpr { Op: CompareOp.Eq or CompareOp.Neq or CompareOp.Lt or CompareOp.Gt or CompareOp.Lte or CompareOp.Gte }:
                return Form("bool", () => Lx.Call(BoolOf, Condition(expression)));
            case BinaryExpr { Op: BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul or BinaryOp.Div } binary:
                return Form(binary.Op.ToString(), () =>
                {
                    var left = Value(binary.Left);
                    return Lx.Call(Op(binary.Op.ToString()), left, Value(binary.Right));
                });
            case CallExpr call:
                return Call(call);
            default:
                return Interpreted(expression);
        }
    }

    private Lx Condition(Expression expression)
    {
        expression = _owner.Reduce(expression);
        if (_owner.TryConstantBool(expression, out var decided)) return Truth(decided);
        switch (expression)
        {
            case ConstantExpr when TryConstant(expression, out var constant) && constant.IsBoolean:
                return Truth(constant.BoolValue);
            case NotExpr not:
                return Form("not", () => Lx.Not(Condition(not.Expr)));
            case BinaryExpr { Op: BinaryOp.And } and:
                return Form("and", () => { var left = Condition(and.Left); return Lx.AndAlso(left, Condition(and.Right)); });
            case BinaryExpr { Op: BinaryOp.Or } or:
                return Form("or", () => { var left = Condition(or.Left); return Lx.OrElse(left, Condition(or.Right)); });
            case IfExpr branch:
                return Form("when", () =>
                {
                    var test = Condition(branch.Condition); var yes = Condition(branch.ThenExpr);
                    return Lx.Condition(test, yes, Condition(branch.ElseExpr));
                });
            case CompareExpr { Op: CompareOp.Eq or CompareOp.Neq or CompareOp.Lt or CompareOp.Gt or CompareOp.Lte or CompareOp.Gte } compare:
                // Both operands are evaluated left to right before comparing.
                if (compare.Op is CompareOp.Eq or CompareOp.Neq && TryConstant(compare.Right, out var text) && text.IsString && text.StringValue is { } literal)
                    return Form(compare.Op + "Text", () =>
                        Lx.Call(Op(compare.Op == CompareOp.Eq ? nameof(SamplingOps.EqText) : nameof(SamplingOps.NeqText)), Value(compare.Left), Text(literal)));
                return Form(compare.Op.ToString(), () =>
                {
                    var left = Value(compare.Left);
                    return Lx.Call(Op(compare.Op.ToString()), left, Value(compare.Right));
                });
            default:
                return Form("truth", () => Lx.Call(TruthOp, Value(expression)));
        }
    }

    private Lx Call(CallExpr call)
    {
        var function = call.Function.ToLowerInvariant();
        var args = call.Args;
        Lx Binary(MethodInfo method) => Form(function, () => { var first = Value(args[0]); return Lx.Call(method, first, Value(args[1])); });
        switch (function, args.Length)
        {
            case ("tonumber", 1):
                return Form(function, () =>
                {
                    var text = Local(typeof(ExprValue));
                    return Lx.Block(Lx.Assign(text, Value(args[0])),
                        Lx.Condition(Lx.Call(HasNumericText, text), Lx.Call(NumericText, text), Lx.Call(Lx.Field(_operands, nameof(SamplingOperands.Owner)), ToNumberSlow, text)));
                });
            case ("tostring", 1): return Form(function, () => Lx.Call(Collection(nameof(CollectionFunctions.ToText)), Value(args[0])));
            case ("length", 1): return Form(function, () => Lx.Call(Collection(nameof(CollectionFunctions.Length)), Value(args[0])));
            case ("contains", 2): return Binary(Collection(nameof(CollectionFunctions.Contains)));
            case ("index", 2): return Binary(Op(nameof(SamplingOps.Index)));
            case ("max", 2): return Binary(Op(nameof(SamplingOps.Max)));
            case ("min", 2): return Binary(Op(nameof(SamplingOps.Min)));
            case ("append", 2): return Append(call);
        }

        // Any other name or arity: evaluate every argument in order, then let
        // the shared implementation report arity and type errors.
        return Form($"call:{function.Length}:{function}:{args.Length}", () =>
        {
            var statements = new List<Lx>();
            var first = Local(typeof(ExprValue)); var second = Local(typeof(ExprValue));
            statements.Add(Lx.Assign(first, args.Length > 0 ? Value(args[0]) : Lx.Call(NumberOf, Lx.Constant(0L))));
            statements.Add(Lx.Assign(second, args.Length > 1 ? Value(args[1]) : Lx.Call(NumberOf, Lx.Constant(0L))));
            for (var i = 2; i < args.Length; i++) statements.Add(Value(args[i]));
            statements.Add(Lx.Call(Lx.Field(_operands, nameof(SamplingOperands.Owner)), CallGeneric, Lx.Constant(function), first, second, Lx.Constant(args.Length)));
            return Lx.Block(typeof(ExprValue), statements);
        });
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
            var first = Local(typeof(ExprValue)); var head = Local(typeof(ExprValue)); var items = Local(typeof(ExprValue[]));
            var statements = new List<Lx>
            {
                Lx.Assign(first, Value(source)),
                Lx.Assign(head, Value(appended[0])),
                Lx.Assign(items, Lx.Call(AppendBegin, first, Lx.Constant(appended.Count))),
            };
            Lx Slot(int i) => Lx.ArrayAccess(items, Lx.Subtract(Lx.ArrayLength(items), Lx.Constant(appended.Count - i)));
            statements.Add(Lx.Assign(Slot(0), head));
            for (var i = 1; i < appended.Count; i++) statements.Add(Lx.Assign(Slot(i), Value(appended[i])));
            statements.Add(Lx.Call(AppendEnd, items));
            return Lx.Block(typeof(ExprValue), statements);
        });
    }
}
