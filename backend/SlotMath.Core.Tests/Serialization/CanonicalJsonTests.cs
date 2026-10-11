using System.Text.Json;
using SlotMath.Core.Model;
using SlotMath.Core.Serialization;

namespace SlotMath.Core.Tests.Serialization;

// ═══════════════════════════════════════════════════════════════════════════
//  D2 — canonical serialization & config hash
// ═══════════════════════════════════════════════════════════════════════════

public class CanonicalJsonTests
{
    [Fact]
    public void DecimalStateContentHashesPreserveNestedLiteralValuesAndNumericType()
    {
        GraphConfig Graph(string literal) => new() { SchemaVersion = "1.0.0", InitialState = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(literal) };
        var graph = Graph("""{"small":1e-9,"record":{"values":[0.1,-2.5e-7]}}""");
        var canonical = CanonicalJson.Serialize(graph);
        using var parsed = JsonDocument.Parse(canonical);
        var initial = parsed.RootElement.GetProperty("initialState");
        Assert.Equal(.000000001m, initial.GetProperty("small").GetDecimal());
        Assert.Equal(.1m, initial.GetProperty("record").GetProperty("values")[0].GetDecimal());
        Assert.Equal(ConfigHash.Compute(graph), ConfigHash.Compute(Graph("""{"record":{"values":[0.1,-2.5e-7]},"small":1e-9}""")));
        Assert.NotEqual(ConfigHash.Compute(graph), ConfigHash.Compute(Graph("""{"small":"1e-9","record":{"values":[0.1,-2.5e-7]}}""")));
        Assert.NotEqual(ConfigHash.Compute(graph), ConfigHash.Compute(Graph("""{"small":0,"record":{"values":[0.1,-2.5e-7]}}""")));
        Assert.NotEqual(ConfigHash.Compute(graph), ConfigHash.Compute(Graph("""{"small":2e-9,"record":{"values":[0.1,-2.5e-7]}}""")));
        Assert.Throws<InvalidOperationException>(() => CanonicalJson.Serialize(new { typedMath = .1 }));
    }
    private static GraphConfig ConfigWithParameters(params (string key, string value)[] parameters)
    {
        var dict = new Dictionary<string, string>();
        foreach (var (k, v) in parameters)
            dict[k] = v;
        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Nodes = [new LibraryNode { Id = "n1", MechanicName = "lines", Parameters = dict }],
        };
    }

    [Fact]
    public void Hash_IsOrderInsensitive_OnDictionaryKeyOrder()
    {
        // Same semantics, different insertion order ⇒ identical canonical hash (D2/G2).
        var a = ConfigWithParameters(("alpha", "1"), ("beta", "2"), ("gamma", "3"));
        var b = ConfigWithParameters(("gamma", "3"), ("alpha", "1"), ("beta", "2"));

        Assert.Equal(ConfigHash.Compute(a), ConfigHash.Compute(b));
        Assert.Equal(CanonicalJson.Serialize(a), CanonicalJson.Serialize(b));
    }

    [Fact]
    public void Hash_ChangesOnOneBitSemanticChange()
    {
        var a = ConfigWithParameters(("alpha", "1"));
        var changedValue = ConfigWithParameters(("alpha", "2"));
        var changedName = a with { Name = "x" };

        Assert.NotEqual(ConfigHash.Compute(a), ConfigHash.Compute(changedValue));
        Assert.NotEqual(ConfigHash.Compute(a), ConfigHash.Compute(changedName));
    }

    [Fact]
    public void CanonicalJson_HasSortedKeysAndNoWhitespace()
    {
        var json = CanonicalJson.Serialize(ConfigWithParameters(("zebra", "1"), ("apple", "2")));

        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain(": ", json);
        // "apple" key must appear before "zebra" (ordinal sort).
        Assert.True(json.IndexOf("apple", StringComparison.Ordinal)
                    < json.IndexOf("zebra", StringComparison.Ordinal));
    }

    [Fact]
    public void Hash_IsDeterministic_AcrossRuns()
    {
        var config = ConfigWithParameters(("a", "1"), ("b", "2"));
        Assert.Equal(ConfigHash.Compute(config), ConfigHash.Compute(config));
        // 64 hex chars = SHA-256.
        Assert.Equal(64, ConfigHash.Compute(config).Length);
    }

    [Fact]
    public void SubgraphHash_PinsContent()
    {
        var mechanic = new CustomMechanic { Name = "tumble", Nodes = [] };
        var same = new CustomMechanic { Name = "tumble", Nodes = [] };
        var different = new CustomMechanic { Name = "tumble", Description = "edited", Nodes = [] };

        Assert.Equal(ConfigHash.SubgraphHash(mechanic), ConfigHash.SubgraphHash(same));
        Assert.NotEqual(ConfigHash.SubgraphHash(mechanic), ConfigHash.SubgraphHash(different));
    }
}
