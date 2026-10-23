using System.Text.Json;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;
using Lx = System.Linq.Expressions.Expression;
using LxParameter = System.Linq.Expressions.ParameterExpression;

namespace SlotMath.Core.Compiler;

// Values are computed lazily at their original first use. Variant entries reset
// per element; proven invariant scalars reset per collection invocation. Nested
// iteration is excluded from the enclosing cache because it introduces bindings.
internal sealed class SamplingSubexpressions
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _invariants = new(StringComparer.Ordinal);
    private readonly Func<Type, LxParameter> _local;

    private readonly Dictionary<string, int> _occurrences = new(StringComparer.Ordinal);
    private readonly HashSet<string>? _variants;
    private bool _limited;
    private int _nextId;
    private long _text;
    private int _work;

    private SamplingSubexpressions(Expression body, Func<Type, LxParameter> local, IEnumerable<string>? variants)
    {
        _local = local;
        _variants = variants?.ToHashSet(StringComparer.Ordinal);
        Count(body, 0);
    }

    internal int InvariantCount => _entries.Values.Count(entry => entry.Invariant);

    public static SamplingSubexpressions? Create(Expression body, Func<Type, LxParameter> local,
        IEnumerable<string>? variants = null)
    {
        if (!Pure(body))
        {
            return null;
        }

        var cache = new SamplingSubexpressions(body, local, variants);
        return cache._limited ? null : cache;
    }

    private static string Key(Expression expression) => JsonSerializer.Serialize(expression, JsonOptions.Default);

    private static Expression[] Children(Expression expression) => expression switch
    {
        BinaryExpr binary => [binary.Left, binary.Right],
        CompareExpr compare => [compare.Left, compare.Right],
        IfExpr branch => [branch.Condition, branch.ThenExpr, branch.ElseExpr],
        NotExpr not => [not.Expr],
        CallExpr call => call.Args,
        BitSetExpr set => set.Args,
        _ => []
    };

    private static bool Pure(Expression expression, int depth = 0) => depth <= 128 && expression is
            ConstantExpr or FieldAccessExpr or BinaryExpr or CompareExpr
            or IfExpr or NotExpr or CallExpr or BitSetExpr
        && (expression is not FieldAccessExpr field ||
            field.Target is null or "state" or "board")
        && Children(expression).All(child => Pure(child, depth + 1));

    private (bool Variant, int Cost) Count(Expression expression, int depth)
    {
        if (++_work > 100000 || depth > 128)
        {
            _limited = true;
            return (true, 0);
        }

        var variant = expression is FieldAccessExpr { Path.Length: > 0 } field &&
                      (_variants is null || _variants.Contains(field.Path[0]));
        var cost = 1;
        foreach (Expression child in Children(expression))
        {
            (bool Variant, int Cost) info = Count(child, depth + 1);
            variant |= info.Variant;
            cost += info.Cost;
        }

        // Leaves are already lowered to a literal, local binding or state read.
        if (expression is not (ConstantExpr or FieldAccessExpr or IfExpr or CompareExpr or NotExpr))
        {
            var key = Key(expression);
            _text += key.Length;
            if (_text > 2000000)
            {
                _limited = true;
                return (true, cost);
            }

            _occurrences[key] = _occurrences.GetValueOrDefault(key) + 1;
            if (_variants is not null && !variant && cost >= 5 && ScalarReusePlan.IntrinsicScalar(expression))
            {
                _invariants.Add(key);
            }
        }

        return (variant, cost);
    }

    public Lx Emit(Expression expression, Func<Lx> lower, Action<string> shape)
    {
        var key = Key(expression);
        var invariant = _invariants.Contains(key);
        if (!invariant && _occurrences.GetValueOrDefault(key) < 2)
        {
            return lower();
        }

        if (_entries.TryGetValue(key, out Entry? entry))
        {
            shape($"cache-read:{entry.Id};");
            return entry.Read;
        }

        var id = _nextId++;
        shape(invariant ? $"invariant-cache:{id}(" : $"cache:{id}(");
        LxParameter present = _local(typeof(bool));
        LxParameter value = _local(typeof(ExprValue));
        Lx evaluate = lower();
        Lx read = Lx.Condition(present, value,
            Lx.Block(Lx.Assign(value, evaluate), Lx.Assign(present, Lx.Constant(true)), value));
        _entries.Add(key, new Entry(id, present, value, read, invariant));
        shape(")");
        return read;
    }

    public IEnumerable<Lx> Reset() => _entries.Values.Where(entry => !entry.Invariant).Select(entry =>
        Lx.Assign(entry.Present, Lx.Constant(false)));

    public IEnumerable<Lx> ResetInvariants() => _entries.Values.Where(entry => entry.Invariant).Select(entry =>
        Lx.Assign(entry.Present, Lx.Constant(false)));

    private sealed record Entry(int Id, LxParameter Present, LxParameter Value, Lx Read, bool Invariant);
}
