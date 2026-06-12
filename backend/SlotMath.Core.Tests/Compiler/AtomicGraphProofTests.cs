using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Compiler;

// ═══════════════════════════════════════════════════════════════════════════
//  AtomicGraphProofTests — "atoms not molecules" at the COMPILER level
//
//  AtomicSubstrateProofTests proves a game can be a pure Slot program built by
//  hand.  This proves the same for a game authored as GRAPH DATA and compiled
//  by GraphCompiler: the win is computed by a level-(b) expression over the
//  drawn state and read out by the sink — NO IEvaluator, NO ITransform, NO
//  Board.  This is the compiler-level realization of the atomic substrate.
//
//  Game (identical to AtomicSubstrateProofTests):
//    1×3 reel, symbols H(3/7), L(3/7), W-wild(1/7), 3-of-a-kind, wilds wild.
//      HHH→5  LLL→2  WWW→3  else→0       (hand-computed E[win] = 444/343)
//
//  Graph shape (only atoms):
//    Draw(c0) → Draw(c1) → Draw(c2) → Modify(win = winExpr) → Sink(reads "win")
//    Each Draw writes its drawn symbol id to a state key; the Modify node
//    evaluates winExpr over c0/c1/c2 and writes the payout to state["win"];
//    the sink reads state["win"] (WinStateKey) as the spin's win.
// ═══════════════════════════════════════════════════════════════════════════

using Dict = Dictionary<string, object?>;

public sealed class AtomicGraphProofTests
{
    private const string H = "H";
    private const string L = "L";
    private const string W = "W";

    // ── Expression builders ──────────────────────────────────────────────
    private static FieldAccessExpr Cell(int i) =>
        new() { Target = "state", Path = [$"c{i}"] };

    private static ConstantExpr Str(string v) =>
        new() { Kind = ConstantKind.String, Value = v };

    private static ConstantExpr Int(int v) =>
        new() { Kind = ConstantKind.Integer, Value = v.ToString() };

    private static CompareExpr Eq(Expression l, Expression r) =>
        new() { Op = CompareOp.Eq, Left = l, Right = r };

    private static BinaryExpr And(Expression l, Expression r) =>
        new() { Op = BinaryOp.And, Left = l, Right = r };

    private static BinaryExpr Or(Expression l, Expression r) =>
        new() { Op = BinaryOp.Or, Left = l, Right = r };

    // cell i counts toward an H-win if it is H or wild; likewise for L.
    private static Expression IsH(int i) => Or(Eq(Cell(i), Str(H)), Eq(Cell(i), Str(W)));
    private static Expression IsL(int i) => Or(Eq(Cell(i), Str(L)), Eq(Cell(i), Str(W)));
    private static Expression IsW(int i) => Eq(Cell(i), Str(W));

    private static Expression All(Func<int, Expression> p) => And(And(p(0), p(1)), p(2));

    //  win = if allWild then 3
    //        else if allH then 5      (each cell ∈ {H,W}, but not all wild)
    //        else if allL then 2      (each cell ∈ {L,W}, but not all wild)
    //        else 0
    //  Order matters: allWild is checked first, so allH/allL only fire on a
    //  genuine non-wild win (allH ∧ allL ⇒ all wild, already handled).
    private static readonly Expression WinExpr = new IfExpr
    {
        Condition = All(IsW),
        ThenExpr = Int(3),
        ElseExpr = new IfExpr
        {
            Condition = All(IsH),
            ThenExpr = Int(5),
            ElseExpr = new IfExpr
            {
                Condition = All(IsL),
                ThenExpr = Int(2),
                ElseExpr = Int(0),
            },
        },
    };

    // ── Graph construction ───────────────────────────────────────────────

    private static DrawNode DrawCell(string id, string writeKey, bool isEntry) => new()
    {
        Id = id,
        Label = id,
        DrawWeights =
        [
            new DrawWeight { OutcomeId = H, Weight = 3, Value = 0 },
            new DrawWeight { OutcomeId = L, Weight = 3, Value = 0 },
            new DrawWeight { OutcomeId = W, Weight = 1, Value = 0 },
        ],
        StateWriteKey = writeKey,
        Inputs = isEntry
            ? new Dictionary<string, Port>()
            : new Dictionary<string, Port> { ["state"] = new() { Name = "state", Type = PortType.State } },
        Outputs = new Dictionary<string, Port> { ["state"] = new() { Name = "state", Type = PortType.State } },
    };

    private static Port StatePort => new() { Name = "state", Type = PortType.State };

