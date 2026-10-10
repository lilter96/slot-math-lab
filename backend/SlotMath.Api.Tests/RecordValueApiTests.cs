using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SlotMath.Api.Features.Runs;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

namespace SlotMath.Api.Tests;

[Collection("SerialTests")]
public sealed class RecordValueApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static GraphConfig Graph() => JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "record-model.json")), SlotMath.Core.JsonOptions.Default)!;
    private static FieldAccessExpr Field(params string[] path) => new() { Target = "state", Path = path };

    [Fact]
    public async Task InvalidScalarSuffixCaseAndNullReadsAreRejectedBeforeRunAdmission()
    {
        var client = factory.CreateClient();
        foreach (var path in new[] { new[] { "selected", "money", "award", "suffix" }, new[] { "selected", "money", "absent" },
            new[] { "selected", "money", "unused" }, new[] { "Selected", "money", "award" } })
        {
            var original = Graph(); var graph = original with { Expressions = new(original.Expressions!) { ["payout"] = Field(path) } };
            var schema = await client.PostAsJsonAsync("/api/runs/measurements/schema", new { config = graph }, SlotMath.Core.JsonOptions.Default);
            Assert.Equal(HttpStatusCode.BadRequest, schema.StatusCode);
            var config = factory.Services.GetRequiredService<InMemoryConfigStore>().Create(graph);
            var launch = await client.PostAsJsonAsync("/api/runs/", new CreateRunRequest { ConfigId = config, SampleSize = 100 });
            Assert.Equal(HttpStatusCode.BadRequest, launch.StatusCode);
            Assert.Contains("EXPRESSION_TYPE_ERROR", await launch.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task FilteredRecordsRetainTheirPayoutAndNestedArrayMetricsAcrossBothEnginesAndEvidence()
    {
        var graph = Graph();
        MeasurementDefinition[] plan = [new() { Id = "award", Name = "Selected record award", NodeId = "sink", Value = Field("selected", "money", "award") },
            new() { Id = "history", Name = "Nested history", NodeId = "sink", Value = Field("selected", "history", "0", "0") }];
        var client = factory.CreateClient();
        var schema = await client.PostAsJsonAsync("/api/runs/measurements/schema", new { config = graph, measurements = plan.Select(MeasurementInput.FromCore) }, SlotMath.Core.JsonOptions.Default);
        Assert.True(schema.IsSuccessStatusCode, await schema.Content.ReadAsStringAsync());
        var fields = (await schema.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fields");
        var leaf = fields.EnumerateArray().Single(field => field.GetProperty("name").GetString() == "selected.money.award");
        Assert.Equal(new[] { "selected", "money", "award" }, leaf.GetProperty("path").EnumerateArray().Select(p => p.GetString()).ToArray());
        Assert.Equal("Number", leaf.GetProperty("type").GetString());
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var config = configs.Create(graph);
        var store = factory.Services.GetRequiredService<InMemoryRunStore>();
        string? previous = null;
        foreach (var engine in new[] { "auto", "reference" })
        {
            var run = store.Create(config, 817, totalSamples: 100, configHash: CanonicalHash.Compute(configs.GetVersion(config, 1)!.Config), measurements: plan, execution: new() { SamplingEngine = engine });
            await factory.Services.GetRequiredService<RunJobService>().ExecuteRunAsync(run.Id, 100, 16);
            var completed = store.Get(run.Id)!; Assert.Equal("completed", completed.Status);
            var award = completed.Progress!.Measurements[0]; Assert.Equal(100, award.Count); Assert.Equal(0, award.Errors);
            Assert.Equal(.25, award.Min); Assert.Equal(1.75, award.Max); Assert.Equal(completed.Progress.RunningRtp, award.Mean);
            var history = completed.Progress.Measurements[1]; Assert.Equal(1, history.Min); Assert.Equal(3, history.Max); Assert.Equal(0, history.Errors);
            var current = JsonSerializer.Serialize(completed.Progress.Measurements);
            if (previous is not null) Assert.Equal(previous, current);
            previous = current;
            var evidence = (await client.GetFromJsonAsync<RunEvidence>($"/api/runs/{run.Id}/evidence"))!;
            Assert.True(evidence.InputVerified);
            Assert.Equal(.25m, evidence.PinnedConfig!.Value.GetProperty("initialState").GetProperty("offers")[0].GetProperty("money").GetProperty("award").GetDecimal());
            Assert.Equal(JsonValueKind.Null, evidence.PinnedConfig.Value.GetProperty("initialState").GetProperty("offers")[0].GetProperty("money").GetProperty("unused").ValueKind);
            Assert.Equal(100, evidence.Run.Progress!.Measurements[0].Count);
        }
    }
}
