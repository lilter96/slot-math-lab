using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Optimization;

namespace SlotMath.Core.Tests.Compiler;

public class GraphSimplifierTests
{
    private static ConstantExpr Number(string value) => new()
    {
        Kind = value.Contains('/') ? ConstantKind.Rational : ConstantKind.Integer,
        Value = value
    };

    private static ConstantExpr Boolean(bool value) => new()
    {
        Kind = ConstantKind.Boolean,
        Value = value ? "true" : "false"
    };

    private static FieldAccessExpr Field(string key) => new()
    {
        Target = "state",
        Path = [key]
    };

    private static BinaryExpr Binary(BinaryOp op, Expression left, Expression right) => new()
    {
        Op = op,
        Left = left,
        Right = right
    };

    private static CompareExpr Positive(string key) => new()
    {
        Op = CompareOp.Gt,
        Left = Field(key),
        Right = Number("0")
    };

    private static IfExpr If(Expression condition, Expression yes, Expression no) => new()
    {
        Condition = condition,
        ThenExpr = yes,
        ElseExpr = no
    };

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions.Default);

    [Fact]
    public void FoldsExactRationalsWithoutRounding()
    {
        BinaryExpr source = Binary(BinaryOp.Add, Number("1/3"), Number("1/6"));
        ExpressionSimplification result = ExpressionSimplifier.Simplify(source);
        var literal = Assert.IsType<ConstantExpr>(result.Expression);
        Assert.Equal(ConstantKind.Rational, literal.Kind);
        Assert.Equal("1/2", literal.Value);
        Assert.Equal(1, result.Rules["exact-constant"]);
    }

    [Fact]
    public void RemovesDeadOperandsButKeepsReachableFailures()
    {
        BinaryExpr invalid = Binary(BinaryOp.Div, Number("1"), Number("0"));
        Assert.Equal("7",
            Assert.IsType<ConstantExpr>(ExpressionSimplifier.Simplify(If(Boolean(true), Number("7"), invalid))
                .Expression).Value);
        Expression kept = ExpressionSimplifier.Simplify(Binary(BinaryOp.Mul, invalid, Number("0"))).Expression;
        var error = Assert.Throws<ExpressionEvaluationException>(() =>
            ExactExpressionEvaluator.Evaluate(kept, EvalContext.Empty));
        Assert.Equal(EvalErrorCodes.DivisionByZero, error.Code);
        Assert.Equal(Json((Expression)invalid), Json(ExpressionSimplifier.Simplify(invalid).Expression));
    }

    [Fact]
    public void IdentitiesNeverEraseRuntimeTypeChecks()
    {
        BinaryExpr source = Binary(BinaryOp.Add, Field("x"), Number("0"));
        ExpressionSimplification result = ExpressionSimplifier.Simplify(source);
        Assert.Empty(result.Rules);
        Assert.Throws<ExpressionEvaluationException>(() => ExactExpressionEvaluator.Evaluate(result.Expression,
            new EvalContext
            {
                State = new Dictionary<string, object?>
                {
                    ["x"] = "wrong type"
                }
            }));
        BinaryExpr nested = Binary(BinaryOp.Mul, source, Number("1"));
        Assert.Equal(Json((Expression)source), Json(ExpressionSimplifier.Simplify(nested).Expression));
    }

    [Fact]
    public void RemovesRepeatedPredicatesOnlyOnDominatedPaths()
    {
        CompareExpr predicate = Positive("x");
        IfExpr source = If(predicate, If(Positive("x"), Number("7"), Number("99")), Number("3"));
        ExpressionSimplification result = ExpressionSimplifier.Simplify(source);
        var root = Assert.IsType<IfExpr>(result.Expression);
        Assert.Equal("7", Assert.IsType<ConstantExpr>(root.ThenExpr).Value);
        Assert.Equal(1, result.Rules["dominated-predicate"]);
        foreach (var x in new[] { -1, 0, 1 })
        {
            var context = new EvalContext
            {
                State = new Dictionary<string, object?>
                {
                    ["x"] = x
                }
            };
            Assert.Equal(x > 0 ? 7 : 3, ExactExpressionEvaluator.Evaluate(result.Expression, context).AsInteger());
        }
    }

    [Fact]
    public void PredicateFactsAndConstantsDoNotCrossLambdaBindings()
    {
        IfExpr source = If(Positive("x"), new MapExpr
        {
            StateKey = "items",
            ItemName = "x",
            ItemType = ExprType.Number,
            Body = If(Positive("x"), Number("7"), Number("3"))
        }, Field("items"));
        ExpressionSimplification result = ExpressionSimplifier.Simplify(source, new Dictionary<string, ExprValue>
        {
            ["x"] = ExprValue.Number(10)
        });
        var map = Assert.IsType<MapExpr>(result.Expression);
        Assert.IsType<IfExpr>(map.Body);
        ExprValue actual = ExactExpressionEvaluator.Evaluate(map, new EvalContext
        {
            State = new Dictionary<string, object?>
            {
                ["items"] = new[] { -1, 1 },
                ["x"] = 10
            }
        });
        Assert.Equal(new BigInteger[] { 3, 7 }, actual.ArrayValue!.Select(item => item.AsInteger()));
    }

    [Fact]
    public void ImmutableTablesCanFoldScalarLookupsButAreNeverAssumedFromInitialState()
    {
        Expression source = new CallExpr
        {
            Function = "index",
            Args = [Field("table"), Number("1")]
        };
        Assert.Empty(ExpressionSimplifier.Simplify(source).Rules);
        ExpressionSimplification result = ExpressionSimplifier.Simplify(source, new Dictionary<string, ExprValue>
        {
            ["table"] = ExprValue.Array([ExprValue.Number(2), ExprValue.Number(11)])
        });
        Assert.Equal("11", Assert.IsType<ConstantExpr>(result.Expression).Value);
    }

    [Fact]
    public void VisitBudgetPreservesTheUnprocessedProgram()
    {
        BinaryExpr source = Binary(BinaryOp.Add, Number("1"), Number("2"));
        ExpressionSimplification result = ExpressionSimplifier.Simplify(source, maxVisits: 0);
        Assert.True(result.BudgetReached);
        Assert.Same(source, result.Expression);
    }

    [Fact]
    public void LargePredicateSignaturesRespectTheTraversalBudgetWithoutSerializingDeepTrees()
    {
        Expression predicate = Positive("x");
        for (var i = 0; i < 512; i++)
        {
            predicate = Binary(BinaryOp.And, Positive("x"), predicate);
        }

        ExpressionSimplification result =
            ExpressionSimplifier.Simplify(If(predicate, Number("7"), Number("3")), maxVisits: 64);
        Assert.True(result.BudgetReached);
        Assert.InRange(result.Visited, 0, 64);
        Assert.IsType<IfExpr>(result.Expression);
        Expression nested = Number("1");
        for (var i = 0; i < 512; i++)
        {
            nested = If(Positive("x"), nested, Number("2"));
        }

        ExpressionSimplification numeric =
            ExpressionSimplifier.Simplify(Binary(BinaryOp.Add, nested, Number("0")), maxVisits: 64);
        Assert.True(numeric.BudgetReached);
        Assert.InRange(numeric.Visited, 0, 64);
    }

    [Fact]
    public void PropagatesOnlySuccessfullyEstablishedBooleanFacts()
    {
        IfExpr source = If(Binary(BinaryOp.And, Positive("x"), Positive("y")),
            If(new NotExpr
            {
                Expr = Positive("x")
            }, Number("99"), Number("7")), Number("3"));
        ExpressionSimplification result = ExpressionSimplifier.Simplify(source);
        Assert.Equal("7", Assert.IsType<ConstantExpr>(Assert.IsType<IfExpr>(result.Expression).ThenExpr).Value);
        IfExpr skipped = If(Binary(BinaryOp.Or, Positive("x"), Positive("y")), Number("7"),
            If(Positive("y"), Number("99"), Number("3")));
        Assert.Equal("3",
            Assert.IsType<ConstantExpr>(Assert.IsType<IfExpr>(ExpressionSimplifier.Simplify(skipped).Expression)
                .ElseExpr).Value);
    }

    [Fact]
    public void NestedImmutableCollectionsCannotBypassConstantEvaluationLimits()
    {
        var constants = new Dictionary<string, ExprValue>
        {
            ["record"] = ExprValue.Record(new Dictionary<string, ExprValue>
            {
                ["items"] = ExprValue.Array(Enumerable.Range(0, 5000).Select(i => ExprValue.Number(i)).ToArray())
            })
        };
        Expression source = new CallExpr
        {
            Function = "contains",
            Args =
            [
                new FieldAccessExpr
                {
                    Target = "state",
                    Path = ["record", "items"]
                },
                Number("4999")
            ]
        };
        ExpressionSimplification result = ExpressionSimplifier.Simplify(source, constants);
        Assert.Empty(result.Rules);
        Assert.Equal(Json(source), Json(result.Expression));
    }

    [Theory]
    [InlineData(BinaryOp.And, true)]
    [InlineData(BinaryOp.Or, false)]
    public void BooleanIdentitiesRetainTheOperandsRuntimeTypeCheck(BinaryOp op, bool identity)
    {
        BinaryExpr source = Binary(op, Boolean(identity), Field("x"));
        ExpressionSimplification result = ExpressionSimplifier.Simplify(source);
        var context = new EvalContext
        {
            State = new Dictionary<string, object?>
            {
                ["x"] = "invalid"
            }
        };
        var expected =
            Assert.Throws<ExpressionEvaluationException>(() => ExactExpressionEvaluator.Evaluate(source, context));
        var actual =
            Assert.Throws<ExpressionEvaluationException>(() =>
                ExactExpressionEvaluator.Evaluate(result.Expression, context));
        Assert.Equal(expected.Code, actual.Code);
        Assert.Equal(expected.Location, actual.Location);
    }

    [Fact]
    public void GeneratedExpressionsKeepExactValuesAndLocatedFailures()
    {
        var random = new System.Random(63_091);

        Expression Numeric(int depth)
        {
            if (depth == 0)
            {
                return random.Next(3) == 0 ? Field("x") : Number(random.Next(-2, 3).ToString());
            }

            return random.Next(3) switch
            {
                0 => Binary((BinaryOp)random.Next(4), Numeric(depth - 1), Numeric(depth - 1)),
                1 => If(Predicate(depth - 1), Numeric(depth - 1), Numeric(depth - 1)),
                _ => Numeric(0)
            };
        }

        Expression Predicate(int depth)
        {
            return depth == 0
                ? new CompareExpr
                {
                    Op = (CompareOp)random.Next(6),
                    Left = Numeric(0),
                    Right = Numeric(0)
                }
                : random.Next(3) switch
                {
                    0 => Binary(random.Next(2) == 0 ? BinaryOp.And : BinaryOp.Or, Predicate(depth - 1),
                        Predicate(depth - 1)),
                    1 => new NotExpr
                    {
                        Expr = Predicate(depth - 1)
                    },
                    _ => Boolean(random.Next(2) == 0)
                };
        }

        for (var example = 0; example < 400; example++)
        {
            Expression source = Numeric(4);
            Expression rewritten = ExpressionSimplifier.Simplify(source).Expression;
            foreach (var x in new object?[] { -1, 0, 3, "invalid", null })
            {
                var state = new Dictionary<string, object?>();
                if (x is not null)
                {
                    state["x"] = x;
                }

                var context = new EvalContext
                {
                    State = state
                };
                ExprValue expected;
                try
                {
                    expected = ExactExpressionEvaluator.Evaluate(source, context);
                }
                catch (ExpressionEvaluationException error)
                {
                    var actual = Assert.Throws<ExpressionEvaluationException>(() =>
                        ExactExpressionEvaluator.Evaluate(rewritten, context));
                    Assert.Equal(error.Code, actual.Code);
                    Assert.Equal(error.Location, actual.Location);
                    continue;
                }

                Assert.Equal(expected, ExactExpressionEvaluator.Evaluate(rewritten, context));
            }
        }
    }

    [Fact]
    public void ProvenNumericFieldsAllowIdentitiesButNeverCrossLambdaBindings()
    {
        BinaryExpr source = Binary(BinaryOp.Add, Field("x"), Number("0"));
        var facts = new HashSet<string>
        {
            "x"
        };
        Assert.IsType<FieldAccessExpr>(ExpressionSimplifier.Simplify(source, numericFields: facts).Expression);
        var map = new MapExpr
        {
            StateKey = "items",
            ItemName = "x",
            ItemType = ExprType.Number,
            Body = source
        };
        Assert.IsType<BinaryExpr>(Assert
            .IsType<MapExpr>(ExpressionSimplifier.Simplify(map, numericFields: facts).Expression).Body);
    }

    [Fact]
    public void NumericInvariantSpecializationFallsBackBeforeInvalidOverridesAreRead()
    {
        GraphConfig source = Graph(Binary(BinaryOp.Add, Field("x"), Number("0")));
        var reset = new ModifyStateNode
        {
            Id = "reset",
            OutputKey = "x",
            ExpressionId = "reset",
            Inputs = source.Nodes[1].Inputs,
            Outputs = source.Nodes[0].Outputs
        };
        source = source with
        {
            Nodes = [source.Nodes[0], reset, source.Nodes[1]],
            Edges =
            [
                source.Edges[0] with
                {
                    TargetNodeId = "reset"
                },
                source.Edges[0] with
                {
                    Id = "reset-edge",
                    SourceNodeId = "reset"
                }
            ],
            Expressions = new Dictionary<string, Expression>(source.Expressions!)
            {
                ["reset"] = Number("7")
            }
        };
        Assert.Contains("x", SamplingMaskState.Infer(source, []));
        ExpressionEvaluationException? control = null;
        foreach (var enabled in new[] { false, true })
        {
            CompileResult compiled = new GraphCompiler(simplifyExpressions: enabled).Compile(source);
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            Assert.Equal(5,
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42).Value);
            var error = Assert.Throws<ExpressionEvaluationException>(() => SampledInterpreter.RunSingle(
                compiled.Program!,
                new Dictionary<string, object?>
                {
                    ["x"] = "invalid"
                }, 42));
            if (control is not null)
            {
                Assert.Equal(control.Code, error.Code);
                Assert.Equal(control.Location, error.Location);
            }

            control = error;
        }
    }

    private static GraphConfig Graph(Expression expression) => new()
    {
        SchemaVersion = "1.0",
        InitialState = new Dictionary<string, JsonElement>
        {
            ["x"] = JsonSerializer.SerializeToElement(5)
        },
        Nodes =
        [
            new ModifyStateNode
            {
                Id = "write",
                ExpressionId = "value",
                OutputKey = "win",
                Outputs = new Dictionary<string, Port>
                {
                    ["out"] = new()
                    {
                        Name = "out",
                        Type = PortType.Number
                    }
                }
            },
            new MetricsSinkNode
            {
                Id = "sink",
                WinStateKey = "win",
                WinCap = 1000,
                Inputs = new Dictionary<string, Port>
                {
                    ["in"] = new()
                    {
                        Name = "in",
                        Type = PortType.Number
                    }
                }
            }
        ],
        Edges =
        [
            new Edge
            {
                Id = "edge",
                SourceNodeId = "write",
                SourcePort = "out",
                TargetNodeId = "sink",
                TargetPort = "in"
            }
        ],
        Expressions = new Dictionary<string, Expression>
        {
            ["value"] = expression
        }
    };

    [Fact]
    public void RefactoringRetainsTopologyAndIsIdempotent()
    {
        GraphConfig source = Graph(If(Boolean(true), Number("7"), Field("x")));
        var before = Json(source);
        GraphSimplification result = GraphSimplifier.Simplify(source);
        Assert.Single(result.Changes);
        Assert.Equal("/expressions/value", result.Changes[0].Path);
        Assert.Equal(Json(source.Nodes), Json(result.Config.Nodes));
        Assert.Same(source.Edges, result.Config.Edges);
        Assert.Equal(before, Json(source));
        Assert.Empty(GraphSimplifier.Simplify(result.Config).Changes);
    }

    [Fact]
    public void RuntimeSpecializationFallsBackForOverriddenInitialState()
    {
        GraphConfig source = Graph(If(Positive("x"), Number("7"), Number("3")));
        CompileResult compiled = new GraphCompiler().Compile(source);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        foreach (var x in new[] { -2, 0, 4 })
        {
            (BigInteger Value, Dictionary<string, object?> State) result = SampledInterpreter.RunSingle(
                compiled.Program!, new Dictionary<string, object?>
                {
                    ["x"] = x
                }, 42);
            Assert.Equal(x > 0 ? 7 : 3, result.Value);
        }

        Assert.Equal(7, SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42).Value);
        Assert.IsType<IfExpr>(source.Expressions!["value"]);
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
    public void CurrentGamesKeepSeededWinsAndCompleteState(string file)
    {
        var source = JsonSerializer.Deserialize<GraphConfig>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Games", file)), JsonOptions.Default)!;
        GraphSimplification refactored = GraphSimplifier.Simplify(source);
        Assert.Empty(GraphSimplifier.Simplify(refactored.Config).Changes);
        CompileResult reference = new GraphCompiler(optimizeSampling: false).Compile(source);
        CompileResult withoutPass = new GraphCompiler(simplifyExpressions: false).Compile(source);
        CompileResult optimized = new GraphCompiler().Compile(refactored.Config);
        Assert.True(reference.IsValid, string.Join(';', reference.Errors));
        Assert.True(withoutPass.IsValid, string.Join(';', withoutPass.Errors));
        Assert.True(optimized.IsValid, string.Join(';', optimized.Errors));
        Assert.Equal(reference.WinScale, optimized.WinScale);
        foreach (var trace in new bool?[] { null, false, true })
        {
            for (var round = 0; round < 12; round++)
            {
                var state = new Dictionary<string, object?>();
                if (trace is { } tracing)
                {
                    state["traceEnabled"] = tracing;
                }

                (BigInteger Value, Dictionary<string, object?> State) expected =
                    SampledInterpreter.RunSingle(reference.Program!, state, 731_941, round);
                (BigInteger Value, Dictionary<string, object?> State) actual =
                    SampledInterpreter.RunSingle(optimized.Program!, state, 731_941, round);
                Assert.Equal(expected.Value, actual.Value);
                Assert.Equal(Json(expected.State.OrderBy(p => p.Key)), Json(actual.State.OrderBy(p => p.Key)));
                (BigInteger Value, Dictionary<string, object?> State) control =
                    SampledInterpreter.RunSingle(withoutPass.Program!, state, 731_941, round);
                Assert.Equal(expected.Value, control.Value);
                Assert.Equal(Json(expected.State.OrderBy(p => p.Key)), Json(control.State.OrderBy(p => p.Key)));
            }
        }
    }
}
