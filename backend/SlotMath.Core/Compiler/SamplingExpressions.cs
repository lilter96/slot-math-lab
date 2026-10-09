using System.Globalization;
using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler;

/// <summary>Specializes AST dispatch, field lookup, and constants once per graph.
/// All amounts still use the exact rational expression value; no double math.</summary>
internal sealed class SamplingExpressions(Func<string, int> slot)
{
    private readonly Dictionary<string, ExprValue> _numbers = new(StringComparer.Ordinal);
    public void Remember(ExprValue value)
    {
        if (value.Kind == ExprType.String && value.StringValue is { } s)
            _numbers.TryAdd(s, ParseNumber(s));
        if (value.Kind == ExprType.Array)
            foreach (var item in value.ArrayValue!) Remember(item);
    }

    public Func<SamplingFrame, ExprValue> Compile(Expression expression)
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
                if (f.Path.Length == 0) return _ => ExprValue.Number(0);
                var key = f.Path[0]; var index = slot(key); var location = string.Join('.', f.Path);
                if (f.Path.Length == 1) return s => s.Read(index, location);
                if (f.Path.Length == 2 && int.TryParse(f.Path[1], out var itemIndex))
                    return s => { s.Read(index, location); return s.Cells[index].IndexedField(itemIndex, key); };
                // Records have their original CLR representation. This uncommon
                // path retains reference semantics, including located errors.
                return s => ExactExpressionEvaluator.Evaluate(f, new EvalContext { State = new Dictionary<string, object?> { [key] = s.Cells[index].Present ? s.Cells[index].Export() : throw new ExpressionEvaluationException("EVAL_MISSING_STATE", $"State field '{key}' is absent.", location) } });
            case IfExpr i:
                var condition = Compile(i.Condition); var yes = Compile(i.ThenExpr); var no = Compile(i.ElseExpr);
                return s => Truth(condition(s)) ? yes(s) : no(s);
            case NotExpr n:
                var operand = Compile(n.Expr); return s => ExprValue.Bool(!Truth(operand(s)));
            case BinaryExpr b:
                var left = Compile(b.Left); var right = Compile(b.Right);
                if (b.Op is BinaryOp.And or BinaryOp.Or)
                    return s =>
                    {
                        var l = left(s);
                        if (l.Kind == ExprType.Boolean && (b.Op == BinaryOp.Or ? l.BoolValue : !l.BoolValue)) return l;
                        var r = right(s);
                        return ExprValue.Bool(b.Op == BinaryOp.Or ? Truth(l) || Truth(r) : Truth(l) && Truth(r));
                    };
                return s =>
                {
                    var l = left(s); var r = right(s);
                    if (l.Kind != ExprType.Number || r.Kind != ExprType.Number) return ExprValue.Number(0);
                    return b.Op switch { BinaryOp.Add => ExprValue.Add(l, r), BinaryOp.Sub => ExprValue.Sub(l, r), BinaryOp.Mul => ExprValue.Mul(l, r), BinaryOp.Div => ExprValue.Div(l, r), _ => ExprValue.Number(0) };
                };
            case CompareExpr c:
                var cl = Compile(c.Left); var cr = Compile(c.Right);
                return s => Compare(c.Op, cl(s), cr(s));
            case CallExpr c:
                var args = c.Args.Select(Compile).ToArray(); var function = c.Function.ToLowerInvariant();
                return s =>
                {
                    var a = args.Length > 0 ? args[0](s) : ExprValue.Number(0);
                    var b = args.Length > 1 ? args[1](s) : ExprValue.Number(0);
                    for (var j = 2; j < args.Length; j++) args[j](s); // arguments are eager, including unused ones
                    return Call(function, a, b, args.Length);
                };
            case FoldExpr f:
                var source = slot(f.StateKey); var acc = slot(f.AccName); var item = slot(f.ItemName);
                var position = f.IndexName is null ? -1 : slot(f.IndexName);
                var init = Compile(f.Init); var body = Compile(f.Body);
                return s =>
                {
                    var value = init(s); var array = s.Cells[source];
                    var savedAcc = s.Cells[acc]; var savedItem = s.Cells[item]; var savedIndex = position < 0 ? default : s.Cells[position];
                    try
                    {
                        for (var j = 0; j < array.ArrayCount; j++)
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
                return CompileMap(slot(m.StateKey), slot(m.ItemName), m.IndexName is null ? -1 : slot(m.IndexName), Compile(m.Body), false);
            case FilterExpr f:
                return CompileMap(slot(f.StateKey), slot(f.ItemName), f.IndexName is null ? -1 : slot(f.IndexName), Compile(f.Predicate), true);
            case AggregateExpr a:
                var src = slot(a.StateKey); var binding = slot(a.ItemName);
                var predicate = a.Predicate is null ? null : Compile(a.Predicate); var selector = a.ValueExpr is null ? null : Compile(a.ValueExpr);
                return s =>
                {
                    var array = s.Cells[src]; var saved = s.Cells[binding];
                    var count = 0; var total = a.Func == AggregateFunc.Product ? BigInteger.One : BigInteger.Zero;
                    try
                    {
                        for (var j = 0; j < array.ArrayCount; j++)
                        {
                            s.CheckCancellation(); var cell = array.Item(j); s.Cells[binding] = cell;
                            if (predicate is not null && !Truth(predicate(s))) continue;
                            var value = selector is not null ? selector(s) : cell.Value.Kind == ExprType.String ? ParseNumber(cell.Value.StringValue!) : cell.Value;
                            var n = value.AsInteger();
                            total = a.Func switch
                            {
                                AggregateFunc.Sum => total + n, AggregateFunc.Product => total * n,
                                AggregateFunc.Min => count == 0 ? n : BigInteger.Min(total, n),
                                AggregateFunc.Max => count == 0 ? n : BigInteger.Max(total, n), _ => total,
                            };
                            count++;
                        }
                        if (count == 0 && a.Func is AggregateFunc.Min or AggregateFunc.Max)
                            throw new ExpressionEvaluationException(EvalErrorCodes.EmptyMinMax, $"{a.Func} over an empty array is undefined (D1).", a.Func.ToString().ToLowerInvariant());
                        return ExprValue.Number(a.Func == AggregateFunc.Count ? count : total);
                    }
                    finally { s.Cells[binding] = saved; }
                };
            default: throw new NotSupportedException($"Sampling plan does not support {expression.GetType().Name}.");
        }
    }

