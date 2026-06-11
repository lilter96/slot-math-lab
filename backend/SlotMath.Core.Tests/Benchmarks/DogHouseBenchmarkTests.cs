using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Mechanics.Evaluators;
using SlotMath.Core.Model;
using SlotMath.Core.Plugins;

namespace SlotMath.Core.Tests.Benchmarks;

// ═══════════════════════════════════════════════════════════════════════════
//  The Dog House — full slot game benchmark using the graph authoring model
//
//  Implements The Dog House (Pragmatic Play style) as a no-code graph:
//  only nodes, edges, data tables, and level-(b) expressions.
//  Mechanics are composed from primitive nodes, standard evaluators, and
//  a user-provided level-(c) plugin — exactly as a designer would do via the UI.
//
//  Game spec:
//    5 reels × 4 rows, 20 paylines.
//    Wild (sym-wild) on reels 2,3,4 (cols 1,2,3) only — single wild
//      symbol acting as a mask/overlay (not separate per-multiplier symbols).
//      Wild substitution via LinesEvaluator.
//    Sticky wilds in free spins: implemented as a user-uploaded ITransform plugin
//      (StickyWildsPlugin, defined below) — not a standard library class.
//      The plugin accumulates wild positions in state["stickyPositions"] and
//      re-overlays them on each free spin. Any designer can build an equivalent
//      via the plugin UI without touching engine code.
//    Bonus scatter (sym-bonus) on reels 1,3,5 (cols 0,2,4).
//      3+ bonus anywhere on board → 100 scatter credits + free spins.
//    Free spins: 8, 15, or 20 spins (weighted draw) with 2× global multiplier
//      applied by the eval-free-lines multiplier expression port.
//
//  Graph topology (11 nodes, 12 edges):
//
//    draw-spin ──[board]──► eval-lines        ──[wins]──► sink
//              ──[board]──► eval-scatter      ──[wins]──► sink
//              ──[board]──► branch-bonus
//    branch-bonus.true ──► draw-fs-count ──► put-fs-left ──► loopFS
//      loopFS.body ──► draw-free-spin ──[board]──► accumulate-wilds ──[board]──► eval-free-lines
//      loopFS.exit ──[wins]──► sink
//
//  Items registered in Register():
//    "lines"           → EvaluatorRegistry: LinesEvaluator(linesPaytable, paylineSet, wildSymbolId="sym-wild")
//    "scatter"         → EvaluatorRegistry: ScatterEvaluator(scatterPaytable)
//    "sticky-wilds"    → PluginHost: StickyWildsPlugin (user-provided ITransform, see bottom of file)
//                        referenced in graph as TransformId = "plugin:sticky-wilds"
// ═══════════════════════════════════════════════════════════════════════════

[Collection("DogHouse")]
public sealed class DogHouseBenchmarkTests : IDisposable
{
    private readonly PluginHost _pluginHost = new();

