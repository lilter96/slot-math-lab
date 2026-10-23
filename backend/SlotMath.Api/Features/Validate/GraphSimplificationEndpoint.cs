using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SlotMath.Core;
using SlotMath.Core.Compiler;
using SlotMath.Core.Model;
using SlotMath.Core.Optimization;

namespace SlotMath.Api.Features.Validate;

public sealed record GraphSimplificationRequest(JsonElement Config);

public sealed record GraphSimplificationResponse(
    JsonElement Config,
    string SourceHash,
    string CandidateHash,
    IReadOnlyList<GraphSimplificationChange> Changes,
    IReadOnlyList<string> Retained,
    int VisitedExpressions,
    bool BudgetReached);

/// <summary>A bounded, non-persisting refactoring preview over the submitted draft.</summary>
public static class GraphSimplificationEndpoint
{
    public static IResult Simplify(GraphSimplificationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var source = request.Config.Deserialize<GraphConfig>(JsonOptions.Default);
            if (source is null)
            {
                return Results.BadRequest(new
                {
                    error = "A graph is required."
                });
            }

            var compiler = new GraphCompiler(allowPlugins: false, optimizeSampling: false);
            CompileResult original = compiler.Compile(source);
            if (!original.IsValid)
            {
                return Results.BadRequest(new
                {
                    error = "Repair validation errors before simplifying.",
                    errors = original.Errors
                });
            }

            GraphSimplification result = GraphSimplifier.Simplify(source, cancellationToken: cancellationToken);
            JsonNode raw = JsonNode.Parse(request.Config.GetRawText())!;
            var sourceOptions = new JsonSerializerOptions(JsonOptions.Default)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.Never
            };
            JsonNode typedSource = JsonSerializer.SerializeToNode(source, sourceOptions)!;
            JsonNode candidate = JsonSerializer.SerializeToNode(result.Config, JsonOptions.Default)!;
            var applied = new List<GraphSimplificationChange>();
            var retained = new List<string>();
            foreach (GraphSimplificationChange change in result.Changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parts = change.Path.Split('/').Skip(1).Select(p => p.Replace("~1", "/").Replace("~0", "~"))
                    .ToArray();
                JsonNode? target = At(raw, parts[..^1]);
                JsonNode? before = At(raw, parts);
                if (target is not JsonObject || before is null || HasExtensions(before, At(typedSource, parts)))
                {
                    retained.Add(change.Path + ": source extensions or noncanonical property names retained.");
                    continue;
                }

                target[parts[^1]] = At(candidate, parts)!.DeepClone();
                applied.Add(change);
            }

            JsonElement output = JsonSerializer.SerializeToElement(raw);
            CompileResult verified = compiler.Compile(output.Deserialize<GraphConfig>(JsonOptions.Default)!);
            if (!verified.IsValid)
            {
                return Results.BadRequest(new
                {
                    error = "The candidate did not pass compiler validation.",
                    errors = verified.Errors
                });
            }

            return Results.Ok(new GraphSimplificationResponse(output, DraftHash(request.Config),
                DraftHash(output), applied, retained, result.VisitedExpressions, result.BudgetReached));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException
                                              or NotSupportedException or FormatException or OverflowException)
        {
            return Results.BadRequest(new
            {
                error = exception.Message
            });
        }
    }

    private static JsonNode? At(JsonNode? node, string[] parts)
    {
        foreach (var part in parts)
        {
            node = node switch
            {
                JsonObject obj => obj[part],
                JsonArray array when int.TryParse(part, out var index) && index >= 0 && index < array.Count => array
                    [index],
                _ => null
            };
        }

        return node;
    }

    private static bool HasExtensions(JsonNode? raw, JsonNode? typed) => raw switch
    {
        JsonObject obj => typed is not JsonObject other || obj.Any(p =>
            !other.ContainsKey(p.Key) || HasExtensions(p.Value, other[p.Key])),
        JsonArray array => typed is not JsonArray other || array.Count != other.Count
                                                        || array.Where((item, index) =>
                                                            HasExtensions(item, other[index])).Any(),
        _ => false
    };

    // Draft identity includes opaque authoring data and its exact JSON number tokens.
    // It is separate from the executable mathematical configuration hash.
    private static string DraftHash(JsonElement value)
    {
        JsonNode? Ordered(JsonNode? node)
        {
            return node switch
            {
                JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => KeyValuePair.Create(p.Key, Ordered(p.Value)))),
                JsonArray array => new JsonArray(array.Select(Ordered).ToArray()),
                _ => node?.DeepClone()
            };
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            Ordered(JsonNode.Parse(value.GetRawText()))!.ToJsonString())));
    }
}
