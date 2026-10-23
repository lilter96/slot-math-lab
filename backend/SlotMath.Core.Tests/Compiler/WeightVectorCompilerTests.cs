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
public class WeightVectorCompilerTests
{
    private static Dictionary<string, Port> Ports() => new()
    {
        ["state"] = new Port
        {
            Name = "state",
            Type = PortType.State
        }
    };

    private static FieldAccessExpr Field(string key) => new()
    {
        Target = "state",
        Path = [key]
    };

    private static ConstantExpr Number(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString()
    };

    private static Edge Edge(string source, string target) => new()
    {
        Id = source + "-" + target,
        SourceNodeId = source,
        TargetNodeId = target,
        SourcePort = "state",
        TargetPort = "state"
    };

    private static DrawWeight Outcome(int value, long weight = 0) => new()
    {
        OutcomeId = "id-" + value,
        Weight = weight,
        Value = value,
        StateValue = Number(value)
    };

    private static GraphConfig Graph(bool randomMode = true, bool asset = false)
    {
        DrawWeight[] outcomes = [Outcome(2), Outcome(10)];
        var draw = new DrawNode
        {
            Id = "weighted",
            WeightVectorExpressionId = "weights",
            StateWriteKey = "data",
            Inputs = Ports(),
            Outputs = Ports(),
            DrawWeights = asset ? null : outcomes,
            DistributionId = asset ? "ordered" : null
        };
        Node[] tail =
        [
            draw,
            new ModifyStateNode
            {
                Id = "pay",
                OutputKey = "win",
                ExpressionId = "pay",
                Inputs = Ports(),
                Outputs = Ports()
            },
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
            SchemaVersion = "1.0",
            InitialState = new Dictionary<string, JsonElement>
            {
                ["mode"] = JsonSerializer.SerializeToElement(0),
                ["left"] = JsonSerializer.SerializeToElement(new[] { 1, 1 }),
                ["right"] = JsonSerializer.SerializeToElement(new[] { 1, 3 })
            },
            Distributions = asset
                ? new Dictionary<string, DistributionAsset>
                {
                    ["ordered"] = new()
                    {
                        Outcomes = outcomes.Select(outcome => outcome with
                        {
                            Weight = 1
                        }).ToArray()
                    }
                }
                : null,
            Nodes = randomMode
                ?
                [
                    new DrawNode
                    {
                        Id = "mode",
                        StateWriteKey = "mode",
                        Outputs = Ports(),
                        DrawWeights = [Outcome(0, 1), Outcome(1, 3)]
                    },
                    .. tail
                ]
                : tail,
            Edges = randomMode
                ? [Edge("mode", "weighted"), Edge("weighted", "pay"), Edge("pay", "sink")]
                : [Edge("weighted", "pay"), Edge("pay", "sink")],
            Expressions = new Dictionary<string, Expression>
            {
                ["weights"] = new IfExpr
                {
                    Condition = new CompareExpr
                    {
                        Op = CompareOp.Eq,
                        Left = Field("mode"),
                        Right = Number(0)
                    },
                    ThenExpr = Field("left"),
                    ElseExpr = Field("right")
                },
                ["pay"] = new BinaryExpr
                {
                    Op = BinaryOp.Add,
                    Left = Field("data"),
                    Right =
                        new BinaryExpr
                        {
                            Op = BinaryOp.Mul,
                            Left = Number(100),
                            Right = Field("mode")
                        }
                }
            }
        };
    }

