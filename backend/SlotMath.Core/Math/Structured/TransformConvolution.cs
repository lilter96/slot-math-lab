using System.Buffers;
using System.Numerics;
using SlotMath.Core.Expressions;

namespace SlotMath.Core.Math.Structured;

internal sealed partial class Inference
{
    private Law? TryTransformConvolution(Law a, Law b)
    {
        if ((long)a.Values.Length * b.Values.Length < 262144 || a.Values.Length < 64 || b.Values.Length < 64)
        {
            return null;
        }

        Rational step = Rational.Zero;

        (Rational Min, Rational Max)? Range(Law law)
        {
            Rational? first = null;
            Rational lo = Rational.Zero, hi = Rational.Zero;
            foreach (ExprValue value in law.Values)
            {
                f.Budget.Tick();
                if (value.Kind != ExprType.Number || value.NumberDenominator.GetBitLength() > 4096 ||
                    BigInteger.Abs(value.NumberNumerator).GetBitLength() > 4096)
                {
                    return null;
                }

                var number = new Rational(value.NumberNumerator, value.NumberDenominator);
                if (first is null)
                {
                    first = lo = hi = number;
                }
                else
                {
                    Rational delta = number - first.Value;
                    step = Common(step, delta.Sign < 0 ? -delta : delta);
                    if (step.Denominator.GetBitLength() > 4096)
                    {
                        return null;
                    }

                    if (number < lo)
                    {
                        lo = number;
                    }

                    if (number > hi)
                    {
                        hi = number;
                    }
                }
            }

            return (lo, hi);
        }

        if (Range(a) is not { } ra || Range(b) is not { } rb || step.IsZero)
        {
            return null;
        }

        Rational spanA = (ra.Max - ra.Min) / step, spanB = (rb.Max - rb.Min) / step;
        if (!spanA.Denominator.IsOne || !spanB.Denominator.IsOne ||
            spanA.Numerator + spanB.Numerator >= ExactIntegerConvolution.MaximumLength ||
            spanA.Numerator >= 4L * a.Values.Length || spanB.Numerator >= 4L * b.Values.Length)
        {
            return null;
        }

        var lengthA = (int)spanA.Numerator + 1;
        var lengthB = (int)spanB.Numerator + 1;
        BigInteger[] left = ArrayPool<BigInteger>.Shared.Rent(lengthA);
        using ExactStorageTracker.Lease? leftCapacity =
            f.Budget.Storage?.Hold(ExactStorageKind.ConvolutionLatticeCapacity, left.Length);
        BigInteger[]? right = null;
        try
        {
            right = ArrayPool<BigInteger>.Shared.Rent(lengthB);
            using ExactStorageTracker.Lease? rightCapacity =
                f.Budget.Storage?.Hold(ExactStorageKind.ConvolutionLatticeCapacity, right.Length);
            Span<BigInteger> x = left.AsSpan(0, lengthA), y = right.AsSpan(0, lengthB);
            x.Clear();
            y.Clear();

            void Load(Law law, Rational origin, BigInteger[] destination)
            {
                for (var i = 0; i < law.Values.Length; i++)
                {
                    ExprValue value = law.Values[i];
                    Rational position = (new Rational(value.NumberNumerator, value.NumberDenominator) - origin) / step;
                    // The rational gcd construction above proves these indices are integral and in range.
                    if (!position.Denominator.IsOne)
                    {
                        throw new StructuredInconsistencyException(
                            "A numeric law left its proven convolution lattice.");
                    }

                    destination[(int)position.Numerator] += law.Mass[i];
                }
            }

            Load(a, ra.Min, left);
            Load(b, rb.Min, right);

            void Spend(long units)
            {
                f.Budget.Tick(units);
                f.Budget.Check();
            }

            if (!ExactIntegerConvolution.TryTransform(x, y, Spend, out BigInteger[] product,
                    true, f.Budget.Storage, System.Math.Max(0, f.Budget.Limit - f.Budget.Used)))
            {
                return null;
            }

            Stats.TransformConvolutions++;
            using ExactStorageTracker.Lease? outputSlots =
                f.Budget.Storage?.Hold(ExactStorageKind.ConvolutionResultSlots, product.Length);
            Stats.LargestConvolutionSpan = System.Math.Max(Stats.LargestConvolutionSpan, product.Length);
            Rational origin = ra.Min + rb.Min;
            BigInteger denominator = Rational.Lcm(origin.Denominator, step.Denominator);
            BigInteger numerator = origin.Numerator * (denominator / origin.Denominator);
            BigInteger increment = step.Numerator * (denominator / step.Denominator);
            using Masses masses = NewMasses(product.Length);
            for (var i = 0; i < product.Length; i++)
            {
                Spend(1);
                if (product[i].IsZero)
                {
                    continue;
                }

                Stats.LargestConvolutionCoefficientBits = System.Math.Max(Stats.LargestConvolutionCoefficientBits,
                    product[i].GetBitLength());
                ExprValue value = ExprValue.Rational(numerator + i * increment, denominator);
                masses.Add(value, V.Hash(value), product[i]);
            }

            return Build(masses, a.Total * b.Total);
        }
        finally
        {
            ArrayPool<BigInteger>.Shared.Return(left, true);
            if (right is not null)
            {
                ArrayPool<BigInteger>.Shared.Return(right, true);
            }
        }
    }
}
