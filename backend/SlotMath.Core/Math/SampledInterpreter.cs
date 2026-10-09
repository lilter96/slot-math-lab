using System.Diagnostics;
using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
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
///     Configuration for a sampled (Monte Carlo) evaluation run.
/// </summary>
public sealed class SampledConfig
{
    public ExecutionOptions? Execution { get; init; }
    /// <summary>Diagnostic replay of a single absolute round. Reconstructs its logical stream prefix; final aggregate is not a full run.</summary>
    public long? ReplayRoundIndex { get; init; }
    /// <summary>Fixed logical stream size. Record this with seed for reproduction; it never depends on worker count.</summary>
    public IReadOnlyList<MeasurementDefinition> Measurements { get; init; } = [];

    public int ChunkSize { get; init; } = SlotMathConstants.Prng.Chunk;

    /// <summary>Master seed for the PRNG. Ensures deterministic reproducibility.</summary>
    public long Seed { get; init; }

    /// <summary>Maximum number of spins to run.</summary>
    public long MaxSpins { get; init; } = 1_000_000;

    /// <summary>
    ///     Optional cap on per-spin win values.  Values above the cap are clamped
    ///     to the cap value and counted as cap-hits.  When null, no capping is applied.
    /// </summary>
    public BigInteger? MaxWinCap { get; init; }

    /// <summary>Number of equal-width histogram bins (default 50).</summary>
    public int HistogramBins { get; init; } = 50;

    /// <summary>Cancellation token for early termination.</summary>
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;

    /// <summary>
    ///     How often to check the cancellation token, in number of spins.
    ///     Default 1000 balances responsiveness with overhead.
    ///     Set to 1 for API runs requiring sub-500ms cancellation.
    /// </summary>
    public int CancellationCheckInterval { get; init; } = 1000;

    /// <summary>How often to report progress, in number of spins (default 1000).</summary>
    public int ProgressReportInterval { get; init; } = 1000;

    /// <summary>
    ///     Optional callback invoked every <see cref="ProgressReportInterval" /> spins
    ///     with a snapshot of current statistics.  Invoked synchronously on the
    ///     worker thread — callers should keep it fast and not block.
    /// </summary>
    public Action<SampledProgress>? ProgressCallback { get; init; }

    /// <summary>
    ///     Maximum number of worker threads.  1 (default) runs the classic
    ///     sequential interpreter.  Above 1, spins are partitioned over a FIXED
    ///     number of logical streams (independent of thread count), each with a
    ///     seed derived deterministically from <see cref="Seed" />, and merged in
    ///     stream order — so the final statistics are a pure function of
    ///     (Seed, MaxSpins) on any machine, while wall-clock time scales with
    ///     available cores.
    /// </summary>
    public int DegreeOfParallelism { get; init; } = 1;

    /// <summary>
    ///     Sub-credit scale of the program's win amounts.  The compiler emits
    ///     wins multiplied by this factor when fractional paytable payouts are
    ///     present; sampled statistics divide it back out per spin so all stats
    ///     are in credit units.
    /// </summary>
    public double WinScale { get; init; } = 1.0;
}

/// <summary>
///     The result of a sampled Monte Carlo evaluation.
/// </summary>
public sealed class SampledResult<S>
{
    public ExecutionSummary? Execution { get; internal init; }
    public IReadOnlyList<MeasurementSnapshot>? ReplayedRound { get; internal init; }
    /// <summary>Streaming statistics accumulated across all completed spins.</summary>
    public IReadOnlyList<MeasurementSnapshot> Measurements { get; internal init; } = [];

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
    /// <summary>Run the same compiled program used by Monte Carlo, returning its final state for a playable UI/replay.</summary>
    public static (T Value, S State) RunSingle<S, T>(Slot<S, T> program, S initialState, long seed, long roundIndex = 0,
        CancellationToken cancellationToken = default)
        where S : notnull
    {
        if (program is ICompiledSampling<S, T> compiled)
        {
            ISamplingRunner<S, T> runner = compiled.CreateRunner(initialState);
            T value = runner.Run(SeededRandom.ForStream(unchecked((ulong)seed), roundIndex), cancellationToken);
            return (value, runner.ExportState());
        }

        Slot<S, (T value, S state)> traced =
            program.SelectMany(value => Slot.GetState<S>().Select(state => (value, state)));
        return RunOneSpin(traced, initialState, SeededRandom.ForStream(unchecked((ulong)seed), roundIndex),
            new Stack<IFlatMapNode>(), cancellationToken);
    }

