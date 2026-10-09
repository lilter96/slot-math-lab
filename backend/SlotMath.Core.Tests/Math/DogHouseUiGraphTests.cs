using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Math;

public class DogHouseUiGraphTests
{
    private static GraphConfig Fixture(string file) => JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "DogHouse", file)), JsonOptions.Default)!;
    private static CompileResult Compile(GraphConfig config)
    {
        var result = new GraphCompiler().Compile(config);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.NodeId}: {e.Message}")));
        return result;
    }
    private static Rational Number(object? value) => value switch { BigInteger i => new(i, 1), ExprValue v => new(v.NumberNumerator, v.NumberDenominator), _ => throw new InvalidOperationException() };

    [Fact]
    public void RationalExpectationGraph_MatchesIndependentReference()
    {
        var proof = Compile(Fixture("dog-house-expectation-ui.json"));
        var (_, state) = SampledInterpreter.RunSingle(proof.Program!, new Dict(), 42);
        Assert.Equal(new Rational(21163, 39200), Number(state["baseRtp"]));
        Assert.Equal(new Rational(3, 490), Number(state["triggerProbability"]));
        Assert.Equal(new Rational(3, 98), Number(state["scatterRtp"]));
        // Independent Fraction oracle, including calibration at all-×2 / all-×3 endpoints.
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "DogHouse", "dog-house-independent-expectation.json")));
        Rational Expected(string value) { var parts = value.Split('/'); return new Rational(BigInteger.Parse(parts[0]), BigInteger.Parse(parts[1])); }
        Assert.Equal(Expected(reference.RootElement.GetProperty("rationalRtp").GetString()!), Number(state["expectedRtp"]));
        Assert.Equal(new Rational(49, 50), Number(state["targetRtp"]));
        Assert.True((bool)state["targetMet"]!);
        Assert.True((bool)state["calibrationFeasible"]!);
        var calibration = reference.RootElement.GetProperty("calibration");
        Assert.Equal(Expected(calibration.GetProperty("rationalRtp2").GetString()!), Number(state["calibrationRtp2"]));
        Assert.Equal(Expected(calibration.GetProperty("rationalRtp3").GetString()!), Number(state["calibrationRtp3"]));
        Assert.Equal(Expected(calibration.GetProperty("rationalRequiredP3").GetString()!), Number(state["requiredP3"]));
        Assert.Equal(new Rational(calibration.GetProperty("weight2").GetInt64(), 1), Number(state["calibratedWeight2"]));
        Assert.Equal(new Rational(calibration.GetProperty("weight3").GetInt64(), 1), Number(state["calibratedWeight3"]));
        Assert.Equal(Number(state["baseRtp"]) + Number(state["scatterRtp"]) + Number(state["bonusRtp"]), Number(state["expectedRtp"]));
        var mass = Enumerable.Range(0, 28).Select(i => Number(state[$"bonus-pmf-{i}"])).Aggregate(Rational.Zero, (a, x) => a + x);
        Assert.Equal(Rational.One, mass);
    }

    [Fact]
    public void MiniGraph_FullPmfMatchesIndependentEnumerationAndExpectationGraph()
    {
        var config = Fixture("dog-house-mini-ui.json"); var compiled = Compile(config);
        var dist = ExactInterpreter.Evaluate(compiled.Program!, new Dict(), StateHasher.CanonicalHash).ValueDistribution();
        var expected = new Dictionary<BigInteger, int>();
        var strips = new[] { new[] {3,13}, new[] {2,3}, new[] {3,13}, new[] {2,13}, new[] {3,13} };
        var payout = config.InitialState!["linePaytable"].EnumerateArray().Select(x => int.Parse(x.GetString()!)).ToArray();
        for (var stops = 0; stops < 32; stops++) for (var mult = 0; mult < 8; mult++)
        {
            var board = Enumerable.Range(0, 15).Select(p => strips[p % 5][((stops >> (p % 5)) + p / 5) % 2]).ToArray();
            var factors = Enumerable.Range(0, 15).Select(p => p % 5 is > 0 and < 4 ? 2 + ((mult >> (p % 5 - 1)) & 1) : 0).ToArray();
            var coins = ManualPay(board, factors, payout, config.PaylineSets[0].Paylines.Select(x => x.Positions).ToArray());
            var value = new BigInteger(coins) * compiled.WinScale / 20;
            expected[value] = expected.GetValueOrDefault(value) + 1;
        }
        Assert.Equal(expected.Count, dist.Count);
        foreach (var entry in dist.Entries) Assert.Equal(new Rational(expected[entry.Value], 256), new Rational(entry.Numerator, dist.Denominator));
        var oracle = expected.Aggregate(Rational.Zero, (sum, x) => sum + new Rational(x.Key * x.Value, 256 * compiled.WinScale));
        var proof = Compile(Fixture("dog-house-mini-expectation-ui.json"));
        Assert.Equal(oracle, Number(SampledInterpreter.RunSingle(proof.Program!, new Dict(), 42).State["expectedRtp"]));
    }

    [Theory]
    [InlineData(98, true, true)]
    [InlineData(95, false, true)]
    [InlineData(105, false, false)]
    public void TargetCheck_UsesRationalDeltaAndRejectsUnreachableCalibration(int targetPercent, bool met, bool feasible)
    {
        var config = Fixture("dog-house-expectation-ui.json");
        var initial = new Dictionary<string, JsonElement>(config.InitialState!) { ["targetRtpPercent"] = JsonSerializer.SerializeToElement(targetPercent) };
        var proof = Compile(config with { InitialState = initial });
        var (_, state) = SampledInterpreter.RunSingle(proof.Program!, new Dict(), 42);
        Assert.Equal(new Rational(targetPercent, 100), Number(state["targetRtp"]));
        Assert.Equal(met, state["targetMet"]); Assert.Equal(feasible, state["calibrationFeasible"]);
        Assert.Equal(Number(state["expectedRtp"]) - Number(state["targetRtp"]), Number(state["targetDelta"]));
    }

    // Direct specification: select one target, walk the prefix, add only participating Wild factors.
    private static int ManualPay(int[] board, int[] factors, int[] payouts, int[][] lines)
    {
        var total = 0;
        foreach (var line in lines)
        {
            var positions = line.Select((row, col) => row * 5 + col).ToArray();
            var symbol = board[positions[0]];
            if (symbol <= 2) continue;
            var count = 0; var multiplier = 0;
            foreach (var p in positions) { if (board[p] != symbol && board[p] != 2) break; count++; if (board[p] == 2) multiplier += factors[p]; }
            if (count >= 3) total += payouts[(symbol - 1) * 3 + count - 3] * System.Math.Max(1, multiplier);
        }
        return total;
    }

    [Fact]
    public void FullUiGraph_BonusStickyTraceAndReplayMatchManualPayout()
    {
        var config = Fixture("dog-house-ui.json"); var compiled = Compile(config);
        var payouts = config.InitialState!["linePaytable"].EnumerateArray().Select(x => int.Parse(x.GetString()!)).ToArray();
        var lines = config.PaylineSets[0].Paylines.Select(x => x.Positions).ToArray();
        Dict? bonus = null; long bonusIndex = -1; BigInteger value = 0;
        for (long i = 0; i < 2000; i++)
        {
            var round = SampledInterpreter.RunSingle(compiled.Program!, new Dict { ["traceEnabled"] = true }, 42, i);
            if (((object?[])round.State["bonusGrid"]!).Length == 0) continue;
            bonus = round.State; bonusIndex = i; value = round.Value; break;
        }
        Assert.NotNull(bonus); Console.WriteLine($"Bonus replay: seed 42 / round {bonusIndex}");
        var boards = (object?[])bonus["boards"]!; var multipliers = (object?[])bonus["multiplierHistory"]!;
        var grid = ((object?[])bonus["bonusGrid"]!).Select(x => int.Parse((string)x!)).ToArray();
        Assert.Equal(9, grid.Length); Assert.All(grid, x => Assert.InRange(x, 1, 3)); Assert.Equal(grid.Sum() + 1, boards.Length);
        var coins = 100; // independent scatter rule: 5 total stakes = 100 line coins
        for (var i = 0; i < boards.Length; i++)
        {
            var board = ((object?[])boards[i]!).Select(x => int.Parse((string)x!)).ToArray();
            var factors = ((object?[])multipliers[i]!).Select(x => int.Parse((string)x!)).ToArray();
            coins += ManualPay(board, factors, payouts, lines);
            if (i <= 1) continue;
            var previous = (object?[])multipliers[i - 1]!;
            for (var p = 0; p < 15; p++) if ((string)previous[p]! != "0") { Assert.Equal(2, board[p]); Assert.Equal(int.Parse((string)previous[p]!), factors[p]); }
        }
        Assert.Equal(new BigInteger(coins) * compiled.WinScale / 20, value);
        var replay = SampledInterpreter.RunSingle(compiled.Program!, new Dict { ["traceEnabled"] = true }, 42, bonusIndex);
        Assert.Equal(value, replay.Value); Assert.Equal(StateHasher.CanonicalHash(bonus), StateHasher.CanonicalHash(replay.State));
        Assert.Equal(value, SampledInterpreter.RunSingle(compiled.Program!, new Dict(), 42, bonusIndex).Value); // trace cannot change math
    }

    [Fact]
    public void FullUiGraph_ParallelSamplesAreBitIdenticalAcrossWorkers()
    {
        var compiled = Compile(Fixture("dog-house-ui.json"));
        SampledResult<Dict> Run(int workers) => SampledInterpreter.Evaluate(compiled.Program!, new Dict(),
            new SampledConfig { Seed = 42, MaxSpins = 10000, ChunkSize = 4096, DegreeOfParallelism = workers, WinScale = (double)compiled.WinScale });
        var serial = Run(1); var parallel = Run(4);
        Assert.Equal(serial.SpinsCompleted, parallel.SpinsCompleted);
        Assert.Equal(serial.Stats.Mean, parallel.Stats.Mean); Assert.Equal(serial.Stats.Variance, parallel.Stats.Variance);
        Assert.Equal(serial.Stats.HitFrequency, parallel.Stats.HitFrequency);
    }

    [Fact]
    public void FractionalStatePayout_IsPreservedInAllInterpreters()
    {
        var config = Fixture("dog-house-ui.json") with { Nodes = [new ModifyStateNode { Id = "payout", OutputKey = "win", ExpressionId = "quarter", Outputs = new() { ["state"] = new() { Name = "state", Type = PortType.State } } },
            new MetricsSinkNode { Id = "sink", WinStateKey = "win", WinCap = 10, Inputs = new() { ["state"] = new() { Name = "state", Type = PortType.State } } }],
            Expressions = new() { ["quarter"] = new ConstantExpr { Kind = ConstantKind.Rational, Value = "1/4" } },
            Edges = [new Edge { Id = "edge", SourceNodeId = "payout", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" }] };
        var compiled = Compile(config); var run = SampledInterpreter.RunSingle(compiled.Program!, new Dict(), 42);
        Assert.Equal(new Rational(1, 4), new Rational(run.Value, compiled.WinScale));
        var exact = ExactInterpreter.Evaluate(compiled.Program!, new Dict(), StateHasher.CanonicalHash).ValueDistribution();
        Assert.Equal(0.25, ExactMetrics.Compute(exact, 10, winScale: compiled.WinScale).Rtp.DisplayValue);
    }
}
