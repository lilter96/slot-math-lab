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
public sealed class FoldWhileCompilerTests
{
    private static ConstantExpr N(int n) => new()
    {
        Kind = ConstantKind.Integer,
        Value = n.ToString()
    };

    private static ConstantExpr B(bool value) => new()
    {
        Kind = ConstantKind.Boolean,
        Value = value.ToString()
    };

    private static FieldAccessExpr S(string name) => new()
    {
        Target = "state",
        Path = [name]
    };

    private static BinaryExpr Add(Expression a, Expression b) => new()
    {
        Op = BinaryOp.Add,
        Left = a,
        Right = b
    };

    private static Dictionary<string, Port> Ports() => new()
    {
        ["state"] = new Port
        {
            Name = "state",
            Type = PortType.State
        }
    };

    private static Edge Edge(string source, string target) => new()
    {
        Id = source + target,
        SourceNodeId = source,
        SourcePort = "state",
        TargetNodeId = target,
        TargetPort = "state"
    };

    private static FoldExpr Quantify(bool any) => new()
    {
        StateKey = "values",
        MaxItems = 6,
        AccName = "acc",
        ItemName = "entry",
        IndexName = "index",
        ItemType = ExprType.Boolean,
        Init = B(!any),
        WhileCondition = any
            ? new NotExpr
            {
                Expr = S("acc")
            }
            : S("acc"),
        Body = S("entry")
    };

