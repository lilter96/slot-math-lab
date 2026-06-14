using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace SlotMath.Core.Mechanics;

/// <summary>
/// Open registry of named <see cref="IFastPathTransform"/> implementations.
///
/// The standard library registers its trusted fast-path transforms at startup.
/// The interpreters and compiler query this registry by name — adding a new
/// fast-path requires zero changes to engine code.
///
/// This registry holds <see cref="IFastPathTransform"/>, NOT the plugin contract
/// <see cref="ITransform"/>; plugins are managed separately by the PluginHost
/// (Invariant 2, G12).
///
/// Because the registry is static, consumers should treat it as append-only
/// during a process lifetime (Clear is provided for testing).
/// </summary>
public static class TransformRegistry
{
    private static readonly ConcurrentDictionary<string, IFastPathTransform> _transforms = new();

    /// <summary>
    /// All currently registered transforms.  Returns a frozen snapshot; callers
    /// that need the very latest entry should re-read the property.
    /// </summary>
    public static IReadOnlyDictionary<string, IFastPathTransform> All =>
        _transforms.ToFrozenDictionary();

    /// <summary>
    /// Register a named transform.  Throws if the name is already registered.
    /// </summary>
    /// <param name="name">Unique name for the transform.</param>
    /// <param name="transform">The transform instance.</param>
    public static void Register(string name, IFastPathTransform transform)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(transform);

        // TryAdd is atomic — a concurrent duplicate registration cannot
        // slip between a contains-check and the write.
        if (!_transforms.TryAdd(name, transform))
            throw new InvalidOperationException(
                $"Transform '{name}' is already registered.");
    }

    /// <summary>
    /// Look up a transform by name.  Returns null if not found.
    /// </summary>
    public static IFastPathTransform? TryGet(string name)
    {
        _transforms.TryGetValue(name, out var t);
        return t;
    }

    /// <summary>
    /// Remove all registered transforms.  Primarily for test cleanup.
    /// </summary>
    public static void Clear()
    {
        _transforms.Clear();
    }
}