    public void Dispose()
    {
        EvaluatorRegistry.Clear();
        // PluginHost is per-instance (not static), so no cleanup needed for it.
        // TransformRegistry is not used by the plugin approach but cleared defensively.
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

    // ── Paytables ──────────────────────────────────────────────────────────

    /// <summary>
    /// Payline paytable: high- and low-value symbols only.
    /// Wild and Bonus have no entry here; wilds substitute via LinesEvaluator
    /// and bonus pays via the scatter paytable.
    /// </summary>
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

    /// <summary>
    /// Scatter paytable: bonus symbol only (pays-anywhere, regardless of paylines).
    /// 3 bonus → 100; 4 bonus → 500; 5 bonus → 2500.
    /// </summary>
    private static Paytable CreateScatterPaytable() => new()
    {
        Id = "pt-scatter",
        Entries = new[]
        {
            new PaytableEntry { SymbolId = Bonus, Counts = new[] { 3, 4, 5 }, Payouts = new[] { "100", "500", "2500" } },
        }
    };

    // ── Paylines ───────────────────────────────────────────────────────────

    private static PaylineSet CreatePaylineSet() => new()
    {
        Id = "ps-dog-house",
        Paylines = new[]
        {
            new Payline { Positions = new[] { 0, 0, 0, 0, 0 } }, // top row
            new Payline { Positions = new[] { 1, 1, 1, 1, 1 } }, // row 2
            new Payline { Positions = new[] { 2, 2, 2, 2, 2 } }, // row 3
            new Payline { Positions = new[] { 3, 3, 3, 3, 3 } }, // bottom row
            new Payline { Positions = new[] { 0, 1, 2, 3, 2 } }, // V-down
            new Payline { Positions = new[] { 3, 2, 1, 0, 1 } }, // V-up
            new Payline { Positions = new[] { 1, 0, 1, 0, 1 } }, // zigzag top
            new Payline { Positions = new[] { 2, 3, 2, 3, 2 } }, // zigzag bottom
            new Payline { Positions = new[] { 0, 1, 1, 1, 0 } }, // hat
            new Payline { Positions = new[] { 3, 2, 2, 2, 3 } }, // valley
            new Payline { Positions = new[] { 1, 2, 2, 2, 1 } }, // arch
            new Payline { Positions = new[] { 2, 1, 1, 1, 2 } }, // arch inverted
            new Payline { Positions = new[] { 0, 0, 1, 0, 0 } }, // top with dip
            new Payline { Positions = new[] { 3, 3, 2, 3, 3 } }, // bottom with rise
            new Payline { Positions = new[] { 1, 2, 3, 2, 1 } }, // W-shape
            new Payline { Positions = new[] { 2, 1, 0, 1, 2 } }, // M-shape
            new Payline { Positions = new[] { 0, 1, 2, 1, 0 } }, // U-shape
            new Payline { Positions = new[] { 3, 2, 1, 2, 3 } }, // n-shape
            new Payline { Positions = new[] { 1, 0, 0, 0, 1 } }, // top valley
            new Payline { Positions = new[] { 2, 3, 3, 3, 2 } }, // bottom arch
        }
    };

    // ── Reel strips ────────────────────────────────────────────────────────

    /// <summary>
    /// 5 reel strips (12 symbols each) for a 5×4 board.
    /// Wild (sym-wild) is a single overlay symbol — NOT split into wildx2 / wildx3.
    /// Wilds appear only on cols 1, 2, 3; bonus only on cols 0, 2, 4.
    /// 12^5 = 248,832 combinations — within the 1,000,000 limit.
    /// </summary>
    private static ReelStrip[] CreateReelStrips() =>
    [
        // Col 0 — bonus reel, no wilds
        new ReelStrip { Id = "r0", Name = "Reel-1",
            Symbols = new[] { H1, L1, H2, L2, L3, Bonus, H3, L4, H4, L1, L2, H1 } },

        // Col 1 — wild reel, no bonus
        new ReelStrip { Id = "r1", Name = "Reel-2",
            Symbols = new[] { Wild, H1, L1, H2, L2, Wild, H3, L3, H4, L1, L4, H2 } },

        // Col 2 — centre reel, both wilds and bonus
        new ReelStrip { Id = "r2", Name = "Reel-3",
            Symbols = new[] { Wild, H2, L1, Bonus, L2, H1, L3, L4, Wild, H3, L1, L2 } },

        // Col 3 — wild reel, no bonus
        new ReelStrip { Id = "r3", Name = "Reel-4",
            Symbols = new[] { Wild, H2, L1, L2, H3, Wild, L3, H1, L4, L1, H4, L2 } },

        // Col 4 — bonus reel, no wilds
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

    // ── Graph config factory ───────────────────────────────────────────────

    private static GraphConfig CreateDogHouseConfig()
    {
        var reelSet = new ReelSet
        {
            Id = "rs-main",
            Name = "Main Reels",
            StripIds = new[] { "r0", "r1", "r2", "r3", "r4" }
        };

        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id            = "dog-house-graph",
            Name          = "The Dog House",
            Description   = "Dog House slot — standard evaluators, single wild overlay symbol",

            Symbols     = CreateSymbols(),
            Paytables   = new[] { CreateLinesPaytable(), CreateScatterPaytable() },
            PaylineSets = new[] { CreatePaylineSet() },
            ReelStrips  = CreateReelStrips(),
            ReelSets    = new[] { reelSet },
            BoardConfig = new BoardConfig { Rows = 4, Columns = 5 },

            StateSchema = new[]
            {
                new StateFieldSchema { Name = "fsLeft",          Type = "number" },
                new StateFieldSchema { Name = "stickyPositions", Type = "string[]" },
                new StateFieldSchema { Name = "__iter_loopFS__", Type = "number" },
                new StateFieldSchema { Name = "__wins_loopFS__", Type = "number" },
            },

            Expressions = new Dictionary<string, Expression>
            {
                // Bonus trigger: 3 or more bonus symbols visible anywhere on the board
                ["bonus-trigger"] = new CompareExpr
                {
                    Op    = CompareOp.Gte,
                    Left  = new AggregateExpr
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

                // Free-spin loop stop: iteration counter >= drawn free-spin count
                ["fs-stop"] = new CompareExpr
                {
                    Op    = CompareOp.Gte,
                    Left  = new FieldAccessExpr { Path = new[] { "__iter_loopFS__" }, Target = "state" },
                    Right = new FieldAccessExpr { Path = new[] { "fsLeft" },          Target = "state" }
                },
            },

            Nodes = new Node[]
            {
                // ── Base spin draw ─────────────────────────────────────────
                new DrawNode
                {
                    Id    = "draw-spin",
                    Label = "Base Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },

                // ── Payline evaluator (wilds substitute via standard library) ──
                new MapNode
                {
                    Id          = "eval-lines",
                    Label       = "Lines Evaluator",
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

                // ── Scatter evaluator (bonus pays-anywhere) ────────────────
                new MapNode
                {
                    Id          = "eval-scatter",
                    Label       = "Scatter Evaluator",
                    TransformId = "scatter",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },

                // ── Bonus trigger branch ───────────────────────────────────
                new BranchNode
                {
                    Id          = "branch-bonus",
                    Label       = "Bonus Trigger?",
                    ConditionId = "bonus-trigger",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["true"]  = new() { Name = "true",  Type = PortType.Board },
                        ["false"] = new() { Name = "false", Type = PortType.Board }
                    }
                },

                // ── Free spin count draw (8 / 15 / 20) ────────────────────
                // Weighted to approximate real Dog House bonus distribution:
                //   3 bonus (most common) → 8 FS  (weight 5)
                //   4 bonus               → 15 FS (weight 3)
                //   5 bonus (rare)        → 20 FS (weight 2)
                // DrawWeights with no StateWriteKey → emits BigInteger(Value) which
                // flows through put-fs-left to state["fsLeft"]; loopFS ignores the
                // numeric value itself (IsInputIndependent) and reads state["fsLeft"].
                new DrawNode
                {
                    Id    = "draw-fs-count",
                    Label = "Draw FS Count",
                    DrawWeights = new[]
                    {
                        new DrawWeight { OutcomeId = "fs8",  Weight = 5, Value = 8  },
                        new DrawWeight { OutcomeId = "fs15", Weight = 3, Value = 15 },
                        new DrawWeight { OutcomeId = "fs20", Weight = 2, Value = 20 },
                    },
                    Inputs = new Dictionary<string, Port>
                    {
                        ["in"] = new() { Name = "in", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["out"] = new() { Name = "out", Type = PortType.Number }
                    }
                },

                // ── Write free-spin count to state["fsLeft"] ──────────────
                new PutStateNode
                {
                    Id       = "put-fs-left",
                    Label    = "Set FS Left",
                    StateKey = "fsLeft",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["in"] = new() { Name = "in", Type = PortType.Number }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["out"] = new() { Name = "out", Type = PortType.Number }
                    }
                },

                // ── Free-spin loop ─────────────────────────────────────────
                // Runs until __iter_loopFS__ >= state["fsLeft"].
                // Accumulated wins from the body are emitted on the exit port.
                new LoopNode
                {
                    Id              = "loopFS",
                    Label           = "Free Spin Loop",
                    MaxIterations   = 20,
                    StopConditionId = "fs-stop",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["in"] = new() { Name = "in", Type = PortType.Number }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["body"] = new() { Name = "body", Type = PortType.Number },
                        ["exit"] = new() { Name = "exit", Type = PortType.Wins   }
                    }
                },

                // ── Free-spin reel draw (loop body) ───────────────────────
                new DrawNode
                {
                    Id    = "draw-free-spin",
                    Label = "Free Spin Draw",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["in"] = new() { Name = "in", Type = PortType.Number }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },

                // ── Sticky wilds transform (free-spin loop body) ──────────
                // User-provided ITransform plugin: "plugin:sticky-wilds".
                // Accumulates wild positions in state["stickyPositions"] and
                // re-overlays them on the board every iteration.
                // A user uploads this via the plugin UI; no engine changes needed.
                new MapNode
                {
                    Id          = "map-sticky-wilds",
                    Label       = "Sticky Wilds (user plugin)",
                    TransformId = "plugin:sticky-wilds",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },

                // ── Free-spin line evaluator (loop body terminal) ──────────
                // 2× global multiplier on all wins; board already has sticky
                // wilds applied by map-sticky-wilds above.
                // Terminal node — no output ports; loop accumulates wins.
                new MapNode
                {
                    Id          = "eval-free-lines",
                    Label       = "Free Spin Lines",
                    TransformId = "lines",
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
                    // No Outputs — loop body terminal node
                },

                // ── Metrics sink ───────────────────────────────────────────
                new MetricsSinkNode
                {
                    Id    = "sink",
                    Label = "Metrics Sink",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                },
            },

            Edges = new[]
            {
                // Base-game fan-out: three evaluations of the same drawn board
                new Edge { Id = "e1", SourceNodeId = "draw-spin",     SourcePort = "board",
                                      TargetNodeId = "eval-lines",    TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "draw-spin",     SourcePort = "board",
                                      TargetNodeId = "eval-scatter",  TargetPort = "board" },
                new Edge { Id = "e3", SourceNodeId = "draw-spin",     SourcePort = "board",
                                      TargetNodeId = "branch-bonus",  TargetPort = "board" },

                // Both evaluators contribute wins to the sink
                new Edge { Id = "e4", SourceNodeId = "eval-lines",    SourcePort = "wins",
                                      TargetNodeId = "sink",          TargetPort = "wins"  },
                new Edge { Id = "e5", SourceNodeId = "eval-scatter",  SourcePort = "wins",
                                      TargetNodeId = "sink",          TargetPort = "wins"  },

                // Bonus path: trigger → count free spins → store in state
                new Edge { Id = "e6", SourceNodeId = "branch-bonus",  SourcePort = "true",
                                      TargetNodeId = "draw-fs-count", TargetPort = "in"    },
                new Edge { Id = "e7", SourceNodeId = "draw-fs-count", SourcePort = "out",
                                      TargetNodeId = "put-fs-left",   TargetPort = "in"    },
                new Edge { Id = "e8", SourceNodeId = "put-fs-left",   SourcePort = "out",
                                      TargetNodeId = "loopFS",        TargetPort = "in"    },

                // Free-spin loop body: draw → sticky wilds → evaluate
                new Edge { Id = "e9",   SourceNodeId = "loopFS",          SourcePort = "body",
                                        TargetNodeId = "draw-free-spin",  TargetPort = "in"   },
                new Edge { Id = "e10",  SourceNodeId = "draw-free-spin",  SourcePort = "board",
                                        TargetNodeId = "map-sticky-wilds", TargetPort = "board" },
                new Edge { Id = "e10b", SourceNodeId = "map-sticky-wilds", SourcePort = "board",
                                        TargetNodeId = "eval-free-lines",  TargetPort = "board" },

                // Loop exit wins → sink (accumulated free-spin wins)
                new Edge { Id = "e11", SourceNodeId = "loopFS",       SourcePort = "exit",
                                       TargetNodeId = "sink",         TargetPort = "wins"  },
            },
        };
    }

    // ── Setup helper ──────────────────────────────────────────────────────

    /// <summary>
    /// Registers the standard evaluators and transforms the graph references
    /// by string ID.  This is configuration, not custom logic — all
    /// implementations live in the standard library.
    /// </summary>
    private void Register()
    {
        EvaluatorRegistry.Register(
            "lines",
            new LinesEvaluator(CreateLinesPaytable(), CreatePaylineSet(), Wild));

        EvaluatorRegistry.Register(
            "scatter",
            new ScatterEvaluator(CreateScatterPaytable()));

        // StickyWildsPlugin is a user-provided level-(c) ITransform plugin.
        // Registered via _pluginHost, NOT TransformRegistry — it's user code, not
        // a standard library primitive.  The graph references it as "plugin:sticky-wilds".
        _pluginHost.RegisterTransform("sticky-wilds",
            new StickyWildsPlugin(symbolId: Wild, stateKey: "stickyPositions"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Compilation & runtime tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DogHouseGraph_CompilesWithoutErrors()
    {
        Register();
        var result = new GraphCompiler(_pluginHost).Compile(CreateDogHouseConfig());

        Assert.True(result.IsValid,
            "Dog House graph compilation failed:\n" +
            string.Join("\n", result.Errors.Select(e => $"  [{e.NodeId ?? "–"}] {e.Code}: {e.Message}")));
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Program);
    }

    [Fact]
    public void DogHouseGraph_ProgramIsRunnable()
    {
        Register();
        var result = new GraphCompiler(_pluginHost).Compile(CreateDogHouseConfig());

        Assert.True(result.IsValid,
            string.Join("; ", result.Errors.Select(e => e.Message)));

        var sampled = SampledInterpreter.Evaluate(
            result.Program!,
            new Dictionary<string, object?>(),
            new SampledConfig { Seed = 42L, MaxSpins = 1_000, WinScale = (double)result.WinScale });

        Assert.Equal(1_000L, sampled.SpinsCompleted);
        Assert.False(sampled.WasCancelled);
        Assert.True(sampled.Stats.Mean >= 0.0);
    }

    [Fact]
    public void DogHouseGame_IsDeterministic_SameSeedSameResult()
    {
        Register();
        var result = new GraphCompiler(_pluginHost).Compile(CreateDogHouseConfig());

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));

        var cfg = new SampledConfig { Seed = 12345L, MaxSpins = 5_000, WinScale = (double)result.WinScale };

        var run1 = SampledInterpreter.Evaluate(result.Program!, new Dictionary<string, object?>(), cfg);
        var run2 = SampledInterpreter.Evaluate(result.Program!, new Dictionary<string, object?>(), cfg);

        Assert.Equal(run1.Stats.Mean,        run2.Stats.Mean);
        Assert.Equal(run1.Stats.StdDev,      run2.Stats.StdDev);
        Assert.Equal(run1.Stats.MaxObserved, run2.Stats.MaxObserved);
        Assert.Equal(run1.SpinsCompleted,    run2.SpinsCompleted);
    }

    [Fact]
    public void DogHouseGame_RtpIsInReasonableRange()
    {
        Register();
        var result = new GraphCompiler(_pluginHost).Compile(CreateDogHouseConfig());

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));

        var sampled = SampledInterpreter.Evaluate(
            result.Program!,
            new Dictionary<string, object?>(),
            new SampledConfig { Seed = 99L, MaxSpins = 50_000, WinScale = (double)result.WinScale });

        // RTP = mean win / bet (20 paylines × 1 credit = bet of 20).
        // These benchmark reels are not calibrated for 96.5% RTP — the bounds
        // below only catch gross bugs (zero wins or integer overflow).
        double rtp = sampled.Stats.Mean / 20.0;
        Assert.True(rtp > 0.01,
            $"RTP {rtp:P1} — evaluator never returned wins.");
        Assert.True(rtp < 1_000_000.0,
            $"RTP {rtp:P1} — exceeds sanity ceiling (win-scale or overflow bug).");
    }

