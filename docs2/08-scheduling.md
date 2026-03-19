# Scheduled Flow Execution

This document explains how to trigger flows automatically on a schedule using Quartz.NET — supporting cron expressions, fixed intervals, and one-time future execution.

---

## Purpose

Instead of triggering flows manually from controllers or the admin API, schedules allow automated execution:

- **Daily reports** — send a summary email every morning at 8 AM.
- **Health checks** — ping an external API every 15 minutes.
- **Delayed tasks** — send a welcome sequence 24 hours after signup.

---

## Schedule Types

| Type         | Description                                                   | Example                         |
| ------------ | ------------------------------------------------------------- | ------------------------------- |
| **Cron**     | Standard Quartz cron expression (6-field format with seconds) | `"0 0 8 * * ?"` = daily at 8 AM |
| **Interval** | Fixed period in minutes                                       | Every 15 minutes                |
| **Once**     | Run once at a specific future Unix timestamp                  | A specific moment in the future |

### Common Cron Expressions

| Expression          | Meaning                        |
| ------------------- | ------------------------------ |
| `0 0 8 * * ?`       | Every day at 8:00 AM           |
| `0 0 */2 * * ?`     | Every 2 hours                  |
| `0 0 9 ? * MON-FRI` | Weekdays at 9:00 AM            |
| `0 0/30 * * * ?`    | Every 30 minutes               |
| `0 0 0 1 * ?`       | 1st of every month at midnight |

---

## How It Works

1. An admin creates a `FlowSchedule` record that references a `FlowDefinition` by key.
2. On application startup, all active schedules are loaded and registered with Quartz.NET.
3. At each trigger time, Quartz fires a `FlowScheduleJob`.
4. The job loads the schedule's default parameters, injects any dynamic placeholders, and calls `FlowDefinitionRunner.TriggerAsync()`.
5. The resulting FlowRun executes through the standard engine pipeline.

---

## Dynamic Built-In Parameters

The following placeholders are automatically available in every scheduled flow, without the admin needing to define them:

| Placeholder      | Value                              | Example      |
| ---------------- | ---------------------------------- | ------------ |
| `{{today_date}}` | Current date in UTC (`yyyy-MM-dd`) | `2026-03-19` |
| `{{unix_now}}`   | Current Unix timestamp (seconds)   | `1742400000` |
| `{{year}}`       | 4-digit year                       | `2026`       |
| `{{month}}`      | 2-digit month                      | `03`         |

These can be used in flow definition node params alongside custom parameters.

---

## Why Quartz.NET

Quartz.NET is already installed in the project (`Quartz 3.15.1` + `Quartz.Extensions.Hosting 3.15.1`) and provides:

| Feature                      | Benefit                                                             |
| ---------------------------- | ------------------------------------------------------------------- |
| Built-in cron support        | No manual parsing needed                                            |
| Misfire recovery             | Handles what happens when the app was down during a scheduled fire  |
| Runtime job management       | Add, remove, pause, and resume schedules without restarting the app |
| Concurrent execution control | Prevent overlapping runs of the same schedule                       |

---

## Misfire Handling

| Schedule Type | Strategy                 | What Happens                                               |
| ------------- | ------------------------ | ---------------------------------------------------------- |
| Cron          | `DoNothing`              | Missed fires during downtime are skipped entirely          |
| Interval      | `NextWithRemainingCount` | Continues from where it left off without catching up       |
| Once          | (default)                | If the app was down at fire time, triggers on next startup |

This prevents the system from flooding with backlogged runs after a restart.

---

## Preventing Overlapping Runs

The `FlowScheduleJob` uses `[DisallowConcurrentExecution]`. If a previous run is still executing when the next trigger time arrives, the new fire is skipped. This is controlled by the `MaxConcurrent` setting on the schedule (default: 1).

---

## Timezone Support

`FlowSchedule.TimeZone` accepts IANA timezone IDs:

| Example            | Region                     |
| ------------------ | -------------------------- |
| `UTC`              | Coordinated Universal Time |
| `Asia/Dhaka`       | Bangladesh Standard Time   |
| `America/New_York` | US Eastern Time            |

