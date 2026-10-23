using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Compiler;

public sealed class RecursiveShapeSeedTests
{
    private static FieldAccessExpr S(params string[] path) => new()
    {
        Target = "state",
        Path = path
    };

    private static ConstantExpr N(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString()
    };

    private static CallExpr At(Expression array) => new()
    {
        Function = "index",
        Args = [array, N(0)]
    };

    private static GraphConfig Graph(bool record, bool invalid)
    {
        var input = record ? invalid ? "[{\"v\":\"wrong\"}]" : "[{\"v\":2}]" : invalid ? "[[\"wrong\"]]" : "[[2]]";
        string[] outputs = ["win", "item", "snapshot"];
        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            InitialState = new Dictionary<string, JsonElement>
            {
                ["win"] = JsonSerializer.SerializeToElement(0),
                ["input"] = JsonSerializer.Deserialize<JsonElement>(input)
            },
            Nodes =
            [
                .. outputs.Reverse().Select(key => (Node)new ModifyStateNode
                {
                    Id = key,
                    ExpressionId = key,
                    OutputKey = key,
                    Inputs = new Dictionary<string, Port>
                    {
                        ["state"] = new()
                        {
                            Name = "state",
                            Type = PortType.State
                        }
                    },
                    Outputs = new Dictionary<string, Port>
                    {
                        ["state"] = new()
                        {
                            Name = "state",
                            Type = PortType.State
                        }
                    }
                }),
                new MetricsSinkNode
                {
                    Id = "sink",
                    WinStateKey = "win",
                    WinCap = 100,
                    Inputs = new Dictionary<string, Port>
                    {
                        ["state"] = new()
                        {
                            Name = "state",
                            Type = PortType.State
                        }
                    }
                }
            ],
            Edges = new[] { "snapshot", "item", "win", "sink" }.Zip(new[] { "item", "win", "sink" }).Select(pair =>
                new Edge
                {
                    Id = pair.First + pair.Second,
                    SourceNodeId = pair.First,
                    SourcePort = "state",
                    TargetNodeId = pair.Second,
                    TargetPort = "state"
                }).ToArray(),
            Expressions = new Dictionary<string, Expression>
            {
                ["snapshot"] = new MapExpr
                {
                    StateKey = "input",
                    ItemName = "entry",
                    ItemType = record ? ExprType.Record : ExprType.Array,
                    MaxItems = 1,
                    Body = S("entry")
                },
                ["item"] = At(S("snapshot")),
                ["win"] = new BinaryExpr
                {
                    Op = BinaryOp.Add,
                    Left = S("win"),
                    Right = record ? S("item", "v") : At(S("item"))
                }
            }
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASeededRecurrenceRecoversAfterStructuredDependenciesRefine(bool record)
    {
        GraphConfig graph = Graph(record, false);
        foreach (var reverse in new[] { false, true })
        {
            graph = graph with
            {
                Nodes = reverse ? graph.Nodes.Reverse().ToArray() : graph.Nodes
            };
            FieldDescriptor field = StateSchemaDeriver.Derive(graph).Single(field => field.Name == "win");
            Assert.Equal(ExprType.Number, field.Type);
            CompileResult compiled = new GraphCompiler().Compile(graph);
            Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
            Assert.Equal(2,
                SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42).Value);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInitialSeedDoesNotAuthorizeAnInvalidStructuredOperand(bool record)
    {
        CompileResult compiled = new GraphCompiler().Compile(Graph(record, true));
        Assert.False(compiled.IsValid);
        Assert.Contains(compiled.Errors, error => error.Code == ErrorCodes.ExpressionTypeError);
    }

    [Fact]
    public void IncompatibleConcreteWritersStillWithholdAScalarProof()
    {
        GraphConfig graph = Graph(false, false);
        graph = graph with
        {
            Expressions = new Dictionary<string, Expression>(graph.Expressions!)
            {
                ["win"] = new ConstantExpr
                {
                    Kind = ConstantKind.String,
                    Value = "not-a-number"
                }
            }
        };
        Assert.Equal(ExprType.Error, StateSchemaDeriver.Derive(graph).Single(field => field.Name == "win").Type);
    }
}
