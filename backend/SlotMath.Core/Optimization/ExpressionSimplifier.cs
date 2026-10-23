using System.Globalization;
using System.Text.Json;
using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Optimization;

public sealed record ExpressionSimplification(
    Expression Expression,
    IReadOnlyDictionary<string, int> Rules,
    int Visited,
    bool BudgetReached);

/// <summary>
///     Reduces pure expressions without changing evaluation order, numeric precision or failures.
///     Facts are optional, caller-proven immutable state, never guesses from a field's name or initial value.
///     Numeric field facts must hold for every present value, including all writes and external overrides.
///     Every rule either evaluates a successful closed expression or removes work justified by control flow.
/// </summary>
public static class ExpressionSimplifier
{
    public static ExpressionSimplification Simplify(Expression expression,
        IReadOnlyDictionary<string, ExprValue>? constants = null, int maxVisits = 50_000,
        IReadOnlySet<string>? numericFields = null) =>
        new Rewriter(constants, maxVisits, numericFields).Run(expression);

    private sealed class Rewriter(
        IReadOnlyDictionary<string, ExprValue>? constants,
        int maxVisits,
        IReadOnlySet<string>? numericFields)
    {
        private const int MaxDepth = 128;
        private const int MaxLiteralLength = 4096;
        private readonly Dictionary<Expression, int> _keys = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, int> _rules = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _signatures = new(StringComparer.Ordinal);

        private readonly Dictionary<string, object?> _state = constants?.ToDictionary(p => p.Key, p => (object?)p.Value,
            StringComparer.Ordinal) ?? new Dictionary<string, object?>(StringComparer.Ordinal);

        private int _assumptionSteps;

        private int _foldWork = 100_000;
        private int _keyVisits;
        private bool _limited;
        private int _nextKey;

        private int _visited;

        public ExpressionSimplification Run(Expression expression) =>
            new(
                Rewrite(expression, new Dictionary<int, bool>(),
                    new HashSet<string>(StringComparer.Ordinal), 0), _rules,
                _visited, _limited);

        private Expression Changed(Expression source, Expression replacement, string rule)
        {
            _rules[rule] = _rules.GetValueOrDefault(rule) + 1;
            return replacement with
            {
                Annotation = source.Annotation ?? replacement.Annotation
            };
        }

        // Intern shallow signatures of child IDs, not serialized whole subtrees.
        // Equality is structural and collision-safe; retained signature data is linear in the inspected AST.
        private int Key(Expression expression, int depth = 0)
        {
            if (_keys.TryGetValue(expression, out var key))
            {
                return key;
            }

            if (++_keyVisits > maxVisits || depth > MaxDepth)
            {
                _limited = true;
                return _keys[expression] = ++_nextKey;
            }

            int Child(Expression child)
            {
                return Key(child, depth + 1);
            }

            int Optional(Expression? child)
            {
                return child is null ? 0 : Child(child);
            }

            object?[]? shape = expression switch
            {
                ConstantExpr c => [nameof(ConstantExpr), c.Kind, c.Value],
                LocalExpr e => [nameof(LocalExpr), e.Name, e.Path],
                LetExpr e => [nameof(LetExpr), e.Name, Child(e.Value), Child(e.Body)],
                RecordExpr e =>
                [
                    nameof(RecordExpr), e.Fields.Select(field => field.Name).ToArray(),
                    e.Fields.Select(field => Child(field.Value)).ToArray()
                ],
                TupleExpr e => [nameof(TupleExpr), e.Items.Select(Child).ToArray()],
                ProjectExpr e => [nameof(ProjectExpr), Child(e.Value), e.Path],
                FieldAccessExpr f => [nameof(FieldAccessExpr), f.Target, f.Path],
                BinaryExpr b => [nameof(BinaryExpr), b.Op, Child(b.Left), Child(b.Right)],
                CompareExpr c => [nameof(CompareExpr), c.Op, Child(c.Left), Child(c.Right)],
                IfExpr i => [nameof(IfExpr), Child(i.Condition), Child(i.ThenExpr), Child(i.ElseExpr)],
                NotExpr n => [nameof(NotExpr), Child(n.Expr)],
                CallExpr c => [nameof(CallExpr), c.Function, c.Args.Select(Child).ToArray()],
                BitSetExpr s => [nameof(BitSetExpr), s.Op, s.Args.Select(Child).ToArray()],
                MapExpr m =>
                [
                    nameof(MapExpr), m.StateKey, Optional(m.Source), m.MaxItems, m.ItemName, m.IndexName, m.ItemType,
                    Child(m.Body)
                ],
                FilterExpr f =>
                [
                    nameof(FilterExpr), f.StateKey, Optional(f.Source), f.MaxItems, f.ItemName, f.IndexName, f.ItemType,
                    Child(f.Predicate)
                ],
                FoldExpr f =>
                [
                    nameof(FoldExpr), f.StateKey, Optional(f.Source), f.MaxItems, f.ItemName, f.IndexName, f.AccName,
                    f.ItemType, Child(f.Init),
                    Child(f.Body)
                ],
                AggregateExpr a =>
                [
                    nameof(AggregateExpr), a.Func, a.StateKey, Optional(a.Source), a.MaxItems, a.ItemName, a.ItemType,
                    Optional(a.Predicate),
                    Optional(a.ValueExpr)
                ],
                _ => null
            };
            if (shape is null)
            {
                return _keys[expression] = ++_nextKey;
            }

            var signature = JsonSerializer.Serialize(new object?[] { shape, expression.Annotation });
            if (!_signatures.TryGetValue(signature, out key))
            {
                _signatures[signature] = key = ++_nextKey;
            }

            return _keys[expression] = key;
        }

        private Expression Rewrite(Expression expression, Dictionary<int, bool> assumptions,
            HashSet<string> bound, int depth)
        {
            if (_visited >= maxVisits || depth > MaxDepth)
            {
                _limited = true;
                return expression;
            }

            _visited++;
            // An assumption means this exact predicate has already succeeded in this lexical scope.
            if (assumptions.Count != 0 && assumptions.TryGetValue(Key(expression), out var assumed))
            {
                return Changed(expression, Boolean(assumed), "dominated-predicate");
            }

            Expression Visit(Expression child)
            {
                return Rewrite(child, assumptions, bound, depth + 1);
            }

            Expression Under(Expression child, Expression predicate, bool value)
            {
                var facts = new Dictionary<int, bool>(assumptions);

                void Assume(Expression proven, bool truth, int level = 0)
                {
                    if (++_assumptionSteps > maxVisits || level > MaxDepth)
                    {
                        _limited = true;
                        return;
                    }

                    facts[Key(proven)] = truth;
                    switch (proven)
                    {
                        case NotExpr not: Assume(not.Expr, !truth, level + 1); break;
                        case BinaryExpr { Op: BinaryOp.And } conjunction when truth:
                            Assume(conjunction.Left, true, level + 1);
                            Assume(conjunction.Right, true, level + 1);
                            break;
                        case BinaryExpr { Op: BinaryOp.Or } disjunction when !truth:
                            Assume(disjunction.Left, false, level + 1);
                            Assume(disjunction.Right, false, level + 1);
                            break;
                    }
                }

                Assume(predicate, value);
                return Rewrite(child, facts, bound, depth + 1);
            }

            Expression InIteration(Expression child, params string?[] names)
            {
                var locals = new HashSet<string>(bound, StringComparer.Ordinal);
                foreach (var name in names.OfType<string>())
                {
                    locals.Add(name);
                }

                // A lambda rebinds state names. No predicate from outside crosses this boundary.
                return Rewrite(child, new Dictionary<int, bool>(), locals, depth + 1);
            }

            Expression rewritten;
            switch (expression)
            {
                case LetExpr let:
                    rewritten = let with
                    {
                        Value = Visit(let.Value),
                        Body = Rewrite(let.Body, new Dictionary<int, bool>(), bound, depth + 1)
                    };
                    break;
                case RecordExpr record:
                    rewritten = record with
                    {
                        Fields = record.Fields.Select(field => field with
                        {
                            Value = Visit(field.Value)
                        }).ToArray()
                    };
                    break;
                case TupleExpr tuple:
                    rewritten = tuple with
                    {
                        Items = tuple.Items.Select(Visit).ToArray()
                    };
                    break;
                case ProjectExpr project:
                    rewritten = project with
                    {
                        Value = Visit(project.Value)
                    };
                    break;
                case IfExpr branch:
                    Expression condition = Visit(branch.Condition);
                    if (Bool(condition, out var taken))
                    {
                        return Changed(expression, Visit(taken ? branch.ThenExpr : branch.ElseExpr), "constant-branch");
                    }

                    rewritten = branch with
                    {
                        Condition = condition,
                        ThenExpr = Under(branch.ThenExpr, condition, true),
                        ElseExpr = Under(branch.ElseExpr, condition, false)
                    };
                    break;
                case BinaryExpr { Op: BinaryOp.And or BinaryOp.Or } logical:
                    Expression left = Visit(logical.Left);
                    if (Bool(left, out var value))
                    {
                        if (logical.Op == BinaryOp.Or ? value : !value)
                        {
                            return Changed(expression, left, "short-circuit");
                        }

                        Expression right = Visit(logical.Right);
                        // Keep the right operand's Boolean check when its runtime type is unknown.
                        if (BooleanResult(right))
                        {
                            return Changed(expression, right, "boolean-identity");
                        }

                        rewritten = logical with
                        {
                            Left = left,
                            Right = right
                        };
                    }
                    else
                    {
                        rewritten = logical with
                        {
                            Left = left,
                            Right = Under(logical.Right, left, logical.Op == BinaryOp.And)
                        };
                    }

                    break;
                case BinaryExpr binary:
                    rewritten = binary with
                    {
                        Left = Visit(binary.Left),
                        Right = Visit(binary.Right)
                    }; break;
                case CompareExpr compare:
                    rewritten = compare with
                    {
                        Left = Visit(compare.Left),
                        Right = Visit(compare.Right)
                    }; break;
                case NotExpr not:
                    rewritten = not with
                    {
                        Expr = Visit(not.Expr)
                    }; break;
                case CallExpr call:
                    rewritten = call with
                    {
                        Args = call.Args.Select(Visit).ToArray()
                    }; break;
                case BitSetExpr set:
                    rewritten = set with
                    {
                        Args = set.Args.Select(Visit).ToArray()
                    }; break;
                case MapExpr map:
                    rewritten = map with
                    {
                        Source = map.Source is null ? null : Visit(map.Source),
                        Body = InIteration(map.Body, map.ItemName, map.IndexName)
                    }; break;
                case FilterExpr filter:
                    rewritten = filter with
                    {
                        Source = filter.Source is null ? null : Visit(filter.Source),
                        Predicate = InIteration(filter.Predicate, filter.ItemName, filter.IndexName)
                    }; break;
                case FoldExpr fold:
                    rewritten = fold with
                    {
                        Source = fold.Source is null ? null : Visit(fold.Source),
                        Init = Visit(fold.Init),
                        Body = InIteration(fold.Body, fold.ItemName, fold.IndexName, fold.AccName)
                    }; break;
                case AggregateExpr aggregate:
                    rewritten = aggregate with
                    {
                        Source = aggregate.Source is null ? null : Visit(aggregate.Source),
                        Predicate = aggregate.Predicate is { } predicate
                            ? InIteration(predicate, aggregate.ItemName)
                            : null,
                        ValueExpr = aggregate.ValueExpr is { } selector
                            ? InIteration(selector, aggregate.ItemName)
                            : null
                    }; break;
                default: rewritten = expression; break;
            }

            if (rewritten is LetExpr dead && !UsesLocal(dead.Body, dead.Name, 0) && !_limited &&
                ExpressionCost.Compute(dead.Value) <= 10000)
            {
                ExpressionIr initializer = ExpressionIrBuilder.Build(dead.Value, TypeCheckContext.Default);
                if (initializer.Effects.CanSpeculate && initializer.Effects.Reads.Count == 0 &&
                    initializer.Effects.Bindings.Count == 0)
                {
                    return Changed(expression, dead.Body, "dead-local");
                }
            }

            if (rewritten is not ConstantExpr && TryLiteral(rewritten, bound, out ConstantExpr literal))
            {
                return Changed(expression, literal,
                    rewritten is FieldAccessExpr ? "immutable-state" : "exact-constant");
            }

            // These identities retain all possibly failing operands and their original order.
            // A raw field is deliberately excluded: removing +0 could otherwise hide a type error.
            if (rewritten is BinaryExpr numeric && NumericResult(numeric.Left, bound) &&
                Unit(numeric.Right, out var unit)
                && ((unit == 0 && numeric.Op is BinaryOp.Add or BinaryOp.Sub)
                    || (unit == 1 && numeric.Op is BinaryOp.Mul or BinaryOp.Div)))
            {
                return Changed(expression, numeric.Left, "numeric-identity");
            }

            if (rewritten is BinaryExpr leading && NumericResult(leading.Right, bound) &&
                Unit(leading.Left, out var first)
                && ((first == 0 && leading.Op == BinaryOp.Add) || (first == 1 && leading.Op == BinaryOp.Mul)))
            {
                return Changed(expression, leading.Right, "numeric-identity");
            }

            if (rewritten is NotExpr { Expr: NotExpr inner } && BooleanResult(inner.Expr))
            {
                return Changed(expression, inner.Expr, "double-negation");
            }

            return rewritten;
        }

        private bool UsesLocal(Expression expression, string name, int depth)
        {
            if (++_assumptionSteps > maxVisits || depth > MaxDepth)
            {
                _limited = true;
                return true;
            }

            bool Uses(Expression child)
            {
                return UsesLocal(child, name, depth + 1);
            }

            return expression switch
            {
                LocalExpr local => local.Name == name,
                LetExpr let => Uses(let.Value) || (let.Name != name && Uses(let.Body)),
                ConstantExpr or FieldAccessExpr => false,
                BinaryExpr e => Uses(e.Left) || Uses(e.Right),
                CompareExpr e => Uses(e.Left) || Uses(e.Right),
                IfExpr e => Uses(e.Condition) || Uses(e.ThenExpr) || Uses(e.ElseExpr),
                NotExpr e => Uses(e.Expr),
                CallExpr e => e.Args.Any(Uses),
                BitSetExpr e => e.Args.Any(Uses),
                ProjectExpr e => Uses(e.Value),
                RecordExpr e => e.Fields.Any(field => Uses(field.Value)),
                TupleExpr e => e.Items.Any(Uses),
                MapExpr e => (e.Source is not null && Uses(e.Source)) || Uses(e.Body),
                FilterExpr e => (e.Source is not null && Uses(e.Source)) || Uses(e.Predicate),
                FoldExpr e => (e.Source is not null && Uses(e.Source)) || Uses(e.Init) || Uses(e.Body),
                AggregateExpr e => (e.Source is not null && Uses(e.Source)) ||
                                   (e.Predicate is not null && Uses(e.Predicate)) ||
                                   (e.ValueExpr is not null && Uses(e.ValueExpr)),
                _ => true
            };
        }

        private bool TryLiteral(Expression expression, HashSet<string> bound, out ConstantExpr literal)
        {
            literal = null!;

            // Bounded scalar evaluation only. Never run user-authored collection loops while optimizing.
            bool Leaf(Expression e)
            {
                return e switch
                {
                    ConstantExpr c => Spend(c.Value.Length),
                    FieldAccessExpr f => f.Target is null or "state" or "board" && f.Path.Length > 0
                        && !bound.Contains(f.Path[0]) &&
                        _state.ContainsKey(f.Path[0]) && KnownField(f),
                    _ => false
                };
            }

            bool Spend(int work)
            {
                if (work > MaxLiteralLength)
                {
                    return false;
                }

                if ((_foldWork -= work + 1) < 0)
                {
                    _limited = true;
                    return false;
                }

                return true;
            }

            bool KnownField(FieldAccessExpr field)
            {
                try
                {
                    // Charge the actual selected value, including nested collection contents.
                    // Charging only the root record would let a nested lookup bypass the work bound.
                    var pending = new Stack<ExprValue>();
                    pending.Push(StateValues.Read(_state, field.Path));
                    long work = 0;
                    while (pending.TryPop(out ExprValue value))
                    {
                        work += value.Kind switch
                        {
                            ExprType.Number => value.NumberNumerator.GetBitLength() +
                                               value.NumberDenominator.GetBitLength(),
                            ExprType.String or ExprType.Symbol => value.StringValue!.Length,
                            ExprType.Array => value.ArrayValue!.Count + 1L,
                            ExprType.Record => value.RecordValue!.Count + 1L,
                            _ => 1
                        };
                        if (work > MaxLiteralLength)
                        {
                            return false;
                        }

                        if (value.ArrayValue is { } items)
                        {
                            foreach (ExprValue item in items)
                            {
                                pending.Push(item);
                            }
                        }

                        if (value.RecordValue is { } fields)
                        {
                            foreach (ExprValue item in fields.Values)
                            {
                                pending.Push(item);
                            }
                        }
                    }

                    return Spend((int)work);
                }
                catch (ExpressionEvaluationException)
                {
                    return false;
                }
            }

            var closed = expression switch
            {
                FieldAccessExpr => Leaf(expression),
                BinaryExpr b => Leaf(b.Left) && Leaf(b.Right),
                CompareExpr c => Leaf(c.Left) && Leaf(c.Right),
                NotExpr n => Leaf(n.Expr),
                CallExpr c => c.Args.Length <= 4 && c.Args.All(Leaf),
                BitSetExpr s => s.Args.Length <= 2 && s.Args.All(Leaf),
                _ => false
            };
            if (!closed)
            {
                return false;
            }

            if (--_foldWork < 0)
            {
                _limited = true;
                return false;
            }

            try
            {
                ExprValue value = ExactExpressionEvaluator.Evaluate(expression, new EvalContext
                {
                    State = _state
                });
                var text = value.Kind switch
                {
                    ExprType.Number => value.NumberDenominator.IsOne
                        ? value.NumberNumerator.ToString(CultureInfo.InvariantCulture)
                        : string.Create(CultureInfo.InvariantCulture,
                            $"{value.NumberNumerator}/{value.NumberDenominator}"),
                    ExprType.Boolean => value.BoolValue ? "true" : "false",
                    ExprType.String => value.StringValue,
                    _ => null
                };
                if (text is null || text.Length > MaxLiteralLength)
                {
                    return false;
                }

                literal = new ConstantExpr
                {
                    Kind = value.Kind switch
                    {
                        ExprType.Boolean => ConstantKind.Boolean,
                        ExprType.String => ConstantKind.String,
                        _ => value.NumberDenominator.IsOne ? ConstantKind.Integer : ConstantKind.Rational
                    },
                    Value = text
                };
                return true;
            }
            catch (Exception ex) when (ex is ExpressionEvaluationException or FormatException or OverflowException
                                           or DivideByZeroException or ArgumentException)
            {
                // Leave a failing expression in its original evaluation position, including dead operands.
                return false;
            }
        }

        private static ConstantExpr Boolean(bool value) => new()
        {
            Kind = ConstantKind.Boolean,
            Value = value ? "true" : "false"
        };

        private static bool Bool(Expression expression, out bool value)
        {
            value = false;
            return expression is ConstantExpr { Kind: ConstantKind.Boolean } c && bool.TryParse(c.Value, out value);
        }

        private static bool Unit(Expression expression, out int value)
        {
            value = -1;
            return expression is ConstantExpr { Kind: ConstantKind.Integer } c
                   && int.TryParse(c.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                   && value is 0 or 1;
        }

        private bool NumericResult(Expression e, HashSet<string> bound, int depth = 0) => depth <= MaxDepth && e switch
        {
            FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } field =>
                !bound.Contains(field.Path[0]) && numericFields?.Contains(field.Path[0]) == true,
            ConstantExpr { Kind: ConstantKind.Integer or ConstantKind.Rational } => true,
            BinaryExpr { Op: BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul or BinaryOp.Div } => true,
            BitSetExpr => true,
            CallExpr c => c.Function.ToLowerInvariant() is "abs" or "min" or "max" or "floor" or "ceil" or "round"
                or "length" or "tonumber",
            IfExpr i => NumericResult(i.ThenExpr, bound, depth + 1) && NumericResult(i.ElseExpr, bound, depth + 1),
            _ => false
        };

        private static bool BooleanResult(Expression e, int depth = 0) => depth <= MaxDepth && e switch
        {
            ConstantExpr { Kind: ConstantKind.Boolean } => true,
            CompareExpr or NotExpr => true,
            BinaryExpr { Op: BinaryOp.And or BinaryOp.Or } => true,
            CallExpr c => c.Function.Equals("contains", StringComparison.OrdinalIgnoreCase),
            IfExpr i => BooleanResult(i.ThenExpr, depth + 1) && BooleanResult(i.ElseExpr, depth + 1),
            _ => false
        };
    }
}
