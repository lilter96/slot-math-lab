using System.Numerics;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;

namespace SlotMath.Core.Math.Regime;

// ═══════════════════════════════════════════════════════════════════════════
//  ProgramAnalysis — static analysis of a Slot program for regime selection
//
//  Walks the program tree to discover:
//    - Annotation nodes (subgraph markers, plugin flags)
//    - Estimated branch count (sum of Draw outcome counts)
//    - Whether any subtree forces sampled mode
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Result of analysing a Slot program tree for regime selection.
/// </summary>
public sealed class ProgramAnalysis
{
    /// <summary>Discovered subgraph annotations.</summary>
    public IReadOnlyList<SubgraphAnnotation> Subgraphs { get; init; } =
        Array.Empty<SubgraphAnnotation>();

    /// <summary>Conservative estimate of total branches (sum of Draw outcome counts).</summary>
    public long EstimatedBranches { get; init; }

    /// <summary>Whether any subtree is marked as containing a plugin.</summary>
    public bool ContainsPlugin { get; init; }

    /// <summary>Number of Draw nodes found.</summary>
    public int DrawCount { get; init; }

    /// <summary>Number of Loop (recursive) structures detected.</summary>
    public int LoopCount { get; init; }

    public override string ToString() =>
        $"ProgramAnalysis[branches≈{EstimatedBranches}, draws={DrawCount}, " +
        $"loops={LoopCount}, plugin={ContainsPlugin}, subgraphs={Subgraphs.Count}]";
}

/// <summary>
/// A subgraph annotation discovered during program analysis.
/// </summary>
public sealed record SubgraphAnnotation
{
    public required string SubgraphId { get; init; }
    public bool ContainsPlugin { get; init; }
    public long EstimatedBranches { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════
//  ProgramAnalyzer
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Walks a Slot program tree and extracts metadata for regime selection.
/// </summary>
public static class ProgramAnalyzer
{
    /// <summary>
    /// Analyze a program tree, collecting annotations and estimating branch count.
    /// </summary>
    public static ProgramAnalysis Analyze<S, T>(Slot<S, T> program) where S : notnull
    {
        var subgraphs = new List<SubgraphAnnotation>();
        var visited = new HashSet<object>();
        long estimatedBranches = 0;
        int drawCount = 0;
        int loopCount = 0;

        var queue = new Queue<(object Node, string? ParentSubgraphId, long Multiplier)>();
        queue.Enqueue((program!, null, 1L));

        while (queue.Count > 0)
        {
            var (node, parentSubgraphId, multiplier) = queue.Dequeue();

            if (!visited.Add(node))
            {
                // Already visited — this is a recursive reference, like a Loop.
                loopCount++;
                continue;
            }

            // ── Dispatch on node type ──────────────────────────────

            if (node is IAnnotationNode ann)
            {
                var annotation = ann as ProgramAnnotation<S, T>;
                var subId = annotation?.SubgraphId ?? "unnamed";
                var hasPlugin = annotation?.ContainsPlugin ?? false;

                // Merge with parent subgraph if unnamed and no plugin.
                if (subId == "unnamed" && !hasPlugin && parentSubgraphId != null)
                    subId = parentSubgraphId;

                subgraphs.Add(new SubgraphAnnotation
                {
                    SubgraphId = subId,
                    ContainsPlugin = hasPlugin,
                    EstimatedBranches = 0 // filled later or left as 0
                });

                queue.Enqueue((ann.InnerUntyped, subId, multiplier));
                continue;
            }

            if (node is IFlatMapNode fm)
            {
                queue.Enqueue((fm.SourceUntyped, parentSubgraphId, multiplier));
                // We can't easily analyse ApplyUntyped without a value.
                // The continuation will be reached via FlatMap's source.
                continue;
            }

            if (node is IDrawNode draw)
            {
                drawCount++;
                // Estimate: number of outcomes at this Draw.
                // We use a sentinel (null) for state — weight functions must handle this.
                WeightSet? ws = null;
                try { ws = (WeightSet)draw.WeightsUntyped(null!); }
                catch { /* can't get weights without state */ }

                var outcomes = ws?.Count ?? 2; // default to 2 if unknown
                estimatedBranches += outcomes * multiplier;

                // Each outcome branch may lead to more nodes.
                for (var i = 0; i < outcomes; i++)
                {
                    try
                    {
                        var next = draw.NextUntyped(i);
                        queue.Enqueue((next, parentSubgraphId,
                            multiplier * (long)outcomes));
                    }
                    catch { break; }
                }
                continue;
            }

            if (node is IGetStateNode gs)
            {
                try
                {
                    var next = gs.NextUntyped(null!);
                    queue.Enqueue((next, parentSubgraphId, multiplier));
                }
                catch { /* can't resolve without state */ }
                continue;
            }

            if (node is IPutStateNode ps)
            {
                queue.Enqueue((ps.NextUntyped, parentSubgraphId, multiplier));
                continue;
            }

            if (node is IPureNode)
            {
                // Leaf — nothing to follow.
                continue;
            }
        }

        return new ProgramAnalysis
        {
            Subgraphs = subgraphs,
            EstimatedBranches = estimatedBranches,
            ContainsPlugin = subgraphs.Any(s => s.ContainsPlugin),
            DrawCount = drawCount,
            LoopCount = loopCount
        };
    }
}
