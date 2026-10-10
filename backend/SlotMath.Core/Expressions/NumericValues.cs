using System.Globalization;
using System.Numerics;

namespace SlotMath.Core.Expressions;

/// <summary>Numeric input boundaries. JSON decimal literals retain their authored
/// rational value; CLR floating inputs retain their finite IEEE754 value exactly.</summary>
internal static class NumericValues
{
    public static ExprValue FromDouble(double value)
    {
        if (!double.IsFinite(value)) throw new ExpressionEvaluationException("EVAL_NONFINITE_NUMBER", "Numeric state input must be finite.");
        var bits = BitConverter.DoubleToInt64Bits(value); var exponent = (int)((bits >> 52) & 2047); var mantissa = bits & 0x000fffffffffffffL;
        var numerator = new BigInteger(exponent == 0 ? mantissa : mantissa | 0x0010000000000000L);
        if (bits < 0) numerator = -numerator;
        var shift = exponent == 0 ? -1074 : exponent - 1075;
        return shift >= 0 ? ExprValue.Number(numerator << shift) : ExprValue.Rational(numerator, BigInteger.One << -shift);
    }

    public static ExprValue FromDecimal(decimal value)
    {
        var bits = decimal.GetBits(value);
        var numerator = (new BigInteger((uint)bits[2]) << 64) | (new BigInteger((uint)bits[1]) << 32) | (uint)bits[0];
        if (bits[3] < 0) numerator = -numerator;
        return ExprValue.Rational(numerator, BigInteger.Pow(10, (bits[3] >> 16) & 255));
    }

    public static ExprValue FromJsonNumber(string text)
    {
        if (text.Length > 4096) throw new ExpressionEvaluationException("EVAL_NUMERIC_BUDGET", "A numeric state literal exceeds the 4096-character budget.");
        var exponentAt = text.IndexOfAny(['e', 'E']);
        var exponent = 0;
        if (exponentAt >= 0 && !int.TryParse(text.AsSpan(exponentAt + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
            throw new ExpressionEvaluationException("EVAL_NUMERIC_BUDGET", "Numeric state exponent exceeds its budget.");
        var mantissa = exponentAt >= 0 ? text[..exponentAt] : text;
        var decimalAt = mantissa.IndexOf('.'); var fractionalDigits = decimalAt < 0 ? 0 : mantissa.Length - decimalAt - 1;
        var power = (long)exponent - fractionalDigits;
        if (power is < -4096 or > 4096) throw new ExpressionEvaluationException("EVAL_NUMERIC_BUDGET", "Numeric state exponent must remain within ±4096 decimal powers.");
        var numerator = BigInteger.Parse(mantissa.Replace(".", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return power >= 0 ? ExprValue.Number(numerator * BigInteger.Pow(10, (int)power))
            : ExprValue.Rational(numerator, BigInteger.Pow(10, (int)-power));
    }
}
