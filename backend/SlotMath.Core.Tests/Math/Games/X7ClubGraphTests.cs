using System.Diagnostics;
using System.Text.Json;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Math.Structured;
using Xunit.Abstractions;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Math.Games;

/// <summary>
///     X7 Club, migrated from tgslots as a graph of subgraphs, held against the original game and against every
///     interpreter of the kernel. The game is two graphs: the paid round and the bought bonus, whose round costs 77
///     bets. The full graphs are too large to enumerate, so the interpreters that visit every outcome run on cut-down
///     models built by the same graph code (three cells in one row, three symbols, two prizes, two respins and a
///     boost of two pulls), whose round cap is low enough to cut real wins.
/// </summary>
public class X7ClubGraphTests(ITestOutputHelper output)
{
    private const long Seed = 20260511;

    // The rules of the released game, stated here on their own so that the played rounds are checked against the
    // rules and not against the tables of the graph: a prize of 1 is worth a bet of 20 credits, a coin restores
    // three respins, and a pull adds bets to every coin of its column or multiplies them.
    private const int Rows = 3;
    private const int Cells = 15;
    private const int Bet = 20;
    private const int Respins = 3;
    private const int MaxPulls = 7;
    private const int MaxWinBets = 7777;

    private static readonly Dictionary<string, (int Add, int Times, bool Ends)> Pulls = new()
    {
        ["STOP"] = (0, 1, true),
        ["PLUS1"] = (Bet, 1, false),
        ["PLUS2"] = (2 * Bet, 1, false),
        ["X7"] = (0, 7, true)
    };

    private static readonly Lazy<Variant> Paid = new(() => new Variant("x7-club",
        new Rational(7574110931017, 3149280000000), new Rational(71, 125)));

    private static readonly Lazy<Variant> Bought = new(() => new Variant("x7-club.buy",
        new Rational(125734587559, 62985600000), Rational.One));

    private static Variant Game(bool buy) => (buy ? Bought : Paid).Value;

    private static long[] Whole(object? values) =>
        [.. GameGraph.Items(values).Select(value => (long)GameGraph.Number(value).Numerator)];

    private static long[] Whole(JsonElement values) => [.. values.EnumerateArray().Select(value => value.GetInt64())];

