using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SlotMath.Core.Serialization;

// ═══════════════════════════════════════════════════════════════════════════
//  Canonical serialization & config hash (PRD v3.1, D2)
//
//  Canonical JSON: UTF-8, object keys sorted by ordinal, no insignificant
//  whitespace, arrays order-significant, rationals as the string "n/d", no
//  floats in hashed content. Two semantically identical configs (differing
//  only in key order or rational representation) serialize identically and
//  therefore hash identically; a one-bit semantic change changes the hash.
//
//  configHash  = SHA-256 over the canonical JSON of the fully resolved config.
//  subgraphHash = SHA-256 over a subgraph definition's own canonical JSON,
//                 so configs pin subgraph content by hash (D22/D23).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>D2 canonical JSON serializer (ordinal-sorted keys, compact, float-free).</summary>
public static class CanonicalJson
{
    /// <summary>Serialize <paramref name="value"/> to canonical JSON (D2).</summary>
    public static string Serialize(object? value)
    {
        var node = JsonSerializer.SerializeToNode(value, JsonOptions.Default);
        var canonical = Canonicalize(node);
        return canonical?.ToJsonString(CompactOptions) ?? "null";
    }

    private static readonly JsonSerializerOptions CompactOptions = new() { WriteIndented = false };

    private static JsonNode? Canonicalize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var sorted = new JsonObject();
                foreach (var kvp in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
                    sorted[kvp.Key] = Canonicalize(kvp.Value?.DeepClone());
                return sorted;

            case JsonArray arr:
                var array = new JsonArray();
                foreach (var item in arr)
                    array.Add(Canonicalize(item?.DeepClone()));
                return array;

            default:
                // Leaf (string / number / bool / null). Reject floats in hashed
                // content (D2): the exact-path config must be integer/rational only.
                if (node is JsonValue value && value.TryGetValue<double>(out var d) && d != System.Math.Floor(d))
                    throw new InvalidOperationException(
                        "Canonical JSON must not contain non-integer floats (D2). Use rationals as \"n/d\".");
                return node?.DeepClone();
        }
    }
}

/// <summary>SHA-256 content hashes over canonical JSON (D2).</summary>
public static class ConfigHash
{
    /// <summary>The fully-resolved config hash (D2). Lowercase hex of SHA-256.</summary>
    public static string Compute(object? config)
    {
        var json = CanonicalJson.Serialize(config);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>A subgraph definition's own content hash (D2, D23). Lowercase hex of SHA-256.</summary>
    public static string SubgraphHash(object? subgraph) => Compute(subgraph);
}
