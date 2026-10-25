using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using SlotMath.Core.Compiler;
using SlotMath.Core.Model;
using Xunit.Abstractions;

namespace SlotMath.Core.Tests;

public class SchemaExportTests(ITestOutputHelper output)
{
    [Fact]
    public void EmitSchemaToFrontend()
    {
        var schemaJson = SchemaEmitter.GetSchemaJson();

        // Find frontend schemas directory
        var baseDir = AppContext.BaseDirectory;
        var schemasDir = Path.GetFullPath(
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "frontend", "schemas"));

        if (!Directory.Exists(schemasDir))
        {
            // Fallback: try relative to the repo root
            schemasDir = Path.GetFullPath(
                Path.Combine(baseDir, "..", "..", "..", "..", "..", "..", "..", "..", "frontend", "schemas"));
        }

        // If we can't find it, output for manual inspection
        if (!Directory.Exists(schemasDir))
        {
            output.WriteLine("Schemas dir not found. Schema JSON:");
            output.WriteLine(schemaJson);
            output.WriteLine($"Base dir: {baseDir}");
            return;
        }

        var outputPath = Path.Combine(schemasDir, "graph-schema.json");
        SchemaEmitter.WriteSchemaToFile(outputPath);
        output.WriteLine($"Schema written to: {outputPath}");
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public void SchemaContainsSchemaVersion()
    {
        var schemaJson = SchemaEmitter.GetSchemaJson();
        // The schema defines a "schemaVersion" property of type string
        Assert.Contains("schemaVersion", schemaJson);
        Assert.Contains("\"type\": \"string\"", schemaJson);
    }

    [Fact]
    public void SchemaIsValidJson()
    {
        var schemaJson = SchemaEmitter.GetSchemaJson();
        using JsonDocument doc = JsonDocument.Parse(schemaJson);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Theory]
    [InlineData("null")]
    [InlineData(
        "{\"exprType\":\"binary\",\"op\":\"Add\",\"left\":null,\"right\":{\"exprType\":\"constant\",\"kind\":\"Integer\",\"value\":\"1\"}}")]
    public void SharedExpressionSchemaRejectsNull(string json)
    {
        JsonNode exported = JsonNode.Parse(SchemaEmitter.GetSchemaJson())!;
        var schema = new JsonObject { ["$defs"] = exported["$defs"]!.DeepClone(), ["$ref"] = "#/$defs/expression" };
        using JsonDocument instance = JsonDocument.Parse(json);
        Assert.False(JsonSchema.FromText(schema.ToJsonString()).Evaluate(instance.RootElement).IsValid);
    }

    [Fact]
    public void OptionalPortDefaultStillAcceptsNull()
    {
        JsonNode exported = JsonNode.Parse(SchemaEmitter.GetSchemaJson())!;
        var schema = new JsonObject
        {
            ["$defs"] = exported["$defs"]!.DeepClone(),
            ["$ref"] = "#/$defs/dictionaryOfStringAndPort"
        };
        using JsonDocument instance =
            JsonDocument.Parse("{\"in\":{\"name\":\"in\",\"type\":\"Number\",\"defaultValue\":null}}");
        Assert.True(JsonSchema.FromText(schema.ToJsonString()).Evaluate(instance.RootElement).IsValid);
    }

    [Fact]
    public void NullNamedExpressionReturnsCodedValidationError()
    {
        var graph = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Nodes = [new MetricsSinkNode { Id = "sink", WinCap = 10 }],
            Expressions = new Dictionary<string, Expression> { ["bad"] = null! }
        };
        CompileError error = Assert.Single(GraphValidator.Validate(graph, null));
        Assert.Equal(ErrorCodes.ExpressionTypeError, error.Code);
        Assert.Contains("cannot be null", error.Message);
    }

    [Fact]
    public void SchemaVersionConstantMatches() => Assert.Equal("1.0.0", SchemaEmitter.CurrentSchemaVersion);
}
