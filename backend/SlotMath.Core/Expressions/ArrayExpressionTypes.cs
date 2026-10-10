using SlotMath.Core.Model;

namespace SlotMath.Core.Expressions;

internal readonly record struct ArrayExpressionType(ExprType? ItemType, bool IsEmpty = false)
{
    public static ArrayExpressionType Join(ArrayExpressionType left, ArrayExpressionType right) =>
        left.IsEmpty ? right : right.IsEmpty ? left : new(left.ItemType == right.ItemType ? left.ItemType : null);
}

/// <summary>Conservative element inference shared by index calls and state
/// writers. Explicit empty arrays are neutral; unknown/mixed arrays are not.</summary>
internal static class ArrayExpressionTypes
{
    public static ArrayExpressionType Infer(Expression expression, TypeCheckContext context) => expression switch
    {
        FieldAccessExpr { Path.Length: 1 } field when field.Target == "state" => Read(field.Path[0], context),
        MapExpr map => new(Scalar(map.Body, Bind(context, new() { Name = map.ItemName, Type = map.ItemType },
            new() { Name = map.IndexName ?? "__unused_index__", Type = ExprType.Number })), Read(map.StateKey, context).IsEmpty),
        FilterExpr filter => Read(filter.StateKey, context),
        IfExpr conditional => ArrayExpressionType.Join(Infer(conditional.ThenExpr, context), Infer(conditional.ElseExpr, context)),
        CallExpr call when call.Function.Equals("append", StringComparison.OrdinalIgnoreCase) && call.Args.Length == 2 =>
            ArrayExpressionType.Join(Infer(call.Args[0], context), new(Scalar(call.Args[1], context))),
        FoldExpr fold => Fold(fold, context),
        _ => default,
    };

    private static ArrayExpressionType Fold(FoldExpr fold, TypeCheckContext context)
    {
        var initial = Infer(fold.Init, context);
        if (ExpressionTypeChecker.InferType(fold.Init, context) != ExprType.Array) return default;
        var result = initial;
        // The body cannot contain iterators. Rebind its accumulator until its
        // homogeneous type stabilizes; a conflicting append withholds it.
        for (var pass = 0; pass < 3; pass++)
        {
            var body = Infer(fold.Body, Bind(context,
                new() { Name = fold.AccName, Type = ExprType.Array, ArrayItemType = result.ItemType, ArrayIsEmpty = result.IsEmpty },
                new() { Name = fold.ItemName, Type = fold.ItemType },
                new() { Name = fold.IndexName ?? "__unused_index__", Type = ExprType.Number }));
            var next = ArrayExpressionType.Join(initial, body);
            if (next == result) break;
            result = next;
        }
        return result;
    }

    private static ExprType? Scalar(Expression expression, TypeCheckContext context)
    {
        var type = ExpressionTypeChecker.InferType(expression, context);
        return type is ExprType.Number or ExprType.String or ExprType.Symbol or ExprType.Boolean ? type : null;
    }
    private static ArrayExpressionType Read(string name, TypeCheckContext context)
    {
        var field = context.StateFields.LastOrDefault(field => field.Name == name);
        return field?.Type == ExprType.Array ? new(field.ArrayItemType, field.ArrayIsEmpty) : default;
    }
    internal static TypeCheckContext Bind(TypeCheckContext context, params FieldDescriptor[] bindings) => new()
    {
        BoardFields = context.BoardFields, MeasurementFields = context.MeasurementFields, CellFields = context.CellFields,
        DecorationTypes = context.DecorationTypes,
        StateFields = context.StateFields.Where(field => !bindings.Any(binding => binding.Name == field.Name)).Concat(bindings).ToArray(),
    };
}
