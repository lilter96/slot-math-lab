using System.Collections;
using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
using SlotMath.Core.Random;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Optimization;

[Collection("SamplingOptions")]
public sealed class CollectionBufferTests
{
    [Fact]
    public void LargeArenaRentalsRespectTheSharedElementBudgetAndRemainDistinct()
    {
        var arena = new SamplingArrayArena();
        ExprValue[][] first = Enumerable.Range(0, 6).Select(_ => arena.Rent(4096)).ToArray();
        Assert.Equal(6, first.Distinct(ReferenceEqualityComparer.Instance).Count());
        for (var i = 0; i < first.Length; i++)
        {
            Array.Fill(first[i], ExprValue.Number(i));
        }

        arena.Reset();
        ExprValue[][] second = Enumerable.Range(0, 6).Select(_ => arena.Rent(4096)).ToArray();
        for (var i = 0; i < 4; i++)
        {
            Assert.Same(first[i], second[i]);
        }

        for (var i = 4; i < 6; i++)
        {
            Assert.DoesNotContain(first, array => ReferenceEquals(array, second[i]));
        }

        // The 16,384-element capacity is shared by small and large lengths.
        ExprValue overflow = ExprValue.Number(999);
        ExprValue[] small = arena.Rent(1);
        small[0] = overflow;
        arena.Reset();
        Assert.NotSame(small, arena.Rent(1));
        Assert.All(first.Take(4).Select((array, i) => (array, i)),
            pair => Assert.All(pair.array, value => Assert.Equal(pair.i, value.AsInteger())));
        Assert.Throws<OverflowException>(() => arena.Rent(-1));
        ExprValue[] oversized = arena.Rent(4097);
        arena.Reset();
        Assert.NotSame(oversized, arena.Rent(4097));
    }

    [Fact]
    public void LargeArenaBucketsRetainExactLengthsWhenRequestOrderChanges()
    {
        var arena = new SamplingArrayArena();
        int[] lengths = [64, 65, 256, 65, 1024, 256, 4096];
        var previous = new Dictionary<int, HashSet<ExprValue[]>>();
        for (var round = 0; round < 6; round++)
        {
            arena.Reset();
            var current = new List<ExprValue[]>();
            foreach (var length in round % 2 == 0 ? lengths : lengths.Reverse())
            {
                ExprValue[] values = arena.Rent(length);
                Assert.Equal(length, values.Length);
                Assert.DoesNotContain(current, value => ReferenceEquals(value, values));
                if (round > 0)
                {
                    Assert.Contains(previous[length], value => ReferenceEquals(value, values));
                }

                Array.Fill(values, ExprValue.Number(current.Count + 100 * round));
                current.Add(values);
            }

            previous = current.GroupBy(values => values.Length)
                .ToDictionary(group => group.Key, group => group.ToHashSet());
        }
    }

    private static ConstantExpr N(int n) => new()
    {
        Kind = ConstantKind.Integer,
        Value = n.ToString()
    };

    private static FieldAccessExpr S(string key) => new()
    {
        Target = "state",
        Path = [key]
    };

    private static BinaryExpr B(BinaryOp op, Expression a, Expression b) => new()
    {
        Op = op,
        Left = a,
        Right = b
    };

    private static MapExpr Producer(Expression? body = null, bool expressionSource = false) => new()
    {
        StateKey = expressionSource ? null : "source",
        Source = expressionSource ? S("source") : null,
        MaxItems = 512,
        ItemName = "p",
        IndexName = "pi",
        ItemType = ExprType.Number,
        Body = body ?? B(BinaryOp.Add, S("p"), N(1))
    };

    private static MapExpr Consumer(Expression source, Expression? body = null, int bound = 512) => new()
    {
        Source = source,
        MaxItems = bound,
        ItemName = "q",
        IndexName = "qi",
        ItemType = ExprType.Number,
        Body = body ?? B(BinaryOp.Mul, S("q"), N(2))
    };

    private static BigInteger[] Numbers(ExprValue value) =>
        value.ArrayValue!.Select(item => item.AsInteger()).ToArray();

