using System.Numerics;
using System.Text.Json;
using CsCheck;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Benchmarks;

public class SamplingPlanEquivalenceTests
{
    private static ConstantExpr N(int n) => new() { Kind = ConstantKind.Integer, Value = n.ToString() };
    private static FieldAccessExpr F(string name) => new() { Path = [name], Target = "state" };
    private static BinaryExpr Add(Expression a, Expression b) => new() { Op = BinaryOp.Add, Left = a, Right = b };
    private static CallExpr Call(string name, params Expression[] args) => new() { Function = name, Args = args };
    private static GraphConfig Fixture(string file) => JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "DogHouse", file)), JsonOptions.Default)!;

    private static ExprValue Native(Expression expr, Dict state, out Dict final)
    {
        var layout = new Dictionary<string, int>();
        int Slot(string name) { if (!layout.TryGetValue(name, out var i)) layout[name] = i = layout.Count; return i; }
        var compiled = new SamplingExpressions(Slot).Compile(expr);
        foreach (var key in state.Keys) Slot(key);
        var frame = new SamplingFrame(layout, new SamplingCell[layout.Count], state);
        frame.Reset(default);
        var value = compiled(frame); final = frame.Export(); return value;
    }

    private static void Equivalent(Expression expr, Dict state)
    {
        var expected = ExactExpressionEvaluator.Evaluate(expr, new EvalContext { State = state });
        Assert.Equal(expected, Native(expr, state, out var final));
        Assert.Equal(StateHasher.CanonicalHash(state), StateHasher.CanonicalHash(final));
    }

    [Fact]
    public void Expressions_Pbt_10000_IterationRationalsAndShortCircuit()
    {
        var mapped = new MapExpr { StateKey = "values", ItemName = "item", IndexName = "position", Body = new BinaryExpr { Op = BinaryOp.Div, Left = Add(F("item"), F("position")), Right = N(3) } };
        var fold = new FoldExpr { StateKey = "values", ItemName = "item", AccName = "acc", IndexName = "position", Init = N(0), Body = Add(F("acc"), new IfExpr { Condition = new CompareExpr { Op = CompareOp.Gt, Left = F("item"), Right = N(0) }, ThenExpr = F("item"), ElseExpr = F("position") }) };
        Gen.Int[-200, 200].Array[0, 12].Sample(values =>
        {
            var state = new Dict { ["values"] = values.Select(n => (object)new BigInteger(n)).ToArray(), ["acc"] = "outer", ["position"] = false };
            Equivalent(mapped, state); Equivalent(fold, state);
            Equivalent(new FilterExpr { StateKey = "values", ItemName = "item", IndexName = "position", Predicate = new CompareExpr { Op = CompareOp.Gte, Left = F("item"), Right = F("position") } }, state);
            foreach (var func in new[] { AggregateFunc.Sum, AggregateFunc.Count, AggregateFunc.Product })
                Equivalent(new AggregateExpr { StateKey = "values", ItemName = "item", Func = func, ValueExpr = new BinaryExpr { Op = BinaryOp.Div, Left = F("item"), Right = N(3) } }, state);
        }, seed: "sampling-plan-expressions-v1", iter: 10000);
    }

    [Fact]
    public void FullGame_Pbt_1000_PayoutFinalStateAndBonusTraceMatchReference()
    {
        var result = new GraphCompiler().Compile(Fixture("dog-house-ui.json"));
        Assert.True(result.IsValid); Assert.Equal("compiled-state-plan-v1", result.SamplingEngine);
        Gen.Select(Gen.Int[0, 1000000], Gen.Int[0, 500], Gen.Bool).Sample((seed, round, trace) =>
        {
            var state = new Dict { ["traceEnabled"] = trace, ["external"] = "preserved" };
            var before = StateHasher.CanonicalHash(state);
            var expected = SampledInterpreter.RunSingle(result.ReferenceProgram!, state, seed, round);
            var actual = SampledInterpreter.RunSingle(result.Program!, state, seed, round);
            Assert.Equal(expected.Value, actual.Value);
            Assert.Equal(StateHasher.CanonicalHash(expected.State), StateHasher.CanonicalHash(actual.State));
            Assert.Equal(before, StateHasher.CanonicalHash(state));
        }, seed: "sampling-plan-full-game-v1", iter: 1000);
        var bonus = SampledInterpreter.RunSingle(result.Program!, new Dict { ["traceEnabled"] = true }, 42, 230);
        var referenceBonus = SampledInterpreter.RunSingle(result.ReferenceProgram!, new Dict { ["traceEnabled"] = true }, 42, 230);
        Assert.Equal(StateHasher.CanonicalHash(referenceBonus.State), StateHasher.CanonicalHash(bonus.State));
    }

    [Fact]
    public void Sampled_AllStatisticsAndHistogramAreBitIdentical_WithParallelismAndLiveProgress()
    {
        var result = new GraphCompiler().Compile(Fixture("dog-house-ui.json"));
        var expected = SampledInterpreter.Evaluate(result.ReferenceProgram!, new Dict(), new SampledConfig { Seed = 42, MaxSpins = 10000, ChunkSize = 4096, WinScale = (double)result.WinScale }).Stats.Snapshot();
        foreach (var workers in new[] { 1, 2, 4 })
        {
            long count = 0;
            var actual = SampledInterpreter.Evaluate(result.Program!, new Dict(), new SampledConfig { Seed = 42, MaxSpins = 10000, ChunkSize = 4096, DegreeOfParallelism = workers, WinScale = (double)result.WinScale,
                ProgressCallback = p => { Assert.True(p.SpinsCompleted >= count); count = p.SpinsCompleted; } }).Stats.Snapshot();
            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
            Assert.Equal(10000, count);
        }
    }

    [Fact]
    public void PartialOperationsPreserveLocatedErrors_AndIfRemainsLazy()
    {
        var errors = new Expression[] { F("absent"), Call("index", F("values"), N(-1)), new FieldAccessExpr { Path = ["values", "3"] }, new BinaryExpr { Op = BinaryOp.Div, Left = N(1), Right = N(0) }, new AggregateExpr { StateKey = "empty", Func = AggregateFunc.Min }, new AggregateExpr { StateKey = "missing", Func = AggregateFunc.Product } };
        var state = new Dict { ["values"] = new object[] { 1 }, ["empty"] = Array.Empty<object>() };
        foreach (var expr in errors)
        {
            var expected = Assert.Throws<ExpressionEvaluationException>(() => ExactExpressionEvaluator.Evaluate(expr, new EvalContext { State = state }));
            var actual = Assert.Throws<ExpressionEvaluationException>(() => Native(expr, state, out _));
            Assert.Equal(expected.Code, actual.Code); Assert.Equal(expected.Location, actual.Location);
            Equivalent(new IfExpr { Condition = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "false" }, ThenExpr = expr, ElseExpr = N(7) }, state);
        }
    }

    [Fact]
    public void RecordFieldsRawTypesAndTypedArraySemanticsArePreserved()
    {
        var state = new Dict { ["record"] = new Dict { ["a"] = new Dict { ["b"] = new BigInteger(7) } }, ["values"] = new object?[] { 8L, ExprValue.Rational(1, 3), null, true, "2", new object[] { 1 } }, ["rawTypedArray"] = ExprValue.Array([ExprValue.Number(3)]), ["list"] = new List<object?> { 5L, "6" } };
        Equivalent(new FieldAccessExpr { Path = ["record", "a", "b"] }, state);
        for (var i = 0; i < 6; i++) Equivalent(new FieldAccessExpr { Path = ["values", i.ToString()] }, state);
        Equivalent(new MapExpr { StateKey = "values", ItemName = "item", Body = F("item") }, state);
        Equivalent(new FilterExpr { StateKey = "values", ItemName = "item", Predicate = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" } }, state);
        Equivalent(F("record"), state);
        Equivalent(new FoldExpr { StateKey = "rawTypedArray", ItemName = "item", AccName = "acc", Init = N(9), Body = N(0) }, state);
        Equivalent(new AggregateExpr { StateKey = "list", Func = AggregateFunc.Sum }, state);
    }

    [Fact]
    public void ExactPmfAndComposedMonadsStillUseTheCanonicalProgram()
    {
        var compiled = new GraphCompiler().Compile(Fixture("dog-house-mini-ui.json"));
        var expected = ExactInterpreter.Evaluate(compiled.ReferenceProgram!, new Dict(), StateHasher.CanonicalHash).ValueDistribution();
        var actual = ExactInterpreter.Evaluate(compiled.Program!, new Dict(), StateHasher.CanonicalHash).ValueDistribution();
        Assert.Equal(expected.Denominator, actual.Denominator); Assert.Equal(expected.Entries, actual.Entries);
        var composed = compiled.Program!.Select(win => win + 1);
        Assert.Equal(SampledInterpreter.RunSingle(compiled.ReferenceProgram!, new Dict(), 7).Value + 1, SampledInterpreter.RunSingle(composed, new Dict(), 7).Value);
    }

    [Fact]
    public void CancellationBeforeFirstRoundIsHonored()
    {
        var compiled = new GraphCompiler().Compile(Fixture("dog-house-ui.json"));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var result = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new SampledConfig { Seed = 1, MaxSpins = 1000, CancellationToken = cts.Token });
        Assert.True(result.WasCancelled); Assert.Equal(0, result.SpinsCompleted);
        Assert.Throws<OperationCanceledException>(() => SampledInterpreter.RunSingle(compiled.Program!, new Dict(), 1, cancellationToken: cts.Token));
    }

    [Fact]
    public void ExportedDefaultsCannotMutateTheSharedPlan_ConcurrentRunnersAreIndependent()
    {
        var compiled = new GraphCompiler().Compile(Fixture("dog-house-ui.json"));
        var first = SampledInterpreter.RunSingle(compiled.Program!, new Dict(), 42, 230);
        var expected = StateHasher.CanonicalHash(first.State);
        ((object?[])first.State["baseReel0"]!)[0] = "corrupted";
        ((object?[])first.State["linePaytable"]!)[0] = "corrupted";
        Parallel.For(0, 32, _ =>
        {
            var next = SampledInterpreter.RunSingle(compiled.Program!, new Dict(), 42, 230);
            Assert.Equal(expected, StateHasher.CanonicalHash(next.State));
        });
    }

    [Fact]
    public void GenericControlFlow_FanOutStateOperationsLoopAccumulationAndCapMatchReference()
    {
        Dictionary<string, Port> Ports(params string[] names) => names.ToDictionary(n => n, n => new Port { Name = n, Type = PortType.Number });
        Edge E(string source, string port, string target) => new() { Id = source + port + target, SourceNodeId = source, SourcePort = port, TargetNodeId = target, TargetPort = "in" };
        var config = new GraphConfig
        {
            SchemaVersion = "1.0", Expressions = new() { ["pass"] = new CompareExpr { Op = CompareOp.Gt, Left = Call("tonumber", F("choice")), Right = N(1) }, ["legacy"] = new BinaryExpr { Op = BinaryOp.Div, Left = N(7), Right = N(2) } },
            Nodes = [
                new DrawNode { Id = "draw", StateWriteKey = "choice", Outputs = Ports("out"), DrawWeights = [new() { OutcomeId = "1", Weight = 1, Value = 1 }, new() { OutcomeId = "2", Weight = 2, Value = 2 }] },
                new ModifyStateNode { Id = "modify", ExpressionId = "legacy", Inputs = Ports("in"), Outputs = Ports("out") },
                new BranchNode { Id = "branch", ConditionId = "pass", Inputs = Ports("in"), Outputs = Ports("true", "false") },
                new LoopNode { Id = "loop", MaxIterations = 3, Inputs = Ports("in"), Outputs = Ports("body", "exit") },
                new DrawNode { Id = "body", Inputs = Ports("in"), DrawWeights = [new() { OutcomeId = "a", Weight = 1, Value = 1 }, new() { OutcomeId = "b", Weight = 1, Value = 3 }] },
                new GetStateNode { Id = "get", StateKey = "__modified__", Inputs = Ports("in"), Outputs = Ports("out") },
                new PutStateNode { Id = "put-a", StateKey = "a", Inputs = Ports("in"), Outputs = Ports("out") },
                new PutStateNode { Id = "put-b", StateKey = "b", Inputs = Ports("in"), Outputs = Ports("out") },
                new MetricsSinkNode { Id = "sink", WinCap = 9, Inputs = Ports("in") }
            ],
            Edges = [E("draw", "out", "modify"), E("modify", "out", "branch"), E("branch", "true", "loop"), E("branch", "false", "get"), E("loop", "body", "body"), E("loop", "exit", "get"), E("get", "out", "put-a"), E("get", "out", "put-b"), E("put-a", "out", "sink"), E("put-b", "out", "sink")],
        };
        var compiled = new GraphCompiler().Compile(config);
        Assert.True(compiled.IsValid, string.Join("; ", compiled.Errors.Select(e => e.Message)));
        Assert.Equal("compiled-state-plan-v1", compiled.SamplingEngine);
        Gen.Int[0, 1000000].Sample(seed =>
        {
            var expected = SampledInterpreter.RunSingle(compiled.ReferenceProgram!, new Dict(), seed);
            var actual = SampledInterpreter.RunSingle(compiled.Program!, new Dict(), seed);
            Assert.Equal(expected.Value, actual.Value);
            Assert.True(actual.Value <= 9);
            Assert.Equal(StateHasher.CanonicalHash(expected.State), StateHasher.CanonicalHash(actual.State));
        }, seed: "sampling-plan-control-v1", iter: 1000);
    }
}
