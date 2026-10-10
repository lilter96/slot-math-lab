using System.Numerics;
using SlotMath.Core.Model;

namespace SlotMath.Core.Expressions;

/// <summary>The numeric DSL operates on exact rationals. Integer conversion is
/// explicit (floor/ceil/round); extrema, magnitude and aggregates preserve the
/// selected value. Both execution engines use this contract, with independent
/// arithmetic examples as the oracle. Round ties are away from zero.</summary>
internal static class NumericFunctions
{
    public static ExprValue Call(string function, ExprValue first, ExprValue second, int count)
    {
        var expected = function is "min" or "max" ? 2 : 1;
        if (count != expected) throw new ExpressionEvaluationException("EVAL_ARITY_ERROR", $"{function} requires {expected} arguments.");
        RequireNumber(first);
        if (expected == 2) RequireNumber(second);
        var numerator = first.NumberNumerator; var denominator = first.NumberDenominator;
        return function switch
        {
            "abs" => numerator.Sign < 0 ? ExprValue.Rational(-numerator, denominator) : first,
            "min" => Compare(first, second) <= 0 ? first : second,
            "max" => Compare(first, second) >= 0 ? first : second,
            "floor" => ExprValue.Number(numerator / denominator - (numerator.Sign < 0 && numerator % denominator != 0 ? 1 : 0)),
            "ceil" => ExprValue.Number(numerator / denominator + (numerator.Sign > 0 && numerator % denominator != 0 ? 1 : 0)),
            "round" => ExprValue.Number(Round(numerator, denominator)),
            _ => throw new ExpressionEvaluationException("EVAL_UNKNOWN_FUNCTION", $"Unknown numeric function '{function}'.")
        };
    }
    private static BigInteger Round(BigInteger numerator, BigInteger denominator)
    {
        var quotient = BigInteger.DivRem(BigInteger.Abs(numerator), denominator, out var remainder);
        if (2 * remainder >= denominator) quotient++;
        return numerator.Sign < 0 ? -quotient : quotient;
    }
    public static int Compare(ExprValue left, ExprValue right) => left.NumberDenominator.IsOne && right.NumberDenominator.IsOne
        ? left.NumberNumerator.CompareTo(right.NumberNumerator)
        : (left.NumberNumerator * right.NumberDenominator).CompareTo(right.NumberNumerator * left.NumberDenominator);
    public static void RequireNumber(ExprValue value)
    {
        if (value.Kind != ExprType.Number) throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "Numeric expression required.");
    }
    public static ExprValue Aggregate(AggregateFunc function, ExprValue total, ExprValue value, bool first)
    {
        RequireNumber(value);
        return function switch
        {
            AggregateFunc.Sum => ExprValue.Add(total, value),
            AggregateFunc.Product => ExprValue.Mul(total, value),
            AggregateFunc.Min => first || Compare(value, total) < 0 ? value : total,
            AggregateFunc.Max => first || Compare(value, total) > 0 ? value : total,
            _ => throw new ExpressionEvaluationException("EVAL_INVALID_OPERATOR", "Unknown numeric aggregate.")
        };
    }
}
