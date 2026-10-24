using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;
using Xunit.Abstractions;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Math.Games;

/// <summary>
///     Nine Lives, migrated from tgslots as a graph of subgraphs, held against the original game and against every
///     interpreter of the kernel. The game is two graphs: the paid round and the bought bonus, whose round costs 240
///     bets. The full graphs are too large to enumerate, so the interpreters that visit every outcome run on cut-down
///     models built by the same graph code (a board of two columns and two rows, two paying symbols, groups of three
///     or four cells), whose round cap is low enough to cut real wins.
/// </summary>
public class NineLivesGraphTests(ITestOutputHelper output)
{
    private const long Seed = 20260511;

    // The rules of the released game, stated here on their own so that the played rounds are checked against the
    // rules and not against the graph: the board, the bet, the free spins, and what bounds a round.
    private const int Columns = 6;
    private const int Rows = 5;
    private const int Cells = Columns * Rows;
    private const int Bet = 20;
    private const int Trigger = 4;
    private const int FreeSpins = 9;
    private const int MaxMultiplier = 25;
    private const int MaxSteps = 30;
    private const int MaxWinBets = 9999;
    private const int BuyCost = 240;
    private const long Wild = 0;
    private const long Coin = 6;
    private const long Scatter = 7;

    private static readonly Lazy<Variant> Paid = new(() => new Variant("nine-lives",
        new Rational(2017134143, 2025000000), new Rational(245819, 500000)));

    private static readonly Lazy<Variant> Bought = new(() => new Variant("nine-lives.buy",
        new Rational(65592774929, 76800000000), new Rational(580951, 640000)));

    private static Variant Game(bool buy) => (buy ? Bought : Paid).Value;

    private static long[] Whole(object? values) =>
        [.. GameGraph.Items(values).Select(value => (long)GameGraph.Number(value).Numerator)];

    private static long[] Whole(JsonElement values) => [.. values.EnumerateArray().Select(value => value.GetInt64())];

    /// <summary>A board as the graph holds it: the id of the symbol on every cell, column by column from the top.</summary>
    private static long[] Symbols(object? board) =>
        [.. GameGraph.Items(board).Select(cell => long.Parse(GameGraph.Text(cell)))];

    /// <summary>The cells marked with 1 in a list of one mark per cell.</summary>
    private static int[] Marked(object? marks) =>
    [
        .. Whole(marks).Select((mark, cell) => (mark, cell)).Where(m => m.mark == 1).Select(m => m.cell)
    ];

    /// <summary>What a played round of the graph leaves in its trace, in the shape the original reports its rounds.</summary>
    private static Played Trace(Dict state)
    {
        object?[] boards = GameGraph.Items(state["spinBoards"]), coins = GameGraph.Items(state["spinCoins"]);
        long[] multipliers = Whole(state["spinMultipliers"]), collected = Whole(state["spinCollected"]);
        long[] stepSpins = Whole(state["stepSpins"]),
            stepMultipliers = Whole(state["stepMultipliers"]),
            wins = Whole(state["stepWins"]);
        object?[] before = GameGraph.Items(state["cascadeBoards"]), removed = GameGraph.Items(state["cascadeRemoved"]);
        var after = GameGraph.Items(state["cascadeAfter"]);
        object?[] hitSymbols = GameGraph.Items(state["stepHitSymbols"]),
            hitSizes = GameGraph.Items(state["stepHitSizes"]);
        object?[] hitPays = GameGraph.Items(state["stepHitPays"]), hitCells = GameGraph.Items(state["stepHitCells"]);
        return new Played(
            [
                .. boards.Select((board, spin) =>
                    new Spin(Symbols(board), Whole(coins[spin]), multipliers[spin], collected[spin]))
            ],
            [
                .. stepSpins.Select((spin, step) => new Step((int)spin, stepMultipliers[step], wins[step],
                    Symbols(before[step]),
                    Marked(removed[step]), Symbols(after[step]),
                    [
                        .. Symbols(hitSymbols[step]).Select((symbol, hit) => new Hit(symbol, Whole(hitSizes[step])[hit],
                            Whole(hitPays[step])[hit], Marked(GameGraph.Items(hitCells[step])[hit])))
                    ]))
            ]);
    }

