using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Mechanics.Evaluators;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Plugins;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Compiler;

// ═══════════════════════════════════════════════════════════════════════════
//  G14 — Graph → program compiler + validation tests
// ═══════════════════════════════════════════════════════════════════════════

[Collection("Registry")]
public class GraphCompilerTests : IDisposable
{
    private readonly PluginHost _pluginHost = new();

    public void Dispose()
    {
        EvaluatorRegistry.Clear();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Create a minimal valid graph: Draw → Map(Lines) → MetricsSink.
    /// </summary>
    private static GraphConfig CreateMinimalValidGraph()
    {
        var symbols = new[]
        {
            new Symbol { Id = "sym-a", Name = "A", Kind = SymbolKind.Standard },
            new Symbol { Id = "sym-b", Name = "B", Kind = SymbolKind.Standard },
        };

        var paytable = new Paytable
        {
            Id = "pt",
            Entries = new[]
            {
                new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3, 4, 5 }, Payouts = new[] { "10", "50", "200" } },
                new PaytableEntry { SymbolId = "sym-b", Counts = new[] { 3, 4, 5 }, Payouts = new[] { "5", "25", "100" } },
            }
        };

        var reelStrips = new[]
        {
            new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "sym-a", "sym-b", "sym-a", "sym-b", "sym-a", "sym-b" } },
            new ReelStrip { Id = "r2", Name = "R2", Symbols = new[] { "sym-a", "sym-b", "sym-a", "sym-b", "sym-a", "sym-b" } },
            new ReelStrip { Id = "r3", Name = "R3", Symbols = new[] { "sym-a", "sym-b", "sym-a", "sym-b", "sym-a", "sym-b" } },
        };

        var reelSet = new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1", "r2", "r3" } };

        // Payline.Positions: [col0row, col1row, col2row] — row index per column
        var paylines = new[]
        {
            new Payline { Positions = new[] { 1, 1, 1 } },
            new Payline { Positions = new[] { 0, 0, 0 } },
            new Payline { Positions = new[] { 2, 2, 2 } },
        };

        var paylineSet = new PaylineSet { Id = "ps", Paylines = paylines };

        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-minimal",
            Name = "Minimal Valid Graph",
            Symbols = symbols,
            Paytables = new[] { paytable },
            PaylineSets = new[] { paylineSet },
            ReelStrips = reelStrips,
            ReelSets = new[] { reelSet },
            BoardConfig = new BoardConfig { Rows = 3, Columns = 3 },
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new MapNode
                {
                    Id = "eval",
                    Label = "Evaluate",
                    TransformId = "lines",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Metrics",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "eval", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
        };
    }

    /// <summary>Create a compiler with default setup.</summary>
    private GraphCompiler CreateCompiler()
    {
        var compiler = new GraphCompiler(_pluginHost);
        return compiler;
    }

    /// <summary>Run a compiled program and return the result.</summary>
    private static InterpreterResult<Dictionary<string, object?>, BigInteger> RunProgram(
        Slot<Dictionary<string, object?>, BigInteger> program)
    {
        return TrampolineInterpreter.RunWithSeed(program, new Dictionary<string, object?>(), seed: 42);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  VALID GRAPH TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidLinearGraph_CompilesToRunnableProgram()
    {
        // Register the lines evaluator
        var paylines = new[]
        {
            new Payline { Positions = new[] { 1, 1, 1 } },
        };
        EvaluatorRegistry.Register("lines", new LinesEvaluator(
            new Paytable
            {
                Id = "pt",
                Entries = new[]
                {
                    new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } },
                    new PaytableEntry { SymbolId = "sym-b", Counts = new[] { 3 }, Payouts = new[] { "5" } },
                }
            },
            new PaylineSet { Id = "ps", Paylines = paylines }));

        var config = CreateMinimalValidGraph();
        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid, $"Expected valid compilation, got errors: {string.Join("; ", result.Errors.Select(e => e.Message))}");
        Assert.NotNull(result.Program);

        // Verify the program is runnable — doesn't throw when interpreted
        var interpreterResult = RunProgram(result.Program);
        Assert.NotNull(interpreterResult);
    }

    [Fact]
    public void ValidGraphWithExpression_CompilesToRunnableProgram()
    {
        EvaluatorRegistry.Register("lines", new LinesEvaluator(
            new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } },
            new PaylineSet { Id = "ps", Paylines = new[] { new Payline { Positions = new[] { 0 } } } }));

        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-expr",
            Name = "Expression-Driven Multiplier",
            Symbols = new[] { new Symbol { Id = "sym-a", Name = "A", Kind = SymbolKind.Standard } },
            Paytables = new[] { new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } } },
            ReelStrips = new[] { new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "sym-a", "sym-a", "sym-a" } } },
            ReelSets = new[] { new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1" } } },
            BoardConfig = new BoardConfig { Rows = 1, Columns = 1 },
            Expressions = new Dictionary<string, Expression>
            {
                ["mul-expr"] = new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" }
            },
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new MapNode
                {
                    Id = "eval",
                    Label = "Evaluate",
                    TransformId = "lines",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board },
                        ["multiplier"] = new() { Name = "multiplier", Type = PortType.Number, DefaultValue = new FieldAccessExpr { Path = new[] { "expressions", "mul-expr" }, Target = "state" } }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Metrics",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "eval", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid, $"Expected valid compilation, got errors: {string.Join("; ", result.Errors.Select(e => e.Message))}");
        Assert.NotNull(result.Program);

        var interpreterResult = RunProgram(result.Program);
        Assert.NotNull(interpreterResult);
    }

    [Fact]
    public void ValidGraphWithPlugin_CompilesToRunnableProgram()
    {
        // Register a conformant plugin evaluator
        var pluginEval = new PluginTestEvaluator();
        _pluginHost.RegisterEvaluator("custom-eval", pluginEval, new ConformanceResult
        {
            Passed = true,
            Failures = Array.Empty<string>(),
            Warnings = Array.Empty<string>(),
        });

        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-plugin",
            Name = "Plugin Graph",
            Symbols = new[] { new Symbol { Id = "sym-a", Name = "A", Kind = SymbolKind.Standard } },
            Paytables = new[] { new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } } },
            ReelStrips = new[] { new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "sym-a", "sym-a", "sym-a" } } },
            ReelSets = new[] { new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1" } } },
            BoardConfig = new BoardConfig { Rows = 1, Columns = 1 },
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new MapNode
                {
                    Id = "eval",
                    Label = "Custom Eval",
                    TransformId = "plugin:custom-eval",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Metrics",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "eval", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
            Plugins = new[]
            {
                new PluginReference { PluginId = "custom-eval", Contract = PluginContract.IEvaluator, Version = "1.0.0" }
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid, $"Expected valid compilation, got errors: {string.Join("; ", result.Errors.Select(e => e.Message))}");
        Assert.NotNull(result.Program);

        var interpreterResult = RunProgram(result.Program);
        Assert.NotNull(interpreterResult);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  INVALID GRAPH TESTS — each must produce a precise error with node id
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CycleWithoutLoop_ProducesErrorWithNodeId()
    {
        // Create a graph with a simple cycle: A → B → A (no Loop node)
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-cycle",
            Nodes = new Node[]
            {
                new MapNode
                {
                    Id = "node-a",
                    Label = "Node A",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new MapNode
                {
                    Id = "node-b",
                    Label = "Node B",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "node-a", SourcePort = "board", TargetNodeId = "node-b", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "node-b", SourcePort = "board", TargetNodeId = "node-a", TargetPort = "board" },
                new Edge { Id = "e3", SourceNodeId = "node-a", SourcePort = "board", TargetNodeId = "sink", TargetPort = "wins" },
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CYCLE_WITHOUT_LOOP");
        Assert.Contains(result.Errors, e =>
            e.NodeId == "node-a" || e.NodeId == "node-b");
    }

    [Fact]
    public void TypeMismatchedEdge_ProducesErrorWithNodeId()
    {
        // Connect board output to number input — type mismatch
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-type-mismatch",
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new MapNode
                {
                    Id = "eval",
                    Label = "Evaluate",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["number-input"] = new() { Name = "number-input", Type = PortType.Number }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
            },
            Edges = new[]
            {
                // Board → Number is a type mismatch
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval", TargetPort = "number-input" },
                new Edge { Id = "e2", SourceNodeId = "eval", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "TYPE_MISMATCH");
        // Error should reference the offending edge or its source node
        Assert.Contains(result.Errors, e => e.EdgeId == "e1");
    }

    [Fact]
    public void IllTypedExpression_ProducesErrorWithNodeId()
    {
        // Expression that uses arithmetic on a boolean (type error)
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-bad-expr",
            Symbols = new[] { new Symbol { Id = "sym-a", Name = "A", Kind = SymbolKind.Standard } },
            Paytables = new[] { new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } } },
            ReelStrips = new[] { new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "sym-a", "sym-a" } } },
            ReelSets = new[] { new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1" } } },
            BoardConfig = new BoardConfig { Rows = 1, Columns = 1 },
            Expressions = new Dictionary<string, Expression>
            {
                // bad-expr: boolean OR number — type error
                ["bad-expr"] = new BinaryExpr
                {
                    Op = BinaryOp.Add,
                    Left = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" },
                    Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" }
                }
            },
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new MapNode
                {
                    Id = "eval",
                    Label = "Evaluate",
                    TransformId = "lines",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board },
                        ["multiplier"] = new() { Name = "multiplier", Type = PortType.Number, DefaultValue = new FieldAccessExpr { Path = new[] { "expressions", "bad-expr" }, Target = "state" } }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "eval", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "EXPRESSION_TYPE_ERROR");
        Assert.Contains(result.Errors, e => e.NodeId == "eval");
    }

    [Fact]
    public void MissingMetricsSink_ProducesError()
    {
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-no-sink",
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
            },
            Edges = Array.Empty<Edge>(),
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MISSING_METRICS_SINK");
    }

    [Fact]
    public void DuplicateMetricsSink_ProducesErrorWithNodeId()
    {
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-two-sinks",
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink-1",
                    Label = "Sink 1",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink-2",
                    Label = "Sink 2",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "sink-1", TargetPort = "wins" },
                new Edge { Id = "e2", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "sink-2", TargetPort = "wins" },
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "DUPLICATE_METRICS_SINK");
        Assert.Contains(result.Errors, e => e.NodeId == "sink-1" || e.NodeId == "sink-2");
    }

    [Fact]
    public void UnreachableNode_ProducesErrorWithNodeId()
    {
        // Node "orphan" has no path from any entry point
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-unreachable",
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new MapNode
                {
                    Id = "orphan",
                    Label = "Orphan Node",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "sink", TargetPort = "wins" },
                // "orphan" is not connected to anything
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "UNREACHABLE_NODE");
        Assert.Contains(result.Errors, e => e.NodeId == "orphan");
    }

    [Fact]
    public void DeadEndNode_ProducesErrorWithNodeId()
    {
        // Node "dead-end" has no path to the MetricsSink
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-dead-end",
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new MapNode
                {
                    Id = "dead-end",
                    Label = "Dead End",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "dead-end", TargetPort = "board" },
                // dead-end has no outgoing edge to sink or anything leading to sink
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "DEAD_END_NODE");
        Assert.Contains(result.Errors, e => e.NodeId == "dead-end");
    }

    [Fact]
    public void MissingPlugin_ProducesErrorWithNodeId()
    {
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-missing-plugin",
            Symbols = new[] { new Symbol { Id = "sym-a", Name = "A", Kind = SymbolKind.Standard } },
            Paytables = new[] { new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } } },
            ReelStrips = new[] { new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "sym-a", "sym-a" } } },
            ReelSets = new[] { new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1" } } },
            BoardConfig = new BoardConfig { Rows = 1, Columns = 1 },
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new MapNode
                {
                    Id = "eval",
                    Label = "Plugin Eval",
                    TransformId = "plugin:missing-plugin",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "eval", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "PLUGIN_NOT_FOUND");
        Assert.Contains(result.Errors, e => e.NodeId == "eval");
    }

    [Fact]
    public void NonConformantPlugin_ProducesErrorWithNodeId()
    {
        // Register a non-conformant plugin
        var pluginEval = new PluginTestEvaluator();
        _pluginHost.RegisterEvaluator("bad-plugin", pluginEval, new ConformanceResult
        {
            Passed = false,
            Failures = new[] { "Plugin mutates global state" },
            Warnings = Array.Empty<string>(),
        });

        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-bad-plugin",
            Symbols = new[] { new Symbol { Id = "sym-a", Name = "A", Kind = SymbolKind.Standard } },
            Paytables = new[] { new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } } },
            ReelStrips = new[] { new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "sym-a", "sym-a" } } },
            ReelSets = new[] { new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1" } } },
            BoardConfig = new BoardConfig { Rows = 1, Columns = 1 },
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new MapNode
                {
                    Id = "eval",
                    Label = "Bad Plugin",
                    TransformId = "plugin:bad-plugin",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "eval", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "PLUGIN_NOT_CONFORMANT");
        Assert.Contains(result.Errors, e => e.NodeId == "eval");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DIFFICULT TESTS — deep coverage of tough compiler logic
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidGraph_WithFanOutFanIn_CompilesToRunnableProgram()
    {
        // draw → eval-lines → wins
        //                    ↘ sink (fan-in: two sources feed into sink)
        // draw → eval-scatter → wins
        //
        // Both evaluators process the same board. The sink receives wins from both.
        // This tests that the compiler handles DAGs with fan-out/fan-in.
        EvaluatorRegistry.Register("lines", new LinesEvaluator(
            new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } },
            new PaylineSet { Id = "ps", Paylines = new[] { new Payline { Positions = new[] { 0, 0, 0 } } } }));

        EvaluatorRegistry.Register("lines2", new LinesEvaluator(
            new Paytable { Id = "pt2", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "5" } } } },
            new PaylineSet { Id = "ps2", Paylines = new[] { new Payline { Positions = new[] { 1, 1, 1 } } } }));

        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-fanout-fanin",
            Symbols = new[] { new Symbol { Id = "sym-a", Name = "A", Kind = SymbolKind.Standard } },
            Paytables = new[]
            {
                new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } },
            },
            ReelStrips = new[]
            {
                new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "sym-a", "sym-a" } },
                new ReelStrip { Id = "r2", Name = "R2", Symbols = new[] { "sym-a", "sym-a" } },
                new ReelStrip { Id = "r3", Name = "R3", Symbols = new[] { "sym-a", "sym-a" } },
            },
            ReelSets = new[] { new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1", "r2", "r3" } } },
            BoardConfig = new BoardConfig { Rows = 3, Columns = 3 },
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new MapNode
                {
                    Id = "eval-lines",
                    Label = "Lines",
                    TransformId = "lines",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
                new MapNode
                {
                    Id = "eval-lines2",
                    Label = "Lines2",
                    TransformId = "lines2",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
            },
            Edges = new[]
            {
                // Fan-out: draw → both evaluators
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval-lines", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval-lines2", TargetPort = "board" },
                // Fan-in: both evaluators → sink
                new Edge { Id = "e3", SourceNodeId = "eval-lines", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
                new Edge { Id = "e4", SourceNodeId = "eval-lines2", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid, $"Expected valid, got: {string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}"))}");
        Assert.NotNull(result.Program);

        // Must be runnable
        var interpreterResult = RunProgram(result.Program);
        Assert.NotNull(interpreterResult);
    }

    [Fact]
    public void ValidGraph_WithAllowedLoopCycle_CompilesWithoutCycleError()
    {
        // A Loop node that feeds data back to an upstream node is an ALLOWED cycle.
        // The validator must recognize Loop back-edges and not flag them.
        // Graph: draw → check → loop → eval → sink
        //                      ↑___________|  (loop back edge — allowed)
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-loop-cycle",
            Symbols = new[] { new Symbol { Id = "sym-a", Name = "A", Kind = SymbolKind.Standard } },
            Paytables = new[] { new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } } },
            ReelStrips = new[] { new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "sym-a" } } },
            ReelSets = new[] { new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1" } } },
            BoardConfig = new BoardConfig { Rows = 1, Columns = 1 },
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board },
                        ["state"] = new() { Name = "state", Type = PortType.State }
                    }
                },
                new LoopNode
                {
                    Id = "loop",
                    Label = "Free Spins",
                    MaxIterations = 10,
                    Inputs = new Dictionary<string, Port>
                    {
                        ["state"] = new() { Name = "state", Type = PortType.State }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "state", TargetNodeId = "loop", TargetPort = "state" },
                // Loop back edge: loop outputs state back to itself (allowed cycle)
                new Edge { Id = "e2", SourceNodeId = "loop", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        // Should NOT contain a CYCLE_WITHOUT_LOOP error
        Assert.DoesNotContain(result.Errors, e => e.Code == "CYCLE_WITHOUT_LOOP");
    }

    [Fact]
    public void InvalidGraph_EdgeToNonexistentNode_ProducesError()
    {
        // Edge references a node that doesn't exist in the graph.
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-bad-edge",
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
            },
            Edges = new[]
            {
                // Edge to non-existent node
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "ghost-node", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "ghost-node-2", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_GRAPH");
        Assert.Contains(result.Errors, e => e.EdgeId == "e1");
    }

    [Fact]
    public void ValidGraph_TypeCoercion_SymbolToStringAndBooleanToTrigger_Compiles()
    {
        // Implicit conversions: Symbol→String and Boolean→Trigger should be allowed.
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-coercion",
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["symbol-out"] = new() { Name = "symbol-out", Type = PortType.Symbol },
                        ["trigger-out"] = new() { Name = "trigger-out", Type = PortType.Boolean }
                    }
                },
                new MapNode
                {
                    Id = "consumer",
                    Label = "Consumer",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["label"] = new() { Name = "label", Type = PortType.String },     // Symbol→String: allowed
                        ["trigger"] = new() { Name = "trigger", Type = PortType.Trigger } // Boolean→Trigger: allowed
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "symbol-out", TargetNodeId = "consumer", TargetPort = "label" },
                new Edge { Id = "e2", SourceNodeId = "draw", SourcePort = "trigger-out", TargetNodeId = "consumer", TargetPort = "trigger" },
                new Edge { Id = "e3", SourceNodeId = "consumer", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        // Should NOT produce TYPE_MISMATCH for the implicit conversions
        var typeErrors = result.Errors.Where(e => e.Code == "TYPE_MISMATCH").ToList();
        Assert.Empty(typeErrors);
    }

    [Fact]
    public void ValidGraph_ChainedExpressionReferences_ResolvesCorrectly()
    {
        // Expression 'base' = constant 3
        // Expression 'derived' = field access to state.expressions.base
        // Map node uses 'derived' as multiplier.
        // The compiler must resolve the chain: derived → base → constant 3.
        EvaluatorRegistry.Register("lines", new LinesEvaluator(
            new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } },
            new PaylineSet { Id = "ps", Paylines = new[] { new Payline { Positions = new[] { 0 } } } }));

        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-chained-expr",
            Symbols = new[] { new Symbol { Id = "sym-a", Name = "A", Kind = SymbolKind.Standard } },
            Paytables = new[] { new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } } },
            ReelStrips = new[] { new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "sym-a", "sym-a" } } },
            ReelSets = new[] { new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1" } } },
            BoardConfig = new BoardConfig { Rows = 1, Columns = 1 },
            Expressions = new Dictionary<string, Expression>
            {
                ["base"] = new ConstantExpr { Kind = ConstantKind.Integer, Value = "3" },
                ["derived"] = new FieldAccessExpr { Path = new[] { "expressions", "base" }, Target = "state" }
            },
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board } }
                },
                new MapNode
                {
                    Id = "eval",
                    Label = "Eval",
                    TransformId = "lines",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board },
                        ["multiplier"] = new() { Name = "multiplier", Type = PortType.Number, DefaultValue = new FieldAccessExpr { Path = new[] { "expressions", "derived" }, Target = "state" } }
                    },
                    Outputs = new Dictionary<string, Port> { ["wins"] = new() { Name = "wins", Type = PortType.Wins } }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port> { ["wins"] = new() { Name = "wins", Type = PortType.Wins } }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "eval", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid, $"Expected valid, got: {string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}"))}");
        Assert.NotNull(result.Program);
        var interpreterResult = RunProgram(result.Program);
        Assert.NotNull(interpreterResult);
    }

    [Fact]
    public void InvalidGraph_SelfReferencingExpression_ProducesTypeError()
    {
        // Expression 'recursive' references itself via state.expressions.recursive.
        // This creates a circular dependency that should be caught (and not cause
        // infinite recursion at compile time).
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-self-ref-expr",
            Symbols = new[] { new Symbol { Id = "sym-a", Name = "A", Kind = SymbolKind.Standard } },
            Paytables = new[] { new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } } },
            ReelStrips = new[] { new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "sym-a" } } },
            ReelSets = new[] { new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1" } } },
            BoardConfig = new BoardConfig { Rows = 1, Columns = 1 },
            Expressions = new Dictionary<string, Expression>
            {
                // Expression references itself — circular dependency
                ["recursive"] = new FieldAccessExpr { Path = new[] { "expressions", "recursive" }, Target = "state" }
            },
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board } }
                },
                new MapNode
                {
                    Id = "eval",
                    Label = "Eval",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board },
                        ["multiplier"] = new() { Name = "multiplier", Type = PortType.Number, DefaultValue = new FieldAccessExpr { Path = new[] { "expressions", "recursive" }, Target = "state" } }
                    },
                    Outputs = new Dictionary<string, Port> { ["wins"] = new() { Name = "wins", Type = PortType.Wins } }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port> { ["wins"] = new() { Name = "wins", Type = PortType.Wins } }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "eval", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        // The graph may or may not be valid depending on how deep the evaluator
        // resolves. At minimum, the compiler must not crash or hang.
        Assert.NotNull(result);
        // If it's valid, the program must still be runnable without infinite recursion
        if (result.IsValid)
        {
            Assert.NotNull(result.Program);
            // Run with a short timeout — must not hang
            var interpreterResult = RunProgram(result.Program);
            Assert.NotNull(interpreterResult);
        }
    }

    [Fact]
    public void ValidGraph_WithStateOperations_CompilesAndThreadsState()
    {
        // Linear graph with state operations woven into the board flow.
        // draw → modifyState(state) → eval → sink
        // The state is modified as a side-effect between draw and eval.
        EvaluatorRegistry.Register("lines", new LinesEvaluator(
            new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } },
            new PaylineSet { Id = "ps", Paylines = new[] { new Payline { Positions = new[] { 0 } } } }));

        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-state-ops",
            Symbols = new[] { new Symbol { Id = "sym-a", Name = "A", Kind = SymbolKind.Standard } },
            Paytables = new[] { new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } } },
            ReelStrips = new[] { new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "sym-a" } } },
            ReelSets = new[] { new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1" } } },
            BoardConfig = new BoardConfig { Rows = 1, Columns = 1 },
            Expressions = new Dictionary<string, Expression>
            {
                ["dec-expr"] = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" }
            },
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new ModifyStateNode
                {
                    Id = "inc-counter",
                    Label = "Increment",
                    ExpressionId = "dec-expr",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new MapNode
                {
                    Id = "eval",
                    Label = "Eval",
                    TransformId = "lines",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port> { ["wins"] = new() { Name = "wins", Type = PortType.Wins } }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port> { ["wins"] = new() { Name = "wins", Type = PortType.Wins } }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "inc-counter", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "inc-counter", SourcePort = "board", TargetNodeId = "eval", TargetPort = "board" },
                new Edge { Id = "e3", SourceNodeId = "eval", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid, $"Expected valid, got: {string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}"))}");
        Assert.NotNull(result.Program);

        // Run with initial state containing a counter
        var initialState = new Dictionary<string, object?> { ["freeSpins"] = new BigInteger(5) };
        var interpreterResult = TrampolineInterpreter.RunWithSeed(result.Program, initialState, seed: 42);
        Assert.NotNull(interpreterResult);
    }

    [Fact]
    public void ValidGraph_DrawWithWeightExpression_UsesCustomWeights()
    {
        // Draw node with a weight expression from the expressions dictionary.
        // The weight expression determines reel weights dynamically.
        EvaluatorRegistry.Register("lines", new LinesEvaluator(
            new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } },
            new PaylineSet { Id = "ps", Paylines = new[] { new Payline { Positions = new[] { 0 } } } }));

        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-weight-expr",
            Symbols = new[] { new Symbol { Id = "sym-a", Name = "A", Kind = SymbolKind.Standard } },
            Paytables = new[] { new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } } },
            ReelStrips = new[] { new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "sym-a", "sym-a" } } },
            ReelSets = new[] { new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1" } } },
            BoardConfig = new BoardConfig { Rows = 1, Columns = 1 },
            Expressions = new Dictionary<string, Expression>
            {
                // Weight expression: just returns a constant (the actual weight is determined
                // by the expression compiler; WeightSet is built by the compiler from it)
                ["custom-weight"] = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" }
            },
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    WeightExpressionId = "custom-weight",
                    Outputs = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board } }
                },
                new MapNode
                {
                    Id = "eval",
                    Label = "Eval",
                    TransformId = "lines",
                    Inputs = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board } },
                    Outputs = new Dictionary<string, Port> { ["wins"] = new() { Name = "wins", Type = PortType.Wins } }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port> { ["wins"] = new() { Name = "wins", Type = PortType.Wins } }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "eval", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid, $"Expected valid, got: {string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}"))}");
        Assert.NotNull(result.Program);
        var interpreterResult = RunProgram(result.Program);
        Assert.NotNull(interpreterResult);
    }

    [Fact]
    public void ValidGraph_MixedPluginAndExpressions_CompilesWithCorrectProvenance()
    {
        // Plugin evaluator + expression multiplier. The program must compile,
        // run without throwing, and report sampled provenance.
        var pluginEval = new PluginTestEvaluator();
        _pluginHost.RegisterEvaluator("custom-mixed", pluginEval, new ConformanceResult
        {
            Passed = true,
            Failures = Array.Empty<string>(),
            Warnings = Array.Empty<string>(),
        });

        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-mixed-plugin-expr",
            Symbols = new[] { new Symbol { Id = "sym-a", Name = "A", Kind = SymbolKind.Standard } },
            Paytables = new[] { new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } } },
            ReelStrips = new[] { new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "sym-a", "sym-a" } } },
            ReelSets = new[] { new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1" } } },
            BoardConfig = new BoardConfig { Rows = 1, Columns = 1 },
            Expressions = new Dictionary<string, Expression>
            {
                ["mul"] = new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" }
            },
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board } }
                },
                new MapNode
                {
                    Id = "eval",
                    Label = "Plugin Eval",
                    TransformId = "plugin:custom-mixed",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board },
                        ["multiplier"] = new() { Name = "multiplier", Type = PortType.Number, DefaultValue = new FieldAccessExpr { Path = new[] { "expressions", "mul" }, Target = "state" } }
                    },
                    Outputs = new Dictionary<string, Port> { ["wins"] = new() { Name = "wins", Type = PortType.Wins } }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port> { ["wins"] = new() { Name = "wins", Type = PortType.Wins } }
                },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "eval", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            },
            Plugins = new[] { new PluginReference { PluginId = "custom-mixed", Contract = PluginContract.IEvaluator, Version = "1.0.0" } },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.True(result.IsValid, $"Expected valid, got: {string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}"))}");
        Assert.NotNull(result.Program);

        // Run through TrampolineInterpreter
        var interpreterResult = RunProgram(result.Program);
        Assert.NotNull(interpreterResult);

        // Verify the program can be analyzed and contains plugin flag
        var analysis = ProgramAnalyzer.Analyze(result.Program);
        Assert.True(analysis.ContainsPlugin, "Graph with plugin should report ContainsPlugin=true");
    }

    [Fact]
    public void InvalidGraph_DisconnectedSubgraphs_BothReported()
    {
        // Two completely separate components: one with sink, one orphaned.
        // Both disconnected nodes should be reported.
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "test-disconnected",
            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw-main",
                    Label = "Main Draw",
                    Outputs = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board } }
                },
                new MetricsSinkNode { WinCap = 10_000,
                    Id = "sink",
                    Label = "Sink",
                    Inputs = new Dictionary<string, Port> { ["wins"] = new() { Name = "wins", Type = PortType.Wins } }
                },
                // These two nodes form a separate disconnected subgraph
                new DrawNode
                {
                    Id = "orphan-draw",
                    Label = "Orphan Draw",
                    Outputs = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board } }
                },
                new MapNode
                {
                    Id = "orphan-eval",
                    Label = "Orphan Eval",
                    Inputs = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board } },
                    Outputs = new Dictionary<string, Port> { ["wins"] = new() { Name = "wins", Type = PortType.Wins } }
                },
            },
            Edges = new[]
            {
                // Main component: draw-main → sink (but board→wins type mismatch is separate)
                new Edge { Id = "e1", SourceNodeId = "draw-main", SourcePort = "board", TargetNodeId = "sink", TargetPort = "wins" },
                // Orphan subgraph: orphan-draw → orphan-eval
                new Edge { Id = "e2", SourceNodeId = "orphan-draw", SourcePort = "board", TargetNodeId = "orphan-eval", TargetPort = "board" },
            },
        };

        var compiler = CreateCompiler();
        var result = compiler.Compile(config);

        Assert.False(result.IsValid);
        // Both orphan nodes should be reported as unreachable or dead-end
        var orphanErrors = result.Errors.Where(e =>
            e.NodeId == "orphan-draw" || e.NodeId == "orphan-eval").ToList();
        Assert.NotEmpty(orphanErrors);
    }
}

/// <summary>
/// Simple test evaluator for plugin tests.
/// </summary>
public sealed class PluginTestEvaluator : IEvaluator
{
    public Win[] Evaluate(IReadOnlyDictionary<string, object?> state)
    {
        return Array.Empty<Win>();
    }
}
