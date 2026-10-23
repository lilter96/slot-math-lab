using System.Collections.Frozen;
using System.Globalization;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler.Ir;

/// <summary>
///     Lowers the generic expression grammar into ordered typed SSA regions.
///     It records conservative effects, never treating inferred state types as runtime guards.
/// </summary>
internal sealed class ExpressionIrBuilder
{
    private readonly IReadOnlyDictionary<string, ExprValue> _facts;
    private readonly Dictionary<string, ExprValue> _usedGuards = new(StringComparer.Ordinal);
    private int _operation;
    private int _region;
    private int _value;

    private ExpressionIrBuilder(TypeCheckContext context, IReadOnlyDictionary<string, ExprValue>? facts)
    {
        _facts = facts is null
            ? FrozenDictionary<string, ExprValue>.Empty
            : facts.ToFrozenDictionary(pair => pair.Key, pair => StateValues.Convert(pair.Value.ToStateObject()),
                StringComparer.Ordinal);
    }

    private static IrEffects Throwing => new(IrEffect.MayThrow);

    internal static ExpressionIr Build(Expression expression, TypeCheckContext context,
        IReadOnlyDictionary<string, ExprValue>? facts = null)
    {
        var builder = new ExpressionIrBuilder(context, facts);
        var scope = new Scope(context);
        IrValue value = builder.Lower(expression, scope, 0);
        return new ExpressionIr(builder.Finish(scope, "root", value),
            builder._usedGuards.ToFrozenDictionary(StringComparer.Ordinal));
    }

    private IrRegion Finish(Scope scope, string role, IrValue value) => new(++_region, role,
        scope.Parameters.AsReadOnly(), scope.Operations.AsReadOnly(), value,
        IrEffects.Union(scope.Operations.Select(operation => operation.Effects)));

    private IrValue Emit(Scope scope, IrOp code, FieldDescriptor? shape, IrEffects own,
        IrValue[]? inputs = null, IrRegion[]? regions = null, ExprValue? constant = null,
        string? detail = null, string[]? path = null, int? bound = null)
    {
        var value = new IrValue(++_value, shape, constant);
        IrRegion[] children = regions ?? [];
        IrEffects effects = children.Length == 0
            ? own
            : IrEffects.Union(new[] { own }.Concat(children.Select(region => region.Effects)));
        scope.Operations.Add(new IrOperation(++_operation, code, value, Array.AsReadOnly(inputs ?? []),
            Array.AsReadOnly(children), own, effects, detail,
            path is null ? null : Array.AsReadOnly((string[])path.Clone()), bound));
        return value;
    }

    private static FieldDescriptor Shape(ExprType type) => ExpressionShapes.Field("value", type);

    private Scope Child(Scope parent, TypeCheckContext? context = null) => new(context ?? parent.Context,
        new Dictionary<string, IrValue>(parent.State, StringComparer.Ordinal),
        new Dictionary<string, IrValue>(parent.Locals, StringComparer.Ordinal));

    private IrValue Parameter(Scope scope, string name, FieldDescriptor? shape)
    {
        var value = new IrValue(++_value, shape);
        scope.Parameters.Add(value);
        return value;
    }

