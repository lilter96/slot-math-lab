using System.Globalization;
using System.Numerics;
using SlotMath.Core.Expressions;

namespace SlotMath.Core.Model;

/// <summary>One rounding operation on the total nonnegative award, before the round cap.
/// Decimal quantum is authored text; no binary floating point enters settlement.</summary>
public sealed record MonetarySettlement
{
    public string Quantum { get; init; } = "0.01";
    public string Mode { get; init; } = "nearestEven";
    public static readonly string[] EvidenceKeys = ["__settlementBefore", "__settlementAfter", "__roundingDifference"];
    public decimal Validate()
    {
        if (Mode is not ("floor" or "ceiling" or "nearestEven" or "nearestAway"))
            throw new ArgumentException("Unknown monetary rounding mode.");
        if (Quantum is null || Quantum.Length > 32 || !decimal.TryParse(Quantum, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var q)
            || q <= 0 || q > 1_000_000 || decimal.Round(q, 9) != q)
            throw new ArgumentException("Quantum must be a positive decimal with at most nine decimal places.");
        return q;
    }
    public ExprValue Apply(ExprValue before)
    {
        var q = Validate();
        if (before.Kind != ExprType.Number || before.NumberNumerator.Sign < 0)
            throw new ArgumentException("Monetary settlement requires a nonnegative exact numeric payout.");
        var parts = decimal.GetBits(q); var decimals = (parts[3] >> 16) & 255;
        var numerator = new BigInteger((uint)parts[0]) + (new BigInteger((uint)parts[1]) << 32) + (new BigInteger((uint)parts[2]) << 64);
        var denominator = BigInteger.Pow(10, decimals);
        var units = BigInteger.DivRem(before.NumberNumerator * denominator, before.NumberDenominator * numerator, out var remainder);
        var divisor = before.NumberDenominator * numerator;
        var up = Mode switch {
            "ceiling" => !remainder.IsZero,
            "nearestAway" => remainder * 2 >= divisor,
            "nearestEven" => remainder * 2 > divisor || remainder * 2 == divisor && !units.IsEven,
            _ => false
        };
        return ExprValue.Rational((units + (up ? 1 : 0)) * numerator, denominator);
    }
}
