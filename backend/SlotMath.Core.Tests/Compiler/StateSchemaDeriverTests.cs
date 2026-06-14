using SlotMath.Core.Catalog;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Compiler;

using ExprMap = Dictionary<string, Expression>;
using PortMap = Dictionary<string, Port>;

// ═══════════════════════════════════════════════════════════════════════════
//  StateSchemaDeriver — per-node-kind derivation + the reborn catalog mechanics
//  derive correct array-typed schemas and compile with NO hand-declared schema
//  (Graph Truth → Everything Derived).
// ═══════════════════════════════════════════════════════════════════════════

public sealed class StateSchemaDeriverTests
{
    private static Port StatePort => new() { Name = "state", Type = PortType.State };

    private static ExprType? TypeOf(IReadOnlyList<FieldDescriptor> schema, string name) =>
        schema.FirstOrDefault(f => f.Name == name)?.Type;

    private static GraphConfig Graph(Node[] nodes, ExprMap? expr = null, StateFieldSchema[]? schema = null) => new()
    {
        SchemaVersion = "1.0.0",
        Nodes = nodes,
        Expressions = expr,
        StateSchema = schema ?? Array.Empty<StateFieldSchema>(),
    };

    // ── Per-node-kind derivation ─────────────────────────────────────────

    [Fact]
    public void DataNode_DerivesArray()
    {
        var schema = StateSchemaDeriver.Derive(Graph(
            [new DataNode { Id = "d", StateKey = "reels", Values = ["A", "B"], Outputs = new PortMap { ["state"] = StatePort } }]));
        Assert.Equal(ExprType.Array, TypeOf(schema, "reels"));
    }

    [Fact]
    public void PutStateNode_DerivesNumber()
    {
        var schema = StateSchemaDeriver.Derive(Graph(
            [new PutStateNode { Id = "p", StateKey = "fsLeft", Inputs = new PortMap { ["state"] = StatePort }, Outputs = new PortMap { ["state"] = StatePort } }]));
        Assert.Equal(ExprType.Number, TypeOf(schema, "fsLeft"));
    }

    [Fact]
    public void LoopNode_DerivesIterAndWinCounters()
    {
        var schema = StateSchemaDeriver.Derive(Graph(
            [new LoopNode { Id = "spins", MaxIterations = 10, Inputs = new PortMap { ["in"] = StatePort }, Outputs = new PortMap { ["body"] = StatePort } }]));
        Assert.Equal(ExprType.Number, TypeOf(schema, "__iter_spins__"));
        Assert.Equal(ExprType.Number, TypeOf(schema, "__wins_spins__"));
    }

    [Fact]
    public void ModifyState_MapExpr_DerivesArray()
    {
        var expr = new ExprMap
        {
            ["overlay"] = new MapExpr
            {
                StateKey = "board",
                ItemName = "c",
                ItemType = ExprType.String,
                Body = new FieldAccessExpr { Target = "state", Path = ["c"] },
            },
        };
        var schema = StateSchemaDeriver.Derive(Graph(
            [
                new DataNode { Id = "b", StateKey = "board", Values = ["A"], Outputs = new PortMap { ["state"] = StatePort } },
                new ModifyStateNode { Id = "m", ExpressionId = "overlay", OutputKey = "board2", Inputs = new PortMap { ["state"] = StatePort }, Outputs = new PortMap { ["state"] = StatePort } },
            ], expr));
        Assert.Equal(ExprType.Array, TypeOf(schema, "board2"));
    }

    [Fact]
    public void ModifyState_AggregateExpr_DerivesNumber()
    {
        var expr = new ExprMap
        {
            ["sum"] = new AggregateExpr { Func = AggregateFunc.Sum, StateKey = "board", ItemName = "c" },
        };
        var schema = StateSchemaDeriver.Derive(Graph(
            [
                new DataNode { Id = "b", StateKey = "board", Values = ["2", "3"], Outputs = new PortMap { ["state"] = StatePort } },
                new ModifyStateNode { Id = "m", ExpressionId = "sum", OutputKey = "total", Inputs = new PortMap { ["state"] = StatePort }, Outputs = new PortMap { ["state"] = StatePort } },
            ], expr));
        Assert.Equal(ExprType.Number, TypeOf(schema, "total"));
    }

    [Fact]
    public void ModifyState_UnknownExpression_DefaultsNumber()
    {
        var schema = StateSchemaDeriver.Derive(Graph(
            [new ModifyStateNode { Id = "m", ExpressionId = "missing", OutputKey = "x", Inputs = new PortMap { ["state"] = StatePort }, Outputs = new PortMap { ["state"] = StatePort } }]));
        Assert.Equal(ExprType.Number, TypeOf(schema, "x"));
    }

    [Fact]
    public void Derive_IsDeterministic_AndOrdered()
    {
        var g = Graph([new DataNode { Id = "d", StateKey = "z", Values = ["A"], Outputs = new PortMap { ["state"] = StatePort } }]);
        var a = StateSchemaDeriver.Derive(g);
        var b = StateSchemaDeriver.Derive(g);
        Assert.Equal(a.Select(f => f.Name), b.Select(f => f.Name));
        Assert.Equal(a.Select(f => f.Name), a.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    // ── The reborn catalog mechanics derive correct array schemas ────────
    //   (inline the subgraph, then derive — Graph Truth → Everything Derived)

    private static GraphConfig InlineMechanic(string mechanic)
    {
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Mechanics = MechanicCatalog.Default.Merge(null),
            Nodes =
            [
                new DataNode { Id = "board_src", StateKey = "board", Values = ["W", "x"], Outputs = new PortMap { ["state"] = StatePort } },
                new LibraryNode { Id = "mech", MechanicName = mechanic, Inputs = new PortMap { ["state"] = StatePort }, Outputs = new PortMap { ["state"] = StatePort } },
            ],
            Edges = [new Edge { Id = "e0", SourceNodeId = "board_src", SourcePort = "state", TargetNodeId = "mech", TargetPort = "state" }],
        };
        var (inlined, errors) = SubgraphInliner.Inline(config);
        Assert.Empty(errors);
        return inlined;
    }

    [Fact]
    public void StickyWild_DerivesStickyPositionsAsArray()
    {
        var schema = StateSchemaDeriver.Derive(InlineMechanic("sticky-wild"));
        Assert.Equal(ExprType.Array, TypeOf(schema, "stickyPositions"));
        Assert.Equal(ExprType.Array, TypeOf(schema, "board"));
    }

    [Fact]
    public void HoldAndWin_DerivesHoldWinNumber_AndCollectedArray()
    {
        var schema = StateSchemaDeriver.Derive(InlineMechanic("hold-and-win"));
        Assert.Equal(ExprType.Number, TypeOf(schema, "holdWin"));
        Assert.Equal(ExprType.Array, TypeOf(schema, "collectedPositions"));
    }

    [Fact]
    public void Cascade_DerivesBoardAsArray()
    {
        var schema = StateSchemaDeriver.Derive(InlineMechanic("cascade"));
        Assert.Equal(ExprType.Array, TypeOf(schema, "board"));
    }
}
