using System.Text.Json.Serialization;

namespace SlotMath.Core.Model;

// ── Port types ─────────────────────────────────────────────────────────

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PortType
{
    /// <summary>
    /// A board-shaped payload: a flat row-major symbol array carried in the
    /// recurrence state (state["board"] + rows/cols).  This is a graph-edge
    /// DATA CONTRACT, NOT a kernel data type — invariant 4 (no engine `Board`
    /// type) is about the removed `Board` CLASS, which no longer exists; the
    /// board is just a user-defined state array.  A `map` node with a Board
    /// input port and a Wins output port marks a sanctioned fast-path
    /// evaluator (invariant 11): the compiler recognises this shape and routes
    /// the state to the registered IFastPathEvaluator.
    /// </summary>
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
[JsonDerivedType(typeof(DataNode), "data")]
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
    /// <summary>When set, the drawn outcomeId is written to this state key after each draw.</summary>
    public string? StateWriteKey { get; init; }
    /// <summary>
    /// When set, a flat array of the drawn symbol IDs (row-major order) is written to this
    /// state key after each reel draw, making the board available as a state array for
    /// level-(b) fold/map/filter expressions. This is the mechanism that lets pure subgraph
    /// mechanics (scatter, lines, ways) read the board from state instead of from a Board object.
    /// </summary>
    public string? BoardStateKey { get; init; }
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

    /// <summary>
    /// When set, the expression's typed result (string / number / boolean) is
    /// written to this state key.  When null, legacy behaviour applies: the
    /// numeric result is written to the internal "__modified__" key.
    ///
    /// This is the atom that lets a graph compute a value over state — e.g. a
    /// fold producing a win symbol, or a conditional producing a payout — with
    /// no evaluator/transform molecule.
    /// </summary>
    public string? OutputKey { get; init; }
}

public sealed record LoopNode : Node
{
    public Expression? ExitReason { get; init; }
    public string? StopConditionId { get; init; }
    public int MaxIterations { get; init; } = 1000;
}

public sealed record BranchNode : Node
{
    public string? ConditionId { get; init; }
}

public sealed record MapNode : Node
{
    public bool ShareWildAcrossSymbols { get; init; }
    public string? TransformId { get; init; }
}

// ── Metrics sink ───────────────────────────────────────────────────────

public sealed record MetricsSinkNode : Node
{
    public MonetarySettlement? Settlement { get; init; }
    public string? MetricId { get; init; }

    /// <summary>
    /// When set, the spin's win amount is read from this state key instead of
    /// the data-flow input value.  This lets a pure atom + expression pipeline
    /// (Draw → Modify(expression) → Sink) deliver its win with no
    /// evaluator/transform molecule producing a Win[].
    /// </summary>
    public string? WinStateKey { get; init; }

    /// <summary>
    /// Declared round win cap (D6/D19).  The compiler rejects a graph whose
    /// MetricsSink has no cap (MISSING_WIN_CAP).  The cap is the maximum total
    /// win per round in game units; wins above this value are clamped.
    /// </summary>
    public long? WinCap { get; init; }
}

// ── Library node ───────────────────────────────────────────────────────

public sealed record LibraryNode : Node
{
    public required string MechanicName { get; init; }
    public Dictionary<string, string> Parameters { get; init; } = new();
}

// ── Data-source node ─────────────────────────────────────────────────────

/// <summary>
/// A generic data source: writes a named array of values into the recurrence
/// state, where downstream nodes and level-(b) expressions (fold/map/filter,
/// aggregations, index access) can work with it. Data is data — a paytable,
/// payline set, reel strip, multiplier ladder, or any other table is just a
/// <see cref="DataNode"/>; the engine special-cases none of them (invariant 7).
/// Integer-valued entries are exposed as numbers; everything else as strings.
/// </summary>
public sealed record DataNode : Node
{
    /// <summary>State key the data array is written under (read as <c>state[StateKey]</c>).</summary>
    public required string StateKey { get; init; }

    /// <summary>The data rows, as strings; integer-parseable entries become numbers.</summary>
    public string[] Values { get; init; } = Array.Empty<string>();
}
