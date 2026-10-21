using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Math.Structured;

public sealed class MarkovLoopTests
{
    private static JsonObject Fixture(int horizon)
    {
        JsonObject graph = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestData", "Markov", "finite-cycle.json")))!.AsObject();
        graph["nodes"]![0]!["maxIterations"] = horizon;
        return graph;
    }

    private static JsonNode Expr(Expression expression) =>
        JsonSerializer.SerializeToNode(expression, JsonOptions.Default)!;

    private static ConstantExpr N(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString(CultureInfo.InvariantCulture)
    };

    private static FieldAccessExpr F(string key) => new()
    {
        Target = "state",
        Path = [key]
    };

    private static BinaryExpr Add(Expression x, Expression y) => new()
    {
        Op = BinaryOp.Add,
        Left = x,
        Right = y
    };

    private static CompareExpr Eq(Expression x, Expression y) => new()
    {
        Op = CompareOp.Eq,
        Left = x,
        Right = y
    };

    private static Expression Choice() => new IfExpr
    {
        Condition = Eq(F("choice"), new ConstantExpr
        {
            Kind = ConstantKind.String,
            Value = "1"
        }),
        ThenExpr = N(1),
        ElseExpr = N(0)
    };

    private static CompileResult Compile(JsonObject graph)
    {
        var config = graph.Deserialize<GraphConfig>(JsonOptions.Default)!;
        CompileResult compiled = new GraphCompiler().Compile(config);
        Assert.True(compiled.IsValid, string.Join("; ", compiled.Errors.Select(error => error.Message)));
        return compiled;
    }

    private static StructuredExactOptions Options(int states = 32) => new()
    {
        Loops = LoopMode.Moments,
        ChainLoopsAbove = 0,
        LoopMatrixMaxStates = states,
        PruneLoopMassBelow = null,
        MaxTime = TimeSpan.FromSeconds(15)
    };

