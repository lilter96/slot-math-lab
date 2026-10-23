using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
using SlotMath.Core.Optimization;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Optimization;

[Collection("SamplingOptions")]
public sealed class ScalarReuseTests
{
    private static ConstantExpr N(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString()
    };

    private static FieldAccessExpr S(string name) => new()
    {
        Target = "state",
        Path = [name]
    };

    private static BinaryExpr B(BinaryOp op, Expression a, Expression b) => new()
    {
        Op = op,
        Left = a,
        Right = b
    };

    private static Expression Formula() => B(BinaryOp.Add, B(BinaryOp.Mul, S("input"), N(3)), N(7));

    private static Dictionary<string, Port> Ports() => new()
    {
        ["state"] = new Port
        {
            Name = "state",
            Type = PortType.State
        }
    };

    private static ModifyStateNode W(string id, string expression, string output) => new()
    {
        Id = id,
        ExpressionId = expression,
        OutputKey = output,
        Inputs = Ports(),
        Outputs = Ports()
    };

    private static Edge E(string a, string b) => new()
    {
        Id = a + ":" + b,
        SourceNodeId = a,
        TargetNodeId = b,
        SourcePort = "state",
        TargetPort = "state"
    };

    private static GraphConfig Graph(bool overwrite = false) => new()
    {
        SchemaVersion = "1.0.0",
        Nodes =
        [
            W("first", "f", "a"), W("middle", "middle", overwrite ? "input" : "spare"),
            W("second", "g", "b"), W("pay", "pay", "win"),
            new MetricsSinkNode
            {
                Id = "sink",
                Inputs = Ports(),
                WinStateKey = "win",
                WinCap = 1000
            }
        ],
        Edges = [E("first", "middle"), E("middle", "second"), E("second", "pay"), E("pay", "sink")],
        Expressions = new Dictionary<string, Expression>
        {
            ["f"] = Formula(),
            ["g"] = Formula(),
            ["middle"] = N(9),
            ["pay"] = B(BinaryOp.Add, S("a"), S("b"))
        },
        InitialState = new Dictionary<string, JsonElement>
        {
            ["input"] = JsonSerializer.SerializeToElement(2)
        }
    };

    private static (Dictionary<string, int> Layout, SamplingFrame Frame) Frame(Dictionary<string, object?> values,
        params string[] extra)
    {
        Dictionary<string, int> layout = values.Keys.Concat(extra).Distinct().Select((key, i) => (key, i))
            .ToDictionary(pair => pair.key, pair => pair.i);
        var frame = new SamplingFrame(layout, new SamplingCell[layout.Count], values);
        frame.Reset(default);
        return (layout, frame);
    }

    private static GraphConfig RunGraph(Node[] nodes, Dictionary<string, Expression>? expressions = null)
    {
        GraphConfig graph = Graph();
        return graph with
        {
            Nodes = [.. nodes, graph.Nodes[^1]],
            Edges = nodes.Zip(nodes.Skip(1)).Select(pair => E(pair.First.Id, pair.Second.Id))
                .Append(E(nodes[^1].Id, "sink")).ToArray(),
            Expressions = expressions ?? graph.Expressions
        };
    }

    [Fact]
    public void GeneratedRunReusesAValueAcrossDisjointWritesAndResetsOnEveryInvocation()
    {
        GraphConfig graph = Graph();
        var ir = CompilerIr.Build(graph, []);
        Node[] nodes = graph.Nodes[..^1];
        var plan = ScalarReusePlan.Build(nodes, graph.Expressions!, ir);
        Assert.Equal(1, plan.EliminatedEvaluations);
        Assert.Equal(plan.Groups["first"], plan.Groups["second"]);
        (Dictionary<string, int> layout, SamplingFrame frame) = Frame(new Dictionary<string, object?>
        {
            ["input"] = 2
        }, "a", "spare", "b", "win");
        var owner = new SamplingExpressions(name => layout[name], SamplingTier.Compiled, ir: ir);
        (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
            native = SamplingCodegen.CompileRun(owner, name => layout[name], nodes, graph.Expressions!, BigInteger.One);
        Assert.Equal(1, native.Operands.ReusedScalarEvaluations);
        foreach (var input in new[] { 2, 9, -3, 2 })
        {
            frame.Cells[layout["input"]] = SamplingCell.Typed(ExprValue.Number(input));
            native.Code(frame, native.Operands, new SeededRandom(42), null);
            Assert.Equal(2 * (input * 3 + 7), frame.Read(layout["win"], "win").AsInteger());
            Assert.Equal(9, frame.Read(layout["spare"], "spare").AsInteger());
        }
    }

    [Fact]
    public void WritesToDependenciesInvalidateAvailability()
    {
        GraphConfig graph = Graph(true);
        var ir = CompilerIr.Build(graph, []);
        Assert.Empty(ScalarReusePlan.Build(graph.Nodes, graph.Expressions!, ir).Groups);
        CompileResult compiled = new GraphCompiler().Compile(graph);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Assert.Equal(new BigInteger(47),
            SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42).Value);
    }

