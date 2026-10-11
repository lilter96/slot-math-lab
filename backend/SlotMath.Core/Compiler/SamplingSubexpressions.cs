using System.Text.Json;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;
using Lx = System.Linq.Expressions.Expression;
using LxParameter = System.Linq.Expressions.ParameterExpression;

namespace SlotMath.Core.Compiler;

// A cache belongs to one execution of a scalar iteration selector. Values are computed lazily,
// in their original order, and flags are reset for each element. Nested iteration is excluded:
// its temporary state bindings must never become values cached in an enclosing selector.
internal sealed class SamplingSubexpressions
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<Type, LxParameter> _local;

    private readonly Dictionary<string, int> _occurrences = new(StringComparer.Ordinal);
    private int _nextId;

    private SamplingSubexpressions(Expression body, Func<Type, LxParameter> local)
    {
        _local = local;
        Count(body);
    }

    public static SamplingSubexpressions? Create(Expression body, Func<Type, LxParameter> local) =>
        Pure(body) ? new SamplingSubexpressions(body, local) : null;

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

    private static bool Pure(Expression expression) => expression is
                                                           ConstantExpr or FieldAccessExpr or BinaryExpr or CompareExpr
                                                           or IfExpr or NotExpr or CallExpr or BitSetExpr
                                                       && Children(expression).All(Pure);

    private void Count(Expression expression)
    {
        // Leaves are already lowered to a literal, local binding or state read.
        if (expression is not (ConstantExpr or FieldAccessExpr or IfExpr or CompareExpr or NotExpr))
        {
            var key = Key(expression);
            _occurrences[key] = _occurrences.GetValueOrDefault(key) + 1;
        }

        foreach (Expression child in Children(expression))
        {
            Count(child);
        }
    }

    public Lx Emit(Expression expression, Func<Lx> lower, Action<string> shape)
    {
        var key = Key(expression);
        if (_occurrences.GetValueOrDefault(key) < 2)
        {
            return lower();
        }

        if (_entries.TryGetValue(key, out Entry? entry))
        {
            shape($"cache-read:{entry.Id};");
            return entry.Read;
        }

        var id = _nextId++;
        shape($"cache:{id}(");
        LxParameter present = _local(typeof(bool));
        LxParameter value = _local(typeof(ExprValue));
        Lx evaluate = lower();
        Lx read = Lx.Condition(present, value,
            Lx.Block(Lx.Assign(value, evaluate), Lx.Assign(present, Lx.Constant(true)), value));
        _entries.Add(key, new Entry(id, present, value, read));
        shape(")");
        return read;
    }

    public IEnumerable<Lx> Reset() => _entries.Values.Select(entry =>
        Lx.Assign(entry.Present, Lx.Constant(false)));

    private sealed record Entry(int Id, LxParameter Present, LxParameter Value, Lx Read);
}
