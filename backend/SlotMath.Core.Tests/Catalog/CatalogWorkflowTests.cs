using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Catalog;

public class CatalogWorkflowTests
{
    [Theory]
    [InlineData("lines", 5)]
    [InlineData("ways", 5)]
    [InlineData("cluster", 5)]
    [InlineData("scatter", 5)]
    [InlineData("sticky-wild", 5)]
    [InlineData("hold-and-win", 35)]
    [InlineData("cascade", 2)]
    public void CompleteCatalogGraph_CompilesAndMatchesManualDistribution(string mechanic, int expected)
    {
        // Explicit rule examples, independent of the production evaluator:
        // W substitutes for H; three H pay 5; three L pay 2; three S pay 5;
        // sticky preserves W; money collection totals 5+20+10; tumble replaces all H with L.
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "AuditCatalog", mechanic + ".json");
        var config = JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(path), JsonOptions.Default)!;
        var compiled = new GraphCompiler().Compile(config);
        Assert.True(compiled.IsValid, string.Join("; ", compiled.Errors));
        var result = ExactInterpreter.Evaluate(compiled.Program!, new Dictionary<string, object?>(), StateHasher.CanonicalHash);
        var dist = result.ValueDistribution();
        var outcome = Assert.Single(dist.Entries);
        Assert.Equal(new BigInteger(expected), outcome.Value);
        Assert.Equal(dist.Denominator, outcome.Numerator);
        Assert.True(dist.IsFullyExact);
    }
}
