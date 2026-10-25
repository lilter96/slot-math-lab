using System.Collections;
using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;
using SlotMath.Core.Optimization;

namespace SlotMath.Core.Tests.Expressions;

[Collection("SamplingOptions")]
public sealed class FoldWhileTests
{
    private static ConstantExpr N(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString()
    };

    private static ConstantExpr B(bool value) => new()
    {
        Kind = ConstantKind.Boolean,
        Value = value.ToString()
    };

    private static FieldAccessExpr S(string key) => new()
    {
        Target = "state",
        Path = [key]
    };

    private static BinaryExpr Add(Expression a, Expression b) => new()
    {
        Op = BinaryOp.Add,
        Left = a,
        Right = b
    };

    private static CompareExpr Less(Expression a, Expression b) => new()
    {
        Op = CompareOp.Lt,
        Left = a,
        Right = b
    };

    private static FoldExpr Sum() => new()
    {
        StateKey = "values",
        MaxItems = 64,
        AccName = "acc",
        ItemName = "entry",
        IndexName = "index",
        ItemType = ExprType.Number,
        Init = N(0),
        WhileCondition = Less(S("acc"), N(50)),
        Body = Add(S("acc"), S("entry"))
    };

    private static FoldExpr Quantify(bool any) => Sum() with
    {
        ItemType = ExprType.Boolean,
        Init = B(!any),
        WhileCondition = any
            ? new NotExpr
            {
                Expr = S("acc")
            }
            : S("acc"),
        Body = S("entry")
    };

