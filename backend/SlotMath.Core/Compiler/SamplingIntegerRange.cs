using System.Numerics;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler;

/// <summary>Compile-time bounds; an admitted operation cannot overflow an Int64 intermediate.</summary>
internal readonly record struct SamplingIntegerRange(long Minimum, long Maximum)
{
    internal static bool TryBinary(BinaryOp operation, SamplingIntegerRange left, SamplingIntegerRange right,
        out SamplingIntegerRange result)
    {
        BigInteger low, high;
        switch (operation)
        {
            case BinaryOp.Add:
                low = (BigInteger)left.Minimum + right.Minimum;
                high = (BigInteger)left.Maximum + right.Maximum;
                break;
            case BinaryOp.Sub:
                low = (BigInteger)left.Minimum - right.Maximum;
                high = (BigInteger)left.Maximum - right.Minimum;
                break;
            case BinaryOp.Mul:
                BigInteger[] products =
                [
                    (BigInteger)left.Minimum * right.Minimum, (BigInteger)left.Minimum * right.Maximum,
                    (BigInteger)left.Maximum * right.Minimum, (BigInteger)left.Maximum * right.Maximum
                ];
                low = products.Min();
                high = products.Max();
                break;
            default:
                result = default;
                return false;
        }

        return TryCreate(low, high, out result);
    }

    internal static bool TryFloorDivide(SamplingIntegerRange numerator, SamplingIntegerRange denominator,
        out SamplingIntegerRange result)
    {
        result = default;
        if (denominator.Minimum != denominator.Maximum || denominator.Minimum == 0)
        {
            return false;
        }

        BigInteger first = Floor(numerator.Minimum, denominator.Minimum);
        BigInteger last = Floor(numerator.Maximum, denominator.Minimum);
        return TryCreate(BigInteger.Min(first, last), BigInteger.Max(first, last), out result);
    }

    private static BigInteger Floor(BigInteger numerator, BigInteger denominator)
    {
        BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        return !remainder.IsZero && numerator.Sign != denominator.Sign ? quotient - 1 : quotient;
    }

    private static bool TryCreate(BigInteger low, BigInteger high, out SamplingIntegerRange result)
    {
        result = default;
        if (low < long.MinValue || high > long.MaxValue)
        {
            return false;
        }

        result = new SamplingIntegerRange((long)low, (long)high);
        return true;
    }
}
