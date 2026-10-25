using SlotMath.Core.Model;

namespace SlotMath.Core.Expressions;

// ═══════════════════════════════════════════════════════════════════════════
//  ExpressionTypeChecker — bottom-up type inference for level-(b) expressions
//
//  Walks the AST once.  Returns a list of errors (empty ⇒ valid).
//  The grammar makes loops/recursion unrepresentable by construction:
//  - The AST is a tree (no cycles, no recursive bindings)
//  - CallExpr is restricted to a closed set of built-in functions
//    (none of which invoke expressions recursively)
// ═══════════════════════════════════════════════════════════════════════════

public static class ExpressionTypeChecker
{
    /// <summary>
    ///     Check an expression and return all type errors.  Empty list = valid.
    /// </summary>
    public static IReadOnlyList<TypeCheckError> Check(Expression expr, TypeCheckContext ctx)
    {
        var errors = new List<TypeCheckError>();
        Infer(expr, ctx, errors);
        return errors;
    }

    /// <summary>
    ///     Check and also verify the expression type matches the expected type.
    /// </summary>
    public static IReadOnlyList<TypeCheckError> Check(
        Expression expr, TypeCheckContext ctx, ExprType expectedType)
    {
        var errors = new List<TypeCheckError>();
        ExprType inferred = Infer(expr, ctx, errors);

        if (errors.Count == 0 && inferred != expectedType && inferred != ExprType.Error)
        {
            // Allow Symbol → String coercion
            if (!(inferred == ExprType.Symbol && expectedType == ExprType.String))
            {
                errors.Add(Error(expr,
                    $"Type mismatch: expression has type {inferred} but {expectedType} is required."));
            }
        }

        return errors;
    }

    /// <summary>
    ///     Best-effort inference of an expression's result type, ignoring errors.
    ///     Used by state-schema derivation (Graph Truth → Everything Derived): a
    ///     state field's type is the type of the expression that writes it.
    /// </summary>
    public static ExprType InferType(Expression expr, TypeCheckContext ctx)
    {
        var errors = new List<TypeCheckError>();
        return Infer(expr, ctx, errors);
    }

    // ── Core inference ───────────────────────────────────────────────────

    private static ExprType Infer(Expression expr, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        return expr switch
        {
            LetExpr let => InferLet(let, ctx, errors),
            LocalExpr local => InferLocal(local, ctx, errors),
            RecordExpr record => InferRecord(record, ctx, errors),
            TupleExpr tuple => InferTuple(tuple, ctx, errors),
            ProjectExpr project => InferProject(project, ctx, errors),
            ReachableExpr reachable => InferReachable(reachable, ctx, errors),
            ScanExpr scan => InferScan(scan, ctx, errors),
            ScatterExpr scatter => InferScatter(scatter, ctx, errors),
            PartitionIndicesExpr partition => InferPartition(partition, ctx, errors),
            SegmentedCompactExpr compact => InferCompact(compact, ctx, errors),
            ConstantExpr c => InferConstant(c, ctx, errors),
            FieldAccessExpr f => InferFieldAccess(f, ctx, errors),
            BinaryExpr b => InferBinary(b, ctx, errors),
            CompareExpr c => InferCompare(c, ctx, errors),
            IfExpr i => InferIf(i, ctx, errors),
            AggregateExpr a => InferAggregate(a, ctx, errors),
            NotExpr n => InferNot(n, ctx, errors),
            CallExpr c => InferCall(c, ctx, errors),
            BitSetExpr set => InferBitSet(set, ctx, errors),
            FoldExpr f => InferFold(f, ctx, errors),
            MapExpr m => InferMap(m, ctx, errors),
            FilterExpr fi => InferFilter(fi, ctx, errors),
            _ => Fail(expr, $"Unknown expression type: {expr.GetType().Name}", errors)
        };
    }

    // ── Constant ─────────────────────────────────────────────────────────

    private static ExprType InferReachable(ReachableExpr expression, TypeCheckContext context,
        List<TypeCheckError> errors)
    {
        ExprType adjacency = Infer(expression.Adjacency, context, errors);
        ExprType allowed = Infer(expression.Allowed, context, errors);
        ExprType seeds = Infer(expression.Seeds, context, errors);
        FieldDescriptor? shape = ExpressionShapes.Infer(expression.Adjacency, context);
        var valid = true;
        if (expression.VertexCount is < 0 or > ReachabilityData.MaxVertices)
        {
            errors.Add(Error(expression, "Reachable vertexCount must be in 0..64."));
            valid = false;
        }

        if (adjacency != ExprType.Array || (shape?.ArrayIsEmpty != true &&
                                            ExpressionShapes.Item(shape)?.Type != ExprType.Number))
        {
            errors.Add(Error(expression, "Reachable adjacency must be an array of Number masks."));
            valid = false;
        }

        if (allowed != ExprType.Number || seeds != ExprType.Number)
        {
            errors.Add(Error(expression, "Reachable allowed and seeds must be Number masks."));
            valid = false;
        }

        return valid ? ExprType.Number : ExprType.Error;
    }

