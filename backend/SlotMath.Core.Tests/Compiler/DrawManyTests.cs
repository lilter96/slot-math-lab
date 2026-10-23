using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Compiler;

[Collection("SamplingOptions")]
public class DrawManyTests
{
    private static Dictionary<string, Port> Ports() => new()
    {
        ["state"] = new Port
        {
            Name = "state",
            Type = PortType.State
        }
    };

    private static ConstantExpr Number(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString()
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

    private static DrawWeight Outcome(int value, int weight) => new()
    {
        OutcomeId = "id-" + value,
        Weight = weight,
        Value = value * 5,
        StateValue = Number(value)
    };

    private static Dictionary<string, DistributionAsset> Assets() => new()
    {
        ["a"] = new DistributionAsset
        {
            Outcomes = [Outcome(999, 0), Outcome(2, 1), Outcome(7, 3), Outcome(9, 0), Outcome(11, 0)]
        },
        ["b"] = new DistributionAsset
        {
            Outcomes = [Outcome(10, 2), Outcome(20, 0), Outcome(30, 1)]
        }
    };

    private static AggregateExpr Sum() => new()
    {
        Func = AggregateFunc.Sum,
        StateKey = "items",
        ItemName = "item",
        ItemType = ExprType.Number,
        ValueExpr = Field("item")
    };

    private static GraphConfig Graph(DrawManyNode batch)
    {
        return new GraphConfig
        {
            SchemaVersion = "1.0",
            Distributions = Assets(),
            InitialState = new Dictionary<string, JsonElement>
            {
                ["n"] = JsonSerializer.SerializeToElement(0)
            },
            Nodes =
            [
                batch with
                {
                    Id = "batch",
                    OutputKey = "items",
                    Inputs = Ports(),
                    Outputs = Ports()
                },
                new PutStateNode
                {
                    Id = "channel",
                    StateKey = "channel",
                    Inputs = Ports(),
                    Outputs = Ports()
                },
                new DrawNode
                {
                    Id = "suffix",
                    DistributionId = "b",
                    StateWriteKey = "suffix",
                    Inputs = Ports(),
                    Outputs = Ports()
                },
                new ModifyStateNode
                {
                    Id = "pay",
                    ExpressionId = "score",
                    OutputKey = "win",
                    Inputs = Ports(),
                    Outputs = Ports()
                },
                new MetricsSinkNode
                {
                    Id = "sink",
                    WinStateKey = "win",
                    WinCap = 100000,
                    Inputs = Ports()
                }
            ],
            Edges = [Edge("batch", "channel"), Edge("channel", "suffix"), Edge("suffix", "pay"), Edge("pay", "sink")],
            Expressions = new Dictionary<string, Expression>
            {
                ["count"] = Field("n"),
                ["score"] = Sum()
            }
        };
    }

    private static DrawManyNode Repeat(int count) => new()
    {
        Id = "batch",
        OutputKey = "items",
        DistributionId = "a",
        Count = count
    };

    private static string State(Dictionary<string, object?> state) =>
        JsonSerializer.Serialize(state.OrderBy(pair => pair.Key), JsonOptions.Default);

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Compiled)]
    [InlineData(SamplingTier.Tiered)]
    public void OrderedBatchesMatchScalarAliasesTheSuffixAndCompleteState(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            var cases = new List<(DrawManyNode Batch, int Count, string[] Schedule)>();
            foreach (var count in new[] { 0, 1, 30, 64, 65 })
            {
                cases.Add((Repeat(count), count, Enumerable.Repeat("a", count).ToArray()));
            }

            string[] schedule = ["a", "b", "a", "b"];
            cases.Add((new DrawManyNode
            {
                Id = "batch",
                OutputKey = "items",
                DistributionIds = schedule
            }, 4, schedule));
            cases.Add((new DrawManyNode
            {
                Id = "batch",
                OutputKey = "items",
                DistributionIds = []
            }, 0, []));
            for (var count = 0; count <= schedule.Length; count++)
            {
                cases.Add((new DrawManyNode
                {
                    Id = "batch",
                    OutputKey = "items",
                    DistributionIds = schedule,
                    CountExpressionId = "count",
                    MaxCount = schedule.Length
                }, count, schedule.Take(count).ToArray()));
            }

            foreach ((DrawManyNode batch, var count, var sequence) in cases)
            {
                GraphConfig source = Graph(batch);
                CompileResult compiled = new GraphCompiler().Compile(source);
                CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(source);
                Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
                Assert.True(reference.IsValid);
                var plan =
                    Assert.IsAssignableFrom<ICompiledSampling<Dictionary<string, object?>, BigInteger>>(
                        compiled.Program);
                Dictionary<string, object?> initial = batch.CountExpressionId is null
                    ? new Dictionary<string, object?>()
                    : new Dictionary<string, object?>
                    {
                        ["n"] = count
                    };
                ISamplingRunner<Dictionary<string, object?>, BigInteger> runner = plan.CreateRunner(initial);
                Dictionary<string, DistributionAsset> assets = Assets();
                Dictionary<string, AliasMethod> aliases = assets.ToDictionary(pair => pair.Key,
                    pair => WeightSet.FromIntegers(pair.Value.Outcomes.Select(outcome => outcome.Weight).ToArray())
                        .AliasTable);
                for (var seed = -8; seed < 32; seed++)
                {
                    SeededRandom oracle = SeededRandom.ForStream(unchecked((ulong)seed), 0);
                    SeededRandom actualRandom = SeededRandom.ForStream(unchecked((ulong)seed), 0);
                    oracle.EnableAudit();
                    actualRandom.EnableAudit();
                    var data = new List<BigInteger>();
                    object? channel = null;
                    foreach (var id in sequence)
                    {
                        DrawWeight outcome = assets[id].Outcomes[aliases[id].Sample(oracle)];
                        data.Add(BigInteger.Parse(outcome.StateValue!.Value));
                        channel = new BigInteger(outcome.Value);
                    }

                    DrawWeight suffix = assets["b"].Outcomes[aliases["b"].Sample(oracle)];
                    BigInteger value = runner.Run(actualRandom, CancellationToken.None);
                    Assert.Equal(data.Aggregate(BigInteger.Zero, (sum, item) => sum + item), value);
                    Assert.Equal(oracle.AuditSnapshot(0), actualRandom.AuditSnapshot(0));
                    Assert.Equal(oracle.NextUInt64(), actualRandom.NextUInt64());
                    Dictionary<string, object?> state = runner.ExportState();
                    Assert.Equal(data,
                        Assert.IsType<object?[]>(state["items"]).Select(item => Assert.IsType<BigInteger>(item)));
                    Assert.Equal(channel, state["channel"]);
                    Assert.Equal(new BigInteger(suffix.Value / 5), state["suffix"]);
                    (BigInteger Value, Dictionary<string, object?> State) expected =
                        SampledInterpreter.RunSingle(reference.Program!, initial, seed);
                    Assert.Equal(expected.Value, value);
                    Assert.Equal(State(expected.State), State(state));
                }
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
    public void BatchScalarTypesAndArrayDataRemainIndependentOfPayoutScaling(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            ConstantExpr[] literals =
            [
                Number(17), new()
                {
                    Kind = ConstantKind.Integer,
                    Value = "18446744073709551616"
                },
                new()
                {
                    Kind = ConstantKind.Rational,
                    Value = "7/3"
                },
                new()
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
                GraphConfig source = Graph(Repeat(3));
                source.Distributions!["a"] = new DistributionAsset
                {
                    Outcomes =
                    [
                        new DrawWeight
                        {
                            OutcomeId = "metadata",
                            Weight = 1,
                            Value = 3,
                            StateValue = literal
                        }
                    ]
                };
                source.Expressions!["score"] = Number(1);
                source.Nodes[^1] = (MetricsSinkNode)source.Nodes[^1] with
                {
                    Settlement = new MonetarySettlement
                    {
                        Quantum = "0.1"
                    }
                };
                CompileResult compiled = new GraphCompiler().Compile(source);
                CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(source);
                Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
                Assert.True(reference.IsValid);
                (BigInteger Value, Dictionary<string, object?> State) actual =
                    SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42);
                (BigInteger Value, Dictionary<string, object?> State) expected =
                    SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), 42);
                Assert.Equal(new BigInteger(10), compiled.WinScale);
                Assert.Equal(new BigInteger(10), actual.Value);
                Assert.Equal(new BigInteger(30), actual.State["channel"]);
                Assert.Equal(State(expected.State), State(actual.State));
                ExprValue data = ExactExpressionEvaluator.Evaluate(literal, EvalContext.Empty);
                foreach (var item in Assert.IsType<object?[]>(actual.State["items"]))
                {
                    Assert.Equal(data, StateValues.Convert(item));
                }
            }
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void TheMaximumBatchUsesBoundedTraversalAndMatchesTheScalarReference()
    {
        GraphConfig source = Graph(Repeat(DrawManyData.MaxCount));
        CompileResult compiled = new GraphCompiler().Compile(source);
        CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(source);
        Assert.True(compiled.IsValid);
        Assert.True(reference.IsValid);
        (BigInteger Value, Dictionary<string, object?> State) actual =
            SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42);
        (BigInteger Value, Dictionary<string, object?> State) expected =
            SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), 42);
        Assert.Equal(DrawManyData.MaxCount, Assert.IsType<object?[]>(actual.State["items"]).Length);
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(State(expected.State), State(actual.State));
    }

    [Fact]
    public void RandomCountExactLawRetainsTheCountAndPrefixDependence()
    {
        GraphConfig source = Graph(new DrawManyNode
        {
            Id = "batch",
            OutputKey = "items",
            DistributionId = "a",
            CountExpressionId = "count",
            MaxCount = 2
        });
        source = source with
        {
            Nodes =
            [
                new DrawNode
                {
                    Id = "count-draw",
                    StateWriteKey = "n",
                    Outputs = Ports(),
                    DrawWeights =
                    [
                        new DrawWeight
                        {
                            OutcomeId = "zero",
                            Weight = 1,
                            Value = 17,
                            StateValue = Number(0)
                        },
                        new DrawWeight
                        {
                            OutcomeId = "two",
                            Weight = 3,
                            Value = 17,
                            StateValue = Number(2)
                        }
                    ]
                },
                .. source.Nodes
            ],
            Edges = [Edge("count-draw", "batch"), .. source.Edges]
        };
        source.Expressions!["score"] = new BinaryExpr
        {
            Op = BinaryOp.Add,
            Left = Sum(),
            Right =
                new BinaryExpr
                {
                    Op = BinaryOp.Mul,
                    Left = Number(100),
                    Right = Field("n")
                }
        };
        CompileResult compiled = new GraphCompiler().Compile(source);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Dist<BigInteger> classic = ExactInterpreter
            .Evaluate(compiled.ReferenceProgram!, new Dictionary<string, object?>(), StateHasher.CanonicalHash)
            .ValueDistribution();
        StructuredExactResult structured = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        Assert.NotNull(structured.Distribution);
        var expected = new Dictionary<BigInteger, Rational>
        {
            [0] = new(1, 4),
            [204] = new(3, 64),
            [209] = new(18, 64),
            [214] = new(27, 64)
        };
        foreach (Dist<BigInteger>? law in new[] { classic, structured.Distribution! })
        {
            (BigInteger numerator, BigInteger denominator) = law.ExpectedBigIntegerValue();
            Assert.Equal(new Rational(1269, 8), new Rational(numerator, denominator));
            Assert.Equal(4, law.Count);
            foreach (Dist<BigInteger>.Entry entry in law.Entries)
            {
                Assert.Equal(expected[entry.Value], new Rational(entry.Numerator, law.Denominator));
            }
        }

        CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(source);
        for (var seed = 0; seed < 32; seed++)
        {
            (BigInteger Value, Dictionary<string, object?> State) actual =
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed);
            (BigInteger Value, Dictionary<string, object?> State) control =
                SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), seed);
            Assert.Equal(control.Value, actual.Value);
            Assert.Equal(State(control.State), State(actual.State));
            if (StateValues.Read(actual.State, ["n"]).AsInteger().IsZero)
            {
                Assert.Equal(new BigInteger(17), actual.State["channel"]);
            }
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Compiled)]
    [InlineData(SamplingTier.Tiered)]
    public void InvalidCountOverridesFailBeforeRngAndPreserveThePreviousArray(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            GraphConfig source = Graph(new DrawManyNode
            {
                Id = "batch",
                OutputKey = "items",
                DistributionId = "a",
                CountExpressionId = "count",
                MaxCount = 4
            });
            CompileResult compiled = new GraphCompiler().Compile(source);
            CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(source);
            Assert.True(compiled.IsValid);
            Assert.True(reference.IsValid);
            var plan =
                Assert.IsAssignableFrom<ICompiledSampling<Dictionary<string, object?>, BigInteger>>(compiled.Program);
            foreach (var invalid in new object[] { -1, 5, "bad", ExprValue.Rational(1, 2), BigInteger.One << 80 })
            {
                var initial = new Dictionary<string, object?>
                {
                    ["n"] = invalid,
                    ["items"] = new object[] { 123 }
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
                Assert.Equal(123, Assert.IsType<object?[]>(runner.ExportState()["items"])[0]);
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
    public void SnapshotsStayIndependentAcrossRoundsRunnersAndPersistentMode(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            foreach (var length in new[] { 0, 30, 64, 65 })
            {
                CompileResult compiled = new GraphCompiler().Compile(Graph(Repeat(length)));
                Assert.True(compiled.IsValid);
                var plan =
                    Assert.IsAssignableFrom<ICompiledSampling<Dictionary<string, object?>, BigInteger>>(
                        compiled.Program);
                ISamplingRunner<Dictionary<string, object?>, BigInteger>[] runners = new[]
                {
                    plan.CreateRunner(new Dictionary<string, object?>()),
                    plan.CreateRunner(new Dictionary<string, object?>(), persistentKeys: ["items"])
                };
                var saved = new List<(Dictionary<string, object?> State, BigInteger Hash)>();
                for (var round = 0; round < 40; round++)
                {
                    ISamplingRunner<Dictionary<string, object?>, BigInteger> runner = runners[round % 2];
                    runner.Run(new SeededRandom(round), CancellationToken.None);
                    Dictionary<string, object?> state = runner.ExportState();
                    Assert.Equal(length, Assert.IsType<object?[]>(state["items"]).Length);
                    saved.Add((state, StateHasher.CanonicalHash(state)));
                }

                Assert.All(saved, snapshot => Assert.Equal(snapshot.Hash, StateHasher.CanonicalHash(snapshot.State)));
            }
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void BatchObservationsUseTheCompletedArrayAndKeepTheSameEvidence()
    {
        GraphConfig source = Graph(Repeat(3));
        MeasurementDefinition[] definitions =
        [
            new()
            {
                Id = "size",
                Name = "Completed size",
                NodeId = "channel",
                Value = new CallExpr
                {
                    Function = "length",
                    Args = [Field("items")]
                }
            }
        ];
        CompileResult compiled = new GraphCompiler().Compile(source, definitions);
        CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(source, definitions);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Assert.True(reference.IsValid);
        var options = new SampledConfig
        {
            MaxSpins = 128,
            Seed = 42,
            Measurements = definitions
        };
        SampledResult<Dictionary<string, object?>> actual =
            SampledInterpreter.Evaluate(compiled.Program!, new Dictionary<string, object?>(), options);
        SampledResult<Dictionary<string, object?>> expected =
            SampledInterpreter.Evaluate(reference.Program!, new Dictionary<string, object?>(), options);
        Assert.Equal(3, Assert.Single(actual.Measurements).Mean);
        Assert.Equal(JsonSerializer.Serialize(expected.Measurements), JsonSerializer.Serialize(actual.Measurements));
    }

    [Fact]
    public void CancellationBeforeTheFirstDrawCannotPublishAnAllocatedArray()
    {
        var layout = new Dictionary<string, int>
        {
            ["items"] = 0
        };
        var frame = new SamplingFrame(layout, [SamplingCell.FromRaw(new object[] { 123 })],
            new Dictionary<string, object?>());
        using var cancellation = new CancellationTokenSource();
        frame.Reset(cancellation.Token);
        SamplingDraw data = new SamplingShapes().BindDraw(Assets()["a"].Outcomes, BigInteger.One);
        var batch = new SamplingDrawMany([data], true, 0, null, 4, _ =>
        {
            cancellation.Cancel();
            return ExprValue.Number(4);
        });
        var rng = new SeededRandom(42);
        rng.EnableAudit();
        Assert.Throws<OperationCanceledException>(() => batch.Run(frame, rng, null));
        Assert.Equal(0, rng.AuditSnapshot(0)!.Words);
        Assert.Equal(123, Assert.IsType<object?[]>(frame.Export()["items"])[0]);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("negative")]
    [InlineData("large")]
    [InlineData("dynamic-limit")]
    [InlineData("dynamic-fixed")]
    [InlineData("schedule-limit")]
    [InlineData("count-type")]
    [InlineData("missing-expression")]
    [InlineData("mixed")]
    public void InvalidBatchContractsAreCodedCompilerErrors(string kind)
    {
        DrawManyNode batch = Repeat(2);
        batch = kind switch
        {
            "missing" => batch with
            {
                DistributionId = "missing"
            },
            "ambiguous" => batch with
            {
                DistributionIds = ["b"]
            },
            "negative" => batch with
            {
                Count = -1
            },
            "large" => batch with
            {
                Count = 4097
            },
            "dynamic-limit" => batch with
            {
                Count = null,
                CountExpressionId = "count"
            },
            "dynamic-fixed" => batch with
            {
                CountExpressionId = "count",
                MaxCount = 4
            },
            "schedule-limit" => batch with
            {
                DistributionId = null,
                DistributionIds = ["a"],
                Count = 2
            },
            "count-type" => batch with
            {
                Count = null,
                CountExpressionId = "count",
                MaxCount = 4
            },
            "missing-expression" => batch with
            {
                Count = null,
                CountExpressionId = "missing",
                MaxCount = 4
            },
            "mixed" => batch with
            {
                DistributionId = null,
                DistributionIds = ["a", "b"],
                Count = null
            },
            _ => batch
        };
        GraphConfig source = Graph(batch);
        if (kind == "count-type")
        {
            source.Expressions!["count"] = new ConstantExpr
            {
                Kind = ConstantKind.Boolean,
                Value = "true"
            };
        }

        if (kind == "mixed")
        {
            source.Distributions!["b"] = new DistributionAsset
            {
                Outcomes =
                [
                    new DrawWeight
                    {
                        OutcomeId = "text",
                        Weight = 1
                    }
                ]
            };
        }

        CompileResult result = new GraphCompiler().Compile(source);
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void BatchJsonAndParameterizedReusableCountRemainEditableAndNamespaced()
    {
        GraphConfig source = Graph(new DrawManyNode
        {
            Id = "batch",
            OutputKey = "items",
            DistributionId = "a",
            CountExpressionId = "count",
            MaxCount = 4
        });
        var inner = (DrawManyNode)source.Nodes[0];
        source.Nodes[0] = new LibraryNode
        {
            Id = "batch",
            MechanicName = "body",
            Outputs = Ports(),
            Parameters = new Dictionary<string, string>
            {
                ["amount"] = "3"
            }
        };
        source = source with
        {
            Mechanics = new Dictionary<string, CustomMechanic>
            {
                ["body"] = new()
                {
                    Name = "Ordered body",
                    Nodes =
                    [
                        inner with
                        {
                            Id = "inside",
                            CountExpressionId = "local-count"
                        }
                    ],
                    Expressions = new Dictionary<string, Expression>
                    {
                        ["local-count"] = new FieldAccessExpr
                        {
                            Target = "state",
                            Path = ["expressions", "amount"]
                        }
                    }
                }
            }
        };
        var json = JsonSerializer.Serialize(source, JsonOptions.Default);
        var parsed = JsonSerializer.Deserialize<GraphConfig>(json, JsonOptions.Default)!;
        CompileResult compiled = new GraphCompiler().Compile(parsed);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Assert.Equal(json, JsonSerializer.Serialize(parsed, JsonOptions.Default));
        Assert.DoesNotContain("boundDistributions", json);
        Assert.Equal("compiled-state-plan-v1", compiled.SamplingEngine);
        (BigInteger Value, Dictionary<string, object?> State) result =
            SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42);
        Assert.Equal(3, Assert.IsType<object?[]>(result.State["items"]).Length);
        Assert.Equal("Array", compiled.MeasurementSchema!.Fields.Single(field => field.Name == "items").Type);
    }

    [Theory]
    [InlineData("batch", 69, 4)]
    [InlineData("family", 169, 6)]
    [InlineData("vector", 59, 4)]
    public void LoopLivenessRetainsStateReadOnlyByStochasticOperands(string kind, int numerator, int denominator)
    {
        Node operation = kind switch
        {
            "batch" => new DrawManyNode
            {
                Id = "operation",
                OutputKey = "items",
                DistributionId = "a",
                CountExpressionId = "count",
                MaxCount = 2
            },
            "family" => new DrawNode
            {
                Id = "operation",
                StateWriteKey = "data",
                DistributionIds = ["a", "b"],
                DistributionIndexExpressionId = "count"
            },
            _ => new DrawNode
            {
                Id = "operation",
                StateWriteKey = "data",
                WeightVectorExpressionId = "weights",
                DrawWeights = [Outcome(2, 0), Outcome(7, 0)]
            }
        };
        operation = operation with
        {
            Inputs = Ports(),
            Outputs = Ports()
        };
        var source = new GraphConfig
        {
            SchemaVersion = "1.0",
            Distributions = Assets(),
            InitialState = new Dictionary<string, JsonElement>
            {
                ["n"] = JsonSerializer.SerializeToElement(0),
                ["total"] = JsonSerializer.SerializeToElement(0),
                ["sequence"] =
                    JsonSerializer.SerializeToElement(kind == "batch" ? new[] { 1, 2, 0 } : new[] { 1, 0, 1 }),
                ["left"] = JsonSerializer.SerializeToElement(new[] { 1, 1 }),
                ["right"] = JsonSerializer.SerializeToElement(new[] { 1, 3 })
            },
            Nodes =
            [
                new LoopNode
                {
                    Id = "repeat",
                    MaxIterations = 4,
                    StopConditionId = "done",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["body"] = new()
                        {
                            Name = "body",
                            Type = PortType.State
                        },
                        ["exit"] = new()
                        {
                            Name = "exit",
                            Type = PortType.State
                        }
                    }
                },
                operation,
                new ModifyStateNode
                {
                    Id = "set-n",
                    ExpressionId = "set-n",
                    OutputKey = "n",
                    Inputs = Ports(),
                    Outputs = Ports()
                },
                new ModifyStateNode
                {
                    Id = "add",
                    ExpressionId = "add",
                    OutputKey = "total",
                    Inputs = Ports(),
                    Outputs = Ports()
                },
                new MetricsSinkNode
                {
                    Id = "sink",
                    WinStateKey = "total",
                    WinCap = 1000,
                    Inputs = Ports()
                }
            ],
            Edges =
            [
                Edge("repeat", "operation") with
                {
                    SourcePort = "body"
                },
                Edge("operation", "set-n"), Edge("set-n", "add"),
                Edge("repeat", "sink") with
                {
                    SourcePort = "exit"
                }
            ],
            Expressions = new Dictionary<string, Expression>
            {
                ["count"] = Field("n"),
                ["weights"] = new IfExpr
                {
                    Condition = new CompareExpr
                    {
                        Op = CompareOp.Eq,
                        Left = Field("n"),
                        Right = Number(0)
                    },
                    ThenExpr = Field("left"),
                    ElseExpr = Field("right")
                },
                ["done"] = new CompareExpr
                {
                    Op = CompareOp.Gte,
                    Left = Field("__iter_repeat__"),
                    Right = Number(3)
                },
                ["set-n"] = new CallExpr
                {
                    Function = "index",
                    Args = [Field("sequence"), Field("__iter_repeat__")]
                },
                ["add"] = new BinaryExpr
                {
                    Op = BinaryOp.Add,
                    Left = Field("total"),
                    Right = kind == "batch" ? Sum() : Field("data")
                }
            }
        };
        CompileResult compiled = new GraphCompiler().Compile(source);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Dist<BigInteger> classic = ExactInterpreter
            .Evaluate(compiled.ReferenceProgram!, new Dictionary<string, object?>(), StateHasher.CanonicalHash)
            .ValueDistribution();
        (BigInteger actualNumerator, BigInteger actualDenominator) = classic.ExpectedBigIntegerValue();
        var expected = new Rational(numerator, denominator);
        Assert.Equal(expected, new Rational(actualNumerator, actualDenominator));
        StructuredExactResult structured = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        Assert.Equal(expected, structured.ExpectedWin);
    }
}
