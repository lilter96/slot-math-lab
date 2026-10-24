using System.Globalization;
using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler;

internal sealed class PrecomputationDeclined(string reason) : Exception(reason);

/// <summary>Bounds temporary arithmetic and container work before enumerating a domain.</summary>
internal sealed class PrecomputationAnalysis
{
    internal const int NumericBits = 4096;
    internal const int MaxElements = 4096;
    internal const int MaxText = 4096;
    private readonly Func<string, ExprValue?> _constant;
    private readonly Dictionary<string, Bound> _locals;
    private readonly Dictionary<string, Bound> _state;
    private int _visits;

    internal PrecomputationAnalysis(Func<string, ExprValue?> constant, IReadOnlyDictionary<string, Bound> state,
        IReadOnlyDictionary<string, Bound> locals)
    {
        _constant = constant;
        _state = new Dictionary<string, Bound>(state, StringComparer.Ordinal);
        _locals = new Dictionary<string, Bound>(locals, StringComparer.Ordinal);
    }

    internal Dictionary<string, ExprValue> Captured { get; } = new(StringComparer.Ordinal);
    internal long Work { get; private set; }

    internal long CapturedBytes { get; private set; }

    internal static Bound Scalar(ExprValue value)
    {
        if (value.Kind == ExprType.Number)
        {
            long n = value.NumberNumerator.GetBitLength() + 1, d = value.NumberDenominator.GetBitLength() + 1;
            if (n > NumericBits || d > NumericBits)
            {
                throw new PrecomputationDeclined("numeric-size");
            }

            return new Bound(ExprType.Number, n, d);
        }

        if (value.Kind is ExprType.String or ExprType.Symbol)
        {
            if (value.StringValue!.Length > MaxText)
            {
                throw new PrecomputationDeclined("text-size");
            }

            return new Bound(value.Kind, Text: value.StringValue.Length);
        }

        if (value.Kind == ExprType.Boolean)
        {
            return new Bound(ExprType.Boolean);
        }

        throw new PrecomputationDeclined("domain-type");
    }

    internal static Bound Join(Bound a, Bound b)
    {
        var memo = new Dictionary<Bound, Dictionary<Bound, Bound>>(ReferenceEqualityComparer.Instance);
        var visits = 0;

        Bound Merge(Bound left, Bound right, int depth)
        {
            if (ReferenceEquals(left, right))
            {
                return left;
            }

            if (++visits > 65536 || depth > 32)
            {
                throw new PrecomputationDeclined("bound-join-size");
            }

            if (left.Kind != right.Kind)
            {
                throw new PrecomputationDeclined("incompatible-bounds");
            }

            if (memo.TryGetValue(left, out Dictionary<Bound, Bound>? pairs) &&
                pairs.TryGetValue(right, out Bound? known))
            {
                return known;
            }

            Bound result;
            if (left.Kind == ExprType.Array)
            {
                result = left with
                {
                    Count = System.Math.Max(left.Count, right.Count),
                    Element = left.Element is null ? right.Element :
                    right.Element is null ? left.Element : Merge(left.Element, right.Element, depth + 1)
                };
            }
            else if (left.Kind == ExprType.Record)
            {
                result = left with
                {
                    Fields = left.Fields!.Keys.Concat(right.Fields!.Keys).Distinct(StringComparer.Ordinal).ToDictionary(
                        key => key,
                        key => left.Fields.TryGetValue(key, out Bound? a) && right.Fields.TryGetValue(key, out Bound? b)
                            ? Merge(a, b, depth + 1)
                            : left.Fields.GetValueOrDefault(key) ?? right.Fields[key], StringComparer.Ordinal)
                };
            }
            else
            {
                result = left with
                {
                    Numerator = System.Math.Max(left.Numerator, right.Numerator),
                    Denominator = System.Math.Max(left.Denominator, right.Denominator),
                    Text = System.Math.Max(left.Text, right.Text)
                };
            }

            if (pairs is null)
            {
                memo[left] = pairs = new Dictionary<Bound, Bound>(ReferenceEqualityComparer.Instance);
            }

            pairs[right] = result;
            return result;
        }

        return Merge(a, b, 0);
    }