    private static bool PlanArray(Expression source, TypeCheckContext context, List<TypeCheckError> errors,
        params ExprType[] itemTypes)
    {
        if (CollectionInputs.HasUnboundedIterator(source))
        {
            errors.Add(Error(source, "Every iterator composed into a bounded plan requires an explicit bound."));
            return false;
        }

        ExprType type = Infer(source, context, errors);
        FieldDescriptor? shape = ExpressionShapes.Infer(source, context);
        return type == ExprType.Array && (shape?.ArrayIsEmpty == true ||
                                          (ExpressionShapes.Item(shape) is { } item && itemTypes.Contains(item.Type)));
    }

    private static ExprType InferScatter(ScatterExpr scatter, TypeCheckContext context, List<TypeCheckError> errors)
    {
        ExprType[] kinds = scatter.Mode == ScatterMode.Sum
            ? [ExprType.Number]
            : [ExprType.Number, ExprType.Boolean, ExprType.String, ExprType.Symbol, ExprType.Record, ExprType.Array];
        var target = PlanArray(scatter.Target, context, errors, kinds);
        var indices = PlanArray(scatter.Indices, context, errors, ExprType.Number);
        var values = PlanArray(scatter.Values, context, errors, kinds);
        if (!target || !indices || !values || scatter.MaxItems is < 0 or > CollectionInputs.MaxItems ||
            scatter.MaxUpdates is < 0 or > CollectionInputs.MaxItems ||
            scatter.Mode is not (ScatterMode.RejectDuplicates or ScatterMode.LastWriteWins or ScatterMode.Sum))
        {
            return Fail(scatter,
                "Scatter requires typed arrays, numeric indices, explicit 0..4096 bounds and a valid duplicate policy.",
                errors);
        }

        FieldDescriptor? a = ExpressionShapes.Infer(scatter.Target, context),
            b = ExpressionShapes.Infer(scatter.Values, context);

        ExprType? Kind(FieldDescriptor? array)
        {
            return ExpressionShapes.Item(array)?.Type == ExprType.Symbol
                ? ExprType.String
                : ExpressionShapes.Item(array)?.Type;
        }

        if (a?.ArrayIsEmpty != true && b?.ArrayIsEmpty != true && Kind(a) != Kind(b))
        {
            return Fail(scatter, "Scatter target and values must have the same element type.", errors);
        }

        return ExprType.Array;
    }

    private static ExprType InferPartition(PartitionIndicesExpr partition, TypeCheckContext context,
        List<TypeCheckError> errors)
    {
        if (!PlanArray(partition.Labels, context, errors, ExprType.Number) ||
            partition.BucketCount is < 0 or > CollectionInputs.MaxItems ||
            partition.MaxItems is < 0 or > CollectionInputs.MaxItems)
        {
            return Fail(partition, "PartitionIndices requires numeric labels and explicit 0..4096 bucket/item bounds.",
                errors);
        }

        return ExprType.Array;
    }

    private static ExprType InferScan(ScanExpr scan, TypeCheckContext context, List<TypeCheckError> errors)
    {
        var valid = PlanArray(scan.Source, context, errors, ExprType.Number, ExprType.Boolean);
        if (scan.MaxItems is < 0 or > CollectionInputs.MaxItems ||
            scan.Mode is not (ScanMode.Inclusive or ScanMode.Exclusive) || !valid)
        {
            return Fail(scan, "Scan requires an integer/Boolean array, explicit 0..4096 bound and valid mode.", errors);
        }

        return ExprType.Array;
    }

