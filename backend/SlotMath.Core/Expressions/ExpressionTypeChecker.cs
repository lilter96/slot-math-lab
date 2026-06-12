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
    /// Check an expression and return all type errors.  Empty list = valid.
    /// </summary>
    public static IReadOnlyList<TypeCheckError> Check(Expression expr, TypeCheckContext ctx)
    {
        var errors = new List<TypeCheckError>();
        Infer(expr, ctx, errors);
        return errors;
    }

    /// <summary>
    /// Check and also verify the expression type matches the expected type.
    /// </summary>
    public static IReadOnlyList<TypeCheckError> Check(
        Expression expr, TypeCheckContext ctx, ExprType expectedType)
    {
        var errors = new List<TypeCheckError>();
        var inferred = Infer(expr, ctx, errors);

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

    // ── Core inference ───────────────────────────────────────────────────

    private static ExprType Infer(Expression expr, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        return expr switch
        {
            ConstantExpr c => InferConstant(c, ctx, errors),
            FieldAccessExpr f => InferFieldAccess(f, ctx, errors),
            BinaryExpr b => InferBinary(b, ctx, errors),
            CompareExpr c => InferCompare(c, ctx, errors),
            IfExpr i => InferIf(i, ctx, errors),
            AggregateExpr a => InferAggregate(a, ctx, errors),
            NotExpr n => InferNot(n, ctx, errors),
            CallExpr c => InferCall(c, ctx, errors),
            FoldExpr f => InferFold(f, ctx, errors),
            MapExpr m => InferMap(m, ctx, errors),
            FilterExpr fi => InferFilter(fi, ctx, errors),
            _ => Fail(expr, $"Unknown expression type: {expr.GetType().Name}", errors),
        };
    }

    // ── Constant ─────────────────────────────────────────────────────────

    private static ExprType InferConstant(ConstantExpr c, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        return c.Kind switch
        {
            ConstantKind.Integer or ConstantKind.Rational => ExprType.Number,
            ConstantKind.Boolean => ExprType.Boolean,
            ConstantKind.String => ExprType.String,
            _ => Fail(c, $"Unknown constant kind: {c.Kind}", errors),
        };
    }

    // ── Field access ─────────────────────────────────────────────────────

    private static ExprType InferFieldAccess(FieldAccessExpr f, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        if (f.Path.Length == 0)
            return Fail(f, "Field access path is empty.", errors);

        var resolved = ctx.ResolvePath(f.Path, f.Target);
        if (resolved == null)
        {
            var where = f.Target ?? "board";
            return Fail(f,
                $"Unknown field '{string.Join(".", f.Path)}' on {where}.", errors);
        }

        return resolved.Value;
    }

    // ── Binary ───────────────────────────────────────────────────────────

    private static ExprType InferBinary(BinaryExpr b, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        var left = Infer(b.Left, ctx, errors);
        var right = Infer(b.Right, ctx, errors);

        if (left == ExprType.Error || right == ExprType.Error)
            return ExprType.Error;

        return b.Op switch
        {
            BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul or BinaryOp.Div =>
                CheckArithmetic(b, left, right, errors),

            BinaryOp.And or BinaryOp.Or =>
                CheckLogical(b, left, right, errors),

            _ => Fail(b, $"Unknown binary operator: {b.Op}", errors),
        };
    }

    private static ExprType CheckArithmetic(BinaryExpr b, ExprType left, ExprType right,
        List<TypeCheckError> errors)
    {
        if (left != ExprType.Number)
            errors.Add(Error(b, $"Left operand of '{b.Op}' must be Number, got {left}."));
        if (right != ExprType.Number)
            errors.Add(Error(b, $"Right operand of '{b.Op}' must be Number, got {right}."));
        return left == ExprType.Number && right == ExprType.Number ? ExprType.Number : ExprType.Error;
    }

    private static ExprType CheckLogical(BinaryExpr b, ExprType left, ExprType right,
        List<TypeCheckError> errors)
    {
        if (left != ExprType.Boolean)
            errors.Add(Error(b, $"Left operand of '{b.Op}' must be Boolean, got {left}."));
        if (right != ExprType.Boolean)
            errors.Add(Error(b, $"Right operand of '{b.Op}' must be Boolean, got {right}."));
        return left == ExprType.Boolean && right == ExprType.Boolean ? ExprType.Boolean : ExprType.Error;
    }

    // ── Comparison ───────────────────────────────────────────────────────

    private static ExprType InferCompare(CompareExpr c, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        var left = Infer(c.Left, ctx, errors);
        var right = Infer(c.Right, ctx, errors);

        if (left == ExprType.Error || right == ExprType.Error)
            return ExprType.Error;

        // Eq/Neq work on any compatible types; ordering comparisons require Number.
        var isOrdered = c.Op is CompareOp.Lt or CompareOp.Gt or CompareOp.Lte or CompareOp.Gte;

        if (isOrdered)
        {
            if (left != ExprType.Number)
                errors.Add(Error(c, $"Left operand of '{c.Op}' must be Number, got {left}."));
            if (right != ExprType.Number)
                errors.Add(Error(c, $"Right operand of '{c.Op}' must be Number, got {right}."));
            return left == ExprType.Number && right == ExprType.Number ? ExprType.Boolean : ExprType.Error;
        }

        // Eq/Neq: types must match (with Symbol/String coercion)
        var l = left == ExprType.Symbol ? ExprType.String : left;
        var r = right == ExprType.Symbol ? ExprType.String : right;
        if (l != r)
            errors.Add(Error(c, $"Cannot compare {left} with {right} using '{c.Op}'."));

        return ExprType.Boolean;
    }

    // ── Conditional ──────────────────────────────────────────────────────

    private static ExprType InferIf(IfExpr i, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        var cond = Infer(i.Condition, ctx, errors);
        if (cond != ExprType.Boolean && cond != ExprType.Error)
            errors.Add(Error(i, $"If condition must be Boolean, got {cond}."));

        var thenType = Infer(i.ThenExpr, ctx, errors);
        var elseType = Infer(i.ElseExpr, ctx, errors);

        if (thenType == ExprType.Error) return elseType;
        if (elseType == ExprType.Error) return thenType;

        if (thenType != elseType)
        {
            // Allow Symbol ↔ String coercion
            var t = thenType == ExprType.Symbol ? ExprType.String : thenType;
            var e = elseType == ExprType.Symbol ? ExprType.String : elseType;
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
        // Aggregates always return Number.
        if (a.Predicate != null)
        {
            // Extend context: inside the predicate, cell-level fields are accessible.
            var predCtx = new TypeCheckContext
            {
                ExpectedType = ExprType.Boolean,
                BoardFields = ctx.CellFields, // shorthand: cell fields as "board-like" context
                StateFields = ctx.StateFields,
                CellFields = ctx.CellFields,
                DecorationTypes = ctx.DecorationTypes,
            };
            var predType = Infer(a.Predicate, predCtx, errors);
            if (predType != ExprType.Boolean && predType != ExprType.Error)
                errors.Add(Error(a, $"Aggregate predicate must be Boolean, got {predType}."));
        }

        // Func determines what's being aggregated.
        // Sum/Product/Min/Max require Number; Count can work on anything.
        if (a.Func is AggregateFunc.Sum or AggregateFunc.Product
            or AggregateFunc.Min or AggregateFunc.Max)
        {
            // The target field must resolve to Number when predicate accesses it.
            // We don't fully verify this statically without a schema, but we note
            // that Count is always valid.
        }

        return ExprType.Number;
    }

    // ── Not ──────────────────────────────────────────────────────────────

    private static ExprType InferNot(NotExpr n, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        var inner = Infer(n.Expr, ctx, errors);
        if (inner != ExprType.Boolean && inner != ExprType.Error)
            errors.Add(Error(n, $"'not' requires Boolean operand, got {inner}."));
        return ExprType.Boolean;
    }

    // ── Call ─────────────────────────────────────────────────────────────

    private static ExprType InferCall(CallExpr c, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        // Built-in functions: a closed set — no user-defined recursion.
        var (returnType, argTypes) = c.Function.ToLowerInvariant() switch
        {
            "abs" => (ExprType.Number, new[] { ExprType.Number }),
            "min" => (ExprType.Number, new[] { ExprType.Number, ExprType.Number }),
            "max" => (ExprType.Number, new[] { ExprType.Number, ExprType.Number }),
            "floor" => (ExprType.Number, new[] { ExprType.Number }),
            "ceil" => (ExprType.Number, new[] { ExprType.Number }),
            "round" => (ExprType.Number, new[] { ExprType.Number }),
            "tonumber" => (ExprType.Number, new[] { ExprType.String }),
            "tostring" => (ExprType.String, new[] { ExprType.Number }),
            "length" => (ExprType.Number, new[] { ExprType.String }),
            "contains" => (ExprType.Boolean, new[] { ExprType.String, ExprType.String }),
            _ => (ExprType.Error, Array.Empty<ExprType>()),
        };

        if (returnType == ExprType.Error)
            return Fail(c, $"Unknown function: '{c.Function}'.", errors);

        if (c.Args.Length != argTypes.Length)
        {
            errors.Add(Error(c,
                $"Function '{c.Function}' expects {argTypes.Length} arguments, got {c.Args.Length}."));
            return ExprType.Error;
        }

        for (var i = 0; i < c.Args.Length; i++)
        {
            var argType = Infer(c.Args[i], ctx, errors);
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
        var initType = Infer(f.Init, ctx, errors);

        // One level of bounded iteration only — a fold body may not contain
        // another fold, map, or filter.  Nested iterators would multiply the
        // iteration bound and break the "bounded by array size → exact-analysable"
        // guarantee (CLAUDE.md G9 / level-b grammar).  Reject before recursing
        // into the body so the error is precise and not buried under secondary errors.
        if (ContainsBoundedIterator(f.Body))
        {
            errors.Add(Error(f,
                "Nested fold is not allowed: a fold body may not contain another " +
                "fold, map, or filter (one level of bounded iteration keeps the grammar exact-analysable)."));
            return initType;
        }

        // Lambda context: acc and item added as virtual state fields
        var lambdaFields = ctx.StateFields.ToList();
        lambdaFields.Add(new FieldDescriptor { Name = f.AccName, Type = initType });
        lambdaFields.Add(new FieldDescriptor { Name = f.ItemName, Type = f.ItemType });

        var lambdaCtx = new TypeCheckContext
        {
            ExpectedType = initType,
            BoardFields = ctx.BoardFields,
            StateFields = lambdaFields,
            CellFields = ctx.CellFields,
            DecorationTypes = ctx.DecorationTypes,
        };

        var bodyType = Infer(f.Body, lambdaCtx, errors);
        if (bodyType != initType && bodyType != ExprType.Error)
            errors.Add(Error(f,
                $"FoldExpr body returns {bodyType} but must match init type {initType}."));

        return initType;
    }

    // ── Map ──────────────────────────────────────────────────────────────

    private static ExprType InferMap(MapExpr m, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        if (ContainsBoundedIterator(m.Body))
        {
            errors.Add(Error(m,
                "Nested iteration is not allowed in a map body: map/filter/fold may not be nested " +
                "(one level of bounded iteration keeps the grammar exact-analysable)."));
            return ExprType.Array;
        }

        var lambdaFields = ctx.StateFields.ToList();
        lambdaFields.Add(new FieldDescriptor { Name = m.ItemName, Type = m.ItemType });

        var lambdaCtx = new TypeCheckContext
        {
            ExpectedType = ExprType.Number,
            BoardFields = ctx.BoardFields,
            StateFields = lambdaFields,
            CellFields = ctx.CellFields,
            DecorationTypes = ctx.DecorationTypes,
        };

        Infer(m.Body, lambdaCtx, errors);
        return ExprType.Array;
    }

    // ── Filter ───────────────────────────────────────────────────────────

    private static ExprType InferFilter(FilterExpr fi, TypeCheckContext ctx, List<TypeCheckError> errors)
    {
        if (ContainsBoundedIterator(fi.Predicate))
        {
            errors.Add(Error(fi,
                "Nested iteration is not allowed in a filter predicate: map/filter/fold may not be nested " +
                "(one level of bounded iteration keeps the grammar exact-analysable)."));
            return ExprType.Array;
        }

        var lambdaFields = ctx.StateFields.ToList();
        lambdaFields.Add(new FieldDescriptor { Name = fi.ItemName, Type = fi.ItemType });

        var lambdaCtx = new TypeCheckContext
        {
            ExpectedType = ExprType.Boolean,
            BoardFields = ctx.BoardFields,
            StateFields = lambdaFields,
            CellFields = ctx.CellFields,
            DecorationTypes = ctx.DecorationTypes,
        };

        var predType = Infer(fi.Predicate, lambdaCtx, errors);
        if (predType != ExprType.Boolean && predType != ExprType.Error)
            errors.Add(Error(fi, $"Filter predicate must be Boolean, got {predType}."));

        return ExprType.Array;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// True if the expression tree contains a fold, map, or filter anywhere.
    /// Used to enforce one-level-of-bounded-iteration: no nested iterators.
    /// </summary>
    private static bool ContainsBoundedIterator(Expression expr) => expr switch
    {
        FoldExpr => true,
        MapExpr => true,
        FilterExpr => true,
        BinaryExpr b => ContainsBoundedIterator(b.Left) || ContainsBoundedIterator(b.Right),
        CompareExpr c => ContainsBoundedIterator(c.Left) || ContainsBoundedIterator(c.Right),
        IfExpr i => ContainsBoundedIterator(i.Condition) || ContainsBoundedIterator(i.ThenExpr) || ContainsBoundedIterator(i.ElseExpr),
        NotExpr n => ContainsBoundedIterator(n.Expr),
        AggregateExpr a => a.Predicate != null && ContainsBoundedIterator(a.Predicate),
        CallExpr call => call.Args.Any(ContainsBoundedIterator),
        _ => false, // ConstantExpr, FieldAccessExpr — leaves
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
            Code = "TYPE_ERROR",
        };
}
