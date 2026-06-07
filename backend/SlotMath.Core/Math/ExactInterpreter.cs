using System.Numerics;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;

namespace SlotMath.Core.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  ExactInterpreter — the natural transformation Slot → Dist
//
//  Cache key is (programObjectId, recurrenceHash).  Because program nodes
//  embedded in the original program tree have stable identities, paths
//  that converge to the same (sub-program, recurrence-state) pair hit the
//  cache automatically.  Dynamically-created Pures from Draw.Next(i) are
//  NOT cached individually — instead the Draw node caches its full result.
//
//  Budget support (G8): when a Budget is provided via ExactConfig, the
//  interpreter checks the branch count at each Draw node and throws
//  BudgetExceededException if the limit is exceeded.  The caller (regime
//  selector / hybrid evaluator) catches this and falls back to sampled.
// ═══════════════════════════════════════════════════════════════════════════

public sealed class ExactConfig
{
    public BigInteger EpsilonNumerator { get; init; }
    public BigInteger EpsilonDenominator { get; init; } = 1;
    public bool HasEpsilon => EpsilonNumerator > 0;

    /// <summary>
    /// Optional budget for branch-count enforcement (G8).
    /// When set, the interpreter throws <see cref="BudgetExceededException"/>
    /// if the total branch count exceeds the budget.
    /// </summary>
    public Budget? Budget { get; init; }
}

public sealed class EvalStats
{
    public long TotalBranchesEvaluated { get; set; }
    public long DrawsEvaluated { get; set; }
    public int CacheHits { get; set; }
    public int CacheMisses { get; set; }
    public int MaxRecursionDepth { get; set; }
}

public static class ExactInterpreter
{
    public static ExactResult<S, T> Evaluate<S, T>(
        Slot<S, T> program,
        S initialState,
        Func<S, BigInteger> recurrenceHasher,
        ExactConfig? config = null)
        where S : notnull
        where T : notnull
    {
        config ??= new ExactConfig();
        var nodeIds = new Dictionary<object, int>();
        var cache = new Dictionary<(int, BigInteger), object>();
        var stats = new EvalStats();

        var dist = EvalSpine<S, T>(
            program, initialState,
            contStack: new Stack<Func<object, object>>(),
            nodeIds, cache, config, recurrenceHasher, stats, depth: 0);

        return new ExactResult<S, T>(dist, stats);
    }

