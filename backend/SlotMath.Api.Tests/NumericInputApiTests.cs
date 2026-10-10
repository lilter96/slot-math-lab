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
public sealed class NumericInputApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static GraphConfig Graph() => JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "numeric-input-model.json")), SlotMath.Core.JsonOptions.Default)!;
    [Fact]
    public async Task NativeDecimalInputPreflightsBothEnginesAndRetainsExactZeroProfileAndNonzeroMetrics()
    {
        MeasurementDefinition[] plan = [new() { Id = "signal", Name = "Tiny signal", NodeId = "sink", Value = new FieldAccessExpr { Target = "state", Path = ["signal"] } },
            new() { Id = "residual", Name = "Exact decimal residual", NodeId = "sink", Value = new BinaryExpr { Op = BinaryOp.Sub, Left = new FieldAccessExpr { Target = "state", Path = ["signal"] }, Right = new ConstantExpr { Kind = ConstantKind.Rational, Value = "1/1000000000" } }, Options = new() { Assertion = "zero" } }];
        var profile = new VerificationProfile("Decimal input identity", .95, [new("signal", "observation-integrity", 100), new("residual", "exact-zero-assertion", 100)]);
        var graph = Graph(); var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var config = configs.Create(graph);
        var client = factory.CreateClient(); var store = factory.Services.GetRequiredService<InMemoryRunStore>();
        var schema = await client.PostAsJsonAsync("/api/runs/measurements/schema", new { config = graph, measurements = plan.Select(MeasurementInput.FromCore), verificationProfile = profile }, SlotMath.Core.JsonOptions.Default);
        Assert.True(schema.IsSuccessStatusCode, await schema.Content.ReadAsStringAsync());
        foreach (var engine in new[] { "auto", "reference" })
        {
            var run = store.Create(config, 817, totalSamples: 100, configHash: CanonicalHash.Compute(configs.GetVersion(config, 1)!.Config), measurements: plan, execution: new() { SamplingEngine = engine }, verificationProfile: profile);
            await factory.Services.GetRequiredService<RunJobService>().ExecuteRunAsync(run.Id, 100, 16);
            var completed = store.Get(run.Id)!; Assert.Equal("completed", completed.Status);
            var signal = completed.Progress!.Measurements[0]; Assert.Equal(100, signal.Count); Assert.Equal(1e-9, signal.Mean); Assert.Equal(1e-9, signal.Min); Assert.Equal(1e-9, signal.Max); Assert.Equal(0, signal.Errors);
            Assert.Equal(1, completed.Progress.RunningRtp);
            var response = await client.PostAsJsonAsync($"/api/runs/{run.Id}/verification", new { }); response.EnsureSuccessStatusCode();
            var report = (await response.Content.ReadFromJsonAsync<RetainedReference<RunVerificationEvidence>>())!;
            Assert.Equal("criteriaMet", report.Report.Report.Status); Assert.Equal(run.VerificationProfileHash, report.Report.Report.ProfileHash);
            var evidence = (await client.GetFromJsonAsync<RunEvidence>($"/api/runs/{run.Id}/evidence"))!; Assert.True(evidence.InputVerified);
            Assert.Equal(.000000001m, evidence.PinnedConfig!.Value.GetProperty("initialState").GetProperty("signal").GetDecimal());
            Assert.Equal(1e-9, evidence.Run.Progress!.Measurements[0].Mean); Assert.Equal(100, report.Report.Source.PaidRounds);
        }
    }
    [Fact]
    public async Task UnboundedDecimalExponentIsRejectedBeforeExecutionAdmission()
    {
        var graph = Graph() with { InitialState = new() { ["signal"] = JsonDocument.Parse("1e4097").RootElement.Clone(), ["payout"] = JsonDocument.Parse("1").RootElement.Clone() } };
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/runs/measurements/schema", new { config = graph }, SlotMath.Core.JsonOptions.Default);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Contains("EVAL_NUMERIC_BUDGET", await response.Content.ReadAsStringAsync());
        var config = factory.Services.GetRequiredService<InMemoryConfigStore>().Create(graph);
        var launch = await client.PostAsJsonAsync("/api/runs/", new CreateRunRequest { ConfigId = config, SampleSize = 100 });
        Assert.Equal(HttpStatusCode.BadRequest, launch.StatusCode); Assert.Contains("EVAL_NUMERIC_BUDGET", await launch.Content.ReadAsStringAsync());
    }
}
