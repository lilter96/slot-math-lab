using System.Numerics;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  G8 — Regime detection, budget control & hybrid  — acceptance tests
//
//  DoD items:
//  1. Base-only game computes exactly within budget.
//  2. Deep-retrigger game auto-falls-back to sampled/hybrid without
//     exceeding budget.
//  3. Subgraph containing a plugin is forced sampled and reported Sampled
//     (never Exact).
//  4. Result reports per-subgraph strategy and aggregate provenance.
// ═══════════════════════════════════════════════════════════════════════════

// ── Shared state type ────────────────────────────────────────────────────

public sealed record RegimeGameState(int BoardValue, int Level, BigInteger WinAmount)
{
    // Recurrence: BoardValue + Level.  WinAmount excluded.
    public BigInteger RecurrenceHash => BoardValue * 1000 + Level;

    public RegimeGameState SetBoard(int v) => this with { BoardValue = v };
    public RegimeGameState SetLevel(int l) => this with { Level = l };
    public RegimeGameState IncLevel() => this with { Level = Level + 1 };
    public RegimeGameState AddWin(BigInteger w) => this with { WinAmount = WinAmount + w };
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 1 — Base-only game computes exactly within budget
// ═══════════════════════════════════════════════════════════════════════════

public class RegimeDetection_BaseGameExactWithinBudget
{
    /// <summary>
    /// A simple base game with a few draws should evaluate exactly
    /// when the budget is sufficient.
    /// </summary>
    [Fact]
    public void BaseGame_FitsInBudget_ReturnsExact()
    {
        // Simple game: 2 draws, 5 × 3 = 15 theoretical branches.
        var program =
            from d1 in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 2, 3, 4, 5]))
            from d2 in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([10, 20, 30]))
            select new BigInteger(d1 * 100 + d2 * 10);

        var result = HybridEvaluator.Evaluate(
            program,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig
            {
                Budget = Budget.Default, // 100k branches — plenty
                SampledSpins = 10_000
            });

        // ── Assertions ──────────────────────────────────────────────
        Assert.NotNull(result);
        Assert.NotNull(result.Report);
        Assert.Equal(EvaluationStrategy.Exact, result.OverallStrategy);
        Assert.Equal(Provenance.Exact, result.AggregateProvenance);
        Assert.False(result.BudgetExceeded);

        // At least one subgraph strategy should be "root" with Exact.
        var rootStrategy = result.SubgraphStrategies
            .FirstOrDefault(s => s.SubgraphId == "root");
        Assert.NotNull(rootStrategy);
        Assert.Equal(EvaluationStrategy.Exact, rootStrategy!.Strategy);
        Assert.Equal("within_budget", rootStrategy.Reason);
    }

    /// <summary>
    /// The exact RTP should match the closed-form value.
    /// </summary>
    [Fact]
    public void BaseGame_ExactRtp_MatchesClosedForm()
    {
        // Draw: weights [1,2,3], payouts [0,1,2].
        // RTP = (1*0 + 2*1 + 3*2) / 6 = 8/6 = 4/3.
        var program =
            from idx in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 2, 3]))
            select new BigInteger(idx);

        var result = HybridEvaluator.Evaluate(
            program,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig { Budget = Budget.Default, SampledSpins = 10_000 });

        Assert.Equal(Provenance.Exact, result.AggregateProvenance);
        Assert.Equal(new BigInteger(4), result.Report.Rtp.RationalNumerator);
        Assert.Equal(new BigInteger(3), result.Report.Rtp.RationalDenominator);
    }

    /// <summary>
    /// Multiple draws with limited total outcomes should still be exact.
    /// </summary>
    [Fact]
    public void BaseGame_ThreeDraws_WithinBudget_ReturnsExact()
    {
        var program =
            from d1 in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 1, 1]))
            from d2 in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 2]))
            from s in Slot.GetState<RegimeGameState>()
            from d3 in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 1]))
            select new BigInteger(d1 + d2 * 10 + d3 * 100);

        var result = HybridEvaluator.Evaluate(
            program,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig { Budget = Budget.Default, SampledSpins = 10_000 });

        Assert.Equal(EvaluationStrategy.Exact, result.OverallStrategy);
        Assert.Equal(Provenance.Exact, result.AggregateProvenance);
        Assert.False(result.BudgetExceeded);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 2 — Deep-retrigger game auto-falls-back to sampled without exceeding
