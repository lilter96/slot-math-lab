using System.Diagnostics;
using System.Text.Json;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;
using Xunit.Abstractions;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Math.Games;

/// <summary>
///     Le Militare, migrated from tgslots as a graph of subgraphs, held against the original game and against every
///     interpreter of the kernel. The game has three volatility modes and six kinds of round: the paid spin, a spin
///     bought with a better chance of free spins, a spin bought with a sure air raid, and free spins bought outright
///     in three tiers. Every kind of round is a graph of its own, checked here in the default mode; the paid spin is
///     checked in the other two modes as well, whose graphs differ only in the tables of the mode. The full graphs
///     are too large to enumerate, so the interpreters that visit every outcome run on cut-down models built by the
///     same graph code (two reels of two rows, one paying symbol), whose round cap is low enough to cut real wins.
/// </summary>
public class LeMilitareGraphTests(ITestOutputHelper output)
{
    private const long Seed = 20260511;

    // The rules of the released game, stated here on their own so that the played rounds are checked against the
    // rules and not against the graph: the board, the bet in credits, the free spins, and what bounds a round.
    private const int Columns = 6;
    private const int Rows = 5;
    private const int Cells = Columns * Rows;
    private const int Bet = 100;
    private const int Trigger = 4;
    private const int MaxSteps = 100;
    private const int MaxWinBets = 15000;
    private const long Cap = (long)MaxWinBets * Bet;
    private const long Wild = 0;
    private const long Plane = 9;
    private const long Launcher = 10;
    private const long Scatter = 11;

    private static readonly string[] Features = ["spin", "chance", "air-raid", "standard", "elite", "super"];

    private static readonly Lazy<JsonDocument> Tables = new(() => GameGraph.Golden("le-militare.tables.golden.json"));

    private static readonly Dictionary<string, Lazy<GameGraph>> Graphs = new[]
        {
            "spin", "chance", "air-raid", "standard", "elite", "super", "recon.spin", "siege.spin", "spin.small",
            "chance.small", "air-raid.small", "standard.small", "elite.small", "super.small"
        }
        .ToDictionary(name => name, name => new Lazy<GameGraph>(() => GameGraph.Load($"le-militare.{name}.json")));

    /// <summary>The cut-down games' RTP and hit frequency, worked out by the classic interpreter.</summary>
    private static readonly Dictionary<string, (Rational Rtp, Rational HitFrequency)> Small = new()
    {
        ["spin"] = (new Rational(338248751, 157464000), new Rational(95, 144)),
        ["chance"] = (new Rational(765067351, 511758000), new Rational(1357, 2160)),
        ["air-raid"] = (new Rational(92341271, 47239200), new Rational(23, 36)),
        ["standard"] = (new Rational(186209191, 239148450), new Rational(41, 81)),
        ["elite"] = (new Rational(17649497, 20503125), new Rational(52, 81)),
        ["super"] = (new Rational(29921, 36000), new Rational(5, 9))
    };

    public static TheoryData<string> Rounds => [.. Graphs.Keys.Where(name => !name.EndsWith(".small"))];

    public static TheoryData<string> Kinds => [.. Features];

    public static TheoryData<string, bool> KindsAndTiers => new()
    {
        { "spin", true },
        { "spin", false },
        { "chance", true },
        { "air-raid", true },
        { "standard", true },
        { "elite", true },
        { "super", true },
        { "super", false }
    };

    /// <summary>
    ///     How many rounds of each graph are sampled against the original, and the shifts they have to tell apart:
    ///     of the RTP, of the win of the opening spin, of the multipliers, of the free spins.
    /// </summary>
    public static TheoryData<string, long, double, double, double, double> Gates => new()
    {
        { "spin", 6_000_000, 0.10, 0.01, 0.02, 0.05 },
        { "chance", 3_000_000, 0.10, 0.02, 0.02, 0.05 },
        { "air-raid", 3_000_000, 0.10, 0.02, 0.01, 0.05 },
        { "standard", 120_000, 0.10, 0.05, 0.05, 0.01 },
        { "elite", 30_000, 0.10, 0.10, 0.05, 0.01 },
        { "super", 20_000, 0.10, 0.10, 0.05, 0.01 },
        { "recon.spin", 3_000_000, 0.15, 0.02, 0.02, 0.05 },
        { "siege.spin", 3_000_000, 0.15, 0.02, 0.05, 0.05 }
    };

    private static JsonElement Config => Tables.Value.RootElement.GetProperty("config");

    private static string[] Names =>
    [
        .. Tables.Value.RootElement.GetProperty("symbols").EnumerateArray().Select(name => name.GetString()!)
    ];

    private static GameGraph Game(string name) => Graphs[name].Value;

    /// <summary>The mode and the kind of round a graph is: <c>recon.spin</c> is the paid spin of the recon mode.</summary>
    private static (string Mode, string Feature) Named(string name) =>
        name.Split('.') is [var mode, var feature]
            ? (mode, feature)
            : (Tables.Value.RootElement.GetProperty("defaultMode").GetString()!, name);

    private static bool IsTier(string feature) => feature is "standard" or "elite" or "super";

    private static long[] Whole(object? values) =>
        [.. GameGraph.Items(values).Select(value => (long)GameGraph.Number(value).Numerator)];

    private static long[] Whole(JsonElement values) => [.. values.EnumerateArray().Select(value => value.GetInt64())];

    private static int[] Places(JsonElement values) => [.. values.EnumerateArray().Select(value => value.GetInt32())];

    /// <summary>An amount the original states in bets, in the credits the graph counts in.</summary>
    private static long Credits(JsonElement bets)
    {
        var credits = bets.GetDecimal() * Bet;
        Assert.Equal(decimal.Truncate(credits), credits);
        return (long)credits;
    }

    /// <summary>A board as the graph holds it: the id of the symbol on every cell, reel by reel from the top.</summary>
    private static long[] Symbols(object? board) =>
        [.. GameGraph.Items(board).Select(cell => long.Parse(GameGraph.Text(cell)))];

    /// <summary>The places marked with 1 in a list of one mark per place.</summary>
    private static int[] Marked(object? marks) =>
    [
        .. Whole(marks).Select((mark, place) => (mark, place)).Where(m => m.mark == 1).Select(m => m.place)
    ];