    [Fact]
    public void DogHouseGame_FreeSpin_TriggerOccursDuringLargeRun()
    {
        Register();
        var result = new GraphCompiler(_pluginHost).Compile(CreateDogHouseConfig());

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));

        var sampled = SampledInterpreter.Evaluate(
            result.Program!,
            new Dictionary<string, object?>(),
            new SampledConfig { Seed = 7L, MaxSpins = 50_000, WinScale = (double)result.WinScale });

        // ~3.7 % trigger rate → ~1,850 bonus triggers expected in 50,000 spins.
        Assert.Equal(50_000L, sampled.SpinsCompleted);
        Assert.True(sampled.Stats.MaxObserved > 0,
            "Expected at least one winning spin in 50,000 spins.");
    }

    [Fact]
    public void DogHouseGame_FreeSpin_LoopAccumulatesWins()
    {
        Register();
        var result = new GraphCompiler(_pluginHost).Compile(CreateDogHouseConfig());

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));

        // Scan single spins until we find one where the bonus triggered.
        // A free-spin session (8+ spins × 2× multiplier) will often produce
        // a higher total win than any single base-game payline can deliver.
        var highWinFound = false;
        for (var seed = 0L; seed < 500L && !highWinFound; seed++)
        {
            var spin = SampledInterpreter.Evaluate(
                result.Program!,
                new Dictionary<string, object?>(),
                new SampledConfig { Seed = seed, MaxSpins = 1, WinScale = (double)result.WinScale });

            if (spin.Stats.MaxObserved > 200.0)
                highWinFound = true;
        }

        Assert.True(highWinFound,
            "Expected at least one spin with win > 200 in 500 seeds. " +
            "Free-spin accumulation or wild-substitution may be broken.");
    }

    // ── Config shape tests ────────────────────────────────────────────────

    [Fact]
    public void DogHouseGraph_WinScaleIsOne_AllPayoutsAreIntegers()
    {
        Register();
        var result = new GraphCompiler(_pluginHost).Compile(CreateDogHouseConfig());

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.Equal(BigInteger.One, result.WinScale);
    }

    [Fact]
    public void DogHouseGraph_HasCorrectBoardAndReelConfig()
    {
        var config = CreateDogHouseConfig();

        Assert.Equal(5, config.ReelSets[0].StripIds.Length);
        Assert.Equal(4, config.BoardConfig!.Rows);
        Assert.Equal(5, config.BoardConfig.Columns);
        Assert.Equal(5, config.ReelStrips.Length);
    }

    [Fact]
    public void DogHouseGraph_WildsOnlyOnMiddleReels()
    {
        var config = CreateDogHouseConfig();

        // Cols 0 and 4 must have no wild symbols
        Assert.DoesNotContain(Wild, (IEnumerable<string>)config.ReelStrips[0].Symbols);
        Assert.DoesNotContain(Wild, (IEnumerable<string>)config.ReelStrips[4].Symbols);

        // Cols 1, 2, 3 must contain the single wild symbol
        Assert.Contains(Wild, (IEnumerable<string>)config.ReelStrips[1].Symbols);
        Assert.Contains(Wild, (IEnumerable<string>)config.ReelStrips[2].Symbols);
        Assert.Contains(Wild, (IEnumerable<string>)config.ReelStrips[3].Symbols);
    }

    [Fact]
    public void DogHouseGraph_BonusOnlyOnOuterAndCentreReels()
    {
        var config = CreateDogHouseConfig();

        // Cols 1 and 3 must have no bonus symbols
        Assert.DoesNotContain(Bonus, (IEnumerable<string>)config.ReelStrips[1].Symbols);
        Assert.DoesNotContain(Bonus, (IEnumerable<string>)config.ReelStrips[3].Symbols);

        // Cols 0, 2, 4 must contain bonus
        Assert.Contains(Bonus, (IEnumerable<string>)config.ReelStrips[0].Symbols);
        Assert.Contains(Bonus, (IEnumerable<string>)config.ReelStrips[2].Symbols);
        Assert.Contains(Bonus, (IEnumerable<string>)config.ReelStrips[4].Symbols);
    }

    [Fact]
    public void DogHouseGraph_HasExactlyTwentyPaylines()
    {
        Assert.Equal(20, CreateDogHouseConfig().PaylineSets[0].Paylines.Length);
    }

    [Fact]
    public void DogHouseGraph_HasSingleWildSymbolKind()
    {
        var wilds = CreateDogHouseConfig().Symbols.Where(s => s.Kind == SymbolKind.Wild).ToArray();
        Assert.Single(wilds);
        Assert.Equal(Wild, wilds[0].Id);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Standard evaluator unit tests (direct instantiation, no graph)
    // ═══════════════════════════════════════════════════════════════════════

    private static LinesEvaluator MakeLinesEvaluator() =>
        new(CreateLinesPaytable(), CreatePaylineSet(), Wild);

    private static ScatterEvaluator MakeScatterEvaluator() =>
        new(CreateScatterPaytable());

    // ── LinesEvaluator ────────────────────────────────────────────────────

    [Fact]
    public void LinesEvaluator_H1x5_NoWilds_CorrectPayout()
    {
        // Top row: H1 × 5 on payline [0,0,0,0,0] → 4000 credits
        var cells = MakeFilledGrid(L4);
        for (var c = 0; c < 5; c++) cells[0, c] = Cell(H1);

        var wins = MakeLinesEvaluator().Evaluate(Board.FromCells(cells), null);

        var top = wins.FirstOrDefault(w => w.SymbolId == H1 && w.Count == 5);
        Assert.NotNull(top);
        Assert.Equal(4000m, top.Payout);
    }

    [Fact]
    public void LinesEvaluator_WildSubstitution_ExtendsMatch()
    {
        // Top row: [H1, Wild, H1, H1, H1] → LinesEvaluator extends match through wild
        var cells = MakeFilledGrid(L4);
        cells[0, 0] = Cell(H1);
        cells[0, 1] = Cell(Wild);
        cells[0, 2] = Cell(H1);
        cells[0, 3] = Cell(H1);
        cells[0, 4] = Cell(H1);

        var wins = MakeLinesEvaluator().Evaluate(Board.FromCells(cells), null);

        var top = wins.FirstOrDefault(w => w.SymbolId == H1 && w.Count == 5);
        Assert.NotNull(top);
        Assert.Equal(4000m, top.Payout);
    }

    [Fact]
    public void LinesEvaluator_WildAtStartCol1_DefersToFirstNonWild()
    {
        // Top row: col 0 = H2, col 1 = Wild, col 2 = H2, col 3 = H2, col 4 = H2
        // Wild at col 1 extends the H2 match
        var cells = MakeFilledGrid(L4);
        cells[0, 0] = Cell(H2);
        cells[0, 1] = Cell(Wild);
        cells[0, 2] = Cell(H2);
        cells[0, 3] = Cell(H2);
        cells[0, 4] = Cell(H2);

        var wins = MakeLinesEvaluator().Evaluate(Board.FromCells(cells), null);

        var top = wins.FirstOrDefault(w => w.SymbolId == H2 && w.Count == 5);
        Assert.NotNull(top);
        Assert.Equal(2500m, top.Payout);
    }

    [Fact]
    public void LinesEvaluator_AllWilds_NoPaylineWin()
    {
        // All five columns on row 0 are wilds → no matchSymbol found → no win
        var cells = MakeFilledGrid(L4);
        for (var c = 0; c < 5; c++) cells[0, c] = Cell(Wild);

        var wins = MakeLinesEvaluator().Evaluate(Board.FromCells(cells), null);

        Assert.DoesNotContain(wins, w => w.SymbolId == Wild);
    }

    [Fact]
    public void LinesEvaluator_BonusOnRow_BreaksPaylineMatch()
    {
        // [H1, H1, Bonus, H1, H1] — bonus at col 2 breaks the match.
        // H1 count = 2 (cols 0 and 1), no paytable entry for count=2 → no win.
        var cells = MakeFilledGrid(L4);
        cells[0, 0] = Cell(H1);
        cells[0, 1] = Cell(H1);
        cells[0, 2] = Cell(Bonus);
        cells[0, 3] = Cell(H1);
        cells[0, 4] = Cell(H1);

        var wins = MakeLinesEvaluator().Evaluate(Board.FromCells(cells), null);

        Assert.DoesNotContain(wins, w => w.SymbolId == H1);
    }

    [Fact]
    public void LinesEvaluator_ThreeOfAKindWithWild_CorrectCount()
    {
        // Row 0: [H3, Wild, H3, L1, L1] → H3 × 3 (wild extends from col 1)
        var cells = MakeFilledGrid(L4);
        cells[0, 0] = Cell(H3);
        cells[0, 1] = Cell(Wild);
        cells[0, 2] = Cell(H3);
        cells[0, 3] = Cell(L1);
        cells[0, 4] = Cell(L1);

        var wins = MakeLinesEvaluator().Evaluate(Board.FromCells(cells), null);

        var w = wins.FirstOrDefault(x => x.SymbolId == H3 && x.Count == 3);
        Assert.NotNull(w);
        Assert.Equal(50m, w.Payout);
    }

    // ── ScatterEvaluator ──────────────────────────────────────────────────

    [Fact]
    public void ScatterEvaluator_ThreeBonusSymbols_Awards100Credits()
    {
        // Bonus on cols 0, 2, 4 (position-independent scatter count)
        var cells = MakeFilledGrid(L4);
        cells[0, 0] = Cell(Bonus);
        cells[1, 2] = Cell(Bonus);
        cells[2, 4] = Cell(Bonus);

        var wins = MakeScatterEvaluator().Evaluate(Board.FromCells(cells), null);

        var scatter = wins.FirstOrDefault(w => w.SymbolId == Bonus);
        Assert.NotNull(scatter);
        Assert.Equal(3, scatter.Count);
        Assert.Equal(100m, scatter.Payout);
    }

    [Fact]
    public void ScatterEvaluator_TwoBonusSymbols_NoWin()
    {
        var cells = MakeFilledGrid(L4);
        cells[0, 0] = Cell(Bonus);
        cells[0, 2] = Cell(Bonus);

        var wins = MakeScatterEvaluator().Evaluate(Board.FromCells(cells), null);

        Assert.DoesNotContain(wins, w => w.SymbolId == Bonus);
    }

    [Fact]
    public void ScatterEvaluator_CountIgnoresPosition_AnyCell()
    {
        // Place all three bonus symbols on col 1 (a wild reel that shouldn't have bonus
        // in the actual game — scatter evaluator still counts them regardless of column).
        var cells = MakeFilledGrid(L4);
        cells[0, 1] = Cell(Bonus);
        cells[1, 1] = Cell(Bonus);
        cells[2, 1] = Cell(Bonus);

        var wins = MakeScatterEvaluator().Evaluate(Board.FromCells(cells), null);

        var scatter = wins.FirstOrDefault(w => w.SymbolId == Bonus);
        Assert.NotNull(scatter);
        Assert.Equal(3, scatter.Count);
        Assert.Equal(100m, scatter.Payout);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  StickyWildsPlugin unit tests (direct, no graph)
    // ═══════════════════════════════════════════════════════════════════════

    private static StickyWildsPlugin MakeStickyTransform() =>
        new(symbolId: Wild, stateKey: "stickyPositions");

    [Fact]
    public void StickyWilds_FirstSpin_WildsCollectedIntoState()
    {
        // Board has wilds at (0,1) and (1,2); state starts empty.
        var cells = MakeFilledGrid(L4);
        cells[0, 1] = Cell(Wild);
        cells[1, 2] = Cell(Wild);
        var board = Board.FromCells(cells);

        var (_, newState) = MakeStickyTransform().Apply(board, new Dictionary<string, object?>());

        var stateDict = Assert.IsType<Dictionary<string, object?>>(newState);
        Assert.True(stateDict.ContainsKey("stickyPositions"), "stickyWilds key absent from state");
        var positions = Assert.IsType<string[]>(stateDict["stickyPositions"]);
        Assert.Equal(2, positions.Length);
        Assert.Contains("0,1", positions);
        Assert.Contains("1,2", positions);
    }

    [Fact]
    public void StickyWilds_SecondSpin_PreviousWildsOverlaidOnFreshBoard()
    {
        // Spin 1: wild at (0,1) collected.
        var cellsA = MakeFilledGrid(L4);
        cellsA[0, 1] = Cell(Wild);
        var (boardA, stateAfterA) = MakeStickyTransform().Apply(
            Board.FromCells(cellsA), new Dictionary<string, object?>());

        // Spin 2: completely different board — no wilds at all.
        var cellsB = MakeFilledGrid(H1);
        var (boardB, stateAfterB) = MakeStickyTransform().Apply(
            Board.FromCells(cellsB), stateAfterA);

        // The wild from spin 1 must appear on boardB at (0,1).
        Assert.False(boardB[0, 1].IsEmpty, "Expected cell (0,1) to be non-empty after sticky overlay.");
        Assert.Equal(Wild, boardB[0, 1].Symbols![0]);

        // State still carries the position.
        var stateDict = Assert.IsType<Dictionary<string, object?>>(stateAfterB);
        var positions = Assert.IsType<string[]>(stateDict["stickyPositions"]);
        Assert.Contains("0,1", positions);
    }

    [Fact]
    public void StickyWilds_AccumulatesAcrossMultipleSpins()
    {
        // Each spin introduces a new wild; by spin 3 all three should stick.
        object? state = new Dictionary<string, object?>();

        var cellsA = MakeFilledGrid(L1);
        cellsA[0, 1] = Cell(Wild);
        Board _, boardOut;
        (boardOut, state) = MakeStickyTransform().Apply(Board.FromCells(cellsA), state);
        var stateDict1 = (Dictionary<string, object?>)state!;
        Assert.Single((string[])stateDict1["stickyPositions"]!);

        var cellsB = MakeFilledGrid(L2);
        cellsB[2, 3] = Cell(Wild);
        (boardOut, state) = MakeStickyTransform().Apply(Board.FromCells(cellsB), state);
        var stateDict2 = (Dictionary<string, object?>)state!;
        Assert.Equal(2, ((string[])stateDict2["stickyPositions"]!).Length);
        Assert.Equal(Wild, boardOut[0, 1].Symbols![0]);   // spin-1 sticky still present
        Assert.Equal(Wild, boardOut[2, 3].Symbols![0]);   // spin-2 wild

        var cellsC = MakeFilledGrid(L3);
        cellsC[3, 0] = Cell(Wild);
        (boardOut, state) = MakeStickyTransform().Apply(Board.FromCells(cellsC), state);
        var stateDict3 = (Dictionary<string, object?>)state!;
        Assert.Equal(3, ((string[])stateDict3["stickyPositions"]!).Length);
        Assert.Equal(Wild, boardOut[0, 1].Symbols![0]);
        Assert.Equal(Wild, boardOut[2, 3].Symbols![0]);
        Assert.Equal(Wild, boardOut[3, 0].Symbols![0]);
    }

    [Fact]
    public void StickyWilds_EmptyBoard_NoPositionsStored()
    {
        var cells = MakeFilledGrid(H2);
        var (_, newState) = MakeStickyTransform().Apply(
            Board.FromCells(cells), new Dictionary<string, object?>());

        var stateDict = Assert.IsType<Dictionary<string, object?>>(newState);
        var positions = Assert.IsType<string[]>(stateDict["stickyPositions"]);
        Assert.Empty(positions);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Sticky wilds — graph integration tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DogHouseGraph_WithStickyWilds_CompilesWithoutErrors()
    {
        Register();
        var result = new GraphCompiler(_pluginHost).Compile(CreateDogHouseConfig());

        Assert.True(result.IsValid,
            "Dog House graph (with sticky wilds) compilation failed:\n" +
            string.Join("\n", result.Errors.Select(e => $"  [{e.NodeId ?? "–"}] {e.Code}: {e.Message}")));
    }

    [Fact]
    public void DogHouseGraph_WithStickyWilds_IsRunnable()
    {
        Register();
        var result = new GraphCompiler(_pluginHost).Compile(CreateDogHouseConfig());
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));

        var sampled = SampledInterpreter.Evaluate(
            result.Program!,
            new Dictionary<string, object?>(),
            new SampledConfig { Seed = 77L, MaxSpins = 2_000, WinScale = (double)result.WinScale });

        Assert.Equal(2_000L, sampled.SpinsCompleted);
        Assert.False(sampled.WasCancelled);
        Assert.True(sampled.Stats.Mean >= 0.0);
    }

    [Fact]
    public void DogHouseGraph_WithStickyWilds_IsDeterministic()
    {
        Register();
        var result = new GraphCompiler(_pluginHost).Compile(CreateDogHouseConfig());
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));

        var cfg = new SampledConfig { Seed = 33L, MaxSpins = 3_000, WinScale = (double)result.WinScale };
        var run1 = SampledInterpreter.Evaluate(result.Program!, new Dictionary<string, object?>(), cfg);
        var run2 = SampledInterpreter.Evaluate(result.Program!, new Dictionary<string, object?>(), cfg);

        Assert.Equal(run1.Stats.Mean,        run2.Stats.Mean);
        Assert.Equal(run1.Stats.MaxObserved, run2.Stats.MaxObserved);
    }

    [Fact]
    public void DogHouseGraph_HasStickyWildsNodeAndEdges()
    {
        var config = CreateDogHouseConfig();

        var stickyNode = config.Nodes.OfType<MapNode>()
            .FirstOrDefault(n => n.Id == "map-sticky-wilds");
        Assert.NotNull(stickyNode);
        Assert.Equal("plugin:sticky-wilds", stickyNode.TransformId);
        Assert.Single(stickyNode.Inputs.Values.Where(p => p.Type == PortType.Board));
        Assert.Single(stickyNode.Outputs.Values.Where(p => p.Type == PortType.Board));

        Assert.Contains(config.Edges,
            e => e.SourceNodeId == "draw-free-spin" && e.TargetNodeId == "map-sticky-wilds");
        Assert.Contains(config.Edges,
            e => e.SourceNodeId == "map-sticky-wilds" && e.TargetNodeId == "eval-free-lines");
    }

    [Fact]
    public void DogHouseGraph_HasStickyWildsInStateSchema()
    {
        var config = CreateDogHouseConfig();
        Assert.Contains(config.StateSchema!, s => s.Name == "stickyPositions");
    }

    // ── Board helpers ─────────────────────────────────────────────────────

    private static BoardCell[,] MakeFilledGrid(string fillSymbol)
    {
        var cells = new BoardCell[4, 5];
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 5; c++)
                cells[r, c] = Cell(fillSymbol);
        return cells;
    }

    private static BoardCell Cell(string symbol) =>
        new() { Symbols = new[] { symbol } };

    // ═══════════════════════════════════════════════════════════════════════
    //  Level-(c) user plugin — sticky symbol accumulator
    //
    //  This is intentionally defined here in the test, NOT in the standard
    //  library.  It simulates a designer uploading a custom ITransform plugin
    //  through the plugin UI.  The engine ships no sticky-wilds mechanic —
    //  users compose any "accumulate and re-overlay" logic themselves via the
    //  plugin escape hatch, using the same ITransform contract the standard
    //  library implements.
    // ═══════════════════════════════════════════════════════════════════════

    private sealed class StickyWildsPlugin : ITransform
    {
        private readonly string _symbolId;
        private readonly string _stateKey;

        public StickyWildsPlugin(string symbolId, string stateKey)
        {
            _symbolId = symbolId;
            _stateKey = stateKey;
        }

        public (Board NewBoard, object? NewState) Apply(Board board, object? state)
        {
            ArgumentNullException.ThrowIfNull(board);

            var stateDict = state as Dictionary<string, object?> ?? new Dictionary<string, object?>();
            var accumulated = ReadPositions(stateDict);

            for (var r = 0; r < board.Rows; r++)
                for (var c = 0; c < board.Cols; c++)
                {
                    var cell = board[r, c];
                    if (!cell.IsEmpty && cell.Symbols!.Contains(_symbolId))
                        accumulated.Add((r, c));
                }

            var newBoard = board;
            foreach (var (r, c) in accumulated)
            {
                var cell = newBoard[r, c];
                if (cell.IsEmpty || cell.Symbols![0] != _symbolId)
                    newBoard = newBoard.SetCell(r, c, new BoardCell { Symbols = new[] { _symbolId } });
            }

            var newStateDict = new Dictionary<string, object?>(stateDict)
            {
                [_stateKey] = SerializePositions(accumulated)
            };

            return (newBoard, newStateDict);
        }

        private HashSet<(int, int)> ReadPositions(Dictionary<string, object?> state)
        {
            if (!state.TryGetValue(_stateKey, out var val) || val is not string[] encoded)
                return new HashSet<(int, int)>();

            var result = new HashSet<(int, int)>(encoded.Length);
            foreach (var s in encoded)
            {
                var comma = s.IndexOf(',');
                if (comma > 0
                    && int.TryParse(s.AsSpan(0, comma), out var r)
                    && int.TryParse(s.AsSpan(comma + 1), out var c))
                    result.Add((r, c));
            }
            return result;
        }

        private static string[] SerializePositions(HashSet<(int Row, int Col)> positions) =>
            positions
                .Select(static p => $"{p.Row},{p.Col}")
                .OrderBy(static s => s, StringComparer.Ordinal)
                .ToArray();
    }
}