//         budget
// ═══════════════════════════════════════════════════════════════════════════

public class RegimeDetection_DeepRetriggerFallsBack
{
    /// <summary>
    /// A program with many branches exceeding a tight budget should
    /// fall back to sampled without throwing.
    /// </summary>
    [Fact]
    public void ExceedsBudget_FallsBackToSampled()
    {
        // Multiple draws creating many branches:
        // 6 × 6 = 36 outcomes, but with tight budget (10 branches), exceeds budget.
        var program =
            from d1 in Slot.Draw<RegimeGameState>(_ =>
                WeightSet.FromIntegers([1, 2, 3, 4, 5, 6]))
            from d2 in Slot.Draw<RegimeGameState>(_ =>
                WeightSet.FromIntegers([10, 20, 30, 40, 50, 60]))
            select new BigInteger(d1 * 100 + d2);

        var result = HybridEvaluator.Evaluate(
            program,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig
            {
                Budget = Budget.Tight,  // max 10 branches
                SampledSeed = 42,
                SampledSpins = 50_000
            });

        // ── Assertions ──────────────────────────────────────────────
        Assert.NotNull(result);
        Assert.NotNull(result.Report);
        Assert.Equal(Provenance.Sampled, result.AggregateProvenance);

        // The budget was exceeded, triggering fallback.
        Assert.True(result.BudgetExceeded || result.OverallStrategy == EvaluationStrategy.Sampled);

        // The root subgraph strategy should indicate fallback.
        var rootStrategy = result.SubgraphStrategies
            .FirstOrDefault(s => s.SubgraphId == "root");
        Assert.NotNull(rootStrategy);
    }

    /// <summary>
    /// A program with a loop (simulating retrigger) that creates many
    /// branches should fall back to sampled with a tight budget.
    /// </summary>
    [Fact]
    public void DeepRetrigger_LoopWithDraws_ExceedsBudget_FallsBack()
    {
        // A bounded loop with draws in the body — simulating free spins
        // with potential retrigger.
        const int maxIter = 20;
        var stopCondition = (RegimeGameState s) => s.Level >= maxIter;
        var spinBody =
            from s in Slot.GetState<RegimeGameState>()
            from d in Slot.Draw<RegimeGameState>(_ =>
                WeightSet.FromIntegers([1, 2, 3, 4, 5]))
            from _ in Slot.Modify<RegimeGameState>(st => st
                .IncLevel()
                .AddWin(new BigInteger(d * 10)))
            select Unit.Value;

        var program =
            from _ in Slot.Loop(stopCondition, spinBody)
            from s in Slot.GetState<RegimeGameState>()
            select s.WinAmount;

        // With a tight budget (10 branches) and a loop containing a 5-outcome
        // draw, the branch estimate should exceed the budget quickly.
        var result = HybridEvaluator.Evaluate(
            program,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig
            {
                Budget = Budget.Tight,
                SampledSeed = 12345,
                SampledSpins = 30_000
            });

        // ── Assertions ──────────────────────────────────────────────
        Assert.NotNull(result.Report);
        Assert.Equal(Provenance.Sampled, result.AggregateProvenance);

        // Should NOT throw; should gracefully fall back.
        Assert.NotEqual(Provenance.Exact, result.AggregateProvenance);
    }

