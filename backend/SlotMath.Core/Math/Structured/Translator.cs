using System.Globalization;
using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Math.Structured;

// ═══════════════════════════════════════════════════════════════════════════
//  Translator — expression AST → term, in a symbolic state
//
//  Mirrors ExactExpressionEvaluator form by form. What the evaluator reads
//  from the state dictionary is read here from a dictionary of terms; what it
//  computes is built with the term constructors. Forms the term language has
//  no structure for (filter, fold over an unknown array, most aggregates) are
//  kept as one opaque node that calls the reference evaluator itself.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>A state field: its value, and whether some executions reach this point without it.</summary>
internal readonly record struct Field(Term Value, bool MaybeAbsent);

/// <summary>Names an iteration form binds over the state (item, index, accumulator), innermost first.</summary>
internal sealed record Binding(string Name, Term Value, Binding? Next);

internal sealed class Translator(TermFactory f)
{
    public Term Translate(Expression expression, Dictionary<string, Field> state) => Go(expression, state, null);

    private Term? Lookup(string name, Dictionary<string, Field> state, Binding? bound)
    {
        for (Binding? binding = bound; binding is not null; binding = binding.Next)
        {
            if (binding.Name == name)
            {
                return binding.Value;
            }
        }

        if (!state.TryGetValue(name, out Field field))
        {
            return null;
        }

        if (field.MaybeAbsent)
        {
            f.FailSeen = true;
        }

        return field.Value;
    }

    private Term Fail()
    {
        f.FailSeen = true;
        return f.ErrT;
    }

    private Term Go(Expression expression, Dictionary<string, Field> state, Binding? bound)
    {
        f.Budget.Tick();
        switch (expression)
        {
            case ConstantExpr constant: return Constant(constant);
            case FieldAccessExpr access: return Access(access, state, bound);
            case BinaryExpr { Op: BinaryOp.And or BinaryOp.Or } logical:
            {
                var isOr = logical.Op == BinaryOp.Or;
                Term left = Go(logical.Left, state, bound);
                // A constant left operand that decides the result leaves the right one unevaluated.
                if (left is ConstTerm { IsErr: false, Value.Kind: ExprType.Boolean } decided &&
                    decided.Value.BoolValue == isOr)
                {
                    return left;
                }

                if (left is ConstTerm { IsErr: true })
                {
                    return left;
                }

                return f.Logic(isOr, left, Go(logical.Right, state, bound));
            }
            case BinaryExpr { Op: BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul or BinaryOp.Div } arithmetic:
                return f.Arith(arithmetic.Op, Go(arithmetic.Left, state, bound), Go(arithmetic.Right, state, bound));
            case CompareExpr compare:
                return f.Cmp(compare.Op, Go(compare.Left, state, bound), Go(compare.Right, state, bound));
            case IfExpr conditional:
            {
                Term condition = Go(conditional.Condition, state, bound);
                if (condition is ConstTerm { Value.Kind: ExprType.Boolean } known)
                {
                    return Go(known.Value.BoolValue ? conditional.ThenExpr : conditional.ElseExpr, state, bound);
                }

                if (condition is ConstTerm)
                {
                    return f.If(condition, f.ZeroT, f.ZeroT);
                }

                return f.If(condition, Go(conditional.ThenExpr, state, bound), Go(conditional.ElseExpr, state, bound));
            }
            case NotExpr not: return f.Not(Go(not.Expr, state, bound));
            case CallExpr call:
            {
                var args = new Term[call.Args.Length];
                for (var i = 0; i < args.Length; i++)
                {
                    args[i] = Go(call.Args[i], state, bound);
                }

                return f.Call(call.Function.ToLowerInvariant(), args);
            }
            case AggregateExpr aggregate: return Aggregate(aggregate, state, bound);
            case MapExpr map: return Map(map, state, bound);
            case FoldExpr fold: return Fold(fold, state, bound);
            default: return Opaque(expression, state, bound);
        }
    }

    private Term Constant(ConstantExpr constant)
    {
        try
        {
            return f.Const(ExactExpressionEvaluator.Evaluate(constant, new EvalContext()));
        }
        catch (ExpressionEvaluationException)
        {
            return Fail();
        }
    }

    private Term Access(FieldAccessExpr access, Dictionary<string, Field> state, Binding? bound)
    {
        // Settlement fields exist only at completed-round measurement points, never during a round.
        if (access.Target is not (null or "state" or "board"))
        {
            return Fail();
        }

        if (access.Path.Length == 0 || access.Path.Any(string.IsNullOrEmpty))
        {
            return Fail();
        }

        Term? value = Lookup(access.Path[0], state, bound);
        if (value is null)
        {
            return Fail();
        }

        var from = 1;
        // A literal index into an array of terms selects one element; the others are untouched as long as they cannot fail.
        while (from < access.Path.Length && value is OpTerm { Code: Op.Arr, CanErr: false } array
                                         && BigInteger.TryParse(access.Path[from], NumberStyles.AllowLeadingSign,
                                             CultureInfo.InvariantCulture, out BigInteger index))
        {
            if (index < 0 || index >= array.Args.Length)
            {
                return Fail();
            }

            value = array.Args[(int)index];
            from++;
        }

        return f.Path(value, access.Path[from..]);
    }

