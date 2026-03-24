# Admin API

Bikiran.Engine includes built-in REST endpoints for managing flow runs, definitions, and schedules. All routes are under the `/api/bikiran-engine` prefix.

---

## Setup

Activate the admin API in your `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddBikiranEngine(options =>
{
    options.ConnectionString = "...";
    options.EnableNodeLogging = true;

    // Register credentials
    options.AddCredential("smtp_main", new SmtpCredential { /* ... */ });

    // Register custom node types for JSON definitions
    options.RegisterNode<MyCustomNode>("MyCustom");
},
dbOptions => dbOptions.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

var app = builder.Build();
app.MapBikiranEngineEndpoints();   // activates all /api/bikiran-engine/* routes
app.Run();
```

---

## Conventions

### Base URL

All endpoints are prefixed with:

```
/api/bikiran-engine
```

### Response Format

Every response follows a consistent structure:

```json
{
  "error": false,
  "message": "Human-readable summary",
  "data": {}
}
```

| Field     | Type            | Description                           |
| --------- | --------------- | ------------------------------------- |
| `error`   | bool            | `false` on success, `true` on failure |
| `message` | string          | Short description of the result       |
| `data`    | object or array | Response payload                      |

### Pagination

Paginated endpoints accept these query parameters:

| Parameter  | Type | Default | Description             |
| ---------- | ---- | ------- | ----------------------- |
| `page`     | int  | `1`     | Page number (1-indexed) |
| `pageSize` | int  | `20`    | Items per page          |

### Soft Deletes

Records are never physically deleted. A `TimeDeleted` field (Unix timestamp) is set to a non-zero value. All list and get endpoints filter out deleted records automatically.

### Timestamps

All timestamp fields use **Unix seconds** (e.g., `1742000000`). Duration fields (`DurationMs`) use **milliseconds**.

---

## Flow Runs

Endpoints for viewing and managing individual flow executions.

**Route prefix:** `/api/bikiran-engine/runs`

### Endpoints

| Method   | Route                        | Description                                  |
| -------- | ---------------------------- | -------------------------------------------- |
| `GET`    | `/runs`                      | List all runs (paginated)                    |
| `GET`    | `/runs/{serviceId}`          | Get full details of a run with all step logs |
| `GET`    | `/runs/{serviceId}/progress` | Get current progress percentage              |
| `GET`    | `/runs/status/{status}`      | Filter runs by status (paginated)            |
| `DELETE` | `/runs/{serviceId}`          | Soft-delete a run record                     |

### GET /runs

List all flow runs, paginated and ordered by most recent first.

**Query Parameters:** `page`, `pageSize`

**Response (200):**

```json
{
  "error": false,
  "message": "Flow runs",
  "data": [
    {
      "id": 1,
      "serviceId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "flowName": "order_notification_flow",
      "status": "completed",
      "triggerSource": "OrdersV3Controller",
      "totalNodes": 4,
      "completedNodes": 4,
      "durationMs": 3421,
      "startedAt": 1742000000,
      "completedAt": 1742000003,
      "errorMessage": null,
      "timeCreated": 1742000000,
      "timeUpdated": 1742000003,
      "timeDeleted": 0
    }
  ]
}
```

### GET /runs/{serviceId}

Get full details of a single run including all step execution logs.

**Response (200):**

```json
{
  "error": false,
  "message": "Flow run details",
  "data": {
    "serviceId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
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
        "errorMessage": null,
        "inputData": null,
        "outputData": "{\"orderId\": 123}",
        "startedAt": 1742000000,
        "completedAt": 1742000001
      }
    ]
  }
}
```

**Response (404):**

```json
{ "error": true, "message": "Flow run not found" }
```

### GET /runs/{serviceId}/progress

Get the current progress of a running (or completed) flow.

**Response (200):**

```json
{
  "error": false,
  "data": {
    "serviceId": "a1b2c3d4...",
    "status": "running",
    "percent": 75,
    "completedNodes": 3,
    "totalNodes": 4
  }
}
```

