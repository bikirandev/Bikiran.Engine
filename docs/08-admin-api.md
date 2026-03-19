# Admin API

Bikiran.Engine includes built-in REST endpoints for managing flow runs, definitions, and schedules. All routes are under the `/api/bikiran-engine` prefix and are activated by calling `app.MapBikiranEngineEndpoints()` in your startup code.

---

## Flow Runs

Endpoints for viewing and managing individual flow executions.

**Route prefix:** `/api/bikiran-engine/runs`

| Method   | Route                        | Description                                                                      |
| -------- | ---------------------------- | -------------------------------------------------------------------------------- |
| `GET`    | `/runs`                      | List all runs (paginated)                                                        |
| `GET`    | `/runs/{serviceId}`          | Get full details of a run, including all node logs                               |
| `GET`    | `/runs/{serviceId}/progress` | Get the current progress percentage                                              |
| `GET`    | `/runs/status/{status}`      | Filter runs by status (`pending`, `running`, `completed`, `failed`, `cancelled`) |
| `DELETE` | `/runs/{serviceId}`          | Soft-delete a run record                                                         |

### Example: Run Detail Response

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

## Flow Definitions

Endpoints for creating, updating, and triggering database-stored flow templates.

**Route prefix:** `/api/bikiran-engine/definitions`

| Method   | Route                         | Description                                        |
| -------- | ----------------------------- | -------------------------------------------------- |
| `GET`    | `/definitions`                | List all definitions (paginated)                   |
| `GET`    | `/definitions/{key}`          | Get the latest version of a definition             |
| `GET`    | `/definitions/{key}/versions` | List all versions of a definition                  |
| `POST`   | `/definitions`                | Create a new definition                            |
| `PUT`    | `/definitions/{key}`          | Update a definition (creates a new version)        |
| `PATCH`  | `/definitions/{key}/toggle`   | Enable or disable a definition                     |
| `DELETE` | `/definitions/{key}`          | Soft-delete a definition                           |
| `POST`   | `/definitions/{key}/trigger`  | Trigger a definition with parameters               |
| `GET`    | `/definitions/{key}/runs`     | List runs that were triggered from this definition |

### Example: Create a Definition

```http
POST /api/bikiran-engine/definitions
Content-Type: application/json

{
  "definitionKey": "welcome_email_flow",
  "displayName": "Welcome Email Flow",
  "description": "Sends a welcome email to a new user after a brief delay",
  "tags": "auth,email,onboarding",
  "flowJson": "{\"name\":\"welcome_email_flow\",\"config\":{\"maxExecutionTimeSeconds\":60,\"onFailure\":\"Continue\"},\"nodes\":[{\"type\":\"Wait\",\"name\":\"brief_delay\",\"params\":{\"delayMs\":500}},{\"type\":\"EmailSend\",\"name\":\"send_welcome\",\"params\":{\"toEmail\":\"{{email}}\",\"toName\":\"{{name}}\",\"subject\":\"Welcome!\",\"template\":\"AUTH_CREATE_ACCOUNT\",\"placeholders\":{\"DisplayName\":\"{{name}}\",\"Email\":\"{{email}}\"}}}]}"
}
```

### Example: Trigger a Definition

```http
POST /api/bikiran-engine/definitions/welcome_email_flow/trigger
Content-Type: application/json

{
  "parameters": {
    "email": "user@example.com",
    "name": "Jane Doe"
  },
  "triggerSource": "AdminPanel"
}
```

---

## Flow Schedules

Endpoints for creating, updating, and managing automated flow triggers.

**Route prefix:** `/api/bikiran-engine/schedules`

| Method   | Route                      | Description                                   |
| -------- | -------------------------- | --------------------------------------------- |
| `GET`    | `/schedules`               | List all schedules                            |
| `GET`    | `/schedules/{key}`         | Get schedule details including next fire time |
| `POST`   | `/schedules`               | Create a new schedule                         |
| `PUT`    | `/schedules/{key}`         | Update a schedule (re-registers in Quartz)    |
| `PATCH`  | `/schedules/{key}/toggle`  | Enable or disable (pause/resume in Quartz)    |
| `DELETE` | `/schedules/{key}`         | Soft-delete and unregister from Quartz        |
| `POST`   | `/schedules/{key}/run-now` | Trigger the schedule immediately              |
| `GET`    | `/schedules/{key}/runs`    | List runs triggered by this schedule          |

### Example: Create a Cron Schedule

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

---

## Finding Schedule-Triggered Runs

To find all runs that were triggered by schedules, filter by the `TriggerSource` field:

```sql
SELECT * FROM FlowRun WHERE TriggerSource LIKE 'FlowSchedule:%'
```

The `FlowDefinitionRun` table provides more detail, including the specific definition key and parameters used for each run.

---

## Logging Details

### What Gets Recorded for Each Run

| Moment                | Status      | Fields Updated                                       |
| --------------------- | ----------- | ---------------------------------------------------- |
| `StartAsync()` called | `pending`   | ServiceId, FlowName, Config, TotalNodes, ContextMeta |
| Execution begins      | `running`   | StartedAt                                            |
| All nodes complete    | `completed` | CompletedAt, DurationMs                              |
| A failure occurs      | `failed`    | ErrorMessage, CompletedAt                            |

### What Gets Recorded for Each Node

| Moment          | Status      | Fields Updated                      |
| --------------- | ----------- | ----------------------------------- |
| Node starts     | `running`   | StartedAt, Sequence, InputData      |
| Node succeeds   | `completed` | OutputData, CompletedAt, DurationMs |
| Node fails      | `failed`    | ErrorMessage, RetryCount            |
| Node is skipped | `skipped`   | (no timing data)                    |
| IfElse resolves | `completed` | BranchTaken (`"true"` or `"false"`) |