    // The rounds were played by the original TypeScript state machine. Each lists the outcome of every random
    // choice it made, in the order the graph draws them, and what the original showed after every step of the
    // Hold & Spin. Half of them were chosen for their feature: rounds in which columns were boosted.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalRounds_ReplayedDrawForDraw_HoldTheSameCoinsAndPayTheSame(bool buy)
    {
        Variant variant = Game(buy);
        using JsonDocument golden = variant.Golden();
        var roundCost = golden.RootElement.GetProperty("roundCost").GetInt32();
        int rounds = 0, chosen = 0, respins = 0, pulls = 0;
        foreach (JsonElement round in golden.RootElement.GetProperty("rounds").EnumerateArray())
        {
            (Rational win, Dict state) =
                variant.Full.Replay(round.GetProperty("draws").EnumerateArray().Select(d => d.GetInt32()));
            JsonElement[] steps = [.. round.GetProperty("steps").EnumerateArray()];
            var coins = Whole(round.GetProperty("coins"));
            if (!buy)
            {
                Assert.Equal(Whole(round.GetProperty("board")),
                    GameGraph.Items(Assert.Single(GameGraph.Items(state["spinBoards"])))
                        .Select(cell => long.Parse(GameGraph.Text(cell))));
                Assert.Equal(coins, Whole(Assert.Single(GameGraph.Items(state["spinCoins"]))));
                Assert.Equal(new Rational(round.GetProperty("lineWin").GetInt64(), 1),
                    GameGraph.Number(state["lineCoins"]));
            }

            // The trace opens with the coins the feature starts with, then has one entry per step of the original.
            var events = GameGraph.Items(state["holdEvents"]);
            var columns = GameGraph.Items(state["holdColumns"]);
            var held = GameGraph.Items(state["holdCoins"]);
            var left = GameGraph.Items(state["holdRespins"]);
            Assert.Equal(buy || steps.Length > 0 ? steps.Length + 1 : 0, events.Length);
            if (events.Length > 0)
            {
                Assert.Equal("start", GameGraph.Text(events[0]));
                Assert.Equal(coins, Whole(held[0]));
            }

            var boosts = 0;
            for (var step = 0; step < steps.Length; step++)
            {
                Assert.Equal(steps[step].GetProperty("event").GetString(), GameGraph.Text(events[step + 1]));
                Assert.Equal(new Rational(steps[step].GetProperty("column").GetInt64(), 1),
                    GameGraph.Number(columns[step + 1]));
                Assert.Equal(Whole(steps[step].GetProperty("coins")), Whole(held[step + 1]));
                Assert.Equal(new Rational(steps[step].GetProperty("respins").GetInt64(), 1),
                    GameGraph.Number(left[step + 1]));
                boosts += steps[step].GetProperty("column").GetInt64() >= 0 ? 1 : 0;
            }

            Assert.Equal(new Rational(round.GetProperty("win").GetInt64(), roundCost), win);
            rounds++;
            pulls += boosts;
            respins += steps.Length - boosts;
            chosen += boosts > (buy ? 3 : 0) ? 1 : 0;
        }

        output.WriteLine($"{rounds} rounds, {chosen} chosen for their boosts, {respins} respins, {pulls} pulls");
        Assert.True(rounds >= 60 && chosen >= 30);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void PlayedRound_TraceFollowsTheRulesAndAccountsForTheWin_AndTracingDoesNotChangeIt(bool buy,
        bool compiledTier)
    {
        GameGraph game = Game(buy).Full;
        int featureRounds = 0, boostedColumns = 0;
        for (var round = 0; round < (buy ? 300 : 6_000); round++)
        {
            (Rational win, Dict state) = game.Play(Seed, round, true, compiledTier);
            Assert.Equal(win, game.Play(Seed, round, false, compiledTier).Win);
            // Both tiers draw from the same stream in the same order, so they play the same round.
            Assert.Equal(win, game.Play(Seed, round, true, !compiledTier).Win);

            Rational roundCost = GameGraph.Number(state["roundCost"]);
            Rational lines = buy ? Rational.Zero : GameGraph.Number(state["lineCoins"]);
            Rational bonus = GameGraph.Number(state["bonusCoins"]);
            Rational paid = lines + bonus;
            var cap = new Rational(MaxWinBets * Bet, 1);
            Assert.Equal((paid > cap ? cap : paid) / roundCost, win);

            string[] events = [.. GameGraph.Items(state["holdEvents"]).Select(GameGraph.Text)];
            if (events.Length == 0)
            {
                // Without six coins there is no Hold & Spin, and the coins on the board pay nothing.
                Assert.False(buy);
                Assert.True(Whole(Assert.Single(GameGraph.Items(state["spinCoins"]))).Count(coin => coin > 0) < 6);
                Assert.Equal(Rational.Zero, bonus);
                continue;
            }

            // No loop of the feature is cut short by its limit: each one ends by the rules of the game.
            Dictionary<string, string> exits = GameGraph.LoopExits(state);
            Assert.NotEmpty(exits);
            Assert.DoesNotContain("modelLimit", exits.Values);
            featureRounds++;
            var columns = Whole(state["holdColumns"]);
            long[][] held = [.. GameGraph.Items(state["holdCoins"]).Select(Whole)];
            var left = Whole(state["holdRespins"]);
            Assert.Equal("start", events[0]);
            Assert.Equal(Respins, left[0]);
            Assert.True(held[0].Count(coin => coin > 0) >= 6);
            var boosted = new HashSet<long>();
            var pullsOfColumn = 0;
            for (var step = 1; step < events.Length; step++)
            {
                long[] before = held[step - 1], after = held[step];
                if (events[step] == "respin")
                {
                    // Held coins never move or change; a new coin is a whole prize, restores the respins, and a
                    // respin without one uses one up.
                    var arrived = 0;
                    for (var cell = 0; cell < Cells; cell++)
                    {
                        if (before[cell] > 0)
                        {
                            Assert.Equal(before[cell], after[cell]);
                        }
                        else if (after[cell] > 0)
                        {
                            Assert.Equal(0, after[cell] % Bet);
                            arrived++;
                        }
                    }

                    Assert.Equal(-1, columns[step]);
                    Assert.Equal(arrived > 0 ? Respins : left[step - 1] - 1, left[step]);
                    pullsOfColumn = 0;
                    continue;
                }

                // A pull changes the three coins of one full column by its kind and nothing else, and a column is
                // boosted once, with at most seven pulls.
                var (add, times, ends) = Pulls[events[step]];
                var column = columns[step];
                Assert.Equal(left[step - 1], left[step]);
                if (events[step - 1] == "respin" || events[step - 1] == "start" || columns[step - 1] != column)
                {
                    Assert.True(boosted.Add(column), "a column was boosted twice");
                    pullsOfColumn = 0;
                }

                pullsOfColumn++;
                for (var cell = 0; cell < Cells; cell++)
                {
                    Assert.True(cell / Rows != column || before[cell] > 0);
                    Assert.Equal(cell / Rows == column ? (before[cell] + add) * times : before[cell], after[cell]);
                }

                var last = step + 1 == events.Length || events[step + 1] == "respin" || columns[step + 1] != column;
                Assert.Equal(ends || pullsOfColumn == MaxPulls, last);
            }

            // The feature ends out of respins or with a full board, every full column has had its boost, and it
            // pays the coins it holds.
            var final = held[^1];
            Assert.True(left[^1] == 0 || final.All(coin => coin > 0));
            Assert.Equal(Enumerable.Range(0, Cells / Rows)
                .Where(column => final.Skip(column * Rows).Take(Rows).All(coin => coin > 0))
                .Select(column => (long)column), boosted.Order());
            Assert.Equal(new Rational(final.Sum(), 1), bonus);
            boostedColumns += boosted.Count;
        }

        output.WriteLine($"{featureRounds} rounds with Hold & Spin, {boostedColumns} boosted columns");
        Assert.True(featureRounds >= 30 && boostedColumns >= 30, $"{featureRounds} features, {boostedColumns} boosts");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmallGame_EveryExactInterpreterGivesTheSameRationals(bool buy)
    {
        Variant variant = Game(buy);
        GameGraph game = variant.Small;
        // Within its branch budget the hybrid evaluator is the classic interpreter: every outcome is visited.
        var sw = Stopwatch.StartNew();
        RegimeResult hybrid = game.Hybrid(2_000_000, 10_000, Seed);
        Assert.Equal(EvaluationStrategy.Exact, hybrid.OverallStrategy);
        SlotMathReport classic = hybrid.Report;
        Assert.Equal(Provenance.Exact, classic.Rtp.Provenance.Provenance);
        Rational rtp = GameGraph.Exact(classic.Rtp);
        Assert.Equal(variant.SmallRtp, rtp);
        Assert.Equal(variant.SmallHitFrequency.ToDouble(), classic.HitFrequency.DisplayValue, 12);

        output.WriteLine($"TIMING classic {sw.Elapsed.TotalSeconds:F1}");
        sw.Restart();
        StructuredExactResult structured = game.Structured();
        output.WriteLine($"TIMING structured {sw.Elapsed.TotalSeconds:F1}");
        sw.Restart();
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        Assert.True(structured.PrunedMass.IsZero);
        Assert.Equal(rtp, GameGraph.Exact(structured.Rtp(game.WinScale)));
        Assert.Equal(variant.SmallHitFrequency, structured.HitFrequency);
        // The cap of this cut-down game is a rule that cuts real wins: the largest win is exactly the cap.
        Assert.Equal(game.WinCap * game.WinScale, structured.Distribution!.Entries.Max(entry => entry.Value));

        // Pruning drops, at every draw, the branches rarer than epsilon and has to say how much it dropped. Rarer
        // than 1 in 5 are the wild (1 in 10) and the multiplying pull (1 in 90); with the declared win cap the
        // result is an enclosure of the exact value (D5).
        SlotMathReport pruned = game.Enumerate(new Rational(1, 5));
        ProvenanceTag enclosure = pruned.Rtp.Provenance;
        Assert.Equal(Provenance.ExactInterval, enclosure.Provenance);
        Assert.Equal(BoundSource.DeclaredWinCap, enclosure.BoundSource);
        Assert.InRange(enclosure.PrunedMass!.Value, 0.01, 0.4);
        Assert.True(enclosure.Lo < rtp.ToDouble() && rtp.ToDouble() < enclosure.Hi);
        output.WriteLine($"TIMING rest {sw.Elapsed.TotalSeconds:F1}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmallGame_SamplingAgreesWithTheExactValue_AndRejectsItShiftedByOnePercent(bool buy)
    {
        long rounds = buy ? 500_000 : 2_000_000;
        Variant variant = Game(buy);
        var exact = variant.SmallRtp.ToDouble();
        SampledResult<Dict> sampled = variant.Small.Sample(rounds, Seed);
        Assert.Equal(rounds, sampled.SpinsCompleted);
        output.WriteLine(
            $"exact {exact} sampled {sampled.Stats.Mean} ± {sampled.Stats.StdErr}, cap hits {sampled.Stats.CapHits}");
        Assert.True(FourSigma.Accepts(exact, sampled.Stats.Mean, sampled.Stats.StdErr));
        Assert.False(FourSigma.Accepts(exact * 1.01, sampled.Stats.Mean, sampled.Stats.StdErr));
        Assert.False(FourSigma.Accepts(exact * 0.99, sampled.Stats.Mean, sampled.Stats.StdErr));
        var hits = variant.SmallHitFrequency.ToDouble();
        if (buy)
        {
            // The bought bonus always pays: it starts with coins held.
            Assert.Equal(hits, sampled.Stats.HitFrequency);
            return;
        }

        Assert.True(FourSigma.Accepts(hits, sampled.Stats.HitFrequency, sampled.Stats.HitFrequencyStdErr));
        Assert.False(FourSigma.Accepts(hits * 0.99, sampled.Stats.HitFrequency, sampled.Stats.HitFrequencyStdErr));
    }

    // The round cap of 7777 bets is a rule of the game that a win can reach, so the exact RTP needs the whole law
    // of the round's win and not only its expectation by linearity, and that law is beyond the structured evaluator
    // for fifteen independent cells and a Hold & Spin. It has to say so within its budget, and never return a number.
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

    // Without an exact value the full game is held against the original directly: tens of millions of rounds of
    // the original state machine and as many of the graph are two independent estimates of the same RTP.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullGame_SamplingAgreesWithTheOriginalGame_AndRejectsAOnePercentShift(bool buy)
    {
        // The paid round is far more volatile than the bought bonus measured against its price.
        long rounds = buy ? 1_000_000 : 40_000_000;
        Variant variant = Game(buy);
        using JsonDocument golden = variant.Golden();
        JsonElement original = golden.RootElement.GetProperty("simulation");
        var observed = original.GetProperty("rtp").GetDouble();
        var originalRounds = original.GetProperty("rounds").GetInt64();

        SampledResult<Dict> sampled = variant.Full.Sample(rounds, Seed, 8);
        Assert.Equal(rounds, sampled.SpinsCompleted);
        var stdErr = System.Math.Sqrt(System.Math.Pow(sampled.Stats.StdErr, 2) +
                                      System.Math.Pow(original.GetProperty("stdErr").GetDouble(), 2));
        output.WriteLine(
            $"graph {sampled.Stats.Mean} ± {sampled.Stats.StdErr} over {rounds} rounds, original {observed} ± " +
            $"{original.GetProperty("stdErr").GetDouble()} over {originalRounds} rounds " +
            $"({(sampled.Stats.Mean - observed) / stdErr:F2} sigma), cap hits {sampled.Stats.CapHits}");
        Assert.True(FourSigma.Accepts(observed, sampled.Stats.Mean, stdErr));
        Assert.False(FourSigma.Accepts(observed * 1.01, sampled.Stats.Mean, stdErr));
        Assert.False(FourSigma.Accepts(observed * 0.99, sampled.Stats.Mean, stdErr));

        var hits = original.GetProperty("hitFrequency").GetDouble();
        if (buy)
        {
            Assert.Equal(1, hits);
            Assert.Equal(1, sampled.Stats.HitFrequency);
            return;
        }

        var hitStdErr = System.Math.Sqrt(System.Math.Pow(sampled.Stats.HitFrequencyStdErr, 2) +
                                         hits * (1 - hits) / originalRounds);
        output.WriteLine($"hit frequency: graph {sampled.Stats.HitFrequency}, original {hits}");
        Assert.True(FourSigma.Accepts(hits, sampled.Stats.HitFrequency, hitStdErr));
        Assert.False(FourSigma.Accepts(hits * 1.01, sampled.Stats.HitFrequency, hitStdErr));
        Assert.False(FourSigma.Accepts(hits * 0.99, sampled.Stats.HitFrequency, hitStdErr));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullGame_SamplingIsBitIdentical_AcrossWorkersAndTiers(bool buy)
    {
        long rounds = buy ? 30_000 : 300_000;
        GameGraph game = Game(buy).Full;
        game.AssertSamplingIsBitIdentical(rounds, Seed);
    }

    // Beyond its branch budget the hybrid evaluator has to fall back to sampling and say so in the provenance.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullGame_Hybrid_FallsBackToSampling_AndSaysSo(bool buy)
    {
        long rounds = buy ? 50_000 : 500_000;
        Variant variant = Game(buy);
        using JsonDocument golden = variant.Golden();
        var observed = golden.RootElement.GetProperty("simulation").GetProperty("rtp").GetDouble();
        RegimeResult hybrid = variant.Full.Hybrid(GameGraph.FallbackBranches, rounds, Seed);
        Assert.Equal(EvaluationStrategy.Sampled, hybrid.OverallStrategy);
        Assert.Equal(Provenance.Sampled, hybrid.Report.Rtp.Provenance.Provenance);
        Assert.Equal(rounds, hybrid.Report.Rtp.SampleCount);
        Assert.InRange(hybrid.Report.Rtp.DisplayValue, observed * 0.9, observed * 1.1);
    }

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
