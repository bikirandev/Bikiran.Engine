# Scheduling

Bikiran.Engine uses Quartz.NET to run flows automatically on a schedule. You define when a flow should run, and the engine handles the rest.

---

## Schedule Types

| Type       | Description                      | Required Field             |
| ---------- | -------------------------------- | -------------------------- |
| `cron`     | Runs on a Quartz cron expression | `cronExpression`           |
| `interval` | Repeats every N minutes          | `intervalMinutes`          |
| `once`     | Fires once at a specific time    | `runOnceAt` (Unix seconds) |

---

## Creating Schedules

### Cron Schedule

Runs a flow at 8 AM every day in the Asia/Dhaka timezone:

```http
POST /api/bikiran-engine/schedules
Content-Type: application/json

{
  "scheduleKey": "daily_report",
  "displayName": "Daily Report Email",
  "definitionKey": "daily_report_flow",
  "scheduleType": "cron",
  "cronExpression": "0 0 8 * * ?",
  "timeZone": "Asia/Dhaka",
  "defaultParameters": {
    "adminEmail": "admin@example.com",
    "reportDate": "{{today_date}}"
  }
}
```

### Interval Schedule

Runs a health check every 15 minutes:

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

### One-Time Schedule

Runs exactly once at a specific Unix timestamp:

```http
POST /api/bikiran-engine/schedules
Content-Type: application/json

{
  "scheduleKey": "welcome_sequence_user_42",
  "displayName": "Welcome Sequence for User 42",
  "definitionKey": "welcome_sequence_flow",
  "scheduleType": "once",
  "runOnceAt": 1774675200,
  "defaultParameters": {
    "userId": "42",
    "email": "user@example.com"
  }
}
```

---

## Schedule Properties

| Property            | Type       | Default | Description                                  |
| ------------------- | ---------- | ------- | -------------------------------------------- |
| `scheduleKey`       | string     | —       | Unique identifier (required)                 |
| `displayName`       | string     | —       | Human-readable name                          |
| `definitionKey`     | string     | —       | Flow definition to trigger                   |
| `scheduleType`      | string     | —       | `"cron"`, `"interval"`, or `"once"`          |
| `cronExpression`    | string?    | null    | Quartz 6-field cron expression               |
| `intervalMinutes`   | int?       | null    | Repeat interval in minutes                   |
| `runOnceAt`         | long?      | null    | Unix timestamp for one-time execution        |
| `defaultParameters` | Dictionary | empty   | Parameters passed on each trigger            |
| `timeZone`          | string     | `"UTC"` | IANA timezone identifier                     |
| `maxConcurrent`     | int        | 1       | Maximum concurrent runs (1 prevents overlap) |

---

## Common Cron Expressions

| Expression           | Schedule                             |
| -------------------- | ------------------------------------ |
| `0 0 8 * * ?`        | Every day at 8:00 AM                 |
| `0 0 */2 * * ?`      | Every 2 hours                        |
| `0 30 9 ? * MON-FRI` | Weekdays at 9:30 AM                  |
| `0 0 0 1 * ?`        | First day of every month at midnight |
| `0 0/15 * * * ?`     | Every 15 minutes                     |
| `0 0 6,18 * * ?`     | At 6:00 AM and 6:00 PM daily         |

Quartz uses 6-field cron: `seconds minutes hours day-of-month month day-of-week`

---

## Timezone Support

Schedules support IANA timezone identifiers. Common examples:

| Timezone ID        | Region                     |
| ------------------ | -------------------------- |
| `UTC`              | Coordinated Universal Time |
| `Asia/Dhaka`       | Bangladesh                 |
| `America/New_York` | US Eastern                 |
| `Europe/London`    | UK                         |
| `Asia/Tokyo`       | Japan                      |

If no timezone is specified, UTC is used.

---

## Built-in Parameters

Schedules support the same built-in parameters as flow definitions:

| Parameter        | Value                     |
| ---------------- | ------------------------- |
| `{{today_date}}` | Current date (yyyy-MM-dd) |
| `{{unix_now}}`   | Current Unix timestamp    |
| `{{year}}`       | Current year              |
| `{{month}}`      | Current month (MM)        |

---

## Misfire Handling

When the application is down and misses a scheduled trigger, the behavior depends on the schedule type:

| Type     | Misfire Strategy          | Behavior                                          |
| -------- | ------------------------- | ------------------------------------------------- |
| Cron     | Do Nothing                | Skips missed fires, waits for next scheduled time |
| Interval | Next With Remaining Count | Continues from now with the same interval         |
| Once     | Fire Now                  | Fires immediately when the application restarts   |

---

## Preventing Overlapping Runs

By default, `maxConcurrent` is set to 1. This means if a scheduled flow is still running when the next trigger fires, the new trigger is skipped.

The `ScheduledFlowJob` is also decorated with `[DisallowConcurrentExecution]`, which prevents Quartz from running the same job concurrently.

---

## Managing Schedules

| Action          | Endpoint                                           |
| --------------- | -------------------------------------------------- |
| List all        | `GET /api/bikiran-engine/schedules`                |
| Get details     | `GET /api/bikiran-engine/schedules/{key}`          |
| Create          | `POST /api/bikiran-engine/schedules`               |
| Update          | `PUT /api/bikiran-engine/schedules/{key}`          |
| Enable/disable  | `PATCH /api/bikiran-engine/schedules/{key}/toggle` |
| Delete          | `DELETE /api/bikiran-engine/schedules/{key}`       |
| Run immediately | `POST /api/bikiran-engine/schedules/{key}/run-now` |
| View runs       | `GET /api/bikiran-engine/schedules/{key}/runs`     |

See [Admin API](08-admin-api.md) for full endpoint details.

---

## Creating Schedules from Code

You can also create schedules directly in code:

```csharp
var schedule = new FlowSchedule
{
    ScheduleKey = $"welcome_{userId}",
    DisplayName = $"Welcome Sequence for User {userId}",
    DefinitionKey = "welcome_sequence_flow",
    ScheduleType = "once",
    RunOnceAt = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),
    DefaultParameters = JsonSerializer.Serialize(new Dictionary<string, string>
    {
        { "userId", userId.ToString() },
        { "email", userEmail }
    }),
    IsActive = true,
    TimeCreated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
    TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
};

_dbContext.FlowSchedule.Add(schedule);
await _dbContext.SaveChangesAsync();

await _schedulerService.RegisterScheduleAsync(schedule);
```

---

## Setup Requirements

Quartz.NET must be registered before `AddBikiranEngine()`:

```csharp
builder.Services.AddQuartz(q =>
{
    q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 10);
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

builder.Services.AddBikiranEngine(options => { /* ... */ });
```

On startup, the engine's `EngineStartupService` calls `FlowSchedulerService.InitializeAsync()`, which loads all active schedules and registers them with Quartz. New schedules created via the API are hot-loaded without an application restart.
