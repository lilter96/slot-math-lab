using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SlotMath.Api.Features.Runs;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Model;
using SlotMath.Core.Measurements;
namespace SlotMath.Api.Tests;
[Collection("SerialTests")]
public class MeasurementApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;
    public MeasurementApiTests(WebApplicationFactory<Program> factory) { this.factory = factory; client = factory.CreateClient(); }
    private static GraphConfig Coin() => JsonSerializer.Deserialize<GraphConfig>("""
    {"schemaVersion":"1.0.0","name":"Measurement API coin","nodes":[{"nodeType":"draw","id":"draw","drawWeights":[{"outcomeId":"zero","value":0,"weight":1},{"outcomeId":"two","value":2,"weight":1}],"outputs":{"out":{"name":"out","type":"Wins"}}},{"nodeType":"metricsSink","id":"sink","winCap":1,"inputs":{"in":{"name":"in","type":"Wins"}}}],"edges":[{"id":"e","sourceNodeId":"draw","sourcePort":"out","targetNodeId":"sink","targetPort":"in"}]}
    """, SlotMath.Core.JsonOptions.Default)!;
    [Fact]
    public async Task PlanPinsStreamsTerminalEvidenceAndReplay()
    {
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var store = factory.Services.GetRequiredService<InMemoryRunStore>();
        var configId = configs.Create(Coin()); MeasurementDefinition[] plan = [new() { Id = "settled", Name = "Settled payout", Unit = "× stake" }];
        var entry = store.Create(configId, 42, 1, 1000, CanonicalHash.Compute(configs.GetVersion(configId, 1)!.Config), 2, plan);
        var queued = await client.GetFromJsonAsync<RunResponse>($"/api/runs/{entry.Id}");
        Assert.Single(queued!.Measurements); Assert.Equal(entry.MeasurementHash, queued.MeasurementHash);
        Assert.Equal(entry.MeasurementHash, queued.Progress!.MeasurementHash); Assert.Equal(0, Assert.Single(queued.Progress.Measurements).Count);
        await factory.Services.GetRequiredService<RunJobService>().ExecuteRunAsync(entry.Id, 1000, 16);
        var completed = await client.GetFromJsonAsync<RunResponse>($"/api/runs/{entry.Id}"); var metric = Assert.Single(completed!.Progress!.Measurements);
        Assert.Equal("completed", completed.Status); Assert.Equal(1000, metric.Count); Assert.Equal(1000, metric.Observations);
        Assert.Equal(completed.Progress.RunningRtp, metric.Mean!.Value, 12); Assert.Equal(1, metric.Max); Assert.Equal(0, metric.Errors);
        using var result = JsonDocument.Parse(completed.ResultJson!);
        Assert.Equal(completed.MeasurementHash, result.RootElement.GetProperty("measurementHash").GetString());
        Assert.Equal(metric.Count, result.RootElement.GetProperty("measurements")[0].GetProperty("count").GetInt64());
        var evidence = await client.GetFromJsonAsync<JsonElement>($"/api/runs/{entry.Id}/evidence");
        Assert.Equal("settled", evidence.GetProperty("run").GetProperty("measurements")[0].GetProperty("id").GetString());
        var response = await client.PostAsJsonAsync("/api/runs", new { configId, configVersion = 1, seed = 42, sampleSize = 1000, degreeOfParallelism = 1, measurements = plan });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); var replay = await response.Content.ReadFromJsonAsync<RunResponse>();
        Assert.Equal(completed.MeasurementHash, replay!.MeasurementHash);
    }
    [Fact]
    public async Task UnknownPointOrInvalidValueRejectedBeforeQueue()
    {
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var id = configs.Create(Coin());
        var invalid = new MeasurementDefinition { Id = "wrong", Name = "Unknown", NodeId = "missing", Value = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/runs", new { configId = id, measurements = new[] { invalid } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/runs", new { configId = id, measurements = (object?)null })).StatusCode);
    }
    [Fact]
    public async Task SchemaIncludesInlinedNodesAndDerivedFieldTypes()
    {
        var fixture = JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "DogHouse", "dog-house-mini-ui.json")), SlotMath.Core.JsonOptions.Default)!;
        var response = await client.PostAsJsonAsync("/api/runs/measurements/schema", new { config = JsonSerializer.SerializeToElement(fixture, SlotMath.Core.JsonOptions.Default) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var schema = await response.Content.ReadFromJsonAsync<MeasurementSchema>(); Assert.NotEmpty(schema!.Points); Assert.NotEmpty(schema.Fields);
        Assert.Contains(schema.Points, p => p.NodeId.Contains('/')); Assert.Contains(schema.Fields, f => f.Name == "spinCoins" && f.Type == "Number");
    }
}
