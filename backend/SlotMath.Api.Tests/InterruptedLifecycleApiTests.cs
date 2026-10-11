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
public sealed class InterruptedLifecycleApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("auto")]
    [InlineData("reference")]
    public async Task RuntimeFailureRetainsTypedLifecycleExposureWithoutSettlingTheFeature(string engine)
    {
        // Independently specified fixture: enter feature, write type, divide by zero before
        // the first reveal and exit. Exactly one failed paid round and one censored instance.
        var graph = JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "interrupted-feature-model.json")), SlotMath.Core.JsonOptions.Default)!;
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var store = factory.Services.GetRequiredService<InMemoryRunStore>();
        var id = configs.Create(graph);
        MeasurementDefinition[] plan = [new() { Id = "feature", Name = "Interrupted feature", NodeId = "end",
            Options = new() { Subject = "episode", Source = "count", Reduction = "sum", EntryNodeId = "fs", ExitNodeId = "sink",
                Group = new ConstantExpr { Kind = ConstantKind.String, Value = "sticky" } } }];
        var run = store.Create(id, 42, 1, 100, CanonicalHash.Compute(configs.GetVersion(id, 1)!.Config), 1, plan, new() { SamplingEngine = engine });
        await factory.Services.GetRequiredService<RunJobService>().ExecuteRunAsync(run.Id, 100, 16);
        var client = factory.CreateClient(); var failed = (await client.GetFromJsonAsync<RunResponse>($"/api/runs/{run.Id}"))!;
        Assert.Equal("failed", failed.Status); Assert.Equal(0, failed.Progress!.SampleCount);
        Assert.Equal(1, failed.Progress.Execution!.AttemptedRounds); Assert.Equal(1, failed.Progress.Execution.FailedRounds);
        var metric = Assert.Single(failed.Progress.Measurements);
        Assert.Equal(0, metric.Count); Assert.Equal(0, metric.Observations); Assert.Equal(0, metric.Errors); Assert.Null(metric.Mean);
        var analysis = metric.Analysis!;
        Assert.Equal(new FeatureLifecycleCounts(1, 1, 0, 1, true), analysis.InterruptedLifecycle!.Failed);
        Assert.Equal(analysis.InterruptedLifecycle, analysis.Groups["sticky"].InterruptedLifecycle);
        Assert.Equal(0, analysis.Entries); Assert.Equal(0, analysis.Exits); Assert.Null(analysis.Normalization);
        var evidence = (await client.GetFromJsonAsync<RunEvidence>($"/api/runs/{run.Id}/evidence"))!;
        Assert.True(evidence.InputVerified);
        Assert.Equal(JsonSerializer.Serialize(metric), JsonSerializer.Serialize(evidence.Run.Progress!.Measurements[0]));
        Assert.NotNull(evidence.Run.RuntimeProvenance!.CoreBinarySha256);
        Assert.Contains("zero", failed.ResultJson!, StringComparison.OrdinalIgnoreCase);
    }
}
