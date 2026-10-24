using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Authoring;

/// <summary>
///     Prepares a bounded conditional product using dynamic programming, then emits ordinary editable atoms.
///     The kernel never recognizes the source definition, emitted names, or an authored mechanic pattern.
/// </summary>
public static class ConditionalProductComposer
{
    public const string Schedule = "ordered-conditional-categorical-v1";

    private static readonly JsonSerializerOptions Compact = new(JsonOptions.Default)
    {
        WriteIndented = false
    };

    public static ConditionalProductComposition Compose(ConditionalProductDefinition definition,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        Check(definition is not null, "PRODUCT_INPUT", "A conditional product definition is required.");
        Check(definition!.Inputs is { Length: <= 64 }, "PRODUCT_INPUT", "A product accepts at most 64 variables.");
        Check(definition.MinimumSum <= definition.MaximumSum, "PRODUCT_RANGE", "The sum range must be ordered.");
        Check(!string.IsNullOrWhiteSpace(definition.Namespace) && definition.Namespace.Length <= 64 &&
              !string.IsNullOrWhiteSpace(definition.OutputKey) && definition.OutputKey.Length <= 128,
            "PRODUCT_IDENTITY", "A bounded namespace and output key are required.");
        var inputs = new ConditionalProductInput[definition.Inputs.Length];
        var increments = new long[inputs.Length][];
        long offset = 0, maximum = 0;
        for (var index = 0; index < inputs.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            ConditionalProductInput input = definition.Inputs[index];
            Check(input is { Outcomes.Length: > 0 and <= 256 } &&
                  input.Contributions is not null && input.Contributions.Length == input.Outcomes.Length,
                "PRODUCT_INPUT", "Each variable requires 1..256 matching outcomes and contributions.");
            Check(input!.Outcomes.All(value => value is not null && value.Weight >= 0 &&
                                               value.OutcomeId is { Length: <= 256 } &&
                                               value.StateValue?.Value?.Length is not > 4096),
                "PRODUCT_INPUT", "Outcomes require bounded IDs/literals and nonnegative integer weights.");
            ConditionalProductInput snapshot = input with
            {
                Outcomes = input.Outcomes.Select(value => value with
                {
                    StateValue = value.StateValue is { } literal
                        ? literal with
                        {
                        }
                        : null
                }).ToArray(),
                Contributions = input.Contributions.ToArray()
            };
            try
            {
                DrawData.Validate([snapshot.Outcomes]);
            }
            catch (Exception exception) when (exception is ArgumentException or ExpressionEvaluationException
                                                  or FormatException or DivideByZeroException or OverflowException)
            {
                throw new ConditionalProductException("PRODUCT_INPUT", exception.Message);
            }

            var positive = Enumerable.Range(0, snapshot.Outcomes.Length)
                .Where(position => snapshot.Outcomes[position].Weight > 0).ToArray();
            Check(positive.Length > 0, "PRODUCT_INPUT", "Every independent variable needs positive weight.");
            var minimum = positive.Min(position => snapshot.Contributions[position]);
            offset += minimum;
            increments[index] = snapshot.Contributions.Select(value => (long)value - minimum).ToArray();
            maximum += positive.Max(position => increments[index][position]);
            inputs[index] = snapshot;
        }

        BigInteger lowerValue = BigInteger.Max(0, new BigInteger(definition.MinimumSum) - offset);
        BigInteger upperValue = BigInteger.Min(maximum, new BigInteger(definition.MaximumSum) - offset);
        Check(upperValue >= 0 && lowerValue <= upperValue, "PRODUCT_EMPTY", "The sum condition has zero mass.");
        Check(upperValue <= 255, "PRODUCT_SUM_BUDGET", "The normalized upper sum exceeds 255.");
        int lower = (int)lowerValue, upper = (int)upperValue;
        var mass = new BigInteger[inputs.Length + 1, upper + 1];
        for (var sum = lower; sum <= upper; sum++)
        {
            mass[inputs.Length, sum] = BigInteger.One;
        }

        long transitions = 0;
        for (var index = inputs.Length - 1; index >= 0; index--)
        {
            for (var sum = 0; sum <= upper; sum++)
            {
                cancellation.ThrowIfCancellationRequested();
                BigInteger total = 0;
                for (var outcome = 0; outcome < inputs[index].Outcomes.Length; outcome++)
                {
                    Check(++transitions <= 8_000_000, "PRODUCT_WORK_BUDGET",
                        "The preparation work limit was exceeded.");
                    long weight = inputs[index].Outcomes[outcome].Weight, delta = increments[index][outcome];
                    if (weight > 0 && delta <= upper - sum)
                    {
                        total += weight * mass[index + 1, sum + (int)delta];
                    }

                    Check(total.GetBitLength() <= 4096, "PRODUCT_NUMERIC_BUDGET",
                        "A conditional mass exceeds 4096 bits.");
                }

                mass[index, sum] = total;
            }
        }

        Check(mass[0, 0] > 0, "PRODUCT_EMPTY", "The sum condition has zero mass.");
        var prefix = definition.Namespace;
        var sumKey = prefix + "/sum";
        var picks = Enumerable.Range(0, inputs.Length).Select(index => prefix + "/pick/" + index).ToArray();
        Check(definition.OutputKey != sumKey && !picks.Contains(definition.OutputKey, StringComparer.Ordinal),
            "PRODUCT_IDENTITY", "The output key collides with an internal state field.");
        var nodes = new List<Node>();
        var expressions = new Dictionary<string, Expression>(StringComparer.Ordinal);
        var assets = new Dictionary<string, DistributionAsset>(StringComparer.Ordinal);
        var reachable = new HashSet<int>
        {
            0
        };
        var totalPositions = 0;

        void Write(string id, string key, Expression value)
        {
            expressions.Add(id, value);
            nodes.Add(new ModifyStateNode
            {
                Id = id,
                OutputKey = key,
                ExpressionId = id,
                Inputs = Ports(),
                Outputs = Ports()
            });
        }

        Write(prefix + "/start", sumKey, Number(0));
        for (var index = 0; index < inputs.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var rows = new Dictionary<int, string>();
            var next = new HashSet<int>();
            foreach (var sum in reachable.Order())
            {
                if (mass[index, sum].IsZero)
                {
                    continue;
                }

                BigInteger[] weights = inputs[index].Outcomes.Select((outcome, position) =>
                    outcome.Weight > 0 && increments[index][position] <= upper - sum
                        ? outcome.Weight * mass[index + 1, sum + (int)increments[index][position]]
                        : BigInteger.Zero).ToArray();
                BigInteger divisor = weights.Aggregate(BigInteger.Zero, BigInteger.GreatestCommonDivisor);
                Check(divisor > 0, "PRODUCT_EMPTY", "A reachable conditional row has zero mass.");
                for (var position = 0; position < weights.Length; position++)
                {
                    weights[position] /= divisor;
                    Check(weights[position] <= long.MaxValue, "PRODUCT_WEIGHT_BUDGET",
                        "An exact conditional coefficient exceeds the categorical asset integer range.");
                    if (weights[position] > 0)
                    {
                        next.Add(sum + (int)increments[index][position]);
                    }
                }

                Check(assets.Count < 1024 && totalPositions <= 262144 - weights.Length,
                    "PRODUCT_ASSET_BUDGET", "The conditional assets exceed the graph data limits.");
                var id = prefix + "/row/" + index + "/" + sum;
                assets.Add(id, new DistributionAsset
                {
                    Outcomes = inputs[index].Outcomes.Select((outcome, position) => new DrawWeight
                    {
                        OutcomeId = outcome.OutcomeId,
                        Weight = (long)weights[position],
                        Value = 0,
                        StateValue = Number(position)
                    }).ToArray()
                });
                totalPositions += weights.Length;
                rows.Add(sum, id);
            }

            var fallback = rows.Values.First();
            var selector = prefix + "/select/" + index;
            Expression present = Select(Field(sumKey), Enumerable.Range(0, upper + 1)
                .Select(sum => (Expression)new ConstantExpr
                {
                    Kind = ConstantKind.Boolean,
                    Value = rows.ContainsKey(sum) ? "true" : "false"
                }).ToArray());
            expressions.Add(selector, new IfExpr
            {
                Condition = present,
                ThenExpr = Field(sumKey),
                ElseExpr = Number(-1)
            });
            nodes.Add(new DrawNode
            {
                Id = prefix + "/draw/" + index,
                DistributionIds = Enumerable.Range(0, upper + 1).Select(sum => rows.GetValueOrDefault(sum, fallback))
                    .ToArray(),
                DistributionIndexExpressionId = selector,
                StateWriteKey = picks[index],
                Inputs = Ports(),
                Outputs = Ports()
            });
            Expression increment = Select(Field(picks[index]),
                increments[index].Select(value => (Expression)Number(value)).ToArray());
            Write(prefix + "/advance/" + index, sumKey, Add(Field(sumKey), increment));
            reachable = next;
        }

        var resultFields = new List<RecordFieldExpr>
        {
            Member("sum", Add(Field(sumKey), Number(offset))),
            Member("indices", new TupleExpr
            {
                Items = picks.Select(name => (Expression)Field(name)).ToArray()
            })
        };
        if (definition.IncludeOutcomeData)
        {
            Expression[] positions = inputs.Select((input, index) => (Expression)new RecordExpr
            {
                Fields =
                [
                    Member("index", Field(picks[index])),
                    Member("outcomeId", Select(Field(picks[index]), input.Outcomes.Select(outcome =>
                        (Expression)new ConstantExpr
                        {
                            Kind = ConstantKind.String,
                            Value = outcome.OutcomeId
                        }).ToArray())),
                    Member("data", Select(Field(picks[index]), input.Outcomes.Select(outcome =>
                        (Expression)(outcome.StateValue ?? new ConstantExpr
                        {
                            Kind = ConstantKind.String,
                            Value = outcome.OutcomeId
                        })).ToArray())),
                    Member("value",
                        Select(Field(picks[index]),
                            input.Outcomes.Select(outcome => (Expression)Number(outcome.Value)).ToArray())),
                    Member("contribution",
                        Select(Field(picks[index]),
                            input.Contributions.Select(value => (Expression)Number(value)).ToArray()))
                ]
            }).ToArray();
            resultFields.Add(Member("positions", new TupleExpr
            {
                Items = positions
            }));
        }

        Write(prefix + "/result", definition.OutputKey, new RecordExpr
        {
            Fields = resultFields.ToArray()
        });
        var body = new CustomMechanic
        {
            Name = prefix,
            Description =
                "Independent finite variables conditioned on an additive integer range; ordered conditional categorical draws.",
            Nodes = nodes.ToArray(),
            Expressions = expressions,
            Edges = nodes.Zip(nodes.Skip(1)).Select((pair, index) => new Edge
            {
                Id = prefix + "/edge/" + index,
                SourceNodeId = pair.First.Id,
                SourcePort = "state",
                TargetNodeId = pair.Second.Id,
                TargetPort = "state"
            }).ToArray()
        };
        ConditionalProductDefinition owned = definition with
        {
            Inputs = inputs
        };
        var source = JsonSerializer.Serialize(new
        {
            Definition = owned,
            Schedule
        }, Compact);
        Check(source.Length <= 1_048_576, "PRODUCT_SOURCE_BUDGET",
            "The source definition exceeds the identity budget.");
        var artifact = JsonSerializer.Serialize(new
        {
            Body = body,
            Distributions = assets
        }, Compact);
        Check(Encoding.UTF8.GetByteCount(artifact) <= 8 * 1024 * 1024,
            "PRODUCT_OUTPUT_BUDGET", "The editable graph exceeds eight MiB.");
        cancellation.ThrowIfCancellationRequested();
        return new ConditionalProductComposition
        {
            Body = body,
            Distributions = assets,
            SourceIdentity = "conditional-product-v1:" +
                             Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            RandomSchedule = Schedule,
            OutputKey = definition.OutputKey,
            InternalStateKeys = [sumKey, .. picks],
            TotalMass = mass[0, 0].ToString(CultureInfo.InvariantCulture),
            DpCells = mass.Length,
            DpTransitions = transitions
        };
    }

    private static void Check([DoesNotReturnIf(false)] bool condition, string code, string message)
    {
        if (!condition)
        {
            throw new ConditionalProductException(code, message);
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

    private static ConstantExpr Number(long value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString(CultureInfo.InvariantCulture)
    };

    private static FieldAccessExpr Field(string name) => new()
    {
        Target = "state",
        Path = [name]
    };

    private static BinaryExpr Add(Expression a, Expression b) => new()
    {
        Op = BinaryOp.Add,
        Left = a,
        Right = b
    };

    private static RecordFieldExpr Member(string name, Expression value) => new()
    {
        Name = name,
        Value = value
    };

    private static Expression Select(Expression index, Expression[] values)
    {
        Expression Part(int first, int last)
        {
            if (first == last)
            {
                return values[first];
            }

            var middle = (first + last + 1) / 2;
            return new IfExpr
            {
                Condition = new CompareExpr
                {
                    Op = CompareOp.Lt,
                    Left = index,
                    Right = Number(middle)
                },
                ThenExpr = Part(first, middle - 1),
                ElseExpr = Part(middle, last)
            };
        }

        return Part(0, values.Length - 1);
    }
}
