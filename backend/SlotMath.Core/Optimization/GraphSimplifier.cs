using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Optimization;

public sealed record GraphSimplificationChange(
    string Path,
    long BeforeCost,
    long AfterCost,
    IReadOnlyDictionary<string, int> Rules);

public sealed record GraphSimplification(
    GraphConfig Config,
    IReadOnlyList<GraphSimplificationChange> Changes,
    int VisitedExpressions,
    bool BudgetReached);

/// <summary>
///     Refactors editable expressions in place, including reusable definitions. Node/edge identity, ordering,
///     state writes, draws and observation points are retained. No graph is flattened or replaced by a special runtime.
/// </summary>
public static class GraphSimplifier
{
    public static GraphSimplification Simplify(GraphConfig graph, int maxVisits = 250_000,
        CancellationToken cancellationToken = default) => Rewrite(graph, null, maxVisits, cancellationToken);

    // Runtime facts are guarded by SamplingPlan's runner entry checks. They must never leak into an authored graph.
    internal static GraphSimplification Specialize(GraphConfig graph,
        IReadOnlyDictionary<string, ExprValue> constants, IReadOnlySet<string> numericFields)
    {
        GraphConfig executable = graph with
        {
            Mechanics = null
        };
        return Rewrite(executable, constants, 250_000, default, numericFields);
    }

    private static GraphSimplification Rewrite(GraphConfig graph, IReadOnlyDictionary<string, ExprValue>? constants,
        int maxVisits, CancellationToken cancellationToken, IReadOnlySet<string>? numericFields = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var changes = new List<GraphSimplificationChange>();
        var visited = 0;
        var limited = false;

        Expression Simplify(Expression expression, string path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (visited >= maxVisits)
            {
                limited = true;
                return expression;
            }

            ExpressionSimplification result =
                ExpressionSimplifier.Simplify(expression, constants, System.Math.Min(50_000, maxVisits - visited),
                    numericFields);
            visited += result.Visited;
            limited |= result.BudgetReached;
            if (result.Rules.Count > 0)
            {
                changes.Add(new GraphSimplificationChange(path, ExpressionCost.Compute(expression),
                    ExpressionCost.Compute(result.Expression),
                    result.Rules));
            }

            return result.Expression;
        }

        static string Escape(string part)
        {
            return part.Replace("~", "~0").Replace("/", "~1");
        }

        Dictionary<string, Expression>? Expressions(Dictionary<string, Expression>? expressions, string prefix)
        {
            return expressions?.ToDictionary(p => p.Key,
                p => Simplify(p.Value, prefix + "/expressions/" + Escape(p.Key)));
        }

        Node[] Nodes(Node[] nodes, string prefix)
        {
            return nodes.Select((node, index) =>
            {
                var path = prefix + "/nodes/" + index;
                Node next = node is LoopNode { ExitReason: { } reason } loop
                    ? loop with
                    {
                        ExitReason = Simplify(reason, path + "/exitReason")
                    }
                    : node;
                return next with
                {
                    Inputs = next.Inputs.ToDictionary(p => p.Key, p => p.Value.DefaultValue is { } value
                        ? p.Value with
                        {
                            DefaultValue = Simplify(value, path + "/inputs/" + Escape(p.Key) + "/defaultValue")
                        }
                        : p.Value),
                    Outputs = next.Outputs.ToDictionary(p => p.Key, p => p.Value.DefaultValue is { } value
                        ? p.Value with
                        {
                            DefaultValue = Simplify(value, path + "/outputs/" + Escape(p.Key) + "/defaultValue")
                        }
                        : p.Value)
                };
            }).ToArray();
        }

        return new GraphSimplification(graph with
        {
            Expressions = Expressions(graph.Expressions, ""),
            Nodes = Nodes(graph.Nodes, ""),
            Mechanics = graph.Mechanics?.ToDictionary(p => p.Key, p => p.Value with
            {
                Expressions = Expressions(p.Value.Expressions, "/mechanics/" + Escape(p.Key)),
                Nodes = Nodes(p.Value.Nodes, "/mechanics/" + Escape(p.Key))
            })
        }, changes, visited, limited);
    }
}