    /// <summary>
    ///     Evaluate a program via Monte Carlo sampling.
    ///     Runs <paramref name="config" />.MaxSpins independent spins, each from the
    ///     initial state.  The program's result type must be <see cref="BigInteger" />
    ///     (the per-spin win amount) or convertible via a selector.
    /// </summary>
    public static SampledResult<S> Evaluate<S>(
        Slot<S, BigInteger> program,
        S initialState,
        SampledConfig config)
        where S : notnull
        => EvaluateChunked(program, initialState, config,
            (stats, win) => stats.Add((double)win / config.WinScale), win => (double)win / config.WinScale);

    /// <summary>
    ///     Evaluate with a selector function that extracts a BigInteger value from T.
    /// </summary>
    public static SampledResult<S> Evaluate<S, T>(
        Slot<S, T> program,
        S initialState,
        Func<T, BigInteger> selector,
        SampledConfig config)
        where S : notnull
        => EvaluateChunked(program, initialState, config,
            (stats, value) => stats.Add(selector(value)), value => (double)selector(value));

    // ═══════════════════════════════════════════════════════════════════════
    //  Chunk-based evaluation (D3)
    //
    //  Work is split into fixed chunks of CHUNK = 65,536 rounds.  Chunk c
    //  covers spins [c·CHUNK, min((c+1)·CHUNK, MaxSpins)) and runs on an
    //  independent stream seeded SplitMix64(masterSeed, c).  Chunk results are
    //  merged in STRICT ASCENDING chunk index order, so the final statistics
    //  are a pure function of (Seed, MaxSpins) — bit-identical at any
    //  DegreeOfParallelism (including 1) and any CPU core topology (D3, D24).
    // ═══════════════════════════════════════════════════════════════════════

