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
public sealed class IndexedArrayCompilerTests
{
    private static ConstantExpr N(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString()
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

    private static CallExpr At(Expression source, int index) => new()
    {
        Function = "index",
        Args = [source, N(index)]
    };

    private static ScatterExpr Reduce(Expression indices, Expression values) => new()
    {
        Target = S("target"),
        Indices = indices,
        Values = values,
        Mode = ScatterMode.Sum,
        MaxItems = 2,
        MaxUpdates = 3
    };

    private static PartitionIndicesExpr Group(Expression labels) => new()
    {
        Labels = labels,
        BucketCount = 2,
        MaxItems = 3
    };

    private static GraphConfig Graph(Dictionary<string, Expression> expressions, Dictionary<string, object?> initial,
        Node[]? prefix = null, Node[]? suffix = null)
    {
        Node[] nodes =
        [
            .. prefix ?? [], .. expressions.Keys.Select(key => (Node)new ModifyStateNode
            {
                Id = key,
                OutputKey = key,
                ExpressionId = key,
                Inputs = Ports(),
                Outputs = Ports()
            }),
            .. suffix ?? [], new MetricsSinkNode
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
        Expression indices = new IfExpr
        {
            Condition = S("flag"),
            ThenExpr = S("a"),
            ElseExpr = S("b")
        };
        Expression values = new IfExpr
        {
            Condition = S("flag"),
            ThenExpr = S("va"),
            ElseExpr = S("vb")
        };
        var expressions = new Dictionary<string, Expression>
        {
            ["reduced"] = Reduce(indices, values),
            ["groups"] = Group(indices),
            ["win"] = new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = At(S("reduced"), 0),
                Right = new CallExpr
                {
                    Function = "length",
                    Args = [At(S("groups"), 0)]
                }
            }
        };
        var initial = new Dictionary<string, object?>
        {
            ["target"] = new[] { 0, 0 },
            ["a"] = new[] { 0, 1, 0 },
            ["b"] = new[] { 1, 1, 0 },
            ["va"] = new[] { 1, 2, 4 },
            ["vb"] = new[] { 10, 20, 40 }
        };
        var flag = new DrawNode
        {
            Id = "flag",
            StateWriteKey = "flag",
            Outputs = Ports(),
            DrawWeights =
            [
                new DrawWeight
                {
                    OutcomeId = "yes",
                    Weight = 1,
                    StateValue = new ConstantExpr
                    {
                        Kind = ConstantKind.Boolean,
                        Value = "true"
                    }
                },
                new DrawWeight
                {
                    OutcomeId = "no",
                    Weight = 3,
                    StateValue = new ConstantExpr
                    {
                        Kind = ConstantKind.Boolean,
                        Value = "false"
                    }
                }
            ]
        };
        var following = new DrawNode
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
        };
        return Graph(expressions, initial, [flag], [following]);
    }

