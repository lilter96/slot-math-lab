using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Compiler;

public sealed class SamplingRecordStorageTests
{
    private static SamplingFrame Frame() => new(new Dictionary<string, int>
    {
        ["value"] = 0
    },
        new SamplingCell[1], new Dictionary<string, object?>(), true);

    [Fact]
    public void TypedProjectionMatchesObjectTraversalIncludingLocatedFailures()
    {
        ExprValue value = ExprValue.Record(new Dictionary<string, ExprValue>
        {
            ["items"] = ExprValue.Array(new[]
            {
                ExprValue.Record(new Dictionary<string, ExprValue>
                {
                    ["n"] = ExprValue.Number(3)
                }),
                ExprValue.Symbol("token"), ExprValue.Null
            }),
            ["null"] = ExprValue.Null
        });
        string[][] paths =
        [
            ["items", "0", "n"], ["items", "1"], ["items", "2"], ["null"],
            ["items", "3"], ["items", "bad"], ["items", "0", "missing"], ["items", "", "n"], [],
            ["items", "0", "n", "x"]
        ];
        foreach (var path in paths)
        {
            try
            {
                ExprValue expected = StateValues.Read((object)value, path, "chosen.location");
                Assert.Equal(expected, StateValues.Read(value, path, "chosen.location"));
            }
            catch (ExpressionEvaluationException expected)
            {
                var actual = Assert.Throws<ExpressionEvaluationException>(() =>
                    StateValues.Read(value, path, "chosen.location"));
                Assert.Equal(expected.Code, actual.Code);
                Assert.Equal(expected.Location, actual.Location);
                Assert.Equal(expected.Message, actual.Message);
            }
        }
    }

    [Fact]
    public void PublicRecordStillCopiesCallerOwnedFields()
    {
        var fields = new Dictionary<string, ExprValue>
        {
            ["x"] = ExprValue.Number(1)
        };
        ExprValue value = ExprValue.Record(fields);
        fields["x"] = ExprValue.Number(2);
        Assert.Equal(ExprValue.Number(1), value.RecordValue!["x"]);
    }

    [Fact]
    public void RentalsStayDistinctWithinRoundAndExportsSurviveNextRoundReuse()
    {
        SamplingFrame frame = Frame();
        string[] names = ["x"];
        frame.Reset(default);
        SamplingRecord first = frame.NewRecord(names);
        first.Fields[0] = ExprValue.Number(1);
        ExprValue original = first.Finish();
        frame.Cells[0].SetTyped(original);
        Dictionary<string, object?> snapshot = frame.Export();
        SamplingRecord second = frame.NewRecord(names);
        second.Fields[0] = ExprValue.Number(2);
        Assert.NotSame(first, second);
        Assert.Equal(ExprValue.Number(1), original.RecordValue!["x"]);
        frame.Reset(default);
        SamplingRecord reused = frame.NewRecord(names);
        Assert.Same(first, reused);
        reused.Fields[0] = ExprValue.Number(3);
        var exported = Assert.IsAssignableFrom<IDictionary<string, object?>>(snapshot["value"]);
        Assert.Equal(BigInteger.One, exported["x"]);
    }

    [Fact]
    public void PersistentStateDisablesRecordReuse()
    {
        SamplingFrame frame = Frame();
        string[] names = ["x"];
        frame.Reset(default);
        SamplingRecord original = frame.NewRecord(names);
        original.Fields[0] = ExprValue.Number(7);
        frame.Cells[0].SetTyped(original.Finish());
        frame.Reset(default, [true]);
        SamplingRecord next = frame.NewRecord(names);
        next.Fields[0] = ExprValue.Number(9);
        Assert.NotSame(original, next);
        Assert.Equal(ExprValue.Number(7), frame.Cells[0].Value.RecordValue!["x"]);
    }

    [Fact]
    public void BudgetOverflowNeverReusesALiveRecord()
    {
        SamplingFrame frame = Frame();
        frame.Reset(default);
        string[] names = ["x"];
        var values = new ExprValue[1_100];
        for (var index = 0; index < values.Length; index++)
        {
            SamplingRecord record = frame.NewRecord(names);
            record.Fields[0] = ExprValue.Number(index);
            values[index] = record.Finish();
        }

        for (var index = 0; index < values.Length; index++)
        {
            Assert.Equal(ExprValue.Number(index), values[index].RecordValue!["x"]);
        }
    }

