using System.Text.Json;
using SlotMath.Core.Model;

namespace SlotMath.Core.Expressions;

/// <summary>Conservative structural inference. Unknown or conflicting fields
/// remain unknown instead of acquiring a numeric type from their container.</summary>
internal static class ExpressionShapes
{
    internal static FieldDescriptor? Item(FieldDescriptor? array) => array?.Type == ExprType.Array
        ? array.ArrayItem ?? (array.ArrayItemType is { } type ? Field("item", type) : null) : null;
    internal static FieldDescriptor Field(string name, ExprType type) => new() { Name = name, Type = type };
    internal static FieldDescriptor Array(FieldDescriptor? item, bool empty = false) => new()
    { Name = "array", Type = ExprType.Array, ArrayItem = item, ArrayItemType = item?.Type, ArrayIsEmpty = empty };

    internal static FieldDescriptor FromJson(string name, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => Field(name, ExprType.Number),
        JsonValueKind.String => Field(name, ExprType.String),
        JsonValueKind.True or JsonValueKind.False => Field(name, ExprType.Boolean),
        JsonValueKind.Null => Field(name, ExprType.Null),
        JsonValueKind.Object => new()
        {
            Name = name,
            Type = ExprType.Record,
            RecordFields = value.EnumerateObject().Select(p => FromJson(p.Name, p.Value)).ToArray()
        },
        JsonValueKind.Array => JsonArray(name, value),
        _ => Field(name, ExprType.Error),
    };

    private static FieldDescriptor JsonArray(string name, JsonElement value)
    {
        FieldDescriptor? item = null;
        var first = true;
        foreach (var element in value.EnumerateArray())
        {
            var shape = FromJson("item", element);
            item = first ? shape : Join(item, shape);
            first = false;
        }
        return Array(item, first) with { Name = name };
    }

    internal static FieldDescriptor? Join(FieldDescriptor? left, FieldDescriptor? right)
    {
        if (left is null || right is null || left.Type != right.Type) return null;
        if (left.Type == ExprType.Array)
        {
            if (left.ArrayIsEmpty) return right;
            if (right.ArrayIsEmpty) return left;
            return Array(Join(Item(left), Item(right))) with { Name = left.Name };
        }
        if (left.Type == ExprType.Record) return left with
        {
            RecordFields = left.RecordFields.Select(field =>
                Join(field, right.RecordFields.SingleOrDefault(other => other.Name == field.Name)))
                .OfType<FieldDescriptor>().ToArray(),
        };
        return left;
    }

    internal static bool Same(FieldDescriptor? a, FieldDescriptor? b) => a is null ? b is null : b is not null
        && a.Type == b.Type && a.ArrayIsEmpty == b.ArrayIsEmpty && SameItem(a, b)
        && a.RecordFields.Count == b.RecordFields.Count
        && a.RecordFields.All(field => Same(field, b.RecordFields.SingleOrDefault(other => other.Name == field.Name)));
    private static bool SameItem(FieldDescriptor a, FieldDescriptor b) => a.Type != ExprType.Array || Same(Item(a), Item(b));

    internal static FieldDescriptor IteratorItem(TypeCheckContext context, string source, string binding, ExprType declared)
    {
        var item = Item(context.ResolveField([source], "state"));
        return item?.Type == declared ? item with { Name = binding } : Field(binding, declared);
    }

    internal static FieldDescriptor? Infer(Expression expression, TypeCheckContext context) => expression switch
    {
        FieldAccessExpr field => context.ResolveField(field.Path, field.Target),
        FilterExpr filter => context.ResolveField([filter.StateKey], "state"),
        MapExpr map => Array(Infer(map.Body, ArrayExpressionTypes.Bind(context,
            IteratorItem(context, map.StateKey, map.ItemName, map.ItemType),
            Field(map.IndexName ?? "__unused_index__", ExprType.Number))), context.ResolveField([map.StateKey], "state")?.ArrayIsEmpty == true),
        IfExpr conditional => Join(Infer(conditional.ThenExpr, context), Infer(conditional.ElseExpr, context)),
        CallExpr call when call.Function.Equals("index", StringComparison.OrdinalIgnoreCase) && call.Args.Length == 2 => Item(Infer(call.Args[0], context)),
        CallExpr call when call.Function.Equals("append", StringComparison.OrdinalIgnoreCase) && call.Args.Length == 2 =>
            Append(Infer(call.Args[0], context), Infer(call.Args[1], context)),
        FoldExpr fold => Fold(fold, context),
        _ => Scalar(expression, context),
    };

    private static FieldDescriptor? Append(FieldDescriptor? source, FieldDescriptor? value) => source?.Type == ExprType.Array
        ? Array(source.ArrayIsEmpty ? value : Join(Item(source), value)) : null;

    private static FieldDescriptor? Fold(FoldExpr fold, TypeCheckContext context)
    {
        var initial = Infer(fold.Init, context);
        var result = initial;
        for (var pass = 0; pass < 3 && result is not null; pass++)
        {
            var body = Infer(fold.Body, ArrayExpressionTypes.Bind(context, result with { Name = fold.AccName },
                IteratorItem(context, fold.StateKey, fold.ItemName, fold.ItemType),
                Field(fold.IndexName ?? "__unused_index__", ExprType.Number)));
            var next = Join(initial, body);
            if (Same(result, next)) break;
            result = next;
        }
        return result;
    }

    private static FieldDescriptor? Scalar(Expression expression, TypeCheckContext context)
    {
        var type = ExpressionTypeChecker.InferType(expression, context);
        return type == ExprType.Error ? null : Field("value", type);
    }
}
