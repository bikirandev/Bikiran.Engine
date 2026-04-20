# Database Reference

Bikiran.Engine manages its own database tables independently from your application. The engine uses an internal `EngineDbContext` — your application's database context is never modified.

All tables are created automatically on first startup and updated automatically when the NuGet package is updated.

---

## Tables Overview

| Table               | Purpose                                         |
| ------------------- | ----------------------------------------------- |
| `FlowRun`           | Tracks each workflow execution                  |
| `FlowNodeLog`       | Records each step's execution within a run      |
| `FlowDefinition`    | Stores reusable flow templates as JSON          |
| `FlowDefinitionRun` | Links a run to the definition that triggered it |
| `FlowSchedule`      | Defines automated triggers for flow definitions |
| `FlowSchemaVersion` | Tracks the current database schema version      |

---

## FlowRun

Stores one record per workflow execution.

| Column                   | Type         | Default     | Description                                                 |
| ------------------------ | ------------ | ----------- | ----------------------------------------------------------- |
| `Id`                     | BIGINT (PK)  | auto        | Primary key                                                 |
| `ServiceId`              | CHAR(36)     | —           | Unique run identifier (UUID)                                |
| `FlowName`               | VARCHAR(100) | —           | Flow name                                                   |
| `Status`                 | VARCHAR(20)  | `"pending"` | `pending`, `running`, `completed`, `failed`, or `cancelled` |
| `TriggerSource`          | VARCHAR(100) | `""`        | Where the flow was triggered from                           |
| `Config`                 | TEXT         | `"{}"`      | JSON-serialized runtime configuration                       |
| `ContextMeta`            | TEXT         | `"{}"`      | JSON snapshot of caller context (IP, user ID, path)         |
| `TotalNodes`             | INT          | `0`         | Total number of main steps (excludes lifecycle events)      |
| `CompletedNodes`         | INT          | `0`         | Number of main steps completed so far                       |
| `TotalApproxMs`          | BIGINT       | `0`         | Sum of all main nodes' `ApproxExecutionTime` in ms          |
| `CompletedApproxMs`      | BIGINT       | `0`         | Rolling sum of completed nodes' approx times in ms          |
| `CurrentNodeApproxMs`    | BIGINT       | `0`         | Approx time of the currently executing node in ms (0 idle)  |
| `CurrentNodeStartedAtMs` | BIGINT       | `0`         | UTC ms timestamp when the current node started (0 idle)     |
| `ErrorMessage`           | VARCHAR(500) | NULL        | Error details if the flow failed                            |
| `CurrentProgressMessage` | VARCHAR(500) | NULL        | Progress message from the currently executing node          |
| `StartedAt`              | BIGINT       | `0`         | Unix timestamp when execution began                         |
| `CompletedAt`            | BIGINT       | `0`         | Unix timestamp when execution finished                      |
| `DurationMs`             | BIGINT       | `0`         | Total execution time in milliseconds                        |
| `CreatorUserId`          | BIGINT       | `0`         | User who triggered the flow                                 |
| `IpString`               | VARCHAR(100) | `""`        | IP address of the triggering request                        |
| `TimeCreated`            | BIGINT       | —           | Record creation timestamp                                   |
| `TimeUpdated`            | BIGINT       | —           | Last update timestamp                                       |
| `TimeDeleted`            | BIGINT       | `0`         | Soft-delete timestamp (0 = active)                          |

---

## FlowNodeLog

Stores one record per step execution within a run.

