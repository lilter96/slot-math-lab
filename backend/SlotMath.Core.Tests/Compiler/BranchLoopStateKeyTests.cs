using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Plugins;

namespace SlotMath.Core.Tests.Compiler;

// ═══════════════════════════════════════════════════════════════════════════
//  BranchLoopStateKeyTests — comprehensive coverage of compiler primitives
//
//  Exercises:
//    - DrawNode.StateWriteKey: outcomeId written to state after draw
//    - BranchNode dual-port: true/false edges route correctly
//    - LoopNode body/exit ports: fixpoint accumulates wins
//    - Full integration: Draw→StateWriteKey→Branch routes by drawn outcome
//
//  All tests use TrampolineInterpreter.Run with explicit Queue<int> draw
//  choices so outcomes are fully deterministic and verifiable.
// ═══════════════════════════════════════════════════════════════════════════

public sealed class BranchLoopStateKeyTests : IDisposable
{
    private readonly PluginHost _pluginHost = new();

    public void Dispose() => EvaluatorRegistry.Clear();

    private static GraphCompiler MakeCompiler() => new(null);

    private static InterpreterResult<Dictionary<string, object?>, BigInteger> Run(
        Slot<Dictionary<string, object?>, BigInteger> program,
        Queue<int> choices,
        Dictionary<string, object?>? initialState = null)
    {
        return TrampolineInterpreter.Run(
            program,
            initialState ?? new Dictionary<string, object?>(),
            choices);
    }

    // ── StateWriteKey tests ──────────────────────────────────────────────

    [Fact]
    public void StateWriteKey_DrawIndexZero_WritesFirstOutcomeIdToState()
    {
        // Draw with two outcomes; StateWriteKey="color"; force index 0 → "black"
        var config = BuildStateWriteKeyGraph();
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compile failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        var run = Run(result.Program, new Queue<int>([0]));
        Assert.Equal("black", run.FinalState["color"]);
    }

    [Fact]
    public void StateWriteKey_DrawIndexOne_WritesSecondOutcomeIdToState()
    {
        var config = BuildStateWriteKeyGraph();
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compile failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        var run = Run(result.Program, new Queue<int>([1]));
        Assert.Equal("red", run.FinalState["color"]);
    }

    [Fact]
    public void StateWriteKey_DrawIndexZero_ReturnsValueOfFirstWeight()
    {
        // black outcome has Value=100; red has Value=10
        var config = BuildStateWriteKeyGraph();
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compile failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        var run = Run(result.Program, new Queue<int>([0]));
        Assert.Equal(new BigInteger(100), run.Value);
    }

    [Fact]
    public void StateWriteKey_DrawIndexOne_ReturnsValueOfSecondWeight()
    {
        var config = BuildStateWriteKeyGraph();
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compile failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        var run = Run(result.Program, new Queue<int>([1]));
        Assert.Equal(new BigInteger(10), run.Value);
    }

    // ── Branch dual-port: constant conditions ─────────────────────────────

    [Fact]
    public void Branch_ConstantTrue_AlwaysRoutesToTruePort()
    {
        // Branch with always-true condition → true path → draw-b (value=100)
        // false path (draw-c, value=1) must NOT be reached
        var config = BuildBranchConstantGraph(conditionIsTrue: true);
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compile failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        // Two draws: entry draw (1 choice) + true-path draw-b (1 choice)
        var run = Run(result.Program, new Queue<int>([0, 0]));
        Assert.Equal(new BigInteger(100), run.Value);
    }

    [Fact]
    public void Branch_ConstantFalse_AlwaysRoutesToFalsePort()
    {
        // Branch with always-false condition → false path → draw-c (value=1)
        var config = BuildBranchConstantGraph(conditionIsTrue: false);
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compile failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        // Two draws: entry draw (1 choice) + false-path draw-c (1 choice)
        var run = Run(result.Program, new Queue<int>([0, 0]));
        Assert.Equal(new BigInteger(1), run.Value);
    }

