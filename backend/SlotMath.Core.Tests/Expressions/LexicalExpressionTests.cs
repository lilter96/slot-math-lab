using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;
using SlotMath.Core.Optimization;

namespace SlotMath.Core.Tests.Expressions;

public sealed class LexicalExpressionTests
{
    private static ConstantExpr N(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString()
    };

    private static LocalExpr L(string name, params string[] path) => new()
    {
        Name = name,
        Path = path
    };

    private static FieldAccessExpr S(params string[] path) => new()
    {
        Target = "state",
        Path = path
    };

    private static BinaryExpr Add(Expression left, Expression right) => new()
    {
        Op = BinaryOp.Add,
        Left = left,
        Right = right
    };

    private static LetExpr Let(string name, Expression value, Expression body) => new()
    {
        Name = name,
        Value = value,
        Body = body
    };

    private static RecordExpr R(params (string Name, Expression Value)[] fields) => new()
    {
        Fields = fields.Select(field => new RecordFieldExpr
        {
            Name = field.Name,
            Value = field.Value
        }).ToArray()
    };

    private static ProjectExpr P(Expression value, params string[] path) => new()
    {
        Value = value,
        Path = path
    };

    private static ExprValue Run(Expression expression, Dictionary<string, object?> state, int mode,
        SamplingShapes? shapes = null)
    {
        if (mode == -1)
        {
            return ExactExpressionEvaluator.Evaluate(expression, new EvalContext
            {
                State = state
            });
        }

        Dictionary<string, int> slots = state.Keys.Select((key, index) => (key, index))
            .ToDictionary(pair => pair.key, pair => pair.index);

        int Slot(string name)
        {
            if (!slots.TryGetValue(name, out var index))
            {
                slots[name] = index = slots.Count;
            }

            return index;
        }

        SamplingTier tier = mode switch
        {
            0 => SamplingTier.Interpreted,
            1 => SamplingTier.Compiled,
            _ => SamplingTier.Tiered
        };
        Func<SamplingFrame, ExprValue>
            program = new SamplingExpressions(Slot, tier, shapes: shapes).Compile(expression);
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], state);
        frame.Reset(default);
        ExprValue result = program(frame);
        if (mode == 2)
        {
            for (var call = 1; call < 80; call++)
            {
                result = program(frame);
            }
        }

        Assert.Null(frame.Locals);
        return result;
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void LexicalNamespaceShadowingAndNonrecursiveInitializersAreIndependentOfState(int mode)
    {
        LetExpr expression = Let("x", N(5), new TupleExpr
        {
            Items = [S("x"), L("x"), Let("x", Add(L("x"), N(1)), L("x")), L("x")]
        });
        var state = new Dictionary<string, object?>
        {
            ["x"] = 100
        };
        ExprValue value = Run(expression, state, mode);
        Assert.Equal(ExprType.Record, value.Kind);
        Assert.Equal(new[] { 100, 5, 6, 5 },
            Enumerable.Range(0, 4).Select(index => (int)value.RecordValue![index.ToString()].NumberNumerator));
        Assert.Equal(100, state["x"]);
        var error = Assert.Throws<ExpressionEvaluationException>(() => Run(Let("y", L("y"), N(1)), state, mode));
        Assert.Equal("EVAL_MISSING_LOCAL", error.Code);
        Assert.Equal("local.y", error.Location);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InitializerRunsOnceEvenWithRepeatedReferences(int mode)
    {
        var probe = new Probe();
        Assert.Equal(ExprValue.Number(14), Run(Let("n", S("probe", "Value"), Add(L("n"), L("n"))),
            new Dictionary<string, object?>
            {
                ["probe"] = probe
            }, mode));
        Assert.Equal(mode == 2 ? 80 : 1, probe.Calls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RecordsRetainExactHeterogeneousValuesAndOrderedEagerFailures(int mode)
    {
        Expression fraction = new ConstantExpr
        {
            Kind = ConstantKind.Rational,
            Value = "9007199254740993/7"
        };
        var flag = new ConstantExpr
        {
            Kind = ConstantKind.Boolean,
            Value = "true"
        };
        LetExpr expression = Let("result", R(("amount", fraction), ("flag", flag),
                ("nested", new TupleExpr
                {
                    Items =
                    [
                        N(3), new ConstantExpr
                        {
                            Kind = ConstantKind.String,
                            Value = "token"
                        }
                    ]
                })),
            new TupleExpr
            {
                Items = [L("result", "amount"), L("result", "nested", "1"), L("result", "flag")]
            });
        ExprValue value = Run(expression, new Dictionary<string, object?>(), mode);
        Assert.Equal(ExprValue.Rational(BigInteger.Parse("9007199254740993"), 7), value.RecordValue!["0"]);
        Assert.Equal(ExprValue.String("token"), value.RecordValue["1"]);
        Assert.Equal(ExprValue.Bool(true), value.RecordValue["2"]);
        RecordExpr failing = R(("zFirst", S("first")), ("aSecond", S("second")));
        var error = Assert.Throws<ExpressionEvaluationException>(() =>
            Run(P(failing, "aSecond"), new Dictionary<string, object?>(), mode));
        Assert.Equal("first", error.Location);
        error = Assert.Throws<ExpressionEvaluationException>(() =>
            Run(Let("unused", S("missing"), N(10)), new Dictionary<string, object?>(), mode));
        Assert.Equal("missing", error.Location);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void LocalsCrossIterationAndInterpretedFallbackBoundaries(int mode)
    {
        var state = new Dictionary<string, object?>
        {
            ["items"] = new[] { 2, 3 }
        };
        var sum = new AggregateExpr
        {
            Func = AggregateFunc.Sum,
            StateKey = "items",
            ItemName = "x",
            ItemType = ExprType.Number,
            ValueExpr = Add(S("x"), L("offset"))
        };
        var fold = new FoldExpr
        {
            StateKey = "items",
            ItemName = "x",
            AccName = "acc",
            ItemType = ExprType.Number,
            Init = N(0),
            Body = Add(S("acc"), Add(S("x"), L("offset")))
        };
        var map = new MapExpr
        {
            StateKey = "items",
            ItemName = "offset",
            ItemType = ExprType.Number,
            Body = Add(S("offset"), L("offset"))
        };
        ExprValue value = Run(Let("offset", N(7), new TupleExpr
        {
            Items = [sum, fold, map]
        }), state, mode);
        Assert.Equal(ExprValue.Number(19), value.RecordValue!["0"]);
        Assert.Equal(ExprValue.Number(19), value.RecordValue["1"]);
        Assert.Equal(new[] { ExprValue.Number(9), ExprValue.Number(10) }, value.RecordValue["2"].ArrayValue);
        Assert.Equal(new[] { "items" }, state.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EscapingErrorsRestoreTheFrameBeforeAnotherExpression(int mode)
    {
        SamplingTier tier = mode == 0 ? SamplingTier.Interpreted :
            mode == 1 ? SamplingTier.Compiled : SamplingTier.Tiered;
        var owner = new SamplingExpressions(_ => 0, tier);
        Func<SamplingFrame, ExprValue> failing = owner.Compile(Let("hidden", N(9), S("missing")));
        Func<SamplingFrame, ExprValue> read = owner.Compile(L("hidden"));
        var frame = new SamplingFrame(new Dictionary<string, int>
        {
            ["missing"] = 0
        }, new SamplingCell[1], new Dictionary<string, object?>());
        frame.Reset(default);
        for (var attempt = 0; attempt < 80; attempt++)
        {
            Assert.Throws<ExpressionEvaluationException>(() => failing(frame));
            Assert.Null(frame.Locals);
            Assert.Equal("EVAL_MISSING_LOCAL", Assert.Throws<ExpressionEvaluationException>(() => read(frame)).Code);
        }
    }

    [Fact]
    public void SharedCompiledShapesBindPathsNamesAndLexicalTargetsPerExpression()
    {
        var shapes = new SamplingShapes();
        var state = new Dictionary<string, object?>();
        Assert.Equal(ExprValue.Number(3),
            Run(Let("a", R(("left", N(3)), ("right", N(9))), L("a", "left")), state, 1, shapes));
        var count = shapes.Count;
        Assert.Equal(ExprValue.Number(11),
            Run(Let("b", R(("other", N(7)), ("chosen", N(11))), L("b", "chosen")), state, 1, shapes));
        Assert.Equal(count, shapes.Count);
        Assert.Equal(ExprValue.Number(2), Run(Let("a", N(2), Let("b", N(8), L("a"))), state, 1, shapes));
        Assert.Equal(ExprValue.Number(8), Run(Let("a", N(2), Let("b", N(8), L("b"))), state, 1, shapes));
    }

    [Fact]
    public void ShapesPropagateThroughLexicalProjectionAndRejectInvalidContracts()
    {
        LetExpr expression = Let("r", R(("x", N(1)), ("t", new TupleExpr
        {
            Items = [N(2)]
        })), Add(L("r", "x"), L("r", "t", "0")));
        Assert.Empty(ExpressionTypeChecker.Check(expression, new TypeCheckContext(), ExprType.Number));
        Expression[] invalid =
        [
            L("missing"), Let("", N(1), N(2)), Let("self", L("self"), N(1)), R(("dup", N(1)), ("dup", N(2))),
            R(("", N(1))),
            P(R(("x", N(1))), "missing"), P(N(1), "ignored"), P(R(("x", N(1))), ""), new TupleExpr
            {
                Items = Enumerable.Repeat<Expression>(N(1), 65).ToArray()
            }
        ];
        foreach (Expression item in invalid)
        {
            Assert.NotEmpty(ExpressionTypeChecker.Check(item, new TypeCheckContext()));
        }

        var nested = new MapExpr
        {
            StateKey = "items",
            ItemName = "x",
            ItemType = ExprType.Number,
            Body = Let("temp", N(1), R(("inner", new MapExpr
            {
                StateKey = "items",
                ItemName = "y",
                ItemType = ExprType.Number,
                Body = S("y")
            })))
        };
        Assert.NotEmpty(ExpressionTypeChecker.Check(nested, new TypeCheckContext
        {
            StateFields =
            [
                new FieldDescriptor
                {
                    Name = "items",
                    Type = ExprType.Array,
                    ArrayItemType = ExprType.Number
                }
            ]
        }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void FieldBudgetPrecedesChildEvaluationAndProjectionErrorsAreLocated(int mode)
    {
        var tooLarge = new TupleExpr
        {
            Items = Enumerable.Repeat<Expression>(S("missing"), 65).ToArray()
        };
        var error = Assert.Throws<ExpressionEvaluationException>(() =>
            Run(tooLarge, new Dictionary<string, object?>(), mode));
        Assert.Equal("EVAL_RECORD_BUDGET", error.Code);
        Assert.Equal("tuple", error.Location);
        Assert.Equal("A tuple exceeds 64 fields.", error.Message);
        var record = new RecordExpr
        {
            Fields = Enumerable.Range(0, 65).Select(index => new RecordFieldExpr
            {
                Name = index.ToString(),
                Value = S("missing")
            }).ToArray()
        };
        error = Assert.Throws<ExpressionEvaluationException>(() =>
            Run(record, new Dictionary<string, object?>(), mode));
        Assert.Equal("record", error.Location);
        Assert.Equal("A record exceeds 64 fields.", error.Message);
        Assert.Equal(64, Run(new TupleExpr
        {
            Items = Enumerable.Range(0, 64).Select(index => (Expression)N(index)).ToArray()
        }, new Dictionary<string, object?>(), mode).RecordValue!.Count);
        error = Assert.Throws<ExpressionEvaluationException>(() =>
            Run(P(R(("present", N(1))), "missing"), new Dictionary<string, object?>(), mode));
        Assert.Equal("EVAL_MISSING_STATE", error.Code);
        Assert.Equal("project", error.Location);
    }

    [Fact]
    public void JsonRoundTripAndSimplifierPreserveLexicalShadowingAndFieldOrder()
    {
        LetExpr expression = Let("x", N(1), new IfExpr
        {
            Condition = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = L("x"),
                Right = N(1)
            },
            ThenExpr = Let("x", N(2), new IfExpr
            {
                Condition = new CompareExpr
                {
                    Op = CompareOp.Eq,
                    Left = L("x"),
                    Right = N(1)
                },
                ThenExpr = N(99),
                ElseExpr = N(7)
            }),
            ElseExpr = N(88)
        });
        var copy = JsonSerializer.Deserialize<Expression>(
            JsonSerializer.Serialize<Expression>(expression, JsonOptions.Default), JsonOptions.Default)!;
        Expression simplified = ExpressionSimplifier.Simplify(copy).Expression;
        Assert.Equal(ExprValue.Number(7), Run(simplified, new Dictionary<string, object?>(), 1));
        Assert.Equal(ExprValue.Number(7), Run(copy, new Dictionary<string, object?>(), -1));
        RecordExpr record = R(("z", N(1)), ("a", N(2)));
        var serialized = JsonSerializer.Serialize<Expression>(record, JsonOptions.Default);
        var restored =
            Assert.IsType<RecordExpr>(JsonSerializer.Deserialize<Expression>(serialized, JsonOptions.Default));
        Assert.Equal(new[] { "z", "a" }, restored.Fields.Select(field => field.Name));
        Assert.Equal(7, ExpressionCost.Compute(Let("x", Add(N(1), N(2)), Add(L("x"), L("x")))));
    }

    private static GraphConfig Graph(Expression expression) => new()
    {
        SchemaVersion = "1.0",
        InitialState = new Dictionary<string, JsonElement>
        {
            ["items"] = JsonSerializer.SerializeToElement(new[] { 2, 3 })
        },
        Nodes =
        [
            new DrawNode
            {
                Id = "draw",
                StateWriteKey = "roll",
                Outputs = Ports(),
                DrawWeights =
                [
                    new DrawWeight
                    {
                        OutcomeId = "lo",
                        Weight = 1,
                        Value = 2,
                        StateValue = N(2)
                    },
                    new DrawWeight
                    {
                        OutcomeId = "hi",
                        Weight = 3,
                        Value = 4,
                        StateValue = N(4)
                    }
                ]
            },
            new ModifyStateNode
            {
                Id = "modify",
                ExpressionId = "value",
                OutputKey = "win",
                Inputs = Ports(),
                Outputs = Ports()
            },
            new MetricsSinkNode
            {
                Id = "sink",
                WinStateKey = "win",
                WinCap = 1000,
                Inputs = Ports()
            }
        ],
        Edges = [Edge("draw", "modify"), Edge("modify", "sink")],
        Expressions = new Dictionary<string, Expression>
        {
            ["value"] = expression
        }
    };

    private static Dictionary<string, Port> Ports() => new()
    {
        ["state"] = new Port
        {
            Name = "state",
            Type = PortType.State
        }
    };

    private static Edge Edge(string source, string target) => new()
    {
        Id = source + target,
        SourceNodeId = source,
        TargetNodeId = target,
        SourcePort = "state",
        TargetPort = "state"
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuredAndCanonicalExactLawsRetainCorrelatedRecordAndOpaqueLocalInputs(bool opaque)
    {
        Expression body = opaque
            ? new AggregateExpr
            {
                Func = AggregateFunc.Sum,
                StateKey = "items",
                ItemName = "item",
                ItemType = ExprType.Number,
                ValueExpr = Add(S("item"), L("offset"))
            }
            : Add(L("offset", "value"), L("offset", "value"));
        LetExpr expression = Let("offset", opaque ? S("roll") : R(("value", S("roll"))), body);
        GraphConfig source = Graph(expression);
        var json = JsonSerializer.Serialize(source, JsonOptions.Default);
        CompileResult compiled = new GraphCompiler().Compile(source);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Dist<BigInteger> reference = ExactInterpreter
            .Evaluate(compiled.ReferenceProgram!, new Dictionary<string, object?>(), StateHasher.CanonicalHash)
            .ValueDistribution();
        StructuredExactResult structured = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        Assert.NotNull(structured.Distribution);
        var expected = opaque ? new[] { 9, 13 } : new[] { 4, 8 };
        foreach (Dist<BigInteger>? law in new[] { reference, structured.Distribution })
        {
            Assert.Equal(2, law.Count);
            foreach (Dist<BigInteger>.Entry entry in law.Entries)
            {
                Assert.Equal(entry.Value == expected[0] ? new Rational(1, 4) : new Rational(3, 4),
                    new Rational(entry.Numerator, law.Denominator));
            }

            Assert.Equal(expected.Select(value => new BigInteger(value)).Order(),
                law.Entries.Select(entry => entry.Value).Order());
        }

        CompileResult baseline = new GraphCompiler(optimizeSampling: false).Compile(source);
        Assert.True(baseline.IsValid);
        for (var seed = 0; seed < 32; seed++)
        {
            (BigInteger Value, Dictionary<string, object?> State) expectedRound =
                SampledInterpreter.RunSingle(baseline.Program!, new Dictionary<string, object?>(), seed);
            (BigInteger Value, Dictionary<string, object?> State) actualRound =
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed);
            Assert.Equal(expectedRound.Value, actualRound.Value);
            Assert.Equal(JsonSerializer.Serialize(expectedRound.State.OrderBy(pair => pair.Key), JsonOptions.Default),
                JsonSerializer.Serialize(actualRound.State.OrderBy(pair => pair.Key), JsonOptions.Default));
            Assert.DoesNotContain("offset", actualRound.State.Keys);
        }

        Assert.Equal(json, JsonSerializer.Serialize(source, JsonOptions.Default));
    }

    [Fact]
    public void ReachableUnusedInitializerErrorsRemainExactAnalysisObligations()
    {
        GraphConfig source = Graph(Let("unused", new BinaryExpr
        {
            Op = BinaryOp.Div,
            Left = N(1),
            Right = new BinaryExpr
            {
                Op = BinaryOp.Sub,
                Left = S("roll"),
                Right = N(2)
            }
        }, N(5)));
        CompileResult compiled = new GraphCompiler().Compile(source);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        StructuredExactResult structured = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.False(structured.Succeeded);
        Assert.Contains("fail", structured.DeclinedReason);
    }

    [Fact]
    public void DerivedStateSchemaRetainsARecordProducedThroughLet()
    {
        GraphConfig source = Graph(Let("r", R(("score", Add(S("roll"), N(10)))), L("r")));
        source.Nodes[1] = (ModifyStateNode)source.Nodes[1] with
        {
            OutputKey = "result"
        };
        source = source with
        {
            Nodes =
            [
                source.Nodes[0], source.Nodes[1], new ModifyStateNode
                {
                    Id = "project",
                    ExpressionId = "pay",
                    OutputKey = "win",
                    Inputs = Ports(),
                    Outputs = Ports()
                },
                source.Nodes[2]
            ],
            Edges = [Edge("draw", "modify"), Edge("modify", "project"), Edge("project", "sink")]
        };
        source.Expressions!["pay"] = S("result", "score");
        CompileResult compiled = new GraphCompiler().Compile(source);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Assert.Equal("compiled-state-plan-v1", compiled.SamplingEngine);
        StructuredExactResult exact = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(exact.Succeeded, exact.DeclinedReason);
        Assert.Equal(new Rational(27, 2), exact.ExpectedWin);
    }

    [Fact]
    public void SeededNestedProgramsMatchAnIndependentIntegerOracle()
    {
        var random = new System.Random(20261023);
        var shapes = new SamplingShapes();
        for (var sample = 0; sample < 64; sample++)
        {
            var a = random.Next(-1000, 1000);
            var b = random.Next(-1000, 1000);
            LetExpr doubled = Let("x", Add(L("x"), N(b)), Add(L("x"), L("x")));
            LetExpr expression = Let("x", N(a), Let("r", R(("x", doubled)),
                new BinaryExpr
                {
                    Op = BinaryOp.Sub,
                    Left = L("r", "x"),
                    Right = L("x")
                }));
            ExprValue expected = ExprValue.Number(a + 2 * b);
            Assert.Equal(expected, Run(expression, new Dictionary<string, object?>(), -1));
            Assert.Equal(expected, Run(expression, new Dictionary<string, object?>(), 1, shapes));
            Assert.Equal(expected,
                Run(ExpressionSimplifier.Simplify(expression).Expression, new Dictionary<string, object?>(), 1,
                    shapes));
        }
    }

    [Fact]
    public void OpaqueInterningKeepsExpressionIdentityAndBothNamespaces()
    {
        var factory = new TermFactory(new WorkBudget(10000, null, default));
        ParamTerm input = factory.NewParam("input", V.Num, null, false);
        Expression stateRead = S("x");
        Term first = factory.Opaque(stateRead, ["x"], [], [input]);
        Assert.Same(first, factory.Opaque(stateRead, ["x"], [], [input]));
        Expression localRead = L("x");
        Term lexical = factory.Opaque(localRead, [], ["x"], [input]);
        Assert.Same(lexical, factory.Opaque(localRead, [], ["x"], [input]));
        Assert.NotSame(lexical, factory.Opaque(localRead, [], ["y"], [input]));
        Assert.NotSame(lexical, factory.Opaque(localRead, ["x"], [], [input]));
        Assert.Equal(ExprValue.Number(12),
            TermFactory.Apply(Op.Opaque, 1, Add(S("x"), L("x")), ["x", "x"],
                [ExprValue.Number(3), ExprValue.Number(9)]));
    }

    public sealed class Probe
    {
        public int Calls { get; private set; }

        public int Value
        {
            get
            {
                Calls++;
                return 7;
            }
        }
    }
}