| Column         | Type         | Default     | Description                                               |
| -------------- | ------------ | ----------- | --------------------------------------------------------- |
| `Id`           | BIGINT (PK)  | auto        | Primary key                                               |
| `ServiceId`    | CHAR(36)     | —           | References `FlowRun.ServiceId`                            |
| `NodeName`     | VARCHAR(100) | —           | Step name                                                 |
| `NodeType`     | VARCHAR(50)  | —           | Type label (`HttpRequest`, `IfElse`, `Wait`, etc.)        |
| `Sequence`     | INT          | —           | 1-based execution order                                   |
| `Status`       | VARCHAR(20)  | `"pending"` | `pending`, `running`, `completed`, `failed`, or `skipped` |
| `InputData`    | TEXT         | `"{}"`      | JSON snapshot of step input                               |
| `OutputData`   | TEXT         | `"{}"`      | JSON snapshot of step output                              |
| `ErrorMessage` | VARCHAR(500) | NULL        | Error details if the step failed                          |
| `BranchTaken`  | VARCHAR(20)  | NULL        | `"true"` or `"false"` for IfElse nodes                    |
| `RetryCount`   | INT          | `0`         | Number of retries attempted                               |
| `ApproxExecutionMs` | BIGINT  | `0`         | Declared approx execution time of this node in ms        |
| `StartedAt`    | BIGINT       | `0`         | Unix timestamp when the step started                      |
| `CompletedAt`  | BIGINT       | `0`         | Unix timestamp when the step finished                     |
| `DurationMs`   | BIGINT       | `0`         | Execution time in milliseconds                            |
| `TimeCreated`  | BIGINT       | —           | Record creation timestamp                                 |
| `TimeUpdated`  | BIGINT       | —           | Last update timestamp                                     |

---

## FlowDefinition

Stores reusable flow templates as JSON.

| Column            | Type          | Default | Description                                 |
| ----------------- | ------------- | ------- | ------------------------------------------- |
| `Id`              | BIGINT (PK)   | auto    | Primary key                                 |
| `DefinitionKey`   | VARCHAR(100)  | —       | Unique slug (e.g., `"order_notification"`)  |
| `DisplayName`     | VARCHAR(200)  | —       | Human-readable label                        |
| `Description`     | TEXT          | `""`    | Optional description                        |
| `Version`         | INT           | `1`     | Incremented on each save                    |
| `IsActive`        | TINYINT(1)    | `1`     | Whether this definition can be triggered    |
| `FlowJson`        | MEDIUMTEXT    | `"{}"`  | JSON body describing the flow and its nodes |
| `Tags`            | VARCHAR(500)  | `""`    | Comma-separated tags                        |
| `ParameterSchema` | VARCHAR(2000) | NULL    | JSON schema for runtime parameters          |
| `LastModifiedBy`  | BIGINT        | `0`     | User ID of last editor                      |
| `TimeCreated`     | BIGINT        | —       | Record creation timestamp                   |
| `TimeUpdated`     | BIGINT        | —       | Last update timestamp                       |
| `TimeDeleted`     | BIGINT        | `0`     | Soft-delete timestamp                       |

**Unique constraint:** `(DefinitionKey, Version)` — each key can have multiple versions.

---

## FlowDefinitionRun

Links a flow run to the definition and parameters that triggered it.

| Column              | Type         | Default | Description                               |
| ------------------- | ------------ | ------- | ----------------------------------------- |
| `Id`                | BIGINT (PK)  | auto    | Primary key                               |
| `FlowRunServiceId`  | CHAR(36)     | —       | References `FlowRun.ServiceId`            |
| `DefinitionId`      | BIGINT       | —       | References `FlowDefinition.Id`            |
| `DefinitionKey`     | VARCHAR(100) | —       | The definition key used                   |
| `DefinitionVersion` | INT          | —       | Version of the definition at trigger time |
| `Parameters`        | TEXT         | `"{}"`  | JSON of runtime parameters                |
| `TriggerUserId`     | BIGINT       | `0`     | User who triggered the run                |
| `TriggerSource`     | VARCHAR(100) | `""`    | Source label                              |
| `TimeCreated`       | BIGINT       | —       | Record creation timestamp                 |

---

## FlowSchedule

Defines automated triggers for flow definitions.