    private static SampledResult<S> EvaluateChunked<S, T>(
        Slot<S, T> program,
        S initialState,
        SampledConfig config,
        Action<StreamingStats, T> add, Func<T, double> payoutSelector)
        where S : notnull
    {
        var startedAt = Stopwatch.GetTimestamp();
        double? maxWinCapDouble = config.MaxWinCap.HasValue ? (double)config.MaxWinCap.Value : null;
        var totalSpins = config.MaxSpins;
        var execution = config.Execution ?? new();
        if (config.ReplayRoundIndex is { } replayIndex && (replayIndex < 0 || replayIndex >= totalSpins)) throw new ArgumentOutOfRangeException(nameof(config.ReplayRoundIndex));
        IReadOnlyList<MeasurementSnapshot>? replayedRound = null;
        execution.Validate(totalSpins, config.DegreeOfParallelism);
        var featureIndex = execution.FeatureMetricId is null ? -1 : config.Measurements.ToList().FindIndex(d => d.Id == execution.FeatureMetricId);
        if (execution.FeatureMetricId is not null && (featureIndex < 0 || config.Measurements[featureIndex] is not { Options: { Source: "event" or "count" } } featureDefinition
            || !(featureDefinition.Options.Subject == "round" && featureDefinition.Options.Reduction == "any" || featureDefinition.NodeId is null && featureDefinition.Options.Subject == "observation" && featureDefinition.Options.Source == "event")))
            throw new ArgumentException("Feature waiting requires a pinned round activation: a completed-round Boolean event, or an any-event round reduction.");
        if (execution.PersistentKeys.Length > 0 && initialState is not Dictionary<string, object?>)
            throw new ArgumentException("Persistent state keys require a dictionary graph state.");
        if (execution.PersistentKeys.Length > 0 && config.Measurements.Any(d => d.Options is { IndependentSubjects: true } or { IndependentParents: true }))
            throw new ArgumentException("Retained state creates dependent rounds. Remove independent-round assumptions from the measurement plan.");

        var chunkSize = config.ChunkSize;
        if (chunkSize is < 1 or > SlotMathConstants.Prng.Chunk)
        {
            throw new ArgumentOutOfRangeException(nameof(config.ChunkSize));
        }

        long partitionSize = execution.Regime == "persistent" ? System.Math.Max(1, totalSpins) : execution.Regime == "sessions"
            ? System.Math.Max(1, chunkSize / execution.SessionLength) * execution.SessionLength : chunkSize;
        var nChunks = totalSpins <= 0 ? 0 : checked((int)((totalSpins + partitionSize - 1) / partitionSize));

        if (nChunks == 0)
        {
            var empty = new StreamingStats(config.HistogramBins, maxWinCapDouble);
            return new SampledResult<S>(
                empty, 0, config.CancellationToken.IsCancellationRequested,
                config.Seed, Stopwatch.GetElapsedTime(startedAt))
            {
                Measurements = MeasurementCollector.Snapshot(config.Measurements,
                    new MeasurementAccumulator[config.Measurements.Count])
            };
        }

        var chunkMeasurements = new MeasurementAccumulator[]?[nChunks];
        var mergedMeasurements = new MeasurementAccumulator[config.Measurements.Count];
        var chunkStats = new StreamingStats?[nChunks];
        var chunkDone = new long[nChunks];
        var chunkAttempts = new long[nChunks];
        var chunkCancelled = new long[nChunks];
        var chunkSessions = new long[nChunks];
        var chunkInterruptedSessions = new long[nChunks];
        var chunkSessionEvidence = new SessionEvidence?[nChunks];
        var chunkLoopEvidence = new LoopTerminationEvidence?[nChunks];
        var cancelFlag = 0;

        // Display snapshots merge small deltas while logical PRNG chunks stay fixed.
        // Serialize callbacks as well as merges: concurrent workers must never
        // publish decreasing sample counts. Final stats still merge full chunks
        // in ascending index order, independent of reporting cadence and DoP.
        var progressLock = new object();
        var progressMerged = new StreamingStats(config.HistogramBins, maxWinCapDouble);
        var progressSessionEvidence = new SessionEvidence();
        var progressLoopEvidence = new LoopTerminationEvidence();
        long progressAttempts = 0, progressCancelled = 0, progressFailed = 0, progressSessions = 0, progressInterruptedSessions = 0;
        ExecutionSummary Summary(long attempts, long completed, long cancelledRounds, long failed, long sessions, long interruptedSessions, SessionEvidence evidence, LoopTerminationEvidence loops) =>
            new(execution.Regime, attempts, completed, cancelledRounds + failed, cancelledRounds, failed, sessions, interruptedSessions, execution.PersistentKeys.Length > 0,
                execution.PersistentKeys.Length == 0 ? "Reset all state before each paid round" : $"Retain only declared keys between rounds; reset at {(execution.Regime == "sessions" ? "each session" : "trajectory start")}",
                "Fixed horizon; ruin is first inability to fund the next wager. Play continues with hypothetical credit; no survivor-only RTP denominator. Session money uses exact shortest round-trip decimals without currency rounding; report values are binary64.", evidence.Snapshot()) { MonetaryAccounting = execution.Regime == "sessions" ? SessionMoney.Contract : null, SamplingEngine = execution.SamplingEngine != "reference" && program is ICompiledSampling<S, T> ? "compiled-sampling-plan" : "reference-interpreter", LoopTerminations = loops.Snapshot(), LoopTerminationsComplete = loops.Complete };

        void RunChunk(int c)
        {
            if (Volatile.Read(ref cancelFlag) != 0 || config.CancellationToken.IsCancellationRequested)
            {
                Interlocked.Exchange(ref cancelFlag, 1);
                return;
            }

            var start = (long)c * partitionSize;
            var end = System.Math.Min(start + partitionSize, totalSpins);
            if (config.ReplayRoundIndex is { } requested && (requested < start || requested >= end)) return;
            if (config.ReplayRoundIndex is { } lastRound) end = lastRound + 1;
            var rng = new SeededRandom(DeriveStreamSeed(config.Seed, c));
            var stats = new StreamingStats(config.HistogramBins, maxWinCapDouble);
            StreamingStats? delta = config.ProgressCallback is null
                ? null
                : new StreamingStats(config.HistogramBins, maxWinCapDouble);
            var lastReport = Stopwatch.GetTimestamp();
            var stack = new Stack<IFlatMapNode>();
            MeasurementCollector? measurements =
                config.Measurements.Count == 0 ? null : new MeasurementCollector(config.Measurements);
            var loopEvidence = new LoopTerminationEvidence();
            ISamplingRunner<S, T>? runner =
                execution.SamplingEngine == "reference" ? null : (program as ICompiledSampling<S, T>)?.CreateRunner(initialState, measurements, execution.PersistentKeys, loopEvidence);
            var sessionEvidence = new SessionEvidence();
            var sessionDelta = new SessionEvidence();
            SessionTrajectory? session = null;
            long attempts = 0, cancelledRounds = 0, failedRounds = 0, sessions = 0, interruptedSessions = 0;
            long lastAttempts = 0, lastCancelled = 0, lastFailed = 0, lastSessions = 0, lastInterruptedSessions = 0;
            S roundInitial = initialState;

            Func<EvalContext, ExprValue>? BindMeasurement(Expression? expression)
            {
                return expression is null ? null : context => ExactExpressionEvaluator.Evaluate(expression, context);
            }

            MeasurementBinding<EvalContext>[] evaluators = config.Measurements.Select(d =>
                new MeasurementBinding<EvalContext>(BindMeasurement(d.Value), BindMeasurement(d.Filter),
                    BindMeasurement(d.Options?.Group), BindMeasurement(d.Options?.Pair),
                    BindMeasurement(d.Options?.Weight), BindMeasurement(d.Options?.AwardId),
                    BindMeasurement(d.Options?.EntryFilter), BindMeasurement(d.Options?.ExitFilter))).ToArray();
            Dictionary<string, int[]> nodeMeasurements = config.Measurements.SelectMany((d, i) =>
                    new[] { d.NodeId, d.Options?.EntryNodeId, d.Options?.ExitNodeId }.OfType<string>().Distinct()
                        .Select(node => (node, i)))
                .GroupBy(p => p.node).ToDictionary(g => g.Key, g => g.Select(p => p.i).ToArray());

            void ObserveNode(string nodeId, S state)
            {
                if (measurements is null || !nodeMeasurements.TryGetValue(nodeId, out var indexes))
                {
                    return;
                }

                var context = new EvalContext { State = state };
                foreach (var index in indexes)
                {
                    measurements.Point(index, nodeId, context, evaluators[index]);
                }
            }

            void ObserveLoop(ILoopCompletionNode node, S state)
            {
                if (state is not Dictionary<string, object?> fields || !fields.TryGetValue(node.IterationKey, out var raw) || raw is not int iterations)
                    throw new InvalidOperationException("Compiled loop counter is missing.");
                loopEvidence.Observe(node.NodeId, iterations, node.MaximumIterations);
            }
            S finalState = initialState;
            Action<S>? exportState = measurements is null && execution.PersistentKeys.Length == 0 ? null : state => finalState = state;
            Action<string, S>? observer = measurements is null ? null : ObserveNode;
            double? rawPayout = null;
            Action<object> rawObserver = value => rawPayout = (double)(BigInteger)value / config.WinScale;
            long done = 0;

            void Flush()
            {
                if (delta is null || delta.Count == 0 && attempts == lastAttempts && interruptedSessions == lastInterruptedSessions)
                {
                    return;
                }

                lock (progressLock)
                {
                    progressMerged.Merge(delta);
                    progressAttempts += attempts - lastAttempts; progressCancelled += cancelledRounds - lastCancelled; progressFailed += failedRounds - lastFailed;
                    progressSessions += sessions - lastSessions; progressInterruptedSessions += interruptedSessions - lastInterruptedSessions;
                    progressSessionEvidence.Merge(sessionDelta);
                    progressLoopEvidence.MergeDelta(loopEvidence);
                    if (measurements is not null)
                    {
                        MeasurementCollector.Merge(mergedMeasurements, measurements.Delta);
                    }

                    config.ProgressCallback!(new SampledProgress
                    {
                        SpinsCompleted = progressMerged.Count,
                        Execution = Summary(progressAttempts, progressMerged.Count, progressCancelled, progressFailed, progressSessions, progressInterruptedSessions, progressSessionEvidence, progressLoopEvidence),
                        TotalSpins = totalSpins,
                        Stats = progressMerged.Snapshot(),
                        Measurements =
                            MeasurementCollector.Snapshot(config.Measurements, mergedMeasurements, false),
                        Elapsed = Stopwatch.GetElapsedTime(startedAt)
                    });
                }

                if (measurements is not null)
                {
                    Array.Clear(measurements.Delta);
                }

                loopEvidence.ClearDelta();
                delta = new StreamingStats(config.HistogramBins, maxWinCapDouble);
                sessionDelta = new(); lastAttempts = attempts; lastCancelled = cancelledRounds; lastFailed = failedRounds; lastSessions = sessions; lastInterruptedSessions = interruptedSessions;
                lastReport = Stopwatch.GetTimestamp();
            }

            for (var i = start; i < end; i++)
            {
                if (done % config.CancellationCheckInterval == 0 && config.CancellationToken.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref cancelFlag, 1);
                    break;
                }

                try
                {
                    if (execution.Regime == "sessions" && i % execution.SessionLength == 0)
                    {
                        rng = new SeededRandom(DeriveStreamSeed(config.Seed, checked((int)(i / execution.SessionLength))));
                        runner?.ResetTrajectory(); roundInitial = initialState;
                        session = new(execution.InitialBankroll, execution.Wager);
                    }
                    attempts++;
                    loopEvidence.Begin();
                    measurements?.Begin(i, config.ReplayRoundIndex == i);
                    rawPayout = null;
                    T value = runner is null
                        ? RunOneSpin(program, roundInitial, rng, stack, config.CancellationToken,
                            observer, exportState, rawObserver, ObserveLoop)
                        : runner.Run(rng, config.CancellationToken);
                    if (measurements is not null)
                    {
                        // Settled payout uses the same scale and cap as global stats.
                        var payout = payoutSelector(value);
                        if (maxWinCapDouble is { } cap)
                        {
                            payout = System.Math.Min(payout, cap);
                        }

                        if (runner is not null)
                        {
                            runner.ObserveRound(payout);
                        }
                        else
                        {
                            for (var m = 0; m < config.Measurements.Count; m++)
                            {
                                var context = new EvalContext { State = finalState, Measurement = new(payout, rawPayout, config.Measurements[m].Options?.Stake ?? 1) };
                                measurements.CompleteRound(m, context, evaluators[m], payout, rawPayout);
                            }
                        }

                        measurements.Prepare();
                    }

                    if (execution.PersistentKeys.Length > 0 && runner is null)
                    {
                        var carried = new Dictionary<string, object?>((Dictionary<string, object?>)(object)initialState);
                        var final = (Dictionary<string, object?>)(object)finalState;
                        foreach (var key in execution.PersistentKeys) if (final.TryGetValue(key, out var stateValue)) carried[key] = stateValue;
                        roundInitial = (S)(object)carried;
                    }
                    if (session is not null)
                    {
                        var activation = featureIndex < 0 ? false : measurements?.CompletedRoundValue(featureIndex) is { } observed ? observed != 0 : (bool?)null;
                        session.Add(System.Math.Min(payoutSelector(value), maxWinCapDouble ?? double.PositiveInfinity), activation);
                        if ((i + 1) % execution.SessionLength == 0) { session.Commit(sessionEvidence, featureIndex >= 0); session.Commit(sessionDelta, featureIndex >= 0); sessions++; session = null; }
                    }
                    if (config.ReplayRoundIndex == i) replayedRound = measurements?.RoundSnapshot() ?? [];
                    measurements?.Commit();
                    loopEvidence.Commit();
                    add(stats, value);
                    if (delta is not null)
                    {
                        add(delta, value);
                    }

                }
                catch (OperationCanceledException) when (config.CancellationToken.IsCancellationRequested)
                {
                    cancelledRounds++;
                    Interlocked.Exchange(ref cancelFlag, 1);
                    break;
                }
                catch
                {
                    failedRounds++; if (session is not null) interruptedSessions++;
                    Flush(); throw;
                }

                done++;
                if (delta is not null && (done % System.Math.Max(1, config.ProgressReportInterval) == 0
                                          || (done % 16 == 0 &&
                                              Stopwatch.GetElapsedTime(lastReport).TotalMilliseconds >= 250)))
                {
                    Flush();
                }
            }

            if (session is not null) interruptedSessions++;
            Flush();
            chunkMeasurements[c] = measurements?.Total;
            chunkStats[c] = stats;
            Volatile.Write(ref chunkDone[c], done);
            chunkAttempts[c] = attempts; chunkCancelled[c] = cancelledRounds; chunkSessions[c] = sessions; chunkInterruptedSessions[c] = interruptedSessions; chunkSessionEvidence[c] = sessionEvidence; chunkLoopEvidence[c] = loopEvidence;
        }

