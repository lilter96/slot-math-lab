using System.Text;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SlotMath.Api.Features.Ai;
using SlotMath.Api.Features.Auth;
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

// ── OpenTelemetry ─────────────────────────────────────────────────────
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("SlotMath.Api", serviceVersion: "1.0.0"))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation();
        t.AddSource("SlotMath.Api");
        t.AddConsoleExporter();
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation();
        m.AddConsoleExporter();
        m.AddPrometheusExporter();
    });

// Structured logging via OTEL (traces + metrics already configured above)

// ── JWT Authentication (G29) ──────────────────────────────────────────
var jwtSecret = builder.Configuration["JWT:Secret"] ?? JwtAuth.DefaultSecret;
var jwtIssuer = builder.Configuration["JWT:Issuer"] ?? JwtAuth.DefaultIssuer;
var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtKey,
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };
    });

builder.Services.AddAuthorizationBuilder();

// ── DI ────────────────────────────────────────────────────────────────
// Rate limiter + validation
builder.Services.AddSingleton<SimpleRateLimiter>();

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

// HttpClient for Anthropic API (AI gateway)
builder.Services.AddHttpClient("anthropic", client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
});

// ── Health checks ─────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck("postgres", () =>
    {
        try
        {
            using var conn = new Npgsql.NpgsqlConnection(connectionString);
            conn.Open();
            return HealthCheckResult.Healthy("PostgreSQL is reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable", ex);
        }
    }, tags: new[] { "db", "postgres" });

// ── RFC-7807 Problem Details ──────────────────────────────────────────
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Instance = $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    };
});

// ── OpenAPI ───────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

var app = builder.Build();

// ── Authentication + Authorization middleware (G29) ───────────────────
app.UseAuthentication();
app.UseAuthorization();

// ── RFC-7807 error handling ───────────────────────────────────────────
app.UseStatusCodePages();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    AllowStatusCode404Response = true,
    ExceptionHandlingPath = null, // use built-in problem details
});

// ── Auto-apply migrations ─────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SlotMathDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// ── Prometheus metrics endpoint ───────────────────────────────────────
app.UseOpenTelemetryPrometheusScrapingEndpoint();

// ── OpenAPI ───────────────────────────────────────────────────────────
app.MapOpenApi();

// ── Hangfire dashboard (dev only) ──────────────────────────────────────
app.UseHangfireDashboard();

// ── SignalR hub ────────────────────────────────────────────────────────
app.MapHub<RunHub>("/hubs/runs");

// ── Health endpoints ───────────────────────────────────────────────────
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.ToString(),
            }),
            totalDuration = report.TotalDuration.ToString(),
        };
        await context.Response.WriteAsJsonAsync(result, System.Text.Json.JsonSerializerOptions.Web);
    },
});

app.MapGet("/ready", async (SlotMathDbContext db) =>
{
    try
    {
        await db.Database.CanConnectAsync();
        return Results.Ok(new { ready = true, postgres = "connected" });
    }
    catch
    {
        return Results.Problem(
            detail: "PostgreSQL database is not reachable.",
            statusCode: 503,
            title: "Service Unavailable");
    }
});

// ── Vertical slices ───────────────────────────────────────────────────
var configStore = app.Services.GetRequiredService<InMemoryConfigStore>();
var runStore = app.Services.GetRequiredService<InMemoryRunStore>();
var pluginHost = app.Services.GetRequiredService<PluginHost>();

app.MapAuth();
app.MapConfigs();
app.MapValidate();
app.MapEvaluate();
app.MapRuns(configStore, runStore, pluginHost);
app.MapPlugins(pluginHost);

// Persistence-backed config endpoints (auth required for save/load, G29)
app.MapPersistedConfigs();

// AI gateway (G26) + auto-tune, lint, explain (G27/G28)
app.MapAi();
app.MapAutoTune();
app.MapLint();
app.MapExplain();

app.Run();
