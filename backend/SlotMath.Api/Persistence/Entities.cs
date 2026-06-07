using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SlotMath.Api.Persistence;

// ═══════════════════════════════════════════════════════════════════════════
//  EF Core entity models for G16 persistence
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// A project groups related config versions together.
/// </summary>
public class SlotMathProject
{
    [Key]
    public required string Id { get; set; }

    [MaxLength(256)]
    public string Name { get; set; } = "";

    [MaxLength(1024)]
    public string? Description { get; set; }

    /// <summary>Owner user ID (null for anonymous).</summary>
    [MaxLength(128)]
    public string? OwnerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ConfigVersionEntity> ConfigVersions { get; set; } = new List<ConfigVersionEntity>();
}

/// <summary>
/// A single version of a slot math config, keyed by (ProjectId, Version).
/// </summary>
public class ConfigVersionEntity
{
    [Key]
    public required string Id { get; set; }

    /// <summary>FK to the owning project.</summary>
    [MaxLength(128)]
    public required string ProjectId { get; set; }

    /// <summary>Monotonic version number within the project (1-based).</summary>
    public int Version { get; set; }

    /// <summary>Full JSON serialization of the GraphConfig.</summary>
    public required string ConfigJson { get; set; }

    /// <summary>Canonical hash of the config for cache lookups.</summary>
    [MaxLength(64)]
    public required string ConfigHash { get; set; }

    /// <summary>JSON Schema version at time of save.</summary>
    [MaxLength(16)]
    public string SchemaVersion { get; set; } = "1.0.0";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [ForeignKey(nameof(ProjectId))]
    public SlotMathProject? Project { get; set; }

    public ICollection<RunEntity> Runs { get; set; } = new List<RunEntity>();
}

/// <summary>
/// A heavy evaluation run record.
/// </summary>
public class RunEntity
{
    [Key]
    public required string Id { get; set; }

    /// <summary>FK to the config version used for this run.</summary>
    [MaxLength(128)]
    public required string ConfigVersionId { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; } // pending, running, completed, failed

    public int SampleSize { get; set; }
    public string? ResultJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    [ForeignKey(nameof(ConfigVersionId))]
    public ConfigVersionEntity? ConfigVersion { get; set; }
}

/// <summary>
/// A registered user (OAuth-backed).
/// </summary>
public class UserEntity
{
    [Key]
    public required string Id { get; set; }

    [MaxLength(256)]
    public string? ExternalId { get; set; }

    [MaxLength(256)]
    public string? Name { get; set; }

    [MaxLength(256)]
    public string? Email { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A registered plugin with its conformance status.
/// </summary>
public class PluginEntity
{
    [Key]
    public required string Id { get; set; }

    [MaxLength(128)]
    public required string Name { get; set; }

    [MaxLength(32)]
    public required string Contract { get; set; } // IEvaluator, ITransform, WeightSource

    [MaxLength(1024)]
    public string? AssemblyPath { get; set; }

    public bool IsApproved { get; set; }
    public bool IsConformant { get; set; }
    public string? ConformanceResultJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
