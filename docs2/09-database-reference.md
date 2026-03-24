# Database Reference

Bikiran.Engine manages its own database tables. Your application's DbContext is never modified. The engine uses a separate `EngineDbContext` with six tables.

---

## Auto-Migration

On application startup, the engine automatically creates its tables and applies migrations:

1. **MySQL/MariaDB** — uses `CREATE TABLE IF NOT EXISTS` statements. Safe for shared databases.
2. **Other providers** (SQL Server, PostgreSQL, SQLite) — uses `EnsureCreatedAsync()` as a fallback. This is a no-op when the database already has tables, so engine tables may need manual creation for shared-database deployments.

The `FlowSchemaVersion` table tracks which schema version is deployed. When you update the NuGet package, the migrator compares versions and applies incremental changes.

---

## Tables

### FlowRun

Tracks each workflow execution from start to finish.

| Column           | Type         | Constraints                   | Description                                                       |
| ---------------- | ------------ | ----------------------------- | ----------------------------------------------------------------- |
| `Id`             | bigint       | PK, auto-increment            | Row ID                                                            |
| `ServiceId`      | varchar(36)  | Required, unique index        | UUID identifying this run                                         |
| `FlowName`       | varchar(100) | Required                      | Name of the flow                                                  |
| `Status`         | varchar(20)  | Required, default `"pending"` | `pending`, `running`, `completed`, `failed`, `cancelled`          |
| `TriggerSource`  | varchar(100) | Default `""`                  | Origin of the trigger (e.g., controller name, `FlowSchedule:key`) |
| `Config`         | longtext     | Required                      | JSON-serialized FlowConfig                                        |
| `ContextMeta`    | longtext     | Required                      | JSON snapshot of caller context (IP, user ID, path)               |
| `TotalNodes`     | int          |                               | Total nodes in the flow                                           |
| `CompletedNodes` | int          |                               | Nodes completed so far                                            |
| `ErrorMessage`   | varchar(500) | Nullable                      | Error details if the run failed                                   |
| `StartedAt`      | bigint       |                               | Unix timestamp when execution started                             |
| `CompletedAt`    | bigint       |                               | Unix timestamp when execution finished                            |
| `DurationMs`     | bigint       |                               | Total execution time in milliseconds                              |
| `CreatorUserId`  | bigint       |                               | ID of the user who triggered the run                              |
| `IpString`       | varchar(100) | Default `""`                  | IP address of the caller                                          |
| `TimeCreated`    | bigint       |                               | Record creation timestamp                                         |
| `TimeUpdated`    | bigint       |                               | Last update timestamp                                             |
| `TimeDeleted`    | bigint       |                               | Soft-delete timestamp (0 = active)                                |

**Indexes:**

- Unique on `ServiceId`

---

### FlowNodeLog

Records execution details for one node within a flow run.

| Column         | Type         | Constraints                   | Description                                            |
| -------------- | ------------ | ----------------------------- | ------------------------------------------------------ |
| `Id`           | bigint       | PK, auto-increment            | Row ID                                                 |
| `ServiceId`    | varchar(36)  | Required                      | FK to FlowRun.ServiceId                                |
| `NodeName`     | varchar(100) | Required                      | Name of the node                                       |
| `NodeType`     | varchar(50)  | Required                      | Type identifier (e.g., `HttpRequest`, `EmailSend`)     |
| `Sequence`     | int          |                               | Execution order within the run                         |
| `Status`       | varchar(20)  | Required, default `"pending"` | `pending`, `running`, `completed`, `failed`, `skipped` |
| `InputData`    | longtext     | Default `"{}"`                | JSON input passed to the node                          |
| `OutputData`   | longtext     | Default `"{}"`                | JSON output from the node                              |
| `ErrorMessage` | varchar(500) | Nullable                      | Error details if the node failed                       |
| `BranchTaken`  | varchar(20)  | Nullable                      | Branch name for conditional nodes (`true`/`false`)     |
| `RetryCount`   | int          |                               | Number of retries executed                             |
| `StartedAt`    | bigint       |                               | Unix timestamp when node started                       |
| `CompletedAt`  | bigint       |                               | Unix timestamp when node finished                      |
| `DurationMs`   | bigint       |                               | Execution time in milliseconds                         |
| `TimeCreated`  | bigint       |                               | Record creation timestamp                              |
| `TimeUpdated`  | bigint       |                               | Last update timestamp                                  |

---

### FlowDefinition

Stores reusable flow templates as JSON. Supports multiple versions per key.

| Column            | Type          | Constraints        | Description                                |
| ----------------- | ------------- | ------------------ | ------------------------------------------ |
| `Id`              | bigint        | PK, auto-increment | Row ID                                     |
| `DefinitionKey`   | varchar(100)  | Required           | Unique slug (e.g., `"order_notification"`) |
| `DisplayName`     | varchar(200)  | Required           | Human-readable name                        |
| `Description`     | text          |                    | What this flow does                        |
| `Version`         | int           | Default `1`        | Version number                             |
| `IsActive`        | bool          | Default `true`     | Whether this version can be triggered      |
| `FlowJson`        | longtext      | Required           | The flow definition JSON                   |
| `Tags`            | varchar(500)  | Default `""`       | Comma-separated tags                       |
| `ParameterSchema` | varchar(2000) | Nullable           | JSON schema for runtime parameters         |
| `LastModifiedBy`  | bigint        |                    | User who last modified this record         |
| `TimeCreated`     | bigint        |                    | Record creation timestamp                  |
| `TimeUpdated`     | bigint        |                    | Last update timestamp                      |
| `TimeDeleted`     | bigint        |                    | Soft-delete timestamp (0 = active)         |

