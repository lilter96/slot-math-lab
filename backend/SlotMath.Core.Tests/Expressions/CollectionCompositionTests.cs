using System.Collections;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Text.Json;
using Json.Schema;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;
using SlotMath.Core.Optimization;

namespace SlotMath.Core.Tests.Expressions;

[Collection("SamplingOptions")]
public sealed class CollectionCompositionTests
{
    private static ConstantExpr N(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString()
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

    private static MapExpr Map(Expression source, Expression body, int bound = 4, ExprType type = ExprType.Number) =>
        new()
        {
            Source = source,
            MaxItems = bound,
            ItemName = "x",
            ItemType = type,
            Body = body
        };

    private static FoldExpr Sum(Expression source, int bound = 4, string item = "x") => new()
    {
        Source = source,
        MaxItems = bound,
        ItemName = item,
        ItemType = ExprType.Number,
        AccName = "acc",
        Init = N(0),
        Body = Add(S("acc"), S(item))
    };

    private static AggregateExpr Aggregate(Expression source, AggregateFunc kind = AggregateFunc.Sum, int bound = 4) =>
        new()
        {
            Source = source,
            MaxItems = bound,
            Func = kind,
            ItemName = "x",
            ItemType = ExprType.Number,
            ValueExpr = S("x")
        };

    private static Dictionary<string, object?> State() => new()
    {
        ["nums"] = new[] { 1, 2, 3 },
        ["empty"] = Array.Empty<int>(),
        ["x"] = "outer"
    };

    private static TypeCheckContext Context() => new()
    {
        StateFields =
        [
            new FieldDescriptor
            {
                Name = "nums",
                Type = ExprType.Array,
                ArrayItemType = ExprType.Number
            },
            new FieldDescriptor
            {
                Name = "empty",
                Type = ExprType.Array,
                ArrayItemType = ExprType.Number
            },
            new FieldDescriptor
            {
                Name = "x",
                Type = ExprType.String
            },
            new FieldDescriptor
            {
                Name = "roll",
                Type = ExprType.Number
            },
            new FieldDescriptor
            {
                Name = "matrix",
                Type = ExprType.Array,
                ArrayItem = new FieldDescriptor
                {
                    Name = "row",
                    Type = ExprType.Array,
                    ArrayItemType = ExprType.Number
                }
            }
        ]
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

        Dictionary<string, int> slots = state.Keys.Select((name, index) => (name, index))
            .ToDictionary(pair => pair.name, pair => pair.index);

        int Slot(string name)
        {
            if (!slots.TryGetValue(name, out var index))
            {
                slots[name] = index = slots.Count;
            }

            return index;
        }

        SamplingTier tier = mode == 0 ? SamplingTier.Interpreted :
            mode == 1 ? SamplingTier.Compiled : SamplingTier.Tiered;
        Func<SamplingFrame, ExprValue>
            program = new SamplingExpressions(Slot, tier, shapes: shapes).Compile(expression);
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], state);
        frame.Reset(default);
        ExprValue result = program(frame);
        if (mode == 2)
        {
            for (var call = 1; call < 80; call++)
            {
                Assert.Equal(result, program(frame));
            }
        }

        Assert.Null(frame.Locals);
        foreach (var (name, initial) in state)
        {
            if (name == "x")
            {
                Assert.Equal(initial, frame.Cells[slots[name]].Export());
            }
        }

