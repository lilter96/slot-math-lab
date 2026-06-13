using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Mechanics.Evaluators;
using SlotMath.Core.Mechanics.Transforms;
using SlotMath.Core.Model;
using SlotMath.Core.Plugins;

namespace SlotMath.Core.Tests.Benchmarks;

// ═══════════════════════════════════════════════════════════════════════════
//  The Dog House — level-(a+b) implementation, ZERO PLUGINS
//
//  Same game as DogHouseBenchmarkTests, but sticky wilds are implemented
//  using standard library generic transforms — no C# plugin code, no
//  game-specific classes.  A designer configures these two primitives in the
//  UI via parameter dropdowns / text fields:
//
//    BoardCellAccumulatorTransform — generic "scan board, accumulate data into state"
//      symbolFilter = "sym-wild"
//      extractMode  = Position      ← "I want to track cell coordinates"
//      stateKey     = "stickyPositions"
//      mergeMode    = Union         ← "I want all positions ever seen, not just this spin"
//
//    BoardCellApplyTransform — generic "read state data, apply to board"
//      stateKey  = "stickyPositions"
//      applyMode = OverlaySymbol    ← "I want to set cells to a symbol"
//      symbolId  = "sym-wild"
//
//  These two primitives compose into sticky wilds (or Hold-and-Win money,
//  expanding locked cells, etc.) entirely through configuration — same code,
//  different parameters.
//
//  Free-spin loop body chain:
//    draw-free-spin
//      ──[board]──► map-accumulate-wilds  (writes positions to state["stickyPositions"])
//                    ──[board]──► map-apply-wilds   (overlays wilds from state onto board)
//                                  ──[board]──► eval-free-lines
//
//  Items registered in Register():
//    "lines"              → EvaluatorRegistry: LinesEvaluator(...)
//    (scatter now a pure catalog subgraph, no C# evaluator)
//    "accumulate-wilds"   → TransformRegistry: BoardCellAccumulatorTransform(Position, Union)
//    "apply-wilds"        → TransformRegistry: BoardCellApplyTransform(OverlaySymbol)
// ═══════════════════════════════════════════════════════════════════════════

[Collection("Registry")]
public sealed class DogHouseNoPluginBenchmarkTests : IDisposable
{
    private readonly PluginHost _pluginHost = new();

    public void Dispose()
    {
        EvaluatorRegistry.Clear();
        TransformRegistry.Clear();
    }

    // ── Symbol ids ─────────────────────────────────────────────────────────

    private const string Wild  = "sym-wild";
    private const string Bonus = "sym-bonus";
    private const string H1   = "sym-h1";
    private const string H2   = "sym-h2";
    private const string H3   = "sym-h3";
    private const string H4   = "sym-h4";
    private const string L1   = "sym-l1";
    private const string L2   = "sym-l2";
    private const string L3   = "sym-l3";
    private const string L4   = "sym-l4";

    // ── Registration — level-(a) only: pick a primitive, configure it ──────

    private static void Register()
    {
        EvaluatorRegistry.Register("lines",
            new LinesEvaluator(CreateLinesPaytable(), CreatePaylineSet(), Wild));
        // Level-(a) configuration of two generic standard-library primitives.
        // No C# code from the user — just selecting a transform by id and
        // filling in its parameters (the UI exposes these as dropdowns + text fields).

        // Step 1: scan each free-spin board for wilds, accumulate their positions
        //         in state["stickyPositions"] as a union across all iterations.
        TransformRegistry.Register("accumulate-wilds",
            new BoardCellAccumulatorTransform(
                symbolFilter: Wild,
                extractMode:  CellExtractMode.Position,
                stateKey:     "stickyPositions",
                mergeMode:    CellMergeMode.Union));

        // Step 2: read the accumulated positions and overlay wilds onto the board
        //         before evaluation — so previous wilds remain visible every spin.
        TransformRegistry.Register("apply-wilds",
            new BoardCellApplyTransform(
                stateKey:  "stickyPositions",
                applyMode: CellApplyMode.OverlaySymbol,
                symbolId:  Wild));
    }