These are passed to `TimeZoneInfo.FindSystemTimeZoneById()`. On .NET 6+, IANA timezones work on both Windows and Linux.

---

## Registration in Program.cs

```csharp
// Register Quartz in-memory store
builder.Services.AddQuartz(q => {
    q.UseDefaultThreadPool(tp => { tp.MaxConcurrency = 10; });
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// Register scheduler service
builder.Services.AddSingleton<FlowSchedulerService>();
builder.Services.AddScoped<FlowDefinitionRunner>();

// Initialize on app startup
app.Lifetime.ApplicationStarted.Register(async () => {
    using var scope = app.Services.CreateScope();
    var scheduler = scope.ServiceProvider.GetRequiredService<FlowSchedulerService>();
    await scheduler.InitializeAsync();
});
```

---

## FlowSchedulerService API

```csharp
public class FlowSchedulerService
{
    Task InitializeAsync();                                 // Load and register all active schedules
    Task RegisterScheduleAsync(FlowSchedule schedule);      // Add or update a schedule in Quartz
    Task UnregisterScheduleAsync(string scheduleKey);       // Remove a schedule from Quartz
    Task<DateTimeOffset?> GetNextFireTimeAsync(string key); // Query the next fire time
}
```

---

## Admin API Usage Examples

### Create a Daily Email Report (Cron)

```http
POST /api/bikiran-engine/schedules
{
  "scheduleKey": "daily_subscription_expiry_report",
  "displayName": "Daily Subscription Expiry Email",
  "definitionKey": "subscription_expiry_warn_flow",
  "scheduleType": "cron",
  "cronExpression": "0 0 8 * * ?",
  "timeZone": "Asia/Dhaka",
  "defaultParameters": {
    "adminEmail": "admin@example.com",
    "reportDate": "{{today_date}}"
  }
}
```

### Create a Health Check Every 15 Minutes (Interval)

```http
POST /api/bikiran-engine/schedules
{
  "scheduleKey": "api_health_check",
  "displayName": "API Health Check",
  "definitionKey": "api_ping_flow",
  "scheduleType": "interval",
  "intervalMinutes": 15,
  "defaultParameters": {}
}
```

### Create a One-Time Delayed Task (Programmatic)

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

// Register in Quartz immediately (no restart needed)
await _schedulerService.RegisterScheduleAsync(schedule);
```

---

## DTOs

### Create/Update Request

```csharp
public class FlowScheduleSaveRequestDTO
{
    public string ScheduleKey { get; set; }
    public string DisplayName { get; set; }
    public string DefinitionKey { get; set; }       // Must match an existing FlowDefinition
    public string ScheduleType { get; set; }        // "cron" / "interval" / "once"
    public string? CronExpression { get; set; }
    public int? IntervalMinutes { get; set; }
    public long? RunOnceAt { get; set; }
    public Dictionary<string, string> DefaultParameters { get; set; }
    public string TimeZone { get; set; }
    public int MaxConcurrent { get; set; }
}
```

### Summary Response

```csharp
public class FlowScheduleSummaryDTO
{
    public long Id { get; set; }
    public string ScheduleKey { get; set; }
    public string DisplayName { get; set; }
    public string DefinitionKey { get; set; }
    public string ScheduleType { get; set; }
    public bool IsActive { get; set; }
    public long LastRunAt { get; set; }
    public string? LastRunStatus { get; set; }
    public string? LastRunServiceId { get; set; }
    public long NextRunAt { get; set; }
}
```

---

## File Structure

```
Scheduling/
├── FlowScheduleJob.cs           ← Quartz IJob that triggers flows
└── FlowSchedulerService.cs      ← Schedule registration and lifecycle

Database/Entities/
└── FlowSchedule.cs

Api/
└── BikiranEngineScheduleController.cs

Models/
└── FlowScheduleDTOs.cs
```

---

## Relationship with Existing Background Services

Existing services like `SubscriptionReminderBackgroundService` are **not replaced** by FlowRunner scheduling. Those follow a different pattern (polling + business logic). FlowRunner scheduling is designed for workflow chains that fit the node model. New recurring automation tasks should prefer FlowRunner schedules when they align with the node-based approach.