    /// <summary>What a played round of the graph leaves in its trace, in the shape the original reports its rounds.</summary>
    private static Played Trace(Dict state)
    {
        object?[] boards = GameGraph.Items(state["spinBoards"]), armed = GameGraph.Items(state["spinArmed"]);
        long[] scatters = Whole(state["spinScatters"]),
            groups = Whole(state["spinGroupCoins"]),
            multipliers = Whole(state["spinMultipliers"]);
        long[] wins = Whole(state["spinWins"]), totals = Whole(state["spinTotals"]);
        long[] lookSpins = Whole(state["lookSpins"]),
            stepSpins = Whole(state["stepSpins"]),
            stepWins = Whole(state["stepWins"]);
        object?[] before = GameGraph.Items(state["lookBefore"]), after = GameGraph.Items(state["lookAfter"]);
        object?[] shots = GameGraph.Items(state["lookShots"]),
            lookArmed = GameGraph.Items(state["lookArmed"]),
            lookHeld = GameGraph.Items(state["lookHeld"]);
        object?[] removed = GameGraph.Items(state["cascadeRemoved"]), stepHeld = GameGraph.Items(state["stepHeld"]);
        object?[] hitSymbols = GameGraph.Items(state["stepHitSymbols"]),
            hitSizes = GameGraph.Items(state["stepHitSizes"]);
        object?[] hitPays = GameGraph.Items(state["stepHitPays"]), hitCells = GameGraph.Items(state["stepHitCells"]);
        Assert.Equal(stepSpins.Length, GameGraph.Items(state["cascadeBoards"]).Length);

        // Every board of a spin that paid is a step, and the steps of a spin are its first boards.
        var looks = new List<Look>();
        for (int look = 0, step = 0; look < lookSpins.Length; look++)
        {
            var spin = (int)lookSpins[look];
            if (step < stepSpins.Length && stepSpins[step] == spin)
            {
                Assert.Equal(Symbols(after[look]), Symbols(GameGraph.Items(state["cascadeBoards"])[step]));
                looks.Add(new Look(spin, Symbols(before[look]), Symbols(after[look]), Whole(shots[look]),
                    Marked(lookArmed[look]), stepWins[step],
                    [
                        .. Symbols(hitSymbols[step]).Select((symbol, hit) => new Hit(symbol, Whole(hitSizes[step])[hit],
                            Whole(hitPays[step])[hit], Marked(GameGraph.Items(hitCells[step])[hit])))
                    ], Marked(removed[step]), Marked(stepHeld[step])));
                step++;
            }
            else
            {
                looks.Add(new Look(spin, Symbols(before[look]), Symbols(after[look]), Whole(shots[look]),
                    Marked(lookArmed[look]), 0, [], [],
                    Marked(lookHeld[look])));
            }
        }

        Assert.Equal(stepSpins.Length, looks.Count(look => look.Hits.Length > 0));
        Raid? raid = state.TryGetValue("raidBoards", out var raids) && GameGraph.Items(raids) is [var raidBoard]
            ? new Raid(Symbols(raidBoard), Whole(state["raidSquadrons"]).Single(),
                [.. Whole(state["raidCells"]).Select(cell => (int)cell)],
                Whole(state["raidMultipliers"]))
            : null;
        return new Played(
        [
            .. boards.Select((board, spin) => new Spin(Symbols(board), scatters[spin], groups[spin], multipliers[spin],
                wins[spin],
                totals[spin] - (spin == 0 ? 0 : totals[spin - 1]), Marked(armed[spin])))
        ], [.. looks], raid);
    }

    private static Played Original(JsonElement round) => new(
        [
            .. round.GetProperty("spins").EnumerateArray().Select(spin => new Spin(Whole(spin.GetProperty("board")),
                spin.GetProperty("scatters").GetInt64(), Credits(spin.GetProperty("groups")),
                spin.GetProperty("multiplier").GetInt64(),
                Credits(spin.GetProperty("win")), Credits(spin.GetProperty("paid")), Places(spin.GetProperty("armed"))))
        ],
        [
            .. round.GetProperty("looks").EnumerateArray().Select(look => new Look(look.GetProperty("spin").GetInt32(),
                Whole(look.GetProperty("before")), Whole(look.GetProperty("after")), Whole(look.GetProperty("shots")),
                Places(look.GetProperty("armed")), Credits(look.GetProperty("win")),
                [
                    .. look.GetProperty("hits").EnumerateArray().Select(hit =>
                        new Hit(hit.GetProperty("symbol").GetInt64(),
                            hit.GetProperty("size").GetInt64(), Credits(hit.GetProperty("pay")),
                            Places(hit.GetProperty("cells"))))
                ], Places(look.GetProperty("removed")), Places(look.GetProperty("held"))))
        ],
        round.GetProperty("raid") is { ValueKind: JsonValueKind.Object } raid
            ? new Raid(Whole(raid.GetProperty("board")), raid.GetProperty("squadron").GetInt64(),
                Places(raid.GetProperty("cells")),
                Whole(raid.GetProperty("multipliers")))
            : null);

    /// <summary>The cluster paytable the original ships, by symbol id: what a group of each size pays, in credits.</summary>
    private static Dictionary<long, Dictionary<int, long>> Paytable() => Config.GetProperty("paytable")
        .EnumerateObject().ToDictionary(
            symbol => (long)Array.IndexOf(Names, symbol.Name),
            symbol => symbol.Value.EnumerateObject()
                .ToDictionary(size => int.Parse(size.Name), size => Credits(size.Value)));

    /// <summary>The reel strips the original ships for the paid spin or for the free spins, by symbol id.</summary>
    private static long[][] Strips(bool free) =>
    [
        .. Config.GetProperty(free ? "reel_strips_free" : "reel_strips_base").EnumerateObject()
            .Select(reel => reel.Value.EnumerateArray().Select(name => (long)Array.IndexOf(Names, name.GetString()))
                .ToArray())
    ];

    /// <summary>The free spins a number of scatters awards: the award of the largest listed count that is not above it.</summary>
    private static int Award(long scatters) => Config.GetProperty("scatter_definition")
        .GetProperty("free_spins_awarded").EnumerateObject()
        .Where(award => int.Parse(award.Name) <= scatters).Select(award => award.Value.GetInt32()).DefaultIfEmpty(0)
        .Max();

    /// <summary>The cells a bought round places its scatters on: left to right along the top row, then along the next.</summary>
    private static int[] Placed(int scatters) =>
    [
        .. Enumerable.Range(0, scatters).Select(index => index % Columns * Rows + index / Columns)
    ];

