using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Random;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Benchmarks;

[Collection("SamplingOptions")]
public class SamplingArrayLifetimeTests
{
    private static readonly Dictionary<string, Port> StatePort = new()
    {
        ["state"] = new Port
        {
            Name = "state",
            Type = PortType.State
        }
    };

    [Fact]
    public void ArenaReusesExactLengthsWhenRequestOrderChangesWithoutAliasingWithinARound()
    {
        var arena = new SamplingArrayArena();
        var lengths = new[] { 1, 64, 5, 30, 5, 1 };
        var previous = new Dictionary<int, HashSet<ExprValue[]>>();
        for (var round = 0; round < 8; round++)
        {
            arena.Reset();
            var current = new List<ExprValue[]>();
            foreach (var length in round % 2 == 0 ? lengths : lengths.Reverse())
            {
                ExprValue[] values = arena.Rent(length);
                Assert.Equal(length, values.Length);
                Assert.DoesNotContain(current, other => ReferenceEquals(other, values));
                if (round > 0)
                {
                    Assert.Contains(previous[length], other => ReferenceEquals(other, values));
                }

                System.Array.Fill(values, ExprValue.Number(current.Count + 100 * round));
                current.Add(values);
            }

            for (var index = 0; index < current.Count; index++)
            {
                Assert.All(current[index], value => Assert.Equal(index + 100 * round, value.AsInteger()));
            }

            previous = current.GroupBy(values => values.Length)
                .ToDictionary(group => group.Key, group => group.ToHashSet());
        }
    }

    [Fact]
    public void ArenaBoundsTotalRetentionAcrossLengthsAndAllocatesDistinctOverflowBuffers()
    {
        var arena = new SamplingArrayArena();
        ExprValue[][] first = Enumerable.Range(0, 1100).Select(_ => arena.Rent(1)).ToArray();
        Assert.Equal(1100, first.Distinct().Count());
        arena.Reset();
        ExprValue[][] second = Enumerable.Range(0, 1100).Select(_ => arena.Rent(1)).ToArray();
        for (var index = 0; index < 1024; index++)
        {
            Assert.Same(first[index], second[index]);
        }

        for (var index = 1024; index < 1100; index++)
        {
            Assert.DoesNotContain(first, other => ReferenceEquals(other, second[index]));
        }

        ExprValue[] uncachedLength = arena.Rent(2);
        arena.Reset();
        Assert.NotSame(uncachedLength, arena.Rent(2));
        Assert.Empty(arena.Rent(0));
        Assert.Equal(65, arena.Rent(65).Length);
        Assert.Throws<OverflowException>(() => arena.Rent(-1));
    }

    [Fact]
    public void ManySmallBuffersShareTheOriginalElementBudgetWithLargeBuffers()
    {
        var arena = new SamplingArrayArena();
        ExprValue[][] small = Enumerable.Range(0, 600).Select(_ => arena.Rent(1)).ToArray();
        ExprValue[][] large = Enumerable.Range(0, 4).Select(_ => arena.Rent(4096)).ToArray();
        for (var index = 0; index < small.Length; index++)
        {
            small[index][0] = ExprValue.Number(index);
        }

        for (var index = 0; index < large.Length; index++)
        {
            System.Array.Fill(large[index], ExprValue.Number(index + 1000));
        }

        arena.Reset();
        foreach (ExprValue[] values in small)
        {
            Assert.Same(values, arena.Rent(1));
        }

        // 600 + 3 * 4096 fits the unchanged 16,384-element budget; the fourth
        // large buffer does not, even though the buffer-count budget has room.
        for (var index = 0; index < 3; index++)
        {
            Assert.Same(large[index], arena.Rent(4096));
        }

        ExprValue[] overflow = arena.Rent(4096);
        Assert.DoesNotContain(large, values => ReferenceEquals(values, overflow));
        for (var index = 0; index < small.Length; index++)
        {
            Assert.Equal(index, small[index][0].AsInteger());
        }

        for (var index = 0; index < large.Length; index++)
        {
            Assert.All(large[index], value => Assert.Equal(index + 1000, value.AsInteger()));
        }
    }

    private static FieldAccessExpr F(string key) => new()
    {
        Target = "state",
        Path = [key]
    };

    private static ConstantExpr N(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString()
    };

    private static CallExpr Call(string function, params Expression[] args) => new()
    {
        Function = function,
        Args = args
    };

    private static BinaryExpr Add(Expression left, Expression right) => new()
    {
        Op = BinaryOp.Add,
        Left = left,
        Right = right
    };

    private static MapExpr Map(string key, Expression body, ExprType type = ExprType.Number) => new()
    {
        StateKey = key,
        ItemName = "item",
        ItemType = type,
        Body = body
    };

    private static ModifyStateNode Write(string key) => new()
    {
        Id = key,
        OutputKey = key,
        ExpressionId = key,
        Inputs = StatePort,
        Outputs = StatePort
    };