        if (config.DegreeOfParallelism > 1 && nChunks > 1)
        {
            Parallel.For(0, nChunks,
                new ParallelOptions { MaxDegreeOfParallelism = config.DegreeOfParallelism },
                RunChunk);
        }
        else
        {
            for (var c = 0; c < nChunks; c++)
            {
                RunChunk(c);
                if (Volatile.Read(ref cancelFlag) != 0)
                {
                    break;
                }
            }
        }

        // D3: strict ascending chunk-index-order merge ⇒ bit-identical result.
        var total = new StreamingStats(config.HistogramBins, maxWinCapDouble);
        var finalMeasurements = new MeasurementAccumulator[config.Measurements.Count];
        var prefixClosed = false;
        var orderedPrefix = true;
        long spinsCompleted = 0;
        var finalSessionEvidence = new SessionEvidence();
        var finalLoopEvidence = new LoopTerminationEvidence();
        for (var c = 0; c < nChunks; c++)
        {
            var completed = Volatile.Read(ref chunkDone[c]);
            if (prefixClosed && completed > 0)
            {
                orderedPrefix = false;
            }

            if (completed < System.Math.Min(partitionSize, totalSpins - (long)c * partitionSize))
            {
                prefixClosed = true;
            }

            if (chunkStats[c] is null)
            {
                continue;
            }

            total.Merge(chunkStats[c]!);
            if (chunkMeasurements[c] is { } measured)
            {
                MeasurementCollector.Merge(finalMeasurements, measured);
            }

            spinsCompleted += Volatile.Read(ref chunkDone[c]);
            if (chunkLoopEvidence[c] is { } loops) finalLoopEvidence.Merge(loops);
            if (chunkSessionEvidence[c] is { } evidence) finalSessionEvidence.Merge(evidence);
        }

