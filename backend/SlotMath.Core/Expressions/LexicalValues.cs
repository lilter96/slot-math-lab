using System.Globalization;

namespace SlotMath.Core.Expressions;

internal sealed record LexicalBinding(string Name, ExprValue Value, LexicalBinding? Previous);

internal static class LexicalValues
{
    internal const int MaxFields = 64;

    internal static ExprValue Read(LexicalBinding? binding, string name, string[] path)
    {
        for (LexicalBinding? current = binding; current is not null; current = current.Previous)
        {
            if (current.Name == name)
            {
                return Project(current.Value, path, "local." + name);
            }
        }

        throw new ExpressionEvaluationException("EVAL_MISSING_LOCAL", $"Local '{name}' is not bound.", "local." + name);
    }

    internal static ExprValue Project(ExprValue value, string[] path, string location = "project") =>
        path.Length == 0 ? StateValues.RequireReadable(value, location) : StateValues.Read(value, path, location);

    internal static ExprValue Record(string[] names, ExprValue[] values)
    {
        if (names.Length != values.Length || names.Length > MaxFields)
        {
            throw new ExpressionEvaluationException("EVAL_RECORD_BUDGET",
                "A record requires at most 64 matching fields.", "record");
        }

        var fields = new Dictionary<string, ExprValue>(StringComparer.Ordinal);
        for (var index = 0; index < names.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(names[index]) || !fields.TryAdd(names[index], values[index]))
            {
                throw new ExpressionEvaluationException("EVAL_RECORD_FIELD",
                    "Record names must be nonempty and unique.", "record");
            }
        }

        return ExprValue.Record(fields);
    }

    internal static string[] TupleNames(int length) => Enumerable.Range(0, length)
        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray();
}
