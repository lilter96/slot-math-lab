using System.Numerics;
using System.Text.Json;
using CsCheck;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Random;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Benchmarks;

// SamplingOptions.Tier is process-wide; tests that select a tier must not overlap.
[CollectionDefinition("SamplingOptions", DisableParallelization = true)]
public sealed class SamplingOptionsCollection;

/// <summary>
///     D25 / invariant 11 for the sampling fast paths: generated code,
///     code shared between expressions of one shape, and constant state all return
///     the value, the final state and the located error of the interpreted closures.
/// </summary>
[Collection("SamplingOptions")]
public class SamplingTierEquivalenceTests
{
    private static readonly string[] Names = ["a", "b", "c", "d", "arr", "missing", "item", "acc", "position"];

    private static readonly Dictionary<string, int> Layout =
        Names.Select((name, index) => (name, index)).ToDictionary(p => p.name, p => p.index);

    // One binder per tier for the whole run, so thousands of random expressions
    // meet in the same shape cache with different constants and fields.
    private static readonly SamplingExpressions SharedCompiled = new(Slot, SamplingTier.Compiled);
    private static readonly SamplingExpressions SharedTiered = new(Slot, SamplingTier.Tiered);

    // Edge cases first: 64-bit boundaries, integers beyond them, a zero
    // denominator, numeric text with and without canonical form.
    private static readonly Gen<Expression> Leaf = Gen.OneOf(
        Gen.OneOfConst("0", "1", "-1", "2", "3", "7", "9223372036854775807", "-9223372036854775808",
            "123456789012345678901234567890").Select(v => (Expression)Constant(ConstantKind.Integer, v)),
        Gen.OneOfConst("1/3", "-7/2", "4/2", "1/0").Select(v => (Expression)Constant(ConstantKind.Rational, v)),
        Gen.OneOfConst("true", "false").Select(v => (Expression)Constant(ConstantKind.Boolean, v)),
        Gen.OneOfConst("2", "abc", "", "007", "-5").Select(v => (Expression)Constant(ConstantKind.String, v)),
        Gen.OneOfConst(Names).Select(name => (Expression)F(name)),
        Gen.OneOfConst(Names).Select(name => (Expression)F(name)));

    // Small trees collide in the shape cache all the time; deep ones reach the
    // nested and interpreted forms.
    private static readonly Gen<Expression> Expressions = Gen.OneOf(Tree(1), Tree(1), Tree(2), Tree(3));

    private static readonly Gen<object?> Scalar = Gen.OneOf(
        Gen.Int[-3, 20].Select(n => (object?)new BigInteger(n)),
        Gen.OneOfConst<object?>(0, 1L, new BigInteger(long.MaxValue), new BigInteger(long.MinValue),
            BigInteger.Parse("123456789012345678901234567890"),
            ExprValue.Rational(1, 3), true, false, "2", "007", "abc", "", "-4", null));

    private static readonly Gen<object?> Value = Gen.OneOf(Scalar, Scalar,
        Scalar.Array[0, 5].Select(items => (object?)items));

    // A mask leaves fields out, so absent state is exercised as well as null.
    private static readonly Gen<Dict> State = Value.Array[4].Select(Scalar.Array[0, 6], Gen.Int[0, 31],
        (values, array, present) =>
        {
            var state = new Dict();
            for (var i = 0; i < 4; i++)
            {
                if ((present & (1 << i)) != 0)
                {
                    state[Names[i]] = values[i];
                }
            }

            if ((present & 16) != 0)
            {
                state["arr"] = array;
            }

            return state;
        });

    private static int Slot(string name) => Layout[name];

    private static ConstantExpr Constant(ConstantKind kind, string value) => new() { Kind = kind, Value = value };
    private static FieldAccessExpr F(string name) => new() { Path = [name], Target = "state" };

