using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Math.Structured;

public sealed class HigherMomentInferenceTests
{
    [Fact]
    public void IndependentSumsUseMomentSequencesInsteadOfTheirJointOutcomeSpace()
    {
        const int count = 1000;
        var budget = new WorkBudget(2000000, TimeSpan.FromSeconds(10), CancellationToken.None);
        var factory = new TermFactory(budget);
        var inference = new Inference(factory);
        Term[] bits = Enumerable.Range(0, count).Select(index => factory.Table(
            factory.NewRv(index.ToString(), [BigInteger.One, BigInteger.One]), [V.Zero, V.One])).ToArray();
        Term total = factory.Lin(V.Zero, bits.Select(bit => (V.One, bit)).ToList());
        var mean = new Rational(count, 2);
        var variance = new Rational(count, 4);
        Assert.Equal(mean * mean * mean + 3 * mean * variance, inference.RawMoment(total, 3));
        Assert.Equal(mean * mean * mean * mean + 6 * mean * mean * variance + 3 * variance * variance
                     - new Rational(count, 8), inference.RawMoment(total, 4));
        Assert.True(budget.Used < 2000000);
    }

    [Fact]
    public void GuardsProductsAndConditionalsMatchIndependentOutcomeEnumeration()
    {
        var factory = new TermFactory(new WorkBudget(1000000, null, CancellationToken.None));
        var inference = new Inference(factory);
        Term a = factory.Table(factory.NewRv("a", [1, 2]), [ExprValue.Number(1), ExprValue.Number(3)]);
        Term b = factory.Table(factory.NewRv("b", [3, 1]), [ExprValue.Number(-1), ExprValue.Number(2)]);
        Term c = factory.Table(factory.NewRv("c", [2, 1]), [ExprValue.Number(2), ExprValue.Number(5)]);
        Term product = factory.Arith(BinaryOp.Mul, a, b);
        Term guard = factory.Cmp(CompareOp.Gt, factory.Arith(BinaryOp.Add, a, b), c);
        Term choice = factory.If(factory.Cmp(CompareOp.Eq, a, factory.Const(ExprValue.Number(1))), b, c);
        Rational expected = Rational.Zero, third = Rational.Zero, fourth = Rational.Zero;
        foreach (var (av, aw) in new[] { (1, 1), (3, 2) })
        {
            foreach (var (bv, bw) in new[] { (-1, 3), (2, 1) })
            {
                foreach (var (cv, cw) in new[] { (2, 2), (5, 1) })
                {
                    var weight = new Rational(aw * bw * cw, 36);
                    var selected = av == 1 ? bv : cv;
                    var value = av * bv + selected;
                    third += weight * new Rational(BigInteger.Pow(value, 3), 1);
                    fourth += weight * new Rational(BigInteger.Pow(value, 4), 1);
                    if (av + bv > cv)
                    {
                        expected += weight * av * bv * selected * cv * av;
                    }
                }
            }
        }

        Assert.Equal(expected, inference.ProductMany(guard, [product, choice, c, a]));
        Term total = factory.Arith(BinaryOp.Add, product, choice);
        Assert.Equal(third, inference.RawMoment(total, 3));
        Assert.Equal(fourth, inference.RawMoment(total, 4));
    }
}