    // ── Data tables (identical to the plugin version) ─────────────────────

    private static Paytable CreateLinesPaytable() => new()
    {
        Id = "pt-lines",
        Entries = new[]
        {
            new PaytableEntry { SymbolId = H1, Counts = new[] { 3, 4, 5 }, Payouts = new[] { "200",  "1000", "4000" } },
            new PaytableEntry { SymbolId = H2, Counts = new[] { 3, 4, 5 }, Payouts = new[] { "100",  "500",  "2500" } },
            new PaytableEntry { SymbolId = H3, Counts = new[] { 3, 4, 5 }, Payouts = new[] { "50",   "250",  "1000" } },
            new PaytableEntry { SymbolId = H4, Counts = new[] { 3, 4, 5 }, Payouts = new[] { "25",   "100",  "500"  } },
            new PaytableEntry { SymbolId = L1, Counts = new[] { 3, 4, 5 }, Payouts = new[] { "15",   "75",   "250"  } },
            new PaytableEntry { SymbolId = L2, Counts = new[] { 3, 4, 5 }, Payouts = new[] { "10",   "50",   "150"  } },
            new PaytableEntry { SymbolId = L3, Counts = new[] { 3, 4, 5 }, Payouts = new[] { "8",    "30",   "100"  } },
            new PaytableEntry { SymbolId = L4, Counts = new[] { 3, 4, 5 }, Payouts = new[] { "5",    "20",   "75"   } },
        }
    };

    private static PaylineSet CreatePaylineSet() => new()
    {
        Id = "ps-dog-house",
        Paylines = new[]
        {
            new Payline { Positions = new[] { 0, 0, 0, 0, 0 } },
            new Payline { Positions = new[] { 1, 1, 1, 1, 1 } },
            new Payline { Positions = new[] { 2, 2, 2, 2, 2 } },
            new Payline { Positions = new[] { 3, 3, 3, 3, 3 } },
            new Payline { Positions = new[] { 0, 1, 2, 3, 2 } },
            new Payline { Positions = new[] { 3, 2, 1, 0, 1 } },
            new Payline { Positions = new[] { 1, 0, 1, 0, 1 } },
            new Payline { Positions = new[] { 2, 3, 2, 3, 2 } },
            new Payline { Positions = new[] { 0, 1, 1, 1, 0 } },
            new Payline { Positions = new[] { 3, 2, 2, 2, 3 } },
            new Payline { Positions = new[] { 1, 2, 2, 2, 1 } },
            new Payline { Positions = new[] { 2, 1, 1, 1, 2 } },
            new Payline { Positions = new[] { 0, 0, 1, 0, 0 } },
            new Payline { Positions = new[] { 3, 3, 2, 3, 3 } },
            new Payline { Positions = new[] { 1, 2, 3, 2, 1 } },
            new Payline { Positions = new[] { 2, 1, 0, 1, 2 } },
            new Payline { Positions = new[] { 0, 1, 2, 1, 0 } },
            new Payline { Positions = new[] { 3, 2, 1, 2, 3 } },
            new Payline { Positions = new[] { 1, 0, 0, 0, 1 } },
            new Payline { Positions = new[] { 2, 3, 3, 3, 2 } },
        }
    };

    private static ReelStrip[] CreateReelStrips() =>
    [
        new ReelStrip { Id = "r0", Name = "Reel-1",
            Symbols = new[] { H1, L1, H2, L2, L3, Bonus, H3, L4, H4, L1, L2, H1 } },
        new ReelStrip { Id = "r1", Name = "Reel-2",
            Symbols = new[] { Wild, H1, L1, H2, L2, Wild, H3, L3, H4, L1, L4, H2 } },
        new ReelStrip { Id = "r2", Name = "Reel-3",
            Symbols = new[] { Wild, H2, L1, Bonus, L2, H1, L3, L4, Wild, H3, L1, L2 } },
        new ReelStrip { Id = "r3", Name = "Reel-4",
            Symbols = new[] { Wild, H2, L1, L2, H3, Wild, L3, H1, L4, L1, H4, L2 } },
        new ReelStrip { Id = "r4", Name = "Reel-5",
            Symbols = new[] { H1, L1, H2, L2, Bonus, H3, L3, H4, L4, L1, H2, L3 } },
    ];