    [Theory]
    [InlineData("draw")]
    [InlineData("observer")]
    [InlineData("opaque")]
    public void EffectBarriersCannotBeCrossed(string kind)
    {
        GraphConfig graph = Graph();
        Node barrier = kind switch
        {
            "draw" => new DrawNode
            {
                Id = "barrier",
                Inputs = Ports(),
                Outputs = Ports(),
                DrawWeights =
                [
                    new DrawWeight
                    {
                        OutcomeId = "a",
                        Weight = 1,
                        Value = 0
                    }
                ]
            },
            "observer" => new MetricsSinkNode
            {
                Id = "barrier",
                Inputs = Ports(),
                WinCap = 1000
            },
            _ => new MapNode
            {
                Id = "barrier",
                Inputs = Ports(),
                Outputs = Ports(),
                TransformId = "unrecognized"
            }
        };
        Node[] nodes = [graph.Nodes[0], barrier, graph.Nodes[2]];
        var ir = CompilerIr.Build(graph, []);
        Assert.Empty(ScalarReusePlan.Build(nodes, graph.Expressions!, ir).Groups);
    }

    [Fact]
    public void DifferentOperandsAndBudgetExhaustionNeverCreateFalseEquivalences()
    {
        GraphConfig graph = Graph();
        graph.Expressions!["g"] = B(BinaryOp.Add, B(BinaryOp.Mul, S("input"), N(3)), N(8));
        var ir = CompilerIr.Build(graph, []);
        Assert.Empty(ScalarReusePlan.Build(graph.Nodes, graph.Expressions, ir).Groups);
        var limited = ScalarReusePlan.Build(graph.Nodes, graph.Expressions, ir, 1);
        Assert.True(limited.BudgetReached);
        Assert.Empty(limited.Groups);
        Assert.Empty(ScalarReusePlan.Build(graph.Nodes, graph.Expressions, null).Groups);
    }

    [Fact]
    public void MeasurementsOnModifyNodesAlsoInvalidateAvailability()
    {
        GraphConfig graph = Graph();
        var ir = CompilerIr.Build(graph, [
            new MeasurementDefinition
            {
                Id = "observe",
                Name = "Observe",
                NodeId = "middle",
                Value = S("input")
            }
        ]);
        Assert.Empty(ScalarReusePlan.Build(graph.Nodes, graph.Expressions!, ir).Groups);
        Assert.Empty(DeferredStorePlan.Build(graph.Nodes, graph.Expressions!, ir).Fields);
    }

