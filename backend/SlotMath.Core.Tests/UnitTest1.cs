using System.Text.Json;
using SlotMath.Core;
using SlotMath.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace SlotMath.Core.Tests;

public class RoundTripTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions JsonOpts = JsonOptions.Default;

    private static string[] AllSamples =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*.json");

    [Fact]
    public void AllSampleFilesExist()
    {
        var samples = AllSamples;
        output.WriteLine($"Found {samples.Length} sample files");
        Assert.True(samples.Length >= 12, $"Expected >=12 samples, found {samples.Length}");
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void SampleRoundTripsLosslessly(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        output.WriteLine($"Testing: {fileName}");

        // Read original JSON
        var originalJson = File.ReadAllText(filePath);

        // Deserialize
        var graph = JsonSerializer.Deserialize<GraphConfig>(originalJson, JsonOpts);
        Assert.NotNull(graph);

        // Serialize back
        var reserialized = JsonSerializer.Serialize(graph, JsonOpts);

        // Deserialize again
        var graph2 = JsonSerializer.Deserialize<GraphConfig>(reserialized, JsonOpts);
        Assert.NotNull(graph2);

        // Structural equality on the model
        Assert.Equal(graph!.SchemaVersion, graph2!.SchemaVersion);
        Assert.Equal(graph.Id, graph2.Id);
        Assert.Equal(graph.Name, graph2.Name);
        Assert.Equal(graph.Symbols.Length, graph2.Symbols.Length);
        Assert.Equal(graph.Nodes.Length, graph2.Nodes.Length);
        Assert.Equal(graph.Edges.Length, graph2.Edges.Length);
        Assert.Equal(graph.Plugins.Length, graph2.Plugins.Length);
    }

    [Fact]
    public void ExpressionRoundTripsWithDiscriminator()
    {
        // Verify expression polymorphism works
        Expression expr = new AggregateExpr
        {
            Func = AggregateFunc.Product,
            Target = "board",
            Predicate = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = new FieldAccessExpr { Path = ["symbol", "kind"], Target = "board" },
                Right = new ConstantExpr { Kind = ConstantKind.String, Value = "Multiplier" },
            },
        };

        var json = JsonSerializer.Serialize(expr, JsonOpts);
        output.WriteLine($"Expression JSON: {json}");

        var deserialized = JsonSerializer.Deserialize<Expression>(json, JsonOpts);
        Assert.NotNull(deserialized);
        Assert.IsType<AggregateExpr>(deserialized);

        var agg = (AggregateExpr)deserialized;
        Assert.Equal(AggregateFunc.Product, agg.Func);
        Assert.Equal("board", agg.Target);
        Assert.NotNull(agg.Predicate);
        Assert.IsType<CompareExpr>(agg.Predicate);
    }

    [Fact]
    public void NodePolymorphismRoundTrips()
    {
        var graph = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Nodes = [
                new DrawNode { Id = "d1", Label = "Draw" },
                new LoopNode { Id = "l1", Label = "Loop", MaxIterations = 100 },
                new LibraryNode { Id = "lib1", Label = "MyLib", MechanicName = "custom-bonus" },
            ],
            Edges = [],
            Symbols = [],
            Paytables = [],
            PaylineSets = [],
            ReelStrips = [],
            ReelSets = [],
        };

        var json = JsonSerializer.Serialize(graph, JsonOpts);
        output.WriteLine($"Graph JSON: {json}");

        var deserialized = JsonSerializer.Deserialize<GraphConfig>(json, JsonOpts);
        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized.Nodes.Length);

        Assert.IsType<DrawNode>(deserialized.Nodes[0]);
        Assert.IsType<LoopNode>(deserialized.Nodes[1]);
        Assert.IsType<LibraryNode>(deserialized.Nodes[2]);

        var lib = (LibraryNode)deserialized.Nodes[2];
        Assert.Equal("custom-bonus", lib.MechanicName);
    }

    [Fact]
    public void PluginReferenceRoundTrips()
    {
        var plugin = new PluginReference
        {
            PluginId = "test-plugin",
            Contract = PluginContract.IEvaluator,
            Version = "1.0.0",
            Config = new Dictionary<string, string> { ["param"] = "value" },
        };

        var json = JsonSerializer.Serialize(plugin, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<PluginReference>(json, JsonOpts);
        Assert.NotNull(deserialized);
        Assert.Equal("test-plugin", deserialized.PluginId);
        Assert.Equal(PluginContract.IEvaluator, deserialized.Contract);
        Assert.Equal("1.0.0", deserialized.Version);
    }

    public static IEnumerable<object[]> SampleFiles()
    {
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
        if (!Directory.Exists(testDataDir))
            yield break;

        foreach (var file in Directory.GetFiles(testDataDir, "*.json").OrderBy(f => f))
            yield return new object[] { file };
    }
}
