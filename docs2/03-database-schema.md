# Database Schema

This document describes all database tables used by Bikiran.Engine across its features.

---

## Tables at a Glance

| Table               | Purpose                                             | Introduced In    |
| ------------------- | --------------------------------------------------- | ---------------- |
| `FlowRun`           | Tracks each workflow execution                      | Core Engine      |
| `FlowNodeLog`       | Records each node's execution within a run          | Core Engine      |
| `FlowDefinition`    | Stores reusable flow templates as JSON              | Flow Definitions |
| `FlowDefinitionRun` | Links a FlowRun to the definition that triggered it | Flow Definitions |
| `FlowSchedule`      | Defines scheduled triggers for flow definitions     | Scheduling       |

---

## FlowRun

Tracks each workflow execution from start to finish.

| Column           | Type         | Default     | Description                                                  |
| ---------------- | ------------ | ----------- | ------------------------------------------------------------ |
| `Id`             | BIGINT (PK)  | auto        | Primary key                                                  |
| `ServiceId`      | VARCHAR(32)  | —           | Unique 32-character run identifier                           |
| `FlowName`       | VARCHAR(100) | —           | Human-readable flow name                                     |
| `Status`         | VARCHAR(20)  | `"pending"` | `pending` / `running` / `completed` / `failed` / `cancelled` |
| `TriggerSource`  | VARCHAR(100) | `""`        | Where the flow was triggered from (e.g., controller name)    |
| `Config`         | TEXT         | `"{}"`      | JSON-serialized `FlowRunConfig`                              |
| `ContextMeta`    | TEXT         | `"{}"`      | JSON snapshot of caller context (IP, user ID, request path)  |
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

**SQL:**

