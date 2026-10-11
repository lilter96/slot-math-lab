using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SlotMath.Api.Features.Runs;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
namespace SlotMath.Api.Tests;

[Collection("SerialTests")]
public sealed class VerificationExperimentApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static GraphConfig Graph() => JsonSerializer.Deserialize<GraphConfig>("""
    {"schemaVersion":"1.0.0","initialState":{"parameter":2},"nodes":[
      {"nodeType":"modifyState","id":"award","outputKey":"payout","expressionId":"read","outputs":{"out":{"name":"out","type":"State"}}},
      {"nodeType":"metricsSink","id":"sink","winCap":100,"winStateKey":"payout","inputs":{"in":{"name":"in","type":"State"}}}],
      "edges":[{"id":"e","sourceNodeId":"award","sourcePort":"out","targetNodeId":"sink","targetPort":"in"}],
      "expressions":{"read":{"exprType":"fieldAccess","target":"state","path":["parameter"]}}}
    """, SlotMath.Core.JsonOptions.Default)!;
    [Fact]
    public async Task AutomaticExperimentPinsAndExecutesNamedPerturbationWithIndependentSeedsAndSensitivity()
    {
        using var client = factory.CreateClient();
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var runs = factory.Services.GetRequiredService<InMemoryRunStore>();
        var id = configs.Create(Graph());
        var response = await client.PostAsJsonAsync("/api/runs/experiments", new ExperimentRequest(id, 1, [new("Higher award", "initialState", "parameter", 3)], [], Samples: 100));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var manifest = (await response.Content.ReadFromJsonAsync<ExperimentEntry>())!;
        Assert.Equal(64, manifest.ManifestSha256.Length); Assert.Equal(2, manifest.Members.Length);
        Assert.Equal(42, manifest.Members[0].Seed); Assert.Equal(43, manifest.Members[1].Seed); Assert.NotEqual(manifest.Members[0].ConfigHash, manifest.Members[1].ConfigHash);
        // The real orchestration service is idempotent: an already running/terminal
        // job cannot be executed a second time. Wait for the admitted Hangfire job.
        ExperimentReport? report = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!deadline.IsCancellationRequested)
        {
            report = await client.GetFromJsonAsync<ExperimentReport>($"/api/runs/experiments/{manifest.Id}", deadline.Token);
            if (report!.Experiment.Status is not ("pending" or "running")) break;
            await Task.Delay(50, deadline.Token);
        }
        Assert.Equal("completed", report!.Experiment.Status); Assert.All(report.Runs, r => Assert.Equal("completed", r.Status));
        var delta = report.Deltas.Single(d => d.Metric == "round.return"); Assert.Equal(1, delta.Difference); Assert.Equal(1, delta.Derivative); Assert.Null(delta.Interval);
        Assert.Contains(runs.Get(manifest.Members[0].RunId)!.Diagnostics, d => d.Kind == "parameter-experiment");
        var before = runs.Get(manifest.Members[1].RunId)!.Sequence;
        await factory.Services.GetRequiredService<ExperimentJobService>().ExecuteAsync(manifest.Id);
        Assert.Equal(before, runs.Get(manifest.Members[1].RunId)!.Sequence);
    }
    [Fact]
    public async Task StoppedSessionExperimentComparesPayoutToActualTurnoverAcrossDifferentDurations()
    {
        using var client = factory.CreateClient();
        var id = factory.Services.GetRequiredService<InMemoryConfigStore>().Create(Graph());
        var execution = new ExecutionOptions { Regime = "sessions", SessionLength = 10, SessionStop = "profitTarget", StopThreshold = 2, InitialBankroll = 10, Wager = 1 };
        var response = await client.PostAsJsonAsync("/api/runs/experiments", new ExperimentRequest(id, 1,
            [new("Double wager", "execution", "wager", 2)], [], Execution: execution, Samples: 20, MetricIds: ["session.payoutPerTurnover"]));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var manifest = (await response.Content.ReadFromJsonAsync<ExperimentEntry>())!;
        ExperimentReport? report = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!deadline.IsCancellationRequested)
        {
            report = await client.GetFromJsonAsync<ExperimentReport>($"/api/runs/experiments/{manifest.Id}", deadline.Token);
            if (report!.Experiment.Status is not ("pending" or "running")) break;
            await Task.Delay(50, deadline.Token);
        }
        Assert.Equal("completed", report!.Experiment.Status);
        Assert.Equal(4, report.Runs[0]!.Progress!.SampleCount); Assert.Equal(20, report.Runs[1]!.Progress!.SampleCount);
        var delta = report.Deltas.Single(d => d.Metric == "session.payoutPerTurnover.ratio");
        Assert.Equal(2, delta.Baseline); Assert.Equal(1, delta.Observed); Assert.Equal(-1, delta.Difference); Assert.Equal(-1, delta.Derivative); Assert.Null(delta.Interval);
    }
    [Fact]
    public async Task EveryCandidateIsValidatedBeforeAnyExperimentRunIsCreated()
    {
        using var client = factory.CreateClient(); var configs = factory.Services.GetRequiredService<InMemoryConfigStore>();
        var id = configs.Create(Graph()); var experiments = factory.Services.GetRequiredService<ExperimentStore>(); var before = experiments.List().Length;
        var response = await client.PostAsJsonAsync("/api/runs/experiments", new ExperimentRequest(id, 1, [new("Invalid cap", "winCap", "winCap", 0, "sink")], [], Samples: 10));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, experiments.List().Length);
    }
    [Theory]
    [InlineData("ruin", .5, 0)]
    [InlineData("ruin", 2, 4)]
    [InlineData("profitTarget", 10, 2)]
    public async Task SessionPolicyCompletionIsRetainedAsCompletedWithActualTurnover(string policy, double bank, long expectedRounds)
    {
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var runs = factory.Services.GetRequiredService<InMemoryRunStore>();
        var graph = policy == "ruin" ? ExperimentAnalysis.Apply(Graph(), new("Loss", "initialState", "parameter", 0)) : Graph();
        var id = configs.Create(graph); var execution = new ExecutionOptions { Regime = "sessions", SessionLength = 10, SessionStop = policy, InitialBankroll = bank, Wager = 1, StopThreshold = 1 };
        var run = runs.Create(id, totalSamples: 20, configHash: CanonicalHash.Compute(configs.GetVersion(id, 1)!.Config), execution: execution);
        await factory.Services.GetRequiredService<RunJobService>().ExecuteRunAsync(run.Id, 20, 16);
        var complete = runs.Get(run.Id)!; Assert.Equal("completed", complete.Status); Assert.Equal(expectedRounds, complete.Progress!.SampleCount);
        Assert.Equal(2, complete.Progress.Execution!.CompletedSessions); Assert.Equal(20, complete.Progress.TotalSamples);
        using var result = JsonDocument.Parse(complete.ResultJson!); Assert.Equal("completed", result.RootElement.GetProperty("status").GetString());
    }
    [Fact]
    public async Task VerifiedDesignAndResourceComparisonRetainHashedEvidenceOnTheirSelectedRun()
    {
        using var client = factory.CreateClient(); var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var runs = factory.Services.GetRequiredService<InMemoryRunStore>();
        var id = configs.Create(Graph()); var run = runs.Create(id, totalSamples: 100, configHash: CanonicalHash.Compute(configs.GetVersion(id, 1)!.Config));
        var resource = await client.PostAsJsonAsync($"/api/runs/measurements/resource-impact?runId={run.Id}", new ResourceImpactRequest(new([["1/2"]], ["2"]), 3));
        Assert.True(resource.IsSuccessStatusCode, await resource.Content.ReadAsStringAsync());
        var report = (await resource.Content.ReadFromJsonAsync<DesignEvidence<ResourceImpactReport>>())!; Assert.Equal("1/2", report.Report.OmittedReward); Assert.True(report.Retention!.Retained);
        var design = await client.PostAsJsonAsync($"/api/runs/measurements/sampling-design?runId={run.Id}", new SamplingDesignRequest([new("loss", "0", "3/4", "1/2"), new("win", "4", "1/4", "1/2")], Samples: 100));
        Assert.True(design.IsSuccessStatusCode, await design.Content.ReadAsStringAsync());
        Assert.Contains(runs.Get(run.Id)!.Diagnostics, d => d.Kind == "resource-impact"); Assert.Contains(runs.Get(run.Id)!.Diagnostics, d => d.Kind == "sampling-design");
    }
}
