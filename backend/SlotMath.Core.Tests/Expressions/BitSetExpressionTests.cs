using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Expressions;

public class BitSetExpressionTests
{
    private static readonly Dictionary<string, int> Layout = new() { ["a"] = 0, ["b"] = 1, ["missing"] = 2, ["items"] = 3, ["item"] = 4, ["acc"] = 5 };
    private static FieldAccessExpr Field(string name) => new() { Target = "state", Path = [name] };
    private static ConstantExpr Number(string value) => new() { Kind = ConstantKind.Integer, Value = value };
    private static BitSetExpr Set(BitSetOp op, params Expression[] args) => new() { Op = op, Args = args };

    [Fact]
    public void FoldMaskRangeProofRespectsShadowedAccumulatorAndItemAliases()
    {
        var expressions = new Expression[]
        {
            new FoldExpr { StateKey = "items", ItemName = "a", AccName = "a", Init = Number("0"), Body = Field("a") },
            new FoldExpr { StateKey = "items", ItemName = "item", AccName = "a", IndexName = "a", Init = Number("0"), Body = Field("a") },
            new FoldExpr { StateKey = "items", ItemName = "item", AccName = "acc", Init = Number("0"), Body = Set(BitSetOp.Union, Field("acc"),
                new FoldExpr { StateKey = "items", ItemName = "acc", AccName = "a", Init = Number("0"), Body = Field("acc") }) },
        };
        string Result(Func<ExprValue> evaluate)
        {
            try { return evaluate().ToString(); }
            catch (ExpressionEvaluationException error) { return $"{error.Code}|{error.Location}|{error.Message}"; }
        }
        foreach (var expression in expressions)
            foreach (var items in new object?[][] { [], [1, 8], [-1], ["text"], [null] })
            {
                var state = new Dictionary<string, object?> { ["items"] = items, ["a"] = "outer-a", ["item"] = "outer-item", ["acc"] = "outer-acc" };
                var expected = Result(() => ExactExpressionEvaluator.Evaluate(expression, new() { State = state }));
                foreach (var tier in Enum.GetValues<SamplingTier>())
                    foreach (var immutable in new[] { false, true })
                    {
                        var code = new SamplingExpressions(name => Layout[name], tier,
                            immutable ? new Dictionary<string, ExprValue> { ["items"] = StateValues.Convert(items) } : null).Compile(expression);
                        var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], state); frame.Reset(default);
                        for (var call = 0; call <= SamplingOptions.HotThreshold; call++)
                        {
                            Assert.Equal(expected, Result(() => code(frame)));
                            Assert.Equal("outer-a", frame.Read(Layout["a"], "a").StringValue);
                            Assert.Equal("outer-item", frame.Read(Layout["item"], "item").StringValue);
                            Assert.Equal("outer-acc", frame.Read(Layout["acc"], "acc").StringValue);
                        }
                    }
            }
    }

    [Fact]
    public void ImmutableMaskTablesPreserveIndexErrorsEagerArgumentsAndSharedShapes()
    {
        object?[][] tables = [[], [0, 8, new BigInteger(ulong.MaxValue)], [1, 3], [-1, 2], ["7", 1], [null, 3], [ExprValue.Rational(3, 2), 8]];
        object?[] positions = [-1, 0, 1, 2, 3, new BigInteger(ulong.MaxValue), "0", true, null, ExprValue.Rational(1, 2)];
        var lookup = new CallExpr { Function = "index", Args = [Field("items"), Field("a")] };
        Expression[] expressions = [Set(BitSetOp.Count, lookup), Set(BitSetOp.Union, lookup, Field("missing")), Set(BitSetOp.Shift, lookup, Field("b"))];
        var shapes = new SamplingShapes();
        string Result(Func<ExprValue> evaluate)
        {
            try { return evaluate().ToString(); }
            catch (ExpressionEvaluationException error) { return $"{error.Code}|{error.Location}|{error.Message}"; }
        }
        foreach (var table in tables)
            foreach (var expression in expressions)
            {
                var immutable = StateValues.Convert(table);
                foreach (var tier in Enum.GetValues<SamplingTier>())
                {
                    var code = new SamplingExpressions(name => Layout[name], tier, new Dictionary<string, ExprValue> { ["items"] = immutable }, shapes).Compile(expression);
                    foreach (var position in positions)
                    {
                        var state = new Dictionary<string, object?> { ["items"] = table, ["a"] = position, ["b"] = -64 };
                        var expected = Result(() => ExactExpressionEvaluator.Evaluate(expression, new() { State = state }));
                        var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], state); frame.Reset(default);
                        for (var call = 0; call <= SamplingOptions.HotThreshold; call++) Assert.Equal(expected, Result(() => code(frame)));
                    }
                }
            }
    }

    [Fact]
    public void FoldIndexRangeProofKeepsTheSixtyFourBoundaryAndRestoresAliases()
    {
        var expression = new FoldExpr
        {
            StateKey = "items",
            ItemName = "item",
            IndexName = "a",
            AccName = "acc",
            Init = Number("0"),
            Body = Set(BitSetOp.Union, Field("acc"), Set(BitSetOp.Shift, Number("1"), Field("a"))),
        };
        var shapes = new SamplingShapes();
        string Result(Func<ExprValue> evaluate)
        {
            try { return evaluate().ToString(); }
            catch (ExpressionEvaluationException error) { return $"{error.Code}|{error.Location}|{error.Message}"; }
        }
        foreach (var length in new[] { 0, 1, 32, 63, 64, 65 })
        {
            var items = Enumerable.Range(0, length).Select(i => (object?)i).ToArray();
            var state = new Dictionary<string, object?> { ["items"] = items, ["a"] = "outer-a", ["item"] = "outer-item", ["acc"] = "outer-acc" };
            var expected = Result(() => ExactExpressionEvaluator.Evaluate(expression, new() { State = state }));
            foreach (var tier in Enum.GetValues<SamplingTier>())
            {
                var code = new SamplingExpressions(name => Layout[name], tier, new Dictionary<string, ExprValue> { ["items"] = StateValues.Convert(items) }, shapes).Compile(expression);
                var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], state); frame.Reset(default);
                for (var call = 0; call <= SamplingOptions.HotThreshold; call++)
                {
                    Assert.Equal(expected, Result(() => code(frame)));
                    Assert.Equal("outer-a", frame.Read(Layout["a"], "a").StringValue);
                    Assert.Equal("outer-item", frame.Read(Layout["item"], "item").StringValue);
                    Assert.Equal("outer-acc", frame.Read(Layout["acc"], "acc").StringValue);
                }
            }
        }
    }

    // This oracle uses sets of positions, independently of the production bit operations.
    private static BigInteger Oracle(BitSetOp op, BigInteger a, BigInteger b)
    {
        HashSet<int> Positions(BigInteger value) => Enumerable.Range(0, 64).Where(i => value / BigInteger.Pow(2, i) % 2 == 1).ToHashSet();
        var left = Positions(a);
        if (op == BitSetOp.Count) return left.Count;
        if (op == BitSetOp.First) return left.Count == 0 ? -1 : left.Min();
        if (op == BitSetOp.Shift) left = left.Select(i => i + (int)b).Where(i => i is >= 0 and < 64).ToHashSet();
        else
        {
            var right = Positions(b);
            if (op == BitSetOp.Union) left.UnionWith(right);
            else if (op == BitSetOp.Intersect) left.IntersectWith(right);
            else left.ExceptWith(right);
        }
        return left.Aggregate(BigInteger.Zero, (sum, i) => sum + BigInteger.Pow(2, i));
    }

    [Fact]
    public void StraightRunMaskProofsRespectOverwritesNullsAndScopedBindings()
    {
        Expression[][] cases =
        [
            [Set(BitSetOp.Union, Number("18446744073709551615"), Number("0")), Set(BitSetOp.Intersect, Field("a"), Number("2")), Set(BitSetOp.First, Number("0")), Set(BitSetOp.Union, Field("a"), Number("0"))],
            [Number("3"), Set(BitSetOp.Count, Field("a")), new ConstantExpr { Kind = ConstantKind.String, Value = "7" }, Set(BitSetOp.Count, Field("a"))],
            [Number("3"), Set(BitSetOp.Count, Field("a")), Field("missing"), Set(BitSetOp.Count, Field("a"))],
            [Number("3"), new MapExpr { StateKey = "items", ItemName = "a", Body = Set(BitSetOp.Union, Field("a"), Number("8")) }, Set(BitSetOp.Count, Field("a"))],
            [Number("3"), new FieldAccessExpr { Target = "state", Path = [""] }],
        ];
        var shapes = new SamplingShapes();
        foreach (var expressions in cases)
        {
            var nodes = expressions.Select((_, index) => (Node)new ModifyStateNode
            { Id = "n" + index, ExpressionId = "e" + index, OutputKey = index % 2 == 0 ? "a" : "b" }).ToArray();
            var ast = expressions.Select((expression, index) => (Key: "e" + index, Expression: expression)).ToDictionary(p => p.Key, p => p.Expression);
            var binder = new SamplingExpressions(name => Layout[name], SamplingTier.Interpreted);
            var steps = expressions.Select(binder.Compile).ToArray();
            object? Interpret(SamplingFrame frame, SlotMath.Core.Random.SeededRandom _, object? input)
            {
                for (var i = 0; i < steps.Length; i++) frame.Cells[Layout[i % 2 == 0 ? "a" : "b"]].SetTyped(steps[i](frame));
                return input;
            }
            string Run(SamplingRunDelegate code)
            {
                var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], new() { ["items"] = new object[] { 1, 4 }, ["missing"] = null });
                frame.Reset(default);
                var result = "ok";
                try { code.Invoke(frame, new(4), null); }
                catch (ExpressionEvaluationException error) { result = $"{error.Code}|{error.Location}|{error.Message}"; }
                return result + " " + SlotMath.Core.Math.StateHasher.CanonicalHash(frame.Export());
            }
            var expected = Run(new(Interpret, () => throw new InvalidOperationException(), SamplingTier.Interpreted));
            foreach (var tier in new[] { SamplingTier.Compiled, SamplingTier.Tiered })
            {
                var owner = new SamplingExpressions(name => Layout[name], tier, shapes: shapes);
                var native = owner.BindRun(nodes, ast, BigInteger.One, Interpret);
                for (var i = 0; i < SamplingOptions.HotThreshold + 2; i++) Assert.Equal(expected, Run(native));
            }
        }
    }

    [Fact]
    public void StateMaskReads_PreserveEagerErrorsAndImmutableBindingsAfterValueChanges()
    {
        var expression = Set(BitSetOp.Union, Field("a"), Field("missing"));
        var compiled = new SamplingExpressions(name => Layout[name], SamplingTier.Compiled).Compile(expression);
        var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], new());
        frame.Reset(default);
        foreach (var value in new object?[] { 7, -1, "7", ExprValue.Rational(1, 2), ulong.MaxValue, null })
        {
            var state = new Dictionary<string, object?> { ["a"] = value };
            frame.Cells[Layout["a"]] = SamplingCell.FromRaw(value);
            var expected = Assert.Throws<ExpressionEvaluationException>(() => ExactExpressionEvaluator.Evaluate(expression, new() { State = state }));
            var actual = Assert.Throws<ExpressionEvaluationException>(() => compiled(frame));
            Assert.Equal((expected.Code, expected.Location, expected.Message), (actual.Code, actual.Location, actual.Message));
        }
        frame.Cells[Layout["a"]] = SamplingCell.FromRaw(7);
        var binder = new SamplingExpressions(name => Layout[name], SamplingTier.Compiled,
            new Dictionary<string, ExprValue> { ["a"] = ExprValue.Number(-1) });
        var failure = Assert.Throws<ExpressionEvaluationException>(() => binder.Compile(Set(BitSetOp.Count, Field("a")))(frame));
        Assert.Equal("EVAL_BITSET_RANGE", failure.Code);
        Assert.Equal("bitset.args[0]", failure.Location);
    }

    [Fact]
    public void AllBitsAndRandomSetsMatchIndependentOracleAcrossTiers()
    {
        var random = new System.Random(671923);
        var masks = new List<BigInteger> { 0, ulong.MaxValue, long.MaxValue };
        masks.AddRange(Enumerable.Range(0, 64).Select(i => BigInteger.Pow(2, i)));
        for (var i = 0; i < 192; i++) masks.Add(new BigInteger(random.GetItems<byte>(Enumerable.Range(0, 256).Select(v => (byte)v).ToArray(), 8), true));
        var shapes = new SamplingShapes();
        foreach (var op in Enum.GetValues<BitSetOp>())
        {
            var expr = Set(op, BitSetOperations.Arity(op) == 1 ? [Field("a")] : [Field("a"), Field("b")]);
            var serialized = JsonSerializer.Serialize<Expression>(expr, JsonOptions.Default);
            Assert.IsType<BitSetExpr>(JsonSerializer.Deserialize<Expression>(serialized, JsonOptions.Default));
            var evaluators = Enum.GetValues<SamplingTier>().Select(tier => new SamplingExpressions(name => Layout[name], tier, shapes: shapes).Compile(expr)).ToArray();
            foreach (var a in masks)
            {
                var values = op == BitSetOp.Shift ? new BigInteger[] { -63, -32, -1, 0, 1, 32, 63 } : [masks[random.Next(masks.Count)]];
                foreach (var b in values)
                {
                    var state = new Dictionary<string, object?> { ["a"] = a, ["b"] = b };
                    var expected = Oracle(op, a, b);
                    Assert.Equal(expected, ExactExpressionEvaluator.Evaluate(expr, new() { State = state }).AsInteger());
                    foreach (var evaluate in evaluators)
                    {
                        var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], state);
                        frame.Reset(default);
                        Assert.Equal(expected, evaluate(frame).AsInteger());
                    }
                }
            }
        }
    }

    [Fact]
    public void InvalidOperandsArityAndEagerErrorOrderAreLocatedAndIdentical()
    {
        var invalid = new[] { Number("-1"), Number("18446744073709551616"), new ConstantExpr { Kind = ConstantKind.Rational, Value = "1/2" }, new ConstantExpr { Kind = ConstantKind.String, Value = "3" } };
        var expressions = invalid.SelectMany(value => new Expression[] { Set(BitSetOp.Count, value), Set(BitSetOp.Union, Number("0"), value) }).ToList();
        expressions.AddRange(new Expression[]
        {
            Set(BitSetOp.Shift, Number("1"), Number("64")), Set(BitSetOp.Shift, Number("1"), Number("-64")),
            Set(BitSetOp.Shift, Number("1"), new ConstantExpr { Kind = ConstantKind.Rational, Value = "3/2" }),
            Set(BitSetOp.Count), Set(BitSetOp.Union, Number("1")), Set((BitSetOp)123, Number("1")),
            Set(BitSetOp.Union, Number("-1"), Field("missing")),
            Set(BitSetOp.Count, Number("1"), Number("2"), Field("missing")),
            Set(BitSetOp.Union, Field("missing"), Number("-1")),
        });
        foreach (var expr in expressions)
        {
            var expected = Assert.Throws<ExpressionEvaluationException>(() => ExactExpressionEvaluator.Evaluate(expr, new() { State = new Dictionary<string, object?>() }));
            Assert.NotNull(expected.Location);
            foreach (var tier in Enum.GetValues<SamplingTier>())
            {
                var evaluator = new SamplingExpressions(name => Layout[name], tier).Compile(expr);
                for (var i = 0; i <= SamplingOptions.HotThreshold; i++)
                {
                    var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], new()); frame.Reset(default);
                    var actual = Assert.Throws<ExpressionEvaluationException>(() => evaluator(frame));
                    Assert.Equal((expected.Code, expected.Location, expected.Message), (actual.Code, actual.Location, actual.Message));
                }
            }
        }
    }

    [Fact]
    public void LazyBranchesAndScopedFoldBindingsKeepTheirExistingContract()
    {
        var skip = new IfExpr { Condition = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "false" }, ThenExpr = Set(BitSetOp.Count, Field("missing")), ElseExpr = Number("7") };
        var fold = new FoldExpr { StateKey = "items", ItemName = "item", AccName = "acc", ItemType = ExprType.Number, Init = Number("0"), Body = Set(BitSetOp.Union, Field("acc"), Field("item")) };
        foreach (var tier in Enum.GetValues<SamplingTier>())
        {
            var binder = new SamplingExpressions(name => Layout[name], tier);
            var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], new() { ["items"] = new object?[] { 1, 8, 8, 32 }, ["item"] = 123, ["acc"] = 456 });
            frame.Reset(default);
            var evaluate = binder.Compile(fold);
            for (var i = 0; i <= SamplingOptions.HotThreshold; i++) Assert.Equal(41, evaluate(frame).AsInteger());
            Assert.Equal(123, frame.Read(Layout["item"], "item").AsInteger());
            Assert.Equal(456, frame.Read(Layout["acc"], "acc").AsInteger());
            Assert.Equal(7, binder.Compile(skip)(frame).AsInteger());
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            frame.CancellationToken = cancellation.Token;
            Assert.Throws<OperationCanceledException>(() => { for (var i = 0; i < 256; i++) evaluate(frame); });
            Assert.Equal(123, frame.Read(Layout["item"], "item").AsInteger());
            Assert.Equal(456, frame.Read(Layout["acc"], "acc").AsInteger());
        }
    }

    [Fact]
    public void NestedWordLoweringPreservesValuesErrorsAndShapeSharing()
    {
        var random = new System.Random(53489);
        Expression Tree(int depth)
        {
            if (depth == 0) return random.Next(2) == 0 ? Field("a") : Field("b");
            var op = (BitSetOp)random.Next(6);
            return Set(op, Enumerable.Range(0, BitSetOperations.Arity(op)).Select(_ => Tree(depth - 1)).ToArray());
        }
        var shapes = new SamplingShapes();
        string Result(Func<ExprValue> evaluate)
        {
            try { return evaluate().ToString(); }
            catch (ExpressionEvaluationException error) { return $"{error.Code}|{error.Location}|{error.Message}"; }
        }
        for (var i = 0; i < 192; i++)
        {
            var expression = Tree(3);
            foreach (var a in new BigInteger[] { -1, 0, 1, 63, 64, ulong.MaxValue })
            {
                var state = new Dictionary<string, object?> { ["a"] = a, ["b"] = i % 3 == 0 ? "invalid" : new BigInteger(i % 65) };
                var expected = Result(() => ExactExpressionEvaluator.Evaluate(expression, new() { State = state }));
                foreach (var tier in new[] { SamplingTier.Interpreted, SamplingTier.Compiled })
                {
                    var code = new SamplingExpressions(name => Layout[name], tier, shapes: shapes).Compile(expression);
                    var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], state); frame.Reset(default);
                    Assert.Equal(expected, Result(() => code(frame)));
                }
            }
        }
        var eager = Set(BitSetOp.Union, Number("-1"), Set(BitSetOp.Intersect, Field("missing"), Number("0")));
        var compiled = new SamplingExpressions(name => Layout[name], SamplingTier.Compiled).Compile(eager);
        var missing = new SamplingFrame(Layout, new SamplingCell[Layout.Count], new()); missing.Reset(default);
        Assert.Equal(Result(() => ExactExpressionEvaluator.Evaluate(eager, new() { State = new Dictionary<string, object?>() })), Result(() => compiled(missing)));
    }

    [Fact]
    public void MaskFoldRangeProofPreservesEmptyLargeNestedAndInvalidInputs()
    {
        var expressions = new Expression[]
        {
            new FoldExpr { StateKey = "items", ItemName = "item", AccName = "acc", Init = Number("18446744073709551615"), Body = new IfExpr
            {
                Condition = new CompareExpr { Op = CompareOp.Gt, Left = Set(BitSetOp.Count, Field("acc")), Right = Number("0") },
                ThenExpr = Set(BitSetOp.Except, Field("acc"), Field("item")), ElseExpr = Field("acc"),
            } },
            new FoldExpr { StateKey = "items", ItemName = "item", AccName = "acc", Init = Number("0"), Body = Set(BitSetOp.Union, Field("acc"),
                new AggregateExpr { Func = AggregateFunc.Sum, StateKey = "items", ItemName = "a", ValueExpr = Field("acc") }) },
            new FoldExpr { StateKey = "items", ItemName = "item", AccName = "acc", Init = Number("0"), Body = Set(BitSetOp.Union, Field("acc"),
                new FoldExpr { StateKey = "items", ItemName = "a", AccName = "acc", Init = Number("0"), Body = Set(BitSetOp.Union, Field("acc"), Field("a")) }) },
            new FoldExpr { StateKey = "items", ItemName = "item", AccName = "acc", Init = Number("-1"), Body = Set(BitSetOp.Union, Field("acc"), Field("item")) },
        };
        object?[][] inputs = [[], [0], [1, 8, 32], [new BigInteger(ulong.MaxValue), 1], [-1], ["bad"], [null]];
        var shapes = new SamplingShapes();
        string Result(Func<ExprValue> evaluate)
        {
            try { return evaluate().ToString(); }
            catch (ExpressionEvaluationException error) { return $"{error.Code}|{error.Location}|{error.Message}"; }
        }
        foreach (var expr in expressions)
            foreach (var input in inputs)
            {
                var state = new Dictionary<string, object?> { ["items"] = input, ["item"] = "outer-item", ["a"] = "outer-a", ["acc"] = "outer-acc" };
                var expected = Result(() => ExactExpressionEvaluator.Evaluate(expr, new() { State = state }));
                foreach (var tier in Enum.GetValues<SamplingTier>())
                    foreach (var immutable in new[] { false, true })
                    {
                        var code = new SamplingExpressions(name => Layout[name], tier,
                            immutable ? new Dictionary<string, ExprValue> { ["items"] = StateValues.Convert(input) } : null, shapes).Compile(expr);
                        var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], state); frame.Reset(default);
                        for (var iteration = 0; iteration <= SamplingOptions.HotThreshold; iteration++)
                        {
                            Assert.Equal(expected, Result(() => code(frame)));
                            Assert.Equal("outer-item", frame.Read(Layout["item"], "item").StringValue);
                            Assert.Equal("outer-a", frame.Read(Layout["a"], "a").StringValue);
                            Assert.Equal("outer-acc", frame.Read(Layout["acc"], "acc").StringValue);
                        }
                    }
            }
    }

    [Fact]
    public void ExportedSchemaIncludesRecursiveFiniteSetExpressions()
    {
        var schema = Json.Schema.JsonSchema.FromText(SchemaEmitter.GetSchemaJson());
        var config = System.Text.Json.Nodes.JsonNode.Parse("""{"schemaVersion":"1.0.0","expressions":{"set":{"exprType":"bitset","op":"Union","args":[{"exprType":"constant","kind":"Integer","value":"1"},{"exprType":"bitset","op":"Count","args":[{"exprType":"constant","kind":"Integer","value":"3"}]}]}}}""")!;
        Assert.True(schema.Evaluate(JsonSerializer.SerializeToElement(config)).IsValid);
        config["expressions"]!["set"]!["op"] = "Unknown";
        Assert.False(schema.Evaluate(JsonSerializer.SerializeToElement(config)).IsValid);
    }

    [Fact]
    public void TypeCheckingAndCostTraverseEveryOperand()
    {
        var valid = Set(BitSetOp.Count, Set(BitSetOp.Union, Number("3"), Number("8")));
        Assert.Empty(ExpressionTypeChecker.Check(valid, new TypeCheckContext(), ExprType.Number));
        Assert.Equal(4, ExpressionCost.Compute(valid));
        Assert.NotEmpty(ExpressionTypeChecker.Check(Set(BitSetOp.First, new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" }), new TypeCheckContext(), ExprType.Number));
        Assert.NotEmpty(ExpressionTypeChecker.Check(Set(BitSetOp.Union, Number("1")), new TypeCheckContext(), ExprType.Number));
        Assert.NotEmpty(ExpressionTypeChecker.Check(Set((BitSetOp)123, Number("1")), new TypeCheckContext(), ExprType.Number));
    }
}
