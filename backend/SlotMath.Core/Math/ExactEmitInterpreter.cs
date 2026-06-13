using System.Diagnostics;
using System.Numerics;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;

namespace SlotMath.Core.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  ExactEmitInterpreter — exact distribution of total Emit'd win (PRD v3.1)
//
//  The natural transformation for Emit-based programs: Slot<S,Unit> → Dist<Rational>
//  over the round's total emitted win, plus exact per-label expected values
//  (per-feature RTP, D4/D13). Probabilities and amounts are exact rationals
//  (invariant 1).
//
//  Built on the same frame-stack / continuation-chain / memoisation design the
//  value-returning ExactInterpreter uses, with one specialisation: the win
//  accumulator is a threaded Rational held SEPARATE from the recurrence state
//  (invariant 4). Before each Draw the accumulator is normalised to zero, so
//  the cached sub-distribution is in delta form; on reuse it is shifted by the
//  caller's accumulator (the convolution step). The recurrence hash is over
//  state only — Emit never enters the memo key.
//
//  Loops desugar to the self-referential structure the memoiser collapses;
//  finiteness comes from state convergence under the cap and the G8 budget.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Result of an exact Emit evaluation.</summary>
public sealed record ExactEmitResult(
    Dist<Rational> TotalWin,
    IReadOnlyDictionary<string, Rational> PerLabelExpectation,
    EvalStats Stats,
    ProvenanceTag Provenance)
{
    /// <summary>Exact expected total win = RTP (bet normalised to 1) (D4).</summary>
    public Rational ExpectedWin => ExactEmitInterpreter.Expectation(TotalWin);

    /// <summary>Exact variance of total win from the full distribution (D4).</summary>
    public Rational Variance => ExactEmitInterpreter.VarianceOf(TotalWin);

    /// <summary>P(total win &gt; 0) = hit frequency (D4).</summary>
    public Rational HitFrequency => ExactEmitInterpreter.ProbabilityPositive(TotalWin);
}

public static class ExactEmitInterpreter
{
    /// <summary>
    /// Evaluate an Emit program exactly. When <paramref name="labels"/> is given,
    /// each label's exact expected value is also computed (a separate pass per
    /// label; by linearity these sum to the total expected win).
    /// </summary>
    public static ExactEmitResult Evaluate<S>(
        Slot<S, Unit> program,
        S initialState,
        Func<S, BigInteger> recurrenceHasher,
        ExactConfig? config = null,
        IReadOnlyCollection<string>? labels = null,
        Rational? winCap = null)
        where S : notnull
    {
        config ??= new ExactConfig();
        var stats = new EvalStats();
        var total = EvalCore(program, initialState, config, recurrenceHasher, labelFilter: null, stats);
        if (winCap is { } cap)
            total = ClampToCap(total, cap);

        var perLabel = new Dictionary<string, Rational>();
        if (labels is not null)
        {
            foreach (var label in labels)
            {
                var labelDist = EvalCore(
                    program, initialState, config, recurrenceHasher, labelFilter: label, new EvalStats());
                perLabel[label] = Expectation(labelDist);
            }
        }

        var provenance = total.IsFullyExact
            ? ProvenanceTag.Exact
            : ProvenanceTag.Interval(
                lo: Expectation(total).ToDouble(),
                hi: Expectation(total).ToDouble() + (double)total.PrunedNumerator / (double)total.PrunedDenominator,
                prunedMass: (double)total.PrunedNumerator / (double)total.PrunedDenominator,
                BoundSource.DeclaredWinCap);

        return new ExactEmitResult(total, perLabel, stats, provenance);
    }

    // ── Metric helpers over the total-win distribution ───────────────────

    /// <summary>Expected value E[X] as an exact rational.</summary>
    public static Rational Expectation(Dist<Rational> dist)
    {
        if (dist.IsEmpty) return Rational.Zero;
        var acc = Rational.Zero;
        foreach (var e in dist.Entries)
            acc += e.Value * (Rational)e.Numerator;
        return acc / (Rational)dist.Denominator;
    }

    /// <summary>Variance E[X²] − E[X]² as an exact rational.</summary>
    public static Rational VarianceOf(Dist<Rational> dist)
    {
        if (dist.IsEmpty) return Rational.Zero;
        var mean = Expectation(dist);
        var sqAcc = Rational.Zero;
        foreach (var e in dist.Entries)
            sqAcc += e.Value * e.Value * (Rational)e.Numerator;
        var meanSq = sqAcc / (Rational)dist.Denominator;
        return meanSq - mean * mean;
    }

