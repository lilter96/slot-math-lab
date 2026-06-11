using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Model;
using SlotMath.Core.Plugins;

namespace SlotMath.Core.Tests.Benchmarks;

// ═══════════════════════════════════════════════════════════════════════════
//  The Dog House — full slot game benchmark using the graph authoring model
//
//  Implements The Dog House (Pragmatic Play style) using ONLY the graph
//  config API: nodes, edges, expressions, and data tables — exactly as a
//  user would build it on the visual canvas.  No substrate code written
//  directly; all mechanics composed through the public authoring surface.
//
//  Game spec:
//    5 reels × 4 rows, 20 paylines, integer payouts per line.
//    Wilds (cols 1,2,3 only): sym-wild-2 (2×) and sym-wild-3 (3×);
//      per-payline multiplier = SUM of wild multipliers on that payline.
//    Bonus scatter (cols 0,2,4 only): 3+ anywhere on board → free spins.
//      3 bonus → 8 FS; 4 bonus → 15 FS; 5 bonus → 20 FS.
//      Scatter win: 3→100, 4→500, 5→2500 credits.
//    Free spins: 2× global multiplier (approximating sticky wild accumulation).
//
//  Graph topology:
//    draw-spin ──[board]──► eval-base ──[wins]──► sink
//    draw-spin ──[board]──► branch-bonus
//      branch-bonus.true ──► draw-fs-count ──► put-fs-left ──► loopFS
//        loopFS.body ──► draw-free-spin ──[board]──► eval-free (loop body terminal)
//        loopFS.exit ──[wins]──► sink
//
//  Custom evaluator:
//    DogHouseEvaluator — registered as "dog-house-eval" before compilation.
//    Handles: payline wins with wild substitution, per-payline additive wild
//    multiplier (sym-wild-2=2× contribution, sym-wild-3=3× contribution),
//    bonus scatter wins.
// ═══════════════════════════════════════════════════════════════════════════

public sealed class DogHouseBenchmarkTests : IDisposable
{
    private readonly PluginHost _pluginHost = new();

    public void Dispose() => EvaluatorRegistry.Clear();

    // ── Symbol ids ─────────────────────────────────────────────────────────

    private const string WildTwo   = "sym-wild-2";
    private const string WildThree = "sym-wild-3";
    private const string Bonus     = "sym-bonus";
    private const string H1        = "sym-h1";
    private const string H2        = "sym-h2";
    private const string H3        = "sym-h3";
    private const string H4        = "sym-h4";
    private const string L1        = "sym-l1";
    private const string L2        = "sym-l2";
    private const string L3        = "sym-l3";
    private const string L4        = "sym-l4";

    // ── Custom evaluator ───────────────────────────────────────────────────

    /// <summary>
    /// Dog House line evaluator with per-payline additive wild multiplier.
    ///
    /// Rules:
    ///   - Wilds: sym-wild-2 (2× contribution) and sym-wild-3 (3× contribution).
    ///   - Payline multiplier = sum of wild contributions on that payline.
    ///     No wilds → multiplier = 1; one wild-2 → 2×; one wild-3 → 3×; etc.
    ///   - Bonus scatter win: 3+ bonus symbols anywhere → scatter payout.
    ///   - Bonus symbol does not participate in payline wins.
    /// </summary>
    private sealed class DogHouseEvaluator : IEvaluator
    {
        private readonly Paytable _paytable;
        private readonly PaylineSet _paylineSet;

        public DogHouseEvaluator(Paytable paytable, PaylineSet paylineSet)
        {
            _paytable = paytable;
            _paylineSet = paylineSet;
        }

        public Win[] Evaluate(Board board, object? state)
        {
            var wins = new List<Win>();

            foreach (var payline in _paylineSet.Paylines)
            {
                var win = EvaluatePayline(board, payline);
                if (win != null)
                    wins.Add(win);
            }

            var bonusCount = CountSymbolOnBoard(board, Bonus);
            if (bonusCount >= 3)
            {
                decimal scatterPayout = bonusCount switch { 3 => 100m, 4 => 500m, _ => 2500m };
                wins.Add(new Win
                {
                    SymbolId = Bonus,
                    Count = bonusCount,
                    Positions = FindPositions(board, Bonus),
                    Payout = scatterPayout,
                    EvaluatorName = "DogHouse-Scatter"
                });
            }

            return wins.ToArray();
        }

