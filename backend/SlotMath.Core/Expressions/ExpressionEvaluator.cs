using System.Globalization;
using System.Numerics;
using SlotMath.Core.Math;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Model;
using SlotMath.Core.Random;

namespace SlotMath.Core.Expressions;

// ═══════════════════════════════════════════════════════════════════════════
//  ExpressionEvaluator — dual-mode evaluation of level-(b) expressions
//
//  Exact mode:  amounts → ExprValue (BigInteger rationals).
//               Used by the exact interpreter path.
//
//  Sampled mode: amounts → double.
//               Used by the Monte Carlo interpreter.
//
//  Both modes are pure, total, deterministic — no I/O, no randomness,
//  no mutable state.  Identical inputs yield identical outputs.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Evaluation context carrying the runtime board and game state.
/// </summary>
public sealed class EvalContext
{
    /// <summary>The current recurrence state (nullable).</summary>
    public object? State { get; init; }

    /// <summary>Optional map of decoration keys to their numeric values for aggregation.</summary>
    public Func<string, string?, BigInteger>? DecorationParser { get; init; }

    /// <summary>For sampled mode: convert a symbol string to its numeric value.</summary>
    public Func<string, double>? SymbolToNumericValue { get; init; }

    public static EvalContext Empty { get; } = new();
}

/// <summary>
/// Evaluates expression ASTs in exact (rational) mode.
/// Pure, total, deterministic.
/// </summary>
public static class ExactExpressionEvaluator
{
    /// <summary>Evaluate to an ExprValue (exact rational).</summary>
    public static ExprValue Evaluate(Expression expr, EvalContext ctx)
    {
        return Eval(expr, ctx);
    }

    /// <summary>Evaluate and return as BigInteger (truncating rational division).</summary>
    public static BigInteger EvaluateAsInteger(Expression expr, EvalContext ctx)
    {
        var v = Eval(expr, ctx);
        return v.AsInteger();
    }

    /// <summary>Evaluate as boolean.</summary>
    public static bool EvaluateAsBool(Expression expr, EvalContext ctx)
    {
        var v = Eval(expr, ctx);
        return v.Kind == ExprType.Boolean && v.BoolValue;
    }

    /// <summary>Evaluate as a WeightSet (for Draw weight expressions).</summary>
    public static WeightSet EvaluateAsWeights(Expression expr, EvalContext ctx)
    {
        var v = Eval(expr, ctx);
        if (v.Kind == ExprType.Number)
        {
            return WeightSet.FromNumerators([v.AsInteger()]);
        }
        return WeightSet.FromNumerators([BigInteger.Zero]);
    }

    // ── Core evaluation ──────────────────────────────────────────────────

    private static ExprValue Eval(Expression expr, EvalContext ctx)
    {
        return expr switch
        {
            ConstantExpr c => EvalConstant(c),
            FieldAccessExpr f => EvalFieldAccess(f, ctx),
            BinaryExpr b => EvalBinary(b, ctx),
            CompareExpr c => EvalCompare(c, ctx),
            IfExpr i => EvalIf(i, ctx),
            AggregateExpr a => EvalAggregate(a, ctx),
            NotExpr n => EvalNot(n, ctx),
            CallExpr c => EvalCall(c, ctx),
            FoldExpr f => EvalFold(f, ctx),
            MapExpr m => EvalMap(m, ctx),
            FilterExpr fi => EvalFilter(fi, ctx),
            _ => throw new InvalidOperationException($"Unknown expression type: {expr.GetType().Name}"),
        };
    }

    private static ExprValue EvalConstant(ConstantExpr c)
    {
        return c.Kind switch
        {
            ConstantKind.Integer => ExprValue.Number(BigInteger.Parse(c.Value, CultureInfo.InvariantCulture)),
            ConstantKind.Rational =>
                ParseRational(c.Value),
            ConstantKind.Boolean => ExprValue.Bool(bool.Parse(c.Value)),
            ConstantKind.String => ExprValue.String(c.Value),
            _ => ExprValue.Number(0),
        };
    }

