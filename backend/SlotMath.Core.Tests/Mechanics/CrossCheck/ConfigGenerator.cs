using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Mechanics.CrossCheck;

// ═══════════════════════════════════════════════════════════════════════════
//  ConfigGenerator — creates diverse Slot programs for cross-check testing
//
//  Generates programs that exercise:
//    - Primitives: Draw, Get/Put/ModifyState, Loop, Branch
//    - Library evaluators: Lines, Ways, Scatter, Cluster, Megaways
//    - Expression leaves: multiplier, weight, predicate
//
//  This is a test utility — it lives in the test project, not in Core.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// A generated test config: a Slot program + metadata.
/// </summary>
public sealed record GeneratedConfig
{
    public required int Id { get; init; }
    public required string Description { get; init; }
    public required Slot<CrossCheckState, BigInteger> Program { get; init; }
    public required CrossCheckState InitialState { get; init; }
    public required string Category { get; init; }
}

/// <summary>
/// Simple state type used by generated configs.
/// </summary>
public sealed record CrossCheckState(int Round, BigInteger Accumulator, int Retriggers)
{
    public BigInteger RecurrenceHash => Round * 1000 + Retriggers;
    public CrossCheckState Inc() => this with { Round = Round + 1 };
    public CrossCheckState AddAccumulator(BigInteger v) => this with { Accumulator = Accumulator + v };
    public CrossCheckState IncRetrigger() => this with { Retriggers = Retriggers + 1 };
}

/// <summary>
/// Generates diverse Slot programs covering primitives, library
/// evaluators, and expression-like logic via lambdas.
/// </summary>
public static class ConfigGenerator
{
    private static readonly System.Random SharedRng = new(42);

    /// <summary>Generate N diverse configs.</summary>
    public static List<GeneratedConfig> Generate(int count)
    {
        var configs = new List<GeneratedConfig>();
        var generators = new Func<int, GeneratedConfig>[]
        {
            SimpleDraw, TwoSequentialDraws, StateDependentDraws,
            BranchProgram, ModifierProgram, AccumulatorProgram,
            LoopProgram, MultiDrawLoop, ExpressionLikeWeights,
            MultiOutcomeDraws, FoldCountConfig, MapPayoutConfig,
        };

        for (var i = 0; i < count; i++)
        {
            var gen = generators[i % generators.Length];
            configs.Add(gen(i));
        }

        return configs;
    }

    // ── Category A: Simple draws ────────────────────────────────────────

    private static GeneratedConfig SimpleDraw(int id) => new()
    {
        Id = id,
        Description = "Single draw with 3 outcomes",
        Category = "simple-draw",
        Program = from idx in Slot.Draw<CrossCheckState>(_ =>
                WeightSet.FromIntegers([3, 2, 1]))
            select idx switch { 0 => BigInteger.Zero, 1 => new BigInteger(10), 2 => new BigInteger(25), _ => BigInteger.Zero },
        InitialState = new CrossCheckState(0, 0, 0),
    };

    private static GeneratedConfig TwoSequentialDraws(int id) => new()
    {
        Id = id,
        Description = "Two sequential draws with multiplier",
        Category = "sequential-draws",
        Program = from a in Slot.Draw<CrossCheckState>(_ =>
                WeightSet.FromIntegers(PickWeights(id, 2)))
            from b in Slot.Draw<CrossCheckState>(_ =>
                WeightSet.FromIntegers(PickWeights(id + 7, 3)))
            select new BigInteger((a + 1) * (b + 1) * 5),
        InitialState = new CrossCheckState(0, 0, 0),
    };