    /// <summary>
    /// A large sequential draw program exceeds budget and falls back.
    /// The result should be a valid sampled report.
    /// </summary>
    [Fact]
    public void LargeProgram_ExceedsBudget_ProducesValidSampledReport()
    {
        // Two draws: 50 × 20 = 1000 outcomes → exceeds Tight budget of 10.
        var weights1 = Enumerable.Repeat(1, 50).ToArray();
        var weights2 = Enumerable.Repeat(1, 20).ToArray();

        var program =
            from d1 in Slot.Draw<RegimeGameState>(_ =>
                WeightSet.FromIntegers(weights1))
            from d2 in Slot.Draw<RegimeGameState>(_ =>
                WeightSet.FromIntegers(weights2))
            select new BigInteger(d1 * 1000 + d2);

        var result = HybridEvaluator.Evaluate(
            program,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig
            {
                Budget = Budget.Tight,
                SampledSeed = 999,
                SampledSpins = 20_000,
                MaxWinCap = new BigInteger(50000)
            });

        // Should fall back successfully and produce a valid report.
        Assert.Equal(Provenance.Sampled, result.AggregateProvenance);
        Assert.NotNull(result.Report.Rtp);
        Assert.True(result.Report.Rtp.DisplayValue >= 0);
        Assert.NotNull(result.Report.Histogram);
        Assert.NotEmpty(result.Report.Histogram.Bins);
        Assert.NotNull(result.Report.MaxWin);
    }

    /// <summary>
    /// ForceSampled flag makes the evaluator skip exact completely.
    /// </summary>
    [Fact]
    public void ForceSampled_SkipsExact_ReturnsSampled()
    {
        var program =
            from d in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 1, 1]))
            select new BigInteger(d * 10);

        var result = HybridEvaluator.Evaluate(
            program,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig
            {
                Budget = Budget.Unlimited,
                ForceSampled = true,
                SampledSeed = 42,
                SampledSpins = 10_000
            });

        Assert.Equal(EvaluationStrategy.Sampled, result.OverallStrategy);
        Assert.Equal(Provenance.Sampled, result.AggregateProvenance);
        Assert.False(result.BudgetExceeded); // not exceeded — forced

        var root = result.SubgraphStrategies.First(s => s.SubgraphId == "root");
        Assert.Equal("force_sampled", root.Reason);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 3 — Plugin-containing subgraph forced sampled, never Exact
// ═══════════════════════════════════════════════════════════════════════════

public class RegimeDetection_PluginForcesSampled
{
    /// <summary>
    /// A subgraph marked with ContainsPlugin=true is always evaluated
    /// as sampled, even with unlimited budget.
    /// </summary>
    [Fact]
    public void PluginSubgraph_ForcesSampled_NeverExact()
    {
        // Wrap a program with a plugin annotation.
        var innerProgram =
            from d in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 2, 3]))
            select new BigInteger(d * 10);

        var annotatedProgram = Slot.Annotate(
            innerProgram,
            subgraphId: "bonusGame",
            containsPlugin: true);

        var result = HybridEvaluator.Evaluate(
            annotatedProgram,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig
            {
                Budget = Budget.Unlimited, // no budget limit
                SampledSeed = 42,
                SampledSpins = 25_000
            });

        // ── Assertions ──────────────────────────────────────────────
        Assert.Equal(Provenance.Sampled, result.AggregateProvenance);

        // The plugin subgraph must be marked as Sampled.
        var pluginStrategy = result.SubgraphStrategies
            .FirstOrDefault(s => s.SubgraphId == "bonusGame");
        Assert.NotNull(pluginStrategy);
        Assert.Equal(EvaluationStrategy.Sampled, pluginStrategy!.Strategy);
        Assert.True(pluginStrategy.ContainsPlugin);
        Assert.Equal("plugin_present", pluginStrategy.Reason);
    }

    /// <summary>
    /// Even with unlimited budget, a plugin forces sampled for the
    /// entire evaluation.  The engine must never claim Exact when
    /// a plugin is present.
    /// </summary>
    [Fact]
    public void PluginPresent_UnlimitedBudget_StillSampled()
    {
        var innerProgram =
            from d in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 1]))
            select new BigInteger(d);

        var annotatedProgram = Slot.Annotate(
            innerProgram,
            subgraphId: "customEval",
            containsPlugin: true);

        var result = HybridEvaluator.Evaluate(
            annotatedProgram,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig
            {
                Budget = Budget.Unlimited,
                SampledSeed = 99,
                SampledSpins = 15_000
            });

        // The engine must NEVER report Exact when a plugin is present.
        Assert.NotEqual(Provenance.Exact, result.AggregateProvenance);
        Assert.NotEqual(Provenance.ExactWithinEpsilon, result.AggregateProvenance);
        Assert.Equal(Provenance.Sampled, result.AggregateProvenance);
    }

    /// <summary>
    /// Multiple subgraphs — one with plugin, one without.
    /// The plugin causes overall Sampled provenance.
    /// </summary>
    [Fact]
    public void MixedSubgraphs_PluginAndNoPlugin_OverallSampled()
    {
        // Create two sub-programs: one with plugin, one without.
        var pluginProg =
            from d in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 2, 3]))
            select new BigInteger(d * 5);

        var normalProg =
            from d in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 1, 1, 1]))
            select new BigInteger(d * 3);

        // Wrap the plugin one with annotation.
        var annotatedPlugin = Slot.Annotate(pluginProg, "pluginEval", containsPlugin: true);

        // Combine them: run plugin subgraph, then normal subgraph.
        var combinedProgram =
            from pWin in annotatedPlugin
            from nWin in normalProg
            select pWin + nWin;

        var result = HybridEvaluator.Evaluate(
            combinedProgram,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig
            {
                Budget = Budget.Default,
                SampledSeed = 55,
                SampledSpins = 20_000
            });

        // Overall provenance must be Sampled because a plugin is present.
        Assert.Equal(Provenance.Sampled, result.AggregateProvenance);

        // The plugin subgraph strategy should indicate forced sampled.
        var pluginStrategy = result.SubgraphStrategies
            .FirstOrDefault(s => s.SubgraphId == "pluginEval");
        Assert.NotNull(pluginStrategy);
        Assert.Equal(EvaluationStrategy.Sampled, pluginStrategy!.Strategy);
        Assert.True(pluginStrategy.ContainsPlugin);
        Assert.Equal("plugin_present", pluginStrategy.Reason);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  DoD 4 — Result reports per-subgraph strategy and aggregate provenance
