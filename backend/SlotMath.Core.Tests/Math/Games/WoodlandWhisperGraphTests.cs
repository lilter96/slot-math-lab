using System.Text.Json;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Math.Structured;
using Xunit.Abstractions;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Math.Games;

/// <summary>
///     Woodland Whisper, migrated from tgslots as a graph of subgraphs, held against the original game and against
///     every interpreter of the kernel. The game is two graphs: the paid round and the bought feature, whose round
///     costs a hundred bets. The full graphs are too large to enumerate, so the interpreters that visit every
///     outcome run on cut-down models built by the same graph code (five lines, tiny reels, two kinds of cards and
///     two free spins).
/// </summary>
public class WoodlandWhisperGraphTests(ITestOutputHelper output)
{
    private const long Seed = 20260511;

    /// <summary>The RTP of the paid round that the original game publishes, worked out analytically (parsheet.json).</summary>
    private const double PublishedRtp = 0.9600060932273558;

    // The exact hit frequency, variance and distribution of the full game are beyond the structured evaluator, and
    // looking for them takes minutes; the expectation is what the full game is checked on.
    private static readonly StructuredExactOptions ExpectationOnly = new()
    {
        DistributionWork = 0, HitFrequencyWork = 0, VarianceWork = 0
    };

    private static readonly Lazy<Variant> Paid = new(() => new Variant("woodland-whisper", "trigger-yes/play",
        new Rational(540905, 98304), new Rational(15, 16), 0.9600060896758569));

    private static readonly Lazy<Variant> Bought = new(() => new Variant("woodland-whisper.buy", "feature/play",
        new Rational(2388109, 18432000), Rational.One, 0.2812458067293919));

    private static Variant Game(bool buy) => (buy ? Bought : Paid).Value;

    private static Rational Sum(object? values) =>
        GameGraph.Items(values).Aggregate(Rational.Zero, (total, value) => total + GameGraph.Number(value));