### GET /runs/status/{status}

Filter runs by status. Valid values: `pending`, `running`, `completed`, `failed`, `cancelled`.

**Query Parameters:** `page`, `pageSize`

### DELETE /runs/{serviceId}

Soft-delete a run record. Sets `TimeDeleted` to the current Unix timestamp.

**Response (200):**

```json
{ "error": false, "message": "Flow run deleted" }
```

---

## Flow Definitions

Endpoints for creating, updating, validating, versioning, and triggering database-stored flow templates.

**Route prefix:** `/api/bikiran-engine/definitions`

### Endpoints

| Method   | Route                                        | Description                               |
| -------- | -------------------------------------------- | ----------------------------------------- |
| `GET`    | `/definitions`                               | List all definitions (paginated)          |
| `GET`    | `/definitions/{key}`                         | Get the latest version of a definition    |
| `GET`    | `/definitions/{key}/versions`                | List all versions of a definition         |
| `POST`   | `/definitions`                               | Create a new definition (v1)              |
| `PUT`    | `/definitions/{key}`                         | Update a definition (creates new version) |
| `PATCH`  | `/definitions/{key}/toggle`                  | Enable or disable a definition            |
| `DELETE` | `/definitions/{key}`                         | Soft-delete all versions                  |
| `POST`   | `/definitions/{key}/trigger`                 | Trigger with runtime parameters           |
| `POST`   | `/definitions/{key}/dry-run`                 | Validate without executing                |
| `GET`    | `/definitions/{key}/runs`                    | List runs triggered from this definition  |
| `POST`   | `/definitions/validate`                      | Validate FlowJson without saving          |
| `PATCH`  | `/definitions/{key}/versions/{ver}/activate` | Activate a specific version               |
| `GET`    | `/definitions/{key}/versions/diff?v1=&v2=`   | Compare two versions                      |
| `GET`    | `/definitions/{key}/export`                  | Export a definition                       |
| `GET`    | `/definitions/export-all`                    | Export all definitions                    |
| `POST`   | `/definitions/import`                        | Import a definition                       |
| `POST`   | `/definitions/extract-parameters`            | Extract placeholder names from FlowJson   |

### POST /definitions

Create a new flow definition (starts at version 1).

**Request Body:**

| Field             | Type   | Required | Description                              |
| ----------------- | ------ | -------- | ---------------------------------------- |
| `definitionKey`   | string | Yes      | Unique slug (e.g., `order_notification`) |
| `displayName`     | string | No       | Human-readable label                     |
| `description`     | string | No       | What this flow does                      |
| `flowJson`        | string | No       | JSON string describing the flow          |
| `tags`            | string | No       | Comma-separated tags                     |
| `parameterSchema` | string | No       | JSON schema for runtime parameters       |

**Response (200):**

```json
{
  "error": false,
  "message": "Definition created",
  "data": {
    "id": 1,
    "definitionKey": "welcome_email_flow",
    "displayName": "Welcome Email Flow",
    "version": 1,
    "isActive": true
  }
}
```

### PUT /definitions/{key}

Update a definition. This creates a **new version** rather than modifying the existing record.

**Optional Headers:**

| Header     | Description                                                      |
| ---------- | ---------------------------------------------------------------- |
| `If-Match` | Expected current version (e.g., `"3"`). Returns 409 on mismatch. |

**Response (200):**

```json
{
  "error": false,
  "message": "Definition updated to v2",
  "data": {
    "id": 2,
    "definitionKey": "welcome_email_flow",
    "version": 2
  }
}
```

**Response (409 — version conflict):**

```json
{
  "error": true,
  "code": "VERSION_CONFLICT",
  "message": "Version conflict: expected v1, current is v2"
}
```

### POST /definitions/{key}/trigger

Trigger execution of a definition with runtime parameters. Uses the latest active version.

**Request Body:**

| Field           | Type                         | Required | Description                                             |
| --------------- | ---------------------------- | -------- | ------------------------------------------------------- |
| `parameters`    | Dictionary\<string, string\> | No       | Values that replace `{{placeholders}}` in the flow JSON |
| `triggerSource` | string                       | No       | Label identifying the trigger origin                    |

