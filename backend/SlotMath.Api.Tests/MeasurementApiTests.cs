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
    [Fact]
    public async Task ExactLawComparisonRetainsDifferentEqualMeanLawsAndRejectsIncompleteEvidence()
    {
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var store = factory.Services.GetRequiredService<InMemoryRunStore>();
        var configId = configs.Create(Coin()); var run = store.Create(configId, 42, 1, 1, CanonicalHash.Compute(configs.GetVersion(configId, 1)!.Config));
        var input = new ExactLawComparisonRequest([new("0", "1/2"), new("1.96", "1/2")], [new("0", "9/10"), new("9.8", "1/10")]);
        var response = await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/reference/comparison", input); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<RetainedReference<ExactLawComparisonReport>>();
        Assert.True(report!.Retention.Retained); Assert.False(report.Report.Equal); Assert.Equal("0/1", report.Report.MeanDifference);
        Assert.Equal("1/2", report.Report.TotalVariation); Assert.Equal("2/5", report.Report.CdfDistance);
        var artifact = Assert.Single(store.Get(run.Id)!.Diagnostics); Assert.Equal("independent-law-comparison", artifact.Kind);
        Assert.Equal(report.Report.AuthoredInputSha256, artifact.InputSha256); Assert.NotNull(report.Report.CoreBinarySha256);
        var rejected = await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/reference/comparison", input with { Right = [new("0", "1/2")] });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); Assert.Single(store.Get(run.Id)!.Diagnostics);
    }
    [Fact]
    public async Task ZeroObservedVarianceDoesNotCertifyAZeroWidthReturnInterval()
    {
        var config = Coin() with { Nodes = Coin().Nodes.Select(n => n is DrawNode d ? d with { DrawWeights = [new() { OutcomeId = "zero", Value = 0, Weight = 1 }] } : n).ToArray() };
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var runs = factory.Services.GetRequiredService<InMemoryRunStore>();
        var id = configs.Create(config); var run = runs.Create(id, 42, 1, 100, CanonicalHash.Compute(configs.GetVersion(id, 1)!.Config));
        await factory.Services.GetRequiredService<RunJobService>().ExecuteRunAsync(run.Id, 100, 16);
        var completed = await client.GetFromJsonAsync<RunResponse>($"/api/runs/{run.Id}"); using var result = JsonDocument.Parse(completed!.ResultJson!);
        Assert.Equal("completed", completed.Status); Assert.Equal(0, completed.Progress!.Volatility); Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("ci95").ValueKind);
    }
    [Fact]
    public async Task IndependentReferenceRetainsItsAuthoredInputAndArtifactWithThePinnedRun()
    {
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var store = factory.Services.GetRequiredService<InMemoryRunStore>();
        var configId = configs.Create(Coin()); var run = store.Create(configId, 42, 1, 1, CanonicalHash.Compute(configs.GetVersion(configId, 1)!.Config));
        var input = new FiniteModelRequest([new("0", "1/2"), new("1.96", "1/2")]);
        var response = await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/reference/distribution", input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<RetainedReference<FiniteModelReport>>(); Assert.Equal("49/50", report!.Report.Mean.Lower); Assert.True(report.Retention.Retained);
        var repeated = await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/reference/distribution", input); Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        var evidence = await client.GetFromJsonAsync<RunEvidence>($"/api/runs/{run.Id}/evidence"); var artifact = Assert.Single(evidence!.Diagnostics);
        Assert.Equal("independent-finite-law", artifact.Kind); Assert.Equal(run.ConfigHash, artifact.ConfigHash);
        Assert.Equal(RuntimeProvenance.AuthoredInputHash(input), artifact.InputSha256); Assert.Equal("1.96", artifact.Input.GetProperty("outcomes")[1].GetProperty("value").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/runs/missing/measurements/reference/distribution", input)).StatusCode);
    }
    private static GraphConfig Coin() => JsonSerializer.Deserialize<GraphConfig>("""
    {"schemaVersion":"1.0.0","name":"Measurement API coin","nodes":[{"nodeType":"draw","id":"draw","drawWeights":[{"outcomeId":"zero","value":0,"weight":1},{"outcomeId":"two","value":2,"weight":1}],"outputs":{"out":{"name":"out","type":"Wins"}}},{"nodeType":"metricsSink","id":"sink","winCap":1,"inputs":{"in":{"name":"in","type":"Wins"}}}],"edges":[{"id":"e","sourceNodeId":"draw","sourcePort":"out","targetNodeId":"sink","targetPort":"in"}]}
    """, SlotMath.Core.JsonOptions.Default)!;
    [Fact]
    public async Task AdvancedPlan_RawSettlementAndFullDistributionSurviveApiEvidenceAndReplay()
    {
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var store = factory.Services.GetRequiredService<InMemoryRunStore>(); var configId = configs.Create(Coin());
        MeasurementDefinition[] plan = [
            new() { Id = "settled", Name = "Settled law", Options = new() { IndependentSubjects = true, ReferenceMean = 0.5, Tolerance = 0.1, ReferenceDistribution = [new(0, 0.5), new(1, 0.5)] } },
            new() { Id = "raw", Name = "Pre-cap award", Options = new() { Source = "rawPayout" } },
            new() { Id = "clip", Name = "Cap deduction", Options = new() { Source = "capDeduction" } } ];
        var entry = store.Create(configId, 42, 1, 1000, CanonicalHash.Compute(configs.GetVersion(configId, 1)!.Config), 2, plan);
        await factory.Services.GetRequiredService<RunJobService>().ExecuteRunAsync(entry.Id, 1000, 16);
        var completed = await client.GetFromJsonAsync<RunResponse>($"/api/runs/{entry.Id}"); Assert.Equal("completed", completed!.Status);
        var settled = completed.Progress!.Measurements[0]; var raw = completed.Progress.Measurements[1]; var clip = completed.Progress.Measurements[2];
        Assert.Equal(settled.Sum!.Value * 2, raw.Sum); Assert.Equal(settled.Sum, clip.Sum); Assert.Equal(settled.Count, raw.Analysis!.Count);
        Assert.True(settled.Analysis!.SupportComplete); Assert.Equal(1000, settled.Analysis.Bins.Sum(b => b.Count)); Assert.NotNull(settled.Analysis.MeanInterval);
        Assert.Equal(0, settled.Analysis.Comparison!.UnexpectedObservations); Assert.Equal("withinPrecision", Assert.Single(settled.Analysis.Checks, c => c.Id == "mean-equivalence").Status);
        Assert.Equal(settled.Analysis.Support.Single(p => p.Value == 1).Count, completed.Progress.CapHits);
        var evidence = await client.GetFromJsonAsync<JsonElement>($"/api/runs/{entry.Id}/evidence");
        Assert.Equal("rawPayout", evidence.GetProperty("run").GetProperty("measurements")[1].GetProperty("options").GetProperty("source").GetString());
        Assert.True(evidence.GetProperty("run").GetProperty("progress").GetProperty("measurements")[0].GetProperty("analysis").GetProperty("supportComplete").GetBoolean());
        var response = await client.PostAsJsonAsync("/api/runs", new { configId, configVersion = 1, seed = 42, sampleSize = 1000, measurements = plan.Select(MeasurementInput.FromCore).ToArray() });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); Assert.Equal(completed.MeasurementHash, (await response.Content.ReadFromJsonAsync<RunResponse>())!.MeasurementHash);
    }
    [Fact]
    public async Task PinnedEnumerationRetainsTheFullCapLawAndConservativeCutMass()
    {
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var store = factory.Services.GetRequiredService<InMemoryRunStore>();
        var id = configs.Create(Coin()); MeasurementDefinition[] plan = [new() { Id = "settled", Name = "Settled law", Options = new() }, new() { Id = "raw", Name = "Raw law", Options = new() { Source = "rawPayout" } }];
        var run = store.Create(id, 42, 1, 10, CanonicalHash.Compute(Coin()), 1, plan);
        var response = await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/reference", new EnumerationBudget()); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var evidence = (await response.Content.ReadFromJsonAsync<GraphMeasurementReference>())!;
        Assert.Equal(run.ConfigHash, evidence.ConfigHash); Assert.Equal(run.MeasurementHash, evidence.MeasurementHash);
        Assert.Equal("Enumerated", evidence.Report.Status); Assert.Equal("1/2", evidence.Report.RoundMean.Lower); Assert.Equal("1/2", evidence.Report.RoundMean.Upper);
        Assert.Equal(new[] { 0d, 2d }, evidence.Report.Measurements.Single(m => m.Id == "raw").Support.Select(p => p.Value));
        var cut = await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/reference", new EnumerationBudget(MaximumPaths: 1));
        var report = (await cut.Content.ReadFromJsonAsync<GraphMeasurementReference>())!.Report;
        Assert.Equal("1/2", report.UnresolvedRoundMass); Assert.Null(report.Measurements[0].ConditionalMean);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/reference", new EnumerationBudget(MaximumPaths: 100001))).StatusCode);
        var carried = store.Create(id, 42, 1, 10, run.ConfigHash, 1, plan, new() { Regime = "persistent", PersistentKeys = ["example"] });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/runs/{carried.Id}/measurements/reference", new EnumerationBudget())).StatusCode);
    }
    [Fact]
    public async Task SessionsArePinnedAndSavedWithCompleteExposureAndAlgorithmIdentity()
    {
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var store = factory.Services.GetRequiredService<InMemoryRunStore>(); var id = configs.Create(Coin());
        var execution = new SlotMath.Core.Math.ExecutionOptions { Regime = "sessions", SessionLength = 5, InitialBankroll = 2, Wager = 1 };
        var run = store.Create(id, 42, 1, 100, CanonicalHash.Compute(Coin()), 2, [], execution);
        await factory.Services.GetRequiredService<RunJobService>().ExecuteRunAsync(run.Id, 100, 16);
        var evidence = (await client.GetFromJsonAsync<RunResponse>($"/api/runs/{run.Id}"))!;
        Assert.Equal("completed", evidence.Status); Assert.Equal(JsonSerializer.Serialize(execution), JsonSerializer.Serialize(evidence.Execution)); Assert.Equal("splitmix64-session-v1-5", evidence.StreamScheme);
        Assert.Equal(20, evidence.Progress!.Execution!.CompletedSessions); Assert.Equal(100, evidence.Progress.Execution.AttemptedRounds);
        Assert.All(evidence.Progress.Execution.SessionMetrics, m => Assert.Equal(20, m.Observations));
        Assert.Equal(64, evidence.RuntimeProvenance!.CoreBinarySha256!.Length);
        using var result = JsonDocument.Parse(evidence.ResultJson!); Assert.Equal(20, result.RootElement.GetProperty("executionSummary").GetProperty("completedSessions").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/runs", new { configId = id, sampleSize = 99, execution })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/runs", new { configId = id, execution = new { regime = "persistent", persistentKeys = new[] { "unknown" } }, degreeOfParallelism = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/runs", new { configId = id, execution = new { regime = "sessions", persistentKeys = (object?)null } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/runs", new { configId = id, execution = new { regime = "sessions", featureMetricId = "missing" } })).StatusCode);
    }
    [Fact]
    public async Task SavedWitnessReconstructionPinsCoordinatesPlanAndActualAlgorithm()
    {
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var store = factory.Services.GetRequiredService<InMemoryRunStore>(); var id = configs.Create(Coin());
        MeasurementDefinition[] plan = [new() { Id = "payout", Name = "Payout", Options = new() }]; var run = store.Create(id, 42, 1, 100, CanonicalHash.Compute(Coin()), 1, plan);
        await factory.Services.GetRequiredService<RunJobService>().ExecuteRunAsync(run.Id, 100, 16);
        var completed = (await client.GetFromJsonAsync<RunResponse>($"/api/runs/{run.Id}"))!; var witness = completed.Progress!.Measurements[0].Witnesses.Single(w => w.Kind == "maximum");
        var response = await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/replay", new MeasurementReplayRequest(witness.RoundIndex)); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var replay = (await response.Content.ReadFromJsonAsync<MeasurementReplayReport>())!;
        Assert.Equal(witness.Value, replay.Measurements[0].Max); Assert.Equal(run.MeasurementHash, replay.MeasurementHash); Assert.Equal(run.RuntimeProvenance, replay.RuntimeProvenance);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/replay", new MeasurementReplayRequest(100))).StatusCode);
    }
    [Fact]
    public async Task DiscreteCalibrationRequiresPinnedIndependentCompleteCountsAndExportsItsInputHash()
    {
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var store = factory.Services.GetRequiredService<InMemoryRunStore>(); var id = configs.Create(Coin());
        MeasurementDefinition[] plan = [new() { Id = "law", Name = "Coin law", Options = new() { IndependentSubjects = true, ReferenceDistribution = [new(0, .5), new(1, .5)] } }];
        var run = store.Create(id, 42, 1, 100, CanonicalHash.Compute(Coin()), 1, plan);
        var request = new MeasurementCalibrationRequest("law"); Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/calibration", request)).StatusCode);
        await factory.Services.GetRequiredService<RunJobService>().ExecuteRunAsync(run.Id, 100, 16);
        var response = await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/calibration", request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = (await response.Content.ReadFromJsonAsync<MeasurementCalibrationReport>())!; Assert.Equal(run.MeasurementHash, report.MeasurementHash); Assert.Equal(100, report.Report.Subjects);
        Assert.InRange(report.Report.PValue, 0, 1); Assert.Equal(64, report.Report.AuthoredInputSha256!.Length);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/runs/{run.Id}/measurements/calibration", request with { MeasurementId = "missing" })).StatusCode);
        var planning = await client.PostAsJsonAsync("/api/runs/measurements/reference/planning", new SamplePlanRequest(EventProbability: 1e-7)); Assert.Equal(HttpStatusCode.OK, planning.StatusCode);
        Assert.Equal(29957322, (await planning.Content.ReadFromJsonAsync<SamplePlanReport>())!.EventDetectionRounds);
    }
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
    [Theory][InlineData("auto")][InlineData("reference")]
    public async Task StrictConversionsRetainExactAssertionsAndInvalidObservationEvidence(string engine)
    {
        var config = Coin() with { InitialState = new()
            { ["values"] = JsonSerializer.Deserialize<JsonElement>("[0.5,0.25]"), ["text"] = JsonSerializer.Deserialize<JsonElement>("\"H\"") } };
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var store = factory.Services.GetRequiredService<InMemoryRunStore>();
        var id = configs.Create(config);
        FieldAccessExpr Values() => new() { Target = "state", Path = ["values"] };
        ConstantExpr Number(string number) => new() { Kind = ConstantKind.Rational, Value = number };
        CallExpr Call(string function, params Expression[] args) => new() { Function = function, Args = args };
        MeasurementDefinition[] plan = [
            new() { Id = "number", Name = "Numeric array index", NodeId = "sink", Value = Call("index", Values(), Number("0")) },
            new() { Id = "roundtrip", Name = "Exact text roundtrip", NodeId = "sink", Value = new BinaryExpr { Op = BinaryOp.Sub,
                Left = Call("tonumber", Call("tostring", Number("1/3"))), Right = Number("1/3") }, Options = new() { Assertion = "zero" } },
            new() { Id = "invalid", Name = "Invalid authored conversion", NodeId = "sink", Value = Call("tonumber", new FieldAccessExpr { Target = "state", Path = ["text"] }) }
        ];
        var rejected = plan[0] with { Value = Call("length", Number("1")) };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/runs", new { configId = id, measurements = new[] { rejected } })).StatusCode);
        var run = store.Create(id, 42, 1, 100, CanonicalHash.Compute(configs.GetVersion(id, 1)!.Config), 1, plan, new() { SamplingEngine = engine });
        await factory.Services.GetRequiredService<RunJobService>().ExecuteRunAsync(run.Id, 100, 16);
        var saved = (await client.GetFromJsonAsync<RunResponse>($"/api/runs/{run.Id}"))!;
        Assert.Equal("completed", saved.Status);
        var numeric = saved.Progress!.Measurements[0]; Assert.Equal(100, numeric.Count); Assert.Equal(.5, numeric.Mean); Assert.Equal(0, numeric.Errors);
        Assert.Equal("noObservedViolations", saved.Progress.Measurements[1].Analysis!.Assertion!.Status);
        var invalid = saved.Progress.Measurements[2]; Assert.Equal(100, invalid.Observations); Assert.Equal(100, invalid.Errors);
        Assert.Equal(0, invalid.Count); Assert.Null(invalid.Mean); Assert.Null(invalid.Sum); Assert.Contains("tonumber", invalid.FirstError);
        using var result = JsonDocument.Parse(saved.ResultJson!);
        Assert.Equal(100, result.RootElement.GetProperty("measurements")[2].GetProperty("errors").GetInt64());
        Assert.True((await client.GetFromJsonAsync<RunEvidence>($"/api/runs/{run.Id}/evidence"))!.InputVerified);
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
