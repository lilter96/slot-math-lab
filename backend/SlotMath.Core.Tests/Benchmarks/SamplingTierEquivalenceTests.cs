using System.Numerics;
using System.Text.Json;
using CsCheck;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Benchmarks;

// SamplingOptions.Tier is process-wide; tests that select a tier must not overlap.
[CollectionDefinition("SamplingOptions", DisableParallelization = true)]
public sealed class SamplingOptionsCollection;

/// <summary>D25 / invariant 11 for the sampling fast paths: generated code,
/// code shared between expressions of one shape, and constant state all return
/// the value, the final state and the located error of the interpreted closures.</summary>
[Collection("SamplingOptions")]
public class SamplingTierEquivalenceTests
{
    private static readonly string[] Names = ["a", "b", "c", "d", "arr", "missing", "item", "acc", "position"];
    private static readonly Dictionary<string, int> Layout = Names.Select((name, index) => (name, index)).ToDictionary(p => p.name, p => p.index);
    private static int Slot(string name) => Layout[name];

    // One binder per tier for the whole run, so thousands of random expressions
    // meet in the same shape cache with different constants and fields.
    private static readonly SamplingExpressions SharedCompiled = new(Slot, SamplingTier.Compiled);
    private static readonly SamplingExpressions SharedTiered = new(Slot, SamplingTier.Tiered);

