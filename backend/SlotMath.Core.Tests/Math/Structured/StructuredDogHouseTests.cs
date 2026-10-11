using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;
using Xunit.Abstractions;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Math.Structured;

public class StructuredDogHouseTests(ITestOutputHelper output)
{
    private static string Data(string file) => Path.Combine(AppContext.BaseDirectory, "TestData", "DogHouse", file);

    private static GraphConfig Fixture(string file) =>
        JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Data(file)), JsonOptions.Default)!;

    private static CompileResult Compile(GraphConfig config)
    {
        CompileResult result = new GraphCompiler().Compile(config);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.NodeId}: {e.Message}")));
        return result;
    }

    private static Rational Parse(string value)
    {
        var parts = value.Split('/');
        return new Rational(BigInteger.Parse(parts[0]), BigInteger.Parse(parts[1]));
    }

    [Fact]
    public void FullGraph_ExactRtpEqualsIndependentRational()
    {
        CompileResult compiled = Compile(Fixture("dog-house-ui.json"));
        StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled);
        output.WriteLine(
            $"succeeded={result.Succeeded} declined={result.DeclinedReason} elapsed={result.Elapsed.TotalMilliseconds:F0}ms " +
            $"rvs={result.RandomVariables} terms={result.Terms} laws={result.Laws} work={result.Work} distribution={result.Distribution?.Count}");
        Assert.True(result.Succeeded, result.DeclinedReason);

        using JsonDocument reference =
            JsonDocument.Parse(File.ReadAllText(Data("dog-house-independent-expectation.json")));
        Rational expected = Parse(reference.RootElement.GetProperty("rationalRtp").GetString()!);
        Assert.Equal(expected, result.ExpectedWin / new Rational(compiled.WinScale, 1));
    }

    // The full round has no enumerated reference for its hit frequency, so the exact value is held against
    // sampling: two million pinned rounds must agree within four standard errors of a proportion, and the
    // same gate must reject the exact value moved by one percent.
    [Fact]
    public void FullGraph_ExactHitFrequencyAgreesWithSampling_AndAShiftedValueDoesNot()
    {
        const long rounds = 2_000_000;
        CompileResult compiled = Compile(Fixture("dog-house-ui.json"));
        StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled);
        Assert.True(result.Succeeded, result.DeclinedReason);
        Assert.Null(result.Distribution);
        Assert.NotNull(result.HitFrequency);
        Rational exact = result.HitFrequency!.Value;
        Assert.Equal(new Rational(9743, 35280), exact);

        using JsonDocument reference =
            JsonDocument.Parse(File.ReadAllText(Data("dog-house-independent-expectation.json")));
        Rational trigger = Parse(reference.RootElement.GetProperty("trigger").GetString()!);
        Assert.True(exact > trigger && exact < new Rational(1, 1));

        SampledResult<Dict> sampled = SampledInterpreter.Evaluate(compiled.Program!, new Dict(),
            new SampledConfig
            {
                Seed = 20260410,
                MaxSpins = rounds,
                DegreeOfParallelism = 4,
                WinScale = (double)compiled.WinScale
            });
        Assert.Equal(rounds, sampled.SpinsCompleted);
        var p = exact.ToDouble();
        var sigma = System.Math.Sqrt(p * (1 - p) / rounds);
        var observed = sampled.Stats.HitFrequency;
        output.WriteLine(
            $"exact={exact.Numerator}/{exact.Denominator} = {p:R}; sampled={observed:R}; sigma={sigma:E3}; off by {(observed - p) / sigma:F2} sigma");

        bool Agrees(double claimed)
        {
            return System.Math.Abs(observed - claimed) <= 4 * System.Math.Sqrt(claimed * (1 - claimed) / rounds);
        }

        Assert.True(Agrees(p), $"sampled {observed} is not within 4 sigma of the exact {p}");
        Assert.False(Agrees(p * 1.01), "the gate accepted a hit frequency shifted up by one percent");
        Assert.False(Agrees(p * 0.99), "the gate accepted a hit frequency shifted down by one percent");
    }

    [Fact]
    public void MiniGraph_DistributionEqualsClassicInterpreter()
    {
        CompileResult compiled = Compile(Fixture("dog-house-mini-ui.json"));
        Dist<BigInteger> classic = ExactInterpreter.Evaluate(compiled.Program!, new Dict(), StateHasher.CanonicalHash)
            .ValueDistribution();
        StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled,
            new StructuredExactOptions { DistributionWork = 400_000_000, AlwaysStructuralHitFrequency = true });
        output.WriteLine(
            $"succeeded={result.Succeeded} declined={result.DeclinedReason} elapsed={result.Elapsed.TotalMilliseconds:F0}ms " +
            $"rvs={result.RandomVariables} terms={result.Terms} laws={result.Laws} work={result.Work} distribution={result.Distribution?.Count}");
        Assert.True(result.Succeeded, result.DeclinedReason);
        Assert.NotNull(result.Distribution);
        Assert.Equal(Normalized(classic), Normalized(result.Distribution!));

        // The hit frequency derived without the distribution is the distribution's.
        BigInteger positive = BigInteger.Zero;
        foreach (Dist<BigInteger>.Entry entry in classic.Entries)
        {
            if (entry.Value.Sign > 0)
            {
                positive += entry.Numerator;
            }
        }

        Assert.Equal(new Rational(positive, classic.Denominator), result.StructuralHitFrequency);
        Assert.Equal(result.StructuralHitFrequency, result.HitFrequency);
    }

    // Negative controls: one weight unit off must be told apart, on the full game and on the full distribution.
    private static GraphConfig ShiftFirstWeight(GraphConfig config, string mechanic, string drawId)
    {
        CustomMechanic graph = config.Mechanics![mechanic];
        DrawNode draw = graph.Nodes.OfType<DrawNode>().Single(d => d.Id == drawId);
        DrawWeight[] weights = draw.DrawWeights!.ToArray();
        weights[0] = weights[0] with { Weight = weights[0].Weight + 1 };
        var mechanics = new Dictionary<string, CustomMechanic>(config.Mechanics)
        {
            [mechanic] = graph with
            {
                Nodes = graph.Nodes.Select(n => n == draw ? draw with { DrawWeights = weights } : n).ToArray()
            }
        };
        return config with { Mechanics = mechanics };
    }

    [Fact]
    public void FullGraph_WeightShiftedByOne_DoesNotEqualIndependentRational()
    {
        CompileResult compiled = Compile(ShiftFirstWeight(Fixture("dog-house-ui.json"), "dog-free-spin", "wild-1"));
        StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled);
        Assert.True(result.Succeeded, result.DeclinedReason);
        using JsonDocument reference =
            JsonDocument.Parse(File.ReadAllText(Data("dog-house-independent-expectation.json")));
        Rational expected = Parse(reference.RootElement.GetProperty("rationalRtp").GetString()!);
        Rational actual = result.ExpectedWin / new Rational(compiled.WinScale, 1);
        Assert.NotEqual(expected, actual);
        // One unit in 10^15 of a wild multiplier's weight moves the RTP by less than 10^-12.
        Rational delta = actual - expected;
        Assert.True((delta.Numerator.Sign < 0 ? Rational.Zero - delta : delta) < new Rational(1, 1_000_000_000_000));
    }

    [Fact]
    public void MiniGraph_WeightShiftedByOne_DoesNotEqualClassicDistribution()
    {
        CompileResult original = Compile(Fixture("dog-house-mini-ui.json"));
        Dist<BigInteger> classic = ExactInterpreter.Evaluate(original.Program!, new Dict(), StateHasher.CanonicalHash)
            .ValueDistribution();
        GraphConfig config = Fixture("dog-house-mini-ui.json");
        KeyValuePair<string, CustomMechanic> mechanic =
            config.Mechanics!.First(m => m.Value.Nodes.OfType<DrawNode>().Any(d => d.DrawWeights!.Length > 1));
        DrawNode draw = mechanic.Value.Nodes.OfType<DrawNode>().First(d => d.DrawWeights!.Length > 1);
        CompileResult shifted = Compile(ShiftFirstWeight(config, mechanic.Key, draw.Id));
        StructuredExactResult result =
            StructuredExactEvaluator.Evaluate(shifted, new StructuredExactOptions { DistributionWork = 400_000_000 });
        Assert.NotNull(result.Distribution);
        Assert.NotEqual(Normalized(classic), Normalized(result.Distribution!));
        Dist<BigInteger> reference = ExactInterpreter.Evaluate(shifted.Program!, new Dict(), StateHasher.CanonicalHash)
            .ValueDistribution();
        Assert.Equal(Normalized(reference), Normalized(result.Distribution!));
    }

    internal static SortedDictionary<BigInteger, Rational> Normalized(Dist<BigInteger> dist)
    {
        var map = new SortedDictionary<BigInteger, Rational>();
        foreach (Dist<BigInteger>.Entry entry in dist.Entries)
        {
            map[entry.Value] = (map.TryGetValue(entry.Value, out Rational mass) ? mass : Rational.Zero) +
                               new Rational(entry.Numerator, dist.Denominator);
        }

        return map;
    }
}