    private static Symbol[] CreateSymbols() =>
    [
        new Symbol { Id = H1,   Name = "Husky",     Kind = SymbolKind.Standard },
        new Symbol { Id = H2,   Name = "Dalmatian", Kind = SymbolKind.Standard },
        new Symbol { Id = H3,   Name = "Bulldog",   Kind = SymbolKind.Standard },
        new Symbol { Id = H4,   Name = "Dachshund", Kind = SymbolKind.Standard },
        new Symbol { Id = L1,   Name = "Ace",       Kind = SymbolKind.Standard },
        new Symbol { Id = L2,   Name = "King",      Kind = SymbolKind.Standard },
        new Symbol { Id = L3,   Name = "Queen",     Kind = SymbolKind.Standard },
        new Symbol { Id = L4,   Name = "Jack",      Kind = SymbolKind.Standard },
        new Symbol { Id = Wild,  Name = "Wild",     Kind = SymbolKind.Wild     },
        new Symbol { Id = Bonus, Name = "Bonus",    Kind = SymbolKind.Bonus    },
    ];

    // ── Graph config — level-(a+b), no plugin references ──────────────────

    private static GraphConfig CreateConfig()
    {
        var reelSet = new ReelSet
        {
            Id = "rs-main", Name = "Main Reels",
            StripIds = new[] { "r0", "r1", "r2", "r3", "r4" }
        };

        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id            = "dog-house-no-plugin",
            Name          = "The Dog House (level a+b)",
            Description   = "Dog House — sticky wilds via generic BoardCellAccumulator + BoardCellApply",

            Symbols     = CreateSymbols(),
            Paytables   = new[] { CreateLinesPaytable() },
            PaylineSets = new[] { CreatePaylineSet() },
            ReelStrips  = CreateReelStrips(),
            ReelSets    = new[] { reelSet },
            BoardConfig = new BoardConfig { Rows = 4, Columns = 5 },

            StateSchema = new[]
            {
                new StateFieldSchema { Name = "fsLeft",          Type = "number"   },
                new StateFieldSchema { Name = "stickyPositions", Type = "string[]" },
                new StateFieldSchema { Name = "__iter_loopFS__", Type = "number"   },
                new StateFieldSchema { Name = "__wins_loopFS__", Type = "number"   },
            },

            Expressions = new Dictionary<string, Expression>
            {
                ["bonus-trigger"] = new CompareExpr
                {
                    Op   = CompareOp.Gte,
                    Left = new AggregateExpr
                    {
                        Func   = AggregateFunc.Count,
                        Target = "board",
                        Predicate = new CompareExpr
                        {
                            Op    = CompareOp.Eq,
                            Left  = new FieldAccessExpr { Path = new[] { "symbol" }, Target = null },
                            Right = new ConstantExpr { Kind = ConstantKind.String, Value = Bonus }
                        }
                    },
                    Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "3" }
                },
                ["fs-stop"] = new CompareExpr
                {
                    Op    = CompareOp.Gte,
                    Left  = new FieldAccessExpr { Path = new[] { "__iter_loopFS__" }, Target = "state" },
                    Right = new FieldAccessExpr { Path = new[] { "fsLeft" },          Target = "state" }
                },
            },

