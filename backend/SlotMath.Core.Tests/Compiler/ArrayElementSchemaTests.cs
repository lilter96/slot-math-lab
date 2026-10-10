using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Compiler;

public sealed class ArrayElementSchemaTests
{
    [Theory]
    [InlineData("[1,2,3]", ExprType.Number)]
    [InlineData("[true,false]", ExprType.Boolean)]
    [InlineData("[\"A\",\"B\"]", ExprType.String)]
    public void InitialHomogeneousArraysExposeScalarIndexType(string json, ExprType expected)
    {
        var fields = StateSchemaDeriver.Derive(new GraphConfig { SchemaVersion = "1.0.0", InitialState = new() { ["values"] = JsonSerializer.Deserialize<JsonElement>(json) } });
        var ctx = new TypeCheckContext { StateFields = fields };
        Assert.Equal(ExprType.Array, ctx.ResolvePath(["values"], "state"));
        Assert.Equal(expected, ctx.ResolvePath(["values", "0"], "state"));
        Assert.Null(ctx.ResolvePath(["values", "0", "unsupported"], "state"));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[1,\"A\"]")]
    [InlineData("[[1],[2]]")]
    public void UnknownOrMixedItemsAreNeverInventedAsNumbers(string json)
    {
        var fields = StateSchemaDeriver.Derive(new GraphConfig { SchemaVersion = "1.0.0", InitialState = new() { ["values"] = JsonSerializer.Deserialize<JsonElement>(json) } });
        Assert.Null(new TypeCheckContext { StateFields = fields }.ResolvePath(["values", "0"], "state"));
    }

    [Fact]
    public void NumericMapAndFilterPropagateElementTypeThroughStateWriters()
    {
        var graph = new GraphConfig
        {
            SchemaVersion = "1.0.0", InitialState = new() { ["raw"] = JsonSerializer.Deserialize<JsonElement>("[-6,8,15]") },
            Nodes = [new ModifyStateNode { Id = "map", OutputKey = "fractions", ExpressionId = "map" },
                new ModifyStateNode { Id = "filter", OutputKey = "positive", ExpressionId = "filter" }],
            Expressions = new()
            {
                ["map"] = new MapExpr { StateKey = "raw", ItemName = "item", ItemType = ExprType.Number,
                    Body = new BinaryExpr { Op = BinaryOp.Div, Left = new FieldAccessExpr { Target = "state", Path = ["item"] }, Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "12" } } },
                ["filter"] = new FilterExpr { StateKey = "fractions", ItemName = "item", ItemType = ExprType.Number, Predicate = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" } },
            },
        };
        var ctx = new TypeCheckContext { StateFields = StateSchemaDeriver.Derive(graph) };
        Assert.Equal(ExprType.Number, ctx.ResolvePath(["fractions", "0"], "state"));
        Assert.Equal(ExprType.Number, ctx.ResolvePath(["positive", "0"], "state"));
        Assert.Empty(ExpressionTypeChecker.Check(new CallExpr { Function = "abs", Args = [new FieldAccessExpr { Target = "state", Path = ["positive", "0"] }] }, ctx));
    }

    [Fact]
    public void ScalarIndexIsRejected()
    {
        var ctx = new TypeCheckContext { StateFields = [new FieldDescriptor { Name = "scalar", Type = ExprType.Number }] };
        Assert.Null(ctx.ResolvePath(["scalar", "0"], "state"));
    }

    [Fact]
    public void ArrayCopiesConvergeBeyondFourPassesRegardlessOfWriterOrder()
    {
        var graph = new GraphConfig
        {
            SchemaVersion = "1.0.0", InitialState = new() { ["v0"] = JsonSerializer.Deserialize<JsonElement>("[1,2]") },
            Nodes = Enumerable.Range(1, 7).Reverse().Select(i => (Node)new ModifyStateNode { Id = $"n{i}", OutputKey = $"v{i}", ExpressionId = $"e{i}" }).ToArray(),
            Expressions = Enumerable.Range(1, 7).ToDictionary(i => $"e{i}", i => (Expression)new FieldAccessExpr { Target = "state", Path = [$"v{i - 1}"] }),
        };
        var ctx = new TypeCheckContext { StateFields = StateSchemaDeriver.Derive(graph) };
        Assert.Equal(ExprType.Number, ctx.ResolvePath(["v7", "0"], "state"));
    }

    [Fact]
    public void ConflictingArrayWritersWithholdIndexedScalarType()
    {
        var graph = new GraphConfig
        {
            SchemaVersion = "1.0.0", InitialState = new() { ["numbers"] = JsonSerializer.Deserialize<JsonElement>("[1]"), ["strings"] = JsonSerializer.Deserialize<JsonElement>("[\"A\"]") },
            Nodes = [new ModifyStateNode { Id = "a", OutputKey = "mixed", ExpressionId = "a" }, new ModifyStateNode { Id = "b", OutputKey = "mixed", ExpressionId = "b" }],
            Expressions = new() { ["a"] = new FieldAccessExpr { Target = "state", Path = ["numbers"] }, ["b"] = new FieldAccessExpr { Target = "state", Path = ["strings"] } },
        };
        var ctx = new TypeCheckContext { StateFields = StateSchemaDeriver.Derive(graph) };
        Assert.Equal(ExprType.Array, ctx.ResolvePath(["mixed"], "state"));
        Assert.Null(ctx.ResolvePath(["mixed", "0"], "state"));
    }
}
