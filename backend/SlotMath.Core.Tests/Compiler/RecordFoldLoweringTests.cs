using System.Globalization;
using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Compiler;

[Collection("SamplingOptions")]
public sealed class RecordFoldLoweringTests
{
    private static readonly string[] StateNames =
        ["values", "inner", "nulls", "symbol", "nestedValue", "acc", "item", "innerAcc", "position"];

    private static readonly Dictionary<string, int> Slots = StateNames
        .Select((name, index) => (name, index))
        .ToDictionary(pair => pair.name, pair => pair.index, StringComparer.Ordinal);

    private static ConstantExpr N(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString(CultureInfo.InvariantCulture)
    };

    private static FieldAccessExpr S(params string[] path) => new()
    {
        Target = "state",
        Path = path
    };

    private static BinaryExpr Add(Expression left, Expression right) => new()
    {
        Op = BinaryOp.Add,
        Left = left,
        Right = right
    };

    private static CallExpr Index(Expression source, int index) => new()
    {
        Function = "index",
        Args = [source, N(index)]
    };

    private static RecordExpr Record(params (string Name, Expression Value)[] fields) => new()
    {
        Fields = fields.Select(field => new RecordFieldExpr
        {
            Name = field.Name,
            Value = field.Value
        }).ToArray()
    };

    private static TupleExpr Tuple(params Expression[] items) => new()
    {
        Items = items
    };

    private static FoldExpr Fold(Expression init, Expression body, string source = "values") => new()
    {
        StateKey = source,
        AccName = "acc",
        ItemName = "item",
        Init = init,
        Body = body,
        ItemType = ExprType.Number,
        MaxItems = 16
    };

    private static ExprValue Run(Expression expression, Dictionary<string, object?> state, SamplingTier tier)
    {
        var frame = new SamplingFrame(Slots, new SamplingCell[Slots.Count], state);
        Func<SamplingFrame, ExprValue> code = new SamplingExpressions(name => Slots[name], tier).Compile(expression);
        var calls = tier == SamplingTier.Tiered ? SamplingOptions.HotThreshold + 2 : 1;
        ExprValue result = default;
        for (var call = 0; call < calls; call++)
        {
            frame.Reset(default);
            result = code(frame);
        }

        return result;
    }