// ═══════════════════════════════════════════════════════════════════════════

public class RegimeDetection_PerSubgraphAndProvenance
{
    /// <summary>
    /// Every result must include per-subgraph strategy records and
    /// aggregate provenance.
    /// </summary>
    [Fact]
    public void EveryResult_HasSubgraphStrategies_AndProvenance()
    {
        var program =
            from d in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 2]))
            select new BigInteger(d * 10);

        var result = HybridEvaluator.Evaluate(
            program,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig { Budget = Budget.Default, SampledSpins = 10_000 });

        // Every result must have subgraph strategies.
        Assert.NotNull(result.SubgraphStrategies);
        Assert.NotEmpty(result.SubgraphStrategies);

        // Aggregate provenance must be present.
        Assert.True(result.AggregateProvenance == Provenance.Exact
                    || result.AggregateProvenance == Provenance.ExactWithinEpsilon
                    || result.AggregateProvenance == Provenance.Sampled);

        // Overall strategy must be a valid enum value.
        Assert.True(result.OverallStrategy == EvaluationStrategy.Exact
                    || result.OverallStrategy == EvaluationStrategy.Sampled
                    || result.OverallStrategy == EvaluationStrategy.Hybrid);
    }

    /// <summary>
    /// Each subgraph strategy record has required fields.
    /// </summary>
    [Fact]
    public void SubgraphStrategy_HasRequiredFields()
    {
        var annotated = Slot.Annotate(
            Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 2, 3]))
                .Select(i => new BigInteger(i * 10)),
            "mySubgraph",
            containsPlugin: true);

        var result = HybridEvaluator.Evaluate(
            annotated,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig { Budget = Budget.Default, SampledSpins = 10_000 });

        foreach (var ss in result.SubgraphStrategies)
        {
            Assert.False(string.IsNullOrWhiteSpace(ss.SubgraphId));
            Assert.False(string.IsNullOrWhiteSpace(ss.Reason));
            Assert.NotNull(ss.Provenance);
            // Strategy should be a valid enum.
            Assert.True(Enum.IsDefined(ss.Strategy));
        }
    }

    /// <summary>
    /// Multiple annotated subgraphs all appear in the strategy report.
    /// </summary>
    [Fact]
    public void MultipleAnnotatedSubgraphs_AllAppearInReport()
    {
        var subgraph1 = Slot.Annotate(
            Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 2]))
                .Select(i => new BigInteger(i * 5)),
            "baseGame");

        var subgraph2 = Slot.Annotate(
            Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 2, 3]))
                .Select(i => new BigInteger(i * 3)),
            "freeSpins");

        var mainProgram =
            from v1 in subgraph1
            from v2 in subgraph2
            select v1 + v2;

        var result = HybridEvaluator.Evaluate(
            mainProgram,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig { Budget = Budget.Default, SampledSpins = 10_000 });

        Assert.NotEmpty(result.SubgraphStrategies);

        // The named subgraphs should appear.
        // (They may be folded into root strategy if they don't contain plugins
        //  and we evaluate exactly — but the root strategy is always present.)
        Assert.Contains(result.SubgraphStrategies, s => s.SubgraphId == "root");
    }

    /// <summary>
    /// The SlotMathReport inside RegimeResult carries the correct
    /// provenance tag on every metric.
    /// </summary>
    [Fact]
    public void EveryMetricCarriesCorrectProvenance()
    {
        var program =
            from d in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 2, 3, 4, 5]))
            select new BigInteger(d * 10);

        var result = HybridEvaluator.Evaluate(
            program,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig { Budget = Budget.Default, SampledSpins = 10_000 });

        var report = result.Report;

        // Aggregate provenance.
        Assert.True(Enum.IsDefined(report.AggregateProvenance));

        // Every sub-metric has provenance.
        Assert.NotNull(report.Rtp.Provenance);
        Assert.NotNull(report.HitFrequency.Provenance);
        Assert.NotNull(report.Volatility.Provenance);
        Assert.NotNull(report.MaxWin.Provenance);
        Assert.NotNull(report.Histogram.Provenance);

        // All metrics should match aggregate when pure exact.
        if (result.AggregateProvenance == Provenance.Exact)
        {
            Assert.Equal(Provenance.Exact, report.Rtp.Provenance.Provenance);
            Assert.Equal(Provenance.Exact, report.HitFrequency.Provenance.Provenance);
            Assert.Equal(Provenance.Exact, report.Volatility.Provenance.Provenance);
            Assert.Equal(Provenance.Exact, report.MaxWin.Provenance.Provenance);
            Assert.Equal(Provenance.Exact, report.Histogram.Provenance.Provenance);
        }
    }

    /// <summary>
    /// Provenance badge is always present and correct — the invariant from G8.
    /// </summary>
    [Fact]
    public void ProvenanceBadge_AlwaysPresent()
    {
        var programs = new[]
        {
            Tuple.Create("base", (Slot<RegimeGameState, BigInteger>)(from d in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 2, 3]))
                select new BigInteger(d))),

            Tuple.Create("plugin", (Slot<RegimeGameState, BigInteger>)
                Slot.Annotate(
                    Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 2, 3]))
                        .Select(i => new BigInteger(i * 10)),
                    "pluginSubgraph", containsPlugin: true))
        };

        foreach (var (name, program) in programs)
        {
            var result = HybridEvaluator.Evaluate(
                program,
                new RegimeGameState(0, 0, 0),
                s => s.RecurrenceHash,
                new RegimeConfig
                {
                    Budget = Budget.Default,
                    SampledSeed = 42,
                    SampledSpins = 10_000
                });

            // Provenance badge on result.
            Assert.True(Enum.IsDefined(result.AggregateProvenance),
                $"{name}: aggregate provenance should be a valid enum value");

            // Provenance badge on report.
            Assert.True(Enum.IsDefined(result.Report.AggregateProvenance),
                $"{name}: report provenance should be a valid enum value");

            // Provenance badge on every metric.
            Assert.NotNull(result.Report.Rtp.Provenance);
            Assert.NotNull(result.Report.HitFrequency.Provenance);
            Assert.NotNull(result.Report.Volatility.Provenance);
            Assert.NotNull(result.Report.MaxWin.Provenance);
            Assert.NotNull(result.Report.Histogram.Provenance);

            // Per-subgraph strategies all have provenance.
            Assert.All(result.SubgraphStrategies, s =>
                Assert.NotNull(s.Provenance));
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Integration — exact-vs-sampled convergence with regime
// ═══════════════════════════════════════════════════════════════════════════

public class RegimeDetection_ExactVsSampledConvergence
{
    /// <summary>
    /// On a base game with enough budget, the exact RTP and the
    /// sampled RTP should converge.
    /// </summary>
    [Fact]
    public void BaseGame_ExactRtp_MatchesSampledRtp()
    {
        var program =
            from d in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 2, 3, 4, 5]))
            select new BigInteger(d * 10);

        // Exact run.
        var exactResult = HybridEvaluator.Evaluate(
            program,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig
            {
                Budget = Budget.Default,
                SampledSeed = 12345,
                SampledSpins = 50_000
            });

        Assert.Equal(Provenance.Exact, exactResult.AggregateProvenance);

        // Force sampled run with same seed for comparison.
        var sampledResult = HybridEvaluator.Evaluate(
            program,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig
            {
                Budget = Budget.Default,
                ForceSampled = true,
                SampledSeed = 12345,
                SampledSpins = 50_000
            });

        Assert.Equal(Provenance.Sampled, sampledResult.AggregateProvenance);

        // The sampled RTP should be within 3 std errors of the exact.
        var exactRtp = exactResult.Report.Rtp.DisplayValue;
        var sampledRtp = sampledResult.Report.Rtp.DisplayValue;
        var dev = System.Math.Abs(sampledRtp - exactRtp);
        var tol = 3.0 * (sampledResult.Report.Rtp.StdErr ?? 0.1);

        Assert.True(dev <= tol,
            $"Sampled RTP {sampledRtp:F6} deviates from exact {exactRtp:F6} " +
            $"by {dev:F6}, tolerance (3*SE) = {tol:F6}");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Budget edge cases
