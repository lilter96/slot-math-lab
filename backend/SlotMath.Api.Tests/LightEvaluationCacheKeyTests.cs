using System.Text.Json;
using SlotMath.Api.Infrastructure;

namespace SlotMath.Api.Tests;

public sealed class LightEvaluationCacheKeyTests
{
    private static EvaluateLightRequest Request(string json = "{\"a\":1,\"b\":2}") =>
        new() { Config = JsonSerializer.Deserialize<JsonElement>(json), Seed = 42, SampleSize = 100, MaxBranches = 20 };
    private static readonly RuntimeProvenance Runtime = new("contract", "algorithms", "framework", "core", "api");

    [Fact]
    public void CanonicalInputsAndSameEngineReuseOneKey()
    {
        Assert.Equal(LightEvaluationCacheKey.Compute(Request(), false, Runtime),
            LightEvaluationCacheKey.Compute(Request("{\"b\":2,\"a\":1}"), false, Runtime));
        Assert.Equal(LightEvaluationCacheKey.Compute(Request(), false), LightEvaluationCacheKey.Compute(Request(), false));
    }

    [Fact]
    public void RuntimeAndPolicyChangesCannotReusePriorMath()
    {
        var baseline = LightEvaluationCacheKey.Compute(Request(), false, Runtime);
        foreach (var changed in new[] { Runtime with { CoreBinarySha256 = "new-core" }, Runtime with { ApiBinarySha256 = "new-api" },
                     Runtime with { NumericalMethods = "corrected" }, Runtime with { MeasurementContract = "v2" },
                     Runtime with { Framework = "new-framework" }, Runtime with { CoreBinarySha256 = null } })
            Assert.NotEqual(baseline, LightEvaluationCacheKey.Compute(Request(), false, changed));
        Assert.NotEqual(baseline, LightEvaluationCacheKey.Compute(Request(), true, Runtime));
    }

    [Fact]
    public void SeedBudgetsAndGraphChangesInvalidateResults()
    {
        var baseline = LightEvaluationCacheKey.Compute(Request(), false, Runtime);
        foreach (var changed in new[] { Request() with { Seed = 43 }, Request() with { SampleSize = 101 },
                     Request() with { MaxBranches = 21 }, Request("{\"a\":2,\"b\":2}") })
            Assert.NotEqual(baseline, LightEvaluationCacheKey.Compute(changed, false, Runtime));
    }
}
