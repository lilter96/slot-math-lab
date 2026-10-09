using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SlotMath.Api.Tests;

[Collection("SerialTests")]
public class GraphExecutionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public GraphExecutionTests(WebApplicationFactory<Program> factory) => client = factory.CreateClient();
    private static JsonElement Fixture(string name) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "DogHouse", name)));
    [Fact]
    public async Task GenericPlay_BonusTraceAndSeedReplayUseTheUiGraph()
    {
        var config = Fixture("dog-house-ui.json");
        var request = new { config, seed = 42, roundIndex = 230, trace = true };
        var response = await client.PostAsJsonAsync("/api/play/round", request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var round = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(9, round.GetProperty("state").GetProperty("bonusGrid").GetArrayLength());
        var replay = await (await client.PostAsJsonAsync("/api/play/round", request)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(round.GetRawText(), replay.GetRawText());
        var withoutTrace = await (await client.PostAsJsonAsync("/api/play/round", new { config, seed = 42, roundIndex = 230 })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(round.GetProperty("win").GetDouble(), withoutTrace.GetProperty("win").GetDouble());
        Assert.Equal(round.GetProperty("configHash").GetString(), withoutTrace.GetProperty("configHash").GetString());
    }
    [Fact]
    public async Task ExplicitModes_PreserveActualProvenanceAndGraphHash()
    {
        var config = Fixture("dog-house-mini-ui.json");
        double exact = 0;
        foreach (var mode in new[] { "Exact", "Pruned", "Sampled", "Hybrid" })
        {
            var response = await client.PostAsJsonAsync("/api/evaluate/graph", new { config, mode, samples = 1000, epsilon = 0.6, seed = 42 });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Complete", result.GetProperty("status").GetString()); Assert.Equal(mode, result.GetProperty("requestedMode").GetString());
            if (mode == "Exact") { exact = result.GetProperty("rtp").GetDouble(); Assert.Equal("Exact", result.GetProperty("provenance").GetString()); }
            if (mode == "Pruned") { Assert.Equal("ExactInterval", result.GetProperty("provenance").GetString()); Assert.True(result.GetProperty("lower").GetDouble() <= exact); Assert.True(result.GetProperty("upper").GetDouble() >= exact); }
            if (mode == "Sampled") Assert.Equal("Sampled", result.GetProperty("actualStrategy").GetString());
            if (mode == "Hybrid") Assert.Contains(result.GetProperty("actualStrategy").GetString(), new[] { "Exact", "Sampled" });
            var play = await (await client.PostAsJsonAsync("/api/play/round", new { config })).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(play.GetProperty("configHash").GetString(), result.GetProperty("configHash").GetString());
        }
    }
    [Fact]
    public async Task MissingExpression_RejectsExecutionInsteadOfReturningZero()
    {
        var graph = JsonNode.Parse(Fixture("dog-house-ui.json").GetRawText())!;
        graph["expressions"]!.AsObject().Remove("credits");
        var response = await client.PostAsJsonAsync("/api/play/round", new { config = graph });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Graph validation failed", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("\"win\":0", await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Play_RejectsUnsafeReplayIndex()
    {
        var response = await client.PostAsJsonAsync("/api/play/round", new { config = Fixture("dog-house-ui.json"), roundIndex = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
