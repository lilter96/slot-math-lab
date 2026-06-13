using System.Diagnostics;
using System.Numerics;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;

namespace SlotMath.Core.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  SampledInterpreter — Monte Carlo evaluation of Slot programs
//
//  Runs N independent spins through the program, using the AliasMethod for
//  weighted random draws.  Accumulates streaming Welford statistics, a
//  histogram, and max-win tracking.
//
//  The interpreter is:
//    - Trampolined: each spin uses a while-loop + explicit continuation stack
//                   (constant stack depth regardless of program depth).
//    - Deterministic: a given seed yields identical sample sequences.
//    - Cancellable: CancellationToken checked between spins; partial stats
//                   returned with the correct n.
//    - Tail-aware: tracks max observed win, cap enforcement, and cap-hit counts.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Configuration for a sampled (Monte Carlo) evaluation run.
/// </summary>
public sealed class SampledConfig
{
    /// <summary>Master seed for the PRNG. Ensures deterministic reproducibility.</summary>
    public long Seed { get; init; }

    /// <summary>Maximum number of spins to run.</summary>
    public long MaxSpins { get; init; } = 1_000_000;

    /// <summary>
    /// Optional cap on per-spin win values.  Values above the cap are clamped
    /// to the cap value and counted as cap-hits.  When null, no capping is applied.
    /// </summary>
    public BigInteger? MaxWinCap { get; init; }

    /// <summary>Number of equal-width histogram bins (default 50).</summary>
    public int HistogramBins { get; init; } = 50;

    /// <summary>Cancellation token for early termination.</summary>
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;

    /// <summary>
    /// How often to check the cancellation token, in number of spins.
    /// Default 1000 balances responsiveness with overhead.
    /// Set to 1 for API runs requiring sub-500ms cancellation.
    /// </summary>
    public int CancellationCheckInterval { get; init; } = 1000;

    /// <summary>How often to report progress, in number of spins (default 1000).</summary>
    public int ProgressReportInterval { get; init; } = 1000;

    /// <summary>
    /// Optional callback invoked every <see cref="ProgressReportInterval"/> spins
    /// with a snapshot of current statistics.  Invoked synchronously on the
    /// worker thread — callers should keep it fast and not block.
    /// </summary>
    public Action<SampledProgress>? ProgressCallback { get; init; }

    /// <summary>
    /// Maximum number of worker threads.  1 (default) runs the classic
    /// sequential interpreter.  Above 1, spins are partitioned over a FIXED
    /// number of logical streams (independent of thread count), each with a
    /// seed derived deterministically from <see cref="Seed"/>, and merged in
    /// stream order — so the final statistics are a pure function of
    /// (Seed, MaxSpins) on any machine, while wall-clock time scales with
    /// available cores.
    /// </summary>
    public int DegreeOfParallelism { get; init; } = 1;

    /// <summary>
    /// Sub-credit scale of the program's win amounts.  The compiler emits
    /// wins multiplied by this factor when fractional paytable payouts are
    /// present; sampled statistics divide it back out per spin so all stats
    /// are in credit units.
    /// </summary>
    public double WinScale { get; init; } = 1.0;
}

/// <summary>
/// The result of a sampled Monte Carlo evaluation.
/// </summary>
public sealed class SampledResult<S>
{
    /// <summary>Streaming statistics accumulated across all completed spins.</summary>
    public StreamingStats Stats { get; }

    /// <summary>Number of spins completed (may be less than MaxSpins if cancelled).</summary>
    public long SpinsCompleted { get; }

    /// <summary>True when the run was cancelled before completing all requested spins.</summary>
    public bool WasCancelled { get; }

    /// <summary>The master seed used.</summary>
    public long Seed { get; }

    /// <summary>Elapsed wall-clock time for the run.</summary>
    public TimeSpan Elapsed { get; }

    internal SampledResult(StreamingStats stats, long spinsCompleted, bool wasCancelled,
                           long seed, TimeSpan elapsed)
    {
        Stats = stats;
        SpinsCompleted = spinsCompleted;
        WasCancelled = wasCancelled;
        Seed = seed;
        Elapsed = elapsed;
    }

    public override string ToString() =>
        $"SampledResult[n={SpinsCompleted}, μ={Stats.Mean:F4}, " +
        $"σ={Stats.StdDev:F4}, cancelled={WasCancelled}]";
}

// ═══════════════════════════════════════════════════════════════════════════
//  Interpreter
// ═══════════════════════════════════════════════════════════════════════════

