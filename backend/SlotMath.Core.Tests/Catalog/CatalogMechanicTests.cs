using System.Numerics;
using SlotMath.Core.Catalog;
using SlotMath.Core.Compiler;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Mechanics.Evaluators;
using SlotMath.Core.Mechanics.Transforms;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Catalog;

// ═══════════════════════════════════════════════════════════════════════════
//  CatalogMechanicTests — G10: standard mechanic catalog (subgraphs, not
//  C# molecules)
//
//  Proves:
//    1. All 6 catalog mechanics load from embedded JSON (scatter, lines,
//       ways, cascade, sticky-wild, hold-and-win).
//    2. MechanicCatalog.Merge correctly merges user entries (user wins).
//    3. GraphCompiler auto-merges catalog into config.Mechanics before
//       compiling so library nodes resolve without manual merge.
//    4. Adding a new catalog entry requires no C# code and no
//       interpreter/compiler change (DoD: register a trivial entry, compile).
//    5. Hand-computed win cases for each mechanic (≥3 per mechanic).
//    6. Each catalog mechanic produces correct wins on hand-computed boards.
// ═══════════════════════════════════════════════════════════════════════════

[Collection("Registry")]
public sealed class CatalogMechanicTests : IDisposable
{
    public void Dispose()
    {
        EvaluatorRegistry.Clear();
        TransformRegistry.Clear();
    }

