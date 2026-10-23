using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlotMath.Core.Compiler;
using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;
using SlotMath.Core.Optimization;

namespace SlotMath.Core.Tests.Expressions;

[Collection("SamplingOptions")]
public sealed class CollectionPlanTests
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

    private static ExprValue Numbers(params int[] values) =>
        ExprValue.Array(values.Select(v => ExprValue.Number(v)).ToArray());

    private static ExprValue Flags(params bool[] values) => ExprValue.Array(values.Select(ExprValue.Bool).ToArray());

    private static BigInteger[] Integers(ExprValue value) =>
        value.ArrayValue!.Select(item => item.AsInteger()).ToArray();

    private static ScanExpr Scan(Expression? source = null, ScanMode mode = ScanMode.Inclusive, int max = 128) => new()
    {
        Source = source ?? S("values"),
        Mode = mode,
        MaxItems = max
    };

    private static SegmentedCompactExpr Compact(SegmentPacking packing = SegmentPacking.End) => new()
    {
        Keep = S("keep"),
        Boundaries = S("boundaries"),
        Packing = packing,
        MaxItems = 128,
        MaxSegments = 128
    };

    private static ExprValue RunScan(ExprValue source, ScanMode mode, int max = 128)
    {
        var length = CollectionPlanData.CheckScan(source, mode, max);
        return CollectionPlanData.Scan(source, mode, new ExprValue[length], default);
    }

    private static ExprValue RunCompact(ExprValue keep, ExprValue boundaries, SegmentPacking packing, int max = 128,
        int segments = 128)
    {
        (int Items, int Segments) shape = CollectionPlanData.CheckCompact(keep, boundaries, packing, max, segments);
        return CollectionPlanData.Compact(keep, boundaries, packing, new ExprValue[shape.Items],
            new ExprValue[shape.Segments], default);
    }

    private static (SamplingFrame Frame, Dictionary<string, int> Slots,
        Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) Bind(Expression expression,
            Dictionary<string, object?> initial, bool optimize = true)
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
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], initial);
        frame.Reset(default);
        return (frame, slots, native.Code, native.Operands);
    }

    [Theory]
    [InlineData(ScanMode.Inclusive)]
    [InlineData(ScanMode.Exclusive)]
    public void PrefixSumsMatchIndependentBigIntegerPrefixes(ScanMode mode)
    {
        var random = new System.Random(8191);
        for (var count = 0; count <= 128; count++)
        {
            BigInteger[] input = Enumerable.Range(0, count)
                .Select(i => new BigInteger(random.Next(-20, 21)) + (i % 5 == 0 ? BigInteger.One << 100 : 0)).ToArray();
            ExprValue source = ExprValue.Array(input.Select(ExprValue.Number).ToArray());
            BigInteger[] expected = Enumerable.Range(0, count).Select(index =>
                input.Take(index + (mode == ScanMode.Inclusive ? 1 : 0))
                    .Aggregate(BigInteger.Zero, (sum, item) => sum + item)).ToArray();
            Assert.Equal(expected, Integers(RunScan(source, mode)));
            Assert.Equal(input, source.ArrayValue!.Select(item => item.AsInteger()));
        }
    }

    [Theory]
    [InlineData(ScanMode.Inclusive)]
    [InlineData(ScanMode.Exclusive)]
    public void BooleanFlagsProduceNumericPrefixesAndRetainTheLastItemCheck(ScanMode mode)
    {
        Assert.Equal(mode == ScanMode.Inclusive ? new BigInteger[] { 1, 1, 2, 3 } : new BigInteger[] { 0, 1, 1, 2 },
            Integers(RunScan(Flags(true, false, true, true), mode)));
        Assert.Empty(RunScan(Flags(), mode).ArrayValue!);
        ExprValue bad = ExprValue.Array([ExprValue.Number(1), ExprValue.Rational(1, 2)]);
        var error = Assert.Throws<ExpressionEvaluationException>(() => RunScan(bad, mode));
        Assert.Equal("EVAL_SCAN_INTEGER", error.Code);
        Assert.Equal("scan.source[1]", error.Location);
    }

    [Theory]
    [InlineData("mixed", "EVAL_TYPE_ERROR", "scan.source[1]")]
    [InlineData("null", "EVAL_TYPE_ERROR", "scan.source[1]")]
    [InlineData("text", "EVAL_TYPE_ERROR", "scan.source[1]")]
    [InlineData("mode", "EVAL_SCAN_MODE", "scan.mode")]
    [InlineData("bound", "EVAL_COLLECTION_BUDGET", "scan.source")]
    public void ScanErrorsAreStrictLocatedAndBounded(string kind, string code, string location)
    {
        ExprValue source = kind switch
        {
            "mixed" => ExprValue.Array([ExprValue.Number(1), ExprValue.Bool(true)]),
            "null" => ExprValue.Array([ExprValue.Number(1), ExprValue.Null]),
            "text" => ExprValue.Array([ExprValue.Number(1), ExprValue.String("2")]),
            _ => Numbers(1, 2)
        };
        var error = Assert.Throws<ExpressionEvaluationException>(() =>
            RunScan(source, kind == "mode" ? (ScanMode)99 : ScanMode.Inclusive, kind == "bound" ? 1 : 128));
        Assert.Equal(code, error.Code);
        Assert.Equal(location, error.Location);
    }

    [Fact]
    public void SmallSegmentedPlansMatchIndependentStableConcatenation()
    {
        for (var count = 0; count <= 6; count++)
        {
            var cutVariants = count == 0 ? 1 : 1 << (count - 1);
            for (var cuts = 0; cuts < cutVariants; cuts++)
            {
                var offsets = new[] { 0 }
                    .Concat(Enumerable.Range(1, System.Math.Max(0, count - 1)).Where(i => (cuts & (1 << (i - 1))) != 0))
                    .Append(count).ToArray();
                for (var encoded = 0; encoded < 1 << count; encoded++)
                {
                    var keep = Enumerable.Range(0, count).Select(i => (encoded & (1 << i)) != 0).ToArray();
                    foreach (SegmentPacking packing in Enum.GetValues<SegmentPacking>())
                    {
                        var expected = new List<int>();
                        var free = new List<int>();
                        foreach ((int First, int Second) pair in offsets.Zip(offsets.Skip(1)))
                        {
                            var kept = Enumerable.Range(pair.First, pair.Second - pair.First).Where(i => keep[i])
                                .ToArray();
                            var holes = pair.Second - pair.First - kept.Length;
                            free.Add(holes);
                            expected.AddRange(packing == SegmentPacking.Start
                                ? kept.Concat(Enumerable.Repeat(-1, holes))
                                : Enumerable.Repeat(-1, holes).Concat(kept));
                        }

                        ExprValue result = RunCompact(Flags(keep), Numbers(offsets), packing);
                        Assert.Equal(expected.Select(i => new BigInteger(i)), Integers(result.RecordValue!["sources"]));
                        Assert.Equal(free.Select(i => new BigInteger(i)), Integers(result.RecordValue["freeCounts"]));
                    }
                }
            }
        }
    }

    [Fact]
    public void EmptyAndRepeatedBoundariesHaveExplicitSemantics()
    {
        ExprValue result = RunCompact(Flags(true, false, true, false, true), Numbers(0, 0, 2, 2, 5, 5),
            SegmentPacking.End);
        Assert.Equal(new BigInteger[] { -1, 0, -1, 2, 4 }, Integers(result.RecordValue!["sources"]));
        Assert.Equal(new BigInteger[] { 0, 1, 0, 1, 0 }, Integers(result.RecordValue["freeCounts"]));
        result = RunCompact(Flags(), Numbers(0), SegmentPacking.Start);
        Assert.Empty(result.RecordValue!["sources"].ArrayValue!);
        Assert.Empty(result.RecordValue["freeCounts"].ArrayValue!);
    }

    [Theory]
    [InlineData("missing", "EVAL_COMPACT_BOUNDARIES", "segmentedCompact.boundaries")]
    [InlineData("first", "EVAL_COMPACT_BOUNDARIES", "segmentedCompact.boundaries[0]")]
    [InlineData("last", "EVAL_COMPACT_BOUNDARIES", "segmentedCompact.boundaries[1]")]
    [InlineData("decrease", "EVAL_COMPACT_BOUNDARIES", "segmentedCompact.boundaries[2]")]
    [InlineData("fraction", "EVAL_COMPACT_BOUNDARIES", "segmentedCompact.boundaries[1]")]
    [InlineData("flag", "EVAL_TYPE_ERROR", "segmentedCompact.keep[1]")]
    [InlineData("packing", "EVAL_COMPACT_PACKING", "segmentedCompact.packing")]
    [InlineData("segments", "EVAL_COMPACT_BOUNDARIES", "segmentedCompact.boundaries")]
    public void CompactionRejectsMalformedPlansBeforeProducingState(string kind, string code, string location)
    {
        ExprValue keep = kind == "flag"
            ? ExprValue.Array([ExprValue.Bool(true), ExprValue.Number(1), ExprValue.Bool(false)])
            : Flags(true, false, true);
        ExprValue boundaries = kind switch
        {
            "missing" => Numbers(),
            "first" => Numbers(1, 3),
            "last" => Numbers(0, 2),
            "decrease" => Numbers(0, 2, 1, 3),
            "fraction" => ExprValue.Array([ExprValue.Number(0), ExprValue.Rational(3, 2), ExprValue.Number(3)]),
            _ => Numbers(0, 3)
        };
        var error = Assert.Throws<ExpressionEvaluationException>(() => RunCompact(keep, boundaries,
            kind == "packing" ? (SegmentPacking)99 : SegmentPacking.End, segments: kind == "segments" ? 0 : 128));
        Assert.Equal(code, error.Code);
        Assert.Equal(location, error.Location);
    }

    [Theory]
    [InlineData(ScanMode.Inclusive)]
    [InlineData(ScanMode.Exclusive)]
    public void ScanCanTransferAPrivateBooleanBufferWithoutChangingItsTypeProof(ScanMode mode)
    {
        var source = new MapExpr
        {
            StateKey = "values",
            MaxItems = 128,
            ItemName = "v",
            ItemType = ExprType.Number,
            Body = new CompareExpr
            {
                Op = CompareOp.Gt,
                Left = S("v"),
                Right = N(0)
            }
        };
        ScanExpr expression = Scan(source, mode);
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
            SamplingOperands Operands) native = Bind(expression, new Dictionary<string, object?>
            {
                ["values"] = new[] { 1, -1, 2, 3 }
            });
        Assert.Equal(1, native.Operands.ReusedCollectionBuffers);
        Assert.Equal(mode == ScanMode.Inclusive ? new BigInteger[] { 1, 1, 2, 3 } : new BigInteger[] { 0, 1, 1, 2 },
            Integers(native.Code(native.Frame, native.Operands)));
        Assert.True(ExpressionIrBuilder.Build(expression, TypeCheckContext.Default).Buffers.CanReuseSource(expression));
    }

    [Fact]
    public void BorrowedInputsAndEscapingLexicalAliasesRemainImmutable()
    {
        ExprValue[] storage = [ExprValue.Number(1), ExprValue.Number(2), ExprValue.Number(3)];
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, SamplingOperands?, ExprValue> Code,
            SamplingOperands Operands) native = Bind(Scan(), new Dictionary<string, object?>
            {
                ["values"] = ExprValue.ArrayOwned(storage, false)
            });
        Assert.Equal(0, native.Operands.ReusedCollectionBuffers);
        Assert.Equal(new BigInteger[] { 1, 3, 6 }, Integers(native.Code(native.Frame, native.Operands)));
        Assert.Equal(new BigInteger[] { 1, 2, 3 }, storage.Select(value => value.AsInteger()));
        var local = new LocalExpr
        {
            Name = "saved"
        };
        var expression = new LetExpr
        {
            Name = "saved",
            Value = new MapExpr
            {
                StateKey = "values",
                MaxItems = 128,
                ItemName = "v",
                ItemType = ExprType.Number,
                Body = S("v")
            },
            Body = new TupleExpr
            {
                Items = [local, Scan(local)]
            }
        };
        native = Bind(expression, new Dictionary<string, object?>
        {
            ["values"] = new[] { 1, 2, 3 }
        });
        Assert.Equal(0, native.Operands.ReusedCollectionBuffers);
        ExprValue result = native.Code(native.Frame, native.Operands);
        Assert.Equal(new BigInteger[] { 1, 2, 3 }, Integers(result.RecordValue!["0"]));
        Assert.Equal(new BigInteger[] { 1, 3, 6 }, Integers(result.RecordValue["1"]));
    }

    private static TypeCheckContext Context() => new()
    {
        StateFields =
        [
            new FieldDescriptor
            {
                Name = "values",
                Type = ExprType.Array,
                ArrayItemType = ExprType.Number
            },
            new FieldDescriptor
            {
                Name = "keep",
                Type = ExprType.Array,
                ArrayItemType = ExprType.Boolean
            },
            new FieldDescriptor
            {
                Name = "boundaries",
                Type = ExprType.Array,
                ArrayItemType = ExprType.Number
            }
        ]
    };

    [Fact]
    public void ResultShapesEffectsBoundsAndLexicalUsesStayVisibleToTheCompiler()
    {
        Assert.Empty(ExpressionTypeChecker.Check(Scan(), Context(), ExprType.Array));
        Assert.Empty(ExpressionTypeChecker.Check(Compact(), Context(), ExprType.Record));
        Assert.NotEmpty(ExpressionTypeChecker.Check(Scan(max: -1), Context()));
        Assert.NotEmpty(ExpressionTypeChecker.Check(Compact() with
        {
            MaxSegments = 4097
        }, Context()));
        FieldDescriptor? shape = ExpressionShapes.Infer(Compact(), Context());
        Assert.Equal(ExprType.Number, ExpressionShapes.Item(ExpressionShapes.Project(shape, ["sources"]))?.Type);
        foreach (Expression expression in new Expression[] { Scan(), Compact() })
        {
            ExpressionIr ir = ExpressionIrBuilder.Build(expression, Context());
            Assert.Empty(IrVerifier.Verify(ir));
            Assert.True(ir.Effects.Flags.HasFlag(IrEffect.Cancellation));
            Assert.False(ir.Effects.CanSpeculate);
            Assert.IsType<LetExpr>(ExpressionSimplifier.Simplify(new LetExpr
            {
                Name = "unused",
                Value = expression,
                Body = N(0)
            }).Expression);
        }

        var used = new LetExpr
        {
            Name = "flags",
            Value = new TupleExpr(),
            Body = Compact() with
            {
                Keep = new LocalExpr
                {
                    Name = "flags"
                }
            }
        };
        Assert.IsType<LetExpr>(ExpressionSimplifier.Simplify(used).Expression);
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void EachTierPreservesExactResultsAndCancellation(SamplingTier tier)
    {
        var layout = new Dictionary<string, int>
        {
            ["values"] = 0,
            ["keep"] = 1,
            ["boundaries"] = 2
        };
        var initial = new Dictionary<string, object?>
        {
            ["values"] = new[] { 1, 2, 3 },
            ["keep"] = new[] { true, false, true },
            ["boundaries"] = new[] { 0, 3 }
        };
        foreach (Expression expression in new Expression[] { Scan(), Scan(mode: ScanMode.Exclusive), Compact() })
        {
            var frame = new SamplingFrame(layout, new SamplingCell[3], initial);
            frame.Reset(default);
            Func<SamplingFrame, ExprValue> evaluate =
                new SamplingExpressions(name => layout[name], tier).Compile(expression);
            ExprValue expected = ExactExpressionEvaluator.Evaluate(expression, new EvalContext
            {
                State = initial
            });
            for (var call = 0; call <= SamplingOptions.HotThreshold; call++)
            {
                frame.CancellationToken = new CancellationToken(true);
                Assert.Throws<OperationCanceledException>(() => evaluate(frame));
                frame.CancellationToken = default;
                Assert.Equal(expected, evaluate(frame));
            }
        }
    }

    [Fact]
    public void EagerOperandsAndLazyBranchesRetainErrorOrder()
    {
        SegmentedCompactExpr bad = Compact() with
        {
            Keep = N(1),
            Boundaries = new BinaryExpr
            {
                Op = BinaryOp.Div,
                Left = N(1),
                Right = N(0)
            }
        };
        Assert.Equal(EvalErrorCodes.DivisionByZero,
            Assert
                .Throws<ExpressionEvaluationException>(() => ExactExpressionEvaluator.Evaluate(bad, EvalContext.Empty))
                .Code);
        var lazy = new IfExpr
        {
            Condition = new ConstantExpr
            {
                Kind = ConstantKind.Boolean,
                Value = "false"
            },
            ThenExpr = bad,
            ElseExpr = N(7)
        };
        Assert.Equal(7, ExactExpressionEvaluator.Evaluate(lazy, EvalContext.Empty).AsInteger());
    }

    [Fact]
    public void JsonRequiresModePackingAndExplicitBounds()
    {
        foreach (Expression expression in new Expression[] { Scan(), Compact() })
        {
            var json = JsonSerializer.Serialize(expression, JsonOptions.Default);
            Assert.Equal(expression.GetType(),
                JsonSerializer.Deserialize<Expression>(json, JsonOptions.Default)!.GetType());
            JsonNode source = JsonNode.Parse(json)!;
            source.AsObject().Remove(expression is ScanExpr ? "mode" : "maxSegments");
            Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<Expression>(source.ToJsonString(), JsonOptions.Default));
        }
    }
}
