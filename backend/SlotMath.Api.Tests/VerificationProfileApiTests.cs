using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SlotMath.Api.Features.Runs;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

namespace SlotMath.Api.Tests;

[Collection("SerialTests")]
public sealed class VerificationProfileApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static MeasurementDefinition[] Plan(int family = 2) => new[] { "x", "y" }.Select(id => new MeasurementDefinition
    {
        Id = id, Name = id, NodeId = "sink", Value = new FieldAccessExpr { Target = "state", Path = [id] },
        Options = new() { IndependentSubjects = true, ReferenceMean = 1, Tolerance = .5, Confidence = .95, ErrorFamilySize = family }
    }).ToArray();
    private static VerificationProfile Profile() => new("Pinned precision", .95, [new("x", "mean-equivalence", 100), new("y", "mean-equivalence", 100), new("x", "observation-integrity")]);
    private static GraphConfig Graph() => JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "component-accounting-model.json")), SlotMath.Core.JsonOptions.Default)!;

    [Fact]
    public async Task CompletedBothEngineReportsRetainPredeclaredProfileAndRejectPostHocReplacement()
    {
        var configs = factory.Services.GetRequiredService<InMemoryConfigStore>(); var store = factory.Services.GetRequiredService<InMemoryRunStore>();
        var graph = Graph(); var config = configs.Create(graph); var client = factory.CreateClient();
        foreach (var engine in new[] { "auto", "reference" })
        {
            var run = store.Create(config, 42, totalSamples: 100, configHash: CanonicalHash.Compute(graph), measurements: Plan(), execution: new() { SamplingEngine = engine }, verificationProfile: Profile());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/runs/{run.Id}/verification", new { })).StatusCode);
            await factory.Services.GetRequiredService<RunJobService>().ExecuteRunAsync(run.Id, 100, 16);
            Assert.Equal("completed", store.Get(run.Id)!.Status);
            var response = await client.PostAsJsonAsync($"/api/runs/{run.Id}/verification", new { verificationProfile = new { name = "Forged post hoc", criteria = Array.Empty<object>() } });
            response.EnsureSuccessStatusCode(); var report = (await response.Content.ReadFromJsonAsync<RetainedReference<RunVerificationEvidence>>())!;
            Assert.Equal("criteriaMet", report.Report.Report.Status); Assert.Equal("Pinned precision", report.Report.Report.Name);
            Assert.Equal(run.VerificationProfileHash, report.Report.Report.ProfileHash); Assert.Equal(.05, report.Report.Report.AllocatedAlpha, 14);
            Assert.True(report.Retention.Retained); Assert.Equal(store.Get(run.Id)!.Sequence, report.Report.Source.Sequence);
            var evidence = (await client.GetFromJsonAsync<RunEvidence>($"/api/runs/{run.Id}/evidence"))!;
            Assert.Equal(run.VerificationProfileHash, evidence.Run.VerificationProfileHash); Assert.Equal(ProfileVerification.Hash(evidence.Run.VerificationProfile!), run.VerificationProfileHash);
            Assert.Equal("verification-profile", Assert.Single(evidence.Diagnostics).Kind);
            using var result = JsonDocument.Parse(evidence.Run.ResultJson!); Assert.Equal(run.VerificationProfileHash, result.RootElement.GetProperty("verificationProfileHash").GetString());
            var repeat = (await (await client.PostAsJsonAsync($"/api/runs/{run.Id}/verification", new { })).Content.ReadFromJsonAsync<RetainedReference<RunVerificationEvidence>>())!;
            Assert.Equal(report.Retention.ArtifactId, repeat.Retention.ArtifactId);
        }
    }

    [Fact]
    public async Task UnderallocatedAndMissingContractsAreRejectedBeforeLaunch()
    {
        var graph = Graph(); var config = factory.Services.GetRequiredService<InMemoryConfigStore>().Create(graph); var client = factory.CreateClient();
        var request = new CreateRunRequest { ConfigId = config, SampleSize = 100, Measurements = Plan(1).Select(MeasurementInput.FromCore).ToArray(), VerificationProfile = Profile() };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/runs/", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/runs/", request with { Measurements = [] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/runs/measurements/schema", new { config = graph, measurements = request.Measurements, verificationProfile = request.VerificationProfile }, SlotMath.Core.JsonOptions.Default)).StatusCode);
        var schema = await client.PostAsJsonAsync("/api/runs/measurements/schema", new { config = graph, measurements = Plan().Select(MeasurementInput.FromCore), verificationProfile = Profile() }, SlotMath.Core.JsonOptions.Default);
        schema.EnsureSuccessStatusCode();
        var run = factory.Services.GetRequiredService<InMemoryRunStore>().Create(config, totalSamples: 100);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/runs/{run.Id}/verification", new { })).StatusCode);
    }

    [Fact]
    public void EncryptedRestartPreservesProfileAndHashWithoutCallerMutation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "slotmath-profile-" + Guid.NewGuid());
        try
        {
            var snapshots = new EncryptedSnapshots(directory, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            var store = new InMemoryRunStore(snapshots); var profile = Profile();
            var run = store.Create("model", totalSamples: 100, measurements: Plan(), verificationProfile: profile);
            profile.Criteria[0] = new("x", "observation-integrity");
            Assert.Equal("mean-equivalence", run.VerificationProfile!.Criteria[0].Check);
            var restored = new InMemoryRunStore(snapshots).Get(run.Id)!;
            Assert.Equal("failed", restored.Status); Assert.Equal(run.VerificationProfileHash, restored.VerificationProfileHash);
            Assert.Equal(ProfileVerification.Hash(restored.VerificationProfile!), restored.VerificationProfileHash);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
