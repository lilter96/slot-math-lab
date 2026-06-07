using SlotMath.Api.Infrastructure;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Plugins;

namespace SlotMath.Api.Features.Plugins;

/// <summary>
/// Plugin registry endpoints — list, get, register plugins.
/// </summary>
public static class PluginsEndpoints
{
    public static RouteGroupBuilder MapPlugins(this IEndpointRouteBuilder app, PluginHost pluginHost)
    {
        var group = app.MapGroup("/api/plugins");

        group.MapGet("/", () =>
        {
            var plugins = pluginHost.PluginIds.Select(id =>
            {
                var entry = pluginHost.TryGetEntry(id);
                return new PluginEntryResponse
                {
                    PluginId = id,
                    Contract = "IEvaluator",
                    IsConformant = entry?.IsConformant ?? false,
                    Version = "1.0.0",
                };
            }).ToList();

            return Results.Ok(plugins);
        });

        group.MapGet("/{id}", (string id) =>
        {
            var entry = pluginHost.TryGetEntry(id);
            if (entry is null)
                return Results.NotFound(new { error = $"Plugin '{id}' not found." });

            return Results.Ok(new PluginEntryResponse
            {
                PluginId = entry.PluginId,
                Contract = "IEvaluator",
                IsConformant = entry.IsConformant,
                Version = "1.0.0",
            });
        });

        group.MapPost("/", (RegisterPluginRequest request) =>
        {
            if (pluginHost.TryGetEntry(request.PluginId) is not null)
                return Results.Conflict(new { error = $"Plugin '{request.PluginId}' is already registered." });

            // In a real system, this would load the plugin assembly.
            // For G15, we require pre-registered plugins via DI.
            return Results.BadRequest(new { error = "Plugin registration requires assembly loading. Pre-register plugins via DI configuration." });
        });

        return group;
    }
}
