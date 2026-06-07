using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SlotMath.Api.Infrastructure;
using SlotMath.Api.Persistence;

namespace SlotMath.Api.Features.PersistedConfigs;

/// <summary>
/// Persistence-backed config endpoints (G16):
/// save → load → re-save with version history and result cache.
/// </summary>
public static class PersistedConfigsEndpoints
{
    public static RouteGroupBuilder MapPersistedConfigs(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/persisted");

        // ── Save new config version ──────────────────────────────────────
        group.MapPost("/{projectId}/configs", async (
            string projectId,
            object config,
            ConfigPersistenceService service) =>
        {
            try
            {
                var entity = await service.SaveConfigAsync(projectId, config);
                return Results.Created(
                    $"/api/persisted/{projectId}/configs/{entity.Version}",
                    new
                    {
                        entity.Id,
                        entity.ProjectId,
                        entity.Version,
                        entity.ConfigHash,
                        entity.CreatedAt,
                    });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // ── Load latest config version ───────────────────────────────────
        group.MapGet("/{projectId}/configs/latest", async (
            string projectId,
            ConfigPersistenceService service) =>
        {
            var entity = await service.LoadLatestAsync(projectId);
            if (entity is null)
                return Results.NotFound(new { error = $"Project '{projectId}' has no configs." });

            return Results.Ok(new
            {
                entity.Id,
                entity.ProjectId,
                entity.Version,
                entity.ConfigHash,
                entity.ConfigJson,
                entity.CreatedAt,
            });
        });

        // ── Load specific version ────────────────────────────────────────
        group.MapGet("/{projectId}/configs/{version:int}", async (
            string projectId,
            int version,
            ConfigPersistenceService service) =>
        {
            var entity = await service.LoadVersionAsync(projectId, version);
            if (entity is null)
                return Results.NotFound(new { error = $"Config '{projectId}' version {version} not found." });

            return Results.Ok(new
            {
                entity.Id,
                entity.ProjectId,
                entity.Version,
                entity.ConfigHash,
                entity.ConfigJson,
                entity.CreatedAt,
            });
        });

        // ── Load version history ─────────────────────────────────────────
        group.MapGet("/{projectId}/configs", async (
            string projectId,
            ConfigPersistenceService service) =>
        {
            var history = await service.LoadHistoryAsync(projectId);
            if (history.Count == 0)
                return Results.NotFound(new { error = $"Project '{projectId}' has no configs." });

            return Results.Ok(history.Select(e => new
            {
                e.Id,
                e.ProjectId,
                e.Version,
                e.ConfigHash,
                e.CreatedAt,
            }));
        });

        // ── Cache a result ───────────────────────────────────────────────
        group.MapPost("/cache/{configHash}", async (
            string configHash,
            object result,
            ConfigPersistenceService service) =>
        {
            var json = JsonSerializer.Serialize(result);
            await service.CacheResultAsync(configHash, json);
            return Results.Ok(new { cached = true, hash = configHash });
        });

        // ── Get cached result ────────────────────────────────────────────
        group.MapGet("/cache/{configHash}", async (
            string configHash,
            ConfigPersistenceService service) =>
        {
            var result = await service.GetCachedResultAsync(configHash);
            if (result is null)
                return Results.NotFound(new { error = "Cache miss.", hash = configHash });

            return Results.Ok(new { hit = true, hash = configHash, result });
        });

        // ── Get recompute counter ────────────────────────────────────────
        group.MapGet("/cache/{configHash}/recompute-count", (
            string configHash,
            ConfigPersistenceService service) =>
        {
            return Results.Ok(new { hash = configHash, recomputeCount = service.RecomputeCount });
        });

        return group;
    }
}
