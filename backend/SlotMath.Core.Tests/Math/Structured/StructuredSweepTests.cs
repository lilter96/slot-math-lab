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
    [Fact]
    public void EveryFixtureGraph_AgreesWithClassicWhereBothAnswer()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData");
        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Order())
        {
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
                        Budget = new Budget { MaxBranches = 2_000_000, MaxTime = TimeSpan.FromSeconds(20) }
                    }).ValueDistribution();
            }
            catch (Exception e)
            {
                classicNote = e.GetType().Name;
            }

            StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled,
                new StructuredExactOptions { DistributionWork = 50_000_000, MaxTime = TimeSpan.FromSeconds(60) });
            output.WriteLine(
                $"{Path.GetFileName(file)}: classic={(classic is null ? classicNote : classic.Count + " values")} structured={(result.Succeeded ? "ok" : "declined: " + result.DeclinedReason)} " +
                $"dist={result.Distribution?.Count} {result.Elapsed.TotalMilliseconds:F0}ms terms={result.Terms}");
            if (classic is null || !result.Succeeded)
            {
                continue;
            }

            (BigInteger numerator, BigInteger denominator) = classic.ExpectedBigIntegerValue();
            Assert.Equal(new Rational(numerator, denominator), result.ExpectedWin);
            if (result.Distribution is not null)
            {
                Assert.Equal(StructuredDogHouseTests.Normalized(classic),
                    StructuredDogHouseTests.Normalized(result.Distribution));
            }
        }
    }
}