    [Fact]
    public void ExactPartitionAndReductionShareTheirFlagRatherThanMultiplyingMarginals()
    {
        CompileResult compiled = new GraphCompiler().Compile(Mixture());
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Dist<BigInteger> classic = ExactInterpreter
            .Evaluate(compiled.ReferenceProgram!, new Dictionary<string, object?>(), StateHasher.CanonicalHash)
            .ValueDistribution();
        StructuredExactResult exact = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(exact.Succeeded, exact.DeclinedReason);
        Assert.Equal(new Rational(65, 2), exact.ExpectedWin);
        foreach (Dist<BigInteger> law in new[] { classic, exact.Distribution! })
        {
            Assert.Equal(2, law.Count);
            foreach (Dist<BigInteger>.Entry entry in law.Entries)
            {
                Assert.Equal(entry.Value == 7 ? new Rational(1, 4) : new Rational(3, 4),
                    new Rational(entry.Numerator, law.Denominator));
            }
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void EveryTierPreservesFullStateAuditsAndTheFollowingWord(SamplingTier tier)
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
            AliasMethod flag = WeightSet.FromIntegers([1, 3]).AliasTable,
                following = WeightSet.FromIntegers([2, 5]).AliasTable;
            for (var seed = 0; seed < 128; seed++)
            {
                var actualRng = new SeededRandom(seed);
                var oracle = new SeededRandom(seed);
                actualRng.EnableAudit();
                oracle.EnableAudit();
                var first = flag.Sample(oracle) == 0;
                var after = following.Sample(oracle) == 0 ? "left" : "right";
                Assert.Equal(first ? 7 : 41, runner.Run(actualRng, default));
                Assert.Equal(after, runner.ExportState()["after"]);
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

    private static GraphConfig Accumulating()
    {
        var expressions = new Dictionary<string, Expression>
        {
            ["target"] = Reduce(S("indices"), S("updates")),
            ["groups"] = Group(S("indices")),
            ["win"] = N(0)
        };
        var initial = new Dictionary<string, object?>
        {
            ["target"] = new[] { 1, 2 },
            ["indices"] = new[] { 0, 1, 0 },
            ["updates"] = new[] { 1, 2, 4 }
        };
        return Graph(expressions, initial);
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void PersistentTargetsAndNestedGroupsKeepEarlierExportsAcrossSessions(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            CompileResult compiled = new GraphCompiler().Compile(Accumulating());
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            var program = (ICompiledSampling<Dictionary<string, object?>, BigInteger>)compiled.Program!;
            ISamplingRunner<Dictionary<string, object?>, BigInteger>[] runners = new[]
            {
                program.CreateRunner(new Dictionary<string, object?>(), persistentKeys: ["target"]),
                program.CreateRunner(new Dictionary<string, object?>(), persistentKeys: ["target"])
            };
            var saved = new List<(Dictionary<string, object?> State, BigInteger Hash)>();
            for (var round = 1; round <= 100; round++)
            {
                foreach (ISamplingRunner<Dictionary<string, object?>, BigInteger> runner in runners)
                {
                    runner.Run(new SeededRandom(round), default);
                    Dictionary<string, object?> state = runner.ExportState();
                    Assert.Equal(new BigInteger[] { 1 + 5 * round, 2 + 2 * round },
                        Assert.IsType<object?[]>(state["target"]).Cast<BigInteger>());
                    var groups = Assert.IsType<object?[]>(state["groups"]);
                    Assert.Equal(new BigInteger[] { 0, 2 }, Assert.IsType<object?[]>(groups[0]).Cast<BigInteger>());
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
                Assert.Equal(new BigInteger[] { 6, 4 },
                    Assert.IsType<object?[]>(runner.ExportState()["target"]).Cast<BigInteger>());
            }

            Assert.All(saved, item => Assert.Equal(item.Hash, StateHasher.CanonicalHash(item.State)));
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void DuplicateOverrideFailsBeforePublishingOrMutatingStoredArrays()
    {
        var expressions = new Dictionary<string, Expression>
        {
            ["result"] = Reduce(S("indices"), S("updates")) with
            {
                Mode = ScatterMode.RejectDuplicates
            },
            ["win"] = N(0)
        };
        var initial = new Dictionary<string, object?>
        {
            ["target"] = new[] { 1, 2 },
            ["indices"] = new[] { 0, 1 },
            ["updates"] = new[] { 10, 20 },
            ["result"] = new[] { 99 }
        };
        CompileResult compiled = new GraphCompiler().Compile(Graph(expressions, initial));
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        ISamplingRunner<Dictionary<string, object?>, BigInteger> runner =
            ((ICompiledSampling<Dictionary<string, object?>, BigInteger>)compiled.Program!).CreateRunner(
                new Dictionary<string, object?>
                {
                    ["indices"] = new[] { 0, 0 }
                });
        var error = Assert.Throws<ExpressionEvaluationException>(() => runner.Run(new SeededRandom(0), default));
        Assert.Equal("EVAL_SCATTER_DUPLICATE", error.Code);
        Assert.Equal(new BigInteger[] { 1, 2 },
            Assert.IsType<object?[]>(runner.ExportState()["target"]).Cast<BigInteger>());
        Assert.Equal(new BigInteger[] { 99 },
            Assert.IsType<object?[]>(runner.ExportState()["result"]).Cast<BigInteger>());
    }

    [Fact]
    public void BooleanStringRecordAndArrayPayloadsKeepTheirCompiledShape()
    {
        object[][] targets =
        [
            [true, false], ["A", "B"], [
                new
                {
                    id = "A",
                    n = 1
                },
                new
                {
                    id = "B",
                    n = 2
                }
            ],
            [new[] { 1 }, new[] { 2, 3 }]
        ];
        foreach (var target in targets)
        {
            var expressions = new Dictionary<string, Expression>
            {
                ["result"] = Reduce(S("indices"), S("updates")) with
                {
                    Mode = ScatterMode.LastWriteWins
                },
                ["win"] = N(0)
            };
            var initial = new Dictionary<string, object?>
            {
                ["target"] = target,
                ["indices"] = new[] { 1 },
                ["updates"] = new[] { target[0] }
            };
            GraphConfig graph = Graph(expressions, initial);
            CompileResult compiled = new GraphCompiler().Compile(graph),
                reference = new GraphCompiler(optimizeSampling: false).Compile(graph);
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            Assert.True(reference.IsValid);
            (BigInteger Value, Dictionary<string, object?> State) actual =
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 0);
            (BigInteger Value, Dictionary<string, object?> State) canonical =
                SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), 0);
            Assert.Equal(JsonSerializer.Serialize(canonical.State.OrderBy(pair => pair.Key), JsonOptions.Default),
                JsonSerializer.Serialize(actual.State.OrderBy(pair => pair.Key), JsonOptions.Default));
        }
    }

    [Fact]
    public void EditableLibraryExpressionReferencesAreRewrittenInEveryOperand()
    {
        Expression Ref(string name)
        {
            return new FieldAccessExpr
            {
                Target = "state",
                Path = ["expressions", name]
            };
        }

        var initial = new Dictionary<string, object?>
        {
            ["target"] = new[] { 0, 0 },
            ["indices"] = new[] { 0, 1, 0 },
            ["updates"] = new[] { 1, 2, 4 }
        };
        GraphConfig graph = Graph(new Dictionary<string, Expression>(), initial, [
            new LibraryNode
            {
                Id = "use",
                MechanicName = "editable-indexed",
                Outputs = Ports()
            }
        ]);
        var nodes = new Node[]
        {
            new ModifyStateNode
            {
                Id = "reduce",
                OutputKey = "reduced",
                ExpressionId = "reduce",
                Inputs = Ports(),
                Outputs = Ports()
            },
            new ModifyStateNode
            {
                Id = "group",
                OutputKey = "groups",
                ExpressionId = "group",
                Inputs = Ports(),
                Outputs = Ports()
            },
            new ModifyStateNode
            {
                Id = "pay",
                OutputKey = "win",
                ExpressionId = "pay",
                Inputs = Ports(),
                Outputs = Ports()
            }
        };
        var expressions = new Dictionary<string, Expression>
        {
            ["seed"] = S("target"),
            ["keys"] = S("indices"),
            ["updates"] = S("updates"),
            ["reduce"] = Reduce(Ref("keys"), Ref("updates")) with
            {
                Target = Ref("seed")
            },
            ["group"] = Group(Ref("keys")),
            ["pay"] = new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = At(S("reduced"), 0),
                Right = new CallExpr
                {
                    Function = "length",
                    Args = [At(S("groups"), 0)]
                }
            }
        };
        graph = graph with
        {
            Mechanics = new Dictionary<string, CustomMechanic>
            {
                ["editable-indexed"] = new()
                {
                    Name = "editable-indexed",
                    Nodes = nodes,
                    Edges = [Edge("reduce", "group"), Edge("group", "pay")],
                    Expressions = expressions
                }
            }
        };
        graph = JsonSerializer.Deserialize<GraphConfig>(JsonSerializer.Serialize(graph, JsonOptions.Default),
            JsonOptions.Default)!;
        CompileResult compiled = new GraphCompiler().Compile(graph);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Assert.Equal(7, SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 0).Value);
        StructuredExactResult exact = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(exact.Succeeded, exact.DeclinedReason);
        Assert.Equal(new Rational(7, 1), exact.ExpectedWin);
    }
}
