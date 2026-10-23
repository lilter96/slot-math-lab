using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using SlotMath.Api.Features.Validate;

namespace SlotMath.Api.Tests;

public class GraphSimplificationTests
{
    private static JsonObject Graph() => JsonNode.Parse("""
                                                        {"schemaVersion":"1.0", "customProjectData":{"owner":"keep","fraction":0.125,"wide":9007199254740993},
                                                         "nodes":[{"id":"write","nodeType":"modifyState","expressionId":"value","outputKey":"win",
                                                                   "outputs":{"out":{"name":"out","type":"Number"}}},
                                                                  {"id":"sink","nodeType":"metricsSink","winStateKey":"win","winCap":100,
                                                                   "inputs":{"in":{"name":"in","type":"Number"}}}],
                                                         "edges":[{"id":"edge","sourceNodeId":"write","sourcePort":"out","targetNodeId":"sink","targetPort":"in"}],
                                                         "expressions":{"value":{"exprType":"binary","op":"Add",
                                                             "left":{"exprType":"constant","kind":"Rational","value":"1/3"},
                                                             "right":{"exprType":"constant","kind":"Rational","value":"2/3"}}}}
                                                        """)!.AsObject();

    private static IResult Preview(JsonObject graph) => GraphSimplificationEndpoint.Simplify(
        new GraphSimplificationRequest(JsonSerializer.SerializeToElement(graph)), CancellationToken.None);

    [Fact]
    public void PreviewPreservesOpaqueDataAndReturnsExactPatchLocations()
    {
        JsonObject graph = Graph();
        var before = graph.ToJsonString();
        IResult result = Preview(graph);
        var response =
            Assert.IsType<GraphSimplificationResponse>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.Equal("keep", response.Config.GetProperty("customProjectData").GetProperty("owner").GetString());
        Assert.Equal("0.125", response.Config.GetProperty("customProjectData").GetProperty("fraction").GetRawText());
        Assert.Equal("9007199254740993",
            response.Config.GetProperty("customProjectData").GetProperty("wide").GetRawText());
        Assert.Equal("1",
            response.Config.GetProperty("expressions").GetProperty("value").GetProperty("value").GetString());
        Assert.Equal("/expressions/value", Assert.Single(response.Changes).Path);
        Assert.NotEqual(response.SourceHash, response.CandidateHash);
        Assert.Equal(before, graph.ToJsonString());
    }

    [Theory]
    [InlineData("retain")]
    [InlineData(null)]
    public void UnsupportedExpressionExtensionsAreRetained(string? extension)
    {
        JsonObject graph = Graph();
        graph["expressions"]!["value"]!["customAnnotation"] = extension;
        IResult result = Preview(graph);
        var response =
            Assert.IsType<GraphSimplificationResponse>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.Empty(response.Changes);
        Assert.Single(response.Retained);
        Assert.Equal(response.SourceHash, response.CandidateHash);
        Assert.Equal(extension,
            response.Config.GetProperty("expressions").GetProperty("value").GetProperty("customAnnotation")
                .GetString());
    }

    [Fact]
    public void KnownNullablePropertiesDoNotPreventSimplification()
    {
        JsonObject graph = Graph();
        graph["expressions"]!["value"]!["annotation"] = null;
        var response =
            Assert.IsType<GraphSimplificationResponse>(Assert.IsAssignableFrom<IValueHttpResult>(Preview(graph)).Value);
        Assert.Single(response.Changes);
        Assert.Empty(response.Retained);
    }

    [Fact]
    public void InvalidGraphsAreNotRepairedByGuessingIntent()
    {
        JsonObject graph = Graph();
        graph["nodes"]![1]!["winCap"] = null;
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(Preview(graph)).StatusCode);
    }
}
