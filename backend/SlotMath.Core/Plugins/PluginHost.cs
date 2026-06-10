using System.Collections.Concurrent;
using SlotMath.Core.Mechanics;

namespace SlotMath.Core.Plugins;

// ═══════════════════════════════════════════════════════════════════════════
//  PluginHost — registry and execution of plugin evaluators (G12)
//
//  Plugins are registered by id and evaluated through the same
//  IEvaluator interface as the standard library — the interpreter
//  path is identical.  The host also tracks conformance status so
//  the UI and regime layer can surface plugin provenance.
//
//  In production, plugins are loaded from sandboxed assemblies via
//  isolated AssemblyLoadContext.  The in-process path is used for
//  testing and for the standard library itself.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Tracks a registered plugin's metadata and conformance status.
/// </summary>
public sealed record PluginEntry
{
    public required string PluginId { get; init; }
    public required IEvaluator Evaluator { get; init; }
    public bool IsConformant { get; init; }
    public ConformanceResult? ConformanceResult { get; init; }
}

/// <summary>
/// The plugin host manages plugin registration, discovery, and execution.
/// </summary>
public sealed class PluginHost
{
    // The host is registered as a singleton and serves concurrent requests:
    // registration/validation race against lookups, so the map must be
    // a concurrent dictionary (PluginEntry itself is an immutable record).
    private readonly ConcurrentDictionary<string, PluginEntry> _plugins = new();

    /// <summary>All registered plugin IDs (point-in-time snapshot).</summary>
    public IReadOnlyCollection<string> PluginIds => _plugins.Keys.ToArray();

    /// <summary>
    /// Register an evaluator as a named plugin.
    /// </summary>
    public void RegisterEvaluator(string pluginId, IEvaluator evaluator)
    {
        _plugins[pluginId] = new PluginEntry
        {
            PluginId = pluginId,
            Evaluator = evaluator,
            IsConformant = false, // must be validated separately
        };
    }

    /// <summary>
    /// Register with conformance result attached.
    /// </summary>
    public void RegisterEvaluator(string pluginId, IEvaluator evaluator,
        ConformanceResult conformance)
    {
        _plugins[pluginId] = new PluginEntry
        {
            PluginId = pluginId,
            Evaluator = evaluator,
            IsConformant = conformance.Passed,
            ConformanceResult = conformance,
        };
    }

    /// <summary>
    /// Try to get a registered evaluator by id.
    /// </summary>
    public IEvaluator? TryGetEvaluator(string pluginId)
    {
        return _plugins.TryGetValue(pluginId, out var entry) ? entry.Evaluator : null;
    }

    /// <summary>
    /// Get the full plugin entry including conformance status.
    /// </summary>
    public PluginEntry? TryGetEntry(string pluginId)
    {
        return _plugins.TryGetValue(pluginId, out var entry) ? entry : null;
    }

    /// <summary>
    /// Evaluate a registered plugin — same path as calling IEvaluator directly.
    /// </summary>
    public Win[] Evaluate(string pluginId, Board board, object? state)
    {
        if (!_plugins.TryGetValue(pluginId, out var entry))
            throw new KeyNotFoundException($"Plugin '{pluginId}' is not registered.");

        return entry.Evaluator.Evaluate(board, state);
    }

    /// <summary>
    /// Run the conformance harness on a registered plugin.
    /// </summary>
    public ConformanceResult Validate(string pluginId, Board testBoard,
        TimeSpan? timeout = null)
    {
        if (!_plugins.TryGetValue(pluginId, out var entry))
            throw new KeyNotFoundException($"Plugin '{pluginId}' is not registered.");

        var result = ConformanceHarness.Validate(entry.Evaluator, testBoard, timeout);
        _plugins[pluginId] = entry with
        {
            IsConformant = result.Passed,
            ConformanceResult = result,
        };
        return result;
    }

    /// <summary>
    /// Whether a non-conformant plugin can be selected.
    /// Returns (canSelect, reason).
    /// </summary>
    public (bool CanSelect, string? Reason) CanSelect(string pluginId)
    {
        if (!_plugins.TryGetValue(pluginId, out var entry))
            return (false, $"Plugin '{pluginId}' not found.");

        if (!entry.IsConformant)
            return (false, entry.ConformanceResult is not null
                ? $"Plugin non-conformant: {string.Join("; ", entry.ConformanceResult.Failures)}"
                : "Plugin has not passed conformance validation.");

        return (true, null);
    }
}
