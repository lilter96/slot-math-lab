using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Benchmarks;

public class SamplingMaskStateTests
{
    private static ConstantExpr N(string value) => new() { Kind = ConstantKind.Integer, Value = value };
    private static FieldAccessExpr F(string key) => new() { Target = "state", Path = [key] };
    private static BitSetExpr Set(BitSetOp op, params Expression[] args) => new() { Op = op, Args = args };
    private static Dictionary<string, JsonElement> Initial(Dict values) => values.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value));
    private static ModifyStateNode Write(string id, string key) => new() { Id = id, ExpressionId = id, OutputKey = key };

    [Fact]
    public void InvariantsRequireValidInitialValuesAndEveryWriteIncludingCycles()
    {
        var config = new GraphConfig
        {
            SchemaVersion = "1.0",
            InitialState = Initial(new() { ["a"] = 0, ["b"] = 1, ["bad"] = 0, ["copy"] = 0, ["signed"] = -1, ["text"] = "3", ["fraction"] = 0.5, ["large"] = ulong.MaxValue }),
            Nodes = [Write("a", "a"), Write("b", "b"), Write("bad", "bad"), Write("copy", "copy")],
            Expressions = new() { ["a"] = F("b"), ["b"] = F("a"), ["bad"] = N("-1"), ["copy"] = F("bad") },
        };
        Assert.Equal(new[] { "a", "b", "large" }, SamplingMaskState.Infer(config, []).Order());
        var broken = config with { Expressions = new(config.Expressions!) { ["b"] = N("-1") } };
        Assert.Equal(new[] { "large" }, SamplingMaskState.Infer(broken, []).Order());
    }

    [Fact]
    public void UnknownWritersAndTemporaryMeasurementAliasesInvalidateFields()
    {
        var config = new GraphConfig
        {
            SchemaVersion = "1.0",
            InitialState = Initial(new() { ["a"] = 0, ["b"] = 0, ["c"] = 0, ["d"] = 0, ["alias"] = 0, ["safe"] = 0 }),
            Nodes = [new DrawNode { Id = "draw", StateWriteKey = "a", BoardStateKey = "d" }, new PutStateNode { Id = "put", StateKey = "b" }, new DataNode { Id = "data", StateKey = "c" }],
        };
        var measurements = new[] { new MeasurementDefinition { Id = "m", Name = "m", Value = new MapExpr { StateKey = "items", ItemName = "alias", Body = F("alias") } } };
        Assert.Equal(new[] { "safe" }, SamplingMaskState.Infer(config, measurements).Order());
        Assert.Empty(SamplingMaskState.Infer(config with { Nodes = [new LibraryNode { Id = "unknown", MechanicName = "unknown" }] }, []));
    }

    [Fact]
    public void FoldAndConditionalProofsRespectAliasPrecedenceAndUnreachableBadWrites()
    {
        var config = new GraphConfig
        {
            SchemaVersion = "1.0",
            InitialState = Initial(new() { ["fold"] = 0, ["shadowed"] = 0, ["conditional"] = 0, ["items"] = new object[] { "text" } }),
            Nodes = [Write("fold", "fold"), Write("shadowed", "shadowed"), Write("conditional", "conditional")],
            Expressions = new()
            {
                ["fold"] = new FoldExpr { StateKey = "items", ItemName = "item", AccName = "acc", Init = N("0"), Body = Set(BitSetOp.Union, F("acc"), N("1")) },
                ["shadowed"] = new FoldExpr { StateKey = "items", ItemName = "acc", AccName = "acc", Init = N("0"), Body = F("acc") },
                ["conditional"] = new IfExpr { Condition = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "false" }, ThenExpr = new ConstantExpr { Kind = ConstantKind.Rational, Value = "1/0" }, ElseExpr = N("0") },
            },
        };
        Assert.Equal(new[] { "fold" }, SamplingMaskState.Infer(config, []).Order());
    }

    [Fact]
    public void InlineBoundsRequireEveryInitialValueAndWriterAndRespectTheSignBit()
    {
        var expressions = new Dictionary<string, Expression>
        {
            ["small"] = Set(BitSetOp.Intersect, F("large"), N("255")),
            ["copy"] = F("small"),
            ["up"] = Set(BitSetOp.Shift, F("copy"), N("55")),
            ["sign"] = Set(BitSetOp.Shift, F("copy"), N("56")),
            ["down"] = Set(BitSetOp.Shift, F("large"), N("-1")),
            ["union"] = Set(BitSetOp.Union, F("up"), F("down")),
            ["cycle"] = F("cycle"),
            ["growing"] = Set(BitSetOp.Union, Set(BitSetOp.Shift, F("growing"), N("1")), N("1")),
            ["seed"] = Set(BitSetOp.Intersect, F("large"), N("1073741823")),
            ["frontier"] = Set(BitSetOp.Shift, N("1"), Set(BitSetOp.First, F("seed"))),
            ["highFrontier"] = Set(BitSetOp.Shift, N("1"), Set(BitSetOp.First, F("large"))),
            ["fold"] = new FoldExpr { StateKey = "items", ItemName = "item", AccName = "acc", Init = N("0"), Body = Set(BitSetOp.Union, F("acc"), F("copy")) },
            ["shadow"] = new FoldExpr { StateKey = "items", ItemName = "acc", AccName = "acc", Init = N("0"), Body = Set(BitSetOp.Except, F("acc"), N("0")) },
        };
        var initial = expressions.Keys.ToDictionary(key => key, _ => (object?)0);
        initial["large"] = ulong.MaxValue;
        initial["items"] = new object[] { "text" };
        var config = new GraphConfig { SchemaVersion = "1.0", InitialState = Initial(initial), Expressions = expressions, Nodes = expressions.Keys.Select(key => Write(key, key)).Cast<Node>().ToArray() };
        var masks = SamplingMaskState.Infer(config, []);
        Assert.Equal(new[] { "copy", "cycle", "down", "fold", "frontier", "seed", "small", "union", "up" }, SamplingMaskState.InferInline(config, masks).Order());
        var changed = config with { InitialState = new(config.InitialState!) { ["up"] = JsonSerializer.SerializeToElement(1UL << 63) } };
        Assert.DoesNotContain("up", SamplingMaskState.InferInline(changed, SamplingMaskState.Infer(changed, [])));
        Assert.DoesNotContain("union", SamplingMaskState.InferInline(changed, SamplingMaskState.Infer(changed, [])));
    }

    [Fact]
    public void SharedCodeDistinguishesInlineAndFullWidthStateInvariants()
    {
        var layout = new Dictionary<string, int> { ["value"] = 0 };
        var masks = new HashSet<string> { "value" };
        var shapes = new SamplingShapes();
        var expression = Set(BitSetOp.Count, F("value"));
        foreach (var inline in new[] { true, false, true, false })
        {
            var owner = new SamplingExpressions(key => layout[key], SamplingTier.Compiled, shapes: shapes, maskFields: masks, inlineMaskFields: inline ? masks : SamplingMaskState.None);
            var code = owner.Compile(expression);
            foreach (var number in inline ? new[] { 0UL, 7UL, (ulong)long.MaxValue } : new[] { 0UL, 1UL << 63, ulong.MaxValue })
            {
                var frame = new SamplingFrame(layout, new SamplingCell[1], new() { ["value"] = new BigInteger(number) });
                frame.Reset(default);
                Assert.Equal(BitOperations.PopCount(number), (int)code(frame).AsInteger());
            }
        }
        Assert.Equal(2, shapes.Count);
    }

    [Fact]
    public void IntegerComparisonsKeepFullWidthValuesAndScopedAliasesAcrossSharedCode()
    {
        var layout = new Dictionary<string, int> { ["value"] = 0, ["items"] = 1, ["item"] = 2 };
        var masks = new HashSet<string> { "value" };
        Expression[] expressions = [
            new CompareExpr { Op = CompareOp.Lt, Left = F("value"), Right = N(long.MaxValue.ToString()) },
            new CompareExpr { Op = CompareOp.Gte, Left = Set(BitSetOp.Count, F("value")), Right = Set(BitSetOp.First, F("value")) },
            new MapExpr { StateKey = "items", ItemName = "value", Body = new CompareExpr { Op = CompareOp.Eq, Left = F("value"), Right = N("7") } },
            new MapExpr { StateKey = "items", ItemName = "item", IndexName = "value", Body = new CompareExpr { Op = CompareOp.Gte, Left = F("value"), Right = N("0") } },
        ];
        foreach (var expression in expressions)
        {
            var shapes = new SamplingShapes();
            foreach (var inline in new[] { true, false, true, false })
            {
                var owner = new SamplingExpressions(key => layout[key], SamplingTier.Compiled, shapes: shapes, maskFields: masks, inlineMaskFields: inline ? masks : SamplingMaskState.None);
                var code = owner.Compile(expression);
                foreach (var number in inline ? new[] { 0UL, 7UL, (ulong)long.MaxValue } : new[] { 0UL, 1UL << 63, ulong.MaxValue })
                {
                    var state = new Dict { ["value"] = new BigInteger(number), ["items"] = new object[] { 7, -1, "text" } };
                    var expected = ExactExpressionEvaluator.Evaluate(expression, new() { State = state });
                    var frame = new SamplingFrame(layout, new SamplingCell[layout.Count], state); frame.Reset(default);
                    Assert.Equal(expected, code(frame));
                    Assert.Equal(new BigInteger(number), frame.Export()["value"]);
                }
            }
        }
    }

    private static GraphConfig Graph()
    {
        Dictionary<string, Port> Ports(params string[] names) => names.ToDictionary(name => name, name => new Port { Name = name, Type = PortType.State });
        Edge E(string source, string target) => new() { Id = source + target, SourceNodeId = source, SourcePort = "state", TargetNodeId = target, TargetPort = "state" };
        return new()
        {
            SchemaVersion = "1.0",
            InitialState = Initial(new() { ["a"] = 0, ["b"] = 0, ["win"] = 0, ["unused"] = false }),
            StateSchema = [new() { Name = "a", Type = "number" }, new() { Name = "b", Type = "number" }, new() { Name = "win", Type = "number" }],
            Expressions = new() { ["copy"] = F("a"), ["count"] = Set(BitSetOp.Count, F("b")), ["change"] = Set(BitSetOp.Except, F("b"), N("1")) },
            Nodes = [
                Write("copy", "b") with { Outputs = Ports("state") },
                Write("count", "win") with { Inputs = Ports("state"), Outputs = Ports("state") },
                Write("change", "a") with { Inputs = Ports("state"), Outputs = Ports("state") },
                new MetricsSinkNode { Id = "sink", WinStateKey = "win", WinCap = 64, Inputs = Ports("state") },
            ],
            Edges = [E("copy", "count"), E("count", "change"), E("change", "sink")],
        };
    }

    [Fact]
    public void ProvenStateReadsAndOverridesPreserveFullValuesAndLocatedErrorsAcrossSharedCode()
    {
        var original = Graph();
        foreach (var config in new[] { original, original with { Expressions = new(original.Expressions!) { ["change"] = Set(BitSetOp.Intersect, F("b"), N("255")) } } })
            foreach (var initial in new[] { "0", "7", "9223372036854775808", "18446744073709551615" })
            {
                var graph = config with { InitialState = new(config.InitialState!) { ["a"] = JsonSerializer.Deserialize<JsonElement>(initial) } };
                var compiled = new GraphCompiler().Compile(graph);
                Assert.True(compiled.IsValid, string.Join("; ", compiled.Errors.Select(e => e.Message)));
                Assert.Equal("compiled-state-plan-v1", compiled.SamplingEngine);
                foreach (var input in new[] { new Dict(), new Dict { ["a"] = new BigInteger(ulong.MaxValue) }, new Dict { ["b"] = -1 }, new Dict { ["unused"] = true } })
                    for (var round = 0; round <= SamplingOptions.HotThreshold; round++)
                    {
                        var expected = SampledInterpreter.RunSingle(compiled.ReferenceProgram!, input, 42);
                        var actual = SampledInterpreter.RunSingle(compiled.Program!, input, 42);
                        Assert.Equal(expected.Value, actual.Value);
                        Assert.Equal(StateHasher.CanonicalHash(expected.State), StateHasher.CanonicalHash(actual.State));
                    }
                foreach (var invalid in new object?[] { -1, "7", null, ExprValue.Rational(1, 2), BigInteger.One << 64 })
                {
                    var input = new Dict { ["a"] = invalid };
                    var expected = Assert.Throws<ExpressionEvaluationException>(() => SampledInterpreter.RunSingle(compiled.ReferenceProgram!, input, 42));
                    var actual = Assert.Throws<ExpressionEvaluationException>(() => SampledInterpreter.RunSingle(compiled.Program!, input, 42));
                    Assert.Equal(expected.Code, actual.Code); Assert.Equal(expected.Location, actual.Location);
                }
            }
    }
}