    /// <summary>P(X &gt; 0) as an exact rational.</summary>
    public static Rational ProbabilityPositive(Dist<Rational> dist)
    {
        if (dist.IsEmpty) return Rational.Zero;
        var hits = BigInteger.Zero;
        foreach (var e in dist.Entries)
            if (e.Value.Sign > 0)
                hits += e.Numerator;
        return new Rational(hits, dist.Denominator);
    }

    /// <summary>P(X ≥ cap) as an exact rational (cap-reached probability, D4).</summary>
    public static Rational ProbabilityAtLeast(Dist<Rational> dist, Rational threshold)
    {
        if (dist.IsEmpty) return Rational.Zero;
        var hits = BigInteger.Zero;
        foreach (var e in dist.Entries)
            if (e.Value >= threshold)
                hits += e.Numerator;
        return new Rational(hits, dist.Denominator);
    }

    // ── Continuation chain (interned cons list of pending FlatMap nodes) ──

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

    private sealed class DrawFrame<S> where S : notnull
    {
        public required DistBuilder<Rational> Builder;
        public required WeightSet Weights;
        public required BigInteger TotalWeight;
        public required IDrawNode Node;
        public required S State;
        public required ContChain Cont;
        public required MemoKey Key;
        public int Index = -1;
        public Rational AccShift;
    }

    private readonly record struct MemoKey(int NodeId, int ContId, BigInteger StateHash);

    // ── Core evaluation loop (mirrors ExactInterpreter.EvalCore) ─────────

