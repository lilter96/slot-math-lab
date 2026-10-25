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
    private Binding? _lexical;

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
            case LetExpr let:
            {
                Term value = Go(let.Value, state, bound);
                Binding? previous = _lexical;
                _lexical = new Binding(let.Name, value, previous);
                // Eager let must retain a failing initializer even if its result is unused.
                try
                {
                    return f.Seq(value, Go(let.Body, state, bound));
                }
                finally
                {
                    _lexical = previous;
                }
            }
            case LocalExpr local:
            {
                for (Binding? binding = _lexical; binding is not null; binding = binding.Next)
                {
                    if (binding.Name == local.Name)
                    {
                        return f.Path(binding.Value, local.Path);
                    }
                }

                return Fail();
            }
            case RecordExpr record:
                if (record.Fields.Length > LexicalValues.MaxFields || record.Fields.Any(field =>
                                                                       string.IsNullOrWhiteSpace(field.Name))
                                                                   || record.Fields.Select(field => field.Name)
                                                                       .Distinct(StringComparer.Ordinal).Count() !=
                                                                   record.Fields.Length)
                {
                    return Fail();
                }

                return f.Record(record.Fields.Select(field => field.Name).ToArray(),
                    record.Fields.Select(field => Go(field.Value, state, bound)).ToArray());
            case TupleExpr tuple:
                return tuple.Items.Length > LexicalValues.MaxFields
                    ? Fail()
                    : f.Record(LexicalValues.TupleNames(tuple.Items.Length),
                        tuple.Items.Select(item => Go(item, state, bound)).ToArray());
            case ProjectExpr project: return f.Path(Go(project.Value, state, bound), project.Path);
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

    private Term? CollectionSource(Expression expression, Dictionary<string, Field> state, Binding? bound)
    {
        CollectionInput input = CollectionInputs.Of(expression);
        Term? value = input.Source is { } source ? Go(source, state, bound) :
            input.StateKey is { } key ? Lookup(key, state, bound) : null;
        if (value is not null && input.MaxItems is { } maximum && Elements(value) is { } elements
            && (maximum is < 0 or > CollectionInputs.MaxItems || elements.Length > maximum))
        {
            return Fail();
        }

        return value;
    }

    private Term Aggregate(AggregateExpr aggregate, Dictionary<string, Field> state, Binding? bound)
    {
        if (aggregate.Func is not (AggregateFunc.Sum or AggregateFunc.Product or AggregateFunc.Count
                or AggregateFunc.Min or AggregateFunc.Max)
            || CollectionSource(aggregate, state, bound) is not { } source || Elements(source) is not { } elements)
        {
            return Opaque(aggregate, state, bound);
        }

        if (aggregate.Func == AggregateFunc.Count && aggregate.Predicate is null && aggregate.ValueExpr is null)
        {
            return f.Const(ExprValue.Number(elements.Length));
        }

        Term total = aggregate.Func == AggregateFunc.Product ? f.OneT : f.ZeroT;
        Term any = f.FalseT;
        foreach (Term element in elements)
        {
            var inner = new Binding(aggregate.ItemName, element, bound);
            Term holds = aggregate.Predicate is null ? f.TrueT : Go(aggregate.Predicate, state, inner);
            if (holds is ConstTerm { IsErr: false, Value.Kind: ExprType.Boolean, Value.BoolValue: false })
            {
                continue;
            }

            if (holds is ConstTerm { IsErr: true })
            {
                return holds;
            }

            if (!holds.IsBool)
            {
                return Opaque(aggregate, state, bound);
            }

            Term selected;
            if (aggregate.ValueExpr is { } selector)
            {
                selected = Go(selector, state, inner);
            }
            else if (element.IsNum || aggregate.Func == AggregateFunc.Count)
            {
                selected = element;
            }
            else if (element is ConstTerm constant)
            {
                ExprValue value = constant.Value;
                selected = value.Kind is ExprType.String or ExprType.Symbol && BigInteger.TryParse(value.StringValue,
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger number)
                    ? f.Const(ExprValue.Number(number))
                    : f.ZeroT;
            }
            else
            {
                return Opaque(aggregate, state, bound);
            }

            if (aggregate.Func != AggregateFunc.Count && !selected.IsNum && selected is not ConstTerm { IsErr: true })
            {
                return Opaque(aggregate, state, bound);
            }

            Term update = aggregate.Func switch
            {
                AggregateFunc.Count => f.Arith(BinaryOp.Add, total, f.Seq(selected, f.OneT)),
                AggregateFunc.Sum => f.Arith(BinaryOp.Add, total, selected),
                AggregateFunc.Product => f.Arith(BinaryOp.Mul, total, selected),
                AggregateFunc.Min => f.If(any, f.Call("min", [total, selected]), selected),
                _ => f.If(any, f.Call("max", [total, selected]), selected)
            };
            total = f.If(holds, update, total);
            any = f.Logic(true, any, holds);
        }

        // An empty selected set is undefined for min/max; total preserves errors
        // even when the final selection guard would otherwise skip its evaluation.
        return aggregate.Func is AggregateFunc.Min or AggregateFunc.Max
            ? f.Seq(total, f.If(any, total, f.ErrT))
            : total;
    }

    private Term Map(MapExpr map, Dictionary<string, Field> state, Binding? bound)
    {
        if (CollectionSource(map, state, bound) is not { } source || Elements(source) is not { } elements)
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
        if (fold.WhileCondition is not null)
        {
            return Opaque(fold, state, bound);
        }

        Term accumulator = Go(fold.Init, state, bound);
        if (CollectionSource(fold, state, bound) is not { } source || Elements(source) is not { } elements)
        {
            return Opaque(fold, state, bound);
        }

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

        var localNames = new List<string>();
        for (Binding? binding = _lexical; binding is not null; binding = binding.Next)
        {
            if (localNames.Contains(binding.Name, StringComparer.Ordinal))
            {
                continue;
            }

            localNames.Add(binding.Name);
            args.Add(binding.Value);
        }

        return f.Opaque(expression, [.. present], [.. localNames], [.. args]);
    }

    /// <summary>Every top-level state name the expression can read.</summary>
    internal static void Reads(Expression? expression, SortedSet<string> names)
    {
        switch (expression)
        {
            case FieldAccessExpr access
                when access.Path.Length > 0 && access.Target is null or "state" or "board":
                names.Add(access.Path[0]); break;
            case LetExpr let:
                Reads(let.Value, names);
                Reads(let.Body, names);
                break;
            case RecordExpr record:
                foreach (RecordFieldExpr field in record.Fields)
                {
                    Reads(field.Value, names);
                }

                break;
            case TupleExpr tuple:
                foreach (Expression item in tuple.Items)
                {
                    Reads(item, names);
                }

                break;
            case ProjectExpr project: Reads(project.Value, names); break;
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
            case BitSetExpr set:
                foreach (Expression arg in set.Args)
                {
                    Reads(arg, names);
                }

                break;
            case ReachableExpr reachable:
                Reads(reachable.Adjacency, names);
                Reads(reachable.Allowed, names);
                Reads(reachable.Seeds, names);
                break;
            case ScatterExpr scatter:
                Reads(scatter.Target, names);
                Reads(scatter.Indices, names);
                Reads(scatter.Values, names);
                break;
            case PartitionIndicesExpr partition: Reads(partition.Labels, names); break;
            case ScanExpr scan: Reads(scan.Source, names); break;
            case SegmentedCompactExpr compact:
                Reads(compact.Keep, names);
                Reads(compact.Boundaries, names);
                break;
            case AggregateExpr aggregate:
                if (aggregate.StateKey is { } aggregateKey)
                {
                    names.Add(aggregateKey);
                }

                Reads(aggregate.Source, names);
                Reads(aggregate.Predicate, names);
                Reads(aggregate.ValueExpr, names);
                break;
            case MapExpr map:
                if (map.StateKey is { } mapKey)
                {
                    names.Add(mapKey);
                }

                Reads(map.Source, names);
                Reads(map.Body, names);
                break;
            case FilterExpr filter:
                if (filter.StateKey is { } filterKey)
                {
                    names.Add(filterKey);
                }

                Reads(filter.Source, names);
                Reads(filter.Predicate, names);
                break;
            case FoldExpr fold:
                if (fold.StateKey is { } foldKey)
                {
                    names.Add(foldKey);
                }

                Reads(fold.Source, names);
                Reads(fold.Init, names);
                Reads(fold.WhileCondition, names);
                Reads(fold.Body, names);
                break;
        }
    }
}