    /// <summary>
    ///     The groups that pay on a board, found the way the rules are told to a player and not the way the graph
    ///     finds them: symbols in the order they first appear, each group flooded out from its first cell through
    ///     cells of the symbol and wilds, and a wild used by a group that paid closed to the symbols after it. The
    ///     size of a group is the sum of what its cells count: a cell of a giant wild below its top counts nothing.
    /// </summary>
    private static List<Hit> PayingGroups(long[] board, Dictionary<long, Dictionary<int, long>> pays,
        Func<int, int> counts)
    {
        var usedWilds = new HashSet<int>();
        var symbols = new List<long>();
        foreach (var symbol in board)
        {
            if (pays.ContainsKey(symbol) && !symbols.Contains(symbol))
            {
                symbols.Add(symbol);
            }
        }

        var groups = new List<Hit>();
        foreach (var symbol in symbols)
        {
            var seen = new bool[board.Length];
            for (var first = 0; first < board.Length; first++)
            {
                if (board[first] != symbol || seen[first])
                {
                    continue;
                }

                var group = new List<int>();
                var open = new Stack<int>([first]);
                seen[first] = true;
                while (open.Count > 0)
                {
                    var cell = open.Pop();
                    group.Add(cell);
                    int column = cell / Rows, row = cell % Rows;
                    foreach (var (nextColumn, nextRow) in new[]
                             {
                                 (column, row - 1), (column, row + 1), (column - 1, row), (column + 1, row)
                             })
                    {
                        var next = nextColumn * Rows + nextRow;
                        if (nextColumn < 0 || nextColumn >= Columns || nextRow < 0 || nextRow >= Rows || seen[next] ||
                            !(board[next] == symbol || (board[next] == Wild && !usedWilds.Contains(next))))
                        {
                            continue;
                        }

                        seen[next] = true;
                        open.Push(next);
                    }
                }

                var size = group.Sum(counts);
                if (!pays[symbol].TryGetValue(size, out var pay) || pay <= 0)
                {
                    continue;
                }

                usedWilds.UnionWith(group.Where(cell => board[cell] == Wild));
                groups.Add(new Hit(symbol, size, pay, [.. group.Order()]));
            }
        }

        return groups;
    }

    /// <summary>Whether the symbols are ones that follow one another somewhere on the strip, which runs on into its own start.</summary>
    private static bool FollowOnStrip(long[] strip, IReadOnlyList<long> symbols) => Enumerable.Range(0, strip.Length)
        .Any(stop => symbols.Select((symbol, offset) => strip[(stop + offset) % strip.Length] == symbol)
            .All(same => same));

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    private static Dictionary<string, long> Draw(GraphConfig graph, string mechanic, string node) =>
        graph.Mechanics![mechanic].Nodes.OfType<DrawNode>().Single(draw => draw.Id == node).DrawWeights!
            .ToDictionary(outcome => outcome.OutcomeId, outcome => outcome.Weight);

    private static Dictionary<string, long> Weights(IEnumerable<long> outcomes, IEnumerable<long> weights) =>
        outcomes.Zip(weights).ToDictionary(pair => pair.First.ToString(), pair => pair.Second);

    // The graph's own tables are the original's, entry by entry: every strip, the weights of every draw and the
    // whole paytable. Sampling can only tell tables apart as far as its precision goes; this comparison is exact.
    [Theory]
    [MemberData(nameof(Rounds))]
    public void TablesOfTheGraph_AreTheOriginalGamesOwn(string name)
    {
        var (mode, feature) = Named(name);
        GraphConfig graph = Game(name).Config;
        JsonElement tables = Config.GetProperty("modes").GetProperty(mode), raid = tables.GetProperty("air_raid");
        JsonElement buy = Config.GetProperty("buy_options");

        long[] Numbers(string key)
        {
            return [.. graph.InitialState![key].EnumerateArray().Select(value => value.GetInt64())];
        }

        foreach (var (kind, free) in new[] { ("paid", false), ("free", true) })
        {
            var strips = Strips(free);
            Assert.Equal(Columns, strips.Length);
            for (var reel = 0; reel < Columns; reel++)
            {
                // Every stop of the strip is as likely as any other, and the strip is stored running on into its
                // own start for the rows of a window.
                Assert.Equal(Enumerable.Range(0, strips[reel].Length).ToDictionary(stop => stop.ToString(), _ => 1L),
                    Draw(graph, $"{kind}-reels-stop-{reel + 1}", "stop"));
                Assert.Equal(
                    Enumerable.Range(0, strips[reel].Length + Rows - 1)
                        .Select(place => strips[reel][place % strips[reel].Length].ToString()),
                    graph.InitialState![$"{kind}Strip{reel}"].EnumerateArray().Select(symbol => symbol.GetString()!));
            }
        }

        JsonElement pool = tables.GetProperty("multiplier_pool");
        Assert.Equal(Weights(Whole(pool.GetProperty("values")), Whole(pool.GetProperty("weights"))),
            Draw(graph, "add-multiplier", "multiplier"));

        var spin = feature switch
        {
            "spin" => "paid-spin",
            "chance" => "chance-spin",
            "air-raid" => "air-raid-spin",
            _ => "bought-spin"
        };
        if (IsTier(feature))
        {
            // Free spins bought outright open with a spin that no air raid comes to.
            Assert.DoesNotContain(graph.Mechanics!.Keys, mechanic => mechanic is "air-raid" or "raid-plane");
        }
        else
        {
            Assert.Equal(
                Weights(Whole(raid.GetProperty("squadron_sizes")), Whole(raid.GetProperty("squadron_weights"))),
                Draw(graph, "air-raid", "squadron"));
            Assert.Equal(Weights([1, 0], Whole(raid.GetProperty("hit_weights"))), Draw(graph, "raid-plane", "hit"));
            Assert.Equal(Enumerable.Range(0, Columns).ToDictionary(reel => reel.ToString(), _ => 1L),
                Draw(graph, "raid-plane", "reel"));
            Assert.Equal(Enumerable.Range(0, Rows).ToDictionary(row => row.ToString(), _ => 1L),
                Draw(graph, "raid-plane", "row"));
            // The raid of a bought air raid spin is sure to come: nothing is drawn for it.
            Assert.Equal(feature != "air-raid",
                graph.Mechanics![spin].Nodes.OfType<DrawNode>().Any(draw => draw.Id == "fire"));
            if (feature != "air-raid")
            {
                Assert.Equal(Weights([1, 0], Whole(raid.GetProperty("trigger_weights"))), Draw(graph, spin, "fire"));
            }
        }

        if (feature == "chance")
        {
            Assert.Equal(Weights([1, 0], Whole(buy.GetProperty("chance_spin").GetProperty("force_weights"))),
                Draw(graph, spin, "force"));
        }

        var placed = feature == "chance" ? Trigger :
            IsTier(feature) ? buy.GetProperty(feature).GetProperty("min_scatters").GetInt32() : 0;
        Assert.Equal(placed > 0, graph.InitialState!.ContainsKey("placedScatters"));
        if (placed > 0)
        {
            Assert.Equal(Placed(placed).Order(),
                Numbers("placedScatters").Select((mark, cell) => (mark, cell)).Where(m => m.mark == 1)
                    .Select(m => m.cell));
        }

        var startArmed = IsTier(feature) ? buy.GetProperty(feature).GetProperty("start_armed_reels").GetInt32() : 0;
        Assert.Equal(Enumerable.Range(0, Columns).Select(reel => reel % 2 == 0 && reel / 2 < startArmed ? 1L : 0),
            Numbers("startArmed"));
        Assert.Equal(IsTier(feature) ? buy.GetProperty(feature).GetProperty("start_multiplier").GetInt64() : 0,
            graph.InitialState["startMultiplier"].GetInt64());

        Dictionary<long, Dictionary<int, long>> pays = Paytable();
        var table = Numbers("clusterPays");
        Assert.Equal(Names.Length * (Cells + 1), table.Length);
        for (var symbol = 0; symbol < Names.Length; symbol++)
        {
            for (var size = 0; size <= Cells; size++)
            {
                var pay = pays.TryGetValue(symbol, out Dictionary<int, long>? row) ? row.GetValueOrDefault(size) : 0;
                Assert.True(pay == table[symbol * (Cells + 1) + size], $"symbol {symbol}, a group of {size}");
            }
        }

        var awards = Numbers("freeSpinAwards");
        Assert.Equal(Enumerable.Range(0, awards.Length).Select(scatters => (long)Award(scatters)), awards);
        Assert.Equal(Award(Cells), awards[^1]);
        Assert.Equal(Trigger, Config.GetProperty("scatter_definition").GetProperty("min_count").GetInt32());
        Assert.Equal(Columns, Config.GetProperty("game_metadata").GetProperty("grid_reels").GetInt32());
        Assert.Equal(Rows, Config.GetProperty("game_metadata").GetProperty("grid_rows").GetInt32());
        Assert.Equal(MaxWinBets, Tables.Value.RootElement.GetProperty("maxWin").GetInt32());
        Assert.Equal(Cap, graph.InitialState["capCoins"].GetInt64());
        Assert.Equal(Bet, graph.InitialState["totalBet"].GetInt64());

        // What the round costs is the original's price, and the sink is told the round limit in that unit.
        var price = Credits(Tables.Value.RootElement.GetProperty("prices").GetProperty(mode).GetProperty(feature));
        Assert.Equal(price, graph.InitialState["roundCost"].GetInt64());
        Assert.Equal((Cap + price - 1) / price, (long)Game(name).WinCap);
    }

