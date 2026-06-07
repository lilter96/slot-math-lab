using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.EntityFrameworkCore;
using SlotMath.Api.Features.Configs;
using SlotMath.Api.Features.Evaluate;
using SlotMath.Api.Features.PersistedConfigs;
using SlotMath.Api.Features.Plugins;
using SlotMath.Api.Features.Runs;
using SlotMath.Api.Features.Validate;
using SlotMath.Api.Infrastructure;
using SlotMath.Api.Persistence;
using SlotMath.Core.Plugins;

var builder = WebApplication.CreateBuilder(args);

// ── DI ────────────────────────────────────────────────────────────────
// In-memory stores (fast path, always available)
builder.Services.AddSingleton<InMemoryConfigStore>();
builder.Services.AddSingleton<InMemoryRunStore>();
builder.Services.AddSingleton<PluginHost>();
builder.Services.AddSingleton<IResultCache, InMemoryResultCache>();

// Hangfire (job runner)
builder.Services.AddHangfire(config =>
    config.UseMemoryStorage());
builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Math.Min(4, Environment.ProcessorCount);
});

// SignalR (real-time progress streaming)
builder.Services.AddSignalR();

// Run job service (transient — Hangfire resolves a new instance per job)
builder.Services.AddTransient<RunJobService>();

// EF Core + Postgres (persistence path)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5433;Database=slotmath;Username=slotmath;Password=slotmath";
builder.Services.AddDbContext<SlotMathDbContext>(options =>
    options.UseNpgsql(connectionString));

// Redis result cache (when configured)
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
        options.Configuration = redisConnection);
    builder.Services.AddSingleton<IResultCache, RedisResultCache>();
}

// Register the persistence-backed config service
builder.Services.AddScoped<ConfigPersistenceService>();

// ── OpenAPI ───────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

var app = builder.Build();

// ── Auto-apply migrations ─────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SlotMathDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// ── OpenAPI ───────────────────────────────────────────────────────────
app.MapOpenApi();

// ── Hangfire dashboard (dev only) ──────────────────────────────────────
app.UseHangfireDashboard();

// ── SignalR hub ────────────────────────────────────────────────────────
app.MapHub<RunHub>("/hubs/runs");

// ── Vertical slices ───────────────────────────────────────────────────
var configStore = app.Services.GetRequiredService<InMemoryConfigStore>();
var runStore = app.Services.GetRequiredService<InMemoryRunStore>();
var pluginHost = app.Services.GetRequiredService<PluginHost>();

app.MapConfigs();
app.MapValidate();
app.MapEvaluate();
app.MapRuns(configStore, runStore, pluginHost);
app.MapPlugins(pluginHost);

// Persistence-backed config endpoints
app.MapPersistedConfigs();

app.Run();
