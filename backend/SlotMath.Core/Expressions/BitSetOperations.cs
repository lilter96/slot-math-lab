using System.Numerics;
using System.Runtime.CompilerServices;
using SlotMath.Core.Model;

namespace SlotMath.Core.Expressions;

/// <summary>Pure finite sets over positions 0..63, represented by exact integers.</summary>
internal static class BitSetOperations
{
    public static int Arity(BitSetOp op) => op switch
    {
        BitSetOp.Union or BitSetOp.Intersect or BitSetOp.Except or BitSetOp.Shift => 2,
        BitSetOp.Count or BitSetOp.First => 1,
        _ => 0,
    };

    public static ExprValue Evaluate(BitSetOp op, int arity, ExprValue first, ExprValue second)
    {
        if (Arity(op) == 0)
            throw new ExpressionEvaluationException("EVAL_INVALID_OPERATOR", "Unknown finite-set operation.", "bitset.op");
        if (arity != Arity(op))
            throw new ExpressionEvaluationException("EVAL_ARITY", $"Finite-set {op} requires {Arity(op)} operands, got {arity}.", "bitset.args");
        return op switch
        {
            BitSetOp.Union => Union(first, second),
            BitSetOp.Intersect => Intersect(first, second),
            BitSetOp.Except => Except(first, second),
            BitSetOp.Shift => Shift(first, second),
            BitSetOp.Count => Count(first),
            BitSetOp.First => First(first),
            _ => throw new InvalidOperationException(),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong Mask(ExprValue value, string location)
    {
        if (value.IsInlineInteger && value.InlineInteger >= 0) return (ulong)value.InlineInteger;
        return CheckedMask(value, location);
    }

    // Exact large values and invalid operands are uncommon. Keep their checks
    // and exception construction out of each generated expression's hot code.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong CheckedMask(ExprValue value, string location)
    {
        if (value.Kind != ExprType.Number)
            throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "A finite-set mask must be a Number.", location);
        if (!value.NumberDenominator.IsOne || value.NumberNumerator < 0 || value.NumberNumerator > ulong.MaxValue)
            throw new ExpressionEvaluationException("EVAL_BITSET_RANGE", "A finite-set mask must be an integer in [0, 2^64 - 1].", location);
        return (ulong)value.NumberNumerator;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ExprValue Value(ulong mask) => mask <= long.MaxValue
        ? ExprValue.Number((long)mask) : LargeValue(mask);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ExprValue LargeValue(ulong mask) => ExprValue.Number(new BigInteger(mask));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Union(ExprValue first, ExprValue second) => Value(Mask(first, "bitset.args[0]") | Mask(second, "bitset.args[1]"));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Intersect(ExprValue first, ExprValue second) => Value(Mask(first, "bitset.args[0]") & Mask(second, "bitset.args[1]"));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Except(ExprValue first, ExprValue second) => Value(Mask(first, "bitset.args[0]") & ~Mask(second, "bitset.args[1]"));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Count(ExprValue first) => ExprValue.Number(BitOperations.PopCount(Mask(first, "bitset.args[0]")));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue First(ExprValue first)
    {
        var mask = Mask(first, "bitset.args[0]");
        return ExprValue.Number(mask == 0 ? -1 : BitOperations.TrailingZeroCount(mask));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ExprValue Shift(ExprValue first, ExprValue second)
    {
        var mask = Mask(first, "bitset.args[0]");
        var distance = Distance(second);
        return Value(distance >= 0 ? mask << distance : mask >> -distance);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int Distance(ExprValue value) => value.IsInlineInteger && value.InlineInteger is >= -63 and <= 63
        ? (int)value.InlineInteger : InvalidShift(value);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int InvalidShift(ExprValue value) => throw new ExpressionEvaluationException(
        value.Kind == ExprType.Number ? "EVAL_BITSET_RANGE" : "EVAL_TYPE_ERROR",
        "A finite-set shift distance must be an integer in [-63, 63].", "bitset.args[1]");
}