    internal static (ExprValue Value, Bound Bound, long Bytes) Snapshot(ExprValue value, ref int nodes, int depth = 0)
    {
        if (++nodes > 65536 || depth > 32)
        {
            throw new PrecomputationDeclined("value-size");
        }

        if (value.Kind is ExprType.Number or ExprType.Boolean or ExprType.String or ExprType.Symbol)
        {
            Bound bound = Scalar(value);
            var bytes = value.Kind == ExprType.Number
                ? value.IsInlineInteger
                    ? 0
                    : 64 + value.NumberNumerator.GetByteCount() + value.NumberDenominator.GetByteCount()
                : value.Kind is ExprType.String or ExprType.Symbol
                    ? 24 + 2L * value.StringValue!.Length
                    : 0;
            return (value, bound, bytes);
        }

        if (value.Kind == ExprType.Array)
        {
            if (value.ItemsArray is not { Length: <= MaxElements } items)
            {
                throw new PrecomputationDeclined("observable-or-large-array");
            }

            var copy = new ExprValue[items.Length];
            Bound? element = null;
            var bytes = 40L + 16L * items.Length;
            for (var i = 0; i < items.Length; i++)
            {
                (ExprValue Value, Bound Bound, long Bytes) child = Snapshot(items[i], ref nodes, depth + 1);
                copy[i] = child.Value;
                bytes += child.Bytes;
                element = element is null ? child.Bound : Join(element, child.Bound);
            }

            return (ExprValue.Array(copy), new Bound(ExprType.Array, Count: items.Length, Element: element), bytes);
        }

        if (value.Kind == ExprType.Record)
        {
            if (value.RecordValue!.Count > 64)
            {
                throw new PrecomputationDeclined("record-size");
            }

            var fields = new Dictionary<string, ExprValue>(StringComparer.Ordinal);
            var bounds = new Dictionary<string, Bound>(StringComparer.Ordinal);
            long bytes = 128;
            foreach (KeyValuePair<string, ExprValue> pair in value.RecordValue)
            {
                (ExprValue Value, Bound Bound, long Bytes) child = Snapshot(pair.Value, ref nodes, depth + 1);
                fields[pair.Key] = child.Value;
                bounds[pair.Key] = child.Bound;
                bytes += child.Bytes + 128 + pair.Key.Length * 2L;
            }

            return (ExprValue.Record(fields), new Bound(ExprType.Record, Fields: bounds), bytes);
        }

        throw new PrecomputationDeclined("unreadable-value");
    }

