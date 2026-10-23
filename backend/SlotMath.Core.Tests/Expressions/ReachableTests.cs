using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;
using SlotMath.Core.Optimization;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Expressions;

[Collection("SamplingOptions")]
public sealed class ReachableTests
{
    private static ConstantExpr N(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString()
    };

    private static FieldAccessExpr S(string field) => new()
    {
        Target = "state",
        Path = [field]
    };

    private static ReachableExpr R(int count = 4) => new()
    {
        Adjacency = S("adjacency"),
        Allowed = S("allowed"),
        Seeds = S("seeds"),
        VertexCount = count
    };

    private static ExprValue A(params int[] values) =>
        ExprValue.Array(values.Select(value => ExprValue.Number(value)).ToArray());

    private static ExprValue Run(ExprValue adjacency, long allowed, long seeds, int count) =>
        ReachabilityData.Evaluate(adjacency, ExprValue.Number(allowed), ExprValue.Number(seeds), count);

    private static Dictionary<string, Port> Ports() => new()
    {
        ["state"] = new Port
        {
            Name = "state",
            Type = PortType.State
        }
    };

    private static Edge E(string a, string b) => new()
    {
        Id = a + b,
        SourceNodeId = a,
        SourcePort = "state",
        TargetNodeId = b,
        TargetPort = "state"
    };

