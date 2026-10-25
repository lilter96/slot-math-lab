using System.Globalization;
using SlotMath.Core.Model;

namespace SlotMath.Benchmarks;

/// <summary>A reproducible workload authored with ordinary atoms, independent of solver selection.</summary>
internal static class NumericSumBenchmark
{
    public static GraphConfig Graph(int count, int stride = 1, int coefficientBits = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 65536);
        if (stride is < 1 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }

        if (coefficientBits is < 0 or > 62)
        {
            throw new ArgumentOutOfRangeException(nameof(coefficientBits));
        }

        Dictionary<string, Port> Ports()
        {
            return new Dictionary<string, Port>
            {
                ["state"] = new()
                {
                    Name = "state",
                    Type = PortType.State
                }
            };
        }

        DrawNode Draw(string id)
        {
            var origin = coefficientBits == 0 ? 0 : 1L << (coefficientBits - 1);
            IEnumerable<DrawWeight> weights = Enumerable.Range(0, count).Select(i => new DrawWeight
            {
                OutcomeId = (i * stride).ToString(CultureInfo.InvariantCulture),
                Weight = coefficientBits == 0 ? 1 + i % 7 : origin + (id == "a" ? 11 + 3L * i : 23 + 5L * i)
            });
            if (stride > 1)
            {
                weights = weights.Append(new DrawWeight
                {
                    OutcomeId = "1",
                    Weight = coefficientBits == 0 ? 1 : origin + (id == "a" ? 112 : 126)
                });
            }

            return new DrawNode
            {
                Id = id,
                StateWriteKey = id,
                Inputs = id == "a" ? [] : Ports(),
                Outputs = Ports(),
                DrawWeights = weights.ToArray()
            };
        }

        CallExpr Number(string id)
        {
            return new CallExpr
            {
                Function = "tonumber",
                Args =
                [
                    new FieldAccessExpr
                    {
                        Target = "state",
                        Path = [id]
                    }
                ]
            };
        }

        Edge Edge(string from, string to)
        {
            return new Edge
            {
                Id = from + "-" + to,
                SourceNodeId = from,
                SourcePort = "state",
                TargetNodeId = to,
                TargetPort = "state"
            };
        }

        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Name = "Independent numeric sum benchmark",
            StateSchema =
            [
                new StateFieldSchema
                {
                    Name = "a",
                    Type = "string"
                },
                new StateFieldSchema
                {
                    Name = "b",
                    Type = "string"
                },
                new StateFieldSchema
                {
                    Name = "win",
                    Type = "number"
                }
            ],
            Nodes =
            [
                Draw("a"), Draw("b"),
                new ModifyStateNode
                {
                    Id = "pay",
                    OutputKey = "win",
                    ExpressionId = "pay",
                    Inputs = Ports(),
                    Outputs = Ports()
                },
                new MetricsSinkNode
                {
                    Id = "sink",
                    WinStateKey = "win",
                    WinCap = 2L * count * stride,
                    Inputs = Ports()
                }
            ],
            Edges = [Edge("a", "b"), Edge("b", "pay"), Edge("pay", "sink")],
            Expressions = new Dictionary<string, Expression>
            {
                ["pay"] = new BinaryExpr
                {
                    Op = BinaryOp.Add,
                    Left = Number("a"),
                    Right = Number("b")
                }
            }
        };
    }
}