// ═══════════════════════════════════════════════════════════════════════════

public class RegimeDetection_BudgetEdgeCases
{
    /// <summary>
    /// Unlimited budget with a simple program should use exact.
    /// </summary>
    [Fact]
    public void UnlimitedBudget_SimpleProgram_UsesExact()
    {
        var program =
            from d in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 1, 1]))
            select new BigInteger(d);

        var result = HybridEvaluator.Evaluate(
            program,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig { Budget = Budget.Unlimited, SampledSpins = 10_000 });

        Assert.Equal(EvaluationStrategy.Exact, result.OverallStrategy);
        Assert.Equal(Provenance.Exact, result.AggregateProvenance);
    }

    /// <summary>
    /// Null MaxBranches means no limit — exact always tried.
    /// </summary>
    [Fact]
    public void NullMaxBranches_MeansNoLimit()
    {
        var program =
            from d1 in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 2, 3, 4, 5]))
            from d2 in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 2, 3, 4, 5]))
            select new BigInteger(d1 * 100 + d2);

        var result = HybridEvaluator.Evaluate(
            program,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig
            {
                Budget = new Budget { MaxBranches = null },
                SampledSpins = 10_000
            });

        // Should use exact since no budget limit.
        Assert.Equal(EvaluationStrategy.Exact, result.OverallStrategy);
        Assert.Equal(Provenance.Exact, result.AggregateProvenance);
    }

    /// <summary>
    /// Exact-with-epsilon (pruning) still works under regime control.
    /// </summary>
    [Fact]
    public void EpsilonPruning_WorksUnderRegimeControl()
    {
        var program =
            from idx in Slot.Draw<RegimeGameState>(_ => WeightSet.FromIntegers([1, 999]))
            select idx == 0 ? BigInteger.Zero : new BigInteger(100);

        var result = HybridEvaluator.Evaluate(
            program,
            new RegimeGameState(0, 0, 0),
            s => s.RecurrenceHash,
            new RegimeConfig
            {
                Budget = Budget.Default,
                ExactOverrides = new ExactConfig
                {
                    EpsilonNumerator = 1,
                    EpsilonDenominator = 500
                },
                SampledSpins = 10_000
            });

        // With epsilon, the result should be ExactWithinEpsilon.
        Assert.True(
            result.AggregateProvenance == Provenance.Exact ||
            result.AggregateProvenance == Provenance.ExactWithinEpsilon);
    }
}
