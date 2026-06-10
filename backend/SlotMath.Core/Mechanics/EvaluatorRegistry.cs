using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace SlotMath.Core.Mechanics;

/// <summary>
/// Open registry of named <see cref="IEvaluator"/> implementations.
///
/// The standard library registers its evaluators at startup; plugins can
/// register additional evaluators at any time.  The interpreters and compiler
/// query this registry by name — adding a new evaluator requires zero changes
/// to engine code.
/// </summary>
public static class EvaluatorRegistry
{
    private static readonly ConcurrentDictionary<string, IEvaluator> _evaluators = new();

    /// <summary>All currently registered evaluators (frozen snapshot).</summary>
    public static IReadOnlyDictionary<string, IEvaluator> All =>
        _evaluators.ToFrozenDictionary();

    /// <summary>Register a named evaluator. Throws if already registered.</summary>
    public static void Register(string name, IEvaluator evaluator)
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
    public static IEvaluator? TryGet(string name)
    {
        _evaluators.TryGetValue(name, out var e);
        return e;
    }

    /// <summary>Remove all registered evaluators (primarily for test cleanup).</summary>
    public static void Clear() => _evaluators.Clear();
}