    private IrValue Lower(Expression expression, Scope scope, int depth)
    {
        if (depth > 128)
        {
            return Emit(scope, IrOp.Opaque, null, IrEffects.Opaque, detail: "depth-budget");
        }

        IrValue Visit(Expression child)
        {
            return Lower(child, scope, depth + 1);
        }

        switch (expression)
        {
            case ConstantExpr literal:
            {
                ExprValue? constant = null;
                try
                {
                    constant = ExactExpressionEvaluator.Evaluate(literal, EvalContext.Empty);
                }
                catch (Exception ex) when (ex is ExpressionEvaluationException or FormatException or OverflowException
                                               or DivideByZeroException)
                {
                }

                ExprType type = literal.Kind switch
                {
                    ConstantKind.Boolean => ExprType.Boolean,
                    ConstantKind.String => ExprType.String,
                    _ => ExprType.Number
                };
                return Emit(scope, IrOp.Constant, Shape(type), constant is null ? Throwing : IrEffects.Empty,
                    constant: constant);
            }
            case FieldAccessExpr field:
            {
                if (field.Target == "measurement")
                {
                    return Emit(scope, IrOp.ReadMeasurement, scope.Context.ResolveField(field.Path, field.Target),
                        new IrEffects(IrEffect.Observe | IrEffect.MayThrow), path: field.Path);
                }

                if (field.Target is not (null or "state" or "board") || field.Path.Length == 0)
                {
                    return Emit(scope, IrOp.Opaque, null, IrEffects.Opaque, detail: "invalid-field");
                }

                var key = field.Path[0];
                if (scope.State.TryGetValue(key, out IrValue? binding))
                {
                    return Project(scope, binding, field.Path[1..], key);
                }

                ExprValue? constant = null;
                if (_facts.TryGetValue(key, out ExprValue fact))
                {
                    try
                    {
                        constant = field.Path.Length == 1
                            ? StateValues.RequireReadable(fact, key)
                            : StateValues.Read(fact, field.Path[1..], string.Join('.', field.Path));
                        _usedGuards[key] = fact;
                    }
                    catch (ExpressionEvaluationException)
                    {
                    }
                }

                return Emit(scope, IrOp.ReadState, scope.Context.ResolveField(field.Path, field.Target),
                    new IrEffects(IrEffect.ReadState | (constant is null ? IrEffect.MayThrow : IrEffect.None), [key]),
                    constant: constant, detail: key, path: field.Path);
            }
            case LocalExpr local:
                return scope.Locals.TryGetValue(local.Name, out IrValue? localValue)
                    ? Project(scope, localValue, local.Path, local.Name)
                    : Emit(scope, IrOp.Opaque, null, Throwing, detail: "missing-local:" + local.Name);
            case LetExpr let:
            {
                IrValue initial = Visit(let.Value);
                Scope inner = Child(scope,
                    initial.Shape is { } shape ? scope.Context.WithLocal(let.Name, shape) : scope.Context);
                inner.Locals[let.Name] = initial;
                IrValue body = Lower(let.Body, inner, depth + 1);
                IrRegion region = Finish(inner, "let-body", body);
                return Emit(scope, IrOp.Let, body.Shape, IrEffects.Empty, [initial], [region], body.Constant, let.Name);
            }
            case ProjectExpr project:
                return Project(scope, Visit(project.Value), project.Path, "project");
            case IfExpr conditional:
            {
                IrValue condition = Visit(conditional.Condition);
                Scope yes = Child(scope);
                Scope no = Child(scope);
                IrValue a = Lower(conditional.ThenExpr, yes, depth + 1);
                IrValue b = Lower(conditional.ElseExpr, no, depth + 1);
                return Emit(scope, IrOp.If, ExpressionShapes.Join(a.Shape, b.Shape), TruthEffect(condition),
                    [condition],
                    [Finish(yes, "then", a), Finish(no, "else", b)]);
            }
            case BinaryExpr { Op: BinaryOp.And or BinaryOp.Or } logical:
            {
                IrValue left = Visit(logical.Left);
                Scope rightScope = Child(scope);
                IrValue right = Lower(logical.Right, rightScope, depth + 1);
                return Emit(scope, logical.Op == BinaryOp.And ? IrOp.And : IrOp.Or, Shape(ExprType.Boolean),
                    new IrEffects(TruthEffect(left).Flags | TruthEffect(right).Flags), [left],
                    [Finish(rightScope, "right-if-needed", right)]);
            }
            case BinaryExpr binary:
                return Scalar(scope, IrOp.Binary, expression, [Visit(binary.Left), Visit(binary.Right)],
                    ExprType.Number, binary.Op.ToString());
            case CompareExpr compare:
                return Scalar(scope, IrOp.Compare, expression, [Visit(compare.Left), Visit(compare.Right)],
                    ExprType.Boolean, compare.Op.ToString());
            case NotExpr not:
                return Scalar(scope, IrOp.Not, expression, [Visit(not.Expr)], ExprType.Boolean);
            case CallExpr call:
            {
                IrValue[] args = call.Args.Select(Visit).ToArray();
                return Scalar(scope, IrOp.Call, expression, args, ExpressionShapes.Infer(call, scope.Context)?.Type,
                    call.Function, IrEffect.Allocate);
            }
            case BitSetExpr set:
                return Scalar(scope, IrOp.BitSet, expression, set.Args.Select(Visit).ToArray(), ExprType.Number,
                    set.Op.ToString());
            case RecordExpr record:
            {
                IrValue[] values = record.Fields.Select(field => Visit(field.Value)).ToArray();
                var shape = new FieldDescriptor
                {
                    Name = "record",
                    Type = ExprType.Record,
                    RecordFields = values.Select((value, index) => value.Shape is { } field
                        ? field with
                        {
                            Name = record.Fields[index].Name
                        }
                        : ExpressionShapes.Field(record.Fields[index].Name, ExprType.Error)).ToArray()
                };
                return Emit(scope, IrOp.Record, shape, new IrEffects(IrEffect.Allocate | IrEffect.MayThrow), values,
                    detail: string.Join(',', record.Fields.Select(field => field.Name)));
            }
            case TupleExpr tuple:
            {
                IrValue[] values = tuple.Items.Select(Visit).ToArray();
                return Emit(scope, IrOp.Tuple, ExpressionShapes.Infer(tuple, scope.Context),
                    new IrEffects(IrEffect.Allocate | IrEffect.MayThrow), values);
            }
            case MapExpr or FilterExpr or FoldExpr or AggregateExpr:
                return Collection(expression, scope, depth);
            default:
                return Emit(scope, IrOp.Opaque, null, IrEffects.Opaque, detail: expression.GetType().Name);
        }
    }

