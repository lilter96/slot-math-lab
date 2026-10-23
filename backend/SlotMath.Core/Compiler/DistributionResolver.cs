using System.Numerics;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler;

/// <summary>
///     Binds categorical assets to ordinary draws before subgraph expansion. Each asset
///     is snapshotted once; author-owned arrays cannot alter lazy compiled operands.
/// </summary>
internal static class DistributionResolver
{
    internal const int MaxAssets = 1024;
    internal const int MaxOutcomes = 65_536;
    internal const int MaxTotalOutcomes = 262_144;

    internal static (GraphConfig Config, IReadOnlyList<CompileError> Errors) Resolve(GraphConfig graph)
    {
        if (graph.Distributions is null
            && !graph.Nodes.OfType<DrawNode>().Any(draw => draw.DistributionId is not null)
            && graph.Mechanics?.Values.Any(mechanic => mechanic.Nodes.OfType<DrawNode>()
                .Any(draw => draw.DistributionId is not null)) != true)
        {
            return (graph, Array.Empty<CompileError>());
        }

        var errors = new List<CompileError>();
        var assets = new Dictionary<string, DrawWeight[]>(StringComparer.Ordinal);
        var total = 0L;

        void Error(string message, string? node = null)
        {
            errors.Add(new CompileError
            {
                Code = ErrorCodes.InvalidGraph,
                NodeId = node,
                Message = message
            });
        }

        if (graph.Distributions is { } declared)
        {
            if (declared.Count > MaxAssets)
            {
                Error($"Distribution assets exceed the limit of {MaxAssets}.");
                return (graph, errors);
            }

            if (declared.Values.Sum(asset => asset?.Outcomes?.LongLength ?? 0) > MaxTotalOutcomes)
            {
                Error($"Distribution assets exceed the total limit of {MaxTotalOutcomes} outcomes.");
                return (graph, errors);
            }

            foreach (var (id, asset) in declared)
            {
                if (string.IsNullOrWhiteSpace(id) || asset?.Outcomes is not { Length: > 0 } outcomes)
                {
                    Error($"Distribution '{id}' requires a nonempty identity and outcomes.");
                    continue;
                }

                total += outcomes.Length;
                if (outcomes.Length > MaxOutcomes || total > MaxTotalOutcomes)
                {
                    Error($"Distribution '{id}' exceeds the outcome resource limits.");
                    continue;
                }

                BigInteger sum = 0;
                var valid = true;
                foreach (DrawWeight? outcome in outcomes)
                {
                    if (outcome is null || outcome.OutcomeId is null || outcome.Weight < 0)
                    {
                        valid = false;
                        break;
                    }

                    sum += outcome.Weight;
                }

                if (!valid || sum <= 0)
                {
                    Error(
                        $"Distribution '{id}' requires nonnegative weights with a positive sum and defined outcomes.");
                    continue;
                }

                assets.Add(id, outcomes.Select(outcome => outcome with
                {
                }).ToArray());
            }
        }

        Node[] Bind(Node[] nodes)
        {
            return nodes.Select(node =>
            {
                if (node is not DrawNode { DistributionId: { } id } draw)
                {
                    return node;
                }

                if (draw.DrawWeights is not null || draw.WeightExpressionId is not null)
                {
                    Error("A distribution reference cannot also specify inline or expression weights.", draw.Id);
                    return node;
                }

                if (!assets.TryGetValue(id, out DrawWeight[]? outcomes))
                {
                    Error($"Distribution '{id}' is not defined or is invalid.", draw.Id);
                    return node;
                }

                return draw with
                {
                    DistributionId = null,
                    DrawWeights = outcomes
                };
            }).ToArray();
        }

        Node[] bound = Bind(graph.Nodes);
        Dictionary<string, CustomMechanic>? mechanics = graph.Mechanics?.ToDictionary(pair => pair.Key,
            pair => pair.Value with
            {
                Nodes = Bind(pair.Value.Nodes)
            }, StringComparer.Ordinal);
        return errors.Count > 0
            ? (graph, errors)
            : (graph with
            {
                Nodes = bound,
                Mechanics = mechanics,
                Distributions = null
            }, errors);
    }
}