    internal Bound Analyze(Expression expression, int depth = 0)
    {
        if (++_visits > 10000 || depth > 128)
        {
            throw new PrecomputationDeclined("analysis-work");
        }

        Work++;

        Bound Visit(Expression child)
        {
            return Analyze(child, depth + 1);
        }

        Bound Project(Bound value, string[] path)
        {
            foreach (var part in path)
            {
                if (value.Kind == ExprType.Record && value.Fields!.TryGetValue(part, out Bound? field))
                {
                    value = field;
                }
                else if (value.Kind == ExprType.Array &&
                         BigInteger.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
                         value.Element is { } item)
                {
                    value = item;
                }
                else
                {
                    throw new PrecomputationDeclined("unknown-projection");
                }
            }

            return value;
        }

        switch (expression)
        {
            case ConstantExpr literal:
                if (literal.Value.Length > MaxText || (literal.Kind is ConstantKind.Integer or ConstantKind.Rational &&
                                                       literal.Value.Length * 4L > NumericBits))
                {
                    throw new PrecomputationDeclined("literal-size");
                }

                return Scalar(ExactExpressionEvaluator.Evaluate(literal, EvalContext.Empty));
            case FieldAccessExpr field:
                if (field.Target is not (null or "state" or "board") || field.Path.Length == 0)
                {
                    throw new PrecomputationDeclined("context-read");
                }

                if (!_state.TryGetValue(field.Path[0], out Bound? state))
                {
                    ExprValue value = _constant(field.Path[0]) ?? throw new PrecomputationDeclined("mutable-parameter");
                    var nodes = 0;
                    (ExprValue Value, Bound Bound, long Bytes) snapshot = Snapshot(value, ref nodes);
                    Captured[field.Path[0]] = snapshot.Value;
                    CapturedBytes += snapshot.Bytes;
                    state = snapshot.Bound;
                    _state[field.Path[0]] = state;
                    Work += nodes;
                }

                return Project(state, field.Path.Skip(1).ToArray());
            case LocalExpr local:
                return _locals.TryGetValue(local.Name, out Bound? bound)
                    ? Project(bound, local.Path)
                    : throw new PrecomputationDeclined("external-local");
            case LetExpr let:
            {
                Bound value = Visit(let.Value);
                var had = _locals.TryGetValue(let.Name, out Bound? old);
                _locals[let.Name] = value;
                try
                {
                    return Visit(let.Body);
                }
                finally
                {
                    if (had)
                    {
                        _locals[let.Name] = old!;
                    }
                    else
                    {
                        _locals.Remove(let.Name);
                    }
                }
            }
            case IfExpr branch:
                Visit(branch.Condition);
                return Join(Visit(branch.ThenExpr), Visit(branch.ElseExpr));
            case NotExpr not:
                Visit(not.Expr);
                return new Bound(ExprType.Boolean);
            case CompareExpr compare:
            {
                Bound a = Visit(compare.Left), b = Visit(compare.Right);
                if (a.Kind == ExprType.Number && b.Kind == ExprType.Number)
                {
                    CheckNumbers(a.Numerator + b.Denominator + 1, b.Numerator + a.Denominator + 1);
                }

                Work += a.Kind is ExprType.Array or ExprType.Record || b.Kind is ExprType.Array or ExprType.Record
                    ? WalkCost(a) + WalkCost(b)
                    : System.Math.Max(a.Text, b.Text);
                return new Bound(ExprType.Boolean);
            }
            case BinaryExpr binary:
            {
                Bound a = Visit(binary.Left), b = Visit(binary.Right);
                if (binary.Op is BinaryOp.And or BinaryOp.Or)
                {
                    return new Bound(ExprType.Boolean);
                }

                if (a.Kind != ExprType.Number || b.Kind != ExprType.Number)
                {
                    throw new PrecomputationDeclined("arithmetic-type");
                }

                long n, d;
                if (binary.Op is BinaryOp.Add or BinaryOp.Sub)
                {
                    n = System.Math.Max(a.Numerator + b.Denominator, b.Numerator + a.Denominator) + 1;
                    d = a.Denominator + b.Denominator;
                }
                else if (binary.Op == BinaryOp.Mul)
                {
                    n = a.Numerator + b.Numerator;
                    d = a.Denominator + b.Denominator;
                }
                else if (binary.Op == BinaryOp.Div)
                {
                    n = a.Numerator + b.Denominator;
                    d = a.Denominator + b.Numerator;
                }
                else
                {
                    throw new PrecomputationDeclined("operator");
                }

                CheckNumbers(n, d);
                return new Bound(ExprType.Number, n, d);
            }
            case BitSetExpr set:
                foreach (Expression item in set.Args)
                {
                    Visit(item);
                }

                Work += 4;
                return new Bound(ExprType.Number, 65, 2);
            case ProjectExpr project: return Project(Visit(project.Value), project.Path);
            case RecordExpr record:
                if (record.Fields.Length > 64)
                {
                    throw new PrecomputationDeclined("record-size");
                }

                return new Bound(ExprType.Record,
                    Fields: record.Fields.ToDictionary(field => field.Name, field => Visit(field.Value),
                        StringComparer.Ordinal));
            case TupleExpr tuple:
                if (tuple.Items.Length > 64)
                {
                    throw new PrecomputationDeclined("tuple-size");
                }

                return new Bound(ExprType.Record,
                    Fields: tuple.Items.Select((value, index) => (value, index)).ToDictionary(
                        pair => pair.index.ToString(CultureInfo.InvariantCulture), pair => Visit(pair.value),
                        StringComparer.Ordinal));
            case CallExpr call:
            {
                Bound[] args = call.Args.Select(Visit).ToArray();
                var function = call.Function.ToLowerInvariant();
                if (args.Length == 1 && function is "abs" or "floor" or "ceil" or "round")
                {
                    CheckNumbers(args[0].Numerator + 1, args[0].Denominator);
                    return args[0] with
                    {
                        Numerator = args[0].Numerator + 1
                    };
                }

                if (args.Length == 2 && function is "min" or "max")
                {
                    CheckNumbers(args[0].Numerator + args[1].Denominator, args[1].Numerator + args[0].Denominator);
                    return Join(args[0], args[1]);
                }

                if (args.Length == 1 && function == "tonumber")
                {
                    var bits = args[0].Text * 4L + 1;
                    CheckNumbers(bits, 2);
                    Work += args[0].Text;
                    return new Bound(ExprType.Number, bits, 2);
                }

                if (args.Length == 1 && function == "tostring")
                {
                    var text = args[0].Numerator + args[0].Denominator + 3;
                    if (text > MaxText)
                    {
                        throw new PrecomputationDeclined("text-size");
                    }

                    Work += text;
                    return new Bound(ExprType.String, Text: (int)text);
                }

                if (args.Length == 1 && function == "length")
                {
                    return new Bound(ExprType.Number, 16, 2);
                }

                if (args.Length == 2 && function == "index" && args[0].Kind == ExprType.Array &&
                    args[0].Element is { } element)
                {
                    return element;
                }

                if (args.Length == 2 && function == "contains")
                {
                    Work += args[0].Kind == ExprType.Array
                        ? args[0].Count * WalkCost(args[0].Element)
                        : (long)args[0].Text * System.Math.Max(1, args[1].Text);
                    return new Bound(ExprType.Boolean);
                }

                if (args.Length == 2 && function == "append" && args[0].Kind == ExprType.Array)
                {
                    var count = args[0].Count + 1;
                    if (count > MaxElements)
                    {
                        throw new PrecomputationDeclined("array-size");
                    }

                    Work += count;
                    return new Bound(ExprType.Array, Count: count,
                        Element: args[0].Element is { } current ? Join(current, args[1]) : args[1]);
                }

                throw new PrecomputationDeclined("unsupported-call");
            }
            default: throw new PrecomputationDeclined("effects-or-iteration");
        }
    }