    private static IrEffects TruthEffect(IrValue value) =>
        value.Constant is { Kind: ExprType.Boolean } ? IrEffects.Empty : Throwing;

    private IrValue Scalar(Scope scope, IrOp code, Expression expression, IrValue[] inputs, ExprType? type,
        string? detail = null, IrEffect additional = IrEffect.None)
    {
        ExprValue? constant = null;
        if (inputs.All(value => value.Constant is not null))
        {
            // This proves totality only for the captured facts; it never changes the authored operation.
            Dictionary<string, object?> state = inputs.Select((value, index) => (
                    Key: index.ToString(CultureInfo.InvariantCulture),
                    Value: value.Constant!.Value.ToStateObject()))
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            Expression Arg(int index)
            {
                return new FieldAccessExpr
                {
                    Target = "state",
                    Path = [index.ToString(CultureInfo.InvariantCulture)]
                };
            }

            Expression evaluated = expression switch
            {
                BinaryExpr e => e with
                {
                    Left = Arg(0),
                    Right = Arg(1)
                },
                CompareExpr e => e with
                {
                    Left = Arg(0),
                    Right = Arg(1)
                },
                NotExpr e => e with
                {
                    Expr = Arg(0)
                },
                CallExpr e => e with
                {
                    Args = Enumerable.Range(0, inputs.Length).Select(Arg).ToArray()
                },
                BitSetExpr e => e with
                {
                    Args = Enumerable.Range(0, inputs.Length).Select(Arg).ToArray()
                },
                _ => expression
            };
            try
            {
                constant = ExactExpressionEvaluator.Evaluate(evaluated, new EvalContext
                {
                    State = state
                });
            }
            catch (Exception ex) when (ex is ExpressionEvaluationException or FormatException or OverflowException
                                           or DivideByZeroException or InvalidOperationException)
            {
            }
        }

        return Emit(scope, code, type is { } kind ? Shape(kind) : null,
            new IrEffects(additional | IrEffect.Allocate | (constant is null ? IrEffect.MayThrow : IrEffect.None)),
            inputs, constant: constant, detail: detail);
    }

    private IrValue Project(Scope scope, IrValue source, string[] path, string name)
    {
        if (path.Length == 0)
        {
            if (source.Constant is { Kind: not ExprType.Null })
            {
                return source;
            }

            return Emit(scope, IrOp.Project, source.Shape, Throwing, [source], detail: name, path: path);
        }

        ExprValue? constant = null;
        if (source.Constant is { } value)
        {
            try
            {
                constant = LexicalValues.Project(value, path);
            }
            catch (ExpressionEvaluationException)
            {
            }
        }

        return Emit(scope, IrOp.Project, ExpressionShapes.Project(source.Shape, path),
            constant is null ? Throwing : IrEffects.Empty,
            [source], constant: constant, detail: name, path: path);
    }

