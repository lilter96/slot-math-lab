using System.Diagnostics;
using System.Numerics;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;

namespace SlotMath.Core.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  ExactInterpreter — the natural transformation Slot → Dist
//
//  Fully iterative: an explicit frame stack replaces recursion, so programs
//  of arbitrary depth (deep loops, long draw chains) evaluate in constant
//  call-stack space.
//
//  Memoisation key is (drawNodeId, continuationId, recurrenceHash):
//    - drawNodeId      identifies the Draw node by reference,
//    - continuationId  identifies the pending continuation chain (the FlatMap
//                      nodes between the draw and the end of the program),
//                      interned so structurally identical chains share an id,
//    - recurrenceHash  is the caller-supplied canonical hash of the state.
//
//  Including the continuation in the key is what makes the cache sound: the
//  distribution computed from a draw runs through the rest of the program,
//  so two occurrences of the same (node, state) only share a result when the
//  rest of the program is also the same.  Continuation chains are immutable
//  cons cells, so entering a draw branch shares the chain instead of copying
//  a stack.
//
//  Loops built with Slot.Loop reuse one FlatMap node per iteration, so the
//  chain id recurs and convergent loop states hit the cache — collapsing the
//  exponential iteration tree into a DAG over distinct recurrence states.
//
//  Budget support (G8): when a Budget is provided via ExactConfig, the
//  interpreter checks branch counts and elapsed time at each Draw node and
//  throws BudgetExceededException if a limit is exceeded.  The caller
//  (regime selector / hybrid evaluator) catches this and falls back to
//  sampled.
// ═══════════════════════════════════════════════════════════════════════════

public sealed class ExactConfig
{
    public BigInteger EpsilonNumerator { get; init; }
    public BigInteger EpsilonDenominator { get; init; } = 1;
    public bool HasEpsilon => EpsilonNumerator > 0;

    /// <summary>
    /// Optional budget for branch-count and time enforcement (G8).
    /// When set, the interpreter throws <see cref="BudgetExceededException"/>
    /// if a limit is exceeded.
    /// </summary>
    public Budget? Budget { get; init; }
}