    private static ExprValue Exact(Expression expression, Dictionary<string, object?> state) =>
        ExactExpressionEvaluator.Evaluate(expression, new EvalContext
        {
            State = state
        });

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Compiled)]
    [InlineData(SamplingTier.Tiered)]
    public void RecordFold_StagesFieldsAndKeepsOldAccumulatorValues(SamplingTier tier)
    {
        Expression fold = Fold(
            Record(("x", N(0)), ("y", N(9))),
            Record(("x", Add(S("acc", "x"), S("item"))), ("y", S("acc", "x"))));
        var state = new Dictionary<string, object?>
        {
            ["values"] = new object[] { 1, 2 }
        };

        ExprValue actual = Run(fold, state, tier);
        Assert.Equal(Exact(fold, state), actual);
        Assert.Equal(ExprValue.Number(3), actual.RecordValue!["x"]);
        Assert.Equal(ExprValue.Number(1), actual.RecordValue["y"]);
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Compiled)]
    [InlineData(SamplingTier.Tiered)]
    public void RecordFold_NestedLetAndFoldCanReadOuterAccumulatorProjection(SamplingTier tier)
    {
        Expression inner = Fold(
                S("acc", "total"),
                Add(Add(S("innerAcc"), S("item")), N(0)),
                "inner") with
        {
            AccName = "innerAcc"
        };
        Expression fold = Fold(
            Record(("total", N(0)), ("count", N(0))),
            new LetExpr
            {
                Name = "innerResult",
                Value = inner,
                Body = Record(
                    ("total", new LocalExpr
                    {
                        Name = "innerResult"
                    }),
                    ("count", Add(S("acc", "count"), N(1))))
            });
        var state = new Dictionary<string, object?>
        {
            ["values"] = new object[] { 1, 2 },
            ["inner"] = new object[] { 3, 4 }
        };

        ExprValue actual = Run(fold, state, tier);
        Assert.Equal(Exact(fold, state), actual);
        Assert.Equal(ExprValue.Number(14), actual.RecordValue!["total"]);
        Assert.Equal(ExprValue.Number(2), actual.RecordValue["count"]);
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Compiled)]
    [InlineData(SamplingTier.Tiered)]
    public void RecordFold_ZeroIterationsReturnsInitializedRecord(SamplingTier tier)
    {
        Expression fold = Fold(Record(("x", N(3)), ("y", N(4))),
            Record(("x", Add(S("acc", "x"), S("item"))), ("y", S("acc", "y"))));
        var state = new Dictionary<string, object?>
        {
            ["values"] = Array.Empty<object>()
        };

        Assert.Equal(Exact(fold, state), Run(fold, state, tier));
    }

    [Fact]
    public void RecordFold_ZeroIterationsPreservesInitialSymbolValues()
    {
        Expression fold = Fold(Record(("symbol", S("symbol")), ("count", N(0))),
            Record(("symbol", S("acc", "symbol")), ("count", Add(S("acc", "count"), N(1)))));
        var state = new Dictionary<string, object?>
        {
            ["values"] = Array.Empty<object>(),
            ["symbol"] = ExprValue.Symbol("A")
        };

        ExprValue actual = Run(fold, state, SamplingTier.Compiled);
        Assert.Equal(Exact(fold, state), actual);
        Assert.Equal(ExprValue.Symbol("A"), actual.RecordValue!["symbol"]);
    }

    [Fact]
    public void RecordFold_FinalBodyRecordPreservesSymbolsWhileNextIterationReadsNormalizedAccumulator()
    {
        RecordExpr init = Record(("x", N(0)));
        Expression oneItemFold = Fold(init, Record(("x", S("item"))));
        var oneItemState = new Dictionary<string, object?>
        {
            ["values"] = new object[] { ExprValue.Symbol("S") }
        };
        ExprValue oneItemExpected = Exact(oneItemFold, oneItemState);
        Assert.Equal(ExprValue.Symbol("S"), oneItemExpected.RecordValue!["x"]);
        Assert.Equal(oneItemExpected, Run(oneItemFold, oneItemState, SamplingTier.Compiled));

        Expression indexedFold = Fold(init,
                Record(("x", new IfExpr
                {
                    Condition = new CompareExpr
                    {
                        Op = CompareOp.Eq,
                        Left = S("position"),
                        Right = N(0)
                    },
                    ThenExpr = S("item"),
                    ElseExpr = S("acc", "x")
                }))) with
        {
            IndexName = "position"
        };
        var indexedState = new Dictionary<string, object?>
        {
            ["values"] = new object[] { ExprValue.Symbol("S"), ExprValue.Symbol("T") }
        };
        ExprValue indexedExpected = Exact(indexedFold, indexedState);
        Assert.Equal(ExprValue.String("S"), indexedExpected.RecordValue!["x"]);
        Assert.Equal(indexedExpected, Run(indexedFold, indexedState, SamplingTier.Compiled));
    }

    [Fact]
    public void GuardedZeroIterationReturnsRawSymbolInitializer()
    {
        Expression fold = Fold(Record(("x", S("symbol"))), Record(("x", S("item")))) with
        {
            WhileCondition = new ConstantExpr
            {
                Kind = ConstantKind.Boolean,
                Value = "false"
            }
        };
        var state = new Dictionary<string, object?>
        {
            ["values"] = new object[] { 1 },
            ["symbol"] = ExprValue.Symbol("S")
        };

        ExprValue expected = Exact(fold, state);
        Assert.Equal(ExprValue.Symbol("S"), expected.RecordValue!["x"]);
        Assert.Equal(expected, Run(fold, state, SamplingTier.Compiled));
    }

    [Theory]
    [InlineData(false, "acc.x")]
    [InlineData(true, "project")]
    public void RecordFold_NullFieldProjectionPreservesLocatedError(bool useProject, string expectedLocation)
    {
        Expression nullValue = Index(S("nulls"), 0);
        Expression init = Record(("x", nullValue), ("y", N(0)));
        Expression projection = useProject
            ? new ProjectExpr
            {
                Value = S("acc"),
                Path = ["x"]
            }
            : S("acc", "x");
        Expression fold = Fold(init, Record(("x", projection), ("y", S("acc", "y"))));
        var state = new Dictionary<string, object?>
        {
            ["values"] = new object[] { 1 },
            ["nulls"] = new object?[] { null }
        };

        foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
        {
            var expected =
                Assert.Throws<ExpressionEvaluationException>(() => Exact(fold, state));
            var actual =
                Assert.Throws<ExpressionEvaluationException>(() => Run(fold, state, tier));
            Assert.Equal("EVAL_NULL_VALUE", expected.Code);
            Assert.Equal(expected.Code, actual.Code);
            Assert.Equal(expectedLocation, actual.Location);
            Assert.Equal(expected.Location, actual.Location);
        }
    }

    [Theory]
    [InlineData(false, "acc.x.missing")]
    [InlineData(true, "project")]
    public void RecordFold_NestedMissingProjectionPreservesErrorLocation(bool useProject, string expectedLocation)
    {
        Expression init = Record(("x", Record(("present", N(1)))), ("y", N(0)));
        Expression projection = useProject
            ? new ProjectExpr
            {
                Value = S("acc"),
                Path = ["x", "missing"]
            }
            : new FieldAccessExpr
            {
                Target = "state",
                Path = ["acc", "x", "missing"]
            };
        Expression fold = Fold(init, Record(("x", projection), ("y", S("acc", "y"))));
        var state = new Dictionary<string, object?>
        {
            ["values"] = new object[] { 1 }
        };

        foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
        {
            var expected =
                Assert.Throws<ExpressionEvaluationException>(() => Exact(fold, state));
            var actual =
                Assert.Throws<ExpressionEvaluationException>(() => Run(fold, state, tier));
            Assert.Equal("EVAL_MISSING_STATE", expected.Code);
            Assert.Equal(expectedLocation, actual.Location);
            Assert.Equal(expected.Code, actual.Code);
            Assert.Equal(expected.Location, actual.Location);
        }
    }

    [Fact]
    public void RecordFold_InnerIteratorBindingShadowsOuterScalarAccumulator()
    {
        var map = new MapExpr
        {
            StateKey = "inner",
            ItemName = "acc",
            Body = S("acc", "x"),
            ItemType = ExprType.Record,
            MaxItems = 4
        };
        Expression body = Record(("x", Index(map, 0)));
        Expression fold = Fold(Record(("x", N(0))), body);
        var state = new Dictionary<string, object?>
        {
            ["values"] = new object[] { 1 },
            ["inner"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["x"] = 5
                }
            }
        };

        ExprValue expected = Exact(fold, state);
        Assert.Equal(ExprValue.Number(5), expected.RecordValue!["x"]);
        Assert.Equal(expected, Run(fold, state, SamplingTier.Compiled));
    }

    [Fact]
    public void RecordFold_LocalExpressionDoesNotAliasStateAccumulator()
    {
        Expression fold = Fold(Record(("x", N(0))),
            Record(("x", new LocalExpr
            {
                Name = "acc",
                Path = ["x"]
            })));
        var state = new Dictionary<string, object?>
        {
            ["values"] = new object[] { 1 }
        };

        var expected = Assert.Throws<ExpressionEvaluationException>(() => Exact(fold, state));
        var actual = Assert.Throws<ExpressionEvaluationException>(() =>
            Run(fold, state, SamplingTier.Compiled));
        Assert.Equal("EVAL_MISSING_LOCAL", expected.Code);
        Assert.Equal(expected.Code, actual.Code);
        Assert.Equal(expected.Location, actual.Location);
    }

    [Fact]
    public void RecordFold_SharedShapeCacheDistinguishesAccumulatorFieldOrdinals()
    {
        Expression MakeFold(string firstField, string secondField)
        {
            return Fold(
                Record(("a", N(10)), ("b", N(20))),
                Record(("a", S("acc", firstField)), ("b", S("acc", secondField))));
        }

        Expression first = MakeFold("a", "b");
        Expression second = MakeFold("b", "a");
        var state = new Dictionary<string, object?>
        {
            ["values"] = new object[] { 1 }
        };
        var shapes = new SamplingShapes();

        ExprValue firstActual = new SamplingExpressions(name => Slots[name], SamplingTier.Compiled, shapes: shapes)
            .Compile(first)(CreateFrame(state));
        ExprValue secondActual = new SamplingExpressions(name => Slots[name], SamplingTier.Compiled, shapes: shapes)
            .Compile(second)(CreateFrame(state));

        Assert.Equal(Exact(first, state), firstActual);
        Assert.Equal(Exact(second, state), secondActual);
        Assert.Equal(ExprValue.Number(20), secondActual.RecordValue!["a"]);
        Assert.Equal(ExprValue.Number(10), secondActual.RecordValue["b"]);
        Assert.Equal(2, shapes.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RecordFold_RecursivelyNormalizesSymbolsButPreservesNulls(int iterations)
    {
        ExprValue nested = ExprValue.Array([
            ExprValue.Symbol("outer"),
            ExprValue.Record(new Dictionary<string, ExprValue>
            {
                ["symbol"] = ExprValue.Symbol("inner"),
                ["null"] = ExprValue.Null,
                ["items"] = ExprValue.Array([ExprValue.Symbol("nested"), ExprValue.Null])
            }),
            ExprValue.Null
        ]);
        Expression fold = Fold(Record(("payload", S("nestedValue")), ("count", N(0))),
            Record(("payload", S("acc", "payload")), ("count", Add(S("acc", "count"), N(1)))));
        var state = new Dictionary<string, object?>
        {
            ["values"] = Enumerable.Range(0, iterations).Cast<object>().ToArray(),
            ["nestedValue"] = nested
        };

        ExprValue expected = Exact(fold, state);
        foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
        {
            ExprValue actual = Run(fold, state, tier);
            Assert.Equal(expected, actual);
            ExprValue payload = actual.RecordValue!["payload"];
            Assert.Equal(iterations == 0 ? ExprType.Symbol : ExprType.String, payload.ArrayValue![0].Kind);
            ExprValue nestedRecord = payload.ArrayValue[1];
            Assert.Equal(iterations == 0 ? ExprType.Symbol : ExprType.String,
                nestedRecord.RecordValue!["symbol"].Kind);
            Assert.Equal(ExprType.Null, nestedRecord.RecordValue["null"].Kind);
            Assert.Equal(ExprType.Null, payload.ArrayValue[2].Kind);
            Assert.Equal(ExprType.Null, nestedRecord.RecordValue["items"].ArrayValue![1].Kind);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordFold_TupleInitAndMatchingRecordOrTupleBodyUseSameLowering(bool tupleBody)
    {
        Expression init = Tuple(N(0), N(9));
        Expression body = tupleBody
            ? Tuple(Add(S("acc", "0"), S("item")), S("acc", "0"))
            : Record(("0", Add(S("acc", "0"), S("item"))), ("1", S("acc", "0")));
        Expression fold = Fold(init, body);
        var state = new Dictionary<string, object?>
        {
            ["values"] = new object[] { 1, 2 }
        };

        ExprValue expected = Exact(fold, state);
        Assert.Equal(expected, Run(fold, state, SamplingTier.Compiled));
        Assert.Equal(ExprValue.Number(3), expected.RecordValue!["0"]);
        Assert.Equal(ExprValue.Number(1), expected.RecordValue["1"]);
    }

    [Fact]
    public void RecordFold_InterpretedFallbackSeesPublishedAliasesAndEarlierLetThenRestoresThemOnThrow()
    {
        var fallback = new BitSetExpr
        {
            Op = BitSetOp.Count,
            Args =
            [
                S("acc", "symbol"), S("item"), new LocalExpr
                {
                    Name = "prior"
                },
                S("position")
            ]
        };
        Expression fold = Fold(
                Record(("symbol", S("symbol")), ("partial", N(0))),
                new LetExpr
                {
                    Name = "prior",
                    Value = Add(S("item"), S("position")),
                    Body = Record(("symbol", new LocalExpr
                    {
                        Name = "prior"
                    }), ("partial", fallback))
                }) with
        {
            IndexName = "position"
        };
        var state = new Dictionary<string, object?>
        {
            ["values"] = new object[] { 3 },
            ["symbol"] = ExprValue.Symbol("5"),
            ["acc"] = new Dictionary<string, object?>
            {
                ["symbol"] = "old",
                ["partial"] = 7
            },
            ["item"] = 42,
            ["position"] = 11
        };
        SamplingFrame frame = CreateFrame(state);
        BigInteger before = StateHasher.CanonicalHash(frame.Export());

        var expected = Assert.Throws<ExpressionEvaluationException>(() => Exact(fold, state));
        var actual = Assert.Throws<ExpressionEvaluationException>(() =>
            new SamplingExpressions(name => Slots[name], SamplingTier.Compiled).Compile(fold)(frame));

        Assert.Equal("EVAL_ARITY", expected.Code);
        Assert.Equal(expected.Code, actual.Code);
        Assert.Equal(expected.Location, actual.Location);
        Assert.Equal(before, StateHasher.CanonicalHash(frame.Export()));
    }

    [Fact]
    public void GuardFalseStillChecksBoundBeforeSkippingOversizedSource()
    {
        Expression fold = Fold(Record(("x", N(0))), Record(("x", S("item")))) with
        {
            MaxItems = 1,
            WhileCondition = new ConstantExpr
            {
                Kind = ConstantKind.Boolean,
                Value = "false"
            }
        };
        var state = new Dictionary<string, object?>
        {
            ["values"] = new object[] { 1, 2 }
        };

        var expected = Assert.Throws<ExpressionEvaluationException>(() => Exact(fold, state));
        var actual = Assert.Throws<ExpressionEvaluationException>(() =>
            Run(fold, state, SamplingTier.Compiled));
        Assert.Equal("EVAL_COLLECTION_BUDGET", expected.Code);
        Assert.Equal(expected.Code, actual.Code);
        Assert.Equal(expected.Location, actual.Location);
    }

    [Fact]
    public void BadGuardThrowsBeforeReadingUnsupportedItem()
    {
        Expression fold = Fold(Record(("x", Record(("nested", N(1))))),
                Record(("x", S("item")))) with
        {
            WhileCondition = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = new FieldAccessExpr
                {
                    Target = "state",
                    Path = ["acc", "x", "missing"]
                },
                Right = N(0)
            }
        };
        var state = new Dictionary<string, object?>
        {
            ["values"] = new[] { new object() }
        };

        var expected = Assert.Throws<ExpressionEvaluationException>(() => Exact(fold, state));
        var actual = Assert.Throws<ExpressionEvaluationException>(() =>
            Run(fold, state, SamplingTier.Compiled));
        Assert.Equal("EVAL_MISSING_STATE", expected.Code);
        Assert.Equal(expected.Code, actual.Code);
        Assert.Equal("acc.x.missing", actual.Location);
        Assert.Equal(expected.Location, actual.Location);
    }

    [Fact]
    public void RecordFold_AllocatesLessThanWholeAccumulatorFallbackAfterWarmup()
    {
        var state = new Dictionary<string, object?>
        {
            ["values"] = Enumerable.Range(0, 21).Cast<object>().ToArray()
        };
        Expression optimized = Fold(Record(("x", N(0)), ("y", N(0))),
                Record(("x", Add(S("acc", "x"), S("item"))), ("y", Add(S("acc", "y"), S("item")))))
            with
        {
            MaxItems = 21
        };
        Expression fallback = Fold(Record(("x", N(0)), ("y", N(0))),
                Record(("x", Add(S("acc", "x"), S("item"))), ("y", S("acc"))))
            with
        {
            MaxItems = 21
        };
        SamplingFrame optimizedFrame = CreateFrame(state);
        SamplingFrame fallbackFrame = CreateFrame(state);
        Func<SamplingFrame, ExprValue> optimizedCode =
            new SamplingExpressions(name => Slots[name], SamplingTier.Compiled).Compile(optimized);
        Func<SamplingFrame, ExprValue> fallbackCode =
            new SamplingExpressions(name => Slots[name], SamplingTier.Compiled).Compile(fallback);

        var optimizedBytes = MeasureAllocations(optimizedCode, optimizedFrame);
        var fallbackBytes = MeasureAllocations(fallbackCode, fallbackFrame);

        Assert.True(optimizedBytes < fallbackBytes,
            $"Expected scalarized fold allocation {optimizedBytes} to be below fallback allocation {fallbackBytes}.");
    }

    private static SamplingFrame CreateFrame(Dictionary<string, object?> state)
    {
        var frame = new SamplingFrame(Slots, new SamplingCell[Slots.Count], state);
        frame.Reset(default);
        return frame;
    }

    private static long MeasureAllocations(Func<SamplingFrame, ExprValue> code, SamplingFrame frame)
    {
        for (var call = 0; call < 16; call++)
        {
            frame.Reset(default);
            _ = code(frame);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var call = 0; call < 64; call++)
        {
            frame.Reset(default);
            _ = code(frame);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void RecordFold_InitializerFieldErrorsPrecedeSourceErrors()
    {
        Expression fold = Fold(Record(("x", N(1)), ("y", new BinaryExpr
        {
            Op = BinaryOp.Div,
            Left = N(1),
            Right = N(0)
        })),
            Record(("x", Add(S("acc", "x"), S("item"))), ("y", S("acc", "y"))));
        var state = new Dictionary<string, object?>();

        var expected = Assert.Throws<ExpressionEvaluationException>(() => Exact(fold, state));
        var actual = Assert.Throws<ExpressionEvaluationException>(() =>
            Run(fold, state, SamplingTier.Compiled));
        Assert.Equal(expected.Code, actual.Code);
        Assert.Equal(expected.Location, actual.Location);
    }

    [Fact]
    public void RecordFold_GuardAndWholeAccumulatorReadKeepCanonicalResults()
    {
        Expression guarded = Fold(Record(("x", N(0)), ("y", N(0))),
                Record(("x", Add(S("acc", "x"), N(1))), ("y", Add(S("acc", "y"), S("item"))))) with
        {
            WhileCondition = new CompareExpr
            {
                Op = CompareOp.Lt,
                Left = S("acc", "x"),
                Right = N(2)
            }
        };
        Expression snapshot = Fold(Record(("x", N(0)), ("y", N(0))),
            new LetExpr
            {
                Name = "saved",
                Value = S("acc"),
                Body = Record(
                    ("x", new ProjectExpr
                    {
                        Value = new LocalExpr
                        {
                            Name = "saved"
                        },
                        Path = ["x"]
                    }),
                    ("y", Add(S("acc", "y"), N(1))))
            });
        var state = new Dictionary<string, object?>
        {
            ["values"] = new object[] { 5, 6, 7 }
        };

        foreach (Expression expression in new[] { guarded, snapshot })
        {
            ExprValue expected = Exact(expression, state);
            Assert.Equal(expected, Run(expression, state, SamplingTier.Interpreted));
            Assert.Equal(expected, Run(expression, state, SamplingTier.Compiled));
            Assert.Equal(expected, Run(expression, state, SamplingTier.Tiered));
        }
    }
}
