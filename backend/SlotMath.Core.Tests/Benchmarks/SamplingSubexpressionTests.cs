using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Benchmarks;

[Collection("SamplingOptions")]
public class SamplingSubexpressionTests
{
    private static FieldAccessExpr Field(string key) => new()
    {
        Target = "state",
        Path = [key]
    };

    private static ConstantExpr Number(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString()
    };

    private static BinaryExpr Add(Expression left, Expression right) =>
        new()
        {
            Op = BinaryOp.Add,
            Left = left,
            Right = right
        };

    private static CallExpr Index(string key, Expression index) =>
        new()
        {
            Function = "index",
            Args = [Field(key), index]
        };

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void LazyRepeatedSelectorsResetForEveryItemAndKeepSharedOperandsDistinct(SamplingTier tier)
    {
        var shapes = new SamplingShapes();
        foreach (var key in new[] { "left", "right" })
        {
            Expression Next()
            {
                return Add(Field("item"), Number(1));
            }

            Expression Read()
            {
                return Index(key, Next());
            }

            var expression = new MapExpr
            {
                StateKey = "source",
                ItemName = "item",
                ItemType = ExprType.Number,
                Body = Add(
                    new IfExpr
                    {
                        Condition = new CompareExpr
                        {
                            Op = CompareOp.Eq,
                            Left = Field("item"),
                            Right = Number(0)
                        },
                        ThenExpr = Number(0),
                        ElseExpr = Read()
                    }, Read())
            };
            var initial = new Dictionary<string, object?>
            {
                ["source"] = new[] { 0, 1, 2, 0 },
                ["left"] = new[] { 1, 10, 20, 30 },
                ["right"] = new[] { 2, 100, 200, 300 },
                ["item"] = 999
            };
            Dictionary<string, int> layout = initial.Keys.Select((name, index) => (name, index))
                .ToDictionary(pair => pair.name, pair => pair.index);
            var frame = new SamplingFrame(layout, new SamplingCell[layout.Count], initial);
            Func<SamplingFrame, ExprValue> run =
                new SamplingExpressions(name => layout[name], tier, shapes: shapes).Compile(expression);
            BigInteger[] expected = key == "left" ? [10, 40, 60, 10] : [100, 400, 600, 100];
            for (var round = 0; round < 100; round++)
            {
                frame.Reset(default);
                ExprValue result = run(frame);
                Assert.Equal(expected, result.ArrayValue!.Select(value => value.AsInteger()));
                Assert.Equal(999, frame.Read(layout["item"], "item").AsInteger());
            }
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void RepeatedInvalidReadsRemainLazyAndKeepTheirErrorLocationAndRestoredBindings(SamplingTier tier)
    {
        Expression Read()
        {
            return Index("values", Field("item"));
        }

        var expression = new MapExpr
        {
            StateKey = "source",
            ItemName = "item",
            ItemType = ExprType.Number,
            Body = new IfExpr
            {
                Condition = new CompareExpr
                {
                    Op = CompareOp.Lt,
                    Left = Field("item"),
                    Right = Number(0)
                },
                ThenExpr = Number(0),
                ElseExpr = Add(Read(), Read())
            }
        };
        var layout = new Dictionary<string, int>
        {
            ["source"] = 0,
            ["values"] = 1,
            ["item"] = 2
        };
        var initial = new Dictionary<string, object?>();
        initial.Add("source", new[] { -1, 0, 4 });
        initial.Add("values", new[] { 10 });
        initial.Add("item", 999);
        var frame = new SamplingFrame(layout, new SamplingCell[3], initial);
        Func<SamplingFrame, ExprValue> run = new SamplingExpressions(name => layout[name], tier).Compile(expression);
        for (var round = 0; round < 100; round++)
        {
            frame.Reset(default);
            var error = Assert.Throws<ExpressionEvaluationException>(() => run(frame));
            Assert.Equal(EvalErrorCodes.IndexOutOfRange, error.Code);
            Assert.Equal("index[4]", error.Location);
            Assert.Equal(999, frame.Read(layout["item"], "item").AsInteger());
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void InvariantReadsRemainLazyAndResetWhenTheSelectorRunsAgain(SamplingTier tier)
    {
        var expression = new MapExpr
        {
            StateKey = "source",
            ItemName = "item",
            ItemType = ExprType.Number,
            Body = new IfExpr
            {
                Condition = new CompareExpr
                {
                    Op = CompareOp.Lt,
                    Left = Field("item"),
                    Right = Number(0)
                },
                ThenExpr = Number(0),
                ElseExpr = Add(Field("item"), Index("values", Number(0)))
            }
        };
        var layout = new Dictionary<string, int>
        {
            ["source"] = 0,
            ["values"] = 1,
            ["item"] = 2
        };
        var frame = new SamplingFrame(layout, new SamplingCell[3], new Dictionary<string, object?>());
        Func<SamplingFrame, ExprValue> run = new SamplingExpressions(name => layout[name], tier).Compile(expression);
        for (var round = 0; round < 100; round++)
        {
            frame.Cells[0] = SamplingCell.Typed(ExprValue.Array([ExprValue.Number(-1)]));
            frame.Cells[1] = default;
            Assert.Equal(BigInteger.Zero, Assert.Single(run(frame).ArrayValue!).AsInteger());
            frame.Cells[0] = SamplingCell.Typed(ExprValue.Array([]));
            Assert.Empty(run(frame).ArrayValue!);
            frame.Cells[0] = SamplingCell.Typed(ExprValue.Array([ExprValue.Number(2), ExprValue.Number(3)]));
            var error = Assert.Throws<ExpressionEvaluationException>(() => run(frame));
            Assert.Equal("values", error.Location);
            frame.Cells[1] = SamplingCell.Typed(ExprValue.Array([ExprValue.Number(round)]));
            Assert.Equal(new BigInteger[] { round + 2, round + 3 },
                run(frame).ArrayValue!.Select(value => value.AsInteger()));
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void InnerInvariantReadsFollowOuterBindingsAndFoldAccumulatorsRemainVariant(SamplingTier tier)
    {
        var expression = new MapExpr
        {
            StateKey = "outer",
            ItemName = "outerItem",
            ItemType = ExprType.Number,
            Body = new FoldExpr
            {
                StateKey = "inner",
                ItemName = "innerItem",
                ItemType = ExprType.Number,
                AccName = "total",
                Init = Number(0),
                Body = Add(Field("total"), Add(Field("outerItem"), Field("innerItem")))
            }
        };
        var initial = new Dictionary<string, object?>();
        initial.Add("outer", new[] { 1, 4 });
        initial.Add("inner", new[] { 2, 3 });
        initial.Add("outerItem", 90);
        initial.Add("innerItem", 91);
        initial.Add("total", 92);
        Dictionary<string, int> layout = initial.Keys.Select((key, index) => (key, index))
            .ToDictionary(pair => pair.key, pair => pair.index);
        var frame = new SamplingFrame(layout, new SamplingCell[layout.Count], initial);
        Func<SamplingFrame, ExprValue> run = new SamplingExpressions(name => layout[name], tier).Compile(expression);
        for (var round = 0; round < 100; round++)
        {
            frame.Reset(default);
            Assert.Equal(new BigInteger[] { 7, 13 }, run(frame).ArrayValue!.Select(value => value.AsInteger()));
            Assert.Equal(90, frame.Read(layout["outerItem"], "outerItem").AsInteger());
            Assert.Equal(91, frame.Read(layout["innerItem"], "innerItem").AsInteger());
            Assert.Equal(92, frame.Read(layout["total"], "total").AsInteger());
        }
    }
}