    private static ExprType InferCompact(SegmentedCompactExpr compact, TypeCheckContext context,
        List<TypeCheckError> errors)
    {
        var keep = PlanArray(compact.Keep, context, errors, ExprType.Boolean);
        var boundaries = PlanArray(compact.Boundaries, context, errors, ExprType.Number);
        if (!keep || !boundaries || compact.MaxItems is < 0 or > CollectionInputs.MaxItems ||
            compact.MaxSegments is < 0 or > CollectionInputs.MaxItems ||
            compact.Packing is not (SegmentPacking.Start or SegmentPacking.End))
        {
            return Fail(compact,
                "Compaction requires Boolean keep flags, numeric offsets, explicit 0..4096 bounds and valid packing.",
                errors);
        }

        return ExprType.Record;
    }

    private static ExprType InferLet(LetExpr let, TypeCheckContext context, List<TypeCheckError> errors)
    {
        if (string.IsNullOrWhiteSpace(let.Name) || let.Name.Length > 128)
        {
            return Fail(let, "A local name requires 1..128 nonempty characters.", errors);
        }

        ExprType type = Infer(let.Value, context, errors);
        FieldDescriptor? value = ExpressionShapes.Infer(let.Value, context);
        if (type == ExprType.Error || value is null)
        {
            return ExprType.Error;
        }

        return Infer(let.Body, context.WithLocal(let.Name, value), errors);
    }

    private static ExprType InferLocal(LocalExpr local, TypeCheckContext context, List<TypeCheckError> errors)
    {
        FieldDescriptor? value = ExpressionShapes.Infer(local, context);
        return value is null || value.Type is ExprType.Error or ExprType.Null
            ? Fail(local, $"Unknown or unreadable local '{local.Name}'.", errors)
            : value.Type;
    }

    private static ExprType InferRecord(RecordExpr record, TypeCheckContext context, List<TypeCheckError> errors)
    {
        if (record.Fields.Length > LexicalValues.MaxFields || record.Fields.Any(field =>
                                                               string.IsNullOrWhiteSpace(field.Name))
                                                           || record.Fields.Select(field => field.Name)
                                                               .Distinct(StringComparer.Ordinal).Count() !=
                                                           record.Fields.Length)
        {
            return Fail(record, "Records require at most 64 unique nonempty field names.", errors);
        }

        foreach (RecordFieldExpr field in record.Fields)
        {
            Infer(field.Value, context, errors);
        }

        return ExprType.Record;
    }

    private static ExprType InferTuple(TupleExpr tuple, TypeCheckContext context, List<TypeCheckError> errors)
    {
        if (tuple.Items.Length > LexicalValues.MaxFields)
        {
            return Fail(tuple, "Tuples require at most 64 elements.", errors);
        }

        foreach (Expression item in tuple.Items)
        {
            Infer(item, context, errors);
        }

        return ExprType.Record;
    }

    private static ExprType InferProject(ProjectExpr project, TypeCheckContext context, List<TypeCheckError> errors)
    {
        if (Infer(project.Value, context, errors) == ExprType.Error)
        {
            return ExprType.Error;
        }

        FieldDescriptor? value = ExpressionShapes.Project(ExpressionShapes.Infer(project.Value, context), project.Path);
        return value is null || value.Type is ExprType.Error or ExprType.Null
            ? Fail(project, "Projection requires a known readable static path.", errors)
            : value.Type;
    }

    private static ExprType InferConstant(ConstantExpr c, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        return c.Kind switch
        {
            ConstantKind.Integer or ConstantKind.Rational => ExprType.Number,
            ConstantKind.Boolean => ExprType.Boolean,
            ConstantKind.String => ExprType.String,
            _ => Fail(c, $"Unknown constant kind: {c.Kind}", errors)
        };
    }

    // ── Field access ─────────────────────────────────────────────────────

    private static ExprType InferFieldAccess(FieldAccessExpr f, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        if (f.Path.Length == 0)
        {
            return Fail(f, "Field access path is empty.", errors);
        }

        ExprType? resolved = ctx.ResolvePath(f.Path, f.Target);
        if (resolved is null or ExprType.Error)
        {
            var where = f.Target ?? "board";
            return Fail(f,
                $"Unknown field '{string.Join(".", f.Path)}' on {where}.", errors);
        }

        if (resolved == ExprType.Null)
        {
            return Fail(f, $"Field '{string.Join(".", f.Path)}' is null; supply an explicit value before reading it.",
                errors);
        }

        return resolved.Value;
    }

    // ── Binary ───────────────────────────────────────────────────────────