    private static Played Original(JsonElement round) => new(
        [
            .. round.GetProperty("spins").EnumerateArray().Select(spin => new Spin(Whole(spin.GetProperty("board")),
                Whole(spin.GetProperty("coins")), spin.GetProperty("multiplier").GetInt64(),
                spin.GetProperty("collected").GetInt64()))
        ],
        [
            .. round.GetProperty("steps").EnumerateArray().Select(step => new Step(step.GetProperty("spin").GetInt32(),
                step.GetProperty("multiplier").GetInt64(), step.GetProperty("win").GetInt64(),
                Whole(step.GetProperty("board")),
                [.. Whole(step.GetProperty("removed")).Select(cell => (int)cell)], Whole(step.GetProperty("after")),
                [
                    .. step.GetProperty("hits").EnumerateArray().Select(hit =>
                        new Hit(hit.GetProperty("symbol").GetInt64(),
                            hit.GetProperty("size").GetInt64(), hit.GetProperty("pay").GetInt64(),
                            [.. Whole(hit.GetProperty("cells")).Select(cell => (int)cell)]))
                ]))
        ]);

    /// <summary>The cluster paytable the original ships, by symbol id: what a group of each size pays, in credits.</summary>
    private static Dictionary<long, Dictionary<int, long>> Paytable(JsonDocument golden)
    {
        string[] names =
            [.. golden.RootElement.GetProperty("symbols").EnumerateArray().Select(name => name.GetString()!)];
        return golden.RootElement.GetProperty("config").GetProperty("paytable").EnumerateObject().ToDictionary(
            symbol => (long)Array.IndexOf(names, symbol.Name),
            symbol => symbol.Value.EnumerateObject()
                .ToDictionary(size => int.Parse(size.Name), size => size.Value.GetInt64()));
    }

