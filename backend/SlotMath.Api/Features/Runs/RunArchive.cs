using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Model;

namespace SlotMath.Api.Features.Runs;

public sealed record RunModel(string Name, string? ModelHash, double? TargetRtp, long? WinCap);
public sealed record RunSummary(string Id, string ConfigId, int ConfigVersion, string? ConfigHash,
    RunModel Model, string Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt,
    long Seed, int DegreeOfParallelism, string StreamScheme, long SampleCount, long TotalSamples,
    double? Rtp, double? StdErr, long ElapsedMs);
public sealed record RunPage(IReadOnlyList<RunSummary> Items, string? NextCursor, int Total,
    int Completed, int Partial, int Failed, int Active);
public sealed record RunEvidence(RunResponse Run, RunModel Model, System.Text.Json.JsonElement? PinnedConfig,
    string? ComputedConfigHash, bool InputVerified)
{ public DiagnosticArtifact[] Diagnostics { get; init; } = []; }

/// <summary>Read-only archive. Document identity is excluded from the comparison
/// fingerprint; every other serialized model field participates.</summary>
public static class RunArchive
{
    public static RunModel Describe(ConfigEntry? entry, string configId) => entry is null
        ? new($"Config {configId}", null, null, null)
        : new(entry.Config.Name ?? "Untitled model", CanonicalHash.Compute(entry.Config with { Id = null }),
            Target(entry.Config), entry.Config.Nodes.OfType<MetricsSinkNode>().FirstOrDefault()?.WinCap);

    private static double? Target(GraphConfig config) => config.InitialState?.TryGetValue("targetRtpPercent", out var value) == true
        && value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetDouble(out var percent)
        && double.IsFinite(percent) && percent >= 0 ? percent / 100 : null;

    public static IResult Query(InMemoryRunStore runs, InMemoryConfigStore configs,
        int? limit, string? cursor, string? status, string? search)
    {
        var take = limit ?? 20;
        if (take is < 1 or > 100 || (search?.Length ?? 0) > 200
            || status is not (null or "all" or "completed" or "partial" or "failed" or "active"))
            return Results.BadRequest(new { error = "Invalid archive filter or page size (1..100)." });
        DateTimeOffset? before = null; string? beforeId = null;
        if (cursor is not null)
        {
            try
            {
                if (cursor.Length > 180) throw new FormatException();
                var decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor)).Split(':', 2);
                if (decoded.Length != 2 || !long.TryParse(decoded[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                    || decoded[1].Length is < 1 or > 128) throw new FormatException();
                before = new DateTimeOffset(ticks, TimeSpan.Zero); beforeId = decoded[1];
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            { return Results.BadRequest(new { error = "Invalid archive cursor." }); }
        }
        var entries = new Dictionary<(string, int), ConfigEntry?>();
        ConfigEntry? Config(RunEntry run)
        {
            var key = (run.ConfigId, run.ConfigVersion);
            if (!entries.TryGetValue(key, out var entry)) entries[key] = entry = configs.GetVersion(key.ConfigId, key.ConfigVersion);
            return entry;
        }
        var all = runs.List().Where(r => string.IsNullOrWhiteSpace(search)
            || (Config(r)?.Config.Name ?? "").Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
            || r.Id.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
            || (r.ConfigHash ?? "").Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
            || r.Seed.ToString(CultureInfo.InvariantCulture) == search.Trim()).ToArray();
        var filtered = all.Where(r => status switch
        {
            "completed" => r.Status == "completed",
            "partial" => r.Status == "cancelled",
            "failed" => r.Status == "failed",
            "active" => r.Status is "pending" or "running" or "cancelling",
            _ => true
        }).ToArray();
        var page = filtered.Where(r => before is null || r.CreatedAt < before
            || r.CreatedAt == before && string.CompareOrdinal(r.Id, beforeId) < 0).Take(take + 1).ToArray();
        var items = page.Take(take).Select(r =>
        {
            var p = InMemoryRunStore.Snapshot(r);
            return new RunSummary(r.Id, r.ConfigId, r.ConfigVersion, r.ConfigHash, Describe(Config(r), r.ConfigId),
                r.Status, r.CreatedAt, r.CompletedAt, r.Seed, r.DegreeOfParallelism, r.Execution?.StreamScheme ?? "splitmix64-chunk-65536",
                p.SampleCount, p.TotalSamples, p.SampleCount > 0 ? p.RunningRtp : null,
                p.SampleCount > 1 ? p.StdErr : null, p.ElapsedMs);
        }).ToArray();
        var last = page.Length > take ? page[take - 1] : null;
        var next = last is null ? null : WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
            $"{last.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{last.Id}"));
        return Results.Ok(new RunPage(items, next, filtered.Length, all.Count(r => r.Status == "completed"),
            all.Count(r => r.Status == "cancelled"), all.Count(r => r.Status == "failed"),
            all.Count(r => r.Status is "pending" or "running" or "cancelling")));
    }
}