    private static ExprType InferBinary(BinaryExpr b, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        ExprType left = Infer(b.Left, ctx, errors);
        ExprType right = Infer(b.Right, ctx, errors);

        if (left == ExprType.Error || right == ExprType.Error)
        {
            return ExprType.Error;
        }

        return b.Op switch
        {
            BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul or BinaryOp.Div =>
                CheckArithmetic(b, left, right, errors),

            BinaryOp.And or BinaryOp.Or =>
                CheckLogical(b, left, right, errors),

            _ => Fail(b, $"Unknown binary operator: {b.Op}", errors)
        };
    }

    private static ExprType CheckArithmetic(BinaryExpr b, ExprType left, ExprType right,
        List<TypeCheckError> errors)
    {
        if (left != ExprType.Number)
        {
            errors.Add(Error(b, $"Left operand of '{b.Op}' must be Number, got {left}."));
        }

        if (right != ExprType.Number)
        {
            errors.Add(Error(b, $"Right operand of '{b.Op}' must be Number, got {right}."));
        }

        return left == ExprType.Number && right == ExprType.Number ? ExprType.Number : ExprType.Error;
    }

    private static ExprType CheckLogical(BinaryExpr b, ExprType left, ExprType right,
        List<TypeCheckError> errors)
    {
        if (left != ExprType.Boolean)
        {
            errors.Add(Error(b, $"Left operand of '{b.Op}' must be Boolean, got {left}."));
        }

        if (right != ExprType.Boolean)
        {
            errors.Add(Error(b, $"Right operand of '{b.Op}' must be Boolean, got {right}."));
        }

        return left == ExprType.Boolean && right == ExprType.Boolean ? ExprType.Boolean : ExprType.Error;
    }

    // ── Comparison ───────────────────────────────────────────────────────

    private static ExprType InferCompare(CompareExpr c, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        ExprType left = Infer(c.Left, ctx, errors);
        ExprType right = Infer(c.Right, ctx, errors);

        if (left == ExprType.Error || right == ExprType.Error)
        {
            return ExprType.Error;
        }

        // Eq/Neq work on any compatible types; ordering comparisons require Number.
        var isOrdered = c.Op is CompareOp.Lt or CompareOp.Gt or CompareOp.Lte or CompareOp.Gte;

        if (isOrdered)
        {
            if (left != ExprType.Number)
            {
                errors.Add(Error(c, $"Left operand of '{c.Op}' must be Number, got {left}."));
            }

            if (right != ExprType.Number)
            {
                errors.Add(Error(c, $"Right operand of '{c.Op}' must be Number, got {right}."));
            }

            return left == ExprType.Number && right == ExprType.Number ? ExprType.Boolean : ExprType.Error;
        }

        // Eq/Neq: types must match (with Symbol/String coercion)
        ExprType l = left == ExprType.Symbol ? ExprType.String : left;
        ExprType r = right == ExprType.Symbol ? ExprType.String : right;
        if (l != r)
        {
            errors.Add(Error(c, $"Cannot compare {left} with {right} using '{c.Op}'."));
        }

        return ExprType.Boolean;
    }

    // ── Conditional ──────────────────────────────────────────────────────

    private static ExprType InferIf(IfExpr i, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        ExprType cond = Infer(i.Condition, ctx, errors);
        if (cond != ExprType.Boolean && cond != ExprType.Error)
        {
            errors.Add(Error(i, $"If condition must be Boolean, got {cond}."));
        }

        ExprType thenType = Infer(i.ThenExpr, ctx, errors);
        ExprType elseType = Infer(i.ElseExpr, ctx, errors);

        if (thenType == ExprType.Error)
        {
            return elseType;
        }

        if (elseType == ExprType.Error)
        {
            return thenType;
        }

        if (thenType != elseType)
        {
            // Allow Symbol ↔ String coercion
            ExprType t = thenType == ExprType.Symbol ? ExprType.String : thenType;
            ExprType e = elseType == ExprType.Symbol ? ExprType.String : elseType;
            if (t != e)
            {
                errors.Add(Error(i,
                    $"If branches have different types: then={thenType}, else={elseType}."));
                return ExprType.Error;
            }
        }

        return thenType;
    }

    // ── Aggregate ────────────────────────────────────────────────────────

