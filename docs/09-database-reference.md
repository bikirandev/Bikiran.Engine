# Database Reference

Bikiran.Engine manages its own database tables independently from your application. The engine uses an internal `EngineDbContext` — your application's `DbContext` is never modified.

All tables are created automatically on first startup and updated automatically when the NuGet package is updated.

---

## Tables Overview

| Table                 | Purpose                                         |
| --------------------- | ----------------------------------------------- |
| `FlowRun`             | Tracks each workflow execution                  |
| `FlowNodeLog`         | Records each node's execution within a run      |
| `FlowDefinition`      | Stores reusable flow templates as JSON          |
| `FlowDefinitionRun`   | Links a run to the definition that triggered it |
| `FlowSchedule`        | Defines automated triggers for flow definitions |
| `FlowSchemaVersion`   | Tracks the current database schema version      |

---

## FlowRun

Stores one record per workflow execution.

| Column           | Type         | Default     | Description                                                  |
| ---------------- | ------------ | ----------- | ------------------------------------------------------------ |
| `Id`             | BIGINT (PK)  | auto        | Primary key                                                  |
| `ServiceId`      | CHAR(36)     | —           | Unique run identifier (UUID)                                 |
| `FlowName`       | VARCHAR(100) | —           | Flow name                                                    |
| `Status`         | VARCHAR(20)  | `"pending"` | `pending` / `running` / `completed` / `failed` / `cancelled` |
| `TriggerSource`  | VARCHAR(100) | `""`        | Where the flow was triggered from                            |
| `Config`         | TEXT         | `"{}"`      | JSON-serialized runtime configuration                        |
| `ContextMeta`    | TEXT         | `"{}"`      | JSON snapshot of caller context (IP, user ID, path)          |
| `TotalNodes`     | INT          | `0`         | Total number of nodes in the flow                            |
| `CompletedNodes` | INT          | `0`         | Number of nodes completed so far                             |
| `ErrorMessage`   | VARCHAR(500) | NULL        | Error details if the flow failed                             |
| `StartedAt`      | BIGINT       | `0`         | Unix timestamp when execution began                          |
| `CompletedAt`    | BIGINT       | `0`         | Unix timestamp when execution finished                       |
| `DurationMs`     | BIGINT       | `0`         | Total execution time in milliseconds                         |
| `CreatorUserId`  | BIGINT       | `0`         | User who triggered the flow                                  |
| `IpString`       | VARCHAR(100) | `""`        | IP address of the triggering request                         |
| `TimeCreated`    | BIGINT       | —           | Record creation timestamp                                    |
| `TimeUpdated`    | BIGINT       | —           | Last update timestamp                                        |
| `TimeDeleted`    | BIGINT       | `0`         | Soft-delete timestamp (0 = active)                           |

```sql
CREATE TABLE FlowRun (
    Id            BIGINT AUTO_INCREMENT PRIMARY KEY,
    ServiceId     CHAR(36)     NOT NULL UNIQUE,
    FlowName      VARCHAR(100) NOT NULL,
    Status        VARCHAR(20)  NOT NULL,
    TriggerSource VARCHAR(100) NOT NULL DEFAULT '',
    Config        TEXT         NOT NULL,
    ContextMeta   TEXT         NOT NULL,
    TotalNodes    INT          NOT NULL DEFAULT 0,
    CompletedNodes INT         NOT NULL DEFAULT 0,
    ErrorMessage  VARCHAR(500)         DEFAULT NULL,
    StartedAt     BIGINT       NOT NULL DEFAULT 0,
    CompletedAt   BIGINT       NOT NULL DEFAULT 0,
    DurationMs    BIGINT       NOT NULL DEFAULT 0,
    CreatorUserId BIGINT       NOT NULL DEFAULT 0,
    IpString      VARCHAR(100) NOT NULL DEFAULT '',
    TimeCreated   BIGINT       NOT NULL,
    TimeUpdated   BIGINT       NOT NULL,
    TimeDeleted   BIGINT       NOT NULL DEFAULT 0
);
```

---

## FlowNodeLog

Stores one record per node execution within a run.

