using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace SlotMath.Core.Mechanics;

/// <summary>
/// Open registry of named <see cref="IFastPathEvaluator"/> implementations.
///
/// The standard library registers its trusted fast-path evaluators (Lines/Ways/
/// Cluster) at startup.  The interpreters and compiler query this registry by
/// name — adding a new fast-path requires zero changes to engine code.
///
/// This registry holds <see cref="IFastPathEvaluator"/>, NOT the plugin contract
/// <see cref="IEvaluator"/>; plugins are managed separately by the PluginHost
/// (Invariant 2, G12).
/// </summary>
public static class EvaluatorRegistry
{
    private static readonly ConcurrentDictionary<string, IFastPathEvaluator> _evaluators = new();

    /// <summary>All currently registered evaluators (frozen snapshot).</summary>
    public static IReadOnlyDictionary<string, IFastPathEvaluator> All =>
        _evaluators.ToFrozenDictionary();

    /// <summary>Register a named evaluator. Throws if already registered.</summary>
    public static void Register(string name, IFastPathEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(evaluator);

        // TryAdd is atomic — a concurrent duplicate registration cannot
        // slip between a contains-check and the write.
        if (!_evaluators.TryAdd(name, evaluator))
            throw new InvalidOperationException(
                $"Evaluator '{name}' is already registered.");
    }

    /// <summary>Look up an evaluator by name. Returns null if not found.</summary>
    public static IFastPathEvaluator? TryGet(string name)
    {
        _evaluators.TryGetValue(name, out var e);
        return e;
    }

    /// <summary>Remove all registered evaluators (primarily for test cleanup).</summary>
    public static void Clear() => _evaluators.Clear();
}