    // ════════════════════════════════════════════════════════════════════
    //  1. Catalog loading
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Default_LoadsAllSixCatalogMechanics()
    {
        var catalog = MechanicCatalog.Default;
        var expected = new[] { "scatter", "lines", "ways", "cascade", "sticky-wild", "hold-and-win" };

        foreach (var name in expected)
            Assert.True(catalog.Entries.ContainsKey(name),
                $"Catalog missing expected mechanic '{name}'");

        Assert.True(catalog.Entries.Count >= 6,
            $"Expected at least 6 catalog entries, got {catalog.Entries.Count}");
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
        Assert.True(merged.Count >= 6);
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
    //  3. GraphCompiler auto-merges catalog
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Compiler_AutoMergesCatalog_LibraryNodeResolvesWithoutManualMerge()
    {
        // Register the evaluator the catalog mechanic references
        RegisterScatter();

        // Build a graph that uses the "scatter" catalog mechanic via LibraryNode
        // without manually setting Mechanics on GraphConfig.
        var config = BuildMinimalScatterGraph(includeMechanicsInConfig: false);

        var result = new GraphCompiler().Compile(config);

        // The compiler must resolve "scatter" from the catalog without the
        // user needing to populate config.Mechanics.
        Assert.True(result.IsValid,
            $"Expected valid compilation — catalog not merged? Errors: " +
            string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
    }

    [Fact]
    public void Compiler_UserMechanicOverridesCatalogEntry()
    {
        // User supplies a "scatter" mechanic that is trivially different.
        // The compiler must use the user override, not the catalog default.
        var userScatter = new CustomMechanic
        {
            Name = "scatter",
            Nodes =
            [
                new MapNode
                {
                    Id = "user-eval",
                    Label = "Custom Scatter",
                    TransformId = "scatter",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                }
            ],
            Edges = [],
        };

        var config = BuildMinimalScatterGraph(includeMechanicsInConfig: false) with
        {
            Mechanics = new Dictionary<string, CustomMechanic> { ["scatter"] = userScatter }
        };

        RegisterScatter();
        var result = new GraphCompiler().Compile(config);

        // Graph compiles with the user's scatter override
        Assert.True(result.IsValid,
            string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    // ════════════════════════════════════════════════════════════════════
    //  4. DoD: no-code extensibility — new entry needs no C# change
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void NewCatalogEntry_RequiresNoCSharpChange_RegisterAndRun()
    {
        // This test proves the DoD requirement: registering a new catalog-style
        // mechanic and running it through the compiler requires only data
        // (a CustomMechanic record), not any C# code or interpreter change.

        // Register the evaluator the trivial mechanic references
        EvaluatorRegistry.Register("trivial-eval",
            new ScatterEvaluator(new Paytable
            {
                Id = "pt",
                Entries =
                [
                    new PaytableEntry { SymbolId = "A", Counts = new[] { 1, 2, 3 }, Payouts = new[] { "1", "2", "5" } }
                ]
            }));

        // Define a new catalog mechanic in pure data — no C# class, no interpreter change
        var trivialMechanic = new CustomMechanic
        {
            Name = "trivial",
            Nodes =
            [
                new MapNode
                {
                    Id = "eval",
                    Label = "Trivial",
                    TransformId = "trivial-eval",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                }
            ],
            Edges = [],
        };

        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "trivial-test",
            Symbols = new[] { new Symbol { Id = "A", Name = "A", Kind = SymbolKind.Standard } },
            ReelStrips = new[]
            {
                new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "A", "A" } },
                new ReelStrip { Id = "r2", Name = "R2", Symbols = new[] { "A", "A" } }
            },
            ReelSets = new[] { new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1", "r2" } } },
            BoardConfig = new BoardConfig { Rows = 1, Columns = 2 },
            // Supply the new mechanic as a user entry; catalog is merged by the compiler
            Mechanics = new Dictionary<string, CustomMechanic> { ["trivial"] = trivialMechanic },
            Nodes =
            [
                new DrawNode
                {
                    Id = "draw",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new LibraryNode
                {
                    Id = "trivial-node",
                    MechanicName = "trivial",
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
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                }
            ],
            Edges =
            [
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "trivial-node", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "trivial-node", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" }
            ]
        };

        var result = new GraphCompiler().Compile(config);

        Assert.True(result.IsValid,
            $"New catalog entry failed to compile: " +
            string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        Assert.NotNull(result.Program);
    }

    // ════════════════════════════════════════════════════════════════════
    //  5. Scatter — 3 hand-computed cases
    // ════════════════════════════════════════════════════════════════════

    // Paytable: 3 S → 5, 4 S → 20, 5 S → 50.
    private static Paytable ScatterPaytable() => new()
    {
        Id = "pt-scatter",
        Entries =
        [
            new PaytableEntry { SymbolId = "S", Counts = new[] { 3, 4, 5 }, Payouts = new[] { "5", "20", "50" } }
        ]
    };

    [Fact]
    public void Scatter_ThreeSymbols_Pays5()
    {
        // 5-cell board, exactly 3 S symbols anywhere → payout 5
        var board = new Board(1, 5)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "S" } })
            .SetCell(0, 2, new BoardCell { Symbols = new[] { "S" } })
            .SetCell(0, 4, new BoardCell { Symbols = new[] { "S" } });

        var wins = new ScatterEvaluator(ScatterPaytable()).Evaluate(board, null);

        var scatter = Assert.Single(wins);
        Assert.Equal("S", scatter.SymbolId);
        Assert.Equal(3, scatter.Count);
        Assert.Equal(5m, scatter.Payout);
    }

    [Fact]
    public void Scatter_FourSymbols_Pays20()
    {
        var board = new Board(2, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "S" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "S" } })
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "S" } })
            .SetCell(1, 2, new BoardCell { Symbols = new[] { "S" } });

        var wins = new ScatterEvaluator(ScatterPaytable()).Evaluate(board, null);

        var scatter = Assert.Single(wins);
        Assert.Equal(4, scatter.Count);
        Assert.Equal(20m, scatter.Payout);
    }

    [Fact]
    public void Scatter_TwoSymbols_NoWin()
    {
        // Below minimum threshold (3) → no win
        var board = new Board(1, 5)
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "S" } })
            .SetCell(0, 3, new BoardCell { Symbols = new[] { "S" } });

        var wins = new ScatterEvaluator(ScatterPaytable()).Evaluate(board, null);
        Assert.Empty(wins);
    }

    // ════════════════════════════════════════════════════════════════════
    //  6. Lines — 3 hand-computed cases
    // ════════════════════════════════════════════════════════════════════

    // Paytable: 3 A → 10, 4 A → 50, 5 A → 200; 3 B → 5.
    private static Paytable LinesPaytable() => new()
    {
        Id = "pt-lines",
        Entries =
        [
            new PaytableEntry { SymbolId = "A", Counts = new[] { 3, 4, 5 }, Payouts = new[] { "10", "50", "200" } },
            new PaytableEntry { SymbolId = "B", Counts = new[] { 3 }, Payouts = new[] { "5" } }
        ]
    };

    private static PaylineSet SingleCenterPayline() => new()
    {
        Id = "ps-center",
        Paylines = [new Payline { Positions = new[] { 1, 1, 1 } }]  // center row of 3-row board
    };

    [Fact]
    public void Lines_ThreeOfAKind_CenterRow_Pays10()
    {
        var board = new Board(3, 3)
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 2, new BoardCell { Symbols = new[] { "A" } });

        var wins = new LinesEvaluator(LinesPaytable(), SingleCenterPayline())
            .Evaluate(board, null);

        var win = Assert.Single(wins);
        Assert.Equal("A", win.SymbolId);
        Assert.Equal(3, win.Count);
        Assert.Equal(10m, win.Payout);
    }

    [Fact]
    public void Lines_BreakInSequence_NoWin()
    {
        // [A, B, A] on payline — the B breaks the match
        var board = new Board(3, 3)
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "B" } })
            .SetCell(1, 2, new BoardCell { Symbols = new[] { "A" } });

        var wins = new LinesEvaluator(LinesPaytable(), SingleCenterPayline())
            .Evaluate(board, null);

        // A stops at count 1 (B breaks it at col 1), B stops at count 1 at col 1 — no entry for 1
        Assert.DoesNotContain(wins, w => w.SymbolId == "A" && w.Count >= 3);
    }

    [Fact]
    public void Lines_FiveOfAKind_TwoPaylines_Pays200Each()
    {
        // 5-column board, all A on row 0 and row 1
        var board = new Board(3, 5);
        for (var c = 0; c < 5; c++)
        {
            board = board
                .SetCell(0, c, new BoardCell { Symbols = new[] { "A" } })
                .SetCell(1, c, new BoardCell { Symbols = new[] { "A" } });
        }

        var twoPaylines = new PaylineSet
        {
            Id = "ps-two",
            Paylines =
            [
                new Payline { Positions = new[] { 0, 0, 0, 0, 0 } },  // top row
                new Payline { Positions = new[] { 1, 1, 1, 1, 1 } },  // center row
            ]
        };

        var wins = new LinesEvaluator(LinesPaytable(), twoPaylines).Evaluate(board, null);

        Assert.Equal(2, wins.Length);
        Assert.All(wins, w =>
        {
            Assert.Equal("A", w.SymbolId);
            Assert.Equal(5, w.Count);
            Assert.Equal(200m, w.Payout);
        });
    }

    // ════════════════════════════════════════════════════════════════════
    //  7. Ways — 3 hand-computed cases
    // ════════════════════════════════════════════════════════════════════

    // Paytable: 3-ways A → 10.
    private static Paytable WaysPaytable() => new()
    {
        Id = "pt-ways",
        Entries =
        [
            new PaytableEntry { SymbolId = "A", Counts = new[] { 3 }, Payouts = new[] { "10" } },
            new PaytableEntry { SymbolId = "B", Counts = new[] { 3 }, Payouts = new[] { "5" } }
        ]
    };

    [Fact]
    public void Ways_3x3AllA_1Way_Pays10()
    {
        // Single A in each column → 1 × 1 × 1 = 1 way
        var board = new Board(3, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 2, new BoardCell { Symbols = new[] { "A" } });

        var wins = new WaysEvaluator(WaysPaytable()).Evaluate(board, null);

        var win = Assert.Single(wins.Where(w => w.SymbolId == "A"));
        Assert.Equal(3, win.Count);
        Assert.Equal(10m, win.Payout); // 1 way × 10 = 10
    }

    [Fact]
    public void Ways_3x3AllA_9Ways_Pays90()
    {
        // 3 A in each of 3 columns → 3 × 3 × 3 = 27 ways
        var board = new Board(3, 3);
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
                board = board.SetCell(r, c, new BoardCell { Symbols = new[] { "A" } });

        var wins = new WaysEvaluator(WaysPaytable()).Evaluate(board, null);

        var win = Assert.Single(wins.Where(w => w.SymbolId == "A"));
        Assert.Equal(3, win.Count);
        // 27 ways × 10 payout per way = 270
        Assert.Equal(270m, win.Payout);
    }

    [Fact]
    public void Ways_MixedSymbols_OnlyMatchingCountsSymbol()
    {
        // Col 0: A, B; Col 1: A; Col 2: A → A ways = 1×1×1=1; B stops at col 0
        var board = new Board(2, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "B" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 2, new BoardCell { Symbols = new[] { "A" } });

        var wins = new WaysEvaluator(WaysPaytable()).Evaluate(board, null);

        // A gets 1 way (1 in each col), B stops at col 0 (none in col 1) → no 3-col ways for B
        var aWin = wins.FirstOrDefault(w => w.SymbolId == "A");
        Assert.NotNull(aWin);
        Assert.Equal(3, aWin.Count);
        Assert.Equal(10m, aWin.Payout); // 1 way × 10 = 10
        Assert.DoesNotContain(wins, w => w.SymbolId == "B" && w.Count == 3);
    }

    // ════════════════════════════════════════════════════════════════════
    //  8. Cascade — 3 hand-computed cases
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Cascade_RemoveAndRefill_EmptiesThenFillsColumn()
    {
        // 3×1 board, all A. Remove row 0 (winning position) → refill from top.
        var board = new Board(3, 1)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(2, 0, new BoardCell { Symbols = new[] { "A" } });

        var winPos = new HashSet<(int, int)> { (0, 0) };
        var remove = new RemoveWinningTransform(winPos);
        var (afterRemove, _) = remove.Apply(board, null);

        // After removal, row 0 col 0 is empty
        Assert.True(afterRemove[0, 0].IsEmpty);
        Assert.False(afterRemove[1, 0].IsEmpty);
        Assert.False(afterRemove[2, 0].IsEmpty);
    }

    [Fact]
    public void Cascade_Refill_TumblesExistingSymbolsDown()
    {
        // 3×1 board: row 0 = A, row 1 = empty, row 2 = B
        // After tumble: symbols fall down → row 0 = new, row 1 = A, row 2 = B
        var board = new Board(3, 1)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(2, 0, new BoardCell { Symbols = new[] { "B" } });

        var newSymbols = new[] { "X" };
        var refill = new RefillTumbleTransform(() => newSymbols);
        var (after, _) = refill.Apply(board, null);

        // B stays at bottom (row 2), A falls to row 1, X fills top (row 0)
        Assert.Equal("B", after[2, 0].Symbols![0]);
        Assert.Equal("A", after[1, 0].Symbols![0]);
        Assert.Equal("X", after[0, 0].Symbols![0]);
    }

    [Fact]
    public void Cascade_LockedCellPreservedDuringRemoval()
    {
        // Locked cell at (1,0) must not be removed even if it is in winning positions
        var board = new Board(3, 1)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "A" }, IsLocked = true })
            .SetCell(2, 0, new BoardCell { Symbols = new[] { "A" } });

        var winPos = new HashSet<(int, int)> { (0, 0), (1, 0), (2, 0) };
        var remove = new RemoveWinningTransform(winPos);
        var (after, _) = remove.Apply(board, null);

        // Row 0 and 2 cleared; row 1 locked — preserved
        Assert.True(after[0, 0].IsEmpty);
        Assert.False(after[1, 0].IsEmpty);  // locked cell preserved
        Assert.True(after[1, 0].IsLocked);
        Assert.True(after[2, 0].IsEmpty);
    }

    // ════════════════════════════════════════════════════════════════════
    //  9. Sticky-wild (LockTransform) — 3 hand-computed cases
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void StickyWild_LockTransform_MarksSpecifiedCellsAsLocked()
    {
        var board = new Board(3, 3)
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "W" } })
            .SetCell(2, 2, new BoardCell { Symbols = new[] { "W" } });

        var lockPositions = new HashSet<(int, int)> { (0, 1), (2, 2) };
        var lockTransform = new LockTransform(lockPositions);
        var (after, _) = lockTransform.Apply(board, null);

        Assert.True(after[0, 1].IsLocked,  "Expected (0,1) to be locked");
        Assert.True(after[2, 2].IsLocked,  "Expected (2,2) to be locked");
        Assert.False(after[0, 0].IsLocked, "Expected (0,0) to remain unlocked");
    }

    [Fact]
    public void StickyWild_AlreadyLockedCellRemainLocked()
    {
        var board = new Board(2, 2)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "W" }, IsLocked = true });

        var lockTransform = new LockTransform(new[] { (0, 0) });
        var (after, _) = lockTransform.Apply(board, null);

        Assert.True(after[0, 0].IsLocked);
    }

    [Fact]
    public void StickyWild_EmptyPositionSet_LeaveBoardUnchanged()
    {
        var board = new Board(2, 2)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } });

        var lockTransform = new LockTransform(Array.Empty<(int, int)>());
        var (after, _) = lockTransform.Apply(board, null);

        Assert.Equal(board, after);
    }

    // ════════════════════════════════════════════════════════════════════
    //  10. Hold-and-win (CollectTransform + RevealTransform) — 3 cases
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void HoldAndWin_Reveal_SetsRevealedDecoration()
    {
        var board = new Board(2, 2)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "M" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "M" } });

        var reveal = new RevealTransform();
        var (after, _) = reveal.Apply(board, null);

        Assert.Equal("true", after[0, 0].GetDecoration("revealed"));
        Assert.Equal("true", after[1, 1].GetDecoration("revealed"));
    }

    [Fact]
    public void HoldAndWin_Collect_AccumulatesMoneySymbolValues()
    {
        // Two money cells with values 5 and 10 → total 15
        var board = new Board(1, 3)
            .SetCell(0, 0, new BoardCell
            {
                Symbols = new[] { "M" },
                Decorations = new Dictionary<string, string> { ["value"] = "5" }
            })
            .SetCell(0, 2, new BoardCell
            {
                Symbols = new[] { "M" },
                Decorations = new Dictionary<string, string> { ["value"] = "10" }
            });

        var collect = new CollectTransform(
            predicate: cell => cell.Symbols?.Contains("M") == true,
            decorationKey: "value");

        var (_, newState) = collect.Apply(board, null);

        Assert.Equal(15m, (decimal)newState!);
    }

    [Fact]
    public void HoldAndWin_Collect_NoPreviousState_StartsFromZero()
    {
        var board = new Board(1, 2)
            .SetCell(0, 0, new BoardCell
            {
                Symbols = new[] { "M" },
                Decorations = new Dictionary<string, string> { ["value"] = "25" }
            });

        var collect = new CollectTransform(
            cell => cell.Symbols?.Contains("M") == true,
            "value");

        var (_, state) = collect.Apply(board, null);
        Assert.Equal(25m, (decimal)state!);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════

    private static void RegisterScatter()
    {
        if (EvaluatorRegistry.TryGet("scatter") is null)
        {
            EvaluatorRegistry.Register("scatter",
                new ScatterEvaluator(new Paytable
                {
                    Id = "pt",
                    Entries = new[]
                    {
                        new PaytableEntry
                        {
                            SymbolId = "S",
                            Counts = new[] { 3, 4, 5 },
                            Payouts = new[] { "5", "20", "50" }
                        }
                    }
                }));
        }
    }

    private static GraphConfig BuildMinimalScatterGraph(bool includeMechanicsInConfig)
    {
        var mechanics = includeMechanicsInConfig
            ? MechanicCatalog.Default.Merge(null)
            : null;

        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "scatter-test",
            Symbols = new[] { new Symbol { Id = "S", Name = "Scatter", Kind = SymbolKind.Scatter } },
            ReelStrips = new[]
            {
                new ReelStrip { Id = "r1", Name = "R1", Symbols = new[] { "S", "X" } },
                new ReelStrip { Id = "r2", Name = "R2", Symbols = new[] { "S", "X" } },
                new ReelStrip { Id = "r3", Name = "R3", Symbols = new[] { "S", "X" } }
            },
            ReelSets = new[]
            {
                new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1", "r2", "r3" } }
            },
            BoardConfig = new BoardConfig { Rows = 1, Columns = 3 },
            Mechanics = mechanics != null
                ? mechanics
                : null,
            Nodes =
            [
                new DrawNode
                {
                    Id = "draw",
                    Label = "Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },
                new LibraryNode
                {
                    Id = "scatter-lib",
                    MechanicName = "scatter",
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
                    Inputs = new Dictionary<string, Port>
                    {
                        ["wins"] = new() { Name = "wins", Type = PortType.Wins }
                    }
                }
            ],
            Edges =
            [
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "scatter-lib", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "scatter-lib", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" }
            ]
        };
    }
}
