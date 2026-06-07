using System.Text.Json;
using SlotMath.Core;
using SlotMath.Core.Model;
using Xunit;
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
            output.WriteLine($"Schemas dir not found. Schema JSON:");
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
        using var doc = System.Text.Json.JsonDocument.Parse(schemaJson);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void SchemaVersionConstantMatches()
    {
        Assert.Equal("1.0.0", SchemaEmitter.CurrentSchemaVersion);
    }
}