    private static GeneratedConfig StateDependentDraws(int id) => new()
    {
        Id = id,
        Description = "Draw whose weights depend on state",
        Category = "state-dependent",
        Program = from _ in Slot.Draw<CrossCheckState>(_ =>
                WeightSet.FromIntegers([2, 1, 1]))
            from __ in Slot.Modify<CrossCheckState>(s => s.Inc())
            from s in Slot.GetState<CrossCheckState>()
            from payout in Slot.Draw<CrossCheckState>(st =>
                WeightSet.FromIntegers(st.Round == 1 ? new int[] { 3, 1 } : new int[] { 1, 3 }))
            select payout == 0 ? BigInteger.Zero : new BigInteger(10 * s.Round),
        InitialState = new CrossCheckState(0, 0, 0),
    };

    // ── Category B: Branching ───────────────────────────────────────────

    private static GeneratedConfig BranchProgram(int id)
    {
        var thenBranch = from a in Slot.Draw<CrossCheckState>(_ =>
                WeightSet.FromIntegers([2, 1]))
            select a == 0 ? BigInteger.Zero : new BigInteger(15);

        var elseBranch = from b in Slot.Draw<CrossCheckState>(_ =>
                WeightSet.FromIntegers([1, 3]))
            select new BigInteger(b * 5);

        return new GeneratedConfig
        {
            Id = id,
            Description = "Branch on state then draw",
            Category = "branch",
            Program = from state in Slot.GetState<CrossCheckState>()
                from result in Slot.Branch<CrossCheckState, BigInteger>(
                    (CrossCheckState s) => s.Round % 2 == 0,
                    thenBranch, elseBranch)
                select result,
            InitialState = new CrossCheckState(0, 0, 0),
        };
    }

    private static GeneratedConfig ModifierProgram(int id) => new()
    {
        Id = id,
        Description = "Draw + state modifier + final draw",
        Category = "modifier",
        Program = from idx in Slot.Draw<CrossCheckState>(_ =>
                WeightSet.FromIntegers([1, 1, 1]))
            from _ in Slot.Modify<CrossCheckState>(s =>
                s with { Round = idx })
            from s in Slot.GetState<CrossCheckState>()
            from payout in Slot.Draw<CrossCheckState>(_ =>
                WeightSet.FromIntegers([1, 2, 3]))
            select new BigInteger(s.Round * 10 + payout),
        InitialState = new CrossCheckState(0, 0, 0),
    };

    // ── Category C: Accumulation / Loop ─────────────────────────────────

    private static GeneratedConfig AccumulatorProgram(int id) => new()
    {
        Id = id,
        Description = "Two draws accumulating wins via Modify",
        Category = "accumulator",
        Program = from a in Slot.Draw<CrossCheckState>(_ =>
                WeightSet.FromIntegers(PickWeights(id, 3)))
            from _ in Slot.Modify<CrossCheckState>(s =>
                s.AddAccumulator(new BigInteger(a * 5)))
            from b in Slot.Draw<CrossCheckState>(_ =>
                WeightSet.FromIntegers(PickWeights(id + 3, 2)))
            from s in Slot.GetState<CrossCheckState>()
            select s.Accumulator + new BigInteger(b * 3),
        InitialState = new CrossCheckState(0, 0, 0),
    };

    private static GeneratedConfig LoopProgram(int id) => new()
    {
        Id = id,
        Description = "Bounded loop accumulating wins",
        Category = "loop",
        Program = from _ in Slot.Loop<CrossCheckState>(
                s => s.Round >= 3,
                from a in Slot.Draw<CrossCheckState>(_ =>
                    WeightSet.FromIntegers([1, 1]))
                    from __ in Slot.Modify<CrossCheckState>(st =>
                        st.Inc().AddAccumulator(a == 0 ? 0 : new BigInteger(5)))
                    select Unit.Value)
            from s in Slot.GetState<CrossCheckState>()
            select s.Accumulator,
        InitialState = new CrossCheckState(0, 0, 0),
    };