    private static ExprType InferAggregate(AggregateExpr a, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        if (!ValidateCollection(a, ctx, errors))
        {
            return ExprType.Number;
        }

        // Aggregates always return Number. The aggregation iterates the state
        // array named by StateKey (invariant 4); inside the predicate and the
        // value selector the current element is bound under ItemName as a
        // virtual state field, exactly like fold/map/filter.
        List<FieldDescriptor> lambdaFields = ctx.StateFields.ToList();
        lambdaFields.Add(BindItem(a, ctx, a.StateKey, a.ItemName, a.ItemType, errors));

        var lambdaCtx = new TypeCheckContext
        {
            ExpectedType = ExprType.Boolean,
            BoardFields = ctx.BoardFields,
            MeasurementFields = ctx.MeasurementFields,
            LocalFields = ctx.LocalFields,
            StateFields = lambdaFields.GroupBy(field => field.Name, StringComparer.Ordinal)
                .Select(group => group.Last()).ToArray(),
            CellFields = ctx.CellFields,
            DecorationTypes = ctx.DecorationTypes
        };

        if (a.Predicate != null)
        {
            ExprType predType = Infer(a.Predicate, lambdaCtx, errors);
            if (predType != ExprType.Boolean && predType != ExprType.Error)
            {
                errors.Add(Error(a, $"Aggregate predicate must be Boolean, got {predType}."));
            }
        }

        // ValueExpr (selector) is evaluated in the element context; for
        // Sum/Product/Min/Max it must yield a Number.
        if (a.ValueExpr != null)
        {
            var valCtx = new TypeCheckContext
            {
                ExpectedType = ExprType.Number,
                BoardFields = ctx.BoardFields,
                MeasurementFields = ctx.MeasurementFields,
                LocalFields = ctx.LocalFields,
                StateFields = lambdaFields.GroupBy(field => field.Name, StringComparer.Ordinal)
                    .Select(group => group.Last()).ToArray(),
                CellFields = ctx.CellFields,
                DecorationTypes = ctx.DecorationTypes
            };
            ExprType valType = Infer(a.ValueExpr, valCtx, errors);
            if (a.Func is AggregateFunc.Sum or AggregateFunc.Product
                    or AggregateFunc.Min or AggregateFunc.Max
                && valType != ExprType.Number && valType != ExprType.Error)
            {
                errors.Add(Error(a, $"Aggregate {a.Func} value selector must be Number, got {valType}."));
            }
        }

        return ExprType.Number;
    }

    // ── Not ──────────────────────────────────────────────────────────────

    private static ExprType InferNot(NotExpr n, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        ExprType inner = Infer(n.Expr, ctx, errors);
        if (inner != ExprType.Boolean && inner != ExprType.Error)
        {
            errors.Add(Error(n, $"'not' requires Boolean operand, got {inner}."));
        }

        return ExprType.Boolean;
    }

    // ── Call ─────────────────────────────────────────────────────────────

    private static ExprType InferBitSet(BitSetExpr set, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        var arity = BitSetOperations.Arity(set.Op);
        if (arity == 0)
        {
            return Fail(set, "Unknown finite-set operation.", errors);
        }

        if (set.Args.Length != arity)
        {
            return Fail(set, $"Finite-set {set.Op} requires {arity} operands.", errors);
        }

        foreach (Expression argument in set.Args)
        {
            ExprType type = Infer(argument, ctx, errors);
            if (type is not (ExprType.Number or ExprType.Error))
            {
                errors.Add(Error(set, $"Finite-set {set.Op} requires numeric operands, got {type}."));
            }
        }

        return ExprType.Number;
    }

