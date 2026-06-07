using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
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
                new MetricsSinkNode
                {
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
                new MetricsSinkNode
                {
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
                new MetricsSinkNode
                {
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
                new MetricsSinkNode
                {
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
                new MetricsSinkNode
                {
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
                new MetricsSinkNode
                {
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
                new MetricsSinkNode
                {
                    Id = "sink-1",
                    Label = "Sink 1",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
                new MetricsSinkNode
                {
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
                new MetricsSinkNode
                {
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
                new MetricsSinkNode
                {
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
                new MetricsSinkNode
                {
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
                new MetricsSinkNode
                {
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
}

/// <summary>
/// Simple test evaluator for plugin tests.
/// </summary>
public sealed class PluginTestEvaluator : IEvaluator
{
    public Win[] Evaluate(Board board, object? state)
    {
        return Array.Empty<Win>();
    }
}
