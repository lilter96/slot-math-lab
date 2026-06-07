using SlotMath.Api.Features.Configs;
using SlotMath.Api.Features.Evaluate;
using SlotMath.Api.Features.Plugins;
using SlotMath.Api.Features.Runs;
using SlotMath.Api.Features.Validate;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Plugins;

var builder = WebApplication.CreateBuilder(args);

// ── DI ────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<InMemoryConfigStore>();
builder.Services.AddSingleton<InMemoryRunStore>();
builder.Services.AddSingleton<PluginHost>();

// ── OpenAPI ───────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

var app = builder.Build();

// ── OpenAPI served at /openapi/{documentName}.json ────────────────────
app.MapOpenApi();

// ── Vertical slices ───────────────────────────────────────────────────
var configStore = app.Services.GetRequiredService<InMemoryConfigStore>();
var runStore = app.Services.GetRequiredService<InMemoryRunStore>();
var pluginHost = app.Services.GetRequiredService<PluginHost>();

app.MapConfigs();
app.MapValidate();
app.MapEvaluate();
app.MapRuns(configStore, runStore, pluginHost);
app.MapPlugins(pluginHost);

app.Run();