    private static GraphConfig Fixture(string file) => JsonSerializer.Deserialize<GraphConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "LabDemo", file)), JsonOptions.Default)!;

    private static Gen<Expression> Tree(int depth)
    {
        if (depth == 0)
        {
            return Leaf;
        }

        Gen<Expression> sub = Tree(depth - 1);
        return Gen.OneOf(
            Leaf,
            Gen.OneOfConst(Enum.GetValues<BinaryOp>()).Select(sub, sub,
                (op, left, right) => (Expression)new BinaryExpr { Op = op, Left = left, Right = right }),
            Gen.OneOfConst(Enum.GetValues<CompareOp>()).Select(sub, sub,
                (op, left, right) => (Expression)new CompareExpr { Op = op, Left = left, Right = right }),
            sub.Select(operand => (Expression)new NotExpr { Expr = operand }),
            sub.Select(sub, sub,
                (test, yes, no) => (Expression)new IfExpr { Condition = test, ThenExpr = yes, ElseExpr = no }),
            // Known functions at right and wrong arity, and an unknown one.
            Gen.OneOfConst("tonumber", "toString", "length", "contains", "index", "max", "min", "append", "abs", "nope")
                .Select(sub.Array[0, 3],
                    (function, args) => (Expression)new CallExpr { Function = function, Args = args }),
            sub.Select(sub,
                (first, second) => (Expression)new CallExpr
                {
                    Function = "append",
                    Args = [new CallExpr { Function = "append", Args = [F("arr"), first] }, second]
                }),
            Gen.Int[0, 4].Select(sub, (form, body) => form switch
            {
                0 => (Expression)new MapExpr
                {
                    StateKey = "arr", ItemName = "item", IndexName = "position", Body = body
                },
                1 => new FilterExpr { StateKey = "arr", ItemName = "item", Predicate = body },
                2 => new FoldExpr
                {
                    StateKey = "arr",
                    ItemName = "item",
                    AccName = "acc",
                    Init = Constant(ConstantKind.Integer, "0"),
                    Body = body
                },
                3 => new AggregateExpr
                {
                    StateKey = "arr", ItemName = "item", Func = AggregateFunc.Sum, ValueExpr = body
                },
                _ => new AggregateExpr
                {
                    StateKey = "arr", ItemName = "item", Func = AggregateFunc.Count, Predicate = body
                }
            }));
    }

    private static string Run(Func<SamplingFrame, ExprValue> code, Dict state)
    {
        var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], state);
        frame.Reset(default);
        string outcome;
        try
        {
            ExprValue value = code(frame);
            outcome = $"{value.Kind}:{StateHasher.CanonicalHash(new Dict { ["value"] = value.ToStateObject() })}";
        }
        catch (ExpressionEvaluationException error)
        {
            outcome = $"{error.Code}|{error.Message}|{error.Location}";
        }
        catch (Exception error) when (error is DivideByZeroException or FormatException or OverflowException
                                          or InvalidCastException)
        {
            outcome = $"{error.GetType().Name}|{error.Message}";
        }

        return outcome + " state " + StateHasher.CanonicalHash(frame.Export());
    }

    private static string Print(Expression expression) => JsonSerializer.Serialize(expression, JsonOptions.Default);

    [Fact]
    public void StraightRuns_Pbt_1000_SharedCodePreservesSequentialStateAndLocatedErrors()
    {
        var compiled = new SamplingExpressions(Slot, SamplingTier.Compiled);
        var tiered = new SamplingExpressions(Slot, SamplingTier.Tiered);

        string Execute(SamplingRunDelegate code, Dict state)
        {
            var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], state);
            frame.Reset(default);
            var rng = new SeededRandom(1421);
            string outcome;
            try
            {
                outcome = code.Invoke(frame, rng, "input")?.ToString() ?? "null";
            }
            catch (ExpressionEvaluationException error)
            {
                outcome = $"{error.Code}|{error.Message}|{error.Location}";
            }
            catch (Exception error) when (error is DivideByZeroException or FormatException or OverflowException
                                              or InvalidCastException)
            {
                outcome = $"{error.GetType().Name}|{error.Message}";
            }

            return outcome + " state " + StateHasher.CanonicalHash(frame.Export()) + " rng " + rng.NextUInt64();
        }

        Expressions.Array[1, 5].Select(State, Gen.Int[0, 63]).Sample((expressions, state, mask) =>
        {
            var discardOutput = (mask & 32) != 0;
            Node[] nodes = expressions.Select((_, i) => (Node)new ModifyStateNode
            {
                Id = "m" + i, ExpressionId = "e" + i, OutputKey = (mask & (1 << i)) == 0 ? Names[i % 4] : null
            }).ToArray();
            Dictionary<string, Expression> ast = expressions
                .Select((expression, i) => (Key: "e" + i, Expression: expression))
                .ToDictionary(p => p.Key, p => p.Expression);
            var binder = new SamplingExpressions(Slot, SamplingTier.Interpreted);
            Func<SamplingFrame, ExprValue>[] steps = expressions.Select(binder.Compile).ToArray();

            object? Interpret(SamplingFrame frame, SeededRandom _, object? input)
            {
                for (var i = 0; i < steps.Length; i++)
                {
                    frame.CheckCancellation();
                    ExprValue value = steps[i](frame);
                    var key = ((ModifyStateNode)nodes[i]).OutputKey;
                    // Unit frames use the existing 'missing' slot for the
                    // unnamed write's runtime key; no graph validation here.
                    frame.Cells[Slot(key ?? "missing")]
                        .SetTyped(key is null ? ExprValue.Number(value.AsInteger()) : value);
                }

                return discardOutput ? null : input;
            }

            int RunSlot(string key)
            {
                return Slot(key == "__modified__" ? "missing" : key);
            }

            var expected =
                Execute(
                    new SamplingRunDelegate(Interpret, () => throw new InvalidOperationException(),
                        SamplingTier.Interpreted), state);
            var native = new SamplingRunDelegate(Interpret,
                () => SamplingCodegen.CompileRun(compiled, RunSlot, nodes, ast, BigInteger.One, discardOutput),
                SamplingTier.Compiled);
            Assert.Equal(expected, Execute(native, state));
            var hot = new SamplingRunDelegate(Interpret,
                () => SamplingCodegen.CompileRun(tiered, RunSlot, nodes, ast, BigInteger.One, discardOutput),
                SamplingTier.Tiered);
            for (var call = 0; call < SamplingOptions.HotThreshold + 2; call++)
            {
                Assert.Equal(expected, Execute(hot, state));
            }
        }, seed: "sampling-straight-runs-v1", iter: 1000, print: p => string.Join("; ", p.Item1.Select(Print)));
    }

    [Fact]
    public void Tiers_Pbt_10000_GeneratedAndSharedCodeMatchInterpretedValuesStateAndErrors()
    {
        Expressions.Select(State).Sample((expression, state) =>
            {
                var expected = Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(expression), state);
                Assert.Equal(expected,
                    Run(new SamplingExpressions(Slot, SamplingTier.Compiled).Compile(expression), state));
                Assert.Equal(expected, Run(SharedCompiled.Compile(expression), state));

                // Below the threshold the closures run; the last calls run generated code.
                Func<SamplingFrame, ExprValue> tiered = SharedTiered.Compile(expression);
                for (var call = 0; call < SamplingOptions.HotThreshold + 2; call++)
                {
                    if (call < 2 || call >= SamplingOptions.HotThreshold)
                    {
                        Assert.Equal(expected, Run(tiered, state));
                    }
                    else
                    {
                        Run(tiered, state);
                    }
                }
            }, seed: "sampling-tier-equivalence-v1", iter: 10000,
            print: p =>
                $"{Print(p.Item1)} on {JsonSerializer.Serialize(p.Item2.ToDictionary(f => f.Key, f => f.Value?.ToString()))}");
    }

    [Fact]
    public void ConstantState_Pbt_10000_FoldedReadsAndBranchesMatchUnfolded()
    {
        Expressions.Select(State, Gen.Int[0, 31]).Sample((expression, state, constant) =>
        {
            // Any readable field that is not an iteration variable may be constant.
            var constants = new Dictionary<string, ExprValue>();
            for (var i = 0; i < 5; i++)
            {
                if ((constant & (1 << i)) != 0 && state.TryGetValue(Names[i], out var raw) &&
                    SamplingCell.FromRaw(raw) is { Readable: true } cell)
                {
                    constants[Names[i]] = cell.StoredValue;
                }
            }

            var expected = Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(expression), state);
            foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
            {
                Func<SamplingFrame, ExprValue> folded =
                    new SamplingExpressions(Slot, tier, constants).Compile(expression);
                for (var call = 0; call < (tier == SamplingTier.Tiered ? SamplingOptions.HotThreshold + 1 : 1); call++)
                {
                    Assert.Equal(expected, Run(folded, state));
                }
            }
        }, seed: "sampling-constant-state-v1", iter: 10000, print: p => $"{Print(p.Item1)} constants {p.Item3}");
    }

    [Fact]
    public void SameShape_SharesOneMethod_AndKeepsEachExpressionsOperands()
    {
        var binder = new SamplingExpressions(Slot, SamplingTier.Compiled);

        Expression Pick(string field, string position, string add)
        {
            return new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = new CallExpr
                {
                    Function = "tonumber",
                    Args =
                    [
                        new CallExpr { Function = "index", Args = [F(field), Constant(ConstantKind.Integer, position)] }
                    ]
                },
                Right = Constant(ConstantKind.Integer, add)
            };
        }

        Func<SamplingFrame, ExprValue> first = binder.Compile(Pick("arr", "0", "10"));
        Func<SamplingFrame, ExprValue> second = binder.Compile(Pick("a", "2", "-1"));
        Assert.Equal(1, binder.Shapes.Count);
        var state = new Dict { ["arr"] = new object[] { "5", "6", "7" }, ["a"] = new object[] { "1", "2", "3" } };
        Assert.Equal(
            Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(Pick("arr", "0", "10")), state),
            Run(first, state));
        Assert.Equal(Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(Pick("a", "2", "-1")), state),
            Run(second, state));
        Assert.NotEqual(Run(first, state), Run(second, state));

        // A different operator, constant kind or function is a different shape.
        binder.Compile(new BinaryExpr { Op = BinaryOp.Sub, Left = F("a"), Right = F("b") });
        binder.Compile(new BinaryExpr { Op = BinaryOp.Add, Left = F("a"), Right = F("b") });
        binder.Compile(new BinaryExpr
        {
            Op = BinaryOp.Add, Left = F("a"), Right = Constant(ConstantKind.Integer, "1")
        });
        binder.Compile(new BinaryExpr
        {
            Op = BinaryOp.Add, Left = F("a"), Right = Constant(ConstantKind.Rational, "1/3")
        });
        Assert.Equal(5, binder.Shapes.Count);
    }

    [Fact]
    public void ImmutableMaps_PreserveLazyReadsAndAliasesAcrossSharedCodeAndSizeBoundaries()
    {
        var shapes = new SamplingShapes();
        foreach (var length in new[] { 0, 1, 31, 32, 33 })
        foreach (var reversed in new[] { false, true })
        foreach (var sameAlias in new[] { false, true })
        {
            ExprValue[] items = Enumerable.Range(0, length).Select(i => ExprValue.Number(reversed ? length - i : i))
                .ToArray();
            var state = new Dict
            {
                ["arr"] = items.Select(item => item.ToStateObject()).ToArray(),
                ["a"] = 17,
                ["item"] = "outer",
                ["position"] = 91
            };
            var alias = sameAlias ? "item" : "position";
            Expression expression = new MapExpr
            {
                StateKey = "arr",
                ItemName = "item",
                IndexName = alias,
                Body = new IfExpr
                {
                    Condition = new CompareExpr
                    {
                        Op = CompareOp.Lt, Left = F(alias), Right = Constant(ConstantKind.Integer, "19")
                    },
                    ThenExpr = F("a"),
                    ElseExpr = F("missing")
                }
            };
            var constants = new Dictionary<string, ExprValue> { ["arr"] = ExprValue.Array(items) };
            var expected = Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(expression), state);
            Assert.Equal(expected,
                Run(new SamplingExpressions(Slot, SamplingTier.Compiled, constants, shapes).Compile(expression),
                    state));
            // A nested form publishes its scoped aliases to interpreted
            // operators, and restores the original cells after failures.
            var nested = new MapExpr
            {
                StateKey = "arr",
                ItemName = "item",
                IndexName = alias,
                Body = new AggregateExpr
                {
                    Func = AggregateFunc.Sum, StateKey = "arr", ItemName = "b", ValueExpr = F(alias)
                }
            };
            Assert.Equal(Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(nested), state),
                Run(new SamplingExpressions(Slot, SamplingTier.Compiled, constants, shapes).Compile(nested), state));
        }
    }

    [Fact]
    public void ImmutableLookupBranches_PreserveRuntimeReadsAndSharedOperands()
    {
        Expression expression = new MapExpr
        {
            StateKey = "arr",
            ItemName = "item",
            IndexName = "position",
            Body = new IfExpr
            {
                Condition = new CompareExpr
                {
                    Op = CompareOp.Eq,
                    Right = F("item"),
                    Left = new CallExpr
                    {
                        Function = "index",
                        Args =
                        [
                            F("c"),
                            new BinaryExpr
                            {
                                Op = BinaryOp.Add,
                                Left = F("position"),
                                Right = Constant(ConstantKind.Integer, "0")
                            }
                        ]
                    }
                },
                ThenExpr = F("a"),
                ElseExpr = F("b")
            }
        };
        var shapes = new SamplingShapes();
        foreach (var matching in new[] { true, false })
        {
            var state = new Dict
            {
                ["arr"] = new[] { 10, 0, 30 },
                ["c"] = matching ? new[] { 10, 20, 30 } : new[] { 3, 2, 1 },
                ["a"] = 17,
                ["b"] = 23,
                ["item"] = "outer",
                ["position"] = 91
            };
            var constants = new Dictionary<string, ExprValue>
            {
                ["arr"] = SamplingCell.FromRaw(state["arr"]).StoredValue,
                ["c"] = SamplingCell.FromRaw(state["c"]).StoredValue
            };
            var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], state);
            frame.Reset(default);
            Func<SamplingFrame, ExprValue> run =
                new SamplingExpressions(Slot, SamplingTier.Compiled, constants, shapes).Compile(expression);
            Assert.Equal(matching ? new long[] { 17, 23, 17 } : new long[] { 23, 23, 23 },
                run(frame).ArrayValue!.Select(value => value.InlineInteger));
            Assert.Equal(StateHasher.CanonicalHash(state), StateHasher.CanonicalHash(frame.Export()));
            Assert.Equal(Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(expression), state),
                Run(run, state));
            state.Remove("b");
            Assert.Equal(Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(expression), state),
                Run(run, state));
        }
    }

    [Fact]
    public void ImmutableMapShapes_PreserveSymbolFlagsBeforeNormalizingStoredArrays()
    {
        var shapes = new SamplingShapes();
        foreach (var symbol in new[] { false, true })
        {
            var constants = new Dictionary<string, ExprValue>
            {
                ["arr"] = ExprValue.Array(new[] { ExprValue.Number(0) }),
                ["a"] = symbol ? ExprValue.Symbol("value") : ExprValue.String("value")
            };
            var binder = new SamplingExpressions(Slot, SamplingTier.Compiled, constants, shapes);
            var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], new Dict());
            frame.Reset(default);
            ExprValue value = binder.Compile(new MapExpr { StateKey = "arr", ItemName = "item", Body = F("a") })(frame);
            Assert.Equal(symbol, value.ContainsSymbols);
            Assert.Equal(symbol ? ExprType.Symbol : ExprType.String, value.ArrayValue![0].Kind);
            SamplingCell stored = SamplingCell.Typed(value);
            Assert.False(stored.Value.ContainsSymbols);
            Assert.Equal(ExprType.String, stored.Value.ArrayValue![0].Kind);
        }
    }

    [Fact]
    public void IterationCode_SharedShapesKeepItemIndexAndOuterBindingsDistinct()
    {
        var binder = new SamplingExpressions(Slot, SamplingTier.Compiled);
        var state = new Dict
        {
            ["arr"] = new object[] { 10, 20, 30 }, ["a"] = new object[] { 4, 5 }, ["item"] = 90, ["position"] = 91
        };

        Expression Map(string key, string item, string index, Expression body)
        {
            return new MapExpr { StateKey = key, ItemName = item, IndexName = index, Body = body };
        }

        Expression[] expressions =
        [
            Map("arr", "item", "position", F("item")),
            Map("arr", "item", "position", F("position")),
            Map("arr", "position", "item", F("item")),
            Map("arr", "item", "item", F("item")),
            Map("arr", "item", "position", Map("a", "b", "c", F("item"))),
            Map("arr", "item", "position", Map("a", "b", "c", F("b"))),
            Map("arr", "item", "position", Map("a", "item", "position", F("item"))),
            Map("arr", "arr", "position", Map("arr", "item", "c", F("item"))),
            Map("arr", "item", "position",
                new FoldExpr
                {
                    StateKey = "a",
                    ItemName = "item",
                    AccName = "acc",
                    Init = F("item"),
                    Body = new BinaryExpr { Op = BinaryOp.Add, Left = F("acc"), Right = F("item") }
                })
        ];
        foreach (Expression expression in expressions)
        {
            Assert.Equal(Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(expression), state),
                Run(binder.Compile(expression), state));
        }

        var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], state);
        frame.Reset(default);
        Assert.Equal(new long[] { 10, 20, 30 },
            binder.Compile(expressions[0])(frame).ArrayValue!.Select(value => value.InlineInteger));
        Assert.Equal(new long[] { 0, 1, 2 },
            binder.Compile(expressions[1])(frame).ArrayValue!.Select(value => value.InlineInteger));
        Assert.Equal(new long[] { 19, 29, 39 },
            binder.Compile(expressions[^1])(frame).ArrayValue!.Select(value => value.InlineInteger));
        Assert.Equal(StateHasher.CanonicalHash(state), StateHasher.CanonicalHash(frame.Export()));
        Expression nestedSource = Map("arr", "item", "position", Map("item", "b", "c", F("b")));
        var nestedState = new Dict { ["arr"] = new object[] { new object[] { 1, 2 }, new object[] { 3 } } };
        Assert.Equal(Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(nestedSource), nestedState),
            Run(binder.Compile(nestedSource), nestedState));
    }

    [Fact]
    public void IterationCode_LazyReadsAndFailuresRestoreBindings()
    {
        Expression[] expressions =
        [
            new MapExpr
            {
                StateKey = "arr",
                ItemName = "item",
                IndexName = "position",
                Body = Constant(ConstantKind.Integer, "7")
            },
            new MapExpr { StateKey = "arr", ItemName = "item", IndexName = "position", Body = F("item") },
            new FilterExpr
            {
                StateKey = "arr",
                ItemName = "item",
                IndexName = "position",
                Predicate = Constant(ConstantKind.Boolean, "true")
            },
            new FilterExpr { StateKey = "arr", ItemName = "item", IndexName = "position", Predicate = F("item") }
        ];
        foreach (var source in new object[]
                 {
                     new object?[] { 1, null, 3 }, Array.Empty<object>(), new[] { new object() },
                     ExprValue.Array([ExprValue.Symbol("A"), ExprValue.String("B")])
                 })
        {
            var state = new Dict { ["arr"] = source, ["item"] = "outer", ["position"] = 73 };
            foreach (Expression expression in expressions)
            {
                Assert.Equal(Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(expression), state),
                    Run(new SamplingExpressions(Slot, SamplingTier.Compiled).Compile(expression), state));
            }
        }

        foreach (var source in new object?[][] { [null], [new object?[] { null }], [new object[] { 1, 2 }] })
        {
            var state = new Dict { ["arr"] = source };
            var nested = new MapExpr
            {
                StateKey = "arr",
                ItemName = "item",
                Body = new MapExpr { StateKey = "item", ItemName = "b", Body = F("b") }
            };
            var constants = new Dictionary<string, ExprValue> { ["arr"] = SamplingCell.FromRaw(source).StoredValue };
            var expected = Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(nested), state);
            Assert.Equal(expected,
                Run(new SamplingExpressions(Slot, SamplingTier.Compiled, constants).Compile(nested), state));
        }

        foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
        {
            var state = new Dict
            {
                ["arr"] = Enumerable.Range(0, 1024).ToArray(), ["item"] = "outer", ["position"] = 73
            };
            var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], state);
            frame.Reset(default);
            frame.CancellationToken = new CancellationToken(true);
            Assert.Throws<OperationCanceledException>(() =>
                new SamplingExpressions(Slot, tier).Compile(expressions[0])(frame));
            Assert.Equal(StateHasher.CanonicalHash(state), StateHasher.CanonicalHash(frame.Export()));
        }
    }

    [Fact]
    public void Count_EvaluatesAcceptedSelectorsAndRestoresBindingsOnFailure()
    {
        var state = new Dict { ["arr"] = new object?[] { "abc", null, 3 }, ["item"] = "outer" };
        Expression[] expressions =
        [
            new AggregateExpr { StateKey = "arr", ItemName = "item", Func = AggregateFunc.Count },
            new AggregateExpr
            {
                StateKey = "arr", ItemName = "item", Func = AggregateFunc.Count, ValueExpr = F("item")
            },
            new AggregateExpr
            {
                StateKey = "arr",
                ItemName = "item",
                Func = AggregateFunc.Count,
                Predicate = Constant(ConstantKind.Boolean, "false"),
                ValueExpr = new BinaryExpr
                {
                    Op = BinaryOp.Div,
                    Left = Constant(ConstantKind.Integer, "1"),
                    Right = Constant(ConstantKind.Integer, "0")
                }
            },
            new AggregateExpr
            {
                StateKey = "arr",
                ItemName = "item",
                Func = AggregateFunc.Count,
                ValueExpr = new BinaryExpr
                {
                    Op = BinaryOp.Div,
                    Left = Constant(ConstantKind.Integer, "1"),
                    Right = Constant(ConstantKind.Integer, "0")
                }
            }
        ];
        foreach (Expression expression in expressions)
        {
            Assert.Equal(Run(new SamplingExpressions(Slot, SamplingTier.Interpreted).Compile(expression), state),
                Run(new SamplingExpressions(Slot, SamplingTier.Compiled).Compile(expression), state));
        }

        var frame = new SamplingFrame(Layout, new SamplingCell[Layout.Count], state);
        frame.Reset(default);
        Assert.Equal(3,
            new SamplingExpressions(Slot, SamplingTier.Compiled).Compile(expressions[0])(frame).InlineInteger);
        Assert.Equal(0,
            new SamplingExpressions(Slot, SamplingTier.Compiled).Compile(expressions[2])(frame).InlineInteger);
    }

    [Fact]
    public void IterationVariable_IsNeverConstantState()
    {
        var binder = new SamplingExpressions(Slot, SamplingTier.Interpreted,
            new Dictionary<string, ExprValue> { ["item"] = ExprValue.Number(1) });
        Assert.Throws<ConstantStateConflict>(() =>
            binder.Compile(new MapExpr { StateKey = "arr", ItemName = "item", Body = F("item") }));
    }

    [Fact]
    public void FullGame_Pbt_1000PerTier_PayoutAndFinalStateMatchReference_WithAndWithoutConstantState()
    {
        SamplingTier before = SamplingOptions.Tier;
        try
        {
            foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
            {
                SamplingOptions.Tier = tier;
                CompileResult result = new GraphCompiler().Compile(Fixture("lab-demo.json"));
                Assert.True(result.IsValid);
                Assert.Equal("compiled-state-plan-v1", result.SamplingEngine);
                // 0: the graph's own initial state, where trace recording is
                // constant-false. 1: an unrelated field. 2, 3: the runner
                // overrides a field the plan treats as constant.
                Gen.Int[0, 1000000].Select(Gen.Int[0, 500], Gen.Int[0, 3]).Sample((seed, round, start) =>
                {
                    Dict state = start switch
                    {
                        0 => new Dict(), 1 => new Dict { ["external"] = "preserved" },
                        2 => new Dict { ["traceEnabled"] = true }, _ => new Dict { ["traceEnabled"] = false }
                    };
                    (BigInteger Value, Dict State) expected =
                        SampledInterpreter.RunSingle(result.ReferenceProgram!, state, seed, round);
                    (BigInteger Value, Dict State) actual =
                        SampledInterpreter.RunSingle(result.Program!, state, seed, round);
                    Assert.Equal(expected.Value, actual.Value);
                    Assert.Equal(StateHasher.CanonicalHash(expected.State), StateHasher.CanonicalHash(actual.State));
                }, seed: "sampling-tier-full-game-v1", iter: 1000);
            }
        }
        finally
        {
            SamplingOptions.Tier = before;
        }
    }

    [Fact]
    public void Sampled_StatisticsAreBitIdentical_AcrossTiersAndWorkerCounts()
    {
        SamplingTier before = SamplingOptions.Tier;
        try
        {
            string? expected = null;
            foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
            {
                SamplingOptions.Tier = tier;
                CompileResult result = new GraphCompiler().Compile(Fixture("lab-demo.json"));
                expected ??= JsonSerializer.Serialize(SampledInterpreter.Evaluate(result.ReferenceProgram!, new Dict(),
                    new SampledConfig
                    {
                        Seed = 42, MaxSpins = 20000, ChunkSize = 2048, WinScale = (double)result.WinScale
                    }).Stats.Snapshot());
                foreach (var workers in new[] { 1, 3, 8 })
                {
                    Assert.Equal(expected, JsonSerializer.Serialize(SampledInterpreter.Evaluate(result.Program!,
                        new Dict(),
                        new SampledConfig
                        {
                            Seed = 42,
                            MaxSpins = 20000,
                            ChunkSize = 2048,
                            DegreeOfParallelism = workers,
                            WinScale = (double)result.WinScale
                        }).Stats.Snapshot()));
                }
            }
        }
        finally
        {
            SamplingOptions.Tier = before;
        }
    }
}