**Indexes:**

- Unique composite on (`DefinitionKey`, `Version`)

---

### FlowDefinitionRun

Links a flow run to the definition and parameters that triggered it.

| Column              | Type         | Constraints        | Description                            |
| ------------------- | ------------ | ------------------ | -------------------------------------- |
| `Id`                | bigint       | PK, auto-increment | Row ID                                 |
| `FlowRunServiceId`  | varchar(36)  | Required           | FK to FlowRun.ServiceId                |
| `DefinitionId`      | bigint       |                    | FK to FlowDefinition.Id                |
| `DefinitionKey`     | varchar(100) | Required           | Definition key at time of trigger      |
| `DefinitionVersion` | int          |                    | Version used for this run              |
| `Parameters`        | text         | Default `"{}"`     | JSON parameters passed at trigger time |
| `TriggerUserId`     | bigint       |                    | User who triggered the run             |
| `TriggerSource`     | varchar(100) | Default `""`       | Where the trigger originated           |
| `TimeCreated`       | bigint       |                    | Record creation timestamp              |

---

### FlowSchedule

Defines an automated trigger for a flow definition.

| Column              | Type         | Constraints              | Description                            |
| ------------------- | ------------ | ------------------------ | -------------------------------------- |
| `Id`                | bigint       | PK, auto-increment       | Row ID                                 |
| `ScheduleKey`       | varchar(100) | Required, unique index   | Unique identifier for the schedule     |
| `DisplayName`       | varchar(200) | Required                 | Human-readable name                    |
| `DefinitionKey`     | varchar(100) | Required                 | Flow definition to trigger             |
| `ScheduleType`      | varchar(20)  | Required                 | `cron`, `interval`, or `once`          |
| `CronExpression`    | varchar(100) | Nullable                 | Quartz 6-field cron (for cron type)    |
| `IntervalMinutes`   | int          | Nullable                 | Repeat interval (for interval type)    |
| `RunOnceAt`         | bigint       | Nullable                 | Unix timestamp (for once type)         |
| `DefaultParameters` | text         | Required, default `"{}"` | JSON parameters passed on each trigger |
| `IsActive`          | bool         | Default `true`           | Whether the schedule is enabled        |
| `TimeZone`          | varchar(50)  | Default `"UTC"`          | IANA timezone ID                       |
| `MaxConcurrent`     | int          | Default `1`              | Max concurrent runs                    |
| `LastRunAt`         | bigint       |                          | When the schedule last fired           |
| `NextRunAt`         | bigint       |                          | Next scheduled fire time               |
| `LastRunServiceId`  | varchar(36)  | Nullable                 | ServiceId of the most recent run       |
| `LastRunStatus`     | varchar(20)  | Nullable                 | Status of the most recent run          |
| `CreatedBy`         | bigint       |                          | User who created the schedule          |
| `TimeCreated`       | bigint       |                          | Record creation timestamp              |
| `TimeUpdated`       | bigint       |                          | Last update timestamp                  |
| `TimeDeleted`       | bigint       |                          | Soft-delete timestamp (0 = active)     |

**Indexes:**

- Unique on `ScheduleKey`

---

### FlowSchemaVersion

Single-row table tracking the current database schema version. Always contains exactly one row with `Id = 1`.

| Column           | Type        | Constraints    | Description                  |
| ---------------- | ----------- | -------------- | ---------------------------- |
| `Id`             | int         | PK, always `1` | Fixed row ID                 |
| `SchemaVersion`  | varchar(20) | Required       | Current schema version       |
| `AppliedAt`      | bigint      |                | When the version was applied |
| `PackageVersion` | varchar(20) | Required       | NuGet package version        |

---

## Soft Deletes

The engine uses soft deletes on `FlowRun`, `FlowDefinition`, and `FlowSchedule`. Records are never physically removed. A `TimeDeleted` value of `0` means the record is active. Any nonzero value is a Unix timestamp indicating when it was deleted.

All API queries automatically filter out soft-deleted records.

---

## Timestamps

All timestamp columns store **Unix timestamps in seconds** (`DateTimeOffset.UtcNow.ToUnixTimeSeconds()`). This applies to:

- `TimeCreated`, `TimeUpdated`, `TimeDeleted`
- `StartedAt`, `CompletedAt`
- `AppliedAt`, `LastRunAt`, `NextRunAt`, `RunOnceAt`

---

## EF Core Configuration

The `EngineDbContext` configures these indexes in `OnModelCreating`:

```csharp
// Composite unique index
modelBuilder.Entity<FlowDefinition>()
    .HasIndex(d => new { d.DefinitionKey, d.Version })
    .IsUnique();

// Unique index
modelBuilder.Entity<FlowSchedule>()
    .HasIndex(s => s.ScheduleKey)
    .IsUnique();

// Unique index
modelBuilder.Entity<FlowRun>()
    .HasIndex(r => r.ServiceId)
    .IsUnique();
```

The engine registers its own `EngineDbContext` separately from your application's DbContext. Both can point to the same database — the engine only touches its own tables.
