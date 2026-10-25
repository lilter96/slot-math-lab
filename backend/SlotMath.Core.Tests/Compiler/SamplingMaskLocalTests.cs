using System.Globalization;
using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Compiler;

[Collection("SamplingOptions")]
public sealed class SamplingMaskLocalTests
{
    private static ConstantExpr N(BigInteger value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString(CultureInfo.InvariantCulture)
    };

    private static FieldAccessExpr S(string field) => new()
    {
        Target = "state",
        Path = [field]
    };

    private static BitSetExpr Set(BitSetOp operation, params Expression[] args) => new()
    {
        Op = operation,
        Args = args
    };

    private static GraphConfig Graph(params (string Field, Expression Value)[] writes)
    {
        Node[] nodes = writes.Select((write, index) => (Node)new ModifyStateNode
        {
            Id = "n" + index,
            OutputKey = write.Field,
            ExpressionId = "e" + index
        }).Append(new MetricsSinkNode
        {
            Id = "sink",
            WinCap = 100,
            WinStateKey = "out"
        }).ToArray();
        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Nodes = nodes,
            Edges = nodes.Zip(nodes.Skip(1), (a, b) => new Edge
            {
                Id = a.Id + b.Id,
                SourceNodeId = a.Id,
                TargetNodeId = b.Id,
                SourcePort = "state",
                TargetPort = "state"
            }).ToArray(),
            Expressions = writes.Select((write, index) => (write.Value, Key: "e" + index))
                .ToDictionary(pair => pair.Key, pair => pair.Value),
            InitialState = new Dictionary<string, JsonElement>
            {
                ["a"] = JsonSerializer.SerializeToElement(3),
                ["b"] = JsonSerializer.SerializeToElement(0),
                ["out"] = JsonSerializer.SerializeToElement(0)
            }
        };
    }

    private static (SamplingFrame Frame, SamplingOperands Operands,
        Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code,
        Dictionary<string, int> Slots) Bind(GraphConfig graph, Dictionary<string, object?> initial,
            bool promote = true, SamplingShapes? shapes = null)
    {
        var slots = new Dictionary<string, int>(StringComparer.Ordinal);

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

        IReadOnlySet<string> masks = SamplingMaskState.Infer(graph, []);
        var owner = new SamplingExpressions(Slot, SamplingTier.Compiled, shapes: shapes,
            maskFields: masks, inlineMaskFields: SamplingMaskState.InferInline(graph, masks),
            ir: CompilerIr.Build(graph, []));
        (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
            native = SamplingCodegen.CompileRun(owner, Slot, graph.Nodes[..^1], graph.Expressions!, BigInteger.One,
                promoteMasks: promote);
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], initial);
        frame.Reset(default);
        return (frame, native.Operands, native.Code, slots);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaskLocalsKeepIndependentBitResultsAndPublishOnEveryInvocation(bool wide)
    {
        BigInteger start = wide ? (BigInteger.One << 63) + 3 : 3;
        GraphConfig graph = Graph(
            ("a", Set(BitSetOp.Union, S("a"), N(8))),
            ("b", Set(BitSetOp.Shift, S("a"), N(-1))),
            ("a", Set(BitSetOp.Except, S("b"), S("a"))),
            ("out", Set(BitSetOp.Count, S("a"))));
        graph.InitialState!["a"] = JsonDocument.Parse(start.ToString()).RootElement.Clone();
        (SamplingFrame Frame, SamplingOperands Operands,
            Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, Dictionary<string, int> Slots)
            engine = Bind(graph, new Dictionary<string, object?>
            {
                ["a"] = start,
                ["b"] = 0,
                ["out"] = 0
            });
        Assert.Equal(2, engine.Operands.PromotedMaskFields);
        for (var repeat = 0; repeat < 100; repeat++)
        {
            engine.Frame.Cells[engine.Slots["a"]] = SamplingCell.FromRaw(start);
            engine.Code(engine.Frame, engine.Operands, new SeededRandom(1), null);
            BigInteger expectedB = (start | 8) >> 1;
            BigInteger expectedA = expectedB & (ulong.MaxValue ^ (start | 8));
            Assert.Equal(expectedA, engine.Frame.Read(engine.Slots["a"], "a").AsInteger());
            Assert.Equal(expectedB, engine.Frame.Read(engine.Slots["b"], "b").AsInteger());
            Assert.Equal(wide ? 2 : 1, engine.Frame.Read(engine.Slots["out"], "out").AsInteger());
            Assert.False(engine.Frame.Cells[engine.Slots["a"]].HasRaw);
        }
    }

    [Fact]
    public void FailedWritesPublishOnlyThePreviousCompletedMask()
    {
        GraphConfig graph = Graph(
            ("a", Set(BitSetOp.Union, S("a"), N(8))),
            ("a", new IfExpr
            {
                Condition = S("missing"),
                ThenExpr = N(5),
                ElseExpr = N(6)
            }),
            ("b", Set(BitSetOp.Union, S("a"), N(1))));
        foreach (var promote in new[] { false, true })
        {
            (SamplingFrame Frame, SamplingOperands Operands,
                Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, Dictionary<string, int>
                Slots) engine = Bind(graph, new Dictionary<string, object?>
                {
                    ["a"] = 3,
                    ["b"] = 0,
                    ["missing"] = null
                }, promote);
            Assert.Equal(promote ? 1 : 0, engine.Operands.PromotedMaskFields);
            var error = Assert.Throws<ExpressionEvaluationException>(() =>
                engine.Code(engine.Frame, engine.Operands, new SeededRandom(2), null));
            Assert.Equal("missing", error.Location);
            Assert.Equal(11, engine.Frame.Read(engine.Slots["a"], "a").AsInteger());
            Assert.True(engine.Frame.Cells[engine.Slots["b"]].HasRaw);
        }
    }

    [Fact]
    public void CancellationPublishesTheSamePartialStateAsOrdinaryStores()
    {
        GraphConfig graph = Graph(Enumerable.Range(0, 300).Select(index =>
            ("a", (Expression)Set(BitSetOp.Union, S("a"), N(BigInteger.One << (index % 63))))).ToArray());
        BigInteger? expected = null;
        foreach (var promote in new[] { false, true })
        {
            (SamplingFrame Frame, SamplingOperands Operands,
                Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, Dictionary<string, int>
                Slots) engine = Bind(graph, new Dictionary<string, object?>
                {
                    ["a"] = 3,
                    ["b"] = 0
                }, promote);
            engine.Frame.CancellationToken = new CancellationToken(true);
            Assert.Throws<OperationCanceledException>(() =>
                engine.Code(engine.Frame, engine.Operands, new SeededRandom(3), null));
            BigInteger actual = StateHasher.CanonicalHash(engine.Frame.Export());
            if (expected is null)
            {
                expected = actual;
            }
            else
            {
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void OpaqueReadsDeclinePromotionAndStillSeeCompletedWrites()
    {
        GraphConfig graph = Graph(
            ("a", Set(BitSetOp.Union, S("a"), N(8))),
            ("a", Set(BitSetOp.Union, S("a"), N(16))),
            ("out", new FieldAccessExpr
            {
                Target = "state",
                Path = ["record", "x"]
            }));
        (SamplingFrame Frame, SamplingOperands Operands,
            Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, Dictionary<string, int> Slots)
            engine = Bind(graph, new Dictionary<string, object?>
            {
                ["a"] = 3,
                ["record"] = new Dictionary<string, object?>
                {
                    ["x"] = 19
                }
            });
        Assert.Equal(0, engine.Operands.PromotedMaskFields);
        engine.Code(engine.Frame, engine.Operands, new SeededRandom(4), null);
        Assert.Equal(27, engine.Frame.Read(engine.Slots["a"], "a").AsInteger());
        Assert.Equal(19, engine.Frame.Read(engine.Slots["out"], "out").AsInteger());
    }

    [Fact]
    public void MaskReadInsideCollectionUsesTheCurrentLocalAndRestoresAliases()
    {
        GraphConfig graph = Graph(
            ("a", Set(BitSetOp.Union, S("a"), N(8))),
            ("items", new MapExpr
            {
                StateKey = "items",
                ItemName = "item",
                ItemType = ExprType.Number,
                Body = Set(BitSetOp.Union, S("a"), S("item"))
            }),
            ("out", Set(BitSetOp.Count, S("a"))));
        (SamplingFrame Frame, SamplingOperands Operands,
            Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, Dictionary<string, int> Slots)
            engine = Bind(graph, new Dictionary<string, object?>
            {
                ["a"] = 3,
                ["items"] = new[] { 0, 4 },
                ["item"] = 99
            });
        Assert.Equal(1, engine.Operands.PromotedMaskFields);
        engine.Code(engine.Frame, engine.Operands, new SeededRandom(5), null);
        Assert.Equal(new BigInteger[] { 11, 15 }, engine.Frame.Read(engine.Slots["items"], "items")
            .ArrayValue!.Select(value => value.AsInteger()));
        Assert.Equal(99, engine.Frame.Read(engine.Slots["item"], "item").AsInteger());
    }

    [Fact]
    public void SelfTouchPublishesTypedValuesWithoutChangingTheMask()
    {
        GraphConfig graph = Graph(("a", S("a")), ("out", Set(BitSetOp.Count, S("a"))));
        (SamplingFrame Frame, SamplingOperands Operands,
            Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, Dictionary<string, int> Slots)
            engine = Bind(graph, new Dictionary<string, object?>
            {
                ["a"] = 3
            });
        Assert.Equal(1, engine.Operands.PromotedMaskFields);
        engine.Code(engine.Frame, engine.Operands, new SeededRandom(6), null);
        Assert.False(engine.Frame.Cells[engine.Slots["a"]].HasRaw);
        Assert.Equal(3, engine.Frame.Read(engine.Slots["a"], "a").AsInteger());
    }
}
