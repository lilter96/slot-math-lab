using System.Collections.Concurrent;
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
    // Served concurrently by the API; every read-modify-write (version
    // increment, list append) must be atomic, so all access is serialized
    // through one gate.  Entries are immutable records, so returned
    // snapshots are safe to use outside the lock.
    private readonly object _gate = new();
    private readonly Dictionary<string, List<ConfigEntry>> _configs = new();

    public string Create(GraphConfig config)
    {
        var id = config.Id ?? Guid.NewGuid().ToString("N");
        lock (_gate)
        {
            if (!_configs.TryGetValue(id, out var entries))
            {
                entries = new List<ConfigEntry>();
                _configs[id] = entries;
            }

            entries.Add(new ConfigEntry
            {
                Id = id,
                Version = entries.Count == 0 ? 1 : entries[^1].Version + 1,
                Config = config with { Id = id },
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        return id;
    }

    public ConfigEntry? GetLatest(string id)
    {
        lock (_gate)
        {
            if (!_configs.TryGetValue(id, out var entries) || entries.Count == 0)
                return null;
            return entries[^1];
        }
    }

    public ConfigEntry? GetVersion(string id, int version)
    {
        lock (_gate)
        {
            if (!_configs.TryGetValue(id, out var entries))
                return null;
            return entries.FirstOrDefault(e => e.Version == version);
        }
    }

    public string Update(string id, GraphConfig config)
    {
        lock (_gate)
        {
            if (!_configs.TryGetValue(id, out var entries) || entries.Count == 0)
                throw new KeyNotFoundException($"Config '{id}' not found.");

            entries.Add(new ConfigEntry
            {
                Id = id,
                Version = entries[^1].Version + 1,
                Config = config with { Id = id },
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        return id;
    }

    public IReadOnlyList<ConfigEntry> GetVersions(string id)
    {
        lock (_gate)
        {
            if (!_configs.TryGetValue(id, out var entries))
                return Array.Empty<ConfigEntry>();
            return entries.ToArray();
        }
    }

    public IReadOnlyList<ConfigEntry> List()
    {
        lock (_gate)
        {
            return _configs.Values
                .Where(v => v.Count > 0)
                .Select(v => v[^1])
                .ToList()
                .AsReadOnly();
        }
    }

    public bool Delete(string id)
    {
        lock (_gate)
        {
            return _configs.Remove(id);
        }
    }
}

/// <summary>
/// A run entry in the store (for heavy runs).
/// </summary>
public sealed record RunEntry
{
    public required string Id { get; init; }
    public required string ConfigId { get; init; }
    public required string Status { get; init; } // "pending", "running", "completed", "failed", "cancelled"
    public string? ResultJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    // ── Progress fields (populated during "running" state) ─────────────
    public long? TotalSamples { get; init; }
    public long? SampleCount { get; init; }
    public double? RunningRtp { get; init; }
    public double? StdErr { get; init; }
    public long? ElapsedMs { get; init; }
}

/// <summary>
/// In-memory run store with cancellation support and progress tracking.
/// </summary>
public sealed class InMemoryRunStore
{
    // Run entries are updated from job worker threads (progress callbacks
    // may fire from parallel sampling workers) while the API reads them —
    // all read-modify-write sequences are serialized through one gate.
    private readonly object _gate = new();
    private readonly Dictionary<string, RunEntry> _runs = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cts = new();
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
        lock (_gate)
        {
            _runs[id] = entry;
        }
        return entry;
    }

    public RunEntry? Get(string id)
    {
        lock (_gate)
        {
            _runs.TryGetValue(id, out var entry);
            return entry;
        }
    }

    public RunEntry Update(string id, string status, string? resultJson)
    {
        lock (_gate)
        {
            if (!_runs.TryGetValue(id, out var entry))
                throw new KeyNotFoundException($"Run '{id}' not found.");

            var updated = entry with
            {
                Status = status,
                ResultJson = resultJson ?? entry.ResultJson,
                CompletedAt = status is "completed" or "failed" or "cancelled" ? DateTimeOffset.UtcNow : entry.CompletedAt,
            };
            _runs[id] = updated;
            return updated;
        }
    }

    /// <summary>
    /// Update progress fields on a running entry.  Progress updates are frequent
    /// and lightweight — only the progress fields are touched.  Terminal
    /// entries are left untouched so a late progress report can never
    /// resurrect a completed/cancelled run back to "running".
    /// </summary>
    public void UpdateProgress(string id, long sampleCount, long totalSamples,
        double runningRtp, double stdErr, long elapsedMs)
    {
        lock (_gate)
        {
            if (!_runs.TryGetValue(id, out var entry))
                return;
            if (entry.Status is "completed" or "failed" or "cancelled")
                return;

            _runs[id] = entry with
            {
                SampleCount = sampleCount,
                TotalSamples = totalSamples,
                RunningRtp = runningRtp,
                StdErr = stdErr,
                ElapsedMs = elapsedMs,
                Status = "running",
            };
        }
    }

    /// <summary>
    /// Create and store a <see cref="CancellationTokenSource"/> for the given run.
    /// The job layer uses this token; the cancel endpoint cancels it.
    /// If a CTS already exists for this run, returns the existing one.
    /// </summary>
    public CancellationTokenSource CreateCancellationToken(string runId)
    {
        return _cts.GetOrAdd(runId, _ => new CancellationTokenSource());
    }

    /// <summary>
    /// Cancel a running job.  Returns true if a CTS was found and cancelled,
    /// false if the run was not found or had no active CTS.
    /// </summary>
    public bool Cancel(string runId)
    {
        if (_cts.TryRemove(runId, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Remove and dispose the CTS for a run (called on normal completion).
    /// </summary>
    public void RemoveCancellationToken(string runId)
    {
        if (_cts.TryRemove(runId, out var cts))
        {
            try { cts.Dispose(); } catch { /* already disposed */ }
        }
    }

    public IReadOnlyList<RunEntry> List(string? configId = null)
    {
        lock (_gate)
        {
            var runs = _runs.Values.AsEnumerable();
            if (configId != null)
                runs = runs.Where(r => r.ConfigId == configId);
            return runs.OrderByDescending(r => r.CreatedAt).ToList().AsReadOnly();
        }
    }
}