    // The rounds were played by the original TypeScript state machine. Each lists the outcome of every random
    // choice it made, in the order the graph draws them, and what the original showed, paid and awarded for every
    // spin. Half of them were chosen for their feature: paid rounds that reached it, bought ones that retriggered it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalRounds_ReplayedDrawForDraw_ShowTheSameBoardsPayAndAwardTheSame(bool buy)
    {
        Variant variant = Game(buy);
        using JsonDocument golden = variant.Golden();
        var roundCost = golden.RootElement.GetProperty("roundCost").GetInt32();
        int rounds = 0, chosen = 0, spins = 0;
        foreach (JsonElement round in golden.RootElement.GetProperty("rounds").EnumerateArray())
        {
            (Rational win, Dict state) =
                variant.Full.Replay(round.GetProperty("draws").EnumerateArray().Select(d => d.GetInt32()));
            JsonElement[] expected = [.. round.GetProperty("spins").EnumerateArray()];
            var boards = GameGraph.Items(state["spinBoards"]);
            var wins = GameGraph.Items(state["spinWins"]);
            var scatters = GameGraph.Items(state["spinScatters"]);
            Assert.Equal(expected.Length, boards.Length);
            var awards = new List<(Rational Spin, Rational Award)>();
            for (var spin = 0; spin < expected.Length; spin++)
            {
                Assert.Equal(expected[spin].GetProperty("board").EnumerateArray().Select(cell => cell.GetInt32()),
                    GameGraph.Items(boards[spin]).Select(cell => int.Parse(GameGraph.Text(cell))));
                Assert.Equal(new Rational(expected[spin].GetProperty("win").GetInt64(), 1),
                    GameGraph.Number(wins[spin]));
                Assert.Equal(new Rational(expected[spin].GetProperty("scatters").GetInt64(), 1),
                    GameGraph.Number(scatters[spin]));
                if (expected[spin].GetProperty("award").GetInt64() is var award and > 0)
                {
                    awards.Add((new Rational(spin + 1, 1), new Rational(award, 1)));
                }
            }

            // Every pick bonus: after which spin it was played, and the free spins it awarded.
            Assert.Equal(awards, GameGraph.Items(state["pickSpins"]).Select(GameGraph.Number)
                .Zip(GameGraph.Items(state["pickAwards"]).Select(GameGraph.Number)));
            Assert.Equal(new Rational(round.GetProperty("win").GetInt64(), roundCost), win);
            rounds++;
            spins += expected.Length;
            chosen += awards.Count > (buy ? 1 : 0) ? 1 : 0;
        }

        output.WriteLine($"{rounds} rounds, {chosen} with {(buy ? "a retrigger" : "the feature")}, {spins} spins");
        Assert.True(rounds >= 60 && chosen >= 30);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void PlayedRound_TraceAccountsForTheWin_AndTracingDoesNotChangeIt(bool buy, bool compiledTier)
    {
        GameGraph game = Game(buy).Full;
        var featureRounds = 0;
        for (var round = 0; round < (buy ? 300 : 3_000); round++)
        {
            (Rational win, Dict state) = game.Play(Seed, round, true, compiledTier);
            Rational roundCost = GameGraph.Number(state["roundCost"]);
            Assert.Equal(win * roundCost, Sum(state["spinWins"]));
            Assert.Equal(win * roundCost, GameGraph.Number(state["roundCoins"]));
            Assert.Equal(win, game.Play(Seed, round, false, compiledTier).Win);
            // Both tiers draw from the same stream in the same order, so they play the same round.
            Assert.Equal(win, game.Play(Seed, round, true, !compiledTier).Win);

            // Every free spin a pick bonus awarded was played: one opening spin, then the awards.
            var played = GameGraph.Items(state["spinWins"]).Length;
            Assert.Equal(new Rational(played - 1, 1), Sum(state["pickAwards"]));
            Assert.Equal(Rational.Zero, GameGraph.Number(state["freeSpins"]));
            // No loop is cut short by its limit: the pick bonus finds its pair and the free spins run out.
            Dictionary<string, string> exits = GameGraph.LoopExits(state);
            Assert.DoesNotContain("modelLimit", exits.Values);
            featureRounds += played > 1 && exits.Count > 0 ? 1 : 0;
        }

        Assert.True(featureRounds >= 10, $"only {featureRounds} rounds reached the free spins");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmallGame_EveryExactInterpreterGivesTheSameRationals(bool buy)
    {
        Variant variant = Game(buy);
        GameGraph game = variant.Small;
        SlotMathReport classic = game.Enumerate();
        Assert.Equal(Provenance.Exact, classic.Rtp.Provenance.Provenance);
        Rational rtp = GameGraph.Exact(classic.Rtp);
        Assert.Equal(variant.SmallRtp, rtp);
        Assert.Equal(variant.SmallHitFrequency.ToDouble(), classic.HitFrequency.DisplayValue, 12);

        StructuredExactResult structured = game.Structured();
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        Assert.True(structured.PrunedMass.IsZero);
        Assert.Equal(rtp, GameGraph.Exact(structured.Rtp(game.WinScale)));
        Assert.Equal(variant.SmallHitFrequency, structured.HitFrequency);

        RegimeResult hybrid = game.Hybrid(2_000_000, 10_000, Seed);
        Assert.Equal(EvaluationStrategy.Exact, hybrid.OverallStrategy);
        Assert.Equal(rtp, GameGraph.Exact(hybrid.Report.Rtp));

        // Pruning drops branches rarer than epsilon and has to say how much it dropped. Only the rare mystery symbol
        // (1 in 10) is rarer than 1 in 5; with the declared win cap the result is an enclosure of the exact value (D5).
        SlotMathReport pruned = game.Enumerate(new Rational(1, 5));
        ProvenanceTag enclosure = pruned.Rtp.Provenance;
        Assert.Equal(Provenance.ExactInterval, enclosure.Provenance);
        Assert.Equal(BoundSource.DeclaredWinCap, enclosure.BoundSource);
        Assert.InRange(enclosure.PrunedMass!.Value, 0.1, 0.3);
        Assert.True(enclosure.Lo < rtp.ToDouble() && rtp.ToDouble() < enclosure.Hi);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void SmallGame_SamplingAgreesWithTheExactValue_AndRejectsItShiftedByOnePercent(bool buy, bool compiledTier)
    {
        long rounds = buy ? 300_000 : 1_000_000;
        Variant variant = Game(buy);
        var exact = variant.SmallRtp.ToDouble();
        SampledResult<Dict> sampled = variant.Small.Sample(rounds, Seed, 4, compiledTier);
        Assert.Equal(rounds, sampled.SpinsCompleted);
        output.WriteLine($"exact {exact} sampled {sampled.Stats.Mean} ± {sampled.Stats.StdErr}");
        Assert.True(FourSigma.Accepts(exact, sampled.Stats.Mean, sampled.Stats.StdErr));
        Assert.False(FourSigma.Accepts(exact * 1.01, sampled.Stats.Mean, sampled.Stats.StdErr));
        Assert.False(FourSigma.Accepts(exact * 0.99, sampled.Stats.Mean, sampled.Stats.StdErr));
        var hits = variant.SmallHitFrequency.ToDouble();
        if (buy)
        {
            // The bought feature always pays: its opening spin shows three scatters.
            Assert.Equal(hits, sampled.Stats.HitFrequency);
            return;
        }

        Assert.True(FourSigma.Accepts(hits, sampled.Stats.HitFrequency, sampled.Stats.HitFrequencyStdErr));
        Assert.False(FourSigma.Accepts(hits * 0.99, sampled.Stats.HitFrequency, sampled.Stats.HitFrequencyStdErr));
    }

    // The full round, pick bonuses, free spins and retriggers included, has an exact RTP although it cannot be
    // enumerated. The original game never ends its free spins early and the graph has to cap them, at 150: the exact
    // expected number of rounds the cap cuts short says how little that changes.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullGame_StructuredExactRtp_AgreesWithTheOriginalGame_AndAShiftedValueDoesNot(bool buy)
    {
        Variant variant = Game(buy);
        StructuredExactResult result = variant.Exact;
        output.WriteLine($"succeeded={result.Succeeded} declined={result.DeclinedReason} " +
                         $"elapsed={result.Elapsed.TotalMilliseconds:F0}ms work={result.Work} laws={result.Laws}");
        Assert.True(result.Succeeded, result.DeclinedReason);
        Assert.True(result.PrunedMass.IsZero);
        RtpMetric rtp = result.Rtp(variant.Full.WinScale);
        Assert.Equal(Provenance.Exact, rtp.Provenance.Provenance);
        var exact = GameGraph.Exact(rtp).ToDouble();
        Assert.Equal(variant.FullRtp, exact, 14);

        StructuredLoopResult freeSpins = Assert.Single(result.Loops, loop => loop.LoopId == variant.FreeSpinsLoop);
        Assert.Equal(150, freeSpins.IterationCap);
        var cut = freeSpins.ExpectedCapHits!.Value.ToDouble();
        output.WriteLine($"rounds cut short by the free-spin cap: {cut:E3}");
        Assert.InRange(cut, 0, buy ? 1e-7 : 1e-9);
        if (!buy)
        {
            // The published value has no cap, so it is the larger one, by what the cut rounds would still have paid.
            Assert.InRange(PublishedRtp - exact, 0, 1e-8);
        }

        using JsonDocument golden = variant.Golden();
        JsonElement original = golden.RootElement.GetProperty("simulation");
        var observed = original.GetProperty("rtp").GetDouble();
        var stdErr = original.GetProperty("stdErr").GetDouble();
        output.WriteLine($"exact {exact} original {observed} ± {stdErr} ({(observed - exact) / stdErr:F2} sigma) " +
                         $"over {original.GetProperty("rounds").GetInt64()} rounds");
        Assert.True(FourSigma.Accepts(exact, observed, stdErr));
        Assert.False(FourSigma.Accepts(exact * 1.01, observed, stdErr));
        Assert.False(FourSigma.Accepts(exact * 0.99, observed, stdErr));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullGame_SamplingAgreesWithTheExactRtp_AndRejectsItShiftedByOnePercent(bool buy)
    {
        // The paid round is far more volatile than the bought feature measured against its price.
        long rounds = buy ? 1_000_000 : 40_000_000;
        Variant variant = Game(buy);
        Assert.True(variant.Exact.Succeeded, variant.Exact.DeclinedReason);
        var exact = GameGraph.Exact(variant.Exact.Rtp(variant.Full.WinScale)).ToDouble();
        SampledResult<Dict> sampled = variant.Full.Sample(rounds, Seed, 8);
        Assert.Equal(rounds, sampled.SpinsCompleted);
        Assert.Equal(0, sampled.Stats.CapHits);
        output.WriteLine(
            $"exact {exact} sampled {sampled.Stats.Mean} ± {sampled.Stats.StdErr}, hit {sampled.Stats.HitFrequency}");
        Assert.True(FourSigma.Accepts(exact, sampled.Stats.Mean, sampled.Stats.StdErr));
        Assert.False(FourSigma.Accepts(exact * 1.01, sampled.Stats.Mean, sampled.Stats.StdErr));
        Assert.False(FourSigma.Accepts(exact * 0.99, sampled.Stats.Mean, sampled.Stats.StdErr));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullGame_SamplingIsBitIdentical_AcrossWorkersAndTiers(bool buy)
    {
        long rounds = buy ? 30_000 : 300_000;
        GameGraph game = Game(buy).Full;
        SampledResult<Dict> one = game.Sample(rounds, Seed, 1);
        SampledResult<Dict> eight = game.Sample(rounds, Seed, 8);
        SampledResult<Dict> reference = game.Sample(rounds, Seed, 4, false);
        foreach (SampledResult<Dict> other in new[] { eight, reference })
        {
            Assert.Equal(one.Stats.Mean, other.Stats.Mean);
            Assert.Equal(one.Stats.Variance, other.Stats.Variance);
            Assert.Equal(one.Stats.NonZeroCount, other.Stats.NonZeroCount);
            Assert.Equal(one.Stats.MaxObserved, other.Stats.MaxObserved);
        }
    }

    // Beyond its branch budget the hybrid evaluator has to fall back to sampling and say so in the provenance.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullGame_Hybrid_FallsBackToSampling_AndSaysSo(bool buy)
    {
        long rounds = buy ? 50_000 : 500_000;
        Variant variant = Game(buy);
        RegimeResult hybrid = variant.Full.Hybrid(100_000, rounds, Seed);
        Assert.Equal(EvaluationStrategy.Sampled, hybrid.OverallStrategy);
        Assert.Equal(Provenance.Sampled, hybrid.Report.Rtp.Provenance.Provenance);
        Assert.Equal(rounds, hybrid.Report.Rtp.SampleCount);
        Assert.InRange(hybrid.Report.Rtp.DisplayValue, variant.FullRtp * 0.9, variant.FullRtp * 1.1);
    }

    /// <summary>One of the game's two graphs with its cut-down model and what is known about both.</summary>
    private sealed class Variant(
        string name,
        string freeSpinsLoop,
        Rational smallRtp,
        Rational smallHitFrequency,
        double fullRtp)
    {
        public GameGraph Full { get; } = GameGraph.Load($"{name}.json");

        public GameGraph Small { get; } = GameGraph.Load($"{name}.small.json");

        /// <summary>The loop that plays the free spins, as the inlined graph names it.</summary>
        public string FreeSpinsLoop => freeSpinsLoop;

        /// <summary>The cut-down game's RTP and hit frequency, worked out by the classic interpreter.</summary>
        public Rational SmallRtp => smallRtp;

        public Rational SmallHitFrequency => smallHitFrequency;

        /// <summary>The exact RTP of the full graph, as the structured evaluator gives it.</summary>
        public double FullRtp => fullRtp;

        public StructuredExactResult Exact => field ??= Full.Structured(ExpectationOnly);

        public JsonDocument Golden() => GameGraph.Golden($"{name}.golden.json");
    }
}