    private static GraphConfig Graph(Dictionary<string, Expression> expressions, Dictionary<string, object?> initial,
        Node[]? prefix = null, Node[]? following = null)
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
            .. following ?? [], new MetricsSinkNode
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
            Edges = nodes.Zip(nodes.Skip(1)).Select(pair => Edge(pair.First.Id, pair.Second.Id)).ToArray(),
            Expressions = expressions,
            InitialState = initial.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value))
        };
    }

    private static GraphConfig Mixture()
    {
        Dictionary<string, Expression> expressions = new()
        {
            ["any"] = Quantify(true),
            ["all"] = Quantify(false),
            ["win"] = Add(new IfExpr
            {
                Condition = S("any"),
                ThenExpr = N(10),
                ElseExpr = N(0)
            }, new IfExpr
            {
                Condition = S("all"),
                ThenExpr = N(1),
                ElseExpr = N(0)
            })
        };
        GraphConfig graph = Graph(expressions, new Dictionary<string, object?>(), [
                new DrawManyNode
                {
                    Id = "flags",
                    OutputKey = "values",
                    Count = 6,
                    DistributionId = "bits",
                    Outputs = Ports()
                }
            ],
            [
                new DrawNode
                {
                    Id = "after",
                    StateWriteKey = "after",
                    Inputs = Ports(),
                    Outputs = Ports(),
                    DrawWeights =
                    [
                        new DrawWeight
                        {
                            OutcomeId = "left",
                            Weight = 2
                        },
                        new DrawWeight
                        {
                            OutcomeId = "right",
                            Weight = 5
                        }
                    ]
                }
            ]);
        return graph with
        {
            Distributions = new Dictionary<string, DistributionAsset>
            {
                ["bits"] = new()
                {
                    Outcomes =
                    [
                        new DrawWeight
                        {
                            OutcomeId = "yes",
                            Weight = 1,
                            StateValue = B(true)
                        },
                        new DrawWeight
                        {
                            OutcomeId = "no",
                            Weight = 3,
                            StateValue = B(false)
                        }
                    ]
                }
            }
        };
    }

    [Fact]
    public void ExactAnyAllJointLawUsesOneSharedIndependentProduct()
    {
        CompileResult compiled = new GraphCompiler().Compile(Mixture());
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Dist<BigInteger> classic = ExactInterpreter
            .Evaluate(compiled.ReferenceProgram!, new Dictionary<string, object?>(), StateHasher.CanonicalHash)
            .ValueDistribution();
        StructuredExactResult exact = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(exact.Succeeded, exact.DeclinedReason);
        Assert.Equal(new Rational(33671, 4096), exact.ExpectedWin);
        var expected = new Dictionary<BigInteger, Rational>
        {
            [0] = new(729, 4096),
            [10] = new(3366, 4096),
            [11] = new(1, 4096)
        };
        foreach (Dist<BigInteger> law in new[] { classic, exact.Distribution! })
        {
            Assert.Equal(3, law.Count);
            foreach (Dist<BigInteger>.Entry entry in law.Entries)
            {
                Assert.Equal(expected[entry.Value], new Rational(entry.Numerator, law.Denominator));
            }
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void EveryTierPreservesCompleteStateAuditAndTheFollowingWord(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            GraphConfig graph = Mixture();
            var source = JsonSerializer.Serialize(graph, JsonOptions.Default);
            CompileResult compiled = new GraphCompiler().Compile(graph),
                reference = new GraphCompiler(optimizeSampling: false).Compile(graph);
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            Assert.True(reference.IsValid);
            ISamplingRunner<Dictionary<string, object?>, BigInteger> runner =
                ((ICompiledSampling<Dictionary<string, object?>, BigInteger>)compiled.Program!).CreateRunner(
                    new Dictionary<string, object?>());
            AliasMethod bits = WeightSet.FromIntegers([1, 3]).AliasTable,
                after = WeightSet.FromIntegers([2, 5]).AliasTable;
            for (var seed = 0; seed < 128; seed++)
            {
                var actualRng = new SeededRandom(seed);
                var oracle = new SeededRandom(seed);
                actualRng.EnableAudit();
                oracle.EnableAudit();
                var flags = Enumerable.Range(0, 6).Select(_ => bits.Sample(oracle) == 0).ToArray();
                var following = after.Sample(oracle) == 0 ? "left" : "right";
                Assert.Equal((flags.Any(value => value) ? 10 : 0) + (flags.All(value => value) ? 1 : 0),
                    runner.Run(actualRng, default));
                Assert.Equal(following, runner.ExportState()["after"]);
                Assert.Equal(JsonSerializer.Serialize(oracle.AuditSnapshot(64)),
                    JsonSerializer.Serialize(actualRng.AuditSnapshot(64)));
                Assert.Equal(oracle.NextUInt64(), actualRng.NextUInt64());
                (BigInteger Value, Dictionary<string, object?> State) actual =
                    SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed);
                (BigInteger Value, Dictionary<string, object?> State) canonical =
                    SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), seed);
                Assert.Equal(canonical.Value, actual.Value);
                Assert.Equal(JsonSerializer.Serialize(canonical.State.OrderBy(pair => pair.Key), JsonOptions.Default),
                    JsonSerializer.Serialize(actual.State.OrderBy(pair => pair.Key), JsonOptions.Default));
            }

            Assert.Equal(source, JsonSerializer.Serialize(graph, JsonOptions.Default));
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    private static GraphConfig ArrayRecurrence() => Graph(new Dictionary<string, Expression>
    {
        ["values"] = new FoldExpr
        {
            StateKey = "steps",
            MaxItems = 3,
            AccName = "acc",
            ItemName = "entry",
            IndexName = "index",
            ItemType = ExprType.Number,
            Init = S("values"),
            WhileCondition = new CompareExpr
            {
                Op = CompareOp.Lt,
                Left = S("index"),
                Right = S("limit")
            },
            Body = new MapExpr
            {
                Source = S("acc"),
                MaxItems = 3,
                ItemName = "v",
                ItemType = ExprType.Number,
                Body = Add(S("v"), N(1))
            }
        },
        ["win"] = N(0)
    }, new Dictionary<string, object?>
    {
        ["values"] = new[] { 1, 2, 3 },
        ["steps"] = new[] { 0, 0, 0 },
        ["limit"] = 2
    });

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void BorrowedAccumulatorArraysAndPersistentExportsRemainIndependent(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            CompileResult compiled = new GraphCompiler().Compile(ArrayRecurrence());
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
                    Assert.Equal(new BigInteger[] { 1 + 2 * round, 2 + 2 * round, 3 + 2 * round },
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
                Assert.Equal(new BigInteger[] { 3, 4, 5 },
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
    public void EditableLibraryParametersAreRewrittenInsideTheWhileCondition()
    {
        var initial = new Dictionary<string, object?>
        {
            ["values"] = new[] { 1, 2, 3, 4 }
        };
        GraphConfig graph = Graph(new Dictionary<string, Expression>(), initial,
        [
            new LibraryNode
            {
                Id = "use",
                MechanicName = "reduction",
                Parameters = new Dictionary<string, string>
                {
                    ["limit"] = "3"
                },
                Outputs = Ports()
            }
        ]);
        graph = graph with
        {
            Mechanics = new Dictionary<string, CustomMechanic>
            {
                ["reduction"] = new()
                {
                    Name = "reduction",
                    Nodes =
                    [
                        new ModifyStateNode
                        {
                            Id = "write",
                            OutputKey = "win",
                            ExpressionId = "reduce",
                            Inputs = Ports(),
                            Outputs = Ports()
                        }
                    ],
                    Expressions = new Dictionary<string, Expression>
                    {
                        ["reduce"] = new FoldExpr
                        {
                            StateKey = "values",
                            MaxItems = 4,
                            AccName = "acc",
                            ItemName = "entry",
                            ItemType = ExprType.Number,
                            Init = N(0),
                            Body = Add(S("acc"), S("entry")),
                            WhileCondition = new CompareExpr
                            {
                                Op = CompareOp.Lt,
                                Left = S("acc"),
                                Right = new FieldAccessExpr
                                {
                                    Target = "state",
                                    Path = ["expressions", "limit"]
                                }
                            }
                        }
                    }
                }
            }
        };
        CompileResult compiled = new GraphCompiler().Compile(graph);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Assert.Equal(3, SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 7).Value);
        StructuredExactResult exact = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(exact.Succeeded, exact.DeclinedReason);
        Assert.Equal(new Rational(3, 1), exact.ExpectedWin);
    }

    [Fact]
    public void RuntimeOverridesInvalidateADefaultGuardAndStopBeforeTheBody()
    {
        CompileResult compiled = new GraphCompiler().Compile(ArrayRecurrence());
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        ISamplingRunner<Dictionary<string, object?>, BigInteger> runner =
            ((ICompiledSampling<Dictionary<string, object?>, BigInteger>)compiled.Program!).CreateRunner(
                new Dictionary<string, object?>
                {
                    ["limit"] = 0
                });
        runner.Run(new SeededRandom(0), default);
        Assert.Equal(new BigInteger[] { 1, 2, 3 },
            Assert.IsType<object?[]>(runner.ExportState()["values"]).Cast<BigInteger>());
    }
}