    internal void ChargeResult(Bound result) => Work += WalkCost(result);

    private static long WalkCost(Bound? value)
    {
        var memo = new Dictionary<Bound, long>(ReferenceEqualityComparer.Instance);
        var visits = 0;

        long Walk(Bound? item, int depth)
        {
            if (item is null)
            {
                return 1;
            }

            if (++visits > 65536 || depth > 32)
            {
                throw new PrecomputationDeclined("structural-growth");
            }

            if (memo.TryGetValue(item, out var cached))
            {
                return cached;
            }

            var result = item.Kind == ExprType.Array ? 1 + item.Count * Walk(item.Element, depth + 1)
                : item.Kind == ExprType.Record ? 1 + item.Fields!.Values.Sum(child => Walk(child, depth + 1))
                : 1 + item.Text + (item.Numerator + item.Denominator + 63) / 64;
            if (result > 65536)
            {
                throw new PrecomputationDeclined("structural-growth");
            }

            memo[item] = result;
            return result;
        }

        return Walk(value, 0);
    }

    private void CheckNumbers(long numerator, long denominator)
    {
        if (numerator > NumericBits || denominator > NumericBits)
        {
            throw new PrecomputationDeclined("numeric-growth");
        }

        var words = (System.Math.Max(numerator, denominator) + 63) / 64;
        Work += words * words;
    }

    internal sealed record Bound(
        ExprType Kind,
        long Numerator = 1,
        long Denominator = 1,
        int Text = 0,
        int Count = 0,
        Bound? Element = null,
        IReadOnlyDictionary<string, Bound>? Fields = null);
}
