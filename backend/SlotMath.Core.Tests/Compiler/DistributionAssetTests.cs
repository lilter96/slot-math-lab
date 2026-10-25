using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Compiler;

[Collection("SamplingOptions")]
public class DistributionAssetTests
{
    private static DrawWeight[] Outcomes() =>
    [
        new()
        {
            OutcomeId = "999",
            Weight = 0,
            Value = 999
        },
        new()
        {
            OutcomeId = "0",
            Weight = 1,
            Value = 0
        },
        new()
        {
            OutcomeId = "4",
            Weight = 2,
            Value = 4
        },
        new()
        {
            OutcomeId = "11",
            Weight = 3,
            Value = 11
        },
        new()
        {
            OutcomeId = "991",
            Weight = 0,
            Value = 991
        }
    ];

    private static Dictionary<string, Port> Ports() => new()
    {
        ["state"] = new Port
        {
            Name = "state",
            Type = PortType.State
        }
    };

    private static Expression ReadNumber(string key) => new CallExpr
    {
        Function = "toNumber",
        Args =
        [
            new FieldAccessExpr
            {
                Target = "state",
                Path = [key]
            }
        ]
    };

    private static GraphConfig Graph(bool referenced = true)
    {
        DrawWeight[] outcomes = Outcomes();
        return new GraphConfig
        {
            SchemaVersion = "1.0",
            Distributions = referenced
                ? new Dictionary<string, DistributionAsset>
                {
                    ["ordered"] = new()
                    {
                        Outcomes = outcomes
                    }
                }
                : null,
            Nodes =
            [
                new DrawNode
                {
                    Id = "first",
                    DistributionId = referenced ? "ordered" : null,
                    DrawWeights = referenced ? null : outcomes,
                    StateWriteKey = "first",
                    Outputs = Ports()
                },
                new DrawNode
                {
                    Id = "second",
                    DistributionId = referenced ? "ordered" : null,
                    DrawWeights = referenced ? null : outcomes,
                    StateWriteKey = "second",
                    Inputs = Ports(),
                    Outputs = Ports()
                },
                new ModifyStateNode
                {
                    Id = "sum",
                    OutputKey = "win",
                    ExpressionId = "sum",
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
            ],
            Edges = [Edge("first", "second"), Edge("second", "sum"), Edge("sum", "sink")],
            Expressions = new Dictionary<string, Expression>
            {
                ["sum"] = new BinaryExpr
                {
                    Op = BinaryOp.Add,
                    Left = ReadNumber("first"),
                    Right = ReadNumber("second")
                }
            }
        };
    }

    private static Edge Edge(string source, string target) => new()
    {
        Id = source + "-" + target,
        SourceNodeId = source,
        TargetNodeId = target,
        SourcePort = "state",
        TargetPort = "state"
    };

    [Fact]
    public void BindingSharesOnePrivateSnapshotAcrossRootAndReusableDefinitions()
    {
        GraphConfig source = Graph();
        source = source with
        {
            Mechanics = new Dictionary<string, CustomMechanic>
            {
                ["reusable"] = new()
                {
                    Name = "Reusable",
                    Nodes = [source.Nodes[0]]
                }
            }
        };
        var before = JsonSerializer.Serialize(source, JsonOptions.Default);
        (GraphConfig resolved, IReadOnlyList<CompileError> errors) = DistributionResolver.Resolve(source);
        Assert.Empty(errors);
        var first = Assert.IsType<DrawNode>(resolved.Nodes[0]);
        Assert.Null(first.DistributionId);
        Assert.Same(first.DrawWeights, Assert.IsType<DrawNode>(resolved.Nodes[1]).DrawWeights);
        Assert.Same(first.DrawWeights, Assert.IsType<DrawNode>(resolved.Mechanics!["reusable"].Nodes[0]).DrawWeights);
        Assert.NotSame(source.Distributions!["ordered"].Outcomes, first.DrawWeights);
        Assert.Equal(before, JsonSerializer.Serialize(source, JsonOptions.Default));
        source.Distributions["ordered"].Outcomes[2] = new DrawWeight
        {
            OutcomeId = "changed",
            Weight = 1
        };
        Assert.Equal("4", first.DrawWeights![2].OutcomeId);
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Compiled)]
    [InlineData(SamplingTier.Tiered)]
    public void AllSamplingTiersKeepValuesStateAndTheFollowingRandomWord(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            CompileResult compiled = new GraphCompiler().Compile(Graph());
            CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(Graph(false));
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            Assert.True(reference.IsValid, string.Join(';', reference.Errors));
            var plan =
                Assert.IsAssignableFrom<ICompiledSampling<Dictionary<string, object?>, BigInteger>>(compiled.Program);
            ISamplingRunner<Dictionary<string, object?>, BigInteger> runner =
                plan.CreateRunner(new Dictionary<string, object?>());
            DrawWeight[] outcomes = Outcomes();
            AliasMethod alias = WeightSet.FromIntegers(outcomes.Select(outcome => outcome.Weight).ToArray()).AliasTable;
            for (var seed = -64; seed < 64; seed++)
            {
                SeededRandom actualRandom = SeededRandom.ForStream(unchecked((ulong)seed), 0);
                SeededRandom oracleRandom = SeededRandom.ForStream(unchecked((ulong)seed), 0);
                actualRandom.EnableAudit();
                oracleRandom.EnableAudit();
                DrawWeight first = outcomes[alias.Sample(oracleRandom)];
                DrawWeight second = outcomes[alias.Sample(oracleRandom)];
                BigInteger actual = runner.Run(actualRandom, CancellationToken.None);
                Assert.Equal(new BigInteger(first.Value + second.Value), actual);
                Assert.Equal(oracleRandom.AuditSnapshot(0), actualRandom.AuditSnapshot(0));
                Assert.Equal(oracleRandom.NextUInt64(), actualRandom.NextUInt64());
                (BigInteger value, Dictionary<string, object?> state) =
                    SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), seed);
                Assert.Equal(value, actual);
                Assert.Equal(JsonSerializer.Serialize(state.OrderBy(pair => pair.Key), JsonOptions.Default),
                    JsonSerializer.Serialize(runner.ExportState().OrderBy(pair => pair.Key), JsonOptions.Default));
            }
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void BothExactPathsMatchAnIndependentClosedForm()
    {
        CompileResult compiled = new GraphCompiler().Compile(Graph());
        Assert.True(compiled.IsValid);
        Dist<BigInteger> law = ExactInterpreter.Evaluate(compiled.ReferenceProgram!, new Dictionary<string, object?>(),
            StateHasher.CanonicalHash).ValueDistribution();
        (BigInteger numerator, BigInteger denominator) = law.ExpectedBigIntegerValue();
        Assert.Equal(new Rational(41, 3), new Rational(numerator, denominator));
        StructuredExactResult structured = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        Assert.Equal(new Rational(41, 3), structured.ExpectedWin);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("negative")]
    [InlineData("zero")]
    [InlineData("inline")]
    [InlineData("expression")]
    public void InvalidAssetContractsFailBeforeExecution(string kind)
    {
        GraphConfig graph = Graph();
        DrawWeight[] outcomes = kind switch
        {
            "empty" => [],
            "negative" =>
            [
                new DrawWeight
                {
                    OutcomeId = "a",
                    Weight = -1
                }
            ],
            "zero" =>
            [
                new DrawWeight
                {
                    OutcomeId = "a",
                    Weight = 0
                }
            ],
            _ => Outcomes()
        };
        graph = graph with
        {
            Distributions = kind == "missing"
                ? null
                : new Dictionary<string, DistributionAsset>
                {
                    ["ordered"] = new()
                    {
                        Outcomes = outcomes
                    }
                }
        };
        var first = (DrawNode)graph.Nodes[0];
        graph.Nodes[0] = kind switch
        {
            "inline" => first with
            {
                DrawWeights = Outcomes()
            },
            "expression" => first with
            {
                WeightExpressionId = "legacy"
            },
            _ => first
        };
        CompileResult compiled = new GraphCompiler().Compile(graph);
        Assert.False(compiled.IsValid);
        Assert.Contains(compiled.Errors, error => error.Code == ErrorCodes.InvalidGraph);
        Assert.NotEmpty(GraphValidator.Validate(graph, null));
    }

    [Fact]
    public void AssetLimitsBoundWorkAndWeightSumsDoNotOverflowInt64()
    {
        GraphConfig graph = Graph();
        graph.Distributions!["ordered"] = new DistributionAsset
        {
            Outcomes =
            [
                new DrawWeight
                {
                    OutcomeId = "0",
                    Weight = long.MaxValue
                },
                new DrawWeight
                {
                    OutcomeId = "1",
                    Weight = long.MaxValue
                }
            ]
        };
        Assert.Empty(DistributionResolver.Resolve(graph).Errors);
        graph.Distributions["ordered"] = new DistributionAsset
        {
            Outcomes = Enumerable.Repeat(Outcomes()[1], DistributionResolver.MaxOutcomes + 1).ToArray()
        };
        Assert.NotEmpty(DistributionResolver.Resolve(graph).Errors);
    }

    [Fact]
    public void CatalogAndAggregateOutcomeBudgetsAreCheckedBeforeSnapshots()
    {
        GraphConfig graph = Graph();
        var small = new DistributionAsset
        {
            Outcomes = Outcomes()
        };
        graph = graph with
        {
            Distributions = Enumerable.Range(0, DistributionResolver.MaxAssets + 1)
                .ToDictionary(index => index.ToString(), _ => small)
        };
        Assert.NotEmpty(DistributionResolver.Resolve(graph).Errors);
        var large = new DistributionAsset
        {
            Outcomes = Enumerable.Repeat(Outcomes()[1], DistributionResolver.MaxOutcomes).ToArray()
        };
        graph = graph with
        {
            Distributions = Enumerable.Range(0, 5).ToDictionary(index => index.ToString(), _ => large)
        };
        (GraphConfig original, IReadOnlyList<CompileError> errors) = DistributionResolver.Resolve(graph);
        Assert.Same(graph, original);
        Assert.Single(errors);
        Assert.Contains("total limit", errors[0].Message);
    }

    [Fact]
    public void SourceMutationCannotChangeAPlanBeforeTieredCodeIsGenerated()
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = SamplingTier.Tiered;
            GraphConfig source = Graph();
            CompileResult compiled = new GraphCompiler().Compile(source);
            CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(Graph(false));
            Assert.True(compiled.IsValid);
            Assert.True(reference.IsValid);
            for (var index = 0; index < source.Distributions!["ordered"].Outcomes.Length; index++)
            {
                source.Distributions["ordered"].Outcomes[index] = new DrawWeight
                {
                    OutcomeId = "999",
                    Weight = 1,
                    Value = 999
                };
            }

            for (var seed = 0; seed < 128; seed++)
            {
                Assert.Equal(
                    SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), seed).Value,
                    SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed).Value);
            }
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void ReferencedDataInsideAReusableSubgraphUsesTheSameCompiledPath()
    {
        GraphConfig graph = Graph();
        Node inner = graph.Nodes[0];
        graph.Nodes[0] = new LibraryNode
        {
            Id = inner.Id,
            MechanicName = "arbitrary-body",
            Outputs = Ports()
        };
        graph = graph with
        {
            Mechanics = new Dictionary<string, CustomMechanic>
            {
                ["arbitrary-body"] = new()
                {
                    Name = "Reusable",
                    Nodes = [inner]
                }
            }
        };
        CompileResult compiled = new GraphCompiler().Compile(graph);
        CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(Graph(false));
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Assert.Equal("compiled-state-plan-v1", compiled.SamplingEngine);
        for (var seed = 0; seed < 32; seed++)
        {
            Assert.Equal(
                SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), seed).Value,
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed).Value);
        }
    }

    [Fact]
    public void BoundOperandsAreInternedByCompleteOrderedDataAndScale()
    {
        var pool = new SamplingShapes();
        DrawWeight[] outcomes = Outcomes();
        SamplingDraw first = pool.BindDraw(outcomes, BigInteger.One);
        Assert.Same(first, pool.BindDraw(Outcomes(), BigInteger.One));
        Assert.NotSame(first, pool.BindDraw(Outcomes(), new BigInteger(10)));
        Assert.NotSame(first, pool.BindDraw(outcomes.Reverse().ToArray(), BigInteger.One));
        outcomes[2] = outcomes[2] with
        {
            Value = 19
        };
        Assert.NotSame(first, pool.BindDraw(outcomes, BigInteger.One));
        Assert.Equal(4, pool.BoundDrawCount);
        Assert.Equal("4", first.StateValues[2].StringValue);
    }

    [Fact]
    public void LegacySerializationDoesNotAcquireNewDefaultProperties()
    {
        var json = JsonSerializer.Serialize(Graph(false), JsonOptions.Default);
        Assert.DoesNotContain("distributionId", json);
        Assert.DoesNotContain("distributions", json);
        var roundTrip = JsonSerializer.Deserialize<GraphConfig>(
            JsonSerializer.Serialize(Graph(), JsonOptions.Default), JsonOptions.Default)!;
        Assert.True(new GraphCompiler().Compile(roundTrip).IsValid);
    }
}
