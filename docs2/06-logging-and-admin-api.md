# Logging, Monitoring, and Admin API

This document covers how flow execution is logged, how to monitor running flows, and the admin API endpoints available for managing flow runs.

---

## What Gets Logged

### FlowRun Record Updates

The `FlowRun` record is updated at key moments during execution:

| Moment                | Status Set To | Fields Updated                                                 |
| --------------------- | ------------- | -------------------------------------------------------------- |
| `StartAsync()` called | `pending`     | `ServiceId`, `FlowName`, `Config`, `TotalNodes`, `ContextMeta` |
| Execution begins      | `running`     | `StartedAt`, `Status`                                          |
| All nodes complete    | `completed`   | `CompletedAt`, `DurationMs`, `Status`                          |
| A failure occurs      | `failed`      | `ErrorMessage`, `Status`, `CompletedAt`                        |

### FlowNodeLog Record Updates

Each node creates its own `FlowNodeLog` record:

| Moment          | Status Set To | Fields Updated                            |
| --------------- | ------------- | ----------------------------------------- |
| Node starts     | `running`     | `StartedAt`, `Sequence`, `InputData`      |
| Node succeeds   | `completed`   | `OutputData`, `CompletedAt`, `DurationMs` |
| Node fails      | `failed`      | `ErrorMessage`, `RetryCount`              |
| Node is skipped | `skipped`     | (no time data)                            |
| IfElse resolves | `completed`   | `BranchTaken` (`"true"` or `"false"`)     |

### Context Metadata

When `HttpContext` is available, the engine captures a snapshot of the caller's context:

```json
{
  "IpAddress": "192.168.1.1",
  "UserId": 12345,
  "RequestPath": "/api/v3/orders",
  "UserAgent": "Mozilla/5.0 ...",
  "Timestamp": 1742000000
}
```

This is stored in `FlowRun.ContextMeta` for debugging and audit purposes.

---

## Progress Monitoring

Progress is calculated using completed vs. total nodes:

```
ProgressPercent = (CompletedNodes / TotalNodes) × 100
```

This value is queryable at any time through the admin API while the flow is running.

---

## Log Writer Interface

Logging uses the `IFlowLogger` interface:

```csharp
public interface IFlowLogger
{
    Task CreateRunAsync(FlowRun run);
    Task UpdateRunAsync(FlowRun run);
    Task CreateNodeLogAsync(FlowNodeLog log);
    Task UpdateNodeLogAsync(FlowNodeLog log);
}
```

The default implementation (`FlowDbLogger`) persists records using the engine's internal `EngineDbContext`. Logging is enabled by default and writes to the engine's own tables (`FlowRun`, `FlowNodeLog`).

---

## Admin API Endpoints

### Flow Runs

**Controller:** `Controllers/BikiranEngineController.cs`
**Route prefix:** `/api/bikiran-engine`

| Method   | Route                                                | Description                                   |
| -------- | ---------------------------------------------------- | --------------------------------------------- |
| `GET`    | `/api/bikiran-engine/runs`                           | List all runs (paginated)                     |
| `GET`    | `/api/bikiran-engine/runs/{serviceId}`               | Get full details of a run including node logs |
| `GET`    | `/api/bikiran-engine/runs/{serviceId}/progress`      | Get live progress percentage                  |
| `GET`    | `/api/bikiran-engine/runs/status/{status}`           | Filter runs by status                         |
| `DELETE` | `/api/bikiran-engine/runs/{serviceId}`               | Soft-delete a run record                      |

### Flow Definitions

**Controller:** `Controllers/BikiranEngineDefinitionController.cs`
**Route prefix:** `/api/bikiran-engine/definitions`

| Method   | Route                                                 | Description                                 |
| -------- | ----------------------------------------------------- | ------------------------------------------- |
| `GET`    | `/api/bikiran-engine/definitions`                     | List all definitions (paginated)            |
| `GET`    | `/api/bikiran-engine/definitions/{key}`               | Get the latest version of a definition      |
| `GET`    | `/api/bikiran-engine/definitions/{key}/versions`      | List all versions of a definition           |
| `POST`   | `/api/bikiran-engine/definitions`                     | Create a new definition                     |
| `PUT`    | `/api/bikiran-engine/definitions/{key}`               | Update a definition (creates a new version) |
| `PATCH`  | `/api/bikiran-engine/definitions/{key}/toggle`        | Enable or disable a definition              |
| `DELETE` | `/api/bikiran-engine/definitions/{key}`               | Soft-delete a definition                    |
| `POST`   | `/api/bikiran-engine/definitions/{key}/trigger`       | Manually trigger a definition run           |
| `GET`    | `/api/bikiran-engine/definitions/{key}/runs`          | List runs triggered from this definition    |

### Flow Schedules

**Controller:** `Controllers/BikiranEngineScheduleController.cs`
**Route prefix:** `/api/bikiran-engine/schedules`

| Method   | Route                                              | Description                                |
| -------- | -------------------------------------------------- | ------------------------------------------ |
| `GET`    | `/api/bikiran-engine/schedules`                    | List all schedules                         |
| `GET`    | `/api/bikiran-engine/schedules/{key}`              | Get schedule detail with next fire time    |
| `POST`   | `/api/bikiran-engine/schedules`                    | Create a new schedule                      |
| `PUT`    | `/api/bikiran-engine/schedules/{key}`              | Update a schedule (re-registers in Quartz) |
| `PATCH`  | `/api/bikiran-engine/schedules/{key}/toggle`       | Enable or disable (pause/resume in Quartz) |
| `DELETE` | `/api/bikiran-engine/schedules/{key}`              | Soft-delete and unregister from Quartz     |
| `POST`   | `/api/bikiran-engine/schedules/{key}/run-now`      | Manually fire the schedule immediately     |
| `GET`    | `/api/bikiran-engine/schedules/{key}/runs`         | List runs triggered by this schedule       |

---

## Example: Run Detail Response

```json
{
  "error": false,
  "message": "Flow run details",
  "data": {
    "serviceId": "a1b2c3d4...",
    "flowName": "order_notification_flow",
    "status": "completed",
    "triggerSource": "OrdersV3Controller",
    "totalNodes": 4,
    "completedNodes": 4,
    "progressPercent": 100,
    "durationMs": 3421,
    "startedAt": 1742000000,
    "completedAt": 1742000003,
    "errorMessage": null,
    "nodeLogs": [
      {
        "sequence": 1,
        "nodeName": "fetch_order",
        "nodeType": "HttpRequest",
        "status": "completed",
        "durationMs": 1200,
        "retryCount": 0,
        "branchTaken": null,
        "errorMessage": null
      }
    ]
  }
}
```

---

## Querying Schedule-Triggered Runs

To find all runs triggered by schedules, filter the `FlowRun` table:

```sql
SELECT * FROM FlowRun WHERE TriggerSource LIKE 'FlowSchedule:%'
```

The `FlowDefinitionRun` table provides additional detail including the `DefinitionKey` and parameters used for each run.
