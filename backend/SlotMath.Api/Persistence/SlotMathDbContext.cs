using Microsoft.EntityFrameworkCore;

namespace SlotMath.Api.Persistence;

/// <summary>
/// EF Core 10 DbContext for the Slot Math Lab.
/// Maps: Projects, ConfigVersions, Runs, Users, Plugins.
/// </summary>
public class SlotMathDbContext : DbContext
{
    public SlotMathDbContext(DbContextOptions<SlotMathDbContext> options)
        : base(options) { }

    public DbSet<SlotMathProject> Projects => Set<SlotMathProject>();
    public DbSet<ConfigVersionEntity> ConfigVersions => Set<ConfigVersionEntity>();
    public DbSet<RunEntity> Runs => Set<RunEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<PluginEntity> Plugins => Set<PluginEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SlotMathProject>(e =>
        {
            e.ToTable("Projects");
            e.HasIndex(p => p.OwnerId);
        });

        modelBuilder.Entity<ConfigVersionEntity>(e =>
        {
            e.ToTable("ConfigVersions");
            e.HasIndex(c => new { c.ProjectId, c.Version }).IsUnique();
            e.HasIndex(c => c.ConfigHash);
            e.HasOne(c => c.Project)
                .WithMany(p => p.ConfigVersions)
                .HasForeignKey(c => c.ProjectId);
        });

        modelBuilder.Entity<RunEntity>(e =>
        {
            e.ToTable("Runs");
            e.HasIndex(r => r.ConfigVersionId);
            e.HasIndex(r => r.Status);
            e.HasOne(r => r.ConfigVersion)
                .WithMany(c => c.Runs)
                .HasForeignKey(r => r.ConfigVersionId);
        });

        modelBuilder.Entity<UserEntity>(e =>
        {
            e.ToTable("Users");
            e.HasIndex(u => u.ExternalId).IsUnique();
        });

        modelBuilder.Entity<PluginEntity>(e =>
        {
            e.ToTable("Plugins");
            e.HasIndex(p => p.Name).IsUnique();
        });
    }
}
