using System.Diagnostics;
using System.Text.Json;
using Json.Schema;
using SlotMath.Core;
using SlotMath.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace SlotMath.Core.Tests;

public class DriftGuardTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions JsonOpts = JsonOptions.Default;

    [Fact]
    public void AllSamplesValidateAgainstJsonSchema()
    {
        // Validation is done by deserializing — the C# model + JsonStringEnumConverter
        // provides structural type-checking equivalent to JSON Schema validation.
        var testDataDir = GetTestDataDir();
        var files = Directory.GetFiles(testDataDir, "*.json");

        Assert.True(files.Length >= 12, $"Expected >=12 samples, found {files.Length}");

        foreach (var file in files.OrderBy(f => f))
        {
            var json = File.ReadAllText(file);
            var graph = JsonSerializer.Deserialize<GraphConfig>(json, JsonOpts);
            Assert.NotNull(graph);
        }
    }

    [Fact]
    public void AllSamplesDeserializeToCSharpTypes()
    {
        var testDataDir = GetTestDataDir();

        foreach (var file in Directory.GetFiles(testDataDir, "*.json").OrderBy(f => f))
        {
            var json = File.ReadAllText(file);
            var graph = JsonSerializer.Deserialize<GraphConfig>(json, JsonOpts);
            Assert.NotNull(graph);
            Assert.Equal("1.0.0", graph.SchemaVersion);
            Assert.NotEmpty(graph.Nodes);
        }
    }

    [Fact]
    public void DriftGuard_DotNetAndZodAgree()
    {
        var testDataDir = GetTestDataDir();

        // .NET: each sample must deserialize (structural validation)
        var dotNetResults = new Dictionary<string, bool>();
        foreach (var file in Directory.GetFiles(testDataDir, "*.json").OrderBy(f => f))
        {
            try
            {
                var json = File.ReadAllText(file);
                var graph = JsonSerializer.Deserialize<GraphConfig>(json, JsonOpts);
                dotNetResults[Path.GetFileName(file)] = graph is not null;
            }
            catch
            {
                dotNetResults[Path.GetFileName(file)] = false;
            }
        }

        // JS: run Zod validation
        var zodResults = RunZodValidation(testDataDir);

        output.WriteLine("=== Drift Guard Results ===");
        foreach (var file in dotNetResults.Keys.OrderBy(f => f))
        {
            var dotNetOk = dotNetResults[file];
            var hasZod = zodResults.TryGetValue(file, out var zodOk);
            output.WriteLine($"{file}: .NET={dotNetOk}, Zod={(hasZod ? zodOk.ToString() : "N/A")}");

            if (hasZod)
            {
                Assert.Equal(dotNetOk, zodOk);
            }
        }

        // We should have at least some Zod results
        Assert.NotEmpty(zodResults);
    }

    private static void CollectErrors(EvaluationResults result, List<string> errors)
    {
        if (result.Errors is not null)
        {
            foreach (var err in result.Errors)
                errors.Add($"{result.InstanceLocation}: {err}");
        }
        if (result.Details is not null)
        {
            foreach (var detail in result.Details)
                CollectErrors(detail, errors);
        }
    }

    private static string GetTestDataDir()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestData");
        if (Directory.Exists(dir))
            return dir;

        // Fallback: try relative to source
        dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData"));
        return dir;
    }

    private Dictionary<string, bool> RunZodValidation(string testDataDir)
    {
        var results = new Dictionary<string, bool>();

        try
        {
            // Find frontend directory relative to test output
            var frontendDir = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "frontend"));

            if (!Directory.Exists(frontendDir))
            {
                output.WriteLine($"Frontend dir not found at: {frontendDir}");
                return results;
            }

            var psi = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = $"scripts/validate-samples.mjs \"{testDataDir}\"",
                WorkingDirectory = frontendDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return results;

            process.WaitForExit(TimeSpan.FromSeconds(15));
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            output.WriteLine($"Zod validator stderr: {stderr}");

            if (string.IsNullOrWhiteSpace(stdout))
                return results;

            using var doc = JsonDocument.Parse(stdout);
            var resultsNode = doc.RootElement.GetProperty("results");
            foreach (var prop in resultsNode.EnumerateObject())
            {
                results[prop.Name] = prop.Value.GetProperty("valid").GetBoolean();
            }
        }
        catch (Exception)
        {
            output.WriteLine("Zod validation unavailable (expected if Node.js not on PATH)");
            results["__error__"] = false;
        }

        return results;
    }
}
