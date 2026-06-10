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
            stats.Add(win);

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

            // ── Annotation: transparent pass-through ───────────────────
            if (current is IAnnotationNode ann)
            {
                current = ann.InnerUntyped;
                continue;
            }

            throw new InvalidOperationException(
                $"Unknown Slot variant: {current.GetType().FullName}");
        }
    }
}
