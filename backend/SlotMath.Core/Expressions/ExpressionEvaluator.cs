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
    /// <summary>The current board (nullable — may not be available for weight expressions).</summary>
    public Board? Board { get; init; }

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
        // Check if this is a state field access.
        if (f.Target == "state" && f.Path.Length > 0)
        {
            return EvalStateField(f.Path, ctx.State);
        }

        // Board field access.
        if (ctx.Board == null)
            return ExprValue.Number(0);

        var field = f.Path[0].ToLowerInvariant();

        // Board-level fields.
        if (field == "rows") return ExprValue.Number(ctx.Board.Rows);
        if (field == "cols") return ExprValue.Number(ctx.Board.Cols);

        // Cell-level fields — access the (0,0) cell if the board is 1x1
        // (used inside aggregation predicates).
        if (ctx.Board.Rows == 1 && ctx.Board.Cols == 1)
        {
            return EvalCellField(ctx.Board[0, 0], field, f.Path);
        }

        return ExprValue.Number(0);
    }

    /// <summary>Evaluate a field access on a single cell (for aggregation predicates).</summary>
    private static ExprValue EvalCellField(BoardCell cell, string field, string[] path)
    {
        return field switch
        {
            "symbol" or "symbols" =>
                cell.Symbols is { Length: > 0 }
                    ? ExprValue.String(cell.Symbols[0])
                    : ExprValue.String(""),

            "islocked" or "is_locked" =>
                ExprValue.Bool(cell.IsLocked),

            "isempty" or "is_empty" =>
                ExprValue.Bool(cell.IsEmpty),

            // Decoration access: field is the decoration key.
            _ => EvalDecorationValue(cell, field),
        };
    }

    /// <summary>Evaluate a decoration value from a cell.</summary>
    private static ExprValue EvalDecorationValue(BoardCell cell, string key)
    {
        var val = cell.GetDecoration(key);
        if (val is null)
            return ExprValue.String("");

        // Try numeric parsing first.
        if (BigInteger.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return ExprValue.Number(n);

        return ExprValue.String(val);
    }

    private static ExprValue EvalStateField(string[] path, object? state)
    {
        if (state == null) return ExprValue.Number(0);

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

    // ── Board aggregation ────────────────────────────────────────────────

    private static ExprValue EvalAggregate(AggregateExpr a, EvalContext ctx)
    {
        var board = ctx.Board;
        if (board == null)
            return ExprValue.Number(0);

        // Collect cell values that match the predicate.
        var values = new List<BigInteger>();

        foreach (var (row, col, cell) in board.AllCells())
        {
            if (cell.IsEmpty && a.Func != AggregateFunc.Count)
                continue;

            // Evaluate predicate if present.
            if (a.Predicate != null)
            {
                var cellCtx = new EvalContext
                {
                    Board = CreateCellBoard(cell),
                    State = ctx.State,
                    DecorationParser = ctx.DecorationParser,
                };
                var predResult = Eval(a.Predicate, cellCtx);
                if (predResult.Kind != ExprType.Boolean || !predResult.BoolValue)
                    continue;
            }

            // Extract value from the cell based on Target.
            var val = ExtractCellValue(cell, a.Target, ctx);
            values.Add(val);
        }

        if (values.Count == 0)
        {
            return a.Func == AggregateFunc.Product ? ExprValue.Number(1) : ExprValue.Number(0);
        }

        return a.Func switch
        {
            AggregateFunc.Sum => ExprValue.Number(values.Aggregate(BigInteger.Zero, (a, b) => a + b)),
            AggregateFunc.Product => ExprValue.Number(values.Aggregate(BigInteger.One, (a, b) => a * b)),
            AggregateFunc.Count => ExprValue.Number(values.Count),
            AggregateFunc.Min => ExprValue.Number(values.Aggregate((a, b) => a < b ? a : b)),
            AggregateFunc.Max => ExprValue.Number(values.Aggregate((a, b) => a > b ? a : b)),
            _ => ExprValue.Number(0),
        };
    }

    /// <summary>
    /// Create a 1x1 board with a single cell for evaluating predicates.
    /// Fields "symbol", "isLocked", "isEmpty", and decoration keys are accessible.
    /// </summary>
    private static Board CreateCellBoard(BoardCell cell)
    {
        var board = new Board(1, 1);
        board = board.SetCell(0, 0, cell);
        return board;
    }

    /// <summary>
    /// Extract a numeric value from a cell based on the target field.
    /// </summary>
    private static BigInteger ExtractCellValue(BoardCell cell, string target, EvalContext ctx)
    {
        return target.ToLowerInvariant() switch
        {
            "symbol" or "symbols" =>
                // Try to parse symbol as number; fall back to decoration value.
                TryParseSymbolValue(cell.Symbols, ctx),

            "isloclocked" or "is_locked" =>
                cell.IsLocked ? BigInteger.One : BigInteger.Zero,

            "isempty" or "is_empty" =>
                cell.IsEmpty ? BigInteger.One : BigInteger.Zero,

            // Try decoration key.
            _ =>
                TryParseDecoration(cell, target, ctx),
        };
    }

    private static BigInteger TryParseSymbolValue(string[]? symbols, EvalContext ctx)
    {
        if (symbols is { Length: > 0 }
            && BigInteger.TryParse(symbols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        // Try decoration "multiplier" or "value".
        return BigInteger.Zero;
    }

    private static BigInteger TryParseDecoration(BoardCell cell, string key, EvalContext ctx)
    {
        var dec = cell.GetDecoration(key);
        if (dec != null)
        {
            if (BigInteger.TryParse(dec, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return n;
            if (ctx.DecorationParser != null)
                return ctx.DecorationParser(key, dec);
        }
        return BigInteger.Zero;
    }

    // ── Not ──────────────────────────────────────────────────────────────

    private static ExprValue EvalNot(NotExpr n, EvalContext ctx)
    {
        var inner = Eval(n.Expr, ctx);
        return ExprValue.Bool(!(inner.Kind == ExprType.Boolean && inner.BoolValue));
    }

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

            "length" => args.Length > 0
                ? ExprValue.Number(args[0].StringValue?.Length ?? 0)
                : ExprValue.Number(0),

            "contains" => args.Length >= 2
                ? ExprValue.Bool(args[0].StringValue?.Contains(args[1].StringValue ?? "", StringComparison.Ordinal) ?? false)
                : ExprValue.Bool(false),

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