    private static string State(Dictionary<string, object?> state) =>
        JsonSerializer.Serialize(state.OrderBy(pair => pair.Key), JsonOptions.Default);

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Compiled)]
    [InlineData(SamplingTier.Tiered)]
    public void DynamicNumericVectorsRetainStateDependenceAndTheCompleteRandomStream(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            foreach (var asset in new[] { false, true })
            {
                CompileResult compiled = new GraphCompiler().Compile(Graph(asset: asset));
                CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(Graph(asset: asset));
                Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
                Assert.True(reference.IsValid);
                var plan =
                    Assert.IsAssignableFrom<ICompiledSampling<Dictionary<string, object?>, BigInteger>>(
                        compiled.Program);
                ISamplingRunner<Dictionary<string, object?>, BigInteger> runner =
                    plan.CreateRunner(new Dictionary<string, object?>());
                AliasMethod modeAlias = WeightSet.FromIntegers([1, 3]).AliasTable;
                AliasMethod[] aliases =
                    [WeightSet.FromIntegers([1, 1]).AliasTable, WeightSet.FromIntegers([1, 3]).AliasTable];
                for (var seed = -32; seed < 96; seed++)
                {
                    SeededRandom actualRandom = SeededRandom.ForStream(unchecked((ulong)seed), 0);
                    SeededRandom oracle = SeededRandom.ForStream(unchecked((ulong)seed), 0);
                    actualRandom.EnableAudit();
                    oracle.EnableAudit();
                    var mode = modeAlias.Sample(oracle);
                    var data = aliases[mode].Sample(oracle) == 0 ? 2 : 10;
                    BigInteger actual = runner.Run(actualRandom, CancellationToken.None);
                    Assert.Equal(new BigInteger(mode * 100 + data), actual);
                    Assert.Equal(oracle.AuditSnapshot(0), actualRandom.AuditSnapshot(0));
                    Assert.Equal(oracle.NextUInt64(), actualRandom.NextUInt64());
                    (BigInteger Value, Dictionary<string, object?> State) expected =
                        SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), seed);
                    Assert.Equal(expected.Value, actual);
                    Assert.Equal(State(expected.State), State(runner.ExportState()));
                }
            }
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void ExactDynamicVectorLawMatchesFourIndependentMasses()
    {
        CompileResult compiled = new GraphCompiler().Compile(Graph());
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Dist<BigInteger> classic = ExactInterpreter
            .Evaluate(compiled.ReferenceProgram!, new Dictionary<string, object?>(), StateHasher.CanonicalHash)
            .ValueDistribution();
        StructuredExactResult structured = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        Assert.NotNull(structured.Distribution);
        var expected = new Dictionary<BigInteger, Rational>
        {
            [2] = new(1, 8),
            [10] = new(1, 8),
            [102] = new(3, 16),
            [110] = new(9, 16)
        };
        foreach (Dist<BigInteger>? law in new[] { classic, structured.Distribution! })
        {
            Assert.Equal(4, law.Count);
            (BigInteger numerator, BigInteger denominator) = law.ExpectedBigIntegerValue();
            Assert.Equal(new Rational(165, 2), new Rational(numerator, denominator));
            foreach (Dist<BigInteger>.Entry entry in law.Entries)
            {
                Assert.Equal(expected[entry.Value], new Rational(entry.Numerator, law.Denominator));
            }
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Compiled)]
    [InlineData(SamplingTier.Tiered)]
    public void RationalOverridesInvalidateDefaultSpecializationAndKeepAliasParity(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            GraphConfig source = Graph(false);
            source.Expressions!["weights"] = Field("left");
            CompileResult compiled = new GraphCompiler().Compile(source);
            CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(source);
            Assert.True(compiled.IsValid);
            Assert.True(reference.IsValid);
            var plan =
                Assert.IsAssignableFrom<ICompiledSampling<Dictionary<string, object?>, BigInteger>>(compiled.Program);
            var initial = new Dictionary<string, object?>
            {
                ["left"] = new object[] { ExprValue.Rational(1, 2), ExprValue.Rational(1, 3) }
            };
            ISamplingRunner<Dictionary<string, object?>, BigInteger> runner = plan.CreateRunner(initial);
            AliasMethod oracleAlias = WeightSet.FromRationals(new BigInteger[] { 3, 2 }, 6).AliasTable;
            for (var seed = 0; seed < 96; seed++)
            {
                SeededRandom actualRandom = SeededRandom.ForStream((ulong)seed, 0);
                SeededRandom oracle = SeededRandom.ForStream((ulong)seed, 0);
                Assert.Equal(new BigInteger(oracleAlias.Sample(oracle) == 0 ? 2 : 10),
                    runner.Run(actualRandom, CancellationToken.None));
                Assert.Equal(oracle.NextUInt64(), actualRandom.NextUInt64());
                (BigInteger Value, Dictionary<string, object?> State) expected =
                    SampledInterpreter.RunSingle(reference.Program!, initial, seed);
                Assert.Equal(State(expected.State), State(runner.ExportState()));
            }
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Compiled)]
    [InlineData(SamplingTier.Tiered)]
    public void InvalidVectorsFailBeforeRandomnessAndAReusedArrayCannotPoisonTheCache(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            CompileResult compiled = new GraphCompiler().Compile(Graph(false));
            CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(Graph(false));
            Assert.True(compiled.IsValid);
            Assert.True(reference.IsValid);
            var plan =
                Assert.IsAssignableFrom<ICompiledSampling<Dictionary<string, object?>, BigInteger>>(compiled.Program);
            foreach (var invalid in new object[]
                     {
                         new object[] { -1, 2 }, new object[] { 0, 0 }, new object[] { 1 },
                         new object[] { "bad", 1 }, 1
                     })
            {
                var initial = new Dictionary<string, object?>
                {
                    ["left"] = invalid
                };
                ISamplingRunner<Dictionary<string, object?>, BigInteger> runner = plan.CreateRunner(initial);
                var rng = new SeededRandom(42);
                rng.EnableAudit();
                var actual =
                    Assert.Throws<ExpressionEvaluationException>(() => runner.Run(rng, CancellationToken.None));
                var expected = Assert.Throws<ExpressionEvaluationException>(() =>
                    SampledInterpreter.RunSingle(reference.Program!, initial, 42));
                Assert.Equal(expected.Code, actual.Code);
                Assert.Equal(expected.Location, actual.Location);
                Assert.Equal(0, rng.AuditSnapshot(0)!.Words);
                Assert.False(runner.ExportState().ContainsKey("data"));
            }

            object[] reused = [1, 0];
            ISamplingRunner<Dictionary<string, object?>, BigInteger> repeated = plan.CreateRunner(
                new Dictionary<string, object?>
                {
                    ["left"] = reused
                });
            Assert.Equal(new BigInteger(2), repeated.Run(new SeededRandom(42), CancellationToken.None));
            reused[0] = 0;
            reused[1] = 1;
            ISamplingRunner<Dictionary<string, object?>, BigInteger> updated = plan.CreateRunner(
                new Dictionary<string, object?>
                {
                    ["left"] = reused
                });
            Assert.Equal(new BigInteger(10), updated.Run(new SeededRandom(42), CancellationToken.None));
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void AGraphChangingItsWeightsBetweenDrawsNeverUsesThePreviousAliasTable()
    {
        GraphConfig source = Graph(false);
        source.InitialState!["left"] = JsonSerializer.SerializeToElement(new[] { 1, 0 });
        source.InitialState["right"] = JsonSerializer.SerializeToElement(new[] { 0, 1 });
        var draw = (DrawNode)source.Nodes[0];
        source = source with
        {
            Nodes =
            [
                draw with
                {
                    Id = "before",
                    StateWriteKey = "first"
                },
                new ModifyStateNode
                {
                    Id = "change",
                    OutputKey = "left",
                    ExpressionId = "change",
                    Inputs = Ports(),
                    Outputs = Ports()
                },
                .. source.Nodes
            ],
            Edges = [Edge("before", "change"), Edge("change", "weighted"), Edge("weighted", "pay"), Edge("pay", "sink")]
        };
        source.Expressions!["change"] = Field("right");
        source.Expressions["weights"] = Field("left");
        source.Expressions["pay"] = new BinaryExpr
        {
            Op = BinaryOp.Add,
            Left = new BinaryExpr
            {
                Op = BinaryOp.Mul,
                Left = Number(100),
                Right = Field("first")
            },
            Right = Field("data")
        };
        foreach (var optimize in new[] { false, true })
        {
            CompileResult compiled = new GraphCompiler(optimizeSampling: optimize).Compile(source);
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            for (var seed = 0; seed < 32; seed++)
            {
                Assert.Equal(new BigInteger(210),
                    SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed).Value);
            }
        }
    }

    [Fact]
    public void InvalidImmutableWeightsInAnUntakenBranchKeepRuntimeFailureTiming()
    {
        GraphConfig source = Graph(false);
        source.InitialState!["left"] = JsonSerializer.SerializeToElement(new[] { -1, 2 });
        source.InitialState["win"] = JsonSerializer.SerializeToElement(0);
        source.Expressions!["weights"] = Field("left");
        source.Expressions["set-take"] = new ConstantExpr
        {
            Kind = ConstantKind.Boolean,
            Value = "false"
        };
        source.Expressions["take"] = Field("take");
        source = source with
        {
            Nodes =
            [
                new ModifyStateNode
                {
                    Id = "set-take",
                    ExpressionId = "set-take",
                    OutputKey = "take",
                    Outputs = Ports()
                },
                new BranchNode
                {
                    Id = "branch",
                    ConditionId = "take",
                    Inputs = Ports(),
                    Outputs = new Dictionary<string, Port>
                    {
                        ["true"] = new()
                        {
                            Name = "true",
                            Type = PortType.State
                        },
                        ["false"] = new()
                        {
                            Name = "false",
                            Type = PortType.State
                        }
                    }
                },
                .. source.Nodes
            ],
            Edges =
            [
                Edge("set-take", "branch"), Edge("branch", "weighted") with
                {
                    SourcePort = "true"
                },
                Edge("branch", "sink") with
                {
                    SourcePort = "false"
                },
                Edge("weighted", "pay"), Edge("pay", "sink")
            ]
        };
        foreach (var optimize in new[] { false, true })
        {
            CompileResult compiled = new GraphCompiler(optimizeSampling: optimize).Compile(source);
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            Assert.Equal(BigInteger.Zero,
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42).Value);
        }
    }

    [Fact]
    public void DynamicStateAndPayoutShareOneOutcomeInTheExactLaw()
    {
        GraphConfig source = Graph();
        var draw = (DrawNode)source.Nodes[1];
        source.Nodes[1] = draw with
        {
            DrawWeights = draw.DrawWeights!.Select(outcome => outcome with
            {
                Value = outcome.Value * 7
            }).ToArray()
        };
        source = source with
        {
            Nodes =
            [
                source.Nodes[0], source.Nodes[1], new PutStateNode
                {
                    Id = "channel",
                    StateKey = "channel",
                    Inputs = Ports(),
                    Outputs = Ports()
                },
                .. source.Nodes[2..]
            ],
            Edges = [Edge("mode", "weighted"), Edge("weighted", "channel"), Edge("channel", "pay"), Edge("pay", "sink")]
        };
        source.Expressions!["pay"] = new BinaryExpr
        {
            Op = BinaryOp.Mul,
            Left = Field("data"),
            Right = Field("channel")
        };
        CompileResult compiled = new GraphCompiler().Compile(source);
        Assert.True(compiled.IsValid);
        Dist<BigInteger> law = ExactInterpreter
            .Evaluate(compiled.ReferenceProgram!, new Dictionary<string, object?>(), StateHasher.CanonicalHash)
            .ValueDistribution();
        (BigInteger numerator, BigInteger denominator) = law.ExpectedBigIntegerValue();
        Assert.Equal(new Rational(490, 1), new Rational(numerator, denominator));
        StructuredExactResult structured = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        Assert.Equal(new Rational(490, 1), structured.ExpectedWin);
        Assert.NotNull(structured.Distribution);
        Assert.Equal(2, structured.Distribution.Count);
        foreach (Dist<BigInteger>.Entry entry in structured.Distribution.Entries)
        {
            Assert.Equal(entry.Value == 28 ? new Rational(5, 16) : new Rational(11, 16),
                new Rational(entry.Numerator, structured.Distribution.Denominator));
        }
    }

    [Fact]
    public void ReusableBodyNamespacesTheVectorExpression()
    {
        GraphConfig source = Graph(false);
        var draw = (DrawNode)source.Nodes[0];
        source.Nodes[0] = new LibraryNode
        {
            Id = "weighted",
            MechanicName = "body",
            Outputs = Ports()
        };
        source = source with
        {
            Mechanics = new Dictionary<string, CustomMechanic>
            {
                ["body"] = new()
                {
                    Name = "General weighted body",
                    Nodes =
                    [
                        draw with
                        {
                            Id = "inside",
                            WeightVectorExpressionId = "local-weights"
                        }
                    ],
                    Expressions = new Dictionary<string, Expression>
                    {
                        ["local-weights"] = Field("left")
                    }
                }
            }
        };
        CompileResult compiled = new GraphCompiler().Compile(source);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Assert.Equal("compiled-state-plan-v1", compiled.SamplingEngine);
        for (var seed = 0; seed < 16; seed++)
        {
            Assert.Contains(
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed).Value,
                new BigInteger[] { 2, 10 });
        }
    }

    [Fact]
    public void VectorJsonRoundTripPreservesTheSourceAndItsEvaluation()
    {
        GraphConfig source = Graph(asset: true);
        var json = JsonSerializer.Serialize(source, JsonOptions.Default);
        var copy = JsonSerializer.Deserialize<GraphConfig>(json, JsonOptions.Default)!;
        CompileResult compiled = new GraphCompiler().Compile(copy);
        CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(source);
        Assert.True(compiled.IsValid);
        Assert.Equal(json, JsonSerializer.Serialize(copy, JsonOptions.Default));
        for (var seed = 0; seed < 32; seed++)
        {
            Assert.Equal(
                SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), seed).Value,
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed).Value);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("scalar")]
    [InlineData("strings")]
    [InlineData("legacy")]
    [InlineData("family")]
    [InlineData("outcomes")]
    public void VectorDeclarationsAreTypedAndUnambiguous(string kind)
    {
        GraphConfig source = Graph(false);
        var draw = (DrawNode)source.Nodes[0];
        switch (kind)
        {
            case "missing":
                draw = draw with
                {
                    WeightVectorExpressionId = "missing"
                }; break;
            case "scalar": source.Expressions!["weights"] = Number(1); break;
            case "strings": source.InitialState!["left"] = JsonSerializer.SerializeToElement(new[] { "1", "2" }); break;
            case "legacy":
                draw = draw with
                {
                    WeightExpressionId = "legacy"
                }; break;
            case "family":
                draw = draw with
                {
                    DistributionIds = ["a"],
                    DistributionIndexExpressionId = "mode"
                }; break;
            case "outcomes":
                draw = draw with
                {
                    DrawWeights = []
                }; break;
        }

        source.Nodes[0] = draw;
        Assert.False(new GraphCompiler().Compile(source).IsValid);
    }
}
