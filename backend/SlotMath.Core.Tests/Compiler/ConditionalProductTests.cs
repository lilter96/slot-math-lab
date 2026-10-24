using System.Globalization;
using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Authoring;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Compiler;

[Collection("SamplingOptions")]
public sealed class ConditionalProductTests
{
    private static ConstantExpr N(long value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString(CultureInfo.InvariantCulture)
    };

    private static FieldAccessExpr F(params string[] path) => new()
    {
        Target = "state",
        Path = path
    };

    private static BinaryExpr Add(Expression a, Expression b) => new()
    {
        Op = BinaryOp.Add,
        Left = a,
        Right = b
    };

    private static BinaryExpr Mul(Expression a, Expression b) => new()
    {
        Op = BinaryOp.Mul,
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

    private static Edge Link(string source, string target) => new()
    {
        Id = source + "->" + target,
        SourceNodeId = source,
        TargetNodeId = target,
        SourcePort = "state",
        TargetPort = "state"
    };

    private static ConditionalProductDefinition Toy(long lower = 3, long upper = 5)
    {
        int[][] contributions = [[0, 1, 2, 3], [0, 1, 2], [0, 1, 3], [0, 2]];
        int[][] weights = [[2, 0, 3, 1], [1, 4, 2], [3, 1, 2], [2, 5]];
        return new ConditionalProductDefinition
        {
            MinimumSum = lower,
            MaximumSum = upper,
            IncludeOutcomeData = true,
            Inputs = contributions.Select((values, stage) => new ConditionalProductInput
            {
                Contributions = values,
                Outcomes = values.Select((_, position) => new DrawWeight
                {
                    OutcomeId = "original/" + stage + "/" + position,
                    Weight = weights[stage][position],
                    Value = position - 7,
                    StateValue = stage switch
                    {
                        0 => new ConstantExpr
                        {
                            Kind = ConstantKind.Boolean,
                            Value = position % 2 == 0 ? "true" : "false"
                        },
                        1 => new ConstantExpr
                        {
                            Kind = ConstantKind.String,
                            Value = "duplicate-data"
                        },
                        2 => new ConstantExpr
                        {
                            Kind = ConstantKind.Rational,
                            Value = position + 1 + "/3"
                        },
                        _ => N(9007199254740993 + position)
                    }
                }).ToArray()
            }).ToArray()
        };
    }

    // This oracle enumerates the original Cartesian product and has no DP recurrence.
    private static List<Joint> Enumerate(ConditionalProductDefinition definition)
    {
        var result = new List<Joint>();
        var choices = new int[definition.Inputs.Length];

        void Visit(int index, long sum, BigInteger weight)
        {
            if (index == choices.Length)
            {
                if (sum >= definition.MinimumSum && sum <= definition.MaximumSum && weight > 0)
                {
                    result.Add(new Joint(choices.ToArray(), weight));
                }

                return;
            }

            ConditionalProductInput input = definition.Inputs[index];
            for (var position = 0; position < input.Outcomes.Length; position++)
            {
                choices[index] = position;
                Visit(index + 1, sum + input.Contributions[position], weight * input.Outcomes[position].Weight);
            }
        }

        Visit(0, 0, BigInteger.One);
        return result;
    }

    private static BigInteger Code(int[] choices)
    {
        BigInteger result = 1, scale = 1;
        foreach (var choice in choices)
        {
            result += choice * scale;
            scale *= 4;
        }

        return result;
    }

    private static GraphConfig Wrap(ConditionalProductComposition composition)
    {
        Expression code = N(1);
        long scale = 1;
        for (var index = 0; index < composition.InternalStateKeys.Length - 1; index++)
        {
            code = Add(code,
                Mul(F(composition.OutputKey, "indices", index.ToString(CultureInfo.InvariantCulture)), N(scale)));
            scale *= 4;
        }

        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = composition.SourceIdentity,
            Distributions = new Dictionary<string, DistributionAsset>(composition.Distributions)
            {
                ["following"] = new()
                {
                    Outcomes =
                    [
                        new DrawWeight
                        {
                            OutcomeId = "a",
                            Weight = 2,
                            Value = 0
                        },
                        new DrawWeight
                        {
                            OutcomeId = "b",
                            Weight = 5,
                            Value = 0
                        }
                    ]
                }
            },
            Mechanics = new Dictionary<string, CustomMechanic>
            {
                [composition.Body.Name] = composition.Body
            },
            Expressions = new Dictionary<string, Expression>
            {
                ["score"] = code
            },
            Nodes =
            [
                new LibraryNode
                {
                    Id = "body",
                    MechanicName = composition.Body.Name,
                    Outputs = Ports()
                },
                new DrawNode
                {
                    Id = "following",
                    DistributionId = "following",
                    StateWriteKey = "after",
                    Inputs = Ports(),
                    Outputs = Ports()
                },
                new ModifyStateNode
                {
                    Id = "score",
                    OutputKey = "win",
                    ExpressionId = "score",
                    Inputs = Ports(),
                    Outputs = Ports()
                },
                new MetricsSinkNode
                {
                    Id = "sink",
                    WinStateKey = "win",
                    WinCap = 1000000,
                    Inputs = Ports()
                }
            ],
            Edges = [Link("body", "following"), Link("following", "score"), Link("score", "sink")]
        };
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    [InlineData(3, 5)]
    [InlineData(0, 10)]
    [InlineData(6, 8)]
    public void EveryExactJointMassMatchesAnIndependentCartesianOracle(long lower, long upper)
    {
        ConditionalProductDefinition definition = Toy(lower, upper);
        List<Joint> expected = Enumerate(definition);
        BigInteger total = expected.Aggregate(BigInteger.Zero, (sum, row) => sum + row.Weight);
        ConditionalProductComposition composition = ConditionalProductComposer.Compose(definition);
        Assert.Equal(total.ToString(CultureInfo.InvariantCulture), composition.TotalMass);
        Assert.All(composition.Body.Nodes, node => Assert.True(node is DrawNode or ModifyStateNode));
        Assert.Equal(2 * definition.Inputs.Length + 2, composition.Body.Nodes.Length);
        GraphConfig graph = Wrap(composition);
        CompileResult compiled = new GraphCompiler().Compile(graph);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Dist<BigInteger> classic = ExactInterpreter.Evaluate(compiled.ReferenceProgram!,
            new Dictionary<string, object?>(), StateHasher.CanonicalHash).ValueDistribution();
        StructuredExactResult structured = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        Dictionary<BigInteger, Rational> oracle =
            expected.ToDictionary(row => Code(row.Choices), row => new Rational(row.Weight, total));
        foreach (Dist<BigInteger> actual in new[] { classic, structured.Distribution! })
        {
            Assert.Equal(oracle.Count, actual.Count);
            foreach (Dist<BigInteger>.Entry row in actual.Entries)
            {
                Assert.Equal(oracle[row.Value], new Rational(row.Numerator, actual.Denominator));
            }
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void ScalarConditionalAliasesPreserveTheDeclaredScheduleAndTypedResults(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            ConditionalProductDefinition definition = Toy();
            ConditionalProductComposition composition = ConditionalProductComposer.Compose(definition);
            GraphConfig graph = Wrap(composition);
            CompileResult compiled = new GraphCompiler().Compile(graph),
                reference = new GraphCompiler(optimizeSampling: false).Compile(graph);
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            Assert.True(reference.IsValid, string.Join(';', reference.Errors));
            ISamplingRunner<Dictionary<string, object?>, BigInteger> runner = Assert
                .IsAssignableFrom<ICompiledSampling<Dictionary<string, object?>, BigInteger>>(compiled.Program)
                .CreateRunner(new Dictionary<string, object?>());
            List<Joint> joint = Enumerate(definition);
            AliasMethod following = WeightSet.FromIntegers([2, 5]).AliasTable;
            string? snapshotHash = null;
            object? snapshot = null;
            for (var seed = 0; seed < 128; seed++)
            {
                var actualRng = new SeededRandom(seed);
                var oracleRng = new SeededRandom(seed);
                actualRng.EnableAudit();
                oracleRng.EnableAudit();
                var choices = new int[definition.Inputs.Length];
                for (var stage = 0; stage < choices.Length; stage++)
                {
                    var weights = new BigInteger[definition.Inputs[stage].Outcomes.Length];
                    foreach (Joint row in joint)
                    {
                        if (Enumerable.Range(0, stage).All(index => row.Choices[index] == choices[index]))
                        {
                            weights[row.Choices[stage]] += row.Weight;
                        }
                    }

                    BigInteger gcd = weights.Aggregate(BigInteger.Zero, BigInteger.GreatestCommonDivisor);
                    choices[stage] = WeightSet.FromNumerators(weights.Select(value => value / gcd).ToArray()).AliasTable
                        .Sample(oracleRng);
                }

                var after = following.Sample(oracleRng) == 0 ? "a" : "b";
                Assert.Equal(Code(choices), runner.Run(actualRng, default));
                Dictionary<string, object?> state = runner.ExportState();
                Assert.Equal(after, state["after"]);
                Assert.Equal(JsonSerializer.Serialize(oracleRng.AuditSnapshot(64)),
                    JsonSerializer.Serialize(actualRng.AuditSnapshot(64)));
                Assert.Equal(oracleRng.NextUInt64(), actualRng.NextUInt64());
                ExprValue result = StateValues.Convert(state[composition.OutputKey]);
                Assert.Equal(
                    choices.Select((choice, stage) => (long)definition.Inputs[stage].Contributions[choice]).Sum(),
                    result.RecordValue!["sum"].AsInteger());
                for (var stage = 0; stage < choices.Length; stage++)
                {
                    ExprValue position =
                        result.RecordValue["positions"].RecordValue![stage.ToString(CultureInfo.InvariantCulture)];
                    DrawWeight expected = definition.Inputs[stage].Outcomes[choices[stage]];
                    Assert.Equal(ExprValue.Number(choices[stage]), position.RecordValue!["index"]);
                    Assert.Equal(ExprValue.String(expected.OutcomeId), position.RecordValue["outcomeId"]);
                    Assert.Equal(DrawData.StateValue(expected), position.RecordValue["data"]);
                    Assert.Equal(ExprValue.Number(expected.Value), position.RecordValue["value"]);
                }

                if (seed == 0)
                {
                    snapshot = state[composition.OutputKey];
                    snapshotHash = JsonSerializer.Serialize(snapshot);
                }
                else
                {
                    Assert.Equal(snapshotHash, JsonSerializer.Serialize(snapshot));
                }

                (BigInteger Value, Dictionary<string, object?> State) a =
                    SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed);
                (BigInteger Value, Dictionary<string, object?> State) b =
                    SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), seed);
                Assert.Equal(b.Value, a.Value);
                Assert.Equal(StateHasher.CanonicalHash(b.State), StateHasher.CanonicalHash(a.State));
            }
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void SignedContributionsNormalizeWithoutLosingTheActualSum()
    {
        ConditionalProductDefinition source = Toy(-9, -2);
        source = source with
        {
            Inputs = source.Inputs.Select((input, stage) => input with
            {
                Contributions = input.Contributions.Select(value => value - stage - 1).ToArray()
            }).ToArray()
        };
        ConditionalProductComposition composition = ConditionalProductComposer.Compose(source);
        Assert.Equal(Enumerate(source).Aggregate(BigInteger.Zero, (sum, row) => sum + row.Weight).ToString(),
            composition.TotalMass);
        CompileResult compiled = new GraphCompiler().Compile(Wrap(composition));
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        List<Joint> joint = Enumerate(source);
        BigInteger mass = joint.Aggregate(BigInteger.Zero, (total, row) => total + row.Weight);
        Dictionary<BigInteger, Rational> expected =
            joint.ToDictionary(row => Code(row.Choices), row => new Rational(row.Weight, mass));
        Dist<BigInteger> classic = ExactInterpreter.Evaluate(compiled.ReferenceProgram!,
            new Dictionary<string, object?>(), StateHasher.CanonicalHash).ValueDistribution();
        StructuredExactResult structured = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        foreach (Dist<BigInteger> law in new[] { classic, structured.Distribution! })
        {
            Assert.Equal(expected.Count, law.Count);
            foreach (Dist<BigInteger>.Entry row in law.Entries)
            {
                Assert.Equal(expected[row.Value], new Rational(row.Numerator, law.Denominator));
            }
        }

        for (var seed = 0; seed < 64; seed++)
        {
            (BigInteger Value, Dictionary<string, object?> State) sampled =
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed);
            ExprValue value = StateValues.Convert(sampled.State[composition.OutputKey]);
            BigInteger sum = value.RecordValue!["sum"].AsInteger();
            Assert.InRange(sum, new BigInteger(source.MinimumSum), new BigInteger(source.MaximumSum));
            BigInteger independently = 0;
            for (var stage = 0; stage < source.Inputs.Length; stage++)
            {
                ExprValue position = value.RecordValue["positions"].RecordValue![stage.ToString()];
                independently += source.Inputs[stage].Contributions[(int)position.RecordValue!["index"].AsInteger()];
            }

            Assert.Equal(independently, sum);
        }
    }

