using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Compiler;

using Dict = Dictionary<string, object?>;
using PortMap = Dictionary<string, SlotMath.Core.Model.Port>;

// ═══════════════════════════════════════════════════════════════════════════
//  Graph Truth → Everything Derived
//
//  The GraphConfig is the single authored truth.  The recurrence-state schema
//  is DERIVED from what the graph writes (StateSchemaDeriver), so authors need
//  not hand-declare it and it cannot drift.  Each derived field's type is the
//  type of the expression that writes it; arrays (board / position lists) are
//  typed Array, sums are Number.  The whole chain — derive → validate →
//  compile → run → metrics — flows from the one graph.
// ═══════════════════════════════════════════════════════════════════════════

public sealed class GraphTruthDerivationTests
{
    private static Port StatePort => new() { Name = "state", Type = PortType.State };

    private static ExprType TypeOf(IReadOnlyList<FieldDescriptor> schema, string name) =>
        schema.First(f => f.Name == name).Type;

    // ── A compact scatter-pays graph (DataNode board + count + payout). ──
    //   With declareSchema=false it carries NO StateSchema — proving the schema
    //   is fully derivable from graph truth.
    private static GraphConfig ScatterGraph(string[] board, bool declareSchema) => new()
    {
        SchemaVersion = "1.0.0",
        Id = "derive-scatter",
        StateSchema = declareSchema
            ?
            [
                new StateFieldSchema { Name = "board", Type = "array" },
                new StateFieldSchema { Name = "scatter_count", Type = "number" },
                new StateFieldSchema { Name = "scatter_win", Type = "number" },
            ]
            : Array.Empty<StateFieldSchema>(),
        Expressions = new Dictionary<string, Expression>
        {
            ["count_s"] = new FoldExpr
            {
                StateKey = "board",
                AccName = "acc",
                ItemName = "sym",
                ItemType = ExprType.String,
                Init = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
                Body = new IfExpr
                {
                    Condition = new CompareExpr
                    {
                        Op = CompareOp.Eq,
                        Left = new FieldAccessExpr { Target = "state", Path = ["sym"] },
                        Right = new ConstantExpr { Kind = ConstantKind.String, Value = "S" },
                    },
                    ThenExpr = new BinaryExpr
                    {
                        Op = BinaryOp.Add,
                        Left = new FieldAccessExpr { Target = "state", Path = ["acc"] },
                        Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
                    },
                    ElseExpr = new FieldAccessExpr { Target = "state", Path = ["acc"] },
                },
            },
            ["payout"] = new IfExpr
            {
                Condition = new CompareExpr
                {
                    Op = CompareOp.Gte,
                    Left = new FieldAccessExpr { Target = "state", Path = ["scatter_count"] },
                    Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "3" },
                },
                ThenExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "5" },
                ElseExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
            },
        },
        Nodes =
        [
            new DataNode { Id = "board_src", StateKey = "board", Values = board,
                Outputs = new PortMap { ["state"] = StatePort } },
            new ModifyStateNode { Id = "count", ExpressionId = "count_s", OutputKey = "scatter_count",
                Inputs = new PortMap { ["state"] = StatePort }, Outputs = new PortMap { ["state"] = StatePort } },
            new ModifyStateNode { Id = "pay", ExpressionId = "payout", OutputKey = "scatter_win",
                Inputs = new PortMap { ["state"] = StatePort }, Outputs = new PortMap { ["state"] = StatePort } },
            new MetricsSinkNode { Id = "sink", WinStateKey = "scatter_win",
                Inputs = new PortMap { ["state"] = StatePort } },
        ],
        Edges =
        [
            new Edge { Id = "e0", SourceNodeId = "board_src", SourcePort = "state", TargetNodeId = "count", TargetPort = "state" },
            new Edge { Id = "e1", SourceNodeId = "count", SourcePort = "state", TargetNodeId = "pay", TargetPort = "state" },
            new Edge { Id = "e2", SourceNodeId = "pay", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" },
        ],
    };

    // ── Derivation unit tests ────────────────────────────────────────────

    [Fact]
    public void Derive_DataNodeBoard_IsArray_FoldOutput_IsNumber()
    {
        var schema = StateSchemaDeriver.Derive(ScatterGraph(["S", "S", "S", "X"], declareSchema: false));

        Assert.Equal(ExprType.Array, TypeOf(schema, "board"));        // data array
        Assert.Equal(ExprType.Number, TypeOf(schema, "scatter_count")); // fold → Number
        Assert.Equal(ExprType.Number, TypeOf(schema, "scatter_win"));   // if → Number
    }

    [Fact]
    public void Derive_ReelDraw_PublishesBoardArrayAndDimensions()
    {
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            BoardConfig = new BoardConfig { Rows = 3, Columns = 2 },
            ReelStrips =
            [
                new ReelStrip { Id = "r1", Name = "R1", Symbols = ["A", "B"] },
                new ReelStrip { Id = "r2", Name = "R2", Symbols = ["A", "B"] },
            ],
            ReelSets = [new ReelSet { Id = "rs", Name = "Main", StripIds = ["r1", "r2"] }],
            Nodes =
            [
                new DrawNode { Id = "draw", BoardStateKey = "board",
                    Outputs = new PortMap { ["board"] = new() { Name = "board", Type = PortType.Board } } },
            ],
        };

        var schema = StateSchemaDeriver.Derive(config);
        Assert.Equal(ExprType.Array, TypeOf(schema, "board"));
        Assert.Equal(ExprType.Number, TypeOf(schema, "rows"));
        Assert.Equal(ExprType.Number, TypeOf(schema, "cols"));
    }

    [Fact]
    public void Derive_FoldWithAppend_IsArray_NotNumber()
    {
        // A ModifyState writing fold(..append..) is a position list → Array.
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Expressions = new Dictionary<string, Expression>
            {
                ["collect"] = new FoldExpr
                {
                    StateKey = "board",
                    AccName = "acc",
                    ItemName = "sym",
                    IndexName = "i",
                    ItemType = ExprType.String,
                    Init = new FieldAccessExpr { Target = "state", Path = ["positions"] },
                    Body = new CallExpr
                    {
                        Function = "append",
                        Args =
                        [
                            new FieldAccessExpr { Target = "state", Path = ["acc"] },
                            new FieldAccessExpr { Target = "state", Path = ["i"] },
                        ],
                    },
                },
            },
            Nodes =
            [
                new DataNode { Id = "b", StateKey = "board", Values = ["W", "x"],
                    Outputs = new PortMap { ["state"] = StatePort } },
                new ModifyStateNode { Id = "c", ExpressionId = "collect", OutputKey = "positions",
                    Inputs = new PortMap { ["state"] = StatePort }, Outputs = new PortMap { ["state"] = StatePort } },
            ],
        };

        var schema = StateSchemaDeriver.Derive(config);
        Assert.Equal(ExprType.Array, TypeOf(schema, "positions"));
    }

    [Fact]
    public void Derive_AuthorSchema_Overrides_DerivedType()
    {
        // Author explicitly declares scatter_count as a string — explicit wins.
        var config = ScatterGraph(["S"], declareSchema: false) with
        {
            StateSchema = [new StateFieldSchema { Name = "scatter_count", Type = "string" }],
        };
        var schema = StateSchemaDeriver.Derive(config);
        Assert.Equal(ExprType.String, TypeOf(schema, "scatter_count"));
    }

    // ── The derivation chain: one graph → validate → compile → run ───────

    [Fact]
    public void ScatterGraph_CompilesAndRuns_WithNoDeclaredStateSchema()
    {
        // THE breakthrough: a graph with ZERO hand-declared StateSchema compiles
        // (schema derived from graph truth) and runs end to end.
        var config = ScatterGraph(["S", "S", "S", "X"], declareSchema: false);

        var result = new GraphCompiler().Compile(config);
        Assert.True(result.IsValid,
            "Derived-schema compile failed: " +
            string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));

        var dist = ExactInterpreter.Evaluate(result.Program!, new Dict(), StateHasher.CanonicalHash);
        var (num, den) = dist.ValueDistribution().ExpectedBigIntegerValue();
        Assert.Equal(new BigInteger(5), num / den); // 3 scatters → pays 5
    }

    [Fact]
    public void DeclaredVsDerived_ProduceIdenticalMetrics()
    {
        // Declaring StateSchema is redundant: the same graph with and without it
        // yields byte-identical metrics — the schema was derivable all along.
        var board = new[] { "S", "S", "S", "X" };

        var declared = new GraphCompiler().Compile(ScatterGraph(board, declareSchema: true));
        var derived = new GraphCompiler().Compile(ScatterGraph(board, declareSchema: false));

        Assert.True(declared.IsValid);
        Assert.True(derived.IsValid);

        var (dn1, dd1) = ExactInterpreter.Evaluate(declared.Program!, new Dict(), StateHasher.CanonicalHash)
            .ValueDistribution().ExpectedBigIntegerValue();
        var (dn2, dd2) = ExactInterpreter.Evaluate(derived.Program!, new Dict(), StateHasher.CanonicalHash)
            .ValueDistribution().ExpectedBigIntegerValue();

        Assert.Equal(dn1 * dd2, dn2 * dd1); // equal rationals
    }
}