    private static Dist<(S, T)> EvalSpine<S, T>(
        object program,
        S state,
        Stack<Func<object, object>> contStack,
        Dictionary<object, int> nodeIds,
        Dictionary<(int, BigInteger), object> cache,
        ExactConfig config,
        Func<S, BigInteger> recurrenceHasher,
        EvalStats stats,
        int depth)
        where S : notnull
    {
        const int maxDepth = 5_000;
        if (depth > maxDepth)
            throw new InvalidOperationException(
                $"Exact evaluation exceeded max recursion depth ({maxDepth}).");

        stats.MaxRecursionDepth = System.Math.Max(stats.MaxRecursionDepth, depth);

        // Cache at the entry-program level (the program node passed to us).
        var entryNodeId = GetOrAssignId(program, nodeIds);
        var entryRecurHash = recurrenceHasher(state);
        var entryKey = (entryNodeId, entryRecurHash);

        if (cache.TryGetValue(entryKey, out var cachedObj))
        {
            stats.CacheHits++;
            return (Dist<(S, T)>)cachedObj;
        }
        stats.CacheMisses++;

        // ── Iterative spine ──────────────────────────────────────────
        object current = program;
        var currentState = state;

        while (true)
        {
            // Unwrap FlatMap and annotation layers (both structural).
            bool unwrapped;
            do
            {
                unwrapped = false;
                while (current is IFlatMapNode fm)
                {
                    contStack.Push(v => fm.ApplyUntyped(v));
                    current = fm.SourceUntyped;
                    unwrapped = true;
                }
                while (current is IAnnotationNode ann)
                {
                    current = ann.InnerUntyped;
                    unwrapped = true;
                }
            } while (unwrapped);

            // ── Pure ─────────────────────────────────────────────────
            if (current is IPureNode pure)
            {
                object value = pure.ValueUntyped;

                if (contStack.Count == 0)
                {
                    var pt = Dist.Point<S, T>(currentState, (T)value);
                    cache[entryKey] = pt;
                    return pt;
                }

                var cont = contStack.Pop();
                current = cont(value);
                continue;
            }

            // ── GetState ─────────────────────────────────────────────
            if (current is IGetStateNode getState)
            {
                current = getState.NextUntyped(currentState!);
                continue;
            }

            // ── PutState ─────────────────────────────────────────────
            if (current is IPutStateNode putState)
            {
                currentState = (S)putState.ValueUntyped;
                current = putState.NextUntyped;
                continue;
            }

            // ── Draw ─────────────────────────────────────────────────
            if (current is IDrawNode draw)
            {
                stats.DrawsEvaluated++;

                var weightSet = (WeightSet)draw.WeightsUntyped(currentState!);
                var totalWeight = weightSet.NumeratorSum;
                var weightDen = weightSet.Denominator;

                // Check if this Draw+state result is cached.
                var drawNodeId = GetOrAssignId(current, nodeIds);
                var drawRecurHash = recurrenceHasher(currentState);
                var drawKey = (drawNodeId, drawRecurHash);

                if (contStack.Count == 0 && cache.TryGetValue(drawKey, out var drawCached))
                {
                    stats.CacheHits++;
                    var r = (Dist<(S, T)>)drawCached;
                    cache[entryKey] = r;
                    return r;
                }

                var builder = new DistBuilder<(S, T)>();
                stats.TotalBranchesEvaluated += weightSet.Count;

                // ── Budget enforcement (G8) ──────────────────────────
                if (config.Budget?.MaxBranches is { } maxBranches
                    && stats.TotalBranchesEvaluated > maxBranches)
                {
                    throw new BudgetExceededException(
                        stats.TotalBranchesEvaluated,
                        stats.DrawsEvaluated,
                        config.Budget,
                        "branches");
                }

                var allZeroContStack = contStack.Count == 0;

                for (var i = 0; i < weightSet.Count; i++)
                {
                    var w = weightSet.Numerators[i];
                    if (w == 0) continue;

                    var branchNum = w;
                    var branchDen = totalWeight * weightDen;

                    // Epsilon pruning.
                    if (config.HasEpsilon)
                    {
                        if (branchNum * config.EpsilonDenominator
                            < config.EpsilonNumerator * branchDen)
                        {
                            builder.AddPrunedMass(branchNum, branchDen);
                            continue;
                        }
                    }

                    var branchProg = draw.NextUntyped(i);

                    Dist<(S, T)> subDist;

                    if (contStack.Count == 0)
                    {
                        // No pending continuations — evaluate branch directly.
                        subDist = EvalSpine<S, T>(
                            branchProg, currentState,
                            new Stack<Func<object, object>>(),
                            nodeIds, cache, config, recurrenceHasher,
                            stats, depth + 1);
                    }
                    else
                    {
                        // Has pending continuations — copy the stack for this branch.
                        var branchStack = new Stack<Func<object, object>>(
                            contStack.Reverse());

                        // Try cache at Draw-level for this branch+stack combo.
                        // Use the branch program + state as cache key.
                        var branchNodeId = GetOrAssignId(branchProg, nodeIds);
                        var branchRecurHash = recurrenceHasher(currentState);
                        var branchKey = (branchNodeId, branchRecurHash);

                        if (cache.TryGetValue(branchKey, out var branchCached))
                        {
                            stats.CacheHits++;
                            subDist = (Dist<(S, T)>)branchCached;
                        }
                        else
                        {
                            subDist = EvalSpine<S, T>(
                                branchProg, currentState, branchStack,
                                nodeIds, cache, config, recurrenceHasher,
                                stats, depth + 1);
                        }
                    }

                    builder.Add(subDist, branchNum, branchDen);
                }

                var result = builder.Build();

                // Cache at the Draw level (only when no pending continuations).
                if (allZeroContStack)
                    cache[drawKey] = result;

                cache[entryKey] = result;
                return result;
            }

            throw new InvalidOperationException(
                $"Unknown Slot variant: {current.GetType().FullName}");
        }
    }

    private static int GetOrAssignId(object node, Dictionary<object, int> nodeIds)
    {
        if (nodeIds.TryGetValue(node, out var id))
            return id;
        id = nodeIds.Count;
        nodeIds[node] = id;
        return id;
    }
}

public sealed record ExactResult<S, T>(
    Dist<(S FinalState, T Value)> Distribution,
    EvalStats Stats
) where T : notnull
{
    public Dist<T> ValueDistribution()
    {
        var d = Distribution;
        if (d.IsEmpty)
        {
            return new Dist<T>(
                Array.Empty<Dist<T>.Entry>(),
                d.Denominator, 0,
                d.PrunedNumerator, d.PrunedDenominator);
        }

        var builder = new DistBuilder<T>();
        foreach (var entry in d.Entries)
            builder.Add(entry.Value.Value, entry.Numerator, d.Denominator);

        if (d.PrunedNumerator > 0)
            builder.AddPrunedMass(d.PrunedNumerator, d.PrunedDenominator);

        return builder.Build();
    }
}

public static class Dist
{
    public static Dist<(S, T)> Point<S, T>(S state, T value)
    {
        var entries = new[] { new Dist<(S, T)>.Entry((state, value), BigInteger.One) };
        return new Dist<(S, T)>(entries, BigInteger.One, BigInteger.One, 0, 1);
    }
}
