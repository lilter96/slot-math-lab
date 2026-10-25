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
public class CategoricalFamilyTests
{
    private static Dictionary<string, Port> Ports() => new()
    {
        ["state"] = new Port
        {
            Name = "state",
            Type = PortType.State
        }
    };

    private static ConstantExpr Number(string value) => new()
    {
        Kind = value.Contains('/') ? ConstantKind.Rational : ConstantKind.Integer,
        Value = value
    };

    private static FieldAccessExpr Field(string key) => new()
    {
        Target = "state",
        Path = [key]
    };

    private static Edge Edge(string source, string target) => new()
    {
        Id = source + "-" + target,
        SourceNodeId = source,
        TargetNodeId = target,
        SourcePort = "state",
        TargetPort = "state"
    };

    private static DrawWeight Outcome(string value, long weight, long payout = 0) => new()
    {
        OutcomeId = "id-" + value,
        StateValue = Number(value),
        Weight = weight,
        Value = payout
    };

    private static GraphConfig Family(bool randomMode = true)
    {
        var family = new DrawNode
        {
            Id = "family",
            DistributionIds = ["left", "right"],
            DistributionIndexExpressionId = "family-index",
            StateWriteKey = "data",
            Inputs = Ports(),
            Outputs = Ports()
        };
        Node[] tail =
        [
            family,
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
                ["mode"] = JsonSerializer.SerializeToElement(0)
            },
            Distributions = new Dictionary<string, DistributionAsset>
            {
                ["left"] = new()
                {
                    Outcomes = [Outcome("2", 1), Outcome("4", 1)]
                },
                ["right"] = new()
                {
                    Outcomes = [Outcome("10", 1), Outcome("20", 2), Outcome("30", 1)]
                }
            },
            Nodes = randomMode
                ?
                [
                    new DrawNode
                    {
                        Id = "mode",
                        StateWriteKey = "mode",
                        DrawWeights = [Outcome("0", 1), Outcome("1", 3)],
                        Outputs = Ports()
                    },
                    .. tail
                ]
                : tail,
            Edges = randomMode
                ? [Edge("mode", "family"), Edge("family", "pay"), Edge("pay", "sink")]
                : [Edge("family", "pay"), Edge("pay", "sink")],
            Expressions = new Dictionary<string, Expression>
            {
                ["family-index"] = Field("mode"),
                ["pay"] = new BinaryExpr
                {
                    Op = BinaryOp.Add,
                    Left = Field("data"),
                    Right = new BinaryExpr
                    {
                        Op = BinaryOp.Mul,
                        Left = Number("100"),
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
    public void TypedDataNeverUsesThePayoutScale(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            ConstantExpr[] literals =
            [
                Number("17"), Number("18446744073709551616"), Number("7/3"), new()
                {
                    Kind = ConstantKind.Boolean,
                    Value = "true"
                },
                new()
                {
                    Kind = ConstantKind.String,
                    Value = "data label"
                }
            ];
            foreach (ConstantExpr literal in literals)
            {
                var source = new GraphConfig
                {
                    SchemaVersion = "1.0",
                    Nodes =
                    [
                        new DrawNode
                        {
                            Id = "draw",
                            StateWriteKey = "data",
                            Outputs = Ports(),
                            DrawWeights =
                            [
                                new DrawWeight
                                {
                                    OutcomeId = "metadata",
                                    Weight = 1,
                                    Value = 3,
                                    StateValue = literal
                                }
                            ]
                        },
                        new MetricsSinkNode
                        {
                            Id = "sink",
                            WinCap = 100,
                            Inputs = Ports(),
                            Settlement = new MonetarySettlement
                            {
                                Quantum = "0.1"
                            }
                        }
                    ],
                    Edges = [Edge("draw", "sink")],
                    ReelSets =
                    [
                        new ReelSet
                        {
                            Id = "unused",
                            Name = "Unrelated data",
                            StripIds = []
                        }
                    ]
                };
                CompileResult compiled = new GraphCompiler().Compile(source);
                CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(source);
                Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
                Assert.True(reference.IsValid, string.Join(';', reference.Errors));
                Assert.Equal(new BigInteger(10), compiled.WinScale);
                (BigInteger Value, Dictionary<string, object?> State) expected =
                    SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), 91);
                (BigInteger Value, Dictionary<string, object?> State) actual =
                    SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 91);
                Assert.Equal(new BigInteger(30), actual.Value);
                Assert.Equal(State(expected.State), State(actual.State));
                ExprValue expectedData = ExactExpressionEvaluator.Evaluate(literal, EvalContext.Empty);
                Assert.Equal(expectedData, StateValues.Read(actual.State, ["data"]));
                Assert.DoesNotContain(compiled.MeasurementSchema!.Fields,
                    field => field.Name is "board" or "rows" or "cols");
                Assert.Equal(expectedData.Kind.ToString(),
                    compiled.MeasurementSchema.Fields.Single(field => field.Name == "data").Type);
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
    public void StateDependentFamiliesKeepTheJointStateAndCompleteRngTranscript(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            CompileResult compiled = new GraphCompiler().Compile(Family());
            CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(Family());
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            Assert.True(reference.IsValid, string.Join(';', reference.Errors));
            var plan =
                Assert.IsAssignableFrom<ICompiledSampling<Dictionary<string, object?>, BigInteger>>(compiled.Program);
            ISamplingRunner<Dictionary<string, object?>, BigInteger> runner =
                plan.CreateRunner(new Dictionary<string, object?>());
            AliasMethod modeAlias = WeightSet.FromIntegers([1, 3]).AliasTable;
            AliasMethod[] aliases =
                [WeightSet.FromIntegers([1, 1]).AliasTable, WeightSet.FromIntegers([1, 2, 1]).AliasTable];
            int[][] values = [[2, 4], [10, 20, 30]];
            for (var seed = -64; seed < 128; seed++)
            {
                SeededRandom actualRandom = SeededRandom.ForStream(unchecked((ulong)seed), 0);
                SeededRandom oracle = SeededRandom.ForStream(unchecked((ulong)seed), 0);
                actualRandom.EnableAudit();
                oracle.EnableAudit();
                var mode = modeAlias.Sample(oracle);
                var data = values[mode][aliases[mode].Sample(oracle)];
                BigInteger actual = runner.Run(actualRandom, CancellationToken.None);
                Assert.Equal(new BigInteger(100 * mode + data), actual);
                Assert.Equal(oracle.AuditSnapshot(0), actualRandom.AuditSnapshot(0));
                Assert.Equal(oracle.NextUInt64(), actualRandom.NextUInt64());
                (BigInteger Value, Dictionary<string, object?> State) expected =
                    SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), seed);
                Assert.Equal(expected.Value, actual);
                Assert.Equal(State(expected.State), State(runner.ExportState()));
            }
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void ExactFamilyMixtureRetainsCorrelationAndTheIndependentFivePointLaw()
    {
        CompileResult compiled = new GraphCompiler().Compile(Family());
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Dist<BigInteger> classic = ExactInterpreter.Evaluate(compiled.ReferenceProgram!,
            new Dictionary<string, object?>(),
            StateHasher.CanonicalHash).ValueDistribution();
        (BigInteger numerator, BigInteger denominator) = classic.ExpectedBigIntegerValue();
        Assert.Equal(new Rational(363, 4), new Rational(numerator, denominator));
        StructuredExactResult structured = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        Assert.Equal(new Rational(363, 4), structured.ExpectedWin);
        Assert.NotNull(structured.Distribution);
        var expected = new Dictionary<BigInteger, Rational>
        {
            [2] = new(1, 8),
            [4] = new(1, 8),
            [110] = new(3, 16),
            [120] = new(3, 8),
            [130] = new(3, 16)
        };
        foreach (Dist<BigInteger>? law in new[] { classic, structured.Distribution! })
        {
            Assert.Equal(5, law.Count);
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
    public void InvalidExternalSelectorsFailBeforeAnyRandomWordIsConsumed(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            CompileResult compiled = new GraphCompiler().Compile(Family(false));
            CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(Family(false));
            Assert.True(compiled.IsValid);
            Assert.True(reference.IsValid);
            var plan =
                Assert.IsAssignableFrom<ICompiledSampling<Dictionary<string, object?>, BigInteger>>(compiled.Program);
            foreach (var invalid in new object[] { -1, 2, "wrong type", ExprValue.Rational(1, 2) })
            {
                var state = new Dictionary<string, object?>
                {
                    ["mode"] = invalid
                };
                var rng = new SeededRandom(42);
                rng.EnableAudit();
                ISamplingRunner<Dictionary<string, object?>, BigInteger> runner = plan.CreateRunner(state);
                var actual =
                    Assert.Throws<ExpressionEvaluationException>(() => runner.Run(rng, CancellationToken.None));
                var expected = Assert.Throws<ExpressionEvaluationException>(() =>
                    SampledInterpreter.RunSingle(reference.Program!, state, 42));
                Assert.Equal(expected.Code, actual.Code);
                Assert.Equal(expected.Location, actual.Location);
                Assert.Equal(0, rng.AuditSnapshot(0)!.Words);
                Assert.False(runner.ExportState().ContainsKey("data"));
            }
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Theory]
    [InlineData("mixed")]
    [InlineData("mixed-family")]
    [InlineData("inline-literal")]
    [InlineData("invalid-literal")]
    [InlineData("selector-type")]
    [InlineData("missing-expression")]
    [InlineData("orphan")]
    [InlineData("ambiguous")]
    [InlineData("missing-member")]
    [InlineData("too-many")]
    [InlineData("empty")]
    [InlineData("no-expression")]
    public void InvalidTypedAndFamilyContractsAreRejectedWithoutMutatingTheSource(string kind)
    {
        GraphConfig source = Family(false);
        var draw = (DrawNode)source.Nodes[0];
        switch (kind)
        {
            case "mixed-family":
                source.Distributions!["right"] = new DistributionAsset
                {
                    Outcomes =
                    [
                        new DrawWeight
                        {
                            OutcomeId = "boolean",
                            Weight = 1,
                            StateValue = new ConstantExpr
                            {
                                Kind = ConstantKind.Boolean,
                                Value = "true"
                            }
                        }
                    ]
                };
                break;
            case "inline-literal":
                draw = draw with
                {
                    DistributionIds = null,
                    DistributionIndexExpressionId = null,
                    DrawWeights = [Outcome("1/0", 1)]
                };
                break;
            case "mixed":
                source.Distributions!["right"].Outcomes[0] = new DrawWeight
                {
                    OutcomeId = "boolean",
                    Weight = 1,
                    StateValue = new ConstantExpr
                    {
                        Kind = ConstantKind.Boolean,
                        Value = "true"
                    }
                }; break;
            case "invalid-literal": source.Distributions!["right"].Outcomes[0] = Outcome("1/0", 1); break;
            case "selector-type":
                source.Expressions!["family-index"] = new ConstantExpr
                {
                    Kind = ConstantKind.Boolean,
                    Value = "true"
                }; break;
            case "missing-expression":
                draw = draw with
                {
                    DistributionIndexExpressionId = "missing"
                }; break;
            case "orphan":
                draw = draw with
                {
                    DistributionIds = null
                }; break;
            case "ambiguous":
                draw = draw with
                {
                    DistributionId = "left"
                }; break;
            case "missing-member":
                draw = draw with
                {
                    DistributionIds = ["left", "missing"]
                }; break;
            case "too-many":
                draw = draw with
                {
                    DistributionIds = Enumerable.Repeat("left", DistributionResolver.MaxFamilyMembers + 1).ToArray()
                }; break;
            case "empty":
                draw = draw with
                {
                    DistributionIds = []
                }; break;
            case "no-expression":
                draw = draw with
                {
                    DistributionIndexExpressionId = null
                }; break;
        }

        source.Nodes[0] = draw;
        var json = JsonSerializer.Serialize(source, JsonOptions.Default);
        CompileResult result = new GraphCompiler().Compile(source);
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(json, JsonSerializer.Serialize(source, JsonOptions.Default));
    }

    [Fact]
    public void FamilyStateAndPayoutChannelsReferToTheSameOutcome()
    {
        GraphConfig source = Family();
        foreach (DistributionAsset asset in source.Distributions!.Values)
        {
            for (var index = 0; index < asset.Outcomes.Length; index++)
            {
                DrawWeight outcome = asset.Outcomes[index];
                asset.Outcomes[index] = outcome with
                {
                    Value = (long)DrawData.StateValue(outcome).AsInteger() * 7
                };
            }
        }

        source = source with
        {
            Nodes =
            [
                source.Nodes[0], source.Nodes[1],
                new PutStateNode
                {
                    Id = "save-channel",
                    StateKey = "channel",
                    Inputs = Ports(),
                    Outputs = Ports()
                },
                source.Nodes[2], (MetricsSinkNode)source.Nodes[3] with
                {
                    WinCap = 10000
                }
            ],
            Edges =
            [
                Edge("mode", "family"), Edge("family", "save-channel"), Edge("save-channel", "pay"), Edge("pay", "sink")
            ]
        };
        source.Expressions!["pay"] = new BinaryExpr
        {
            Op = BinaryOp.Mul,
            Left = Field("data"),
            Right = Field("channel")
        };
        CompileResult compiled = new GraphCompiler().Compile(source);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Dist<BigInteger> classic = ExactInterpreter.Evaluate(compiled.ReferenceProgram!,
            new Dictionary<string, object?>(),
            StateHasher.CanonicalHash).ValueDistribution();
        (BigInteger numerator, BigInteger denominator) = classic.ExpectedBigIntegerValue();
        // Independent law: 7*D^2, with masses 1/8,1/8,3/16,3/8,3/16 at D=2,4,10,20,30.
        Assert.Equal(new Rational(2380, 1), new Rational(numerator, denominator));
        StructuredExactResult structured = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        Assert.Equal(new Rational(2380, 1), structured.ExpectedWin);
        for (var seed = 0; seed < 32; seed++)
        {
            (BigInteger Value, Dictionary<string, object?> State) result =
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed);
            BigInteger data = StateValues.Read(result.State, ["data"]).AsInteger();
            Assert.Equal(7 * data * data, result.Value);
        }
    }

    [Fact]
    public void FamilySelectorIsNamespacedInsideAParameterizedReusableBody()
    {
        GraphConfig source = Family(false);
        var draw = (DrawNode)source.Nodes[0];
        source.Nodes[0] = new LibraryNode
        {
            Id = "family",
            MechanicName = "body",
            Outputs = Ports(),
            Parameters = new Dictionary<string, string>
            {
                ["pick"] = "1"
            }
        };
        source = source with
        {
            Mechanics = new Dictionary<string, CustomMechanic>
            {
                ["body"] = new()
                {
                    Name = "Arbitrary selection",
                    Nodes =
                    [
                        draw with
                        {
                            Id = "draw",
                            DistributionIndexExpressionId = "local-index"
                        }
                    ],
                    Expressions = new Dictionary<string, Expression>
                    {
                        ["local-index"] = new FieldAccessExpr
                        {
                            Target = "state",
                            Path = ["expressions", "pick"]
                        }
                    }
                }
            }
        };
        CompileResult compiled = new GraphCompiler().Compile(source);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Assert.Equal("compiled-state-plan-v1", compiled.SamplingEngine);
        for (var seed = 0; seed < 32; seed++)
        {
            (BigInteger Value, Dictionary<string, object?> State) result =
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed);
            BigInteger data = StateValues.Read(result.State, ["data"]).AsInteger();
            Assert.Contains(data, new BigInteger[] { 10, 20, 30 });
            Assert.Equal(data, result.Value);
        }
    }

    [Fact]
    public void CompiledFamilyDataCannotBeChangedThroughSourceAssetsOrIds()
    {
        GraphConfig source = Family();
        CompileResult compiled = new GraphCompiler().Compile(source);
        CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(Family());
        Assert.True(compiled.IsValid);
        source.Distributions!["left"].Outcomes[0] = Outcome("999", 1);
        ((DrawNode)source.Nodes[1]).DistributionIds![0] = "missing";
        source.Expressions!["family-index"] = Number("999");
        for (var seed = 0; seed < 128; seed++)
        {
            Assert.Equal(
                SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), seed).Value,
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed).Value);
        }
    }

    [Fact]
    public void TypedFamilyJsonRoundTripsWithoutLeakingBoundCompilerData()
    {
        GraphConfig source = Family();
        var json = JsonSerializer.Serialize(source, JsonOptions.Default);
        Assert.DoesNotContain("boundDistributions", json);
        var parsed = JsonSerializer.Deserialize<GraphConfig>(json, JsonOptions.Default)!;
        CompileResult compiled = new GraphCompiler().Compile(parsed);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Assert.Equal(json, JsonSerializer.Serialize(parsed, JsonOptions.Default));
        Assert.Equal("id-2", parsed.Distributions!["left"].Outcomes[0].OutcomeId);
        Assert.Equal("2", parsed.Distributions["left"].Outcomes[0].StateValue!.Value);
        CompileResult expected = new GraphCompiler(optimizeSampling: false).Compile(source);
        for (var seed = 0; seed < 16; seed++)
        {
            Assert.Equal(SampledInterpreter.RunSingle(expected.Program!, new Dictionary<string, object?>(), seed).Value,
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed).Value);
        }
    }
}
