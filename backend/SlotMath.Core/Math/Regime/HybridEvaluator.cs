using System.Numerics;
using SlotMath.Core.Monad;

namespace SlotMath.Core.Math.Regime;

// ═══════════════════════════════════════════════════════════════════════════
//  HybridEvaluator — regime-aware evaluation orchestrator (G8)
//
//  Decides whether to use exact, sampled, or hybrid evaluation based on:
//    - Budget constraints (branch ceiling, time limit)
//    - Plugin presence (forces sampled for those subgraphs)
//    - Program complexity (branch estimates)
//
//  Tries exact first within budget; falls back to sampled when budget is
//  exceeded.  Reports per-subgraph strategy and aggregate provenance.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Configuration for the hybrid (regime-aware) evaluator.
/// </summary>
public sealed class RegimeConfig
{
    /// <summary>Budget controlling exact vs sampled fallback.</summary>
    public Budget Budget { get; init; } = Budget.Default;

    /// <summary>Exact interpreter config (epsilon, etc.).</summary>
    public ExactConfig? ExactOverrides { get; init; }

    /// <summary>Seed for sampled evaluation (used on fallback).</summary>
    public long SampledSeed { get; init; } = 42;

    /// <summary>Number of Monte Carlo spins when falling back to sampled.</summary>
    public long SampledSpins { get; init; } = 100_000;

    /// <summary>Histogram bins for sampled evaluation.</summary>
    public int HistogramBins { get; init; } = 50;

    /// <summary>Max-win cap for sampled evaluation.</summary>
    public BigInteger? MaxWinCap { get; init; }

    /// <summary>Cancellation token.</summary>
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;

    /// <summary>How often to report progress, in number of spins (default 1000).</summary>
    public int ProgressReportInterval { get; init; } = 1000;

    /// <summary>
    /// Optional callback invoked every <see cref="ProgressReportInterval"/> spins
    /// with a snapshot of current statistics.
    /// </summary>
    public Action<SampledProgress>? ProgressCallback { get; init; }

    /// <summary>
    /// When true, always uses sampled (skip exact attempt).
    /// Useful for testing the fallback path.
    /// </summary>
    public bool ForceSampled { get; init; }

    /// <summary>
    /// Worker threads for sampled evaluation (see
    /// <see cref="SampledConfig.DegreeOfParallelism"/>).  Results stay
    /// deterministic per seed regardless of this value.
    /// </summary>
    public int DegreeOfParallelism { get; init; } = 1;

    /// <summary>
    /// Sub-credit scale of the program's win amounts (from
    /// <see cref="SlotMath.Core.Compiler.CompileResult.WinScale"/>).  Reports
    /// are converted back to credit units — exactly on the exact path.
    /// </summary>
    public BigInteger WinScale { get; init; } = BigInteger.One;
}