    // The three modes price every bought round differently. The graphs of the default mode are held against the
    // original above; here every one of the eighteen graphs states the original's price.
    [Fact]
    public void PriceOfEveryRound_InEveryMode_IsTheOriginals()
    {
        using JsonDocument prices = GameGraph.Golden("le-militare.prices.json");
        JsonElement original = Tables.Value.RootElement.GetProperty("prices");
        var compared = 0;
        foreach (JsonProperty mode in original.EnumerateObject())
        {
            foreach (JsonProperty feature in mode.Value.EnumerateObject())
            {
                Assert.Equal(Credits(feature.Value),
                    prices.RootElement.GetProperty(mode.Name).GetProperty(feature.Name).GetInt64());
                compared++;
            }
        }

        Assert.Equal(18, compared);
        Assert.Equal(3, prices.RootElement.EnumerateObject().Count());
    }

    // The rounds were played by the original TypeScript state machine. Each lists the outcome of every random
    // choice it made, in the order the graph draws them, and what the original showed: every spin as it landed,
    // the air raid, and every board of its cascades before and after the combat. Half of them were chosen for
    // what happens in them: free spins, an air raid that hits, a reel that arms with planes on the board.
    [Theory]
    [MemberData(nameof(Rounds))]
    public void OriginalRounds_ReplayedDrawForDraw_FightTheSameAndPayTheSame(string name)
    {
        GameGraph game = Game(name);
        using JsonDocument golden = GameGraph.Golden($"le-militare.{name}.golden.json");
        var roundCost = game.Config.InitialState!["roundCost"].GetInt64();
        int rounds = 0,
            bonuses = 0,
            raids = 0,
            shots = 0,
            armed = 0,
            steps = 0,
            wildGroups = 0,
            giantGroups = 0,
            heldSteps = 0;
        foreach (JsonElement round in golden.RootElement.GetProperty("rounds").EnumerateArray())
        {
            (Rational win, Dict state) =
                game.Replay(round.GetProperty("draws").EnumerateArray().Select(d => d.GetInt32()));
            Played original = Original(round), played = Trace(state);
            Assert.Equal(original.Spins.Length, played.Spins.Length);
            for (var spin = 0; spin < original.Spins.Length; spin++)
            {
                Assert.Equal(Json(original.Spins[spin]), Json(played.Spins[spin]));
            }

            Assert.Equal(original.Looks.Length, played.Looks.Length);
            for (var look = 0; look < original.Looks.Length; look++)
            {
                Assert.Equal(Json(original.Looks[look]), Json(played.Looks[look]));
            }

            Assert.Equal(Json(original.Raid), Json(played.Raid));
            Assert.Equal(new Rational(Credits(round.GetProperty("win")), roundCost), win);
            Assert.DoesNotContain("modelLimit", GameGraph.LoopExits(state).Values);
            rounds++;
            bonuses += played.Spins.Length > 1 ? 1 : 0;
            raids += played.Raid is { Cells.Length: > 0 } ? 1 : 0;
            shots += played.Looks.Sum(look => look.Shots.Length);
            armed += played.Looks.Sum(look => look.Armed.Length);
            steps += played.Looks.Count(look => look.Hits.Length > 0);
            heldSteps += played.Looks.Count(look => look.Hits.Length > 0 && look.Held.Length > 0);
            wildGroups += played.Looks.Sum(look =>
                look.Hits.Count(hit => hit.Cells.Any(cell => look.After[cell] == Wild)));
            giantGroups += played.Looks.Sum(look => look.Hits.Count(hit => hit.Size < hit.Cells.Length));
        }

        output.WriteLine(
            $"{rounds} rounds, {bonuses} with free spins, {raids} with an air raid that hit, {shots} planes shot down, {armed} reels armed, " +
            $"{steps} steps that paid ({heldSteps} with held cells), {wildGroups} groups through a wild, {giantGroups} through a giant wild");
        Assert.True(rounds >= 6 && bonuses >= 3 && shots >= 5 && armed >= 5 && steps >= 40 && wildGroups >= 10 &&
                    giantGroups >= 3 && heldSteps >= 3);
        Assert.True(IsTier(Named(name).Feature) || raids >= 3);
    }

