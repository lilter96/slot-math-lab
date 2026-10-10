using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace SlotMath.Core.Expressions;

/// <summary>Shared field traversal and value conversion for both execution engines.
/// Every path segment is consumed; collections retain their actual values.</summary>
internal static class StateValues
{
    public static ExprValue Read(object? state, string[] path, string? location = null)
    {
        location ??= string.Join('.', path);
        if (path.Length == 0 || path.Any(string.IsNullOrEmpty))
            throw new ExpressionEvaluationException("EVAL_INVALID_PATH", "A field path must contain nonempty segments.", location);
        if (state is null) throw Missing(location);
        var current = state;
        foreach (var segment in path)
        {
            if (current is null or ExprValue { Kind: ExprType.Null }) throw Null(location);
            current = current switch
            {
                IDictionary<string, object?> record => record.TryGetValue(segment, out var value) ? value : throw Missing(location),
                IReadOnlyDictionary<string, object?> record => record.TryGetValue(segment, out var value) ? value : throw Missing(location),
                ExprValue { Kind: ExprType.Record } record => record.RecordValue!.TryGetValue(segment, out var value) ? value : throw Missing(location),
                ExprValue { Kind: ExprType.Array } array => array.ArrayValue![Index(segment, array.ArrayValue!.Count, location)],
                IList array => array[Index(segment, array.Count, location)],
                IDictionary => throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "Record fields require string keys.", location),
                IEnumerable array when current is not string => ReadEnumerable(array, segment, location),
                string or ValueType => throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "A scalar has no nested fields.", location),
                _ => ReadProperty(current, segment, location),
            };
        }
        return RequireReadable(Convert(current, location), location);
    }

    private static object? ReadEnumerable(IEnumerable array, string segment, string location)
    {
        var values = array.Cast<object?>().ToArray();
        return values[Index(segment, values.Length, location)];
    }

    private static object? ReadProperty(object value, string name, string location)
    {
        var property = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.CanRead && p.GetIndexParameters().Length == 0
                && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        return property is null ? throw Missing(location) : property.GetValue(value);
    }

    internal static int Index(string segment, int length, string location)
    {
        if (!BigInteger.TryParse(segment, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var index))
            throw new ExpressionEvaluationException("EVAL_INVALID_INDEX", "An array path requires an integer index.", location);
        if (index < 0 || index >= length)
            throw IndexError(index, length, location);
        return (int)index;
    }

    internal static ExpressionEvaluationException IndexError(BigInteger index, int length, string location) =>
        new(EvalErrorCodes.IndexOutOfRange, $"Index {index} is out of range [0, {length}).", location);

    internal static ExprValue RequireReadable(ExprValue value, string location) => value.Kind == ExprType.Null ? throw Null(location) : value;

    public static ExprValue Convert(object? value, string? location = null, int depth = 0)
    {
        if (depth > 64) throw new ExpressionEvaluationException("EVAL_VALUE_BUDGET", "State values exceed 64 nested collections.", location);
        ExprValue Child(object? child) => Convert(child, location, depth + 1);
        return value switch
        {
            null => ExprValue.Null,
            ExprValue typed => typed,
            BigInteger n => ExprValue.Number(n),
            int n => ExprValue.Number(n), long n => ExprValue.Number(n),
            byte n => ExprValue.Number(n), sbyte n => ExprValue.Number(n), short n => ExprValue.Number(n), ushort n => ExprValue.Number(n),
            uint n => ExprValue.Number(n), ulong n => ExprValue.Number(n),
            double n => NumericValues.FromDouble(n), float n => NumericValues.FromDouble(n), decimal n => NumericValues.FromDecimal(n),
            string text => ExprValue.String(text), bool boolean => ExprValue.Bool(boolean),
            IDictionary<string, object?> record => ExprValue.Record(record.ToDictionary(p => p.Key, p => Child(p.Value), StringComparer.Ordinal)),
            IReadOnlyDictionary<string, object?> record => ExprValue.Record(record.ToDictionary(p => p.Key, p => Child(p.Value), StringComparer.Ordinal)),
            IDictionary => throw Unsupported(location),
            IEnumerable array => ExprValue.Array(array.Cast<object?>().Select(Child).ToArray()),
            _ => throw Unsupported(location),
        };
    }

    private static ExpressionEvaluationException Missing(string location) => new("EVAL_MISSING_STATE", $"State field '{location}' is absent.", location);
    private static ExpressionEvaluationException Null(string location) => new("EVAL_NULL_VALUE", $"State field '{location}' is null; supply an explicit value before reading it.", location);
    private static ExpressionEvaluationException Unsupported(string? location) => new("EVAL_TYPE_ERROR", "State contains a value outside the expression type contract.", location);
}