    private static GraphConfig BuildConfig() => new()
    {
        SchemaVersion = "1.0.0",
        Id = "atomic-graph-proof",
        Name = "Atomic 1x3 (graph)",
        // Declare the state shape so the expression type-checker sees the
        // drawn cells and the win amount.
        StateSchema =
        [
            new StateFieldSchema { Name = "c0", Type = "string" },
            new StateFieldSchema { Name = "c1", Type = "string" },
            new StateFieldSchema { Name = "c2", Type = "string" },
            new StateFieldSchema { Name = "win", Type = "number" },
        ],
        Expressions = new Dictionary<string, Expression> { ["winExpr"] = WinExpr },
        Nodes =
        [
            DrawCell("d0", "c0", isEntry: true),
            DrawCell("d1", "c1", isEntry: false),
            DrawCell("d2", "c2", isEntry: false),
            new ModifyStateNode
            {
                Id = "win",
                Label = "Compute win",
                ExpressionId = "winExpr",
                OutputKey = "win",                       // ← write payout to state["win"]
                Inputs = new Dictionary<string, Port> { ["state"] = StatePort },
                Outputs = new Dictionary<string, Port> { ["state"] = StatePort },
            },
            new MetricsSinkNode
            {
                Id = "sink",
                Label = "Metrics",
                WinStateKey = "win",                     // ← read win from state["win"]
                Inputs = new Dictionary<string, Port> { ["state"] = StatePort },
            },
        ],
        Edges =
        [
            new Edge { Id = "e0", SourceNodeId = "d0", SourcePort = "state", TargetNodeId = "d1", TargetPort = "state" },
            new Edge { Id = "e1", SourceNodeId = "d1", SourcePort = "state", TargetNodeId = "d2", TargetPort = "state" },
            new Edge { Id = "e2", SourceNodeId = "d2", SourcePort = "state", TargetNodeId = "win", TargetPort = "state" },
            new Edge { Id = "e3", SourceNodeId = "win", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" },
        ],
    };

    private static SlotMath.Core.Monad.Slot<Dict, BigInteger> CompileProgram()
    {
        var result = new GraphCompiler().Compile(BuildConfig());
        Assert.True(result.IsValid,
            $"Expected valid graph, got: {string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}"))}");
        Assert.NotNull(result.Program);
        return result.Program!;
    }

    // ── KEY PROOF: exact RTP via the compiled graph equals 444/343 ────────

    [Fact]
    public void AtomicGraph_Compiles_NoMoleculeNeeded()
    {
        // The graph references no evaluator/transform/plugin — only Draw +
        // ModifyState(expression) + Sink.  It must still compile cleanly.
        var config = BuildConfig();
        Assert.Null(config.Plugins.FirstOrDefault());          // no plugins
        Assert.DoesNotContain(config.Nodes, n => n is MapNode); // no Map/evaluator molecule
        _ = CompileProgram();
    }

    [Fact]
    public void AtomicGraph_ExactRtp_Equals_444over343()
    {
        var program = CompileProgram();
        var result = ExactInterpreter.Evaluate(program, new Dict(), StateHasher.CanonicalHash);

        Assert.True(result.Distribution.IsFullyExact,
            "Expected no epsilon-pruning for this tiny game.");

        var (num, den) = result.ValueDistribution().ExpectedBigIntegerValue();
        Assert.Equal(new BigInteger(444), num);
        Assert.Equal(new BigInteger(343), den);
    }

    [Fact]
    public void AtomicGraph_ExactDistribution_HasDenominator343()
    {
        var program = CompileProgram();
        var dist = ExactInterpreter.Evaluate(program, new Dict(), StateHasher.CanonicalHash)
            .ValueDistribution();

        // Total mass = 1 and denominator = 7³ (three independent 7-weight draws).
        Assert.Equal(dist.TotalNumerator, dist.Denominator);
        Assert.Equal(new BigInteger(343), dist.Denominator);
    }

    [Fact]
    public void AtomicGraph_SampledRtp_ConvergesTo_444over343()
    {
        var program = CompileProgram();
        var r = SampledInterpreter.Evaluate(program, new Dict(),
            new SampledConfig { Seed = 7, MaxSpins = 200_000 });

        var exact = 444.0 / 343.0;
        Assert.InRange(r.Stats.Mean, exact - 3 * r.Stats.StdErr, exact + 3 * r.Stats.StdErr);
    }

    [Fact]
    public void AtomicGraph_IsDeterministic_SameSeedSameMean()
    {
        var program = CompileProgram();
        var cfg = new SampledConfig { Seed = 42, MaxSpins = 50_000 };
        var r1 = SampledInterpreter.Evaluate(program, new Dict(), cfg);
        var r2 = SampledInterpreter.Evaluate(program, new Dict(), cfg);
        Assert.Equal(r1.Stats.Mean, r2.Stats.Mean);
    }
}
