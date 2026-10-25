using System.Globalization;
using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Compiler;

public sealed class IntegerFloorDivisionTests
{
    private static ConstantExpr N(long value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString(CultureInfo.InvariantCulture)
    };

    private static FieldAccessExpr F(string key) => new()
    {
        Target = "state",
        Path = [key]
    };

    private static BinaryExpr Divide(Expression left, Expression right) => new()
    {
        Op = BinaryOp.Div,
        Left = left,
        Right = right
    };

    private static CallExpr Floor(Expression value) => new()
    {
        Function = "floor",
        Args = [value]
    };

    private static (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Evaluate) Bind(
        Expression expression, SamplingTier tier, Dictionary<string, object?>? initial = null)
    {
        initial ??= new Dictionary<string, object?>();
        var slots = new Dictionary<string, int>();

        int Slot(string name)
        {
            if (!slots.TryGetValue(name, out var index))
            {
                slots[name] = index = slots.Count;
            }

            return index;
        }

        foreach (var name in initial.Keys)
        {
            Slot(name);
        }

        var owner = new SamplingExpressions(Slot, tier);
        Func<SamplingFrame, ExprValue> evaluate = owner.Compile(expression);
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], initial, true);
        frame.Reset(default);
        return (frame, slots, evaluate);
    }

    private static BigInteger Expected(BigInteger numerator, BigInteger denominator)
    {
        BigInteger result = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        return remainder != 0 && numerator.Sign != denominator.Sign ? result - 1 : result;
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void SignedIntegerPairsAndExtremesMatchAnIndependentBigIntegerOracle(SamplingTier tier)
    {
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Evaluate) engine =
            Bind(Floor(Divide(F("left"), F("right"))), tier);
        long[] values =
        [
            long.MinValue, long.MinValue + 1, -9007199254740993, -17, -5, -1, 0, 1, 5, 17, 9007199254740993,
            long.MaxValue - 1, long.MaxValue
        ];
        foreach (var left in values.Concat(Enumerable.Range(-20, 41).Select(value => (long)value)))
        {
            foreach (var right in values.Concat(Enumerable.Range(-9, 19).Select(value => (long)value)))
            {
                engine.Frame.Cells[engine.Slots["left"]].SetTyped(ExprValue.Number(left));
                engine.Frame.Cells[engine.Slots["right"]].SetTyped(ExprValue.Number(right));
                if (right == 0)
                {
                    var error = Assert.Throws<ExpressionEvaluationException>(() => engine.Evaluate(engine.Frame));
                    Assert.Equal(EvalErrorCodes.DivisionByZero, error.Code);
                    Assert.Equal("/", error.Location);
                }
                else
                {
                    Assert.Equal(Expected(left, right), engine.Evaluate(engine.Frame).AsInteger());
                }
            }
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void ProperRationalsAndWideIntegersKeepTheExactFallback(SamplingTier tier)
    {
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Evaluate) engine =
            Bind(Floor(Divide(F("left"), F("right"))), tier);
        (ExprValue, ExprValue)[] cases = new[]
        {
            (ExprValue.Rational(1, 3), ExprValue.Rational(-2, 7)),
            (ExprValue.Rational(-1, 3), ExprValue.Rational(-2, 7)),
            (ExprValue.Number(BigInteger.One << 130), ExprValue.Number(-17)),
            (ExprValue.Number(-(BigInteger.One << 130) - 3), ExprValue.Number(17)),
            (ExprValue.Number(long.MinValue), ExprValue.Number(-1))
        };
        for (var repeat = 0; repeat < 20; repeat++)
        {
            foreach ((ExprValue left, ExprValue right) in cases)
            {
                engine.Frame.Cells[engine.Slots["left"]].SetTyped(left);
                engine.Frame.Cells[engine.Slots["right"]].SetTyped(right);
                Assert.Equal(Expected(left.NumberNumerator * right.NumberDenominator,
                    left.NumberDenominator * right.NumberNumerator), engine.Evaluate(engine.Frame).AsInteger());
            }
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void OperandErrorsKeepEagerLeftToRightOrderAndLazyParents(SamplingTier tier)
    {
        Expression badIndex = new CallExpr
        {
            Function = "index",
            Args = [F("values"), N(-1)]
        };
        Expression[] expressions =
        [
            Floor(Divide(F("missing-left"), F("missing-right"))),
            Floor(Divide(new ConstantExpr
            {
                Kind = ConstantKind.Boolean,
                Value = "false"
            }, badIndex)),
            Floor(Divide(N(1), N(0))),
            new CallExpr
            {
                Function = "floor",
                Args = [Divide(N(1), N(2)), badIndex]
            }
        ];
        var initial = new Dictionary<string, object?>
        {
            ["values"] = new object[] { 3 }
        };
        foreach (Expression expression in expressions)
        {
            var expected = Assert.Throws<ExpressionEvaluationException>(() => ExactExpressionEvaluator.Evaluate(
                expression, new EvalContext
                {
                    State = initial
                }));
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Evaluate) engine =
                Bind(expression, tier, initial);
            var actual = Assert.Throws<ExpressionEvaluationException>(() => engine.Evaluate(engine.Frame));
            Assert.Equal(expected.Code, actual.Code);
            Assert.Equal(expected.Location, actual.Location);
            Expression lazy = new IfExpr
            {
                Condition = new ConstantExpr
                {
                    Kind = ConstantKind.Boolean,
                    Value = "false"
                },
                ThenExpr = expression,
                ElseExpr = N(7)
            };
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Evaluate) parent =
                Bind(lazy, tier, initial);
            Assert.Equal(new BigInteger(7), parent.Evaluate(parent.Frame).AsInteger());
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void IteratorIndicesAndShapeSharingUseCurrentOperands(SamplingTier tier)
    {
        Expression map = new MapExpr
        {
            StateKey = "values",
            ItemName = "item",
            IndexName = "position",
            ItemType = ExprType.Number,
            Body = Floor(Divide(F("position"), N(5)))
        };
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Evaluate) engine = Bind(map,
            tier, new Dictionary<string, object?>
            {
                ["values"] = Enumerable.Range(0, 30).Select(value => (object)value).ToArray()
            });
        for (var repeat = 0; repeat < 100; repeat++)
        {
            Assert.Equal(Enumerable.Range(0, 30).Select(value => new BigInteger(value / 5)),
                engine.Evaluate(engine.Frame).ArrayValue!.Select(value => value.AsInteger()));
        }

        var shapes = new SamplingShapes();
        var slots = new Dictionary<string, int>
        {
            ["left"] = 0,
            ["right"] = 1
        };
        var owner = new SamplingExpressions(name => slots[name], SamplingTier.Compiled, shapes: shapes);
        Func<SamplingFrame, ExprValue> divided = owner.Compile(Floor(Divide(F("left"), F("right"))));
        Func<SamplingFrame, ExprValue> plain = owner.Compile(Floor(F("left")));
        var frame = new SamplingFrame(slots, new SamplingCell[2], new Dictionary<string, object?>
        {
            ["left"] = 7,
            ["right"] = 2
        });
        frame.Reset(default);
        Assert.Equal(new BigInteger(3), divided(frame).AsInteger());
        Assert.Equal(new BigInteger(7), plain(frame).AsInteger());
    }
}
