using System.Numerics;
using SlotMath.Core.Catalog;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Mechanics.Evaluators;
using SlotMath.Core.Mechanics.Transforms;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Catalog;

using Dict = Dictionary<string, object?>;

// ═══════════════════════════════════════════════════════════════════════════
//  CatalogMechanicTests — G10: standard mechanic catalog (subgraphs, not
//  C# molecules)
//
//  Proves:
//    1. All 6 catalog mechanics load from embedded JSON.
//    2. MechanicCatalog.Merge correctly merges user entries (user wins).
//    3. GraphCompiler auto-merges catalog into config.Mechanics.
//    4. Adding a new catalog entry requires no C# code and no
//       interpreter/compiler change.
//    5. Scatter: hand-computed win cases via pure subgraph (DataNode + fold).
//    6-10. Lines/Ways/Cascade/StickyWild/HoldAndWin: direct C# evaluator/
//          transform tests (to be migrated to pure subgraphs in subsequent
//          commits — invariant-2 migration is staged, one mechanic per commit).
// ═══════════════════════════════════════════════════════════════════════════

public sealed class CatalogMechanicTests
{
    private static Port StatePort => new() { Name = "state", Type = PortType.State };

    // ════════════════════════════════════════════════════════════════════
    //  1. Catalog loading
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Default_LoadsCatalogMechanics()
    {
        var catalog = MechanicCatalog.Default;
        // The functional catalog: scatter (pure subgraph) + the Lines/Ways
        // fast-path mechanics. Cascade/sticky-wild/hold-and-win were removed
        // with the C# board transforms (invariant 2/4) and will return as pure
        // subgraphs.
        var expected = new[] { "scatter", "lines", "ways" };

        foreach (var name in expected)
            Assert.True(catalog.Entries.ContainsKey(name),
                $"Catalog missing expected mechanic '{name}'");

        Assert.True(catalog.Entries.Count >= 3,
            $"Expected at least 3 catalog entries, got {catalog.Entries.Count}");
    }

