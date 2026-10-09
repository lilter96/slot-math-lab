using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SlotMath.Api.Infrastructure;
using SlotMath.Api.Persistence;

namespace SlotMath.Api.Tests;

[CollectionDefinition("PersistenceTests", DisableParallelization = true)]
public class PersistenceTestCollection { }

/// <summary>
/// G16 integration tests — persistence (Postgres) + result cache.
/// Each test uses a unique project ID to avoid cross-test contamination.
/// </summary>
[Collection("PersistenceTests")]
public class PersistenceIntegrationTests : IDisposable
{
    private readonly SlotMathDbContext _db;
    private readonly IResultCache _cache;
    private readonly ConfigPersistenceService _persistence;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ServiceProvider _provider;
    private readonly string _testId;
    private readonly List<string> _cleanupProjects = new();

    public PersistenceIntegrationTests()
    {
        _testId = Guid.NewGuid().ToString("N")[..8];
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=slotmath;Username=slotmath;Password=slotmath";

        var services = new ServiceCollection();
        services.AddDbContext<SlotMathDbContext>(options =>
            options.UseNpgsql(connStr));
        services.AddSingleton<IResultCache, InMemoryResultCache>();
        services.AddScoped<ConfigPersistenceService>();

        _provider = services.BuildServiceProvider();
        var scope = _provider.CreateScope();
        _db = scope.ServiceProvider.GetRequiredService<SlotMathDbContext>();
        _cache = scope.ServiceProvider.GetRequiredService<IResultCache>();
        _persistence = scope.ServiceProvider.GetRequiredService<ConfigPersistenceService>();

        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        foreach (var pid in _cleanupProjects)
        {
            try
            {
                _db.ConfigVersions.Where(c => c.ProjectId == pid).ExecuteDelete();
                _db.Projects.Where(p => p.Id == pid).ExecuteDelete();
            }
            catch { /* best effort */ }
        }
        _db.SaveChanges();
        _db.Dispose();
        _provider.Dispose();
    }

    private string Pid(string name) { var id = $"{name}-{_testId}"; _cleanupProjects.Add(id); return id; }