**Automatic system parameters** (injected without providing them):

| Placeholder      | Value                       |
| ---------------- | --------------------------- |
| `{{today_date}}` | Current date (`YYYY-MM-DD`) |
| `{{unix_now}}`   | Current Unix timestamp      |
| `{{year}}`       | Current year                |
| `{{month}}`      | Current month               |

**Response (200):**

```json
{
  "error": false,
  "message": "Definition triggered",
  "data": {
    "serviceId": "f8c2a1d3-9b4e-4f5a-8c7d-1234567890ab",
    "definitionKey": "welcome_email_flow",
    "definitionVersion": 2,
    "flowName": "Welcome Email Flow"
  }
}
```

> The trigger returns immediately. Use the `serviceId` with the Flow Runs endpoints to track progress.

### PATCH /definitions/{key}/toggle

Toggle a definition between active and inactive. Inactive definitions cannot be triggered.

**Response (200):**

```json
{ "error": false, "message": "Definition is now inactive" }
```

### POST /definitions/validate

Validate a FlowJson structure without saving. Useful for editor integrations.

**Request Body:**

```json
{ "flowJson": "{\"name\": \"test\", \"nodes\": [...]}" }
```

**Response (200):**

```json
{ "error": false, "code": "VALIDATION_RESULT", "message": "FlowJson is valid" }
```

---

## Flow Schedules

Endpoints for creating and managing automated flow triggers.

**Route prefix:** `/api/bikiran-engine/schedules`

### Endpoints

| Method   | Route                     | Description                    |
| -------- | ------------------------- | ------------------------------ |
| `GET`    | `/schedules`              | List all schedules (paginated) |
| `GET`    | `/schedules/{key}`        | Get a specific schedule        |
| `POST`   | `/schedules`              | Create a new schedule          |
| `PUT`    | `/schedules/{key}`        | Update an existing schedule    |
| `PATCH`  | `/schedules/{key}/toggle` | Enable or disable a schedule   |
| `DELETE` | `/schedules/{key}`        | Soft-delete a schedule         |

### POST /schedules

Create a new schedule.

**Request Body:**

| Field               | Type   | Required     | Description                           |
| ------------------- | ------ | ------------ | ------------------------------------- |
| `scheduleKey`       | string | Yes          | Unique schedule identifier            |
| `displayName`       | string | Yes          | Human-readable label                  |
| `definitionKey`     | string | Yes          | Flow definition to trigger            |
| `scheduleType`      | string | Yes          | `"cron"`, `"interval"`, or `"once"`   |
| `cronExpression`    | string | For cron     | Quartz cron expression                |
| `intervalMinutes`   | int    | For interval | Repeat interval in minutes            |
| `runOnceAt`         | long   | For once     | Unix timestamp for one-time execution |
| `defaultParameters` | object | No           | Key-value pairs passed on trigger     |
| `timeZone`          | string | No           | IANA timezone ID (default: `"UTC"`)   |
| `maxConcurrent`     | int    | No           | Maximum concurrent runs (default: 1)  |

---

## Error Codes

The API uses specific error codes in failure responses for programmatic handling:

| Code                   | HTTP Status | Meaning                                                    |
| ---------------------- | ----------- | ---------------------------------------------------------- |
| `DEFINITION_NOT_FOUND` | 404         | The requested definition key does not exist                |
| `DEFINITION_INACTIVE`  | 400         | The definition is disabled and cannot be triggered         |
| `INVALID_FLOW_JSON`    | 400         | The FlowJson failed validation                             |
| `VERSION_CONFLICT`     | 409         | Another update occurred since you last read the definition |
| `VALIDATION_RESULT`    | 200/400     | Result of a FlowJson validation check                      |
| `RUN_NOT_FOUND`        | 404         | The requested flow run does not exist                      |
| `SCHEDULE_NOT_FOUND`   | 404         | The requested schedule does not exist                      |
