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
public sealed class ParentExposureApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("auto")]
    [InlineData("reference")]
    public async Task LiveFinalRetainedAndEnumeratedParentsUseTheSameAuthoredChildPopulation(string engine)
    {
        // Three children inside one feature per paid round. The empty round
        // sum still produces a numeric identity without a matching child.
        var graph = JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "measurement-model.json")), SlotMath.Core.JsonOptions.Default)!;
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var store = factory.Services.GetRequiredService<InMemoryRunStore>();
        var configId = configs.Create(graph);
        MeasurementDefinition[] plan = [
            new() { Id = "episode", Name = "Owning feature parents", NodeId = "end", Options = new() { Source = "count", Subject = "episode", Reduction = "count", EntryNodeId = "fs", ExitNodeId = "sink", Group = new ConstantExpr { Kind = ConstantKind.String, Value = "sticky" } } },
            new() { Id = "empty", Name = "Empty round identity", NodeId = "end", Options = new() { Source = "count", Subject = "round" }, Filter = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "false" } } ];
        var run = store.Create(configId, 42, 1, 100, CanonicalHash.Compute(configs.GetVersion(configId, 1)!.Config), 2, plan, new() { SamplingEngine = engine });
        await factory.Services.GetRequiredService<RunJobService>().ExecuteRunAsync(run.Id, 100, 16);
        var client = factory.CreateClient(); var completed = (await client.GetFromJsonAsync<RunResponse>($"/api/runs/{run.Id}"))!;
        Assert.Equal("completed", completed.Status); var metrics = completed.Progress!.Measurements;
        Assert.Equal(100, metrics[0].Count); Assert.Equal(3, metrics[0].Mean);
        Assert.Equal(new ParentExposure(100, 100), metrics[0].Analysis!.ParentExposure);
        Assert.Equal(new ParentExposure(100, 100), metrics[0].Analysis!.Groups["sticky"].ParentExposure);
        Assert.Equal(100, metrics[1].Count); Assert.Equal(0, metrics[1].Mean);
        Assert.Equal(new ParentExposure(0, null), metrics[1].Analysis!.ParentExposure);
        var retained = (await client.GetFromJsonAsync<RunEvidence>($"/api/runs/{run.Id}/evidence"))!; Assert.True(retained.InputVerified);
        Assert.Equal(JsonSerializer.Serialize(metrics), JsonSerializer.Serialize(retained.Run.Progress!.Measurements));
        using var final = JsonDocument.Parse(completed.ResultJson!);
        Assert.Equal(100, final.RootElement.GetProperty("measurements")[0].GetProperty("analysis").GetProperty("parentExposure").GetProperty("episodesWithMatchingChildren").GetInt64());
        var response = await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/reference", new EnumerationBudget()); response.EnsureSuccessStatusCode();
        using var reference = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var law = reference.RootElement.GetProperty("report").GetProperty("measurements")[0].GetProperty("parentExposure");
        Assert.Equal("1/1", law.GetProperty("knownMatchingPaidRoundsPerRound").GetString());
        Assert.Equal("1/1", law.GetProperty("knownMatchingEpisodesPerRound").GetString()); Assert.True(law.GetProperty("complete").GetBoolean());
    }
}