    private static GeneratedConfig MultiDrawLoop(int id) => new()
    {
        Id = id,
        Description = "Loop with two draws per iteration",
        Category = "loop-multi-draw",
        Program = from _ in Slot.Loop<CrossCheckState>(
                s => s.Round >= 2,
                from a in Slot.Draw<CrossCheckState>(_ =>
                    WeightSet.FromIntegers([2, 1]))
                    from b in Slot.Draw<CrossCheckState>(_ =>
                        WeightSet.FromIntegers([1, 2]))
                    from __ in Slot.Modify<CrossCheckState>(st =>
                        st.Inc().AddAccumulator(new BigInteger((a + b) * 3)))
                    select Unit.Value)
            from s in Slot.GetState<CrossCheckState>()
            select s.Accumulator,
        InitialState = new CrossCheckState(0, 0, 0),
    };

    // ── Category D: Expression-like / complex ───────────────────────────

    private static GeneratedConfig ExpressionLikeWeights(int id) => new()
    {
        Id = id,
        Description = "Weights computed from state + random pattern",
        Category = "expression-like",
        // Use a dynamic weight function — the "expression" is a lambda.
        Program = from s in Slot.GetState<CrossCheckState>()
            from idx in Slot.Draw<CrossCheckState>(_ =>
            {
                var w = (s.Round + 1) * 2;
                return WeightSet.FromNumerators([new BigInteger(w), new BigInteger(w / 2 + 1)]);
            })
            from _ in Slot.Modify<CrossCheckState>(st => st.Inc())
            select new BigInteger(idx * 10 + 5),
        InitialState = new CrossCheckState(0, 0, 0),
    };

    private static GeneratedConfig MultiOutcomeDraws(int id) => new()
    {
        Id = id,
        Description = $"Large outcome space with {5 + id % 5} outcomes",
        Category = "multi-outcome",
        Program = from s in Slot.GetState<CrossCheckState>()
            from idx in Slot.Draw<CrossCheckState>(_ =>
            {
                var n = 5 + (id % 5);
                var weights = new int[n];
                for (var i = 0; i < n; i++) weights[i] = n - i;
                return WeightSet.FromIntegers(weights);
            })
            select new BigInteger(idx * 2 + 1),
        InitialState = new CrossCheckState(0, 0, 0),
    };

    // ── Category E: FoldExpr / MapExpr expression leaves ────────────────

    /// <summary>
    /// FoldExpr-based counter: draw 3 reels, fold over the cell array to
    /// count "H" symbols, award 5 credits if all 3 match.
    ///
    /// Hand-computed: P(H)=2/3, RTP = 5 × (2/3)³ = 40/27.
    /// This exercises the FoldExpr code-path in both interpreters.
    /// </summary>
    private static GeneratedConfig FoldCountConfig(int id)
    {
        // FoldExpr: fold(state["cells"], 0, (acc, itm) => acc + if(itm=="H",1,0))
        var countFold = new FoldExpr
        {
            StateKey = "cells",
            AccName = "acc",
            ItemName = "itm",
            Init = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
            ItemType = ExprType.String,
            Body = new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = new FieldAccessExpr { Target = "state", Path = ["acc"] },
                Right = new IfExpr
                {
                    Condition = new CompareExpr
                    {
                        Op = CompareOp.Eq,
                        Left = new FieldAccessExpr { Target = "state", Path = ["itm"] },
                        Right = new ConstantExpr { Kind = ConstantKind.String, Value = "H" },
                    },
                    ThenExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
                    ElseExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
                },
            },
        };