    private static ExprType InferCall(CallExpr c, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        var fn = c.Function.ToLowerInvariant();
        if (fn is "length" or "contains" or "append" or "index")
        {
            var count = fn == "length" ? 1 : 2;
            if (c.Args.Length != count)
            {
                return Fail(c, $"Function '{fn}' expects {count} arguments, got {c.Args.Length}.", errors);
            }

            ExprType first = Infer(c.Args[0], ctx, errors);
            ExprType second = count == 2 ? Infer(c.Args[1], ctx, errors) : ExprType.Error;
            if (first == ExprType.Error || (count == 2 && second == ExprType.Error))
            {
                return ExprType.Error;
            }

            if (fn is "length" or "contains")
            {
                if (first is not (ExprType.Array or ExprType.String or ExprType.Symbol))
                {
                    return Fail(c, $"Function '{fn}' requires an array or text.", errors);
                }

                if (fn == "contains" && first != ExprType.Array && second is not (ExprType.String or ExprType.Symbol))
                {
                    return Fail(c, "String contains requires a text substring.", errors);
                }

                return fn == "length" ? ExprType.Number : ExprType.Boolean;
            }

            if (first != ExprType.Array)
            {
                return Fail(c, $"Function '{fn}' requires an array.", errors);
            }

            if (fn == "append")
            {
                return ExprType.Array;
            }

            if (second != ExprType.Number)
            {
                return Fail(c, "Function 'index' requires a numeric integer position.", errors);
            }

            ExprType? element = ArrayExpressionTypes.Infer(c.Args[0], ctx).ItemType;
            return element ?? Fail(c,
                "Function 'index' requires a known homogeneous array element type; mixed or unknown elements need an explicit typed selector.",
                errors);
        }

        // Built-in functions: a closed set — no user-defined recursion.
        (ExprType returnType, ExprType[] argTypes) = fn switch
        {
            "abs" => (ExprType.Number, new[] { ExprType.Number }),
            "min" => (ExprType.Number, new[] { ExprType.Number, ExprType.Number }),
            "max" => (ExprType.Number, new[] { ExprType.Number, ExprType.Number }),
            "floor" => (ExprType.Number, new[] { ExprType.Number }),
            "ceil" => (ExprType.Number, new[] { ExprType.Number }),
            "round" => (ExprType.Number, new[] { ExprType.Number }),
            "tonumber" => (ExprType.Number, new[] { ExprType.String }),
            "tostring" => (ExprType.String, new[] { ExprType.Number }),
            _ => (ExprType.Error, Array.Empty<ExprType>())
        };

        if (returnType == ExprType.Error)
        {
            return Fail(c, $"Unknown function: '{c.Function}'.", errors);
        }

        if (c.Args.Length != argTypes.Length)
        {
            errors.Add(Error(c,
                $"Function '{c.Function}' expects {argTypes.Length} arguments, got {c.Args.Length}."));
            return ExprType.Error;
        }

        for (var i = 0; i < c.Args.Length; i++)
        {
            ExprType argType = Infer(c.Args[i], ctx, errors);
            if (argType != argTypes[i] && argType != ExprType.Error)
            {
                // Allow Symbol → String coercion
                if (!(argType == ExprType.Symbol && argTypes[i] == ExprType.String))
                {
                    errors.Add(Error(c,
                        $"Function '{c.Function}' argument {i + 1}: expected {argTypes[i]}, got {argType}."));
                }
            }
        }

        return returnType;
    }

    // ── Fold ─────────────────────────────────────────────────────────────

    private static ExprType InferFold(FoldExpr f, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        ExprType initType = Infer(f.Init, ctx, errors);

        if (!ValidateCollection(f, ctx, errors))
        {
            return initType;
        }

        if (f.MaxItems is null && ContainsBoundedIterator(f.Body))
        {
            errors.Add(Error(f,
                "Nested fold is not allowed: a fold body may not contain another fold, map, or filter (one level of bounded iteration keeps the grammar exact-analysable)."));
            return initType;
        }

        if (f.WhileCondition is not null && f.MaxItems is null)
        {
            errors.Add(Error(f, "Fold WhileCondition requires an explicit MaxItems in 0..4096."));
        }

        // Lambda context: acc and item added as virtual state fields
        List<FieldDescriptor> lambdaFields = ctx.StateFields.ToList();
        lambdaFields.Add(ExpressionShapes.Infer(f.Init, ctx) is { } initialShape
            ? initialShape with
            {
                Name = f.AccName
            }
            : new FieldDescriptor
            {
                Name = f.AccName,
                Type = initType
            });
        lambdaFields.Add(BindItem(f, ctx, f.StateKey, f.ItemName, f.ItemType, errors));
        if (f.IndexName != null)
        {
            lambdaFields.Add(new FieldDescriptor
            {
                Name = f.IndexName,
                Type = ExprType.Number
            });
        }

        var lambdaCtx = new TypeCheckContext
        {
            ExpectedType = initType,
            BoardFields = ctx.BoardFields,
            MeasurementFields = ctx.MeasurementFields,
            LocalFields = ctx.LocalFields,
            StateFields = lambdaFields.GroupBy(field => field.Name, StringComparer.Ordinal)
                .Select(group => group.Last()).ToArray(),
            CellFields = ctx.CellFields,
            DecorationTypes = ctx.DecorationTypes
        };

        if (f.WhileCondition is not null)
        {
            TypeCheckContext guardContext = new()
            {
                ExpectedType = ExprType.Boolean,
                BoardFields =
                    ctx.BoardFields.Where(field =>
                        !string.Equals(field.Name, f.ItemName, StringComparison.OrdinalIgnoreCase)).ToArray(),
                MeasurementFields = ctx.MeasurementFields,
                LocalFields = ctx.LocalFields,
                StateFields = lambdaCtx.StateFields.Where(field => field.Name != f.ItemName).ToArray(),
                CellFields =
                    ctx.CellFields.Where(field =>
                        !string.Equals(field.Name, f.ItemName, StringComparison.OrdinalIgnoreCase)).ToArray(),
                DecorationTypes = ctx.DecorationTypes
            };
            ExprType guardType = Infer(f.WhileCondition, guardContext, errors);
            if (guardType is not (ExprType.Boolean or ExprType.Error))
            {
                errors.Add(
                    Error(f, "Fold WhileCondition must be Boolean and cannot read its unavailable current item."));
            }
        }

        ExprType bodyType = Infer(f.Body, lambdaCtx, errors);
        if (bodyType != initType && bodyType != ExprType.Error)
        {
            errors.Add(Error(f,
                $"FoldExpr body returns {bodyType} but must match init type {initType}."));
        }

        return initType;
    }

