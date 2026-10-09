using System.Numerics;
using SlotMath.Core.Expressions;

namespace SlotMath.Core.Measurements;
/// <summary>Read-only instrumentation namespace. It is never inserted into game state.</summary>
public readonly record struct SettlementContext(double Payout, double? RawPayout, double Cost)
{
    public ExprValue Read(string key) => FromDouble(key switch
    {
        "payout" => Payout, "rawPayout" => RawPayout ?? throw new InvalidOperationException("Raw payout is unavailable."),
        "capDeduction" => (RawPayout ?? throw new InvalidOperationException("Raw payout is unavailable.")) - Payout,
        "cost" => Cost, "net" => Payout - Cost,
        _ => throw new InvalidOperationException($"Unknown settlement field '{key}'.")
    });
    // Preserve the supplied finite double exactly, with no arbitrary decimal truncation.
    private static ExprValue FromDouble(double value)
    {
        if (!double.IsFinite(value)) throw new ArithmeticException("Settlement field is nonfinite.");
        var bits = BitConverter.DoubleToInt64Bits(value); var exponent = (int)((bits >> 52) & 2047); var mantissa = bits & 0x000fffffffffffffL;
        var numerator = new BigInteger(exponent == 0 ? mantissa : mantissa | 0x0010000000000000L);
        if (bits < 0) numerator = -numerator;
        var shift = exponent == 0 ? -1074 : exponent - 1075;
        return shift >= 0 ? ExprValue.Number(numerator << shift) : ExprValue.Rational(numerator, BigInteger.One << -shift);
    }
    public static IReadOnlyList<FieldDescriptor> Fields { get; } = new[] { "payout", "rawPayout", "capDeduction", "cost", "net" }.Select(k => new FieldDescriptor { Name = k, Type = ExprType.Number }).ToArray();
}
