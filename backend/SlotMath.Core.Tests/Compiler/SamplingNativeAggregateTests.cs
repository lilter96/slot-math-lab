using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Compiler;

[Collection("SamplingOptions")]
public sealed class SamplingNativeAggregateTests
{
    private static FieldAccessExpr S(string name) => new()
    {
        Target = "state",
        Path = [name]
    };

    private static ConstantExpr N(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString()
    };

    private static AggregateExpr A(AggregateFunc function, Expression? value = null, Expression? predicate = null) =>
        new()
        {
            Func = function,
            StateKey = "values",
            ItemName = "item",
            ItemType = ExprType.Number,
            ValueExpr = value,
            Predicate = predicate
        };

    private static (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots,
        Func<SamplingFrame, ExprValue> Run) Bind(Expression expression, SamplingTier tier, object? values,
            SamplingShapes? shapes = null)
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

        var initial = new Dictionary<string, object?>
        {
            ["values"] = values,
            ["item"] = 777,
            ["flag"] = false
        };
        foreach (var name in initial.Keys)
        {
            Slot(name);
        }

        var owner = new SamplingExpressions(Slot, tier, shapes: shapes);
        Func<SamplingFrame, ExprValue> run = owner.Compile(expression);
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], initial, true);
        frame.Reset(default);
        return (owner, frame, slots, run);
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void AllNumericAggregatesRetainIndependentWideAndRationalResults(SamplingTier tier)
    {
        ExprValue[] values =
            [ExprValue.Rational(-1, 3), ExprValue.Number(BigInteger.One << 90), ExprValue.Rational(2, 7)];
        var expected = new Dictionary<AggregateFunc, (BigInteger, BigInteger)>
        {
            [AggregateFunc.Sum] = ((BigInteger.One << 90) * 21 - 1, 21),
            [AggregateFunc.Product] = (-(BigInteger.One << 91), 21),
            [AggregateFunc.Min] = (-1, 3),
            [AggregateFunc.Max] = (BigInteger.One << 90, 1)
        };
        foreach (AggregateFunc operation in expected.Keys)
        {
            (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots,
                Func<SamplingFrame, ExprValue> Run) engine = Bind(A(operation, S("item")), tier, values);
            for (var repeat = 0; repeat < 100; repeat++)
            {
                ExprValue actual = engine.Run(engine.Frame);
                Assert.Equal(expected[operation], (actual.NumberNumerator, actual.NumberDenominator));
                Assert.Equal(777, engine.Frame.Cells[engine.Slots["item"]].Value.AsInteger());
            }

            (Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) native =
                SamplingCodegen.CompileValue(engine.Owner, name => engine.Slots[name], A(operation, S("item")));
            Assert.Empty(native.Operands.Closures);
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void ImplicitSelectionRetainsLegacyIntegerTextAndZeroScores(SamplingTier tier)
    {
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Run) sum = Bind(A(AggregateFunc.Sum), tier,
                new object?[] { " 2 ", "-3", "1/2", "1.5", "word", true, null, 7 });
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Run) product = Bind(A(AggregateFunc.Product), tier, new object?[] { "2", "-3", 7 });
        for (var repeat = 0; repeat < 100; repeat++)
        {
            Assert.Equal(6, sum.Run(sum.Frame).AsInteger());
            Assert.Equal(-42, product.Run(product.Frame).AsInteger());
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void FilteredEmptyInputsKeepIdentitiesAndLocatedExtremaErrors(SamplingTier tier)
    {
        var never = new ConstantExpr
        {
            Kind = ConstantKind.Boolean,
            Value = "false"
        };
        foreach (AggregateFunc operation in new[]
                 {
                     AggregateFunc.Sum, AggregateFunc.Product, AggregateFunc.Min, AggregateFunc.Max
                 })
        {
            (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots,
                Func<SamplingFrame, ExprValue> Run) engine = Bind(A(operation, S("missing"), never), tier,
                    new[] { 1, 2 });
            for (var repeat = 0; repeat < 100; repeat++)
            {
                if (operation is AggregateFunc.Min or AggregateFunc.Max)
                {
                    var error = Assert.Throws<ExpressionEvaluationException>(() => engine.Run(engine.Frame));
                    Assert.Equal(EvalErrorCodes.EmptyMinMax, error.Code);
                    Assert.Equal(operation.ToString().ToLowerInvariant(), error.Location);
                }
                else
                {
                    Assert.Equal(operation == AggregateFunc.Product ? BigInteger.One : BigInteger.Zero,
                        engine.Run(engine.Frame).AsInteger());
                }

                Assert.Equal(777, engine.Frame.Cells[engine.Slots["item"]].Value.AsInteger());
            }
        }
    }

    [Fact]
    public void SelectorErrorsAndCancellationRestoreBindingsAndMatchTheExistingSampledTier()
    {
        Expression expression = A(AggregateFunc.Sum, new IfExpr
        {
            Condition = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = S("item"),
                Right = N(1)
            },
            ThenExpr = new ConstantExpr
            {
                Kind = ConstantKind.Boolean,
                Value = "true"
            },
            ElseExpr = new BinaryExpr
            {
                Op = BinaryOp.Div,
                Left = N(1),
                Right = N(0)
            }
        });
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Run) reference = Bind(expression, SamplingTier.Interpreted, new[] { 1, 2 });
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Run) native = Bind(expression, SamplingTier.Compiled, new[] { 1, 2 });
        var expected = Assert.Throws<ExpressionEvaluationException>(() => reference.Run(reference.Frame));
        var actual = Assert.Throws<ExpressionEvaluationException>(() => native.Run(native.Frame));
        Assert.Equal(expected.Code, actual.Code);
        Assert.Equal(expected.Location, actual.Location);
        Assert.Equal(777, native.Frame.Cells[native.Slots["item"]].Value.AsInteger());
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Run) cancelled = Bind(A(AggregateFunc.Product, S("item")), SamplingTier.Compiled,
                Enumerable.Repeat(1, 1024).ToArray());
        cancelled.Frame.CancellationToken = new CancellationToken(true);
        Assert.Throws<OperationCanceledException>(() => cancelled.Run(cancelled.Frame));
        Assert.Equal(777, cancelled.Frame.Cells[cancelled.Slots["item"]].Value.AsInteger());
    }

    [Fact]
    public void SharedCodeDistinguishesOperationsAndResetsForCurrentSources()
    {
        var shapes = new SamplingShapes();
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Run) sum = Bind(A(AggregateFunc.Sum, S("item")), SamplingTier.Compiled, new[] { 2, 3 }, shapes);
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Run) product = Bind(A(AggregateFunc.Product, S("item")), SamplingTier.Compiled, new[] { 2, 3 }, shapes);
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Run) minimum = Bind(A(AggregateFunc.Min, S("item")), SamplingTier.Compiled, new[] { 2, 3 }, shapes);
        for (var repeat = 1; repeat <= 100; repeat++)
        {
            ExprValue values = ExprValue.Array([ExprValue.Number(repeat), ExprValue.Number(repeat + 1)]);
            sum.Frame.Cells[sum.Slots["values"]].SetTyped(values);
            product.Frame.Cells[product.Slots["values"]].SetTyped(values);
            minimum.Frame.Cells[minimum.Slots["values"]].SetTyped(values);
            Assert.Equal(2 * repeat + 1, sum.Run(sum.Frame).AsInteger());
            Assert.Equal(repeat * (repeat + 1), product.Run(product.Frame).AsInteger());
            Assert.Equal(repeat, minimum.Run(minimum.Frame).AsInteger());
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void NestedArraySourcesAndShadowedItemBindingsRetainOuterState(SamplingTier tier)
    {
        Expression expression = new MapExpr
        {
            StateKey = "values",
            ItemName = "item",
            ItemType = ExprType.Array,
            Body = A(AggregateFunc.Sum, S("item")) with
            {
                StateKey = "item"
            }
        };
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Run) engine = Bind(expression, tier, new object[] { new[] { 1, 2 }, new[] { 3, 4 } });
        for (var repeat = 0; repeat < 100; repeat++)
        {
            Assert.Equal(new BigInteger[] { 3, 7 },
                engine.Run(engine.Frame).ArrayValue!.Select(value => value.AsInteger()));
            Assert.Equal(777, engine.Frame.Cells[engine.Slots["item"]].Value.AsInteger());
        }
    }
}