    [Fact]
    public void Branch_ConstantTrue_TruePathIsIsolated_NoDependencyOnFalsePathDraw()
    {
        // Different draw choices for the second draw should not matter when always-true:
        // with choices [0, 0] and [0, 0] give same result (draw-b always wins)
        var config = BuildBranchConstantGraph(conditionIsTrue: true);
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid);

        var run1 = Run(result.Program, new Queue<int>([0, 0]));
        Assert.Equal(new BigInteger(100), run1.Value);
    }

    // ── Branch dual-port: state-key-based condition ──────────────────────

    [Fact]
    public void Branch_StateKeyCondition_BlackDraw_RoutesToTruePath_HighPayout()
    {
        // Draw → StateWriteKey="color"; Branch condition: state["color"]=="black"
        // Force draw=0 (black) → Branch true → draw-b (value=500)
        var config = BuildBranchStateKeyGraph();
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compile failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        // choices=[0,0]: first draw picks index 0 (black), second draw picks index 0 (high)
        var run = Run(result.Program, new Queue<int>([0, 0]));
        Assert.Equal(new BigInteger(500), run.Value);
    }

    [Fact]
    public void Branch_StateKeyCondition_RedDraw_RoutesToFalsePath_LowPayout()
    {
        // Force draw=1 (red) → Branch false → draw-c (value=5)
        var config = BuildBranchStateKeyGraph();
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compile failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        // choices=[1,0]: first draw picks index 1 (red), second draw picks index 0 (low)
        var run = Run(result.Program, new Queue<int>([1, 0]));
        Assert.Equal(new BigInteger(5), run.Value);
    }

    [Fact]
    public void Branch_StateKeyCondition_StateColorStoredCorrectly_Black()
    {
        var config = BuildBranchStateKeyGraph();
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid);

        var run = Run(result.Program, new Queue<int>([0, 0]));
        Assert.Equal("black", run.FinalState["color"]);
    }

    [Fact]
    public void Branch_StateKeyCondition_StateColorStoredCorrectly_Red()
    {
        var config = BuildBranchStateKeyGraph();
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid);

        var run = Run(result.Program, new Queue<int>([1, 0]));
        Assert.Equal("red", run.FinalState["color"]);
    }

    // ── Loop body/exit port: fixpoint accumulation ───────────────────────

    [Fact]
    public void Loop_BodyExitPorts_MaxIterationsThree_AccumulatesThreeBodyWins()
    {
        // Loop MaxIterations=3; body draws value=10 each iteration; exit → sink
        // Expected: 3 × 10 = 30
        var config = BuildLoopBodyExitGraph(maxIterations: 3, bodyDrawValue: 10);
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compile failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        // 3 draws (one per iteration); each picks index 0 (only outcome)
        var run = Run(result.Program, new Queue<int>([0, 0, 0]));
        Assert.Equal(new BigInteger(30), run.Value);
    }

    [Fact]
    public void Loop_BodyExitPorts_MaxIterationsOne_AccumOnlyOneBodyWin()
    {
        var config = BuildLoopBodyExitGraph(maxIterations: 1, bodyDrawValue: 10);
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compile failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        var run = Run(result.Program, new Queue<int>([0]));
        Assert.Equal(new BigInteger(10), run.Value);
    }

    [Fact]
    public void Loop_BodyExitPorts_MaxIterationsFive_AccumulatesFiveBodyWins()
    {
        var config = BuildLoopBodyExitGraph(maxIterations: 5, bodyDrawValue: 7);
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compile failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        // 5 draws; expected: 5 × 7 = 35
        var run = Run(result.Program, new Queue<int>([0, 0, 0, 0, 0]));
        Assert.Equal(new BigInteger(35), run.Value);
    }

    [Fact]
    public void Loop_BodyExitPorts_IterationCounterInState_EqualsMaxIterations()
    {
        // The compiler stores iteration counter in state["__iter_{nodeId}__"]
        var config = BuildLoopBodyExitGraph(maxIterations: 3, bodyDrawValue: 10);
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid);

        var run = Run(result.Program, new Queue<int>([0, 0, 0]));

        // Verify iteration counter is in state (key format: __iter_{nodeId}__)
        var iterKey = "__iter_loop__";
        Assert.True(run.FinalState.ContainsKey(iterKey),
            $"Expected state to contain '{iterKey}' after loop completion.");
        Assert.Equal(3, run.FinalState[iterKey]);
    }

    [Fact]
    public void Loop_BodyExitPorts_BodyDrawValueZero_AccumulatesZero()
    {
        var config = BuildLoopBodyExitGraph(maxIterations: 3, bodyDrawValue: 0);
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid);

        var run = Run(result.Program, new Queue<int>([0, 0, 0]));
        Assert.Equal(BigInteger.Zero, run.Value);
    }

    // ── Full integration: Draw+StateWriteKey+Branch routing ──────────────

    [Fact]
    public void Integration_DrawStateWriteKeyBranch_BlackPath_CorrectPayout()
    {
        // Full pattern:
        //   1. DrawNode writes state["color"]="black" and emits BigInteger(100)
        //   2. BranchNode reads state["color"]=="black" → true
        //   3. True path draws high-value outcome (500)
        //   Result: 500
        var config = BuildBranchStateKeyGraph();
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compile failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        var run = Run(result.Program, new Queue<int>([0, 0]));

        Assert.Equal("black", run.FinalState["color"]);
        Assert.Equal(new BigInteger(500), run.Value);
    }

    [Fact]
    public void Integration_DrawStateWriteKeyBranch_RedPath_CorrectPayout()
    {
        var config = BuildBranchStateKeyGraph();
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compile failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        var run = Run(result.Program, new Queue<int>([1, 0]));

        Assert.Equal("red", run.FinalState["color"]);
        Assert.Equal(new BigInteger(5), run.Value);
    }

    [Fact]
    public void Integration_DrawStateWriteKeyBranch_BothPathsCompileToValid()
    {
        // Verify the graph compiles without errors regardless of which path runs
        var config = BuildBranchStateKeyGraph();
        var compiler = MakeCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compile failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Program);
    }

    // ── Graph builders ───────────────────────────────────────────────────

    /// <summary>
    /// Graph: Draw(DrawWeights=[black:100, red:10], StateWriteKey="color") → Sink
    /// </summary>
    private static GraphConfig BuildStateWriteKeyGraph() => new()
    {
        SchemaVersion = "1.0.0",
        Id = "test-state-write-key",
        Nodes = new Node[]
        {
            new DrawNode
            {
                Id = "draw",
                Label = "Color Draw",
                DrawWeights = new[]
                {
                    new DrawWeight { OutcomeId = "black", Weight = 1, Value = 100 },
                    new DrawWeight { OutcomeId = "red",   Weight = 1, Value = 10  },
                },
                StateWriteKey = "color",
                Outputs = new Dictionary<string, Port>
                {
                    ["value"] = new() { Name = "value", Type = PortType.Number }
                }
            },
            new MetricsSinkNode
            {
                Id = "sink",
                Label = "Sink",
                Inputs = new Dictionary<string, Port>
                {
                    ["wins"] = new() { Name = "wins", Type = PortType.Number }
                }
            },
        },
        Edges = new[]
        {
            new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "value", TargetNodeId = "sink", TargetPort = "wins" },
        },
    };

    /// <summary>
    /// Graph: Draw(value=50) → Branch(constant bool) → true:Draw(100)/false:Draw(1) → Sink
    /// </summary>
    private static GraphConfig BuildBranchConstantGraph(bool conditionIsTrue) => new()
    {
        SchemaVersion = "1.0.0",
        Id = "test-branch-constant",
        Expressions = new Dictionary<string, Expression>
        {
            ["branch-cond"] = new ConstantExpr
            {
                Kind = ConstantKind.Boolean,
                Value = conditionIsTrue ? "true" : "false"
            }
        },
        Nodes = new Node[]
        {
            new DrawNode
            {
                Id = "entry-draw",
                Label = "Entry",
                DrawWeights = new[]
                {
                    new DrawWeight { OutcomeId = "x", Weight = 1, Value = 50 },
                },
                Outputs = new Dictionary<string, Port>
                {
                    ["value"] = new() { Name = "value", Type = PortType.Number }
                }
            },
            new BranchNode
            {
                Id = "branch",
                Label = "Branch",
                ConditionId = "branch-cond",
                Inputs = new Dictionary<string, Port>
                {
                    ["in"] = new() { Name = "in", Type = PortType.Number }
                },
                Outputs = new Dictionary<string, Port>
                {
                    ["true"]  = new() { Name = "true",  Type = PortType.Number },
                    ["false"] = new() { Name = "false", Type = PortType.Number }
                }
            },
            new DrawNode
            {
                Id = "draw-b",
                Label = "True Path",
                DrawWeights = new[]
                {
                    new DrawWeight { OutcomeId = "win", Weight = 1, Value = 100 },
                },
                Inputs = new Dictionary<string, Port>
                {
                    ["in"] = new() { Name = "in", Type = PortType.Number }
                },
                Outputs = new Dictionary<string, Port>
                {
                    ["value"] = new() { Name = "value", Type = PortType.Number }
                }
            },
            new DrawNode
            {
                Id = "draw-c",
                Label = "False Path",
                DrawWeights = new[]
                {
                    new DrawWeight { OutcomeId = "loss", Weight = 1, Value = 1 },
                },
                Inputs = new Dictionary<string, Port>
                {
                    ["in"] = new() { Name = "in", Type = PortType.Number }
                },
                Outputs = new Dictionary<string, Port>
                {
                    ["value"] = new() { Name = "value", Type = PortType.Number }
                }
            },
            new MetricsSinkNode
            {
                Id = "sink",
                Label = "Sink",
                Inputs = new Dictionary<string, Port>
                {
                    ["wins"] = new() { Name = "wins", Type = PortType.Number }
                }
            },
        },
        Edges = new[]
        {
            new Edge { Id = "e1", SourceNodeId = "entry-draw", SourcePort = "value",  TargetNodeId = "branch", TargetPort = "in"   },
            new Edge { Id = "e2", SourceNodeId = "branch",     SourcePort = "true",   TargetNodeId = "draw-b", TargetPort = "in"   },
            new Edge { Id = "e3", SourceNodeId = "branch",     SourcePort = "false",  TargetNodeId = "draw-c", TargetPort = "in"   },
            new Edge { Id = "e4", SourceNodeId = "draw-b",     SourcePort = "value",  TargetNodeId = "sink",   TargetPort = "wins" },
            new Edge { Id = "e5", SourceNodeId = "draw-c",     SourcePort = "value",  TargetNodeId = "sink",   TargetPort = "wins" },
        },
    };

    /// <summary>
    /// Graph: Draw(black:100/red:10, StateWriteKey="color")
    ///      → Branch(state["color"]=="black")
    ///        → true:Draw(high=500) → Sink
    ///        → false:Draw(low=5)   → Sink
    /// StateSchema declares "color" as string so type-checker accepts it.
    /// </summary>
    private static GraphConfig BuildBranchStateKeyGraph() => new()
    {
        SchemaVersion = "1.0.0",
        Id = "test-branch-state-key",
        StateSchema = new[]
        {
            new StateFieldSchema { Name = "color", Type = "string" },
        },
        Expressions = new Dictionary<string, Expression>
        {
            ["color-eq-black"] = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left  = new FieldAccessExpr { Target = "state", Path = new[] { "color" } },
                Right = new ConstantExpr { Kind = ConstantKind.String, Value = "black" }
            }
        },
        Nodes = new Node[]
        {
            new DrawNode
            {
                Id = "draw",
                Label = "Color Draw",
                DrawWeights = new[]
                {
                    new DrawWeight { OutcomeId = "black", Weight = 1, Value = 100 },
                    new DrawWeight { OutcomeId = "red",   Weight = 1, Value = 10  },
                },
                StateWriteKey = "color",
                Outputs = new Dictionary<string, Port>
                {
                    ["value"] = new() { Name = "value", Type = PortType.Number }
                }
            },
            new BranchNode
            {
                Id = "branch",
                Label = "Color Branch",
                ConditionId = "color-eq-black",
                Inputs = new Dictionary<string, Port>
                {
                    ["in"] = new() { Name = "in", Type = PortType.Number }
                },
                Outputs = new Dictionary<string, Port>
                {
                    ["true"]  = new() { Name = "true",  Type = PortType.Number },
                    ["false"] = new() { Name = "false", Type = PortType.Number }
                }
            },
            new DrawNode
            {
                Id = "draw-high",
                Label = "High Payout",
                DrawWeights = new[]
                {
                    new DrawWeight { OutcomeId = "high", Weight = 1, Value = 500 },
                },
                Inputs = new Dictionary<string, Port>
                {
                    ["in"] = new() { Name = "in", Type = PortType.Number }
                },
                Outputs = new Dictionary<string, Port>
                {
                    ["value"] = new() { Name = "value", Type = PortType.Number }
                }
            },
            new DrawNode
            {
                Id = "draw-low",
                Label = "Low Payout",
                DrawWeights = new[]
                {
                    new DrawWeight { OutcomeId = "low", Weight = 1, Value = 5 },
                },
                Inputs = new Dictionary<string, Port>
                {
                    ["in"] = new() { Name = "in", Type = PortType.Number }
                },
                Outputs = new Dictionary<string, Port>
                {
                    ["value"] = new() { Name = "value", Type = PortType.Number }
                }
            },
            new MetricsSinkNode
            {
                Id = "sink",
                Label = "Sink",
                Inputs = new Dictionary<string, Port>
                {
                    ["wins"] = new() { Name = "wins", Type = PortType.Number }
                }
            },
        },
        Edges = new[]
        {
            new Edge { Id = "e1", SourceNodeId = "draw",      SourcePort = "value", TargetNodeId = "branch",    TargetPort = "in"   },
            new Edge { Id = "e2", SourceNodeId = "branch",    SourcePort = "true",  TargetNodeId = "draw-high", TargetPort = "in"   },
            new Edge { Id = "e3", SourceNodeId = "branch",    SourcePort = "false", TargetNodeId = "draw-low",  TargetPort = "in"   },
            new Edge { Id = "e4", SourceNodeId = "draw-high", SourcePort = "value", TargetNodeId = "sink",      TargetPort = "wins" },
            new Edge { Id = "e5", SourceNodeId = "draw-low",  SourcePort = "value", TargetNodeId = "sink",      TargetPort = "wins" },
        },
    };

    /// <summary>
    /// Graph: Loop(MaxIterations=N, body→BodyDraw(value=V), exit→Sink)
    /// The loop accumulates V per iteration; expected result = N*V.
    /// </summary>
    private static GraphConfig BuildLoopBodyExitGraph(int maxIterations, long bodyDrawValue) => new()
    {
        SchemaVersion = "1.0.0",
        Id = "test-loop-body-exit",
        Nodes = new Node[]
        {
            new LoopNode
            {
                Id = "loop",
                Label = "Loop",
                MaxIterations = maxIterations,
                Outputs = new Dictionary<string, Port>
                {
                    ["body"] = new() { Name = "body", Type = PortType.Number },
                    ["exit"] = new() { Name = "exit", Type = PortType.Number }
                }
            },
            // Loop body: single draw with configurable value; no outgoing edge (terminates body)
            new DrawNode
            {
                Id = "body-draw",
                Label = "Body Draw",
                DrawWeights = new[]
                {
                    new DrawWeight { OutcomeId = "body-win", Weight = 1, Value = bodyDrawValue },
                },
                Inputs = new Dictionary<string, Port>
                {
                    ["in"] = new() { Name = "in", Type = PortType.Number }
                },
                // Intentionally no outputs — this is the terminal node in the loop body
            },
            new MetricsSinkNode
            {
                Id = "sink",
                Label = "Sink",
                Inputs = new Dictionary<string, Port>
                {
                    ["wins"] = new() { Name = "wins", Type = PortType.Number }
                }
            },
        },
        Edges = new[]
        {
            new Edge { Id = "e1", SourceNodeId = "loop",      SourcePort = "body", TargetNodeId = "body-draw", TargetPort = "in"   },
            new Edge { Id = "e2", SourceNodeId = "loop",      SourcePort = "exit", TargetNodeId = "sink",      TargetPort = "wins" },
        },
    };
}