    private object CreateSampleConfig(string configId)
    {
        return new
        {
            schemaVersion = "1.0.0",
            id = configId,
            name = "Test Config",
            symbols = new[]
            {
                new { id = "sym-a", name = "A", kind = "Standard" },
            },
            paytables = new[]
            {
                new
                {
                    id = "pt",
                    entries = new[]
                    {
                        new { symbolId = "sym-a", counts = new[] { 3 }, payouts = new[] { "10" } },
                    }
                }
            },
            reelStrips = new[]
            {
                new { id = "r1", name = "R1", symbols = new[] { "sym-a", "sym-a" } },
            },
            reelSets = new[]
            {
                new { id = "rs", name = "Main", stripIds = new[] { "r1" } },
            },
            boardConfig = new { rows = 1, columns = 1 },
            nodes = new object[]
            {
                new { nodeType = "draw", id = "draw", outputs = new { board = new { name = "board", type = "Board" } } },
                new { nodeType = "metricsSink", winCap = 10000, id = "sink", inputs = new { wins = new { name = "wins", type = "Wins" } } },
            },
            edges = Array.Empty<object>(),
            plugins = Array.Empty<object>(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MIGRATIONS — tables exist and are queryable
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Migrations_TablesExist()
    {
        var projects = _db.Projects.ToList();
        var configs = _db.ConfigVersions.ToList();
        var runs = _db.Runs.ToList();
        var users = _db.Users.ToList();
        var plugins = _db.Plugins.ToList();

        Assert.NotNull(projects);
        Assert.NotNull(configs);
        Assert.NotNull(runs);
        Assert.NotNull(users);
        Assert.NotNull(plugins);
    }

    [Fact]
    public void Migrations_ApplyCleanlyToEmptyDb()
    {
        var project = new SlotMathProject { Id = Pid("mig"), Name = "Migration Test" };
        _db.Projects.Add(project);
        _db.SaveChanges();

        var loaded = _db.Projects.Find(Pid("mig"));
        Assert.NotNull(loaded);
        Assert.Equal("Migration Test", loaded!.Name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SAVE → LOAD → RE-SAVE preserves all versions
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveLoadResave_PreservesAllVersions()
    {
        var pid = Pid("slr");

        // Save v1
        var config1 = CreateSampleConfig("cfg1");
        var v1 = await _persistence.SaveConfigAsync(pid, config1);
        Assert.Equal(1, v1.Version);
        Assert.NotEmpty(v1.ConfigHash);

        // Save v2 (different config)
        var config2 = CreateSampleConfig("cfg2");
        var v2 = await _persistence.SaveConfigAsync(pid, config2);
        Assert.Equal(2, v2.Version);
        Assert.NotEqual(v1.ConfigHash, v2.ConfigHash);

        // Save v3 (same config as v2)
        var v3 = await _persistence.SaveConfigAsync(pid, config2);
        Assert.Equal(3, v3.Version);
        Assert.Equal(v2.ConfigHash, v3.ConfigHash);

        // Load latest
        var latest = await _persistence.LoadLatestAsync(pid);
        Assert.NotNull(latest);
        Assert.Equal(3, latest!.Version);

        // Load specific versions
        var loadedV1 = await _persistence.LoadVersionAsync(pid, 1);
        Assert.NotNull(loadedV1);
        Assert.Equal(v1.ConfigHash, loadedV1!.ConfigHash);

        var loadedV2 = await _persistence.LoadVersionAsync(pid, 2);
        Assert.NotNull(loadedV2);
        Assert.Equal(v2.ConfigHash, loadedV2!.ConfigHash);

        // History
        var history = await _persistence.LoadHistoryAsync(pid);
        Assert.Equal(3, history.Count);
        Assert.Equal(1, history[0].Version);
        Assert.Equal(2, history[1].Version);
        Assert.Equal(3, history[2].Version);

        // All versions have valid JSON
        foreach (var v in history)
        {
            Assert.False(string.IsNullOrWhiteSpace(v.ConfigJson));
            var deserialized = JsonSerializer.Deserialize<object>(v.ConfigJson, _jsonOptions);
            Assert.NotNull(deserialized);
        }
    }

    [Fact]
    public async Task LoadHistory_QueryableByVersion()
    {
        var pid = Pid("hist");

        for (int i = 0; i < 5; i++)
        {
            var config = CreateSampleConfig($"cfg-{i}");
            await _persistence.SaveConfigAsync(pid, config);
        }

        var history = await _persistence.LoadHistoryAsync(pid);
        Assert.Equal(5, history.Count);
        for (int i = 0; i < 5; i++)
            Assert.Equal(i + 1, history[i].Version);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  RESULT CACHE — identical hash returns cached result, no recomputation
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Cache_IdenticalHash_ReturnsCachedResult()
    {
        var config = CreateSampleConfig("cache-a");
        var hash = CanonicalHash.Compute(config);

        // Cache miss
        var counterBefore = _cache.RecomputeCount;
        var miss = await _cache.GetAsync(hash);
        Assert.Null(miss);
        Assert.True(_cache.RecomputeCount > counterBefore);

        // Store result
        var resultJson = "{\"rtp\":0.95,\"hitFrequency\":0.3,\"volatility\":1.2}";
        await _cache.SetAsync(hash, resultJson);

        // Cache hit
        var hit = await _cache.GetAsync(hash);
        Assert.NotNull(hit);
        Assert.Equal(resultJson, hit);

        // Different config = different hash
        var config2 = CreateSampleConfig("cache-b");
        var hash2 = CanonicalHash.Compute(config2);
        Assert.NotEqual(hash, hash2);
    }

    [Fact]
    public async Task Cache_IdenticalConfigNoRecomputation()
    {
        var config = CreateSampleConfig("nocache");
        var hash = CanonicalHash.Compute(config);

        var initialCount = _cache.RecomputeCount;

        // First request: cache miss
        Assert.Null(await _cache.GetAsync(hash));

        // Store
        await _cache.SetAsync(hash, "{\"rtp\":0.96}");

        // Second request: cache hit, no recompute
        var hit = await _cache.GetAsync(hash);
        Assert.NotNull(hit);

        // Only 1 recompute (the first miss)
        Assert.Equal(initialCount + 1, _cache.RecomputeCount);
    }

    [Fact]
    public async Task Cache_CanonicalHash_Deterministic()
    {
        var c1 = CreateSampleConfig("det-1");
        var c2 = CreateSampleConfig("det-1");
        Assert.Equal(CanonicalHash.Compute(c1), CanonicalHash.Compute(c2));
    }

    [Fact]
    public async Task Cache_CanonicalHash_DifferentForDifferentConfigs()
    {
        var c1 = CreateSampleConfig("diff-1");
        var c2 = CreateSampleConfig("diff-2");
        Assert.NotEqual(CanonicalHash.Compute(c1), CanonicalHash.Compute(c2));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  COMPOSITE
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Composite_SaveThenCacheThenReSave()
    {
        var pid = Pid("comp");

        var config = CreateSampleConfig("comp-cfg");
        var v1 = await _persistence.SaveConfigAsync(pid, config);

        var resultJson = "{\"rtp\":0.97,\"source\":\"exact\"}";
        await _persistence.CacheResultAsync(v1.ConfigHash, resultJson);

        // Re-save same config
        var v2 = await _persistence.SaveConfigAsync(pid, config);
        Assert.Equal(2, v2.Version);
        Assert.Equal(v1.ConfigHash, v2.ConfigHash);

        // Cache hit
        var cached = await _persistence.GetCachedResultAsync(v2.ConfigHash);
        Assert.NotNull(cached);
        Assert.Equal(resultJson, cached);

        // History
        var history = await _persistence.LoadHistoryAsync(pid);
        Assert.Equal(2, history.Count);
    }
}
