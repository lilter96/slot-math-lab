using Microsoft.Extensions.Configuration;

namespace SlotMath.Api.Infrastructure;

/// <summary>
/// Product feature flags, read once from the "Features" configuration section at startup.
/// A disabled flag means its endpoints are never registered (they answer 404), so changing
/// a flag requires an application restart — that is deliberate.
/// </summary>
public sealed record FeatureFlags(bool Ai, bool AutoTune, bool Plugins, bool Play)
{
    /// <summary>
    /// Reads the flags. Absent keys default to enabled in local/test environments and to the
    /// 1.0 defaults elsewhere: Ai/AutoTune/Plugins off, Play on. Explicit values always win,
    /// including the string form of environment variables such as Features__Ai=false. A blank
    /// or unparsable value fails startup and names the offending key.
    /// </summary>
    public static FeatureFlags Read(IConfiguration configuration, bool localMode)
    {
        var section = configuration.GetSection("Features");
        return new FeatureFlags(
            ReadFlag("Ai", productionDefault: false),
            ReadFlag("AutoTune", productionDefault: false),
            ReadFlag("Plugins", productionDefault: false),
            ReadFlag("Play", productionDefault: true));

        bool ReadFlag(string name, bool productionDefault)
        {
            // Only a genuinely absent key falls back to the default. An explicitly blank or
            // whitespace value is a configuration error, so a stray empty env var (e.g.
            // Features__Play=) cannot silently enable a flag in production.
            var raw = section[name];
            if (raw is null) return localMode || productionDefault;
            if (!bool.TryParse(raw, out var value))
                throw new InvalidOperationException($"Features:{name} must be \"true\" or \"false\" but was \"{raw}\".");
            return value;
        }
    }
}
