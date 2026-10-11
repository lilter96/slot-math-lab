using System.Text.Json;
using SlotMath.Core.Expressions;

namespace SlotMath.Core.Compiler;

internal static class InitialStateValues
{
    public static object? Materialize(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => value.EnumerateArray().Select(Materialize).ToArray(),
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => Materialize(p.Value)),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => NumericValues.FromJsonNumber(value.GetRawText()).ToStateObject(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new InvalidOperationException("Unsupported initial state value."),
    };

    // The compiled template is immutable. Every reference-interpreter round
    // receives its own arrays/records; numeric parsing occurs once per compile.
    public static object? Clone(object? value) => value switch
    {
        IDictionary<string, object?> d => d.ToDictionary(p => p.Key, p => Clone(p.Value)),
        object?[] a => a.Select(Clone).ToArray(),
        _ => value,
    };
}