public sealed class EvalStats
{
    public long TotalBranchesEvaluated { get; set; }
    public long DrawsEvaluated { get; set; }
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }

    /// <summary>Maximum depth of nested unresolved draws (frame-stack depth).</summary>
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
        var stats = new EvalStats();
        var dist = EvalCore<S, T>(program, initialState, config, recurrenceHasher, stats);
        return new ExactResult<S, T>(dist, stats);
    }

    // ── Continuation chain ───────────────────────────────────────────────
    //
    // An immutable cons list of pending FlatMap nodes.  Cells are interned
    // per evaluation: pushing the same FlatMap node onto the same chain
    // always yields the same cell, so chain identity (Id) equals chain
    // content and can be used directly in memo keys.

    private sealed class ContChain
    {
        public readonly IFlatMapNode Node;
        public readonly ContChain? Next;
        public readonly int Id;
        private Dictionary<object, ContChain>? _children;

        private ContChain(IFlatMapNode node, ContChain? next, int id)
        {
            Node = node;
            Next = next;
            Id = id;
        }

        public static ContChain NewRoot() => new(null!, null, 0);

        public bool IsRoot => Next is null && Node is null;

        public ContChain Push(IFlatMapNode fm, ref int idCounter)
        {
            _children ??= new Dictionary<object, ContChain>(ReferenceEqualityComparer.Instance);
            if (!_children.TryGetValue(fm, out var child))
            {
                child = new ContChain(fm, this, ++idCounter);
                _children[fm] = child;
            }
            return child;
        }
    }

    // ── Pending draw frame ───────────────────────────────────────────────

    private sealed class DrawFrame<S, T> where S : notnull
    {
        public required DistBuilder<(S, T)> Builder;
        public required WeightSet Weights;
        public required BigInteger TotalWeight;
        public required IDrawNode Node;
        public required S State;
        public required ContChain Cont;
        public required MemoKey Key;
        public int Index = -1; // index of the branch currently being evaluated
    }

    private readonly record struct MemoKey(int NodeId, int ContId, BigInteger StateHash);

    // ── Core evaluation loop ─────────────────────────────────────────────

    private static Dist<(S, T)> EvalCore<S, T>(
        object program,
        S initialState,
        ExactConfig config,
        Func<S, BigInteger> recurrenceHasher,
        EvalStats stats)
        where S : notnull
    {
        var nodeIds = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var cache = new Dictionary<MemoKey, Dist<(S, T)>>();
        var frames = new Stack<DrawFrame<S, T>>();
        var root = ContChain.NewRoot();
        var contIdCounter = 0;
        var startTimestamp = Stopwatch.GetTimestamp();
        var maxBranches = config.Budget?.MaxBranches;
        var maxTime = config.Budget?.MaxTime;

        object current = program;
        var state = initialState;
        var cont = root;

        // When a sub-evaluation finishes, its distribution lands here and the
        // completion loop below folds it into the enclosing draw frame.
        Dist<(S, T)>? completed = null;

        while (true)
        {
            if (completed is not null)
            {
                // ── Fold a finished sub-distribution into the top frame ──
                if (frames.Count == 0)
                    return completed;

                var frame = frames.Peek();
                frame.Builder.Add(
                    completed, frame.Weights.Numerators[frame.Index], frame.TotalWeight);
                completed = null;

                if (AdvanceFrame(frame, config))
                {
                    current = frame.Node.NextUntyped(frame.Index);
                    state = frame.State;
                    cont = frame.Cont;
                }
                else
                {
                    completed = FinishFrame(frames, cache);
                }
                continue;
            }

            // ── Unwrap structural layers ─────────────────────────────────
            while (true)
            {
                if (current is IFlatMapNode fm)
                {
                    cont = cont.Push(fm, ref contIdCounter);
                    current = fm.SourceUntyped;
                }
                else if (current is IAnnotationNode ann)
                {
                    current = ann.InnerUntyped;
                }
                else
                {
                    break;
                }
            }

            // ── Pure ─────────────────────────────────────────────────────
            if (current is IPureNode pure)
            {
                var value = pure.ValueUntyped;
                if (!cont.IsRoot)
                {
                    var cell = cont;
                    cont = cell.Next!;
                    current = cell.Node.ApplyUntyped(value);
                    continue;
                }

                // Leaf of the whole program: a single (state, value) outcome.
                if (frames.Count == 0)
                    return Dist.Point<S, T>(state, (T)value);

                var frame = frames.Peek();
                frame.Builder.Add(
                    (state, (T)value), frame.Weights.Numerators[frame.Index], frame.TotalWeight);

                if (AdvanceFrame(frame, config))
                {
                    current = frame.Node.NextUntyped(frame.Index);
                    state = frame.State;
                    cont = frame.Cont;
                }
                else
                {
                    completed = FinishFrame(frames, cache);
                }
                continue;
            }

            // ── GetState ─────────────────────────────────────────────────
            if (current is IGetStateNode getState)
            {
                current = getState.NextUntyped(state!);
                continue;
            }

            // ── PutState ─────────────────────────────────────────────────
            if (current is IPutStateNode putState)
            {
                state = (S)putState.ValueUntyped;
                current = putState.NextUntyped;
                continue;
            }

            // ── ModifyState (fused get+put) ──────────────────────────────
            if (current is IModifyStateNode modify)
            {
                state = (S)modify.ApplyUntyped(state!);
                current = modify.NextUntyped;
                continue;
            }

            // ── Draw ─────────────────────────────────────────────────────
            if (current is IDrawNode draw)
            {
                stats.DrawsEvaluated++;

                var key = new MemoKey(
                    GetOrAssignId(draw, nodeIds), cont.Id, recurrenceHasher(state));

                if (cache.TryGetValue(key, out var hit))
                {
                    stats.CacheHits++;
                    completed = hit;
                    continue;
                }
                stats.CacheMisses++;

                var weightSet = (WeightSet)draw.WeightsUntyped(state!);
                stats.TotalBranchesEvaluated += weightSet.Count;

                // ── Budget enforcement (G8) ──────────────────────────────
                if (maxBranches is { } mb && stats.TotalBranchesEvaluated > mb)
                {
                    throw new BudgetExceededException(
                        stats.TotalBranchesEvaluated, stats.DrawsEvaluated,
                        config.Budget!, "branches");
                }
                if (maxTime is { } mt && Stopwatch.GetElapsedTime(startTimestamp) > mt)
                {
                    throw new BudgetExceededException(
                        stats.TotalBranchesEvaluated, stats.DrawsEvaluated,
                        config.Budget!, "time");
                }

                var frame = new DrawFrame<S, T>
                {
                    Builder = new DistBuilder<(S, T)>(),
                    Weights = weightSet,
                    // The probability of branch i is Numerators[i] / NumeratorSum;
                    // a shared weight denominator cancels out of the normalisation.
                    TotalWeight = weightSet.NumeratorSum,
                    Node = draw,
                    State = state,
                    Cont = cont,
                    Key = key,
                };
                frames.Push(frame);
                if (frames.Count > stats.MaxRecursionDepth)
                    stats.MaxRecursionDepth = frames.Count;

                if (AdvanceFrame(frame, config))
                {
                    current = draw.NextUntyped(frame.Index);
                    // state and cont stay as they are — branches start from
                    // the draw's state and share its continuation chain.
                }
                else
                {
                    completed = FinishFrame(frames, cache);
                }
                continue;
            }

            throw new InvalidOperationException(
                $"Unknown Slot variant: {current.GetType().FullName}");
        }
    }

    /// <summary>
    /// Advance the frame to its next evaluable branch, accumulating pruned
    /// mass for epsilon-pruned branches.  Returns false when no branches remain.
    /// </summary>
    private static bool AdvanceFrame<S, T>(DrawFrame<S, T> frame, ExactConfig config)
        where S : notnull
    {
        var numerators = frame.Weights.Numerators;
        for (var i = frame.Index + 1; i < numerators.Length; i++)
        {
            var w = numerators[i];
            if (w == 0)
                continue;

            if (config.HasEpsilon
                && w * config.EpsilonDenominator < config.EpsilonNumerator * frame.TotalWeight)
            {
                frame.Builder.AddPrunedMass(w, frame.TotalWeight);
                continue;
            }

            frame.Index = i;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Build the finished frame's distribution, cache it, and pop the frame.
    /// </summary>
    private static Dist<(S, T)> FinishFrame<S, T>(
        Stack<DrawFrame<S, T>> frames,
        Dictionary<MemoKey, Dist<(S, T)>> cache)
        where S : notnull
    {
        var frame = frames.Pop();
        var dist = frame.Builder.Build();
        cache[frame.Key] = dist;
        return dist;
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
