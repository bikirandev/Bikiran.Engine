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
    public const string CurrentSchemaVersion = "1.1.0";

    /// <summary>NuGet package version.</summary>
    public const string PackageVersion = "1.1.0";

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
            // Create engine tables if they don't exist.
            // EnsureCreatedAsync() is not used because it skips table creation
            // when the database already contains tables from the host application.
            await CreateTablesIfNotExistAsync();

            var versionRecord = await _db.FlowSchemaVersion.FirstOrDefaultAsync();

            if (versionRecord == null)
            {
                // First-time setup: record the current version
                _db.FlowSchemaVersion.Add(new FlowSchemaVersion
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
    /// Creates all engine tables using CREATE TABLE IF NOT EXISTS.
    /// This is safe for shared databases where EnsureCreatedAsync() would be a no-op.
    /// </summary>
    private async Task CreateTablesIfNotExistAsync()
    {
        var providerName = _db.Database.ProviderName ?? "";
        var isMySql = providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase) ||
                      providerName.Contains("Pomelo", StringComparison.OrdinalIgnoreCase);

        if (isMySql)
        {
            await CreateMySqlTablesAsync();
        }
        else
        {
            // Fallback for non-MySQL providers. Note: EnsureCreatedAsync() is a no-op when
            // the database already contains tables from the host application; engine tables
            // may need to be created manually for shared-database deployments on non-MySQL providers.
            _logger?.LogDebug("Bikiran.Engine: Using EnsureCreatedAsync for non-MySQL provider ({Provider}).", providerName);
            await _db.Database.EnsureCreatedAsync();
        }
    }

    private async Task CreateMySqlTablesAsync()
    {
        await _db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `FlowSchemaVersion` (
                `Id` int NOT NULL,
                `SchemaVersion` varchar(20) NOT NULL,
                `AppliedAt` bigint NOT NULL,
                `PackageVersion` varchar(20) NOT NULL,
                PRIMARY KEY (`Id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        await _db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `FlowRun` (
                `Id` bigint NOT NULL AUTO_INCREMENT,
                `ServiceId` varchar(36) NOT NULL,
                `FlowName` varchar(100) NOT NULL,
                `Status` varchar(20) NOT NULL DEFAULT 'pending',
                `TriggerSource` varchar(100) NOT NULL DEFAULT '',
                `Config` longtext NOT NULL,
                `ContextMeta` longtext NOT NULL,
                `TotalNodes` int NOT NULL DEFAULT 0,
                `CompletedNodes` int NOT NULL DEFAULT 0,
                `ErrorMessage` varchar(500) NULL,
                `StartedAt` bigint NOT NULL DEFAULT 0,
                `CompletedAt` bigint NOT NULL DEFAULT 0,
                `DurationMs` bigint NOT NULL DEFAULT 0,
                `CreatorUserId` bigint NOT NULL DEFAULT 0,
                `IpString` varchar(100) NOT NULL DEFAULT '',
                `TimeCreated` bigint NOT NULL DEFAULT 0,
                `TimeUpdated` bigint NOT NULL DEFAULT 0,
                `TimeDeleted` bigint NOT NULL DEFAULT 0,
                PRIMARY KEY (`Id`),
                UNIQUE INDEX `IX_FlowRun_ServiceId` (`ServiceId`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        await _db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `FlowNodeLog` (
                `Id` bigint NOT NULL AUTO_INCREMENT,
                `ServiceId` varchar(36) NOT NULL,
                `NodeName` varchar(100) NOT NULL,
                `NodeType` varchar(50) NOT NULL,
                `Sequence` int NOT NULL DEFAULT 0,
                `Status` varchar(20) NOT NULL DEFAULT 'pending',
                `InputData` longtext NOT NULL,
                `OutputData` longtext NOT NULL,
                `ErrorMessage` varchar(500) NULL,
                `BranchTaken` varchar(20) NULL,
                `RetryCount` int NOT NULL DEFAULT 0,
                `StartedAt` bigint NOT NULL DEFAULT 0,
                `CompletedAt` bigint NOT NULL DEFAULT 0,
                `DurationMs` bigint NOT NULL DEFAULT 0,
                `TimeCreated` bigint NOT NULL DEFAULT 0,
                `TimeUpdated` bigint NOT NULL DEFAULT 0,
                PRIMARY KEY (`Id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        await _db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `FlowDefinition` (
                `Id` bigint NOT NULL AUTO_INCREMENT,
                `DefinitionKey` varchar(100) NOT NULL,
                `DisplayName` varchar(200) NOT NULL,
                `Description` longtext NOT NULL,
                `Version` int NOT NULL DEFAULT 1,
                `IsActive` tinyint(1) NOT NULL DEFAULT 1,
                `FlowJson` longtext NOT NULL,
                `Tags` varchar(500) NOT NULL DEFAULT '',
                `ParameterSchema` varchar(2000) NULL,
                `LastModifiedBy` bigint NOT NULL DEFAULT 0,
                `TimeCreated` bigint NOT NULL DEFAULT 0,
                `TimeUpdated` bigint NOT NULL DEFAULT 0,
                `TimeDeleted` bigint NOT NULL DEFAULT 0,
                PRIMARY KEY (`Id`),
                UNIQUE INDEX `IX_FlowDefinition_DefinitionKey_Version` (`DefinitionKey`, `Version`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        await _db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `FlowDefinitionRun` (
                `Id` bigint NOT NULL AUTO_INCREMENT,
                `FlowRunServiceId` varchar(36) NOT NULL,
                `DefinitionId` bigint NOT NULL DEFAULT 0,
                `DefinitionKey` varchar(100) NOT NULL,
                `DefinitionVersion` int NOT NULL DEFAULT 0,
                `Parameters` longtext NOT NULL,
                `TriggerUserId` bigint NOT NULL DEFAULT 0,
                `TriggerSource` varchar(100) NOT NULL DEFAULT '',
                `TimeCreated` bigint NOT NULL DEFAULT 0,
                PRIMARY KEY (`Id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        await _db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `FlowSchedule` (
                `Id` bigint NOT NULL AUTO_INCREMENT,
                `ScheduleKey` varchar(100) NOT NULL,
                `DisplayName` varchar(200) NOT NULL,
                `DefinitionKey` varchar(100) NOT NULL,
                `ScheduleType` varchar(20) NOT NULL,
                `CronExpression` varchar(100) NULL,
                `IntervalMinutes` int NULL,
                `RunOnceAt` bigint NULL,
                `DefaultParameters` longtext NOT NULL,
                `IsActive` tinyint(1) NOT NULL DEFAULT 1,
                `TimeZone` varchar(50) NOT NULL DEFAULT 'UTC',
                `MaxConcurrent` int NOT NULL DEFAULT 1,
                `LastRunAt` bigint NOT NULL DEFAULT 0,
                `NextRunAt` bigint NOT NULL DEFAULT 0,
                `LastRunServiceId` varchar(36) NULL,
                `LastRunStatus` varchar(20) NULL,
                `CreatedBy` bigint NOT NULL DEFAULT 0,
                `TimeCreated` bigint NOT NULL DEFAULT 0,
                `TimeUpdated` bigint NOT NULL DEFAULT 0,
                `TimeDeleted` bigint NOT NULL DEFAULT 0,
                PRIMARY KEY (`Id`),
                UNIQUE INDEX `IX_FlowSchedule_ScheduleKey` (`ScheduleKey`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        _logger?.LogInformation("Bikiran.Engine: Ensured all engine tables exist.");
    }

    /// <summary>
    /// Applies incremental migration scripts based on the currently stored schema version.
    /// Add new migration blocks here as the package evolves.
    /// </summary>
    private async Task ApplyMigrationsAsync(FlowSchemaVersion current)
    {
        // Migration: 1.0.x → 1.1.0: Add ParameterSchema column to FlowDefinition
        if (string.Compare(current.SchemaVersion, "1.1.0", StringComparison.Ordinal) < 0)
        {
            var providerName = _db.Database.ProviderName ?? "";
            var isMySql = providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase) ||
                          providerName.Contains("Pomelo", StringComparison.OrdinalIgnoreCase);

            if (isMySql)
            {
                // Check if column already exists to avoid error on re-run
                await _db.Database.ExecuteSqlRawAsync("""
                    SET @col_exists = (
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_NAME = 'FlowDefinition' AND COLUMN_NAME = 'ParameterSchema'
                        AND TABLE_SCHEMA = DATABASE()
                    );
                    SET @sql = IF(@col_exists = 0,
                        'ALTER TABLE `FlowDefinition` ADD COLUMN `ParameterSchema` varchar(2000) NULL AFTER `Tags`',
                        'SELECT 1');
                    PREPARE stmt FROM @sql;
                    EXECUTE stmt;
                    DEALLOCATE PREPARE stmt;
                    """);
            }

            _logger?.LogInformation("Bikiran.Engine: Applied migration to 1.1.0 (ParameterSchema column).");
        }
    }
}