    private static StructuredExactResult Evaluate(CompileResult compiled, StructuredExactOptions? options = null)
    {
        StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled, options ?? Options());
        Assert.True(result.Succeeded, result.DeclinedReason);
        return result;
    }

    private static void Powered(StructuredExactResult result) =>
        Assert.Contains(result.LoopNotes,
            note => note.Contains("finite-state matrix powers", StringComparison.Ordinal));

    private static JsonObject RandomModel(int horizon, bool fractional = false)
    {
        JsonObject graph = Fixture(horizon);
        var draw = new DrawNode
        {
            Id = "draw",
            StateWriteKey = "choice",
            Inputs = new Dictionary<string, Port>
            {
                ["in"] = new()
                {
                    Name = "in",
                    Type = PortType.Number
                }
            },
            Outputs = new Dictionary<string, Port>
            {
                ["out"] = new()
                {
                    Name = "out",
                    Type = PortType.Number
                }
            },
            DrawWeights =
            [
                new DrawWeight
                {
                    OutcomeId = "0",
                    Weight = 1
                },
                new DrawWeight
                {
                    OutcomeId = "1",
                    Weight = 1
                }
            ]
        };
        graph["nodes"]!.AsArray().Add(JsonSerializer.SerializeToNode<Node>(draw, JsonOptions.Default));
        graph["edges"]![0]!["targetNodeId"] = "draw";
        graph["edges"]!.AsArray().Add(JsonSerializer.SerializeToNode(new Edge
        {
            Id = "draw-pay",
            SourceNodeId = "draw",
            SourcePort = "out",
            TargetNodeId = "pay",
            TargetPort = "in"
        }, JsonOptions.Default));
        Expression reward = Add(F("phase"), Choice());
        if (fractional)
        {
            // Payout precision is declared by paytables, including state-authored payouts.
            graph["symbols"] = JsonSerializer.SerializeToNode(new[]
            {
                new Symbol
                {
                    Id = "unit",
                    Name = "Unit"
                }
            }, JsonOptions.Default);
            graph["paytables"] = JsonSerializer.SerializeToNode(new[]
            {
                new Paytable
                {
                    Id = "precision",
                    Entries =
                    [
                        new PaytableEntry
                        {
                            SymbolId = "unit",
                            Counts = [1],
                            Payouts = ["1/3"]
                        }
                    ]
                }
            }, JsonOptions.Default);
            reward = new BinaryExpr
            {
                Op = BinaryOp.Mul,
                Left = reward,
                Right = new ConstantExpr
                {
                    Kind = ConstantKind.Rational,
                    Value = "1/3"
                }
            };
        }

        graph["expressions"]!["pay"] = Expr(Add(F("win"), reward));
        graph["expressions"]!["advance"] = Expr(Choice());
        return graph;
    }

    private static void ObserveExit(JsonObject graph)
    {
        var after = new ModifyStateNode
        {
            Id = "after",
            ExpressionId = "after",
            OutputKey = "win",
            Inputs = new Dictionary<string, Port>
            {
                ["in"] = new()
                {
                    Name = "in",
                    Type = PortType.Number
                }
            },
            Outputs = new Dictionary<string, Port>
            {
                ["out"] = new()
                {
                    Name = "out",
                    Type = PortType.Number
                }
            }
        };
        graph["nodes"]!.AsArray().Add(JsonSerializer.SerializeToNode<Node>(after, JsonOptions.Default));
        graph["edges"]![2]!["targetNodeId"] = "after";
        graph["edges"]!.AsArray().Add(JsonSerializer.SerializeToNode(new Edge
        {
            Id = "after-sink",
            SourceNodeId = "after",
            SourcePort = "out",
            TargetNodeId = "sink",
            TargetPort = "in"
        }, JsonOptions.Default));
        graph["expressions"]!["after"] = Expr(Add(F("win"), new BinaryExpr
        {
            Op = BinaryOp.Mul,
            Left = N(100),
            Right = F("phase")
        }));
    }

    [Theory]
    [InlineData(99999)]
    [InlineData(100000)]
    public void ClosedPeriodicJsonGraphIsExactUnderAWorkBudgetTooSmallForIteration(int horizon)
    {
        CompileResult compiled = Compile(Fixture(horizon));
        StructuredExactResult result = Evaluate(compiled, Options() with
        {
            MaxWork = 100000
        });
        Powered(result);
        Assert.Equal(new Rational(4 * (horizon / 2) + horizon % 2, 1), result.ExpectedWin);
        Assert.Equal(Rational.Zero, result.Variance(compiled.WinScale));
        Assert.Equal(Rational.One, result.HitFrequency);
        Assert.Equal(Rational.One, Assert.Single(result.Loops).ExpectedCapHits);
        Assert.Equal(Rational.Zero, result.PrunedMass);
        StructuredExactResult iterative = StructuredExactEvaluator.Evaluate(compiled,
            Options(0) with
            {
                MaxWork = 100000
            });
        Assert.False(iterative.Succeeded);
        Assert.Contains("work budget", iterative.DeclinedReason);
    }

    [Fact]
    public void DefaultAutomaticEvaluationUsesMatrixMomentsWhenTheFullLawExceedsItsAllowance()
    {
        CompileResult compiled = Compile(Fixture(100000));
        StructuredExactResult result = Evaluate(compiled, new StructuredExactOptions
        {
            MaxWork = 100000,
            MaxTime = TimeSpan.FromSeconds(15)
        });
        Powered(result);
        Assert.Equal(new Rational(200000, 1), result.ExpectedWin);
        Assert.Equal(Rational.Zero, result.Variance(compiled.WinScale));
    }

    [Fact]
    public void DefaultAutoProvesTheWholeDeterministicLawWithoutAccumulatedWinStates()
    {
        CompileResult compiled = Compile(Fixture(100000));
        StructuredExactResult result = Evaluate(compiled, new StructuredExactOptions());
        Powered(result);
        Assert.Contains(result.LoopNotes,
            note => note.Contains("determine the complete reward law", StringComparison.Ordinal));
        Dist<BigInteger>.Entry atom = Assert.Single(result.Distribution!.Entries);
        Assert.Equal(new BigInteger(200000), atom.Value);
        Assert.Equal(result.Distribution.Denominator, atom.Numerator);
        Assert.True(result.Terms < 1000, $"terms={result.Terms}");
        Assert.True(result.Work < 10000, $"work={result.Work}");
    }

    [Fact]
    public void AutoMatrixProbePreservesTheFullLawOfNondegenerateRewards()
    {
        CompileResult compiled = Compile(RandomModel(64));
        StructuredExactResult result = Evaluate(compiled, new StructuredExactOptions());
        StructuredExactResult reference = Evaluate(compiled, Options() with
        {
            Loops = LoopMode.Law
        });
        Assert.Contains(result.LoopNotes,
            note => note.Contains("conditional reward variance", StringComparison.Ordinal));
        Assert.NotNull(result.Distribution);
        Assert.Equal(reference.ExpectedWin, result.ExpectedWin);
        Assert.Equal(reference.SecondMoment, result.SecondMoment);
        Assert.Equal(reference.Distribution!.Denominator, result.Distribution.Denominator);
        Assert.Equal(reference.Distribution.Entries.ToDictionary(entry => entry.Value, entry => entry.Numerator),
            result.Distribution.Entries.ToDictionary(entry => entry.Value, entry => entry.Numerator));
    }

    [Fact]
    public void AnActivePayoutCapStillRequiresTheDistribution()
    {
        JsonObject graph = RandomModel(64);
        graph["nodes"]![3]!["winCap"] = 1;
        CompileResult compiled = Compile(graph);
        StructuredExactResult moments = StructuredExactEvaluator.Evaluate(compiled, Options());
        Assert.False(moments.Succeeded);
        StructuredExactResult result = Evaluate(compiled, new StructuredExactOptions
        {
            MaxTime = TimeSpan.FromSeconds(15)
        });
        Rational positive = Rational.One - new Rational(1, BigInteger.One << 64);
        Assert.Equal(positive, result.ExpectedWin);
        Assert.Equal(positive, result.HitFrequency);
        Assert.Equal(positive * (Rational.One - positive), result.Variance(compiled.WinScale));
        Assert.NotNull(result.Distribution);
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void FailingExpressionsPastTheHorizonAreNeverExecuted(int horizon, bool succeeds)
    {
        JsonObject graph = Fixture(horizon);
        graph["expressions"]!["advance"] = Expr(Add(F("phase"), N(1)));
        graph["expressions"]!["pay"] = Expr(Add(F("win"), new IfExpr
        {
            Condition = Eq(F("phase"), N(64)),
            ThenExpr = new BinaryExpr
            {
                Op = BinaryOp.Div,
                Left = N(1),
                Right = N(0)
            },
            ElseExpr = N(1)
        }));
        StructuredExactResult result = StructuredExactEvaluator.Evaluate(Compile(graph), Options(128));
        Assert.Equal(succeeds, result.Succeeded);
        if (succeeds)
        {
            Powered(result);
            Assert.Equal(new Rational(64, 1), result.ExpectedWin);
        }
    }

    [Theory]
    [InlineData(64, false)]
    [InlineData(65, true)]
    [InlineData(255, false)]
    public void ConditionalRewardsAndFractionalUnitsMatchIndependentMomentFormulas(int horizon, bool fractional)
    {
        CompileResult compiled = Compile(RandomModel(horizon, fractional));
        StructuredExactResult result = Evaluate(compiled);
        Powered(result);
        var divisor = fractional ? 3 : 1;
        Assert.Equal(new Rational(2 * horizon - 1, 2 * divisor),
            result.ExpectedWin / new Rational(compiled.WinScale, 1));
        Assert.Equal(new Rational(4 * horizon - 3, 4 * divisor * divisor), result.Variance(compiled.WinScale));
        Assert.Equal(Rational.One - new Rational(1, BigInteger.One << horizon), result.HitFrequency);
        Assert.Equal(Rational.One, Assert.Single(result.Loops).ExpectedCapHits);
    }

    [Fact]
    public void ExitStateRemainsCorrelatedWithEarlierRewardsAndLaterGraphExpressions()
    {
        const int horizon = 65;
        JsonObject graph = RandomModel(horizon);
        ObserveExit(graph);
        CompileResult compiled = Compile(graph);
        StructuredExactResult result = Evaluate(compiled);
        Powered(result);
        Assert.Equal(new Rational(2 * horizon + 99, 2), result.ExpectedWin);
        Assert.Equal(new Rational(4 * horizon + 10197, 4), result.Variance(compiled.WinScale));
        StructuredExactResult iterative = Evaluate(compiled, Options(0));
        Assert.Equal(iterative.ExpectedWin, result.ExpectedWin);
        Assert.Equal(iterative.SecondMoment, result.SecondMoment);
        Assert.Equal(iterative.HitFrequency, result.HitFrequency);

        Dist<BigInteger> distribution = ExactInterpreter.Evaluate(compiled.ReferenceProgram!,
                new Dictionary<string, object?>(), StateHasher.CanonicalHash,
                new ExactConfig
                {
                    Budget = new Budget
                    {
                        MaxBranches = 300000,
                        MaxTime = TimeSpan.FromSeconds(10)
                    }
                })
            .ValueDistribution();
        Rational mean = Rational.Zero, second = Rational.Zero;
        foreach (Dist<BigInteger>.Entry entry in distribution.Entries)
        {
            var probability = new Rational(entry.Numerator, distribution.Denominator);
            mean += probability * new Rational(entry.Value, 1);
            second += probability * new Rational(entry.Value * entry.Value, 1);
        }

        Assert.Equal(mean, result.ExpectedWin);
        Assert.Equal(second, result.SecondMoment);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(129)]
    public void AbsorptionOnTheLastBodyIterationStillCountsAsACapExit(int horizon)
    {
        JsonObject graph = RandomModel(horizon);
        graph["nodes"]![0]!["stopConditionId"] = "stop";
        graph["expressions"]!["stop"] = Expr(Eq(F("phase"), N(1)));
        graph["expressions"]!["pay"] = Expr(Add(F("win"), N(1)));
        CompileResult compiled = Compile(graph);
        StructuredExactResult result = Evaluate(compiled);
        Powered(result);
        var tail = new Rational(1, BigInteger.One << horizon);
        Rational mean = 2 * (Rational.One - tail);
        Assert.Equal(mean, result.ExpectedWin);
        Assert.Equal(new Rational(6, 1) - (4 * horizon + 6) * tail - mean * mean, result.Variance(compiled.WinScale));
        Assert.Equal(2 * tail, Assert.Single(result.Loops).ExpectedCapHits);
        Assert.Equal(Rational.One, result.HitFrequency);
    }

    [Theory]
    [InlineData(64, 1)]
    [InlineData(65, 0)]
    public void StopChecksAtTheHorizonPreserveDeclaredExitReasons(int horizon, int capHits)
    {
        JsonObject graph = Fixture(horizon);
        graph["nodes"]![0]!["stopConditionId"] = "stop";
        graph["nodes"]![0]!["exitReason"] = Expr(new ConstantExpr
        {
            Kind = ConstantKind.String,
            Value = "authoredStop"
        });
        graph["expressions"]!["stop"] = Expr(Eq(F("phase"), N(64)));
        graph["expressions"]!["advance"] = Expr(Add(F("phase"), N(1)));
        graph["expressions"]!["pay"] = Expr(Add(F("win"), N(1)));
        ObserveExit(graph);
        graph["expressions"]!["after"] = Expr(Add(F("win"), new IfExpr
        {
            Condition = Eq(F("__exitReason_repeat__"), new ConstantExpr
            {
                Kind = ConstantKind.String,
                Value = "authoredStop"
            }),
            ThenExpr = N(100),
            ElseExpr = N(0)
        }));
        CompileResult compiled = Compile(graph);
        StructuredExactResult result = Evaluate(compiled, Options(128));
        Powered(result);
        Assert.Equal(new Rational(64 + 100 * (1 - capHits), 1), result.ExpectedWin);
        Assert.Equal(new Rational(capHits, 1), Assert.Single(result.Loops).ExpectedCapHits);
    }

    [Fact]
    public void GrowingStateSpacesKeepTheTransferFallback()
    {
        JsonObject graph = Fixture(128);
        graph["nodes"]![0]!["stopConditionId"] = "stop";
        graph["expressions"]!["stop"] = Expr(Eq(F("phase"), N(1000)));
        graph["expressions"]!["advance"] = Expr(Add(F("phase"), N(1)));
        graph["expressions"]!["pay"] = Expr(Add(F("win"), N(1)));
        ObserveExit(graph);
        CompileResult compiled = Compile(graph);
        StructuredExactResult result = Evaluate(compiled);
        Assert.DoesNotContain(result.LoopNotes,
            note => note.Contains("finite-state matrix powers", StringComparison.Ordinal));
        Assert.Equal(new Rational(12928, 1), result.ExpectedWin);
    }

    [Fact]
    public void MatrixWorkExhaustionKeepsTheTransferFallback()
    {
        CompileResult compiled = Compile(RandomModel(4096));
        StructuredExactResult result = Evaluate(compiled, Options() with
        {
            LoopMatrixWork = 4096
        });
        Assert.DoesNotContain(result.LoopNotes,
            note => note.Contains("finite-state matrix powers", StringComparison.Ordinal));
        Assert.Equal(new Rational(8191, 2), result.ExpectedWin);
        Assert.Equal(new Rational(16381, 4), result.Variance(compiled.WinScale));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnavailableExtraMomentsRemainUnknown(bool noVariance)
    {
        CompileResult compiled = Compile(RandomModel(64));
        StructuredExactResult result = Evaluate(compiled, Options() with
        {
            DistributionWork = 0,
            VarianceWork = noVariance ? 0 : 4000000,
            HitFrequencyWork = noVariance ? 40000000 : 0
        });
        Powered(result);
        Assert.Equal(new Rational(127, 2), result.ExpectedWin);
        if (noVariance)
        {
            Assert.Null(result.SecondMoment);
        }
        else
        {
            Assert.Null(result.HitFrequency);
        }
    }

    [Fact]
    public void PrunedMatrixPrefixesReportTheirActualMassAndEncloseTheCappedValue()
    {
        JsonObject graph = RandomModel(100000);
        graph["nodes"]![0]!["stopConditionId"] = "stop";
        graph["expressions"]!["stop"] = Expr(Eq(F("phase"), N(1)));
        graph["expressions"]!["pay"] = Expr(Add(F("win"), N(1)));
        CompileResult compiled = Compile(graph);
        StructuredExactResult result = Evaluate(compiled, Options() with
        {
            PruneLoopMassBelow = new Rational(1, BigInteger.Pow(10, 12)),
            PruneLoopsAboveBits = 64
        });
        Powered(result);
        var tail = new Rational(1, BigInteger.One << 127);
        Assert.Equal(tail, result.PrunedMass);
        Assert.Equal(2 * (Rational.One - tail), result.ExpectedWin);
        Assert.Equal(tail, Assert.Single(result.Loops).ExpectedCapHits);
        (Rational lower, Rational upper) = result.RtpInterval(compiled.WinScale);
        Rational capped = 2 * (Rational.One - new Rational(1, BigInteger.One << 100000));
        Assert.True(lower <= capped && capped <= upper);
        Assert.Equal(Provenance.ExactInterval, result.Rtp(compiled.WinScale).Provenance.Provenance);
    }

    [Fact]
    public void CancellationIsNotSwallowedByTheMatrixFallback()
    {
        CompileResult compiled = Compile(Fixture(100000));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            StructuredExactEvaluator.Evaluate(compiled, Options(), cancellation.Token));
    }
}