            Nodes = new Node[]
            {
                new DrawNode
                {
                    Id = "draw-spin", Label = "Base Spin",
                    Outputs = new Dictionary<string, Port>
                        { ["board"] = new() { Name = "board", Type = PortType.Board } }
                },
                new MapNode
                {
                    Id = "eval-lines", Label = "Lines Evaluator", TransformId = "lines",
                    Inputs  = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board } },
                    Outputs = new Dictionary<string, Port> { ["wins"]  = new() { Name = "wins",  Type = PortType.Wins  } }
                },
                new BranchNode
                {
                    Id = "branch-bonus", Label = "Bonus Trigger?", ConditionId = "bonus-trigger",
                    Inputs  = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board } },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["true"]  = new() { Name = "true",  Type = PortType.Board },
                        ["false"] = new() { Name = "false", Type = PortType.Board }
                    }
                },
                new DrawNode
                {
                    Id = "draw-fs-count", Label = "Draw FS Count",
                    DrawWeights = new[]
                    {
                        new DrawWeight { OutcomeId = "fs8",  Weight = 5, Value = 8  },
                        new DrawWeight { OutcomeId = "fs15", Weight = 3, Value = 15 },
                        new DrawWeight { OutcomeId = "fs20", Weight = 2, Value = 20 },
                    },
                    Inputs  = new Dictionary<string, Port> { ["in"]  = new() { Name = "in",  Type = PortType.Board  } },
                    Outputs = new Dictionary<string, Port> { ["out"] = new() { Name = "out", Type = PortType.Number } }
                },
                new PutStateNode
                {
                    Id = "put-fs-left", Label = "Set FS Left", StateKey = "fsLeft",
                    Inputs  = new Dictionary<string, Port> { ["in"]  = new() { Name = "in",  Type = PortType.Number } },
                    Outputs = new Dictionary<string, Port> { ["out"] = new() { Name = "out", Type = PortType.Number } }
                },
                new LoopNode
                {
                    Id = "loopFS", Label = "Free Spin Loop",
                    MaxIterations = 20, StopConditionId = "fs-stop",
                    Inputs  = new Dictionary<string, Port> { ["in"]   = new() { Name = "in",   Type = PortType.Number } },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["body"] = new() { Name = "body", Type = PortType.Number },
                        ["exit"] = new() { Name = "exit", Type = PortType.Wins   }
                    }
                },
                new DrawNode
                {
                    Id = "draw-free-spin", Label = "Free Spin Draw",
                    Inputs  = new Dictionary<string, Port> { ["in"]    = new() { Name = "in",    Type = PortType.Number } },
                    Outputs = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board  } }
                },

                // ── Sticky wilds via generic primitives (level a config) ───
                // Both configured entirely via parameters — no code written.

                // Node 1: scan board for wilds, accumulate positions into state.
                //   Configured as: extractMode=Position, mergeMode=Union
                //   → same primitive works for Hold-and-Win (Count+Sum) etc.
                new MapNode
                {
                    Id          = "map-accumulate-wilds",
                    Label       = "Accumulate Wild Positions",
                    TransformId = "accumulate-wilds",
                    Inputs  = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board } },
                    Outputs = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board } }
                },

                // Node 2: read accumulated positions from state, overlay wilds.
                //   Configured as: applyMode=OverlaySymbol, symbolId="sym-wild"
                //   → same primitive handles LockCells for a different mechanic.
                new MapNode
                {
                    Id          = "map-apply-wilds",
                    Label       = "Apply Sticky Wild Overlay",
                    TransformId = "apply-wilds",
                    Inputs  = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board } },
                    Outputs = new Dictionary<string, Port> { ["board"] = new() { Name = "board", Type = PortType.Board } }
                },

                new MapNode
                {
                    Id = "eval-free-lines", Label = "Free Spin Lines", TransformId = "lines",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board },
                        ["multiplier"] = new()
                        {
                            Name         = "multiplier",
                            Type         = PortType.Number,
                            DefaultValue = new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" }
                        }
                    }
                },

                new MetricsSinkNode
                {
                    Id = "sink", Label = "Metrics Sink",
                    Inputs = new Dictionary<string, Port> { ["wins"] = new() { Name = "wins", Type = PortType.Wins } }
                },
            },

            Edges = new[]
            {
                new Edge { Id = "e1",  SourceNodeId = "draw-spin",            SourcePort = "board", TargetNodeId = "eval-lines",           TargetPort = "board" },
                new Edge { Id = "e3",  SourceNodeId = "draw-spin",            SourcePort = "board", TargetNodeId = "branch-bonus",         TargetPort = "board" },
                new Edge { Id = "e4",  SourceNodeId = "eval-lines",           SourcePort = "wins",  TargetNodeId = "sink",                 TargetPort = "wins"  },
                new Edge { Id = "e6",  SourceNodeId = "branch-bonus",         SourcePort = "true",  TargetNodeId = "draw-fs-count",       TargetPort = "in"    },
                new Edge { Id = "e7",  SourceNodeId = "draw-fs-count",       SourcePort = "out",   TargetNodeId = "put-fs-left",         TargetPort = "in"    },
                new Edge { Id = "e8",  SourceNodeId = "put-fs-left",         SourcePort = "out",   TargetNodeId = "loopFS",              TargetPort = "in"    },
                new Edge { Id = "e9",  SourceNodeId = "loopFS",              SourcePort = "body",  TargetNodeId = "draw-free-spin",      TargetPort = "in"    },
                // draw → accumulate (write positions to state) → apply (overlay from state) → eval
                new Edge { Id = "e10", SourceNodeId = "draw-free-spin",      SourcePort = "board", TargetNodeId = "map-accumulate-wilds", TargetPort = "board" },
                new Edge { Id = "e11", SourceNodeId = "map-accumulate-wilds", SourcePort = "board", TargetNodeId = "map-apply-wilds",     TargetPort = "board" },
                new Edge { Id = "e12", SourceNodeId = "map-apply-wilds",     SourcePort = "board", TargetNodeId = "eval-free-lines",      TargetPort = "board" },
                new Edge { Id = "e13", SourceNodeId = "loopFS",              SourcePort = "exit",  TargetNodeId = "sink",                 TargetPort = "wins"  },
            },
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Graph tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NoPlugin_GraphCompilesWithoutErrors()
    {
        Register();
        var result = new GraphCompiler(_pluginHost).Compile(CreateConfig());
        Assert.True(result.IsValid,
            "Level-(a+b) Dog House graph failed to compile:\n" +
            string.Join("\n", result.Errors.Select(e => $"  [{e.NodeId ?? "-"}] {e.Code}: {e.Message}")));
    }

    [Fact]
    public void NoPlugin_GraphIsRunnable()
    {
        Register();
        var result = new GraphCompiler(_pluginHost).Compile(CreateConfig());
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));

        var sampled = SampledInterpreter.Evaluate(
            result.Program!,
            new Dictionary<string, object?>(),
            new SampledConfig { Seed = 42L, MaxSpins = 2_000, WinScale = (double)result.WinScale });

        Assert.Equal(2_000L, sampled.SpinsCompleted);
        Assert.False(sampled.WasCancelled);
        Assert.True(sampled.Stats.Mean >= 0.0);
    }

    [Fact]
    public void NoPlugin_GraphIsDeterministic()
    {
        Register();
        var result = new GraphCompiler(_pluginHost).Compile(CreateConfig());
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));

        var cfg = new SampledConfig { Seed = 99L, MaxSpins = 2_000, WinScale = (double)result.WinScale };
        var run1 = SampledInterpreter.Evaluate(result.Program!, new Dictionary<string, object?>(), cfg);
        var run2 = SampledInterpreter.Evaluate(result.Program!, new Dictionary<string, object?>(), cfg);

        Assert.Equal(run1.Stats.Mean,        run2.Stats.Mean);
        Assert.Equal(run1.Stats.MaxObserved, run2.Stats.MaxObserved);
    }

    [Fact]
    public void NoPlugin_HasCorrectTransformNodes()
    {
        var config = CreateConfig();

        var accNode   = config.Nodes.OfType<MapNode>().FirstOrDefault(n => n.Id == "map-accumulate-wilds");
        var applyNode = config.Nodes.OfType<MapNode>().FirstOrDefault(n => n.Id == "map-apply-wilds");

        Assert.NotNull(accNode);
        Assert.NotNull(applyNode);
        Assert.Equal("accumulate-wilds", accNode.TransformId);
        Assert.Equal("apply-wilds",      applyNode.TransformId);

        Assert.Contains(config.Edges, e =>
            e.SourceNodeId == "draw-free-spin" && e.TargetNodeId == "map-accumulate-wilds");
        Assert.Contains(config.Edges, e =>
            e.SourceNodeId == "map-accumulate-wilds" && e.TargetNodeId == "map-apply-wilds");
        Assert.Contains(config.Edges, e =>
            e.SourceNodeId == "map-apply-wilds" && e.TargetNodeId == "eval-free-lines");
    }

    [Fact]
    public void NoPlugin_HasNoPluginReferences()
    {
        var config = CreateConfig();
        Assert.DoesNotContain(config.Nodes.OfType<MapNode>(),
            n => n.TransformId?.StartsWith("plugin:") == true);
        Assert.Empty(config.Plugins);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BoardCellAccumulatorTransform — unit tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Accumulator_Position_Union_CollectsAndAccumulatesAcrossSpins()
    {
        var transform = new BoardCellAccumulatorTransform(
            Wild, CellExtractMode.Position, "pos", CellMergeMode.Union);

        // Spin 1: wild at (0,1)
        var cells1 = MakeGrid(2, 3, L1);
        cells1[0, 1] = Cell(Wild);
        var (_, state1) = transform.Apply(Board.FromCells(cells1), new Dictionary<string, object?>());
        var positions1 = ((Dictionary<string, object?>)state1!)["pos"] as string[];
        Assert.Equal(["0,1"], positions1);

        // Spin 2: wild at (1,2) — previous (0,1) should still be in state
        var cells2 = MakeGrid(2, 3, L2);
        cells2[1, 2] = Cell(Wild);
        var (_, state2) = transform.Apply(Board.FromCells(cells2), state1);
        var positions2 = ((Dictionary<string, object?>)state2!)["pos"] as string[];
        Assert.Equal(2, positions2!.Length);
        Assert.Contains("0,1", positions2);
        Assert.Contains("1,2", positions2);
    }

    [Fact]
    public void Accumulator_Count_Sum_AddsAcrossSpins()
    {
        var transform = new BoardCellAccumulatorTransform(
            Wild, CellExtractMode.Count, "wildCount", CellMergeMode.Sum);

        var cells1 = MakeGrid(2, 3, L1);
        cells1[0, 0] = Cell(Wild);
        cells1[0, 2] = Cell(Wild);

        var (_, state1) = transform.Apply(Board.FromCells(cells1), new Dictionary<string, object?>());
        Assert.Equal(2, ((Dictionary<string, object?>)state1!)["wildCount"]);

        var cells2 = MakeGrid(2, 3, L1);
        cells2[1, 1] = Cell(Wild);

        var (_, state2) = transform.Apply(Board.FromCells(cells2), state1);
        Assert.Equal(3, ((Dictionary<string, object?>)state2!)["wildCount"]);
    }

    [Fact]
    public void Accumulator_Count_Max_KeepsHighestCount()
    {
        var transform = new BoardCellAccumulatorTransform(
            Wild, CellExtractMode.Count, "maxWilds", CellMergeMode.Max);

        var cells1 = MakeGrid(2, 3, L1);
        cells1[0, 0] = Cell(Wild);
        cells1[0, 1] = Cell(Wild);
        cells1[0, 2] = Cell(Wild);

        var (_, state1) = transform.Apply(Board.FromCells(cells1), new Dictionary<string, object?>());
        Assert.Equal(3, ((Dictionary<string, object?>)state1!)["maxWilds"]);

        var cells2 = MakeGrid(2, 3, L1);
        cells2[1, 0] = Cell(Wild);

        var (_, state2) = transform.Apply(Board.FromCells(cells2), state1);
        Assert.Equal(3, ((Dictionary<string, object?>)state2!)["maxWilds"]); // max stays 3
    }

    [Fact]
    public void Accumulator_Symbol_Union_TracksDistinctSymbolsSeen()
    {
        var transform = new BoardCellAccumulatorTransform(
            null, CellExtractMode.Symbol, "symbols", CellMergeMode.Union);

        // 1×4 grid: H1 at (0,0), H2 at (0,1), H1 duplicate at (0,2), L1 still at (0,3)
        var cells1 = MakeGrid(1, 4, L1);
        cells1[0, 0] = Cell(H1);
        cells1[0, 1] = Cell(H2);
        cells1[0, 2] = Cell(H1); // duplicate of (0,0) — should be deduplicated

        var (_, state1) = transform.Apply(Board.FromCells(cells1), new Dictionary<string, object?>());
        var syms1 = ((Dictionary<string, object?>)state1!)["symbols"] as string[];
        Assert.Equal(3, syms1!.Length); // H1, H2, L1 — H1 duplicate collapsed
        Assert.Contains(H1, syms1);
        Assert.Contains(H2, syms1);
        Assert.Contains(L1, syms1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BoardCellApplyTransform — unit tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_OverlaySymbol_SetsWildsAtStoredPositions()
    {
        var state = new Dictionary<string, object?> { ["pos"] = new[] { "0,1", "1,0" } };
        var cells = MakeGrid(2, 2, H2);
        var board = Board.FromCells(cells);

        var (newBoard, newState) = new BoardCellApplyTransform("pos", CellApplyMode.OverlaySymbol, Wild)
            .Apply(board, state);

        Assert.Equal(H2,   newBoard[0, 0].Symbols![0]);
        Assert.Equal(Wild, newBoard[0, 1].Symbols![0]);
        Assert.Equal(Wild, newBoard[1, 0].Symbols![0]);
        Assert.Equal(H2,   newBoard[1, 1].Symbols![0]);
        Assert.Same(state, newState); // state unchanged
    }

    [Fact]
    public void Apply_EmptyPositions_ReturnsBoardUnchanged()
    {
        var state = new Dictionary<string, object?> { ["pos"] = Array.Empty<string>() };
        var cells = MakeGrid(2, 2, L3);
        var board = Board.FromCells(cells);

        var (newBoard, _) = new BoardCellApplyTransform("pos", CellApplyMode.OverlaySymbol, Wild)
            .Apply(board, state);

        Assert.Equal(L3, newBoard[0, 0].Symbols![0]); // unchanged
    }

    [Fact]
    public void AccumulatorAndApply_ComposeStickyWildsCorrectly()
    {
        var accumulate = new BoardCellAccumulatorTransform(
            Wild, CellExtractMode.Position, "sticky", CellMergeMode.Union);
        var apply = new BoardCellApplyTransform("sticky", CellApplyMode.OverlaySymbol, Wild);

        // Spin 1: wild lands at (0,0)
        var cells1 = MakeGrid(2, 3, L1);
        cells1[0, 0] = Cell(Wild);

        object? state = new Dictionary<string, object?>();
        Board board;
        (board, state) = accumulate.Apply(Board.FromCells(cells1), state);
        (board, state) = apply.Apply(board, state);
        Assert.Equal(Wild, board[0, 0].Symbols![0]);

        // Spin 2: fresh board (no wilds drawn) — (0,0) still sticks
        var cells2 = MakeGrid(2, 3, H1);
        (board, state) = accumulate.Apply(Board.FromCells(cells2), state);
        (board, state) = apply.Apply(board, state);
        Assert.Equal(Wild, board[0, 0].Symbols![0]);
        Assert.Equal(H1,   board[0, 1].Symbols![0]);

        // Spin 3: new wild at (1,2) — both positions now sticky
        var cells3 = MakeGrid(2, 3, L4);
        cells3[1, 2] = Cell(Wild);
        (board, state) = accumulate.Apply(Board.FromCells(cells3), state);
        (board, state) = apply.Apply(board, state);
        Assert.Equal(Wild, board[0, 0].Symbols![0]);
        Assert.Equal(Wild, board[1, 2].Symbols![0]);
    }

    // ── Board helpers ─────────────────────────────────────────────────────

    private static BoardCell[,] MakeGrid(int rows, int cols, string fillSymbol)
    {
        var cells = new BoardCell[rows, cols];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
                cells[r, c] = Cell(fillSymbol);
        return cells;
    }

    private static BoardCell Cell(string symbol) => new() { Symbols = new[] { symbol } };
}
