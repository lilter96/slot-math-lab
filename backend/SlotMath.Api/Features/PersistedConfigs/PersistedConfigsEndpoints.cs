using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SlotMath.Api.Infrastructure;
using SlotMath.Api.Persistence;

namespace SlotMath.Api.Features.PersistedConfigs;

/// <summary>
/// Persistence-backed config endpoints (G16 + G29):
/// - Anonymous: read (cache) only
/// - Authenticated: save / load own configs with ownership enforcement
/// </summary>
public static class PersistedConfigsEndpoints
{
    public static RouteGroupBuilder MapPersistedConfigs(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/persisted");

        // ── Save new config version (requires auth) ──────────────────────
        group.MapPost("/{projectId}/configs", [Authorize] async (
            string projectId,
            object config,
            HttpContext ctx,
            ConfigPersistenceService service,
            SlotMathDbContext db) =>
        {
            var userId = JwtAuth.GetUserId(ctx.User);
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            // Ownership: if the project already exists, only its owner can save
            var project = await db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == projectId);
            if (project is not null
                && project.OwnerId is not null
                && project.OwnerId != userId)
            {
                return Results.Problem(
                    detail: $"Project '{projectId}' belongs to another user.",
                    statusCode: 403,
                    title: "Forbidden");
            }

            try
            {
                var entity = await service.SaveConfigAsync(projectId, config, ownerId: userId);
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

        // ── Load latest config version (requires auth) ───────────────────
        group.MapGet("/{projectId}/configs/latest", [Authorize] async (
            string projectId,
            HttpContext ctx,
            ConfigPersistenceService service,
            SlotMathDbContext db) =>
        {
            var userId = JwtAuth.GetUserId(ctx.User);
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var project = await db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == projectId);
            if (project is not null
                && project.OwnerId is not null
                && project.OwnerId != userId)
            {
                return Results.Problem(
                    detail: $"Project '{projectId}' belongs to another user.",
                    statusCode: 403,
                    title: "Forbidden");
            }

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

        // ── Load specific version (requires auth) ────────────────────────
        group.MapGet("/{projectId}/configs/{version:int}", [Authorize] async (
            string projectId,
            int version,
            HttpContext ctx,
            ConfigPersistenceService service,
            SlotMathDbContext db) =>
        {
            var userId = JwtAuth.GetUserId(ctx.User);
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var project = await db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == projectId);
            if (project is not null
                && project.OwnerId is not null
                && project.OwnerId != userId)
            {
                return Results.Problem(
                    detail: $"Project '{projectId}' belongs to another user.",
                    statusCode: 403,
                    title: "Forbidden");
            }

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

        // ── Load version history (requires auth) ─────────────────────────
        group.MapGet("/{projectId}/configs", [Authorize] async (
            string projectId,
            HttpContext ctx,
            ConfigPersistenceService service,
            SlotMathDbContext db) =>
        {
            var userId = JwtAuth.GetUserId(ctx.User);
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var project = await db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == projectId);
            if (project is not null
                && project.OwnerId is not null
                && project.OwnerId != userId)
            {
                return Results.Problem(
                    detail: $"Project '{projectId}' belongs to another user.",
                    statusCode: 403,
                    title: "Forbidden");
            }

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

        // ── Cache a result (anonymous OK) ────────────────────────────────
        group.MapPost("/cache/{configHash}", async (
            string configHash,
            object result,
            System.Security.Claims.ClaimsPrincipal user,
            ConfigPersistenceService service) =>
        {
            if (!JwtAuth.IsAuthenticated(user)) return Results.Unauthorized();
            var json = JsonSerializer.Serialize(result);
            await service.CacheResultAsync(configHash, json);
            return Results.Ok(new { cached = true, hash = configHash });
        });

        // ── Get cached result (anonymous OK) ─────────────────────────────
        group.MapGet("/cache/{configHash}", async (
            string configHash,
            ConfigPersistenceService service) =>
        {
            var result = await service.GetCachedResultAsync(configHash);
            if (result is null)
                return Results.NotFound(new { error = "Cache miss.", hash = configHash });

            return Results.Ok(new { hit = true, hash = configHash, result });
        });

        // ── Get recompute counter (anonymous OK) ─────────────────────────
        group.MapGet("/cache/{configHash}/recompute-count", (
            string configHash,
            ConfigPersistenceService service) =>
        {
            return Results.Ok(new { hash = configHash, recomputeCount = service.RecomputeCount });
        });

        return group;
    }
}
