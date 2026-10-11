using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;
using Xunit.Abstractions;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Math.Structured;

public class StructuredSweepTests(ITestOutputHelper output)
{
    // The game graphs have suites of their own (Math/Games, StructuredLabDemoTests), which compare the interpreters
    // on models cut down until the classic interpreter can enumerate them. Here they would only use up its budget.
    private static readonly string[] OwnSuites = ["Games", "LabDemo"];

    [Fact]
    public void EveryFixtureGraph_AgreesWithClassicWhereBothAnswer()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData");
        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Order())
        {
            if (OwnSuites.Contains(Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar)[0]))
            {
                continue;
            }

            GraphConfig? config;
            try
            {
                config = JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(file), JsonOptions.Default);
            }
            catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
            {
                continue;
            }

            if (config?.Nodes is not { Length: > 0 })
            {
                continue;
            }

            CompileResult compiled;
            try
            {
                compiled = new GraphCompiler().Compile(config);
            }
            catch (Exception e)
            {
                output.WriteLine($"{Path.GetFileName(file)}: compile threw {e.GetType().Name}");
                continue;
            }

            if (!compiled.IsValid)
            {
                output.WriteLine($"{Path.GetFileName(file)}: invalid");
                continue;
            }

            Dist<BigInteger>? classic = null;
            var classicNote = "";
            try
            {
                classic = ExactInterpreter.Evaluate(compiled.ReferenceProgram!, new Dict(), StateHasher.CanonicalHash,
                    new ExactConfig
                    {
                        Budget = new Budget
                        {
                            MaxBranches = 2_000_000,
                            MaxTime = TimeSpan.FromSeconds(20)
                        }
                    }).ValueDistribution();
            }
            catch (Exception e)
            {
                classicNote = e.GetType().Name;
            }

            if (classic is null)
            {
                output.WriteLine($"{Path.GetFileName(file)}: classic={classicNote}, nothing to compare with");
                continue;
            }

            StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled,
                new StructuredExactOptions
                {
                    DistributionWork = 50_000_000,
                    MaxTime = TimeSpan.FromSeconds(60)
                });
            output.WriteLine(
                $"{Path.GetFileName(file)}: classic={classic.Count} values structured={(result.Succeeded ? "ok" : "declined: " + result.DeclinedReason)} " +
                $"dist={result.Distribution?.Count} {result.Elapsed.TotalMilliseconds:F0}ms terms={result.Terms}");
            if (!result.Succeeded)
            {
                continue;
            }

            (BigInteger numerator, BigInteger denominator) = classic.ExpectedBigIntegerValue();
            Assert.Equal(new Rational(numerator, denominator), result.ExpectedWin);
            if (result.Distribution is not null)
            {
                Assert.Equal(StructuredLabDemoTests.Normalized(classic),
                    StructuredLabDemoTests.Normalized(result.Distribution));
            }
        }
    }
}