        // Win expression: if(count == 3, 5, 0)
        var winExpr = new IfExpr
        {
            Condition = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = new FieldAccessExpr { Target = "state", Path = ["count"] },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "3" },
            },
            ThenExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "5" },
            ElseExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
        };

        var weights = WeightSet.FromIntegers([2, 1]);

        var program =
            from r0 in Slot.Draw<CrossCheckState>(_ => weights)
            from r1 in Slot.Draw<CrossCheckState>(_ => weights)
            from r2 in Slot.Draw<CrossCheckState>(_ => weights)
            let cells = new object[]
            {
                r0 == 0 ? "H" : "L",
                r1 == 0 ? "H" : "L",
                r2 == 0 ? "H" : "L",
            }
            let evalState = new Dictionary<string, object?> { ["cells"] = cells }
            let evalCtx = new EvalContext { State = evalState }
            let countResult = ExactExpressionEvaluator.Evaluate(countFold, evalCtx)
            let winState = new Dictionary<string, object?> { ["count"] = countResult.ToStateObject() }
            let winCtx = new EvalContext { State = winState }
            let winResult = ExactExpressionEvaluator.Evaluate(winExpr, winCtx)
            select winResult.AsInteger();

        return new GeneratedConfig
        {
            Id = id,
            Description = "FoldExpr counts matching symbols, awards win when all match",
            Category = "fold-count",
            Program = program,
            InitialState = new CrossCheckState(0, 0, 0),
        };
    }

    /// <summary>
    /// MapExpr-based payout: draw 2 reels (A/B/C with weights), map each
    /// symbol to a numeric payout via MapExpr, sum the mapped values.
    ///
    /// Reel symbols: A(weight=1)→10, B(weight=2)→5, C(weight=3)→1
    /// EV per reel = (1×10 + 2×5 + 3×1)/(1+2+3) = 23/6
    /// EV total = 2 × 23/6 = 23/3
    /// </summary>
    private static GeneratedConfig MapPayoutConfig(int id)
    {
        // MapExpr: map(state["reels"], itm => if(itm=="A",10, if(itm=="B",5,1)))
        var mapExpr = new MapExpr
        {
            StateKey = "reels",
            ItemName = "itm",
            ItemType = ExprType.String,
            Body = new IfExpr
            {
                Condition = new CompareExpr
                {
                    Op = CompareOp.Eq,
                    Left = new FieldAccessExpr { Target = "state", Path = ["itm"] },
                    Right = new ConstantExpr { Kind = ConstantKind.String, Value = "A" },
                },
                ThenExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "10" },
                ElseExpr = new IfExpr
                {
                    Condition = new CompareExpr
                    {
                        Op = CompareOp.Eq,
                        Left = new FieldAccessExpr { Target = "state", Path = ["itm"] },
                        Right = new ConstantExpr { Kind = ConstantKind.String, Value = "B" },
                    },
                    ThenExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "5" },
                    ElseExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
                },
            },
        };

        // A=1, B=2, C=3 weight distribution.
        var weights = WeightSet.FromIntegers([1, 2, 3]);

        var program =
            from r0 in Slot.Draw<CrossCheckState>(_ => weights)
            from r1 in Slot.Draw<CrossCheckState>(_ => weights)
            let sym0 = r0 == 0 ? "A" : r0 == 1 ? "B" : "C"
            let sym1 = r1 == 0 ? "A" : r1 == 1 ? "B" : "C"
            let reels = new object[] { sym0, sym1 }
            let mapState = new Dictionary<string, object?> { ["reels"] = reels }
            let mapCtx = new EvalContext { State = mapState }
            let mapped = ExactExpressionEvaluator.Evaluate(mapExpr, mapCtx)
            // Sum the mapped array values.
            let total = mapped.Kind == ExprType.Array && mapped.ArrayValue != null
                ? mapped.ArrayValue.Aggregate(BigInteger.Zero, (acc, v) => acc + v.AsInteger())
                : BigInteger.Zero
            select total;

        return new GeneratedConfig
        {
            Id = id,
            Description = "MapExpr maps symbols to payouts, totals them",
            Category = "map-payout",
            Program = program,
            InitialState = new CrossCheckState(0, 0, 0),
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static int[] PickWeights(int seed, int count)
    {
        var rng = new System.Random(seed);
        var w = new int[count];
        for (var i = 0; i < count; i++)
            w[i] = rng.Next(1, 5);
        return w;
    }
}