    /// <summary>The elements of an array-valued term whose length is fixed, or null.</summary>
    private Term[]? Elements(Term array) => array switch
    {
        OpTerm { Code: Op.Arr, CanErr: false } terms => terms.Args,
        ConstTerm { Value.Kind: ExprType.Array } constant => constant.Value.ArrayValue!.Select(f.Const).ToArray(),
        _ => null
    };

    private Term Aggregate(AggregateExpr aggregate, Dictionary<string, Field> state, Binding? bound)
    {
        if (aggregate.Func != AggregateFunc.Count || aggregate.ValueExpr is not null)
        {
            return Opaque(aggregate, state, bound);
        }

        // The evaluator reads the array straight from the state dictionary, where iteration bindings are also stored.
        if (Lookup(aggregate.StateKey, state, bound) is not { } source || Elements(source) is not { } elements)
        {
            return Opaque(aggregate, state, bound);
        }

        if (aggregate.Predicate is null)
        {
            return f.Const(ExprValue.Number(elements.Length));
        }

        var parts = new List<(ExprValue, Term)>(elements.Length);
        foreach (Term element in elements)
        {
            Term holds = Go(aggregate.Predicate, state, new Binding(aggregate.ItemName, element, bound));
            Term counted = f.If(holds, f.OneT, f.ZeroT);
            if (!counted.IsNum)
            {
                return counted is ConstTerm { IsErr: true } ? counted : Opaque(aggregate, state, bound);
            }

            parts.Add((V.One, counted));
        }

        return f.Lin(V.Zero, parts);
    }

    private Term Map(MapExpr map, Dictionary<string, Field> state, Binding? bound)
    {
        if (Lookup(map.StateKey, state, bound) is not { } source || Elements(source) is not { } elements)
        {
            return Opaque(map, state, bound);
        }

        var results = new Term[elements.Length];
        for (var i = 0; i < elements.Length; i++)
        {
            var inner = new Binding(map.ItemName, elements[i], bound);
            if (map.IndexName is not null)
            {
                inner = new Binding(map.IndexName, f.Const(ExprValue.Number(i)), inner);
            }

            results[i] = Go(map.Body, state, inner);
            if (results[i] is ConstTerm { IsErr: true })
            {
                return results[i];
            }
        }

        return f.Arr(results);
    }

    private Term Fold(FoldExpr fold, Dictionary<string, Field> state, Binding? bound)
    {
        if (Lookup(fold.StateKey, state, bound) is not { } source || Elements(source) is not { } elements)
        {
            return Opaque(fold, state, bound);
        }

        Term accumulator = Go(fold.Init, state, bound);
        for (var i = 0; i < elements.Length; i++)
        {
            // An iteration need not read the accumulator, so one that can fail is not unrolled.
            if (!accumulator.Pure)
            {
                return accumulator is ConstTerm ? accumulator : Opaque(fold, state, bound);
            }

            var inner = new Binding(fold.ItemName, elements[i], new Binding(fold.AccName, accumulator, bound));
            if (fold.IndexName is not null)
            {
                inner = new Binding(fold.IndexName, f.Const(ExprValue.Number(i)), inner);
            }

            accumulator = Go(fold.Body, state, inner);
        }

        return accumulator;
    }

    private Term Opaque(Expression expression, Dictionary<string, Field> state, Binding? bound)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        Reads(expression, names);
        var present = new List<string>(names.Count);
        var args = new List<Term>(names.Count);
        foreach (var name in names)
        {
            if (Lookup(name, state, bound) is not { } value)
            {
                continue; // absent: the evaluator reports it if it reads it
            }

            present.Add(name);
            args.Add(value);
        }

        return f.Opaque(expression, [.. present], [.. args]);
    }

    /// <summary>Every top-level state name the expression can read.</summary>
    internal static void Reads(Expression? expression, SortedSet<string> names)
    {
        switch (expression)
        {
            case FieldAccessExpr access
                when access.Path.Length > 0 && access.Target is null or "state" or "board":
                names.Add(access.Path[0]); break;
            case BinaryExpr binary:
                Reads(binary.Left, names);
                Reads(binary.Right, names);
                break;
            case CompareExpr compare:
                Reads(compare.Left, names);
                Reads(compare.Right, names);
                break;
            case IfExpr conditional:
                Reads(conditional.Condition, names);
                Reads(conditional.ThenExpr, names);
                Reads(conditional.ElseExpr, names);
                break;
            case NotExpr not: Reads(not.Expr, names); break;
            case CallExpr call:
                foreach (Expression arg in call.Args)
                {
                    Reads(arg, names);
                }

                break;
            case AggregateExpr aggregate:
                names.Add(aggregate.StateKey);
                Reads(aggregate.Predicate, names);
                Reads(aggregate.ValueExpr, names);
                break;
            case MapExpr map:
                names.Add(map.StateKey);
                Reads(map.Body, names);
                break;
            case FilterExpr filter:
                names.Add(filter.StateKey);
                Reads(filter.Predicate, names);
                break;
            case FoldExpr fold:
                names.Add(fold.StateKey);
                Reads(fold.Init, names);
                Reads(fold.Body, names);
                break;
        }
    }
}
