using System.Numerics;
using SlotMath.Core.Expressions;
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
    internal const int MaxFamilyMembers = 256;

    private static bool RequiresBinding(DrawNode draw) => draw.DistributionId is not null
                                                          || draw.DistributionIds is not null
                                                          || (draw.DistributionIndexExpressionId is not null &&
                                                              draw.BoundDistributions is null);

    private static bool RequiresDataBinding(Node node) => (node is DrawNode draw && RequiresBinding(draw))
                                                          || node is DrawManyNode { BoundDistributions: null };

    internal static (GraphConfig Config, IReadOnlyList<CompileError> Errors) Resolve(GraphConfig graph)
    {
        if (graph.Distributions is null
            && !graph.Nodes.Any(RequiresDataBinding)
            && graph.Mechanics?.Values.Any(mechanic => mechanic.Nodes.Any(RequiresDataBinding)) != true)
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

                try
                {
                    DrawData.Validate([outcomes]);
                }
                catch (Exception exception) when (exception is ArgumentException or FormatException
                                                      or ExpressionEvaluationException or DivideByZeroException
                                                      or OverflowException)
                {
                    Error($"Distribution '{id}': {exception.Message}");
                    continue;
                }

                assets.Add(id, outcomes.Select(outcome => outcome with
                {
                }).ToArray());
            }
        }

        Node BindBatch(DrawManyNode batch)
        {
            if (batch.BoundDistributions is not null)
            {
                return batch;
            }

            var repeat = batch.DistributionId is not null;
            if (string.IsNullOrWhiteSpace(batch.OutputKey)
                || repeat == batch.DistributionIds is not null)
            {
                Error("DrawMany requires an output key and exactly one distribution or schedule.", batch.Id);
                return batch;
            }

            var ids = repeat ? [batch.DistributionId!] : batch.DistributionIds!;
            if (ids.Length > DrawManyData.MaxCount || (batch.CountExpressionId is not null
                                                       && (string.IsNullOrWhiteSpace(batch.CountExpressionId) ||
                                                           batch.Count is not null ||
                                                           batch.MaxCount is null)))
            {
                Error("A dynamic DrawMany requires one count expression, an explicit maximum and no fixed count.",
                    batch.Id);
                return batch;
            }

            int? count = batch.CountExpressionId is null ? batch.Count ?? (repeat ? 1 : ids.Length) : null;
            var maximum = batch.MaxCount ?? count!.Value;
            if (maximum is < 0 or > DrawManyData.MaxCount || count is < 0 ||
                (count is { } fixedCount && fixedCount > maximum)
                || (!repeat && maximum > ids.Length))
            {
                Error("DrawMany count limits must be in [0,4096] and within its schedule.", batch.Id);
                return batch;
            }

            var rows = new DrawWeight[ids.Length][];
            for (var index = 0; index < ids.Length; index++)
            {
                if (ids[index] is null || !assets.TryGetValue(ids[index], out DrawWeight[]? row))
                {
                    Error($"DrawMany distribution '{ids[index]}' is missing or invalid.", batch.Id);
                    return batch;
                }

                rows[index] = row;
            }

            try
            {
                DrawData.Validate(rows);
            }
            catch (ArgumentException exception)
            {
                Error(exception.Message, batch.Id);
                return batch;
            }

            return batch with
            {
                DistributionId = null,
                DistributionIds = null,
                BoundDistributions = rows,
                RepeatDistribution = repeat,
                Count = count,
                MaxCount = maximum
            };
        }

        Node[] Bind(Node[] nodes)
        {
            return nodes.Select(node =>
            {
                if (node is DrawManyNode batch)
                {
                    return BindBatch(batch);
                }

                if (node is not DrawNode draw || !RequiresBinding(draw))
                {
                    return node;
                }

                if (draw.DrawWeights is not null || draw.WeightExpressionId is not null)
                {
                    Error("A distribution reference cannot also specify inline or expression weights.", draw.Id);
                    return node;
                }

                if (draw.DistributionIds is { } ids)
                {
                    if (draw.WeightVectorExpressionId is not null || draw.DistributionId is not null ||
                        ids.Length is 0 or > MaxFamilyMembers
                        || string.IsNullOrWhiteSpace(
                            draw.DistributionIndexExpressionId))
                    {
                        Error("A categorical family requires 1..256 asset IDs and exactly one index expression.",
                            draw.Id);
                        return node;
                    }

                    var rows = new DrawWeight[ids.Length][];
                    for (var index = 0; index < ids.Length; index++)
                    {
                        if (ids[index] is null || !assets.TryGetValue(ids[index], out DrawWeight[]? row))
                        {
                            Error($"Distribution family member '{ids[index]}' is not defined or is invalid.", draw.Id);
                            return node;
                        }

                        rows[index] = row;
                    }

                    try
                    {
                        DrawData.Validate(rows);
                    }
                    catch (ArgumentException exception)
                    {
                        Error(exception.Message, draw.Id);
                        return node;
                    }

                    return draw with
                    {
                        DistributionIds = null,
                        BoundDistributions = rows
                    };
                }

                if (draw.DistributionId is not { } id || draw.DistributionIndexExpressionId is not null)
                {
                    Error("A distribution index expression requires an explicit family.", draw.Id);
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
