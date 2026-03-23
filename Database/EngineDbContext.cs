using Bikiran.Engine.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bikiran.Engine.Database;

/// <summary>
/// EF Core database context used exclusively by Bikiran.Engine.
/// Your application's own DbContext is never modified by the engine.
/// </summary>
public class EngineDbContext : DbContext
{
    public EngineDbContext(DbContextOptions<EngineDbContext> options) : base(options) { }

    public DbSet<FlowRun> FlowRun => Set<FlowRun>();
    public DbSet<FlowNodeLog> FlowNodeLog => Set<FlowNodeLog>();
    public DbSet<FlowDefinition> FlowDefinition => Set<FlowDefinition>();
    public DbSet<FlowDefinitionRun> FlowDefinitionRun => Set<FlowDefinitionRun>();
    public DbSet<FlowSchedule> FlowSchedule => Set<FlowSchedule>();
    public DbSet<FlowSchemaVersion> FlowSchemaVersion => Set<FlowSchemaVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Composite unique index on (DefinitionKey, Version)
        modelBuilder.Entity<FlowDefinition>()
            .HasIndex(d => new { d.DefinitionKey, d.Version })
            .IsUnique();

        // Unique index on ScheduleKey
        modelBuilder.Entity<FlowSchedule>()
            .HasIndex(s => s.ScheduleKey)
            .IsUnique();

        // Unique index on ServiceId
        modelBuilder.Entity<FlowRun>()
            .HasIndex(r => r.ServiceId)
            .IsUnique();
    }
}
