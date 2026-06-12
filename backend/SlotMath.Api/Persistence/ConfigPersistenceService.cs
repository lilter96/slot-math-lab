using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SlotMath.Api.Infrastructure;
using SlotMath.Core;

namespace SlotMath.Api.Persistence;

/// <summary>
/// Persistence-backed service for saving, loading, and versioning configs.
/// </summary>
public class ConfigPersistenceService
{
    private readonly SlotMathDbContext _db;
    private readonly IResultCache _cache;

    public ConfigPersistenceService(SlotMathDbContext db, IResultCache cache)
    {
        _db = db;
        _cache = cache;
    }

    /// <summary>
    /// Save a new config version under a project. Returns the created entity.
    /// </summary>
    public async Task<ConfigVersionEntity> SaveConfigAsync(
        string projectId, object config, string? projectName = null, string? ownerId = null)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions.Default);
        var hash = CanonicalHash.Compute(config);

        // Ensure the project exists
        var project = await _db.Projects.FindAsync(projectId);
        if (project is null)
        {
            project = new SlotMathProject
            {
                Id = projectId,
                Name = projectName ?? projectId,
                OwnerId = ownerId,
            };
            _db.Projects.Add(project);
        }
        else
        {
            project.UpdatedAt = DateTimeOffset.UtcNow;
            // Set owner on first authenticated save if not already set
            if (project.OwnerId is null && ownerId is not null)
                project.OwnerId = ownerId;
        }

        // Determine the next version number
        var lastVersion = await _db.ConfigVersions
            .Where(c => c.ProjectId == projectId)
            .MaxAsync(c => (int?)c.Version) ?? 0;

        var entity = new ConfigVersionEntity
        {
            Id = $"{projectId}/v{lastVersion + 1}",
            ProjectId = projectId,
            Version = lastVersion + 1,
            ConfigJson = json,
            ConfigHash = hash,
            SchemaVersion = "1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.ConfigVersions.Add(entity);
        await _db.SaveChangesAsync();
        return entity;
    }

    /// <summary>
    /// Load the latest config version for a project.
    /// </summary>
    public async Task<ConfigVersionEntity?> LoadLatestAsync(string projectId)
    {
        return await _db.ConfigVersions
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.Version)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Load a specific config version.
    /// </summary>
    public async Task<ConfigVersionEntity?> LoadVersionAsync(string projectId, int version)
    {
        return await _db.ConfigVersions
            .Where(c => c.ProjectId == projectId && c.Version == version)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Load ALL config versions for a project (history).
    /// </summary>
    public async Task<IReadOnlyList<ConfigVersionEntity>> LoadHistoryAsync(string projectId)
    {
        return await _db.ConfigVersions
            .Where(c => c.ProjectId == projectId)
            .OrderBy(c => c.Version)
            .ToListAsync();
    }

    /// <summary>
    /// Try to get a cached evaluation result for a config hash.
    /// </summary>
    public async Task<string?> GetCachedResultAsync(string configHash)
    {
        return await _cache.GetAsync(configHash);
    }

    /// <summary>
    /// Store a result in the cache keyed by config hash.
    /// </summary>
    public async Task CacheResultAsync(string configHash, string resultJson)
    {
        await _cache.SetAsync(configHash, resultJson);
    }

    /// <summary>
    /// Get the recompute counter (for testing cache behavior).
    /// </summary>
    public long RecomputeCount => _cache.RecomputeCount;
}
