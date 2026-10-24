using System.Collections;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlotMath.Core.Compiler;
using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Optimization;

namespace SlotMath.Core.Tests.Expressions;

[Collection("SamplingOptions")]
public sealed class IndexedArrayTests
{
    private static ConstantExpr N(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString()
    };

    private static FieldAccessExpr S(string name) => new()
    {
        Target = "state",
        Path = [name]
    };

    private static ExprValue V(params int[] values) =>
        ExprValue.Array(values.Select(value => ExprValue.Number(value)).ToArray());

    private static ScatterExpr Scatter(ScatterMode mode = ScatterMode.LastWriteWins, Expression? target = null) => new()
    {
        Target = target ?? S("target"),
        Indices = S("indices"),
        Values = S("updates"),
        Mode = mode,
        MaxItems = 128,
        MaxUpdates = 128
    };

    private static PartitionIndicesExpr Partition(int buckets = 4) => new()
    {
        Labels = S("labels"),
        BucketCount = buckets,
        MaxItems = 128
    };

    private static BigInteger[] Integers(ExprValue value) =>
        value.ArrayValue!.Select(item => item.AsInteger()).ToArray();

    private static ExprValue Apply(ExprValue target, ExprValue indices, ExprValue values, ScatterMode mode,
        bool reuse = false, CancellationToken token = default)
    {
        (int Items, int Updates) shape = IndexedArrayData.CheckScatter(target, indices, values, mode, 4096, 4096);
        return IndexedArrayData.Scatter(target, indices, values, mode,
            reuse ? target.ItemsArray! : new ExprValue[shape.Items], new ExprValue[shape.Updates], token);
    }

    private static ExprValue Group(ExprValue labels, int buckets, CancellationToken token = default)
    {
        var length = IndexedArrayData.CheckPartition(labels, buckets, 4096);
        Span<int> keys = stackalloc int[length], counts = stackalloc int[buckets];
        IndexedArrayData.PartitionPlan(labels, keys, counts, token);
        var groups = new ExprValue[buckets];
        for (var bucket = 0; bucket < buckets; bucket++)
        {
            groups[bucket] = ExprValue.ArrayOwned(new ExprValue[counts[bucket]], false);
        }

        return IndexedArrayData.Partition(groups, keys, counts, token);
    }

    private static (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval) Bind(
        Expression expression, Dictionary<string, object?> initial, SamplingTier tier)
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