    private static Edge Connect(string from, string to, string port = "state") => new()
    {
        Id = from + "-" + to,
        SourceNodeId = from,
        SourcePort = port,
        TargetNodeId = to,
        TargetPort = "state"
    };

    private static GraphConfig Graph(Dictionary<string, Expression> expressions, Dict initial, Node[]? prefix = null)
    {
        Node[] nodes = (prefix ?? []).Concat(expressions.Keys.Select(Write)).Append(new MetricsSinkNode
        {
            Id = "sink",
            WinStateKey = "win",
            WinCap = 1,
            Inputs = StatePort
        }).ToArray();
        initial["win"] = 0;
        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Nodes = nodes,
            Expressions = expressions,
            Edges = nodes.Zip(nodes.Skip(1), (left, right) => Connect(left.Id, right.Id)).ToArray(),
            InitialState =
                initial.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value))
        };
    }

    private static ICompiledSampling<Dict, BigInteger> Compile(GraphConfig graph)
    {
        CompileResult compiled = new GraphCompiler().Compile(graph);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors.Select(error => error.Message)));
        return Assert.IsAssignableFrom<ICompiledSampling<Dict, BigInteger>>(compiled.Program);
    }

    private static object?[] Array(object? value) => Assert.IsType<object?[]>(value);

    private static BigInteger[] Numbers(object? value) =>
        Array(value).Select(item => Assert.IsType<BigInteger>(item)).ToArray();

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void SavedExportsAndNestedAliasesRemainIndependentAcrossRoundsAndRunners(SamplingTier tier)
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = tier;
            var expressions = new Dictionary<string, Expression>
            {
                ["mapped"] = Map("source", Add(F("item"), Call("tonumber", F("salt")))),
                ["alias"] = F("mapped"),
                ["filtered"] = new FilterExpr
                {
                    StateKey = "mapped",
                    ItemName = "item",
                    ItemType = ExprType.Number,
                    Predicate = new CompareExpr
                    {
                        Op = CompareOp.Gte,
                        Left = F("item"),
                        Right = N(7)
                    }
                },
                ["changed"] = Map("mapped", Add(F("item"), N(100))),
                ["nested"] = Call("append", Call("append", F("empty"), F("alias")), F("changed")),
                ["copied"] = Map("nested", F("item"), ExprType.Array)
            };
            var initialState = new Dict
            {
                ["source"] = new[] { 0, 1, 2 },
                ["empty"] = System.Array.Empty<int>(),
                ["salt"] = "0"
            };
            Node[] prefix =
            [
                new DrawNode
                {
                    Id = "draw",
                    StateWriteKey = "salt",
                    DrawWeights =
                    [
                        new DrawWeight
                        {
                            OutcomeId = "2",
                            Weight = 1
                        },
                        new DrawWeight
                        {
                            OutcomeId = "5",
                            Weight = 2
                        }
                    ],
                    Outputs = StatePort
                }
            ];
            GraphConfig graph = Graph(expressions, initialState, prefix);
            ICompiledSampling<Dict, BigInteger> plan = Compile(graph);
            foreach (var length in new[] { -1, 0, 1, 5, 31, 64, 65 })
            {
                var source = Enumerable.Range(0, length < 0 ? 3 : length).ToArray();
                Dict initial = length < 0
                    ? new Dict()
                    : new Dict
                    {
                        ["source"] = source
                    };
                ISamplingRunner<Dict, BigInteger>[] runners = new[]
                {
                    plan.CreateRunner(initial), plan.CreateRunner(initial)
                };
                var saved = new List<(Dict State, BigInteger Hash)>();
                for (var round = 0; round < 160; round++)
                {
                    ISamplingRunner<Dict, BigInteger> runner = runners[round % 2];
                    Assert.Equal(BigInteger.Zero, runner.Run(new SeededRandom(round), default));
                    Dict state = runner.ExportState();
                    var salt = int.Parse(Assert.IsType<string>(state["salt"]));
                    BigInteger[] expected = source.Select(value => new BigInteger(value + salt)).ToArray();
                    Assert.Equal(expected, Numbers(state["mapped"]));
                    Assert.Equal(expected, Numbers(state["alias"]));
                    Assert.Equal(expected.Where(value => value >= 7), Numbers(state["filtered"]));
                    Assert.Equal(expected.Select(value => value + 100), Numbers(state["changed"]));
                    foreach (var key in new[] { "nested", "copied" })
                    {
                        var pair = Array(state[key]);
                        Assert.Equal(expected, Numbers(pair[0]));
                        Assert.Equal(expected.Select(value => value + 100), Numbers(pair[1]));
                    }

                    if (round % 17 == 0)
                    {
                        saved.Add((state, StateHasher.CanonicalHash(state)));
                    }
                }

                Assert.All(saved, snapshot => Assert.Equal(snapshot.Hash, StateHasher.CanonicalHash(snapshot.State)));
            }
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void LargeRoundsDoNotRecycleLiveArraysWhenTheCacheBudgetIsExceeded()
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = SamplingTier.Compiled;
            GraphConfig graph = Graph(new Dictionary<string, Expression>
            {
                ["mapped"] = Map("source", Add(F("item"), F("__iter_repeat__"))),
                ["history"] = Call("append", F("history"), F("mapped"))
            }, new Dict
            {
                ["source"] = new[] { 0, 1 },
                ["history"] = System.Array.Empty<int>()
            });
            graph = graph with
            {
                Nodes = graph.Nodes.Prepend(new LoopNode
                {
                    Id = "repeat",
                    MaxIterations = 300,
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
                }).ToArray(),
                Edges =
                [
                    Connect("repeat", "mapped", "body"), Connect("mapped", "history"), Connect("repeat", "sink", "exit")
                ]
            };
            ISamplingRunner<Dict, BigInteger> runner = Compile(graph).CreateRunner(new Dict());
            var saved = new List<(Dict State, BigInteger Hash)>();
            for (var round = 0; round < 4; round++)
            {
                runner.Run(new SeededRandom(round), default);
                Dict state = runner.ExportState();
                var history = Array(state["history"]);
                Assert.Equal(300, history.Length);
                for (var index = 0; index < history.Length; index++)
                {
                    Assert.Equal(new BigInteger[] { index, index + 1 }, Numbers(history[index]));
                }

                saved.Add((state, StateHasher.CanonicalHash(state)));
            }

            Assert.All(saved, snapshot => Assert.Equal(snapshot.Hash, StateHasher.CanonicalHash(snapshot.State)));
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void PersistentArraysKeepTheirValuesAndEarlierExportsAcrossTrajectoryResets()
    {
        SamplingTier previous = SamplingOptions.Tier;
        try
        {
            SamplingOptions.Tier = SamplingTier.Compiled;
            GraphConfig graph = Graph(new Dictionary<string, Expression>
            {
                ["source"] = Map("source", Add(F("item"), N(1)))
            }, new Dict
            {
                ["source"] = new[] { 0, 1 }
            });
            ISamplingRunner<Dict, BigInteger> runner =
                Compile(graph).CreateRunner(new Dict(), persistentKeys: ["source"]);
            var saved = new List<(Dict State, BigInteger Hash)>();
            for (var round = 1; round <= 80; round++)
            {
                runner.Run(new SeededRandom(round), default);
                Dict state = runner.ExportState();
                Assert.Equal(new BigInteger[] { round, round + 1 }, Numbers(state["source"]));
                if (round % 10 == 0)
                {
                    saved.Add((state, StateHasher.CanonicalHash(state)));
                }
            }

            runner.ResetTrajectory();
            runner.Run(new SeededRandom(42), default);
            Assert.Equal(new BigInteger[] { 1, 2 }, Numbers(runner.ExportState()["source"]));
            Assert.All(saved, snapshot => Assert.Equal(snapshot.Hash, StateHasher.CanonicalHash(snapshot.State)));
        }
        finally
        {
            SamplingOptions.Tier = previous;
        }
    }

    [Fact]
    public void PartialArrayErrorsAndCancellationDoNotCorruptLaterResultsOrExports()
    {
        var layout = new Dictionary<string, int>
        {
            ["items"] = 0,
            ["values"] = 1,
            ["item"] = 2,
            ["output"] = 3
        };
        var frame = new SamplingFrame(layout, new SamplingCell[4], new Dict
        {
            ["items"] = new object[] { 0, 1, 9 },
            ["values"] = new object[] { 10, 20 }
        }, true);
        MapExpr expression = Map("items", Call("index", F("values"), F("item")));
        Func<SamplingFrame, ExprValue> evaluate =
            new SamplingExpressions(key => layout[key], SamplingTier.Compiled).Compile(expression);
        var saved = new List<(Dict State, BigInteger Hash)>();
        for (var round = 0; round < 5; round++)
        {
            frame.Reset(default);
            var error = Assert.Throws<ExpressionEvaluationException>(() => evaluate(frame));
            Assert.Equal(EvalErrorCodes.IndexOutOfRange, error.Code);
            Assert.Equal("index[9]", error.Location);
            frame.Reset(default);
            frame.Cells[0].SetTyped(ExprValue.Array([ExprValue.Number(1), ExprValue.Number(0)]));
            frame.Cells[3].SetTyped(evaluate(frame));
            Dict state = frame.Export();
            Assert.Equal(new BigInteger[] { 20, 10 }, Numbers(state["output"]));
            saved.Add((state, StateHasher.CanonicalHash(state)));
            Assert.Throws<OperationCanceledException>(() => frame.Reset(new CancellationToken(true)));
        }

        Assert.All(saved, snapshot => Assert.Equal(snapshot.Hash, StateHasher.CanonicalHash(snapshot.State)));
    }
}
