using System.Text.Json;
namespace SlotMath.Api.Infrastructure;

/// <summary>Server-calculated evidence, retained separately from live progress.
/// Inputs, outputs and the executing artifact are fingerprinted independently.</summary>
public sealed record DiagnosticArtifact(string Id, string Kind, DateTimeOffset CreatedAt, string InputSha256,
    string OutputSha256, string? ConfigHash, string? MeasurementHash, RuntimeProvenance RuntimeProvenance,
    JsonElement Input, JsonElement Output);
public sealed record DiagnosticRetention(string? ArtifactId, bool Retained, string Note);