/// <summary>
/// The hybrid evaluator — the top-level entry point for G8 regime-aware
/// evaluation.  Combines exact and sampled interpreters, budget enforcement,
/// plugin detection, and strategy reporting.
/// </summary>
public static class HybridEvaluator
{
    /// <summary>
    /// Evaluate a program with regime detection, budget control, and
    /// automatic exact → sampled fallback.
    /// </summary>
    /// <param name="program">The slot program to evaluate.</param>
    /// <param name="initialState">Initial game state.</param>
    /// <param name="recurrenceHasher">Hash function for memoization.</param>
    /// <param name="config">Regime configuration.</param>
    /// <returns>A <see cref="RegimeResult"/> with metrics and strategy metadata.</returns>
    public static RegimeResult Evaluate<S>(
        Slot<S, BigInteger> program,
        S initialState,
        Func<S, BigInteger> recurrenceHasher,
        RegimeConfig? config = null)
        where S : notnull
    {
        config ??= new RegimeConfig();

        // ── Step 1: Analyze the program ─────────────────────────────────
        var analysis = ProgramAnalyzer.Analyze(program);

        // ── Step 2: Determine strategy ──────────────────────────────────
        var strategies = new List<SubgraphStrategy>();

        // Build subgraph strategy records from analysis.
        foreach (var sg in analysis.Subgraphs)
        {
            var strategy = sg.ContainsPlugin
                ? EvaluationStrategy.Sampled
                : analysis.EstimatedBranches <= (config.Budget.MaxBranches ?? long.MaxValue)
                    ? EvaluationStrategy.Exact
                    : EvaluationStrategy.Sampled;

            strategies.Add(new SubgraphStrategy
            {
                SubgraphId = sg.SubgraphId,
                Strategy = strategy,
                Reason = sg.ContainsPlugin
                    ? "plugin_present"
                    : analysis.EstimatedBranches <= (config.Budget.MaxBranches ?? long.MaxValue)
                        ? "within_budget"
                        : "branch_estimate_exceeded",
                EstimatedBranches = sg.EstimatedBranches,
                ContainsPlugin = sg.ContainsPlugin,
                Provenance = strategy == EvaluationStrategy.Sampled
                    ? Provenance.Sampled
                    : Provenance.Exact
            });
        }

        // Always add a root subgraph.
        if (!analysis.Subgraphs.Any(s => s.SubgraphId == "root"))
        {
            var rootStrategy = analysis.ContainsPlugin || config.ForceSampled
                ? EvaluationStrategy.Sampled
                : analysis.EstimatedBranches <= (config.Budget.MaxBranches ?? long.MaxValue)
                    ? EvaluationStrategy.Exact
                    : EvaluationStrategy.Sampled;

            strategies.Insert(0, new SubgraphStrategy
            {
                SubgraphId = "root",
                Strategy = rootStrategy,
                Reason = analysis.ContainsPlugin
                    ? "plugin_present"
                    : config.ForceSampled
                        ? "force_sampled"
                        : analysis.EstimatedBranches <= (config.Budget.MaxBranches ?? long.MaxValue)
                            ? "within_budget"
                            : "branch_estimate_exceeded",
                EstimatedBranches = analysis.EstimatedBranches,
                ContainsPlugin = analysis.ContainsPlugin
            });
        }

        // ── Step 3: Evaluate ────────────────────────────────────────────
        var overallStrategy = strategies.Any(s => s.Strategy == EvaluationStrategy.Sampled)
            ? EvaluationStrategy.Sampled
            : EvaluationStrategy.Exact;

        // If any subgraph is sampled, the overall is at best Sampled.
        // True Hybrid (exact for some, sampled for others) requires the compiler;
        // for now, any plugin forces sampled for the entire program.

        if (config.ForceSampled)
            overallStrategy = EvaluationStrategy.Sampled;

        var budgetExceeded = false;
        SlotMathReport? report;
        Provenance aggregateProvenance;

        if (overallStrategy == EvaluationStrategy.Exact && !analysis.ContainsPlugin && !config.ForceSampled)
        {
            // ── Try exact evaluation within budget ─────────────────────
            try
            {
                var exactConfig = config.ExactOverrides ?? new ExactConfig();
                exactConfig = new ExactConfig
                {
                    EpsilonNumerator = exactConfig.EpsilonNumerator,
                    EpsilonDenominator = exactConfig.EpsilonDenominator,
                    Budget = config.Budget
                };

                var exactResult = ExactInterpreter.Evaluate(
                    program, initialState, recurrenceHasher, exactConfig);

                report = ExactMetrics.Compute(
                    exactResult.ValueDistribution(),
                    config.MaxWinCap,
                    numHistogramBins: config.HistogramBins,
                    winScale: config.WinScale);

                aggregateProvenance = report.AggregateProvenance;

                // Update strategy records with actual provenance.
                for (var i = 0; i < strategies.Count; i++)
                {
                    strategies[i] = strategies[i] with
                    {
                        Strategy = EvaluationStrategy.Exact,
                        Reason = strategies[i].ContainsPlugin
                            ? "plugin_present"
                            : "within_budget",
                        Provenance = aggregateProvenance
                    };
                }
                overallStrategy = EvaluationStrategy.Exact;
            }
            catch (BudgetExceededException)
            {
                budgetExceeded = true;
                overallStrategy = EvaluationStrategy.Sampled;

                // Mark all strategies as sampled due to fallback.
                for (var i = 0; i < strategies.Count; i++)
                {
                    strategies[i] = strategies[i] with
                    {
                        Strategy = EvaluationStrategy.Sampled,
                        Reason = strategies[i].ContainsPlugin
                            ? "plugin_present"
                            : "branch_estimate_exceeded",
                        Provenance = Provenance.Sampled
                    };
                }

                // Fall through to sampled.
                report = RunSampled(program, initialState, config);
                aggregateProvenance = Provenance.Sampled;
            }
        }
        else
        {
            // ── Sampled (forced or plugin present) ─────────────────────
            report = RunSampled(program, initialState, config);
            aggregateProvenance = Provenance.Sampled;

            for (var i = 0; i < strategies.Count; i++)
            {
                strategies[i] = strategies[i] with { Provenance = Provenance.Sampled };
            }
        }

        return new RegimeResult
        {
            Report = report!,
            SubgraphStrategies = strategies,
            AggregateProvenance = aggregateProvenance,
            OverallStrategy = overallStrategy,
            BudgetExceeded = budgetExceeded
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static SlotMathReport RunSampled<S>(
        Slot<S, BigInteger> program, S initialState, RegimeConfig config)
        where S : notnull
    {
        var sampledConfig = new SampledConfig
        {
            Seed = config.SampledSeed,
            MaxSpins = config.SampledSpins,
            MaxWinCap = config.MaxWinCap,
            HistogramBins = config.HistogramBins,
            CancellationToken = config.CancellationToken,
            CancellationCheckInterval = config.ProgressCallback is not null ? 1 : 1000,
            ProgressReportInterval = config.ProgressReportInterval,
            ProgressCallback = config.ProgressCallback,
            DegreeOfParallelism = config.DegreeOfParallelism,
            // Sampled stats divide the scale out per spin, so the cap and
            // all reported values stay in credit units.
            WinScale = (double)config.WinScale
        };

        var sampledResult = SampledInterpreter.Evaluate(
            program, initialState, sampledConfig);

        return SampledMetrics.ComputeFromResult(sampledResult);
    }
}
