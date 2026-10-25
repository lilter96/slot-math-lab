using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Compiler.Ir;

public sealed class ExpressionIrTests
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

    private static BinaryExpr Add(Expression a, Expression b) => new()
    {
        Op = BinaryOp.Add,
        Left = a,
        Right = b
    };

    private static TypeCheckContext Context() => new()
    {
        StateFields =
        [
            new FieldDescriptor
            {
                Name = "x",
                Type = ExprType.Number
            },
            new FieldDescriptor
            {
                Name = "nums",
                Type = ExprType.Array,
                ArrayItemType = ExprType.Number
            }
        ]
    };

    private static ExpressionIr Ir(Expression expression, IReadOnlyDictionary<string, ExprValue>? facts = null)
    {
        ExpressionIr ir = ExpressionIrBuilder.Build(expression, Context(), facts);
        Assert.Empty(IrVerifier.Verify(ir));
        return ir;
    }

    [Fact]
    public void ShapesDescribeContractsWithoutProvingExternalReadsSafe()
    {
        ExpressionIr ir = Ir(Add(S("x"), N(2)));
        Assert.Equal(ExprType.Number, ir.Root.Result.Type);
        Assert.Contains("x", ir.Effects.Reads);
        Assert.True(ir.Effects.Flags.HasFlag(IrEffect.MayThrow));
        Assert.False(ir.Effects.CanSpeculate);
        Assert.Empty(ir.Guards);
        ExpressionIr literal = Ir(Add(N(2), N(3)));
        Assert.Equal(ExprValue.Number(5), literal.Root.Result.Constant);
        Assert.True(literal.Effects.CanSpeculate);
    }

    [Fact]
    public void CapturedFactsRequireOverrideGuardsAndOwnTheirSnapshot()
    {
        ExprValue[] values = new[] { ExprValue.Number(7) };
        var facts = new Dictionary<string, ExprValue>
        {
            ["nums"] = ExprValue.Array(values),
            ["x"] = ExprValue.Number(11)
        };
        ExpressionIr ir = Ir(Add(S("nums", "0"), S("x")), facts);
        Assert.True(ir.Effects.CanSpeculate);
        Assert.Equal(ExprValue.Number(18), ir.Root.Result.Constant);
        Assert.False(ir.AcceptsOverrides(["x"]));
        Assert.False(ir.AcceptsOverrides(["nums"]));
        Assert.True(ir.AcceptsOverrides(["other"]));
        values[0] = ExprValue.Number(999);
        facts["x"] = ExprValue.Number(0);
        Assert.Equal(ExprValue.Number(7), ir.Guards["nums"].ArrayValue![0]);
        Assert.Equal(ExprValue.Number(11), ir.Guards["x"]);
    }

    [Fact]
    public void FailedProjectionAndDivisionRemainThrowingDespiteFacts()
    {
        var facts = new Dictionary<string, ExprValue>
        {
            ["nums"] = ExprValue.Array([ExprValue.Number(1)]),
            ["x"] = ExprValue.Number(0)
        };
        Assert.False(Ir(S("nums", "2"), facts).Effects.CanSpeculate);
        Assert.False(Ir(new BinaryExpr
        {
            Op = BinaryOp.Div,
            Left = N(1),
            Right = S("x")
        }, facts).Effects.CanSpeculate);
    }

    [Fact]
    public void LexicalLetIsSingleAssignmentAndNeverWritesRecurrenceState()
    {
        ExpressionIr ir = Ir(new LetExpr
        {
            Name = "x",
            Value = N(3),
            Body = Add(new LocalExpr
            {
                Name = "x"
            }, S("x"))
        });
        IrOperation let = Assert.Single(ir.Root.Operations, operation => operation.Code == IrOp.Let);
        IrValue initial = Assert.Single(let.Inputs);
        IrRegion body = Assert.Single(let.Regions);
        Assert.Contains(body.Operations, operation => operation.Inputs.Any(value => value.Id == initial.Id));
        Assert.Contains("x", ir.Effects.Reads);
        Assert.Empty(ir.Effects.Writes);
        Assert.Empty(ir.Effects.Bindings);
        Assert.Equal(ExprType.Number, ir.Root.Result.Type);
    }

    [Fact]
    public void SiblingConditionalRegionsDoNotBecomeEagerRootOperations()
    {
        ExpressionIr ir = Ir(new IfExpr
        {
            Condition = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = S("x"),
                Right = N(1)
            },
            ThenExpr = S("thenMissing"),
            ElseExpr = S("elseMissing")
        });
        IrOperation conditional = Assert.Single(ir.Root.Operations, operation => operation.Code == IrOp.If);
        Assert.Equal(2, conditional.Regions.Count);
        Assert.DoesNotContain(ir.Root.Operations, operation => operation.Detail is "thenMissing" or "elseMissing");
        Assert.Contains(conditional.Regions[0].Effects.Reads, key => key == "thenMissing");
        Assert.Contains(conditional.Regions[1].Effects.Reads, key => key == "elseMissing");
    }

    [Theory]
    [InlineData(BinaryOp.And)]
    [InlineData(BinaryOp.Or)]
    public void LogicalRightOperandsRemainConditional(BinaryOp op)
    {
        ExpressionIr ir = Ir(new BinaryExpr
        {
            Op = op,
            Left = new ConstantExpr
            {
                Kind = ConstantKind.Boolean,
                Value = "false"
            },
            Right = S("missing")
        });
        IrOperation logical = Assert.Single(ir.Root.Operations, operation => operation.Code is IrOp.And or IrOp.Or);
        Assert.Single(logical.Inputs);
        Assert.Single(logical.Regions);
        Assert.DoesNotContain(ir.Root.Operations, operation => operation.Detail == "missing");
    }

    [Fact]
    public void FoldInitializerPrecedesSourceAndBodyNamesAreParameters()
    {
        ExpressionIr ir = Ir(new FoldExpr
        {
            Source = S("nums"),
            MaxItems = 4,
            Init = S("x"),
            AccName = "acc",
            ItemName = "x",
            IndexName = "index",
            ItemType = ExprType.Number,
            Body = Add(S("acc"), S("x"))
        });
        Assert.Equal(new[] { "x", "nums" },
            ir.Root.Operations.Where(operation => operation.Code == IrOp.ReadState)
                .Select(operation => operation.Detail));
        IrOperation fold = Assert.Single(ir.Root.Operations, operation => operation.Code == IrOp.Fold);
        IrRegion body = Assert.Single(fold.Regions);
        Assert.Equal(3, body.Parameters.Count);
        Assert.Empty(body.Effects.Reads);
        Assert.True(ir.Effects.Bindings.SetEquals(["acc", "x", "index"]));
        Assert.True(ir.Effects.Flags.HasFlag(IrEffect.Cancellation));
    }

    [Fact]
    public void SourceReadsOuterStateBeforeTheInnerItemBindingExists()
    {
        ExpressionIr ir = Ir(new MapExpr
        {
            Source = S("x"),
            MaxItems = 4,
            ItemName = "x",
            ItemType = ExprType.Number,
            Body = S("x")
        });
        IrOperation read = Assert.Single(ir.Root.Operations, operation => operation.Code == IrOp.ReadState);
        IrOperation map = Assert.Single(ir.Root.Operations, operation => operation.Code == IrOp.Map);
        Assert.Equal(read.Result.Id, Assert.Single(map.Inputs).Id);
        Assert.Empty(Assert.Single(map.Regions).Effects.Reads);
    }

    [Fact]
    public void CountSelectorGetsItsOwnConditionalRegion()
    {
        ExpressionIr ir = Ir(new AggregateExpr
        {
            Source = S("nums"),
            MaxItems = 4,
            ItemName = "x",
            ItemType = ExprType.Number,
            Func = AggregateFunc.Count,
            Predicate = new CompareExpr
            {
                Op = CompareOp.Gt,
                Left = S("x"),
                Right = N(0)
            },
            ValueExpr = S("selectorMissing")
        });
        IrOperation aggregate = Assert.Single(ir.Root.Operations, operation => operation.Code == IrOp.Aggregate);
        IrRegion iteration = Assert.Single(aggregate.Regions);
        IrOperation step = Assert.Single(iteration.Operations);
        Assert.Equal(IrOp.AggregateStep, step.Code);
        Assert.Equal(new[] { "predicate", "selector-if-selected" }, step.Regions.Select(region => region.Role));
        Assert.Empty(step.Regions[0].Effects.Reads);
        Assert.Contains("selectorMissing", step.Regions[1].Effects.Reads);
    }

    [Fact]
    public void RecordTupleAndProjectionShapesSurviveLowering()
    {
        var expression = new LetExpr
        {
            Name = "r",
            Value = new RecordExpr
            {
                Fields =
                [
                    new RecordFieldExpr
                    {
                        Name = "pair",
                        Value = new TupleExpr
                        {
                            Items =
                            [
                                N(1), new ConstantExpr
                                {
                                    Kind = ConstantKind.String,
                                    Value = "text"
                                }
                            ]
                        }
                    }
                ]
            },
            Body = new LocalExpr
            {
                Name = "r",
                Path = ["pair", "1"]
            }
        };
        Assert.Equal(ExprType.String, Ir(expression).Root.Result.Type);
    }

    [Fact]
    public void UnknownFormsNeverProduceOptimizationFacts()
    {
        ExpressionIr ir = Ir(new UnknownExpr());
        Assert.False(ir.Effects.Known);
        Assert.False(ir.Effects.CanSpeculate);
        Assert.True(ir.Effects.Flags.HasFlag(IrEffect.Random));
        Assert.True(ir.Effects.Flags.HasFlag(IrEffect.WriteState));
    }

    [Fact]
    public void VerifierRejectsSiblingDefinitionsUsedAcrossBranches()
    {
        ExpressionIr source = Ir(new IfExpr
        {
            Condition = new ConstantExpr
            {
                Kind = ConstantKind.Boolean,
                Value = "true"
            },
            ThenExpr = N(1),
            ElseExpr = N(2)
        });
        IrOperation branch = source.Root.Operations.Last();
        IrRegion yes = branch.Regions[0];
        IrRegion no = branch.Regions[1];
        IrOperation bad = no.Operations[0] with
        {
            Inputs = new[] { yes.Result }
        };
        IrRegion changed = no with
        {
            Operations = new[] { bad }
        };
        IrRegion broken = source.Root with
        {
            Operations = source.Root.Operations.Select(operation => operation.Id == branch.Id
                ? operation with
                {
                    Regions = new[] { yes, changed }
                }
                : operation).ToArray()
        };
        Assert.Contains(IrVerifier.Verify(new ExpressionIr(broken, source.Guards)),
            error => error.Contains("does not dominate"));
    }

    private static Dictionary<string, Port> Ports() => new()
    {
        ["state"] = new Port
        {
            Name = "state",
            Type = PortType.State
        }
    };

    private static Edge Edge(string a, string b) => new()
    {
        Id = a + b,
        SourceNodeId = a,
        TargetNodeId = b,
        SourcePort = "state",
        TargetPort = "state"
    };

    private static GraphConfig Graph() => new()
    {
        SchemaVersion = "1.0.0",
        InitialState = new Dictionary<string, JsonElement>
        {
            ["stable"] = JsonSerializer.SerializeToElement(7),
            ["x"] = JsonSerializer.SerializeToElement(99),
            ["nums"] = JsonSerializer.SerializeToElement(new[] { 1, 2 })
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
                        OutcomeId = "a",
                        Weight = 1,
                        Value = 1,
                        StateValue = N(1)
                    },
                    new DrawWeight
                    {
                        OutcomeId = "b",
                        Weight = 1,
                        Value = 3,
                        StateValue = N(3)
                    }
                ]
            },
            new ModifyStateNode
            {
                Id = "pay",
                ExpressionId = "pay",
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
        Edges = [Edge("draw", "pay"), Edge("pay", "sink")],
        Expressions = new Dictionary<string, Expression>
        {
            ["pay"] = Add(S("stable"), S("roll"))
        }
    };

    [Fact]
    public void CompileResultExposesVerifiedIrAndSamplingConsumesItsStableFields()
    {
        GraphConfig source = Graph();
        source.Expressions!["unused"] = new MapExpr
        {
            StateKey = "nums",
            ItemName = "x",
            ItemType = ExprType.Number,
            Body = S("x")
        };
        CompileResult compiled = new GraphCompiler().Compile(source);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        var ir = Assert.IsType<CompilerIr>(compiled.Ir);
        Assert.Contains("stable", ir.StableInitialValues.Keys);
        Assert.DoesNotContain("x", ir.StableInitialValues.Keys);
        Assert.True(ir.Nodes["draw"].Effects.Flags.HasFlag(IrEffect.Random));
        Assert.True(ir.Nodes["sink"].Effects.Flags.HasFlag(IrEffect.Observe));
        Assert.Contains("win", ir.Nodes["pay"].Effects.Writes);
        foreach (ExpressionIr expression in ir.Expressions.Values)
        {
            Assert.Empty(IrVerifier.Verify(expression));
        }

        CompileResult baseline = new GraphCompiler(optimizeSampling: false).Compile(source);
        for (var seed = 0; seed < 32; seed++)
        {
            foreach (Dictionary<string, object?> initial in new[]
                     {
                         new Dictionary<string, object?>(), new Dictionary<string, object?>
                         {
                             ["stable"] = 11
                         }
                     })
            {
                (BigInteger Value, Dictionary<string, object?> State) expected =
                    SampledInterpreter.RunSingle(baseline.Program!, initial, seed);
                (BigInteger Value, Dictionary<string, object?> State) actual =
                    SampledInterpreter.RunSingle(compiled.Program!, initial, seed);
                Assert.Equal(expected.Value, actual.Value);
                Assert.Equal(JsonSerializer.Serialize(expected.State.OrderBy(pair => pair.Key), JsonOptions.Default),
                    JsonSerializer.Serialize(actual.State.OrderBy(pair => pair.Key), JsonOptions.Default));
            }
        }
    }

    [Fact]
    public void LocalNamesDoNotInvalidateGlobalFactsButMeasurementIteratorsDo()
    {
        GraphConfig source = Graph();
        source.Expressions!["local"] = new LetExpr
        {
            Name = "stable",
            Value = N(1),
            Body = new LocalExpr
            {
                Name = "stable"
            }
        };
        CompileResult compiled = new GraphCompiler().Compile(source);
        Assert.Contains("stable", compiled.Ir!.StableInitialValues.Keys);
        var measurement = new MeasurementDefinition
        {
            Id = "count",
            Name = "Count",
            NodeId = "sink",
            Value = new AggregateExpr
            {
                Func = AggregateFunc.Count,
                StateKey = "nums",
                ItemName = "stable",
                ItemType = ExprType.Number
            }
        };
        var observed = CompilerIr.Build(compiled.Structured!.Config, [measurement]);
        Assert.DoesNotContain("stable", observed.StableInitialValues.Keys);
        Assert.True(observed.Nodes["sink"].Effects.Flags.HasFlag(IrEffect.Observe));
    }

    [Fact]
    public void OpaqueNodesKillFactsWithoutInspectingTransformNames()
    {
        GraphConfig graph = Graph();
        graph.Nodes[0] = new MapNode
        {
            Id = "draw",
            TransformId = "arbitrary",
            Outputs = Ports()
        };
        var ir = CompilerIr.Build(graph, []);
        Assert.False(ir.Effects.Known);
        Assert.Empty(ir.StableInitialValues);
    }

    [Fact]
    public void SequentialFanoutRetainsAuthoredEdgeOrder()
    {
        GraphConfig graph = Graph();
        graph = graph with
        {
            Edges = [Edge("draw", "sink"), Edge("draw", "pay"), Edge("pay", "sink")]
        };
        var ir = CompilerIr.Build(graph, []);
        Assert.Equal(new[] { "sink", "pay" }, ir.Nodes["draw"].Continuations.Select(edge => edge.Target));
    }

    [Fact]
    public void VerifierRejectsAnErrorFlagRemovedFromAChildSummary()
    {
        ExpressionIr source = Ir(S("x"));
        IrOperation operation = source.Root.Operations[0] with
        {
            Effects = IrEffects.Empty
        };
        IrRegion changed = source.Root with
        {
            Operations = new[] { operation }
        };
        Assert.Contains(IrVerifier.Verify(new ExpressionIr(changed, source.Guards)),
            error => error.Contains("effect summary"));
    }

    [Fact]
    public void ConstantLocalArithmeticUsesSsaOperandsRatherThanOuterState()
    {
        var expression = new LetExpr
        {
            Name = "x",
            Value = N(3),
            Body = Add(new LocalExpr
            {
                Name = "x"
            }, N(2))
        };
        ExpressionIr ir = Ir(expression, new Dictionary<string, ExprValue>
        {
            ["x"] = ExprValue.Number(99)
        });
        Assert.Equal(ExprValue.Number(5), ir.Root.Result.Constant);
        Assert.True(ir.Effects.CanSpeculate);
        Assert.Empty(ir.Guards);
    }

    [Fact]
    public void EveryCurrentExpressionFormHasOrderedRegionsAndKnownEffects()
    {
        Expression[] forms =
        [
            N(1), S("x"), Add(N(1), N(2)), new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = N(1),
                Right = N(2)
            },
            new NotExpr
            {
                Expr = new ConstantExpr
                {
                    Kind = ConstantKind.Boolean,
                    Value = "true"
                }
            },
            new IfExpr
            {
                Condition = new ConstantExpr
                {
                    Kind = ConstantKind.Boolean,
                    Value = "true"
                },
                ThenExpr = N(1),
                ElseExpr = N(2)
            },
            new CallExpr
            {
                Function = "length",
                Args = [S("nums")]
            },
            new BitSetExpr
            {
                Op = BitSetOp.Count,
                Args = [N(3)]
            },
            new TupleExpr
            {
                Items = [N(1), N(2)]
            },
            new RecordExpr
            {
                Fields =
                [
                    new RecordFieldExpr
                    {
                        Name = "x",
                        Value = N(1)
                    }
                ]
            },
            new ProjectExpr
            {
                Value = new TupleExpr
                {
                    Items = [N(1)]
                },
                Path = ["0"]
            },
            new LetExpr
            {
                Name = "a",
                Value = N(1),
                Body = new LocalExpr
                {
                    Name = "a"
                }
            },
            new MapExpr
            {
                Source = S("nums"),
                MaxItems = 4,
                ItemName = "x",
                ItemType = ExprType.Number,
                Body = S("x")
            },
            new FilterExpr
            {
                Source = S("nums"),
                MaxItems = 4,
                ItemName = "x",
                ItemType = ExprType.Number,
                Predicate = new CompareExpr
                {
                    Op = CompareOp.Gt,
                    Left = S("x"),
                    Right = N(0)
                }
            },
            new FoldExpr
            {
                Source = S("nums"),
                MaxItems = 4,
                ItemName = "x",
                AccName = "a",
                ItemType = ExprType.Number,
                Init = N(0),
                Body = Add(S("a"), S("x"))
            },
            new AggregateExpr
            {
                Source = S("nums"),
                MaxItems = 4,
                ItemName = "x",
                ItemType = ExprType.Number,
                Func = AggregateFunc.Sum,
                ValueExpr = S("x")
            }
        ];
        foreach (Expression form in forms)
        {
            Assert.True(Ir(form).Effects.Known);
        }
    }

    [Fact]
    public void StructuredRootKeysCannotCollideThroughAuthoredSeparators()
    {
        GraphConfig graph = Graph();
        graph.Nodes[0] = (DrawNode)graph.Nodes[0] with
        {
            Id = "a:input:b",
            Inputs = new Dictionary<string, Port>
            {
                ["c"] = new()
                {
                    Name = "c",
                    Type = PortType.Number,
                    DefaultValue = N(11)
                }
            }
        };
        graph.Nodes[1] = (ModifyStateNode)graph.Nodes[1] with
        {
            Id = "a",
            Inputs = new Dictionary<string, Port>
            {
                ["b:input:c"] = new()
                {
                    Name = "b:input:c",
                    Type = PortType.Number,
                    DefaultValue = N(22)
                }
            }
        };
        var ir = CompilerIr.Build(graph, []);
        IrExpressionKey first = ir.Nodes["a:input:b"].ExpressionKeys.Single(key => key.Kind == IrRootKind.Node);
        IrExpressionKey second = ir.Nodes["a"].ExpressionKeys.Single(key => key.Kind == IrRootKind.Node);
        Assert.NotEqual(first, second);
        Assert.Equal(ExprValue.Number(11), ir.Expressions[first].Root.Result.Constant);
        Assert.Equal(ExprValue.Number(22), ir.Expressions[second].Root.Result.Constant);
    }

    [Theory]
    [InlineData("nine-lives.json")]
    [InlineData("ancient-dragon.json")]
    [InlineData("woodland-whisper.json")]
    [InlineData("x7-club.json")]
    [InlineData("le-militare.standard.json")]
    [InlineData("nine-lives.buy.json")]
    [InlineData("woodland-whisper.buy.json")]
    [InlineData("x7-club.buy.json")]
    [InlineData("le-militare.chance.json")]
    [InlineData("le-militare.elite.json")]
    [InlineData("le-militare.siege.spin.json")]
    [InlineData("le-militare.spin.json")]
    [InlineData("le-militare.recon.spin.json")]
    [InlineData("le-militare.super.json")]
    [InlineData("le-militare.air-raid.json")]
    public void CurrentGameExpressionRegionsPassStructuralAndEffectVerification(string file)
    {
        var source = JsonSerializer.Deserialize<GraphConfig>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Games", file)), JsonOptions.Default)!;
        var before = JsonSerializer.Serialize(source, JsonOptions.Default);
        CompileResult compiled = new GraphCompiler().Compile(source);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        var ir = Assert.IsType<CompilerIr>(compiled.Ir);
        Assert.NotEmpty(ir.Expressions);
        Assert.Empty(MemorySsaVerifier.Verify(ir.Memory));
        foreach ((IrExpressionKey key, ExpressionIr expression) in ir.Expressions)
        {
            Assert.True(IrVerifier.Verify(expression).Count == 0,
                key + ": " + string.Join(';', IrVerifier.Verify(expression)));
        }

        Assert.Equal(before, JsonSerializer.Serialize(source, JsonOptions.Default));
    }

    private sealed record UnknownExpr : Expression;
}
