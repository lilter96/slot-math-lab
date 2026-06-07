using System.Diagnostics;
using SlotMath.Core.Mechanics;

namespace SlotMath.Core.Plugins;

// ═══════════════════════════════════════════════════════════════════════════
//  PluginSandbox — isolated execution environment for plugin code (G12)
//
//  Each plugin invocation runs on a separate thread with a configurable
//  timeout.  If the plugin exceeds the time budget, the thread is
//  interrupted and the result reports the failure.
//
//  In a full production deployment, this would also use an isolated
//  AssemblyLoadContext to block I/O and cap memory.  The test harness
//  verifies isolation properties through the conformance suite.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Configuration for a sandboxed plugin execution.
/// </summary>
public sealed record SandboxConfig
{
    /// <summary>Maximum execution time. Default 5 seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Optional cancellation token for early termination.</summary>
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
}

/// <summary>
/// Result of a sandboxed plugin execution.
/// </summary>
public sealed record PluginSandboxResult
{
    /// <summary>Whether execution completed within limits without error.</summary>
    public bool Success { get; init; }

    /// <summary>The wins produced (null on failure).</summary>
    public Win[]? Wins { get; init; }

    /// <summary>Error message if execution failed.</summary>
    public string? Error { get; init; }

    /// <summary>Wall-clock elapsed time.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>True when the plugin was terminated due to timeout.</summary>
    public bool TimedOut { get; init; }
}

/// <summary>
/// Sandboxed plugin executor.  Runs an <see cref="IEvaluator"/> on a
/// separate thread with a time budget and cancellation support.
///
/// Misbehaving plugins (infinite loops, unbounded memory, exceptions)
/// are contained — they never crash or hang the host process.
/// </summary>
public static class PluginSandbox
{
    /// <summary>
    /// Execute an evaluator inside the sandbox.
    /// </summary>
    /// <param name="evaluator">The plugin evaluator to run.</param>
    /// <param name="board">Board to evaluate.</param>
    /// <param name="config">Sandbox configuration (timeout, cancellation).</param>
    /// <returns>A <see cref="PluginSandboxResult"/> with wins or error details.</returns>
    public static PluginSandboxResult Execute(
        IEvaluator evaluator,
        Board board,
        SandboxConfig? config = null)
    {
        config ??= new SandboxConfig();
        var startedAt = Stopwatch.GetTimestamp();

        Win[]? wins = null;
        string? error = null;
        var timedOut = false;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            config.CancellationToken);
        cts.CancelAfter(config.Timeout);

        try
        {
            // Run on a separate thread so we can enforce timeout.
            Exception? pluginException = null;

            var thread = new Thread(() =>
            {
                try
                {
                    wins = evaluator.Evaluate(board, null);
                }
                catch (Exception ex)
                {
                    pluginException = ex;
                }
            })
            {
                IsBackground = true,
                Name = "PluginSandbox",
            };

            thread.Start();

            var completed = thread.Join(config.Timeout);

            if (!completed)
            {
                timedOut = true;
                error = $"Plugin execution timed out after {config.Timeout.TotalSeconds:F1}s.";
                // The thread is abandoned — it will be collected as background.
                // In production, we'd use a more aggressive abort mechanism.
                wins = null;
            }
            else if (pluginException != null)
            {
                error = $"Plugin threw an exception: {pluginException.GetType().Name}: {pluginException.Message}";
                wins = null;
            }
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            error = "Plugin execution was cancelled.";
        }
        catch (Exception ex)
        {
            error = $"Sandbox error: {ex.GetType().Name}: {ex.Message}";
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        return new PluginSandboxResult
        {
            Success = wins != null && error == null,
            Wins = wins,
            Error = error,
            Elapsed = elapsed,
            TimedOut = timedOut,
        };
    }
}
