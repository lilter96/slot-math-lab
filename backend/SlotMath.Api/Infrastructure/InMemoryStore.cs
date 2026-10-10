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

    private readonly EncryptedSnapshots? _snapshots;
    public InMemoryConfigStore(EncryptedSnapshots? snapshots = null)
    {
        _snapshots = snapshots;
        _configs = snapshots?.Read<Dictionary<string, List<ConfigEntry>>>("configs") ?? new();
    }

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
            _snapshots?.Write("configs", _configs);
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
            _snapshots?.Write("configs", _configs);
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

    /// <summary>Coordinates run pinning with deletion under the same config gate.</summary>
    public T? UseVersion<T>(string id, int version, Func<ConfigEntry, T> action) where T : class
    {
        lock (_gate)
        {
            var entry = _configs.GetValueOrDefault(id)?.FirstOrDefault(e => e.Version == version);
            return entry is null ? null : action(entry);
        }
    }

    public bool Delete(string id, Func<bool>? canDelete = null)
    {
        lock (_gate)
        {
            if (canDelete is not null && !canDelete()) throw new InvalidOperationException("This config is pinned by saved runs and cannot be deleted.");
            var removed = _configs.Remove(id);
            if (removed) _snapshots?.Write("configs", _configs);
            return removed;
        }
    }
}

/// <summary>
/// A run entry in the store (for heavy runs).
/// </summary>
public sealed record RunEntry
{
    public SlotMath.Core.Measurements.VerificationProfile? VerificationProfile { get; init; }
    public string? VerificationProfileHash { get; init; }
    public DiagnosticArtifact[] Diagnostics { get; init; } = [];
    public RuntimeProvenance? RuntimeProvenance { get; init; }
    public SlotMath.Core.Math.ExecutionOptions? Execution { get; init; }
    public SlotMath.Core.Measurements.MeasurementDefinition[] Measurements { get; init; } = [];
    public string? MeasurementHash { get; init; }
    public string StreamEpoch { get; init; } = "";
    public long Sequence { get; init; }
    public RunProgressMessage? Progress { get; init; }
    public string? ConfigHash { get; init; }
    public int DegreeOfParallelism { get; init; } = 2;
    public int ConfigVersion { get; init; } = 1;
    public long Seed { get; init; } = 42;
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
    private readonly EncryptedSnapshots? _snapshots;
    private readonly TimeProvider _clock;
    private readonly string _streamEpoch = Guid.NewGuid().ToString("N");
    private long _lastCheckpoint;
    public InMemoryRunStore(EncryptedSnapshots? snapshots = null, TimeProvider? clock = null)
    {
        _snapshots = snapshots;
        _clock = clock ?? TimeProvider.System;
        _runs = snapshots?.Read<Dictionary<string, RunEntry>>("runs") ?? new();
        var interrupted = false;
        foreach (var (id, run) in _runs.ToArray())
        {
            var restored = run with { Sequence = Math.Max(run.Sequence, run.Progress?.Sequence ?? 0),
                StreamEpoch = string.IsNullOrEmpty(run.StreamEpoch) ? _streamEpoch : run.StreamEpoch };
            if (!Terminal(run.Status))
            {
                restored = restored with { Sequence = restored.Sequence + 1, StreamEpoch = _streamEpoch, Status = "failed", CompletedAt = _clock.GetUtcNow(),
                    ResultJson = "{\"code\":\"RUN_INTERRUPTED\",\"error\":\"Server restarted; last checkpoint retained. Replay with the recorded seed.\"}" };
                interrupted = true;
            }
            if (restored.Progress is not null) restored = restored with { Progress = Snapshot(restored) };
            _runs[id] = restored;
        }
        if (interrupted) Persist();
        _lastCheckpoint = _clock.GetTimestamp();
    }