    // ── Map ──────────────────────────────────────────────────────────────

    private static ExprType InferMap(MapExpr m, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        if (!ValidateCollection(m, ctx, errors))
        {
            return ExprType.Array;
        }

        if (m.MaxItems is null && ContainsBoundedIterator(m.Body))
        {
            errors.Add(Error(m,
                "Nested iteration is not allowed in a map body: map/filter/fold may not be nested " +
                "(one level of bounded iteration keeps the grammar exact-analysable)."));
            return ExprType.Array;
        }

        List<FieldDescriptor> lambdaFields = ctx.StateFields.ToList();
        lambdaFields.Add(BindItem(m, ctx, m.StateKey, m.ItemName, m.ItemType, errors));
        if (m.IndexName != null)
        {
            lambdaFields.Add(new FieldDescriptor
            {
                Name = m.IndexName,
                Type = ExprType.Number
            });
        }

        var lambdaCtx = new TypeCheckContext
        {
            ExpectedType = ExprType.Number,
            BoardFields = ctx.BoardFields,
            MeasurementFields = ctx.MeasurementFields,
            LocalFields = ctx.LocalFields,
            StateFields = lambdaFields.GroupBy(field => field.Name, StringComparer.Ordinal)
                .Select(group => group.Last()).ToArray(),
            CellFields = ctx.CellFields,
            DecorationTypes = ctx.DecorationTypes
        };

        Infer(m.Body, lambdaCtx, errors);
        return ExprType.Array;
    }

    // ── Filter ───────────────────────────────────────────────────────────

    private static ExprType InferFilter(FilterExpr fi, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        if (!ValidateCollection(fi, ctx, errors))
        {
            return ExprType.Array;
        }

        if (fi.MaxItems is null && ContainsBoundedIterator(fi.Predicate))
        {
            errors.Add(Error(fi,
                "Nested iteration is not allowed in a filter predicate: map/filter/fold may not be nested " +
                "(one level of bounded iteration keeps the grammar exact-analysable)."));
            return ExprType.Array;
        }

        List<FieldDescriptor> lambdaFields = ctx.StateFields.ToList();
        lambdaFields.Add(BindItem(fi, ctx, fi.StateKey, fi.ItemName, fi.ItemType, errors));
        if (fi.IndexName != null)
        {
            lambdaFields.Add(new FieldDescriptor
            {
                Name = fi.IndexName,
                Type = ExprType.Number
            });
        }

        var lambdaCtx = new TypeCheckContext
        {
            ExpectedType = ExprType.Boolean,
            BoardFields = ctx.BoardFields,
            MeasurementFields = ctx.MeasurementFields,
            LocalFields = ctx.LocalFields,
            StateFields = lambdaFields.GroupBy(field => field.Name, StringComparer.Ordinal)
                .Select(group => group.Last()).ToArray(),
            CellFields = ctx.CellFields,
            DecorationTypes = ctx.DecorationTypes
        };

        ExprType predType = Infer(fi.Predicate, lambdaCtx, errors);
        if (predType != ExprType.Boolean && predType != ExprType.Error)
        {
            errors.Add(Error(fi, $"Filter predicate must be Boolean, got {predType}."));
        }

        return ExprType.Array;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static bool ValidateCollection(Expression expression, TypeCheckContext context, List<TypeCheckError> errors)
    {
        CollectionInput input = CollectionInputs.Of(expression);
        if (input.StateKey is null == input.Source is null ||
            (input.StateKey is not null && string.IsNullOrWhiteSpace(input.StateKey)))
        {
            errors.Add(Error(expression, "A collection requires exactly one nonempty StateKey or Source."));
            return false;
        }

        if (input.MaxItems is < 0 or > CollectionInputs.MaxItems ||
            (input.Source is not null && input.MaxItems is null))
        {
            errors.Add(Error(expression,
                "Source requires MaxItems in 0..4096; a StateKey may declare the same bound."));
            return false;
        }

        string?[] bindings = expression switch
        {
            MapExpr e => [e.ItemName, e.IndexName],
            FilterExpr e => [e.ItemName, e.IndexName],
            FoldExpr e => [e.AccName, e.ItemName, e.IndexName],
            AggregateExpr e => [e.ItemName],
            _ => []
        };
        var names = bindings.OfType<string>().ToArray();
        if (input.MaxItems is not null && (names.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 128)
                                           || names.Distinct(StringComparer.Ordinal).Count() != names.Length))
        {
            errors.Add(Error(expression,
                "Bounded iterator bindings require distinct nonblank names of at most 128 characters."));
            return false;
        }

        if (input.MaxItems is not null &&
            CollectionInputs.Children(expression).Any(CollectionInputs.HasUnboundedIterator))
        {
            errors.Add(Error(expression,
                "Every iterator composed within a bounded collection requires an explicit MaxItems bound."));
            return false;
        }

        if (input.MaxItems is not null && input.Source is null)
        {
            FieldDescriptor? shape = CollectionInputs.Shape(input, context);
            if (shape is not null && shape.Type != ExprType.Array)
            {
                errors.Add(Error(expression, "Bounded collection StateKey must name an array."));
                return false;
            }
        }

        if (input.Source is not null && Infer(input.Source, context, errors) is not (ExprType.Array or ExprType.Error))
        {
            errors.Add(Error(expression, "Collection Source must be an array-valued expression."));
            return false;
        }

        return true;
    }

