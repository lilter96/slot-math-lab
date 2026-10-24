using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Compiler;

[Collection("SamplingOptions")]
public sealed class PrecomputationPipelineTests
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

    private static Expression Op(BinaryOp op, Expression a, Expression b) => new BinaryExpr
    {
        Op = op,
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

    private static Edge Edge(string a, string b) => new()
    {
        Id = a + b,
        SourceNodeId = a,
        SourcePort = "state",
        TargetNodeId = b,
        TargetPort = "state"
    };

    private static Expression Body()
    {
        Expression value = N(0);
        for (var j = 1; j <= 12; j++)
        {
            value = Op(BinaryOp.Add, value,
                Op(BinaryOp.Mul, Op(BinaryOp.Add, S("x"), N(j)), Op(BinaryOp.Add, S("y"), N(j))));
        }

        return Op(BinaryOp.Mul, value, S("factor")) with
        {
            Precompute = new PrecomputationHint
            {
                ExpectedUses = 1_000_000,
                Domains =
                [
                    new PrecomputationDomain
                    {
                        Name = "x",
                        Values = Enumerable.Range(0, 4).Select(v => new PrecomputationValue
                        {
                            Kind = ConstantKind.Integer,
                            Value = v.ToString()
                        }).ToArray()
                    },
                    new PrecomputationDomain
                    {
                        Name = "y",
                        Values = Enumerable.Range(0, 4).Select(v => new PrecomputationValue
                        {
                            Kind = ConstantKind.Integer,
                            Value = v.ToString()
                        }).ToArray()
                    }
                ]
            }
        };
    }

    private static GraphConfig Graph()
    {
        var x = new DrawNode
        {
            Id = "x",
            StateWriteKey = "x",
            Outputs = Ports(),
            DrawWeights = Enumerable.Range(0, 4).Select(i => new DrawWeight
            {
                OutcomeId = "id" + i,
                Weight = i + 1,
                StateValue = N(i)
            }).ToArray()
        };
        DrawNode y = x with
        {
            Id = "y",
            StateWriteKey = "y",
            Inputs = Ports()
        };
        Node[] nodes =
        [
            x, y, new ModifyStateNode
            {
                Id = "compute",
                OutputKey = "computed",
                ExpressionId = "compute",
                Inputs = Ports(),
                Outputs = Ports()
            },
            new ModifyStateNode
            {
                Id = "win",
                OutputKey = "win",
                ExpressionId = "win",
                Inputs = Ports(),
                Outputs = Ports()
            },
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
            },
            new MetricsSinkNode
            {
                Id = "sink",
                WinStateKey = "win",
                WinCap = 100000,
                Inputs = Ports()
            }
        ];
        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Nodes = nodes,
            Edges = nodes.Zip(nodes.Skip(1)).Select(pair => Edge(pair.First.Id, pair.Second.Id)).ToArray(),
            Expressions = new Dictionary<string, Expression>
            {
                ["compute"] = Body(),
                ["win"] = S("computed")
            },
            InitialState = new Dictionary<string, JsonElement>
            {
                ["factor"] = JsonSerializer.SerializeToElement(3)
            }
        };
    }

    private static int Expected(int x, int y, int factor = 3) =>
        Enumerable.Range(1, 12).Sum(j => (x + j) * (y + j)) * factor;

    [Fact]
    public void CompleteExactLawAndEditableSourceRetainTheOriginalFunction()
    {
        GraphConfig graph = Graph();
        var original = JsonSerializer.Serialize(graph, JsonOptions.Default);
        CompileResult compiled = new GraphCompiler().Compile(graph);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Assert.Equal(1, Assert.IsType<SamplingPlanSlot>(compiled.Program).Precomputations.BuiltTables);
        var expected = new Dictionary<BigInteger, Rational>();
        for (var x = 0; x < 4; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                BigInteger win = Expected(x, y);
                Rational mass = new((x + 1) * (y + 1), 100);
                expected[win] = expected.GetValueOrDefault(win, new Rational(0, 1)) + mass;
            }
        }

        Dist<BigInteger> classic = ExactInterpreter
            .Evaluate(compiled.ReferenceProgram!, new Dictionary<string, object?>(), StateHasher.CanonicalHash)
            .ValueDistribution();
        StructuredExactResult exact = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(exact.Succeeded, exact.DeclinedReason);
        foreach (Dist<BigInteger> law in new[] { classic, exact.Distribution! })
        {
            Assert.Equal(expected.Count, law.Count);
            foreach (Dist<BigInteger>.Entry entry in law.Entries)
            {
                Assert.Equal(expected[entry.Value], new Rational(entry.Numerator, law.Denominator));
            }
        }

        Assert.Equal(original, JsonSerializer.Serialize(graph, JsonOptions.Default));
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void OptimizedLookupPreservesStateRngAuditsAndTheFollowingWord(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            GraphConfig graph = Graph();
            CompileResult compiled = new GraphCompiler().Compile(graph),
                reference = new GraphCompiler(optimizeSampling: false).Compile(graph);
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            Assert.True(reference.IsValid);
            ISamplingRunner<Dictionary<string, object?>, BigInteger> runner =
                ((ICompiledSampling<Dictionary<string, object?>, BigInteger>)compiled.Program!).CreateRunner(
                    new Dictionary<string, object?>());
            AliasMethod inputs = WeightSet.FromIntegers([1, 2, 3, 4]).AliasTable,
                following = WeightSet.FromIntegers([2, 5]).AliasTable;
            for (var seed = 0; seed < 128; seed++)
            {
                var actualRng = new SeededRandom(seed);
                var oracle = new SeededRandom(seed);
                actualRng.EnableAudit();
                oracle.EnableAudit();
                int x = inputs.Sample(oracle), y = inputs.Sample(oracle);
                var after = following.Sample(oracle) == 0 ? "left" : "right";
                Assert.Equal(Expected(x, y), runner.Run(actualRng, default));
                Assert.Equal(after, runner.ExportState()["after"]);
                Assert.Equal(JsonSerializer.Serialize(oracle.AuditSnapshot(64)),
                    JsonSerializer.Serialize(actualRng.AuditSnapshot(64)));
                Assert.Equal(oracle.NextUInt64(), actualRng.NextUInt64());
                (BigInteger Value, Dictionary<string, object?> State) actual =
                    SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed);
                (BigInteger Value, Dictionary<string, object?> State) canonical =
                    SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), seed);
                Assert.Equal(canonical.Value, actual.Value);
                Assert.Equal(StateHasher.CanonicalHash(canonical.State), StateHasher.CanonicalHash(actual.State));
            }
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void CapturedParameterOverridesNeverReuseAStaleTable()
    {
        CompileResult compiled = new GraphCompiler().Compile(Graph());
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        var program = (ICompiledSampling<Dictionary<string, object?>, BigInteger>)compiled.Program!;
        ISamplingRunner<Dictionary<string, object?>, BigInteger> runner = program.CreateRunner(
            new Dictionary<string, object?>
            {
                ["factor"] = 7
            });
        var rng = new SeededRandom(17);
        var oracle = new SeededRandom(17);
        AliasMethod inputs = WeightSet.FromIntegers([1, 2, 3, 4]).AliasTable;
        int x = inputs.Sample(oracle), y = inputs.Sample(oracle);
        Assert.Equal(Expected(x, y, 7), runner.Run(rng, default));
    }

    [Fact]
    public void LibraryInliningRewritesCompilerOwnedDomainNamesAndKeepsLocalNames()
    {
        Expression expression = Body() with
        {
            Precompute = Body().Precompute! with
            {
                Domains =
                [
                    new PrecomputationDomain
                    {
                        Name = "__iter_loop__",
                        Values =
                        [
                            new PrecomputationValue
                            {
                                Value = "0"
                            }
                        ]
                    },
                    new PrecomputationDomain
                    {
                        Name = "local",
                        Scope = PrecomputationScope.Local,
                        Values =
                        [
                            new PrecomputationValue
                            {
                                Value = "1"
                            }
                        ]
                    }
                ]
            }
        };
        var library = new LibraryNode
        {
            Id = "use",
            MechanicName = "annotated",
            Outputs = Ports()
        };
        var graph = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Nodes = [library],
            Mechanics = new Dictionary<string, CustomMechanic>
            {
                ["annotated"] = new()
                {
                    Name = "annotated",
                    Nodes =
                    [
                        new ModifyStateNode
                        {
                            Id = "write",
                            ExpressionId = "body",
                            OutputKey = "computed",
                            Inputs = Ports(),
                            Outputs = Ports()
                        }
                    ],
                    Expressions = new Dictionary<string, Expression>
                    {
                        ["body"] = expression
                    }
                }
            }
        };
        (GraphConfig Config, IReadOnlyList<CompileError> Errors) inlined = SubgraphInliner.Inline(graph);
        Assert.Empty(inlined.Errors);
        PrecomputationHint hint = inlined.Config.Expressions!["use/body"].Precompute!;
        Assert.Equal("__iter_use/loop__", hint.Domains[0].Name);
        Assert.Equal("local", hint.Domains[1].Name);
    }
}
