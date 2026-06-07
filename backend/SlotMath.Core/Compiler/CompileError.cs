namespace SlotMath.Core.Compiler;

// ═══════════════════════════════════════════════════════════════════════════
//  CompileError — a single validation or compilation error
//
//  Each error carries:
//    - NodeId: the graph node responsible (may be null for graph-level errors)
//    - EdgeId: the graph edge responsible (may be null)
//    - Message: human-readable description
//    - Code: machine-readable error code for programmatic handling
// ═══════════════════════════════════════════════════════════════════════════

public sealed record CompileError
{
    /// <summary>The offending node id, or null for graph-level errors.</summary>
    public string? NodeId { get; init; }

    /// <summary>The offending edge id, or null for node-level errors.</summary>
    public string? EdgeId { get; init; }

    /// <summary>Human-readable error description.</summary>
    public required string Message { get; init; }

    /// <summary>Machine-readable error code.</summary>
    public required string Code { get; init; }
}

// ── Error codes ──────────────────────────────────────────────────────────

public static class ErrorCodes
{
    public const string CycleWithoutLoop = "CYCLE_WITHOUT_LOOP";
    public const string TypeMismatch = "TYPE_MISMATCH";
    public const string ExpressionTypeError = "EXPRESSION_TYPE_ERROR";
    public const string MissingMetricsSink = "MISSING_METRICS_SINK";
    public const string DuplicateMetricsSink = "DUPLICATE_METRICS_SINK";
    public const string UnreachableNode = "UNREACHABLE_NODE";
    public const string DeadEndNode = "DEAD_END_NODE";
    public const string PluginNotFound = "PLUGIN_NOT_FOUND";
    public const string PluginNotConformant = "PLUGIN_NOT_CONFORMANT";
    public const string MissingTransform = "MISSING_TRANSFORM";
    public const string InvalidGraph = "INVALID_GRAPH";
}