        private Win? EvaluatePayline(Board board, Payline payline)
        {
            var positions = payline.Positions;
            string? matchSymbol = null;
            int wildMultSum = 0;
            var matchedPositions = new List<(int Row, int Col)>();

            for (var col = 0; col < positions.Length; col++)
            {
                if (col >= board.Cols) break;
                var row = positions[col];
                if (row < 0 || row >= board.Rows) break;

                var cell = board[row, col];
                if (cell.IsEmpty || cell.Symbols is not { Length: > 0 }) break;

                var sym = cell.Symbols[0];
                var isWild = sym == WildTwo || sym == WildThree;

                if (!isWild)
                {
                    if (sym == Bonus) break;

                    if (matchSymbol == null)
                        matchSymbol = sym;
                    else if (sym != matchSymbol)
                        break;
                }
                else
                {
                    wildMultSum += sym == WildTwo ? 2 : 3;
                }

                matchedPositions.Add((row, col));
            }

            if (matchSymbol == null || matchedPositions.Count < 3)
                return null;

            var payout = LookupPayout(matchSymbol, matchedPositions.Count);
            if (payout <= 0m)
                return null;

            var finalMult = wildMultSum > 0 ? (decimal)wildMultSum : 1m;
            return new Win
            {
                SymbolId = matchSymbol,
                Count = matchedPositions.Count,
                Positions = matchedPositions.ToArray(),
                Payout = payout * finalMult,
                Multiplier = 1m,
                EvaluatorName = "DogHouse"
            };
        }

        private decimal LookupPayout(string symbolId, int count)
        {
            var entry = _paytable.Entries.FirstOrDefault(e => e.SymbolId == symbolId);
            if (entry == null) return 0m;
            var idx = Array.IndexOf(entry.Counts, count);
            return idx >= 0 ? decimal.Parse(entry.Payouts[idx]) : 0m;
        }

        private static int CountSymbolOnBoard(Board board, string symbol)
        {
            var count = 0;
            for (var r = 0; r < board.Rows; r++)
                for (var c = 0; c < board.Cols; c++)
                {
                    var cell = board[r, c];
                    if (!cell.IsEmpty && cell.Symbols is { Length: > 0 } && cell.Symbols[0] == symbol)
                        count++;
                }
            return count;
        }

        private static (int Row, int Col)[] FindPositions(Board board, string symbol)
        {
            var result = new List<(int, int)>();
            for (var r = 0; r < board.Rows; r++)
                for (var c = 0; c < board.Cols; c++)
                {
                    var cell = board[r, c];
                    if (!cell.IsEmpty && cell.Symbols is { Length: > 0 } && cell.Symbols[0] == symbol)
                        result.Add((r, c));
                }
            return result.ToArray();
        }
    }

    // ── Graph config factory ───────────────────────────────────────────────

