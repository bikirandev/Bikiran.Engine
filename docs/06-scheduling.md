# Scheduling

Schedules let you trigger flows automatically at defined times — without any manual intervention. Bikiran.Engine uses Quartz.NET to support three schedule types: cron expressions, fixed intervals, and one-time future execution.

---

## When to Use Scheduling

| Scenario                                            | Schedule Type |
| --------------------------------------------------- | ------------- |
| Send a daily report email every morning at 8 AM     | Cron          |
| Ping an external API every 15 minutes               | Interval      |
| Send a welcome email 24 hours after a user signs up | One-time      |

---

## Schedule Types

| Type         | Description                                                        | Example                         |
| ------------ | ------------------------------------------------------------------ | ------------------------------- |
| **Cron**     | Standard Quartz cron expression (6-field format including seconds) | `"0 0 8 * * ?"` — daily at 8 AM |
| **Interval** | Repeats every N minutes                                            | Every 15 minutes                |
| **Once**     | Runs once at a specific Unix timestamp                             | A specific moment in the future |

### Useful Cron Expressions

| Expression          | Meaning                        |
| ------------------- | ------------------------------ |
| `0 0 8 * * ?`       | Every day at 8:00 AM           |
| `0 0 */2 * * ?`     | Every 2 hours                  |
| `0 0 9 ? * MON-FRI` | Weekdays at 9:00 AM            |
| `0 0/30 * * * ?`    | Every 30 minutes               |
| `0 0 0 1 * ?`       | 1st of every month at midnight |

---

## How It Works

1. An admin creates a `FlowSchedule` record that references a flow definition by its key.
2. On application startup, all active schedules are loaded and registered with Quartz.NET.
3. At each trigger time, Quartz fires a job that loads the schedule's parameters and triggers the linked flow definition.
4. The resulting flow run goes through the standard execution pipeline.

---

## Automatic Placeholders

Every scheduled flow automatically has access to these placeholders, without the admin needing to define them:

| Placeholder      | Value                            | Example      |
| ---------------- | -------------------------------- | ------------ |
| `{{today_date}}` | Current UTC date (`yyyy-MM-dd`)  | `2026-03-19` |
| `{{unix_now}}`   | Current Unix timestamp (seconds) | `1742400000` |
| `{{year}}`       | 4-digit year                     | `2026`       |
| `{{month}}`      | 2-digit month                    | `03`         |

These can be used alongside custom parameters in flow definition node params.

---

## Timezone Support

Each schedule has a `TimeZone` field that accepts IANA timezone IDs:

| Example            | Region                     |
| ------------------ | -------------------------- |
| `UTC`              | Coordinated Universal Time |
| `Asia/Dhaka`       | Bangladesh Standard Time   |
| `America/New_York` | US Eastern Time            |

On .NET 6+, IANA timezones work on both Windows and Linux.

---

## Handling Missed Triggers

When the application is down during a scheduled trigger time, the engine handles it safely:

| Schedule Type | What Happens                                               |
| ------------- | ---------------------------------------------------------- |
| Cron          | Missed triggers are skipped entirely                       |
| Interval      | Resumes from the current time without catching up          |
| Once          | If the app was down at fire time, triggers on next startup |

This prevents a flood of backlogged runs after a restart.

---

## Preventing Overlapping Runs

If a previous run from the same schedule is still executing when the next trigger time arrives, the new run is skipped. This is controlled by the `MaxConcurrent` setting on the schedule (default: 1).

---

## Setup in Program.cs

```csharp
// Register Quartz in-memory store
builder.Services.AddQuartz(q => {
    q.UseDefaultThreadPool(tp => { tp.MaxConcurrency = 10; });
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// Register scheduler service
builder.Services.AddSingleton<FlowSchedulerService>();
builder.Services.AddScoped<FlowDefinitionRunner>();

// Load all active schedules on app startup
app.Lifetime.ApplicationStarted.Register(async () => {
    using var scope = app.Services.CreateScope();
    var scheduler = scope.ServiceProvider.GetRequiredService<FlowSchedulerService>();
    await scheduler.InitializeAsync();
});
```

---

## Scheduler Methods

| Method                                 | Description                                              |
| -------------------------------------- | -------------------------------------------------------- |
| `InitializeAsync()`                    | Loads and registers all active schedules on startup      |
| `RegisterScheduleAsync(schedule)`      | Adds or updates a schedule in Quartz (no restart needed) |
| `UnregisterScheduleAsync(scheduleKey)` | Removes a schedule from Quartz                           |
| `GetNextFireTimeAsync(key)`            | Returns the next expected trigger time                   |

---

## Creating Schedules

### Daily Report (Cron) — via API

```http
POST /api/bikiran-engine/schedules
Content-Type: application/json

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

### Health Check (Interval) — via API

```http
POST /api/bikiran-engine/schedules
Content-Type: application/json

{
  "scheduleKey": "api_health_check",
  "displayName": "API Health Check",
  "definitionKey": "api_ping_flow",
  "scheduleType": "interval",
  "intervalMinutes": 15,
  "defaultParameters": {}
}
```

### One-Time Delayed Task — via Code

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

// Register in Quartz immediately — no app restart needed
await _schedulerService.RegisterScheduleAsync(schedule);
```

---

## Data Models

### Create or Update a Schedule

| Field               | Type                         | Description                                    |
| ------------------- | ---------------------------- | ---------------------------------------------- |
| `ScheduleKey`       | string                       | Unique schedule identifier                     |
| `DisplayName`       | string                       | Human-readable label                           |
| `DefinitionKey`     | string                       | Must match an existing FlowDefinition          |
| `ScheduleType`      | string                       | `"cron"`, `"interval"`, or `"once"`            |
| `CronExpression`    | string?                      | Quartz cron expression (for cron type)         |
| `IntervalMinutes`   | int?                         | Repeat interval in minutes (for interval type) |
| `RunOnceAt`         | long?                        | Unix timestamp for one-time execution          |
| `DefaultParameters` | Dictionary\<string, string\> | Key-value pairs passed on trigger              |
| `TimeZone`          | string                       | IANA timezone ID                               |
| `MaxConcurrent`     | int                          | Maximum concurrent runs (default: 1)           |

### Schedule Summary Response

| Field              | Type    | Description                     |
| ------------------ | ------- | ------------------------------- |
| `Id`               | long    | Primary key                     |
| `ScheduleKey`      | string  | Unique schedule identifier      |
| `DisplayName`      | string  | Human-readable label            |
| `DefinitionKey`    | string  | Linked flow definition          |
| `ScheduleType`     | string  | Schedule type                   |
| `IsActive`         | bool    | Whether the schedule is enabled |
| `LastRunAt`        | long    | Most recent trigger timestamp   |
| `LastRunStatus`    | string? | Status of the last run          |
| `LastRunServiceId` | string? | ServiceId of the last run       |
| `NextRunAt`        | long    | Expected next trigger time      |