    /// <summary>
    ///     The groups that pay on a board, found the way the rules are told to a player and not the way the graph
    ///     finds them: symbols in the order they first appear, each group flooded out from its first cell through
    ///     cells of the symbol and wilds, and a wild used by a group that paid closed to the symbols after it.
    /// </summary>
    private static List<Hit> PayingGroups(long[] board, Dictionary<long, Dictionary<int, long>> pays)
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
            var rows = board.Length / Columns;
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
                    int column = cell / rows, row = cell % rows;
                    foreach (var (nextColumn, nextRow) in new[]
                             {
                                 (column, row - 1), (column, row + 1), (column - 1, row), (column + 1, row)
                             })
                    {
                        var next = nextColumn * rows + nextRow;
                        if (nextColumn < 0 || nextColumn >= Columns || nextRow < 0 || nextRow >= rows || seen[next] ||
                            !(board[next] == symbol || (board[next] == Wild && !usedWilds.Contains(next))))
                        {
                            continue;
                        }

                        seen[next] = true;
                        open.Push(next);
                    }
                }

                if (!pays[symbol].TryGetValue(group.Count, out var pay) || pay <= 0)
                {
                    continue;
                }

                usedWilds.UnionWith(group.Where(cell => board[cell] == Wild));
                groups.Add(new Hit(symbol, group.Count, pay, [.. group.Order()]));
            }
        }

        return groups;
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    // The graph's own tables are the original's, entry by entry: the weights of every draw and the whole paytable.
    // Sampling can only tell tables apart as far as its precision goes; this comparison is exact.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TablesOfTheGraph_AreTheOriginalGamesOwn(bool buy)
    {
        Variant variant = Game(buy);
        using JsonDocument golden = variant.Golden();
        JsonElement config = golden.RootElement.GetProperty("config");
        GraphConfig graph = variant.Full.Config;

        // The one table every draw of a subgraph uses. A weight of 0 is no outcome at all, in the original as in
        // the graph.
        Dictionary<string, long> Draw(string mechanic, int draws = 1)
        {
            Node[] nodes = graph.Mechanics![mechanic].Nodes;
            DrawManyNode? batch = nodes.OfType<DrawManyNode>().SingleOrDefault();
            if (batch is not null)
            {
                Assert.Empty(nodes.OfType<DrawNode>());
                Assert.Equal(draws, batch.Count);
                Assert.Null(batch.DistributionIds);
                Assert.NotNull(batch.DistributionId);
                return graph.Distributions![batch.DistributionId].Outcomes
                    .ToDictionary(outcome => outcome.OutcomeId, outcome => outcome.Weight);
            }

            Dictionary<string, long>[] tables =
            [
                .. nodes.OfType<DrawNode>().Select(draw =>
                    draw.DrawWeights!.ToDictionary(outcome => outcome.OutcomeId, outcome => outcome.Weight))
            ];
            Assert.Equal(draws, tables.Length);
            Assert.All(tables, table => Assert.Equal(tables[0], table));
            return tables[0];
        }

        Dictionary<string, long> Weights(string table)
        {
            return Whole(config.GetProperty(table)).Select((weight, symbol) => (weight, symbol))
                .Where(entry => entry.weight > 0).ToDictionary(entry => entry.symbol.ToString(), entry => entry.weight);
        }

        if (!buy)
        {
            Assert.Equal(Weights("weights"), Draw("paid-board", Cells));
        }

        Assert.Equal(Weights("bonusWeights"), Draw("free-board", Cells));
        Assert.Equal(Weights("refillWeights"), Draw("drop-symbol"));
        Assert.Equal(
            config.GetProperty("prizes").EnumerateArray()
                .ToDictionary(prize => prize[0].GetInt64().ToString(), prize => prize[1].GetInt64()),
            Draw("coin-prizes", Cells));

        Dictionary<long, Dictionary<int, long>> pays = Paytable(golden);
        long[] table = [.. graph.InitialState!["clusterPays"].EnumerateArray().Select(pay => pay.GetInt64())];
        var symbols = golden.RootElement.GetProperty("symbols").GetArrayLength();
        Assert.Equal(symbols * (Cells + 1), table.Length);
        for (var symbol = 0; symbol < symbols; symbol++)
        {
            for (var size = 0; size <= Cells; size++)
            {
                var pay = pays.TryGetValue(symbol, out Dictionary<int, long>? row) ? row.GetValueOrDefault(size) : 0;
                Assert.True(pay == table[symbol * (Cells + 1) + size], $"symbol {symbol}, a group of {size}");
            }
        }

        Assert.Equal(Bet, config.GetProperty("baseCost").GetInt32());
        Assert.Equal(FreeSpins, config.GetProperty("freeSpins").GetInt32());
        Assert.Equal(MaxMultiplier, config.GetProperty("maxMultiplier").GetInt32());
        Assert.Equal(MaxSteps, config.GetProperty("maxCascades").GetInt32());
        Assert.Equal(MaxWinBets, config.GetProperty("maxWinX").GetInt32());
        Assert.Equal(BuyCost, config.GetProperty("buyCost").GetInt32());
    }

    // The rounds were played by the original TypeScript state machine. Each lists the outcome of every random
    // choice it made, in the order the graph draws them, and what the original showed: every spin as it landed
    // and every step of its cascades. Half of them were chosen for their feature: paid rounds that reached the
    // free spins, bought rounds with a cascade of four steps or more.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalRounds_ReplayedDrawForDraw_CascadeTheSameAndPayTheSame(bool buy)
    {
        Variant variant = Game(buy);
        using JsonDocument golden = variant.Golden();
        var roundCost = golden.RootElement.GetProperty("roundCost").GetInt32();
        int rounds = 0, chosen = 0, steps = 0, groups = 0, wildGroups = 0;
        foreach (JsonElement round in golden.RootElement.GetProperty("rounds").EnumerateArray())
        {
            (Rational win, Dict state) =
                variant.Full.Replay(round.GetProperty("draws").EnumerateArray().Select(d => d.GetInt32()));
            Played original = Original(round), played = Trace(state);
            Assert.Equal(original.Spins.Length, played.Spins.Length);
            for (var spin = 0; spin < original.Spins.Length; spin++)
            {
                Assert.Equal(Json(original.Spins[spin]), Json(played.Spins[spin]));
            }

            Assert.Equal(original.Steps.Length, played.Steps.Length);
            for (var step = 0; step < original.Steps.Length; step++)
            {
                Assert.Equal(Json(original.Steps[step]), Json(played.Steps[step]));
            }

            Assert.Equal(new Rational(round.GetProperty("win").GetInt64(), roundCost), win);
            rounds++;
            steps += played.Steps.Length;
            groups += played.Steps.Sum(step => step.Hits.Length);
            wildGroups += played.Steps.Sum(step =>
                step.Hits.Count(hit => hit.Cells.Any(cell => step.Board[cell] == Wild)));
            chosen += buy
                ? played.Steps.GroupBy(step => step.Spin).Any(spin => spin.Count() >= 4) ? 1 : 0
                : played.Spins.Length > 1
                    ? 1
                    : 0;
        }

        output.WriteLine(
            $"{rounds} rounds, {chosen} chosen for their feature, {steps} steps, {groups} groups, {wildGroups} of them through a wild");
        Assert.True(rounds >= 60 && chosen >= 30 && steps >= 300 && wildGroups >= 30);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void PlayedRound_TraceFollowsTheRulesAndAccountsForTheWin_AndTracingDoesNotChangeIt(bool buy,
        bool compiledTier)
    {
        Variant variant = Game(buy);
        GameGraph game = variant.Full;
        using JsonDocument golden = variant.Golden();
        Dictionary<long, Dictionary<int, long>> pays = Paytable(golden);
        const long Cap = MaxWinBets * Bet;
        var roundCost = buy ? Bet * BuyCost : Bet;
        int bonuses = 0, steps = 0, wildGroups = 0, coins = 0;
        for (var round = 0; round < (buy ? 40 : 4_000); round++)
        {
            (Rational win, Dict state) = game.Play(Seed, round, true, compiledTier);
            Assert.Equal(win, game.Play(Seed, round, false, compiledTier).Win);
            // Both tiers draw from the same stream in the same order, so they play the same round.
            Assert.Equal(win, game.Play(Seed, round, true, !compiledTier).Win);
            // No loop is cut short by its limit: each one ends by the rules of the game.
            Assert.DoesNotContain("modelLimit", GameGraph.LoopExits(state).Values);

            Played played = Trace(state);
            var paid = played.Steps.Sum(step => step.Win) + played.Spins.Sum(spin => spin.Collected);
            Assert.Equal(new Rational(System.Math.Min(paid, Cap), roundCost), win);

            // A paid round is one spin, and nine free spins after it when it lands four scatters; a bought round
            // is the nine free spins. Only a round that has reached the most it may pay ends sooner.
            var triggered = buy || played.Spins[0].Board.Count(symbol => symbol == Scatter) >= Trigger;
            var spins = (buy ? 0 : 1) + (triggered ? FreeSpins : 0);
            Assert.True(paid >= Cap ? played.Spins.Length <= spins : played.Spins.Length == spins,
                $"{played.Spins.Length} spins");
            bonuses += triggered ? 1 : 0;

            long multiplier = 1;
            for (var index = 0; index < played.Spins.Length; index++)
            {
                Spin spin = played.Spins[index];
                var free = buy || index > 0;
                Assert.Equal(multiplier, spin.Multiplier);
                // Only a coin of a free spin carries a prize, a whole number of bets; it is collected once, at
                // the multiplier the spin starts with, and leaves a wild.
                for (var cell = 0; cell < Cells; cell++)
                {
                    Assert.True(free && spin.Board[cell] == Coin
                        ? spin.Coins[cell] > 0 && spin.Coins[cell] % Bet == 0
                        : spin.Coins[cell] == 0);
                }

                Assert.Equal(free ? spin.Coins.Sum() * multiplier : 0, spin.Collected);
                coins += spin.Coins.Count(coin => coin > 0);
                var board = free ? [.. spin.Board.Select(symbol => symbol == Coin ? Wild : symbol)] : spin.Board;

                Step[] cascade = [.. played.Steps.Where(step => step.Spin == index)];
                for (var number = 0; number < cascade.Length; number++)
                {
                    Step step = cascade[number];
                    Assert.Equal(board, step.Board);
                    List<Hit> groups = PayingGroups(board, pays);
                    Assert.NotEmpty(groups);
                    Assert.Equal(Json(groups), Json(step.Hits));
                    // Every step pays more than the one before, up to the limit of the multiplier.
                    Assert.Equal(System.Math.Min(MaxMultiplier, multiplier + number), step.Multiplier);
                    Assert.Equal(groups.Sum(group => group.Pay) * step.Multiplier, step.Win);

                    // Every cell of a symbol that paid leaves the board, with the wilds its groups used.
                    HashSet<long> paidSymbols = groups.Select(group => group.Symbol).ToHashSet();
                    HashSet<int> removed = groups.SelectMany(group => group.Cells)
                        .Concat(Enumerable.Range(0, Cells).Where(cell => paidSymbols.Contains(board[cell])))
                        .ToHashSet();
                    Assert.Equal(removed.Order(), step.Removed);
                    // What stays falls to the bottom of its column in the order it stood, and what drops in
                    // above it is never a coin or a scatter.
                    for (var column = 0; column < Columns; column++)
                    {
                        long[] kept =
                        [
                            .. Enumerable.Range(column * Rows, Rows).Where(cell => !removed.Contains(cell))
                                .Select(cell => board[cell])
                        ];
                        long[] fallen = [.. step.After.Skip(column * Rows).Take(Rows)];
                        Assert.Equal(kept, fallen[(Rows - kept.Length)..]);
                        Assert.DoesNotContain(fallen[..(Rows - kept.Length)],
                            symbol => symbol == Coin || symbol == Scatter);
                    }

                    board = step.After;
                    wildGroups += groups.Count(group => group.Cells.Any(cell => step.Board[cell] == Wild));
                }

                // A cascade ends on a board that pays nothing, or after thirty steps.
                Assert.True(cascade.Length == MaxSteps || PayingGroups(board, pays).Count == 0);
                steps += cascade.Length;
                // The steps a free spin paid raise the multiplier of the spins after it.
                multiplier = free ? System.Math.Min(MaxMultiplier, multiplier + cascade.Length) : 1;
            }
        }

        output.WriteLine(
            $"{bonuses} rounds with free spins, {steps} steps, {wildGroups} groups through a wild, {coins} coins");
        Assert.True(bonuses >= 3 && steps >= 300 && wildGroups >= 10 && coins >= 30,
            $"{bonuses} bonuses, {steps} steps");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmallGame_EveryExactInterpreterGivesTheSameRationals(bool buy)
    {
        Variant variant = Game(buy);
        GameGraph game = variant.Small;
        // Within its branch budget the hybrid evaluator is the classic interpreter: every outcome is visited.
        RegimeResult hybrid = game.Hybrid(2_000_000, 10_000, Seed);
        Assert.Equal(EvaluationStrategy.Exact, hybrid.OverallStrategy);
        SlotMathReport classic = hybrid.Report;
        Assert.Equal(Provenance.Exact, classic.Rtp.Provenance.Provenance);
        Rational rtp = GameGraph.Exact(classic.Rtp);
        Assert.Equal(variant.SmallRtp, rtp);
        Assert.Equal(variant.SmallHitFrequency.ToDouble(), classic.HitFrequency.DisplayValue, 12);

        StructuredExactResult structured = game.Structured();
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        Assert.True(structured.PrunedMass.IsZero);
        Assert.Equal(rtp, GameGraph.Exact(structured.Rtp(game.WinScale)));
        Assert.Equal(variant.SmallHitFrequency, structured.HitFrequency);
        // The cap of this cut-down game is a rule that cuts real wins: the largest win is exactly the cap, which
        // for the bought round is not a whole number of its price.
        var cap = new Rational(8 * 2, buy ? 2 * 3 : 2);
        Assert.Equal(cap, new Rational(structured.Distribution!.Entries.Max(entry => entry.Value), game.WinScale));

        // Pruning drops, at every draw, the branches rarer than epsilon and has to say how much it dropped. Rarer
        // than 3 in 20 are the wild of a paid spin (1 in 10) and the coin of a free spin (1 in 20); with the
        // declared win cap the result is an enclosure of the exact value (D5).
        SlotMathReport pruned = game.Enumerate(new Rational(3, 20));
        ProvenanceTag enclosure = pruned.Rtp.Provenance;
        Assert.Equal(Provenance.ExactInterval, enclosure.Provenance);
        Assert.Equal(BoundSource.DeclaredWinCap, enclosure.BoundSource);
        output.WriteLine(
            $"pruned mass {enclosure.PrunedMass}, enclosure [{enclosure.Lo}, {enclosure.Hi}] of {rtp.ToDouble()}");
        Assert.InRange(enclosure.PrunedMass!.Value, 0.01, 0.5);
        Assert.True(enclosure.Lo < rtp.ToDouble() && rtp.ToDouble() < enclosure.Hi);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmallGame_SamplingAgreesWithTheExactValue_AndRejectsItShiftedByOnePercent(bool buy)
    {
        const long Rounds = 2_000_000;
        Variant variant = Game(buy);
        var exact = variant.SmallRtp.ToDouble();
        SampledResult<Dict> sampled = variant.Small.Sample(Rounds, Seed);
        Assert.Equal(Rounds, sampled.SpinsCompleted);
        output.WriteLine(
            $"exact {exact} sampled {sampled.Stats.Mean} ± {sampled.Stats.StdErr}, cap hits {sampled.Stats.CapHits}");
        Assert.True(FourSigma.Accepts(exact, sampled.Stats.Mean, sampled.Stats.StdErr));
        Assert.False(FourSigma.Accepts(exact * 1.01, sampled.Stats.Mean, sampled.Stats.StdErr));
        Assert.False(FourSigma.Accepts(exact * 0.99, sampled.Stats.Mean, sampled.Stats.StdErr));
        var hits = variant.SmallHitFrequency.ToDouble();
        Assert.True(FourSigma.Accepts(hits, sampled.Stats.HitFrequency, sampled.Stats.HitFrequencyStdErr));
        Assert.False(FourSigma.Accepts(hits * 0.99, sampled.Stats.HitFrequency, sampled.Stats.HitFrequencyStdErr));
    }

    // The round cap of 9999 bets is a rule of the game that a win can reach, so the exact RTP needs the whole law
    // of the round's win and not only its expectation by linearity, and that law is beyond the structured evaluator
    // for thirty independent cells that cascade. It has to say so within its budget, and never return a number.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullGame_StructuredExact_DeclinesWithinItsBudget(bool buy)
    {
        StructuredExactResult result =
            Game(buy).Full.Structured(new StructuredExactOptions
            {
                MaxTime = TimeSpan.FromSeconds(3)
            });
        output.WriteLine(
            $"declined: {result.DeclinedReason} after {result.Elapsed.TotalSeconds:F1} s, work {result.Work}");
        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.DeclinedReason));
        Assert.InRange(result.Elapsed.TotalSeconds, 0, 15);
    }

    // Without an exact value the full game is held against the original directly: 40 million rounds of the original
    // state machine and 12 million of the graph are two independent estimates of the same game.
    //
    // A paid round can pay 9999 bets and its win has a standard deviation of about 19 bets, so a 1% shift of the
    // RTP is beyond what any affordable number of rounds can tell apart: it would take some 300 million rounds of
    // each. The 1% controls are therefore on what the RTP is made of and can be measured that finely: the win of
    // the paid spin, which is more than half of the RTP, and how often a round pays. How often the free spins
    // start is known exactly, and what they are worth is held to the original by the bought round.
    [Fact]
    public void FullGame_PaidRound_SamplingAgreesWithTheOriginalGame_AndRejectsShiftedValues()
    {
        const long Rounds = 12_000_000;
        Variant variant = Game(false);
        using JsonDocument golden = variant.Golden();
        JsonElement original = golden.RootElement.GetProperty("simulation");
        var originalRounds = original.GetProperty("rounds").GetInt64();

        double Original(string name)
        {
            return original.GetProperty(name).GetDouble();
        }

        double Both(double one, double other)
        {
            return System.Math.Sqrt(one * one + other * other);
        }

        // What the paid spin has paid is read where the round decides whether the free spins start.
        var bet = new ConstantExpr
        {
            Kind = ConstantKind.Integer,
            Value = Bet.ToString()
        };
        (SampledResult<Dict> sampled, Dictionary<string, (double Mean, double StdErr)> observed) = variant.Full.Measure(
            Rounds, Seed, 8,
            GameGraph.Quantity("paidSpin",
                new BinaryExpr
                {
                    Op = BinaryOp.Div,
                    Left = GameGraph.State("roundCoins"),
                    Right = bet
                }, "trigger"),
            GameGraph.Quantity("scatters", GameGraph.State("scatterCount")),
            GameGraph.Quantity("triggered",
                new IfExpr
                {
                    Condition = new CompareExpr
                    {
                        Op = CompareOp.Gte,
                        Left = GameGraph.State("scatterCount"),
                        Right = new ConstantExpr
                        {
                            Kind = ConstantKind.Integer,
                            Value = Trigger.ToString()
                        }
                    },
                    ThenExpr = new ConstantExpr
                    {
                        Kind = ConstantKind.Integer,
                        Value = "1"
                    },
                    ElseExpr = new ConstantExpr
                    {
                        Kind = ConstantKind.Integer,
                        Value = "0"
                    }
                }));
        Assert.Equal(Rounds, sampled.SpinsCompleted);

        var rtp = Original("rtp");
        var stdErr = Both(sampled.Stats.StdErr, Original("stdErr"));
        output.WriteLine(
            $"RTP: graph {sampled.Stats.Mean} ± {sampled.Stats.StdErr} over {Rounds} rounds, original {rtp} ± " +
            $"{Original("stdErr")} over {originalRounds} rounds ({(sampled.Stats.Mean - rtp) / stdErr:F2} sigma), cap hits {sampled.Stats.CapHits}");
        Assert.True(FourSigma.Accepts(rtp, sampled.Stats.Mean, stdErr));
        Assert.False(FourSigma.Accepts(rtp * 1.05, sampled.Stats.Mean, stdErr));
        Assert.False(FourSigma.Accepts(rtp * 0.95, sampled.Stats.Mean, stdErr));

        var paidSpin = Original("baseRtp");
        var paidSpinStdErr = Both(observed["paidSpin"].StdErr, Original("baseStdErr"));
        output.WriteLine(
            $"paid spin: graph {observed["paidSpin"].Mean} ± {observed["paidSpin"].StdErr}, original {paidSpin} ± {Original("baseStdErr")}");
        Assert.True(FourSigma.Accepts(paidSpin, observed["paidSpin"].Mean, paidSpinStdErr));
        Assert.False(FourSigma.Accepts(paidSpin * 1.01, observed["paidSpin"].Mean, paidSpinStdErr));
        Assert.False(FourSigma.Accepts(paidSpin * 0.99, observed["paidSpin"].Mean, paidSpinStdErr));

        var hits = Original("hitFrequency");
        var hitStdErr = Both(sampled.Stats.HitFrequencyStdErr, System.Math.Sqrt(hits * (1 - hits) / originalRounds));
        output.WriteLine($"hit frequency: graph {sampled.Stats.HitFrequency}, original {hits}");
        Assert.True(FourSigma.Accepts(hits, sampled.Stats.HitFrequency, hitStdErr));
        Assert.False(FourSigma.Accepts(hits * 1.01, sampled.Stats.HitFrequency, hitStdErr));
        Assert.False(FourSigma.Accepts(hits * 0.99, sampled.Stats.HitFrequency, hitStdErr));

        // The free spins start when four or more of the thirty cells land a scatter, each on its own with the
        // weight the original gives it: a binomial tail, worked out here exactly. One round in 520 starts them,
        // which bounds how finely 12 million rounds can count them: the control is a shift of 5%.
        var weights = Whole(golden.RootElement.GetProperty("config").GetProperty("weights"));
        var scatter = new Rational(weights[Scatter], weights.Sum());
        Rational fewer = Rational.Zero;
        for (var count = 0; count < Trigger; count++)
        {
            Rational ways = Enumerable.Range(0, count)
                .Aggregate(Rational.One, (product, k) => product * new Rational(Cells - k, k + 1));
            fewer += ways * Power(scatter, count) * Power(Rational.One - scatter, Cells - count);
        }

        var trigger = (Rational.One - fewer).ToDouble();
        output.WriteLine(
            $"free spins start: graph {observed["triggered"].Mean} ± {observed["triggered"].StdErr}, exactly {trigger}, " +
            $"original {Original("triggerFrequency")}");
        Assert.True(FourSigma.Accepts(trigger, observed["triggered"].Mean, observed["triggered"].StdErr));
        Assert.False(FourSigma.Accepts(trigger * 1.05, observed["triggered"].Mean, observed["triggered"].StdErr));
        Assert.False(FourSigma.Accepts(trigger * 0.95, observed["triggered"].Mean, observed["triggered"].StdErr));
        Assert.True(FourSigma.Accepts(trigger, Original("triggerFrequency"),
            System.Math.Sqrt(trigger * (1 - trigger) / originalRounds)));
        // The scatters of a spin are thirty independent cells, so their mean is exact too and tells 1% apart.
        var scatters = (scatter * new Rational(Cells, 1)).ToDouble();
        Assert.True(FourSigma.Accepts(scatters, observed["scatters"].Mean, observed["scatters"].StdErr));
        Assert.False(FourSigma.Accepts(scatters * 1.01, observed["scatters"].Mean, observed["scatters"].StdErr));
        Assert.False(FourSigma.Accepts(scatters * 0.99, observed["scatters"].Mean, observed["scatters"].StdErr));
    }

    // The bought round is the nine free spins, measured against its price of 240 bets. The graph plays about two
    // thousand of them a second, which is enough to tell a 3% shift of their RTP apart but not 1%; the 1% control
    // is on the multiplier the free spins end with, which counts every step that paid and so depends on every
    // rule of the cascade: the groups, the wilds the coins leave, what falls and what drops in.
    [Fact]
    public void FullGame_BoughtRound_SamplingAgreesWithTheOriginalGame_AndRejectsShiftedValues()
    {
        const long Rounds = 200_000;
        Variant variant = Game(true);
        using JsonDocument golden = variant.Golden();
        JsonElement original = golden.RootElement.GetProperty("simulation");

        double Original(string name)
        {
            return original.GetProperty(name).GetDouble();
        }

        double Both(double one, double other)
        {
            return System.Math.Sqrt(one * one + other * other);
        }

        (SampledResult<Dict> sampled, Dictionary<string, (double Mean, double StdErr)> observed) =
            variant.Full.Measure(Rounds, Seed, 8, GameGraph.Quantity("multiplier", GameGraph.State("multiplier")));
        Assert.Equal(Rounds, sampled.SpinsCompleted);

        var rtp = Original("rtp");
        var stdErr = Both(sampled.Stats.StdErr, Original("stdErr"));
        output.WriteLine(
            $"RTP: graph {sampled.Stats.Mean} ± {sampled.Stats.StdErr} over {Rounds} rounds, original {rtp} ± " +
            $"{Original("stdErr")} over {original.GetProperty("rounds").GetInt64()} rounds " +
            $"({(sampled.Stats.Mean - rtp) / stdErr:F2} sigma), cap hits {sampled.Stats.CapHits}");
        Assert.True(FourSigma.Accepts(rtp, sampled.Stats.Mean, stdErr));
        Assert.False(FourSigma.Accepts(rtp * 1.03, sampled.Stats.Mean, stdErr));
        Assert.False(FourSigma.Accepts(rtp * 0.97, sampled.Stats.Mean, stdErr));

        var multiplier = Original("finalMultiplier");
        var multiplierStdErr = Both(observed["multiplier"].StdErr, Original("finalMultiplierStdErr"));
        output.WriteLine(
            $"multiplier at the end: graph {observed["multiplier"].Mean} ± {observed["multiplier"].StdErr}, " +
            $"original {multiplier} ± {Original("finalMultiplierStdErr")}");
        Assert.True(FourSigma.Accepts(multiplier, observed["multiplier"].Mean, multiplierStdErr));
        Assert.False(FourSigma.Accepts(multiplier * 1.01, observed["multiplier"].Mean, multiplierStdErr));
        Assert.False(FourSigma.Accepts(multiplier * 0.99, observed["multiplier"].Mean, multiplierStdErr));

        // A bought round all but always pays something.
        output.WriteLine($"hit frequency: graph {sampled.Stats.HitFrequency}, original {Original("hitFrequency")}");
        Assert.InRange(sampled.Stats.HitFrequency, Original("hitFrequency") - 0.001, 1);
    }

    private static Rational Power(Rational value, int exponent) =>
        new(BigInteger.Pow(value.Numerator, exponent), BigInteger.Pow(value.Denominator, exponent));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullGame_SamplingIsBitIdentical_AcrossWorkersAndTiers(bool buy)
    {
        long rounds = buy ? 6_000 : 200_000;
        GameGraph game = Game(buy).Full;
        game.AssertSamplingIsBitIdentical(rounds, Seed);
    }

    // Beyond its branch budget the hybrid evaluator has to fall back to sampling and say so in the provenance.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullGame_Hybrid_FallsBackToSampling_AndSaysSo(bool buy)
    {
        long rounds = buy ? 20_000 : 1_000_000;
        Variant variant = Game(buy);
        using JsonDocument golden = variant.Golden();
        var observed = golden.RootElement.GetProperty("simulation").GetProperty("rtp").GetDouble();
        RegimeResult hybrid = variant.Full.Hybrid(GameGraph.FallbackBranches, rounds, Seed);
        Assert.Equal(EvaluationStrategy.Sampled, hybrid.OverallStrategy);
        Assert.Equal(Provenance.Sampled, hybrid.Report.Rtp.Provenance.Provenance);
        Assert.Equal(rounds, hybrid.Report.Rtp.SampleCount);
        output.WriteLine($"sampled {hybrid.Report.Rtp.DisplayValue}, original {observed}");
        Assert.InRange(hybrid.Report.Rtp.DisplayValue, observed * 0.8, observed * 1.2);
    }

    private sealed record Spin(long[] Board, long[] Coins, long Multiplier, long Collected);

    /// <summary>A group that paid: its symbol, its size, its pay in credits before the multiplier, its cells in board order.</summary>
    private sealed record Hit(long Symbol, long Size, long Pay, int[] Cells);

    /// <summary>One step of a cascade that paid: the board that paid, the cells that left it and the board after the fall.</summary>
    private sealed record Step(
        int Spin,
        long Multiplier,
        long Win,
        long[] Board,
        int[] Removed,
        long[] After,
        Hit[] Hits);

    private sealed record Played(Spin[] Spins, Step[] Steps);

    /// <summary>One of the game's two graphs with its cut-down model and what is known about both.</summary>
    private sealed class Variant(string name, Rational smallRtp, Rational smallHitFrequency)
    {
        public GameGraph Full { get; } = GameGraph.Load($"{name}.json");

        public GameGraph Small { get; } = GameGraph.Load($"{name}.small.json");

        /// <summary>The cut-down game's RTP and hit frequency, worked out by the classic interpreter.</summary>
        public Rational SmallRtp => smallRtp;

        public Rational SmallHitFrequency => smallHitFrequency;

        public JsonDocument Golden() => GameGraph.Golden($"{name}.golden.json");
    }
}
