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
    public static ArrayExpressionType Infer(Expression expression, TypeCheckContext context)
    {
        var shape = ExpressionShapes.Infer(expression, context);
        return shape?.Type == ExprType.Array ? new(ExpressionShapes.Item(shape)?.Type, shape.ArrayIsEmpty) : default;
    }
    internal static TypeCheckContext Bind(TypeCheckContext context, params FieldDescriptor[] bindings) => new()
    {
        BoardFields = context.BoardFields, MeasurementFields = context.MeasurementFields, CellFields = context.CellFields,
        DecorationTypes = context.DecorationTypes,
        StateFields = context.StateFields.Where(field => !bindings.Any(binding => binding.Name == field.Name)).Concat(bindings).ToArray(),
    };
}