        Func<SamplingFrame, ExprValue> eval = new SamplingExpressions(Slot, tier).Compile(expression);
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], initial, true);
        frame.Reset(default);
        return (frame, slots, eval);
    }

    private static (SamplingFrame Frame, SamplingOperands Operands, Func<SamplingFrame, SamplingOperands?, ExprValue>
        Code) Native(Expression expression, Dictionary<string, object?> initial)
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
            SamplingCodegen.CompileValue(new SamplingExpressions(Slot), Slot, expression);
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], initial, true);
        frame.Reset(default);
        return (frame, native.Operands, native.Code);
    }

    [Theory]
    [InlineData(ScatterMode.LastWriteWins)]
    [InlineData(ScatterMode.Sum)]
    public void IndexedUpdatesMatchIndependentGroupedIntegerOracles(ScatterMode mode)
    {
        var random = new System.Random(2003);
        for (var length = 0; length <= 64; length++)
        {
            var count = length == 0 ? 0 : random.Next(129);
            var target = Enumerable.Range(0, length).Select(_ => random.Next(-100, 101)).ToArray();
            var indices = Enumerable.Range(0, count).Select(_ => random.Next(length)).ToArray();
            var values = Enumerable.Range(0, count).Select(_ => random.Next(-50, 51)).ToArray();
            BigInteger[] expected = target.Select((value, position) =>
            {
                var selected = Enumerable.Range(0, count).Where(index => indices[index] == position).ToArray();
                return mode == ScatterMode.Sum ? new BigInteger(value) + selected.Sum(index => values[index]) :
                    selected.Length == 0 ? value : values[selected[^1]];
            }).ToArray();
            ExprValue source = V(target);
            Assert.Equal(expected, Integers(Apply(source, V(indices), V(values), mode)));
            Assert.Equal(target.Select(value => new BigInteger(value)), Integers(source));
        }
    }

    [Fact]
    public void IndexedReductionRetainsRationalsAndWideNumbersExactly()
    {
        BigInteger wide = BigInteger.One << 100;
        ExprValue target = ExprValue.Array([ExprValue.Rational(wide, 6), ExprValue.Rational(-5, 6)]);
        ExprValue values =
            ExprValue.Array([ExprValue.Rational(2, 6), ExprValue.Rational(-9, 6), ExprValue.Rational(11, 6)]);
        ExprValue result = Apply(target, V(0, 1, 0), values, ScatterMode.Sum);
        Assert.Equal(new Rational(wide + 13, 6),
            new Rational(result.ArrayValue![0].NumberNumerator, result.ArrayValue[0].NumberDenominator));
        Assert.Equal(new Rational(-14, 6),
            new Rational(result.ArrayValue[1].NumberNumerator, result.ArrayValue[1].NumberDenominator));
    }

    [Fact]
    public void PartitionMatchesEverySmallDenseLabelAssignmentIncludingEmptyBuckets()
    {
        for (var buckets = 0; buckets <= 4; buckets++)
        {
            for (var length = 0; length <= 6; length++)
            {
                if (buckets == 0 && length != 0)
                {
                    continue;
                }

                var assignments = (int)BigInteger.Pow(buckets, length);
                if (length == 0)
                {
                    assignments = 1;
                }

                for (var code = 0; code < assignments; code++)
                {
                    var labels = new int[length];
                    var remaining = code;
                    for (var index = 0; index < length; index++)
                    {
                        labels[index] = remaining % buckets;
                        remaining /= buckets;
                    }

                    ExprValue groups = Group(V(labels), buckets);
                    Assert.Equal(buckets, groups.ArrayValue!.Count);
                    for (var bucket = 0; bucket < buckets; bucket++)
                    {
                        Assert.Equal(
                            Enumerable.Range(0, length).Where(index => labels[index] == bucket)
                                .Select(index => new BigInteger(index)), Integers(groups.ArrayValue[bucket]));
                    }

                    Assert.Equal(Enumerable.Range(0, length).Select(index => new BigInteger(index)),
                        groups.ArrayValue.SelectMany(group => Integers(group)).Order());
                }
            }
        }

        Assert.Equal(4096, Group(V(), 4096).ArrayValue!.Count);
    }

    [Theory]
    [InlineData(ScatterMode.RejectDuplicates)]
    [InlineData(ScatterMode.LastWriteWins)]
    [InlineData(ScatterMode.Sum)]
    public void DuplicatePoliciesAreExplicitAndSourcesStayUnchanged(ScatterMode mode)
    {
        ExprValue target = V(10, 20), indices = V(0, 1, 0), values = V(1, 2, 3);
        if (mode == ScatterMode.RejectDuplicates)
        {
            var error = Assert.Throws<ExpressionEvaluationException>(() => Apply(target, indices, values, mode, true));
            Assert.Equal("EVAL_SCATTER_DUPLICATE", error.Code);
            Assert.Equal("scatter.indices[2]", error.Location);
        }
        else
        {
            Assert.Equal(mode == ScatterMode.Sum ? new BigInteger[] { 14, 22 } : new BigInteger[] { 3, 2 },
                Integers(Apply(target, indices, values, mode)));
        }

        Assert.Equal(new BigInteger[] { 10, 20 }, Integers(target));
    }

    [Theory]
    [InlineData(ScatterMode.LastWriteWins)]
    [InlineData(ScatterMode.Sum)]
    public void UpdateValuesAreSnapshottedBeforeAnyInPlaceWrite(ScatterMode mode)
    {
        ExprValue target = V(1, 2);
        ExprValue result = Apply(target, V(1, 0), target, mode, true);
        Assert.Equal(mode == ScatterMode.Sum ? new BigInteger[] { 3, 3 } : new BigInteger[] { 2, 1 }, Integers(result));
    }

    [Fact]
    public void TypedPayloadsAndExactSymbolMetadataSurviveUpdates()
    {
        foreach (ExprValue[] items in new[]
                 {
                     new[] { ExprValue.Bool(true), ExprValue.Bool(false) },
                     new[] { ExprValue.String("A"), ExprValue.String("B") }, new[] { V(1), V(2, 3) }, new[]
                     {
                         ExprValue.Record(new Dictionary<string, ExprValue>
                         {
                             ["x"] = ExprValue.Number(1)
                         }),
                         ExprValue.Record(new Dictionary<string, ExprValue>
                         {
                             ["x"] = ExprValue.Number(2)
                         })
                     }
                 })
        {
            ExprValue target = ExprValue.Array(items), values = ExprValue.Array(items.Reverse().ToArray());
            Assert.Equal(values, Apply(target, V(0, 1), values, ScatterMode.RejectDuplicates));
            Assert.Equal(ExprValue.Array(items), target);
        }

        ExprValue symbols = ExprValue.Array([ExprValue.Symbol("A"), ExprValue.Symbol("B")]);
        ExprValue strings = ExprValue.Array([ExprValue.String("X"), ExprValue.String("Y")]);
        Assert.True(symbols.ContainsSymbols);
        Assert.False(Apply(symbols, V(0, 1), strings, ScatterMode.LastWriteWins).ContainsSymbols);
        Assert.True(Apply(symbols, V(0), ExprValue.Array([ExprValue.String("X")]), ScatterMode.LastWriteWins)
            .ContainsSymbols);
    }

    [Theory]
    [InlineData("fraction", "EVAL_INDEXED_DOMAIN", "scatter.indices[0]")]
    [InlineData("negative", "EVAL_INDEXED_DOMAIN", "scatter.indices[0]")]
    [InlineData("outside", "EVAL_INDEXED_DOMAIN", "scatter.indices[0]")]
    [InlineData("length", "EVAL_SCATTER_LENGTH", "scatter.values")]
    [InlineData("targetType", "EVAL_TYPE_ERROR", "scatter.target[1]")]
    [InlineData("valueType", "EVAL_TYPE_ERROR", "scatter.values[0]")]
    [InlineData("sumBoolean", "EVAL_TYPE_ERROR", "scatter.values[0]")]
    [InlineData("duplicateBeforeValue", "EVAL_SCATTER_DUPLICATE", "scatter.indices[1]")]
    public void InvalidUpdatesHaveStableOperandAndElementLocations(string scenario, string code, string location)
    {
        ExprValue target = V(1, 2), indices = V(0), values = V(10);
        var mode = ScatterMode.LastWriteWins;
        if (scenario == "fraction")
        {
            indices = ExprValue.Array([ExprValue.Rational(1, 2)]);
        }

        if (scenario == "negative")
        {
            indices = V(-1);
        }

        if (scenario == "outside")
        {
            indices = V(2);
        }

        if (scenario == "length")
        {
            values = V();
        }

        if (scenario == "targetType")
        {
            target = ExprValue.Array([ExprValue.Number(1), ExprValue.Bool(true)]);
        }

        if (scenario == "valueType")
        {
            values = ExprValue.Array([ExprValue.Bool(true)]);
        }

        if (scenario == "sumBoolean")
        {
            mode = ScatterMode.Sum;
            values = ExprValue.Array([ExprValue.Bool(true)]);
        }

        if (scenario == "duplicateBeforeValue")
        {
            mode = ScatterMode.RejectDuplicates;
            indices = V(0, 0);
            values = ExprValue.Array([ExprValue.Null, ExprValue.Null]);
        }

        var error = Assert.Throws<ExpressionEvaluationException>(() => Apply(target, indices, values, mode));
        Assert.Equal(code, error.Code);
        Assert.Equal(location, error.Location);
    }

    [Fact]
    public void KernelReadsEachInputElementOnceAndChecksAllLabels()
    {
        Probe target = new([ExprValue.Number(1), ExprValue.Number(2)]),
            indices = new([ExprValue.Number(1)]),
            values = new([ExprValue.Number(9)]);
        ExprValue a = ExprValue.Array(target), b = ExprValue.Array(indices), c = ExprValue.Array(values);
        target.Reads = indices.Reads = values.Reads = 0;
        Assert.Equal(new BigInteger[] { 1, 9 }, Integers(Apply(a, b, c, ScatterMode.LastWriteWins)));
        Assert.Equal(2, target.Reads);
        Assert.Equal(1, indices.Reads);
        Assert.Equal(1, values.Reads);
        var labels = new Probe([ExprValue.Number(0), ExprValue.Number(1), ExprValue.Number(0)]);
        ExprValue labelValues = ExprValue.Array(labels);
        labels.Reads = 0;
        Assert.Equal(new BigInteger[] { 0, 2 }, Integers(Group(labelValues, 2).ArrayValue![0]));
        Assert.Equal(3, labels.Reads);
        var invalid = Assert.Throws<ExpressionEvaluationException>(() => Group(V(0, 2), 2));
        Assert.Equal("partitionIndices.labels[1]", invalid.Location);
    }

    [Fact]
    public void PrivateTargetsTransferButEscapingAliasesAndBorrowedStateDoNot()
    {
        var producer = new MapExpr
        {
            StateKey = "target",
            MaxItems = 128,
            ItemName = "v",
            ItemType = ExprType.Number,
            Body = new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = S("v"),
                Right = N(1)
            }
        };
        ScatterExpr scatter = Scatter(target: producer);
        var state = new Dictionary<string, object?>
        {
            ["target"] = new[] { 1, 2, 3, 4 },
            ["indices"] = new[] { 0, 3 },
            ["updates"] = new[] { 10, 20 }
        };
        (SamplingFrame Frame, SamplingOperands Operands, Func<SamplingFrame, SamplingOperands?, ExprValue> Code)
            native = Native(scatter, state);
        Assert.Equal(1, native.Operands.ReusedCollectionBuffers);
        Assert.Equal(new BigInteger[] { 10, 3, 4, 20 }, Integers(native.Code(native.Frame, native.Operands)));
        Assert.True(ExpressionIrBuilder.Build(scatter, TypeCheckContext.Default).Buffers.CanReuseSource(scatter));
        var escaped = new LetExpr
        {
            Name = "x",
            Value = producer,
            Body = new TupleExpr
            {
                Items =
                [
                    new LocalExpr
                    {
                        Name = "x"
                    },
                    scatter with
                    {
                        Target = new LocalExpr
                        {
                            Name = "x"
                        }
                    }
                ]
            }
        };
        native = Native(escaped, state);
        Assert.Equal(0, native.Operands.ReusedCollectionBuffers);
        ExprValue result = native.Code(native.Frame, native.Operands);
        Assert.Equal(new BigInteger[] { 2, 3, 4, 5 }, Integers(result.RecordValue!["0"]));
        Assert.Equal(new BigInteger[] { 10, 3, 4, 20 }, Integers(result.RecordValue["1"]));
        native = Native(Scatter(), state);
        Assert.Equal(0, native.Operands.ReusedCollectionBuffers);
        native.Code(native.Frame, native.Operands);
        Assert.Equal(new[] { 1, 2, 3, 4 }, (int[])state["target"]!);
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void EveryTierRetainsResultsBoundsCancellationAndBorrowedInputs(SamplingTier tier)
    {
        var state = new Dictionary<string, object?>
        {
            ["target"] = new[] { 1, 2, 3, 4 },
            ["indices"] = new[] { 0, 3 },
            ["updates"] = new[] { 10, 20 },
            ["labels"] = new[] { 1, 0, 1, 3 }
        };
        foreach (Expression expression in new Expression[] { Scatter(), Scatter(ScatterMode.Sum), Partition() })
        {
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval) engine =
                Bind(expression, state, tier);
            ExprValue expected = ExactExpressionEvaluator.Evaluate(expression, new EvalContext
            {
                State = state
            });
            for (var call = 0; call <= SamplingOptions.HotThreshold; call++)
            {
                engine.Frame.Reset(default);
                Assert.Equal(expected, engine.Eval(engine.Frame));
                engine.Frame.CancellationToken = new CancellationToken(true);
                Assert.Throws<OperationCanceledException>(() => engine.Eval(engine.Frame));
            }
        }
    }

    [Fact]
    public void BoundsAndJsonRequiredPropertiesCannotBeOmitted()
    {
        ExprValue empty = V();
        Assert.Throws<ExpressionEvaluationException>(() =>
            IndexedArrayData.CheckScatter(empty, empty, empty, (ScatterMode)999, 1, 1));
        Assert.Throws<ExpressionEvaluationException>(() =>
            IndexedArrayData.CheckScatter(empty, empty, empty, ScatterMode.Sum, -1, 1));
        Assert.Throws<ExpressionEvaluationException>(() => IndexedArrayData.CheckPartition(empty, 4097, 1));
        Assert.Throws<ExpressionEvaluationException>(() => Group(V(0), 0));
        foreach ((Expression expression, var required) in new (Expression, string[])[]
                 {
                     (Scatter(), ["mode", "maxItems", "maxUpdates"]), (Partition(), ["bucketCount", "maxItems"])
                 })
        {
            foreach (var key in required)
            {
                JsonObject json = JsonNode.Parse(JsonSerializer.Serialize(expression, JsonOptions.Default))!.AsObject();
                json.Remove(key);
                Assert.Throws<JsonException>(() =>
                    JsonSerializer.Deserialize<Expression>(json.ToJsonString(), JsonOptions.Default));
            }
        }
    }

    [Fact]
    public void ShapesEffectsBudgetsAndLexicalDependenciesAreExplicit()
    {
        var context = new TypeCheckContext
        {
            StateFields = new[] { "target", "indices", "updates", "labels" }.Select(name =>
                ExpressionShapes.Array(ExpressionShapes.Field("item", ExprType.Number)) with
                {
                    Name = name
                }).ToArray()
        };
        ScatterExpr scatter = Scatter() with
        {
            MaxItems = 4,
            MaxUpdates = 2
        };
        Assert.Empty(ExpressionTypeChecker.Check(scatter, context));
        Assert.Equal(14, ExpressionCost.Compute(scatter));
        Assert.Equal(ExprType.Number, ExpressionShapes.Item(ExpressionShapes.Infer(scatter, context))!.Type);
        FieldDescriptor nested = ExpressionShapes.Infer(Partition(), context)!;
        Assert.Equal(ExprType.Number, ExpressionShapes.Item(ExpressionShapes.Item(nested))!.Type);
        foreach (Expression expression in new Expression[] { scatter, Partition() })
        {
            Assert.True((ExpressionIrBuilder.Build(expression, context).Effects.Flags & IrEffect.Cancellation) != 0);
        }

        Assert.NotEmpty(ExpressionTypeChecker.Check(scatter with
        {
            Target = new MapExpr
            {
                StateKey = "target",
                ItemName = "v",
                ItemType = ExprType.Number,
                Body = S("v")
            }
        }, context));
        var local = new LetExpr
        {
            Name = "labels",
            Value = S("labels"),
            Body = Partition() with
            {
                Labels = new LocalExpr
                {
                    Name = "labels"
                }
            }
        };
        Assert.IsType<LetExpr>(ExpressionSimplifier.Simplify(local).Expression);
    }

    [Fact]
    public void PartitionOuterStorageCanTransferWhileNestedGroupsRemainBorrowed()
    {
        PartitionIndicesExpr partition = Partition();
        var map = new MapExpr
        {
            Source = partition,
            MaxItems = 4,
            ItemName = "group",
            ItemType = ExprType.Array,
            Body = new CallExpr
            {
                Function = "length",
                Args = [S("group")]
            }
        };
        (SamplingFrame Frame, SamplingOperands Operands, Func<SamplingFrame, SamplingOperands?, ExprValue> Code)
            native = Native(map, new Dictionary<string, object?>
            {
                ["labels"] = new[] { 1, 0, 1, 3 }
            });
        Assert.Equal(1, native.Operands.ReusedCollectionBuffers);
        Assert.Equal(new BigInteger[] { 1, 2, 0, 1 }, Integers(native.Code(native.Frame, native.Operands)));
    }

    [Fact]
    public void EveryScatterOperandCountsTowardsAliasingProofs()
    {
        var producer = new MapExpr
        {
            StateKey = "target",
            MaxItems = 3,
            ItemName = "v",
            ItemType = ExprType.Number,
            Body = S("v")
        };
        ScatterExpr scatter = Scatter() with
        {
            Target = new LocalExpr
            {
                Name = "x"
            },
            Indices = new LocalExpr
            {
                Name = "x"
            }
        };
        var expression = new LetExpr
        {
            Name = "x",
            Value = producer,
            Body = scatter
        };
        Assert.False(ExpressionIrBuilder.Build(expression, TypeCheckContext.Default).Buffers.CanReuseSource(scatter));
        (SamplingFrame Frame, SamplingOperands Operands, Func<SamplingFrame, SamplingOperands?, ExprValue> Code)
            native = Native(expression, new Dictionary<string, object?>
            {
                ["target"] = new[] { 0, 1, 2 },
                ["updates"] = new[] { 10, 20, 30 }
            });
        Assert.Equal(0, native.Operands.ReusedCollectionBuffers);
        Assert.Equal(new BigInteger[] { 10, 20, 30 }, Integers(native.Code(native.Frame, native.Operands)));
    }

    [Fact]
    public void EagerOperandsAndLazyEnclosingBranchesRetainErrorOrder()
    {
        ScatterExpr expression = Scatter() with
        {
            Target = S("targetMissing"),
            Indices = S("indicesMissing"),
            Values = S("valuesMissing")
        };
        var context = new EvalContext
        {
            State = new Dictionary<string, object?>
            {
                ["target"] = V(1),
                ["indices"] = V(0)
            }
        };
        Assert.Equal("targetMissing",
            Assert.Throws<ExpressionEvaluationException>(() => ExactExpressionEvaluator.Evaluate(expression, context))
                .Location);
        expression = expression with
        {
            Target = S("target")
        };
        Assert.Equal("indicesMissing",
            Assert.Throws<ExpressionEvaluationException>(() => ExactExpressionEvaluator.Evaluate(expression, context))
                .Location);
        expression = expression with
        {
            Indices = S("indices")
        };
        Assert.Equal("valuesMissing",
            Assert.Throws<ExpressionEvaluationException>(() => ExactExpressionEvaluator.Evaluate(expression, context))
                .Location);
        var skipped = new IfExpr
        {
            Condition = new ConstantExpr
            {
                Kind = ConstantKind.Boolean,
                Value = "false"
            },
            ThenExpr = expression,
            ElseExpr = N(5)
        };
        Assert.Equal(5, ExactExpressionEvaluator.Evaluate(skipped, context).AsInteger());
    }

    [Fact]
    public void CancellationDuringValidationStopsBeforeAnySourceMutation()
    {
        using var cancellation = new CancellationTokenSource();
        var probe = new Probe([ExprValue.Number(1), ExprValue.Number(2)]);
        ExprValue target = ExprValue.Array(probe);
        probe.Reads = 0;
        probe.OnRead = () =>
        {
            if (probe.Reads == 1)
            {
                cancellation.Cancel();
            }
        };
        Assert.Throws<OperationCanceledException>(() =>
            Apply(target, V(0), V(99), ScatterMode.LastWriteWins, token: cancellation.Token));
        Assert.Equal(1, probe.Reads);
        probe.OnRead = null;
        Assert.Equal(new BigInteger[] { 1, 2 }, Integers(target));
    }

    [Fact]
    public void SharedNativeShapesKeepDuplicateModesAndBucketDomainsDistinct()
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

        var state = new Dictionary<string, object?>
        {
            ["target"] = new[] { 1, 2 },
            ["indices"] = new[] { 0, 0 },
            ["updates"] = new[] { 3, 4 },
            ["labels"] = new[] { 1, 0 }
        };
        foreach (var key in state.Keys)
        {
            Slot(key);
        }

        var owner = new SamplingExpressions(Slot, SamplingTier.Compiled, shapes: new SamplingShapes());
        Func<SamplingFrame, ExprValue> last = owner.Compile(Scatter()),
            sum = owner.Compile(Scatter(ScatterMode.Sum)),
            two = owner.Compile(Partition(2)),
            three = owner.Compile(Partition(3));
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], state);
        frame.Reset(default);
        Assert.Equal(new BigInteger[] { 4, 2 }, Integers(last(frame)));
        Assert.Equal(new BigInteger[] { 8, 2 }, Integers(sum(frame)));
        Assert.Equal(2, two(frame).ArrayValue!.Count);
        Assert.Equal(3, three(frame).ArrayValue!.Count);
        Assert.Equal(new BigInteger[] { 4, 2 }, Integers(last(frame)));
    }

    [Fact]
    public void ConflictingRecordFieldsNeverAcquireAnInventedResultType()
    {
        FieldDescriptor Record(ExprType second)
        {
            return new FieldDescriptor
            {
                Name = "row",
                Type = ExprType.Record,
                RecordFields = [ExpressionShapes.Field("x", ExprType.Number), ExpressionShapes.Field("y", second)]
            };
        }

        var context = new TypeCheckContext
        {
            StateFields =
            [
                ExpressionShapes.Array(Record(ExprType.String)) with
                {
                    Name = "target"
                },
                ExpressionShapes.Array(Record(ExprType.Number)) with
                {
                    Name = "updates"
                },
                ExpressionShapes.Array(ExpressionShapes.Field("item", ExprType.Number)) with
                {
                    Name = "indices"
                }
            ]
        };
        Assert.Empty(ExpressionTypeChecker.Check(Scatter(), context));
        FieldDescriptor result = ExpressionShapes.Item(ExpressionShapes.Infer(Scatter(), context))!;
        Assert.Equal(ExprType.Number, ExpressionShapes.Project(result, ["x"])!.Type);
        Assert.Null(ExpressionShapes.Project(result, ["y"]));
    }

    private sealed class Probe(ExprValue[] items) : IReadOnlyList<ExprValue>
    {
        public int Reads { get; set; }
        public Action? OnRead { get; set; }

        public ExprValue this[int index]
        {
            get
            {
                Reads++;
                OnRead?.Invoke();
                return items[index];
            }
        }

        public int Count => items.Length;

        public IEnumerator<ExprValue> GetEnumerator() =>
            Enumerable.Range(0, items.Length).Select(index => this[index]).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
