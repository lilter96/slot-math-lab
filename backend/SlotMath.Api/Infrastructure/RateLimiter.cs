using System.Collections.Concurrent;

namespace SlotMath.Api.Infrastructure;

/// <summary>
/// Simple in-memory per-IP rate limiter (G29).
/// Production would use Redis or AspNetCore built-in rate limiting.
/// </summary>
public sealed class SimpleRateLimiter
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();

    public sealed class Bucket
    {
        public long WindowStart;
        public int Count;
    }

    /// <summary>
    /// Returns true if the request is allowed, false if rate-limited.
    /// </summary>
    public bool IsAllowed(string key, int maxRequestsPerMinute)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var windowStart = now - 60; // 1-minute sliding window

        var bucket = _buckets.GetOrAdd(key, _ => new Bucket { WindowStart = windowStart, Count = 0 });

        lock (bucket)
        {
            // Reset window if expired
            if (bucket.WindowStart < windowStart)
            {
                bucket.WindowStart = now;
                bucket.Count = 0;
            }

            bucket.Count++;

            if (bucket.Count > maxRequestsPerMinute)
            {
                return false; // rate limited
            }

            return true;
        }
    }

    /// <summary>
    /// Cleanup expired buckets (call periodically from background task).
    /// </summary>
    public void Cleanup()
    {
        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 120;
        foreach (var (key, bucket) in _buckets)
        {
            if (bucket.WindowStart < cutoff)
                _buckets.TryRemove(key, out _);
        }
    }
}

/// <summary>
/// Extension methods for rate limiting endpoints.
/// </summary>
public static class RateLimitExtensions
{
    public static RouteHandlerBuilder RequireRateLimit(
        this RouteHandlerBuilder builder,
        SimpleRateLimiter limiter,
        string endpointKey,
        int maxPerMinute)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var key = $"{ip}:{endpointKey}";

            if (!limiter.IsAllowed(key, maxPerMinute))
            {
                return Results.Problem(
                    detail: $"Rate limit exceeded: {maxPerMinute} requests/minute for {endpointKey}. Please wait and retry.",
                    statusCode: 429,
                    title: "Too Many Requests");
            }

            return await next(context);
        });
    }
}
