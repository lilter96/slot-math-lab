using SlotMath.Core.Measurements;

namespace SlotMath.Api.Infrastructure;

// ═══════════════════════════════════════════════════════════════════════════
//  Shared API request/response types (G15)
// ═══════════════════════════════════════════════════════════════════════════

public sealed record ConfigListResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required int LatestVersion { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ConfigDetailResponse
{
    public required string Id { get; init; }
    public required int Version { get; init; }
    public required object Config { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record CreateConfigRequest
{
    public required object Config { get; init; }
}

public sealed record CreateConfigResponse
{
    public required string Id { get; init; }
    public required int Version { get; init; }
}

public sealed record UpdateConfigRequest
{
    public required object Config { get; init; }
}

public sealed record ValidateRequest
{
    public required object Config { get; init; }
}

public sealed record ValidateResponse
{
    public required bool IsValid { get; init; }
    public IReadOnlyList<ValidateErrorItem> Errors { get; init; } = Array.Empty<ValidateErrorItem>();
}

public sealed record ValidateErrorItem
{
    public string? NodeId { get; init; }
    public string? EdgeId { get; init; }
    public required string Message { get; init; }
    public required string Code { get; init; }
}

public sealed record EvaluateLightRequest
{
    public long Seed { get; init; } = 42;
    public required object Config { get; init; }
    public int? MaxBranches { get; init; }
    public int? SampleSize { get; init; }
}

public sealed record EvaluateLightResponse
{
    public double? Lo { get; init; }
    public double? Hi { get; init; }
    public double? PrunedMass { get; init; }
    public long Seed { get; init; }
    public IReadOnlyList<ValidateErrorItem> Errors { get; init; } = Array.Empty<ValidateErrorItem>();
    public required string Strategy { get; init; } // "Exact", "Sampled", "NeedsFullRun"
    public double? Rtp { get; init; }
    public double? HitFrequency { get; init; }
    public double? Volatility { get; init; }
    public double? StdErr { get; init; }
    public string? Ci95 { get; init; }
    public string? Provenance { get; init; }
    public int? SampleCount { get; init; }
    public double? ElapsedMs { get; init; }
}

public sealed record CreateRunRequest
{
    public MeasurementInput[] Measurements { get; init; } = [];
    public int? ConfigVersion { get; init; }
    public long Seed { get; init; } = 42;
    public required string ConfigId { get; init; }
    public int? SampleSize { get; init; }
    public int? ProgressBatchSize { get; init; }
    public int DegreeOfParallelism { get; init; } = 2;
}

public sealed record RunResponse
{
    public MeasurementInput[] Measurements { get; init; } = [];
    public string? MeasurementHash { get; init; }
    public string StreamEpoch { get; init; } = "";
    public long Sequence { get; init; }
    public int ConfigVersion { get; init; }
    public string? ConfigHash { get; init; }
    public int DegreeOfParallelism { get; init; }
    public string StreamScheme { get; init; } = "splitmix64-chunk-65536";
    public long Seed { get; init; }
    public required string Id { get; init; }
    public required string ConfigId { get; init; }
    public required string Status { get; init; }
    public string? ResultJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    /// <summary>Latest snapshot, including queued and terminal runs.</summary>
    public RunProgressMessage? Progress { get; init; }

    public static RunResponse From(RunEntry run) => new()
    {
        Measurements = run.Measurements.Select(MeasurementInput.FromCore).ToArray(), MeasurementHash = run.MeasurementHash,
        Id = run.Id, ConfigId = run.ConfigId, Seed = run.Seed, ConfigVersion = run.ConfigVersion,
        ConfigHash = run.ConfigHash, DegreeOfParallelism = run.DegreeOfParallelism, Status = run.Status,
        Sequence = Math.Max(run.Sequence, run.Progress?.Sequence ?? 0), StreamEpoch = run.StreamEpoch, CreatedAt = run.CreatedAt,
        CompletedAt = run.CompletedAt, ResultJson = run.ResultJson, Progress = InMemoryRunStore.Snapshot(run),
    };
}

/// <summary>
/// Progress update pushed from server to client via SignalR.
/// Also included in RunResponse when a run is in-flight.
/// </summary>
public sealed record RunHistogramBin(double Lo, double? Hi, long Count);

public sealed record RunProgressMessage
{
    public IReadOnlyList<MeasurementSnapshot> Measurements { get; init; } = [];
    public string? MeasurementHash { get; init; }
    public string StreamEpoch { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ResultJson { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CompletedAt { get; init; }
    public long Sequence { get; init; }
    public double HitFrequency { get; init; }
    public long NonZeroCount { get; init; }
    public double Volatility { get; init; }
    public double MaxWin { get; init; }
    public long CapHits { get; init; }
    public IReadOnlyList<RunHistogramBin> Histogram { get; init; } = Array.Empty<RunHistogramBin>();

    public required string RunId { get; init; }
    public required long SampleCount { get; init; }
    public required long TotalSamples { get; init; }
    public required double RunningRtp { get; init; }
    public required double StdErr { get; init; }
    public required string Status { get; init; }
    public required long ElapsedMs { get; init; }
}

public sealed record PluginEntryResponse
{
    public required string PluginId { get; init; }
    public required string Contract { get; init; }
    public required bool IsConformant { get; init; }
    public string? Version { get; init; }
}

public sealed record RegisterPluginRequest
{
    public required string PluginId { get; init; }
    public required string Contract { get; init; }
    public string? Version { get; init; }
}