| Column         | Type         | Default     | Description                                                |
| -------------- | ------------ | ----------- | ---------------------------------------------------------- |
| `Id`           | BIGINT (PK)  | auto        | Primary key                                                |
| `ServiceId`    | CHAR(36)     | —           | References `FlowRun.ServiceId`                             |
| `NodeName`     | VARCHAR(100) | —           | Node name                                                  |
| `NodeType`     | VARCHAR(50)  | —           | Type label (`HttpRequest`, `IfElse`, `Wait`, etc.)         |
| `Sequence`     | INT          | —           | 1-based execution order                                    |
| `Status`       | VARCHAR(20)  | `"pending"` | `pending` / `running` / `completed` / `failed` / `skipped` |
| `InputData`    | TEXT         | `"{}"`      | JSON snapshot of node input                                |
| `OutputData`   | TEXT         | `"{}"`      | JSON snapshot of node output                               |
| `ErrorMessage` | VARCHAR(500) | NULL        | Error details if the node failed                           |
| `BranchTaken`  | VARCHAR(20)  | NULL        | `"true"` or `"false"` for IfElse nodes                     |
| `RetryCount`   | INT          | `0`         | Number of retries attempted                                |
| `StartedAt`    | BIGINT       | `0`         | Unix timestamp when the node started                       |
| `CompletedAt`  | BIGINT       | `0`         | Unix timestamp when the node finished                      |
| `DurationMs`   | BIGINT       | `0`         | Execution time in milliseconds                             |
| `TimeCreated`  | BIGINT       | —           | Record creation timestamp                                  |
| `TimeUpdated`  | BIGINT       | —           | Last update timestamp                                      |

```sql
CREATE TABLE FlowNodeLog (
    Id          BIGINT AUTO_INCREMENT PRIMARY KEY,
    ServiceId   CHAR(36)     NOT NULL,
    NodeName    VARCHAR(100) NOT NULL,
    NodeType    VARCHAR(50)  NOT NULL,
    Sequence    INT          NOT NULL,
    Status      VARCHAR(20)  NOT NULL,
    InputData   TEXT         NOT NULL,
    OutputData  TEXT         NOT NULL,
    ErrorMessage VARCHAR(500)        DEFAULT NULL,
    BranchTaken VARCHAR(20)          DEFAULT NULL,
    RetryCount  INT          NOT NULL DEFAULT 0,
    StartedAt   BIGINT       NOT NULL DEFAULT 0,
    CompletedAt BIGINT       NOT NULL DEFAULT 0,
    DurationMs  BIGINT       NOT NULL DEFAULT 0,
    TimeCreated BIGINT       NOT NULL,
    TimeUpdated BIGINT       NOT NULL
);
```

---

## FlowDefinition

Stores reusable flow templates as JSON.

| Column           | Type         | Default | Description                                 |
| ---------------- | ------------ | ------- | ------------------------------------------- |
| `Id`             | BIGINT (PK)  | auto    | Primary key                                 |
| `DefinitionKey`  | VARCHAR(100) | —       | Unique slug (e.g., `"order_notification"`)  |
| `DisplayName`    | VARCHAR(200) | —       | Human-readable label                        |
| `Description`    | TEXT         | `""`    | Optional description                        |
| `Version`        | INT          | `1`     | Incremented on each save                    |
| `IsActive`       | TINYINT(1)   | `1`     | Whether this definition can be triggered    |
| `FlowJson`       | MEDIUMTEXT   | `"{}"`  | JSON body describing the flow and its nodes |
| `Tags`           | VARCHAR(500) | `""`    | Comma-separated tags                        |
| `LastModifiedBy` | BIGINT       | `0`     | User ID of last editor                      |
| `TimeCreated`    | BIGINT       | —       | Record creation timestamp                   |
| `TimeUpdated`    | BIGINT       | —       | Last update timestamp                       |
| `TimeDeleted`    | BIGINT       | `0`     | Soft-delete timestamp                       |

**Unique constraint:** `(DefinitionKey, Version)` — each key can have multiple versions.

```sql
CREATE TABLE FlowDefinition (
    Id              BIGINT AUTO_INCREMENT PRIMARY KEY,
    DefinitionKey   VARCHAR(100) NOT NULL,
    DisplayName     VARCHAR(200) NOT NULL,
    Description     TEXT         NOT NULL DEFAULT '',
    Version         INT          NOT NULL DEFAULT 1,
    IsActive        TINYINT(1)   NOT NULL DEFAULT 1,
    FlowJson        MEDIUMTEXT   NOT NULL,
    Tags            VARCHAR(500) NOT NULL DEFAULT '',
    LastModifiedBy  BIGINT       NOT NULL DEFAULT 0,
    TimeCreated     BIGINT       NOT NULL,
    TimeUpdated     BIGINT       NOT NULL,
    TimeDeleted     BIGINT       NOT NULL DEFAULT 0,
    UNIQUE KEY uq_key_version (DefinitionKey, Version)
);
```

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

