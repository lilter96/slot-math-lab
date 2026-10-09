using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using SlotMath.Core.Compiler;
using SlotMath.Core.Model;
using SlotMath.Core.Plugins;

namespace SlotMath.Api.Infrastructure;

/// <summary>Bounded, process-local cache of immutable, plugin-free state plans.
/// Concurrent requests for one graph compile once. Every execution gets a
/// private runner; edited graphs get a different canonical content key.</summary>
public sealed class CompiledGraphCache(PluginHost plugins, IWebHostEnvironment environment) : IDisposable
{
    private const long Budget = 32 * 1024 * 1024;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = Budget });
    private readonly ConcurrentDictionary<string, Lazy<CompileResult>> _pending = new();

    public CompileResult Compile(GraphConfig config, IReadOnlyList<SlotMath.Core.Measurements.MeasurementDefinition>? measurements = null)
    {
        var hash = CanonicalHash.Compute(config) + (measurements is { Count: > 0 } ? ":" + MeasurementHash.Compute(measurements) : "");
        if (_cache.TryGetValue<CompileResult>(hash, out var cached)) return cached!;
        var pending = _pending.GetOrAdd(hash, _ => new Lazy<CompileResult>(
            () => new GraphCompiler(plugins, allowPlugins: !environment.IsProduction()).Compile(config, measurements), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var result = pending.Value;
            // Registry/plugin-based programs may depend on external runtime
            // registrations. They are deliberately recompiled on each request.
            if (result.IsValid && result.SamplingEngine == "compiled-state-plan-v1")
            {
                // Account conservatively for ASTs, delegates, typed arrays, and
                // the canonical program. This is an estimate, not a heap quota.
                var estimate = Math.Max(65536L, (JsonSerializer.SerializeToUtf8Bytes(config, SlotMath.Core.JsonOptions.Default).LongLength + JsonSerializer.SerializeToUtf8Bytes(measurements ?? [], SlotMath.Core.JsonOptions.Default).LongLength) * 32);
                if (estimate <= Budget)
                    _cache.Set(hash, result, new MemoryCacheEntryOptions { Size = estimate, SlidingExpiration = TimeSpan.FromMinutes(10), AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) });
            }
            return result;
        }
        finally { _pending.TryRemove(new KeyValuePair<string, Lazy<CompileResult>>(hash, pending)); }
    }

    public void Dispose() => _cache.Dispose();
}
