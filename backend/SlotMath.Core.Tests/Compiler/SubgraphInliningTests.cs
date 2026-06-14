using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Compiler;

// ═══════════════════════════════════════════════════════════════════════════
//  SubgraphInliningTests — G9: the compiler resolves a subgraph (LibraryNode)
//  reference to a runnable program, built entirely from atoms.
//
//  Proves:
//    • a pure-atom mechanic packaged as a named subgraph still yields the
//      hand-computed exact RTP (444/343) when referenced via a LibraryNode;
//    • 10-level-deep subgraph nesting compiles and evaluates (no overflow);
//    • the same mechanic used twice in one graph does not collide (both run);
//    • a missing mechanic and a cyclic mechanic reference each fail with a
//      precise error naming the offending library node.
//
//  No interpreter/compiler-core change is needed to add a catalog mechanic —
//  inlining is a pre-pass that expands subgraphs to primitives (invariant 2).
// ═══════════════════════════════════════════════════════════════════════════

using Dict = Dictionary<string, object?>;

public sealed class SubgraphInliningTests
{
    private const string H = "H";
    private const string L = "L";
    private const string W = "W";

    // ── Expression helpers ───────────────────────────────────────────────
    private static FieldAccessExpr Cell(int i) => new() { Target = "state", Path = [$"c{i}"] };
    private static ConstantExpr Str(string v) => new() { Kind = ConstantKind.String, Value = v };
    private static ConstantExpr Int(int v) => new() { Kind = ConstantKind.Integer, Value = v.ToString() };
    private static CompareExpr Eq(Expression l, Expression r) => new() { Op = CompareOp.Eq, Left = l, Right = r };
    private static BinaryExpr And(Expression l, Expression r) => new() { Op = BinaryOp.And, Left = l, Right = r };
    private static BinaryExpr Or(Expression l, Expression r) => new() { Op = BinaryOp.Or, Left = l, Right = r };

    private static Expression All(Func<int, Expression> p) => And(And(p(0), p(1)), p(2));
    private static Expression IsH(int i) => Or(Eq(Cell(i), Str(H)), Eq(Cell(i), Str(W)));
    private static Expression IsL(int i) => Or(Eq(Cell(i), Str(L)), Eq(Cell(i), Str(W)));
    private static Expression IsW(int i) => Eq(Cell(i), Str(W));

    private static readonly Expression WinExpr = new IfExpr
    {
        Condition = All(IsW),
        ThenExpr = Int(3),
        ElseExpr = new IfExpr
        {
            Condition = All(IsH),
            ThenExpr = Int(5),
            ElseExpr = new IfExpr { Condition = All(IsL), ThenExpr = Int(2), ElseExpr = Int(0) },
        },
    };

    // ── Port / node helpers ──────────────────────────────────────────────
    private static Dictionary<string, Port> StatePorts(params string[] names) =>
        names.ToDictionary(n => n, n => new Port { Name = n, Type = PortType.State });

    private static DrawNode DrawCell(string id, string writeKey, bool entry) => new()
    {
        Id = id,
        DrawWeights =
        [
            new DrawWeight { OutcomeId = H, Weight = 3, Value = 0 },
            new DrawWeight { OutcomeId = L, Weight = 3, Value = 0 },
            new DrawWeight { OutcomeId = W, Weight = 1, Value = 0 },
        ],
        StateWriteKey = writeKey,
        Inputs = entry ? new Dictionary<string, Port>() : StatePorts("state"),
        Outputs = StatePorts("state"),
    };

    private static StateFieldSchema[] CellSchema =>
    [
        new() { Name = "c0", Type = "string" },
        new() { Name = "c1", Type = "string" },
        new() { Name = "c2", Type = "string" },
        new() { Name = "win", Type = "number" },
    ];