    private static ConstantExpr Constant(ConstantKind kind, string value) => new() { Kind = kind, Value = value };
    private static FieldAccessExpr F(string name) => new() { Path = [name], Target = "state" };
    private static GraphConfig Fixture(string file) => JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "DogHouse", file)), JsonOptions.Default)!;

    // Edge cases first: 64-bit boundaries, integers beyond them, a zero
    // denominator, numeric text with and without canonical form.
    private static readonly Gen<Expression> Leaf = Gen.OneOf(
        Gen.OneOfConst("0", "1", "-1", "2", "3", "7", "9223372036854775807", "-9223372036854775808", "123456789012345678901234567890").Select(v => (Expression)Constant(ConstantKind.Integer, v)),
        Gen.OneOfConst("1/3", "-7/2", "4/2", "1/0").Select(v => (Expression)Constant(ConstantKind.Rational, v)),
        Gen.OneOfConst("true", "false").Select(v => (Expression)Constant(ConstantKind.Boolean, v)),
        Gen.OneOfConst("2", "abc", "", "007", "-5").Select(v => (Expression)Constant(ConstantKind.String, v)),
        Gen.OneOfConst(Names).Select(name => (Expression)F(name)),
        Gen.OneOfConst(Names).Select(name => (Expression)F(name)));

    private static Gen<Expression> Tree(int depth)
    {
        if (depth == 0) return Leaf;
        var sub = Tree(depth - 1);
        return Gen.OneOf(
            Leaf,
            Gen.Select(Gen.OneOfConst(Enum.GetValues<BinaryOp>()), sub, sub, (op, left, right) => (Expression)new BinaryExpr { Op = op, Left = left, Right = right }),
            Gen.Select(Gen.OneOfConst(Enum.GetValues<CompareOp>()), sub, sub, (op, left, right) => (Expression)new CompareExpr { Op = op, Left = left, Right = right }),
            sub.Select(operand => (Expression)new NotExpr { Expr = operand }),
            Gen.Select(sub, sub, sub, (test, yes, no) => (Expression)new IfExpr { Condition = test, ThenExpr = yes, ElseExpr = no }),
            // Known functions at right and wrong arity, and an unknown one.
            Gen.Select(Gen.OneOfConst("tonumber", "toString", "length", "contains", "index", "max", "min", "append", "abs", "nope"), sub.Array[0, 3],
                (function, args) => (Expression)new CallExpr { Function = function, Args = args }),
            Gen.Select(sub, sub, (first, second) => (Expression)new CallExpr { Function = "append", Args = [new CallExpr { Function = "append", Args = [F("arr"), first] }, second] }),
            Gen.Select(Gen.Int[0, 4], sub, (form, body) => form switch
            {
                0 => (Expression)new MapExpr { StateKey = "arr", ItemName = "item", IndexName = "position", Body = body },
                1 => new FilterExpr { StateKey = "arr", ItemName = "item", Predicate = body },
                2 => new FoldExpr { StateKey = "arr", ItemName = "item", AccName = "acc", Init = Constant(ConstantKind.Integer, "0"), Body = body },
                3 => new AggregateExpr { StateKey = "arr", ItemName = "item", Func = AggregateFunc.Sum, ValueExpr = body },
                _ => new AggregateExpr { StateKey = "arr", ItemName = "item", Func = AggregateFunc.Count, Predicate = body },
            }));
    }

    // Small trees collide in the shape cache all the time; deep ones reach the
    // nested and interpreted forms.
    private static readonly Gen<Expression> Expressions = Gen.OneOf(Tree(1), Tree(1), Tree(2), Tree(3));

    private static readonly Gen<object?> Scalar = Gen.OneOf(
        Gen.Int[-3, 20].Select(n => (object?)new BigInteger(n)),
        Gen.OneOfConst<object?>(0, 1L, new BigInteger(long.MaxValue), new BigInteger(long.MinValue), BigInteger.Parse("123456789012345678901234567890"),
            ExprValue.Rational(1, 3), true, false, "2", "007", "abc", "", "-4", null));
    private static readonly Gen<object?> Value = Gen.OneOf(Scalar, Scalar, Scalar.Array[0, 5].Select(items => (object?)items));

    // A mask leaves fields out, so absent state is exercised as well as null.
    private static readonly Gen<Dict> State = Gen.Select(Value.Array[4], Scalar.Array[0, 6], Gen.Int[0, 31], (values, array, present) =>
    {
        var state = new Dict();
        for (var i = 0; i < 4; i++) if ((present & 1 << i) != 0) state[Names[i]] = values[i];
        if ((present & 16) != 0) state["arr"] = array;
        return state;
    });

    private static string Run(Func<SamplingFrame, ExprValue> code, Dict state)
    {
        var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], state);
        frame.Reset(default);
        string outcome;
        try { var value = code(frame); outcome = $"{value.Kind}:{StateHasher.CanonicalHash(new Dict { ["value"] = value.ToStateObject() })}"; }
        catch (ExpressionEvaluationException error) { outcome = $"{error.Code}|{error.Message}|{error.Location}"; }
        catch (Exception error) when (error is DivideByZeroException or FormatException or OverflowException or InvalidCastException) { outcome = $"{error.GetType().Name}|{error.Message}"; }
        return outcome + " state " + StateHasher.CanonicalHash(frame.Export());
    }

    private static string Print(Expression expression) => JsonSerializer.Serialize(expression, JsonOptions.Default);

    [Fact]
    public void Tiers_Pbt_10000_GeneratedAndSharedCodeMatchInterpretedValuesStateAndErrors()
    {
        Gen.Select(Expressions, State).Sample((expression, state) =>
        {
            var expected = Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(expression), state);
            Assert.Equal(expected, Run(new SamplingExpressions(Slot, SamplingTier.Compiled).Compile(expression), state));
            Assert.Equal(expected, Run(SharedCompiled.Compile(expression), state));

            // Below the threshold the closures run; the last calls run generated code.
            var tiered = SharedTiered.Compile(expression);
            for (var call = 0; call < SamplingOptions.HotThreshold + 2; call++)
                if (call < 2 || call >= SamplingOptions.HotThreshold) Assert.Equal(expected, Run(tiered, state));
                else Run(tiered, state);
        }, seed: "sampling-tier-equivalence-v1", iter: 10000, print: p => $"{Print(p.Item1)} on {JsonSerializer.Serialize(p.Item2.ToDictionary(f => f.Key, f => f.Value?.ToString()))}");
    }

    [Fact]
    public void ConstantState_Pbt_10000_FoldedReadsAndBranchesMatchUnfolded()
    {
        Gen.Select(Expressions, State, Gen.Int[0, 31]).Sample((expression, state, constant) =>
        {
            // Any readable field that is not an iteration variable may be constant.
            var constants = new Dictionary<string, ExprValue>();
            for (var i = 0; i < 5; i++)
                if ((constant & 1 << i) != 0 && state.TryGetValue(Names[i], out var raw) && SamplingCell.FromRaw(raw) is { Readable: true } cell) constants[Names[i]] = cell.StoredValue;
            var expected = Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(expression), state);
            foreach (var tier in Enum.GetValues<SamplingTier>())
            {
                var folded = new SamplingExpressions(Slot, tier, constants).Compile(expression);
                for (var call = 0; call < (tier == SamplingTier.Tiered ? SamplingOptions.HotThreshold + 1 : 1); call++) Assert.Equal(expected, Run(folded, state));
            }
        }, seed: "sampling-constant-state-v1", iter: 10000, print: p => $"{Print(p.Item1)} constants {p.Item3}");
    }

    [Fact]
    public void SameShape_SharesOneMethod_AndKeepsEachExpressionsOperands()
    {
        var binder = new SamplingExpressions(Slot, SamplingTier.Compiled);
        Expression Pick(string field, string position, string add) => new BinaryExpr
        {
            Op = BinaryOp.Add,
            Left = new CallExpr { Function = "tonumber", Args = [new CallExpr { Function = "index", Args = [F(field), Constant(ConstantKind.Integer, position)] }] },
            Right = Constant(ConstantKind.Integer, add),
        };
        var first = binder.Compile(Pick("arr", "0", "10")); var second = binder.Compile(Pick("a", "2", "-1"));
        Assert.Equal(1, binder.Shapes.Count);
        var state = new Dict { ["arr"] = new object[] { "5", "6", "7" }, ["a"] = new object[] { "1", "2", "3" } };
        Assert.Equal(Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(Pick("arr", "0", "10")), state), Run(first, state));
        Assert.Equal(Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(Pick("a", "2", "-1")), state), Run(second, state));
        Assert.NotEqual(Run(first, state), Run(second, state));

        // A different operator, constant kind or function is a different shape.
        binder.Compile(new BinaryExpr { Op = BinaryOp.Sub, Left = F("a"), Right = F("b") });
        binder.Compile(new BinaryExpr { Op = BinaryOp.Add, Left = F("a"), Right = F("b") });
        binder.Compile(new BinaryExpr { Op = BinaryOp.Add, Left = F("a"), Right = Constant(ConstantKind.Integer, "1") });
        binder.Compile(new BinaryExpr { Op = BinaryOp.Add, Left = F("a"), Right = Constant(ConstantKind.Rational, "1/3") });
        Assert.Equal(5, binder.Shapes.Count);
    }

    [Fact]
    public void IterationVariable_IsNeverConstantState()
    {
        var binder = new SamplingExpressions(Slot, SamplingTier.Interpreted, new Dictionary<string, ExprValue> { ["item"] = ExprValue.Number(1) });
        Assert.Throws<ConstantStateConflict>(() => binder.Compile(new MapExpr { StateKey = "arr", ItemName = "item", Body = F("item") }));
    }

    [Fact]
    public void FullGame_Pbt_1000PerTier_PayoutAndFinalStateMatchReference_WithAndWithoutConstantState()
    {
        var before = SamplingOptions.Tier;
        try
        {
            foreach (var tier in Enum.GetValues<SamplingTier>())
            {
                SamplingOptions.Tier = tier;
                var result = new GraphCompiler().Compile(Fixture("dog-house-ui.json"));
                Assert.True(result.IsValid); Assert.Equal("compiled-state-plan-v1", result.SamplingEngine);
                // 0: the graph's own initial state, where trace recording is
                // constant-false. 1: an unrelated field. 2, 3: the runner
                // overrides a field the plan treats as constant.
                Gen.Select(Gen.Int[0, 1000000], Gen.Int[0, 500], Gen.Int[0, 3]).Sample((seed, round, start) =>
                {
                    var state = start switch { 0 => new Dict(), 1 => new Dict { ["external"] = "preserved" }, 2 => new Dict { ["traceEnabled"] = true }, _ => new Dict { ["traceEnabled"] = false } };
                    var expected = SampledInterpreter.RunSingle(result.ReferenceProgram!, state, seed, round);
                    var actual = SampledInterpreter.RunSingle(result.Program!, state, seed, round);
                    Assert.Equal(expected.Value, actual.Value);
                    Assert.Equal(StateHasher.CanonicalHash(expected.State), StateHasher.CanonicalHash(actual.State));
                }, seed: "sampling-tier-full-game-v1", iter: 1000);
            }
        }
        finally { SamplingOptions.Tier = before; }
    }

    [Fact]
    public void Sampled_StatisticsAreBitIdentical_AcrossTiersAndWorkerCounts()
    {
        var before = SamplingOptions.Tier;
        try
        {
            string? expected = null;
            foreach (var tier in Enum.GetValues<SamplingTier>())
            {
                SamplingOptions.Tier = tier;
                var result = new GraphCompiler().Compile(Fixture("dog-house-ui.json"));
                expected ??= JsonSerializer.Serialize(SampledInterpreter.Evaluate(result.ReferenceProgram!, new Dict(),
                    new SampledConfig { Seed = 42, MaxSpins = 20000, ChunkSize = 2048, WinScale = (double)result.WinScale }).Stats.Snapshot());
                foreach (var workers in new[] { 1, 3, 8 })
                    Assert.Equal(expected, JsonSerializer.Serialize(SampledInterpreter.Evaluate(result.Program!, new Dict(),
                        new SampledConfig { Seed = 42, MaxSpins = 20000, ChunkSize = 2048, DegreeOfParallelism = workers, WinScale = (double)result.WinScale }).Stats.Snapshot()));
            }
        }
        finally { SamplingOptions.Tier = before; }
    }
}
