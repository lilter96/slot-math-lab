using System.Collections.Frozen;

namespace SlotMath.Core.Mechanics;

/// <summary>
/// Open registry of named <see cref="ITransform"/> implementations.
///
/// The standard library registers its transforms at startup; plugins can
/// register additional transforms at any time.  The interpreters and compiler
/// query this registry by name — adding a new transform requires zero changes
/// to engine code.
///
/// Because the registry is static, consumers should treat it as append-only
/// during a process lifetime (Clear is provided for testing).
/// </summary>
public static class TransformRegistry
{
    private static readonly Dictionary<string, ITransform> _transforms = new();

    /// <summary>
    /// All currently registered transforms.  Returns a frozen snapshot; callers
    /// that need the very latest entry should re-read the property.
    /// </summary>
    public static IReadOnlyDictionary<string, ITransform> All =>
        _transforms.ToFrozenDictionary();

    /// <summary>
    /// Register a named transform.  Throws if the name is already registered.
    /// </summary>
    /// <param name="name">Unique name for the transform.</param>
    /// <param name="transform">The transform instance.</param>
    public static void Register(string name, ITransform transform)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(transform);

        if (_transforms.ContainsKey(name))
            throw new InvalidOperationException(
                $"Transform '{name}' is already registered.");

        _transforms[name] = transform;
    }

    /// <summary>
    /// Look up a transform by name.  Returns null if not found.
    /// </summary>
    public static ITransform? TryGet(string name)
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
