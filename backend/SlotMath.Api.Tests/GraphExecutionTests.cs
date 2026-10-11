using System.Net;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SlotMath.Api.Tests;

[Collection("SerialTests")]
public class GraphExecutionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public GraphExecutionTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    private static JsonElement Fixture(string name) =>
        JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData",
            "DogHouse", name)));

    [Fact]
    public async Task GenericPlay_BonusTraceAndSeedReplayUseTheUiGraph()
    {
        JsonElement config = Fixture("dog-house-ui.json");
        var request = new { config, seed = 42, roundIndex = 230, trace = true };
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/play/round", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var round = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(9, round.GetProperty("state").GetProperty("bonusGrid").GetArrayLength());
        var replay = await (await _client.PostAsJsonAsync("/api/play/round", request)).Content
            .ReadFromJsonAsync<JsonElement>();
        Assert.Equal(round.GetRawText(), replay.GetRawText());
        var withoutTrace =
            await (await _client.PostAsJsonAsync("/api/play/round", new { config, seed = 42, roundIndex = 230 }))
                .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(round.GetProperty("win").GetDouble(), withoutTrace.GetProperty("win").GetDouble());
        Assert.Equal(round.GetProperty("configHash").GetString(), withoutTrace.GetProperty("configHash").GetString());
    }

    [Fact]
    public async Task ExplicitModes_PreserveActualProvenanceAndGraphHash()
    {
        JsonElement config = Fixture("dog-house-mini-ui.json");
        double exact = 0;
        foreach (var mode in new[] { "Exact", "Pruned", "Sampled", "Hybrid" })
        {
            HttpResponseMessage response = await _client.PostAsJsonAsync("/api/evaluate/graph",
                new
                {
                    config,
                    mode,
                    samples = 1000,
                    epsilon = 0.6,
                    seed = 42
                });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Complete", result.GetProperty("status").GetString());
            Assert.Equal(mode, result.GetProperty("requestedMode").GetString());
            if (mode == "Exact")
            {
                exact = result.GetProperty("rtp").GetDouble();
                Assert.Equal("Exact", result.GetProperty("provenance").GetString());
            }

            if (mode == "Pruned")
            {
                Assert.Equal("ExactInterval", result.GetProperty("provenance").GetString());
                Assert.True(result.GetProperty("lower").GetDouble() <= exact);
                Assert.True(result.GetProperty("upper").GetDouble() >= exact);
            }

            if (mode == "Sampled")
            {
                Assert.Equal("Sampled", result.GetProperty("actualStrategy").GetString());
            }

            if (mode == "Hybrid")
            {
                Assert.Contains(result.GetProperty("actualStrategy").GetString(), new[] { "Exact", "Sampled" });
            }

            var play = await (await _client.PostAsJsonAsync("/api/play/round", new { config })).Content
                .ReadFromJsonAsync<JsonElement>();
            Assert.Equal(play.GetProperty("configHash").GetString(), result.GetProperty("configHash").GetString());
        }
    }

    [Fact]
    public async Task FullGame_ExactAndAutoReturnTheExactRationalRtpWithoutEnumeration()
    {
        JsonElement config = Fixture("dog-house-ui.json");

        static (BigInteger N, BigInteger D) Parse(string value)
        {
            return (BigInteger.Parse(value.Split('/')[0]),
                BigInteger.Parse(value.Split('/')[1]));
        }

        (BigInteger N, BigInteger D) expected =
            Parse(Fixture("dog-house-independent-expectation.json").GetProperty("rationalRtp").GetString()!);
        foreach (var mode in new[] { "Exact", "Hybrid" })
        {
            HttpResponseMessage response = await _client.PostAsJsonAsync("/api/evaluate/graph",
                new { config, mode, samples = 1000, seed = 42 });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Complete", result.GetProperty("status").GetString());
            Assert.Equal("Exact", result.GetProperty("provenance").GetString());
            Assert.Equal("Exact", result.GetProperty("actualStrategy").GetString());
            Assert.Equal("structured", result.GetProperty("method").GetString());
            (BigInteger N, BigInteger D) actual = Parse(result.GetProperty("rationalRtp").GetString()!);
            Assert.Equal(expected.N * actual.D, actual.N * expected.D);
            Assert.Equal(result.GetProperty("rtp").GetDouble(), result.GetProperty("lower").GetDouble());
            Assert.Equal(result.GetProperty("rtp").GetDouble(), result.GetProperty("upper").GetDouble());
            // The payout distribution of the full round is too large to build: its metrics are absent, never invented.
            // The hit frequency does not need it and is exact.
            Assert.Equal("NotBuilt", result.GetProperty("distribution").GetString());
            Assert.Equal("9743/35280", result.GetProperty("rationalHitFrequency").GetString());
            Assert.Equal(9743.0 / 35280.0, result.GetProperty("hitFrequency").GetDouble(), 15);
            Assert.Equal(JsonValueKind.Null, result.GetProperty("variance").ValueKind);
            Assert.Equal(JsonValueKind.Null, result.GetProperty("maxObserved").ValueKind);
            Assert.Equal(0, result.GetProperty("samples").GetInt64());
        }
    }

    [Fact]
    public async Task MissingExpression_RejectsExecutionInsteadOfReturningZero()
    {
        JsonNode graph = JsonNode.Parse(Fixture("dog-house-ui.json").GetRawText())!;
        graph["expressions"]!.AsObject().Remove("credits");
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/play/round", new { config = graph });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Graph validation failed", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("\"win\":0", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Play_RejectsUnsafeReplayIndex()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/play/round",
            new { config = Fixture("dog-house-ui.json"), roundIndex = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