    private static ExprValue ParseRational(string value)
    {
        // Format: "num/den" or "num"
        var parts = value.Split('/');
        if (parts.Length == 2
            && BigInteger.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var num)
            && BigInteger.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var den))
        {
            return ExprValue.Rational(num, den);
        }
        if (BigInteger.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return ExprValue.Number(n);
        return ExprValue.Number(0);
    }

    private static ExprValue EvalFieldAccess(FieldAccessExpr f, EvalContext ctx)
    {
        // All field access reads from state (invariant 4: the engine has no
        // Board type — a "board" is a user-defined array in state S, read via
        // state["board"] + fold/map/filter/aggregate).
        if (f.Path.Length > 0)
        {
            return EvalStateField(f.Path, ctx.State);
        }

        return ExprValue.Number(0);
    }

    private static ExprValue EvalStateField(string[] path, object? state)
    {
        if (state == null) return ExprValue.Number(0);

        // Handle Dictionary<string, object?> state (used by GraphCompiler)
        if (state is IDictionary<string, object?> dict)
        {
            var key = path[0];
            if (!dict.TryGetValue(key, out var dictVal))
                return ExprValue.Number(0);

            // Nested record field access: state["cell"]["symbol"] via
            // path = ["cell", "field", ...].  A "cell" element of a board array
            // is a Dictionary (invariant 4: cells are records, not a BoardCell
            // type), so descend through dictionary-valued entries by key.
            if (path.Length >= 2 && dictVal is IDictionary<string, object?> nestedDict
                && !int.TryParse(path[1], out _))
            {
                return EvalStateField(path[1..], nestedDict);
            }

            // Array index access: state["key"][idx] via path = ["key", "idx"].
            // D1: an out-of-range index into an array is a located, deterministic
            // error — never a silent value.
            if (path.Length == 2 && int.TryParse(path[1], out var idx))
            {
                switch (dictVal)
                {
                    case string[] sarr:
                        if (idx < 0 || idx >= sarr.Length)
                            throw IndexError(key, idx, sarr.Length);
                        return ExprValue.String(sarr[idx]);
                    case object[] oarr:
                        if (idx < 0 || idx >= oarr.Length)
                            throw IndexError(key, idx, oarr.Length);
                        return oarr[idx] switch
                        {
                            string s => ExprValue.String(s),
                            BigInteger bi => ExprValue.Number(bi),
                            int i => ExprValue.Number(i),
                            bool b => ExprValue.Bool(b),
                            _ => ExprValue.Number(0),
                        };
                    default:
                        // Not an array: not an index-out-of-range (handled as a type-level miss).
                        return ExprValue.Number(0);
                }
            }

            return ToExprValue(dictVal);
        }

        var t = state.GetType();
        var prop = t.GetProperty(string.Join("", path.Select(s =>
            s.Length > 0 ? char.ToUpper(s[0]) + s[1..] : s)));

        if (prop != null)
        {
            var val = prop.GetValue(state);
            return val switch
            {
                BigInteger bi => ExprValue.Number(bi),
                int i => ExprValue.Number(i),
                long l => ExprValue.Number(l),
                double d => ExprValue.Rational(
                    new BigInteger((long)(d * 1_000_000)), 1_000_000),
                bool b => ExprValue.Bool(b),
                string s => ExprValue.String(s),
                _ => ExprValue.Number(0),
            };
        }

        // Try direct field name match (case-insensitive)
        prop = t.GetProperties()
            .FirstOrDefault(p => string.Equals(p.Name, path[0], StringComparison.OrdinalIgnoreCase));
        if (prop != null)
        {
            var val = prop.GetValue(state);
            return val switch
            {
                BigInteger bi => ExprValue.Number(bi),
                int i => ExprValue.Number(i),
                long l => ExprValue.Number(l),
                bool b => ExprValue.Bool(b),
                string s => ExprValue.String(s),
                _ => ExprValue.Number(0),
            };
        }

        return ExprValue.Number(0);
    }

    private static ExpressionEvaluationException IndexError(string key, int idx, int length) =>
        new(EvalErrorCodes.IndexOutOfRange,
            $"Index {idx} is out of range [0, {length}) for state array '{key}' (D1).",
            $"{key}[{idx}]");

    /// <summary>
    /// Convert a raw state value to an ExprValue.  Arrays (string[]/object?[]/
    /// lists, e.g. a board) become <see cref="ExprValue.Array"/> so expressions
    /// can use contains/length/append and index-aware map/fold/filter (invariant 4).
    /// </summary>
    private static ExprValue ToExprValue(object? value) => value switch
    {
        BigInteger bi => ExprValue.Number(bi),
        int i => ExprValue.Number(i),
        long l => ExprValue.Number(l),
        string s => ExprValue.String(s),
        bool b => ExprValue.Bool(b),
        ExprValue ev => ev,
        null => ExprValue.Number(0),
        string[] sarr => ExprValue.Array(Array.ConvertAll(sarr, x => ToExprValue(x))),
        object?[] oarr => ExprValue.Array(Array.ConvertAll(oarr, ToExprValue)),
        System.Collections.IEnumerable e => ExprValue.Array(e.Cast<object?>().Select(ToExprValue).ToList()),
        _ => ExprValue.Number(0),
    };

    private static ExprValue EvalBinary(BinaryExpr b, EvalContext ctx)
    {
        return b.Op switch
        {
            BinaryOp.Add => EvalArithmetic(b.Left, b.Right, ctx, "+",
                (a, bl) => ExprValue.Add(a, bl)),
            BinaryOp.Sub => EvalArithmetic(b.Left, b.Right, ctx, "-",
                (a, bl) => ExprValue.Sub(a, bl)),
            BinaryOp.Mul => EvalArithmetic(b.Left, b.Right, ctx, "*",
                (a, bl) => ExprValue.Mul(a, bl)),
            BinaryOp.Div => EvalArithmetic(b.Left, b.Right, ctx, "/",
                (a, bl) => ExprValue.Div(a, bl)),
            BinaryOp.And => EvalLogical(b.Left, b.Right, ctx, false),
            BinaryOp.Or => EvalLogical(b.Left, b.Right, ctx, true),
            _ => ExprValue.Number(0),
        };
    }

    private static ExprValue EvalArithmetic(
        Expression left, Expression right, EvalContext ctx,
        string op, Func<ExprValue, ExprValue, ExprValue> combine)
    {
        var l = Eval(left, ctx);
        var r = Eval(right, ctx);
        if (l.Kind != ExprType.Number || r.Kind != ExprType.Number)
            return ExprValue.Number(0);
        return combine(l, r);
    }

    private static ExprValue EvalLogical(
        Expression left, Expression right, EvalContext ctx, bool isOr)
    {
        var l = Eval(left, ctx);
        // Short-circuit
        if (isOr && l.Kind == ExprType.Boolean && l.BoolValue)
            return ExprValue.Bool(true);
        if (!isOr && l.Kind == ExprType.Boolean && !l.BoolValue)
            return ExprValue.Bool(false);

        var r = Eval(right, ctx);
        var lb = l.Kind == ExprType.Boolean && l.BoolValue;
        var rb = r.Kind == ExprType.Boolean && r.BoolValue;
        return ExprValue.Bool(isOr ? lb || rb : lb && rb);
    }

    private static ExprValue EvalCompare(CompareExpr c, EvalContext ctx)
    {
        var l = Eval(c.Left, ctx);
        var r = Eval(c.Right, ctx);

        if (l.Kind == ExprType.Number && r.Kind == ExprType.Number)
        {
            // Compare as rationals: a/b vs c/d  =>  a*d vs c*b
            var leftProd = l.NumberNumerator * r.NumberDenominator;
            var rightProd = r.NumberNumerator * l.NumberDenominator;
            var cmp = leftProd.CompareTo(rightProd);

            return ExprValue.Bool(c.Op switch
            {
                CompareOp.Eq => cmp == 0,
                CompareOp.Neq => cmp != 0,
                CompareOp.Lt => cmp < 0,
                CompareOp.Gt => cmp > 0,
                CompareOp.Lte => cmp <= 0,
                CompareOp.Gte => cmp >= 0,
                _ => false,
            });
        }

        if (l.Kind == ExprType.Boolean && r.Kind == ExprType.Boolean)
        {
            return ExprValue.Bool(c.Op switch
            {
                CompareOp.Eq => l.BoolValue == r.BoolValue,
                CompareOp.Neq => l.BoolValue != r.BoolValue,
                _ => false,
            });
        }

        // String comparison
        var ls = l.Kind == ExprType.String ? l.StringValue : l.ToString();
        var rs = r.Kind == ExprType.String ? r.StringValue : r.ToString();
        var strCmp = string.Compare(ls, rs, StringComparison.Ordinal);

        return ExprValue.Bool(c.Op switch
        {
            CompareOp.Eq => strCmp == 0,
            CompareOp.Neq => strCmp != 0,
            CompareOp.Lt => strCmp < 0,
            CompareOp.Gt => strCmp > 0,
            CompareOp.Lte => strCmp <= 0,
            CompareOp.Gte => strCmp >= 0,
            _ => false,
        });
    }

    private static ExprValue EvalIf(IfExpr i, EvalContext ctx)
    {
        var cond = Eval(i.Condition, ctx);
        var isTrue = cond.Kind == ExprType.Boolean && cond.BoolValue;
        return isTrue ? Eval(i.ThenExpr, ctx) : Eval(i.ElseExpr, ctx);
    }

    // ── State-array aggregation (invariant 4: a board is an array in S) ────

    private static ExprValue EvalAggregate(AggregateExpr a, EvalContext ctx)
    {
        // Aggregate over the state array named by StateKey.  Each element is
        // bound under ItemName so the optional Predicate (filter) and ValueExpr
        // (selector) can reference it — exactly like fold/map/filter.  Elements
        // may be plain symbols or cell records; ValueExpr extracts the numeric
        // value to aggregate.  No engine Board type (invariant 4).
        var arr = ExtractStateArray(a.StateKey, ctx.State);

        var values = new List<ExprValue>();
        if (arr != null)
        {
            var baseState = ctx.State as IDictionary<string, object?>
                ?? new Dictionary<string, object?>();

            foreach (var item in arr)
            {
                var iterCtx = new EvalContext
                {
                    State = new Dictionary<string, object?>(baseState) { [a.ItemName] = item },
                    DecorationParser = ctx.DecorationParser,
                    SymbolToNumericValue = ctx.SymbolToNumericValue,
                };

                if (a.Predicate != null)
                {
                    var predResult = Eval(a.Predicate, iterCtx);
                    if (predResult.Kind != ExprType.Boolean || !predResult.BoolValue)
                        continue;
                }

                // ValueExpr selects the value to aggregate; absent → the element itself.
                values.Add(a.ValueExpr != null ? Eval(a.ValueExpr, iterCtx) : ItemToValue(item));
            }
        }

        if (values.Count == 0)
        {
            // D1: empty sum/product/count return identities; empty min/max is a
            // located, deterministic error (undefined, not a silent value).
            return a.Func switch
            {
                AggregateFunc.Sum or AggregateFunc.Count => ExprValue.Number(0),
                AggregateFunc.Product => ExprValue.Number(1),
                AggregateFunc.Min or AggregateFunc.Max => throw new ExpressionEvaluationException(
                    EvalErrorCodes.EmptyMinMax,
                    $"{a.Func} over an empty array is undefined (D1).",
                    a.Func.ToString().ToLowerInvariant()),
                _ => ExprValue.Number(0),
            };
        }

        // Count is independent of element numeric value.
        if (a.Func == AggregateFunc.Count)
            return ExprValue.Number(values.Count);

        var nums = values.Select(v => v.AsInteger()).ToList();
        return a.Func switch
        {
            AggregateFunc.Sum => ExprValue.Number(nums.Aggregate(BigInteger.Zero, (x, y) => x + y)),
            AggregateFunc.Product => ExprValue.Number(nums.Aggregate(BigInteger.One, (x, y) => x * y)),
            AggregateFunc.Min => ExprValue.Number(nums.Aggregate((x, y) => x < y ? x : y)),
            AggregateFunc.Max => ExprValue.Number(nums.Aggregate((x, y) => x > y ? x : y)),
            _ => ExprValue.Number(0),
        };
    }

    /// <summary>
    /// Convert a raw state-array element into an ExprValue for aggregation.
    /// Numeric strings parse to numbers; non-numeric strings aggregate as 0
    /// (use Count for symbol matching).
    /// </summary>
    private static ExprValue ItemToValue(object? item) => item switch
    {
        BigInteger bi => ExprValue.Number(bi),
        int i => ExprValue.Number(i),
        long l => ExprValue.Number(l),
        bool b => ExprValue.Bool(b),
        ExprValue ev => ev,
        string s when BigInteger.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            => ExprValue.Number(n),
        string s => ExprValue.String(s),
        _ => ExprValue.Number(0),
    };

    // ── Not ──────────────────────────────────────────────────────────────

    private static ExprValue EvalNot(NotExpr n, EvalContext ctx)
    {
        var inner = Eval(n.Expr, ctx);
        return ExprValue.Bool(!(inner.Kind == ExprType.Boolean && inner.BoolValue));
    }

    // ── Bounded fold over a state array ─────────────────────────────────

    private static ExprValue EvalFold(FoldExpr f, EvalContext ctx)
    {
        var acc = Eval(f.Init, ctx);

        var arr = ExtractStateArray(f.StateKey, ctx.State);
        if (arr == null) return acc;

        var baseState = ctx.State as IDictionary<string, object?>
            ?? new Dictionary<string, object?>();

        var index = 0;
        foreach (var item in arr)
        {
            var iterState = new Dictionary<string, object?>(baseState)
            {
                [f.AccName] = ExprValueToStateObject(acc),
                [f.ItemName] = item,
            };
            if (f.IndexName != null) iterState[f.IndexName] = index;
            acc = Eval(f.Body, new EvalContext
            {
                State = iterState,
                DecorationParser = ctx.DecorationParser,
                SymbolToNumericValue = ctx.SymbolToNumericValue,
            });
            index++;
        }

        return acc;
    }

    private static IEnumerable<object?>? ExtractStateArray(string key, object? state)
    {
        if (state is IDictionary<string, object?> dict && dict.TryGetValue(key, out var val))
        {
            return val switch
            {
                string[] sarr => sarr.Cast<object?>(),
                object[] oarr => oarr,
                System.Collections.IEnumerable en when val is not string => en.Cast<object?>(),
                _ => null,
            };
        }
        return null;
    }

    private static object? ExprValueToStateObject(ExprValue v) => v.ToStateObject();

    // ── Bounded map over a state array ───────────────────────────────────

    private static ExprValue EvalMap(MapExpr m, EvalContext ctx)
    {
        var arr = ExtractStateArray(m.StateKey, ctx.State);
        if (arr == null) return ExprValue.Array([]);

        var baseState = ctx.State as IDictionary<string, object?> ?? new Dictionary<string, object?>();
        var result = new List<ExprValue>();

        var index = 0;
        foreach (var item in arr)
        {
            var iterState = new Dictionary<string, object?>(baseState)
            {
                [m.ItemName] = item,
            };
            if (m.IndexName != null) iterState[m.IndexName] = index;
            result.Add(Eval(m.Body, new EvalContext
            {
                State = iterState,
                DecorationParser = ctx.DecorationParser,
                SymbolToNumericValue = ctx.SymbolToNumericValue,
            }));
            index++;
        }

        return ExprValue.Array(result);
    }

    // ── Bounded filter over a state array ────────────────────────────────

    private static ExprValue EvalFilter(FilterExpr f, EvalContext ctx)
    {
        var arr = ExtractStateArray(f.StateKey, ctx.State);
        if (arr == null) return ExprValue.Array([]);

        var baseState = ctx.State as IDictionary<string, object?> ?? new Dictionary<string, object?>();
        var result = new List<ExprValue>();

        var index = 0;
        foreach (var item in arr)
        {
            var iterState = new Dictionary<string, object?>(baseState)
            {
                [f.ItemName] = item,
            };
            if (f.IndexName != null) iterState[f.IndexName] = index;
            var pred = Eval(f.Predicate, new EvalContext
            {
                State = iterState,
                DecorationParser = ctx.DecorationParser,
                SymbolToNumericValue = ctx.SymbolToNumericValue,
            });
            if (pred.Kind == ExprType.Boolean && pred.BoolValue)
                result.Add(ObjectToExprValue(item));
            index++;
        }

        return ExprValue.Array(result);
    }

    private static ExprValue ObjectToExprValue(object? item) => item switch
    {
        string s => ExprValue.String(s),
        BigInteger bi => ExprValue.Number(bi),
        int i => ExprValue.Number(i),
        long l => ExprValue.Number(l),
        bool b => ExprValue.Bool(b),
        ExprValue ev => ev,
        null => ExprValue.Number(0),
        _ => ExprValue.Number(0),
    };

    // ── Built-in function calls ──────────────────────────────────────────

    private static ExprValue EvalCall(CallExpr c, EvalContext ctx)
    {
        var args = c.Args.Select(a => Eval(a, ctx)).ToArray();

        return c.Function.ToLowerInvariant() switch
        {
            "abs" => args.Length > 0
                ? ExprValue.Number(BigInteger.Abs(args[0].AsInteger()))
                : ExprValue.Number(0),

            "min" => args.Length >= 2
                ? ExprValue.Number(BigInteger.Min(args[0].AsInteger(), args[1].AsInteger()))
                : ExprValue.Number(0),

            "max" => args.Length >= 2
                ? ExprValue.Number(BigInteger.Max(args[0].AsInteger(), args[1].AsInteger()))
                : ExprValue.Number(0),

            "floor" => args.Length > 0
                ? ExprValue.Number(args[0].AsInteger()) // BigInteger division truncates toward zero
                : ExprValue.Number(0),

            "ceil" => args.Length > 0
                ? Ceil(args[0])
                : ExprValue.Number(0),

            "round" => args.Length > 0
                ? Round(args[0])
                : ExprValue.Number(0),

            "toNumber" => args.Length > 0 && args[0].Kind == ExprType.String
                ? ParseNumber(args[0].StringValue!)
                : ExprValue.Number(0),

            "toString" => args.Length > 0
                ? ExprValue.String(args[0].AsInteger().ToString())
                : ExprValue.String("0"),

            // length(arr) → element count; length(str) → character count.
            "length" => args.Length > 0
                ? ExprValue.Number(args[0].Kind == ExprType.Array
                    ? args[0].ArrayValue!.Count
                    : args[0].StringValue?.Length ?? 0)
                : ExprValue.Number(0),

            // contains(arr, x) → array membership; contains(str, sub) → substring.
            "contains" => args.Length >= 2
                ? ExprValue.Bool(args[0].Kind == ExprType.Array
                    ? args[0].ArrayValue!.Any(e => e.Equals(args[1]))
                    : args[0].StringValue?.Contains(args[1].StringValue ?? "", StringComparison.Ordinal) ?? false)
                : ExprValue.Bool(false),

            // append(arr, x) → a new array with x appended (bounded; for fold accumulation).
            "append" when args.Length >= 2 && args[0].Kind == ExprType.Array =>
                ExprValue.Array([.. args[0].ArrayValue!, args[1]]),

            _ => ExprValue.Number(0),
        };
    }

    private static ExprValue Ceil(ExprValue v)
    {
        var num = v.NumberNumerator;
        var den = v.NumberDenominator;
        // Ceil(a/b) = (a + b - 1) / b for positive; for negative just a/b truncates to ceil.
        if (num >= 0)
            return ExprValue.Number((num + den - 1) / den);
        return ExprValue.Number(num / den);
    }

    private static ExprValue Round(ExprValue v)
    {
        var num = v.NumberNumerator;
        var den = v.NumberDenominator;
        // Round half-up: (a + b/2) / b
        var halfDen = den / 2;
        if (num >= 0)
            return ExprValue.Number((num + halfDen) / den);
        return ExprValue.Number((num - halfDen) / den);
    }

    private static ExprValue ParseNumber(string s)
    {
        if (BigInteger.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return ExprValue.Number(n);
        return ExprValue.Number(0);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  SampledExpressionEvaluator — double-based Monte Carlo evaluator
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Evaluates expression ASTs in sampled (double) mode for the Monte Carlo path.
/// Pure, total, deterministic (no randomness or I/O).
/// </summary>
public static class SampledExpressionEvaluator
{
    /// <summary>Evaluate to a double (number expressions) or the natural value.</summary>
    public static double Evaluate(Expression expr, EvalContext ctx)
    {
        var v = ExactExpressionEvaluator.Evaluate(expr, ctx);
        return v.Kind switch
        {
            ExprType.Number => v.AsDouble(),
            ExprType.Boolean => v.BoolValue ? 1.0 : 0.0,
            _ => 0.0,
        };
    }

    /// <summary>Evaluate as boolean.</summary>
    public static bool EvaluateAsBool(Expression expr, EvalContext ctx)
    {
        var v = ExactExpressionEvaluator.Evaluate(expr, ctx);
        return v.Kind == ExprType.Boolean && v.BoolValue;
    }

    /// <summary>Evaluate and return as BigInteger (via the exact evaluator).</summary>
    public static BigInteger EvaluateAsInteger(Expression expr, EvalContext ctx)
    {
        return ExactExpressionEvaluator.EvaluateAsInteger(expr, ctx);
    }

    /// <summary>Evaluate as WeightSet.</summary>
    public static WeightSet EvaluateAsWeights(Expression expr, EvalContext ctx)
    {
        return ExactExpressionEvaluator.EvaluateAsWeights(expr, ctx);
    }
}
