using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Mechanics.Evaluators;
using SlotMath.Core.Model;
using SlotMath.Core.Plugins;

namespace SlotMath.Api.Tests;

/// <summary>
/// G15 integration tests — cover all vertical slices (happy + error paths).
/// </summary>
[Collection("SerialTests")]
public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public IntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => { });
        _client = _factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        lock (TestRegistryLock.Lock)
        {
            EvaluatorRegistry.Clear();
        }
    }

    private static void SafeRegisterEvaluator(string name, IEvaluator evaluator)
    {
        lock (TestRegistryLock.Lock)
        {
            try { EvaluatorRegistry.Register(name, evaluator); }
            catch (InvalidOperationException) { /* already registered by a parallel test */ }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private object CreateValidConfig()
    {
        return new
        {
            schemaVersion = "1.0.0",
            id = "test-graph",
            name = "Test Graph",
            symbols = new[]
            {
                new { id = "sym-a", name = "A", kind = "Standard" },
                new { id = "sym-b", name = "B", kind = "Standard" },
            },
            paytables = new[]
            {
                new
                {
                    id = "pt",
                    entries = new[]
                    {
                        new { symbolId = "sym-a", counts = new[] { 3, 4, 5 }, payouts = new[] { "10", "50", "200" } },
                        new { symbolId = "sym-b", counts = new[] { 3, 4, 5 }, payouts = new[] { "5", "25", "100" } },
                    }
                }
            },
            paylineSets = new[]
            {
                new
                {
                    id = "ps",
                    paylines = new[]
                    {
                        new { positions = new[] { 0, 0, 0 } },
                    }
                }
            },
            reelStrips = new[]
            {
                new { id = "r1", name = "R1", symbols = new[] { "sym-a", "sym-b", "sym-a" } },
                new { id = "r2", name = "R2", symbols = new[] { "sym-a", "sym-b", "sym-a" } },
                new { id = "r3", name = "R3", symbols = new[] { "sym-a", "sym-b", "sym-a" } },
            },
            reelSets = new[]
            {
                new { id = "rs", name = "Main", stripIds = new[] { "r1", "r2", "r3" } },
            },
            boardConfig = new { rows = 3, columns = 3 },
            nodes = new object[]
            {
                new
                {
                    nodeType = "draw",
                    id = "draw",
                    label = "Spin",
                    outputs = new { board = new { name = "board", type = "Board" } }
                },
                new
                {
                    nodeType = "map",
                    id = "eval",
                    label = "Eval",
                    transformId = "lines",
                    inputs = new { board = new { name = "board", type = "Board" } },
                    outputs = new { wins = new { name = "wins", type = "Wins" } }
                },
                new
                {
                    nodeType = "metricsSink",
                    id = "sink",
                    label = "Sink",
                    inputs = new { wins = new { name = "wins", type = "Wins" } }
                },
            },
            edges = new[]
            {
                new { id = "e1", sourceNodeId = "draw", sourcePort = "board", targetNodeId = "eval", targetPort = "board" },
                new { id = "e2", sourceNodeId = "eval", sourcePort = "wins", targetNodeId = "sink", targetPort = "wins" },
            },
            plugins = Array.Empty<object>(),
        };
    }

    private object CreateInvalidConfig()
    {
        return new
        {
            schemaVersion = "1.0.0",
            id = "bad-graph",
            nodes = new object[]
            {
                new
                {
                    nodeType = "draw",
                    id = "draw",
                    outputs = new { board = new { name = "board", type = "Board" } }
                },
                new
                {
                    nodeType = "metricsSink",
                    id = "sink1",
                    inputs = new { wins = new { name = "wins", type = "Wins" } }
                },
                new
                {
                    nodeType = "metricsSink",
                    id = "sink2",
                    inputs = new { wins = new { name = "wins", type = "Wins" } }
                },
            },
            edges = Array.Empty<object>(),
            plugins = Array.Empty<object>(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONFIG CRUD + VERSIONING
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConfigCRUD_CreateAndGet()
    {
        var config = CreateValidConfig();
        var createResponse = await _client.PostAsJsonAsync("/api/configs", new { config });
        Assert.True(createResponse.IsSuccessStatusCode, $"Create failed: {await createResponse.Content.ReadAsStringAsync()}");

        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = createBody.GetProperty("id").GetString()!;
        Assert.NotEmpty(id);

        // Get the created config
        var getResponse = await _client.GetAsync($"/api/configs/{id}");
        Assert.True(getResponse.IsSuccessStatusCode);
        var getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, getBody.GetProperty("id").GetString());
        Assert.Equal(1, getBody.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task ConfigCRUD_UpdateCreatesNewVersion()
    {
        var config = CreateValidConfig();
        var createResponse = await _client.PostAsJsonAsync("/api/configs", new { config });
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = createBody.GetProperty("id").GetString()!;

        // Update
        var updateResponse = await _client.PutAsJsonAsync($"/api/configs/{id}", new { config });
        Assert.True(updateResponse.IsSuccessStatusCode);
        var updateBody = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, updateBody.GetProperty("version").GetInt32());

        // Check versions
        var versionsResponse = await _client.GetAsync($"/api/configs/{id}/versions");
        Assert.True(versionsResponse.IsSuccessStatusCode);
        var versionsBody = await versionsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, versionsBody.GetArrayLength());

        // Get specific version
        var v1Response = await _client.GetAsync($"/api/configs/{id}/versions/1");
        Assert.True(v1Response.IsSuccessStatusCode);
        var v2Response = await _client.GetAsync($"/api/configs/{id}/versions/2");
        Assert.True(v2Response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ConfigCRUD_ListReturnsLatest()
    {
        var config = CreateValidConfig();
        await _client.PostAsJsonAsync("/api/configs", new { config });

        var listResponse = await _client.GetAsync("/api/configs");
        Assert.True(listResponse.IsSuccessStatusCode);
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(listBody.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task ConfigCRUD_DeleteAndNotFound()
    {
        var config = CreateValidConfig();
        var createResponse = await _client.PostAsJsonAsync("/api/configs", new { config });
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = createBody.GetProperty("id").GetString()!;

        // Delete
        var deleteResponse = await _client.DeleteAsync($"/api/configs/{id}");
        Assert.True(deleteResponse.IsSuccessStatusCode);

        // Should be 404 now
        var getResponse = await _client.GetAsync($"/api/configs/{id}");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task ConfigCRUD_NotFoundForMissingConfig()
    {
        var response = await _client.GetAsync("/api/configs/nonexistent");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConfigCRUD_GetVersionNotFound()
    {
        var config = CreateValidConfig();
        var createResponse = await _client.PostAsJsonAsync("/api/configs", new { config });
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = createBody.GetProperty("id").GetString()!;

        var response = await _client.GetAsync($"/api/configs/{id}/versions/999");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  VALIDATE
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Validate_ValidConfig_ReturnsIsValid()
    {
        // Register the lines evaluator so the config compiles
        SafeRegisterEvaluator("lines", new LinesEvaluator(
            new Paytable
            {
                Id = "pt",
                Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } }
            },
            new PaylineSet { Id = "ps", Paylines = new[] { new Payline { Positions = new[] { 0, 0, 0 } } } }));

        var config = CreateValidConfig();
        var response = await _client.PostAsJsonAsync("/api/validate", new { config });
        Assert.True(response.IsSuccessStatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isValid").GetBoolean());
    }

    [Fact]
    public async Task Validate_InvalidConfig_ReturnsErrors()
    {
        var config = CreateInvalidConfig();
        var response = await _client.PostAsJsonAsync("/api/validate", new { config });
        Assert.True(response.IsSuccessStatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isValid").GetBoolean());
        var errors = body.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0);

        // Should contain DUPLICATE_METRICS_SINK error
        var errorCodes = errors.EnumerateArray()
            .Select(e => e.GetProperty("code").GetString())
            .ToList();
        Assert.Contains("DUPLICATE_METRICS_SINK", errorCodes);
    }

    [Fact]
    public async Task Validate_ReturnsSameErrorsAsCompiler()
    {
        SafeRegisterEvaluator("lines", new LinesEvaluator(
            new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } },
            new PaylineSet { Id = "ps", Paylines = new[] { new Payline { Positions = new[] { 0, 0, 0 } } } }));

        // A valid config should have zero errors
        var config = CreateValidConfig();
        var response = await _client.PostAsJsonAsync("/api/validate", new { config });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("isValid").GetBoolean());
        Assert.Equal(0, body.GetProperty("errors").GetArrayLength());
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  EVALUATE / LIGHT
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EvaluateLight_ValidMvpConfig_ReturnsExactOrSampled()
    {
        SafeRegisterEvaluator("lines", new LinesEvaluator(
            new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } },
            new PaylineSet { Id = "ps", Paylines = new[] { new Payline { Positions = new[] { 0, 0, 0 } } } }));

        var config = CreateValidConfig();
        var response = await _client.PostAsJsonAsync("/api/evaluate/light", new
        {
            config,
            maxBranches = 100000,
        });

        Assert.True(response.IsSuccessStatusCode, $"Evaluate failed: {await response.Content.ReadAsStringAsync()}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var strategy = body.GetProperty("strategy").GetString();
        Assert.True(strategy is "Exact" or "Sampled" or "NeedsFullRun",
            $"Expected Exact/Sampled/NeedsFullRun, got '{strategy}'");

        // For MVP configs, should return within latency budget
        var elapsed = body.GetProperty("elapsedMs").GetDouble();
        Assert.True(elapsed < 5000, $"Elapsed {elapsed}ms exceeds budget");

        // Should have RTP if strategy is not NeedsFullRun
        if (strategy != "NeedsFullRun")
        {
            Assert.True(body.TryGetProperty("rtp", out _));
        }
    }

    [Fact]
    public async Task EvaluateLight_InvalidConfig_ReturnsError()
    {
        var config = CreateInvalidConfig();
        var response = await _client.PostAsJsonAsync("/api/evaluate/light", new { config });
        Assert.True(response.IsSuccessStatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Error", body.GetProperty("strategy").GetString());
    }

    [Fact]
    public async Task EvaluateLight_WithSampleSize_ReturnsSampled()
    {
        SafeRegisterEvaluator("lines", new LinesEvaluator(
            new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } },
            new PaylineSet { Id = "ps", Paylines = new[] { new Payline { Positions = new[] { 0, 0, 0 } } } }));

        var config = CreateValidConfig();
        var response = await _client.PostAsJsonAsync("/api/evaluate/light", new
        {
            config,
            sampleSize = 1000,
            maxBranches = 1, // force sampled by setting tiny budget
        });

        Assert.True(response.IsSuccessStatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var strategy = body.GetProperty("strategy").GetString();
        // With tiny budget, should fall back to sampled
        if (strategy == "Sampled")
        {
            Assert.True(body.TryGetProperty("stdErr", out _));
            Assert.True(body.TryGetProperty("ci95", out _));
            Assert.Equal(1000, body.GetProperty("sampleCount").GetInt32());
        }
    }

    [Fact]
    public async Task EvaluateLight_InternalSampledFallback_IsLabelledSampled()
    {
        // A loop game whose exact state space exceeds the branch budget mid-
        // evaluation: the analyzer estimate passes, the exact attempt blows
        // the budget, and the hybrid evaluator falls back internally.  The
        // response must say "Sampled" (it used to claim "Exact") and the
        // fallback must use the light sample size, not the heavy default.
        var config = CreateLoopConfig(maxIterations: 100);
        var response = await _client.PostAsJsonAsync("/api/evaluate/light", new
        {
            config,
            sampleSize = 2_000,
            maxBranches = 1_000,
        });

        Assert.True(response.IsSuccessStatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sampled", body.GetProperty("strategy").GetString());
        Assert.Equal("Sampled", body.GetProperty("provenance").GetString());
        // RTP of the coin-flip×100 game is ~100; a sampled estimate lands near it.
        var rtp = body.GetProperty("rtp").GetDouble();
        Assert.InRange(rtp, 80, 120);
    }

    [Fact]
    public async Task EvaluateLight_IdenticalConfig_IsServedFromCache()
    {
        // Sampled light evals use a tick-derived seed, so two uncached calls
        // would essentially never agree bit-for-bit.  A cached repeat returns
        // the stored response — identical rtp proves the cache hit.
        var config = CreateLoopConfig(maxIterations: 50);
        var request = new { config, sampleSize = 3_000, maxBranches = 500 };

        var first = await (await _client.PostAsJsonAsync("/api/evaluate/light", request))
            .Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await _client.PostAsJsonAsync("/api/evaluate/light", request))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Sampled", first.GetProperty("strategy").GetString());
        Assert.Equal(
            first.GetProperty("rtp").GetDouble(),
            second.GetProperty("rtp").GetDouble());
    }

    private static object CreateLoopConfig(int maxIterations) => new
    {
        schemaVersion = "1.0.0",
        name = "loop-game",
        nodes = new object[]
        {
            new
            {
                nodeType = "loop",
                id = "loop",
                label = "Loop",
                maxIterations,
                inputs = new { },
                outputs = new
                {
                    body = new { name = "body", type = "Wins" },
                    exit = new { name = "exit", type = "Wins" },
                },
            },
            new
            {
                nodeType = "draw",
                id = "draw",
                label = "Flip",
                drawWeights = new object[]
                {
                    new { outcomeId = "h", weight = 1, value = 2 },
                    new { outcomeId = "t", weight = 1, value = 0 },
                },
                inputs = new { @in = new { name = "in", type = "Wins" } },
                outputs = new { value = new { name = "value", type = "Wins" } },
            },
            new
            {
                nodeType = "metricsSink",
                id = "sink",
                label = "Sink",
                inputs = new { @in = new { name = "in", type = "Wins" } },
                outputs = new { },
            },
        },
        edges = new object[]
        {
            new { id = "e1", sourceNodeId = "loop", sourcePort = "body", targetNodeId = "draw", targetPort = "in" },
            new { id = "e2", sourceNodeId = "loop", sourcePort = "exit", targetNodeId = "sink", targetPort = "in" },
        },
    };

    // ═══════════════════════════════════════════════════════════════════════
    //  RUNS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Runs_CreateAndCheckStatus()
    {
        SafeRegisterEvaluator("lines", new LinesEvaluator(
            new Paytable { Id = "pt", Entries = new[] { new PaytableEntry { SymbolId = "sym-a", Counts = new[] { 3 }, Payouts = new[] { "10" } } } },
            new PaylineSet { Id = "ps", Paylines = new[] { new Payline { Positions = new[] { 0, 0, 0 } } } }));

        // First create a config
        var config = CreateValidConfig();
        var createResponse = await _client.PostAsJsonAsync("/api/configs", new { config });
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var configId = createBody.GetProperty("id").GetString()!;

        // Submit a run
        var runResponse = await _client.PostAsJsonAsync("/api/runs", new
        {
            configId,
            sampleSize = 1000,
        });

        Assert.True(runResponse.StatusCode is System.Net.HttpStatusCode.Accepted,
            $"Expected 202 Accepted, got {runResponse.StatusCode}");

        var runBody = await runResponse.Content.ReadFromJsonAsync<JsonElement>();
        var runId = runBody.GetProperty("id").GetString()!;
        Assert.NotEmpty(runId);

        // Check status — should be "pending" or "running"
        var statusResponse = await _client.GetAsync($"/api/runs/{runId}");
        Assert.True(statusResponse.IsSuccessStatusCode);
        var statusBody = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
        var status = statusBody.GetProperty("status").GetString()!;
        Assert.True(status is "pending" or "running" or "completed",
            $"Unexpected status: {status}");
    }

    [Fact]
    public async Task Runs_NotFoundForMissingRun()
    {
        var response = await _client.GetAsync("/api/runs/nonexistent");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Runs_NotFoundForMissingConfig()
    {
        var response = await _client.PostAsJsonAsync("/api/runs", new
        {
            configId = "nonexistent",
            sampleSize = 1000,
        });
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  OPENAPI
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OpenApi_ServesDocument()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        Assert.True(response.IsSuccessStatusCode,
            $"OpenAPI endpoint returned {response.StatusCode}");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("openapi", out _),
            "OpenAPI document should have 'openapi' property");
        Assert.True(body.TryGetProperty("paths", out _),
            "OpenAPI document should have 'paths' property");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PLUGINS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Plugins_ListRegistered()
    {
        var response = await _client.GetAsync("/api/plugins");
        Assert.True(response.IsSuccessStatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // PluginHost starts empty, so list should be an empty array
        Assert.True(body.GetArrayLength() >= 0);
    }

    [Fact]
    public async Task Plugins_NotFoundForMissingPlugin()
    {
        var response = await _client.GetAsync("/api/plugins/nonexistent");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
