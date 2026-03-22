using Bikiran.Engine.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bikiran.Engine.Database.Migration;

/// <summary>
/// Handles automatic database schema creation and incremental migration on startup.
/// The engine manages its own tables — your application's DbContext is never modified.
/// </summary>
public class SchemaMigrator
{
    /// <summary>Current schema version embedded in this package version.</summary>
    public const string CurrentSchemaVersion = "1.0.0";

    /// <summary>NuGet package version.</summary>
    public const string PackageVersion = "1.0.0";

    private readonly EngineDbContext _db;
    private readonly ILogger<SchemaMigrator>? _logger;

    public SchemaMigrator(EngineDbContext db, ILogger<SchemaMigrator>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Checks the current schema version and applies any outstanding migrations.
    /// Called automatically on application startup.
    /// </summary>
    public async Task MigrateAsync()
    {
        try
        {
            // Ensure all engine tables exist (EF Core handles creation via EnsureCreated equivalent)
            await _db.Database.EnsureCreatedAsync();

            var versionRecord = await _db.EngineSchemaVersion.FirstOrDefaultAsync();

            if (versionRecord == null)
            {
                // First-time setup: record the current version
                _db.EngineSchemaVersion.Add(new EngineSchemaVersion
                {
                    Id = 1,
                    SchemaVersion = CurrentSchemaVersion,
                    PackageVersion = PackageVersion,
                    AppliedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
                await _db.SaveChangesAsync();
                _logger?.LogInformation(
                    "Bikiran.Engine: Database initialized at schema version {Version}.", CurrentSchemaVersion);
                return;
            }

            if (versionRecord.SchemaVersion == CurrentSchemaVersion)
            {
                _logger?.LogDebug(
                    "Bikiran.Engine: Schema is up to date (version {Version}).", CurrentSchemaVersion);
                return;
            }

            // Apply incremental migrations based on the stored version
            await ApplyMigrationsAsync(versionRecord);

            versionRecord.SchemaVersion = CurrentSchemaVersion;
            versionRecord.PackageVersion = PackageVersion;
            versionRecord.AppliedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await _db.SaveChangesAsync();

            _logger?.LogInformation(
                "Bikiran.Engine: Schema migrated to version {Version}.", CurrentSchemaVersion);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Bikiran.Engine: Schema migration failed.");
            throw;
        }
    }

    /// <summary>
    /// Applies incremental migration scripts based on the currently stored schema version.
    /// Add new migration blocks here as the package evolves.
    /// </summary>
    private Task ApplyMigrationsAsync(EngineSchemaVersion current)
    {
        // Example future migration block:
        // if (string.Compare(current.SchemaVersion, "1.1.0", StringComparison.Ordinal) < 0)
        //     await _db.Database.ExecuteSqlRawAsync("ALTER TABLE FlowRun ADD COLUMN ...");

        // No additional migrations needed for version 1.0.0
        return Task.CompletedTask;
    }
}