    [Theory]
    [MemberData(nameof(KindsAndTiers))]
    public void PlayedRound_TraceFollowsTheRulesAndAccountsForTheWin_AndTracingDoesNotChangeIt(string feature,
        bool compiledTier)
    {
        GameGraph game = Game(feature);
        Dictionary<long, Dictionary<int, long>> pays = Paytable();
        JsonElement tables = Config.GetProperty("modes").GetProperty(Named(feature).Mode),
            buy = Config.GetProperty("buy_options");
        HashSet<long> multipliers = [.. Whole(tables.GetProperty("multiplier_pool").GetProperty("values"))];
        HashSet<long> squadrons = [.. Whole(tables.GetProperty("air_raid").GetProperty("squadron_sizes"))];
        var tier = IsTier(feature);
        var roundCost = Credits(Tables.Value.RootElement.GetProperty("prices").GetProperty(Named(feature).Mode)
            .GetProperty(feature));
        var boughtScatters = tier ? buy.GetProperty(feature).GetProperty("min_scatters").GetInt32() : 0;
        var startMultiplier = tier ? buy.GetProperty(feature).GetProperty("start_multiplier").GetInt64() : 0;
        int bonuses = 0, raids = 0, shots = 0, armings = 0, steps = 0, wildGroups = 0, giantGroups = 0, retriggers = 0;
        for (var round = 0; round < (tier ? 30 : 3_000); round++)
        {
            (Rational win, Dict state) = game.Play(Seed, round, true, compiledTier);
            Assert.Equal(win, game.Play(Seed, round, false, compiledTier).Win);
            // Both tiers draw from the same stream in the same order, so they play the same round.
            Assert.Equal(win, game.Play(Seed, round, true, !compiledTier).Win);
            // No loop is cut short by its limit: each one ends by the rules of the game.
            Assert.DoesNotContain("modelLimit", GameGraph.LoopExits(state).Values);

            Played played = Trace(state);
            long total = 0, multiplier = 0;
            var armed = new HashSet<int>();
            var spinsLeft = 0;
            for (var index = 0; index < played.Spins.Length; index++)
            {
                Spin spin = played.Spins[index];
                var free = index > 0;
                var strips = Strips(free);
                var held = new bool[Cells];
                var board = spin.Board;
                if (!free)
                {
                    // The opening spin: the reels stop, an air raid may come, and a bought round places its scatters.
                    var landed = played.Raid?.Board ?? board;
                    var placed = feature == "chance" && GameGraph.Text(state["forced"]) == "1"
                        ? Placed(Trigger)
                        : Placed(boughtScatters);
                    for (var reel = 0; reel < Columns; reel++)
                    {
                        // Before the raid every reel shows symbols that follow one another on its strip; without
                        // one, the same holds of the cells below the scatters placed along the top.
                        var covered = played.Raid is null ? placed.Count(cell => cell / Rows == reel) : 0;
                        Assert.True(
                            FollowOnStrip(strips[reel], [.. landed.Skip(reel * Rows + covered).Take(Rows - covered)]),
                            $"round {round}, reel {reel}");
                    }

                    if (played.Raid is { } raid)
                    {
                        // Every plane shot down falls on a cell, turns it wild and leaves a multiplier.
                        Assert.Contains(raid.Squadron, squadrons);
                        Assert.True(raid.Cells.Length <= raid.Squadron && raid.Cells.Length == raid.Multipliers.Length);
                        Assert.All(raid.Multipliers, times => Assert.Contains(times, multipliers));
                        Assert.All(raid.Cells, cell => Assert.InRange(cell, 0, Cells - 1));
                        Assert.Equal(
                            raid.Board.Select((symbol, cell) =>
                                placed.Contains(cell) ? Scatter : raid.Cells.Contains(cell) ? Wild : symbol), board);
                        multiplier = raid.Multipliers.Sum();
                        raids += raid.Cells.Length > 0 ? 1 : 0;
                    }

                    Assert.True(feature != "air-raid" || played.Raid is not null);
                    Assert.True(!tier || played.Raid is null);
                    Assert.All(placed, cell => Assert.Equal(Scatter, board[cell]));
                }
                else
                {
                    if (index == 1)
                    {
                        // The free spins keep nothing of the opening spin: they start with what was bought.
                        multiplier = startMultiplier;
                        armed.Clear();
                    }

                    // An armed reel is a giant wild that stays through the free spins; the other reels show
                    // symbols that follow one another on their strips.
                    for (var reel = 0; reel < Columns; reel++)
                    {
                        long[] shown = [.. board.Skip(reel * Rows).Take(Rows)];
                        Assert.True(armed.Contains(reel)
                            ? shown.All(symbol => symbol == Wild)
                            : FollowOnStrip(strips[reel], shown));
                        Array.Fill(held, armed.Contains(reel), reel * Rows, Rows);
                    }
                }

                var scatters = board.Count(symbol => symbol == Scatter);
                Look[] looks = [.. played.Looks.Where(look => look.Spin == index)];
                long coins = 0;
                var paidSteps = 0;
                for (var number = 0; number < looks.Length; number++)
                {
                    Look look = looks[number];
                    Assert.Equal(board, look.Before);

                    // The combat: a reel that shows a launcher arms, unless it is armed already, and turns wild.
                    // While any reel is armed every plane is shot down, becomes a wild and leaves a multiplier.
                    int[] arming =
                    [
                        .. Enumerable.Range(0, Columns).Where(reel =>
                            !armed.Contains(reel) && board.Skip(reel * Rows).Take(Rows).Contains(Launcher))
                    ];
                    Assert.Equal(arming, look.Armed);
                    long[] fought = [.. board];
                    if (arming.Length > 0 || armed.Count > 0)
                    {
                        int[] planes = [.. Enumerable.Range(0, Cells).Where(cell => board[cell] == Plane)];
                        Assert.Equal(planes.Length, look.Shots.Length);
                        Assert.All(look.Shots, times => Assert.Contains(times, multipliers));
                        multiplier += look.Shots.Sum();
                        foreach (var cell in planes)
                        {
                            fought[cell] = Wild;
                            // A plane shot down in a paid spin is held for the rest of the spin.
                            held[cell] = !free;
                        }

                        foreach (var reel in arming)
                        {
                            Array.Fill(fought, Wild, reel * Rows, Rows);
                            // A reel that arms in the free spins is held; in a paid spin its wilds pay and leave.
                            Array.Fill(held, free, reel * Rows, Rows);
                            armed.Add(reel);
                        }

                        shots += planes.Length;
                        armings += arming.Length;
                    }
                    else
                    {
                        Assert.Empty(look.Shots);
                    }

                    Assert.Equal(fought, look.After);
                    board = fought;

                    // In the free spins an armed reel is one giant wild: it counts once towards the size of a group.
                    List<Hit> groups = PayingGroups(board, pays,
                        cell => free && armed.Contains(cell / Rows) && cell % Rows != 0 ? 0 : 1);
                    if (groups.Count == 0)
                    {
                        // A cascade ends on a board that pays nothing.
                        Assert.Equal(looks.Length - 1, number);
                        Assert.Empty(look.Hits);
                        Assert.Equal(Enumerable.Range(0, Cells).Where(cell => held[cell]), look.Held);
                        break;
                    }

                    Assert.Equal(Json(groups), Json(look.Hits));
                    Assert.Equal(groups.Sum(group => group.Pay), look.Win);
                    coins += look.Win;
                    paidSteps++;
                    wildGroups += groups.Count(group => group.Cells.Any(cell => board[cell] == Wild));
                    giantGroups += groups.Count(group => group.Size < group.Cells.Length);

                    // Every cell of a symbol that paid leaves the board, with the wilds its groups used; a held cell stays.
                    HashSet<long> paidSymbols = groups.Select(group => group.Symbol).ToHashSet();
                    int[] removed =
                    [
                        .. groups.SelectMany(group => group.Cells).Concat(Enumerable.Range(0, Cells)
                                .Where(cell => paidSymbols.Contains(board[cell])))
                            .Distinct().Where(cell => !held[cell]).Order()
                    ];
                    Assert.Equal(removed, look.Removed);
                    Assert.NotEmpty(removed);
                    // A cascade has at most a hundred steps; the board after the last of them is not looked at.
                    if (number == looks.Length - 1)
                    {
                        Assert.Equal(MaxSteps, paidSteps);
                        scatters = (int)spin.Scatters;
                        break;
                    }

                    // What stays falls to the bottom of its reel in the order it stood, held or not as it was. The
                    // reel fills the cells above with symbols that follow one another on its strip, the first lowest.
                    var next = looks[number + 1].Before;
                    var fallen = new bool[Cells];
                    for (var reel = 0; reel < Columns; reel++)
                    {
                        int[] kept = [.. Enumerable.Range(reel * Rows, Rows).Where(cell => !removed.Contains(cell))];
                        var empty = Rows - kept.Length;
                        Assert.Equal(kept.Select(cell => board[cell]),
                            next.Skip(reel * Rows + empty).Take(kept.Length));
                        long[] dropped = [.. next.Skip(reel * Rows).Take(empty).Reverse()];
                        Assert.True(FollowOnStrip(strips[reel], dropped), $"round {round}, spin {index}, reel {reel}");
                        scatters += dropped.Count(symbol => symbol == Scatter);
                        for (var place = 0; place < kept.Length; place++)
                        {
                            fallen[reel * Rows + empty + place] = held[kept[place]];
                        }
                    }

                    held = fallen;
                    Assert.Equal(Enumerable.Range(0, Cells).Where(cell => held[cell]), look.Held);
                    board = next;
                }

                steps += paidSteps;
                Assert.Equal(scatters, spin.Scatters);
                Assert.Equal(coins, spin.Groups);
                Assert.Equal(multiplier, spin.Multiplier);
                Assert.Equal(armed.Order(), spin.Armed);
                // A spin pays its groups times the sum of its multipliers, and a round never more than its limit.
                Assert.Equal(coins * System.Math.Max(1, multiplier), spin.Win);
                var paid = System.Math.Min(spin.Win, Cap - total);
                Assert.Equal(paid, spin.Paid);
                total += paid;

                // Free spins bought outright are as many as their tier awards. Otherwise four scatters or more
                // start them, and in the free spins award more. A round that has reached its limit has none left.
                if (!free)
                {
                    spinsLeft = total >= Cap ? 0 : tier ? Award(boughtScatters) : Award(scatters);
                }
                else
                {
                    spinsLeft = total >= Cap ? 0 : spinsLeft - 1 + Award(scatters);
                    retriggers += total < Cap && scatters >= Trigger ? 1 : 0;
                }

                Assert.Equal(spinsLeft > 0, index < played.Spins.Length - 1);
            }

            Assert.Equal(new Rational(total, roundCost), win);
            bonuses += played.Spins.Length > 1 ? 1 : 0;
        }

        output.WriteLine(
            $"{bonuses} rounds with free spins ({retriggers} spins awarded more), {raids} air raids that hit, {shots} planes shot down, " +
            $"{armings} reels armed, {steps} steps that paid, {wildGroups} groups through a wild, {giantGroups} through a giant wild");
        Assert.True(
            bonuses >= 5 && shots >= 10 && armings >= 10 && steps >= 200 && wildGroups >= 20 && giantGroups >= 5,
            $"{bonuses} bonuses, {shots} shots, {armings} armed, {steps} steps, {wildGroups} wild groups, {giantGroups} giant groups");
        Assert.True(tier || raids >= 20, $"{raids} raids");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void SmallGame_EveryExactInterpreterGivesTheSameRationals(string feature)
    {
        GameGraph game = Game($"{feature}.small");
        (Rational expected, Rational hitFrequency) = Small[feature];
        // Within its branch budget the hybrid evaluator is the classic interpreter: every outcome is visited.
        var sw = Stopwatch.StartNew();
        RegimeResult hybrid = game.Hybrid(2_000_000, 10_000, Seed);
        Assert.Equal(EvaluationStrategy.Exact, hybrid.OverallStrategy);
        SlotMathReport classic = hybrid.Report;
        Assert.Equal(Provenance.Exact, classic.Rtp.Provenance.Provenance);
        Rational rtp = GameGraph.Exact(classic.Rtp);
        output.WriteLine($"classic: RTP {rtp} = {rtp.ToDouble()}, hit frequency {classic.HitFrequency.DisplayValue}");
        Assert.Equal(expected, rtp);
        Assert.Equal(hitFrequency.ToDouble(), classic.HitFrequency.DisplayValue, 12);

        output.WriteLine($"TIMING classic {sw.Elapsed.TotalSeconds:F1}");
        sw.Restart();
        StructuredExactResult structured = game.Structured();
        output.WriteLine($"TIMING structured {sw.Elapsed.TotalSeconds:F1}");
        sw.Restart();
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        Assert.True(structured.PrunedMass.IsZero);
        Assert.Equal(rtp, GameGraph.Exact(structured.Rtp(game.WinScale)));
        Assert.Equal(hitFrequency, structured.HitFrequency);
        // The cap of this cut-down game is a rule that cuts real wins: the largest win is exactly the cap of eight
        // bets, which for a bought round is not a whole number of its price.
        var roundCost = game.Config.InitialState!["roundCost"].GetInt64();
        var largest = new Rational(structured.Distribution!.Entries.Max(entry => entry.Value), game.WinScale);
        output.WriteLine(
            $"structured: hit frequency {structured.HitFrequency}, largest win {largest} of a limit of {new Rational(8 * Bet, roundCost)}");
        Assert.True(largest <= new Rational(8 * Bet, roundCost));
        Assert.True(feature == "super" || largest == new Rational(8 * Bet, roundCost));

        // Pruning drops, at every draw, the branches rarer than epsilon and has to say how much it dropped. Rarer
        // than 3 in 20 is the large multiplier (1 in 10); with the declared win cap the result is an enclosure of
        // the exact value (D5).
        SlotMathReport pruned = game.Enumerate(new Rational(3, 20));
        ProvenanceTag enclosure = pruned.Rtp.Provenance;
        Assert.Equal(Provenance.ExactInterval, enclosure.Provenance);
        Assert.Equal(BoundSource.DeclaredWinCap, enclosure.BoundSource);
        output.WriteLine(
            $"pruned mass {enclosure.PrunedMass}, enclosure [{enclosure.Lo}, {enclosure.Hi}] of {rtp.ToDouble()}");
        Assert.InRange(enclosure.PrunedMass!.Value, 0.001, 0.5);
        Assert.True(enclosure.Lo < rtp.ToDouble() && rtp.ToDouble() < enclosure.Hi);
        output.WriteLine($"TIMING rest {sw.Elapsed.TotalSeconds:F1}");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void SmallGame_SamplingAgreesWithTheExactValue_AndRejectsItShiftedByOnePercent(string feature)
    {
        const long Sampled = 2_000_000;
        (Rational rtp, Rational hitFrequency) = Small[feature];
        var exact = rtp.ToDouble();
        SampledResult<Dict> sampled = Game($"{feature}.small").Sample(Sampled, Seed);
        Assert.Equal(Sampled, sampled.SpinsCompleted);
        output.WriteLine(
            $"exact {exact} sampled {sampled.Stats.Mean} ± {sampled.Stats.StdErr}, cap hits {sampled.Stats.CapHits}");
        Assert.True(FourSigma.Accepts(exact, sampled.Stats.Mean, sampled.Stats.StdErr));
        Assert.False(FourSigma.Accepts(exact * 1.01, sampled.Stats.Mean, sampled.Stats.StdErr));
        Assert.False(FourSigma.Accepts(exact * 0.99, sampled.Stats.Mean, sampled.Stats.StdErr));
        var hits = hitFrequency.ToDouble();
        Assert.True(FourSigma.Accepts(hits, sampled.Stats.HitFrequency, sampled.Stats.HitFrequencyStdErr));
        Assert.False(FourSigma.Accepts(hits * 0.99, sampled.Stats.HitFrequency, sampled.Stats.HitFrequencyStdErr));
    }

    // The round cap of 15000 bets is a rule of the game that a win can reach, so the exact RTP needs the whole law
    // of the round's win and not only its expectation by linearity, and that law is beyond the structured evaluator
    // for six reels that cascade. It has to say so within its budget, and never return a number.
    [Theory]
    [MemberData(nameof(Rounds))]
    public void FullGame_StructuredExact_DeclinesWithinItsBudget(string name)
    {
        StructuredExactResult result =
            Game(name).Structured(new StructuredExactOptions
            {
                MaxTime = TimeSpan.FromSeconds(3)
            });
        output.WriteLine(
            $"declined: {result.DeclinedReason} after {result.Elapsed.TotalSeconds:F1} s, work {result.Work}");
        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.DeclinedReason));
        Assert.InRange(result.Elapsed.TotalSeconds, 0, 15);
    }