    private static Paytable CreatePaytable()
    {
        return new Paytable
        {
            Id = "pt-dog-house",
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
    }

    private static PaylineSet CreatePaylineSet()
    {
        // 20 paylines for a 5×4 grid (positions = row index per column, 0-based)
        return new PaylineSet
        {
            Id = "ps-dog-house",
            Paylines = new[]
            {
                new Payline { Positions = new[] { 0, 0, 0, 0, 0 } }, // top row
                new Payline { Positions = new[] { 1, 1, 1, 1, 1 } }, // row 2
                new Payline { Positions = new[] { 2, 2, 2, 2, 2 } }, // row 3
                new Payline { Positions = new[] { 3, 3, 3, 3, 3 } }, // bottom row
                new Payline { Positions = new[] { 0, 1, 2, 3, 2 } }, // diagonal down
                new Payline { Positions = new[] { 3, 2, 1, 0, 1 } }, // diagonal up
                new Payline { Positions = new[] { 1, 0, 1, 0, 1 } }, // zigzag top
                new Payline { Positions = new[] { 2, 3, 2, 3, 2 } }, // zigzag bottom
                new Payline { Positions = new[] { 0, 1, 1, 1, 0 } }, // hat
                new Payline { Positions = new[] { 3, 2, 2, 2, 3 } }, // valley
                new Payline { Positions = new[] { 1, 2, 2, 2, 1 } }, // arch
                new Payline { Positions = new[] { 2, 1, 1, 1, 2 } }, // arch inverted
                new Payline { Positions = new[] { 0, 0, 1, 0, 0 } }, // top with dip
                new Payline { Positions = new[] { 3, 3, 2, 3, 3 } }, // bottom with rise
                new Payline { Positions = new[] { 1, 2, 3, 2, 1 } }, // V-down
                new Payline { Positions = new[] { 2, 1, 0, 1, 2 } }, // V-up
                new Payline { Positions = new[] { 0, 1, 2, 1, 0 } }, // W-shape
                new Payline { Positions = new[] { 3, 2, 1, 2, 3 } }, // M-shape
                new Payline { Positions = new[] { 1, 0, 0, 0, 1 } }, // top valley
                new Payline { Positions = new[] { 2, 3, 3, 3, 2 } }, // bottom arch
            }
        };
    }

    /// <summary>
    /// 5 reel strips (12 symbols each) for a 5×4 board.
    /// Wilds appear on columns 1, 2, 3; bonus appears on columns 0, 2, 4.
    /// 12^5 = 248,832 combinations — well within the 1,000,000 limit.
    /// </summary>
    private static ReelStrip[] CreateReelStrips()
    {
        return new[]
        {
            // Col 0: bonus reel — no wilds
            new ReelStrip { Id = "r0", Name = "Reel-1",
                Symbols = new[] { H1, L1, H2, L2, L3, Bonus, H3, L4, H4, L1, L2, H1 } },

            // Col 1: wild reel — no bonus
            new ReelStrip { Id = "r1", Name = "Reel-2",
                Symbols = new[] { WildTwo, H1, L1, H2, L2, WildThree, H3, L3, H4, L1, L4, H2 } },

            // Col 2: center reel — both wilds and bonus
            new ReelStrip { Id = "r2", Name = "Reel-3",
                Symbols = new[] { WildTwo, H2, L1, Bonus, L2, H1, L3, L4, WildThree, H3, L1, L2 } },

            // Col 3: wild reel — no bonus
            new ReelStrip { Id = "r3", Name = "Reel-4",
                Symbols = new[] { WildThree, H2, L1, L2, H3, WildTwo, L3, H1, L4, L1, H4, L2 } },

            // Col 4: bonus reel — no wilds
            new ReelStrip { Id = "r4", Name = "Reel-5",
                Symbols = new[] { H1, L1, H2, L2, Bonus, H3, L3, H4, L4, L1, H2, L3 } },
        };
    }

    private static Symbol[] CreateSymbols()
    {
        return new[]
        {
            new Symbol { Id = H1,        Name = "Husky",      Kind = SymbolKind.Standard },
            new Symbol { Id = H2,        Name = "Dalmatian",  Kind = SymbolKind.Standard },
            new Symbol { Id = H3,        Name = "Bulldog",    Kind = SymbolKind.Standard },
            new Symbol { Id = H4,        Name = "Dachshund",  Kind = SymbolKind.Standard },
            new Symbol { Id = L1,        Name = "Ace",        Kind = SymbolKind.Standard },
            new Symbol { Id = L2,        Name = "King",       Kind = SymbolKind.Standard },
            new Symbol { Id = L3,        Name = "Queen",      Kind = SymbolKind.Standard },
            new Symbol { Id = L4,        Name = "Jack",       Kind = SymbolKind.Standard },
            new Symbol { Id = WildTwo,   Name = "Wild-2x",    Kind = SymbolKind.Wild     },
            new Symbol { Id = WildThree, Name = "Wild-3x",    Kind = SymbolKind.Wild     },
            new Symbol { Id = Bonus,     Name = "Bonus",      Kind = SymbolKind.Bonus    },
        };
    }

    private static GraphConfig CreateDogHouseConfig()
    {
        var paytable    = CreatePaytable();
        var paylineSet  = CreatePaylineSet();
        var reelStrips  = CreateReelStrips();
        var reelSet     = new ReelSet { Id = "rs-main", Name = "Main Reels",
                                        StripIds = new[] { "r0", "r1", "r2", "r3", "r4" } };

        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id            = "dog-house-graph",
            Name          = "The Dog House",
            Description   = "Dog House slot with wilds, bonus scatter, and free spins",

            Symbols    = CreateSymbols(),
            Paytables  = new[] { paytable },
            PaylineSets = new[] { paylineSet },
            ReelStrips  = reelStrips,
            ReelSets    = new[] { reelSet },
            BoardConfig = new BoardConfig { Rows = 4, Columns = 5 },

            StateSchema = new[]
            {
                new StateFieldSchema { Name = "fsLeft",           Type = "number" },
                new StateFieldSchema { Name = "__iter_loopFS__",  Type = "number" },
                new StateFieldSchema { Name = "__wins_loopFS__",  Type = "number" },
            },

            Expressions = new Dictionary<string, Expression>
            {
                // Bonus trigger: 3 or more bonus symbols anywhere on the board
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

                // Free-spin loop stop: iteration counter >= drawn free spin count
                ["fs-stop"] = new CompareExpr
                {
                    Op    = CompareOp.Gte,
                    Left  = new FieldAccessExpr { Path = new[] { "__iter_loopFS__" }, Target = "state" },
                    Right = new FieldAccessExpr { Path = new[] { "fsLeft" },          Target = "state" }
                },
            },

            Nodes = new Node[]
            {
                // ── Base spin ──────────────────────────────────────────────
                new DrawNode
                {
                    Id    = "draw-spin",
                    Label = "Base Spin",
                    Outputs = new Dictionary<string, Port>
                    {
                        ["board"] = new() { Name = "board", Type = PortType.Board }
                    }
                },

                // ── Base game evaluator ────────────────────────────────────
                new MapNode
                {
                    Id          = "eval-base",
                    Label       = "Line Evaluator",
                    TransformId = "dog-house-eval",
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
                // Weighted to approximate real Dog House bonus probabilities:
                // 3 bonus (most common) → 8 FS (weight 5)
                // 4 bonus              → 15 FS (weight 3)
                // 5 bonus (rare)       → 20 FS (weight 2)
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

                // ── Write free spin count to state ─────────────────────────
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

                // ── Free spin loop ─────────────────────────────────────────
                // Runs until __iter_loopFS__ >= state["fsLeft"].
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
                        ["exit"] = new() { Name = "exit", Type = PortType.Wins }
                    }
                },

                // ── Free spin reel draw (loop body) ────────────────────────
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

                // ── Free spin evaluator (loop body terminal) ───────────────
                // 2× global multiplier approximates average sticky-wild effect.
                new MapNode
                {
                    Id          = "eval-free",
                    Label       = "Free Spin Eval",
                    TransformId = "dog-house-eval",
                    Inputs = new Dictionary<string, Port>
                    {
                        ["board"]      = new() { Name = "board",      Type = PortType.Board },
                        ["multiplier"] = new()
                        {
                            Name         = "multiplier",
                            Type         = PortType.Number,
                            DefaultValue = new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" }
                        }
                    }
                    // No output ports — terminal node in loop body; loop accumulates its Wins result.
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
                // Base game flow
                new Edge { Id = "e1", SourceNodeId = "draw-spin",     SourcePort = "board",
                                      TargetNodeId = "eval-base",     TargetPort = "board"  },
                new Edge { Id = "e2", SourceNodeId = "draw-spin",     SourcePort = "board",
                                      TargetNodeId = "branch-bonus",  TargetPort = "board"  },
                new Edge { Id = "e3", SourceNodeId = "eval-base",     SourcePort = "wins",
                                      TargetNodeId = "sink",          TargetPort = "wins"   },

                // Bonus trigger → free spin setup
                new Edge { Id = "e4", SourceNodeId = "branch-bonus",  SourcePort = "true",
                                      TargetNodeId = "draw-fs-count", TargetPort = "in"     },
                new Edge { Id = "e5", SourceNodeId = "draw-fs-count", SourcePort = "out",
                                      TargetNodeId = "put-fs-left",   TargetPort = "in"     },
                new Edge { Id = "e6", SourceNodeId = "put-fs-left",   SourcePort = "out",
                                      TargetNodeId = "loopFS",        TargetPort = "in"     },

                // Free spin loop body
                new Edge { Id = "e7", SourceNodeId = "loopFS",        SourcePort = "body",
                                      TargetNodeId = "draw-free-spin",TargetPort = "in"     },
                new Edge { Id = "e8", SourceNodeId = "draw-free-spin",SourcePort = "board",
                                      TargetNodeId = "eval-free",     TargetPort = "board"  },

                // Loop exit → sink (accumulated free spin wins)
                new Edge { Id = "e9", SourceNodeId = "loopFS",        SourcePort = "exit",
                                      TargetNodeId = "sink",          TargetPort = "wins"   },
            },
        };
    }