    private static Dist<Rational> EvalCore<S>(
        object program,
        S initialState,
        ExactConfig config,
        Func<S, BigInteger> recurrenceHasher,
        string? labelFilter,
        EvalStats stats)
        where S : notnull
    {
        var nodeIds = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var cache = new Dictionary<MemoKey, Dist<Rational>>();
        var frames = new Stack<DrawFrame<S>>();
        var root = ContChain.NewRoot();
        var contIdCounter = 0;
        var startTimestamp = Stopwatch.GetTimestamp();
        var maxBranches = config.Budget?.MaxBranches;
        var maxTime = config.Budget?.MaxTime;

        object current = program;
        var state = initialState;
        var cont = root;
        var acc = Rational.Zero;

        Dist<Rational>? completed = null;

        while (true)
        {
            if (completed is not null)
            {
                if (frames.Count == 0)
                    return completed;

                var frame = frames.Peek();
                frame.Builder.Add(completed, frame.Weights.Numerators[frame.Index], frame.TotalWeight);
                completed = null;

                if (AdvanceFrame(frame, config))
                {
                    current = frame.Node.NextUntyped(frame.Index);
                    state = frame.State;
                    cont = frame.Cont;
                    acc = Rational.Zero;
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
                else if (current is ILoopNode loop)
                {
                    current = loop.DesugarUntyped;
                }
                else
                {
                    break;
                }
            }

            // ── Pure: thread the program value through continuations; the
            //   FINAL leaf value is the accumulated win (the value flow and the
            //   win accumulator are independent — invariant 4). ──────────────
            if (current is IPureNode pure)
            {
                if (!cont.IsRoot)
                {
                    var cell = cont;
                    cont = cell.Next!;
                    current = cell.Node.ApplyUntyped(pure.ValueUntyped);
                    continue;
                }

                if (frames.Count == 0)
                    return PointDist(acc);

                var frame = frames.Peek();
                frame.Builder.Add(acc, frame.Weights.Numerators[frame.Index], frame.TotalWeight);

                if (AdvanceFrame(frame, config))
                {
                    current = frame.Node.NextUntyped(frame.Index);
                    state = frame.State;
                    cont = frame.Cont;
                    acc = Rational.Zero;
                }
                else
                {
                    completed = FinishFrame(frames, cache);
                }
                continue;
            }

            if (current is IGetStateNode getState)
            {
                current = getState.NextUntyped(state!);
                continue;
            }

            if (current is IPutStateNode putState)
            {
                state = (S)putState.ValueUntyped;
                current = putState.NextUntyped;
                continue;
            }

            if (current is IModifyStateNode modify)
            {
                state = (S)modify.ApplyUntyped(state!);
                current = modify.NextUntyped;
                continue;
            }

            // ── Emit: accumulate the labeled win (held separate from state) ──
            if (current is IEmitNode emit)
            {
                if (labelFilter is null || emit.Label == labelFilter)
                    acc += emit.AmountUntyped(state!);
                current = emit.NextUntyped;
                continue;
            }

            // ── Truncate: the current path's mass becomes pruned (D6) ────
            if (current is ITruncateNode)
            {
                completed = PrunedMassDist();
                continue;
            }

            if (current is IDrawNode draw)
            {
                stats.DrawsEvaluated++;

                // Normalise the accumulator to zero (invariant 4): cache in delta
                // form, shift the result by the entry accumulator on the way out.
                var accShift = acc;
                acc = Rational.Zero;

                var key = new MemoKey(
                    GetOrAssignId(draw, nodeIds), cont.Id, recurrenceHasher(state));

                if (cache.TryGetValue(key, out var hit))
                {
                    stats.CacheHits++;
                    completed = ShiftByAccumulator(hit, accShift);
                    continue;
                }
                stats.CacheMisses++;

                var weightSet = (WeightSet)draw.WeightsUntyped(state!);
                stats.TotalBranchesEvaluated += weightSet.Count;

                if (maxBranches is { } mb && stats.TotalBranchesEvaluated > mb)
                    throw new BudgetExceededException(
                        stats.TotalBranchesEvaluated, stats.DrawsEvaluated, config.Budget!, "branches");
                if (maxTime is { } mt && Stopwatch.GetElapsedTime(startTimestamp) > mt)
                    throw new BudgetExceededException(
                        stats.TotalBranchesEvaluated, stats.DrawsEvaluated, config.Budget!, "time");

                var frame = new DrawFrame<S>
                {
                    Builder = new DistBuilder<Rational>(),
                    Weights = weightSet,
                    TotalWeight = weightSet.NumeratorSum,
                    Node = draw,
                    State = state,
                    Cont = cont,
                    Key = key,
                    AccShift = accShift,
                };
                frames.Push(frame);
                if (frames.Count > stats.MaxRecursionDepth)
                    stats.MaxRecursionDepth = frames.Count;

                if (AdvanceFrame(frame, config))
                {
                    current = draw.NextUntyped(frame.Index);
                    // state stays; branches start from the draw's state and acc = 0.
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

    private static bool AdvanceFrame<S>(DrawFrame<S> frame, ExactConfig config) where S : notnull
    {
        var numerators = frame.Weights.Numerators;
        for (var i = frame.Index + 1; i < numerators.Length; i++)
        {
            var w = numerators[i];
            if (w == 0) continue;

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

    private static Dist<Rational> FinishFrame<S>(
        Stack<DrawFrame<S>> frames,
        Dictionary<MemoKey, Dist<Rational>> cache)
        where S : notnull
    {
        var frame = frames.Pop();
        var dist = frame.Builder.Build();
        cache[frame.Key] = dist;
        return ShiftByAccumulator(dist, frame.AccShift);
    }

    /// <summary>Translate every outcome's win by <paramref name="shift"/> (the convolution step).</summary>
    private static Dist<Rational> ShiftByAccumulator(Dist<Rational> dist, Rational shift)
    {
        if (shift.IsZero || dist.IsEmpty)
            return dist;

        var source = dist.Entries;
        var entries = new Dist<Rational>.Entry[source.Count];
        for (var i = 0; i < entries.Length; i++)
            entries[i] = new Dist<Rational>.Entry(source[i].Value + shift, source[i].Numerator);

        return new Dist<Rational>(
            entries, dist.Denominator, dist.TotalNumerator, dist.PrunedNumerator, dist.PrunedDenominator);
    }

    /// <summary>Clamp outcomes above the win cap to the cap (D13), merging equal values.</summary>
    private static Dist<Rational> ClampToCap(Dist<Rational> dist, Rational cap)
    {
        if (dist.IsEmpty) return dist;
        var anyOver = false;
        foreach (var e in dist.Entries)
            if (e.Value > cap) { anyOver = true; break; }
        if (!anyOver) return dist;

        var builder = new DistBuilder<Rational>();
        foreach (var e in dist.Entries)
            builder.Add(e.Value > cap ? cap : e.Value, e.Numerator, dist.Denominator);
        if (dist.PrunedNumerator > 0)
            builder.AddPrunedMass(dist.PrunedNumerator, dist.PrunedDenominator);
        return builder.Build();
    }

    private static Dist<Rational> PointDist(Rational value)
    {
        var builder = new DistBuilder<Rational>();
        builder.Add(value, BigInteger.One, BigInteger.One);
        return builder.Build();
    }

    /// <summary>A distribution that is entirely pruned mass (one unit) — a truncated path.</summary>
    private static Dist<Rational> PrunedMassDist() =>
        new(Array.Empty<Dist<Rational>.Entry>(),
            denominator: BigInteger.One, totalNumerator: BigInteger.Zero,
            prunedNumerator: BigInteger.One, prunedDenominator: BigInteger.One);

    private static int GetOrAssignId(object node, Dictionary<object, int> nodeIds)
    {
        if (nodeIds.TryGetValue(node, out var id))
            return id;
        id = nodeIds.Count;
        nodeIds[node] = id;
        return id;
    }
}
