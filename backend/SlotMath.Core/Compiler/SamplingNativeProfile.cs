using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler;

internal sealed record SamplingNativeSymbol(string MethodName, string Kind, string[] NodeIds, string? ExpressionJson);

// Names are attached when IL is generated. Capturing their source map is an
// explicit diagnostic option; neither operation adds work to a sampled round.
internal static class SamplingNativeProfile
{
    private const int MaximumSites = 4096;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, SamplingNativeSymbol> Symbols = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions SourceOptions = new(JsonOptions.Default)
    {
        MaxDepth = 512
    };

    private static bool _enabled;
    internal static bool Truncated { get; private set; }

    internal static void Configure(bool enabled)
    {
        lock (Gate)
        {
            Symbols.Clear();
            Truncated = false;
            Volatile.Write(ref _enabled, enabled);
        }
    }

    private static string Fingerprint(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];

    internal static string Register(string kind, string key, IReadOnlyList<Node>? nodes = null,
        Expression? expression = null)
    {
        var name = "sml_" + kind.TrimEnd(':').Replace('-', '_') + "_" + Fingerprint(key);
        if (!Volatile.Read(ref _enabled))
        {
            return name;
        }

        var ids = nodes?.Select(node => node.Id).ToArray() ?? [];
        var json = expression is null ? null : JsonSerializer.Serialize(expression, SourceOptions);
        var identity = name + ":" + Fingerprint(JsonSerializer.Serialize(ids) + "\n" + json);
        lock (Gate)
        {
            if (Symbols.ContainsKey(identity))
            {
                return name;
            }

            if (Symbols.Count >= MaximumSites)
            {
                Truncated = true;
                return name;
            }

            Symbols.Add(identity, new SamplingNativeSymbol(name, kind, ids, json));
        }

        return name;
    }

    internal static SamplingNativeSymbol[] Snapshot()
    {
        lock (Gate)
        {
            return Symbols.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToArray();
        }
    }
}