        var cancelled = Volatile.Read(ref cancelFlag) != 0;
        TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
        var executionSummary = Summary(chunkAttempts.Sum(), spinsCompleted, chunkCancelled.Sum(), 0, chunkSessions.Sum(),
            chunkInterruptedSessions.Sum(), finalSessionEvidence, finalLoopEvidence);

        if (!cancelled && config.ProgressCallback is not null)
        {
            config.ProgressCallback(new SampledProgress
            {
                SpinsCompleted = spinsCompleted,
                Execution = executionSummary,
                TotalSpins = totalSpins,
                Stats = total.Snapshot(),
                Measurements = MeasurementCollector.Snapshot(config.Measurements, finalMeasurements, orderedPrefix),
                Elapsed = elapsed
            });
        }

        return new SampledResult<S>(total, spinsCompleted, cancelled, config.Seed, elapsed)
        {
            Execution = executionSummary,
            ReplayedRound = replayedRound,
            Measurements = MeasurementCollector.Snapshot(config.Measurements, finalMeasurements, orderedPrefix)
        };
    }

    /// <summary>
    ///     Derive a per-chunk seed from the master seed — SplitMix64-style so
    ///     streams are statistically independent yet fully reproducible.
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
    ///     Run the program once from the initial state, making weighted random
    ///     draw choices via <paramref name="rng" /> + <see cref="AliasMethod" />.
    ///     Uses a trampoline (while loop + explicit continuation stack) so the
    ///     call-stack depth is O(1) regardless of program depth or loop iterations.
    /// </summary>
    internal static T RunOneSpin<S, T>(
        Slot<S, T> program,
        S initialState,
        SeededRandom rng)
        where S : notnull
        => RunOneSpin(program, initialState, rng, new Stack<IFlatMapNode>());

    /// <summary>
    ///     Hot-path overload reusing a caller-provided continuation stack across
    ///     spins.  The pending FlatMap nodes are pushed directly (no closure
    ///     allocation per bind), and alias tables come pre-built from the
    ///     <see cref="WeightSet.AliasTable" /> cache.
    /// </summary>
    internal static T RunOneSpin<S, T>(
        Slot<S, T> program,
        S initialState,
        SeededRandom rng,
        Stack<IFlatMapNode> stack, CancellationToken cancellationToken = default, Action<string, S>? observe = null,
        Action<S>? completed = null, Action<object>? rawSettlement = null, Action<ILoopCompletionNode, S>? loopCompleted = null)
        where S : notnull
    {
        S state = initialState;
        object current = program!;
        stack.Clear();
        var operations = 0;

        while (true)
        {
            if (cancellationToken.CanBeCanceled && (operations++ & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

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
                {
                    completed?.Invoke(state);
                    return (T)pure.ValueUntyped;
                }

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

            if (current is ISettlementNode settlement)
            {
                stack.Push(new SettlementContinuation(settlement, rawSettlement));
                current = settlement.RawUntyped;
                continue;
            }

            // ── Annotation: transparent pass-through ───────────────────
            if (current is IAnnotationNode ann)
            {
                if (current is IObservationNode point)
                {
                    observe?.Invoke(point.NodeId, state);
                }

                if (current is ILoopCompletionNode loopCompletion) loopCompleted?.Invoke(loopCompletion, state);
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
    ///     Monte-Carlo evaluate an Emit-based program (PRD v3.1, D13): the per-spin
    ///     value is the total amount emitted during the spin. Win/loop caps are
    ///     enforced per round; loop-cap hits are counted (D6).
    /// </summary>
    public static SampledResult<S> EvaluateEmit<S>(
        Slot<S, Unit> program,
        S initialState,
        SampledConfig config)
        where S : notnull
    {
        if (config.Measurements.Count > 0 || config.Execution is { Regime: not "independentRounds" } or { PersistentKeys.Length: > 0 })
            throw new ArgumentException("Emit sampling does not support observation plans or retained/session state. Compile a canonical value-returning graph for those execution contracts.");
        var startedAt = Stopwatch.GetTimestamp();
        var rng = new SeededRandom(config.Seed);
        BigInteger? maxWinCap = config.MaxWinCap;
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

            var ctx = new EmitSpinCtx<S> { State = initialState, Rng = rng, CancellationToken = config.CancellationToken };
            RunEmitProgram(program, ctx);

            // Win cap (D13) is applied once at the sink: StreamingStats clamps
            // values above the cap and counts them as cap-hits.
            stats.Add(ctx.Win.ToDouble() / config.WinScale);
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
        return new SampledResult<S>(stats, spin, cancelled, config.Seed, elapsed);
    }

    private sealed class EmitSpinCtx<S>
    {
        public required S State;
        public required SeededRandom Rng;
        public Rational Win = Rational.Zero;
        public bool LoopCapHit;
        public CancellationToken CancellationToken;
    }

    /// <summary>
    ///     Lean single-spin runner that threads state and accumulates emitted win
    ///     via a shared context. Mirrors the trampoline: draw chains iterate, only a
    ///     loop body recurses (depth bounded by loop nesting, never iteration count).
    /// </summary>
    private static void RunEmitProgram<S>(object program, EmitSpinCtx<S> ctx)
        where S : notnull
    {
        var current = program;
        var stack = new Stack<IFlatMapNode>();

        while (true)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
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
                {
                    return;
                }

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

            if (current is ITruncateNode)
            {
                ctx.LoopCapHit = true;
                return;
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
                {
                    ctx.LoopCapHit = true;
                }

                if (stack.Count == 0)
                {
                    return;
                }

                current = stack.Pop().ApplyUntyped(Unit.Value);
                continue;
            }

            throw new InvalidOperationException(
                $"Unknown Slot variant: {current.GetType().FullName}");
        }
    }
}