    private static (SamplingFrame Frame, Dictionary<string, int> Slots,
        Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) Bind(Expression expression,
            Dict initial, bool optimize = true, bool arena = false)
    {
        var slots = new Dictionary<string, int>();

        int Slot(string key)
        {
            if (!slots.TryGetValue(key, out var index))
            {
                slots[key] = index = slots.Count;
            }

            return index;
        }

        foreach (var key in initial.Keys)
        {
            Slot(key);
        }

        (Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) native =
            SamplingCodegen.CompileValue(new SamplingExpressions(Slot, optimizeCollections: optimize), Slot,
                expression);
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], initial, arena);
        frame.Reset(default);
        return (frame, slots, native.Code, native.Operands);
    }

    [Fact]
    public void SingleUseFreshContainersCanTransferButStateInputsCannot()
    {
        MapExpr producer = Producer();
        MapExpr consumer = Consumer(producer);
        ExpressionIr ir = ExpressionIrBuilder.Build(consumer, TypeCheckContext.Default);
        Assert.Empty(IrVerifier.Verify(ir));
        CollectionBufferAnalysis plan = ir.Buffers;
        Assert.False(plan.BudgetReached);
        Assert.False(plan.CanReuseSource(producer));
        Assert.True(plan.CanReuseSource(consumer));
        var producerValue = Assert.Single(ir.Collections[producer]);
        Assert.True(plan.Values[producerValue].Unique);
        Assert.False(plan.Values[producerValue].Borrowed);
        Assert.Contains(producerValue, plan.Values[producerValue].Origins);
        Assert.Same(plan, ir.Buffers);
        var limited = CollectionBufferAnalysis.Build(ir, 1);
        Assert.True(limited.BudgetReached);
        Assert.False(limited.CanReuseSource(consumer));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LexicalAliasesAreCountedAcrossScopesAndEscapes(bool escape)
    {
        var local = new LocalExpr
        {
            Name = "saved"
        };
        MapExpr consumer = Consumer(local);
        var expression = new LetExpr
        {
            Name = "saved",
            Value = Producer(),
            Body = escape
                ? new TupleExpr
                {
                    Items = [local, consumer]
                }
                : consumer
        };
        ExpressionIr ir = ExpressionIrBuilder.Build(expression, TypeCheckContext.Default);
        Assert.Equal(!escape, ir.Buffers.CanReuseSource(consumer));
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
            SamplingOperands Operands) native = Bind(expression, new Dict
            {
                ["source"] = new[] { 1, 2, 3 }
            });
        Assert.Equal(escape ? 0 : 1, native.Operands.ReusedCollectionBuffers);
        ExprValue result = native.Code(native.Frame, native.Operands);
        if (escape)
        {
            Assert.Equal(new BigInteger[] { 2, 3, 4 }, Numbers(result.RecordValue!["0"]));
            Assert.Equal(new BigInteger[] { 4, 6, 8 }, Numbers(result.RecordValue["1"]));
        }
        else
        {
            Assert.Equal(new BigInteger[] { 4, 6, 8 }, Numbers(result));
        }

        Assert.Null(native.Frame.Locals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalOwnershipRequiresEveryPossibleSourceToBePrivate(bool borrowedArm)
    {
        var source = new IfExpr
        {
            Condition = S("choose"),
            ThenExpr = Producer(),
            ElseExpr = borrowedArm ? S("source") : Producer(B(BinaryOp.Add, S("p"), N(5)))
        };
        MapExpr consumer = Consumer(source);
        Assert.Equal(!borrowedArm,
            ExpressionIrBuilder.Build(consumer, TypeCheckContext.Default).Buffers.CanReuseSource(consumer));
        foreach (var choose in new[] { false, true })
        {
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
                SamplingOperands Operands) native = Bind(consumer, new Dict
                {
                    ["source"] = new[] { 1, 2 },
                    ["choose"] = choose
                });
            Assert.Equal(borrowedArm ? 0 : 1, native.Operands.ReusedCollectionBuffers);
            Assert.Equal(new BigInteger[] { choose ? 4 : borrowedArm ? 2 : 12, choose ? 6 : borrowedArm ? 4 : 14 },
                Numbers(native.Code(native.Frame, native.Operands)));
        }
    }

    [Fact]
    public void BorrowedTypedStorageIsNeverOverwritten()
    {
        ExprValue[] storage = [ExprValue.Number(1), ExprValue.Number(2), ExprValue.Number(3)];
        MapExpr expression = Consumer(S("source"));
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
            SamplingOperands Operands) native = Bind(expression, new Dict
            {
                ["source"] = ExprValue.ArrayOwned(storage, false)
            });
        Assert.Equal(0, native.Operands.ReusedCollectionBuffers);
        Assert.Equal(new BigInteger[] { 2, 4, 6 }, Numbers(native.Code(native.Frame, native.Operands)));
        Assert.Equal(new BigInteger[] { 1, 2, 3 }, storage.Select(value => value.AsInteger()));
    }

    [Fact]
    public void ObservationEffectsInvalidateOwnershipAdmission()
    {
        MapExpr consumer = Consumer(Producer(), new FieldAccessExpr
        {
            Target = "measurement",
            Path = ["gross"]
        });
        Assert.False(ExpressionIrBuilder.Build(consumer, TypeCheckContext.Default).Buffers.CanReuseSource(consumer));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(128)]
    public void PrivateMapAndStableFilterCompactionMatchAnIndependentOracle(int length)
    {
        MapExpr producer = Producer();
        var expression = new FilterExpr
        {
            Source = producer,
            MaxItems = 512,
            ItemName = "q",
            IndexName = "qi",
            ItemType = ExprType.Number,
            Predicate = new CompareExpr
            {
                Op = CompareOp.Gt,
                Left = S("q"),
                Right = N(4)
            }
        };
        var source = Enumerable.Range(0, length).ToArray();
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
            SamplingOperands Operands) native = Bind(expression, new Dict
            {
                ["source"] = source
            });
        Assert.Equal(1, native.Operands.ReusedCollectionBuffers);
        Assert.Equal(source.Select(n => new BigInteger(n + 1)).Where(n => n > 4),
            Numbers(native.Code(native.Frame, native.Operands)));
        Assert.Equal(Enumerable.Range(0, length), source);
    }

    [Fact]
    public void ArrayAllocationDropsWithoutUsingAnArena()
    {
        MapExpr expression = Consumer(Producer());
        var input = new Dict
        {
            ["source"] = Enumerable.Range(0, 128).ToArray()
        };
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
            SamplingOperands Operands) optimized = Bind(expression, input);
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
            SamplingOperands Operands) baseline = Bind(expression, input, false);
        for (var warmup = 0; warmup < 64; warmup++)
        {
            optimized.Code(optimized.Frame, optimized.Operands);
            baseline.Code(baseline.Frame, baseline.Operands);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var run = 0; run < 64; run++)
        {
            baseline.Code(baseline.Frame, baseline.Operands);
        }

        var oldBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var run = 0; run < 64; run++)
        {
            optimized.Code(optimized.Frame, optimized.Operands);
        }

        var newBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(newBytes < oldBytes * 3 / 4,
            $"Owned source should remove one of two buffers: {oldBytes} -> {newBytes}.");
        Assert.Equal(new BigInteger[] { 2, 4, 6 },
            Numbers(optimized.Code(optimized.Frame, optimized.Operands)).Take(3));
    }

    [Fact]
    public void ProducerErrorsFinishBeforeConsumerErrors()
    {
        var index = new CallExpr
        {
            Function = "index",
            Args = [S("values"), S("p")]
        };
        MapExpr expression = Consumer(Producer(index), B(BinaryOp.Div, N(1), N(0)));
        foreach (var optimize in new[] { false, true })
        {
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
                SamplingOperands Operands) native = Bind(expression, new Dict
                {
                    ["source"] = new[] { 0, 1, 9 },
                    ["values"] = new[] { 10, 20 },
                    ["p"] = 77,
                    ["q"] = 88
                }, optimize);
            var error = Assert.Throws<ExpressionEvaluationException>(() => native.Code(native.Frame, native.Operands));
            Assert.Equal(EvalErrorCodes.IndexOutOfRange, error.Code);
            Assert.Equal("index[9]", error.Location);
            Assert.Equal(77, native.Frame.Read(native.Slots["p"], "p").AsInteger());
            Assert.Equal(88, native.Frame.Read(native.Slots["q"], "q").AsInteger());
        }
    }

    [Fact]
    public void ConstantProducerFusionRetainsIndexAndLogicalCancellationClock()
    {
        MapExpr expression = Consumer(Producer(N(7), true), B(BinaryOp.Add, S("q"), S("qi")));
        foreach (var optimize in new[] { false, true })
        {
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
                SamplingOperands Operands) native = Bind(expression, new Dict
                {
                    ["source"] = Enumerable.Range(0, 10).ToArray()
                }, optimize);
            Assert.Equal(optimize ? 1 : 0, native.Operands.FusedCollectionPasses);
            Assert.Equal(Enumerable.Range(7, 10).Select(n => new BigInteger(n)),
                Numbers(native.Code(native.Frame, native.Operands)));
            native.Frame.CancellationToken = new CancellationToken(true);
            for (var check = 0; check < 235; check++)
            {
                native.Frame.CheckCancellation();
            }

            Assert.Throws<OperationCanceledException>(() => native.Frame.CheckCancellation());
        }
    }

    [Fact]
    public void CancelableProducerStillCancelsBeforeConsumerFailure()
    {
        MapExpr expression = Consumer(Producer(N(7), true), B(BinaryOp.Div, N(1), N(0)));
        foreach (var optimize in new[] { false, true })
        {
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
                SamplingOperands Operands) native = Bind(expression, new Dict
                {
                    ["source"] = Enumerable.Range(0, 300).ToArray()
                }, optimize);
            native.Frame.CancellationToken = new CancellationToken(true);
            Assert.Throws<OperationCanceledException>(() => native.Code(native.Frame, native.Operands));
        }
    }

    [Fact]
    public void RawIndexCallbacksAreNeitherSkippedNorRepeatedByFusion()
    {
        MapExpr expression = Consumer(Producer(N(7)), B(BinaryOp.Div, N(1), N(0)));
        foreach (var optimize in new[] { false, true })
        {
            var source = new ThrowingList();
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
                SamplingOperands Operands) native = Bind(expression, new Dict
                {
                    ["source"] = source
                }, optimize);
            source.Reads = 0; // Frame import has its own, preexisting conversion attempt.
            var error = Assert.Throws<ExpressionEvaluationException>(() => native.Code(native.Frame, native.Operands));
            Assert.Equal("RAW_INDEX", error.Code);
            Assert.Equal("source[3]", error.Location);
            Assert.Equal(4, source.Reads);
        }
    }

    [Fact]
    public void ConsumerBoundsAreCheckedAfterTheProducer()
    {
        MapExpr expression = Consumer(Producer(N(7), true), bound: 2);
        foreach (var optimize in new[] { false, true })
        {
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
                SamplingOperands Operands) native = Bind(expression, new Dict
                {
                    ["source"] = new[] { 0, 1, 2 }
                }, optimize);
            var error = Assert.Throws<ExpressionEvaluationException>(() => native.Code(native.Frame, native.Operands));
            Assert.Equal("EVAL_COLLECTION_BUDGET", error.Code);
            Assert.Equal("map.source", error.Location);
        }
    }

    [Fact]
    public void ASharedAstInDifferentLexicalContextsDoesNotBorrowOneConstantProof()
    {
        MapExpr producer = Producer(new LocalExpr
        {
            Name = "z"
        });
        MapExpr consumer = Consumer(producer);
        var expression = new TupleExpr
        {
            Items =
            [
                new LetExpr
                {
                    Name = "z",
                    Value = N(2),
                    Body = consumer
                },
                new LetExpr
                {
                    Name = "z",
                    Value = N(3),
                    Body = consumer
                }
            ]
        };
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
            SamplingOperands Operands) native = Bind(expression, new Dict
            {
                ["source"] = new[] { 1, 2 }
            });
        Assert.Equal(0, native.Operands.FusedCollectionPasses);
        ExprValue result = native.Code(native.Frame, native.Operands);
        Assert.Equal(new BigInteger[] { 4, 4 }, Numbers(result.RecordValue!["0"]));
        Assert.Equal(new BigInteger[] { 6, 6 }, Numbers(result.RecordValue["1"]));
    }

    [Fact]
    public void NestedBorrowedElementsRemainUnchangedWhenTheirPrivateContainerIsReused()
    {
        ExprValue[] storage = [ExprValue.Number(1), ExprValue.Number(2)];
        MapExpr producer = Producer(S("shared")) with
        {
            ItemType = ExprType.Number
        };
        var nested = new MapExpr
        {
            Source = S("q"),
            MaxItems = 512,
            ItemName = "r",
            ItemType = ExprType.Number,
            Body = B(BinaryOp.Add, S("r"), N(10))
        };
        MapExpr expression = Consumer(producer, nested) with
        {
            ItemType = ExprType.Array
        };
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
            SamplingOperands Operands) native = Bind(expression, new Dict
            {
                ["source"] = new[] { 0, 1 },
                ["shared"] = ExprValue.ArrayOwned(storage, false)
            });
        Assert.Equal(1, native.Operands.ReusedCollectionBuffers);
        ExprValue result = native.Code(native.Frame, native.Operands);
        Assert.All(result.ArrayValue!, value => Assert.Equal(new BigInteger[] { 11, 12 }, Numbers(value)));
        Assert.Equal(new BigInteger[] { 1, 2 }, storage.Select(value => value.AsInteger()));
    }

    [Fact]
    public void SymbolNormalizationIsPreservedBeforeReusingTheSource()
    {
        MapExpr producer = Producer(S("p")) with
        {
            ItemType = ExprType.String
        };
        MapExpr expression = Consumer(producer, S("q")) with
        {
            ItemType = ExprType.String
        };
        foreach (var optimize in new[] { false, true })
        {
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
                SamplingOperands Operands) native = Bind(expression, new Dict
                {
                    ["source"] = new[] { "2", "X" }
                }, optimize);
            ExprValue result = native.Code(native.Frame, native.Operands);
            Assert.All(result.ArrayValue!, value => Assert.Equal(ExprType.String, value.Kind));
            Assert.Equal(new[] { "2", "X" }, result.ArrayValue!.Select(value => value.StringValue));
        }
    }

    [Fact]
    public void MapFilterMapTransfersPrivateBuffersAcrossCompaction()
    {
        var filter = new FilterExpr
        {
            Source = Producer(),
            MaxItems = 512,
            ItemName = "f",
            ItemType = ExprType.Number,
            Predicate = new CompareExpr
            {
                Op = CompareOp.Gt,
                Left = S("f"),
                Right = N(4)
            }
        };
        MapExpr expression = Consumer(filter);
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
            SamplingOperands Operands) native = Bind(expression, new Dict
            {
                ["source"] = Enumerable.Range(0, 10).ToArray()
            });
        Assert.Equal(2, native.Operands.ReusedCollectionBuffers);
        Assert.Equal(new BigInteger[] { 10, 12, 14, 16, 18, 20 }, Numbers(native.Code(native.Frame, native.Operands)));
    }

    [Fact]
    public void VirtualizationPreservesTheOriginalNegativeLengthFailure()
    {
        MapExpr expression = Consumer(Producer(N(7)) with
        {
            MaxItems = null
        });
        foreach (var optimize in new[] { false, true })
        {
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
                SamplingOperands Operands) native = Bind(expression, new Dict
                {
                    ["source"] = Array.Empty<int>()
                }, optimize);
            native.Frame.Cells[native.Slots["source"]] = new SamplingCell
            {
                Present = true,
                HasRaw = true,
                Raw = new NegativeCountList()
            };
            Assert.Throws<OverflowException>(() => native.Code(native.Frame, native.Operands));
        }
    }

    private static Dictionary<string, Port> Ports() => new()
    {
        ["state"] = new Port
        {
            Name = "state",
            Type = PortType.State
        }
    };

    private static Edge Edge(string a, string b) => new()
    {
        Id = a + b,
        SourceNodeId = a,
        TargetNodeId = b,
        SourcePort = "state",
        TargetPort = "state"
    };

    private static GraphConfig Graph(Dictionary<string, Expression> expressions) => new()
    {
        SchemaVersion = "1.0.0",
        InitialState = new Dictionary<string, JsonElement>
        {
            ["source"] = JsonSerializer.SerializeToElement(new[] { 0, 1, 2 })
        },
        Expressions = expressions,
        Nodes =
        [
            .. expressions.Keys.Select(key => (Node)new ModifyStateNode
            {
                Id = key,
                ExpressionId = key,
                OutputKey = key,
                Inputs = Ports(),
                Outputs = Ports()
            }),
            new MetricsSinkNode
            {
                Id = "sink",
                WinStateKey = "win",
                WinCap = 1000,
                Inputs = Ports()
            }
        ],
        Edges = expressions.Keys.Zip(expressions.Keys.Skip(1)).Select(pair => Edge(pair.First, pair.Second))
            .Append(Edge(expressions.Keys.Last(), "sink")).ToArray()
    };

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void PersistentSessionsAndEarlierExportsKeepTheirSnapshots(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            GraphConfig graph = Graph(new Dictionary<string, Expression>
            {
                ["source"] = Consumer(Producer()),
                ["win"] = N(0)
            });
            CompileResult compiled = new GraphCompiler().Compile(graph);
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            var program = Assert.IsAssignableFrom<ICompiledSampling<Dict, BigInteger>>(compiled.Program);
            ISamplingRunner<Dict, BigInteger>[] runners = new[]
            {
                program.CreateRunner(new Dict(), persistentKeys: ["source"]),
                program.CreateRunner(new Dict(), persistentKeys: ["source"])
            };
            var saved = new List<(Dict State, BigInteger Hash)>();
            for (var round = 1; round <= 100; round++)
            {
                foreach (ISamplingRunner<Dict, BigInteger> runner in runners)
                {
                    Assert.Equal(BigInteger.Zero, runner.Run(new SeededRandom(round), default));
                    Dict state = runner.ExportState();
                    BigInteger scale = BigInteger.One << round;
                    Assert.Equal(new[] { 2 * scale - 2, 3 * scale - 2, 4 * scale - 2 },
                        Assert.IsType<object?[]>(state["source"]).Cast<BigInteger>());
                    if (round % 17 == 0)
                    {
                        saved.Add((state, StateHasher.CanonicalHash(state)));
                    }
                }
            }

            foreach (ISamplingRunner<Dict, BigInteger> runner in runners)
            {
                runner.ResetTrajectory();
                runner.Run(new SeededRandom(0), default);
                Assert.Equal(new BigInteger[] { 2, 4, 6 },
                    Assert.IsType<object?[]>(runner.ExportState()["source"]).Cast<BigInteger>());
            }

            Assert.All(saved, snapshot => Assert.Equal(snapshot.Hash, StateHasher.CanonicalHash(snapshot.State)));
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void MeasurementsSeeTheBorrowedInputBeforeAndAfterPrivateBufferReuse()
    {
        GraphConfig graph = Graph(new Dictionary<string, Expression>
        {
            ["base"] = Producer(),
            ["result"] = Consumer(new MapExpr
            {
                StateKey = "base",
                MaxItems = 512,
                ItemName = "p",
                ItemType = ExprType.Number,
                Body = B(BinaryOp.Mul, S("p"), N(2))
            }, B(BinaryOp.Add, S("q"), N(10))),
            ["win"] = N(0)
        });
        Expression value = new CallExpr
        {
            Function = "index",
            Args = [S("base"), N(0)]
        };
        MeasurementDefinition[] definitions =
        [
            new()
            {
                Id = "before",
                Name = "Before",
                NodeId = "result",
                Value = value
            },
            new()
            {
                Id = "after",
                Name = "After",
                NodeId = "win",
                Value = value
            }
        ];
        CompileResult compiled = new GraphCompiler().Compile(graph, definitions);
        CompileResult canonical = new GraphCompiler(optimizeSampling: false).Compile(graph, definitions);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Assert.True(canonical.IsValid);
        var options = new SampledConfig
        {
            Seed = 42,
            MaxSpins = 128,
            Measurements = definitions
        };
        SampledResult<Dict> actual = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), options);
        SampledResult<Dict> expected = SampledInterpreter.Evaluate(canonical.Program!, new Dict(), options);
        Assert.Equal(JsonSerializer.Serialize(expected.Measurements), JsonSerializer.Serialize(actual.Measurements));
        Assert.Equal(new double?[] { 1, 1 }, actual.Measurements.Select(measurement => measurement.Mean));
    }

    private sealed class ThrowingList() : ArrayList(new object[] { 0, 1, 2, 3 })
    {
        internal int Reads;

        public override object? this[int index]
        {
            get
            {
                Reads++;
                if (index == 3)
                {
                    throw new ExpressionEvaluationException("RAW_INDEX", "Original raw getter failed.", "source[3]");
                }

                return base[index];
            }
            set => base[index] = value;
        }
    }

    private sealed class NegativeCountList : ArrayList
    {
        public override int Count => -1;
    }
}
