using System.Globalization;
using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler;

/// <summary>Specializes AST dispatch, field lookup, and constants once per graph.
/// All amounts still use the exact rational expression value; no double math.
/// An expression starts as a closure tree and is replaced by generated IL
/// (<see cref="SamplingCodegen"/>) once it is hot.
/// <para><paramref name="constants"/> holds state fields that keep their initial
/// value for the whole round because nothing writes them. A read of such a
/// field is its value, and a conditional on one keeps only the branch taken.</para></summary>
internal sealed class SamplingExpressions(Func<string, int> slot, SamplingTier? tier = null, IReadOnlyDictionary<string, ExprValue>? constants = null, SamplingShapes? shapes = null)
{
    internal SamplingShapes Shapes { get; } = shapes ?? new();
    private static readonly IReadOnlyDictionary<string, ExprValue> None = new Dictionary<string, ExprValue>();
    private readonly SamplingTier _tier = tier ?? SamplingOptions.Tier;
    private readonly IReadOnlyDictionary<string, ExprValue> _constants = constants ?? None;
    // Code is generated while other workers evaluate, so lookups must tolerate a concurrent insert.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ExprValue> _numbers = new(StringComparer.Ordinal);

    internal bool TryConstant(Expression expression, out ExprValue value)
    {
        value = default;
        return expression is FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } field && _constants.TryGetValue(field.Path[0], out value);
    }

    internal bool TryConstantBool(Expression expression, out bool value)
    {
        value = TryConstant(expression, out var constant) && constant.Kind == ExprType.Boolean && constant.BoolValue;
        return value || TryConstant(expression, out constant) && constant.Kind == ExprType.Boolean;
    }

    /// <summary>The expression that is actually evaluated once conditionals on constant state are decided.</summary>
    internal Expression Reduce(Expression expression)
    {
        while (expression is IfExpr branch && TryConstantBool(branch.Condition, out var taken)) expression = taken ? branch.ThenExpr : branch.ElseExpr;
        return expression;
    }

    // An iteration variable is assigned while its body runs, so it can never be constant state.
    private int Variable(string name) => _constants.ContainsKey(name) ? throw new ConstantStateConflict(name) : slot(name);
    public void Remember(ExprValue value)
    {
        // Cache only valid integer symbols without interpreting every text
        // label as an explicit conversion. Invalid strings remain errors when
        // tonumber is actually evaluated, including in conditional branches.
        if (value.Kind == ExprType.String && value.StringValue is { Length: <= 4096 } s
            && BigInteger.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            _numbers.TryAdd(s, ExprValue.Number(integer));
        if (value.Kind == ExprType.Array)
            foreach (var item in value.ArrayValue!) Remember(item);
    }

    public Func<SamplingFrame, ExprValue> Compile(Expression expression) => Bind(expression).Entry;

    /// <summary>Binds field slots and constants now; code generation may follow later.</summary>
    public SamplingDelegate<ExprValue> Bind(Expression expression)
    {
        var interpreted = Interpret(expression);
        // A constant or a plain field read is already a single call.
        var simple = Reduce(expression) is ConstantExpr or FieldAccessExpr;
        return new(interpreted, simple ? null : () => SamplingCodegen.CompileValue(this, slot, expression), _tier);
    }

    /// <summary>A condition that must evaluate to a Boolean, as a typed predicate.</summary>
    public SamplingDelegate<bool> BindCondition(Expression expression)
    {
        var interpreted = Interpret(expression);
        return new(frame => RequiredBoolean(interpreted(frame)), Reduce(expression) is ConstantExpr or FieldAccessExpr ? null
            : () => SamplingCodegen.CompileCondition(this, slot, expression), _tier);
    }

    private Func<SamplingFrame, ExprValue> Interpret(Expression expression) => Interpret(expression, Interpret);

    /// <summary>The closure implementation of one node. <paramref name="compile"/>
    /// supplies the implementation of its operands.</summary>
    internal Func<SamplingFrame, ExprValue> Interpret(Expression expression, Func<Expression, Func<SamplingFrame, ExprValue>> compile)
    {
        switch (expression)
        {
            case ConstantExpr c:
                ExprValue constant;
                try { constant = ExactExpressionEvaluator.Evaluate(c, EvalContext.Empty); }
                catch (Exception ex) when (ex is FormatException or DivideByZeroException or ExpressionEvaluationException)
                { return _ => ExactExpressionEvaluator.Evaluate(c, EvalContext.Empty); }
                Remember(constant);
                return _ => constant;
            case FieldAccessExpr f:
                if (f.Target == "measurement") return s => f.Path.Length == 1 && s.Settlement is { } settlement ? settlement.Read(f.Path[0]) : throw new ExpressionEvaluationException("EVAL_MISSING_SETTLEMENT", "Settlement fields require completed-round measurement context.");
                if (f.Target is not (null or "state" or "board")) return _ => throw new ExpressionEvaluationException("EVAL_INVALID_TARGET", "Unknown field namespace.", f.Target);
                if (f.Path.Length == 0 || f.Path.Any(string.IsNullOrEmpty)) return _ => throw new ExpressionEvaluationException("EVAL_INVALID_PATH", "A field path must contain nonempty segments.", string.Join('.', f.Path));
                var key = f.Path[0]; var index = slot(key); var location = string.Join('.', f.Path);
                if (f.Path.Length == 1) return _constants.TryGetValue(key, out var unchanging) ? _ => unchanging : s => s.Read(index, location);
                if (f.Path.Length == 2 && int.TryParse(f.Path[1], out var itemIndex))
                    return s => s.Cells[index].IsRecord ? ReadNested(s) : s.Cells[index].IndexedField(itemIndex, key, location);
                // Records have their original CLR representation. This uncommon
                // path retains reference semantics, including located errors.
                return ReadNested;
                ExprValue ReadNested(SamplingFrame frame) => frame.Cells[index].Present
                    ? StateValues.Read(frame.Cells[index].Export(), f.Path[1..], location)
                    : throw new ExpressionEvaluationException("EVAL_MISSING_STATE", $"State field '{key}' is absent.", location);
            case IfExpr i:
                var condition = compile(i.Condition); var yes = compile(i.ThenExpr); var no = compile(i.ElseExpr);
                if (TryConstantBool(i.Condition, out var taken)) return taken ? yes : no;
                return s => Truth(condition(s)) ? yes(s) : no(s);
            case NotExpr n:
                var operand = compile(n.Expr); return s => ExprValue.Bool(!Truth(operand(s)));
            case BinaryExpr b:
                var left = compile(b.Left); var right = compile(b.Right);
                if (b.Op is BinaryOp.And or BinaryOp.Or)
                    return s =>
                    {
                        var l = Truth(left(s));
                        if (b.Op == BinaryOp.Or ? l : !l) return ExprValue.Bool(l);
                        var r = Truth(right(s));
                        return ExprValue.Bool(b.Op == BinaryOp.Or ? l || r : l && r);
                    };
                return s =>
                {
                    var l = left(s); var r = right(s);
                    if (l.Kind != ExprType.Number || r.Kind != ExprType.Number) throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", $"Operator {b.Op} requires numeric operands.");
                    return b.Op switch { BinaryOp.Add => ExprValue.Add(l, r), BinaryOp.Sub => ExprValue.Sub(l, r), BinaryOp.Mul => ExprValue.Mul(l, r), BinaryOp.Div => ExprValue.Div(l, r), _ => throw new ExpressionEvaluationException("EVAL_INVALID_OPERATOR", "Unknown binary operator.") };
                };
            case CompareExpr c:
                var cl = compile(c.Left); var cr = compile(c.Right);
                return s => ExprValue.Bool(CompareValues(c.Op, cl(s), cr(s)));
            case CallExpr c:
                var args = c.Args.Select(compile).ToArray(); var function = c.Function.ToLowerInvariant();
                return s =>
                {
                    var a = args.Length > 0 ? args[0](s) : ExprValue.Number(0);
                    var b = args.Length > 1 ? args[1](s) : ExprValue.Number(0);
                    for (var j = 2; j < args.Length; j++) args[j](s); // arguments are eager, including unused ones
                    return Call(function, a, b, args.Length);
                };
            case FoldExpr f:
                var source = slot(f.StateKey); var acc = Variable(f.AccName); var item = Variable(f.ItemName);
                var position = f.IndexName is null ? -1 : Variable(f.IndexName);
                var init = compile(f.Init); var body = compile(f.Body);
                return s =>
                {
                    var value = init(s); var array = s.Cells[source]; var length = array.RequireArrayCount(f.StateKey);
                    var savedAcc = s.Cells[acc]; var savedItem = s.Cells[item]; var savedIndex = position < 0 ? default : s.Cells[position];
                    try
                    {
                        for (var j = 0; j < length; j++)
                        {
                            s.CheckCancellation();
                            s.Cells[acc] = SamplingCell.Typed(value); s.Cells[item] = array.Item(j);
                            if (position >= 0) s.Cells[position] = SamplingCell.FromRaw(j);
                            value = body(s);
                        }
                        return value;
                    }
                    finally { s.Cells[acc] = savedAcc; s.Cells[item] = savedItem; if (position >= 0) s.Cells[position] = savedIndex; }
                };
            case MapExpr m:
                return CompileMap(m.StateKey, slot(m.StateKey), Variable(m.ItemName), m.IndexName is null ? -1 : Variable(m.IndexName), compile(m.Body), false);
            case FilterExpr f:
                return CompileMap(f.StateKey, slot(f.StateKey), Variable(f.ItemName), f.IndexName is null ? -1 : Variable(f.IndexName), compile(f.Predicate), true);
            case AggregateExpr a:
                if (a.Func is not (AggregateFunc.Sum or AggregateFunc.Product or AggregateFunc.Min or AggregateFunc.Max or AggregateFunc.Count))
                    throw new ExpressionEvaluationException("EVAL_INVALID_OPERATOR", "Unknown aggregate operator.");
                var src = slot(a.StateKey); var binding = Variable(a.ItemName);
                var predicate = a.Predicate is null ? null : compile(a.Predicate); var selector = a.ValueExpr is null ? null : compile(a.ValueExpr);
                return s =>
                {
                    var array = s.Cells[src]; var length = array.RequireArrayCount(a.StateKey); var saved = s.Cells[binding];
                    var count = 0; var total = ExprValue.Number(a.Func == AggregateFunc.Product ? 1 : 0);
                    try
                    {
                        for (var j = 0; j < length; j++)
                        {
                            s.CheckCancellation(); var cell = array.Item(j); s.Cells[binding] = cell;
                            if (predicate is not null && !Truth(predicate(s))) continue;
                            var value = selector is not null ? selector(s) : cell.Value.Kind == ExprType.String ? ParseNumber(cell.Value.StringValue!) : cell.Value;
                            if (a.Func != AggregateFunc.Count)
                            {
                                if (selector is null && value.Kind != ExprType.Number) value = ExprValue.Number(0);
                                total = NumericFunctions.Aggregate(a.Func, total, value, count == 0);
                            }
                            count++;
                        }
                        if (count == 0 && a.Func is AggregateFunc.Min or AggregateFunc.Max)
                            throw new ExpressionEvaluationException(EvalErrorCodes.EmptyMinMax, $"{a.Func} over an empty array is undefined (D1).", a.Func.ToString().ToLowerInvariant());
                        return a.Func == AggregateFunc.Count ? ExprValue.Number(count) : total;
                    }
                    finally { s.Cells[binding] = saved; }
                };
            default: throw new NotSupportedException($"Sampling plan does not support {expression.GetType().Name}.");
        }
    }

    private static Func<SamplingFrame, ExprValue> CompileMap(string key, int source, int binding, int position, Func<SamplingFrame, ExprValue> body, bool filter) => s =>
    {
        var array = s.Cells[source]; var length = array.RequireArrayCount(key); var savedItem = s.Cells[binding]; var savedIndex = position < 0 ? default : s.Cells[position];
        var values = new ExprValue[length]; var count = 0;
        try
        {
            for (var j = 0; j < length; j++)
            {
                s.CheckCancellation(); var cell = array.Item(j); s.Cells[binding] = cell;
                if (position >= 0) s.Cells[position] = SamplingCell.FromRaw(j);
                var result = body(s);
                if (!filter || Truth(result)) values[count++] = filter ? cell.FilterValue : result;
            }
            if (count != values.Length) Array.Resize(ref values, count);
            return ExprValue.Array(values);
        }
        finally { s.Cells[binding] = savedItem; if (position >= 0) s.Cells[position] = savedIndex; }
    };

    public static bool Truth(ExprValue v) => RequiredBoolean(v);
    public static bool RequiredBoolean(ExprValue v) => v.Kind == ExprType.Boolean ? v.BoolValue : throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "Boolean expression required.");
    private static ExprValue ParseNumber(string s) => ExprValue.Number(BigInteger.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0);

    internal static bool CompareValues(CompareOp op, ExprValue l, ExprValue r)
    {
        if (op is CompareOp.Eq or CompareOp.Neq && l.Kind == r.Kind && l.Kind is ExprType.Record or ExprType.Array or ExprType.Null)
            return op == CompareOp.Eq ? l.Equals(r) : !l.Equals(r);
        if (l.Kind == ExprType.Boolean && r.Kind == ExprType.Boolean)
            return op switch { CompareOp.Eq => l.BoolValue == r.BoolValue, CompareOp.Neq => l.BoolValue != r.BoolValue, _ => false };
        var cmp = l.Kind == ExprType.Number && r.Kind == ExprType.Number
            ? ExprValue.CompareNumbers(l, r)
            : string.Compare(l.Kind == ExprType.String ? l.StringValue : l.ToString(), r.Kind == ExprType.String ? r.StringValue : r.ToString(), StringComparison.Ordinal);
        return op switch { CompareOp.Eq => cmp == 0, CompareOp.Neq => cmp != 0, CompareOp.Lt => cmp < 0, CompareOp.Gt => cmp > 0, CompareOp.Lte => cmp <= 0, CompareOp.Gte => cmp >= 0, _ => false };
    }

    internal ExprValue Call(string function, ExprValue a, ExprValue b, int count) => function == "tonumber" && count == 1 ? ToNumber(a) : CollectionFunctions.Call(function, a, b, count);

    internal ExprValue ToNumber(ExprValue a)
    {
        if (a.TryGetNumericText(out var inline)) return ExprValue.Number(inline);
        return a.Kind is ExprType.String or ExprType.Symbol && _numbers.TryGetValue(a.StringValue!, out var number)
            ? number : CollectionFunctions.Call("tonumber", a, default, 1);
    }
}

/// <summary>A field assumed to keep its initial value turned out to be
/// assigned. The plan is then built without constant state.</summary>
internal sealed class ConstantStateConflict(string key) : Exception($"State field '{key}' is assigned and cannot be treated as constant.");
