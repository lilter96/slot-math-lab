using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SlotMath.Api.Features.Runs;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Model;

namespace SlotMath.Api.Tests;

[Collection("SerialTests")]
public class PrivateWorkspaceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public PrivateWorkspaceTests(WebApplicationFactory<Program> factory) => _factory = factory;
    private async Task<HttpClient> User(string id)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var response = await client.PostAsJsonAsync("/api/auth/token", new { userId = id });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
    private static GraphConfig Model() => JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestData", "LabDemo", "lab-demo-mini.json")), SlotMath.Core.JsonOptions.Default)! with
    { Id = "same-game-template", Name = "Private game" };
    private static async Task<string> Create(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/configs", new { config = JsonSerializer.SerializeToElement(Model(), SlotMath.Core.JsonOptions.Default) });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }
    [Fact]
    public async Task UsersOwnMultipleIndependentCopies_AndCannotReadOrMutateOtherOwners()
    {
        using var alice = await User("alice-" + Guid.NewGuid());
        using var bob = await User("bob-" + Guid.NewGuid());
        using var anonymous = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var first = await Create(alice); var second = await Create(alice); var other = await Create(bob);
        Assert.NotEqual(first, second); Assert.NotEqual(first, other);
        var own = await alice.GetFromJsonAsync<JsonElement>("/api/configs");
        Assert.Equal(new[] { first, second }.Order(), own.EnumerateArray().Select(x => x.GetProperty("id").GetString()).Order());
        Assert.Equal("same-game-template", (await alice.GetFromJsonAsync<JsonElement>($"/api/configs/{first}")).GetProperty("config").GetProperty("id").GetString());
        foreach (var stranger in new[] { bob, anonymous })
        {
            foreach (var path in new[] { $"/api/configs/{first}", $"/api/configs/{first}/versions", $"/api/configs/{first}/versions/1" })
                Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync($"/api/configs/{first}", new { config = JsonSerializer.SerializeToElement(Model(), SlotMath.Core.JsonOptions.Default) })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/configs/{first}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync("/api/runs", new { configId = first, sampleSize = 100 })).StatusCode);
        }
        var launched = await alice.PostAsJsonAsync("/api/runs", new { configId = first, seed = 1, sampleSize = 100, degreeOfParallelism = 1 });
        Assert.Equal(HttpStatusCode.Accepted, launched.StatusCode);
        var runId = (await launched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        foreach (var stranger in new[] { bob, anonymous })
        {
            foreach (var suffix in new[] { "", "/evidence" })
                Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/runs/{runId}{suffix}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/runs/{runId}")).StatusCode);
            var archive = await stranger.GetFromJsonAsync<JsonElement>($"/api/runs?search={runId}");
            Assert.Equal(0, archive.GetProperty("total").GetInt32());
        }
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/api/runs/{runId}/measurements/replay", new { roundIndex = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/api/runs/{runId}/verification", new { })).StatusCode);
        await using var strangerHub = Hub(bob);
        await strangerHub.StartAsync();
        var denied = await Assert.ThrowsAsync<HubException>(() => strangerHub.InvokeAsync<RunResponse>("SubscribeToRun", runId));
        Assert.Contains("RUN_NOT_FOUND", denied.Message);
        await Assert.ThrowsAsync<HubException>(() => strangerHub.InvokeAsync<RunResponse>("GetRunSnapshot", runId));
        await using var ownerHub = Hub(alice);
        await ownerHub.StartAsync();
        Assert.Equal(runId, (await ownerHub.InvokeAsync<RunResponse>("SubscribeToRun", runId)).Id);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/runs/{runId}/evidence")).StatusCode);
        var store = _factory.Services.GetRequiredService<InMemoryRunStore>();
        Assert.NotNull(store.GetForOwner(runId, "user:" + (await alice.GetFromJsonAsync<JsonElement>("/api/auth/status")).GetProperty("userId").GetString()));
        Assert.Null(store.GetForOwner(runId, "anonymous"));
        Assert.Null(store.GetForOwner(runId, "user:someone-else"));
        // The hosted worker still completes an owned run outside HTTP request context.
        for (var attempt = 0; attempt < 100 && store.Get(runId)?.Status is "pending" or "running"; attempt++) await Task.Delay(50);
        Assert.Equal("completed", store.Get(runId)!.Status);
        var latestBody = new { config = JsonSerializer.SerializeToElement(Model(), SlotMath.Core.JsonOptions.Default) };
        var latest = await alice.PostAsJsonAsync("/api/runs/latest-model", latestBody);
        latest.EnsureSuccessStatusCode();
        Assert.Equal(runId, (await latest.Content.ReadFromJsonAsync<LatestModelRunResponse>())!.Evidence!.Run.Id);
        foreach (var stranger in new[] { bob, anonymous })
        {
            var response = await stranger.PostAsJsonAsync("/api/runs/latest-model", latestBody);
            response.EnsureSuccessStatusCode();
            Assert.Null((await response.Content.ReadFromJsonAsync<LatestModelRunResponse>())!.Evidence);
        }
    }
    private HubConnection Hub(HttpClient client) => new HubConnectionBuilder().WithUrl("http://localhost/hubs/runs", options =>
    {
        options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
        options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
        options.AccessTokenProvider = () => Task.FromResult(client.DefaultRequestHeaders.Authorization?.Parameter);
    }).Build();

    [Fact]
    public async Task ExperimentsAndTheirChildRunsArePrivateToTheManifestOwner()
    {
        using var owner = await User("experiment-owner-" + Guid.NewGuid());
        using var stranger = await User("experiment-stranger-" + Guid.NewGuid());
        var graph = JsonSerializer.Deserialize<JsonElement>("""
        {"schemaVersion":"1.0.0","initialState":{"parameter":2},"nodes":[
          {"nodeType":"modifyState","id":"award","outputKey":"payout","expressionId":"read","outputs":{"out":{"name":"out","type":"State"}}},
          {"nodeType":"metricsSink","id":"sink","winCap":100,"winStateKey":"payout","inputs":{"in":{"name":"in","type":"State"}}}],
          "edges":[{"id":"e","sourceNodeId":"award","sourcePort":"out","targetNodeId":"sink","targetPort":"in"}],
          "expressions":{"read":{"exprType":"fieldAccess","target":"state","path":["parameter"]}}}
        """);
        var saved = await owner.PostAsJsonAsync("/api/configs", new { config = graph });
        var id = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var response = await owner.PostAsJsonAsync("/api/runs/experiments", new ExperimentRequest(id, 1, [new("Higher", "initialState", "parameter", 3)], [], Samples: 100));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var manifest = (await response.Content.ReadFromJsonAsync<ExperimentEntry>())!;
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/runs/experiments/{manifest.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/runs/experiments/{manifest.Id}")).StatusCode);
        Assert.Empty((await stranger.GetFromJsonAsync<ExperimentEntry[]>("/api/runs/experiments"))!);
        foreach (var member in manifest.Members)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/runs/{member.RunId}/evidence")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/configs/{member.ConfigId}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/runs/{member.RunId}/evidence")).StatusCode);
        }
    }

    [Fact]
    public void OwnershipSurvivesEncryptedRestart_AndLegacyRecordsBelongOnlyToTheConfiguredLegacyOwner()
    {
        var directory = Path.Combine(Path.GetTempPath(), "private-workspace-" + Guid.NewGuid());
        try
        {
            var snapshots = new EncryptedSnapshots(directory, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            var model = Model();
            snapshots.Write("configs", new Dictionary<string, List<ConfigEntry>> { ["legacy-id"] = [new() { Id = "legacy-id", Version = 1, Config = model, CreatedAt = DateTimeOffset.UtcNow }] });
            var accessor = new HttpContextAccessor();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:User"] = "legacy" }).Build();
            var ownership = new RequestOwnership(accessor, config, _factory.Services.GetRequiredService<IWebHostEnvironment>());
            var store = new InMemoryConfigStore(snapshots, ownership);
            static ClaimsPrincipal Principal(string name) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, name)], "test"));
            accessor.HttpContext = new DefaultHttpContext { User = Principal("alice") };
            Assert.Null(store.GetLatest("legacy-id"));
            var id = store.Create(model);
            var runs = new InMemoryRunStore(snapshots, ownership: ownership, configs: store);
            var run = runs.Create(id);
            accessor.HttpContext = new DefaultHttpContext { User = Principal("bob") };
            var restored = new InMemoryConfigStore(snapshots, ownership);
            var restoredRuns = new InMemoryRunStore(snapshots, ownership: ownership, configs: restored);
            Assert.Null(restored.GetLatest(id)); Assert.Null(restoredRuns.Get(run.Id)); Assert.Null(restored.GetLatest("legacy-id"));
            accessor.HttpContext = new DefaultHttpContext { User = Principal("alice") };
            Assert.Equal(model.Name, restored.GetLatest(id)!.Config.Name); Assert.Equal("user:alice", restoredRuns.Get(run.Id)!.OwnerId);
            accessor.HttpContext = new DefaultHttpContext { User = Principal("legacy") };
            Assert.NotNull(restored.GetLatest("legacy-id")); Assert.Null(restored.GetLatest(id));
            accessor.HttpContext = null;
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ProductionSupportsConfiguredPrivateAccountsWithoutAnAnonymousTokenIssuer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "private-accounts-" + Guid.NewGuid());
        try
        {
            using var production = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("JWT:Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
                builder.UseSetting("Storage:Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
                builder.UseSetting("Storage:Directory", directory);
                foreach (var (index, name) in new[] { (0, "alice"), (1, "bob") })
                {
                    var salt = RandomNumberGenerator.GetBytes(16);
                    var hash = Rfc2898DeriveBytes.Pbkdf2(name + "-secret", salt, 210000, HashAlgorithmName.SHA256, 32);
                    builder.UseSetting($"Auth:Users:{index}:UserId", name);
                    builder.UseSetting($"Auth:Users:{index}:PasswordHash", $"pbkdf2:210000:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}");
                }
            });
            using var alice = production.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            using var bob = production.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync("/api/configs")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await alice.PostAsJsonAsync("/api/auth/token", new { userId = "alice" })).StatusCode);
            foreach (var (client, name) in new[] { (alice, "alice"), (bob, "bob") })
            {
                var response = await client.PostAsJsonAsync("/api/auth/token", new { userId = name, password = name + "-secret" });
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
            }
            var id = await Create(alice);
            Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/configs/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/configs/{id}")).StatusCode);
            Assert.Empty((await bob.GetFromJsonAsync<JsonElement>("/api/configs")).EnumerateArray());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void ConfiguredAccountsUseSeparatePasswordHashes_AndPreserveLegacyCredentials()
    {
        static string Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            return $"pbkdf2:210000:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32))}";
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:User"] = "legacy",
            ["Auth:PasswordHash"] = Hash("legacy-secret"),
            ["Auth:Users:0:UserId"] = "alice",
            ["Auth:Users:0:PasswordHash"] = Hash("alice-secret"),
            ["Auth:Users:1:UserId"] = "bob",
            ["Auth:Users:1:PasswordHash"] = Hash("bob-secret")
        }).Build();
        var accounts = new AuthAccounts(config);
        Assert.True(accounts.Verify("alice", "alice-secret")); Assert.True(accounts.Verify("bob", "bob-secret"));
        Assert.True(accounts.Verify("legacy", "legacy-secret")); Assert.False(accounts.Verify("bob", "alice-secret"));
        Assert.False(accounts.Verify("unknown", "alice-secret")); Assert.False(accounts.Verify(null!, "alice-secret"));
    }
}
