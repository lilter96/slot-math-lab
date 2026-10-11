using System.Text.Json;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Math.Structured;
using Xunit.Abstractions;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Math.Games;

/// <summary>
///     Ancient Dragon, migrated from tgslots as a graph of subgraphs, held against the original game and against
///     every interpreter of the kernel. The full game is too large to enumerate, so the interpreters that visit
///     every outcome run on a cut-down model built by the same graph code (five lines, tiny reels, two free spins).
/// </summary>
public class AncientDragonGraphTests(ITestOutputHelper output)
{
    private const long Seed = 20260511;

    /// <summary>The cut-down game, small enough to visit every outcome: its RTP worked out by the classic interpreter.</summary>
    private static readonly Rational SmallRtp = new(17689, 3200);

    private static readonly Lazy<GameGraph> Full = new(() => GameGraph.Load("ancient-dragon.json"));
    private static readonly Lazy<GameGraph> Small = new(() => GameGraph.Load("ancient-dragon.small.json"));
    private static readonly Lazy<StructuredExactResult> FullExact = new(() => Full.Value.Structured());

    private static Rational Sum(object? values) =>
        GameGraph.Items(values).Aggregate(Rational.Zero, (total, value) => total + GameGraph.Number(value));

    // The rounds were played by the original TypeScript state machine. Each lists the outcome of every random
    // choice it made, in the order the graph draws them, and what the original showed and paid for every spin.
    [Fact]
    public void OriginalRounds_ReplayedDrawForDraw_ShowTheSameBoardsAndPayTheSame()
    {
        GameGraph game = Full.Value;
        using JsonDocument golden = GameGraph.Golden("ancient-dragon.golden.json");
        var totalBet = golden.RootElement.GetProperty("totalBet").GetInt32();
        int rounds = 0, featureRounds = 0, spins = 0;
        foreach (JsonElement round in golden.RootElement.GetProperty("rounds").EnumerateArray())
        {
            (Rational win, Dict state) =
                game.Replay(round.GetProperty("draws").EnumerateArray().Select(d => d.GetInt32()));
            JsonElement[] expected = [.. round.GetProperty("spins").EnumerateArray()];
            var boards = GameGraph.Items(state["spinBoards"]);
            var wins = GameGraph.Items(state["spinWins"]);
            var scatters = GameGraph.Items(state["spinScatters"]);
            Assert.Equal(expected.Length, boards.Length);
            for (var spin = 0; spin < expected.Length; spin++)
            {
                Assert.Equal(expected[spin].GetProperty("board").EnumerateArray().Select(cell => cell.GetInt32()),
                    GameGraph.Items(boards[spin]).Select(cell => int.Parse(GameGraph.Text(cell))));
                Assert.Equal(new Rational(expected[spin].GetProperty("win").GetInt64(), 1),
                    GameGraph.Number(wins[spin]));
                Assert.Equal(new Rational(expected[spin].GetProperty("scatters").GetInt64(), 1),
                    GameGraph.Number(scatters[spin]));
            }

            Assert.Equal(new Rational(round.GetProperty("win").GetInt64(), totalBet), win);
            rounds++;
            spins += expected.Length;
            featureRounds += expected.Length > 1 ? 1 : 0;
        }

        output.WriteLine($"{rounds} rounds, {featureRounds} with free spins, {spins} spins");
        Assert.True(rounds >= 60 && featureRounds >= 30);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PlayedRound_TraceAccountsForTheWin_AndTracingDoesNotChangeIt(bool compiledTier)
    {
        GameGraph game = Full.Value;
        var featureRounds = 0;
        for (var round = 0; round < 3_000; round++)
        {
            (Rational win, Dict state) = game.Play(Seed, round, true, compiledTier);
            Rational totalBet = GameGraph.Number(state["totalBet"]);
            Assert.Equal(win * totalBet, Sum(state["spinWins"]));
            Assert.Equal(win * totalBet, GameGraph.Number(state["roundCoins"]));
            Assert.Equal(win, game.Play(Seed, round, false, compiledTier).Win);
            // Both tiers draw from the same stream in the same order, so they play the same round.
            Assert.Equal(win, game.Play(Seed, round, true, !compiledTier).Win);

            var left = GameGraph.Items(state["spinFreeSpinsLeft"]);
            Assert.Equal(Rational.Zero, GameGraph.Number(left[^1]));
            // The free spins run out by the rules and are never cut short by the limit of their loop.
            Dictionary<string, string> exits = GameGraph.LoopExits(state);
            Assert.DoesNotContain("modelLimit", exits.Values);
            featureRounds += left.Length > 1 && exits.Count > 0 ? 1 : 0;
        }

        Assert.True(featureRounds >= 10, $"only {featureRounds} of 3000 rounds reached the free spins");
    }

    [Fact]
    public void SmallGame_EveryExactInterpreterGivesTheSameRationals()
    {
        GameGraph game = Small.Value;
        // Within its branch budget the hybrid evaluator is the classic interpreter: every outcome is visited.
        RegimeResult hybrid = game.Hybrid(2_000_000, 10_000, Seed);
        Assert.Equal(EvaluationStrategy.Exact, hybrid.OverallStrategy);
        SlotMathReport classic = hybrid.Report;
        Assert.Equal(Provenance.Exact, classic.Rtp.Provenance.Provenance);
        Rational rtp = GameGraph.Exact(classic.Rtp);
        Assert.Equal(SmallRtp, rtp);
        Assert.Equal(15.0 / 16, classic.HitFrequency.DisplayValue, 12);

        StructuredExactResult structured = game.Structured();
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        Assert.True(structured.PrunedMass.IsZero);
        Assert.Equal(rtp, GameGraph.Exact(structured.Rtp(game.WinScale)));
        Assert.Equal(new Rational(15, 16), structured.HitFrequency);

        // Pruning drops, at every draw, the branches rarer than epsilon and has to say how much it dropped. Only the
        // rare mystery symbol (1 in 10) is rarer than 1 in 5, and it is dropped on every spin that is reached: the
        // paid spin and both free spins of the 3 rounds in 8 that trigger them. With the declared win cap the
        // result is an enclosure of the exact value (D5).
        SlotMathReport pruned = game.Enumerate(new Rational(1, 5));
        ProvenanceTag enclosure = pruned.Rtp.Provenance;
        Assert.Equal(Provenance.ExactInterval, enclosure.Provenance);
        Assert.Equal(BoundSource.DeclaredWinCap, enclosure.BoundSource);
        Assert.Equal(0.1 + 0.9 * (3.0 / 8) * (0.1 + 0.9 * 0.1), enclosure.PrunedMass!.Value, 12);
        Assert.InRange(rtp.ToDouble(), enclosure.Lo!.Value, enclosure.Hi!.Value);
        Assert.True(enclosure.Lo < rtp.ToDouble() && rtp.ToDouble() < enclosure.Hi);
    }

    [Fact]
    public void SmallGame_SamplingAgreesWithTheExactValue_AndRejectsItShiftedByOnePercent()
    {
        const long rounds = 500_000;
        GameGraph game = Small.Value;
        var exact = SmallRtp.ToDouble();
        SampledResult<Dict> sampled = game.Sample(rounds, Seed);
        Assert.Equal(rounds, sampled.SpinsCompleted);
        output.WriteLine($"exact {exact} sampled {sampled.Stats.Mean} ± {sampled.Stats.StdErr}");
        Assert.True(FourSigma.Accepts(exact, sampled.Stats.Mean, sampled.Stats.StdErr));
        Assert.False(FourSigma.Accepts(exact * 1.01, sampled.Stats.Mean, sampled.Stats.StdErr));
        Assert.False(FourSigma.Accepts(exact * 0.99, sampled.Stats.Mean, sampled.Stats.StdErr));
        Assert.True(FourSigma.Accepts(15.0 / 16, sampled.Stats.HitFrequency, sampled.Stats.HitFrequencyStdErr));
        Assert.False(FourSigma.Accepts(15.0 / 16 * 0.99, sampled.Stats.HitFrequency, sampled.Stats.HitFrequencyStdErr));
    }

    // The full round, free spins and retriggers included, has an exact RTP although it cannot be enumerated.
    // The original game publishes no exact value, so the exact RTP is held against ten million rounds of the
    // original TypeScript game, and the same gate must reject the exact value moved by one percent.
    [Fact]
    public void FullGame_StructuredExactRtp_AgreesWithTheOriginalGame_AndAShiftedValueDoesNot()
    {
        StructuredExactResult result = FullExact.Value;
        output.WriteLine($"succeeded={result.Succeeded} declined={result.DeclinedReason} " +
                         $"elapsed={result.Elapsed.TotalMilliseconds:F0}ms work={result.Work} laws={result.Laws}");
        Assert.True(result.Succeeded, result.DeclinedReason);
        Assert.True(result.PrunedMass.IsZero);
        RtpMetric rtp = result.Rtp(Full.Value.WinScale);
        Assert.Equal(Provenance.Exact, rtp.Provenance.Provenance);
        var exact = GameGraph.Exact(rtp).ToDouble();
        Assert.Equal(0.8887462865893143, exact, 14);

        using JsonDocument golden = GameGraph.Golden("ancient-dragon.golden.json");
        JsonElement original = golden.RootElement.GetProperty("simulation");
        var observed = original.GetProperty("rtp").GetDouble();
        var stdErr = original.GetProperty("stdErr").GetDouble();
        Assert.Equal(10_000_000, original.GetProperty("rounds").GetInt64());
        output.WriteLine($"exact {exact} original {observed} ± {stdErr} ({(observed - exact) / stdErr:F2} sigma)");
        Assert.True(FourSigma.Accepts(exact, observed, stdErr));
        Assert.False(FourSigma.Accepts(exact * 1.01, observed, stdErr));
        Assert.False(FourSigma.Accepts(exact * 0.99, observed, stdErr));
    }

    [Fact]
    public void FullGame_SamplingAgreesWithTheExactRtp_AndRejectsItShiftedByOnePercent()
    {
        const long rounds = 6_000_000;
        Assert.True(FullExact.Value.Succeeded, FullExact.Value.DeclinedReason);
        var exact = GameGraph.Exact(FullExact.Value.Rtp(Full.Value.WinScale)).ToDouble();
        SampledResult<Dict> sampled = Full.Value.Sample(rounds, Seed);
        Assert.Equal(rounds, sampled.SpinsCompleted);
        Assert.Equal(0, sampled.Stats.CapHits);
        output.WriteLine(
            $"exact {exact} sampled {sampled.Stats.Mean} ± {sampled.Stats.StdErr}, hit {sampled.Stats.HitFrequency}");
        Assert.True(FourSigma.Accepts(exact, sampled.Stats.Mean, sampled.Stats.StdErr));
        Assert.False(FourSigma.Accepts(exact * 1.01, sampled.Stats.Mean, sampled.Stats.StdErr));
        Assert.False(FourSigma.Accepts(exact * 0.99, sampled.Stats.Mean, sampled.Stats.StdErr));
    }

    [Fact]
    public void FullGame_SamplingIsBitIdentical_AcrossWorkersAndTiers()
    {
        const long rounds = 300_000;
        GameGraph game = Full.Value;
        game.AssertSamplingIsBitIdentical(rounds, Seed);
    }

    // Beyond its branch budget the hybrid evaluator has to fall back to sampling and say so in the provenance.
    [Fact]
    public void FullGame_Hybrid_FallsBackToSampling_AndSaysSo()
    {
        RegimeResult hybrid = Full.Value.Hybrid(GameGraph.FallbackBranches, 500_000, Seed);
        Assert.Equal(EvaluationStrategy.Sampled, hybrid.OverallStrategy);
        Assert.Equal(Provenance.Sampled, hybrid.Report.Rtp.Provenance.Provenance);
        Assert.Equal(500_000, hybrid.Report.Rtp.SampleCount);
        Assert.InRange(hybrid.Report.Rtp.DisplayValue, 0.8, 1.0);
    }
}
