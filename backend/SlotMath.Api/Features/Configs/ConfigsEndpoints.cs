using System.Text.Json;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Model;

namespace SlotMath.Api.Features.Configs;

/// <summary>
/// Config CRUD + versioning endpoints (G15 vertical slice).
/// </summary>
public static class ConfigsEndpoints
{
    public static RouteGroupBuilder MapConfigs(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/configs");

        group.MapGet("/", (InMemoryConfigStore store) =>
        {
            var configs = store.List();
            return Results.Ok(configs.Select(c => new ConfigListResponse
            {
                Id = c.Id,
                Name = c.Config.Name ?? c.Id,
                LatestVersion = c.Version,
                UpdatedAt = c.CreatedAt,
            }));
        });

        group.MapGet("/{id}", (string id, InMemoryConfigStore store) =>
        {
            var entry = store.GetLatest(id);
            if (entry is null)
                return Results.NotFound(new { error = $"Config '{id}' not found." });

            return Results.Ok(new ConfigDetailResponse
            {
                Id = entry.Id,
                Version = entry.Version,
                Config = entry.Config,
                CreatedAt = entry.CreatedAt,
            });
        });

        group.MapPost("/", (CreateConfigRequest request, InMemoryConfigStore store) =>
        {
            GraphConfig config;
            try
            {
                config = DeserializeConfig(request.Config);
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new { error = $"Invalid config JSON: {ex.Message}" });
            }

            var id = store.Create(config);
            return Results.Created($"/api/configs/{id}", new CreateConfigResponse
            {
                Id = id,
                Version = 1,
            });
        });

        group.MapPut("/{id}", (string id, UpdateConfigRequest request, InMemoryConfigStore store) =>
        {
            GraphConfig config;
            try
            {
                config = DeserializeConfig(request.Config);
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new { error = $"Invalid config JSON: {ex.Message}" });
            }

            try
            {
                store.Update(id, config);
                var latest = store.GetLatest(id)!;
                return Results.Ok(new CreateConfigResponse
                {
                    Id = id,
                    Version = latest.Version,
                });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = $"Config '{id}' not found." });
            }
        });

        group.MapGet("/{id}/versions", (string id, InMemoryConfigStore store) =>
        {
            var versions = store.GetVersions(id);
            if (versions.Count == 0)
                return Results.NotFound(new { error = $"Config '{id}' not found." });

            return Results.Ok(versions.Select(v => new
            {
                v.Id,
                v.Version,
                v.CreatedAt,
            }));
        });

        group.MapGet("/{id}/versions/{version:int}", (string id, int version, InMemoryConfigStore store) =>
        {
            var entry = store.GetVersion(id, version);
            if (entry is null)
                return Results.NotFound(new { error = $"Config '{id}' version {version} not found." });

            return Results.Ok(new ConfigDetailResponse
            {
                Id = entry.Id,
                Version = entry.Version,
                Config = entry.Config,
                CreatedAt = entry.CreatedAt,
            });
        });

        group.MapDelete("/{id}", (string id, InMemoryConfigStore store) =>
        {
            if (!store.Delete(id))
                return Results.NotFound(new { error = $"Config '{id}' not found." });
            return Results.NoContent();
        });

        return group;
    }

    internal static GraphConfig DeserializeConfig(object config)
    {
        var json = JsonSerializer.Serialize(config, SlotMath.Core.JsonOptions.Default);
        return JsonSerializer.Deserialize<GraphConfig>(json, SlotMath.Core.JsonOptions.Default)!;
    }
}
