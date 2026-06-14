using System.Collections.Concurrent;
using SlotMath.Core.Mechanics;

namespace SlotMath.Core.Plugins;

// ═══════════════════════════════════════════════════════════════════════════
//  PluginHost — registry and execution of user-provided plugins (G12)
//
//  Supports both IEvaluator and ITransform plugins.  Plugins implement the
//  same contracts as the standard library — the interpreter path is identical.
//  The host tracks conformance status so the UI and regime layer can surface
//  plugin provenance.
//
//  In production, plugins are loaded from sandboxed assemblies via isolated
//  AssemblyLoadContext.  The in-process path is used for testing.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Tracks a registered evaluator plugin's metadata and conformance status.
/// </summary>
public sealed record PluginEntry
{
    public required string PluginId { get; init; }
    public required IEvaluator Evaluator { get; init; }
    public bool IsConformant { get; init; }
    public ConformanceResult? ConformanceResult { get; init; }

    /// <summary>The plugin ABI version it was built against (D2/D18).</summary>
    public int AbiVersion { get; init; } = PluginHost.CurrentAbiVersion;
}

/// <summary>Outcome of an admin plugin-registration attempt (D18, G12).</summary>
public sealed record PluginRegistration(bool Accepted, string? Code, string? Reason)
{
    public static PluginRegistration Ok { get; } = new(true, null, null);
}

/// <summary>
/// Tracks a registered transform plugin's metadata.
/// </summary>
public sealed record TransformPluginEntry
{
    public required string PluginId { get; init; }
    public required ITransform Transform { get; init; }
}

/// <summary>
/// The plugin host manages plugin registration, discovery, and execution.
/// </summary>
public sealed class PluginHost
{
    private readonly ConcurrentDictionary<string, PluginEntry> _plugins = new();
    private readonly ConcurrentDictionary<string, TransformPluginEntry> _transforms = new();

    /// <summary>The plugin ABI version this host supports (D2/D18). Pinned; bumped on a breaking ABI change.</summary>
    public const int CurrentAbiVersion = 1;

    /// <summary>
    /// Admin registration with an explicit ABI version (G12): a plugin built
    /// against a mismatched ABI is rejected with a coded error and NOT registered.
    /// </summary>
    public PluginRegistration TryRegisterEvaluator(
        string pluginId, IEvaluator evaluator, int abiVersion, ConformanceResult conformance)
    {
        if (abiVersion != CurrentAbiVersion)
            return new PluginRegistration(
                false,
                Compiler.ErrorCodes.PluginAbiMismatch,
                $"Plugin '{pluginId}' was built against ABI v{abiVersion}, but the host supports v{CurrentAbiVersion}.");

        _plugins[pluginId] = new PluginEntry
        {
            PluginId = pluginId,
            Evaluator = evaluator,
            IsConformant = conformance.Passed,
            ConformanceResult = conformance,
            AbiVersion = abiVersion,
        };
        return PluginRegistration.Ok;
    }

    /// <summary>All registered evaluator plugin IDs (point-in-time snapshot).</summary>
    public IReadOnlyCollection<string> PluginIds => _plugins.Keys.ToArray();

    // ── Evaluator plugins ─────────────────────────────────────────────────

    /// <summary>
    /// Register an evaluator as a named plugin.
    /// </summary>
    public void RegisterEvaluator(string pluginId, IEvaluator evaluator)
    {
        _plugins[pluginId] = new PluginEntry
        {
            PluginId = pluginId,
            Evaluator = evaluator,
            IsConformant = false,
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
    /// Try to get a registered evaluator plugin by id.
    /// </summary>
    public IEvaluator? TryGetEvaluator(string pluginId) =>
        _plugins.TryGetValue(pluginId, out var entry) ? entry.Evaluator : null;

    /// <summary>
    /// Get the full plugin entry including conformance status.
    /// </summary>
    public PluginEntry? TryGetEntry(string pluginId) =>
        _plugins.TryGetValue(pluginId, out var entry) ? entry : null;

    /// <summary>
    /// Evaluate a registered plugin — same path as calling IEvaluator directly.
    /// </summary>
    public Win[] Evaluate(string pluginId, IReadOnlyDictionary<string, object?> state)
    {
        if (!_plugins.TryGetValue(pluginId, out var entry))
            throw new KeyNotFoundException($"Plugin '{pluginId}' is not registered.");
        return entry.Evaluator.Evaluate(state);
    }

    /// <summary>
    /// Run the conformance harness on a registered evaluator plugin.
    /// </summary>
    public ConformanceResult Validate(string pluginId,
        IReadOnlyDictionary<string, object?> testState,
        TimeSpan? timeout = null)
    {
        if (!_plugins.TryGetValue(pluginId, out var entry))
            throw new KeyNotFoundException($"Plugin '{pluginId}' is not registered.");

        var result = ConformanceHarness.Validate(entry.Evaluator, testState, timeout);
        _plugins[pluginId] = entry with
        {
            IsConformant = result.Passed,
            ConformanceResult = result,
        };
        return result;
    }

    /// <summary>
    /// Whether a non-conformant evaluator plugin can be selected.
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

    // ── Transform plugins ─────────────────────────────────────────────────

    /// <summary>
    /// Register an ITransform as a named plugin.
    /// </summary>
    public void RegisterTransform(string pluginId, ITransform transform)
    {
        _transforms[pluginId] = new TransformPluginEntry
        {
            PluginId = pluginId,
            Transform = transform,
        };
    }

    /// <summary>
    /// Try to get a registered transform plugin by id.
    /// </summary>
    public ITransform? TryGetTransform(string pluginId) =>
        _transforms.TryGetValue(pluginId, out var entry) ? entry.Transform : null;
}

