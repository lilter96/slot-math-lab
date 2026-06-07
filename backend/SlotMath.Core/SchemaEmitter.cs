using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Json.Schema.Generation;
using SlotMath.Core.Model;

namespace SlotMath.Core;

public static class SchemaEmitter
{
    public const string CurrentSchemaVersion = "1.0.0";

    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    private static JsonSchema? s_schema;

    /// <summary>Returns the generated JSON Schema (PascalCase property names, as derived from C# types).</summary>
    public static JsonSchema GetSchema()
    {
        if (s_schema is not null)
            return s_schema;

        s_schema = new JsonSchemaBuilder()
            .Schema(MetaSchemas.Draft202012Id)
            .Title("Slot Math Lab Graph Schema")
            .Description("Canonical schema for slot math graph configuration.")
            .FromType<GraphConfig>()
            .Build();

        return s_schema;
    }

    /// <summary>Returns the JSON Schema as a camelCase JSON string (used for TS type generation and file export).</summary>
    public static string GetSchemaJson()
    {
        var node = JsonSerializer.SerializeToNode(GetSchema(), s_writeOptions)!;
        CamelCaseProperties(node);
        return JsonSerializer.Serialize(node, s_writeOptions);
    }

    public static void WriteSchemaToFile(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, GetSchemaJson());
    }

    public static string GetCurrentSchemaVersion() => CurrentSchemaVersion;

    private static void CamelCaseProperties(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("properties", out var propsNode) && propsNode is JsonObject props)
            {
                var renamed = new Dictionary<string, JsonNode?>();
                foreach (var kv in props)
                {
                    var camelKey = Char.ToLowerInvariant(kv.Key[0]) + kv.Key[1..];
                    renamed[camelKey] = kv.Value;
                }
                props.Clear();
                foreach (var kv in renamed)
                    props[kv.Key] = kv.Value;
            }

            if (obj.TryGetPropertyValue("required", out var reqNode) && reqNode is JsonArray reqArray)
            {
                for (var i = 0; i < reqArray.Count; i++)
                {
                    if (reqArray[i] is JsonValue val && val.TryGetValue<string>(out var str) && str is not null)
                        reqArray[i] = JsonValue.Create(Char.ToLowerInvariant(str[0]) + str[1..]);
                }
            }

            foreach (var kv in obj)
            {
                if (kv.Value is not null)
                    CamelCaseProperties(kv.Value);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not null)
                    CamelCaseProperties(item);
            }
        }
    }
}
