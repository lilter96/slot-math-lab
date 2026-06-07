using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace SlotMath.Api.Infrastructure;

// ═══════════════════════════════════════════════════════════════════════════
//  CanonicalHash — deterministic hash of a GraphConfig for cache keys
//
//  Two configs producing the same hash MUST produce identical metrics.
//  The hash covers the full config JSON (normalised), so any table change
//  yields a new hash.
// ═══════════════════════════════════════════════════════════════════════════

public static class CanonicalHash
{
    private static readonly JsonSerializerOptions NormalizedJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Compute a canonical SHA-256 hash of a config object.
    /// </summary>
    public static string Compute(object config)
    {
        var json = JsonSerializer.Serialize(config, NormalizedJson);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexStringLower(bytes);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  IResultCache — abstraction over the result cache
// ═══════════════════════════════════════════════════════════════════════════

public interface IResultCache
{
    /// <summary>Try to get a cached result. Returns null on miss.</summary>
    Task<string?> GetAsync(string configHash, CancellationToken ct = default);

    /// <summary>Store a result keyed by config hash.</summary>
    Task SetAsync(string configHash, string resultJson, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>Current recompute counter (for testing).</summary>
    long RecomputeCount { get; }
}

// ═══════════════════════════════════════════════════════════════════════════
//  RedisResultCache — Redis-backed result cache with recompute counter
// ═══════════════════════════════════════════════════════════════════════════

public sealed class RedisResultCache : IResultCache
{
    private readonly IDistributedCache _cache;
    private long _recomputeCount;

    public long RecomputeCount => Interlocked.Read(ref _recomputeCount);

    public RedisResultCache(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<string?> GetAsync(string configHash, CancellationToken ct = default)
    {
        var result = await _cache.GetStringAsync($"result:{configHash}", ct);
        if (result is not null)
            return result;

        // Cache miss — caller must recompute
        Interlocked.Increment(ref _recomputeCount);
        return null;
    }

    public async Task SetAsync(string configHash, string resultJson, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl ?? TimeSpan.FromHours(24),
        };
        await _cache.SetStringAsync($"result:{configHash}", resultJson, options, ct);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  InMemoryResultCache — for testing without Redis
// ═══════════════════════════════════════════════════════════════════════════

public sealed class InMemoryResultCache : IResultCache
{
    private readonly Dictionary<string, string> _cache = new();
    private long _recomputeCount;

    public long RecomputeCount => Interlocked.Read(ref _recomputeCount);

    public Task<string?> GetAsync(string configHash, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(configHash, out var result))
            return Task.FromResult<string?>(result);

        Interlocked.Increment(ref _recomputeCount);
        return Task.FromResult<string?>(null);
    }

    public Task SetAsync(string configHash, string resultJson, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        _cache[configHash] = resultJson;
        return Task.CompletedTask;
    }
}
