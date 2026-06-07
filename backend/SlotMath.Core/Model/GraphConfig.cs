namespace SlotMath.Core.Model;

// ── Top-level graph config ─────────────────────────────────────────────

public sealed record GraphConfig
{
    public required string SchemaVersion { get; init; }
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }

    // Data tables — invariant #7: tables, not nodes, for data
    public Symbol[] Symbols { get; init; } = Array.Empty<Symbol>();
    public Paytable[] Paytables { get; init; } = Array.Empty<Paytable>();
    public PaylineSet[] PaylineSets { get; init; } = Array.Empty<PaylineSet>();
    public ReelStrip[] ReelStrips { get; init; } = Array.Empty<ReelStrip>();
    public ReelSet[] ReelSets { get; init; } = Array.Empty<ReelSet>();
    public BoardConfig? BoardConfig { get; init; }

    // The graph
    public Node[] Nodes { get; init; } = Array.Empty<Node>();
    public Edge[] Edges { get; init; } = Array.Empty<Edge>();

    // Shared expression definitions (referenced by id from ports/nodes)
    public Dictionary<string, Expression>? Expressions { get; init; }

    // Custom sub-graph mechanics (named, reusable)
    public Dictionary<string, CustomMechanic>? Mechanics { get; init; }

    // Level-c plugin references
    public PluginReference[] Plugins { get; init; } = Array.Empty<PluginReference>();
}

// ── Custom mechanic ────────────────────────────────────────────────────

public sealed record CustomMechanic
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public Node[] Nodes { get; init; } = Array.Empty<Node>();
    public Edge[] Edges { get; init; } = Array.Empty<Edge>();
    public Dictionary<string, Expression>? Expressions { get; init; }
    public PluginReference[] Plugins { get; init; } = Array.Empty<PluginReference>();
}
