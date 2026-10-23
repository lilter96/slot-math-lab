using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Optimization;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Optimization;

[Collection("SamplingOptions")]
public sealed class StateConstantPropagationTests
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

    private static BinaryExpr Add(Expression a, Expression b) => new()
    {
        Op = BinaryOp.Add,
        Left = a,
        Right = b
    };

    private static Dictionary<string, Port> Ports(params string[] names) =>
        (names.Length == 0 ? new[] { "state" } : names).ToDictionary(name => name, name => new Port
        {
            Name = name,
            Type = PortType.State
        });

    private static ModifyStateNode Write(string node, string expression, string key) => new()
    {
        Id = node,
        ExpressionId = expression,
        OutputKey = key,
        Inputs = Ports(),
        Outputs = Ports()
    };

    private static Edge Edge(string a, string b, string port = "state") => new()
    {
        Id = a + port + b,
        SourceNodeId = a,
        TargetNodeId = b,
        SourcePort = port,
        TargetPort = "state"
    };

    private static MetricsSinkNode Sink(string key = "win") => new()
    {
        Id = "sink",
        Inputs = Ports(),
        WinStateKey = key,
        WinCap = 1000
    };

    private static GraphConfig Graph(Node[] nodes, Edge[] edges, Dictionary<string, Expression> expressions) => new()
    {
        SchemaVersion = "1.0.0",
        Nodes = nodes,
        Edges = edges,
        Expressions = expressions,
        InitialState = new Dictionary<string, JsonElement>
        {
            ["stable"] = JsonSerializer.SerializeToElement(3),
            ["nums"] = JsonSerializer.SerializeToElement(new[] { 1, 2, 3 }),
            ["total"] = JsonSerializer.SerializeToElement(0)
        }
    };

    private static GraphConfig Straight() => Graph([Write("set", "five", "x"), Write("pay", "pay", "win"), Sink()],
        [Edge("set", "pay"), Edge("pay", "sink")], new Dictionary<string, Expression>
        {
            ["five"] = N(5),
            ["pay"] = Add(S("x"), N(2))
        });

    private static SamplingPreparation Prepared(CompileResult result)
    {
        Assert.True(result.IsValid, string.Join(';', result.Errors));
        return Assert.IsType<SamplingPlanSlot>(result.Program).Preparation;
    }

    private static Expression Executable(SamplingPreparation preparation, string node) =>
        preparation.Config.Expressions![
            ((ModifyStateNode)preparation.Config.Nodes.Single(item => item.Id == node)).ExpressionId!];

    [Fact]
    public void DominatingWritePropagatesWithoutRemovingStateOrChangingTheSource()
    {
        GraphConfig source = Straight();
        var before = JsonSerializer.Serialize(source, JsonOptions.Default);
        CompileResult compiled = new GraphCompiler().Compile(source);
        SamplingPreparation preparation = Prepared(compiled);
        Assert.Equal("7", Assert.IsType<ConstantExpr>(Executable(preparation, "pay")).Value);
        Assert.Contains(preparation.Propagation!.Changes, change => change.NodeId == "pay");
        Assert.Equal(3, preparation.Config.Nodes.Length);
        Assert.Equal(2, preparation.Config.Edges.Length);
        Assert.Equal(before, JsonSerializer.Serialize(source, JsonOptions.Default));
        Assert.Empty(MemorySsaVerifier.Verify(preparation.Ir.Memory));
        (BigInteger Value, Dictionary<string, object?> State) actual =
            SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42);
        Assert.Equal(new BigInteger(7), actual.Value);
        Assert.Equal(new BigInteger(5), actual.State["x"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SharedExpressionIdsAreSpecializedPerUse(bool overwrite)
    {
        GraphConfig graph = Graph(
            [
                Write("set", "five", "x"), Write("first", "shared", "a"), Write("change", "other", "x"),
                Write("last", "shared", "win"), Sink()
            ],
            [Edge("set", "first"), Edge("first", "change"), Edge("change", "last"), Edge("last", "sink")],
            new Dictionary<string, Expression>
            {
                ["five"] = N(5),
                ["other"] = overwrite ? N(9) : N(5),
                ["shared"] = Add(S("x"), N(1))
            });
        SamplingPreparation prepared = Prepared(new GraphCompiler().Compile(graph));
        Assert.Equal("6", Assert.IsType<ConstantExpr>(Executable(prepared, "first")).Value);
        Assert.Equal(overwrite ? "10" : "6", Assert.IsType<ConstantExpr>(Executable(prepared, "last")).Value);
        Assert.IsType<BinaryExpr>(graph.Expressions!["shared"]);
    }

    private static GraphConfig BranchGraph(bool same)
    {
        return Graph([
                new DrawNode
                {
                    Id = "random",
                    Outputs = Ports(),
                    StateWriteKey = "mode",
                    DrawWeights =
                    [
                        new DrawWeight
                        {
                            OutcomeId = "a",
                            Weight = 1,
                            Value = 0,
                            StateValue = N(0)
                        },
                        new DrawWeight
                        {
                            OutcomeId = "b",
                            Weight = 1,
                            Value = 1,
                            StateValue = N(1)
                        }
                    ]
                },
                new BranchNode
                {
                    Id = "branch",
                    ConditionId = "condition",
                    Inputs = Ports(),
                    Outputs = Ports("true", "false")
                },
                Write("a", "five", "x"), Write("b", "other", "x"), Write("pay", "pay", "win"), Sink()
            ],
            [
                Edge("random", "branch"), Edge("branch", "a", "true"), Edge("branch", "b", "false"), Edge("a", "pay"),
                Edge("b", "pay"), Edge("pay", "sink")
            ],
            new Dictionary<string, Expression>
            {
                ["condition"] = new CompareExpr
                {
                    Op = CompareOp.Eq,
                    Left = S("mode"),
                    Right = N(0)
                },
                ["five"] = N(5),
                ["other"] = N(same ? 5 : 9),
                ["pay"] = Add(S("x"), N(1))
            });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PhiJoinsKeepOnlyFactsCommonToAllReachingArms(bool same)
    {
        CompileResult compiled = new GraphCompiler().Compile(BranchGraph(same));
        SamplingPreparation prepared = Prepared(compiled);
        if (same)
        {
            Assert.Equal("6", Assert.IsType<ConstantExpr>(Executable(prepared, "pay")).Value);
        }
        else
        {
            Assert.IsType<BinaryExpr>(Executable(prepared, "pay"));
        }

        CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(BranchGraph(same));
        for (var seed = 0; seed < 32; seed++)
        {
            Assert.Equal(
                SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), seed).Value,
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed).Value);
        }
    }

    [Fact]
    public void ConstantConditionDoesNotPolluteFactsFromAnUnreachableArm()
    {
        GraphConfig graph = BranchGraph(false);
        graph.Expressions!["condition"] = new ConstantExpr
        {
            Kind = ConstantKind.Boolean,
            Value = "true"
        };
        SamplingPreparation prepared = Prepared(new GraphCompiler().Compile(graph));
        Assert.Equal("6", Assert.IsType<ConstantExpr>(Executable(prepared, "pay")).Value);
    }

    [Fact]
    public void SequentialFanoutSeesWritesFromThePrecedingContinuation()
    {
        GraphConfig graph = Graph([
                new GetStateNode
                {
                    Id = "fork",
                    Outputs = Ports()
                },
                Write("set", "five", "x"), Write("pay", "pay", "win"), Sink()
            ],
            [Edge("fork", "set"), Edge("fork", "pay"), Edge("set", "sink"), Edge("pay", "sink")],
            new Dictionary<string, Expression>
            {
                ["five"] = N(5),
                ["pay"] = Add(S("x"), N(2))
            });
        SamplingPreparation prepared = Prepared(new GraphCompiler().Compile(graph));
        Assert.Equal("7", Assert.IsType<ConstantExpr>(Executable(prepared, "pay")).Value);
    }

    [Fact]
    public void InitialFactOverridesUseAFreshUnguardedPreparation()
    {
        GraphConfig graph = Graph([Write("set", "copy", "x"), Write("pay", "pay", "win"), Sink()],
            [Edge("set", "pay"), Edge("pay", "sink")], new Dictionary<string, Expression>
            {
                ["copy"] = S("stable"),
                ["pay"] = Add(S("x"), N(2))
            });
        CompileResult compiled = new GraphCompiler().Compile(graph);
        SamplingPreparation prepared = Prepared(compiled);
        Assert.Equal("5", Assert.IsType<ConstantExpr>(Executable(prepared, "pay")).Value);
        CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(graph);
        foreach (var value in new[] { 0, 9, 21 })
        {
            var input = new Dictionary<string, object?>
            {
                ["stable"] = value
            };
            (BigInteger Value, Dictionary<string, object?> State) expected =
                SampledInterpreter.RunSingle(reference.Program!, input, 42);
            (BigInteger Value, Dictionary<string, object?> State) actual =
                SampledInterpreter.RunSingle(compiled.Program!, input, 42);
            Assert.Equal(expected.Value, actual.Value);
            Assert.Equal(new BigInteger(value + 2), actual.Value);
        }
    }

    [Fact]
    public void LoopCarriedWritesAreWidenedAndCannotInheritEntryConstants()
    {
        var loop = new LoopNode
        {
            Id = "loop",
            MaxIterations = 3,
            Inputs = Ports(),
            Outputs = Ports("body", "exit")
        };
        GraphConfig graph = Graph(
            [Write("initial", "zero", "x"), loop, Write("body", "step", "x"), Write("pay", "pay", "win"), Sink()],
            [Edge("initial", "loop"), Edge("loop", "body", "body"), Edge("loop", "pay", "exit"), Edge("pay", "sink")],
            new Dictionary<string, Expression>
            {
                ["zero"] = N(0),
                ["step"] = Add(S("x"), N(1)),
                ["pay"] = S("x")
            });
        CompileResult compiled = new GraphCompiler().Compile(graph);
        SamplingPreparation prepared = Prepared(compiled);
        Assert.IsType<BinaryExpr>(Executable(prepared, "body"));
        Assert.IsType<FieldAccessExpr>(Executable(prepared, "pay"));
        Assert.Equal(new BigInteger(3),
            SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42).Value);
    }

    [Fact]
    public void ScopedIteratorNamesDoNotReceiveARecurrenceFact()
    {
        var map = new MapExpr
        {
            StateKey = "nums",
            ItemName = "x",
            ItemType = ExprType.Number,
            Body = S("x")
        };
        GraphConfig graph = Graph(
            [Write("set", "five", "x"), Write("map", "map", "mapped"), Write("pay", "five", "win"), Sink()],
            [Edge("set", "map"), Edge("map", "pay"), Edge("pay", "sink")], new Dictionary<string, Expression>
            {
                ["five"] = N(5),
                ["map"] = map
            });
        CompileResult compiled = new GraphCompiler().Compile(graph);
        SamplingPreparation prepared = Prepared(compiled);
        (BigInteger Value, Dictionary<string, object?> State) actual =
            SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42);
        Assert.Equal(new object?[] { new BigInteger(1), new BigInteger(2), new BigInteger(3) },
            Assert.IsType<object?[]>(actual.State["mapped"]));
        Assert.IsType<FieldAccessExpr>(((MapExpr)Executable(prepared, "map")).Body);
    }

    [Fact]
    public void EagerErrorsRemainAtTheirRuntimePointAndCannotBecomeZeroFacts()
    {
        GraphConfig graph = Straight();
        graph.Expressions!["five"] = new BinaryExpr
        {
            Op = BinaryOp.Div,
            Left = N(1),
            Right = N(0)
        };
        CompileResult compiled = new GraphCompiler().Compile(graph);
        SamplingPreparation prepared = Prepared(compiled);
        Assert.IsType<BinaryExpr>(Executable(prepared, "set"));
        Assert.IsType<BinaryExpr>(Executable(prepared, "pay"));
        Assert.Equal(EvalErrorCodes.DivisionByZero,
            Assert.Throws<ExpressionEvaluationException>(() =>
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42)).Code);
    }

    [Fact]
    public void BudgetExhaustionDiscardsEveryPartialRewrite()
    {
        GraphConfig graph = Straight();
        var ir = CompilerIr.Build(graph, []);
        StatePropagationResult result = StateConstantPropagation.Run(graph, ir, ir.StableInitialValues, 1);
        Assert.True(result.BudgetReached);
        Assert.Same(graph, result.Config);
        Assert.Empty(result.Changes);
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Compiled)]
    [InlineData(SamplingTier.Tiered)]
    public void EnabledAndDisabledPassesKeepCompleteSeededState(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            GraphConfig graph = BranchGraph(true);
            var source = JsonSerializer.Serialize(graph, JsonOptions.Default);
            CompileResult optimized = new GraphCompiler().Compile(graph);
            CompileResult noPass = new GraphCompiler(simplifyExpressions: false).Compile(graph);
            CompileResult canonical = new GraphCompiler(optimizeSampling: false).Compile(graph);
            Assert.True(optimized.IsValid);
            Assert.True(noPass.IsValid);
            Assert.True(canonical.IsValid);
            for (var seed = 0; seed < 128; seed++)
            {
                (BigInteger Value, Dictionary<string, object?> State) expected =
                    SampledInterpreter.RunSingle(canonical.Program!, new Dictionary<string, object?>(), seed);
                foreach (Slot<Dictionary<string, object?>, BigInteger>? program in new[]
                         {
                             optimized.Program!, noPass.Program!
                         })
                {
                    (BigInteger Value, Dictionary<string, object?> State) actual =
                        SampledInterpreter.RunSingle(program, new Dictionary<string, object?>(), seed);
                    Assert.Equal(expected.Value, actual.Value);
                    Assert.Equal(
                        JsonSerializer.Serialize(expected.State.OrderBy(pair => pair.Key), JsonOptions.Default),
                        JsonSerializer.Serialize(actual.State.OrderBy(pair => pair.Key), JsonOptions.Default));
                }
            }

            Assert.Equal(source, JsonSerializer.Serialize(graph, JsonOptions.Default));
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void LegacyDefaultWritesPropagateTheirStoredIntegerNotTheFraction()
    {
        GraphConfig graph = Straight();
        graph.Nodes[0] = (ModifyStateNode)graph.Nodes[0] with
        {
            OutputKey = null
        };
        graph.Expressions!["five"] = new ConstantExpr
        {
            Kind = ConstantKind.Rational,
            Value = "5/2"
        };
        graph.Expressions["pay"] = Add(S("__modified__"), N(1));
        graph.InitialState!["__modified__"] = JsonSerializer.SerializeToElement(0);
        CompileResult compiled = new GraphCompiler().Compile(graph);
        SamplingPreparation prepared = Prepared(compiled);
        Assert.Equal("3", Assert.IsType<ConstantExpr>(Executable(prepared, "pay")).Value);
        Assert.Equal(new BigInteger(3),
            SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 0).Value);
    }

    [Fact]
    public void ZeroIterationLoopsDoNotClobberAFieldWrittenOnlyByTheBody()
    {
        GraphConfig graph = Graph([
                Write("set", "five", "x"), new LoopNode
                {
                    Id = "loop",
                    MaxIterations = 2,
                    StopConditionId = "done",
                    Inputs = Ports(),
                    Outputs = Ports("body", "exit")
                },
                Write("body", "nine", "x"), Write("pay", "pay", "win"), Sink()
            ],
            [Edge("set", "loop"), Edge("loop", "body", "body"), Edge("loop", "pay", "exit"), Edge("pay", "sink")],
            new Dictionary<string, Expression>
            {
                ["five"] = N(5),
                ["nine"] = N(9),
                ["pay"] = Add(S("x"), N(1)),
                ["done"] = new ConstantExpr
                {
                    Kind = ConstantKind.Boolean,
                    Value = "true"
                }
            });
        CompileResult compiled = new GraphCompiler().Compile(graph);
        SamplingPreparation prepared = Prepared(compiled);
        Assert.Equal("6", Assert.IsType<ConstantExpr>(Executable(prepared, "pay")).Value);
        Assert.Equal(new BigInteger(6),
            SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 0).Value);
    }

    [Fact]
    public void OpaqueWritesInvalidateDominatingFactsWithoutNameDispatch()
    {
        GraphConfig graph = Straight();
        graph = graph with
        {
            Nodes =
            [
                graph.Nodes[0], new MapNode
                {
                    Id = "opaque",
                    TransformId = "arbitrary",
                    Inputs = Ports(),
                    Outputs = Ports()
                },
                graph.Nodes[1], graph.Nodes[2]
            ],
            Edges = [Edge("set", "opaque"), Edge("opaque", "pay"), Edge("pay", "sink")]
        };
        var ir = CompilerIr.Build(graph, []);
        StatePropagationResult result = StateConstantPropagation.Run(graph, ir, ir.StableInitialValues);
        Assert.DoesNotContain(result.Changes, change => change.NodeId == "pay");
    }

    [Fact]
    public void MeasurementPointsStillSeeEveryAuthoredStateWrite()
    {
        GraphConfig graph = Straight();
        graph.InitialState!["x"] = JsonSerializer.SerializeToElement(1);
        MeasurementDefinition[] definitions =
        [
            new()
            {
                Id = "before",
                Name = "Before",
                NodeId = "set",
                Value = S("x")
            },
            new()
            {
                Id = "after",
                Name = "After",
                NodeId = "pay",
                Value = S("x")
            },
            new()
            {
                Id = "win",
                Name = "Win",
                NodeId = "sink",
                Value = S("win")
            }
        ];
        CompileResult compiled = new GraphCompiler().Compile(graph, definitions);
        CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(graph, definitions);
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
        Assert.Equal(JsonSerializer.Serialize(expected.Measurements), JsonSerializer.Serialize(actual.Measurements));
        Assert.Equal(new double?[] { 1.0, 5.0, 7.0 }, actual.Measurements.Select(measurement => measurement.Mean));
    }

    [Fact]
    public void DrawTranscriptAndFollowingWordAreUnchangedByConditionalPropagation()
    {
        GraphConfig graph = BranchGraph(true);
        var prefix = (DrawNode)graph.Nodes[0];
        graph = graph with
        {
            Nodes =
            [
                .. graph.Nodes[..^1], prefix with
                {
                    Id = "suffix",
                    StateWriteKey = "suffix",
                    Inputs = Ports()
                },
                graph.Nodes[^1]
            ],
            Edges =
            [
                .. graph.Edges.Where(edge => edge.SourceNodeId != "pay"), Edge("pay", "suffix"), Edge("suffix", "sink")
            ]
        };
        CompileResult optimized = new GraphCompiler().Compile(graph);
        CompileResult baseline = new GraphCompiler(simplifyExpressions: false).Compile(graph);
        Assert.True(optimized.IsValid);
        Assert.True(baseline.IsValid);
        ISamplingRunner<Dictionary<string, object?>, BigInteger> actual =
            ((ICompiledSampling<Dictionary<string, object?>, BigInteger>)optimized.Program!).CreateRunner(
                new Dictionary<string, object?>());
        ISamplingRunner<Dictionary<string, object?>, BigInteger> reference =
            ((ICompiledSampling<Dictionary<string, object?>, BigInteger>)baseline.Program!).CreateRunner(
                new Dictionary<string, object?>());
        for (var seed = 0; seed < 32; seed++)
        {
            var a = new SeededRandom(seed);
            var b = new SeededRandom(seed);
            a.EnableAudit();
            b.EnableAudit();
            Assert.Equal(reference.Run(b, default), actual.Run(a, default));
            Assert.Equal(JsonSerializer.Serialize(b.AuditSnapshot(64)), JsonSerializer.Serialize(a.AuditSnapshot(64)));
            Assert.Equal(b.NextUInt64(), a.NextUInt64());
            Assert.Equal(
                JsonSerializer.Serialize(reference.ExportState().OrderBy(pair => pair.Key), JsonOptions.Default),
                JsonSerializer.Serialize(actual.ExportState().OrderBy(pair => pair.Key), JsonOptions.Default));
        }
    }

    [Fact]
    public void WorkerReplicasShareOneVerifiedPreparation()
    {
        CompileResult compiled = new GraphCompiler().Compile(Straight());
        var wrapper = Assert.IsType<SamplingPlanSlot>(compiled.Program);
        var first = (SamplingPlan)wrapper.Rent();
        var second = (SamplingPlan)wrapper.Rent();
        try
        {
            Assert.Same(wrapper.Preparation, first.Preparation);
            Assert.Same(first.Preparation, second.Preparation);
        }
        finally
        {
            wrapper.Return(first);
            wrapper.Return(second);
        }
    }

    [Fact]
    public void UnknownReadsInAnUntakenExpressionBranchDoNotBlockAConstant()
    {
        GraphConfig graph = Straight();
        var random = (DrawNode)BranchGraph(false).Nodes[0];
        graph = graph with
        {
            Nodes = [random, .. graph.Nodes],
            Edges = [Edge("random", "set"), .. graph.Edges]
        };
        graph.Expressions!["five"] = new IfExpr
        {
            Condition = new CompareExpr
            {
                Op = CompareOp.Gt,
                Left = S("stable"),
                Right = N(0)
            },
            ThenExpr = N(5),
            ElseExpr = S("mode")
        };
        CompileResult compiled = new GraphCompiler().Compile(graph);
        SamplingPreparation prepared = Prepared(compiled);
        Assert.Equal("7", Assert.IsType<ConstantExpr>(Executable(prepared, "pay")).Value);
        Assert.Equal(new BigInteger(7),
            SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42).Value);
    }

    [Fact]
    public void ServiceCounterStorageDoesNotBecomeAnOrdinaryScalarFact()
    {
        GraphConfig graph = Graph([
                new LoopNode
                {
                    Id = "loop",
                    MaxIterations = 2,
                    StopConditionId = "done",
                    Outputs = Ports("body", "exit")
                },
                Write("body", "five", "x"), Write("touch", "counter", "__iter_loop__"), Write("pay", "five", "win"),
                Sink()
            ],
            [Edge("loop", "body", "body"), Edge("loop", "touch", "exit"), Edge("touch", "pay"), Edge("pay", "sink")],
            new Dictionary<string, Expression>
            {
                ["done"] = new ConstantExpr
                {
                    Kind = ConstantKind.Boolean,
                    Value = "true"
                },
                ["five"] = N(5),
                ["counter"] = S("__iter_loop__")
            });
        CompileResult compiled = new GraphCompiler().Compile(graph);
        SamplingPreparation prepared = Prepared(compiled);
        Assert.IsType<FieldAccessExpr>(Executable(prepared, "touch"));
        (BigInteger Value, Dictionary<string, object?> State) result =
            SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 0);
        CompileResult baseline = new GraphCompiler(simplifyExpressions: false).Compile(graph);
        (BigInteger Value, Dictionary<string, object?> State) expected =
            SampledInterpreter.RunSingle(baseline.Program!, new Dictionary<string, object?>(), 0);
        var expectedCounter = Assert.IsAssignableFrom<object>(expected.State["__iter_loop__"]);
        var actualCounter = Assert.IsAssignableFrom<object>(result.State["__iter_loop__"]);
        Assert.Equal(expectedCounter.GetType(), actualCounter.GetType());
        Assert.Equal(expectedCounter, actualCounter);
    }
}
