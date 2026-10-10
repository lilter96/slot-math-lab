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
public sealed class ComponentAccountingApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static Expression Field(string name) => new FieldAccessExpr { Target = "state", Path = [name] };
    private static MeasurementDefinition D(string id, Expression value, Expression? pair = null, bool assertion = false) => new()
    {
        Id = id,
        Name = id,
        NodeId = "sink",
        Value = value,
        Unit = "coins",
        Options = new() { Pair = pair, Assertion = assertion ? "zero" : "none", Group = Field("cohort"), Stake = 2, SupportLimit = 32, GroupLimit = 1 }
    };
    private static MeasurementDefinition[] Plan() => [D("total", Field("total")), D("x", Field("x")), D("y", Field("y")), D("z", Field("z")),
        D("xy", Field("x"), Field("y")), D("xz", Field("x"), Field("z")), D("yz", Field("y"), Field("z")),
        D("residual", new BinaryExpr { Op = BinaryOp.Sub, Left = Field("total"), Right = new BinaryExpr { Op = BinaryOp.Add,
            Left = new BinaryExpr { Op = BinaryOp.Add, Left = Field("x"), Right = Field("y") }, Right = Field("z") } }, assertion: true)];

    [Fact]
    public async Task PinnedThreeComponentAccountingReconcilesBothEnginesAndRetainsProducerIdentity()
    {
        var graph = JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "component-accounting-model.json")), SlotMath.Core.JsonOptions.Default)!;
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var store = factory.Services.GetRequiredService<InMemoryRunStore>();
        var configId = configs.Create(graph); var client = factory.CreateClient(); var plan = Plan();
        MeasurementSnapshot[]? previous = null;
        foreach (var engine in new[] { "auto", "reference" })
        {
            var run = store.Create(configId, 42, 1, 100, CanonicalHash.Compute(graph), 2, plan, new() { SamplingEngine = engine });
            var request = new ComponentAccountingRequest("Three components", "total", ["x", "y", "z"], "residual", "all");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/accounting", request)).StatusCode);
            await factory.Services.GetRequiredService<RunJobService>().ExecuteRunAsync(run.Id, 100, 16);
            var completed = store.Get(run.Id)!; Assert.Equal("completed", completed.Status);
            var response = await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/accounting", request); response.EnsureSuccessStatusCode();
            var body = (await response.Content.ReadFromJsonAsync<RetainedReference<MeasurementAccountingReport>>())!;
            var report = body.Report.Report; Assert.Equal("noObservedViolations", report.Status); Assert.Equal(100, report.Count);
            Assert.True(report.TotalSampleVariance > 0); Assert.Equal(3 * report.TotalSampleVariance!.Value, report.ComponentVarianceSum!.Value, 10);
            Assert.Equal(-2 * report.TotalSampleVariance.Value, report.TwiceCovarianceSum!.Value, 10);
            Assert.Equal(report.TotalSampleVariance.Value, report.ReconstructedVariance!.Value, 10); Assert.Equal(0, report.ExactViolations);
            Assert.True(body.Retention.Retained); Assert.Equal(completed.Sequence, body.Report.Source.Sequence); Assert.Equal(completed.RuntimeProvenance, body.Report.Source.Producer);
            Assert.Equal(100, body.Report.Source.PaidRounds); Assert.Equal(completed.MeasurementHash, body.Report.Source.MeasurementHash);
            var archive = (await client.GetFromJsonAsync<RunEvidence>($"/api/runs/{run.Id}/evidence"))!; var retained = Assert.Single(archive.Diagnostics);
            Assert.Equal("component-accounting", retained.Kind); Assert.Equal(completed.ConfigHash, retained.ConfigHash);
            Assert.Equal(64, retained.OutputSha256.Length); Assert.Equal("component-accounting-v1", retained.Output.GetProperty("report").GetProperty("algorithmVersion").GetString());
            var repeat = (await (await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/accounting", request)).Content.ReadFromJsonAsync<RetainedReference<MeasurementAccountingReport>>())!;
            Assert.Equal(body.Retention.ArtifactId, repeat.Retention.ArtifactId); Assert.Single(store.Get(run.Id)!.Diagnostics);
            if (previous is not null) Assert.Equal(JsonSerializer.Serialize(previous), JsonSerializer.Serialize(completed.Progress!.Measurements));
            previous = completed.Progress!.Measurements.ToArray();
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/accounting", request with { ComponentMeasurementIds = ["x", "y"] })).StatusCode);
        }
    }
}
