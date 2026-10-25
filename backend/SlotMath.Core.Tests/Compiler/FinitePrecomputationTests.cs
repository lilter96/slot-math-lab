using System.Collections;
using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Optimization;

namespace SlotMath.Core.Tests.Compiler;

[Collection("SamplingOptions")]
public sealed class FinitePrecomputationTests
{
    private static ConstantExpr N(int n) => new()
    {
        Kind = ConstantKind.Integer,
        Value = n.ToString()
    };

    private static PrecomputationValue D(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString()
    };

    private static FieldAccessExpr S(string key) => new()
    {
        Target = "state",
        Path = [key]
    };

    private static Expression Op(BinaryOp op, Expression a, Expression b) => new BinaryExpr
    {
        Op = op,
        Left = a,
        Right = b
    };

    private static Expression Heavy(Expression? x = null, Expression? y = null)
    {
        x ??= S("x");
        y ??= S("y");
        Expression value = N(0);
        for (var j = 1; j <= 12; j++)
        {
            value = Op(BinaryOp.Add, value,
                Op(BinaryOp.Div, Op(BinaryOp.Mul, Op(BinaryOp.Add, x, N(j)), Op(BinaryOp.Add, y, N(j))), N(j)));
        }

        return value;
    }

    private static PrecomputationHint Hint(PrecomputationScope scope = PrecomputationScope.State) => new()
    {
        ExpectedUses = 1_000_000,
        Domains =
        [
            new PrecomputationDomain
            {
                Name = "x",
                Scope = scope,
                Values = [D(0), D(1), D(2), D(3)]
            },
            new PrecomputationDomain
            {
                Name = "y",
                Scope = scope,
                Values = [D(0), D(1), D(2), D(3)]
            }
        ]
    };

    private static Rational Expected(int x, int y)
    {
        BigInteger numerator = 0;
        const int denominator = 27720;
        for (var j = 1; j <= 12; j++)
        {
            numerator += (x + j) * (y + j) * (denominator / j);
        }

        return new Rational(numerator, denominator);
    }

