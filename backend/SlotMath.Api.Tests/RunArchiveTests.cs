using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SlotMath.Api.Features.Runs;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Model;

namespace SlotMath.Api.Tests;

[Collection("SerialTests")]
public class RunArchiveTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;
    private readonly InMemoryConfigStore configs;
    private readonly InMemoryRunStore runs;
    public RunArchiveTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory; client = factory.CreateClient();
        configs = factory.Services.GetRequiredService<InMemoryConfigStore>();
        runs = factory.Services.GetRequiredService<InMemoryRunStore>();
    }
    private static GraphConfig Fixture(string name) => JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestData", "DogHouse", "dog-house-mini-ui.json")), SlotMath.Core.JsonOptions.Default)! with { Id = null, Name = name };
    private RunEntry Seed(string configId, int version = 1, long seed = 42) => configs.UseVersion(configId, version, entry =>
        runs.Create(configId, seed, entry.Version, 1000, CanonicalHash.Compute(entry.Config)))!;
    [Fact]
    public async Task StableCursor_SearchAndStatusCounts_DoNotReturnBulkEvidence()
    {
        var name = "Archive " + Guid.NewGuid(); var id = configs.Create(Fixture(name));
        var seeded = Enumerable.Range(0, 23).Select(i => Seed(id, seed: i)).ToArray();
        runs.Update(seeded[0].Id, "completed", "{}"); runs.Update(seeded[1].Id, "cancelled", "{}"); runs.Update(seeded[2].Id, "failed", "{}");
        var first = await client.GetFromJsonAsync<RunPage>($"/api/runs?limit=10&search={Uri.EscapeDataString(name)}");
        Assert.Equal(23, first!.Total); Assert.Equal(1, first.Completed); Assert.Equal(1, first.Partial); Assert.Equal(1, first.Failed); Assert.Equal(20, first.Active);
        Assert.Equal(10, first.Items.Count); Assert.NotNull(first.NextCursor);
        Assert.All(first.Items, item => Assert.Null(item.Rtp));
        var inserted = Seed(id, seed: 99);
        var next = await client.GetFromJsonAsync<RunPage>($"/api/runs?limit=100&search={Uri.EscapeDataString(name)}&cursor={first.NextCursor}");
        Assert.Equal(13, next!.Items.Count); Assert.Null(next.NextCursor);
        Assert.DoesNotContain(next.Items, x => x.Id == inserted.Id);
        Assert.Equal(23, first.Items.Concat(next.Items).Select(x => x.Id).Distinct().Count());
        var partial = await client.GetFromJsonAsync<RunPage>($"/api/runs?search={Uri.EscapeDataString(name)}&status=partial");
        Assert.Single(partial!.Items); Assert.Equal("cancelled", partial.Items[0].Status);
        var hash = seeded[0].ConfigHash;
        var json = await client.GetStringAsync($"/api/runs?search={hash}&limit=1");
        Assert.DoesNotContain("resultJson", json); Assert.DoesNotContain("histogram", json); Assert.DoesNotContain("pinnedConfig", json);
    }
    [Theory]
    [InlineData("limit=0")][InlineData("limit=101")][InlineData("cursor=not-valid")][InlineData("status=unknown")]
    public async Task ArchiveRejectsMalformedQueries(string query) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/runs?" + query)).StatusCode);
    [Fact]
    public async Task ManifestPinsVersionAndSerializesExecutableAst_WithIdIndependentComparisonHash()
    {
        var name = "Pinned " + Guid.NewGuid(); var config = Fixture(name);
        var id = configs.Create(config); var first = Seed(id);
        var originalHash = first.ConfigHash; configs.Update(id, config with { Name = "Edited latest" });
        var evidence = await client.GetFromJsonAsync<JsonElement>($"/api/runs/{first.Id}/evidence");
        Assert.True(evidence.GetProperty("inputVerified").GetBoolean()); Assert.Equal(originalHash, evidence.GetProperty("computedConfigHash").GetString());
        var pinned = evidence.GetProperty("pinnedConfig"); Assert.Equal(name, pinned.GetProperty("name").GetString());
        var decoded = JsonSerializer.Deserialize<GraphConfig>(pinned.GetRawText(), SlotMath.Core.JsonOptions.Default)!;
        Assert.Equal(originalHash, CanonicalHash.Compute(decoded));
        var copyId = configs.Create(config); var copy = Seed(copyId);
        var comparison = await client.GetFromJsonAsync<RunEvidence>($"/api/runs/{copy.Id}/evidence");
        Assert.NotEqual(originalHash, copy.ConfigHash);
        Assert.Equal(evidence.GetProperty("model").GetProperty("modelHash").GetString(), comparison!.Model.ModelHash);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/configs/{id}")).StatusCode);
        var unused = configs.Create(config); Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/configs/{unused}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/runs/missing/evidence")).StatusCode);
    }
    [Fact]
    public async Task ExplicitReplayVersionSurvivesNewerConfig_AndRejectsMissingVersion()
    {
        var id = configs.Create(Fixture("Replay " + Guid.NewGuid()));
        var originalHash = CanonicalHash.Compute(configs.GetVersion(id, 1)!.Config);
        configs.Update(id, Fixture("Changed after run"));
        var created = await client.PostAsJsonAsync("/api/runs", new { configId = id, configVersion = 1, seed = 42, sampleSize = 100, degreeOfParallelism = 2 });
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var run = await created.Content.ReadFromJsonAsync<RunResponse>();
        Assert.Equal(1, run!.ConfigVersion); Assert.Equal(originalHash, run.ConfigHash);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/runs", new { configId = id, configVersion = 999 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/runs", new { configId = id, configVersion = 0 })).StatusCode);
    }
    [Fact]
    public async Task LegacyMissingInputIsExplicitlyUnverified()
    {
        var run = runs.Create("missing-" + Guid.NewGuid(), configHash: new string('a', 64));
        var evidence = await client.GetFromJsonAsync<RunEvidence>($"/api/runs/{run.Id}/evidence");
        Assert.False(evidence!.InputVerified); Assert.Null(evidence.PinnedConfig); Assert.Null(evidence.Model.ModelHash); Assert.Null(evidence.Model.TargetRtp);
    }
    [Fact]
    public async Task ConcurrentPinAndDeleteCannotCreateARecordWithoutItsOriginalConfig()
    {
        var configStore = new InMemoryConfigStore(); var runStore = new InMemoryRunStore();
        for (var i = 0; i < 100; i++)
        {
            var id = configStore.Create(Fixture("Concurrent pin"));
            RunEntry? run = null;
            await Task.WhenAll(Task.Run(() => run = configStore.UseVersion(id, 1, entry => runStore.Create(id, configHash: CanonicalHash.Compute(entry.Config)))),
                Task.Run(() => { try { configStore.Delete(id, () => runStore.List(id).Count == 0); } catch (InvalidOperationException) { } }));
            if (run is not null) Assert.NotNull(configStore.GetVersion(run.ConfigId, run.ConfigVersion));
        }
    }
}
