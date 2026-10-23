using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Compiler;

public sealed class ReferenceShapeValidationTests
{
    private static GraphConfig Graph() => JsonSerializer.Deserialize<GraphConfig>("""
        {
          "schemaVersion": "1.0.0",
          "initialState": {"win":0},
          "nodes": [
            {"nodeType":"library","id":"call","mechanicName":"body","parameters":{"scale":"2"},"outputs":{"state":{"name":"state","type":"State"}}},
            {"nodeType":"metricsSink","id":"sink","winStateKey":"win","winCap":100,"inputs":{"state":{"name":"state","type":"State"}}}
          ],
          "edges": [{"id":"next","sourceNodeId":"call","sourcePort":"state","targetNodeId":"sink","targetPort":"state"}],
          "mechanics": {"body": {
            "name":"body",
            "nodes":[{"nodeType":"modifyState","id":"add","outputKey":"win","expressionId":"add","inputs":{"state":{"name":"state","type":"State"}},"outputs":{"state":{"name":"state","type":"State"}}}],
            "edges":[],
            "expressions":{"add":{"exprType":"binary","op":"Add","left":{"exprType":"fieldAccess","target":"state","path":["win"]},"right":{"exprType":"fieldAccess","target":"state","path":["expressions","scale"]}}}
          }}
        }
        """, JsonOptions.Default)!;

    [Fact]
    public void ParameterizedRecurrenceResolvesBeforeInferringItsOutputShape()
    {
        GraphConfig graph = Graph();
        var source = JsonSerializer.Serialize(graph, JsonOptions.Default);
        CompileResult compiled = new GraphCompiler().Compile(graph);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Assert.Equal(2, SampledInterpreter.RunSingle(compiled.Program!, new Dictionary<string, object?>(), 42).Value);
        Assert.Equal(source, JsonSerializer.Serialize(graph, JsonOptions.Default));
    }

    [Theory]
    [InlineData(ConstantKind.String, "payload", "String")]
    [InlineData(ConstantKind.Boolean, "true", "Boolean")]
    [InlineData(ConstantKind.Integer, "123", "Number")]
    public void NamedValuesKeepTheirActualTypeAcrossWriterSchemaValidation(ConstantKind kind, string value, string type)
    {
        GraphConfig graph = Graph() with
        {
            Mechanics = null,
            Nodes =
            [
                new ModifyStateNode
                {
                    Id = "write",
                    ExpressionId = "reference",
                    OutputKey = "typed"
                },
                new MetricsSinkNode
                {
                    Id = "sink",
                    WinStateKey = "win",
                    WinCap = 100
                }
            ],
            Edges =
            [
                new Edge
                {
                    Id = "next",
                    SourceNodeId = "write",
                    SourcePort = "state",
                    TargetNodeId = "sink",
                    TargetPort = "state"
                }
            ],
            Expressions = new Dictionary<string, Expression>
            {
                ["reference"] = new FieldAccessExpr
                {
                    Target = "state",
                    Path = ["expressions", "constant"]
                },
                ["constant"] = new ConstantExpr
                {
                    Kind = kind,
                    Value = value
                }
            }
        };
        Node[] nodes = graph.Nodes.Select(node => node with
        {
            Inputs = new Dictionary<string, Port>
            {
                ["state"] = new()
                {
                    Name = "state",
                    Type = PortType.State
                }
            },
            Outputs = node is MetricsSinkNode
                ? []
                : new Dictionary<string, Port>
                {
                    ["state"] = new()
                    {
                        Name = "state",
                        Type = PortType.State
                    }
                }
        }).ToArray();
        CompileResult compiled = new GraphCompiler().Compile(graph with
        {
            Nodes = nodes
        });
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors));
        Assert.Equal(type, compiled.MeasurementSchema!.Fields.Single(field => field.Name == "typed").Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrCyclicReferencesReturnCompileErrors(bool cyclic)
    {
        GraphConfig graph = Graph();
        CustomMechanic body = graph.Mechanics!["body"];
        graph = graph with
        {
            Mechanics = new Dictionary<string, CustomMechanic>
            {
                ["body"] = body with
                {
                    Expressions = new Dictionary<string, Expression>
                    {
                        ["add"] = new FieldAccessExpr
                        {
                            Target = "state",
                            Path = ["expressions", cyclic ? "add" : "missing"]
                        }
                    }
                }
            }
        };
        CompileResult compiled = new GraphCompiler().Compile(graph);
        Assert.False(compiled.IsValid);
        Assert.Contains(compiled.Errors,
            error => error.Code == ErrorCodes.ExpressionTypeError &&
                     error.Message.Contains(cyclic ? "Cyclic" : "does not exist"));
    }
}