```sql
CREATE TABLE FlowDefinitionRun (
    Id              BIGINT AUTO_INCREMENT PRIMARY KEY,
    FlowRunServiceId CHAR(36)     NOT NULL,
    DefinitionId    BIGINT       NOT NULL,
    DefinitionKey   VARCHAR(100) NOT NULL,
    DefinitionVersion INT        NOT NULL,
    Parameters      TEXT         NOT NULL DEFAULT '{}',
    TriggerUserId   BIGINT       NOT NULL DEFAULT 0,
    TriggerSource   VARCHAR(100) NOT NULL DEFAULT '',
    TimeCreated     BIGINT       NOT NULL
);
```

---

## FlowSchedule

Defines automated triggers for flow definitions.

| Column              | Type         | Default | Description                                    |
| ------------------- | ------------ | ------- | ---------------------------------------------- |
| `Id`                | BIGINT (PK)  | auto    | Primary key                                    |
| `ScheduleKey`       | VARCHAR(100) | —       | Unique schedule identifier                     |
| `DisplayName`       | VARCHAR(200) | —       | Human-readable label                           |
| `DefinitionKey`     | VARCHAR(100) | —       | Flow definition to trigger                     |
| `ScheduleType`      | VARCHAR(20)  | —       | `cron` / `interval` / `once`                   |
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

```sql
CREATE TABLE FlowSchedule (
    Id               BIGINT AUTO_INCREMENT PRIMARY KEY,
    ScheduleKey      VARCHAR(100) NOT NULL UNIQUE,
    DisplayName      VARCHAR(200) NOT NULL,
    DefinitionKey    VARCHAR(100) NOT NULL,
    ScheduleType     VARCHAR(20)  NOT NULL,
    CronExpression   VARCHAR(100) DEFAULT NULL,
    IntervalMinutes  INT          DEFAULT NULL,
    RunOnceAt        BIGINT       DEFAULT NULL,
    DefaultParameters TEXT        NOT NULL DEFAULT '{}',
    IsActive         TINYINT(1)   NOT NULL DEFAULT 1,
    TimeZone         VARCHAR(50)  NOT NULL DEFAULT 'UTC',
    MaxConcurrent    INT          NOT NULL DEFAULT 1,
    LastRunAt        BIGINT       NOT NULL DEFAULT 0,
    NextRunAt        BIGINT       NOT NULL DEFAULT 0,
    LastRunServiceId CHAR(36)     DEFAULT NULL,
    LastRunStatus    VARCHAR(20)  DEFAULT NULL,
    CreatedBy        BIGINT       NOT NULL DEFAULT 0,
    TimeCreated      BIGINT       NOT NULL,
    TimeUpdated      BIGINT       NOT NULL,
    TimeDeleted      BIGINT       NOT NULL DEFAULT 0
);
```

---

## FlowSchemaVersion

A single-row table that tracks the current database schema version. Used by the auto-migration system to detect and apply changes when the NuGet package is updated.

| Column           | Type        | Default | Description                                      |
| ---------------- | ----------- | ------- | ------------------------------------------------ |
| `Id`             | INT (PK)    | `1`     | Always 1 (single row)                            |
| `SchemaVersion`  | VARCHAR(20) | —       | Current schema version                           |
| `AppliedAt`      | BIGINT      | —       | Unix timestamp of last migration                 |
| `PackageVersion` | VARCHAR(20) | —       | NuGet package version that applied the migration |

```sql
CREATE TABLE FlowSchemaVersion (
    Id             INT          PRIMARY KEY DEFAULT 1,
    SchemaVersion  VARCHAR(20)  NOT NULL,
    AppliedAt      BIGINT       NOT NULL,
    PackageVersion VARCHAR(20)  NOT NULL,
    CHECK (Id = 1)
);
```

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

- You never run `dotnet ef migrations add` for engine tables
- Your application's `DbContext` is not modified
- Updating the NuGet package automatically updates the schema on next startup
- The engine reuses your application's database connection string

---

## EF Core Entity Classes

Each table has a corresponding C# entity class in the package:

| Table                 | Entity Class                               |
| --------------------- | ------------------------------------------ |
| `FlowRun`             | `Database/Entities/FlowRun.cs`             |
| `FlowNodeLog`         | `Database/Entities/FlowNodeLog.cs`         |
| `FlowDefinition`      | `Database/Entities/FlowDefinition.cs`      |
| `FlowDefinitionRun`   | `Database/Entities/FlowDefinitionRun.cs`   |
| `FlowSchedule`        | `Database/Entities/FlowSchedule.cs`        |
| `FlowSchemaVersion`   | `Database/Entities/FlowSchemaVersion.cs`   |