    [Fact]
    public void SymbolFlagsAndStoredNormalizationMatchOrdinaryRecords()
    {
        SamplingFrame frame = Frame();
        frame.Reset(default);
        SamplingRecord record = frame.NewRecord(["nested"]);
        record.Fields[0] = ExprValue.Array(new[] { ExprValue.Symbol("s"), ExprValue.Null });
        ExprValue value = record.Finish();
        Assert.True(value.ContainsSymbols);
        SamplingCell cell = SamplingCell.Typed(value);
        Assert.False(cell.Value.ContainsSymbols);
        Assert.Equal(ExprType.String, cell.Value.RecordValue!["nested"].ArrayValue![0].Kind);
        Assert.Equal(ExprType.Symbol, value.RecordValue!["nested"].ArrayValue![0].Kind);
    }

    [Fact]
    public void WarmedArenaRecordsAllocateNoPerRoundPayloads()
    {
        SamplingFrame frame = Frame();
        string[] names = ["x", "y"];

        void Round()
        {
            frame.Reset(default);
            for (var index = 0; index < 64; index++)
            {
                SamplingRecord record = frame.NewRecord(names);
                record.Fields[0] = ExprValue.Number(index);
                record.Fields[1] = ExprValue.Number(index + 1);
                _ = record.Finish();
            }
        }

        for (var index = 0; index < 10; index++)
        {
            Round();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 100; index++)
        {
            Round();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1_024, $"Warmed records allocated {allocated} bytes.");
    }

    [Fact]
    public void FailedGeneratedRecordConstructionCanReuseBucketWithNewSymbolFlags()
    {
        var layout = new Dictionary<string, int>
        {
            ["symbol"] = 0,
            ["second"] = 1
        };
        Expression expression = new RecordExpr
        {
            Fields =
            [
                new RecordFieldExpr
                {
                    Name = "first",
                    Value = Field("symbol")
                },
                new RecordFieldExpr
                {
                    Name = "second",
                    Value = Field("second")
                }
            ]
        };
        var initial = new Dictionary<string, object?>
        {
            ["symbol"] = ExprValue.Symbol("old")
        };
        var frame = new SamplingFrame(layout, new SamplingCell[layout.Count], initial, true);
        Func<SamplingFrame, ExprValue> code =
            new SamplingExpressions(name => layout[name], SamplingTier.Compiled).Compile(expression);

        frame.Reset(default);
        var error = Assert.Throws<ExpressionEvaluationException>(() => code(frame));
        Assert.Equal("EVAL_MISSING_STATE", error.Code);

        frame.Reset(default);
        frame.Cells[layout["symbol"]].SetTyped(ExprValue.String("plain"));
        frame.Cells[layout["second"]].SetTyped(ExprValue.Number(12));
        ExprValue actual = code(frame);

        var freshFrame = new SamplingFrame(layout, new SamplingCell[layout.Count], initial);
        freshFrame.Reset(default);
        freshFrame.Cells[layout["symbol"]].SetTyped(ExprValue.String("plain"));
        freshFrame.Cells[layout["second"]].SetTyped(ExprValue.Number(12));
        ExprValue expected = code(freshFrame);

        Assert.Equal(expected, actual);
        Assert.False(actual.ContainsSymbols);
        Assert.Equal(ExprValue.String("plain"), actual.RecordValue!["first"]);
        Assert.Equal(ExprValue.Number(12), actual.RecordValue["second"]);

        frame.Reset(default);
        frame.Cells[layout["symbol"]].SetTyped(ExprValue.String("plain"));
        frame.Cells[layout["second"]].SetTyped(ExprValue.Number(12));
        ExprValue reused = code(frame);
        Assert.Same(actual.RecordValue, reused.RecordValue);
    }

