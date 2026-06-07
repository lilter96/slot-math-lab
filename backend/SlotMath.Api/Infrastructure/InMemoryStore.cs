using SlotMath.Core.Model;

namespace SlotMath.Api.Infrastructure;

// ═══════════════════════════════════════════════════════════════════════════
//  In-memory stores for G15 (replaced by EF Core in G16)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// A versioned config entry in the store.
/// </summary>
public sealed record ConfigEntry
{
    public required string Id { get; init; }
    public required int Version { get; init; }
    public required GraphConfig Config { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// In-memory config store with versioning.
/// </summary>
public sealed class InMemoryConfigStore
{
    private readonly Dictionary<string, List<ConfigEntry>> _configs = new();

    public string Create(GraphConfig config)
    {
        var id = config.Id ?? Guid.NewGuid().ToString("N");
        var entry = new ConfigEntry
        {
            Id = id,
            Version = 1,
            Config = config with { Id = id },
            CreatedAt = DateTimeOffset.UtcNow,
        };

        if (!_configs.ContainsKey(id))
            _configs[id] = new List<ConfigEntry>();
        _configs[id].Add(entry);

        return id;
    }

    public ConfigEntry? GetLatest(string id)
    {
        if (!_configs.TryGetValue(id, out var entries) || entries.Count == 0)
            return null;
        return entries[^1];
    }

    public ConfigEntry? GetVersion(string id, int version)
    {
        if (!_configs.TryGetValue(id, out var entries))
            return null;
        return entries.FirstOrDefault(e => e.Version == version);
    }

    public string Update(string id, GraphConfig config)
    {
        if (!_configs.TryGetValue(id, out var entries) || entries.Count == 0)
            throw new KeyNotFoundException($"Config '{id}' not found.");

        var latestVersion = entries[^1].Version;
        var entry = new ConfigEntry
        {
            Id = id,
            Version = latestVersion + 1,
            Config = config with { Id = id },
            CreatedAt = DateTimeOffset.UtcNow,
        };
        entries.Add(entry);
        return id;
    }

    public IReadOnlyList<ConfigEntry> GetVersions(string id)
    {
        if (!_configs.TryGetValue(id, out var entries))
            return Array.Empty<ConfigEntry>();
        return entries.AsReadOnly();
    }

    public IReadOnlyList<ConfigEntry> List()
    {
        return _configs.Values
            .Where(v => v.Count > 0)
            .Select(v => v[^1])
            .ToList()
            .AsReadOnly();
    }

    public bool Delete(string id)
    {
        return _configs.Remove(id);
    }
}

/// <summary>
/// A run entry in the store (for heavy runs).
/// </summary>
public sealed record RunEntry
{
    public required string Id { get; init; }
    public required string ConfigId { get; init; }
    public required string Status { get; init; } // "pending", "running", "completed", "failed"
    public string? ResultJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>
/// In-memory run store.
/// </summary>
public sealed class InMemoryRunStore
{
    private readonly Dictionary<string, RunEntry> _runs = new();
    private int _counter;

    public RunEntry Create(string configId)
    {
        var id = Interlocked.Increment(ref _counter).ToString();
        var entry = new RunEntry
        {
            Id = id,
            ConfigId = configId,
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _runs[id] = entry;
        return entry;
    }

    public RunEntry? Get(string id)
    {
        _runs.TryGetValue(id, out var entry);
        return entry;
    }

    public RunEntry Update(string id, string status, string? resultJson)
    {
        if (!_runs.TryGetValue(id, out var entry))
            throw new KeyNotFoundException($"Run '{id}' not found.");

        var updated = entry with
        {
            Status = status,
            ResultJson = resultJson ?? entry.ResultJson,
            CompletedAt = status is "completed" or "failed" ? DateTimeOffset.UtcNow : entry.CompletedAt,
        };
        _runs[id] = updated;
        return updated;
    }

    public IReadOnlyList<RunEntry> List(string? configId = null)
    {
        var runs = _runs.Values.AsEnumerable();
        if (configId != null)
            runs = runs.Where(r => r.ConfigId == configId);
        return runs.OrderByDescending(r => r.CreatedAt).ToList().AsReadOnly();
    }
}
