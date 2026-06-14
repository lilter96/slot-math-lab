using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Compiler;

using Dict = Dictionary<string, object?>;

// ═══════════════════════════════════════════════════════════════════════════
//  DataNode — generic data sources (invariant 7)
//
//  Data is just data: a DataNode writes a named array into state, and a
//  level-(b) fold over it computes a value. No paytable/board special-casing —
//  any table is a DataNode and the engine works with it generically. This is
//  the enabling step for expressing catalog mechanics as pure subgraphs.
// ═══════════════════════════════════════════════════════════════════════════

public sealed class DataNodeTests
{
    private static Port StatePort => new() { Name = "state", Type = PortType.State };

    private static FieldAccessExpr State(string field) => new() { Target = "state", Path = [field] };

    private static ConstantExpr Int(int v) => new() { Kind = ConstantKind.Integer, Value = v.ToString() };

    // sum = fold(state["payouts"], 0, (acc, x) => acc + x)
    private static FoldExpr SumFold => new()
    {
        StateKey = "payouts",
        AccName = "acc",
        ItemName = "x",
        ItemType = ExprType.Number,
        Init = Int(0),
        Body = new BinaryExpr { Op = BinaryOp.Add, Left = State("acc"), Right = State("x") },
    };

    private static GraphConfig BuildConfig() => new()
    {
        SchemaVersion = "1.0.0",
        Id = "data-node-proof",
        StateSchema =
        [
            new StateFieldSchema { Name = "payouts", Type = "number" },
            new StateFieldSchema { Name = "win", Type = "number" },
        ],
        Expressions = new Dictionary<string, Expression> { ["sum"] = SumFold },
        Nodes =
        [
            new DataNode
            {
                Id = "src",
                StateKey = "payouts",
                Values = ["5", "2", "3", "0"],
                Outputs = new Dictionary<string, Port> { ["state"] = StatePort },
            },
            new ModifyStateNode
            {
                Id = "m",
                ExpressionId = "sum",
                OutputKey = "win",
                Inputs = new Dictionary<string, Port> { ["state"] = StatePort },
                Outputs = new Dictionary<string, Port> { ["state"] = StatePort },
            },
            new MetricsSinkNode { WinCap = 10_000,
                Id = "sink",
                WinStateKey = "win",
                Inputs = new Dictionary<string, Port> { ["state"] = StatePort },
            },
        ],
        Edges =
        [
            new Edge { Id = "e0", SourceNodeId = "src", SourcePort = "state", TargetNodeId = "m", TargetPort = "state" },
            new Edge { Id = "e1", SourceNodeId = "m", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" },
        ],
    };

    [Fact]
    public void DataNode_FoldOverGenericData_CompilesAndYieldsExactWin()
    {
        var result = new GraphCompiler().Compile(BuildConfig());
        Assert.True(result.IsValid,
            $"compile errors: {string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}"))}");

        var dist = ExactInterpreter.Evaluate(result.Program!, new Dict(), StateHasher.CanonicalHash);
        var (num, den) = dist.ValueDistribution().ExpectedBigIntegerValue();

        // Deterministic: win = fold([5,2,3,0]) = 10.
        Assert.Equal(new BigInteger(10), num / den);
    }
}