    private IrValue Collection(Expression expression, Scope scope, int depth)
    {
        CollectionInput input = CollectionInputs.Of(expression);
        var inputs = new List<IrValue>();
        IrValue? init = expression is FoldExpr fold ? Lower(fold.Init, scope, depth + 1) : null;
        if (init is not null)
        {
            inputs.Add(init);
        }

        Expression sourceExpression = input.Source ?? new FieldAccessExpr
        {
            Target = "state",
            Path = [input.StateKey!]
        };
        IrValue source = Lower(sourceExpression, scope, depth + 1);
        inputs.Add(source);
        var itemName = expression switch
        {
            MapExpr e => e.ItemName,
            FilterExpr e => e.ItemName,
            FoldExpr e => e.ItemName,
            AggregateExpr e => e.ItemName,
            _ => ""
        };
        var indexName = expression switch
        {
            MapExpr e => e.IndexName,
            FilterExpr e => e.IndexName,
            FoldExpr e => e.IndexName,
            _ => null
        };
        var accName = expression is FoldExpr f ? f.AccName : null;
        ExprType declared = expression switch
        {
            MapExpr e => e.ItemType,
            FilterExpr e => e.ItemType,
            FoldExpr e => e.ItemType,
            AggregateExpr e => e.ItemType,
            _ => ExprType.Error
        };
        FieldDescriptor itemShape = ExpressionShapes.Item(source.Shape) ?? Shape(declared);
        var bindings = new List<FieldDescriptor>
        {
            itemShape with
            {
                Name = itemName
            }
        };
        if (init?.Shape is { } initial && accName is not null)
        {
            bindings.Insert(0, initial with
            {
                Name = accName
            });
        }

        if (indexName is not null)
        {
            bindings.Add(ExpressionShapes.Field(indexName, ExprType.Number));
        }

        Scope body = Child(scope, ArrayExpressionTypes.Bind(scope.Context, [.. bindings]));
        if (accName is not null)
        {
            body.State[accName] = Parameter(body, accName, init?.Shape);
        }

        body.State[itemName] = Parameter(body, itemName, itemShape);
        if (indexName is not null)
        {
            body.State[indexName] = Parameter(body, indexName, Shape(ExprType.Number));
        }

        var names = new[] { accName, itemName, indexName }.OfType<string>().ToArray();
        var regions = new List<IrRegion>();
        FieldDescriptor? shape;
        IrOp code;
        if (expression is AggregateExpr aggregate)
        {
            Scope predicateScope = Child(body);
            IrValue predicate = aggregate.Predicate is null
                ? Lower(new ConstantExpr
                {
                    Kind = ConstantKind.Boolean,
                    Value = "true"
                }, predicateScope, depth + 1)
                : Lower(aggregate.Predicate, predicateScope, depth + 1);
            regions.Add(Finish(predicateScope, "predicate", predicate));
            Scope selectorScope = Child(body);
            IrValue selector = aggregate.ValueExpr is null
                ? body.State[itemName]
                : Lower(aggregate.ValueExpr, selectorScope, depth + 1);
            regions.Add(Finish(selectorScope, "selector-if-selected", selector));
            // The parameter-owning region remains explicit even when selectors are lazy.
            regions = [FinishWithChildren(body, "iteration", selector, [.. regions])];
            shape = Shape(ExprType.Number);
            code = IrOp.Aggregate;
        }
        else
        {
            Expression child = expression switch
            {
                MapExpr e => e.Body,
                FilterExpr e => e.Predicate,
                FoldExpr e => e.Body,
                _ => throw new InvalidOperationException()
            };
            IrValue result = Lower(child, body, depth + 1);
            regions.Add(Finish(body, expression is FilterExpr ? "predicate" : "body", result));
            shape = expression is MapExpr
                ? ExpressionShapes.Array(result.Shape, source.Shape?.ArrayIsEmpty == true || input.MaxItems == 0)
                : expression is FilterExpr
                    ? source.Shape
                    : init?.Shape;
            code = expression is MapExpr ? IrOp.Map : expression is FilterExpr ? IrOp.Filter : IrOp.Fold;
        }

        var own = new IrEffects(
            IrEffect.MayThrow | IrEffect.Cancellation |
            (expression is MapExpr or FilterExpr ? IrEffect.Allocate : IrEffect.None), bindings: names);
        return Emit(scope, code, shape, own, [.. inputs], [.. regions], detail: input.Kind, bound: input.MaxItems);
    }

    private IrRegion FinishWithChildren(Scope scope, string role, IrValue value, IrRegion[] regions)
    {
        // One structural instruction anchors conditional child regions to their parameter scope.
        IrValue output = Emit(scope, IrOp.AggregateStep, value.Shape, IrEffects.Empty, regions: regions);
        return Finish(scope, role, output);
    }

    private sealed class Scope(
        TypeCheckContext context,
        Dictionary<string, IrValue>? state = null,
        Dictionary<string, IrValue>? locals = null)
    {
        internal readonly TypeCheckContext Context = context;

        internal readonly Dictionary<string, IrValue> Locals =
            locals ?? new Dictionary<string, IrValue>(StringComparer.Ordinal);

        internal readonly List<IrOperation> Operations = [];
        internal readonly List<IrValue> Parameters = [];

        internal readonly Dictionary<string, IrValue> State =
            state ?? new Dictionary<string, IrValue>(StringComparer.Ordinal);
    }
}
