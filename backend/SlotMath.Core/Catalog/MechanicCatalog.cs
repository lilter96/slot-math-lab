using System.Reflection;
using System.Text.Json;
using SlotMath.Core.Model;

namespace SlotMath.Core.Catalog;

// ═══════════════════════════════════════════════════════════════════════════
//  MechanicCatalog — standard mechanic catalog loaded from embedded JSON
//
//  Every *.mechanic.json embedded resource in this assembly is loaded as a
//  CustomMechanic and made available by name.  The catalog is an OPEN SET:
//  adding a new mechanic requires only dropping a new JSON file into this
//  directory and marking it EmbeddedResource — zero C# code, zero
//  interpreter/compiler changes.
//
//  Usage:
//    • MechanicCatalog.Default — the standard catalog (lazy singleton).
//    • catalog.Merge(userMechanics) — returns a new dictionary merging the
//      catalog with user-supplied entries (user entries win on conflict).
//    • Pass the merged dictionary to GraphConfig.Mechanics before compiling.
// ═══════════════════════════════════════════════════════════════════════════

public sealed class MechanicCatalog
{
    // ── Singleton ────────────────────────────────────────────────────────

    private static readonly Lazy<MechanicCatalog> _default =
        new(Load, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>The standard catalog loaded from embedded JSON resources.</summary>
    public static MechanicCatalog Default => _default.Value;

    // ── State ────────────────────────────────────────────────────────────

    private readonly IReadOnlyDictionary<string, CustomMechanic> _entries;

    private MechanicCatalog(IReadOnlyDictionary<string, CustomMechanic> entries)
    {
        _entries = entries;
    }

    /// <summary>All mechanics keyed by name.</summary>
    public IReadOnlyDictionary<string, CustomMechanic> Entries => _entries;

    // ── Merge ────────────────────────────────────────────────────────────

    /// <summary>
    /// Return a new dictionary merging the catalog with <paramref name="userMechanics"/>.
    /// User entries override catalog entries of the same name.  When
    /// <paramref name="userMechanics"/> is null or empty, a copy of the catalog
    /// entries is returned.
    /// </summary>
    public Dictionary<string, CustomMechanic> Merge(
        IReadOnlyDictionary<string, CustomMechanic>? userMechanics)
    {
        var result = new Dictionary<string, CustomMechanic>(_entries);

        if (userMechanics is { Count: > 0 })
        {
            foreach (var (name, mechanic) in userMechanics)
                result[name] = mechanic;
        }

        return result;
    }

    // ── Loading ─────────────────────────────────────────────────────────

    private static MechanicCatalog Load()
    {
        var assembly = typeof(MechanicCatalog).Assembly;
        var entries = new Dictionary<string, CustomMechanic>(StringComparer.OrdinalIgnoreCase);

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith(".mechanic.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var mechanic = LoadMechanic(assembly, resourceName);
            if (mechanic is not null)
                entries[mechanic.Name] = mechanic;
        }

        return new MechanicCatalog(entries);
    }

    private static CustomMechanic? LoadMechanic(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        return JsonSerializer.Deserialize<CustomMechanic>(stream, JsonOptions.Default);
    }
}