    private static (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval,
        SamplingExpressions Owner) Bind(Expression expression, SamplingTier tier,
            Dictionary<string, object?>? state = null, SamplingShapes? shapes = null,
            IReadOnlyDictionary<string, ExprValue>? constants = null)
    {
        state ??= new Dictionary<string, object?>
        {
            ["x"] = 0,
            ["y"] = 0
        };
        var slots = new Dictionary<string, int>();

        int Slot(string name)
        {
            if (!slots.TryGetValue(name, out var index))
            {
                slots[name] = index = slots.Count;
            }

            return index;
        }

        foreach (var key in state.Keys)
        {
            Slot(key);
        }

        var owner = new SamplingExpressions(Slot, tier, constants, shapes);
        Func<SamplingFrame, ExprValue> eval = owner.Compile(expression);
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], state, true);
        frame.Reset(default);
        return (frame, slots, eval, owner);
    }

    private static Rational Rational(ExprValue value) => new(value.NumberNumerator, value.NumberDenominator);

    [Fact]
    public void CacheIdentityDoesNotChargePrettyPrintedWhitespace()
    {
        Expression body = Heavy();
        for (var shift = 1; shift <= 2; shift++)
        {
            body = Op(BinaryOp.Add, body, Heavy(S("x"), Op(BinaryOp.Add, S("y"), N(shift))));
        }

        Assert.True(JsonSerializer.Serialize(body, JsonOptions.Default).Length > 65536);
        Assert.True(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonOptions.Default)
        {
            WriteIndented = false
        }).Length < 65536);
        Expression annotated = body with
        {
            Precompute = Hint()
        };
        PrecomputationPlan plan = new FinitePrecomputationCache().Get(annotated, _ => null);
        Assert.True(plan.Admitted, plan.Reason);
        Assert.Equal(16, plan.Entries);
        for (var x = 0; x < 4; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                var cells = new SamplingCell[2];
                var slots = new Dictionary<string, int>
                {
                    ["x"] = 0,
                    ["y"] = 1
                };
                var frame = new SamplingFrame(slots, cells, new Dictionary<string, object?>());
                frame.Reset(default);
                frame.Cells[0].SetTyped(ExprValue.Number(x));
                frame.Cells[1].SetTyped(ExprValue.Number(y));
                Assert.True(plan.Table!.TryLookup(frame, [0, 1], out ExprValue actual));
                Assert.Equal(Expected(x, y) + Expected(x, y + 1) + Expected(x, y + 2), Rational(actual));
            }
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void EveryDeclaredInputAndOutsideDomainMissMatchesIndependentRationals(SamplingTier tier)
    {
        Expression body = Heavy() with
        {
            Precompute = Hint()
        };
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval, SamplingExpressions
            Owner) engine = Bind(body, tier);
        PrecomputationPlan plan = engine.Owner.Precomputation(body);
        Assert.True(plan.Admitted, plan.Reason);
        Assert.Equal(16, plan.Entries);
        for (var repeat = 0; repeat < 6; repeat++)
        {
            for (var x = -1; x <= 4; x++)
            {
                for (var y = -1; y <= 4; y++)
                {
                    engine.Frame.Cells[engine.Slots["x"]].SetTyped(ExprValue.Number(x));
                    engine.Frame.Cells[engine.Slots["y"]].SetTyped(ExprValue.Number(y));
                    Assert.Equal(Expected(x, y), Rational(engine.Eval(engine.Frame)));
                    Assert.Equal(Expected(x, y), Rational(ExactExpressionEvaluator.Evaluate(body, new EvalContext
                    {
                        State = new Dictionary<string, object?>
                        {
                            ["x"] = x,
                            ["y"] = y
                        }
                    })));
                }
            }
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void MissingAndDeferredInputsDoNotExposeASkippedBranch(SamplingTier tier)
    {
        PrecomputationHint hint = Hint() with
        {
            Domains =
            [
                new PrecomputationDomain
                {
                    Name = "flag",
                    Values =
                    [
                        new PrecomputationValue
                        {
                            Kind = ConstantKind.Boolean,
                            Value = "true"
                        },
                        new PrecomputationValue
                        {
                            Kind = ConstantKind.Boolean,
                            Value = "false"
                        }
                    ]
                },
                .. Hint().Domains
            ]
        };
        Expression expression = new IfExpr
        {
            Condition = S("flag"),
            ThenExpr = Heavy(),
            ElseExpr = N(99),
            Precompute = hint
        };
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval, SamplingExpressions
            Owner) engine = Bind(expression, tier, new Dictionary<string, object?>
            {
                ["flag"] = false,
                ["y"] = 1
            });
        Assert.True(engine.Owner.Precomputation(expression).Admitted, engine.Owner.Precomputation(expression).Reason);
        Assert.Equal(99, engine.Eval(engine.Frame).AsInteger());
        engine.Frame.Cells[engine.Slots["x"]] = SamplingCell.FromRaw(new object());
        Assert.Equal(99, engine.Eval(engine.Frame).AsInteger());
        engine.Frame.Cells[engine.Slots["flag"]].SetTyped(ExprValue.Bool(true));
        Assert.Throws<ExpressionEvaluationException>(() => engine.Eval(engine.Frame));
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void LexicalInputsAndOutsideMissesKeepOuterScopes(SamplingTier tier)
    {
        Expression body = Heavy(new LocalExpr
        {
            Name = "a"
        }, new LocalExpr
        {
            Name = "b"
        });
        PrecomputationHint hint = Hint(PrecomputationScope.Local) with
        {
            Domains =
            [
                Hint(PrecomputationScope.Local).Domains[0] with
                {
                    Name = "a"
                },
                Hint(PrecomputationScope.Local).Domains[1] with
                {
                    Name = "b"
                }
            ]
        };
        body = body with
        {
            Precompute = hint
        };
        Expression expression = new LetExpr
        {
            Name = "a",
            Value = S("x"),
            Body = new LetExpr
            {
                Name = "b",
                Value = S("y"),
                Body = body
            }
        };
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval, SamplingExpressions
            Owner) engine = Bind(expression, tier);
        var outer = new LexicalBinding("outside", ExprValue.Number(71), null);
        engine.Frame.Locals = outer;
        Assert.True(engine.Owner.Precomputation(body).Admitted, engine.Owner.Precomputation(body).Reason);
        foreach (var x in new[] { 0, 3, 9 })
        {
            engine.Frame.Cells[engine.Slots["x"]].SetTyped(ExprValue.Number(x));
            engine.Frame.Cells[engine.Slots["y"]].SetTyped(ExprValue.Number(2));
            Assert.Equal(Expected(x, 2), Rational(engine.Eval(engine.Frame)));
            Assert.Same(outer, engine.Frame.Locals);
        }
    }

    [Fact]
    public void AFailingDomainEntryDeclinesRatherThanCachingZeroOrAnException()
    {
        Expression body = new IfExpr
        {
            Condition = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = S("x"),
                Right = N(3)
            },
            ThenExpr = Op(BinaryOp.Div, N(1), N(0)),
            ElseExpr = Heavy(),
            Precompute = Hint()
        };
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval, SamplingExpressions
            Owner) engine = Bind(body, SamplingTier.Compiled);
        PrecomputationPlan plan = engine.Owner.Precomputation(body);
        Assert.False(plan.Admitted);
        Assert.StartsWith("domain-error:", plan.Reason);
        engine.Frame.Cells[engine.Slots["x"]].SetTyped(ExprValue.Number(0));
        engine.Frame.Cells[engine.Slots["y"]].SetTyped(ExprValue.Number(2));
        Assert.Equal(Expected(0, 2), Rational(engine.Eval(engine.Frame)));
        engine.Frame.Cells[engine.Slots["x"]].SetTyped(ExprValue.Number(3));
        Assert.Throws<ExpressionEvaluationException>(() => engine.Eval(engine.Frame));
    }

    [Theory]
    [InlineData("uses")]
    [InlineData("entries")]
    [InlineData("work")]
    [InlineData("memory")]
    [InlineData("duplicate")]
    [InlineData("type")]
    public void AdmissionBudgetsAndDomainErrorsOnlyDisableTheOptimization(string test)
    {
        PrecomputationHint hint = Hint();
        if (test == "uses")
        {
            hint = hint with
            {
                ExpectedUses = 1
            };
        }

        if (test == "entries")
        {
            hint = hint with
            {
                MaxEntries = 15
            };
        }

        if (test == "work")
        {
            hint = hint with
            {
                MaxWork = 1
            };
        }

        if (test == "memory")
        {
            hint = hint with
            {
                MaxBytes = 64
            };
        }

        if (test == "duplicate")
        {
            hint = hint with
            {
                Domains =
                [
                    hint.Domains[0] with
                    {
                        Values = [D(0), D(0)]
                    },
                    hint.Domains[1]
                ]
            };
        }

        if (test == "type")
        {
            hint = hint with
            {
                Domains =
                [
                    hint.Domains[0] with
                    {
                        Values =
                        [
                            D(0), new PrecomputationValue
                            {
                                Kind = ConstantKind.Boolean,
                                Value = "true"
                            }
                        ]
                    },
                    hint.Domains[1]
                ]
            };
        }

        Expression body = Heavy() with
        {
            Precompute = hint
        };
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval, SamplingExpressions
            Owner) engine = Bind(body, SamplingTier.Compiled);
        Assert.False(engine.Owner.Precomputation(body).Admitted);
        Assert.Equal(Expected(0, 0), Rational(engine.Eval(engine.Frame)));
    }

    [Fact]
    public void ShortDecisionTreeHoldoutIsRejectedBeforeConstruction()
    {
        Expression body = N(1);
        var domains = new List<PrecomputationDomain>();
        for (var i = 0; i < 5; i++)
        {
            domains.Add(new PrecomputationDomain
            {
                Name = "x" + i,
                Values = Enumerable.Range(0, 8).Select(D).ToArray()
            });
        }

        for (var i = 3; i >= 0; i--)
        {
            body = new IfExpr
            {
                Condition = new CompareExpr
                {
                    Op = CompareOp.Eq,
                    Left = S("x" + i),
                    Right = S("x" + (i + 1))
                },
                ThenExpr = body,
                ElseExpr = N(0)
            };
        }

        body = body with
        {
            Precompute = new PrecomputationHint
            {
                Domains = domains.ToArray(),
                ExpectedUses = 1_000_000
            }
        };
        var cache = new FinitePrecomputationCache();
        PrecomputationPlan plan = cache.Get(body, _ => null);
        Assert.False(plan.Admitted);
        Assert.Equal("not-amortized", plan.Reason);
        Assert.Equal(0, cache.BuiltTables);
        Assert.Equal(0, cache.RetainedBytes);
    }

    [Fact]
    public void NumericGrowthIsBoundedBeforeAnyExhaustiveEvaluation()
    {
        // Build a properly nested squaring chain from the outside in.
        Expression body = new LocalExpr
        {
            Name = "v20"
        };
        for (var i = 20; i >= 1; i--)
        {
            body = new LetExpr
            {
                Name = "v" + i,
                Value = Op(BinaryOp.Mul, new LocalExpr
                {
                    Name = "v" + (i - 1)
                }, new LocalExpr
                {
                    Name = "v" + (i - 1)
                }),
                Body = body
            };
        }

        body = new LetExpr
        {
            Name = "v0",
            Value = S("x"),
            Body = body,
            Precompute = Hint() with
            {
                Domains =
                [
                    new PrecomputationDomain
                    {
                        Name = "x",
                        Values = [D(2)]
                    }
                ]
            }
        };
        PrecomputationPlan plan = new FinitePrecomputationCache().Get(body, _ => null);
        Assert.False(plan.Admitted);
        Assert.Equal("numeric-growth", plan.Reason);
    }

    [Fact]
    public void BodiesWithCancellationMutableParametersOrExternalLocalsDecline()
    {
        Expression[] expressions =
        [
            new ScanExpr
            {
                Source = S("values"),
                Mode = ScanMode.Inclusive,
                MaxItems = 2
            },
            Op(BinaryOp.Add, Heavy(), S("parameter")), Op(BinaryOp.Add, Heavy(), new LocalExpr
            {
                Name = "outside"
            })
        ];
        foreach (Expression expression in expressions)
        {
            Assert.False(new FinitePrecomputationCache().Get(expression with
            {
                Precompute = Hint()
            }, _ => null).Admitted);
        }
    }

    [Fact]
    public void IdentityIncludesBodyCapturedParametersTypesAndDomainOrder()
    {
        var shapes = new SamplingShapes();
        Expression original = Op(BinaryOp.Mul, Heavy(), S("factor")) with
        {
            Precompute = Hint()
        };
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval, SamplingExpressions
            Owner) first = Bind(original, SamplingTier.Compiled, shapes: shapes,
                constants: new Dictionary<string, ExprValue>
                {
                    ["factor"] = ExprValue.Number(2)
                });
        Assert.True(first.Owner.Precomputation(original).Admitted, first.Owner.Precomputation(original).Reason);
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval, SamplingExpressions
            Owner) second = Bind(original, SamplingTier.Compiled, shapes: shapes,
                constants: new Dictionary<string, ExprValue>
                {
                    ["factor"] = ExprValue.Number(3)
                });
        Assert.Equal(Expected(0, 0) * new Rational(2, 1), Rational(first.Eval(first.Frame)));
        Assert.Equal(Expected(0, 0) * new Rational(3, 1), Rational(second.Eval(second.Frame)));
        Expression reversed = original with
        {
            Precompute = Hint() with
            {
                Domains =
                [
                    Hint().Domains[0] with
                    {
                        Values = [D(3), D(2), D(1), D(0)]
                    },
                    Hint().Domains[1]
                ]
            }
        };
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval, SamplingExpressions
            Owner) third = Bind(reversed, SamplingTier.Compiled, shapes: shapes,
                constants: new Dictionary<string, ExprValue>
                {
                    ["factor"] = ExprValue.Number(2)
                });
        Assert.Equal(Expected(0, 0) * new Rational(2, 1), Rational(third.Eval(third.Frame)));
        Assert.Equal(3, shapes.Precomputations.BuiltTables);
    }

    [Fact]
    public void ConcurrentOwnersShareOneImmutableConstructionAndNoPartialRows()
    {
        Expression expression = Heavy() with
        {
            Precompute = Hint()
        };
        var shapes = new SamplingShapes();
        var plans = new PrecomputationPlan[16];
        Parallel.For(0, plans.Length,
            index => plans[index] = new SamplingExpressions(_ => 0, shapes: shapes).Precomputation(expression));
        Assert.True(plans[0].Admitted, plans[0].Reason);
        Assert.All(plans, plan => Assert.Same(plans[0].Table, plan.Table));
        Assert.Equal(1, shapes.Precomputations.BuiltTables);
    }

    [Fact]
    public void NativeLetElidesFrameBindingsOnlyWhenNoFallbackNeedsThem()
    {
        var aggregate = new AggregateExpr
        {
            Func = AggregateFunc.Sum,
            StateKey = "values",
            MaxItems = 2,
            ItemName = "item",
            ItemType = ExprType.Number,
            ValueExpr = Op(BinaryOp.Add, S("item"), new LocalExpr
            {
                Name = "x"
            })
        };
        Expression expression = new LetExpr
        {
            Name = "x",
            Value = N(3),
            Body = aggregate
        };
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval, SamplingExpressions
            Owner) engine = Bind(expression, SamplingTier.Compiled, new Dictionary<string, object?>
            {
                ["values"] = new[] { 1, 2 }
            });
        var outer = new LexicalBinding("x", ExprValue.Number(99), null);
        engine.Frame.Locals = outer;
        Assert.Equal(9, engine.Eval(engine.Frame).AsInteger());
        Assert.Same(outer, engine.Frame.Locals);
    }

    [Fact]
    public void ArrayIteratorInputsUseCurrentValuesRatherThanStaleFrameSlots()
    {
        foreach (var index in new[] { false, true })
        {
            var name = index ? "position" : "entry";
            Expression body = Heavy(S(name), S("y")) with
            {
                Precompute = Hint() with
                {
                    Domains =
                    [
                        Hint().Domains[0] with
                        {
                            Name = name
                        },
                        Hint().Domains[1]
                    ]
                }
            };
            Expression expression = new MapExpr
            {
                StateKey = "values",
                MaxItems = 4,
                ItemName = "entry",
                IndexName = "position",
                ItemType = ExprType.Number,
                Body = body
            };
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval,
                SamplingExpressions Owner) engine = Bind(expression, SamplingTier.Compiled,
                    new Dictionary<string, object?>
                    {
                        ["values"] = new[] { 3, 2, 1, 0 },
                        ["y"] = 1,
                        ["entry"] = 111,
                        ["position"] = 222
                    });
            Assert.True(engine.Owner.Precomputation(body).Admitted, engine.Owner.Precomputation(body).Reason);
            ExprValue result = engine.Eval(engine.Frame);
            for (var i = 0; i < 4; i++)
            {
                Assert.Equal(Expected(index ? i : 3 - i, 1), Rational(result.ArrayValue![i]));
            }

            Assert.Equal(111, engine.Frame.Cells[engine.Slots["entry"]].ReadValue("entry").AsInteger());
            Assert.Equal(222, engine.Frame.Cells[engine.Slots["position"]].ReadValue("position").AsInteger());
        }
    }

    [Fact]
    public void CachedContainersStayBorrowedWhenConsumedByACompiledMap()
    {
        Expression body = new RecordExpr
        {
            Fields =
            [
                new RecordFieldExpr
                {
                    Name = "score",
                    Value = Heavy()
                },
                new RecordFieldExpr
                {
                    Name = "items",
                    Value = new CallExpr
                    {
                        Function = "append",
                        Args = [S("seed"), S("x")]
                    }
                }
            ],
            Precompute = Hint()
        };
        Expression expression = new LetExpr
        {
            Name = "r",
            Value = body,
            Body = new MapExpr
            {
                Source = new LocalExpr
                {
                    Name = "r",
                    Path = ["items"]
                },
                MaxItems = 4,
                ItemName = "item",
                ItemType = ExprType.Number,
                Body = Op(BinaryOp.Add, S("item"), N(1))
            }
        };
        var constants = new Dictionary<string, ExprValue>
        {
            ["seed"] = ExprValue.Array(
                new[] { ExprValue.Number(1), ExprValue.Number(2), ExprValue.Number(3) })
        };
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval, SamplingExpressions
            Owner) engine = Bind(expression, SamplingTier.Compiled, constants: constants);
        PrecomputationPlan plan = engine.Owner.Precomputation(body);
        Assert.True(plan.Admitted, plan.Reason);
        ExprValue stored = plan.Table!.Result(0);
        var before = JsonSerializer.Serialize(stored.ToStateObject(), JsonOptions.Default);
        for (var repeat = 0; repeat < 5; repeat++)
        {
            engine.Frame.Reset(default);
            Assert.Equal(new BigInteger[] { 2, 3, 4, 1 },
                engine.Eval(engine.Frame).ArrayValue!.Select(value => value.AsInteger()));
        }

        Assert.Equal(before, JsonSerializer.Serialize(plan.Table.Result(0).ToStateObject(), JsonOptions.Default));
    }

    [Fact]
    public void MalformedSoftHintsDoNotChangeOrdinaryResultsOrLegacyJson()
    {
        PrecomputationHint[] hints =
        [
            Hint() with
            {
                Domains = null!
            },
            Hint() with
            {
                Domains = [null!]
            },
            Hint() with
            {
                Domains = [new PrecomputationDomain()]
            },
            Hint() with
            {
                Domains =
                [
                    Hint().Domains[0] with
                    {
                        Name = null!
                    },
                    Hint().Domains[1]
                ]
            },
            Hint() with
            {
                Domains =
                [
                    Hint().Domains[0] with
                    {
                        Values =
                        [
                            new PrecomputationValue
                            {
                                Value = null!
                            }
                        ]
                    },
                    Hint().Domains[1]
                ]
            }
        ];
        foreach (PrecomputationHint hint in hints)
        {
            Expression expression = Heavy() with
            {
                Precompute = hint
            };
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval,
                SamplingExpressions Owner) engine = Bind(expression, SamplingTier.Compiled);
            Assert.False(engine.Owner.Precomputation(expression).Admitted);
            Assert.Equal(Expected(0, 0), Rational(engine.Eval(engine.Frame)));
        }

        Assert.DoesNotContain("Precompute", JsonSerializer.Serialize(Heavy()));
        Expression authored = Heavy() with
        {
            Precompute = Hint()
        };
        var json = JsonSerializer.Serialize(authored, JsonOptions.Default);
        Assert.Equal(json,
            JsonSerializer.Serialize(JsonSerializer.Deserialize<Expression>(json, JsonOptions.Default),
                JsonOptions.Default));
        Expression identity = Op(BinaryOp.Add, Heavy(), N(0)) with
        {
            Precompute = Hint()
        };
        Assert.NotNull(ExpressionSimplifier.Simplify(identity).Expression.Precompute);
    }

    [Fact]
    public void ObservableCapturedContainersAreDeclinedWithoutInvokingTheirGetter()
    {
        var probe = new ReadProbe();
        ExprValue captured = ExprValue.Array(probe);
        probe.Reads = 0;
        var element = new CallExpr
        {
            Function = "index",
            Args = [S("parameter"), N(0)]
        };
        Expression body = Op(BinaryOp.Add, Heavy(), element) with
        {
            Precompute = Hint()
        };
        PrecomputationPlan plan =
            new FinitePrecomputationCache().Get(body, name => name == "parameter" ? captured : null);
        Assert.False(plan.Admitted);
        Assert.Equal(0, probe.Reads);
    }

    [Fact]
    public void CachedPlanCountIsBoundedPerProgram()
    {
        var cache = new FinitePrecomputationCache();
        for (var index = 0; index < 64; index++)
        {
            Expression body = Op(BinaryOp.Add, Heavy(), N(index + 1)) with
            {
                Precompute = Hint()
            };
            Assert.True(cache.Get(body, _ => null).Admitted);
        }

        Assert.Equal(64, cache.BuiltTables);
        Assert.Equal("cache-plan-limit", cache.Get(Heavy() with
        {
            Precompute = Hint()
        }, _ => null).Reason);
        Assert.InRange(cache.RetainedBytes, 1, 16 * 1024 * 1024);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedRecordGrowthIsRejectedBeforeExpandingItsLogicalTree(bool comparison)
    {
        Expression body = comparison
            ? new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = new LocalExpr
                {
                    Name = "r24"
                },
                Right = new LocalExpr
                {
                    Name = "r24"
                }
            }
            : new LocalExpr
            {
                Name = "r24"
            };
        for (var index = 24; index >= 1; index--)
        {
            body = new LetExpr
            {
                Name = "r" + index,
                Value = new RecordExpr
                {
                    Fields =
                    [
                        new RecordFieldExpr
                        {
                            Name = "a",
                            Value = new LocalExpr
                            {
                                Name = "r" + (index - 1)
                            }
                        },
                        new RecordFieldExpr
                        {
                            Name = "b",
                            Value = new LocalExpr
                            {
                                Name = "r" + (index - 1)
                            }
                        }
                    ]
                },
                Body = body
            };
        }

        body = new LetExpr
        {
            Name = "r0",
            Value = S("x"),
            Body = body,
            Precompute = Hint() with
            {
                Domains = [Hint().Domains[0]]
            }
        };
        PrecomputationPlan plan = new FinitePrecomputationCache().Get(body, _ => null);
        Assert.False(plan.Admitted);
        Assert.Equal("structural-growth", plan.Reason);
    }

    [Fact]
    public void BooleanConditionEntryUsesTheSameAdmittedTable()
    {
        Expression expression = new CompareExpr
        {
            Op = CompareOp.Gt,
            Left = Heavy(),
            Right = N(120),
            Precompute = Hint()
        };
        var slots = new Dictionary<string, int>
        {
            ["x"] = 0,
            ["y"] = 1
        };
        var owner = new SamplingExpressions(name => slots[name], SamplingTier.Compiled);
        Func<SamplingFrame, bool> condition = owner.BindCondition(expression).Entry;
        var frame = new SamplingFrame(slots, new SamplingCell[2], new Dictionary<string, object?>
        {
            ["x"] = 0,
            ["y"] = 0
        });
        frame.Reset(default);
        Assert.True(owner.Precomputation(expression).Admitted, owner.Precomputation(expression).Reason);
        for (var x = 0; x < 4; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                frame.Cells[0].SetTyped(ExprValue.Number(x));
                frame.Cells[1].SetTyped(ExprValue.Number(y));
                Assert.Equal(Expected(x, y).CompareTo(new Rational(120, 1)) > 0, condition(frame));
            }
        }
    }

    [Fact]
    public void ScalarDomainTypesArePartOfTheSharedTableIdentity()
    {
        Expression body = new RecordExpr
        {
            Fields =
            [
                new RecordFieldExpr
                {
                    Name = "payload",
                    Value = S("payload")
                },
                new RecordFieldExpr
                {
                    Name = "score",
                    Value = Heavy()
                }
            ]
        };
        var cache = new FinitePrecomputationCache();
        var plans = new PrecomputationPlan[2];
        for (var kind = 0; kind < 2; kind++)
        {
            var payload = new PrecomputationDomain
            {
                Name = "payload",
                Values =
                [
                    new PrecomputationValue
                    {
                        Kind = kind == 0 ? ConstantKind.Integer : ConstantKind.String,
                        Value = "1"
                    }
                ]
            };
            plans[kind] = cache.Get(body with
            {
                Precompute = Hint() with
                {
                    Domains = [payload, .. Hint().Domains]
                }
            }, _ => null);
            Assert.True(plans[kind].Admitted, plans[kind].Reason);
        }

        Assert.Equal(2, cache.BuiltTables);
        Assert.NotSame(plans[0].Table, plans[1].Table);
        Assert.Equal(ExprType.Number, plans[0].Table!.Result(0).RecordValue!["payload"].Kind);
        Assert.Equal(ExprType.String, plans[1].Table!.Result(0).RecordValue!["payload"].Kind);
    }

    private sealed class ReadProbe : IReadOnlyList<ExprValue>
    {
        public int Reads { get; set; }

        public ExprValue this[int index]
        {
            get
            {
                Reads++;
                return ExprValue.Number(1);
            }
        }

        public int Count => 1;

        public IEnumerator<ExprValue> GetEnumerator()
        {
            Reads++;
            yield return ExprValue.Number(1);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
