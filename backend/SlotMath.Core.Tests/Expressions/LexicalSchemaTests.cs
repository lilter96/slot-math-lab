using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace SlotMath.Core.Tests.Expressions;

public sealed class LexicalSchemaTests
{
    private static JsonSchema Schema()
    {
        JsonNode exported = JsonNode.Parse(SchemaEmitter.GetSchemaJson())!;
        var expression = new JsonObject
        {
            ["$defs"] = exported["$defs"]!.DeepClone(),
            ["$ref"] = "#/$defs/expression"
        };
        return JsonSchema.FromText(expression.ToJsonString());
    }

    [Theory]
    [InlineData(
        """{"exprType":"let","name":"x","value":{"exprType":"record","fields":[{"name":"score","value":{"exprType":"constant","kind":"Rational","value":"1/3"}}]},"body":{"exprType":"local","name":"x","path":["score"]}}""")]
    [InlineData(
        """{"exprType":"project","value":{"exprType":"tuple","items":[{"exprType":"constant","kind":"Integer","value":"7"}]},"path":["0"]}""")]
    [InlineData(
        """{"exprType":"aggregate","func":"Count","stateKey":"items","itemName":"item","predicate":null,"valueExpr":null}""")]
    public void RecursiveFormsAndNullableOptionalChildrenUseTheSharedContract(string json)
    {
        using JsonDocument value = JsonDocument.Parse(json);
        Assert.True(Schema().Evaluate(value.RootElement).IsValid);
    }

    [Theory]
    [InlineData(
        """{"exprType":"let","name":"x","value":null,"body":{"exprType":"constant","kind":"Integer","value":"1"}}""")]
    [InlineData("""{"exprType":"record","fields":[{"name":"x","value":{"exprType":"missing"}}]}""")]
    [InlineData("""{"exprType":"record","fields":[{"name":"x"}]}""")]
    [InlineData("""{"exprType":"tuple","items":[null]}""")]
    [InlineData("""{"exprType":"project","value":{"exprType":"local","name":"x"}}""")]
    public void NestedUnknownFormsNullItemsAndMissingRequiredValuesAreRejected(string json)
    {
        using JsonDocument value = JsonDocument.Parse(json);
        Assert.False(Schema().Evaluate(value.RootElement).IsValid);
    }

    [Fact]
    public void RecursiveSchemaStaysShallowAndStableWhenExportedRepeatedly()
    {
        var json = SchemaEmitter.GetSchemaJson();
        Assert.Equal(json, SchemaEmitter.GetSchemaJson());
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement expression = document.RootElement.GetProperty("$defs").GetProperty("expression");
        JsonElement[] variants = expression.GetProperty("oneOf").EnumerateArray().ToArray();
        Assert.Equal(variants.Length,
            variants.Select(variant =>
                    variant.GetProperty("properties").GetProperty("exprType").GetProperty("const").GetString())
                .Distinct()
                .Count());
        JsonElement record = variants.Single(variant =>
            variant.GetProperty("properties").GetProperty("exprType").GetProperty("const").GetString() == "record");
        Assert.Equal("#/$defs/expression",
            record.GetProperty("properties").GetProperty("fields").GetProperty("items").GetProperty("properties")
                .GetProperty("value").GetProperty("$ref").GetString());
    }
}