    private static FieldDescriptor BindItem(Expression expression, TypeCheckContext context, string? source,
        string name,
        ExprType declared, List<TypeCheckError> errors)
    {
        FieldDescriptor? known =
            ExpressionShapes.Item(CollectionInputs.Shape(CollectionInputs.Of(expression), context));
        if (known is not null && known.Type != declared)
        {
            errors.Add(Error(expression,
                $"Iterator item type {declared} differs from the {known.Type} items in '{source}'."));
        }

        return ExpressionShapes.IteratorItem(context, source, name, declared, CollectionInputs.Of(expression).Source);
    }

    /// <summary>
    ///     True if the expression tree contains a fold, map, or filter anywhere.
    ///     Used to enforce one-level-of-bounded-iteration: no nested iterators.
    /// </summary>
    private static bool ContainsBoundedIterator(Expression expr) => expr switch
    {
        LetExpr e => ContainsBoundedIterator(e.Value) || ContainsBoundedIterator(e.Body),
        RecordExpr e => e.Fields.Any(field => ContainsBoundedIterator(field.Value)),
        TupleExpr e => e.Items.Any(ContainsBoundedIterator),
        ProjectExpr e => ContainsBoundedIterator(e.Value),
        FoldExpr => true,
        MapExpr => true,
        FilterExpr => true,
        BinaryExpr b => ContainsBoundedIterator(b.Left) || ContainsBoundedIterator(b.Right),
        CompareExpr c => ContainsBoundedIterator(c.Left) || ContainsBoundedIterator(c.Right),
        IfExpr i => ContainsBoundedIterator(i.Condition) || ContainsBoundedIterator(i.ThenExpr) ||
                    ContainsBoundedIterator(i.ElseExpr),
        NotExpr n => ContainsBoundedIterator(n.Expr),
        AggregateExpr a => (a.Source != null && ContainsBoundedIterator(a.Source)) ||
                           (a.Predicate != null && ContainsBoundedIterator(a.Predicate)) ||
                           (a.ValueExpr != null && ContainsBoundedIterator(a.ValueExpr)),
        CallExpr call => call.Args.Any(ContainsBoundedIterator),
        BitSetExpr set => set.Args.Any(ContainsBoundedIterator),
        _ => false // ConstantExpr, FieldAccessExpr — leaves
    };

    private static ExprType Fail(Expression expr, string message, List<TypeCheckError> errors)
    {
        errors.Add(Error(expr, message));
        return ExprType.Error;
    }

    private static TypeCheckError Error(Expression expr, string message) =>
        new()
        {
            Message = message,
            NodeAnnotation = expr.Annotation,
            Code = "TYPE_ERROR"
        };
}
