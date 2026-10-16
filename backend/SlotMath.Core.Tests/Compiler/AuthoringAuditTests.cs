using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;

namespace SlotMath.Core.Tests.Compiler;

public sealed class AuthoringAuditTests
{
    [Theory]
    [InlineData("aggregate-default", 3, 1)]
    [InlineData("aggregate-explicit", 3, 1)]
    [InlineData("aggregate-local-alias", 3, 1)]
    [InlineData("alias-undefined", 7, 1)]
    [InlineData("alias-null", 7, 1)]
    [InlineData("alias-state", 7, 1)]
    [InlineData("alias-board", 7, 1)]
    [InlineData("fold-initializer", 14, 1)]
    [InlineData("map-locals", 3, 1)]
    [InlineData("filter-locals", 2, 1)]
    [InlineData("aggregate-nested", 6, 1)]
    [InlineData("parameter-original", 7, 1)]
    [InlineData("parameter-bound", 7, 1)]
    [InlineData("parameter-composed-original", 7, 1)]
    [InlineData("parameter-composed-bound", 7, 1)]
    [InlineData("parameter-weighted-original", 7, 1)]
    [InlineData("parameter-weighted-bound", 7, 1)]
    public void FrontendLoweredSource_PreservesIndependentExpectedValue_InAllExistingEngines(string fixture,
        int expected, int expectedDenominator)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "AuthoringAudit", fixture + ".json");
        var config = JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(path), JsonOptions.Default)!;
        CompileResult compiled = new GraphCompiler().Compile(config);
        Assert.True(compiled.IsValid, string.Join("; ", compiled.Errors.Select(error => error.Message)));
        // Existing dynamic scalar weights choose the reference fallback; other cases exercise native plans.
        Assert.Equal(fixture.StartsWith("parameter-weighted", StringComparison.Ordinal)
            ? "reference-interpreter"
            : "compiled-state-plan-v1", compiled.SamplingEngine);

        foreach (Slot<Dictionary<string, object?>, BigInteger>? program in new[]
                 {
                     compiled.ReferenceProgram!, compiled.Program!
                 })
        {
            ExactResult<Dictionary<string, object?>, BigInteger> exact = ExactInterpreter.Evaluate(program,
                new Dictionary<string, object?>(),
                StateHasher.CanonicalHash);
            Assert.True(exact.Distribution.IsFullyExact);
            (BigInteger numerator, BigInteger denominator) = exact.ValueDistribution().ExpectedBigIntegerValue();
            Assert.Equal(new BigInteger(expected) * compiled.WinScale * denominator, numerator * expectedDenominator);
            foreach (var seed in new long[] { 1, 42, 2026 })
            {
                (BigInteger Value, Dictionary<string, object?> State) result =
                    SampledInterpreter.RunSingle(program, new Dictionary<string, object?>(), seed);
                var payout = expectedDenominator == 1 ? expected : result.State["choice"]?.ToString() == "a" ? 7 : 0;
                Assert.Equal(new BigInteger(payout) * compiled.WinScale, result.Value);
            }
        }
    }
}