    private static (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval) Bind(
        Expression expression, Dictionary<string, object?> state, SamplingTier tier,
        IReadOnlyDictionary<string, ExprValue>? constants = null)
    {
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

        Func<SamplingFrame, ExprValue> eval =
            new SamplingExpressions(Slot, tier, constants).Compile(expression);
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], state);
        frame.Reset(default);
        return (frame, slots, eval);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnyAndAllMatchIndependentTruthTablesIncludingEmptyArrays(bool any)
    {
        FoldExpr fold = Quantify(any);
        var state = new Dictionary<string, object?>
        {
            ["values"] = Array.Empty<bool>()
        };
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval)[] engines =
            Enum.GetValues<SamplingTier>().Select(tier => Bind(fold, state, tier)).ToArray();
        for (var count = 0; count <= 9; count++)
        {
            for (var mask = 0; mask < 1 << count; mask++)
            {
                var values = Enumerable.Range(0, count).Select(index => (mask & (1 << index)) != 0).ToArray();
                var expected = any ? values.Any(value => value) : values.All(value => value);
                state["values"] = values;
                Assert.Equal(expected, ExactExpressionEvaluator.Evaluate(fold, new EvalContext
                {
                    State = state
                }).BoolValue);
                foreach ((SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval)
                         engine in engines)
                {
                    engine.Frame.Cells[engine.Slots["values"]] = SamplingCell.FromRaw(values);
                    Assert.Equal(expected, engine.Eval(engine.Frame).BoolValue);
                }
            }
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void PrefixRecurrenceMatchesAnIndependentLoopWithNegativeAndWideIntegers(SamplingTier tier)
    {
        var random = new System.Random(193);
        FoldExpr fold = Sum();
        var state = new Dictionary<string, object?>
        {
            ["values"] = Array.Empty<BigInteger>()
        };
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval) engine = Bind(fold,
            state, tier);
        for (var trial = 0; trial < 128; trial++)
        {
            BigInteger[] values = Enumerable.Range(0, random.Next(65)).Select(index =>
                trial % 7 == 0 && index == 1 ? BigInteger.One << 100 : random.Next(-10, 20)).ToArray();
            BigInteger expected = 0;
            foreach (BigInteger value in values)
            {
                if (expected >= 50)
                {
                    break;
                }

                expected += value;
            }

            state["values"] = values;
            Assert.Equal(expected, ExactExpressionEvaluator.Evaluate(fold, new EvalContext
            {
                State = state
            }).AsInteger());
            engine.Frame.Cells[engine.Slots["values"]] = SamplingCell.FromRaw(values);
            Assert.Equal(expected, engine.Eval(engine.Frame).AsInteger());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheGuardRunsBeforeTheFirstGetterAndAnUnvisitedTailIsNeverRead(bool initialFalse)
    {
        FoldExpr fold = initialFalse
            ? Sum() with
            {
                WhileCondition = B(false),
                Init = N(7)
            }
            : Quantify(true);
        var list = new IndexedProbe(initialFalse ? new object?[] { 1, 2 } : [true, null], initialFalse ? 0 : 1);
        var state = new Dictionary<string, object?>
        {
            ["values"] = list
        };
        ExprValue expected = initialFalse ? ExprValue.Number(7) : ExprValue.Bool(true);
        Assert.Equal(expected, ExactExpressionEvaluator.Evaluate(fold, new EvalContext
        {
            State = state
        }));
        Assert.Equal(initialFalse ? 0 : 1, list.Reads);
        foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
        {
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval) engine = Bind(
                fold, new Dictionary<string, object?>
                {
                    ["values"] = Array.Empty<object>()
                }, tier);
            // Probe the operator boundary directly; importing external state is a separate conversion phase.
            engine.Frame.Cells[engine.Slots["values"]] = new SamplingCell
            {
                Present = true,
                HasRaw = true,
                Raw = list
            };
            list.Reads = 0;
            for (var call = 0; call <= SamplingOptions.HotThreshold; call++)
            {
                Assert.Equal(expected, engine.Eval(engine.Frame));
            }

            Assert.Equal(initialFalse ? 0 : SamplingOptions.HotThreshold + 1, list.Reads);
        }
    }

    [Fact]
    public void ReachedGetterErrorsPrecedeBodyErrorsAndExplicitProducerEvaluationRemainsEager()
    {
        FoldExpr fold = Sum() with
        {
            WhileCondition = B(true),
            Body = S("bodyMissing")
        };
        var list = new IndexedProbe([1], 0);
        var state = new Dictionary<string, object?>
        {
            ["values"] = list
        };
        Assert.Throws<InvalidOperationException>(() => ExactExpressionEvaluator.Evaluate(fold, new EvalContext
        {
            State = state
        }));
        fold = fold with
        {
            Source = S("values"),
            StateKey = null,
            WhileCondition = B(false)
        };
        Assert.Throws<InvalidOperationException>(() => ExactExpressionEvaluator.Evaluate(fold, new EvalContext
        {
            State = state
        }));
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void CancellationAndGuardFailuresRestoreAllScopedBindings(SamplingTier tier)
    {
        FoldExpr fold = Sum() with
        {
            WhileCondition = Less(S("acc"), N(2)),
            Body = Add(S("acc"), N(1))
        };
        var state = new Dictionary<string, object?>
        {
            ["values"] = new[] { 1, 2 },
            ["acc"] = 111,
            ["entry"] = 222,
            ["index"] = 333
        };
        (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval) engine = Bind(fold,
            state, tier);
        for (var call = 0; call <= SamplingOptions.HotThreshold; call++)
        {
            engine.Frame.CancellationToken = new CancellationToken(true);
            Assert.Throws<OperationCanceledException>(() => engine.Eval(engine.Frame));
            engine.Frame.CancellationToken = default;
            Assert.Equal(2, engine.Eval(engine.Frame).AsInteger());
            foreach (var (key, value) in new[] { ("acc", 111), ("entry", 222), ("index", 333) })
            {
                Assert.Equal(value, engine.Frame.Cells[engine.Slots[key]].ReadValue(key).AsInteger());
            }
        }

        using var cancellation = new CancellationTokenSource();
        var list = new IndexedProbe([1, 2], 9)
        {
            OnRead = cancellation.Cancel
        };
        engine.Frame.CancellationToken = cancellation.Token;
        engine.Frame.Cells[engine.Slots["values"]] = new SamplingCell
        {
            Present = true,
            HasRaw = true,
            Raw = list
        };
        Assert.Throws<OperationCanceledException>(() => engine.Eval(engine.Frame));
        Assert.Equal(1, list.Reads);
        Assert.Equal(111, engine.Frame.Cells[engine.Slots["acc"]].ReadValue("acc").AsInteger());
    }

    [Fact]
    public void AnEmptySourceDoesNotRunTheGuardButStillPollsCancellation()
    {
        FoldExpr fold = Sum() with
        {
            WhileCondition = S("unreached")
        };
        var state = new Dictionary<string, object?>
        {
            ["values"] = Array.Empty<int>()
        };
        Assert.Equal(0, ExactExpressionEvaluator.Evaluate(fold, new EvalContext
        {
            State = state
        }).AsInteger());
        Assert.Throws<OperationCanceledException>(() => ExactExpressionEvaluator.Evaluate(fold, new EvalContext
        {
            State = state,
            CancellationToken = new CancellationToken(true)
        }));
        foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
        {
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval) engine =
                Bind(fold, state, tier);
            Assert.Equal(0, engine.Eval(engine.Frame).AsInteger());
            engine.Frame.CancellationToken = new CancellationToken(true);
            Assert.Throws<OperationCanceledException>(() => engine.Eval(engine.Frame));
        }
    }

    [Fact]
    public void BoundsAndStreamingInputsAreCheckedBeforeAnOtherwiseFalseGuard()
    {
        FoldExpr fold = Sum() with
        {
            MaxItems = 1,
            WhileCondition = B(false)
        };
        var state = new Dictionary<string, object?>
        {
            ["values"] = new[] { 1, 2 }
        };
        Assert.Equal("EVAL_COLLECTION_BUDGET", Assert.Throws<ExpressionEvaluationException>(() =>
            ExactExpressionEvaluator.Evaluate(fold, new EvalContext
            {
                State = state
            })).Code);
        state["values"] = new StreamingProbe();
        Assert.Equal("EVAL_TYPE_ERROR", Assert.Throws<ExpressionEvaluationException>(() =>
            ExactExpressionEvaluator.Evaluate(fold, new EvalContext
            {
                State = state
            })).Code);
        foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
        {
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval) engine = Bind(
                fold, new Dictionary<string, object?>
                {
                    ["values"] = Array.Empty<int>()
                }, tier);
            engine.Frame.Cells[engine.Slots["values"]] = new SamplingCell
            {
                Present = true,
                HasRaw = true,
                Raw = state["values"]
            };
            Assert.Equal("EVAL_TYPE_ERROR",
                Assert.Throws<ExpressionEvaluationException>(() => engine.Eval(engine.Frame)).Code);
        }
    }

    [Fact]
    public void GuardScopeIsStrictAndNestedIteratorsCanBindTheirOwnItem()
    {
        var context = new TypeCheckContext
        {
            StateFields =
            [
                ExpressionShapes.Array(ExpressionShapes.Field("value", ExprType.Number)) with
                {
                    Name = "values"
                },
                ExpressionShapes.Field("entry", ExprType.Boolean)
            ]
        };
        Assert.NotEmpty(ExpressionTypeChecker.Check(Sum() with
        {
            WhileCondition = N(1)
        }, context));
        Assert.NotEmpty(ExpressionTypeChecker.Check(Sum() with
        {
            WhileCondition = S("entry")
        }, context));
        Assert.NotEmpty(ExpressionTypeChecker.Check(Sum() with
        {
            MaxItems = null
        }, context));
        foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
        {
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval) engine = Bind(
                Sum() with
                {
                    WhileCondition = S("entry")
                }, new Dictionary<string, object?>
                {
                    ["values"] = new[] { 1 },
                    ["entry"] = true
                }, tier);
            Assert.Equal("EVAL_MISSING_STATE",
                Assert.Throws<ExpressionEvaluationException>(() => engine.Eval(engine.Frame)).Code);
            Assert.True(engine.Frame.Cells[engine.Slots["entry"]].ReadValue("entry").BoolValue);
        }

        FoldExpr nested = Sum() with
        {
            WhileCondition = new CallExpr
            {
                Function = "index",
                Args =
                [
                    new MapExpr
                    {
                        StateKey = "controls",
                        MaxItems = 1,
                        ItemName = "entry",
                        ItemType = ExprType.Boolean,
                        Body = S("entry")
                    },
                    N(0)
                ]
            }
        };
        var state = new Dictionary<string, object?>
        {
            ["values"] = new[] { 1, 2, 3 },
            ["controls"] = new[] { true }
        };
        Assert.Equal(6, ExactExpressionEvaluator.Evaluate(nested, new EvalContext
        {
            State = state
        }).AsInteger());
        foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
        {
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval) engine =
                Bind(nested, state, tier);
            Assert.Equal(6, engine.Eval(engine.Frame).AsInteger());
        }
    }

    [Fact]
    public void InitSourceAndGuardErrorsKeepTheirOrder()
    {
        FoldExpr fold = Sum() with
        {
            StateKey = null,
            Source = S("sourceMissing"),
            Init = S("initMissing"),
            WhileCondition = S("guardMissing")
        };
        var state = new Dictionary<string, object?>
        {
            ["values"] = new[] { 1 }
        };
        Assert.Equal("initMissing", Assert.Throws<ExpressionEvaluationException>(() =>
            ExactExpressionEvaluator.Evaluate(fold, new EvalContext
            {
                State = state
            })).Location);
        fold = fold with
        {
            Init = N(0)
        };
        Assert.Equal("sourceMissing", Assert.Throws<ExpressionEvaluationException>(() =>
            ExactExpressionEvaluator.Evaluate(fold, new EvalContext
            {
                State = state
            })).Location);
        fold = fold with
        {
            Source = S("values")
        };
        Assert.Equal("guardMissing", Assert.Throws<ExpressionEvaluationException>(() =>
            ExactExpressionEvaluator.Evaluate(fold, new EvalContext
            {
                State = state
            })).Location);
    }

    [Fact]
    public void IrCostGuardRewritesLexicalUsesAndLegacyJsonKeepTheNewControlFlow()
    {
        FoldExpr fold = Sum() with
        {
            MaxItems = 5
        };
        Assert.Equal(37, ExpressionCost.Compute(fold));
        Assert.Equal(long.MaxValue, ExpressionCost.Compute(fold with
        {
            MaxItems = null
        }));
        ExpressionIr ir = ExpressionIrBuilder.Build(fold, TypeCheckContext.Default);
        IrOperation operation = ir.Root.Operations.Single(op => op.Code == IrOp.FoldWhile);
        Assert.Equal(new[] { "while-before-item", "body" }, operation.Regions.Select(region => region.Role));
        Assert.True((operation.Effects.Flags & IrEffect.Cancellation) != 0);
        var rewritten = Assert.IsType<FoldExpr>(ExpressionSimplifier.Simplify(fold with
        {
            WhileCondition = new NotExpr
            {
                Expr = B(false)
            }
        }).Expression);
        Assert.True(bool.Parse(Assert.IsType<ConstantExpr>(rewritten.WhileCondition).Value));
        var let = new LetExpr
        {
            Name = "limit",
            Value = N(1),
            Body = fold with
            {
                WhileCondition = new CompareExpr
                {
                    Op = CompareOp.Lt,
                    Left = S("acc"),
                    Right = new LocalExpr
                    {
                        Name = "limit"
                    }
                }
            }
        };
        Assert.IsType<LetExpr>(ExpressionSimplifier.Simplify(let).Expression);
        var json = JsonSerializer.Serialize<Expression>(fold, JsonOptions.Default);
        Assert.Contains("whileCondition", json);
        Assert.Equal(json,
            JsonSerializer.Serialize(JsonSerializer.Deserialize<Expression>(json, JsonOptions.Default),
                JsonOptions.Default));
        Assert.DoesNotContain("whileCondition", JsonSerializer.Serialize<Expression>(fold with
        {
            WhileCondition = null
        }));
    }

    [Fact]
    public void NativeShapeSharingKeepsOrdinaryAndShortCircuitFoldsDistinct()
    {
        var slots = new Dictionary<string, int>();

        int Slot(string key)
        {
            if (!slots.TryGetValue(key, out var index))
            {
                slots[key] = index = slots.Count;
            }

            return index;
        }

        var shapes = new SamplingShapes();
        var owner = new SamplingExpressions(Slot, SamplingTier.Compiled, shapes: shapes);
        FoldExpr guarded = Quantify(true),
            ordinary = guarded with
            {
                WhileCondition = null
            };
        Func<SamplingFrame, ExprValue> first = owner.Compile(ordinary), second = owner.Compile(guarded);
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], new Dictionary<string, object?>
        {
            ["values"] = new[] { true, false }
        });
        frame.Reset(default);
        Assert.False(first(frame).BoolValue);
        Assert.True(second(frame).BoolValue);
        Assert.False(first(frame).BoolValue);
    }

    [Fact]
    public void AnExpressionSourceNormalizesOnlyTheSelectedSymbolElement()
    {
        FoldExpr fold = Sum() with
        {
            StateKey = null,
            Source = S("symbols"),
            MaxItems = 2,
            ItemType = ExprType.String,
            WhileCondition = Less(S("index"), N(1)),
            Body = new CallExpr
            {
                Function = "toNumber",
                Args = [S("entry")]
            }
        };
        var state = new Dictionary<string, object?>
        {
            ["symbols"] = ExprValue.Array([ExprValue.Symbol("7"), ExprValue.Symbol("unvisited")])
        };
        Assert.Equal(7, ExactExpressionEvaluator.Evaluate(fold, new EvalContext
        {
            State = state
        }).AsInteger());
        foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
        {
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval) engine =
                Bind(fold, state, tier);
            Assert.Equal(7, engine.Eval(engine.Frame).AsInteger());
        }
    }

    [Fact]
    public void RecordAccumulatorsRetainTheirFieldsAcrossThePreItemGuard()
    {
        ProjectExpr Part(string key)
        {
            return new ProjectExpr
            {
                Value = S("acc"),
                Path = [key]
            };
        }

        FoldExpr fold = Sum() with
        {
            Init = new RecordExpr
            {
                Fields =
                [
                    new RecordFieldExpr
                    {
                        Name = "sum",
                        Value = N(0)
                    },
                    new RecordFieldExpr
                    {
                        Name = "count",
                        Value = N(0)
                    }
                ]
            },
            WhileCondition = Less(Part("count"), N(2)),
            Body = new RecordExpr
            {
                Fields =
                [
                    new RecordFieldExpr
                    {
                        Name = "sum",
                        Value = Add(Part("sum"), S("entry"))
                    },
                    new RecordFieldExpr
                    {
                        Name = "count",
                        Value = Add(Part("count"), N(1))
                    }
                ]
            }
        };
        var state = new Dictionary<string, object?>
        {
            ["values"] = new[] { 1, 2, 3 },
            ["acc"] = 999
        };
        var results = new List<ExprValue>
        {
            ExactExpressionEvaluator.Evaluate(fold, new EvalContext
            {
                State = state
            })
        };
        foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
        {
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval) engine =
                Bind(fold, state, tier);
            results.Add(engine.Eval(engine.Frame));
            Assert.Equal(999, engine.Frame.Cells[engine.Slots["acc"]].ReadValue("acc").AsInteger());
        }

        foreach (ExprValue result in results)
        {
            Assert.Equal(3, result.RecordValue!["sum"].AsInteger());
            Assert.Equal(2, result.RecordValue["count"].AsInteger());
        }
    }

    [Fact]
    public void SimplificationRetainsRepeatedCancellationBearingGuards()
    {
        FoldExpr condition = Quantify(true);
        Expression expression = new IfExpr
        {
            Condition = condition,
            ThenExpr = new IfExpr
            {
                Condition = condition,
                ThenExpr = N(1),
                ElseExpr = N(2)
            },
            ElseExpr = N(3)
        };
        Expression simplified = ExpressionSimplifier.Simplify(expression).Expression;
        var values = new IndexedProbe([true], 9);
        Assert.Equal(1, ExactExpressionEvaluator.Evaluate(simplified, new EvalContext
        {
            State = new Dictionary<string, object?>
            {
                ["values"] = values
            }
        }).AsInteger());
        Assert.Equal(2, values.Reads);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(4097)]
    public void InvalidOrMissingBoundsFailEvenWithAFalseGuardAndImmutableSources(int? maximum)
    {
        FoldExpr fold = Sum() with
        {
            MaxItems = maximum,
            WhileCondition = B(false)
        };
        var state = new Dictionary<string, object?>
        {
            ["values"] = new[] { 1 }
        };
        Assert.Equal("EVAL_COLLECTION_BUDGET", Assert.Throws<ExpressionEvaluationException>(() =>
            ExactExpressionEvaluator.Evaluate(fold, new EvalContext
            {
                State = state
            })).Code);
        foreach (var immutable in new[] { false, true })
        {
            IReadOnlyDictionary<string, ExprValue>? facts = immutable
                ? new Dictionary<string, ExprValue>
                {
                    ["values"] = ExprValue.Array([ExprValue.Number(1)])
                }
                : null;
            foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
            {
                (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval) engine =
                    Bind(fold, state, tier, facts);
                Assert.Equal("EVAL_COLLECTION_BUDGET",
                    Assert.Throws<ExpressionEvaluationException>(() => engine.Eval(engine.Frame)).Code);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QuantifiersRejectAReachedNonBooleanEvenAtTheLastPosition(bool any)
    {
        FoldExpr fold = Quantify(any);
        var state = new Dictionary<string, object?>
        {
            ["values"] = new[] { 1 }
        };
        Assert.Equal("EVAL_TYPE_ERROR", Assert.Throws<ExpressionEvaluationException>(() =>
            ExactExpressionEvaluator.Evaluate(fold, new EvalContext
            {
                State = state
            })).Code);
        foreach (SamplingTier tier in Enum.GetValues<SamplingTier>())
        {
            (SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue> Eval) engine =
                Bind(fold, state, tier);
            Assert.Equal("EVAL_TYPE_ERROR",
                Assert.Throws<ExpressionEvaluationException>(() => engine.Eval(engine.Frame)).Code);
        }
    }

    [Theory]
    [InlineData("board")]
    [InlineData(null)]
    public void BoardAliasesCannotExposeTheUnavailableCurrentItem(string? target)
    {
        FoldExpr fold = Sum() with
        {
            ItemName = "rows",
            WhileCondition = Less(new FieldAccessExpr
            {
                Target = target,
                Path = ["rows"]
            }, N(1)),
            Body = Add(S("acc"), S("rows"))
        };
        var context = new TypeCheckContext
        {
            BoardFields = TypeCheckContext.Default.BoardFields,
            CellFields = TypeCheckContext.Default.CellFields,
            StateFields =
            [
                ExpressionShapes.Array(ExpressionShapes.Field("value", ExprType.Number)) with
                {
                    Name = "values"
                },
                ExpressionShapes.Field("rows", ExprType.Number)
            ]
        };
        Assert.NotEmpty(ExpressionTypeChecker.Check(fold, context));
    }

    private sealed class StreamingProbe : IEnumerable
    {
        public IEnumerator GetEnumerator() =>
            throw new InvalidOperationException("Streaming enumeration must not occur.");
    }

    private sealed class IndexedProbe(object?[] items, int throwFrom) : IList
    {
        public int Reads { get; set; }
        public Action? OnRead { get; init; }

        public object? this[int index]
        {
            get
            {
                Reads++;
                if (index >= throwFrom)
                {
                    throw new InvalidOperationException("Unvisited getter was read.");
                }

                OnRead?.Invoke();
                return items[index];
            }
            set => throw new NotSupportedException();
        }

        public int Count => items.Length;
        public bool IsFixedSize => true;
        public bool IsReadOnly => true;
        public bool IsSynchronized => false;
        public object SyncRoot => this;

        public IEnumerator GetEnumerator() =>
            throw new InvalidOperationException("Indexed arrays must not be enumerated.");

        public int Add(object? value) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(object? value) => throw new NotSupportedException();
        public int IndexOf(object? value) => throw new NotSupportedException();
        public void Insert(int index, object? value) => throw new NotSupportedException();
        public void Remove(object? value) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
        public void CopyTo(Array array, int index) => throw new NotSupportedException();
    }
}