    [Theory]
    [MemberData(nameof(Rounds))]
    public void FullGame_SamplingIsBitIdentical_AcrossWorkersAndTiers(string name)
    {
        // A bought tier is dozens of free spins a round, and the reference tier interprets every one of them.
        var tier = IsTier(Named(name).Feature);
        Game(name).AssertSamplingIsBitIdentical(tier ? 3_000 : 100_000, Seed, tier ? 200 : 1_000);
    }

    // A round can pay 15000 bets, and a paid round's win has a standard deviation of some 30 bets, so no affordable
    // number of rounds tells a 1% shift of the RTP apart: the shift the RTP is held to is the one stated for each
    // kind of round. The 1% controls are on what the RTP is made of and can be measured that finely: how often a
    // round pays, the scatters of the opening spin, what the opening spin pays, how many free spins are played.
    // The original's statistics are its own simulation of 300 thousand to 40 million rounds of each kind.
    [Theory]
    [MemberData(nameof(Gates))]
    public void FullGame_SamplingAgreesWithTheOriginalGame_AndRejectsShiftedValues(string name, long rounds,
        double rtpShift, double openingShift,
        double multiplierShift, double freeSpinShift)
    {
        GameGraph game = Game(name);
        var tier = IsTier(Named(name).Feature);
        using JsonDocument golden = GameGraph.Golden($"le-militare.{name}.golden.json");
        JsonElement original = golden.RootElement.GetProperty("simulation");
        var originalRounds = original.GetProperty("rounds").GetInt64();
        Assert.True(originalRounds >= 300_000, $"the original was simulated for {originalRounds} rounds only");

        double Original(string value)
        {
            return original.GetProperty(value).GetDouble();
        }

        double Share(double frequency, long of)
        {
            return System.Math.Sqrt(frequency * (1 - frequency) / of);
        }

        // What the opening spin has left is read where the round decides whether the free spins start.
        var decided = tier ? "bonus" : "trigger";

        Expression Whole(long value)
        {
            return new ConstantExpr
            {
                Kind = ConstantKind.Integer,
                Value = value.ToString()
            };
        }

        (SampledResult<Dict> sampled, Dictionary<string, (double Mean, double StdErr)> observed) = game.Measure(rounds,
            Seed, 8,
            GameGraph.Quantity("openingWin",
                new BinaryExpr
                {
                    Op = BinaryOp.Div,
                    Left = GameGraph.State("roundCoins"),
                    Right = Whole(Bet)
                },
                decided),
            GameGraph.Quantity("openingScatters", GameGraph.State("scatterCount"), decided),
            GameGraph.Quantity("openingMultiplier", GameGraph.State("multiplierSum"), decided),
            GameGraph.Quantity("freeSpins", GameGraph.State("freeSpinsPlayed")),
            GameGraph.Quantity("finalMultiplier", GameGraph.State("multiplierSum")),
            GameGraph.Quantity("bonus",
                new IfExpr
                {
                    Condition = new CompareExpr
                    {
                        Op = CompareOp.Gt,
                        Left = GameGraph.State("freeSpinsPlayed"),
                        Right = Whole(0)
                    },
                    ThenExpr = Whole(1),
                    ElseExpr = Whole(0)
                }));
        Assert.Equal(rounds, sampled.SpinsCompleted);

        void Agree(string what, double expected, double expectedStdErr, double graph, double graphStdErr, double shift)
        {
            var stdErr = System.Math.Sqrt(graphStdErr * graphStdErr + expectedStdErr * expectedStdErr);
            output.WriteLine($"{what}: graph {graph} ± {graphStdErr}, original {expected} ± {expectedStdErr} " +
                             $"({(graph - expected) / stdErr:F2} sigma; a shift of {shift:P0} is {expected * shift / stdErr:F1} sigma)");
            Assert.True(FourSigma.Accepts(expected, graph, stdErr), what);
            Assert.False(FourSigma.Accepts(expected * (1 + shift), graph, stdErr), $"{what} raised by {shift:P0}");
            Assert.False(FourSigma.Accepts(expected * (1 - shift), graph, stdErr), $"{what} lowered by {shift:P0}");
        }

        output.WriteLine(
            $"{rounds} rounds of the graph, {originalRounds} of the original; cap hits {sampled.Stats.CapHits}");
        Agree("RTP", Original("rtp"), Original("stdErr"), sampled.Stats.Mean, sampled.Stats.StdErr, rtpShift);
        Agree("rounds that pay", Original("hitFrequency"), Share(Original("hitFrequency"), originalRounds),
            sampled.Stats.HitFrequency,
            sampled.Stats.HitFrequencyStdErr, 0.01);
        Agree("scatters of the opening spin", Original("openingScatters"), Original("openingScattersStdErr"),
            observed["openingScatters"].Mean,
            observed["openingScatters"].StdErr, 0.01);
        Agree("win of the opening spin", Original("openingWin"), Original("openingWinStdErr"),
            observed["openingWin"].Mean,
            observed["openingWin"].StdErr, openingShift);
        Agree("free spins played", Original("freeSpins"), Original("freeSpinsStdErr"), observed["freeSpins"].Mean,
            observed["freeSpins"].StdErr,
            freeSpinShift);
        Agree("multiplier of the last spin", Original("finalMultiplier"), Original("finalMultiplierStdErr"),
            observed["finalMultiplier"].Mean,
            observed["finalMultiplier"].StdErr, multiplierShift);
        if (tier)
        {
            // Free spins bought outright always start, and their opening spin has no launcher to arm a reel with:
            // it leaves no multiplier.
            Assert.Equal(1, Original("bonusFrequency"));
            Assert.Equal(1, observed["bonus"].Mean);
            Assert.Equal(0, Original("openingMultiplier"));
            Assert.Equal(0, observed["openingMultiplier"].Mean);
        }
        else
        {
            Agree("multiplier of the opening spin", Original("openingMultiplier"), Original("openingMultiplierStdErr"),
                observed["openingMultiplier"].Mean, observed["openingMultiplier"].StdErr, multiplierShift);
            Agree("rounds with free spins", Original("bonusFrequency"),
                Share(Original("bonusFrequency"), originalRounds), observed["bonus"].Mean,
                observed["bonus"].StdErr, freeSpinShift);
        }
    }

