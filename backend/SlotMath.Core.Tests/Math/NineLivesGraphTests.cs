using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Math;

public class NineLivesGraphTests
{
    private static GraphConfig Fixture(string name) => JsonSerializer.Deserialize<GraphConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "LabDemo", name)), JsonOptions.Default)!;

    [Theory]
    [InlineData("lab-demo.json")]
    [InlineData("lab-demo-buy.json")]
    public void ComposedGraph_CompilesWithoutMechanicDispatchAndReplays(string name)
    {
        GraphConfig config = Fixture(name);
        CompileResult compiled = new GraphCompiler().Compile(config);
        Assert.True(compiled.IsValid,
            string.Join("; ", compiled.Errors.Select(error => $"{error.NodeId}: {error.Message}")));
        Assert.Equal("compiled-state-plan-v1", compiled.SamplingEngine);
        GraphConfig flattened = SubgraphInliner.Inline(config).Config;
        Assert.All(flattened.Nodes,
            node => Assert.True(node is DrawNode or ModifyStateNode or GetStateNode or BranchNode or LoopNode
                or MetricsSinkNode));
        for (var index = 0; index < 5; index++)
        {
            (BigInteger Value, Dict State) run =
                SampledInterpreter.RunSingle(compiled.Program!, new Dict { ["traceEnabled"] = true }, 42, index);
            (BigInteger Value, Dict State) replay =
                SampledInterpreter.RunSingle(compiled.Program!, new Dict { ["traceEnabled"] = true }, 42, index);
            Assert.Equal(run.Value, replay.Value);
            Assert.Equal(StateHasher.CanonicalHash(run.State), StateHasher.CanonicalHash(replay.State));
            (BigInteger Value, Dict State) reference = SampledInterpreter.RunSingle(compiled.ReferenceProgram!,
                new Dict { ["traceEnabled"] = true }, 42, index);
            Assert.Equal(run.Value, reference.Value);
            Assert.Equal(StateHasher.CanonicalHash(run.State), StateHasher.CanonicalHash(reference.State));
            (BigInteger Value, Dict State) untraced =
                SampledInterpreter.RunSingle(compiled.Program!, new Dict(), 42, index);
            Assert.Equal(run.Value, untraced.Value);
        }
    }

    private static BigInteger Integer(object? value) => value switch
    {
        BigInteger integer => integer,
        string text => BigInteger.Parse(text),
        ExprValue expression => expression.NumberNumerator / expression.NumberDenominator,
        _ => throw new InvalidOperationException($"Unexpected number {value}")
    };

    private static object?[] Array(object? value) => (object?[])value!;
    private static int[] Numbers(object? value) => Array(value).Select(item => (int)Integer(item)).ToArray();
    private static int[] Numbers(JsonElement value) => value.EnumerateArray().Select(item => item.GetInt32()).ToArray();

    [Fact]
    public void CompleteRounds_MatchIndependentFractionReferenceIncludingEveryCascade()
    {
        using JsonDocument reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestData",
            "LabDemo", "lab-demo-reference.json")));
        CompileResult baseGraph = new GraphCompiler().Compile(Fixture("lab-demo.json"));
        CompileResult buyGraph = new GraphCompiler().Compile(Fixture("lab-demo-buy.json"));
        Assert.True(baseGraph.IsValid);
        Assert.True(buyGraph.IsValid);
        foreach (JsonElement expected in reference.RootElement.GetProperty("rounds").EnumerateArray())
        {
            CompileResult compiled = expected.GetProperty("buy").GetBoolean() ? buyGraph : baseGraph;
            (BigInteger Value, Dict State) round = SampledInterpreter.RunSingle(compiled.Program!,
                new Dict { ["traceEnabled"] = true },
                expected.GetProperty("seed").GetInt64(), expected.GetProperty("index").GetInt64());
            var untraced = SampledInterpreter.RunSingle(compiled.Program!, new Dict { ["traceEnabled"] = false },
                expected.GetProperty("seed").GetInt64(), expected.GetProperty("index").GetInt64());
            Assert.Equal(round.Value, untraced.Value);
            Assert.Equal(Integer(round.State["roundCoins"]), Integer(untraced.State["roundCoins"]));
            Assert.Equal(Numbers(round.State["board"]), Numbers(untraced.State["board"]));
            var parts = expected.GetProperty("win").GetString()!.Split('/');
            BigInteger numerator = BigInteger.Parse(parts[0]);
            BigInteger denominator = parts.Length == 1 ? BigInteger.One : BigInteger.Parse(parts[1]);
            Assert.Equal(numerator * compiled.WinScale, round.Value * denominator);
            Assert.Equal(expected.GetProperty("baseCoins").GetInt32(), (int)Integer(round.State["baseCoins"]));
            Assert.Equal(expected.GetProperty("freeCoins").GetInt32(), (int)Integer(round.State["freeCoins"]));
            Assert.Equal(Integer(round.State["roundCoins"]),
                Integer(round.State["baseCoins"]) + Integer(round.State["freeCoins"]));
            Assert.Equal(expected.GetProperty("scatters").GetInt32(), (int)Integer(round.State["scatters"]));
            Assert.Equal(expected.GetProperty("bonusTriggered").GetBoolean(), round.State["bonusTriggered"]);
            JsonElement[] spins = expected.GetProperty("spins").EnumerateArray().ToArray();
            Assert.Equal(spins.Length, Array(round.State["boards"]).Length);
            for (var i = 0; i < spins.Length; i++)
            {
                Assert.Equal(Numbers(spins[i].GetProperty("board")), Numbers(Array(round.State["boards"])[i]));
                Assert.Equal(Numbers(spins[i].GetProperty("final")), Numbers(Array(round.State["finalBoards"])[i]));
                foreach (var (field, key) in new[]
                         {
                             ("win", "winHistory"), ("collection", "collectionWinHistory"),
                             ("cluster", "clusterWinHistory"), ("multiplier", "multiplierHistory"),
                             ("roundWin", "roundWinHistory")
                         })
                {
                    Assert.Equal(spins[i].GetProperty(field).GetInt32(), (int)Integer(Array(round.State[key])[i]));
                }

                JsonElement[] coins = spins[i].GetProperty("coins").EnumerateArray().ToArray();
                Assert.Equal(coins.Select(coin => coin.GetProperty("position").GetInt32()),
                    Numbers(Array(round.State["coinPositionHistory"])[i]));
                Assert.Equal(coins.Select(coin => coin.GetProperty("value").GetInt32()),
                    Numbers(Array(round.State["coinValueHistory"])[i]));
            }

            JsonElement[] steps = expected.GetProperty("steps").EnumerateArray().ToArray();
            Assert.Equal(steps.Length, Array(round.State["stepWins"]).Length);
            for (var i = 0; i < steps.Length; i++)
            {
                Assert.Equal(Numbers(steps[i].GetProperty("before")),
                    Numbers(Array(round.State["stepBoardsBefore"])[i]));
                Assert.Equal(Numbers(steps[i].GetProperty("after")), Numbers(Array(round.State["stepBoardsAfter"])[i]));
                Assert.Equal(steps[i].GetProperty("win").GetInt32(), (int)Integer(Array(round.State["stepWins"])[i]));
                Assert.Equal(steps[i].GetProperty("multiplier").GetInt32(),
                    (int)Integer(Array(round.State["stepMultipliers"])[i]));
                Assert.Equal(steps[i].GetProperty("spin").GetInt32(), (int)Integer(Array(round.State["stepSpins"])[i]));
                JsonElement[] hits = steps[i].GetProperty("hits").EnumerateArray().ToArray();
                Assert.Equal(hits.Select(hit => hit.GetProperty("symbol").GetInt32()),
                    Numbers(Array(round.State["stepHitSymbols"])[i]));
                Assert.Equal(hits.Select(hit => hit.GetProperty("size").GetInt32()),
                    Numbers(Array(round.State["stepHitSizes"])[i]));
                Assert.Equal(hits.Select(hit => hit.GetProperty("payout").GetInt32()),
                    Numbers(Array(round.State["stepHitPayouts"])[i]));
                for (var hit = 0; hit < hits.Length; hit++)
                {
                    Assert.Equal(Numbers(hits[hit].GetProperty("positions")),
                        Numbers(Array(Array(round.State["stepHitPositions"])[i])[hit]));
                }
            }
        }
    }
    [Fact]
    public void ComponentBoards_MatchIndependentReferenceIncludingExclusiveWilds()
    {
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "LabDemo", "lab-demo-reference.json")));
        foreach (var expected in reference.RootElement.GetProperty("components").EnumerateArray())
        {
            var fixture = Fixture("lab-demo-mini.json");
            var initial = new Dictionary<string, JsonElement>(fixture.InitialState!) { ["board"] = JsonSerializer.SerializeToElement(Numbers(expected.GetProperty("board")).Select(value => value.ToString()).ToArray()) };
            var config = fixture with
            {
                InitialState = initial,
                Nodes = fixture.Nodes.Where(node => node.Id is not "choice" and not "board").ToArray(),
                Edges = fixture.Edges.Where(edge => edge.SourceNodeId is not "choice" and not "board").ToArray(),
                Expressions = fixture.Expressions!.Where(pair => pair.Key != "board").ToDictionary()
            };
            var compiled = new GraphCompiler().Compile(config);
            Assert.True(compiled.IsValid, string.Join("; ", compiled.Errors.Select(error => error.Message)));
            var state = SampledInterpreter.RunSingle(compiled.Program!, new Dict { ["traceEnabled"] = true }, 42).State;
            var hits = expected.GetProperty("hits").EnumerateArray().ToArray();
            var untraced = SampledInterpreter.RunSingle(compiled.Program!, new Dict { ["traceEnabled"] = false }, 42).State;
            Assert.Equal(hits.Sum(hit => hit.GetProperty("payout").GetInt32()), (int)Integer(untraced["evalWin"]));
            Assert.Equal(Numbers(state["winningPositions"]).Order(), Numbers(untraced["winningPositions"]).Order());
            Assert.Equal(hits.Select(hit => hit.GetProperty("symbol").GetInt32()), Numbers(state["hitSymbols"]));
            Assert.Equal(hits.Select(hit => hit.GetProperty("size").GetInt32()), Numbers(state["hitSizes"]));
            Assert.Equal(hits.Select(hit => hit.GetProperty("payout").GetInt32()), Numbers(state["hitPayouts"]));
            for (var hit = 0; hit < hits.Length; hit++) Assert.Equal(Numbers(hits[hit].GetProperty("positions")), Numbers(Array(state["hitPositions"])[hit]));
        }
    }

    [Fact]
    public void MiniEnumeration_HasExactIndependentExpectationAndDetectsChangedPaytable()
    {
        var fixture = Fixture("lab-demo-mini.json");
        Rational Expectation(GraphConfig config)
        {
            var compiled = new GraphCompiler().Compile(config);
            Assert.True(compiled.IsValid, string.Join("; ", compiled.Errors.Select(error => error.Message)));
            var distribution = ExactInterpreter.Evaluate(compiled.Program!, new Dict(), StateHasher.CanonicalHash).ValueDistribution();
            var (numerator, denominator) = distribution.ExpectedBigIntegerValue();
            return new Rational(numerator, denominator * compiled.WinScale);
        }
        // One of two equally weighted boards has five connected BONE cells;
        // it pays one raw credit on a twenty-credit stake, so E = 1 / 40.
        Assert.Equal(new Rational(1, 40), Expectation(fixture));
        var initial = new Dictionary<string, JsonElement>(fixture.InitialState!);
        var table = initial["clusterPaytable"].EnumerateArray().Select(value => value.GetString()!).ToArray();
        table[0] = "2";
        initial["clusterPaytable"] = JsonSerializer.SerializeToElement(table);
        Assert.NotEqual(new Rational(1, 40), Expectation(fixture with { InitialState = initial }));
    }

    [Fact]
    public void CoinCollection_ReachesTheExactRoundCapAndSkipsRemainingLives()
    {
        var config = Fixture("lab-demo-buy.json");
        var mechanics = new Dictionary<string, CustomMechanic>(config.Mechanics!);
        var spin = mechanics["free-spin"];
        mechanics["free-spin"] = spin with
        {
            Nodes = spin.Nodes.Select(node => node is DrawNode draw
            ? draw with { DrawWeights = [new() { OutcomeId = draw.Id.StartsWith("prize-") ? "250" : "6", Weight = 1, Value = 0 }] }
            : node).ToArray()
        };
        var compiled = new GraphCompiler().Compile(config with { Mechanics = mechanics });
        Assert.True(compiled.IsValid, string.Join("; ", compiled.Errors.Select(error => error.Message)));
        var round = SampledInterpreter.RunSingle(compiled.Program!, new Dict { ["traceEnabled"] = true }, 42);
        Assert.Equal(9999 * compiled.WinScale, round.Value);
        Assert.Equal(2, Array(round.State["boards"]).Length);
        Assert.Equal(new[] { 150000, 49980 }, Numbers(round.State["collectionWinHistory"]));
        Assert.Equal(new[] { 8, 0 }, Numbers(round.State["remainingHistory"]));
        Assert.Empty(Array(round.State["stepWins"]));
        Assert.Equal(true, round.State["bonusCompleted"]);
    }

}
