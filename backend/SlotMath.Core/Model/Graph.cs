using System.Text.Json.Serialization;

namespace SlotMath.Core.Model;

// ── Port types ─────────────────────────────────────────────────────────

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PortType
{
    Board,
    State,
    Weights,
    Wins,
    Number,
    Boolean,
    String,
    Symbol,
    Trigger,
}

// ── Port ───────────────────────────────────────────────────────────────

public sealed record Port
{
    public required string Name { get; init; }
    public PortType Type { get; init; }
    public Expression? DefaultValue { get; init; }
}

// ── Edge ───────────────────────────────────────────────────────────────

public sealed record Edge
{
    public required string Id { get; init; }
    public required string SourceNodeId { get; init; }
    public required string SourcePort { get; init; }
    public required string TargetNodeId { get; init; }
    public required string TargetPort { get; init; }
}

// ── Node base ──────────────────────────────────────────────────────────

[JsonPolymorphic(TypeDiscriminatorPropertyName = "nodeType")]
[JsonDerivedType(typeof(DrawNode), "draw")]
[JsonDerivedType(typeof(GetStateNode), "getState")]
[JsonDerivedType(typeof(PutStateNode), "putState")]
[JsonDerivedType(typeof(ModifyStateNode), "modifyState")]
[JsonDerivedType(typeof(LoopNode), "loop")]
[JsonDerivedType(typeof(BranchNode), "branch")]
[JsonDerivedType(typeof(MapNode), "map")]
[JsonDerivedType(typeof(LibraryNode), "library")]
[JsonDerivedType(typeof(MetricsSinkNode), "metricsSink")]
public abstract record Node
{
    public required string Id { get; init; }
    public string? Label { get; init; }
    public Dictionary<string, Port> Inputs { get; init; } = new();
    public Dictionary<string, Port> Outputs { get; init; } = new();
}

// ── Draw weight ────────────────────────────────────────────────────────

public sealed record DrawWeight
{
    public required string OutcomeId { get; init; }
    public long Weight { get; init; }
    /// <summary>Numeric value produced when this outcome is drawn. Can be any integer — not necessarily monetary.</summary>
    public long Value { get; init; }
}

// ── Primitive nodes ────────────────────────────────────────────────────

public sealed record DrawNode : Node
{
    public string? WeightExpressionId { get; init; }
    /// <summary>Inline weighted outcomes. When present, used instead of ReelSets.</summary>
    public DrawWeight[]? DrawWeights { get; init; }
}

public sealed record GetStateNode : Node
{
    public string? StateKey { get; init; }
}

public sealed record PutStateNode : Node
{
    public required string StateKey { get; init; }
}

public sealed record ModifyStateNode : Node
{
    public string? ExpressionId { get; init; }
}

public sealed record LoopNode : Node
{
    public string? StopConditionId { get; init; }
    public int MaxIterations { get; init; } = 1000;
}

public sealed record BranchNode : Node
{
    public string? ConditionId { get; init; }
}

public sealed record MapNode : Node
{
    public string? TransformId { get; init; }
}

// ── Metrics sink ───────────────────────────────────────────────────────

public sealed record MetricsSinkNode : Node
{
    public string? MetricId { get; init; }
}

// ── Library node ───────────────────────────────────────────────────────

public sealed record LibraryNode : Node
{
    public required string MechanicName { get; init; }
    public Dictionary<string, string> Parameters { get; init; } = new();
}
