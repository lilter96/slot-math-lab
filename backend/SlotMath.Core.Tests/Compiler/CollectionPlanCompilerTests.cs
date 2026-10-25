using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Compiler;

[Collection("SamplingOptions")]
public sealed class CollectionPlanCompilerTests
{
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
        SourcePort = "state",
        TargetNodeId = b,
        TargetPort = "state"
    };

    private static BinaryExpr B(BinaryOp op, Expression a, Expression b) => new()
    {
        Op = op,
        Left = a,
        Right = b
    };

    private static ProjectExpr Part(Expression record, string key) => new()
    {
        Value = record,
        Path = [key]
    };

    private static CallExpr Index(Expression source, Expression i) => new()
    {
        Function = "index",
        Args = [source, i]
    };

    private static GraphConfig Graph(Dictionary<string, Expression> expressions, Dictionary<string, object?> initial,
        Node[]? prefix = null)
    {
        Node[] nodes =
        [
            .. prefix ?? [], .. expressions.Keys.Select(key => (Node)new ModifyStateNode
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
        ];
        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Nodes = nodes,
            Expressions = expressions,
            InitialState = initial.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)),
            Edges = nodes.Zip(nodes.Skip(1)).Select(pair => Edge(pair.First.Id, pair.Second.Id)).ToArray()
        };
    }

    private static GraphConfig Mixture()
    {
        var flag = new DrawNode
        {
            Id = "flag",
            StateWriteKey = "flag",
            Outputs = Ports(),
            DrawWeights =
            [
                new DrawWeight
                {
                    OutcomeId = "first",
                    Weight = 1,
                    StateValue = new ConstantExpr
                    {
                        Kind = ConstantKind.Boolean,
                        Value = "true"
                    }
                },
                new DrawWeight
                {
                    OutcomeId = "second",
                    Weight = 3,
                    StateValue = new ConstantExpr
                    {
                        Kind = ConstantKind.Boolean,
                        Value = "false"
                    }
                }
            ]
        };
        Expression scale = new IfExpr
        {
            Condition = S("flag"),
            ThenExpr = N(1),
            ElseExpr = N(2)
        };
        return Graph(new Dictionary<string, Expression>
        {
            ["prefix"] = new ScanExpr
            {
                Source = new MapExpr
                {
                    StateKey = "values",
                    MaxItems = 3,
                    ItemName = "v",
                    ItemType = ExprType.Number,
                    Body = B(BinaryOp.Mul, S("v"), scale)
                },
                Mode = ScanMode.Inclusive,
                MaxItems = 3
            },
            ["plan"] = new SegmentedCompactExpr
            {
                Keep = new IfExpr
                {
                    Condition = S("flag"),
                    ThenExpr = S("keepA"),
                    ElseExpr = S("keepB")
                },
                Boundaries = S("boundaries"),
                Packing = SegmentPacking.End,
                MaxItems = 4,
                MaxSegments = 2
            },
            ["gathered"] = new MapExpr
            {
                Source = Part(S("plan"), "sources"),
                MaxItems = 4,
                ItemName = "i",
                ItemType = ExprType.Number,
                Body = new IfExpr
                {
                    Condition = new CompareExpr
                    {
                        Op = CompareOp.Lt,
                        Left = S("i"),
                        Right = N(0)
                    },
                    ThenExpr = N(0),
                    ElseExpr = Index(S("weights"), S("i"))
                }
            },
            ["win"] = B(BinaryOp.Add, Index(S("prefix"), N(2)), new AggregateExpr
            {
                Func = AggregateFunc.Sum,
                StateKey = "gathered",
                MaxItems = 4,
                ItemName = "g",
                ItemType = ExprType.Number,
                ValueExpr = S("g")
            })
        }, new Dictionary<string, object?>
        {
            ["values"] = new[] { 1, 2, 3 },
            ["weights"] = new[] { 1, 2, 4, 8 },
            ["keepA"] = new[] { true, false, true, false },
            ["keepB"] = new[] { false, true, false, true },
            ["boundaries"] = new[] { 0, 2, 4 }
        }, [flag]);
    }

    [Fact]
    public void ExactJointLawRetainsTheSameFlagAcrossScanAndCompaction()
    {
        CompileResult compiled = new GraphCompiler().Compile(Mixture());
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Dist<BigInteger> classic = ExactInterpreter
            .Evaluate(compiled.ReferenceProgram!, new Dictionary<string, object?>(), StateHasher.CanonicalHash)
            .ValueDistribution();
        StructuredExactResult exact = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(exact.Succeeded, exact.DeclinedReason);
        Assert.Equal(new Rational(77, 4), exact.ExpectedWin);
        foreach (Dist<BigInteger> law in new[] { classic, exact.Distribution! })
        {
            Assert.Equal(2, law.Count);
            foreach (Dist<BigInteger>.Entry item in law.Entries)
            {
                Assert.Equal(item.Value == 11 ? new Rational(1, 4) : new Rational(3, 4),
                    new Rational(item.Numerator, law.Denominator));
            }
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void CompleteSeededStateAndFollowingRngWordMatchCanonicalExecution(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            GraphConfig graph = Mixture();
            var before = JsonSerializer.Serialize(graph, JsonOptions.Default);
            CompileResult compiled = new GraphCompiler().Compile(graph);
            CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(graph);
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            Assert.True(reference.IsValid);
            ISamplingRunner<Dictionary<string, object?>, BigInteger> runner =
                ((ICompiledSampling<Dictionary<string, object?>, BigInteger>)compiled.Program!).CreateRunner(
                    new Dictionary<string, object?>());
            AliasMethod alias = WeightSet.FromIntegers([1, 3]).AliasTable;
            for (var seed = 0; seed < 128; seed++)
            {
                var actualRng = new SeededRandom(seed);
                var oracleRng = new SeededRandom(seed);
                actualRng.EnableAudit();
                oracleRng.EnableAudit();
                var expected = alias.Sample(oracleRng) == 0 ? 11 : 22;
                Assert.Equal(expected, runner.Run(actualRng, default));
                Assert.Equal(JsonSerializer.Serialize(oracleRng.AuditSnapshot(64)),
                    JsonSerializer.Serialize(actualRng.AuditSnapshot(64)));
                Assert.Equal(oracleRng.NextUInt64(), actualRng.NextUInt64());
                (BigInteger Value, Dictionary<string, object?> State) actual =
                    SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed);
                (BigInteger Value, Dictionary<string, object?> State) canonical =
                    SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), seed);
                Assert.Equal(canonical.Value, actual.Value);
                Assert.Equal(JsonSerializer.Serialize(canonical.State.OrderBy(p => p.Key), JsonOptions.Default),
                    JsonSerializer.Serialize(actual.State.OrderBy(p => p.Key), JsonOptions.Default));
            }

            Assert.Equal(before, JsonSerializer.Serialize(graph, JsonOptions.Default));
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void PersistentScanSessionsKeepEarlierExportsAndResetTheirTrajectory(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            GraphConfig graph = Graph(new Dictionary<string, Expression>
            {
                ["values"] = new ScanExpr
                {
                    Source = S("values"),
                    Mode = ScanMode.Inclusive,
                    MaxItems = 3
                },
                ["win"] = N(0)
            }, new Dictionary<string, object?>
            {
                ["values"] = new[] { 1, 2, 3 }
            });
            CompileResult compiled = new GraphCompiler().Compile(graph);
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            var program = (ICompiledSampling<Dictionary<string, object?>, BigInteger>)compiled.Program!;
            ISamplingRunner<Dictionary<string, object?>, BigInteger>[] runners = new[]
            {
                program.CreateRunner(new Dictionary<string, object?>(), persistentKeys: ["values"]),
                program.CreateRunner(new Dictionary<string, object?>(), persistentKeys: ["values"])
            };
            var saved = new List<(Dictionary<string, object?> State, BigInteger Hash)>();
            for (var round = 1; round <= 100; round++)
            {
                foreach (ISamplingRunner<Dictionary<string, object?>, BigInteger> runner in runners)
                {
                    runner.Run(new SeededRandom(round), default);
                    Dictionary<string, object?> state = runner.ExportState();
                    Assert.Equal(new BigInteger[] { 1, 2 + round, 3 + 2 * round + round * (round + 1) / 2 },
                        Assert.IsType<object?[]>(state["values"]).Cast<BigInteger>());
                    if (round % 17 == 0)
                    {
                        saved.Add((state, StateHasher.CanonicalHash(state)));
                    }
                }
            }

            foreach (ISamplingRunner<Dictionary<string, object?>, BigInteger> runner in runners)
            {
                runner.ResetTrajectory();
                runner.Run(new SeededRandom(0), default);
                Assert.Equal(new BigInteger[] { 1, 3, 6 },
                    Assert.IsType<object?[]>(runner.ExportState()["values"]).Cast<BigInteger>());
            }

            Assert.All(saved, item => Assert.Equal(item.Hash, StateHasher.CanonicalHash(item.State)));
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void RuntimeOverrideErrorsLeaveThePreviousStoredArrayUntouched()
    {
        GraphConfig graph = Graph(new Dictionary<string, Expression>
        {
            ["prefix"] = new ScanExpr
            {
                Source = S("values"),
                Mode = ScanMode.Exclusive,
                MaxItems = 3
            },
            ["win"] = N(0)
        }, new Dictionary<string, object?>
        {
            ["values"] = new[] { 1, 2, 3 },
            ["prefix"] = new[] { 99 }
        });
        CompileResult compiled = new GraphCompiler().Compile(graph);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        var plan = (ICompiledSampling<Dictionary<string, object?>, BigInteger>)compiled.Program!;
        ISamplingRunner<Dictionary<string, object?>, BigInteger> runner = plan.CreateRunner(
            new Dictionary<string, object?>
            {
                ["values"] = new object[] { 1, ExprValue.Rational(1, 2), 3 }
            });
        var error = Assert.Throws<ExpressionEvaluationException>(() => runner.Run(new SeededRandom(42), default));
        Assert.Equal("EVAL_SCAN_INTEGER", error.Code);
        Assert.Equal(new BigInteger[] { 99 },
            Assert.IsType<object?[]>(runner.ExportState()["prefix"]).Cast<BigInteger>());
    }

    [Fact]
    public void CompactionCarriesDifferentAttributeTypesThroughOneEditableJsonPlan()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Primitives",
            "segmented-attribute-transfer.json");
        var graph = JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(path), JsonOptions.Default)!;
        CompileResult compiled = new GraphCompiler().Compile(graph);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        (BigInteger Value, Dictionary<string, object?> State) result =
            SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42);
        var transferred = Assert.IsAssignableFrom<IDictionary<string, object?>>(result.State["transferred"]);
        Assert.Equal(new object?[] { "new", "A", "C", "new", "E", "F" },
            Assert.IsType<object?[]>(transferred["symbols"]));
        Assert.Equal(
            new object?[]
            {
                BigInteger.Zero, BigInteger.One, new BigInteger(3), BigInteger.Zero, new BigInteger(5),
                new BigInteger(6)
            }, Assert.IsType<object?[]>(transferred["multipliers"]));
        Assert.Equal(new object?[] { false, true, false, false, true, false },
            Assert.IsType<object?[]>(transferred["held"]));
        Assert.Equal(new BigInteger(2), result.Value);
        StructuredExactResult exact = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(exact.Succeeded, exact.DeclinedReason);
        Assert.Equal(new Rational(2, 1), exact.ExpectedWin);
    }
}
