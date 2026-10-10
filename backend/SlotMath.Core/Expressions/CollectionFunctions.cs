using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace SlotMath.Core.Expressions;

/// <summary>Shared strict built-ins for reference and compiled execution.
/// Explicit text conversion is separate from implicit symbol scoring.</summary>
internal static class CollectionFunctions
{
    public static ExprValue Call(string function, ExprValue first, ExprValue second, int count)
    {
        if (function is "abs" or "min" or "max" or "floor" or "ceil" or "round")
            return NumericFunctions.Call(function, first, second, count);
        var expected = function switch
        {
            "tonumber" or "tostring" or "length" => 1,
            "contains" or "append" or "index" => 2,
            _ => throw new ExpressionEvaluationException("EVAL_UNKNOWN_FUNCTION", $"Unknown function '{function}'.", function),
        };
        if (count != expected) throw new ExpressionEvaluationException("EVAL_ARITY_ERROR", $"{function} requires {expected} arguments.", function);
        return function switch
        {
            "tonumber" => ToNumber(first),
            "tostring" => ToText(first),
            "length" => Length(first),
            "contains" => Contains(first, second),
            "append" => Append(first, second),
            _ => Index(first, second),
        };
    }

    // The compiled sampling plan binds these directly once arity is known.
    public static ExprValue ToNumber(ExprValue first)
    {
        if (first.TryGetNumericText(out var cached)) return ExprValue.Number(cached);
        RequireText(first, "tonumber"); return ParseNumber(first.StringValue!);
    }

    public static ExprValue ToText(ExprValue first)
    {
        NumericFunctions.RequireNumber(first);
        return ExprValue.String(first.NumberDenominator.IsOne
            ? first.NumberNumerator.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{first.NumberNumerator}/{first.NumberDenominator}"));
    }

    public static ExprValue Length(ExprValue first)
    {
        if (first.Kind == ExprType.Array) return ExprValue.Number(first.ArrayValue!.Count);
        RequireText(first, "length"); return ExprValue.Number(first.StringValue!.Length);
    }

    public static ExprValue Contains(ExprValue first, ExprValue second)
    {
        if (first.Kind == ExprType.Array)
        {
            foreach (var item in first.ArrayValue!) if (item.Equals(second)) return ExprValue.Bool(true);
            return ExprValue.Bool(false);
        }
        RequireText(first, "contains"); RequireText(second, "contains");
        return ExprValue.Bool(first.StringValue!.Contains(second.StringValue!, StringComparison.Ordinal));
    }

    public static ExprValue Append(ExprValue first, ExprValue second)
    {
        RequireArray(first, "append");
        if (first.ItemsArray is { } items)
        {
            var copy = new ExprValue[items.Length + 1];
            items.CopyTo(copy, 0); copy[items.Length] = second;
            return ExprValue.ArrayOwned(copy, first.ContainsSymbols || second.ContainsSymbols);
        }
        return ExprValue.Array([.. first.ArrayValue!, second]);
    }

    public static ExprValue Index(ExprValue first, ExprValue second)
    {
        if (second.IsInlineInteger && first.ItemsArray is { } items && (ulong)second.InlineInteger < (ulong)items.Length)
            return items[second.InlineInteger];
        RequireArray(first, "index"); NumericFunctions.RequireNumber(second);
        if (second.NumberNumerator % second.NumberDenominator != 0)
            throw new ExpressionEvaluationException("EVAL_INVALID_INDEX", "index requires an integer position; fractional positions are not truncated.", "index");
        var index = second.NumberNumerator / second.NumberDenominator;
        var array = first.ArrayValue!;
        if (index < 0 || index >= array.Count)
            throw new ExpressionEvaluationException(EvalErrorCodes.IndexOutOfRange,
                $"index({index}) is out of range [0, {array.Count}) (D1).", $"index[{index}]");
        return array[(int)index];
    }

    public static ExprValue ParseNumber(string text)
    {
        if (text.Length > 4096) throw new ExpressionEvaluationException("EVAL_NUMERIC_BUDGET", "Numeric text exceeds the 4096-character budget.", "tonumber");
        text = text.Trim();
        var parts = text.Split('/');
        if (parts.Length == 2 && BigInteger.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numerator)
            && BigInteger.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var denominator) && !denominator.IsZero)
            return ExprValue.Rational(numerator, denominator);
        if (parts.Length == 1 && Regex.IsMatch(text, @"\A[+-]?[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            return NumericValues.FromJsonNumber(text);
        throw new ExpressionEvaluationException("EVAL_INVALID_NUMBER", "tonumber requires an invariant integer, decimal, exponent or nonzero-denominator rational.", "tonumber");
    }

    private static void RequireText(ExprValue value, string function)
    {
        if (value.Kind is not (ExprType.String or ExprType.Symbol))
            throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", $"{function} requires text.", function);
    }
    private static void RequireArray(ExprValue value, string function)
    {
        if (value.Kind != ExprType.Array) throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", $"{function} requires an array.", function);
    }
}