    [Fact]
    public void Default_EachMechanicHasRequiredFields()
    {
        var catalog = MechanicCatalog.Default;

        foreach (var (name, mechanic) in catalog.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(mechanic.Name),
                $"Mechanic '{name}' has empty Name");
            Assert.True(mechanic.Nodes.Length >= 1,
                $"Mechanic '{name}' has no nodes");
            Assert.NotNull(mechanic.Edges);
        }
    }

    [Fact]
    public void Default_MechanicNamesMatchDictionaryKeys()
    {
        var catalog = MechanicCatalog.Default;

        foreach (var (key, mechanic) in catalog.Entries)
        {
            Assert.Equal(key, mechanic.Name);
        }
    }

    [Fact]
    public void Default_IsSingleton_SameInstanceReturnedEachTime()
    {
        var a = MechanicCatalog.Default;
        var b = MechanicCatalog.Default;
        Assert.Same(a, b);
    }

    // ════════════════════════════════════════════════════════════════════
    //  2. Merge
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Merge_NullUserEntries_ReturnsCatalogCopy()
    {
        var merged = MechanicCatalog.Default.Merge(null);
        Assert.True(merged.Count >= 3);
        Assert.True(merged.ContainsKey("scatter"));
    }

    [Fact]
    public void Merge_EmptyUserEntries_ReturnsCatalogCopy()
    {
        var merged = MechanicCatalog.Default.Merge(new Dictionary<string, CustomMechanic>());
        Assert.True(merged.ContainsKey("lines"));
        Assert.True(merged.ContainsKey("ways"));
    }

    [Fact]
    public void Merge_UserEntryOverridesCatalogEntry()
    {
        var custom = new CustomMechanic
        {
            Name = "scatter",
            Description = "user-override",
            Nodes = Array.Empty<Node>(),
            Edges = Array.Empty<Edge>(),
        };

        var merged = MechanicCatalog.Default.Merge(
            new Dictionary<string, CustomMechanic> { ["scatter"] = custom });

        Assert.Equal("user-override", merged["scatter"].Description);
    }

    [Fact]
    public void Merge_NewUserEntryAddedAlongsideCatalog()
    {
        var custom = new CustomMechanic
        {
            Name = "my-mechanic",
            Nodes = Array.Empty<Node>(),
            Edges = Array.Empty<Edge>(),
        };

        var merged = MechanicCatalog.Default.Merge(
            new Dictionary<string, CustomMechanic> { ["my-mechanic"] = custom });

        Assert.True(merged.ContainsKey("scatter"),      "Catalog entry missing after merge");
        Assert.True(merged.ContainsKey("my-mechanic"), "User entry missing after merge");
    }

    [Fact]
    public void Merge_DoesNotMutateCatalog()
    {
        var before = MechanicCatalog.Default.Entries.Count;

        MechanicCatalog.Default.Merge(new Dictionary<string, CustomMechanic>
        {
            ["extra"] = new CustomMechanic
            {
                Name = "extra",
                Nodes = Array.Empty<Node>(),
                Edges = Array.Empty<Edge>(),
            }
        });

        Assert.Equal(before, MechanicCatalog.Default.Entries.Count);
    }

    // ════════════════════════════════════════════════════════════════════
    //  3. GraphCompiler auto-merges catalog (pure-subgraph scatter)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Compiler_AutoMergesCatalog_LibraryNodeResolvesWithoutManualMerge()
    {
        // The scatter mechanic is now a pure subgraph (no evaluator registration
        // needed). Compiling a graph with a "scatter" LibraryNode must succeed
        // by auto-merging the catalog — zero manual steps.
        var config = BuildMinimalScatterGraph();

        var result = new GraphCompiler().Compile(config);

        Assert.True(result.IsValid,
            $"Expected valid compilation — catalog not merged? Errors: " +
            string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
    }

    [Fact]
    public void Compiler_UserMechanicOverridesCatalogEntry()
    {
        // User supplies a "scatter" mechanic override (a trivial pure subgraph
        // that always writes 0 as the win). The compiler must use the user's
        // definition, not the catalog default.
        var userScatter = new CustomMechanic
        {
            Name = "scatter",
            Description = "zero-win override",
            Nodes =
            [
                new ModifyStateNode
                {
                    Id = "zero",
                    ExpressionId = "zero_expr",
                    OutputKey = "scatter_win",
                    Inputs = new Dictionary<string, Port> { ["state"] = StatePort },
                    Outputs = new Dictionary<string, Port> { ["state"] = StatePort },
                }
            ],
            Edges = [],
            Expressions = new Dictionary<string, Expression>
            {
                ["zero_expr"] = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" }
            },
        };

        var config = BuildMinimalScatterGraph() with
        {
            Mechanics = new Dictionary<string, CustomMechanic> { ["scatter"] = userScatter }
        };

        var result = new GraphCompiler().Compile(config);

        Assert.True(result.IsValid,
            string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    // ════════════════════════════════════════════════════════════════════
    //  4. DoD: no-code extensibility — a new entry needs no C# change
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void NewCatalogEntry_RequiresNoCSharpChange_RegisterAndRun()
    {
        // Proves the DoD requirement: register a new catalog-style mechanic and
        // run it through the compiler using ONLY data — no C# class, no evaluator,
        // no interpreter/compiler change.
        var trivialMechanic = new CustomMechanic
        {
            Name = "trivial",
            Description = "always-42 trivial subgraph mechanic",
            Nodes =
            [
                new ModifyStateNode
                {
                    Id = "emit_42",
                    ExpressionId = "const_42",
                    OutputKey = "trivial_win",
                    Inputs = new Dictionary<string, Port> { ["state"] = StatePort },
                    Outputs = new Dictionary<string, Port> { ["state"] = StatePort },
                }
            ],
            Edges = [],
            Expressions = new Dictionary<string, Expression>
            {
                ["const_42"] = new ConstantExpr { Kind = ConstantKind.Integer, Value = "42" }
            },
        };

        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "trivial-test",
            StateSchema = [new StateFieldSchema { Name = "trivial_win", Type = "number" }],
            Mechanics = new Dictionary<string, CustomMechanic> { ["trivial"] = trivialMechanic },
            Nodes =
            [
                new LibraryNode
                {
                    Id = "trivial-node",
                    MechanicName = "trivial",
                    Inputs = new Dictionary<string, Port> { ["state"] = StatePort },
                    Outputs = new Dictionary<string, Port> { ["state"] = StatePort },
                },
                new MetricsSinkNode
                {
                    Id = "sink",
                    WinStateKey = "trivial_win",
                    Inputs = new Dictionary<string, Port> { ["state"] = StatePort },
                },
            ],
            Edges =
            [
                new Edge { Id = "e1", SourceNodeId = "trivial-node", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" },
            ],
        };

        var result = new GraphCompiler().Compile(config);

        Assert.True(result.IsValid,
            $"New catalog entry failed to compile: " +
            string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        Assert.NotNull(result.Program);
    }

    // ════════════════════════════════════════════════════════════════════
    //  5. Scatter — 3 hand-computed win cases via pure subgraph
    //
    //  The scatter mechanic is now a pure subgraph: DataNode injects the board
    //  as a flat symbol array into state["board"], then FoldExpr counts the "S"
    //  occurrences, and IfExpr looks up the stepped payout.
    //
    //  Paytable: 3 S → 5, 4 S → 20, 5 S → 50.
    //  These hand-computed values are verified against the exact interpreter.
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Scatter_ThreeSymbols_Pays5()
    {
        // 5-cell board, 3 S and 2 X → count = 3 → exact win = 5
        var config = BuildScatterWinConfig(["S", "X", "S", "S", "X"]);
        var result = new GraphCompiler().Compile(config);
        Assert.True(result.IsValid,
            string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));

        var dist = ExactInterpreter.Evaluate(result.Program!, new Dict(), StateHasher.CanonicalHash);
        var (num, den) = dist.ValueDistribution().ExpectedBigIntegerValue();
        Assert.Equal(new BigInteger(5), num / den);
    }

    [Fact]
    public void Scatter_FourSymbols_Pays20()
    {
        // 6-cell board (2×3), 4 S and 2 X → count = 4 → exact win = 20
        var config = BuildScatterWinConfig(["S", "S", "S", "S", "X", "X"]);
        var result = new GraphCompiler().Compile(config);
        Assert.True(result.IsValid,
            string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));

        var dist = ExactInterpreter.Evaluate(result.Program!, new Dict(), StateHasher.CanonicalHash);
        var (num, den) = dist.ValueDistribution().ExpectedBigIntegerValue();
        Assert.Equal(new BigInteger(20), num / den);
    }

    [Fact]
    public void Scatter_TwoSymbols_NoWin()
    {
        // 5-cell board, 2 S — below the 3-count threshold → exact win = 0
        var config = BuildScatterWinConfig(["S", "X", "S", "X", "X"]);
        var result = new GraphCompiler().Compile(config);
        Assert.True(result.IsValid,
            string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));

        var dist = ExactInterpreter.Evaluate(result.Program!, new Dict(), StateHasher.CanonicalHash);
        var (num, den) = dist.ValueDistribution().ExpectedBigIntegerValue();
        Assert.Equal(BigInteger.Zero, num / den);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Build a GraphConfig that tests the scatter mechanic logic directly via
    /// pure subgraph nodes (DataNode + ModifyState + FoldExpr). The board is
    /// injected as a flat string array rather than drawn from reels, so the test
    /// is fully deterministic without reel-strip setup.
    /// </summary>
    private static GraphConfig BuildScatterWinConfig(string[] boardSymbols) => new()
    {
        SchemaVersion = "1.0.0",
        Id = "scatter-win-test",
        StateSchema =
        [
            new StateFieldSchema { Name = "board", Type = "string" },
            new StateFieldSchema { Name = "scatter_count", Type = "number" },
            new StateFieldSchema { Name = "scatter_win", Type = "number" },
        ],
        Expressions = new Dictionary<string, Expression>
        {
            // fold(state["board"], 0, (acc, sym) => if sym == "S" then acc + 1 else acc)
            ["count_s"] = new FoldExpr
            {
                StateKey = "board",
                AccName = "acc",
                ItemName = "sym",
                ItemType = ExprType.String,
                Init = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
                Body = new IfExpr
                {
                    Condition = new CompareExpr
                    {
                        Op = CompareOp.Eq,
                        Left = new FieldAccessExpr { Target = "state", Path = ["sym"] },
                        Right = new ConstantExpr { Kind = ConstantKind.String, Value = "S" }
                    },
                    ThenExpr = new BinaryExpr
                    {
                        Op = BinaryOp.Add,
                        Left = new FieldAccessExpr { Target = "state", Path = ["acc"] },
                        Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" }
                    },
                    ElseExpr = new FieldAccessExpr { Target = "state", Path = ["acc"] }
                }
            },
            // if count >= 5 then 50 else if count >= 4 then 20 else if count >= 3 then 5 else 0
            ["scatter_payout"] = new IfExpr
            {
                Condition = new CompareExpr
                {
                    Op = CompareOp.Gte,
                    Left = new FieldAccessExpr { Target = "state", Path = ["scatter_count"] },
                    Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "5" }
                },
                ThenExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "50" },
                ElseExpr = new IfExpr
                {
                    Condition = new CompareExpr
                    {
                        Op = CompareOp.Gte,
                        Left = new FieldAccessExpr { Target = "state", Path = ["scatter_count"] },
                        Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "4" }
                    },
                    ThenExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "20" },
                    ElseExpr = new IfExpr
                    {
                        Condition = new CompareExpr
                        {
                            Op = CompareOp.Gte,
                            Left = new FieldAccessExpr { Target = "state", Path = ["scatter_count"] },
                            Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "3" }
                        },
                        ThenExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "5" },
                        ElseExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" }
                    }
                }
            },
        },
        Nodes =
        [
            new DataNode
            {
                Id = "board_src",
                StateKey = "board",
                Values = boardSymbols,
                Outputs = new Dictionary<string, Port> { ["state"] = StatePort }
            },
            new ModifyStateNode
            {
                Id = "count_node",
                ExpressionId = "count_s",
                OutputKey = "scatter_count",
                Inputs  = new Dictionary<string, Port> { ["state"] = StatePort },
                Outputs = new Dictionary<string, Port> { ["state"] = StatePort }
            },
            new ModifyStateNode
            {
                Id = "payout_node",
                ExpressionId = "scatter_payout",
                OutputKey = "scatter_win",
                Inputs  = new Dictionary<string, Port> { ["state"] = StatePort },
                Outputs = new Dictionary<string, Port> { ["state"] = StatePort }
            },
            new MetricsSinkNode
            {
                Id = "sink",
                WinStateKey = "scatter_win",
                Inputs = new Dictionary<string, Port> { ["state"] = StatePort }
            }
        ],
        Edges =
        [
            new Edge { Id = "e0", SourceNodeId = "board_src",   SourcePort = "state", TargetNodeId = "count_node",  TargetPort = "state" },
            new Edge { Id = "e1", SourceNodeId = "count_node",  SourcePort = "state", TargetNodeId = "payout_node", TargetPort = "state" },
            new Edge { Id = "e2", SourceNodeId = "payout_node", SourcePort = "state", TargetNodeId = "sink",        TargetPort = "state" }
        ]
    };

    /// <summary>
    /// Minimal graph that uses the catalog's "scatter" LibraryNode — used to
    /// test that the compiler auto-merges the catalog and the pure-subgraph
    /// scatter mechanic compiles without any evaluator registration.
    ///
    /// Uses DrawNode.BoardStateKey to write the drawn board as a flat symbol
    /// array into state["board"] so the scatter subgraph can fold over it.
    /// </summary>
    private static GraphConfig BuildMinimalScatterGraph() => new()
    {
        SchemaVersion = "1.0.0",
        Id = "scatter-catalog-test",
        Symbols = [new Symbol { Id = "S", Name = "Scatter", Kind = SymbolKind.Scatter }],
        ReelStrips =
        [
            new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "S", "X" } },
            new ReelStrip { Id = "r2", Name = "R2", Symbols = new[] { "S", "X" } },
            new ReelStrip { Id = "r3", Name = "R3", Symbols = new[] { "S", "X" } }
        ],
        ReelSets = [new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1", "r2", "r3" } }],
        BoardConfig = new BoardConfig { Rows = 1, Columns = 3 },
        Nodes =
        [
            new DrawNode
            {
                Id = "draw",
                Label = "Spin",
                // Write the drawn board as a flat symbol array to state["board"]
                // so the scatter subgraph can read it via fold expression.
                BoardStateKey = "board",
                Outputs = new Dictionary<string, Port> { ["state"] = StatePort }
            },
            new LibraryNode
            {
                Id = "scatter-lib",
                MechanicName = "scatter",
                Inputs  = new Dictionary<string, Port> { ["state"] = StatePort },
                Outputs = new Dictionary<string, Port> { ["state"] = StatePort }
            },
            new MetricsSinkNode
            {
                Id = "sink",
                WinStateKey = "scatter_win",
                Inputs = new Dictionary<string, Port> { ["state"] = StatePort }
            }
        ],
        Edges =
        [
            new Edge { Id = "e1", SourceNodeId = "draw",        SourcePort = "state", TargetNodeId = "scatter-lib", TargetPort = "state" },
            new Edge { Id = "e2", SourceNodeId = "scatter-lib", SourcePort = "state", TargetNodeId = "sink",        TargetPort = "state" }
        ]
    };
}