public static class SampledInterpreter
{
    /// <summary>
    /// Evaluate a program via Monte Carlo sampling.
    ///
    /// Runs <paramref name="config"/>.MaxSpins independent spins, each from the
    /// initial state.  The program's result type must be <see cref="BigInteger"/>
    /// (the per-spin win amount) or convertible via a selector.
    /// </summary>
    public static SampledResult<S> Evaluate<S>(
        Slot<S, BigInteger> program,
        S initialState,
        SampledConfig config)
        where S : notnull
    {
        if (config.DegreeOfParallelism > 1 && config.MaxSpins > 1)
            return EvaluateParallel(program, initialState, config);

        var startedAt = Stopwatch.GetTimestamp();
        var rng = new SeededRandom(config.Seed);
        var maxWinCap = config.MaxWinCap;
        double? maxWinCapDouble = maxWinCap.HasValue ? (double)maxWinCap.Value : null;
        var stats = new StreamingStats(config.HistogramBins, maxWinCapDouble);
        var cancelled = false;
        var stack = new Stack<IFlatMapNode>();
        long spin;

        for (spin = 0; spin < config.MaxSpins; spin++)
        {
            // ── Cancellation check ──────────────────────────────────────
            if (spin > 0 && spin % config.CancellationCheckInterval == 0
                && config.CancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            // ── Run one spin ────────────────────────────────────────────
            var win = RunOneSpin(program, initialState, rng, stack);
            stats.Add((double)win / config.WinScale);

            // ── Progress callback ──────────────────────────────────────
            if (spin > 0 && spin % config.ProgressReportInterval == 0
                && config.ProgressCallback is not null)
            {
                config.ProgressCallback(new SampledProgress
                {
                    SpinsCompleted = spin + 1,
                    TotalSpins = config.MaxSpins,
                    Stats = stats.Snapshot(),
                    Elapsed = Stopwatch.GetElapsedTime(startedAt),
                });
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        // Final progress callback with complete stats
        if (!cancelled && config.ProgressCallback is not null)
        {
            config.ProgressCallback(new SampledProgress
            {
                SpinsCompleted = spin,
                TotalSpins = config.MaxSpins,
                Stats = stats.Snapshot(),
                Elapsed = elapsed,
            });
        }

        // If we were cancelled before any spin completed, spin is 0.
        return new SampledResult<S>(stats, spin, cancelled, config.Seed, elapsed);
    }

    /// <summary>
    /// Evaluate with a selector function that extracts a BigInteger value from T.
    /// </summary>
    public static SampledResult<S> Evaluate<S, T>(
        Slot<S, T> program,
        S initialState,
        Func<T, BigInteger> selector,
        SampledConfig config)
        where S : notnull
    {
        var startedAt = Stopwatch.GetTimestamp();
        var rng = new SeededRandom(config.Seed);
        var maxWinCap = config.MaxWinCap;
        double? maxWinCapDouble = maxWinCap.HasValue ? (double)maxWinCap.Value : null;
        var stats = new StreamingStats(config.HistogramBins, maxWinCapDouble);
        var cancelled = false;
        var stack = new Stack<IFlatMapNode>();
        long spin;

        for (spin = 0; spin < config.MaxSpins; spin++)
        {
            if (spin > 0 && spin % config.CancellationCheckInterval == 0
                && config.CancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            var value = RunOneSpin(program, initialState, rng, stack);
            stats.Add(selector(value));

            // ── Progress callback ──────────────────────────────────────
            if (spin > 0 && spin % config.ProgressReportInterval == 0
                && config.ProgressCallback is not null)
            {
                config.ProgressCallback(new SampledProgress
                {
                    SpinsCompleted = spin + 1,
                    TotalSpins = config.MaxSpins,
                    Stats = stats.Snapshot(),
                    Elapsed = Stopwatch.GetElapsedTime(startedAt),
                });
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        // Final progress callback with complete stats
        if (!cancelled && config.ProgressCallback is not null)
        {
            config.ProgressCallback(new SampledProgress
            {
                SpinsCompleted = spin,
                TotalSpins = config.MaxSpins,
                Stats = stats.Snapshot(),
                Elapsed = elapsed,
            });
        }

        return new SampledResult<S>(stats, spin, cancelled, config.Seed, elapsed);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Parallel evaluation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Number of logical sample streams used by parallel evaluation.  Fixed
    /// (not derived from core count) so results are identical anywhere.
    /// </summary>
    internal const int ParallelStreams = 32;

    private static SampledResult<S> EvaluateParallel<S>(
        Slot<S, BigInteger> program,
        S initialState,
        SampledConfig config)
        where S : notnull
    {
        var startedAt = Stopwatch.GetTimestamp();
        var maxWinCap = config.MaxWinCap;
        double? maxWinCapDouble = maxWinCap.HasValue ? (double)maxWinCap.Value : null;

        var streams = (int)System.Math.Min(ParallelStreams, config.MaxSpins);
        var baseSpins = config.MaxSpins / streams;
        var remainder = config.MaxSpins % streams;

        var streamStats = new StreamingStats[streams];
        var streamCompleted = new long[streams];
        var published = new StreamingStatsSnapshot?[streams];
        long reportedThreshold = 0;
        var anyCancelled = false;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = config.DegreeOfParallelism,
        };

        // The report interval is an aggregate cadence: each stream publishes
        // its snapshot every interval/streams spins so the global counter
        // crosses the configured interval at the configured rate.  (Gating
        // per-stream on the full interval would mean streams shorter than
        // the interval never report at all.)
        var publishInterval = System.Math.Max(1, config.ProgressReportInterval / streams);

        Parallel.For(0, streams, options, streamIndex =>
        {
            var rng = new SeededRandom(DeriveStreamSeed(config.Seed, streamIndex));
            var stats = new StreamingStats(config.HistogramBins, maxWinCapDouble);
            var stack = new Stack<IFlatMapNode>();
            var spinsForStream = baseSpins + (streamIndex < remainder ? 1 : 0);
            long done = 0;

            for (long i = 0; i < spinsForStream; i++)
            {
                if (i > 0 && i % config.CancellationCheckInterval == 0
                    && config.CancellationToken.IsCancellationRequested)
                {
                    Volatile.Write(ref anyCancelled, true);
                    break;
                }

                stats.Add((double)RunOneSpin(program, initialState, rng, stack) / config.WinScale);
                done++;

                if (config.ProgressCallback is not null
                    && done % publishInterval == 0)
                {
                    published[streamIndex] = stats.Snapshot();
                    MaybeReportProgress(
                        config, published, streamCompleted, streamIndex, done,
                        ref reportedThreshold, startedAt);
                }
            }

            streamStats[streamIndex] = stats;
            Volatile.Write(ref streamCompleted[streamIndex], done);
        });

        // Deterministic merge in stream order.
        var total = new StreamingStats(config.HistogramBins, maxWinCapDouble);
        long spinsCompleted = 0;
        for (var s = 0; s < streams; s++)
        {
            total.Merge(streamStats[s]);
            spinsCompleted += streamCompleted[s];
        }

        var cancelled = Volatile.Read(ref anyCancelled);
        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        if (!cancelled && config.ProgressCallback is not null)
        {
            config.ProgressCallback(new SampledProgress
            {
                SpinsCompleted = spinsCompleted,
                TotalSpins = config.MaxSpins,
                Stats = total.Snapshot(),
                Elapsed = elapsed,
            });
        }

        return new SampledResult<S>(total, spinsCompleted, cancelled, config.Seed, elapsed);
    }

    /// <summary>
    /// Report aggregated progress at most once per crossing of the report
    /// interval (across all streams).  Whichever worker crosses the
    /// threshold first wins the CAS and reports a merge of the latest
    /// published per-stream snapshots.
    /// </summary>
    private static void MaybeReportProgress(
        SampledConfig config,
        StreamingStatsSnapshot?[] published,
        long[] streamCompleted,
        int reportingStream,
        long reportingStreamDone,
        ref long reportedThreshold,
        long startedAt)
    {
        // Approximate aggregate spin count from published snapshots plus the
        // reporting stream's live counter.
        long aggregate = reportingStreamDone;
        for (var s = 0; s < published.Length; s++)
        {
            if (s == reportingStream) continue;
            aggregate += published[s]?.Count ?? Volatile.Read(ref streamCompleted[s]);
        }

        var threshold = Volatile.Read(ref reportedThreshold);
        var nextThreshold = threshold + config.ProgressReportInterval;
        if (aggregate < nextThreshold)
            return;
        if (Interlocked.CompareExchange(ref reportedThreshold, aggregate, threshold) != threshold)
            return;

        // Merge published snapshots (cheap: ≤ 32 entries) for the report.
        long n = 0;
        double mean = 0, m2 = 0;
        foreach (var snapshot in published)
        {
            if (snapshot is null || snapshot.Count == 0) continue;
            var totalN = n + snapshot.Count;
            var delta = snapshot.Mean - mean;
            m2 = m2 + snapshot.Variance * (snapshot.Count - 1)
                 + delta * delta * n * snapshot.Count / totalN;
            mean = (n * mean + snapshot.Count * snapshot.Mean) / totalN;
            n = totalN;
        }

        var variance = n > 1 ? m2 / (n - 1) : 0.0;
        var stdErr = n > 0 ? System.Math.Sqrt(variance / n) : 0.0;

        config.ProgressCallback!(new SampledProgress
        {
            SpinsCompleted = n,
            TotalSpins = config.MaxSpins,
            Stats = new StreamingStatsSnapshot(
                Count: n,
                Mean: mean,
                Variance: variance,
                StdDev: System.Math.Sqrt(variance),
                StdErr: stdErr,
                Ci95Half: 1.96 * stdErr,
                MinObserved: double.NaN,
                MaxObserved: double.NaN,
                CapHits: 0,
                NonZeroCount: 0,
                HitFrequency: 0,
                HitFrequencyStdErr: 0,
                MaxWinCap: null,
                Histogram: Array.Empty<HistogramBin>()),
            Elapsed = Stopwatch.GetElapsedTime(startedAt),
        });
    }

    /// <summary>
    /// Derive a per-stream seed from the master seed — SplitMix64-style so
    /// streams are statistically independent yet fully reproducible.
    /// </summary>
    internal static long DeriveStreamSeed(long seed, int stream)
    {
        unchecked
        {
            var z = (ulong)seed + 0x9E3779B97F4A7C15UL * ((ulong)stream + 1);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (long)(z ^ (z >> 31));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Single-spin trampoline
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Run the program once from the initial state, making weighted random
    /// draw choices via <paramref name="rng"/> + <see cref="AliasMethod"/>.
    ///
    /// Uses a trampoline (while loop + explicit continuation stack) so the
    /// call-stack depth is O(1) regardless of program depth or loop iterations.
    /// </summary>
    internal static T RunOneSpin<S, T>(
        Slot<S, T> program,
        S initialState,
        SeededRandom rng)
        where S : notnull
        => RunOneSpin(program, initialState, rng, new Stack<IFlatMapNode>());

    /// <summary>
    /// Hot-path overload reusing a caller-provided continuation stack across
    /// spins.  The pending FlatMap nodes are pushed directly (no closure
    /// allocation per bind), and alias tables come pre-built from the
    /// <see cref="WeightSet.AliasTable"/> cache.
    /// </summary>
    internal static T RunOneSpin<S, T>(
        Slot<S, T> program,
        S initialState,
        SeededRandom rng,
        Stack<IFlatMapNode> stack)
        where S : notnull
    {
        var state = initialState;
        object current = program!;
        stack.Clear();

        while (true)
        {
            // ── FlatMap: push the node itself as the continuation ──────
            if (current is IFlatMapNode fm)
            {
                stack.Push(fm);
                current = fm.SourceUntyped;
                continue;
            }

            // ── Pure: terminal value ───────────────────────────────────
            if (current is IPureNode pure)
            {
                if (stack.Count == 0)
                    return (T)pure.ValueUntyped;

                current = stack.Pop().ApplyUntyped(pure.ValueUntyped);
                continue;
            }

            // ── Draw: weighted random choice via cached alias table ────
            if (current is IDrawNode draw)
            {
                var weightSet = (WeightSet)draw.WeightsUntyped(state!);
                var choice = weightSet.AliasTable.Sample(rng);
                current = draw.NextUntyped(choice);
                continue;
            }

            // ── GetState: read state, feed into continuation ───────────
            if (current is IGetStateNode getState)
            {
                current = getState.NextUntyped(state!);
                continue;
            }

            // ── PutState: write state, continue ────────────────────────
            if (current is IPutStateNode putState)
            {
                state = (S)putState.ValueUntyped;
                current = putState.NextUntyped;
                continue;
            }

            // ── ModifyState: fused state update ────────────────────────
            if (current is IModifyStateNode modify)
            {
                state = (S)modify.ApplyUntyped(state!);
                current = modify.NextUntyped;
                continue;
            }

            // ── Annotation: transparent pass-through ───────────────────
            if (current is IAnnotationNode ann)
            {
                current = ann.InnerUntyped;
                continue;
            }

            // ── Emit: the value-returning path ignores wins (use EvaluateEmit) ──
            if (current is IEmitNode emitNode)
            {
                current = emitNode.NextUntyped;
                continue;
            }

            // ── Loop: desugar to the cap-free self-referential structure ──
            if (current is ILoopNode loopNode)
            {
                current = loopNode.DesugarUntyped;
                continue;
            }

            throw new InvalidOperationException(
                $"Unknown Slot variant: {current.GetType().FullName}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Emit-aware Monte Carlo (D13) — per-spin win = total emitted amount
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Monte-Carlo evaluate an Emit-based program (PRD v3.1, D13): the per-spin
    /// value is the total amount emitted during the spin. Win/loop caps are
    /// enforced per round; loop-cap hits are counted (D6).
    /// </summary>
    public static SampledResult<S> EvaluateEmit<S>(
        Slot<S, Unit> program,
        S initialState,
        SampledConfig config)
        where S : notnull
    {
        var startedAt = Stopwatch.GetTimestamp();
        var rng = new SeededRandom(config.Seed);
        var maxWinCap = config.MaxWinCap;
        double? maxWinCapDouble = maxWinCap.HasValue ? (double)maxWinCap.Value : null;
        var stats = new StreamingStats(config.HistogramBins, maxWinCapDouble);
        var cancelled = false;
        long spin;

        for (spin = 0; spin < config.MaxSpins; spin++)
        {
            if (spin > 0 && spin % config.CancellationCheckInterval == 0
                && config.CancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            var ctx = new EmitSpinCtx<S> { State = initialState, Rng = rng };
            RunEmitProgram(program, ctx);

            // Win cap (D13) is applied once at the sink: StreamingStats clamps
            // values above the cap and counts them as cap-hits.
            stats.Add(ctx.Win.ToDouble() / config.WinScale);
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        return new SampledResult<S>(stats, spin, cancelled, config.Seed, elapsed);
    }

    private sealed class EmitSpinCtx<S>
    {
        public required S State;
        public required SeededRandom Rng;
        public Rational Win = Rational.Zero;
        public bool LoopCapHit;
    }

    /// <summary>
    /// Lean single-spin runner that threads state and accumulates emitted win
    /// via a shared context. Mirrors the trampoline: draw chains iterate, only a
    /// loop body recurses (depth bounded by loop nesting, never iteration count).
    /// </summary>
    private static void RunEmitProgram<S>(object program, EmitSpinCtx<S> ctx)
        where S : notnull
    {
        object current = program;
        var stack = new Stack<IFlatMapNode>();

        while (true)
        {
            if (current is IFlatMapNode fm)
            {
                stack.Push(fm);
                current = fm.SourceUntyped;
                continue;
            }

            if (current is IAnnotationNode ann)
            {
                current = ann.InnerUntyped;
                continue;
            }

            if (current is IPureNode pure)
            {
                if (stack.Count == 0)
                    return;
                current = stack.Pop().ApplyUntyped(pure.ValueUntyped);
                continue;
            }

            if (current is IDrawNode draw)
            {
                var weightSet = (WeightSet)draw.WeightsUntyped(ctx.State!);
                var choice = weightSet.AliasTable.Sample(ctx.Rng);
                current = draw.NextUntyped(choice);
                continue;
            }

            if (current is IGetStateNode getState)
            {
                current = getState.NextUntyped(ctx.State!);
                continue;
            }

            if (current is IPutStateNode putState)
            {
                ctx.State = (S)putState.ValueUntyped;
                current = putState.NextUntyped;
                continue;
            }

            if (current is IModifyStateNode modify)
            {
                ctx.State = (S)modify.ApplyUntyped(ctx.State!);
                current = modify.NextUntyped;
                continue;
            }

            if (current is IEmitNode emit)
            {
                ctx.Win += emit.AmountUntyped(ctx.State!);
                current = emit.NextUntyped;
                continue;
            }

            if (current is ILoopNode loop)
            {
                long iter = 0;
                while (!loop.StopUntyped(ctx.State!) && iter < loop.Cap)
                {
                    RunEmitProgram(loop.BodyUntyped, ctx);
                    iter++;
                }
                if (iter >= loop.Cap && !loop.StopUntyped(ctx.State!))
                    ctx.LoopCapHit = true;

                if (stack.Count == 0)
                    return;
                current = stack.Pop().ApplyUntyped(Unit.Value);
                continue;
            }

            throw new InvalidOperationException(
                $"Unknown Slot variant: {current.GetType().FullName}");
        }
    }
}