    // Beyond its branch budget the hybrid evaluator has to fall back to sampling and say so in the provenance.
    [Theory]
    [MemberData(nameof(Kinds))]
    public void FullGame_Hybrid_FallsBackToSampling_AndSaysSo(string feature)
    {
        long rounds = IsTier(feature) ? 3_000 : 300_000;
        using JsonDocument golden = GameGraph.Golden($"le-militare.{feature}.golden.json");
        var observed = golden.RootElement.GetProperty("simulation").GetProperty("rtp").GetDouble();
        RegimeResult hybrid = Game(feature).Hybrid(GameGraph.FallbackBranches, rounds, Seed);
        Assert.Equal(EvaluationStrategy.Sampled, hybrid.OverallStrategy);
        Assert.Equal(Provenance.Sampled, hybrid.Report.Rtp.Provenance.Provenance);
        Assert.Equal(rounds, hybrid.Report.Rtp.SampleCount);
        var stdErr = hybrid.Report.Rtp.StdErr!.Value;
        output.WriteLine($"sampled {hybrid.Report.Rtp.DisplayValue} ± {stdErr}, original {observed}");
        Assert.True(FourSigma.Accepts(observed, hybrid.Report.Rtp.DisplayValue, stdErr));
        Assert.False(FourSigma.Accepts(observed * 2, hybrid.Report.Rtp.DisplayValue, stdErr));
    }

    private sealed record Spin(
        long[] Board,
        long Scatters,
        long Groups,
        long Multiplier,
        long Win,
        long Paid,
        int[] Armed);

    /// <summary>
    ///     A group that paid: its symbol, its size as the paytable counts it, its pay in credits, its cells in board
    ///     order.
    /// </summary>
    private sealed record Hit(long Symbol, long Size, long Pay, int[] Cells);

    /// <summary>
    ///     One board the game looked at: the board before and after the combat, the multipliers the planes left, the
    ///     reels that armed. A board that paid has its groups, the cells that left it, and the cells held once the
    ///     symbols have fallen; a board that did not has the cells held on it.
    /// </summary>
    private sealed record Look(
        int Spin,
        long[] Before,
        long[] After,
        long[] Shots,
        int[] Armed,
        long Win,
        Hit[] Hits,
        int[] Removed,
        int[] Held);

    /// <summary>
    ///     The air raid of the opening spin: the board before it, the squadron, and where its planes fell with what
    ///     multiplier.
    /// </summary>
    private sealed record Raid(long[] Board, long Squadron, int[] Cells, long[] Multipliers);

    private sealed record Played(Spin[] Spins, Look[] Looks, Raid? Raid);
}