    // The 444/343 game's spin+win logic packaged as a reusable named subgraph.
    // Input: none (its first Draw is the entry).  Output port "state" on the
    // win node, which the host wires to the sink.  Its win expression lives in
    // the mechanic's own expression table (namespaced on inline).
    private static CustomMechanic Spin3Win() => new()
    {
        Name = "spin3win",
        Expressions = new Dictionary<string, Expression> { ["winExpr"] = WinExpr },
        Nodes =
        [
            DrawCell("d0", "c0", entry: true),
            DrawCell("d1", "c1", entry: false),
            DrawCell("d2", "c2", entry: false),
            new ModifyStateNode
            {
                Id = "win",
                ExpressionId = "winExpr",   // namespaced → "<lib>/winExpr" on inline
                OutputKey = "win",
                Inputs = StatePorts("state"),
                Outputs = StatePorts("state"),
            },
        ],
        Edges =
        [
            new Edge { Id = "e0", SourceNodeId = "d0", SourcePort = "state", TargetNodeId = "d1", TargetPort = "state" },
            new Edge { Id = "e1", SourceNodeId = "d1", SourcePort = "state", TargetNodeId = "d2", TargetPort = "state" },
            new Edge { Id = "e2", SourceNodeId = "d2", SourcePort = "state", TargetNodeId = "win", TargetPort = "state" },
        ],
    };