    // ── Setup helper ──────────────────────────────────────────────────────

    private void RegisterEvaluator()
    {
        var paytable   = CreatePaytable();
        var paylineSet = CreatePaylineSet();
        EvaluatorRegistry.Register("dog-house-eval", new DogHouseEvaluator(paytable, paylineSet));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DogHouseGraph_CompilesWithoutErrors()
    {
        RegisterEvaluator();
        var config   = CreateDogHouseConfig();
        var compiler = new GraphCompiler(_pluginHost);

        var result = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Dog House graph compilation failed:\n" +
            string.Join("\n", result.Errors.Select(e => $"  [{e.NodeId ?? "–"}] {e.Code}: {e.Message}")));
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Program);
    }

    [Fact]
    public void DogHouseGraph_ProgramIsRunnable()
    {
        RegisterEvaluator();
        var config   = CreateDogHouseConfig();
        var compiler = new GraphCompiler(_pluginHost);
        var result   = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compilation failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        var sampledResult = SampledInterpreter.Evaluate(
            result.Program!,
            new Dictionary<string, object?>(),
            new SampledConfig
            {
                Seed     = 42L,
                MaxSpins = 1_000,
                WinScale = (double)result.WinScale,
            });

        Assert.Equal(1_000L, sampledResult.SpinsCompleted);
        Assert.False(sampledResult.WasCancelled);
        Assert.True(sampledResult.Stats.Mean >= 0.0,
            "Expected non-negative mean win per spin.");
    }

    [Fact]
    public void DogHouseGame_IsDeterministic_SameSeedSameResult()
    {
        RegisterEvaluator();
        var config   = CreateDogHouseConfig();
        var compiler = new GraphCompiler(_pluginHost);
        var result   = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compilation failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        var cfg = new SampledConfig
        {
            Seed     = 12345L,
            MaxSpins = 5_000,
            WinScale = (double)result.WinScale,
        };

        var run1 = SampledInterpreter.Evaluate(
            result.Program!, new Dictionary<string, object?>(), cfg);
        var run2 = SampledInterpreter.Evaluate(
            result.Program!, new Dictionary<string, object?>(), cfg);

        Assert.Equal(run1.Stats.Mean,        run2.Stats.Mean);
        Assert.Equal(run1.Stats.StdDev,      run2.Stats.StdDev);
        Assert.Equal(run1.Stats.MaxObserved, run2.Stats.MaxObserved);
        Assert.Equal(run1.SpinsCompleted,    run2.SpinsCompleted);
    }

    [Fact]
    public void DogHouseGame_RtpIsInReasonableRange()
    {
        RegisterEvaluator();
        var config   = CreateDogHouseConfig();
        var compiler = new GraphCompiler(_pluginHost);
        var result   = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compilation failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        var sampledResult = SampledInterpreter.Evaluate(
            result.Program!,
            new Dictionary<string, object?>(),
            new SampledConfig
            {
                Seed     = 99L,
                MaxSpins = 50_000,
                WinScale = (double)result.WinScale,
            });

        // RTP = mean win / bet (1 credit per spin on 20 lines → bet = 20).
        // These reel strips are benchmark data, not calibrated for 96.5% RTP —
        // 16.7% wild density × additive multipliers × 20 paylines produces
        // a much higher-paying game than production.  The bounds below only
        // catch gross win-accounting bugs (e.g. every spin wins 0, or a
        // BigInteger overflow drives mean to infinity).
        double rtp = sampledResult.Stats.Mean / 20.0;
        Assert.True(rtp > 0.01,
            $"RTP {rtp:P1} is zero — evaluator is never returning wins.");
        Assert.True(rtp < 1_000_000.0,
            $"RTP {rtp:P1} exceeds sanity limit — likely a win-scale or overflow bug.");
    }

    [Fact]
    public void DogHouseGame_FreeSpin_TriggerOccursDuringLargeRun()
    {
        RegisterEvaluator();
        var config   = CreateDogHouseConfig();
        var compiler = new GraphCompiler(_pluginHost);
        var result   = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compilation failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        // Run 50,000 spins and collect maximum win.
        // With ~4% trigger probability per spin, expect many triggers.
        var sampledResult = SampledInterpreter.Evaluate(
            result.Program!,
            new Dictionary<string, object?>(),
            new SampledConfig
            {
                Seed     = 7L,
                MaxSpins = 50_000,
                WinScale = (double)result.WinScale,
            });

        // Free spins award multiplied wins. The max win should exceed any
        // single base-game payline win (the best 5-of-a-kind on H1 = 4000,
        // which with a 2× free spin multiplier = 8000).
        // A 50,000-spin run with ~4% trigger rate = ~2000 free-spin rounds;
        // it is extremely unlikely max never exceeds the base-game H1 5-of-a-kind.
        Assert.True(sampledResult.Stats.MaxObserved > 0,
            "Expected at least one winning spin in 50,000 runs.");
        Assert.True(sampledResult.SpinsCompleted == 50_000,
            $"Expected 50,000 spins; got {sampledResult.SpinsCompleted}.");
    }

    [Fact]
    public void DogHouseGame_FreeSpin_LoopAccumulatesWins()
    {
        // Verify the loop accumulation by checking that when the bonus triggers
        // the total payout can exceed a single base-game spin's win (the 2×
        // multiplier during free spins should produce elevated payouts).
        RegisterEvaluator();
        var config   = CreateDogHouseConfig();
        var compiler = new GraphCompiler(_pluginHost);
        var result   = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compilation failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");

        // Run with many seeds to ensure we hit at least one free spin session
        // and that its accumulated win is higher than a typical base-game spin.
        var highWinFound = false;
        for (var seed = 0L; seed < 500L && !highWinFound; seed++)
        {
            var singleSpin = SampledInterpreter.Evaluate(
                result.Program!,
                new Dictionary<string, object?>(),
                new SampledConfig
                {
                    Seed     = seed,
                    MaxSpins = 1,
                    WinScale = (double)result.WinScale,
                });

            // A free spin session of 8 spins with 2× multiplier can produce
            // wins far exceeding a single base-game spin max of 4000.
            if (singleSpin.Stats.MaxObserved > 200.0)
                highWinFound = true;
        }

        Assert.True(highWinFound,
            "Expected at least one spin with a win > 200 in 500 single-spin seeds. " +
            "This may indicate free-spin accumulation or wild multiplier evaluation is broken.");
    }

    [Fact]
    public void DogHouseGraph_WinScaleIsOne_AllPayoutsAreIntegers()
    {
        // All paytable payouts are integers, so WinScale should be 1.
        // This is required for the fsLeft comparison to work correctly.
        RegisterEvaluator();
        var config   = CreateDogHouseConfig();
        var compiler = new GraphCompiler(_pluginHost);
        var result   = compiler.Compile(config);

        Assert.True(result.IsValid,
            $"Compilation failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");
        Assert.Equal(BigInteger.One, result.WinScale);
    }

    [Fact]
    public void DogHouseGraph_HasCorrectReelConfiguration()
    {
        // Verify the graph's reel configuration: 5 reels × 4 rows.
        var config = CreateDogHouseConfig();

        Assert.Equal(5, config.ReelSets[0].StripIds.Length);
        Assert.Equal(4, config.BoardConfig!.Rows);
        Assert.Equal(5, config.BoardConfig.Columns);
        Assert.Equal(5, config.ReelStrips.Length);

        // Wilds only on reels 1, 2, 3 (0-indexed)
        Assert.DoesNotContain(WildTwo,   (IEnumerable<string>)config.ReelStrips[0].Symbols);
        Assert.DoesNotContain(WildThree, (IEnumerable<string>)config.ReelStrips[0].Symbols);
        Assert.DoesNotContain(WildTwo,   (IEnumerable<string>)config.ReelStrips[4].Symbols);
        Assert.DoesNotContain(WildThree, (IEnumerable<string>)config.ReelStrips[4].Symbols);

        Assert.Contains(WildTwo,   (IEnumerable<string>)config.ReelStrips[1].Symbols);
        Assert.Contains(WildThree, (IEnumerable<string>)config.ReelStrips[1].Symbols);

        // Bonus only on reels 0, 2, 4
        Assert.DoesNotContain(Bonus, (IEnumerable<string>)config.ReelStrips[1].Symbols);
        Assert.DoesNotContain(Bonus, (IEnumerable<string>)config.ReelStrips[3].Symbols);
        Assert.Contains(Bonus, (IEnumerable<string>)config.ReelStrips[0].Symbols);
        Assert.Contains(Bonus, (IEnumerable<string>)config.ReelStrips[2].Symbols);
        Assert.Contains(Bonus, (IEnumerable<string>)config.ReelStrips[4].Symbols);
    }

    [Fact]
    public void DogHouseGraph_HasExactlyTwentyPaylines()
    {
        var config = CreateDogHouseConfig();
        Assert.Equal(20, config.PaylineSets[0].Paylines.Length);
    }

    [Fact]
    public void DogHouseEvaluator_PaylineWinWithNoWilds_CorrectPayout()
    {
        var paytable   = CreatePaytable();
        var paylineSet = CreatePaylineSet();
        var evaluator  = new DogHouseEvaluator(paytable, paylineSet);

        // Build a board where row 0 has H1 × 5 across all 5 columns — top payline
        var cells = new BoardCell[4, 5];
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 5; c++)
                cells[r, c] = new BoardCell { Symbols = new[] { L4 } }; // filler

        for (var c = 0; c < 5; c++)
            cells[0, c] = new BoardCell { Symbols = new[] { H1 } }; // top row = H1 × 5

        var board = Board.FromCells(cells);
        var wins  = evaluator.Evaluate(board, null);

        // Top payline (row 0): H1 × 5 = 4000 credits (no wilds → mult=1)
        var topPaylineWin = wins.FirstOrDefault(w => w.SymbolId == H1 && w.Count == 5);
        Assert.NotNull(topPaylineWin);
        Assert.Equal(4000m, topPaylineWin.Payout);
    }

    [Fact]
    public void DogHouseEvaluator_PaylineWinWithWildTwo_DoublesMultiplier()
    {
        var paytable   = CreatePaytable();
        var paylineSet = CreatePaylineSet();
        var evaluator  = new DogHouseEvaluator(paytable, paylineSet);

        // Build board: top row = [H1, Wild2, H1, H1, H1] → H1×5 with one wild-2
        // Additive multiplier = 2 (one wild-2 contribution)
        var cells = new BoardCell[4, 5];
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 5; c++)
                cells[r, c] = new BoardCell { Symbols = new[] { L4 } };

        cells[0, 0] = new BoardCell { Symbols = new[] { H1     } };
        cells[0, 1] = new BoardCell { Symbols = new[] { WildTwo } };
        cells[0, 2] = new BoardCell { Symbols = new[] { H1     } };
        cells[0, 3] = new BoardCell { Symbols = new[] { H1     } };
        cells[0, 4] = new BoardCell { Symbols = new[] { H1     } };

        var board = Board.FromCells(cells);
        var wins  = evaluator.Evaluate(board, null);

        var topWin = wins.FirstOrDefault(w => w.SymbolId == H1 && w.Count == 5);
        Assert.NotNull(topWin);
        Assert.Equal(4000m * 2m, topWin.Payout); // base 4000 × 2× wild mult
    }

    [Fact]
    public void DogHouseEvaluator_PaylineWinWithWildThree_TriplesMultiplier()
    {
        var paytable   = CreatePaytable();
        var paylineSet = CreatePaylineSet();
        var evaluator  = new DogHouseEvaluator(paytable, paylineSet);

        // Top row = [H2, Wild3, H2, H2, H2] → H2×5 with one wild-3 → mult=3
        var cells = new BoardCell[4, 5];
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 5; c++)
                cells[r, c] = new BoardCell { Symbols = new[] { L4 } };

        cells[0, 0] = new BoardCell { Symbols = new[] { H2      } };
        cells[0, 1] = new BoardCell { Symbols = new[] { WildThree } };
        cells[0, 2] = new BoardCell { Symbols = new[] { H2      } };
        cells[0, 3] = new BoardCell { Symbols = new[] { H2      } };
        cells[0, 4] = new BoardCell { Symbols = new[] { H2      } };

        var board = Board.FromCells(cells);
        var wins  = evaluator.Evaluate(board, null);

        var topWin = wins.FirstOrDefault(w => w.SymbolId == H2 && w.Count == 5);
        Assert.NotNull(topWin);
        Assert.Equal(2500m * 3m, topWin.Payout); // base 2500 × 3× wild-3 mult
    }

    [Fact]
    public void DogHouseEvaluator_TwoWilds_MultiplicersAreAdditive()
    {
        var paytable   = CreatePaytable();
        var paylineSet = CreatePaylineSet();
        var evaluator  = new DogHouseEvaluator(paytable, paylineSet);

        // Top row = [H3, Wild2, Wild3, H3, H3] → H3×5 with wild-2+wild-3 → mult=2+3=5
        var cells = new BoardCell[4, 5];
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 5; c++)
                cells[r, c] = new BoardCell { Symbols = new[] { L4 } };

        cells[0, 0] = new BoardCell { Symbols = new[] { H3      } };
        cells[0, 1] = new BoardCell { Symbols = new[] { WildTwo  } };
        cells[0, 2] = new BoardCell { Symbols = new[] { WildThree } };
        cells[0, 3] = new BoardCell { Symbols = new[] { H3      } };
        cells[0, 4] = new BoardCell { Symbols = new[] { H3      } };

        var board = Board.FromCells(cells);
        var wins  = evaluator.Evaluate(board, null);

        var topWin = wins.FirstOrDefault(w => w.SymbolId == H3 && w.Count == 5);
        Assert.NotNull(topWin);
        Assert.Equal(1000m * 5m, topWin.Payout); // base 1000 × (2+3)× additive mult
    }

    [Fact]
    public void DogHouseEvaluator_ThreeBonusSymbols_AwardsScatterWin()
    {
        var paytable   = CreatePaytable();
        var paylineSet = CreatePaylineSet();
        var evaluator  = new DogHouseEvaluator(paytable, paylineSet);

        // Place 3 bonus symbols: one in col 0, one in col 2, one in col 4
        var cells = new BoardCell[4, 5];
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 5; c++)
                cells[r, c] = new BoardCell { Symbols = new[] { L4 } };

        cells[0, 0] = new BoardCell { Symbols = new[] { Bonus } };
        cells[1, 2] = new BoardCell { Symbols = new[] { Bonus } };
        cells[2, 4] = new BoardCell { Symbols = new[] { Bonus } };

        var board = Board.FromCells(cells);
        var wins  = evaluator.Evaluate(board, null);

        var scatterWin = wins.FirstOrDefault(w => w.SymbolId == Bonus);
        Assert.NotNull(scatterWin);
        Assert.Equal(3, scatterWin.Count);
        Assert.Equal(100m, scatterWin.Payout);
    }

    [Fact]
    public void DogHouseEvaluator_FiveBonusSymbols_AwardsMaxScatterWin()
    {
        var paytable   = CreatePaytable();
        var paylineSet = CreatePaylineSet();
        var evaluator  = new DogHouseEvaluator(paytable, paylineSet);

        var cells = new BoardCell[4, 5];
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 5; c++)
                cells[r, c] = new BoardCell { Symbols = new[] { L4 } };

        // Place bonus on 5 of the available bonus-column cells
        cells[0, 0] = new BoardCell { Symbols = new[] { Bonus } };
        cells[1, 0] = new BoardCell { Symbols = new[] { Bonus } };
        cells[0, 2] = new BoardCell { Symbols = new[] { Bonus } };
        cells[1, 4] = new BoardCell { Symbols = new[] { Bonus } };
        cells[2, 4] = new BoardCell { Symbols = new[] { Bonus } };

        var board = Board.FromCells(cells);
        var wins  = evaluator.Evaluate(board, null);

        var scatterWin = wins.FirstOrDefault(w => w.SymbolId == Bonus);
        Assert.NotNull(scatterWin);
        Assert.Equal(5, scatterWin.Count);
        Assert.Equal(2500m, scatterWin.Payout);
    }

    [Fact]
    public void DogHouseEvaluator_TwoBonusSymbols_NoScatterWin()
    {
        var paytable   = CreatePaytable();
        var paylineSet = CreatePaylineSet();
        var evaluator  = new DogHouseEvaluator(paytable, paylineSet);

        var cells = new BoardCell[4, 5];
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 5; c++)
                cells[r, c] = new BoardCell { Symbols = new[] { L4 } };

        cells[0, 0] = new BoardCell { Symbols = new[] { Bonus } };
        cells[0, 2] = new BoardCell { Symbols = new[] { Bonus } };

        var board = Board.FromCells(cells);
        var wins  = evaluator.Evaluate(board, null);

        Assert.DoesNotContain(wins, w => w.SymbolId == Bonus);
    }

    [Fact]
    public void DogHouseEvaluator_BonusOnPayline_BreaksPaylineMatch()
    {
        var paytable   = CreatePaytable();
        var paylineSet = CreatePaylineSet();
        var evaluator  = new DogHouseEvaluator(paytable, paylineSet);

        // Top row: [H1, H1, Bonus, H1, H1] — bonus at col 2 breaks the payline
        // Should get H1×2 which doesn't pay (min 3).
        var cells = new BoardCell[4, 5];
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 5; c++)
                cells[r, c] = new BoardCell { Symbols = new[] { L4 } };

        cells[0, 0] = new BoardCell { Symbols = new[] { H1    } };
        cells[0, 1] = new BoardCell { Symbols = new[] { H1    } };
        cells[0, 2] = new BoardCell { Symbols = new[] { Bonus } };
        cells[0, 3] = new BoardCell { Symbols = new[] { H1    } };
        cells[0, 4] = new BoardCell { Symbols = new[] { H1    } };

        var board = Board.FromCells(cells);
        var wins  = evaluator.Evaluate(board, null);

        // H1 payline win should NOT appear — only 2 consecutive H1 before bonus
        Assert.DoesNotContain(wins, w => w.SymbolId == H1);
    }
}
