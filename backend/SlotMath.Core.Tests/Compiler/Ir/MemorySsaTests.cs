using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Compiler.Ir;

public sealed class MemorySsaTests
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

    private static Dictionary<string, Port> Ports(params string[] names) =>
        (names.Length == 0 ? new[] { "state" } : names).ToDictionary(name => name, name => new Port
        {
            Name = name,
            Type = PortType.State
        });

    private static Edge Edge(string a, string b, string port = "state") => new()
    {
        Id = a + port + b,
        SourceNodeId = a,
        TargetNodeId = b,
        SourcePort = port,
        TargetPort = "state"
    };

    private static ModifyStateNode Write(string id, string expression, string field) => new()
    {
        Id = id,
        ExpressionId = expression,
        OutputKey = field,
        Inputs = Ports(),
        Outputs = Ports()
    };

    private static MetricsSinkNode Sink(string? win = "x") => new()
    {
        Id = "sink",
        WinStateKey = win,
        WinCap = 1000,
        Inputs = Ports()
    };

    private static GraphConfig Graph(Node[] nodes, Edge[] edges, Dictionary<string, Expression>? expressions = null) =>
        new()
        {
            SchemaVersion = "1.0.0",
            Nodes = nodes,
            Edges = edges,
            Expressions = expressions ?? new Dictionary<string, Expression>
            {
                ["one"] = N(1),
                ["two"] = N(2),
                ["x"] = S("x")
            },
            InitialState = new Dictionary<string, JsonElement>
            {
                ["x"] = JsonSerializer.SerializeToElement(7),
                ["stable"] = JsonSerializer.SerializeToElement(9)
            }
        };

    private static CompileResult Compile(GraphConfig graph, params MeasurementDefinition[] measurements)
    {
        CompileResult result = new GraphCompiler().Compile(graph, measurements);
        Assert.True(result.IsValid, string.Join(';', result.Errors));
        Assert.Empty(MemorySsaVerifier.Verify(result.Ir!.Memory));
        return result;
    }

    [Fact]
    public void DisjointWritesDoNotClobberAReadButTheSameFieldDoes()
    {
        GraphConfig graph = Graph([Write("y", "one", "y"), Write("x", "two", "x"), Sink()],
            [Edge("y", "x"), Edge("x", "sink")]);
        CompileResult compiled = Compile(graph);
        MemorySsa memory = compiled.Ir!.Memory;
        MemoryFunction function = memory.Functions[new ContinuationKey("y", "sink")];
        MemoryStep writeY = function.Body.Steps.Single(step => step.Phase == "node-commit");
        Assert.True(memory.SameValueSource(writeY.Input, writeY.Result, "x"));
        Assert.False(memory.SameValueSource(writeY.Input, writeY.Result, "y"));
        MemoryStep call = function.Body.Steps.Single(step => step.Call is not null);
        Assert.False(memory.SameValueSource(writeY.Result, call.Result, "x"));
        Assert.True(memory.SameValueSource(writeY.Result, call.Result, "stable"));
        Assert.DoesNotContain("x", compiled.Ir.StableInitialValues.Keys);
        Assert.Contains("stable", compiled.Ir.StableInitialValues.Keys);
    }

    [Fact]
    public void SequentialFanoutPassesPreviousCallMemoryToTheNextContinuation()
    {
        GraphConfig graph = Graph([
                new GetStateNode
                {
                    Id = "fork",
                    Outputs = Ports()
                },
                Write("a", "one", "x"), Write("b", "x", "y"), Sink("y")
            ],
            [Edge("fork", "a"), Edge("fork", "b"), Edge("a", "sink"), Edge("b", "sink")]);
        CompileResult compiled = Compile(graph);
        MemoryFunction function = compiled.Ir!.Memory.Functions[new ContinuationKey("fork", "sink")];
        MemoryStep[] calls = function.Body.Steps.Where(step => step.Call is not null).ToArray();
        Assert.Equal(new[] { "a", "b" }, calls.Select(step => step.Call!.Target.NodeId));
        Assert.Equal(calls[0].Result, calls[1].Input);
        Assert.All(calls, step => Assert.Equal(FlowChannel.OriginalInput, step.Call!.Channel));
        Assert.DoesNotContain(function.Body.Steps, step => step.Kind == MemoryStepKind.Branch);
        Assert.Equal(BigInteger.One,
            SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42).Value);
    }

    [Fact]
    public void BranchMergesBothMemoryAlternativesAndAnUnwrittenArm()
    {
        var branch = new BranchNode
        {
            Id = "branch",
            ConditionId = "condition",
            Outputs = Ports("true", "false")
        };
        GraphConfig graph = Graph([branch, Write("a", "one", "x"), Write("b", "two", "y"), Sink()],
            [Edge("branch", "a", "true"), Edge("branch", "b", "false"), Edge("a", "sink"), Edge("b", "sink")]);
        graph.Expressions!["condition"] = new CompareExpr
        {
            Op = CompareOp.Gt,
            Left = S("stable"),
            Right = N(0)
        };
        CompileResult compiled = Compile(graph);
        MemorySsa memory = compiled.Ir!.Memory;
        MemoryFunction function = memory.Functions[new ContinuationKey("branch", "sink")];
        MemoryStep fork = function.Body.Steps.Single(step => step.Kind == MemoryStepKind.Branch);
        Assert.Equal(MemoryKind.Phi, memory.Versions[fork.Result].Kind);
        Assert.Equal(2, fork.Regions.Count);
        Assert.All(fork.Regions, region => Assert.Equal(fork.Input, region.Input));
        Assert.Equal(fork.Regions.Select(region => region.Result).Order(), memory.Versions[fork.Result].Inputs.Order());
        ReachingMemory reaching = memory.ReachingDefinitions(fork.Result, "x");
        Assert.Contains(function.Body.Input, reaching.Definitions);
        Assert.Contains(reaching.Definitions, id => memory.Versions[id].Writes.Contains("x"));
        Assert.True(memory.SameValueSource(function.Body.Input, fork.Result, "stable"));
    }

    private static GraphConfig LoopGraph()
    {
        var loop = new LoopNode
        {
            Id = "loop",
            MaxIterations = 2,
            StopConditionId = "stop",
            ExitReason = new ConstantExpr
            {
                Kind = ConstantKind.String,
                Value = "done"
            },
            Outputs = Ports("body", "exit")
        };
        return Graph([loop, Write("body", "one", "x"), Sink()],
            [Edge("loop", "body", "body"), Edge("loop", "sink", "exit")],
            new Dictionary<string, Expression>
            {
                ["one"] = N(1),
                ["stop"] = new CompareExpr
                {
                    Op = CompareOp.Eq,
                    Left = S("stable"),
                    Right = N(0)
                }
            });
    }

    [Fact]
    public void LoopHeaderHasEntryAndBackedgeAndReadsCountersAfterBody()
    {
        CompileResult compiled = Compile(LoopGraph());
        MemorySsa memory = compiled.Ir!.Memory;
        MemoryFunction function = memory.Functions[new ContinuationKey("loop", "sink")];
        MemoryStep loop = function.Body.Steps.Single(step => step.Kind == MemoryStepKind.Loop);
        MemoryVersion phi = memory.Versions[loop.LoopHeader!.Value];
        Assert.Equal(MemoryKind.Phi, phi.Kind);
        Assert.Equal(loop.Input, phi.Inputs[0]);
        Assert.Equal(loop.Regions[1].Result, phi.Inputs[1]);
        MemoryRegion body = loop.Regions[1];
        MemoryStep call = body.Steps.Single(step => step.Call is not null);
        Assert.Equal(FlowChannel.Null, call.Call!.Channel);
        MemoryStep counterRead = body.Steps.Single(step => step.Phase == "counter-after-body");
        Assert.Equal(call.Result, counterRead.Input);
        Assert.True(memory.Versions[body.Result].MayWrite);
        ReachingMemory sources = memory.ReachingDefinitions(phi.Id, "x");
        Assert.True(sources.Definitions.Count >= 2); // worklist terminates through the cyclic header.
        Assert.True(memory.SameValueSource(function.Body.Input, loop.Result, "stable"));
    }

    [Fact]
    public void CapShortCircuitsStopAndExitReasonExpressions()
    {
        MemorySsa memory = Compile(LoopGraph()).Ir!.Memory;
        MemoryStep loop = memory.Functions[new ContinuationKey("loop", "sink")].Body.Steps
            .Single(step => step.Kind == MemoryStepKind.Loop);
        MemoryRegion guard = loop.Regions[0];
        Assert.Equal("cap-before-stop", guard.Steps[1].Phase);
        MemoryStep conditional = guard.Steps.Single(step => step.Phase == "cap-short-circuit");
        Assert.Equal("stop-condition", Assert.Single(Assert.Single(conditional.Regions).Steps).Phase);
        Assert.DoesNotContain(guard.Steps, step => step.Expression is not null);
        MemoryRegion exit = loop.Regions[2];
        Assert.Equal("exit-cap-test", exit.Steps[0].Phase);
        Assert.Equal("exit-reason",
            Assert.Single(Assert.Single(exit.Steps.Single(step => step.Phase == "reason-short-circuit").Regions).Steps)
                .Phase);
        Assert.True(exit.Steps.Single(step => step.Phase == "loop-total").Input > exit.Input);
    }

    [Fact]
    public void UnknownWritesClobberEveryFieldWithoutTransformIdInspection()
    {
        GraphConfig graph = Graph([
            new MapNode
            {
                Id = "opaque",
                TransformId = "ignored",
                Outputs = Ports()
            },
            Sink()
        ], [Edge("opaque", "sink")]);
        var ir = CompilerIr.Build(graph, []);
        MemorySsa memory = ir.Memory;
        Assert.Empty(MemorySsaVerifier.Verify(memory));
        Assert.True(memory.UnknownWrites);
        Assert.Empty(ir.StableInitialValues);
        MemoryFunction function = memory.Functions[new ContinuationKey("opaque", "sink")];
        Assert.True(memory.ReachingDefinitions(function.Body.Result, "stable").Unknown);
        Assert.False(memory.SameValueSource(function.Body.Input, function.Body.Result, "stable"));
    }

    [Fact]
    public void MultipleEntriesAndFinalSettlementShareOneOrderedState()
    {
        GraphConfig graph = Graph([
            Write("a", "one", "x"), Write("b", "x", "y"), Sink("y") with
            {
                Settlement = new MonetarySettlement()
            }
        ], [Edge("a", "sink"), Edge("b", "sink")]);
        CompileResult compiled = Compile(graph);
        MemorySsa memory = compiled.Ir!.Memory;
        MemoryStep[] calls = memory.Root.Steps.Where(step => step.Call is not null).ToArray();
        Assert.Equal(calls[0].Result, calls[1].Input);
        MemoryStep settlement = memory.Root.Steps.Single(step => step.Phase == "settlement-commit");
        Assert.Equal(calls[1].Result, settlement.Input);
        Assert.True(memory.Versions[settlement.Result].Writes.SetEquals(MonetarySettlement.EvidenceKeys));
        Assert.Equal("exportable-final-state", memory.Root.Steps.Last().Phase);
        MemoryFunction sinkFunction = memory.Functions[new ContinuationKey("sink", "sink")];
        Assert.Equal(sinkFunction.Body.Input, sinkFunction.Body.Result);
    }

    [Fact]
    public void MeasurementReadsAreBeforeTheContinuationAndRemainConditional()
    {
        GraphConfig graph = Graph([Write("a", "one", "x"), Sink()], [Edge("a", "sink")]);
        var measurement = new MeasurementDefinition
        {
            Id = "m",
            Name = "Before",
            NodeId = "a",
            Value = S("x")
        };
        CompileResult compiled = Compile(graph, measurement);
        MemorySsa memory = compiled.Ir!.Memory;
        MemoryStep first = memory.Functions[new ContinuationKey("a", "sink")].Body.Steps[0];
        Assert.Equal(MemoryStepKind.Observe, first.Kind);
        Assert.Equal("collector-if-present", Assert.Single(first.Regions).Role);
        Assert.Contains("x", first.Effects.Reads);
        Assert.True(first.Effects.Flags.HasFlag(IrEffect.Observe));
    }

    [Fact]
    public void ReusedTailFunctionsDoNotExpandEveryExecutionPath()
    {
        var nodes = new List<Node>
        {
            Sink()
        };
        var edges = new List<Edge>();
        for (var layer = 0; layer < 12; layer++)
        {
            nodes.Add(new GetStateNode
            {
                Id = "n" + layer,
                Outputs = Ports()
            });
            var next = layer == 11 ? "sink" : "n" + (layer + 1);
            edges.Add(Edge("n" + layer, next, "one"));
            edges.Add(Edge("n" + layer, next, "two"));
        }

        var ir = CompilerIr.Build(Graph([.. nodes], [.. edges]), []);
        Assert.Empty(MemorySsaVerifier.Verify(ir.Memory));
        Assert.Equal(nodes.Count, ir.Memory.Functions.Count);
        Assert.True(ir.Memory.Versions.Count < 8 * nodes.Count);
    }

    [Fact]
    public void VerifierRejectsStaleMemoryAndUnstructuredCycles()
    {
        MemorySsa memory = Compile(LoopGraph()).Ir!.Memory;
        MemoryStep first = memory.Root.Steps[0];
        MemoryRegion root = memory.Root with
        {
            Steps = memory.Root.Steps.Select(step => step.Id == first.Id
                ? step with
                {
                    Input = -1
                }
                : step).ToArray()
        };
        var broken = new MemorySsa(memory.Versions, memory.Functions, root, memory.Expressions);
        Assert.Contains(MemorySsaVerifier.Verify(broken), error => error.Contains("current memory"));
        Dictionary<int, MemoryVersion> versions = memory.Versions.ToDictionary(pair => pair.Key, pair => pair.Value);
        MemoryVersion definition = versions.Values.First(version => version.Kind == MemoryKind.Write);
        versions[definition.Id] = definition with
        {
            Inputs = new[] { definition.Id }
        };
        broken = new MemorySsa(versions, memory.Functions, memory.Root, memory.Expressions);
        Assert.Contains(MemorySsaVerifier.Verify(broken), error => error.Contains("cycle"));
    }

    [Theory]
    [InlineData((int)IrEffect.None, true)]
    [InlineData((int)IrEffect.MayThrow, false)]
    [InlineData((int)IrEffect.Random, false)]
    [InlineData((int)IrEffect.Observe, false)]
    [InlineData((int)IrEffect.Cancellation, false)]
    [InlineData((int)IrEffect.Unknown, false)]
    public void ReadReuseRequiresGuardsAndEveryInterveningBarrier(int barrierFlags, bool allowed)
    {
        var barrier = (IrEffect)barrierFlags;
        var context = new TypeCheckContext
        {
            StateFields =
            [
                new FieldDescriptor
                {
                    Name = "x",
                    Type = ExprType.Number
                }
            ]
        };
        var facts = new Dictionary<string, ExprValue>
        {
            ["x"] = ExprValue.Number(7)
        };
        ExpressionIr expression = ExpressionIrBuilder.Build(S("x"), context, facts);
        var key = new IrExpressionKey(IrRootKind.Named, "read", "");
        var initial = new MemoryVersion(1, MemoryKind.Input, [], new HashSet<string>(), false, false, "", "entry");
        var changed = new MemoryVersion(2, MemoryKind.Write, [1], new HashSet<string>
        {
            "y"
        }, false, false, "", "write");
        var steps = new[]
        {
            new MemoryStep(1, MemoryStepKind.Read, "", "read", 1, 1, expression.Effects, [], Expression: key),
            new MemoryStep(2, MemoryStepKind.Write, "", "write", 1, 2,
                new IrEffects(IrEffect.WriteState | barrier, writes: ["y"]), []),
            new MemoryStep(3, MemoryStepKind.Read, "", "read", 2, 2, expression.Effects, [], Expression: key)
        };
        var region = new MemoryRegion(1, "test", 1, 2, steps, IrEffects.Union(steps.Select(step => step.Effects)));
        var definitions = new Dictionary<int, MemoryVersion>
        {
            [1] = initial,
            [2] = changed
        };
        var roots = new Dictionary<IrExpressionKey, ExpressionIr>
        {
            [key] = expression
        };
        var memory = new MemorySsa(definitions, new Dictionary<ContinuationKey, MemoryFunction>(), region, roots);
        Assert.Empty(MemorySsaVerifier.Verify(memory));
        Assert.Equal(allowed, memory.CanReuseRead(region, 0, 2, "x", expression, []));
        Assert.False(memory.CanReuseRead(region, 0, 2, "x", expression, ["x"]));
        Assert.False(memory.CanReuseRead(region, 0, 2, "x", ExpressionIrBuilder.Build(S("x"), TypeCheckContext.Default),
            []));
        var versions = new Dictionary<int, MemoryVersion>
        {
            [1] = initial,
            [2] = changed with
            {
                Writes = new HashSet<string>
                {
                    "x"
                }
            }
        };
        var overwritten = new MemorySsa(versions, memory.Functions, region, memory.Expressions);
        Assert.False(overwritten.CanReuseRead(region, 0, 2, "x", expression, []));
    }

    [Fact]
    public void VerifierRejectsAConditionalWriteDiscardedAtTheJoin()
    {
        var branch = new BranchNode
        {
            Id = "branch",
            ConditionId = "condition",
            Outputs = Ports("true", "false")
        };
        GraphConfig graph = Graph([branch, Write("a", "one", "x"), Write("b", "two", "y"), Sink()],
            [Edge("branch", "a", "true"), Edge("branch", "b", "false"), Edge("a", "sink"), Edge("b", "sink")]);
        graph.Expressions!["condition"] = new ConstantExpr
        {
            Kind = ConstantKind.Boolean,
            Value = "true"
        };
        MemorySsa memory = Compile(graph).Ir!.Memory;
        var key = new ContinuationKey("branch", "sink");
        MemoryFunction function = memory.Functions[key];
        MemoryStep[] changed = function.Body.Steps.Select(step => step.Kind == MemoryStepKind.Branch
            ? step with
            {
                Result = step.Input
            }
            : step).ToArray();
        MemoryRegion body = function.Body with
        {
            Steps = changed,
            Result = changed.Last().Result
        };
        Dictionary<ContinuationKey, MemoryFunction> functions =
            memory.Functions.ToDictionary(pair => pair.Key, pair => pair.Value);
        functions[key] = function with
        {
            Body = body
        };
        Assert.Contains(
            MemorySsaVerifier.Verify(new MemorySsa(memory.Versions, functions, memory.Root, memory.Expressions)),
            error => error.Contains("discards conditional writes"));
    }

    [Fact]
    public void CounterClobbersRemainVisibleWithoutAssumingTheCapProvesTermination()
    {
        GraphConfig graph = LoopGraph();
        graph.Nodes[1] = (ModifyStateNode)graph.Nodes[1] with
        {
            OutputKey = "__iter_loop__"
        };
        MemorySsa memory = Compile(graph).Ir!.Memory;
        MemoryStep loop = memory.Functions[new ContinuationKey("loop", "sink")].Body.Steps
            .Single(step => step.Kind == MemoryStepKind.Loop);
        MemoryStep bodyCall = loop.Regions[1].Steps.Single(step => step.Call is not null);
        Assert.Contains("__iter_loop__", bodyCall.Effects.Writes);
        Assert.False(memory.SameValueSource(bodyCall.Input, bodyCall.Result, "__iter_loop__"));
        Assert.Equal(2, loop.IterationCap);
        // No execution here: a user body that resets the compiler counter may
        // prevent reaching its limit. The IR records the actual counter test.
    }
}