    [Fact]
    public void NestedCodegenRecordsAndSharedShapesBindNamesAndProjectionPerOwner()
    {
        Expression Make(string firstName, string secondName, string selectedName)
        {
            Expression fold = new FoldExpr
            {
                StateKey = "values",
                AccName = "acc",
                ItemName = "item",
                Init = Record((firstName, Number(1)), (secondName, Number(2))),
                Body = Record(
                    (firstName, Add(Field("acc", firstName), Field("item"))),
                    (secondName, Add(Field("acc", secondName), Number(10)))),
                MaxItems = 4
            };
            return Record(
                ("envelope", Record(("result", fold), ("copy", new ProjectExpr
                {
                    Value = fold,
                    Path = [selectedName]
                }))),
                ("selected", new ProjectExpr
                {
                    Value = fold,
                    Path = [selectedName]
                }));
        }

        var shapes = new SamplingShapes();
        var state = new Dictionary<string, object?>
        {
            ["values"] = new object[] { 3, 4 }
        };
        Expression first = Make("west", "east", "west");
        Expression second = Make("east", "west", "east");
        var firstOwner = new SamplingExpressions(name => name switch { "values" => 0, "acc" => 1, "item" => 2, _ => 3 },
            SamplingTier.Compiled, shapes: shapes);
        var secondOwner = new SamplingExpressions(
            name => name switch { "values" => 0, "acc" => 1, "item" => 2, _ => 3 },
            SamplingTier.Compiled, shapes: shapes);
        var layout = new Dictionary<string, int>
        {
            ["values"] = 0,
            ["acc"] = 1,
            ["item"] = 2
        };
        var firstFrame = new SamplingFrame(layout, new SamplingCell[3], state);
        var secondFrame = new SamplingFrame(layout, new SamplingCell[3], state);
        firstFrame.Reset(default);
        secondFrame.Reset(default);
        ExprValue firstActual = firstOwner.Compile(first)(firstFrame);
        ExprValue secondActual = secondOwner.Compile(second)(secondFrame);

        Assert.Equal(ExactExpressionEvaluator.Evaluate(first, new EvalContext
        {
            State = state
        }), firstActual);
        Assert.Equal(ExactExpressionEvaluator.Evaluate(second, new EvalContext
        {
            State = state
        }), secondActual);
        Assert.Equal(ExprValue.Number(8), firstActual.RecordValue!["selected"]);
        Assert.Equal(ExprValue.Number(8), secondActual.RecordValue!["selected"]);
        Assert.Equal(1, shapes.Count);
    }

    [Fact]
    public void LargeRecordBucketsStopAtFieldBudgetAndExportsRemainStable()
    {
        SamplingFrame frame = Frame();
        var names = Enumerable.Range(0, 64).Select(index => $"f{index}").ToArray();
        var live = new ExprValue[257];
        frame.Reset(default);
        for (var recordIndex = 0; recordIndex < live.Length; recordIndex++)
        {
            SamplingRecord record = frame.NewRecord(names);
            for (var fieldIndex = 0; fieldIndex < names.Length; fieldIndex++)
            {
                record.Fields[fieldIndex] = ExprValue.Number(recordIndex * 100 + fieldIndex);
            }

            live[recordIndex] = record.Finish();
            if (recordIndex > 0)
            {
                Assert.NotSame(live[recordIndex - 1].RecordValue, live[recordIndex].RecordValue);
            }
        }

        frame.Cells[0].SetTyped(live[0]);
        Dictionary<string, object?> snapshot = frame.Export();
        var exported = Assert.IsAssignableFrom<IDictionary<string, object?>>(snapshot["value"]);
        Assert.Equal(new BigInteger(12), Assert.IsType<BigInteger>(exported["f12"]));

        frame.Reset(default);
        SamplingRecord reused = frame.NewRecord(names);
        Assert.Same(live[0].RecordValue, reused);
        for (var fieldIndex = 0; fieldIndex < names.Length; fieldIndex++)
        {
            reused.Fields[fieldIndex] = ExprValue.Number(-fieldIndex);
        }

        _ = reused.Finish();
        Assert.Equal(new BigInteger(12), Assert.IsType<BigInteger>(exported["f12"]));

        frame.Reset(default);
        var nextRound = new SamplingRecord[257];
        for (var recordIndex = 0; recordIndex < nextRound.Length; recordIndex++)
        {
            nextRound[recordIndex] = frame.NewRecord(names);
        }

        Assert.Same(live[255].RecordValue, nextRound[255]);
        Assert.NotSame(live[256].RecordValue, nextRound[256]);
    }