| Column              | Type         | Default | Description                                    |
| ------------------- | ------------ | ------- | ---------------------------------------------- |
| `Id`                | BIGINT (PK)  | auto    | Primary key                                    |
| `ScheduleKey`       | VARCHAR(100) | —       | Unique schedule identifier                     |
| `DisplayName`       | VARCHAR(200) | —       | Human-readable label                           |
| `DefinitionKey`     | VARCHAR(100) | —       | Flow definition to trigger                     |
| `ScheduleType`      | VARCHAR(20)  | —       | `cron`, `interval`, or `once`                  |
| `CronExpression`    | VARCHAR(100) | NULL    | Quartz cron expression (for cron type)         |
| `IntervalMinutes`   | INT          | NULL    | Repeat interval in minutes (for interval type) |
| `RunOnceAt`         | BIGINT       | NULL    | Unix timestamp for one-time execution          |
| `DefaultParameters` | TEXT         | `"{}"`  | JSON parameters passed on trigger              |
| `IsActive`          | TINYINT(1)   | `1`     | Whether the schedule is enabled                |
| `TimeZone`          | VARCHAR(50)  | `"UTC"` | IANA timezone ID                               |
| `MaxConcurrent`     | INT          | `1`     | Maximum concurrent runs (1 = no overlap)       |
| `LastRunAt`         | BIGINT       | `0`     | Most recent trigger timestamp                  |
| `NextRunAt`         | BIGINT       | `0`     | Expected next trigger time                     |
| `LastRunServiceId`  | CHAR(36)     | NULL    | ServiceId of the most recent run               |
| `LastRunStatus`     | VARCHAR(20)  | NULL    | Status of the most recent run                  |
| `CreatedBy`         | BIGINT       | `0`     | User who created the schedule                  |
| `TimeCreated`       | BIGINT       | —       | Record creation timestamp                      |
| `TimeUpdated`       | BIGINT       | —       | Last update timestamp                          |
| `TimeDeleted`       | BIGINT       | `0`     | Soft-delete timestamp                          |

---

## FlowSchemaVersion

A single-row table that tracks the current database schema version. Used by the auto-migration system to detect and apply changes.

| Column           | Type        | Default | Description                                      |
| ---------------- | ----------- | ------- | ------------------------------------------------ |
| `Id`             | INT (PK)    | `1`     | Always 1 (single row)                            |
| `SchemaVersion`  | VARCHAR(20) | —       | Current schema version                           |
| `AppliedAt`      | BIGINT      | —       | Unix timestamp of last migration                 |
| `PackageVersion` | VARCHAR(20) | —       | NuGet package version that applied the migration |

---

## Auto-Migration

The engine manages its own database schema without requiring manual migrations.

**How it works:**

1. On startup, the engine checks for the `FlowSchemaVersion` table.
2. If it does not exist, all tables are created from scratch.
3. If it exists, the stored version is compared with the package's expected version.
4. If there is a mismatch, incremental migration scripts are applied.
5. The `FlowSchemaVersion` record is updated.

**What this means for you:**

- You never run migration commands for engine tables
- Your application's database context is not modified
- Updating the NuGet package automatically updates the schema on next startup
- The engine reuses your application's database connection string

---

## Entity Classes

Each table has a corresponding C# entity class in the package:

| Table               | Entity Class                             |
| ------------------- | ---------------------------------------- |
| `FlowRun`           | `Database/Entities/FlowRun.cs`           |
| `FlowNodeLog`       | `Database/Entities/FlowNodeLog.cs`       |
| `FlowDefinition`    | `Database/Entities/FlowDefinition.cs`    |
| `FlowDefinitionRun` | `Database/Entities/FlowDefinitionRun.cs` |
| `FlowSchedule`      | `Database/Entities/FlowSchedule.cs`      |
| `FlowSchemaVersion` | `Database/Entities/FlowSchemaVersion.cs` |