    [Fact]
    public void ZeroWeightExtremeContributionsDoNotExpandThePositiveSupport()
    {
        var source = new ConditionalProductDefinition
        {
            MinimumSum = int.MaxValue,
            MaximumSum = int.MaxValue,
            Inputs =
            [
                new ConditionalProductInput
                {
                    Contributions = [int.MinValue, int.MaxValue],
                    Outcomes =
                    [
                        new DrawWeight
                        {
                            OutcomeId = "zero",
                            Weight = 0
                        },
                        new DrawWeight
                        {
                            OutcomeId = "one",
                            Weight = 7
                        }
                    ]
                }
            ]
        };
        ConditionalProductComposition composition = ConditionalProductComposer.Compose(source);
        Assert.Equal(2, composition.DpCells);
        Assert.Equal("7", composition.TotalMass);
        Assert.Equal(new long[] { 0, 1 },
            Assert.Single(composition.Distributions).Value.Outcomes.Select(value => value.Weight));
        CompileResult compiled = new GraphCompiler().Compile(Wrap(composition));
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        (BigInteger Value, Dictionary<string, object?> State) sample =
            SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 19);
        ExprValue result = StateValues.Convert(sample.State[composition.OutputKey]);
        Assert.Equal(new BigInteger(int.MaxValue), result.RecordValue!["sum"].AsInteger());
    }

    [Fact]
    public void EmptyProductAndExtremeBoundsHaveExactDeterministicSemantics()
    {
        var source = new ConditionalProductDefinition
        {
            MinimumSum = long.MinValue,
            MaximumSum = long.MaxValue
        };
        ConditionalProductComposition empty = ConditionalProductComposer.Compose(source);
        Assert.Equal("1", empty.TotalMass);
        Assert.Empty(empty.Distributions);
        Assert.Equal(2, empty.Body.Nodes.Length);
        CompileResult compiled = new GraphCompiler().Compile(Wrap(empty));
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Assert.Equal(BigInteger.One,
            SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 7).Value);
        ConditionalProductComposition unrestricted =
            ConditionalProductComposer.Compose(Toy(long.MinValue, long.MaxValue));
        Assert.Equal("1764", unrestricted.TotalMass);
    }

    [Fact]
    public void OrderedPositionsZeroWeightsAndIndependentSourceSnapshotsRemainEditable()
    {
        ConditionalProductDefinition source = Toy();
        var original = JsonSerializer.Serialize(source, JsonOptions.Default);
        ConditionalProductComposition composition = ConditionalProductComposer.Compose(source);
        Assert.Equal(original, JsonSerializer.Serialize(source, JsonOptions.Default));
        foreach (KeyValuePair<string, DistributionAsset> pair in composition.Distributions)
        {
            var stage = int.Parse(pair.Key.Split('/')[2], CultureInfo.InvariantCulture);
            Assert.Equal(source.Inputs[stage].Outcomes.Select(value => value.OutcomeId),
                pair.Value.Outcomes.Select(value => value.OutcomeId));
            Assert.Equal(source.Inputs[stage].Outcomes.Length, pair.Value.Outcomes.Length);
            for (var index = 0; index < pair.Value.Outcomes.Length; index++)
            {
                if (source.Inputs[stage].Outcomes[index].Weight == 0)
                {
                    Assert.Equal(0, pair.Value.Outcomes[index].Weight);
                }
            }
        }

        var artifact = JsonSerializer.Serialize(composition, JsonOptions.Default);
        source.Inputs[0].Outcomes[0] = source.Inputs[0].Outcomes[0] with
        {
            Weight = 999
        };
        source.Inputs[0].Contributions[0] = 999;
        Assert.Equal(artifact, JsonSerializer.Serialize(composition, JsonOptions.Default));
        GraphConfig graph = Wrap(composition);
        var json = JsonSerializer.Serialize(graph, JsonOptions.Default);
        var restored = JsonSerializer.Deserialize<GraphConfig>(json, JsonOptions.Default)!;
        Assert.Equal(json, JsonSerializer.Serialize(restored, JsonOptions.Default));
        Assert.True(new GraphCompiler().Compile(restored).IsValid);
    }

    [Fact]
    public void IdentityIncludesParametersOrderPayloadAndTheNewRandomSchedule()
    {
        ConditionalProductDefinition source = Toy();
        var identity = ConditionalProductComposer.Compose(source).SourceIdentity;
        Assert.Equal(identity, ConditionalProductComposer.Compose(Toy()).SourceIdentity);
        ConditionalProductDefinition[] variants = new[]
        {
            source with
            {
                MinimumSum = 4
            },
            source with
            {
                Namespace = "other"
            },
            source with
            {
                OutputKey = "other"
            },
            source with
            {
                IncludeOutcomeData = false
            },
            source with
            {
                Inputs = source.Inputs.Reverse().ToArray()
            },
            source with
            {
                Inputs = source.Inputs.Select((input, index) => index == 1
                    ? input with
                    {
                        Outcomes = input.Outcomes.Select((outcome, position) => position == 0
                            ? outcome with
                            {
                                StateValue = new ConstantExpr
                                {
                                    Kind = ConstantKind.String,
                                    Value = "edited"
                                }
                            }
                            : outcome).ToArray()
                    }
                    : input).ToArray()
            }
        };
        foreach (ConditionalProductDefinition variant in variants)
        {
            Assert.NotEqual(identity, ConditionalProductComposer.Compose(variant).SourceIdentity);
        }

        Assert.Equal(ConditionalProductComposer.Schedule, ConditionalProductComposer.Compose(source).RandomSchedule);
    }

    [Fact]
    public void LargeProductPreparesAPolynomialGridWithoutEnumeratingTuples()
    {
        var source = new ConditionalProductDefinition
        {
            MinimumSum = 80,
            MaximumSum = 80,
            Inputs = Enumerable.Range(0, 20).Select(_ => new ConditionalProductInput
            {
                Contributions = Enumerable.Range(0, 8).ToArray(),
                Outcomes = Enumerable.Range(0, 8).Select(value => new DrawWeight
                {
                    OutcomeId = value.ToString(),
                    Weight = 1,
                    Value = 0
                }).ToArray()
            }).ToArray()
        };
        ConditionalProductComposition composition = ConditionalProductComposer.Compose(source);
        Assert.Equal(1701, composition.DpCells);
        Assert.Equal(12960, composition.DpTransitions);
        Assert.Equal(704, composition.Distributions.Count);
        Assert.Equal(5632, composition.Distributions.Values.Sum(value => value.Outcomes.Length));
        Assert.Equal(42, composition.Body.Nodes.Length);
        CompileResult compiled = new GraphCompiler().Compile(Wrap(composition));
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        for (var seed = 0; seed < 16; seed++)
        {
            (BigInteger Value, Dictionary<string, object?> State) sample =
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed);
            ExprValue result = StateValues.Convert(sample.State[composition.OutputKey]);
            Assert.Equal(new BigInteger(80), result.RecordValue!["sum"].AsInteger());
            BigInteger total = result.RecordValue["indices"].RecordValue!.Values.Aggregate(BigInteger.Zero,
                (sum, value) => sum + value.AsInteger());
            Assert.Equal(new BigInteger(80), total);
        }
    }

    [Theory]
    [InlineData("null", "PRODUCT_INPUT")]
    [InlineData("null-inputs", "PRODUCT_INPUT")]
    [InlineData("null-stage", "PRODUCT_INPUT")]
    [InlineData("too-many", "PRODUCT_INPUT")]
    [InlineData("null-contributions", "PRODUCT_INPUT")]
    [InlineData("mixed-type", "PRODUCT_INPUT")]
    [InlineData("mismatch", "PRODUCT_INPUT")]
    [InlineData("negative-weight", "PRODUCT_INPUT")]
    [InlineData("zero-weights", "PRODUCT_INPUT")]
    [InlineData("range", "PRODUCT_RANGE")]
    [InlineData("empty", "PRODUCT_EMPTY")]
    [InlineData("namespace", "PRODUCT_IDENTITY")]
    [InlineData("collision", "PRODUCT_IDENTITY")]
    [InlineData("sum-budget", "PRODUCT_SUM_BUDGET")]
    public void InvalidOrOverBudgetRequestsNeverProduceAnUnconditionalFallback(string kind, string expected)
    {
        ConditionalProductDefinition source = Toy();
        source = kind switch
        {
            "null" => null!,
            "null-inputs" => source with
            {
                Inputs = null!
            },
            "null-stage" => source with
            {
                Inputs = [null!]
            },
            "too-many" => source with
            {
                Inputs = Enumerable.Repeat(source.Inputs[0], 65).ToArray()
            },
            "null-contributions" => source with
            {
                Inputs =
                [
                    source.Inputs[0] with
                    {
                        Contributions = null!
                    }
                ]
            },
            "mixed-type" => source with
            {
                Inputs =
                [
                    new ConditionalProductInput
                    {
                        Contributions = [0, 1],
                        Outcomes =
                        [
                            new DrawWeight
                            {
                                OutcomeId = "a",
                                Weight = 1
                            },
                            new DrawWeight
                            {
                                OutcomeId = "b",
                                Weight = 1,
                                StateValue = N(1)
                            }
                        ]
                    }
                ]
            },
            "mismatch" => source with
            {
                Inputs =
                [
                    source.Inputs[0] with
                    {
                        Contributions = []
                    }
                ]
            },
            "negative-weight" => source with
            {
                Inputs =
                [
                    new ConditionalProductInput
                    {
                        Outcomes =
                        [
                            new DrawWeight
                            {
                                OutcomeId = "a",
                                Weight = -1
                            }
                        ],
                        Contributions = [0]
                    }
                ]
            },
            "zero-weights" => source with
            {
                Inputs =
                [
                    new ConditionalProductInput
                    {
                        Outcomes =
                        [
                            new DrawWeight
                            {
                                OutcomeId = "a",
                                Weight = 0
                            }
                        ],
                        Contributions = [0]
                    }
                ]
            },
            "range" => source with
            {
                MinimumSum = 99
            },
            "empty" => source with
            {
                MinimumSum = 99,
                MaximumSum = 100
            },
            "namespace" => source with
            {
                Namespace = ""
            },
            "collision" => source with
            {
                OutputKey = "product/pick/0"
            },
            _ => new ConditionalProductDefinition
            {
                MaximumSum = 256,
                Inputs =
                [
                    new ConditionalProductInput
                    {
                        Outcomes =
                        [
                            new DrawWeight
                            {
                                OutcomeId = "a",
                                Weight = 1
                            },
                            new DrawWeight
                            {
                                OutcomeId = "b",
                                Weight = 1
                            }
                        ],
                        Contributions = [0, 256]
                    }
                ]
            }
        };
        Assert.Equal(expected,
            Assert.Throws<ConditionalProductException>(() => ConditionalProductComposer.Compose(source)).Code);
    }

    [Fact]
    public void WideExactCoefficientsDeclineInsteadOfRounding()
    {
        var source = new ConditionalProductDefinition
        {
            MinimumSum = 1,
            MaximumSum = 1,
            Inputs = new[] { new[] { long.MaxValue, 1 }, new[] { 1, long.MaxValue - 2 } }.Select(weights =>
                new ConditionalProductInput
                {
                    Contributions = [0, 1],
                    Outcomes = weights.Select((weight, index) => new DrawWeight
                    {
                        OutcomeId = index.ToString(),
                        Weight = weight
                    }).ToArray()
                }).ToArray()
        };
        Assert.Equal("PRODUCT_WEIGHT_BUDGET",
            Assert.Throws<ConditionalProductException>(() => ConditionalProductComposer.Compose(source)).Code);
    }

    [Fact]
    public void NumericAndAssetBudgetsBoundPreparation()
    {
        var numeric = new ConditionalProductDefinition
        {
            Inputs = Enumerable.Range(0, 64).Select(_ => new ConditionalProductInput
            {
                Contributions = new int[256],
                Outcomes = Enumerable.Range(0, 256).Select(i => new DrawWeight
                {
                    OutcomeId = i.ToString(),
                    Weight = long.MaxValue
                }).ToArray()
            }).ToArray()
        };
        Assert.Equal("PRODUCT_NUMERIC_BUDGET",
            Assert.Throws<ConditionalProductException>(() => ConditionalProductComposer.Compose(numeric)).Code);
        var assets = new ConditionalProductDefinition
        {
            MinimumSum = 32,
            MaximumSum = 32,
            Inputs = Enumerable.Range(0, 64).Select(_ => new ConditionalProductInput
            {
                Contributions = [0, 1],
                Outcomes =
                [
                    new DrawWeight
                    {
                        OutcomeId = "a",
                        Weight = 1
                    },
                    new DrawWeight
                    {
                        OutcomeId = "b",
                        Weight = 1
                    }
                ]
            }).ToArray()
        };
        Assert.Equal("PRODUCT_ASSET_BUDGET",
            Assert.Throws<ConditionalProductException>(() => ConditionalProductComposer.Compose(assets)).Code);
    }

    [Fact]
    public void CancelledPreparationPublishesNoArtifactAndLeavesSourceUntouched()
    {
        ConditionalProductDefinition source = Toy();
        var before = JsonSerializer.Serialize(source, JsonOptions.Default);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ConditionalProductComposer.Compose(source, cancellation.Token));
        Assert.Equal(before, JsonSerializer.Serialize(source, JsonOptions.Default));
    }

    private sealed record Joint(int[] Choices, BigInteger Weight);
}