    private static Func<SamplingFrame, ExprValue> CompileMap(int source, int binding, int position, Func<SamplingFrame, ExprValue> body, bool filter) => s =>
    {
        var array = s.Cells[source]; var savedItem = s.Cells[binding]; var savedIndex = position < 0 ? default : s.Cells[position];
        var values = new ExprValue[array.ArrayCount]; var count = 0;
        try
        {
            for (var j = 0; j < array.ArrayCount; j++)
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

    public static bool Truth(ExprValue v) => v.Kind == ExprType.Boolean && v.BoolValue;
    public static bool RequiredBoolean(ExprValue v) => v.Kind == ExprType.Boolean ? v.BoolValue : throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "Boolean expression required.");
    private static ExprValue ParseNumber(string s) => ExprValue.Number(BigInteger.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0);

    private static ExprValue Compare(CompareOp op, ExprValue l, ExprValue r)
    {
        if (l.Kind == ExprType.Boolean && r.Kind == ExprType.Boolean)
            return ExprValue.Bool(op switch { CompareOp.Eq => l.BoolValue == r.BoolValue, CompareOp.Neq => l.BoolValue != r.BoolValue, _ => false });
        var cmp = l.Kind == ExprType.Number && r.Kind == ExprType.Number
            ? (l.NumberNumerator * r.NumberDenominator).CompareTo(r.NumberNumerator * l.NumberDenominator)
            : string.Compare(l.Kind == ExprType.String ? l.StringValue : l.ToString(), r.Kind == ExprType.String ? r.StringValue : r.ToString(), StringComparison.Ordinal);
        return ExprValue.Bool(op switch { CompareOp.Eq => cmp == 0, CompareOp.Neq => cmp != 0, CompareOp.Lt => cmp < 0, CompareOp.Gt => cmp > 0, CompareOp.Lte => cmp <= 0, CompareOp.Gte => cmp >= 0, _ => false });
    }

    private ExprValue Call(string function, ExprValue a, ExprValue b, int count)
    {
        switch (function)
        {
            case "abs": return ExprValue.Number(count > 0 ? BigInteger.Abs(a.AsInteger()) : 0);
            case "min": return ExprValue.Number(count >= 2 ? BigInteger.Min(a.AsInteger(), b.AsInteger()) : 0);
            case "max": return ExprValue.Number(count >= 2 ? BigInteger.Max(a.AsInteger(), b.AsInteger()) : 0);
            case "floor": return ExprValue.Number(count > 0 ? a.AsInteger() : 0);
            case "ceil": return count == 0 ? ExprValue.Number(0) : ExprValue.Number(a.NumberNumerator.Sign >= 0 ? (a.NumberNumerator + a.NumberDenominator - 1) / a.NumberDenominator : a.NumberNumerator / a.NumberDenominator);
            case "round": return count == 0 ? ExprValue.Number(0) : ExprValue.Number((a.NumberNumerator + (a.NumberNumerator.Sign >= 0 ? a.NumberDenominator / 2 : -a.NumberDenominator / 2)) / a.NumberDenominator);
            case "tonumber": return count > 0 && a.Kind == ExprType.String ? _numbers.TryGetValue(a.StringValue!, out var n) ? n : ParseNumber(a.StringValue!) : ExprValue.Number(0);
            case "tostring": return ExprValue.String(count > 0 ? a.AsInteger().ToString() : "0");
            case "length": return ExprValue.Number(count == 0 ? 0 : a.Kind == ExprType.Array ? a.ArrayValue!.Count : a.StringValue?.Length ?? 0);
            case "contains":
                if (count < 2) return ExprValue.Bool(false);
                if (a.Kind != ExprType.Array) return ExprValue.Bool(a.StringValue?.Contains(b.StringValue ?? "", StringComparison.Ordinal) ?? false);
                foreach (var value in a.ArrayValue!) if (value.Equals(b)) return ExprValue.Bool(true);
                return ExprValue.Bool(false);
            case "append" when count >= 2 && a.Kind == ExprType.Array:
                var values = new ExprValue[a.ArrayValue!.Count + 1];
                for (var j = 0; j < values.Length - 1; j++) values[j] = a.ArrayValue[j];
                values[^1] = b; return ExprValue.Array(values);
            case "index" when count >= 2 && a.Kind == ExprType.Array:
                var index = (int)b.AsInteger();
                if (index < 0 || index >= a.ArrayValue!.Count)
                    throw new ExpressionEvaluationException(EvalErrorCodes.IndexOutOfRange, $"index({index}) is out of range [0, {a.ArrayValue!.Count}) (D1).", $"index[{index}]");
                return a.ArrayValue![index];
            default: return ExprValue.Number(0);
        }
    }
}