        return result;
    }

    private static Expression Pipeline() => Sum(new FilterExpr
    {
        Source = Map(S("nums"), Add(S("x"), N(1))),
        MaxItems = 4,
        ItemName = "x",
        ItemType = ExprType.Number,
        Predicate = new CompareExpr
        {
            Op = CompareOp.Gt,
            Left = S("x"),
            Right = N(2)
        }
    });

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ExpressionSourcesComposeWithoutIntermediateState(int mode)
    {
        Expression expression = Pipeline();
        Assert.Empty(ExpressionTypeChecker.Check(expression, Context()));
        Dictionary<string, object?> state = State();
        Assert.Equal(ExprValue.Number(7), Run(expression, state, mode));
        Assert.Equal(new[] { "empty", "nums", "x" }, state.Keys.Order());
        Assert.Equal("outer", state["x"]);
        Assert.Equal(ExprValue.Number(7), Run(ExpressionSimplifier.Simplify(expression).Expression, state, mode));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NestedSourcesReadOuterBindingsBeforeInnerShadowing(int mode)
    {
        MapExpr expression = Map(S("matrix"), Sum(S("x"), 2), 2, ExprType.Array);
        Assert.Empty(ExpressionTypeChecker.Check(expression, Context()));
        Dictionary<string, object?> state = State();
        state["matrix"] = new[] { new[] { 1, 2 }, new[] { 3, 4 } };
        Assert.Equal(new[] { ExprValue.Number(3), ExprValue.Number(7) }, Run(expression, state, mode).ArrayValue);
        Assert.Equal("outer", state["x"]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RecordShapesAndLexicalArraySourcesSurviveComposition(int mode)
    {
        MapExpr records = Map(S("nums"), new RecordExpr
        {
            Fields =
            [
                new RecordFieldExpr
                {
                    Name = "n",
                    Value = S("x")
                },
                new RecordFieldExpr
                {
                    Name = "pair",
                    Value = new TupleExpr
                    {
                        Items = [S("x"), Add(S("x"), N(1))]
                    }
                }
            ]
        });
        var expression = new LetExpr
        {
            Name = "rows",
            Value = records,
            Body = Sum(Map(new LocalExpr
            {
                Name = "rows"
            }, Add(S("x", "n"), S("x", "pair", "1")), type: ExprType.Record))
        };
        Assert.Empty(ExpressionTypeChecker.Check(expression, Context()));
        Assert.Equal(ExprValue.Number(15), Run(expression, State(), mode));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EmptySourcesPreserveIdentitiesAndDoNotEvaluateBodies(int mode)
    {
        var fail = new BinaryExpr
        {
            Op = BinaryOp.Div,
            Left = N(1),
            Right = N(0)
        };
        Assert.Empty(Run(Map(S("empty"), fail, 0), State(), mode).ArrayValue!);
        Assert.Equal(ExprValue.Number(13), Run(Sum(S("empty"), 0) with
        {
            Init = N(13),
            Body = fail
        }, State(), mode));
        Assert.Equal(ExprValue.Number(0), Run(Aggregate(S("empty"), AggregateFunc.Count, 0) with
        {
            ValueExpr = fail
        }, State(), mode));
        Assert.Equal(ExprValue.Number(1), Run(Aggregate(S("empty"), AggregateFunc.Product, 0), State(), mode));
        var error = Assert.Throws<ExpressionEvaluationException>(() =>
            Run(Aggregate(S("empty"), AggregateFunc.Min, 0), State(), mode));
        Assert.Equal(EvalErrorCodes.EmptyMinMax, error.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SourceEvaluatesOnceAndItsBoundPrecedesBodyEvaluation(int mode)
    {
        var probe = new Probe();
        Dictionary<string, object?> state = State();
        state["probe"] = probe;
        Assert.Equal(ExprValue.Number(6), Run(Sum(S("probe", "Items"), 3), state, mode));
        Assert.Equal(mode == 2 ? 80 : 1, probe.Calls);
        var error = Assert.Throws<ExpressionEvaluationException>(() =>
            Run(Map(S("probe", "Items"), S("bodyFailure"), 2), state, mode));
        Assert.Equal("EVAL_COLLECTION_BUDGET", error.Code);
        Assert.Equal("map.source", error.Location);
        error = Assert.Throws<ExpressionEvaluationException>(() => Run(Sum(S("sourceFailure")) with
        {
            Init = S("initFailure")
        }, state, mode));
        Assert.Equal("initFailure", error.Location);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void WholeSourcePassFinishesBeforeTheOuterBodyStarts(int mode)
    {
        MapExpr source = Map(S("nums"), new IfExpr
        {
            Condition = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = S("x"),
                Right = N(2)
            },
            ThenExpr = S("sourceFailure"),
            ElseExpr = S("x")
        });
        var error = Assert.Throws<ExpressionEvaluationException>(() =>
            Run(Map(source, S("outerFailure")), State(), mode));
        Assert.Equal("sourceFailure", error.Location);
        Assert.Equal("outer", State()["x"]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AggregateDefaultNumericTextConventionMatchesStateArrays(int mode)
    {
        Dictionary<string, object?> state = State();
        state["texts"] = new[] { "2", "3", "label" };
        AggregateExpr expression = Aggregate(S("texts")) with
        {
            ValueExpr = null,
            ItemType = ExprType.String
        };
        Assert.Equal(ExprValue.Number(5), Run(expression, state, mode));
    }

    [Theory]
    [InlineData("map")]
    [InlineData("filter")]
    [InlineData("fold")]
    [InlineData("aggregate")]
    public void CardinalityLimitsApplyToEveryCollectionForm(string kind)
    {
        Expression expression = Collection(kind, S("nums"), 2);
        for (var mode = -1; mode <= 2; mode++)
        {
            var error = Assert.Throws<ExpressionEvaluationException>(() => Run(expression, State(), mode));
            Assert.Equal("EVAL_COLLECTION_BUDGET", error.Code);
            Assert.Equal(kind + ".source", error.Location);
        }
    }

    private static Expression Collection(string kind, Expression source, int? bound) => kind switch
    {
        "map" => Map(source, S("x")) with
        {
            MaxItems = bound
        },
        "filter" => new FilterExpr
        {
            Source = source,
            MaxItems = bound,
            ItemName = "x",
            ItemType = ExprType.Number,
            Predicate = new ConstantExpr
            {
                Kind = ConstantKind.Boolean,
                Value = "true"
            }
        },
        "fold" => Sum(source) with
        {
            MaxItems = bound
        },
        _ => Aggregate(source) with
        {
            MaxItems = bound
        }
    };

    [Theory]
    [InlineData("map")]
    [InlineData("filter")]
    [InlineData("fold")]
    [InlineData("aggregate")]
    public void TypeCheckingRejectsMissingBoundsNonarraysAndHiddenUnboundedWork(string kind)
    {
        TypeCheckContext context = Context();
        Assert.Empty(ExpressionTypeChecker.Check(Collection(kind, S("nums"), 3), context));
        foreach (Expression expression in new[]
                 {
                     Collection(kind, S("nums"), null), Collection(kind, S("nums"), -1),
                     Collection(kind, S("nums"), 4097), Collection(kind, N(1), 3), Collection(kind, new MapExpr
                     {
                         StateKey = "nums",
                         ItemName = "x",
                         ItemType = ExprType.Number,
                         Body = S("x")
                     }, 3)
                 })
        {
            Assert.NotEmpty(ExpressionTypeChecker.Check(expression, context));
        }

        Assert.NotEmpty(ExpressionTypeChecker.Check(Map(S("nums"), S("x")) with
        {
            StateKey = "nums"
        }, context));
        Assert.NotEmpty(ExpressionTypeChecker.Check(Map(S("nums"), S("x")) with
        {
            Source = null
        }, context));
        Assert.NotEmpty(ExpressionTypeChecker.Check(Sum(S("nums")) with
        {
            AccName = "x"
        }, context));
        Assert.NotEmpty(ExpressionTypeChecker.Check(Map(S("nums"), new LetExpr
        {
            Name = "hide",
            Value = N(0),
            Body = new MapExpr
            {
                StateKey = "nums",
                ItemName = "y",
                ItemType = ExprType.Number,
                Body = S("y")
            }
        }), context));
    }

    [Fact]
    public void CostAccountsForBoundsAndSaturatesInsteadOfWrapping()
    {
        Assert.Equal(10, ExpressionCost.Compute(Map(S("nums"), Add(S("x"), N(1)), 2)));
        Assert.Equal(20, ExpressionCost.Compute(Map(S("nums"), Map(S("nums"), N(0), 3), 2)));
        Expression large = N(1);
        for (var depth = 0; depth < 12; depth++)
        {
            large = Map(S("nums"), large, 4096);
        }

        Assert.Equal(long.MaxValue, ExpressionCost.Compute(large));
        Assert.False(ExpressionCost.WithinBudget(large, out _));
        Expression deep = N(1);
        for (var depth = 0; depth < 200; depth++)
        {
            deep = Add(deep, N(1));
        }

        Assert.Equal(long.MaxValue, ExpressionCost.Compute(deep));
        CompileResult rejected = new GraphCompiler().Compile(Graph(Map(S("nums"), Map(S("nums"), N(0), 128), 128)));
        Assert.Contains(rejected.Errors, error => error.Code == ErrorCodes.ExpressionBudgetExceeded);
    }

    [Fact]
    public void CompiledShapeIncludesSourceAndBoundOperands()
    {
        var shapes = new SamplingShapes();
        Assert.Equal(ExprValue.Number(6), Run(Sum(S("nums"), 3), State(), 1, shapes));
        var error = Assert.Throws<ExpressionEvaluationException>(() => Run(Sum(S("nums"), 2), State(), 1, shapes));
        Assert.Equal("EVAL_COLLECTION_BUDGET", error.Code);
        Assert.Equal(ExprValue.Number(0), Run(Sum(S("empty"), 2), State(), 1, shapes));
    }

    [Fact]
    public void BoundedStateKeyKeepsTheLegacySerializedShapeWhenBoundsAreOmitted()
    {
        Expression legacy = new MapExpr
        {
            StateKey = "nums",
            ItemName = "x",
            ItemType = ExprType.Number,
            Body = S("x")
        };
        var json = JsonSerializer.Serialize(legacy, JsonOptions.Default);
        Assert.DoesNotContain("source", json);
        Assert.DoesNotContain("maxItems", json);
        MapExpr bounded = (MapExpr)legacy with
        {
            MaxItems = 2
        };
        var error = Assert.Throws<ExpressionEvaluationException>(() => Run(bounded, State(), 1));
        Assert.Equal("EVAL_COLLECTION_BUDGET", error.Code);
        var sourceJson = JsonSerializer.Serialize(Pipeline(), JsonOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<Expression>(sourceJson, JsonOptions.Default)!;
        Assert.Equal(ExprValue.Number(7), Run(roundTrip, State(), 1));
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0.0",
            expressions = new
            {
                pipeline = roundTrip
            }
        }, JsonOptions.Default));
        Assert.True(JsonSchema.FromText(SchemaEmitter.GetSchemaJson()).Evaluate(document.RootElement).IsValid);
    }

    [Fact]
    public void StreamingStateBoundsReadOnlyTheRequiredPrefixDuringExecution()
    {
        var source = new CountingStream(1000000);
        var expression = new MapExpr
        {
            StateKey = "stream",
            MaxItems = 3,
            ItemName = "x",
            ItemType = ExprType.Number,
            Body = S("bodyFailure")
        };
        var error = Assert.Throws<ExpressionEvaluationException>(() => ExactExpressionEvaluator.Evaluate(expression,
            new EvalContext
            {
                State = new Dictionary<string, object?>
                {
                    ["stream"] = source
                }
            }));
        Assert.Equal("EVAL_COLLECTION_BUDGET", error.Code);
        Assert.Equal(4, source.Reads);
        foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
        {
            var slots = new Dictionary<string, int>
            {
                ["stream"] = 0,
                ["x"] = 1,
                ["bodyFailure"] = 2
            };
            Func<SamplingFrame, ExprValue> program =
                new SamplingExpressions(name => slots[name], tier).Compile(expression);
            var frame = new SamplingFrame(slots, new SamplingCell[3], new Dictionary<string, object?>());
            frame.Reset(default);
            source = new CountingStream(1000000);
            frame.Cells[0] = new SamplingCell
            {
                Present = true,
                HasRaw = true,
                Raw = source
            };
            Assert.Equal("EVAL_COLLECTION_BUDGET",
                Assert.Throws<ExpressionEvaluationException>(() => program(frame)).Code);
            Assert.Equal(4, source.Reads);
        }
    }

    private static Dictionary<string, Port> Ports() => new()
    {
        ["state"] = new Port
        {
            Name = "state",
            Type = PortType.State
        }
    };

    private static Edge Edge(string from, string to) => new()
    {
        Id = from + to,
        SourceNodeId = from,
        TargetNodeId = to,
        SourcePort = "state",
        TargetPort = "state"
    };

    private static GraphConfig Graph(Expression expression) => new()
    {
        SchemaVersion = "1.0.0",
        InitialState = new Dictionary<string, JsonElement>
        {
            ["nums"] = JsonSerializer.SerializeToElement(new[] { 1, 2, 3 })
        },
        Nodes =
        [
            new DrawNode
            {
                Id = "roll",
                StateWriteKey = "roll",
                Outputs = Ports(),
                DrawWeights =
                [
                    new DrawWeight
                    {
                        OutcomeId = "a",
                        Weight = 1,
                        Value = 1,
                        StateValue = N(1)
                    },
                    new DrawWeight
                    {
                        OutcomeId = "b",
                        Weight = 3,
                        Value = 3,
                        StateValue = N(3)
                    }
                ]
            },
            new ModifyStateNode
            {
                Id = "calc",
                OutputKey = "win",
                ExpressionId = "value",
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
        Edges = [Edge("roll", "calc"), Edge("calc", "sink")],
        Expressions = new Dictionary<string, Expression>
        {
            ["value"] = expression
        }
    };

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Compiled)]
    [InlineData(SamplingTier.Tiered)]
    public void CompleteRoundsAndExactLawsMatchAnIndependentScalarExpression(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            var product = new BinaryExpr
            {
                Op = BinaryOp.Mul,
                Left = S("x"),
                Right = S("roll")
            };
            GraphConfig source = Graph(Aggregate(Map(S("nums"), product)));
            GraphConfig scalar = Graph(new AggregateExpr
            {
                StateKey = "nums",
                Func = AggregateFunc.Sum,
                ItemName = "x",
                ItemType = ExprType.Number,
                ValueExpr = product
            });
            var before = JsonSerializer.Serialize(source, JsonOptions.Default);
            CompileResult compiled = new GraphCompiler().Compile(source);
            CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(scalar);
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            Assert.True(reference.IsValid);
            Assert.Equal("compiled-state-plan-v1", compiled.SamplingEngine);
            for (var seed = 0; seed < 128; seed++)
            {
                (BigInteger Value, Dictionary<string, object?> State) expected =
                    SampledInterpreter.RunSingle(reference.Program!, new Dictionary<string, object?>(), seed);
                (BigInteger Value, Dictionary<string, object?> State) actual =
                    SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), seed);
                Assert.Equal(expected.Value, actual.Value);
                Assert.Equal(JsonSerializer.Serialize(expected.State.OrderBy(pair => pair.Key), JsonOptions.Default),
                    JsonSerializer.Serialize(actual.State.OrderBy(pair => pair.Key), JsonOptions.Default));
            }

            StructuredExactResult structured =
                StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
            Assert.True(structured.Succeeded, structured.DeclinedReason);
            Dist<BigInteger> classic = ExactInterpreter
                .Evaluate(compiled.ReferenceProgram!, new Dictionary<string, object?>(), StateHasher.CanonicalHash)
                .ValueDistribution();
            Assert.Equal(new Rational(15, 1), structured.ExpectedWin);
            foreach (Dist<BigInteger>? law in new[] { classic, structured.Distribution! })
            {
                Assert.Equal(2, law.Count);
                foreach (Dist<BigInteger>.Entry entry in law.Entries)
                {
                    Assert.Equal(entry.Value == 6 ? new Rational(1, 4) : new Rational(3, 4),
                        new Rational(entry.Numerator, law.Denominator));
                }
            }

            Assert.Equal(before, JsonSerializer.Serialize(source, JsonOptions.Default));
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void ReachableSourceBudgetErrorsPreventAFalseExactResult()
    {
        var source = new IfExpr
        {
            Condition = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = S("roll"),
                Right = N(1)
            },
            ThenExpr = S("nums"),
            ElseExpr = S("empty")
        };
        GraphConfig graph = Graph(Sum(source, 2));
        graph.InitialState!["empty"] = JsonSerializer.SerializeToElement(Array.Empty<int>());
        CompileResult compiled = new GraphCompiler().Compile(graph);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        StructuredExactResult exact = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.False(exact.Succeeded);
        Assert.Contains("fail", exact.DeclinedReason);
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Compiled)]
    [InlineData(SamplingTier.Tiered)]
    public void NestedFailuresRestoreBindingsBeforeAnotherExpression(SamplingTier tier)
    {
        var slots = new Dictionary<string, int>
        {
            ["nums"] = 0,
            ["x"] = 1,
            ["missing"] = 2,
            ["v"] = 3
        };
        var owner = new SamplingExpressions(name => slots[name], tier);
        AggregateExpr failingBody = Aggregate(S("nums")) with
        {
            ItemName = "v",
            ValueExpr = Add(S("x"), S("missing"))
        };
        Func<SamplingFrame, ExprValue> failing = owner.Compile(Map(Map(S("nums"), failingBody), N(1)));
        Func<SamplingFrame, ExprValue> outer = owner.Compile(S("x"));
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], new Dictionary<string, object?>
        {
            ["nums"] = new[] { 1, 2, 3 },
            ["x"] = "outer"
        });
        frame.Reset(default);
        for (var attempt = 0; attempt < 80; attempt++)
        {
            Assert.Equal("missing", Assert.Throws<ExpressionEvaluationException>(() => failing(frame)).Location);
            Assert.Equal(ExprValue.String("outer"), outer(frame));
            Assert.False(frame.Cells[slots["v"]].Present);
        }
    }

    [Fact]
    public void StructuredNestedFoldAndMapRetainSharedRandomDependencies()
    {
        FoldExpr row = Sum(Map(S("x"), new BinaryExpr
        {
            Op = BinaryOp.Mul,
            Left = S("x"),
            Right = S("roll")
        }, 2), 2);
        FoldExpr expression = Sum(Map(S("matrix"), row, 2, ExprType.Array), 2);
        GraphConfig graph = Graph(expression);
        graph.InitialState!["matrix"] = JsonSerializer.SerializeToElement(new[] { new[] { 1, 2 }, new[] { 3, 4 } });
        CompileResult compiled = new GraphCompiler().Compile(graph);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        StructuredExactResult structured = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(structured.Succeeded, structured.DeclinedReason);
        Assert.Equal(new Rational(25, 1), structured.ExpectedWin);
        Dist<BigInteger> reference = ExactInterpreter
            .Evaluate(compiled.ReferenceProgram!, new Dictionary<string, object?>(), StateHasher.CanonicalHash)
            .ValueDistribution();
        foreach (Dist<BigInteger>? law in new[] { reference, structured.Distribution! })
        {
            Assert.Equal(2, law.Count);
            foreach (Dist<BigInteger>.Entry entry in law.Entries)
            {
                Assert.Contains(entry.Value, new BigInteger[] { 10, 30 });
                Assert.Equal(entry.Value == 10 ? new Rational(1, 4) : new Rational(3, 4),
                    new Rational(entry.Numerator, law.Denominator));
            }
        }
    }

    [Fact]
    public void ReusableSubgraphNamespacesReferencesInsideCollectionSources()
    {
        GraphConfig graph = Graph(N(0));
        var node = (ModifyStateNode)graph.Nodes[1];
        graph.Nodes[1] = new LibraryNode
        {
            Id = "calc",
            MechanicName = "reusable",
            Inputs = Ports(),
            Outputs = Ports()
        };
        graph = graph with
        {
            Mechanics = new Dictionary<string, CustomMechanic>
            {
                ["reusable"] = new()
                {
                    Name = "General array composition",
                    Nodes =
                    [
                        node with
                        {
                            Id = "inside",
                            ExpressionId = "score"
                        }
                    ],
                    Expressions = new Dictionary<string, Expression>
                    {
                        ["values"] = Map(S("nums"), Add(S("x"), S("roll"))),
                        ["score"] = Sum(S("expressions", "values"))
                    }
                }
            }
        };
        var json = JsonSerializer.Serialize(graph, JsonOptions.Default);
        var copy = JsonSerializer.Deserialize<GraphConfig>(json, JsonOptions.Default)!;
        CompileResult compiled = new GraphCompiler().Compile(copy);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        StructuredExactResult exact = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(exact.Succeeded, exact.DeclinedReason);
        Assert.Equal(new Rational(27, 2), exact.ExpectedWin);
        Assert.Equal(json, JsonSerializer.Serialize(copy, JsonOptions.Default));
    }

    [Fact]
    public void PreparedConstantSourcesRespectRunnerOverridesAndBounds()
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = SamplingTier.Compiled;
            AggregateExpr expression = Aggregate(Map(S("nums"), new BinaryExpr
            {
                Op = BinaryOp.Mul,
                Left = S("x"),
                Right = S("roll")
            }));
            GraphConfig graph = Graph(expression);
            CompileResult compiled = new GraphCompiler().Compile(graph);
            CompileResult baseline = new GraphCompiler(optimizeSampling: false).Compile(graph);
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            for (var seed = 0; seed < 32; seed++)
            {
                foreach (var values in new[] { new[] { 1, 2, 3 }, new[] { 10, 20 } })
                {
                    var input = new Dictionary<string, object?>
                    {
                        ["nums"] = values
                    };
                    (BigInteger Value, Dictionary<string, object?> State) expected =
                        SampledInterpreter.RunSingle(baseline.Program!, input, seed);
                    (BigInteger Value, Dictionary<string, object?> State) actual =
                        SampledInterpreter.RunSingle(compiled.Program!, input, seed);
                    Assert.Equal(expected.Value, actual.Value);
                    Assert.Equal(values.Sum() * (BigInteger)actual.State["roll"]!, actual.Value);
                }
            }

            var oversized = new Dictionary<string, object?>
            {
                ["nums"] = new[] { 1, 2, 3, 4, 5 }
            };
            Assert.Equal("EVAL_COLLECTION_BUDGET",
                Assert.Throws<ExpressionEvaluationException>(() =>
                    SampledInterpreter.RunSingle(compiled.Program!, oversized, 0)).Code);
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void IndependentBatchAggregationPreservesFactorizationBeyondJointEnumeration()
    {
        AggregateExpr expression = Aggregate(Map(S("nums"), new BinaryExpr
        {
            Op = BinaryOp.Mul,
            Left = S("x"),
            Right = N(2)
        }, 64), bound: 64);
        GraphConfig graph = Graph(expression);
        var original = (DrawNode)graph.Nodes[0];
        graph.Nodes[0] = new DrawManyNode
        {
            Id = "roll",
            DistributionId = "data",
            OutputKey = "nums",
            Count = 64,
            Outputs = Ports()
        };
        graph = graph with
        {
            Distributions = new Dictionary<string, DistributionAsset>
            {
                ["data"] = new()
                {
                    Outcomes = original.DrawWeights!
                }
            }
        };
        CompileResult compiled = new GraphCompiler().Compile(graph);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions
        {
            MaxWork = 1000000,
            DistributionWork = 1000000
        });
        Assert.True(result.Succeeded, result.DeclinedReason);
        Assert.Equal(new Rational(320, 1), result.ExpectedWin);
        Assert.Equal(new Rational(102592, 1), result.SecondMoment);
        Assert.Equal(new Rational(192, 1), result.Variance(1));
        Assert.NotNull(result.Distribution);
        Assert.Equal(65, result.Distribution.Count);
        BigInteger denominator = BigInteger.Pow(4, 64);
        BigInteger combination = 1;
        var expected = new Dictionary<BigInteger, Rational>();
        for (var highs = 0; highs <= 64; highs++)
        {
            expected[128 + 4 * highs] = new Rational(combination * BigInteger.Pow(3, highs), denominator);
            if (highs < 64)
            {
                combination = combination * (64 - highs) / (highs + 1);
            }
        }

        foreach (Dist<BigInteger>.Entry entry in result.Distribution.Entries)
        {
            Assert.Equal(expected[entry.Value], new Rational(entry.Numerator, result.Distribution.Denominator));
        }
    }

    [Theory]
    [InlineData(AggregateFunc.Sum, 9, 15)]
    [InlineData(AggregateFunc.Product, 24, 120)]
    [InlineData(AggregateFunc.Count, 1, 3)]
    [InlineData(AggregateFunc.Min, 2, 4)]
    [InlineData(AggregateFunc.Max, 4, 6)]
    public void StructuralAggregatesMatchIndependentSmallLaws(AggregateFunc kind, int low, int high)
    {
        AggregateExpr expression = Aggregate(S("nums"), kind) with
        {
            ValueExpr = Add(S("x"), S("roll")),
            Predicate = kind == AggregateFunc.Count
                ? new CompareExpr
                {
                    Op = CompareOp.Lte,
                    Left = S("x"),
                    Right = S("roll")
                }
                : null
        };
        CompileResult compiled = new GraphCompiler().Compile(Graph(expression));
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        StructuredExactResult exact = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        Assert.True(exact.Succeeded, exact.DeclinedReason);
        Dist<BigInteger> classic = ExactInterpreter
            .Evaluate(compiled.ReferenceProgram!, new Dictionary<string, object?>(), StateHasher.CanonicalHash)
            .ValueDistribution();
        foreach (Dist<BigInteger>? law in new[] { classic, exact.Distribution! })
        {
            Assert.Equal(2, law.Count);
            Assert.Equal(new[] { new BigInteger(low), new BigInteger(high) },
                law.Entries.Select(entry => entry.Value).Order());
            foreach (Dist<BigInteger>.Entry entry in law.Entries)
            {
                Assert.Equal(entry.Value == low ? new Rational(1, 4) : new Rational(3, 4),
                    new Rational(entry.Numerator, law.Denominator));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CountSelectorErrorsStayStrictWhilePredicateFalseSkipsThem(bool guarded)
    {
        var selector = new BinaryExpr
        {
            Op = BinaryOp.Div,
            Left = N(1),
            Right = new BinaryExpr
            {
                Op = BinaryOp.Sub,
                Left = S("roll"),
                Right = N(1)
            }
        };
        AggregateExpr expression = Aggregate(S("nums"), AggregateFunc.Count) with
        {
            ValueExpr = selector,
            Predicate = guarded
                ? new CompareExpr
                {
                    Op = CompareOp.Gt,
                    Left = S("roll"),
                    Right = N(1)
                }
                : null
        };
        CompileResult compiled = new GraphCompiler().Compile(Graph(expression));
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        StructuredExactResult exact = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions());
        if (!guarded)
        {
            Assert.False(exact.Succeeded);
            Assert.Contains("fail", exact.DeclinedReason);
            return;
        }

        Assert.True(exact.Succeeded, exact.DeclinedReason);
        Assert.Equal(new Rational(9, 4), exact.ExpectedWin);
        Dist<BigInteger> classic = ExactInterpreter
            .Evaluate(compiled.ReferenceProgram!, new Dictionary<string, object?>(), StateHasher.CanonicalHash)
            .ValueDistribution();
        Assert.Equal(classic.ExpectedBigIntegerValue(), exact.Distribution!.ExpectedBigIntegerValue());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoopLivenessRetainsFieldsReadOnlyInsideCollectionSources(bool chain)
    {
        var source = new IfExpr
        {
            Condition = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = S("mode"),
                Right = N(0)
            },
            ThenExpr = S("left"),
            ElseExpr = S("right")
        };
        GraphConfig graph = Graph(Sum(source, 2));
        var draw = (DrawNode)graph.Nodes[0];
        graph.Nodes[0] = draw with
        {
            StateWriteKey = "mode",
            DrawWeights =
            [
                draw.DrawWeights![0] with
                {
                    StateValue = N(0)
                },
                draw.DrawWeights[1] with
                {
                    StateValue = N(1)
                }
            ]
        };
        graph.InitialState!["left"] = JsonSerializer.SerializeToElement(new[] { 1 });
        graph.InitialState["right"] = JsonSerializer.SerializeToElement(new[] { 2, 3 });
        graph.InitialState["total"] = JsonSerializer.SerializeToElement(0);
        graph.Expressions!["add"] = Add(S("total"), S("step"));
        graph = graph with
        {
            Nodes =
            [
                graph.Nodes[0], new LoopNode
                {
                    Id = "repeat",
                    MaxIterations = 2,
                    Inputs = Ports(),
                    Outputs = new Dictionary<string, Port>
                    {
                        ["body"] = new()
                        {
                            Name = "body",
                            Type = PortType.State
                        },
                        ["exit"] = new()
                        {
                            Name = "exit",
                            Type = PortType.State
                        }
                    }
                },
                (ModifyStateNode)graph.Nodes[1] with
                {
                    OutputKey = "step"
                },
                new ModifyStateNode
                {
                    Id = "add",
                    ExpressionId = "add",
                    OutputKey = "total",
                    Inputs = Ports(),
                    Outputs = Ports()
                },
                (MetricsSinkNode)graph.Nodes[2] with
                {
                    WinStateKey = "total"
                }
            ],
            Edges =
            [
                Edge("roll", "repeat"), Edge("repeat", "calc") with
                {
                    SourcePort = "body"
                },
                Edge("calc", "add"), Edge("repeat", "sink") with
                {
                    SourcePort = "exit"
                }
            ]
        };
        CompileResult compiled = new GraphCompiler().Compile(graph);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        StructuredExactResult exact = StructuredExactEvaluator.Evaluate(compiled, new StructuredExactOptions
        {
            ChainLoopsAbove = chain ? 0 : 32,
            PruneLoopMassBelow = null
        });
        Assert.True(exact.Succeeded, exact.DeclinedReason);
        Assert.Equal(new Rational(8, 1), exact.ExpectedWin);
        if (chain)
        {
            Assert.StartsWith("chain", Assert.Single(exact.Loops).Evaluation);
        }

        Dist<BigInteger> classic = ExactInterpreter
            .Evaluate(compiled.ReferenceProgram!, new Dictionary<string, object?>(), StateHasher.CanonicalHash)
            .ValueDistribution();
        foreach (Dist<BigInteger>? law in new[] { classic, exact.Distribution! })
        {
            Assert.Equal(2, law.Count);
            foreach (Dist<BigInteger>.Entry entry in law.Entries)
            {
                Assert.Contains(entry.Value, new BigInteger[] { 2, 10 });
                Assert.Equal(entry.Value == 2 ? new Rational(1, 4) : new Rational(3, 4),
                    new Rational(entry.Numerator, law.Denominator));
            }
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Compiled)]
    [InlineData(SamplingTier.Tiered)]
    public void CancellationRestoresPublishedIteratorBindings(SamplingTier tier)
    {
        using var cancellation = new CancellationTokenSource();
        var probe = new CancelProbe(cancellation);
        var slots = new Dictionary<string, int>
        {
            ["nums"] = 0,
            ["x"] = 1,
            ["probe"] = 2
        };
        MapExpr expression = Map(S("nums"), Add(S("x"), S("probe", "Value")), 512);
        Func<SamplingFrame, ExprValue> program = new SamplingExpressions(name => slots[name], tier).Compile(expression);
        var frame = new SamplingFrame(slots, new SamplingCell[3], new Dictionary<string, object?>
        {
            ["nums"] = Enumerable.Range(0, 512).ToArray(),
            ["x"] = "outer",
            ["probe"] = probe
        });
        if (tier == SamplingTier.Tiered)
        {
            for (var call = 0; call < 80; call++)
            {
                frame.Reset(default);
                program(frame);
            }
        }

        probe.Calls = 0;
        probe.Armed = true;
        frame.Reset(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => program(frame));
        Assert.InRange(probe.Calls, 3, 255);
        Assert.Equal("outer", frame.Read(slots["x"], "x").StringValue);
        Assert.Null(frame.Locals);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SourceValuesRetainWideIntegersAndProperRationals(int mode)
    {
        Dictionary<string, object?> state = State();
        state["nums"] = new[] { ExprValue.Number(ulong.MaxValue), ExprValue.Rational(1, 3) };
        ExprValue expected = ExprValue.Rational(new BigInteger(ulong.MaxValue) * 3 + 1, 3);
        Assert.Equal(expected, Run(Sum(S("nums"), 2), state, mode));
        Assert.Equal(expected, Run(Aggregate(S("nums"), bound: 2), state, mode));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TypedSourceLoweringHandlesReadonlyListsAndNormalizesNestedSymbols(int mode)
    {
        Dictionary<string, object?> state = State();
        state["nums"] =
            ExprValue.Array(new ReadOnlyCollection<ExprValue>(new[]
            {
                ExprValue.Number(1), ExprValue.Number(2), ExprValue.Rational(1, 3)
            }));
        Assert.Equal(ExprValue.Rational(10, 3), Run(Sum(S("nums"), 3), state, mode));
        state["nums"] = ExprValue.Array([
            ExprValue.Symbol("token"), ExprValue.Record(new Dictionary<string, ExprValue>
            {
                ["nested"] = ExprValue.Symbol("inside")
            })
        ]);
        ExprValue value = Run(Map(S("nums"), S("x"), 2), state, mode);
        Assert.False(value.ContainsSymbols);
        Assert.Equal(ExprValue.String("token"), value.ArrayValue![0]);
        Assert.Equal(ExprValue.String("inside"), value.ArrayValue[1].RecordValue!["nested"]);
    }

    public sealed class Probe
    {
        public int Calls { get; private set; }

        public int[] Items
        {
            get
            {
                Calls++;
                return [1, 2, 3];
            }
        }
    }

    private sealed class CountingStream(int length) : IEnumerable
    {
        public int Reads;

        public IEnumerator GetEnumerator()
        {
            for (var item = 0; item < length; item++)
            {
                Reads++;
                yield return item;
            }
        }
    }

    public sealed class CancelProbe(CancellationTokenSource cancellation)
    {
        public bool Armed;
        public int Calls;

        public int Value
        {
            get
            {
                Calls++;
                if (Armed && Calls == 3)
                {
                    cancellation.Cancel();
                }

                return 1;
            }
        }
    }
}