    [Fact]
    public void UnreadRepeatedStoresPublishTheLastValueAndKeepOtherFields()
    {
        GraphConfig graph = RunGraph([W("first", "f", "win"), W("middle", "middle", "spare"), W("second", "g", "win")]);
        var ir = CompilerIr.Build(graph, []);
        (Dictionary<string, int> layout, SamplingFrame frame) = Frame(new Dictionary<string, object?>
        {
            ["input"] = 2,
            ["win"] = -100
        }, "spare");
        (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
            native = SamplingCodegen.CompileRun(new SamplingExpressions(name => layout[name], ir: ir),
                name => layout[name], graph.Nodes[..^1], graph.Expressions!, BigInteger.One);
        Assert.Equal(1, native.Operands.EliminatedStateStores);
        Assert.Equal(1, native.Operands.ReusedScalarEvaluations);
        native.Code(frame, native.Operands, new SeededRandom(0), null);
        Assert.Equal(13, frame.Read(layout["win"], "win").AsInteger());
        Assert.Equal(9, frame.Read(layout["spare"], "spare").AsInteger());
        frame.Cells[layout["input"]] = SamplingCell.Typed(ExprValue.Number(9));
        native.Code(frame, native.Operands, new SeededRandom(0), null);
        Assert.Equal(34, frame.Read(layout["win"], "win").AsInteger());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedLaterStoresPublishThePreviousCompletedValue(bool integer)
    {
        var key = integer ? "__modified__" : "win";
        GraphConfig graph = RunGraph([
                W("first", "fraction", key) with
                {
                    OutputKey = integer ? null : key
                },
                W("second", "bad", key) with
                {
                    OutputKey = integer ? null : key
                }
            ],
            new Dictionary<string, Expression>
            {
                ["fraction"] = new ConstantExpr
                {
                    Kind = ConstantKind.Rational,
                    Value = "5/2"
                },
                ["bad"] = B(BinaryOp.Div, N(1), B(BinaryOp.Sub, S("input"), S("input")))
            });
        var ir = CompilerIr.Build(graph, []);
        (Dictionary<string, int> layout, SamplingFrame frame) = Frame(new Dictionary<string, object?>
        {
            ["input"] = 2,
            [key] = -100
        });
        (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
            native = SamplingCodegen.CompileRun(new SamplingExpressions(name => layout[name], ir: ir),
                name => layout[name], graph.Nodes[..^1], graph.Expressions!, BigInteger.One);
        Assert.Equal(1, native.Operands.EliminatedStateStores);
        Assert.Equal(EvalErrorCodes.DivisionByZero,
            Assert.Throws<ExpressionEvaluationException>(() =>
                native.Code(frame, native.Operands, new SeededRandom(0), null)).Code);
        Assert.Equal(integer ? ExprValue.Number(2) : ExprValue.Rational(5, 2), frame.Read(layout[key], key));
    }

    [Fact]
    public void CancelledRunsPublishTheLastCompletedStore()
    {
        Node[] nodes = Enumerable.Range(0, 300).Select(i => (Node)W("n" + i, "e" + i, "win")).ToArray();
        GraphConfig graph = RunGraph(nodes, Enumerable.Range(0, 300).ToDictionary(i => "e" + i, i => (Expression)N(i)));
        var ir = CompilerIr.Build(graph, []);
        (Dictionary<string, int> layout, SamplingFrame frame) = Frame(new Dictionary<string, object?>
        {
            ["win"] = -100
        });
        (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
            native = SamplingCodegen.CompileRun(new SamplingExpressions(name => layout[name], ir: ir),
                name => layout[name], nodes, graph.Expressions!, BigInteger.One);
        Assert.Equal(299, native.Operands.EliminatedStateStores);
        using var cancellation = new CancellationTokenSource();
        frame.CancellationToken = cancellation.Token;
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => native.Code(frame, native.Operands, new SeededRandom(0), null));
        Assert.Equal(254, frame.Read(layout["win"], "win").AsInteger());
    }

    [Fact]
    public void InternalReadsAndUnprovenStoresPreventDeferral()
    {
        GraphConfig graph = Graph();
        graph.Nodes[1] = W("middle", "middle", "a");
        var ir = CompilerIr.Build(graph, []);
        Assert.Empty(DeferredStorePlan.Build(graph.Nodes[..^1], graph.Expressions!, ir).Fields);
        graph = RunGraph([W("first", "f", "win"), W("second", "copy", "win")]);
        graph.Expressions!["copy"] = S("input");
        Assert.Empty(DeferredStorePlan.Build(graph.Nodes[..^1], graph.Expressions, CompilerIr.Build(graph, [])).Fields);
        var limited = DeferredStorePlan.Build(graph.Nodes[..^1], graph.Expressions, CompilerIr.Build(graph, []), 1);
        Assert.True(limited.BudgetReached);
        Assert.Empty(limited.Fields);
    }

    [Fact]
    public void DrawOverwritesCannotBeReplacedByAnEarlierDeferredStore()
    {
        var draw = new DrawNode
        {
            Id = "draw",
            Inputs = Ports(),
            Outputs = Ports(),
            StateWriteKey = "win",
            DrawWeights =
            [
                new DrawWeight
                {
                    OutcomeId = "seven",
                    Weight = 1,
                    Value = 7,
                    StateValue = N(7)
                }
            ]
        };
        GraphConfig graph = RunGraph([W("first", "f", "win"), W("second", "g", "win"), draw]);
        var ir = CompilerIr.Build(graph, []);
        Assert.Empty(DeferredStorePlan.Build(graph.Nodes[..^1], graph.Expressions!, ir).Fields);
        (Dictionary<string, int> layout, SamplingFrame frame) = Frame(new Dictionary<string, object?>
        {
            ["input"] = 2,
            ["win"] = 0
        });
        (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
            native = SamplingCodegen.CompileRun(new SamplingExpressions(name => layout[name], ir: ir),
                name => layout[name], graph.Nodes[..^1], graph.Expressions!, BigInteger.One);
        native.Code(frame, native.Operands, new SeededRandom(0), null);
        Assert.Equal(7, frame.Read(layout["win"], "win").AsInteger());
    }

    [Fact]
    public void FailureBeforeTheFirstDeferredStoreLeavesRawStorageUntouched()
    {
        GraphConfig graph = RunGraph([W("first", "bad", "win"), W("second", "bad", "win")],
            new Dictionary<string, Expression>
            {
                ["bad"] = B(BinaryOp.Div, N(1), B(BinaryOp.Sub, S("input"), S("input")))
            });
        var ir = CompilerIr.Build(graph, []);
        (Dictionary<string, int> layout, SamplingFrame frame) = Frame(new Dictionary<string, object?>
        {
            ["input"] = 2,
            ["win"] = 777
        });
        (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
            native = SamplingCodegen.CompileRun(new SamplingExpressions(name => layout[name], ir: ir),
                name => layout[name], graph.Nodes[..^1], graph.Expressions!, BigInteger.One);
        Assert.Equal(1, native.Operands.EliminatedStateStores);
        Assert.Throws<ExpressionEvaluationException>(() =>
            native.Code(frame, native.Operands, new SeededRandom(0), null));
        Assert.True(frame.Cells[layout["win"]].HasRaw);
        Assert.Equal(777, Assert.IsType<int>(frame.Cells[layout["win"]].Raw));
    }

    [Fact]
    public void DeferredMaskStoresKeepThePackedRepresentation()
    {
        Expression Mask(int value)
        {
            return new BitSetExpr
            {
                Op = BitSetOp.Union,
                Args =
                [
                    N(value), new BitSetExpr
                    {
                        Op = BitSetOp.Shift,
                        Args = [N(1), N(63)]
                    }
                ]
            };
        }

        GraphConfig graph = RunGraph([W("first", "one", "mask"), W("second", "two", "mask")],
            new Dictionary<string, Expression>
            {
                ["one"] = Mask(2),
                ["two"] = Mask(4)
            });
        var ir = CompilerIr.Build(graph, []);
        (Dictionary<string, int> layout, SamplingFrame actual) = Frame(new Dictionary<string, object?>
        {
            ["mask"] = 0
        });
        var expected = new SamplingFrame(layout, new SamplingCell[layout.Count], new Dictionary<string, object?>
        {
            ["mask"] = 0
        });
        expected.Reset(default);
        var fields = new HashSet<string>
        {
            "mask"
        };
        (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
            optimized = SamplingCodegen.CompileRun(
                new SamplingExpressions(name => layout[name], inlineMaskFields: fields, ir: ir), name => layout[name],
                graph.Nodes[..^1], graph.Expressions!, BigInteger.One);
        (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
            baseline = SamplingCodegen.CompileRun(
                new SamplingExpressions(name => layout[name], inlineMaskFields: fields), name => layout[name],
                graph.Nodes[..^1], graph.Expressions!, BigInteger.One);
        Assert.Equal(1, optimized.Operands.EliminatedStateStores);
        optimized.Code(actual, optimized.Operands, new SeededRandom(0), null);
        baseline.Code(expected, baseline.Operands, new SeededRandom(0), null);
        Assert.Equal(expected.Cells[layout["mask"]].StoredValue, actual.Cells[layout["mask"]].StoredValue);
        Assert.Equal((1UL << 63) | 4, actual.Cells[layout["mask"]].StoredMask);
    }

    [Fact]
    public void ARepeatedThrowingValueStillFailsBeforeTheFirstWrite()
    {
        GraphConfig graph = Graph();
        graph.Expressions!["f"] =
            graph.Expressions["g"] = B(BinaryOp.Div, N(1), B(BinaryOp.Sub, S("input"), S("input")));
        var ir = CompilerIr.Build(graph, []);
        (Dictionary<string, int> layout, SamplingFrame frame) = Frame(new Dictionary<string, object?>
        {
            ["input"] = 2,
            ["a"] = 777
        }, "spare", "b", "win");
        (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
            native = SamplingCodegen.CompileRun(new SamplingExpressions(name => layout[name], ir: ir),
                name => layout[name], graph.Nodes[..^1], graph.Expressions, BigInteger.One);
        Assert.Equal(1, native.Operands.ReusedScalarEvaluations);
        var error = Assert.Throws<ExpressionEvaluationException>(() =>
            native.Code(frame, native.Operands, new SeededRandom(0), null));
        Assert.Equal(EvalErrorCodes.DivisionByZero, error.Code);
        Assert.Equal(777, frame.Read(layout["a"], "a").AsInteger());
        Assert.False(frame.Cells[layout["b"]].Present);
    }

    [Fact]
    public void CancellationCheckpointsAndPartialWritesArePreserved()
    {
        GraphConfig graph = Graph();
        Node[] nodes = Enumerable.Range(0, 300).Select(i => (Node)W("n" + i, "f", "out" + i)).ToArray();
        graph = graph with
        {
            Nodes = [.. nodes, graph.Nodes[^1]],
            Edges = []
        };
        var ir = CompilerIr.Build(graph, []);
        (Dictionary<string, int> layout, SamplingFrame actual) = Frame(new Dictionary<string, object?>
        {
            ["input"] = 2
        }, nodes.Cast<ModifyStateNode>().Select(node => node.OutputKey!).ToArray());
        var expected = new SamplingFrame(layout, new SamplingCell[layout.Count], new Dictionary<string, object?>
        {
            ["input"] = 2
        });
        (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
            optimized = SamplingCodegen.CompileRun(new SamplingExpressions(name => layout[name], ir: ir),
                name => layout[name], nodes, graph.Expressions!, BigInteger.One);
        (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
            baseline = SamplingCodegen.CompileRun(new SamplingExpressions(name => layout[name]), name => layout[name],
                nodes, graph.Expressions!, BigInteger.One);
        Assert.Equal(299, optimized.Operands.ReusedScalarEvaluations);
        using var cancellation = new CancellationTokenSource();
        actual.Reset(default);
        expected.Reset(default);
        actual.CancellationToken = cancellation.Token;
        expected.CancellationToken = cancellation.Token;
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            optimized.Code(actual, optimized.Operands, new SeededRandom(0), null));
        Assert.Throws<OperationCanceledException>(() =>
            baseline.Code(expected, baseline.Operands, new SeededRandom(0), null));
        for (var i = 0; i < 300; i++)
        {
            Assert.Equal(i < 255, actual.Cells[layout["out" + i]].Present);
            Assert.Equal(expected.Cells[layout["out" + i]].Present, actual.Cells[layout["out" + i]].Present);
            if (i < 255)
            {
                Assert.Equal(expected.Read(layout["out" + i], "out" + i), actual.Read(layout["out" + i], "out" + i));
            }
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void CompleteSeededStateMatchesTheIndependentProgram(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            GraphConfig graph = Graph();
            graph = graph with
            {
                Nodes =
                [
                    new DrawNode
                    {
                        Id = "random",
                        StateWriteKey = "input",
                        Outputs = Ports(),
                        DrawWeights =
                        [
                            new DrawWeight
                            {
                                OutcomeId = "one",
                                Weight = 1,
                                Value = 1,
                                StateValue = N(1)
                            },
                            new DrawWeight
                            {
                                OutcomeId = "three",
                                Weight = 3,
                                Value = 3,
                                StateValue = N(3)
                            }
                        ]
                    },
                    .. graph.Nodes
                ],
                Edges = [E("random", "first"), .. graph.Edges]
            };
            var source = JsonSerializer.Serialize(graph, JsonOptions.Default);
            CompileResult compiled = new GraphCompiler().Compile(graph);
            CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(graph);
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            Assert.True(reference.IsValid);
            SamplingPreparation prepared = Assert.IsType<SamplingPlanSlot>(compiled.Program).Preparation;
            Assert.Equal(1,
                ScalarReusePlan.Build(prepared.Config.Nodes, prepared.Config.Expressions!, prepared.Ir)
                    .EliminatedEvaluations);
            for (var seed = 0; seed < 128; seed++)
            {
                (BigInteger Value, Dictionary<string, object?> State) actual =
                    SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed);
                (BigInteger Value, Dictionary<string, object?> State) expected =
                    SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), seed);
                Assert.Equal(expected.Value, actual.Value);
                Assert.Equal(JsonSerializer.Serialize(expected.State.OrderBy(pair => pair.Key), JsonOptions.Default),
                    JsonSerializer.Serialize(actual.State.OrderBy(pair => pair.Key), JsonOptions.Default));
            }

            Assert.Equal(source, JsonSerializer.Serialize(graph, JsonOptions.Default));
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    private static MapExpr InvariantMap(string item = "item") => new()
    {
        StateKey = "source",
        ItemName = item,
        ItemType = ExprType.Number,
        Body = new IfExpr
        {
            Condition = new CompareExpr
            {
                Op = CompareOp.Lt,
                Left = S(item),
                Right = N(0)
            },
            ThenExpr = N(0),
            ElseExpr = B(BinaryOp.Add, S(item), B(BinaryOp.Div, B(BinaryOp.Mul, S("factor"), N(6)), S("denominator")))
        }
    };

    [Fact]
    public void ReuseKeepsTheDrawTranscriptAndFollowingWord()
    {
        GraphConfig graph = Graph();
        var draw = new DrawNode
        {
            Id = "prefix",
            StateWriteKey = "input",
            Inputs = Ports(),
            Outputs = Ports(),
            DrawWeights =
            [
                new DrawWeight
                {
                    OutcomeId = "one",
                    Weight = 1,
                    Value = 1,
                    StateValue = N(1)
                },
                new DrawWeight
                {
                    OutcomeId = "three",
                    Weight = 3,
                    Value = 3,
                    StateValue = N(3)
                }
            ]
        };
        Node[] nodes =
        [
            draw, .. graph.Nodes[..^1], draw with
            {
                Id = "suffix",
                StateWriteKey = "suffix"
            }
        ];
        graph = graph with
        {
            Nodes = [.. nodes, graph.Nodes[^1]]
        };
        var ir = CompilerIr.Build(graph, []);
        (Dictionary<string, int> layout, SamplingFrame actual) = Frame(new Dictionary<string, object?>
        {
            ["input"] = 2
        }, "a", "spare", "b", "win", "suffix");
        var expected = new SamplingFrame(layout, new SamplingCell[layout.Count], new Dictionary<string, object?>
        {
            ["input"] = 2
        });
        (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
            optimized = SamplingCodegen.CompileRun(new SamplingExpressions(name => layout[name], ir: ir),
                name => layout[name], nodes, graph.Expressions!, BigInteger.One);
        (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
            baseline = SamplingCodegen.CompileRun(new SamplingExpressions(name => layout[name]), name => layout[name],
                nodes, graph.Expressions!, BigInteger.One);
        Assert.Equal(1, optimized.Operands.ReusedScalarEvaluations);
        for (var seed = 0; seed < 32; seed++)
        {
            actual.Reset(default);
            expected.Reset(default);
            var a = new SeededRandom(seed);
            var b = new SeededRandom(seed);
            a.EnableAudit();
            b.EnableAudit();
            optimized.Code(actual, optimized.Operands, a, null);
            baseline.Code(expected, baseline.Operands, b, null);
            Assert.Equal(JsonSerializer.Serialize(b.AuditSnapshot(64)), JsonSerializer.Serialize(a.AuditSnapshot(64)));
            Assert.Equal(b.NextUInt64(), a.NextUInt64());
            Assert.Equal(expected.Read(layout["win"], "win"), actual.Read(layout["win"], "win"));
            Assert.Equal(expected.Read(layout["suffix"], "suffix"), actual.Read(layout["suffix"], "suffix"));
        }
    }

    [Fact]
    public void InlineMaskStoresDoNotBecomeOrdinaryScalarCaptures()
    {
        GraphConfig graph = Graph();
        graph.Expressions!["f"] = graph.Expressions["g"] = new BitSetExpr
        {
            Op = BitSetOp.Union,
            Args =
            [
                S("input"), new BitSetExpr
                {
                    Op = BitSetOp.Shift,
                    Args = [N(1), N(63)]
                }
            ]
        };
        var ir = CompilerIr.Build(graph, []);
        (Dictionary<string, int> layout, SamplingFrame frame) = Frame(new Dictionary<string, object?>
        {
            ["input"] = 2
        }, "a", "spare", "b", "win");
        var owner = new SamplingExpressions(name => layout[name], inlineMaskFields: new HashSet<string>
        {
            "a",
            "b"
        }, ir: ir);
        (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
            native = SamplingCodegen.CompileRun(owner, name => layout[name], graph.Nodes[..^2], graph.Expressions,
                BigInteger.One);
        Assert.Equal(0, native.Operands.ReusedScalarEvaluations);
        native.Code(frame, native.Operands, new SeededRandom(0), null);
        Assert.Equal(frame.Cells[layout["a"]].StoredMask, frame.Cells[layout["b"]].StoredMask);
        Assert.Equal((1UL << 63) | 2, frame.Cells[layout["b"]].StoredMask);
    }

    [Fact]
    public void InvariantValuesAreLazyAndResetForEveryCollectionInvocation()
    {
        (Dictionary<string, int> layout, SamplingFrame frame) = Frame(new Dictionary<string, object?>
        {
            ["source"] = new[] { -1 },
            ["factor"] = 3,
            ["denominator"] = 0,
            ["item"] = 99
        });
        MapExpr expression = InvariantMap();
        (Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) native =
            SamplingCodegen.CompileValue(new SamplingExpressions(name => layout[name]), name => layout[name],
                expression);
        Assert.True(native.Operands.InvariantScalarValues > 0);
        Assert.Equal(BigInteger.Zero, Assert.Single(native.Code(frame, native.Operands).ArrayValue!).AsInteger());
        frame.Cells[layout["source"]] = SamplingCell.Typed(ExprValue.Array([]));
        Assert.Empty(native.Code(frame, native.Operands).ArrayValue!);
        frame.Cells[layout["source"]] =
            SamplingCell.Typed(ExprValue.Array([ExprValue.Number(-1), ExprValue.Number(1)]));
        Assert.Equal(EvalErrorCodes.DivisionByZero,
            Assert.Throws<ExpressionEvaluationException>(() => native.Code(frame, native.Operands)).Code);
        Assert.Equal(99, frame.Read(layout["item"], "item").AsInteger());
        frame.Cells[layout["denominator"]] = SamplingCell.Typed(ExprValue.Number(2));
        Assert.Equal(new BigInteger[] { 0, 10 },
            native.Code(frame, native.Operands).ArrayValue!.Select(value => value.AsInteger()));
        frame.Cells[layout["factor"]] = SamplingCell.Typed(ExprValue.Number(4));
        Assert.Equal(new BigInteger[] { 0, 13 },
            native.Code(frame, native.Operands).ArrayValue!.Select(value => value.AsInteger()));
    }

    [Fact]
    public void IteratorShadowingPreventsAnInvariantProof()
    {
        (Dictionary<string, int> layout, SamplingFrame frame) = Frame(new Dictionary<string, object?>
        {
            ["source"] = new[] { 1, 2, 3 },
            ["factor"] = 99,
            ["denominator"] = 2
        });
        (Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) native =
            SamplingCodegen.CompileValue(new SamplingExpressions(name => layout[name]), name => layout[name],
                InvariantMap("factor"));
        Assert.Equal(0, native.Operands.InvariantScalarValues);
        Assert.Equal(new BigInteger[] { 4, 8, 12 },
            native.Code(frame, native.Operands).ArrayValue!.Select(value => value.AsInteger()));
        Assert.Equal(99, frame.Read(layout["factor"], "factor").AsInteger());
    }

    [Fact]
    public void NestedLoopInvariantsResetWhenTheOuterBindingChanges()
    {
        var expression = new MapExpr
        {
            StateKey = "outer",
            ItemName = "outerItem",
            ItemType = ExprType.Number,
            Body = new FoldExpr
            {
                StateKey = "inner",
                ItemName = "innerItem",
                AccName = "total",
                ItemType = ExprType.Number,
                Init = N(0),
                Body = B(BinaryOp.Add, S("total"),
                    B(BinaryOp.Add, S("innerItem"),
                        B(BinaryOp.Mul, S("outerItem"), B(BinaryOp.Add, S("factor"), N(2)))))
            }
        };
        (Dictionary<string, int> layout, SamplingFrame frame) = Frame(new Dictionary<string, object?>
        {
            ["outer"] = new[] { 1, 4 },
            ["inner"] = new[] { 2, 3 },
            ["factor"] = 3,
            ["outerItem"] = 90,
            ["innerItem"] = 91,
            ["total"] = 92
        });
        (Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) native =
            SamplingCodegen.CompileValue(new SamplingExpressions(name => layout[name]), name => layout[name],
                expression);
        Assert.True(native.Operands.InvariantScalarValues > 0);
        for (var round = 0; round < 5; round++)
        {
            Assert.Equal(new BigInteger[] { 15, 45 },
                native.Code(frame, native.Operands).ArrayValue!.Select(value => value.AsInteger()));
            Assert.Equal(90, frame.Read(layout["outerItem"], "outerItem").AsInteger());
            Assert.Equal(91, frame.Read(layout["innerItem"], "innerItem").AsInteger());
            Assert.Equal(92, frame.Read(layout["total"], "total").AsInteger());
        }
    }

    [Fact]
    public void FilterPredicatesCanReuseAScalarWithoutCachingItemDependentDecisions()
    {
        var expression = new FilterExpr
        {
            StateKey = "source",
            ItemName = "item",
            ItemType = ExprType.Number,
            Predicate = new CompareExpr
            {
                Op = CompareOp.Gt,
                Left = S("item"),
                Right = B(BinaryOp.Div, B(BinaryOp.Mul, S("factor"), N(6)), S("denominator"))
            }
        };
        (Dictionary<string, int> layout, SamplingFrame frame) = Frame(new Dictionary<string, object?>
        {
            ["source"] = new[] { 1, 5, 9 },
            ["factor"] = 2,
            ["denominator"] = 2,
            ["item"] = 99
        });
        (Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) native =
            SamplingCodegen.CompileValue(new SamplingExpressions(name => layout[name]), name => layout[name],
                expression);
        Assert.True(native.Operands.InvariantScalarValues > 0);
        Assert.Equal(new BigInteger[] { 9 },
            native.Code(frame, native.Operands).ArrayValue!.Select(value => value.AsInteger()));
    }

    [Fact]
    public void DeadLexicalValuesAreRemovedOnlyWhenTotalAndUnused()
    {
        var dead = new LetExpr
        {
            Name = "unused",
            Value = N(5),
            Body = S("input")
        };
        ExpressionSimplification simplified = ExpressionSimplifier.Simplify(dead);
        Assert.IsType<FieldAccessExpr>(simplified.Expression);
        Assert.Equal(1, simplified.Rules["dead-local"]);
        LetExpr throwing = dead with
        {
            Value = B(BinaryOp.Div, N(1), N(0))
        };
        Assert.IsType<LetExpr>(ExpressionSimplifier.Simplify(throwing).Expression);
        Assert.Equal(EvalErrorCodes.DivisionByZero,
            Assert.Throws<ExpressionEvaluationException>(() =>
                ExactExpressionEvaluator.Evaluate(ExpressionSimplifier.Simplify(throwing).Expression,
                    EvalContext.Empty)).Code);
        Assert.IsType<LetExpr>(ExpressionSimplifier.Simplify(dead with
        {
            Value = S("missing")
        }).Expression);
        Assert.IsType<LetExpr>(ExpressionSimplifier.Simplify(dead with
        {
            Value = new ConstantExpr
            {
                Kind = ConstantKind.Integer,
                Value = "invalid"
            }
        }).Expression);
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 3)]
    public void LexicalShadowingRetainsUsesInTheInnerInitializer(bool readOuter, int expected)
    {
        var expression = new LetExpr
        {
            Name = "value",
            Value = N(3),
            Body = new LetExpr
            {
                Name = "value",
                Value = readOuter
                    ? new LocalExpr
                    {
                        Name = "value"
                    }
                    : N(4),
                Body = new LocalExpr
                {
                    Name = "value"
                }
            }
        };
        ExpressionSimplification simplified = ExpressionSimplifier.Simplify(expression);
        Assert.Equal(expected, ExactExpressionEvaluator.Evaluate(simplified.Expression, EvalContext.Empty).AsInteger());
        Assert.Equal(readOuter, ((LetExpr)simplified.Expression).Value is ConstantExpr { Value: "3" });
    }
}
