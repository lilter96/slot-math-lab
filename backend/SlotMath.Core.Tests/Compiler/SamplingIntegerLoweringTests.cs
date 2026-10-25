using System.Globalization;
using System.Numerics;
using System.Reflection;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Compiler;

[Collection("SamplingOptions")]
public sealed class SamplingIntegerLoweringTests
{
    private static ConstantExpr N(long value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString(CultureInfo.InvariantCulture)
    };

    private static FieldAccessExpr S(string name) => new()
    {
        Target = "state",
        Path = [name]
    };

    private static BinaryExpr B(BinaryOp operation, Expression left, Expression right) =>
        new()
        {
            Op = operation,
            Left = left,
            Right = right
        };

    private static CallExpr Floor(Expression numerator, long denominator) => new()
    {
        Function = "floor",
        Args = [B(BinaryOp.Div, numerator, N(denominator))]
    };

    private static CallExpr Index(Expression source, Expression index) => new()
    {
        Function = "index",
        Args = [source, index]
    };

    private static MapExpr Map(Expression body) => new()
    {
        StateKey = "items",
        ItemName = "item",
        IndexName = "position",
        ItemType = ExprType.Number,
        Body = body
    };

    private static (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots,
        Func<SamplingFrame, ExprValue> Evaluate) Bind(Expression expression, SamplingTier tier,
            Dictionary<string, object?> initial, SamplingShapes? shapes = null)
    {
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

        var owner = new SamplingExpressions(Slot, tier, shapes: shapes);
        Func<SamplingFrame, ExprValue> evaluate = owner.Compile(expression);
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], initial, true);
        frame.Reset(default);
        return (owner, frame, slots, evaluate);
    }

    private static Dictionary<string, object?> Initial() => new()
    {
        ["items"] = Enumerable.Range(0, 66).Select(value => (object)value).ToArray(),
        ["values"] = new object[] { 101, 103, 107, 109, 113 },
        ["position"] = 777,
        ["flag"] = false,
        ["badMask"] = "not a mask"
    };

    private static BigInteger FloorOracle(BigInteger numerator, BigInteger denominator)
    {
        BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        return !remainder.IsZero && numerator.Sign != denominator.Sign ? quotient - 1 : quotient;
    }

    [Fact]
    public void IntervalProofRetainsSignedExtremaAndDeclinesOverflowAndVariableDivisors()
    {
        var left = new SamplingIntegerRange(-5, 7);
        var right = new SamplingIntegerRange(-3, 2);
        Assert.True(SamplingIntegerRange.TryBinary(BinaryOp.Add, left, right, out SamplingIntegerRange sum));
        Assert.Equal(new SamplingIntegerRange(-8, 9), sum);
        Assert.True(SamplingIntegerRange.TryBinary(BinaryOp.Sub, left, right, out SamplingIntegerRange difference));
        Assert.Equal(new SamplingIntegerRange(-7, 10), difference);
        Assert.True(SamplingIntegerRange.TryBinary(BinaryOp.Mul, left, right, out SamplingIntegerRange product));
        Assert.Equal(new SamplingIntegerRange(-21, 15), product);
        Assert.False(SamplingIntegerRange.TryBinary(BinaryOp.Add,
            new SamplingIntegerRange(0, long.MaxValue), new SamplingIntegerRange(1, 1), out _));
        Assert.False(SamplingIntegerRange.TryBinary(BinaryOp.Sub,
            new SamplingIntegerRange(long.MinValue, 0), new SamplingIntegerRange(1, 1), out _));
        Assert.False(SamplingIntegerRange.TryBinary(BinaryOp.Mul,
            new SamplingIntegerRange(long.MinValue, long.MaxValue), new SamplingIntegerRange(-1, -1), out _));
        Assert.False(SamplingIntegerRange.TryFloorDivide(left, new SamplingIntegerRange(0, 0), out _));
        Assert.False(SamplingIntegerRange.TryFloorDivide(left, new SamplingIntegerRange(2, 3), out _));
        Assert.False(SamplingIntegerRange.TryFloorDivide(new SamplingIntegerRange(long.MinValue, long.MinValue),
            new SamplingIntegerRange(-1, -1), out _));
        Assert.True(SamplingIntegerRange.TryFloorDivide(left, new SamplingIntegerRange(-3, -3),
            out SamplingIntegerRange quotient));
        Assert.Equal(new SamplingIntegerRange(-3, 1), quotient);
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void RepeatedIndexArithmeticKeepsFloorSemanticsCacheResetsAndShadowedState(SamplingTier tier)
    {
        Expression numerator = B(BinaryOp.Sub, S("position"), N(17));
        Expression quotient = Floor(numerator, 5);
        Expression remainder = B(BinaryOp.Sub, numerator, B(BinaryOp.Mul, quotient, N(5)));
        Expression select = Index(S("values"), remainder);
        Expression body = new IfExpr
        {
            Condition = new CompareExpr
            {
                Op = CompareOp.Gte,
                Left = quotient,
                Right = N(0)
            },
            ThenExpr = select,
            ElseExpr = select
        };
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Evaluate) engine = Bind(Map(body), tier, Initial());
        for (var repeat = 0; repeat < 100; repeat++)
        {
            var offset = repeat % 2;
            long[] values = [101 + offset, 103 + offset, 107 + offset, 109 + offset, 113 + offset];
            engine.Frame.Cells[engine.Slots["values"]]
                .SetTyped(ExprValue.Array(values.Select(ExprValue.Number).ToArray()));
            BigInteger[] expected = Enumerable.Range(0, 66).Select(index =>
            {
                BigInteger n = index - 17;
                return new BigInteger(values[(int)(n - FloorOracle(n, 5) * 5)]);
            }).ToArray();
            Assert.Equal(expected, engine.Evaluate(engine.Frame).ArrayValue!.Select(value => value.AsInteger()));
            Assert.Equal(new BigInteger(777), engine.Frame.Cells[engine.Slots["position"]].Value.AsInteger());
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void SignedFloorDivisorsAndTheirExtremesMatchIndependentExactArithmetic(SamplingTier tier)
    {
        foreach (var denominator in new[] { -9L, -1, 1, 5, long.MinValue, long.MaxValue })
        {
            (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots,
                Func<SamplingFrame, ExprValue> Evaluate) engine =
                    Bind(Map(Floor(B(BinaryOp.Sub, S("position"), N(17)), denominator)), tier, Initial());
            BigInteger[] expected = Enumerable.Range(0, 66).Select(index => FloorOracle(index - 17, denominator))
                .ToArray();
            for (var repeat = 0; repeat < 100; repeat++)
            {
                Assert.Equal(expected, engine.Evaluate(engine.Frame).ArrayValue!.Select(value => value.AsInteger()));
            }
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void UnprovenIntermediatesRetainWideAndRationalFallbacks(SamplingTier tier)
    {
        Expression[] bodies =
        [
            B(BinaryOp.Add, N(long.MaxValue), S("position")),
            B(BinaryOp.Sub, N(long.MinValue), S("position")),
            B(BinaryOp.Mul, N(long.MaxValue), S("position")),
            B(BinaryOp.Add, S("position"), B(BinaryOp.Div, N(1), N(2))),
            Floor(B(BinaryOp.Sub, N(long.MinValue), S("position")), -1)
        ];
        foreach (Expression body in bodies)
        {
            Expression expression = Map(body);
            Dictionary<string, object?> initial = Initial();
            ExprValue expected = ExactExpressionEvaluator.Evaluate(expression, new EvalContext
            {
                State = initial
            });
            (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots,
                Func<SamplingFrame, ExprValue> Evaluate) engine = Bind(expression, tier, initial);
            for (var repeat = 0; repeat < 100; repeat++)
            {
                ExprValue actual = engine.Evaluate(engine.Frame);
                Assert.Equal(expected.ArrayValue!.Select(value => (value.NumberNumerator, value.NumberDenominator)),
                    actual.ArrayValue!.Select(value => (value.NumberNumerator, value.NumberDenominator)));
            }
        }
    }

    [Fact]
    public void IndexOperandsRemainEagerAndConditionalPositionsRemainLazyAfterPromotion()
    {
        Expression badMask = new BitSetExpr
        {
            Op = BitSetOp.Count,
            Args = [S("badMask")]
        };
        Expression position = new IfExpr
        {
            Condition = S("flag"),
            ThenExpr = badMask,
            ElseExpr = S("position")
        };
        Expression expression = Map(Index(S("values"), position));
        Dictionary<string, object?> initial = Initial();
        initial["items"] = new object[] { 1, 2 };
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Evaluate) engine = Bind(expression, SamplingTier.Compiled, initial);
        for (var repeat = 0; repeat < 100; repeat++)
        {
            engine.Evaluate(engine.Frame);
        }

        engine.Frame.Cells[engine.Slots["flag"]].SetTyped(ExprValue.Bool(true));
        var expectedState = new Dictionary<string, object?>(initial)
        {
            ["flag"] = true
        };
        var expected = Assert.Throws<ExpressionEvaluationException>(() => ExactExpressionEvaluator.Evaluate(expression,
            new EvalContext
            {
                State = expectedState
            }));
        var actual = Assert.Throws<ExpressionEvaluationException>(() => engine.Evaluate(engine.Frame));
        Assert.Equal(expected.Code, actual.Code);
        Assert.Equal(expected.Location, actual.Location);

        foreach (Expression source in new Expression[] { S("missing"), N(1) })
        {
            Expression invalid = Map(Index(source, B(BinaryOp.Add, S("position"), badMask)));
            (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots,
                Func<SamplingFrame, ExprValue> Evaluate) bound = Bind(invalid, SamplingTier.Compiled, initial);
            expected = Assert.Throws<ExpressionEvaluationException>(() => ExactExpressionEvaluator.Evaluate(invalid,
                new EvalContext
                {
                    State = initial
                }));
            actual = Assert.Throws<ExpressionEvaluationException>(() => bound.Evaluate(bound.Frame));
            Assert.Equal(expected.Code, actual.Code);
            Assert.Equal(expected.Location, actual.Location);
        }
    }

    [Fact]
    public void SharedNativeShapesKeepTheirOwnSignedDivisorOperands()
    {
        var shapes = new SamplingShapes();
        Expression first = Map(Floor(B(BinaryOp.Sub, S("position"), N(17)), 5));
        Expression second = Map(Floor(B(BinaryOp.Sub, S("position"), N(17)), -7));
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Evaluate) left = Bind(first, SamplingTier.Compiled, Initial(), shapes);
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Evaluate) right = Bind(second, SamplingTier.Compiled, Initial(), shapes);
        (Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) a =
            SamplingCodegen.CompileValue(left.Owner, name => left.Slots[name], first);
        (Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) b =
            SamplingCodegen.CompileValue(right.Owner, name => right.Slots[name], second);
        Assert.Same(a.Code.Method, b.Code.Method);
        for (var repeat = 0; repeat < 100; repeat++)
        {
            Assert.Equal(Enumerable.Range(0, 66).Select(index => FloorOracle(index - 17, 5)),
                a.Code(left.Frame, a.Operands).ArrayValue!.Select(value => value.AsInteger()));
            Assert.Equal(Enumerable.Range(0, 66).Select(index => FloorOracle(index - 17, -7)),
                b.Code(right.Frame, b.Operands).ArrayValue!.Select(value => value.AsInteger()));
        }
    }

    [Fact]
    public void SignedLiteralsKeepTheirFullPrecisionAcrossSharedCodeAndSpecializedZero()
    {
        var shapes = new SamplingShapes();
        MethodInfo? first = null;
        MethodInfo? zero = null;
        foreach (var value in new[] { 0L, 1, -1, long.MinValue, long.MaxValue, 9007199254740993 })
        {
            (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots,
                Func<SamplingFrame, ExprValue> Evaluate) engine = Bind(N(value), SamplingTier.Compiled,
                    new Dictionary<string, object?>(), shapes);
            (Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) native =
                SamplingCodegen.CompileValue(engine.Owner, name => engine.Slots[name], N(value));
            if (value == 0)
            {
                zero = native.Code.Method;
            }
            else
            {
                first ??= native.Code.Method;
                Assert.Same(first, native.Code.Method);
            }

            for (var repeat = 0; repeat < 100; repeat++)
            {
                ExprValue result = native.Code(engine.Frame, native.Operands);
                Assert.True(result.IsInlineInteger);
                Assert.Equal(new BigInteger(value), result.AsInteger());
            }
        }

        Assert.NotSame(first, zero);
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void SetCardinalityAndFirstStayExactForTheUnsignedHighBitAndEmptySet(SamplingTier tier)
    {
        Expression count = new BitSetExpr
        {
            Op = BitSetOp.Count,
            Args = [S("mask")]
        };
        Expression first = new BitSetExpr
        {
            Op = BitSetOp.First,
            Args = [S("mask")]
        };
        Expression expression = Map(B(BinaryOp.Add, count, first));
        Dictionary<string, object?> initial = Initial();
        initial["mask"] = 0;
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Evaluate) engine = Bind(expression, tier, initial);
        foreach (BigInteger mask in new[]
                 {
                     BigInteger.Zero, BigInteger.One, BigInteger.One << 63, new BigInteger(ulong.MaxValue)
                 })
        {
            var expectedCount = 0;
            var expectedFirst = -1;
            for (var bit = 0; bit < 64; bit++)
            {
                if ((mask & (BigInteger.One << bit)).IsZero)
                {
                    continue;
                }

                expectedCount++;
                if (expectedFirst < 0)
                {
                    expectedFirst = bit;
                }
            }

            engine.Frame.Cells[engine.Slots["mask"]].SetTyped(ExprValue.Number(mask));
            for (var repeat = 0; repeat < 100; repeat++)
            {
                Assert.All(engine.Evaluate(engine.Frame).ArrayValue!,
                    value => Assert.Equal(new BigInteger(expectedCount + expectedFirst), value.AsInteger()));
            }
        }

        engine.Frame.Cells[engine.Slots["mask"]].SetTyped(ExprValue.Number(-1));
        var invalid = new Dictionary<string, object?>(initial)
        {
            ["mask"] = -1
        };
        var expected = Assert.Throws<ExpressionEvaluationException>(() => ExactExpressionEvaluator.Evaluate(expression,
            new EvalContext
            {
                State = invalid
            }));
        var actual = Assert.Throws<ExpressionEvaluationException>(() => engine.Evaluate(engine.Frame));
        Assert.Equal(expected.Code, actual.Code);
        Assert.Equal(expected.Location, actual.Location);
    }
}