    private static SlotMath.Core.Monad.Slot<Dict, BigInteger> Compile(GraphConfig config)
    {
        var result = new GraphCompiler().Compile(config);
        Assert.True(result.IsValid,
            $"Expected valid, got: {string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}"))}");
        return result.Program!;
    }

    // ── 1. Mechanic reference yields the exact hand-computed RTP ──────────

    [Fact]
    public void MechanicReference_ExactRtp_Equals_444over343()
    {
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "subgraph-444",
            StateSchema = CellSchema,
            Mechanics = new Dictionary<string, CustomMechanic> { ["spin3win"] = Spin3Win() },
            Nodes =
            [
                new LibraryNode
                {
                    Id = "game",
                    MechanicName = "spin3win",
                    Outputs = StatePorts("state"),
                },
                new MetricsSinkNode { WinCap = 10_000, Id = "sink", WinStateKey = "win", Inputs = StatePorts("state") },
            ],
            Edges =
            [
                new Edge { Id = "e", SourceNodeId = "game", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" },
            ],
        };

        var program = Compile(config);
        var result = ExactInterpreter.Evaluate(program, new Dict(), StateHasher.CanonicalHash);

        Assert.True(result.Distribution.IsFullyExact);
        var (num, den) = result.ValueDistribution().ExpectedBigIntegerValue();
        Assert.Equal(new BigInteger(444), num);
        Assert.Equal(new BigInteger(343), den);
    }

    [Fact]
    public void MechanicReference_NoLibraryNodesRemainAfterInlining()
    {
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "subgraph-flatten",
            StateSchema = CellSchema,
            Mechanics = new Dictionary<string, CustomMechanic> { ["spin3win"] = Spin3Win() },
            Nodes =
            [
                new LibraryNode { Id = "game", MechanicName = "spin3win", Outputs = StatePorts("state") },
                new MetricsSinkNode { WinCap = 10_000, Id = "sink", WinStateKey = "win", Inputs = StatePorts("state") },
            ],
            Edges =
            [
                new Edge { Id = "e", SourceNodeId = "game", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" },
            ],
        };

        var (flat, errors) = SubgraphInliner.Inline(config);
        Assert.Empty(errors);
        Assert.DoesNotContain(flat.Nodes, n => n is LibraryNode);
        // d0,d1,d2,win are namespaced under the library node id "game/".
        Assert.Contains(flat.Nodes, n => n.Id == "game/win");
        Assert.True(flat.Expressions!.ContainsKey("game/winExpr"));
    }

    // ── 2. Ten-level nesting compiles and evaluates ───────────────────────

    // Inner core mechanic: Draw a win/lose symbol (50/50), pay 2 on "win".
    // E[win] = 1.  Output port "state" on the pay node.
    private static CustomMechanic Core() => new()
    {
        Name = "core",
        Expressions = new Dictionary<string, Expression>
        {
            ["pay"] = new IfExpr
            {
                Condition = Eq(new FieldAccessExpr { Target = "state", Path = ["d"] }, Str("win")),
                ThenExpr = Int(2),
                ElseExpr = Int(0),
            },
        },
        Nodes =
        [
            new DrawNode
            {
                Id = "draw",
                DrawWeights =
                [
                    new DrawWeight { OutcomeId = "win", Weight = 1, Value = 0 },
                    new DrawWeight { OutcomeId = "lose", Weight = 1, Value = 0 },
                ],
                StateWriteKey = "d",
                Outputs = StatePorts("state"),
            },
            new ModifyStateNode
            {
                Id = "pay",
                ExpressionId = "pay",
                OutputKey = "win",
                Inputs = StatePorts("state"),
                Outputs = StatePorts("state"),
            },
        ],
        Edges =
        [
            new Edge { Id = "e", SourceNodeId = "draw", SourcePort = "state", TargetNodeId = "pay", TargetPort = "state" },
        ],
    };

    // A pass-through wrapper that just references the next mechanic.
    private static CustomMechanic Wrapper(string innerMechanic) => new()
    {
        Name = "wrap-" + innerMechanic,
        Nodes =
        [
            new LibraryNode
            {
                Id = "inner",
                MechanicName = innerMechanic,
                Outputs = StatePorts("state"),
            },
        ],
        Edges = [],
    };

    [Fact]
    public void TenLevelNesting_CompilesAndEvaluates()
    {
        // wrap0 → wrap1 → … → wrap8 → core   (10 mechanics, 10 inline passes)
        var mechanics = new Dictionary<string, CustomMechanic> { ["core"] = Core() };
        for (var k = 0; k < 9; k++)
        {
            var inner = k == 8 ? "core" : $"wrap{k + 1}";
            mechanics[$"wrap{k}"] = Wrapper(inner) with { Name = $"wrap{k}" };
        }

        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "subgraph-nesting",
            StateSchema = [new() { Name = "d", Type = "string" }, new() { Name = "win", Type = "number" }],
            Mechanics = mechanics,
            Nodes =
            [
                new LibraryNode { Id = "top", MechanicName = "wrap0", Outputs = StatePorts("state") },
                new MetricsSinkNode { WinCap = 10_000, Id = "sink", WinStateKey = "win", Inputs = StatePorts("state") },
            ],
            Edges =
            [
                new Edge { Id = "e", SourceNodeId = "top", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" },
            ],
        };

        var program = Compile(config);
        var result = ExactInterpreter.Evaluate(program, new Dict(), StateHasher.CanonicalHash);

        // E[win] = 0.5 × 2 = 1.
        var (num, den) = result.ValueDistribution().ExpectedBigIntegerValue();
        Assert.Equal(num, den); // 1/1
    }

    // ── 3. Same mechanic used twice — no id collision ─────────────────────

    [Fact]
    public void SameMechanicTwice_NoCollision_BothContribute()
    {
        // Mechanic "step": draw win/lose (50/50), add 1 to running win on "win".
        var step = new CustomMechanic
        {
            Name = "step",
            Expressions = new Dictionary<string, Expression>
            {
                ["add"] = new BinaryExpr
                {
                    Op = BinaryOp.Add,
                    Left = new FieldAccessExpr { Target = "state", Path = ["win"] },
                    Right = new IfExpr
                    {
                        Condition = Eq(new FieldAccessExpr { Target = "state", Path = ["d"] }, Str("win")),
                        ThenExpr = Int(1),
                        ElseExpr = Int(0),
                    },
                },
            },
            Nodes =
            [
                new DrawNode
                {
                    Id = "draw",
                    DrawWeights =
                    [
                        new DrawWeight { OutcomeId = "win", Weight = 1, Value = 0 },
                        new DrawWeight { OutcomeId = "lose", Weight = 1, Value = 0 },
                    ],
                    StateWriteKey = "d",
                    Inputs = StatePorts("state"),
                    Outputs = StatePorts("state"),
                },
                new ModifyStateNode
                {
                    Id = "acc",
                    ExpressionId = "add",
                    OutputKey = "win",
                    Inputs = StatePorts("state"),
                    Outputs = StatePorts("state"),
                },
            ],
            Edges =
            [
                new Edge { Id = "e", SourceNodeId = "draw", SourcePort = "state", TargetNodeId = "acc", TargetPort = "state" },
            ],
        };

        // step(a) → step(b) → sink.  Each adds Bernoulli(0.5); E[total] = 1.
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "subgraph-twice",
            StateSchema = [new() { Name = "d", Type = "string" }, new() { Name = "win", Type = "number" }],
            Mechanics = new Dictionary<string, CustomMechanic> { ["step"] = step },
            Nodes =
            [
                new LibraryNode { Id = "a", MechanicName = "step", Inputs = StatePorts("state"), Outputs = StatePorts("state") },
                new LibraryNode { Id = "b", MechanicName = "step", Inputs = StatePorts("state"), Outputs = StatePorts("state") },
                new MetricsSinkNode { WinCap = 10_000, Id = "sink", WinStateKey = "win", Inputs = StatePorts("state") },
            ],
            Edges =
            [
                new Edge { Id = "e1", SourceNodeId = "a", SourcePort = "state", TargetNodeId = "b", TargetPort = "state" },
                new Edge { Id = "e2", SourceNodeId = "b", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" },
            ],
        };

        // Inlining must produce distinct namespaced copies (a/draw, b/draw, …).
        var (flat, inlineErrors) = SubgraphInliner.Inline(config);
        Assert.Empty(inlineErrors);
        Assert.Contains(flat.Nodes, n => n.Id == "a/draw");
        Assert.Contains(flat.Nodes, n => n.Id == "b/draw");
        Assert.Equal(flat.Nodes.Select(n => n.Id).Distinct().Count(), flat.Nodes.Length);

        var program = Compile(config);
        var result = ExactInterpreter.Evaluate(program, new Dict { ["win"] = BigInteger.Zero }, StateHasher.CanonicalHash);
        var (num, den) = result.ValueDistribution().ExpectedBigIntegerValue();
        Assert.Equal(num, den); // E = 1/1
    }

    // ── 4. Missing mechanic → precise error ───────────────────────────────

    [Fact]
    public void MissingMechanic_ProducesErrorNamingNode()
    {
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "subgraph-missing",
            Nodes =
            [
                new LibraryNode { Id = "ghost", MechanicName = "does-not-exist", Outputs = StatePorts("state") },
                new MetricsSinkNode { WinCap = 10_000, Id = "sink", WinStateKey = "win", Inputs = StatePorts("state") },
            ],
            Edges =
            [
                new Edge { Id = "e", SourceNodeId = "ghost", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" },
            ],
        };

        var result = new GraphCompiler().Compile(config);
        Assert.False(result.IsValid);
        var err = Assert.Single(result.Errors);
        Assert.Equal("ghost", err.NodeId);
        Assert.Contains("does-not-exist", err.Message);
    }

    // ── 5. Cyclic mechanic reference → precise error ──────────────────────

    [Fact]
    public void CyclicMechanic_ProducesNestingError()
    {
        // selfref contains a library node that references selfref → infinite.
        var selfref = new CustomMechanic
        {
            Name = "selfref",
            Nodes =
            [
                new LibraryNode { Id = "again", MechanicName = "selfref", Inputs = StatePorts("state"), Outputs = StatePorts("state") },
            ],
            Edges = [],
        };

        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "subgraph-cyclic",
            Mechanics = new Dictionary<string, CustomMechanic> { ["selfref"] = selfref },
            Nodes =
            [
                new LibraryNode { Id = "top", MechanicName = "selfref", Outputs = StatePorts("state") },
                new MetricsSinkNode { WinCap = 10_000, Id = "sink", WinStateKey = "win", Inputs = StatePorts("state") },
            ],
            Edges =
            [
                new Edge { Id = "e", SourceNodeId = "top", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" },
            ],
        };

        var (_, errors) = SubgraphInliner.Inline(config);
        var err = Assert.Single(errors);
        Assert.Contains("nesting exceeded", err.Message);
    }
}