    [Fact]
    public void EverySmallDirectedGraphMatchesIndependentTransitiveClosure()
    {
        // Floyd-Warshall is independent of the production word-frontier traversal.
        for (var encoded = 0; encoded < 512; encoded++)
        {
            var rows = Enumerable.Range(0, 3).Select(row => (encoded >> (3 * row)) & 7).ToArray();
            ExprValue adjacency = A(rows);
            for (var allowed = 0; allowed < 8; allowed++)
            {
                var paths = new bool[3, 3];
                for (var i = 0; i < 3; i++)
                {
                    for (var j = 0; j < 3; j++)
                    {
                        paths[i, j] = (allowed & (1 << i)) != 0 && (allowed & (1 << j)) != 0 &&
                                      (i == j || (rows[i] & (1 << j)) != 0);
                    }
                }

                for (var k = 0; k < 3; k++)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        for (var j = 0; j < 3; j++)
                        {
                            paths[i, j] |= paths[i, k] && paths[k, j];
                        }
                    }
                }

                for (var seeds = 0; seeds < 8; seeds++)
                {
                    var expected = 0;
                    for (var vertex = 0; vertex < 3; vertex++)
                    {
                        if (Enumerable.Range(0, 3).Any(seed => (seeds & (1 << seed)) != 0 && paths[seed, vertex]))
                        {
                            expected |= 1 << vertex;
                        }
                    }

                    Assert.Equal(expected, Run(adjacency, allowed, seeds, 3).AsInteger());
                }
            }
        }
    }

    [Fact]
    public void DirectionBlockedVerticesCyclesAndEmptyDomainsHaveDefinedResults()
    {
        Assert.Equal(7, Run(A(2, 4, 1, 0), 15, 1, 4).AsInteger());
        Assert.Equal(1, Run(A(0, 1), 3, 1, 2).AsInteger());
        Assert.Equal(3, Run(A(0, 1), 3, 2, 2).AsInteger());
        Assert.Equal(6, Run(A(2, 4, 1, 0), 6, 11, 4).AsInteger());
        Assert.Equal(0, Run(A(2, 4, 1, 0), 0, 15, 4).AsInteger());
        Assert.Equal(0, Run(A(), 0, 0, 0).AsInteger());
    }

    [Fact]
    public void Bit63AndTheFullUnsignedDomainAreExact()
    {
        ExprValue[] rows = Enumerable.Repeat(ExprValue.Number(0), 64).ToArray();
        rows[0] = ExprValue.Number(BigInteger.One << 63);
        rows[63] = ExprValue.Number(BigInteger.One << 62);
        rows[62] = ExprValue.Number(1);
        ExprValue result = ReachabilityData.Evaluate(ExprValue.Array(rows),
            ExprValue.Number(new BigInteger(ulong.MaxValue)), ExprValue.Number(1), 64);
        Assert.Equal((BigInteger.One << 63) + (BigInteger.One << 62) + 1, result.AsInteger());
        rows[63] = ExprValue.Number(new BigInteger(ulong.MaxValue));
        Assert.Equal(new BigInteger(ulong.MaxValue),
            ReachabilityData.Evaluate(ExprValue.Array(rows), ExprValue.Number(new BigInteger(ulong.MaxValue)),
                ExprValue.Number(1), 64).AsInteger());
    }

    [Fact]
    public void ValidTraversalAllocatesNothingEvenWhenAnAdjacencyRowUsesBit63()
    {
        ExprValue[] rows = Enumerable.Repeat(ExprValue.Number(0), 64).ToArray();
        rows[0] = ExprValue.Number(BigInteger.One << 63);
        ExprValue adjacency = ExprValue.Array(rows);
        for (var warmup = 0; warmup < 64; warmup++)
        {
            Run(adjacency, 1, 1, 64);
        }

        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var run = 0; run < 1024; run++)
        {
            Run(adjacency, 1, 1, 64);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
    }

    [Theory]
    [InlineData("count", "EVAL_REACHABLE_BUDGET", "reachable.vertexCount")]
    [InlineData("length", "EVAL_REACHABLE_LENGTH", "reachable.adjacency")]
    [InlineData("array", "EVAL_TYPE_ERROR", "reachable.adjacency")]
    [InlineData("negative", "EVAL_BITSET_RANGE", "reachable.adjacency[3]")]
    [InlineData("fraction", "EVAL_BITSET_RANGE", "reachable.adjacency[3]")]
    [InlineData("overflow", "EVAL_BITSET_RANGE", "reachable.adjacency[3]")]
    [InlineData("text", "EVAL_TYPE_ERROR", "reachable.adjacency[3]")]
    [InlineData("edge", "EVAL_REACHABLE_DOMAIN", "reachable.adjacency[3]")]
    [InlineData("allowed", "EVAL_REACHABLE_DOMAIN", "reachable.allowed")]
    [InlineData("seeds", "EVAL_REACHABLE_DOMAIN", "reachable.seeds")]
    public void AllInputsAreValidatedEvenWhenNoVertexWouldBeVisited(string kind, string code, string location)
    {
        ExprValue[] rows = [ExprValue.Number(0), ExprValue.Number(0), ExprValue.Number(0), ExprValue.Number(0)];
        rows[3] = kind switch
        {
            "negative" => ExprValue.Number(-1),
            "fraction" => ExprValue.Rational(1, 2),
            "overflow" => ExprValue.Number(BigInteger.One << 64),
            "text" => ExprValue.String("1"),
            "edge" => ExprValue.Number(16),
            _ => rows[3]
        };
        ExprValue adjacency = kind == "array" ? ExprValue.Number(0) : ExprValue.Array(rows);
        var error = Assert.Throws<ExpressionEvaluationException>(() => ReachabilityData.Evaluate(adjacency,
            ExprValue.Number(kind == "allowed" ? 16 : 0), ExprValue.Number(kind == "seeds" ? 16 : 0),
            kind == "count" ? 65 : kind == "length" ? 3 : 4));
        Assert.Equal(code, error.Code);
        Assert.Equal(location, error.Location);
    }

    private static TypeCheckContext Context() => new()
    {
        StateFields =
        [
            new FieldDescriptor
            {
                Name = "adjacency",
                Type = ExprType.Array,
                ArrayItemType = ExprType.Number
            },
            new FieldDescriptor
            {
                Name = "allowed",
                Type = ExprType.Number
            },
            new FieldDescriptor
            {
                Name = "seeds",
                Type = ExprType.Number
            }
        ]
    };

    [Fact]
    public void TypeCostAndEffectsAreGenericBoundedAndCannotBeSpeculated()
    {
        ReachableExpr expression = R(64);
        Assert.Empty(ExpressionTypeChecker.Check(expression, Context(), ExprType.Number));
        Assert.Equal(132, ExpressionCost.Compute(expression));
        Assert.NotEmpty(ExpressionTypeChecker.Check(expression with
        {
            VertexCount = 65
        }, Context()));
        Assert.NotEmpty(ExpressionTypeChecker.Check(expression with
        {
            VertexCount = -1
        }, Context()));
        Assert.NotEmpty(ExpressionTypeChecker.Check(expression with
        {
            Adjacency = N(1)
        }, Context()));
        ExpressionIr ir = ExpressionIrBuilder.Build(expression, Context());
        Assert.Empty(IrVerifier.Verify(ir));
        IrOperation operation = Assert.Single(ir.Operations(), op => op.Code == IrOp.Reachable);
        Assert.Equal(64, operation.Bound);
        Assert.Equal(3, operation.Inputs.Count);
        Assert.True(ir.Effects.Flags.HasFlag(IrEffect.Cancellation));
        Assert.False(ir.Effects.CanSpeculate);
        Assert.True(ir.Effects.Reads.SetEquals(new[] { "adjacency", "allowed", "seeds" }));
        var dead = new LetExpr
        {
            Name = "unused",
            Value = expression,
            Body = N(7)
        };
        Assert.IsType<LetExpr>(ExpressionSimplifier.Simplify(dead).Expression);
        var used = new LetExpr
        {
            Name = "permit",
            Value = N(7),
            Body = expression with
            {
                Allowed = new LocalExpr
                {
                    Name = "permit"
                }
            }
        };
        Assert.IsType<LetExpr>(ExpressionSimplifier.Simplify(used).Expression);
    }

    [Fact]
    public void OperandEvaluationAndLazyBranchesKeepTheirOriginalErrorOrder()
    {
        ReachableExpr expression = R() with
        {
            Adjacency = N(1),
            Allowed = new BinaryExpr
            {
                Op = BinaryOp.Div,
                Left = N(1),
                Right = N(0)
            }
        };
        Assert.Equal(EvalErrorCodes.DivisionByZero,
            Assert.Throws<ExpressionEvaluationException>(() =>
                ExactExpressionEvaluator.Evaluate(expression, EvalContext.Empty)).Code);
        Expression lazy = new IfExpr
        {
            Condition = new ConstantExpr
            {
                Kind = ConstantKind.Boolean,
                Value = "false"
            },
            ThenExpr = expression,
            ElseExpr = N(11)
        };
        Assert.Equal(11, ExactExpressionEvaluator.Evaluate(lazy, EvalContext.Empty).AsInteger());
    }

    [Fact]
    public void CancellationSurvivesLexicalAndCollectionContexts()
    {
        var state = new Dictionary<string, object?>
        {
            ["adjacency"] = new[] { 2, 4, 9, 0 },
            ["allowed"] = 15,
            ["seeds"] = 1,
            ["items"] = new[] { 0 }
        };
        Expression[] expressions =
        [
            R(), new LetExpr
            {
                Name = "x",
                Value = N(1),
                Body = R()
            },
            new MapExpr
            {
                StateKey = "items",
                ItemName = "i",
                ItemType = ExprType.Number,
                Body = R()
            }
        ];
        foreach (Expression expression in expressions)
        {
            Assert.Throws<OperationCanceledException>(() => ExactExpressionEvaluator.Evaluate(expression,
                new EvalContext
                {
                    State = state,
                    CancellationToken = new CancellationToken(true)
                }));
        }
    }

    private static GraphConfig Graph(bool random = true)
    {
        Node[] prefix = random
            ?
            [
                new DrawNode
                {
                    Id = "allowed",
                    StateWriteKey = "allowed",
                    Outputs = Ports(),
                    DrawWeights =
                    [
                        new DrawWeight
                        {
                            OutcomeId = "restricted",
                            Weight = 1,
                            Value = 3,
                            StateValue = N(3)
                        },
                        new DrawWeight
                        {
                            OutcomeId = "all",
                            Weight = 3,
                            Value = 15,
                            StateValue = N(15)
                        }
                    ]
                },
                new DrawNode
                {
                    Id = "seeds",
                    StateWriteKey = "seeds",
                    Inputs = Ports(),
                    Outputs = Ports(),
                    DrawWeights =
                    [
                        new DrawWeight
                        {
                            OutcomeId = "first",
                            Weight = 1,
                            Value = 1,
                            StateValue = N(1)
                        },
                        new DrawWeight
                        {
                            OutcomeId = "third",
                            Weight = 1,
                            Value = 4,
                            StateValue = N(4)
                        }
                    ]
                }
            ]
            : [];
        Node[] nodes =
        [
            .. prefix, new ModifyStateNode
            {
                Id = "reach",
                ExpressionId = "reach",
                OutputKey = "reached",
                Inputs = Ports(),
                Outputs = Ports()
            },
            new ModifyStateNode
            {
                Id = "pay",
                ExpressionId = "pay",
                OutputKey = "win",
                Inputs = Ports(),
                Outputs = Ports()
            },
            new MetricsSinkNode
            {
                Id = "sink",
                WinStateKey = "win",
                WinCap = 100,
                Inputs = Ports()
            }
        ];
        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Nodes = nodes,
            Edges = nodes.Zip(nodes.Skip(1)).Select(pair => E(pair.First.Id, pair.Second.Id)).ToArray(),
            InitialState = new Dictionary<string, JsonElement>
            {
                ["adjacency"] = JsonSerializer.SerializeToElement(new[] { 2, 4, 9, 0 }),
                ["allowed"] = JsonSerializer.SerializeToElement(15),
                ["seeds"] = JsonSerializer.SerializeToElement(1),
                ["reached"] = JsonSerializer.SerializeToElement(99)
            },
            Expressions = new Dictionary<string, Expression>
            {
                ["reach"] = R(),
                ["pay"] = new BitSetExpr
                {
                    Op = BitSetOp.Count,
                    Args = [S("reached")]
                }
            }
        };
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void CompleteSeededStateAndRngTranscriptMatchIndependentDraws(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            GraphConfig graph = Graph();
            var source = JsonSerializer.Serialize(graph, JsonOptions.Default);
            CompileResult compiled = new GraphCompiler().Compile(graph);
            CompileResult canonical = new GraphCompiler(optimizeSampling: false).Compile(graph);
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            Assert.True(canonical.IsValid);
            ISamplingRunner<Dictionary<string, object?>, BigInteger> runner =
                ((ICompiledSampling<Dictionary<string, object?>, BigInteger>)compiled.Program!).CreateRunner(
                    new Dictionary<string, object?>());
            AliasMethod a = WeightSet.FromIntegers([1, 3]).AliasTable;
            AliasMethod b = WeightSet.FromIntegers([1, 1]).AliasTable;
            for (var seed = 0; seed < 192; seed++)
            {
                var actualRng = new SeededRandom(seed);
                var oracleRng = new SeededRandom(seed);
                actualRng.EnableAudit();
                oracleRng.EnableAudit();
                var allowed = a.Sample(oracleRng) == 0 ? 3 : 15;
                var seeds = b.Sample(oracleRng) == 0 ? 1 : 4;
                var expected = allowed == 15 ? 4 : seeds == 1 ? 2 : 0;
                Assert.Equal(expected, runner.Run(actualRng, default));
                Assert.Equal(JsonSerializer.Serialize(oracleRng.AuditSnapshot(64)),
                    JsonSerializer.Serialize(actualRng.AuditSnapshot(64)));
                Assert.Equal(oracleRng.NextUInt64(), actualRng.NextUInt64());
                (BigInteger Value, Dictionary<string, object?> State) actual =
                    SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed);
                (BigInteger Value, Dictionary<string, object?> State) reference =
                    SampledInterpreter.RunSingle(canonical.Program!, new Dictionary<string, object?>(), seed);
                Assert.Equal(reference.Value, actual.Value);
                Assert.Equal(JsonSerializer.Serialize(reference.State.OrderBy(pair => pair.Key), JsonOptions.Default),
                    JsonSerializer.Serialize(actual.State.OrderBy(pair => pair.Key), JsonOptions.Default));
            }

            Assert.Equal(source, JsonSerializer.Serialize(graph, JsonOptions.Default));
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void ExactClosureMixtureHasTheIndependentThreePointLaw()
    {
        CompileResult compiled = new GraphCompiler().Compile(Graph());
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Dist<BigInteger> classic = ExactInterpreter
            .Evaluate(compiled.ReferenceProgram!, new Dictionary<string, object?>(), StateHasher.CanonicalHash)
            .ValueDistribution();
        StructuredExactResult structured = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        Assert.Equal(new Rational(13, 4), structured.ExpectedWin);
        var expected = new Dictionary<BigInteger, Rational>
        {
            [0] = new(1, 8),
            [2] = new(1, 8),
            [4] = new(3, 4)
        };
        foreach (Dist<BigInteger> law in new[] { classic, structured.Distribution! })
        {
            Assert.Equal(3, law.Count);
            foreach (Dist<BigInteger>.Entry entry in law.Entries)
            {
                Assert.Equal(expected[entry.Value], new Rational(entry.Numerator, law.Denominator));
            }
        }
    }

    [Fact]
    public void OverriddenInputsInvalidateDefaultFactsAndFailBeforeWriting()
    {
        CompileResult compiled = new GraphCompiler().Compile(Graph(false));
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        var plan = (ICompiledSampling<Dictionary<string, object?>, BigInteger>)compiled.Program!;
        ISamplingRunner<Dictionary<string, object?>, BigInteger> runner = plan.CreateRunner(
            new Dictionary<string, object?>
            {
                ["adjacency"] = new[] { 2, 4, 16, 0 }
            });
        var rng = new SeededRandom(42);
        rng.EnableAudit();
        var error = Assert.Throws<ExpressionEvaluationException>(() => runner.Run(rng, default));
        Assert.Equal("EVAL_REACHABLE_DOMAIN", error.Code);
        Assert.Equal(new BigInteger(99), runner.ExportState()["reached"]);
        Assert.Equal(0, rng.AuditSnapshot(0)!.Words);
        Assert.Equal(new BigInteger(4),
            plan.CreateRunner(new Dictionary<string, object?>()).Run(new SeededRandom(42), default));
    }

    [Fact]
    public void JsonRoundTripRetainsTheTypedOperatorAndRejectsMissingBounds()
    {
        var json = JsonSerializer.Serialize<Expression>(R(64), JsonOptions.Default);
        Assert.Equal(64,
            Assert.IsType<ReachableExpr>(JsonSerializer.Deserialize<Expression>(json, JsonOptions.Default))
                .VertexCount);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Expression>(
            "{\"exprType\":\"reachable\",\"adjacency\":{\"exprType\":\"constant\",\"kind\":\"Integer\",\"value\":\"0\"},\"allowed\":{\"exprType\":\"constant\",\"kind\":\"Integer\",\"value\":\"0\"},\"seeds\":{\"exprType\":\"constant\",\"kind\":\"Integer\",\"value\":\"0\"}}",
            JsonOptions.Default));
    }

    private static FoldExpr Relaxation(int count) => new()
    {
        StateKey = "adjacency",
        MaxItems = count,
        ItemName = "pass",
        AccName = "found",
        Init = new BitSetExpr
        {
            Op = BitSetOp.Intersect,
            Args = [S("seeds"), S("allowed")]
        },
        Body = new FoldExpr
        {
            StateKey = "adjacency",
            MaxItems = count,
            ItemName = "neighbors",
            IndexName = "vertex",
            AccName = "next",
            Init = S("found"),
            Body = new IfExpr
            {
                Condition = new CompareExpr
                {
                    Op = CompareOp.Neq,
                    Left = new BitSetExpr
                    {
                        Op = BitSetOp.Intersect,
                        Args =
                        [
                            S("found"), new BitSetExpr
                            {
                                Op = BitSetOp.Shift,
                                Args = [N(1), S("vertex")]
                            }
                        ]
                    },
                    Right = N(0)
                },
                ThenExpr = new BitSetExpr
                {
                    Op = BitSetOp.Union,
                    Args =
                    [
                        S("next"), new BitSetExpr
                        {
                            Op = BitSetOp.Intersect,
                            Args = [S("neighbors"), S("allowed")]
                        }
                    ]
                },
                ElseExpr = S("next")
            }
        }
    };

    [Fact]
    public void RepeatedPredicatesMustRetainTraversalCancellationPoints()
    {
        Expression condition = new CompareExpr
        {
            Op = CompareOp.Neq,
            Left = R(),
            Right = N(0)
        };
        Expression source = new IfExpr
        {
            Condition = condition,
            ThenExpr = new IfExpr
            {
                Condition = condition,
                ThenExpr = N(1),
                ElseExpr = N(2)
            },
            ElseExpr = N(3)
        };
        Expression simplified = ExpressionSimplifier.Simplify(source).Expression;

        int Count(Expression expression)
        {
            return (expression is ReachableExpr ? 1 : 0) + CollectionInputs.Children(expression).Sum(Count);
        }

        Assert.Equal(2, Count(simplified));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(15)]
    [InlineData(25)]
    public void TheAtomMatchesAnEditableCompositionOfExistingFolds(int count)
    {
        var adjacency = Enumerable.Range(0, count).Select(vertex => 1 << ((vertex + 1) % count)).ToArray();
        var domain = (1 << count) - 1;
        foreach (var allowed in new[] { domain, domain & ~(1 << (count / 2)) })
        {
            foreach (var seed in new[] { 1, 1 << (count - 1) })
            {
                var context = new EvalContext
                {
                    State = new Dictionary<string, object?>
                    {
                        ["adjacency"] = adjacency,
                        ["allowed"] = allowed,
                        ["seeds"] = seed
                    }
                };
                Assert.Equal(ExactExpressionEvaluator.Evaluate(Relaxation(count), context),
                    ExactExpressionEvaluator.Evaluate(R(count), context));
                Assert.True(ExpressionCost.WithinBudget(Relaxation(count), out _));
            }
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void EverySamplingTierRetainsCancellationAndTheUnsignedHighBit(SamplingTier tier)
    {
        var layout = new Dictionary<string, int>
        {
            ["adjacency"] = 0,
            ["allowed"] = 1,
            ["seeds"] = 2
        };
        BigInteger[] rows = Enumerable.Repeat(BigInteger.Zero, 64).ToArray();
        rows[0] = BigInteger.One << 63;
        var frame = new SamplingFrame(layout, new SamplingCell[3], new Dictionary<string, object?>
        {
            ["adjacency"] = rows,
            ["allowed"] = new BigInteger(ulong.MaxValue),
            ["seeds"] = 1
        });
        frame.Reset(default);
        Func<SamplingFrame, ExprValue> evaluate = new SamplingExpressions(name => layout[name], tier).Compile(R(64));
        for (var call = 0; call <= SamplingOptions.HotThreshold; call++)
        {
            frame.CancellationToken = new CancellationToken(true);
            Assert.Throws<OperationCanceledException>(() => evaluate(frame));
            frame.CancellationToken = default;
            Assert.Equal((BigInteger.One << 63) + 1, evaluate(frame).AsInteger());
        }
    }

    [Theory]
    [InlineData("reachable-directed-cycle", 4)]
    [InlineData("reachable-restricted-network", 3)]
    public void EditableJsonSubgraphsComposeTheAtomWithoutMechanicDispatch(string name, int expected)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Primitives", name + ".json");
        var graph = JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(path), JsonOptions.Default)!;
        var before = JsonSerializer.Serialize(graph, JsonOptions.Default);
        CompileResult compiled = new GraphCompiler().Compile(graph);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Assert.Equal(expected,
            SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42).Value);
        StructuredExactResult exact = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(exact.Succeeded, exact.DeclinedReason);
        Assert.Equal(new Rational(expected, 1), exact.ExpectedWin);
        Assert.Equal(before, JsonSerializer.Serialize(graph, JsonOptions.Default));
    }
}
