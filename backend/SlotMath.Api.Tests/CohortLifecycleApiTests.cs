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
public sealed class CohortLifecycleApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory][InlineData("auto")][InlineData("reference")]
    public async Task EmptyFeatureCohortsKeepLifecycleCountsInFinalAndRetainedApiEvidence(string engine)
    {
        var graph = JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "measurement-model.json")), SlotMath.Core.JsonOptions.Default)!;
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var store = factory.Services.GetRequiredService<InMemoryRunStore>();
        var id = configs.Create(graph);
        MeasurementDefinition[] plan = [new() { Id = "empty", Name = "Empty feature", NodeId = "end",
            Value = new FieldAccessExpr { Target = "state", Path = ["spinWin"] }, Filter = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "false" },
            Options = new() { Subject = "episode", Reduction = "average", EntryNodeId = "fs", ExitNodeId = "sink", Group = new ConstantExpr { Kind = ConstantKind.String, Value = "empty" } } }];
        var run = store.Create(id, 42, 1, 100, CanonicalHash.Compute(configs.GetVersion(id, 1)!.Config), 2, plan, new() { SamplingEngine = engine });
        await factory.Services.GetRequiredService<RunJobService>().ExecuteRunAsync(run.Id, 100, 16);
        var client = factory.CreateClient(); var completed = (await client.GetFromJsonAsync<RunResponse>($"/api/runs/{run.Id}"))!;
        Assert.Equal("completed", completed.Status); var metric = Assert.Single(completed.Progress!.Measurements);
        Assert.Equal(0, metric.Count); Assert.Equal(100, metric.Excluded); Assert.Equal(0, metric.Errors); Assert.Null(metric.Mean);
        var cohort = metric.Analysis!.Groups["empty"]; Assert.Equal(100, cohort.Entries); Assert.Equal(100, cohort.Exits); Assert.Equal(0, cohort.UnclosedEpisodes);
        Assert.Equal(100, cohort.Normalization!.PaidRounds); Assert.Equal(0, cohort.Count); Assert.Null(cohort.Mean);
        Assert.All(cohort.Tails, t => { Assert.Null(t.Probability); Assert.Null(t.SecondMoment); });
        var retained = (await client.GetFromJsonAsync<RunEvidence>($"/api/runs/{run.Id}/evidence"))!;
        Assert.True(retained.InputVerified);
        Assert.Equal(JsonSerializer.Serialize(metric), JsonSerializer.Serialize(retained.Run.Progress!.Measurements[0]));
        using var final = JsonDocument.Parse(completed.ResultJson!);
        Assert.Equal(100, final.RootElement.GetProperty("measurements")[0].GetProperty("analysis").GetProperty("groups").GetProperty("empty").GetProperty("entries").GetInt64());
    }
}
