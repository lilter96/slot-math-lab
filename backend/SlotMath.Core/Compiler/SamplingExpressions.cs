using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;
using SlotMath.Core.Random;

namespace SlotMath.Core.Compiler;

/// <summary>
///     Specializes AST dispatch, field lookup, and constants once per graph.
///     All amounts still use the exact rational expression value; no double math.
///     An expression starts as a closure tree and is replaced by generated IL
///     (<see cref="SamplingCodegen" />) once it is hot.
///     <para>
///         <paramref name="constants" /> holds state fields that keep their initial
///         value for the whole round because nothing writes them. A read of such a
///         field is its value, and a conditional on one keeps only the branch taken.
///     </para>
/// </summary>
internal sealed class SamplingExpressions(
    Func<string, int> slot,
    SamplingTier? tier = null,
    IReadOnlyDictionary<string, ExprValue>? constants = null,
    SamplingShapes? shapes = null,
    IReadOnlySet<string>? maskFields = null,
    IReadOnlySet<string>? inlineMaskFields = null,
    CompilerIr? ir = null,
    bool optimizeCollections = true)
{
    private static readonly IReadOnlyDictionary<string, ExprValue> None = new Dictionary<string, ExprValue>();
    private readonly IReadOnlyDictionary<string, ExprValue> _constants = constants ?? None;
    private readonly IReadOnlySet<string> _inlineMaskFields = inlineMaskFields ?? SamplingMaskState.None;

    private readonly ConcurrentDictionary<string, SamplingIntegerTable?> _integerTables = new(StringComparer.Ordinal);
    private readonly IReadOnlySet<string> _maskFields = maskFields ?? SamplingMaskState.None;

    // Code is generated while other workers evaluate, so lookups must tolerate a concurrent insert.
    private readonly ConcurrentDictionary<string, ExprValue> _numbers =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<Expression, PrecomputationPlan> _precomputationPlans =
        new(ReferenceEqualityComparer.Instance);

    private readonly ConcurrentDictionary<ReachableExpr, ReachabilityData.Prepared?> _reachabilityPlans =
        new(ReferenceEqualityComparer.Instance);

    private readonly SamplingTier _tier = tier ?? SamplingOptions.Tier;
    internal SamplingShapes Shapes { get; } = shapes ?? new SamplingShapes();
    internal CompilerIr? Ir { get; } = ir;

    internal PrecomputationPlan Precomputation(Expression expression) => _precomputationPlans.GetOrAdd(expression,
        source => Shapes.Precomputations.Get(source,
            name => _constants.TryGetValue(name, out ExprValue value) ? value : null));

    internal ReachabilityData.Prepared? Reachability(ReachableExpr expression) => _reachabilityPlans.GetOrAdd(
        expression,
        source => TryConstant(source.Adjacency, out ExprValue adjacency)
            ? ReachabilityData.Prepare(adjacency, source.VertexCount)
            : null);

    internal CollectionBufferAnalysis? Buffers(Expression expression)
    {
        if (!optimizeCollections)
        {
            return null;
        }

        ExpressionIr? owned = Ir?.Expressions.Values.FirstOrDefault(root => ReferenceEquals(root.Source, expression));
        ExpressionIr source = owned ?? ExpressionIrBuilder.Build(expression, TypeCheckContext.Default);
        return source.Collections.Count == 0 ? null : source.Buffers;
    }

    internal bool IsMaskField(string name) => _maskFields.Contains(name);

    internal bool IsInlineMaskField(string name) => _inlineMaskFields.Contains(name);

    internal SamplingIntegerTable? IntegerTable(string name) => _integerTables.GetOrAdd(name,
        key => _constants.TryGetValue(key, out ExprValue source) ? SamplingIntegerTable.Prepare(source) : null);

    internal bool TryConstant(Expression expression, out ExprValue value)
    {
        value = default;
        return expression is FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } field &&
               _constants.TryGetValue(field.Path[0], out value);
    }

    internal bool TryConstantBool(Expression expression, out bool value)
    {
        if (TryConstant(expression, out ExprValue constant) && constant.IsBoolean)
        {
            value = constant.BoolValue;
            return true;
        }

        if (expression is ConstantExpr { Kind: ConstantKind.Boolean } literal &&
            bool.TryParse(literal.Value, out value))
        {
            return true;
        }

        if (expression is NotExpr not && TryConstantBool(not.Expr, out value))
        {
            value = !value;
            return true;
        }

        if (expression is BinaryExpr { Op: BinaryOp.And or BinaryOp.Or } binary &&
            TryConstantBool(binary.Left, out value))
        {
            if (binary.Op == BinaryOp.And ? !value : value)
            {
                return true;
            }

            return TryConstantBool(binary.Right, out value);
        }

        value = false;
        return false;
    }

    /// <summary>The expression that is actually evaluated once conditionals on constant state are decided.</summary>
    internal Expression Reduce(Expression expression)
    {
        while (expression is IfExpr branch && TryConstantBool(branch.Condition, out var taken))
        {
            expression = taken ? branch.ThenExpr : branch.ElseExpr;
        }

        return expression;
    }

    // An iteration variable is assigned while its body runs, so it can never be constant state.
    internal int Variable(string name) =>
        _constants.ContainsKey(name) ? throw new ConstantStateConflict(name) : slot(name);

    public void Remember(ExprValue value)
    {
        // Cache only valid integer symbols without interpreting every text
        // label as an explicit conversion. Invalid strings remain errors when
        // tonumber is actually evaluated, including in conditional branches.
        if (value.Kind == ExprType.String && value.StringValue is { Length: <= 4096 } s
                                          && BigInteger.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                              out BigInteger integer))
        {
            _numbers.TryAdd(s, ExprValue.Number(integer));
        }

        if (value.Kind == ExprType.Array)
        {
            foreach (ExprValue item in value.ArrayValue!)
            {
                Remember(item);
            }
        }
    }

    public Func<SamplingFrame, ExprValue> Compile(Expression expression) => Bind(expression).Entry;

    internal SamplingRunDelegate BindRun(IReadOnlyList<Node> nodes, IReadOnlyDictionary<string, Expression> expressions,
        BigInteger scale,
        Func<SamplingFrame, SeededRandom, object?, object?> interpreted,
        bool discardOutput = false,
        Func<DrawNode, SamplingDraw>? bindDraw = null) =>
        new(interpreted,
            () => SamplingCodegen.CompileRun(this, slot, nodes, expressions, scale, discardOutput, bindDraw), _tier);

    /// <summary>Binds field slots and constants now; code generation may follow later.</summary>
    public SamplingDelegate<ExprValue> Bind(Expression expression)
    {
        Func<SamplingFrame, ExprValue> interpreted = Interpret(expression);
        // A constant or a plain field read is already a single call.
        var simple = Reduce(expression) is ConstantExpr or FieldAccessExpr;
        return new SamplingDelegate<ExprValue>(interpreted,
            simple ? null : () => SamplingCodegen.CompileValue(this, slot, expression), _tier);
    }

    internal SamplingDelegate<bool> BindWrite(Expression expression, int destination, bool integer,
        bool inlineMask = false)
    {
        Func<SamplingFrame, ExprValue> interpreted = Interpret(expression);

        bool Write(SamplingFrame frame)
        {
            ExprValue value = interpreted(frame);
            frame.Cells[destination].SetTyped(integer ? ExprValue.Number(value.AsInteger()) : value);
            return true;
        }

        return new SamplingDelegate<bool>(Write,
            () => SamplingCodegen.CompileWrite(this, slot, expression, destination, integer, inlineMask),
            _tier);
    }

    /// <summary>A condition that must evaluate to a Boolean, as a typed predicate.</summary>
    public SamplingDelegate<bool> BindCondition(Expression expression)
    {
        Func<SamplingFrame, ExprValue> interpreted = Interpret(expression);
        return new SamplingDelegate<bool>(frame => RequiredBoolean(interpreted(frame)),
            Reduce(expression) is ConstantExpr or FieldAccessExpr
                ? null
                : () => SamplingCodegen.CompileCondition(this, slot, expression), _tier);
    }

    private Func<SamplingFrame, ExprValue> Interpret(Expression expression) => Interpret(expression, Interpret);

    /// <summary>
    ///     The closure implementation of one node. <paramref name="compile" />
    ///     supplies the implementation of its operands.
    /// </summary>
    internal Func<SamplingFrame, ExprValue> Interpret(Expression expression,
        Func<Expression, Func<SamplingFrame, ExprValue>> compile)
    {
        if (expression.Precompute is not null)
        {
            Func<SamplingFrame, ExprValue> fallback = Interpret(expression with
            {
                Precompute = null
            }, compile);
            PrecomputationPlan plan = Precomputation(expression);
            if (plan.Table is not { } table)
            {
                return fallback;
            }

            var inputs = table.Domains
                .Select(domain => domain.Scope == PrecomputationScope.State ? slot(domain.Name) : -1).ToArray();
            return frame => table.TryLookup(frame, inputs, out ExprValue result) ? result : fallback(frame);
        }

        switch (expression)
        {
            case LetExpr let:
            {
                Func<SamplingFrame, ExprValue> value = compile(let.Value);
                Func<SamplingFrame, ExprValue> letBody = compile(let.Body);
                return frame =>
                {
                    ExprValue evaluated = value(frame);
                    LexicalBinding? previous = frame.Locals;
                    frame.Locals = new LexicalBinding(let.Name, evaluated, previous);
                    try
                    {
                        return letBody(frame);
                    }
                    finally
                    {
                        frame.Locals = previous;
                    }
                };
            }
            case LocalExpr local:
                return frame => LexicalValues.Read(frame.Locals, local.Name, local.Path);
            case ProjectExpr project:
            {
                Func<SamplingFrame, ExprValue> value = compile(project.Value);
                return frame => LexicalValues.Project(value(frame), project.Path);
            }
            case RecordExpr record:
                return Record(record.Fields.Select(field => field.Name).ToArray(),
                    record.Fields.Select(field => field.Value).ToArray(), compile);
            case TupleExpr tuple:
                return Record(LexicalValues.TupleNames(tuple.Items.Length), tuple.Items, compile, "tuple");
            case ConstantExpr c:
                ExprValue constant;
                try
                {
                    constant = ExactExpressionEvaluator.Evaluate(c, EvalContext.Empty);
                }
                catch (Exception ex) when (ex is FormatException or DivideByZeroException
                                               or ExpressionEvaluationException)
                {
                    return _ => ExactExpressionEvaluator.Evaluate(c, EvalContext.Empty);
                }

                Remember(constant);
                return _ => constant;
            case FieldAccessExpr f:
                if (f.Target == "measurement")
                {
                    return s => f.Path.Length == 1 && s.Settlement is { } settlement
                        ? settlement.Read(f.Path[0])
                        : throw new ExpressionEvaluationException("EVAL_MISSING_SETTLEMENT",
                            "Settlement fields require completed-round measurement context.");
                }

                if (f.Target is not (null or "state" or "board"))
                {
                    return _ => throw new ExpressionEvaluationException("EVAL_INVALID_TARGET",
                        "Unknown field namespace.", f.Target);
                }

                if (f.Path.Length == 0 || f.Path.Any(string.IsNullOrEmpty))
                {
                    return _ => throw new ExpressionEvaluationException("EVAL_INVALID_PATH",
                        "A field path must contain nonempty segments.", string.Join('.', f.Path));
                }

                var key = f.Path[0];
                var index = slot(key);
                var location = string.Join('.', f.Path);
                if (f.Path.Length == 1)
                {
                    return _constants.TryGetValue(key, out ExprValue unchanging)
                        ? _ => unchanging
                        : s => s.Read(index, location);
                }

                if (f.Path.Length == 2 && int.TryParse(f.Path[1], out var itemIndex))
                {
                    return s => s.Cells[index].IsRecord
                        ? ReadNested(s)
                        : s.Cells[index].IndexedField(itemIndex, key, location);
                }

                // Records have their original CLR representation. This uncommon
                // path retains reference semantics, including located errors.
                return ReadNested;

                ExprValue ReadNested(SamplingFrame frame)
                {
                    return frame.Cells[index].Present
                        ? StateValues.Read(frame.Cells[index].Export(), f.Path[1..], location)
                        : throw new ExpressionEvaluationException("EVAL_MISSING_STATE",
                            $"State field '{key}' is absent.",
                            location);
                }
            case IfExpr i:
                Func<SamplingFrame, ExprValue> condition = compile(i.Condition);
                Func<SamplingFrame, ExprValue> yes = compile(i.ThenExpr);
                Func<SamplingFrame, ExprValue> no = compile(i.ElseExpr);
                if (TryConstantBool(i.Condition, out var taken))
                {
                    return taken ? yes : no;
                }

                return s => Truth(condition(s)) ? yes(s) : no(s);
            case NotExpr n:
                Func<SamplingFrame, ExprValue> operand = compile(n.Expr);
                return s => ExprValue.Bool(!Truth(operand(s)));
            case ReachableExpr reachable:
                Func<SamplingFrame, ExprValue> adjacency = compile(reachable.Adjacency);
                Func<SamplingFrame, ExprValue> allowed = compile(reachable.Allowed);
                Func<SamplingFrame, ExprValue> seeds = compile(reachable.Seeds);
                if (Reachability(reachable) is { } prepared)
                {
                    return frame => prepared.Evaluate(allowed(frame), seeds(frame), frame.CancellationToken);
                }

                return frame => ReachabilityData.Evaluate(adjacency(frame), allowed(frame), seeds(frame),
                    reachable.VertexCount, frame.CancellationToken);
            case ScatterExpr scatter:
                Func<SamplingFrame, ExprValue> target = compile(scatter.Target),
                    destinations = compile(scatter.Indices),
                    updates = compile(scatter.Values);
                return frame => frame.Scatter(target(frame), destinations(frame), updates(frame), scatter.Mode,
                    scatter.MaxItems, scatter.MaxUpdates, false);
            case PartitionIndicesExpr partition:
                Func<SamplingFrame, ExprValue> labels = compile(partition.Labels);
                return frame => frame.Partition(labels(frame), partition.BucketCount, partition.MaxItems);
            case ScanExpr scan:
                Func<SamplingFrame, ExprValue> scanSource = compile(scan.Source);
                return frame => frame.Scan(scanSource(frame), scan.Mode, scan.MaxItems, false);
            case SegmentedCompactExpr compact:
                Func<SamplingFrame, ExprValue> keep = compile(compact.Keep);
                Func<SamplingFrame, ExprValue> boundaries = compile(compact.Boundaries);
                return frame => frame.Compact(keep(frame), boundaries(frame), compact.Packing, compact.MaxItems,
                    compact.MaxSegments);
            case BinaryExpr b:
                Func<SamplingFrame, ExprValue> left = compile(b.Left);
                Func<SamplingFrame, ExprValue> right = compile(b.Right);
                if (b.Op is BinaryOp.And or BinaryOp.Or)
                {
                    return s =>
                    {
                        var l = Truth(left(s));
                        if (b.Op == BinaryOp.Or ? l : !l)
                        {
                            return ExprValue.Bool(l);
                        }

                        var r = Truth(right(s));
                        return ExprValue.Bool(b.Op == BinaryOp.Or ? l || r : l && r);
                    };
                }

                return s =>
                {
                    ExprValue l = left(s);
                    ExprValue r = right(s);
                    if (l.Kind != ExprType.Number || r.Kind != ExprType.Number)
                    {
                        throw new ExpressionEvaluationException("EVAL_TYPE_ERROR",
                            $"Operator {b.Op} requires numeric operands.");
                    }

                    return b.Op switch
                    {
                        BinaryOp.Add => ExprValue.Add(l, r),
                        BinaryOp.Sub => ExprValue.Sub(l, r),
                        BinaryOp.Mul => ExprValue.Mul(l, r),
                        BinaryOp.Div => ExprValue.Div(l, r),
                        _ => throw new ExpressionEvaluationException("EVAL_INVALID_OPERATOR",
                            "Unknown binary operator.")
                    };
                };
            case CompareExpr c:
                Func<SamplingFrame, ExprValue> cl = compile(c.Left);
                Func<SamplingFrame, ExprValue> cr = compile(c.Right);
                return s => ExprValue.Bool(CompareValues(c.Op, cl(s), cr(s)));
            case CallExpr c:
                var function = c.Function.ToLowerInvariant();
                if (function == "floor" && c.Args is [BinaryExpr { Op: BinaryOp.Div } quotient])
                {
                    Func<SamplingFrame, ExprValue> numerator = compile(quotient.Left),
                        denominator = compile(quotient.Right);
                    return frame => SamplingOps.FloorDivide(numerator(frame), denominator(frame));
                }

                Func<SamplingFrame, ExprValue>[] args = c.Args.Select(compile).ToArray();
                return s =>
                {
                    ExprValue a = args.Length > 0 ? args[0](s) : ExprValue.Number(0);
                    ExprValue b = args.Length > 1 ? args[1](s) : ExprValue.Number(0);
                    for (var j = 2; j < args.Length; j++)
                    {
                        args[j](s); // arguments are eager, including unused ones
                    }

                    return Call(function, a, b, args.Length);
                };
            case BitSetExpr set:
                Func<SamplingFrame, ExprValue>[] operands = set.Args.Select(compile).ToArray();
                return s =>
                {
                    ExprValue first = operands.Length > 0 ? operands[0](s) : default;
                    ExprValue second = operands.Length > 1 ? operands[1](s) : default;
                    for (var j = 2; j < operands.Length; j++)
                    {
                        operands[j](s);
                    }

                    return BitSetOperations.Evaluate(set.Op, operands.Length, first, second);
                };
            case FoldExpr f:
                Func<SamplingFrame, SamplingCollectionValue> readSource = BindCollection(f, compile);
                var acc = Variable(f.AccName);
                var item = Variable(f.ItemName);
                var position = f.IndexName is null ? -1 : Variable(f.IndexName);
                Func<SamplingFrame, ExprValue> init = compile(f.Init);
                Func<SamplingFrame, ExprValue> body = compile(f.Body);
                Func<SamplingFrame, ExprValue>? guard = f.WhileCondition is null ? null : compile(f.WhileCondition);
                return s =>
                {
                    ExprValue value = init(s);
                    SamplingCollectionValue collection = readSource(s);
                    SamplingCell array = collection.Cell;
                    var length = collection.Length;
                    if (guard is not null)
                    {
                        s.CancellationToken.ThrowIfCancellationRequested();
                    }

                    SamplingCell savedAcc = s.Cells[acc];
                    SamplingCell savedItem = s.Cells[item];
                    SamplingCell savedIndex = position < 0 ? default : s.Cells[position];
                    try
                    {
                        for (var j = 0; j < length; j++)
                        {
                            s.CheckCancellation();
                            if (guard is not null)
                            {
                                s.CancellationToken.ThrowIfCancellationRequested();
                            }

                            s.Cells[acc] = SamplingCell.Typed(value);
                            if (guard is null)
                            {
                                s.Cells[item] = array.Item(j);
                            }

                            if (position >= 0)
                            {
                                s.Cells[position] = SamplingCell.FromInt(j);
                            }

                            if (guard is not null)
                            {
                                s.Cells[item] = default;
                                if (!Truth(guard(s)))
                                {
                                    break;
                                }

                                s.Cells[item] = array.Item(j);
                            }

                            ExprType accumulatorType = value.Kind;
                            value = body(s);
                            if (guard is not null)
                            {
                                value = CollectionInputs.CheckAccumulator(value, accumulatorType);
                            }
                        }

                        return value;
                    }
                    finally
                    {
                        s.Cells[acc] = savedAcc;
                        s.Cells[item] = savedItem;
                        if (position >= 0)
                        {
                            s.Cells[position] = savedIndex;
                        }
                    }
                };
            case MapExpr m:
                return CompileMap(BindCollection(m, compile), Variable(m.ItemName),
                    m.IndexName is null ? -1 : Variable(m.IndexName), compile(m.Body), false);
            case FilterExpr f:
                return CompileMap(BindCollection(f, compile), Variable(f.ItemName),
                    f.IndexName is null ? -1 : Variable(f.IndexName), compile(f.Predicate), true);
            case AggregateExpr a:
                if (a.Func is not (AggregateFunc.Sum or AggregateFunc.Product or AggregateFunc.Min or AggregateFunc.Max
                    or AggregateFunc.Count))
                {
                    throw new ExpressionEvaluationException("EVAL_INVALID_OPERATOR", "Unknown aggregate operator.");
                }

                Func<SamplingFrame, SamplingCollectionValue> readAggregate = BindCollection(a, compile);
                var binding = Variable(a.ItemName);
                Func<SamplingFrame, ExprValue>? predicate = a.Predicate is null ? null : compile(a.Predicate);
                Func<SamplingFrame, ExprValue>? selector = a.ValueExpr is null ? null : compile(a.ValueExpr);
                return s =>
                {
                    SamplingCollectionValue collection = readAggregate(s);
                    SamplingCell array = collection.Cell;
                    var length = collection.Length;
                    SamplingCell saved = s.Cells[binding];
                    var count = 0;
                    ExprValue total = ExprValue.Number(a.Func == AggregateFunc.Product ? 1 : 0);
                    try
                    {
                        for (var j = 0; j < length; j++)
                        {
                            s.CheckCancellation();
                            SamplingCell cell = array.Item(j);
                            s.Cells[binding] = cell;
                            if (predicate is not null && !Truth(predicate(s)))
                            {
                                continue;
                            }

                            ExprValue value = selector is not null ? selector(s) :
                                cell.Value.Kind == ExprType.String ? ParseNumber(cell.Value.StringValue!) : cell.Value;
                            if (a.Func != AggregateFunc.Count)
                            {
                                if (selector is null && value.Kind != ExprType.Number)
                                {
                                    value = ExprValue.Number(0);
                                }

                                total = NumericFunctions.Aggregate(a.Func, total, value, count == 0);
                            }

                            count++;
                        }

                        if (count == 0 && a.Func is AggregateFunc.Min or AggregateFunc.Max)
                        {
                            throw new ExpressionEvaluationException(EvalErrorCodes.EmptyMinMax,
                                $"{a.Func} over an empty array is undefined (D1).",
                                a.Func.ToString().ToLowerInvariant());
                        }

                        return a.Func == AggregateFunc.Count ? ExprValue.Number(count) : total;
                    }
                    finally
                    {
                        s.Cells[binding] = saved;
                    }
                };
            default: throw new NotSupportedException($"Sampling plan does not support {expression.GetType().Name}.");
        }
    }

    private Func<SamplingFrame, SamplingCollectionValue> BindCollection(Expression expression,
        Func<Expression, Func<SamplingFrame, ExprValue>> compile)
    {
        CollectionInput input = CollectionInputs.Of(expression);
        if (expression is FoldExpr { WhileCondition: not null })
        {
            if (input.Source is { } indexedSource)
            {
                Func<SamplingFrame, ExprValue> value = compile(indexedSource);
                return frame => SamplingCollectionSource.ReadIndexed(SamplingCell.ArraySource(value(frame)),
                    input.MaxItems ?? -1, input.Key, input.Location);
            }

            var indexedSlot = slot(input.StateKey!);
            return frame => SamplingCollectionSource.ReadIndexed(frame.Cells[indexedSlot], input.MaxItems ?? -1,
                input.Key, input.Location);
        }

        if (input.Source is { } source)
        {
            Func<SamplingFrame, ExprValue> value = compile(source);
            return frame => SamplingCollectionSource.Read(SamplingCell.Typed(value(frame)), input.MaxItems ?? -1,
                input.Key, input.Location);
        }

        var index = slot(input.StateKey!);
        if (input.MaxItems is { } bound)
        {
            return frame => SamplingCollectionSource.Read(frame.Cells[index], bound, input.Key, input.Location);
        }

        return frame =>
        {
            SamplingCell cell = frame.Cells[index];
            return new SamplingCollectionValue(cell, cell.RequireArrayCount(input.Key));
        };
    }

    private static Func<SamplingFrame, ExprValue> CompileMap(Func<SamplingFrame, SamplingCollectionValue> readSource,
        int binding, int position,
        Func<SamplingFrame, ExprValue> body, bool filter) => s =>
    {
        SamplingCollectionValue collection = readSource(s);
        SamplingCell array = collection.Cell;
        var length = collection.Length;
        SamplingCell savedItem = s.Cells[binding];
        SamplingCell savedIndex = position < 0 ? default : s.Cells[position];
        var values = new ExprValue[length];
        var count = 0;
        try
        {
            for (var j = 0; j < length; j++)
            {
                s.CheckCancellation();
                SamplingCell cell = array.Item(j);
                s.Cells[binding] = cell;
                if (position >= 0)
                {
                    s.Cells[position] = SamplingCell.FromInt(j);
                }

                ExprValue result = body(s);
                if (!filter || Truth(result))
                {
                    values[count++] = filter ? cell.FilterValue : result;
                }
            }

            if (count != values.Length)
            {
                Array.Resize(ref values, count);
            }

            return ExprValue.Array(values);
        }
        finally
        {
            s.Cells[binding] = savedItem;
            if (position >= 0)
            {
                s.Cells[position] = savedIndex;
            }
        }
    };

    public static bool Truth(ExprValue v) => RequiredBoolean(v);

    public static bool RequiredBoolean(ExprValue v) => v.Kind == ExprType.Boolean
        ? v.BoolValue
        : throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "Boolean expression required.");

    private static ExprValue ParseNumber(string s) =>
        ExprValue.Number(BigInteger.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger n)
            ? n
            : 0);

    private static Func<SamplingFrame, ExprValue> Record(string[] names, Expression[] fields,
        Func<Expression, Func<SamplingFrame, ExprValue>> compile, string location = "record")
    {
        if (fields.Length > LexicalValues.MaxFields)
        {
            return _ => throw new ExpressionEvaluationException("EVAL_RECORD_BUDGET",
                $"A {location} exceeds 64 fields.", location);
        }

        Func<SamplingFrame, ExprValue>[] values = fields.Select(compile).ToArray();
        return frame =>
        {
            var evaluated = new ExprValue[values.Length];
            for (var index = 0; index < values.Length; index++)
            {
                evaluated[index] = values[index](frame);
            }

            return LexicalValues.Record(names, evaluated);
        };
    }

    internal static bool CompareValues(CompareOp op, ExprValue l, ExprValue r)
    {
        if (op is CompareOp.Eq or CompareOp.Neq && l.Kind == r.Kind &&
            l.Kind is ExprType.Record or ExprType.Array or ExprType.Null)
        {
            return op == CompareOp.Eq ? l.Equals(r) : !l.Equals(r);
        }

        if (l.Kind == ExprType.Boolean && r.Kind == ExprType.Boolean)
        {
            return op switch
            {
                CompareOp.Eq => l.BoolValue == r.BoolValue,
                CompareOp.Neq => l.BoolValue != r.BoolValue,
                _ => false
            };
        }

        var cmp = l.Kind == ExprType.Number && r.Kind == ExprType.Number
            ? ExprValue.CompareNumbers(l, r)
            : string.Compare(l.Kind == ExprType.String ? l.StringValue : l.ToString(),
                r.Kind == ExprType.String ? r.StringValue : r.ToString(), StringComparison.Ordinal);
        return op switch
        {
            CompareOp.Eq => cmp == 0,
            CompareOp.Neq => cmp != 0,
            CompareOp.Lt => cmp < 0,
            CompareOp.Gt => cmp > 0,
            CompareOp.Lte => cmp <= 0,
            CompareOp.Gte => cmp >= 0,
            _ => false
        };
    }

    internal ExprValue Call(string function, ExprValue a, ExprValue b, int count) =>
        function == "tonumber" && count == 1 ? ToNumber(a) : CollectionFunctions.Call(function, a, b, count);

    internal ExprValue ToNumber(ExprValue a)
    {
        if (a.TryGetNumericText(out var inline))
        {
            return ExprValue.Number(inline);
        }

        return a.Kind is ExprType.String or ExprType.Symbol &&
               _numbers.TryGetValue(a.StringValue!, out ExprValue number)
            ? number
            : CollectionFunctions.Call("tonumber", a, default, 1);
    }
}

/// <summary>
///     A field assumed to keep its initial value turned out to be
///     assigned. The plan is then built without constant state.
/// </summary>
internal sealed class ConstantStateConflict(string key)
    : Exception($"State field '{key}' is assigned and cannot be treated as constant.");