    public RunEntry Create(string configId, long seed = 42, int configVersion = 1, long totalSamples = 0, string? configHash = null, int degreeOfParallelism = 2, SlotMath.Core.Measurements.MeasurementDefinition[]? measurements = null, SlotMath.Core.Math.ExecutionOptions? execution = null, SlotMath.Core.Measurements.VerificationProfile? verificationProfile = null)
    {
        if (verificationProfile is not null)
        {
            SlotMath.Core.Measurements.ProfileVerification.Validate(verificationProfile, measurements ?? []);
            verificationProfile = verificationProfile with { Criteria = verificationProfile.Criteria.ToArray() };
        }
        // Never reuse an identity after restart, including non-persisted dev runs.
        var id = Guid.NewGuid().ToString("N");
        var entry = new RunEntry
        {
            Id = id,
            VerificationProfile = verificationProfile,
            VerificationProfileHash = verificationProfile is null ? null : SlotMath.Core.Measurements.ProfileVerification.Hash(verificationProfile),
            RuntimeProvenance = RuntimeProvenance.Current,
            Execution = execution,
            StreamEpoch = _streamEpoch,
            ConfigId = configId,
            Seed = seed,
            ConfigVersion = configVersion,
            ConfigHash = configHash,
            DegreeOfParallelism = degreeOfParallelism,
            Measurements = measurements ?? [], MeasurementHash = measurements is { Length: > 0 } ? MeasurementHash.Compute(measurements) : null,
            TotalSamples = totalSamples,
            Status = "pending",
            CreatedAt = _clock.GetUtcNow(),
        };
        lock (_gate)
        {
            _runs[id] = entry;
            Persist();
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

    public DiagnosticRetention RetainDiagnostic(string id, string kind, object input, object output)
    {
        var inputHash = RuntimeProvenance.AuthoredInputHash(input);
        var outputHash = RuntimeProvenance.AuthoredInputHash(output);
        var artifactId = RuntimeProvenance.AuthoredInputHash(new { kind, inputHash, outputHash, RuntimeProvenance.Current.CoreBinarySha256 });
        var artifact = new DiagnosticArtifact(artifactId, kind, _clock.GetUtcNow(), inputHash, outputHash, null, null,
            RuntimeProvenance.Current, System.Text.Json.JsonSerializer.SerializeToElement(input, SlotMath.Core.JsonOptions.Default),
            System.Text.Json.JsonSerializer.SerializeToElement(output, SlotMath.Core.JsonOptions.Default));
        lock (_gate)
        {
            if (!_runs.TryGetValue(id, out var run)) return new(null, false, "Run no longer available. Export this calculation to retain it.");
            if (run.Diagnostics.Any(d => d.Id == artifactId)) return new(artifactId, true, "Identical server evidence is already retained.");
            artifact = artifact with { ConfigHash = run.ConfigHash, MeasurementHash = run.MeasurementHash };
            var next = run.Diagnostics.Append(artifact).ToArray();
            if (next.Length > 16 || System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(next, SlotMath.Core.JsonOptions.Default).Length > 2_000_000)
                return new(null, false, "Saved diagnostic budget reached (16 artifacts / 2 MB per run). Existing evidence is preserved; export this calculation.");
            _runs[id] = run with { Diagnostics = next };
            try { Persist(); }
            catch (IOException)
            { _runs[id] = run; return new(null, false, "Diagnostic storage failed. Existing evidence is preserved; export this calculation."); }
            return new(artifactId, true, _snapshots is null ? "Retained in the server's current in-memory archive." : "Retained in the encrypted durable archive.");
        }
    }

    public RunEntry Update(string id, string status, string? resultJson)
    {
        lock (_gate)
        {
            if (!_runs.TryGetValue(id, out var entry))
                throw new KeyNotFoundException($"Run '{id}' not found.");
            if (Terminal(entry.Status)) return entry;
            if (status == entry.Status && (resultJson is null || resultJson == entry.ResultJson)) return entry;
            var sequence = NextSequence(entry);

            var updated = entry with
            {
                Status = status,
                Sequence = sequence,
                Progress = entry.Progress is null ? null : entry.Progress with { Status = status, Sequence = sequence },
                ResultJson = resultJson ?? entry.ResultJson,
                CompletedAt = Terminal(status) ? _clock.GetUtcNow() : entry.CompletedAt,
            };
            _runs[id] = updated;
            if (Terminal(status)) Persist();
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
            if (entry.Status is "completed" or "failed" or "cancelled" || sampleCount < (entry.SampleCount ?? 0))
                return;

            PublishProgress(id, Snapshot(entry) with { SampleCount = sampleCount, TotalSamples = totalSamples,
                RunningRtp = runningRtp, StdErr = stdErr, ElapsedMs = elapsedMs, Status = "running" });
        }
    }

    public RunProgressMessage? PublishProgress(string id, RunProgressMessage progress)
    {
        lock (_gate)
        {
            if (!_runs.TryGetValue(id, out var entry) || entry.Status is "completed" or "failed" or "cancelled") return null;
            if (progress.SampleCount < (entry.SampleCount ?? 0)) return null;
            var next = progress with { RunId = id, StreamEpoch = entry.StreamEpoch, Sequence = NextSequence(entry), ResultJson = null, CompletedAt = null,
                Status = entry.Status == "cancelling" ? "cancelling" : progress.Status };
            _runs[id] = entry with { Sequence = next.Sequence, Progress = next, Status = next.Status, SampleCount = next.SampleCount,
                TotalSamples = next.TotalSamples, RunningRtp = next.RunningRtp, StdErr = next.StdErr, ElapsedMs = next.ElapsedMs };
            if (_snapshots is not null && _clock.GetElapsedTime(_lastCheckpoint) >= TimeSpan.FromSeconds(5)) Persist();
            return next;
        }
    }

    public static RunProgressMessage Snapshot(RunEntry run) => (run.Progress ?? new RunProgressMessage
    {
        MeasurementHash = run.MeasurementHash,
        Measurements = run.Measurements.Select(d => new SlotMath.Core.Measurements.MeasurementSnapshot(d.Id, 0, 0, 0, 0, null, null, null, null, null, null)).ToArray(),
        RunId = run.Id, Status = run.Status, SampleCount = run.SampleCount ?? 0, TotalSamples = run.TotalSamples ?? 0,
        RunningRtp = run.RunningRtp ?? 0, StdErr = run.StdErr ?? 0, ElapsedMs = run.ElapsedMs ?? 0,
    }) with { Sequence = Math.Max(run.Sequence, run.Progress?.Sequence ?? 0), StreamEpoch = run.StreamEpoch, Status = run.Status,
        ResultJson = Terminal(run.Status) ? run.ResultJson : null, CompletedAt = run.CompletedAt };

    private static bool Terminal(string status) => status is "completed" or "failed" or "cancelled";
    private static long NextSequence(RunEntry run) => Math.Max(run.Sequence, run.Progress?.Sequence ?? 0) + 1;
    private void Persist()
    {
        _snapshots?.Write("runs", _runs);
        _lastCheckpoint = _clock.GetTimestamp();
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
        lock (_gate)
        {
            if (_cts.TryGetValue(runId, out var cts) && _runs.TryGetValue(runId, out var run) && !Terminal(run.Status))
            {
                if (run.Status != "cancelling")
                {
                    var sequence = NextSequence(run);
                    _runs[runId] = run with { Status = "cancelling", Sequence = sequence,
                        Progress = run.Progress is null ? null : run.Progress with { Status = "cancelling", Sequence = sequence } };
                }
                cts.Cancel(); // Worker owns disposal; retain early cancellation until it starts.
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Remove and dispose the CTS for a run (called on normal completion).
    /// </summary>
    public void RemoveCancellationToken(string runId)
    {
        lock (_gate)
        {
            if (_cts.TryRemove(runId, out var cts)) cts.Dispose();
        }
    }

    public IReadOnlyList<RunEntry> List(string? configId = null)
    {
        RunEntry[] snapshot;
        lock (_gate) snapshot = _runs.Values.ToArray();
        return snapshot.Where(r => configId is null || r.ConfigId == configId)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id, StringComparer.Ordinal).ToArray();
    }
}