    [Fact]
    public void RunnerCollectorDisablesRecordReuseBeforeFirstRound()
    {
        CompileResult compiled = new GraphCompiler().Compile(RecordGraph());
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        var plan = Assert.IsAssignableFrom<ICompiledSampling<Dictionary<string, object?>, BigInteger>>(
            compiled.Program);

        ISamplingRunner<Dictionary<string, object?>, BigInteger> unmeasured =
            plan.CreateRunner(new Dictionary<string, object?>());
        ISamplingRunner<Dictionary<string, object?>, BigInteger> measured = plan.CreateRunner(
            new Dictionary<string, object?>(),
            new MeasurementCollector(Array.Empty<MeasurementDefinition>()));

        SamplingFrame unmeasuredFrame = RunnerFrame(unmeasured);
        SamplingFrame measuredFrame = RunnerFrame(measured);
        string[] names = ["probe"];
        unmeasuredFrame.Reset(default);
        SamplingRecord pooledFirst = unmeasuredFrame.NewRecord(names);
        unmeasuredFrame.Reset(default);
        SamplingRecord pooledNext = unmeasuredFrame.NewRecord(names);
        Assert.Same(pooledFirst, pooledNext);

        measuredFrame.Reset(default);
        SamplingRecord ordinaryFirst = measuredFrame.NewRecord(names);
        measuredFrame.Reset(default);
        SamplingRecord ordinaryNext = measuredFrame.NewRecord(names);
        Assert.NotSame(ordinaryFirst, ordinaryNext);
    }

    private static SamplingFrame RunnerFrame(ISamplingRunner<Dictionary<string, object?>, BigInteger> runner)
    {
        FieldInfo frameField = runner.GetType().GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return Assert.IsType<SamplingFrame>(frameField.GetValue(runner));
    }

    private static GraphConfig RecordGraph()
    {
        Dictionary<string, Port> ports = new()
        {
            ["state"] = new Port
            {
                Name = "state",
                Type = PortType.State
            }
        };
        Node[] nodes =
        [
            new ModifyStateNode
            {
                Id = "result",
                ExpressionId = "result",
                OutputKey = "result",
                Inputs = ports,
                Outputs = ports
            },
            new ModifyStateNode
            {
                Id = "win",
                ExpressionId = "win",
                OutputKey = "win",
                Inputs = ports,
                Outputs = ports
            },
            new MetricsSinkNode
            {
                Id = "sink",
                WinStateKey = "win",
                WinCap = 1000,
                Inputs = ports
            }
        ];
        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Nodes = nodes,
            Edges = nodes.Zip(nodes.Skip(1)).Select(pair => new Edge
            {
                Id = pair.First.Id + pair.Second.Id,
                SourceNodeId = pair.First.Id,
                SourcePort = "state",
                TargetNodeId = pair.Second.Id,
                TargetPort = "state"
            }).ToArray(),
            Expressions = new Dictionary<string, Expression>
            {
                ["result"] = Record(("outer", Record(("x", Number(1)), ("y", Number(2))))),
                ["win"] = Number(0)
            },
            InitialState = new Dictionary<string, JsonElement>()
        };
    }

    private static FieldAccessExpr Field(params string[] path) => new()
    {
        Target = "state",
        Path = path
    };

    private static RecordExpr Record(params (string Name, Expression Value)[] fields) => new()
    {
        Fields = fields.Select(field => new RecordFieldExpr
        {
            Name = field.Name,
            Value = field.Value
        }).ToArray()
    };

    private static BinaryExpr Add(Expression left, Expression right) => new()
    {
        Op = BinaryOp.Add,
        Left = left,
        Right = right
    };

    private static ConstantExpr Number(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString(CultureInfo.InvariantCulture)
    };
}
