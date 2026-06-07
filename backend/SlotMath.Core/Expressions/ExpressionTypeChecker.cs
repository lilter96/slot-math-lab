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

    // ── Helpers ──────────────────────────────────────────────────────────

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