```sql
CREATE TABLE FlowRun (
    Id            BIGINT AUTO_INCREMENT PRIMARY KEY,
    ServiceId     VARCHAR(32)  NOT NULL UNIQUE,
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

Records one node's execution within a FlowRun. One row per node per run.

| Column         | Type         | Default     | Description                                                |
| -------------- | ------------ | ----------- | ---------------------------------------------------------- |
| `Id`           | BIGINT (PK)  | auto        | Primary key                                                |
| `ServiceId`    | VARCHAR(32)  | —           | References `FlowRun.ServiceId`                             |
| `NodeName`     | VARCHAR(100) | —           | Node name (lowercase_underscore)                           |
| `NodeType`     | VARCHAR(50)  | —           | Type label (`HttpRequest`, `IfElse`, `Wait`, etc.)         |
| `Sequence`     | INT          | —           | 1-based execution order                                    |
| `Status`       | VARCHAR(20)  | `"pending"` | `pending` / `running` / `completed` / `failed` / `skipped` |
| `InputData`    | TEXT         | `"{}"`      | JSON snapshot of node input                                |
| `OutputData`   | TEXT         | `"{}"`      | JSON snapshot of node output                               |
| `ErrorMessage` | VARCHAR(500) | NULL        | Error details if the node failed                           |
| `BranchTaken`  | VARCHAR(20)  | NULL        | `"true"` or `"false"` for IfElse nodes                     |
| `RetryCount`   | INT          | `0`         | Number of retries attempted                                |
| `StartedAt`    | BIGINT       | `0`         | Unix timestamp when node started                           |
| `CompletedAt`  | BIGINT       | `0`         | Unix timestamp when node finished                          |
| `DurationMs`   | BIGINT       | `0`         | Node execution time in milliseconds                        |
| `TimeCreated`  | BIGINT       | —           | Record creation timestamp                                  |
| `TimeUpdated`  | BIGINT       | —           | Last update timestamp                                      |

**SQL:**

```sql
CREATE TABLE FlowNodeLog (
    Id          BIGINT AUTO_INCREMENT PRIMARY KEY,
    ServiceId   VARCHAR(32)  NOT NULL,
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

Stores reusable flow templates as structured JSON. Admins can create and update definitions without code changes.

| Column           | Type         | Default | Description                                 |
| ---------------- | ------------ | ------- | ------------------------------------------- |
| `Id`             | BIGINT (PK)  | auto    | Primary key                                 |
| `DefinitionKey`  | VARCHAR(100) | —       | Unique slug (e.g., `"order_notification"`)  |
| `DisplayName`    | VARCHAR(200) | —       | Human-readable label for admin UI           |
| `Description`    | TEXT         | `""`    | Optional description                        |
| `Version`        | INT          | `1`     | Incremented on each save                    |
| `IsActive`       | TINYINT(1)   | `1`     | Whether this definition can be triggered    |
| `FlowJson`       | MEDIUMTEXT   | `"{}"`  | JSON body describing the flow and its nodes |
| `Tags`           | VARCHAR(500) | `""`    | Comma-separated tags for categorization     |
| `LastModifiedBy` | BIGINT       | `0`     | User ID of last editor                      |
| `TimeCreated`    | BIGINT       | —       | Record creation timestamp                   |
| `TimeUpdated`    | BIGINT       | —       | Last update timestamp                       |
| `TimeDeleted`    | BIGINT       | `0`     | Soft-delete timestamp                       |

**Unique constraint:** `(DefinitionKey, Version)` — each key can have multiple versions.

**SQL:**

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

Links a FlowRun to the definition and parameters that triggered it.

| Column              | Type         | Default | Description                                         |
| ------------------- | ------------ | ------- | --------------------------------------------------- |
| `Id`                | BIGINT (PK)  | auto    | Primary key                                         |
| `FlowRunServiceId`  | VARCHAR(32)  | —       | References `FlowRun.ServiceId`                      |
| `DefinitionId`      | BIGINT       | —       | References `FlowDefinition.Id`                      |
| `DefinitionKey`     | VARCHAR(100) | —       | The definition key used                             |
| `DefinitionVersion` | INT          | —       | The version of the definition at trigger time       |
| `Parameters`        | TEXT         | `"{}"`  | JSON of runtime parameters supplied at trigger time |
| `TriggerUserId`     | BIGINT       | `0`     | User who triggered the run                          |
| `TriggerSource`     | VARCHAR(100) | `""`    | Source label (e.g., controller name)                |
| `TimeCreated`       | BIGINT       | —       | Record creation timestamp                           |

**SQL:**

```sql
CREATE TABLE FlowDefinitionRun (
    Id              BIGINT AUTO_INCREMENT PRIMARY KEY,
    FlowRunServiceId VARCHAR(32)  NOT NULL,
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

Defines automated triggers for flow definitions using cron, interval, or one-time schedules.

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
| `DefaultParameters` | TEXT         | `"{}"`  | JSON parameters passed to the flow on trigger  |
| `IsActive`          | TINYINT(1)   | `1`     | Whether the schedule is enabled                |
| `TimeZone`          | VARCHAR(50)  | `"UTC"` | IANA timezone ID for cron evaluation           |
| `MaxConcurrent`     | INT          | `1`     | Max concurrent runs (1 = no overlap)           |
| `LastRunAt`         | BIGINT       | `0`     | Unix timestamp of most recent trigger          |
| `NextRunAt`         | BIGINT       | `0`     | Expected next trigger time                     |
| `LastRunServiceId`  | VARCHAR(32)  | NULL    | ServiceId of the most recent run               |
| `LastRunStatus`     | VARCHAR(20)  | NULL    | Status of the most recent run                  |
| `CreatedBy`         | BIGINT       | `0`     | User who created the schedule                  |
| `TimeCreated`       | BIGINT       | —       | Record creation timestamp                      |
| `TimeUpdated`       | BIGINT       | —       | Last update timestamp                          |
| `TimeDeleted`       | BIGINT       | `0`     | Soft-delete timestamp                          |

**SQL:**

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
    LastRunServiceId VARCHAR(32)  DEFAULT NULL,
    LastRunStatus    VARCHAR(20)  DEFAULT NULL,
    CreatedBy        BIGINT       NOT NULL DEFAULT 0,
    TimeCreated      BIGINT       NOT NULL,
    TimeUpdated      BIGINT       NOT NULL,
    TimeDeleted      BIGINT       NOT NULL DEFAULT 0
);
```

---

## EF Core Table Classes

Each table has a corresponding C# class with EF Core attributes. These classes live in the `Tables/` folder.

| Table               | C# Class File                 |
| ------------------- | ----------------------------- |
| `FlowRun`           | `Tables/FlowRun.cs`           |
| `FlowNodeLog`       | `Tables/FlowNodeLog.cs`       |
| `FlowDefinition`    | `Tables/FlowDefinition.cs`    |
| `FlowDefinitionRun` | `Tables/FlowDefinitionRun.cs` |
| `FlowSchedule`      | `Tables/FlowSchedule.cs`      |

All classes use `[Table]`, `[Key]`, `[Column]`, `[Required]`, and `[MaxLength]` attributes for mapping. All `DbSet<T>` entries must be registered in `AppDbContext`.
