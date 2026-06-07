using System.Text.Json.Serialization;

namespace SlotMath.Core.Model;

// ── Plugin reference (level c) ─────────────────────────────────────────

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PluginContract
{
    IEvaluator,
    ITransform,
    WeightSource,
}

public sealed record PluginReference
{
    public required string PluginId { get; init; }
    public required PluginContract Contract { get; init; }
    public string? Version { get; init; }
    public Dictionary<string, string>? Config { get; init; }
}
