using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;
using Xunit.Abstractions;

namespace SlotMath.Core.Tests.Math.Structured;

/// <summary>
///     The structured evaluator on the graphs of the lab demo. Its cluster search needs the whole law of the board,
///     which is beyond the evaluator for the round and for its component graph alike: it has to say so within its
///     budget and return no number. The exact value of the component graph comes from enumeration
///     (<c>NineLivesGraphTests</c>).
/// </summary>
public class StructuredLabDemoTests(ITestOutputHelper output)
{
    private static GraphConfig Fixture(string file) => JsonSerializer.Deserialize<GraphConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "LabDemo", file)), JsonOptions.Default)!;

    [Theory]
    [InlineData("lab-demo.json")]
    [InlineData("lab-demo-buy.json")]
    [InlineData("lab-demo-mini.json")]
    public void Graph_IsDeclinedWithinItsBudget_AndReturnsNoNumber(string file)
    {
        CompileResult compiled = new GraphCompiler().Compile(Fixture(file));
        Assert.True(compiled.IsValid, string.Join("; ", compiled.Errors.Select(e => $"{e.NodeId}: {e.Message}")));
        StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled,
            new StructuredExactOptions { MaxTime = TimeSpan.FromSeconds(12) });
        output.WriteLine($"declined={result.DeclinedReason} elapsed={result.Elapsed.TotalMilliseconds:F0}ms");
        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.DeclinedReason));
        Assert.Null(result.Distribution);
        Assert.Null(result.HitFrequency);
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
