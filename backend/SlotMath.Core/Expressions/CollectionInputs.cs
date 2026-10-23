using SlotMath.Core.Model;

namespace SlotMath.Core.Expressions;

internal readonly record struct CollectionInput(string? StateKey, Expression? Source, int? MaxItems, string Kind)
{
    internal string Location => Kind + ".source";
    internal string Key => StateKey ?? Location;
}

internal static class CollectionInputs
{
    internal const int MaxItems = 4096;

    internal static CollectionInput Of(Expression expression) => expression switch
    {
        MapExpr e => new CollectionInput(e.StateKey, e.Source, e.MaxItems, "map"),
        FilterExpr e => new CollectionInput(e.StateKey, e.Source, e.MaxItems, "filter"),
        FoldExpr e => new CollectionInput(e.StateKey, e.Source, e.MaxItems, "fold"),
        AggregateExpr e => new CollectionInput(e.StateKey, e.Source, e.MaxItems, "aggregate"),
        _ => throw new ArgumentException("A collection expression is required.", nameof(expression))
    };

    internal static FieldDescriptor? Shape(CollectionInput input, TypeCheckContext context) =>
        input.Source is { } source
            ? ExpressionShapes.Infer(source, context)
            : input.StateKey is { } key
                ? context.ResolveField([key], "state")
                : null;

    internal static IEnumerable<Expression> Children(Expression expression) => expression switch
    {
        LetExpr e => [e.Value, e.Body],
        RecordExpr e => e.Fields.Select(field => field.Value),
        TupleExpr e => e.Items,
        ProjectExpr e => [e.Value],
        ReachableExpr e => [e.Adjacency, e.Allowed, e.Seeds],
        BinaryExpr e => [e.Left, e.Right],
        CompareExpr e => [e.Left, e.Right],
        IfExpr e => [e.Condition, e.ThenExpr, e.ElseExpr],
        NotExpr e => [e.Expr],
        CallExpr e => e.Args,
        BitSetExpr e => e.Args,
        MapExpr e => new[] { e.Source, e.Body }.OfType<Expression>(),
        FilterExpr e => new[] { e.Source, e.Predicate }.OfType<Expression>(),
        FoldExpr e => new[] { e.Init, e.Source, e.Body }.OfType<Expression>(),
        AggregateExpr e => new[] { e.Source, e.Predicate, e.ValueExpr }.OfType<Expression>(),
        _ => []
    };

    internal static bool HasUnboundedIterator(Expression expression) =>
        (expression is MapExpr or FilterExpr or FoldExpr or AggregateExpr && Of(expression).MaxItems is null)
        || Children(expression).Any(HasUnboundedIterator);

    internal static int CheckCount(int count, int bound, string location)
    {
        if (bound is < 0 or > MaxItems || count < 0 || count > bound)
        {
            throw new ExpressionEvaluationException("EVAL_COLLECTION_BUDGET",
                $"Collection source exceeds its {bound}-item bound (maximum {MaxItems}).", location);
        }

        return count;
    }

    internal static object?[] BoundedItems(IEnumerable<object?> values, int bound, string location)
    {
        CheckCount(0, bound, location);
        var items = values.Take(bound + 1).ToArray();
        CheckCount(items.Length, bound, location);
        return items;
    }

    internal static int Length(ExprValue value, int bound, string location)
    {
        if (value.Kind != ExprType.Array)
        {
            throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", $"State field '{location}' must be an array.",
                location);
        }

        return CheckCount(value.ArrayValue!.Count, bound, location);
    }

    internal static IReadOnlyList<ExprValue> Values(ExprValue value, int bound, string location)
    {
        Length(value, bound, location);
        return value.ArrayValue!;
    }
}
