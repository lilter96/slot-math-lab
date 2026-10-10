using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SlotMath.Api.Tests;

[Collection("SerialTests")]
public sealed class FractionalGraphTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData(10000, "Exact")]
    [InlineData(1, "Sampled")]
    public async Task NativeAuthoredFractionsHaveManualPayoutInBothEvaluationRegimes(int maxBranches, string strategy)
    {
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "fractional-model.json")))!;
        // Two equiprobable draws retain a deterministic payout but allow the
        // light route's branch budget to select either actual evaluation regime.
        config["nodes"]![0]!["drawWeights"]!.AsArray().Add(new JsonObject { ["outcomeId"] = "other", ["value"] = 0, ["weight"] = 1 });
        var response = await factory.CreateClient().PostAsJsonAsync("/api/evaluate/light", new { config, maxBranches, sampleSize = 128, seed = 817 });
        response.EnsureSuccessStatusCode(); var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(strategy, result.GetProperty("strategy").GetString());
        // Independent rational specification: 17 + 6 + 1 + 0 = 24.
        Assert.Equal(24, result.GetProperty("rtp").GetDouble());
        Assert.Equal(817, result.GetProperty("seed").GetInt64());
        Assert.Equal(1, result.GetProperty("hitFrequency").GetDouble());
        Assert.Empty(result.GetProperty("errors").EnumerateArray());
    }
}
