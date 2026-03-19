# FlowRunner — Phase 4: Scheduled Flow Execution

> **Status:** Planning  
> **Depends on:** Phase 1 (Core Engine) + Phase 2 (Enhanced Nodes) + Phase 3 (Flow Definitions in DB)  
> **Goal:** Allow flows to be triggered automatically on a schedule using Quartz.NET (already in the project). Support cron expressions, fixed intervals, and one-time future triggers.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Architecture Decisions](#2-architecture-decisions)
3. [Database Schema](#3-database-schema)
4. [Schedule Types](#4-schedule-types)
5. [Quartz Integration](#5-quartz-integration)
6. [FlowSchedulerService](#6-flowschedulerservice)
7. [Admin API Endpoints](#8-admin-api-endpoints)
8. [DTO Definitions](#9-dto-definitions)
9. [File & Folder Structure](#10-file--folder-structure)
10. [Step-by-Step Implementation Guide](#11-step-by-step-implementation-guide)
11. [Usage Examples](#12-usage-examples)
12. [Operational Notes](#13-operational-notes)

---

## 1. Overview

Phase 4 plugs FlowRunner into **Quartz.NET** (already installed: `Quartz 3.15.1` + `Quartz.Extensions.Hosting 3.15.1` in `BikiranWebAPI.csproj`).

Instead of calling `FlowBuilder.Create(...)` manually in code, an admin can define a schedule:

- **Cron** — standard cron expression (e.g. `"0 0 8 * * ?"` for 8 AM daily)
- **Interval** — fixed period (every N minutes/hours)
- **Once** — run once at a specific Unix timestamp in the future

Each schedule record references a `FlowDefinition` by key (from Phase 3). At trigger time, Quartz calls the `FlowScheduleJob`, which invokes `FlowDefinitionRunner.TriggerAsync()` with stored parameters.

This replaces the bespoke `SubscriptionReminderBackgroundService` pattern for future recurring automation tasks — existing services continue unchanged.

---

## 2. Architecture Decisions

### 2.1 Why Quartz (not a raw background service)?

| Feature                    | Raw BackgroundService        | Quartz.NET                      |
| -------------------------- | ---------------------------- | ------------------------------- |
| Cron scheduling            | No — requires manual parsing | Built-in                        |
| Missed fire recovery       | Manual                       | Built-in (`MisfireInstruction`) |
| Persistent job store       | Manual                       | `AdoJobStore` (MySQL)           |
| Distributed/multi-instance | Not safe                     | Clustering support              |
| Dynamic job management     | Restart required             | Runtime add/remove/pause/resume |

Quartz is already in the project and is the natural fit.

### 2.2 Persistent vs In-Memory Job Store

Phase 4 uses the **in-memory** Quartz store initially. This means schedules are reloaded from `FlowSchedule` on app startup. If the process restarts, jobs in progress are lost (any running `FlowRun` will stall with status `running`).

**Phase 5+:** Optionally migrate to `AdoJobStore` for persistence across restarts. Not required for Phase 4.

### 2.3 Parameters Storage

Schedule-level default parameters are stored in `FlowSchedule.DefaultParameters` (JSON). Can be overridden at trigger time by the job. Cannot be dynamic (no C# delegates). Dynamic parameters (e.g. "today's date") are resolved by the job via placeholder conventions (see Section 5.3).

### 2.4 Relationship with `SubscriptionReminderBackgroundService`

Existing services (`SubscriptionReminderBackgroundService`, `ExecutionService`) are **not replaced**. They follow a different pattern (polling + business logic). FlowRunner scheduling is for workflow chains, not tightly-coupled business logic. New recurring tasks should prefer FlowRunner schedules when they fit the node model.

---

## 3. Database Schema

### 3.1 Table: `FlowSchedule`

```sql
CREATE TABLE FlowSchedule (
    Id               BIGINT AUTO_INCREMENT PRIMARY KEY,
    ScheduleKey      VARCHAR(100) NOT NULL UNIQUE,   -- e.g. "daily_subscription_reminder"
    DisplayName      VARCHAR(200) NOT NULL,
    DefinitionKey    VARCHAR(100) NOT NULL,            -- FK concept → FlowDefinition.DefinitionKey
    ScheduleType     VARCHAR(20)  NOT NULL,            -- cron|interval|once
    CronExpression   VARCHAR(100) DEFAULT NULL,        -- e.g. "0 0 8 * * ?"
    IntervalMinutes  INT          DEFAULT NULL,        -- e.g. 60 (for interval type)
    RunOnceAt        BIGINT       DEFAULT NULL,        -- Unix timestamp (for once type)
    DefaultParameters TEXT        NOT NULL DEFAULT '{}',  -- JSON: params passed to TriggerAsync
    IsActive         TINYINT(1)   NOT NULL DEFAULT 1,
    TimeZone         VARCHAR(50)  NOT NULL DEFAULT 'UTC',
    MaxConcurrent    INT          NOT NULL DEFAULT 1,  -- Quartz disallow concurrent if 1
    LastRunAt        BIGINT       NOT NULL DEFAULT 0,
    NextRunAt        BIGINT       NOT NULL DEFAULT 0,
    LastRunServiceId VARCHAR(32)  DEFAULT NULL,        -- Last FlowRun.ServiceId
    LastRunStatus    VARCHAR(20)  DEFAULT NULL,        -- completed|failed
    CreatedBy        BIGINT       NOT NULL DEFAULT 0,
    TimeCreated      BIGINT       NOT NULL,
    TimeUpdated      BIGINT       NOT NULL,
    TimeDeleted      BIGINT       NOT NULL DEFAULT 0
);
```

### 3.2 C# Table Class

```csharp
// Tables/FlowSchedule.cs
[Table("FlowSchedule")]
public class FlowSchedule
{
    [Key, Column("Id")]
    public long Id { get; set; }

    [Required, MaxLength(100), Column("ScheduleKey")]
    public string ScheduleKey { get; set; } = string.Empty;

    [Required, MaxLength(200), Column("DisplayName")]
    public string DisplayName { get; set; } = string.Empty;

    [Required, MaxLength(100), Column("DefinitionKey")]
    public string DefinitionKey { get; set; } = string.Empty;

    [Required, MaxLength(20), Column("ScheduleType")]
    public string ScheduleType { get; set; } = "cron";  // FlowScheduleTypeEnum

    [MaxLength(100), Column("CronExpression")]
    public string? CronExpression { get; set; }

    [Column("IntervalMinutes")]
    public int? IntervalMinutes { get; set; }

    [Column("RunOnceAt")]
    public long? RunOnceAt { get; set; }

    [Column("DefaultParameters", TypeName = "text")]
    public string DefaultParameters { get; set; } = "{}";

    [Column("IsActive")]
    public bool IsActive { get; set; } = true;

    [MaxLength(50), Column("TimeZone")]
    public string TimeZone { get; set; } = "UTC";

    [Column("MaxConcurrent")]
    public int MaxConcurrent { get; set; } = 1;

    [Column("LastRunAt")]
    public long LastRunAt { get; set; }

    [Column("NextRunAt")]
    public long NextRunAt { get; set; }

    [MaxLength(32), Column("LastRunServiceId")]
    public string? LastRunServiceId { get; set; }

    [MaxLength(20), Column("LastRunStatus")]
    public string? LastRunStatus { get; set; }

    [Column("CreatedBy")]
    public long CreatedBy { get; set; }

    [Column("TimeCreated")]
    public long TimeCreated { get; set; }

    [Column("TimeUpdated")]
    public long TimeUpdated { get; set; }

    [Column("TimeDeleted")]
    public long TimeDeleted { get; set; }
}

public enum FlowScheduleTypeEnum
{
    cron,
    interval,
    once
}
```

---

## 4. Schedule Types

### 4.1 Cron

Standard Quartz cron expression (6-field format with seconds).

| Expression            | Meaning                        |
| --------------------- | ------------------------------ |
| `"0 0 8 * * ?"`       | Every day at 8:00 AM           |
| `"0 0 */2 * * ?"`     | Every 2 hours                  |
| `"0 0 9 ? * MON-FRI"` | Weekdays at 9 AM               |
| `"0 0/30 * * * ?"`    | Every 30 minutes               |
| `"0 0 0 1 * ?"`       | 1st of every month at midnight |

Quartz cron docs: https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/crontriggers.html

### 4.2 Interval

Fixed period in minutes. Implemented via Quartz `SimpleScheduleBuilder.RepeatMinutelyForever(n)`.

### 4.3 Once

Single future execution at a specific Unix timestamp. Implemented via Quartz `TriggerBuilder.StartAt(DateTimeOffset)`. After execution, the job is automatically unscheduled.

---

## 5. Quartz Integration

### 5.1 The Quartz Job: `FlowScheduleJob`

**File:** `Services/FlowRunner/Scheduling/FlowScheduleJob.cs`

```csharp
[DisallowConcurrentExecution]  // Prevents overlapping if MaxConcurrent=1
public class FlowScheduleJob(IServiceProvider serviceProvider) : IJob
{
    public static readonly JobKey Key = new("FlowScheduleJob", "FlowRunner");

    public async Task Execute(IJobExecutionContext context)
    {
        var scheduleKey = context.JobDetail.JobDataMap.GetString("scheduleKey")
            ?? throw new InvalidOperationException("Job missing 'scheduleKey'.");

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<EmailSenderV3Service>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<FlowScheduleJob>>();

        // Load schedule
        var schedule = await db.FlowSchedule
            .FirstOrDefaultAsync(s => s.ScheduleKey == scheduleKey && s.IsActive && s.TimeDeleted == 0);

        if (schedule == null)
        {
            logger.LogWarning("FlowScheduleJob: schedule '{Key}' not found or inactive.", scheduleKey);
            return;
        }

        // Resolve dynamic parameters
        var parameters = JsonConvert.DeserializeObject<Dictionary<string, string>>(schedule.DefaultParameters)
            ?? new();

        InjectDynamicParameters(parameters);  // e.g. {{today_date}}, {{unix_now}}

        // Run the flow definition
        var runner = new FlowDefinitionRunner(db);
        try
        {
            var serviceId = await runner.TriggerAsync(
                definitionKey: schedule.DefinitionKey,
                parameters: parameters,
                contextSetup: ctx => { ctx.EmailSender = emailSender; },
                triggerSource: $"FlowSchedule:{scheduleKey}"
            );

            // Update schedule metadata
            schedule.LastRunAt = TimeOperation.GetUnixTime();
            schedule.LastRunServiceId = serviceId;
            schedule.LastRunStatus = "triggered";
            schedule.TimeUpdated = TimeOperation.GetUnixTime();
            await db.SaveChangesAsync();

            logger.LogInformation("FlowScheduleJob: '{Key}' triggered → ServiceId={Id}", scheduleKey, serviceId);
        }
        catch (Exception ex)
        {
            schedule.LastRunAt = TimeOperation.GetUnixTime();
            schedule.LastRunStatus = "failed";
            schedule.TimeUpdated = TimeOperation.GetUnixTime();
            await db.SaveChangesAsync();

            logger.LogError(ex, "FlowScheduleJob: '{Key}' failed to trigger.", scheduleKey);
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }

    private static void InjectDynamicParameters(Dictionary<string, string> parameters)
    {
        // Built-in dynamic placeholders always available in scheduled flows:
        parameters.TryAdd("today_date", DateTime.UtcNow.ToString("yyyy-MM-dd"));
        parameters.TryAdd("unix_now",   TimeOperation.GetUnixTime().ToString());
        parameters.TryAdd("year",       DateTime.UtcNow.Year.ToString());
        parameters.TryAdd("month",      DateTime.UtcNow.Month.ToString("D2"));
    }
}
```

### 5.2 `FlowSchedulerService`

**File:** `Services/FlowRunner/Scheduling/FlowSchedulerService.cs`

Responsible for loading all active `FlowSchedule` records from DB on startup and registering them with Quartz. Also exposes methods to add/update/remove schedules dynamically (called from admin API).

```csharp
public class FlowSchedulerService(ISchedulerFactory schedulerFactory, AppDbContext db, ILogger<FlowSchedulerService> logger)
{
    private IScheduler? _scheduler;

    public async Task InitializeAsync()
    {
        _scheduler = await schedulerFactory.GetScheduler();

        var schedules = await db.FlowSchedule
            .Where(s => s.IsActive && s.TimeDeleted == 0)
            .ToListAsync();

        foreach (var schedule in schedules)
        {
            try { await RegisterScheduleAsync(schedule); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to register schedule '{Key}'", schedule.ScheduleKey);
            }
        }

        await _scheduler.Start();
        logger.LogInformation("FlowSchedulerService: initialized {Count} schedules.", schedules.Count);
    }

    public async Task RegisterScheduleAsync(FlowSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(_scheduler);

        var jobKey = new JobKey($"FlowSchedule.{schedule.ScheduleKey}", "FlowRunner");

        // Remove any existing job with this key before re-registering
        await _scheduler.DeleteJob(jobKey);

        var job = JobBuilder.Create<FlowScheduleJob>()
            .WithIdentity(jobKey)
            .UsingJobData("scheduleKey", schedule.ScheduleKey)
            .StoreDurably(false)
            .Build();

        ITrigger trigger = schedule.ScheduleType switch
        {
            "cron" => BuildCronTrigger(schedule, jobKey),
            "interval" => BuildIntervalTrigger(schedule, jobKey),
            "once" => BuildOnceTrigger(schedule, jobKey),
            _ => throw new InvalidOperationException($"Unknown schedule type: '{schedule.ScheduleType}'")
        };

        await _scheduler.ScheduleJob(job, trigger);
        logger.LogInformation("Registered schedule '{Key}' ({Type})", schedule.ScheduleKey, schedule.ScheduleType);
    }

    public async Task UnregisterScheduleAsync(string scheduleKey)
    {
        ArgumentNullException.ThrowIfNull(_scheduler);
        var jobKey = new JobKey($"FlowSchedule.{scheduleKey}", "FlowRunner");
        await _scheduler.DeleteJob(jobKey);
    }

    public async Task<DateTimeOffset?> GetNextFireTimeAsync(string scheduleKey)
    {
        if (_scheduler == null) return null;
        var triggerKey = new TriggerKey($"Trigger.{scheduleKey}", "FlowRunner");
        var trigger = await _scheduler.GetTrigger(triggerKey);
        return trigger?.GetNextFireTimeUtc();
    }

    private static ITrigger BuildCronTrigger(FlowSchedule s, JobKey jobKey) =>
        TriggerBuilder.Create()
            .WithIdentity($"Trigger.{s.ScheduleKey}", "FlowRunner")
            .ForJob(jobKey)
            .WithCronSchedule(s.CronExpression!,
                x => x.InTimeZone(TimeZoneInfo.FindSystemTimeZoneById(s.TimeZone))
                       .WithMisfireHandlingInstructionDoNothing())
            .Build();

    private static ITrigger BuildIntervalTrigger(FlowSchedule s, JobKey jobKey) =>
        TriggerBuilder.Create()
            .WithIdentity($"Trigger.{s.ScheduleKey}", "FlowRunner")
            .ForJob(jobKey)
            .WithSimpleSchedule(x => x
                .WithIntervalInMinutes(s.IntervalMinutes ?? 60)
                .RepeatForever()
                .WithMisfireHandlingInstructionNextWithRemainingCount())
            .Build();

    private static ITrigger BuildOnceTrigger(FlowSchedule s, JobKey jobKey) =>
        TriggerBuilder.Create()
            .WithIdentity($"Trigger.{s.ScheduleKey}", "FlowRunner")
            .ForJob(jobKey)
            .StartAt(DateTimeOffset.FromUnixTimeSeconds(s.RunOnceAt ?? 0))
            .Build();
}
```

### 5.3 Registration in `Program.cs`

```csharp
// In Program.cs — after existing service registrations:

// 1. Register Quartz in-memory store
builder.Services.AddQuartz(q => {
    q.UseDefaultThreadPool(tp => { tp.MaxConcurrency = 10; });
    // Jobs are registered dynamically via FlowSchedulerService, not here
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// 2. Register scheduler service (singleton, owns the scheduler reference)
builder.Services.AddSingleton<FlowSchedulerService>();
builder.Services.AddScoped<FlowDefinitionRunner>();

// 3. In app pipeline — after builder.Build():
var app = builder.Build();
// ...existing middleware...

// Initialize FlowSchedulerService AFTER app starts
app.Lifetime.ApplicationStarted.Register(async () => {
    using var scope = app.Services.CreateScope();
    var scheduler = scope.ServiceProvider.GetRequiredService<FlowSchedulerService>();
    await scheduler.InitializeAsync();
});
```

---

## 6. FlowSchedulerService

See Section 5.2 above for the full implementation details. Summary of public API:

```csharp
public class FlowSchedulerService
{
    Task InitializeAsync()                               // Call on app startup
    Task RegisterScheduleAsync(FlowSchedule schedule)   // Add or update a schedule in Quartz
    Task UnregisterScheduleAsync(string scheduleKey)    // Remove from Quartz
    Task<DateTimeOffset?> GetNextFireTimeAsync(string scheduleKey) // Query next fire time
}
```

---

## 7. Dynamic Built-in Parameters

The following placeholders are automatically available in scheduled flow parameters without the admin needing to define them manually:

| Placeholder      | Value                    | Example      |
| ---------------- | ------------------------ | ------------ |
| `{{today_date}}` | `yyyy-MM-dd` in UTC      | `2026-03-18` |
| `{{unix_now}}`   | Unix timestamp (seconds) | `1742300000` |
| `{{year}}`       | 4-digit year             | `2026`       |
| `{{month}}`      | 2-digit month            | `03`         |

Admins can use these in `FlowDefinition` node params just like caller-supplied parameters.

---

## 8. Admin API Endpoints

**Controller:** `ControllersAdm/FlowScheduleAdmController.cs`  
**Route prefix:** `admin/flow-runner/schedules`

| Method   | Route                                       | Description                                |
| -------- | ------------------------------------------- | ------------------------------------------ |
| `GET`    | `admin/flow-runner/schedules`               | List all schedules                         |
| `GET`    | `admin/flow-runner/schedules/{key}`         | Get schedule detail + next fire time       |
| `POST`   | `admin/flow-runner/schedules`               | Create a new schedule                      |
| `PUT`    | `admin/flow-runner/schedules/{key}`         | Update schedule (re-registers in Quartz)   |
| `PATCH`  | `admin/flow-runner/schedules/{key}/toggle`  | Enable or disable (pause/resume in Quartz) |
| `DELETE` | `admin/flow-runner/schedules/{key}`         | Soft-delete + unregister from Quartz       |
| `POST`   | `admin/flow-runner/schedules/{key}/run-now` | Manually fire the schedule immediately     |
| `GET`    | `admin/flow-runner/schedules/{key}/runs`    | List FlowRuns triggered by this schedule   |

---

## 9. DTO Definitions

**File:** `Models/FlowRunner/V3/FlowScheduleDTOs.cs`

```csharp
// Request: Create or Update
public class FlowScheduleSaveRequestDTO
{
    public string ScheduleKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DefinitionKey { get; set; } = string.Empty;   // must exist in FlowDefinition
    public string ScheduleType { get; set; } = "cron";           // cron|interval|once
    public string? CronExpression { get; set; }
    public int? IntervalMinutes { get; set; }
    public long? RunOnceAt { get; set; }
    public Dictionary<string, string> DefaultParameters { get; set; } = new();
    public string TimeZone { get; set; } = "UTC";
    public int MaxConcurrent { get; set; } = 1;
}

// Response: Summary (list)
public class FlowScheduleSummaryDTO
{
    public long Id { get; set; }
    public string ScheduleKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DefinitionKey { get; set; } = string.Empty;
    public string ScheduleType { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public long LastRunAt { get; set; }
    public string? LastRunStatus { get; set; }
    public string? LastRunServiceId { get; set; }
    public long NextRunAt { get; set; }   // Populated from Quartz at read time
}

// Response: Detail
public class FlowScheduleDetailDTO : FlowScheduleSummaryDTO
{
    public string? CronExpression { get; set; }
    public int? IntervalMinutes { get; set; }
    public long? RunOnceAt { get; set; }
    public Dictionary<string, string> DefaultParameters { get; set; } = new();
    public string TimeZone { get; set; } = string.Empty;
    public int MaxConcurrent { get; set; }
    public string? NextFireTime { get; set; }  // Human-readable UTC string from Quartz
}
```

---

## 10. File & Folder Structure

```
Services/
└── FlowRunner/
    └── Scheduling/
        ├── FlowScheduleJob.cs          ← Quartz IJob implementation
        └── FlowSchedulerService.cs     ← Schedule registration + lifecycle

Tables/
└── FlowSchedule.cs                     ← EF Core table model (NEW)

ControllersAdm/
└── FlowScheduleAdmController.cs        ← 8 admin endpoints (NEW)

Models/
└── FlowRunner/
    └── V3/
        └── FlowScheduleDTOs.cs         ← Request/Response DTOs (NEW)

Data/
└── AppDbContext.cs                      ← Add DbSet<FlowSchedule>

Migrations/
└── ..._AddFlowScheduleTable.cs         ← EF migration (NEW)

Program.cs                              ← Quartz registration + startup init
```

---

## 11. Step-by-Step Implementation Guide

### Step 1 — Create `FlowSchedule` Table

Create `Tables/FlowSchedule.cs`. Add `DbSet<FlowSchedule>` to `AppDbContext`. Run migration.

---

### Step 2 — Create `FlowScheduleJob`

Implement `FlowScheduleJob` as a Quartz `IJob`. Must use scoped service provider (not constructor injection) for `AppDbContext` and `EmailSenderV3Service`.

**Verify:** In a TestController, manually call `Execute` with a mock `IJobExecutionContext` to confirm it calls `FlowDefinitionRunner.TriggerAsync` correctly.

---

### Step 3 — Create `FlowSchedulerService`

Implement `RegisterScheduleAsync`, `UnregisterScheduleAsync`, `InitializeAsync`, and `GetNextFireTimeAsync`.

**Verify:** On app start, confirm Quartz logs `"Registered schedule..."` for each active schedule.

---

### Step 4 — Register Quartz in `Program.cs`

Register Quartz services and `FlowSchedulerService` singleton. Wire up `ApplicationStarted` to call `InitializeAsync`.

**Verify:** App starts without errors. Check log output for schedule registration.

---

### Step 5 — Create Admin Controller

Implement `FlowScheduleAdmController`. Key logic:

- On `POST` / `PUT`: save to DB, then call `_schedulerService.RegisterScheduleAsync(schedule)`.
- On `PATCH` toggle: update `IsActive` in DB, then register or unregister in Quartz.
- On `DELETE`: soft-delete (set `TimeDeleted`), then call `UnregisterScheduleAsync`.
- On detail `GET`: fetch `NextFireTimeAsync` from `FlowSchedulerService` and add to response.

---

### Step 6 — Integration Test

1. Create a `FlowDefinition` with key `"scheduler_test_flow"`.
2. Create a schedule with `ScheduleType=interval`, `IntervalMinutes=1`.
3. Wait 1 minute and verify a `FlowRun` row appears.
4. Disable the schedule and confirm no new runs appear after another minute.

---

## 12. Usage Examples

### Example A: Daily Email Report (Cron)

```http
POST /admin/flow-runner/schedules
{
  "scheduleKey": "daily_subscription_expiry_report",
  "displayName": "Daily Subscription Expiry Email",
  "definitionKey": "subscription_expiry_warn_flow",
  "scheduleType": "cron",
  "cronExpression": "0 0 8 * * ?",
  "timeZone": "Asia/Dhaka",
  "defaultParameters": {
    "adminEmail": "admin@n8nclouds.com",
    "reportDate": "{{today_date}}"
  }
}
```

---

### Example B: Poll External API Every 15 Minutes (Interval)

```http
POST /admin/flow-runner/schedules
{
  "scheduleKey": "appocean_health_check",
  "displayName": "AppOcean Health Check",
  "definitionKey": "appocean_ping_flow",
  "scheduleType": "interval",
  "intervalMinutes": 15,
  "defaultParameters": {}
}
```

---

### Example C: One-Time User Welcome Sequence

Triggered programmatically with a future Unix timestamp:

```csharp
var oneDayFromNow = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();

_context.FlowSchedule.Add(new FlowSchedule {
    ScheduleKey = $"welcome_sequence_{userId}",
    DisplayName = $"Welcome Sequence for User {userId}",
    DefinitionKey = "user_welcome_sequence_flow",
    ScheduleType = "once",
    RunOnceAt = oneDayFromNow,
    DefaultParameters = JsonConvert.SerializeObject(new {
        userId = userId.ToString(),
        email = userEmail
    }),
    IsActive = true,
    TimeCreated = TimeOperation.GetUnixTime(),
    TimeUpdated = TimeOperation.GetUnixTime()
});
await _context.SaveChangesAsync();

// Immediately register in Quartz (no restart needed)
await _schedulerService.RegisterScheduleAsync(schedule);
```

---

## 13. Operational Notes

### Misfire Handling

- **Cron** uses `WithMisfireHandlingInstructionDoNothing()` — missed fires during downtime are skipped, not executed on recovery. This prevents flooding when the app restarts after a long downtime.
- **Interval** uses `WithMisfireHandlingInstructionNextWithRemainingCount()` — continues from where it left off without catching up on missed fires.

### Timezone Support

`FlowSchedule.TimeZone` accepts IANA timezone IDs (e.g. `"Asia/Dhaka"`, `"America/New_York"`, `"UTC"`). These are passed to `TimeZoneInfo.FindSystemTimeZoneById()`. On Linux Docker, IANA timezones work natively. On Windows, they map to Windows timezone names automatically via .NET 6+.

### Preventing Overlapping Runs

`[DisallowConcurrentExecution]` on `FlowScheduleJob` ensures that if a previous run is still executing when the next fire time arrives, the new fire is skipped (with a Quartz log warning). This prevents the `FlowSchedule.MaxConcurrent=1` scenario from creating duplicate runs.

### Monitoring Running Schedules

Query `FlowRun` table with `TriggerSource LIKE 'FlowSchedule:%'` to see all schedule-triggered runs. The `FlowDefinitionRun` table provides `DefinitionKey` and the parameters used for each run.

---

_End of Phase 4 Plan — Ready for implementation after Phase 3 is complete._
