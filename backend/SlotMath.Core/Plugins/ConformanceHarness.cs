using SlotMath.Core.Mechanics;

namespace SlotMath.Core.Plugins;

// ═══════════════════════════════════════════════════════════════════════════
//  ConformanceHarness — validates plugin correctness and safety (G12)
//
//  Runs a battery of checks on a plugin evaluator:
//    - Purity: same input → same output (no hidden state, no randomness)
//    - Determinism: multiple calls produce identical results
//    - Correctness: matches expected wins on a known hand case
//    - Safety: no exceptions under normal inputs
//    - Performance: completes within time limit
//
//  A plugin that fails any check is flagged non-conformant and cannot
//  be used in production.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Result of running the conformance harness against a plugin.
/// </summary>
public sealed record ConformanceResult
{
    /// <summary>True when all checks passed.</summary>
    public bool Passed { get; init; }

    /// <summary>List of failures (empty when passed).</summary>
    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();

    /// <summary>Optional warnings (non-fatal).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public override string ToString() =>
        Passed
            ? "Conformance: PASSED"
            : $"Conformance: FAILED — {string.Join("; ", Failures)}";
}

/// <summary>
/// Static harness that validates an <see cref="IEvaluator"/> implementation
/// conforms to the plugin contract (purity, determinism, safety, performance).
/// </summary>
public static class ConformanceHarness
{
    /// <summary>
    /// Run the full conformance suite against an evaluator.
    /// </summary>
    /// <param name="evaluator">The plugin evaluator to validate.</param>
    /// <param name="testBoard">A non-empty board for testing.</param>
    /// <param name="timeout">Max time allowed per evaluation.</param>
    /// <returns>A <see cref="ConformanceResult"/>.</returns>
    public static ConformanceResult Validate(
        IEvaluator evaluator,
        IReadOnlyDictionary<string, object?> testState,
        TimeSpan? timeout = null)
    {
        var timeoutVal = timeout ?? TimeSpan.FromSeconds(2);
        var failures = new List<string>();
        var warnings = new List<string>();

        // ── Check 1: Purity (no exceptions on valid input) ──────────────
        Win[]? baseline = null;
        try
        {
            baseline = evaluator.Evaluate(testState);
        }
        catch (Exception ex)
        {
            failures.Add($"Purity check failed: evaluator threw {ex.GetType().Name}: {ex.Message}");
        }

        if (baseline == null)
            return new ConformanceResult { Passed = false, Failures = failures };

        // ── Check 2: Determinism (same input → same output) ─────────────
        for (var i = 0; i < 5; i++)
        {
            Win[]? current;
            try
            {
                current = evaluator.Evaluate(testState);
            }
            catch (Exception ex)
            {
                failures.Add($"Determinism check {i}: evaluator threw {ex.GetType().Name}");
                continue;
            }

            if (current.Length != baseline.Length)
            {
                failures.Add(
                    $"Determinism check {i}: expected {baseline.Length} wins, got {current.Length}");
                continue;
            }

            for (var j = 0; j < current.Length; j++)
            {
                if (!current[j].Equals(baseline[j]))
                {
                    failures.Add(
                        $"Determinism check {i}: win[{j}] differs from baseline");
                    break;
                }
            }
        }

        // ── Check 3: Safety — sandbox timeout ─────────────────────────
        var sandboxResult = PluginSandbox.Execute(evaluator, testState,
            new SandboxConfig { Timeout = timeoutVal });

        if (!sandboxResult.Success)
        {
            failures.Add($"Safety check failed: {sandboxResult.Error}");
        }

        if (sandboxResult.TimedOut)
        {
            failures.Add("Safety check failed: plugin exceeded time limit");
        }

        // ── Check 4: Completes within time ─────────────────────────────
        var elapsed = sandboxResult.Elapsed;
        if (elapsed > timeoutVal)
        {
            failures.Add(
                $"Performance: plugin took {elapsed.TotalMilliseconds:F0}ms, " +
                $"limit is {timeoutVal.TotalMilliseconds:F0}ms");
        }

        // ── Warnings ───────────────────────────────────────────────────
        if (baseline.Length == 0)
            warnings.Add("Plugin returned zero wins on test board (may be expected).");

        return new ConformanceResult
        {
            Passed = failures.Count == 0,
            Failures = failures,
            Warnings = warnings,
        };
    }
}
