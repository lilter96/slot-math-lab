using System.Text.Json;
using System.Text.Json.Nodes;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler;

internal static class ExpressionResolver
{
    public static GraphConfig Resolve(GraphConfig config)
    {
        var definitions = config.Expressions ?? new();
        JsonNode Expand(JsonNode node, HashSet<string> visiting, int depth)
        {
            if (depth > 64) throw new CompilationException(null, ErrorCodes.ExpressionTypeError, "Expression reference nesting exceeds 64.");
            if (node is JsonObject obj)
            {
                if (obj["exprType"]?.GetValue<string>() == "fieldAccess" && obj["path"] is JsonArray path
                    && path.Count == 2 && path[0]?.GetValue<string>() == "expressions")
                {
                    var id = path[1]!.GetValue<string>();
                    if (!definitions.TryGetValue(id, out var expression))
                        throw new CompilationException(null, ErrorCodes.ExpressionTypeError, $"Expression '{id}' does not exist.");
                    if (!visiting.Add(id))
                        throw new CompilationException(null, ErrorCodes.ExpressionTypeError, $"Cyclic expression reference '{id}'.");
                    var replacement = Expand(JsonSerializer.SerializeToNode<Expression>(expression, JsonOptions.Default)!, visiting, depth + 1);
                    visiting.Remove(id);
                    return replacement;
                }
                foreach (var (key, value) in obj.ToArray())
                    if (value is JsonObject or JsonArray) obj[key] = Expand(value.DeepClone(), visiting, depth + 1);
            }
            else if (node is JsonArray array)
                for (var i = 0; i < array.Count; i++)
                    if (array[i] is JsonObject or JsonArray) array[i] = Expand(array[i]!.DeepClone(), visiting, depth + 1);
            return node;
        }
        Expression Resolve(Expression expression) => Expand(
            JsonSerializer.SerializeToNode<Expression>(expression, JsonOptions.Default)!, new(), 0)
            .Deserialize<Expression>(JsonOptions.Default)!;
        return config with
        {
            Expressions = definitions.ToDictionary(p => p.Key, p => Resolve(p.Value)),
            Nodes = config.Nodes.Select(node => (node is LoopNode loop && loop.ExitReason is not null ? loop with { ExitReason = Resolve(loop.ExitReason) } : node) with
            {
                Inputs = node.Inputs.ToDictionary(p => p.Key, p => p.Value.DefaultValue is { } value
                    ? p.Value with { DefaultValue = Resolve(value) } : p.Value)
            }).ToArray()
        };
    }
}
