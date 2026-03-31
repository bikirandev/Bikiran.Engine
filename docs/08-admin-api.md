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
    "currentProgressMessage": null,
    "durationMs": 3421,
    "startedAt": 1742000000,
    "completedAt": 1742000003,
    "errorMessage": null,
    "nodeLogs": [
      {
        "sequence": 1,
        "nodeName": "FetchOrder",
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
    "totalNodes": 4,
    "currentProgressMessage": "Waiting for DNS propagation"
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

### GET /definitions

List all definitions, returning the latest version per definition key, paginated.

**Query Parameters:** `page`, `pageSize`

**Response (200):**

```json
{
  "error": false,
  "message": "Flow definitions",
  "data": [
    {
      "id": 1,
      "definitionKey": "welcome_email_flow",
      "displayName": "Welcome Email Flow",
      "description": "Sends a welcome email to new users",
      "version": 2,
      "isActive": true,
      "flowJson": "{...}",
      "tags": "email,onboarding",
      "parameterSchema": null,
      "lastModifiedBy": 0,
      "timeCreated": 1742000000,
      "timeUpdated": 1742000500,
      "timeDeleted": 0
    }
  ]
}
```

### GET /definitions/{key}

Get the latest version of a specific definition.

**Response (200):**

```json
{
  "error": false,
  "data": {
    "id": 2,
    "definitionKey": "welcome_email_flow",
    "displayName": "Welcome Email Flow",
    "description": "Sends a welcome email to new users",
    "version": 2,
    "isActive": true,
    "flowJson": "{...}",
    "tags": "email,onboarding",
    "parameterSchema": null,
    "lastModifiedBy": 0,
    "timeCreated": 1742000000,
    "timeUpdated": 1742000500,
    "timeDeleted": 0
  }
}
```

**Response (404):**

```json
{
  "error": true,
  "code": "DEFINITION_NOT_FOUND",
  "message": "Definition not found"
}
```

### GET /definitions/{key}/versions

List all versions of a definition, ordered by version descending.

**Response (200):**

```json
{
  "error": false,
  "data": [
    {
      "id": 2,
      "definitionKey": "welcome_email_flow",
      "displayName": "Welcome Email Flow",
      "version": 2,
      "isActive": true,
      "flowJson": "{...}",
      "tags": "email,onboarding",
      "parameterSchema": null,
      "timeCreated": 1742000500,
      "timeUpdated": 1742000500,
      "timeDeleted": 0
    },
    {
      "id": 1,
      "definitionKey": "welcome_email_flow",
      "displayName": "Welcome Email",
      "version": 1,
      "isActive": false,
      "flowJson": "{...}",
      "tags": "email",
      "parameterSchema": null,
      "timeCreated": 1742000000,
      "timeUpdated": 1742000500,
      "timeDeleted": 0
    }
  ]
}
```

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

**Request Body:** Same fields as `POST /definitions` (except `definitionKey` which comes from the URL).

**Optional Headers:**

| Header     | Description                                                      |
| ---------- | ---------------------------------------------------------------- |
| `If-Match` | Expected current version (e.g., `"3"`). Returns 409 on mismatch. |

**Response Headers:**

| Header | Description                          |
| ------ | ------------------------------------ |
| `ETag` | The new version number (e.g., `"2"`) |

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

### PATCH /definitions/{key}/toggle

Toggle a definition between active and inactive. Inactive definitions cannot be triggered.

**Response (200):**

```json
{ "error": false, "message": "Definition is now inactive" }
```

### DELETE /definitions/{key}

Soft-delete all versions of a definition. Sets `TimeDeleted` to the current Unix timestamp on every version.

**Response (200):**

```json
{ "error": false, "message": "Definition deleted" }
```

**Response (404):**

```json
{
  "error": true,
  "code": "DEFINITION_NOT_FOUND",
  "message": "Definition not found"
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

**Response (400):**

```json
{
  "error": true,
  "code": "TRIGGER_FAILED",
  "message": "Definition is inactive or not found"
}
```

### POST /definitions/{key}/dry-run

Validate and parse a definition with parameters without executing. Useful for testing parameter substitution and flow structure.

**Request Body:** Same as `POST /definitions/{key}/trigger`.

**Response (200):**

```json
{
  "error": false,
  "code": "VALIDATION_RESULT",
  "message": "Dry-run succeeded",
  "data": {
    "dryRunId": "dry-run:welcome_email_flow:v2"
  }
}
```

**Response (400):**

```json
{
  "error": true,
  "code": "TRIGGER_FAILED",
  "message": "Definition is inactive or not found"
}
```

### GET /definitions/{key}/runs

List all runs triggered from this definition, paginated.

**Query Parameters:** `page`, `pageSize`

**Response (200):**

```json
{
  "error": false,
  "data": [
    {
      "id": 1,
      "flowRunServiceId": "f8c2a1d3-9b4e-4f5a-8c7d-1234567890ab",
      "definitionId": 2,
      "definitionKey": "welcome_email_flow",
      "definitionVersion": 2,
      "parameters": "{\"email\": \"user@example.com\"}",
      "triggerUserId": 0,
      "triggerSource": "AdminPanel",
      "timeCreated": 1742000100
    }
  ]
}
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

**Response (400):**

```json
{
  "error": true,
  "code": "INVALID_FLOW_JSON",
  "message": "Validation failed",
  "data": ["Node 'fetch_data' is missing required field: url"]
}
```

### PATCH /definitions/{key}/versions/{ver}/activate

Activate a specific version and deactivate all other versions.

**Response (200):**

```json
{
  "error": false,
  "message": "Version 2 is now the active version for 'welcome_email_flow'"
}
```

**Response (404):**

```json
{
  "error": true,
  "code": "VERSION_NOT_FOUND",
  "message": "Version 3 not found for 'welcome_email_flow'"
}
```

### GET /definitions/{key}/versions/diff?v1=&v2=

Compare two versions of a definition side-by-side.

**Query Parameters:**

| Parameter | Type | Required | Description           |
| --------- | ---- | -------- | --------------------- |
| `v1`      | int  | Yes      | First version number  |
| `v2`      | int  | Yes      | Second version number |

**Response (200):**

```json
{
  "error": false,
  "data": {
    "key": "welcome_email_flow",
    "version1": {
      "version": 1,
      "flowJson": "{...}",
      "parameterSchema": null,
      "displayName": "Welcome Email",
      "isActive": false,
      "timeCreated": 1742000000
    },
    "version2": {
      "version": 2,
      "flowJson": "{...}",
      "parameterSchema": null,
      "displayName": "Welcome Email Flow",
      "isActive": true,
      "timeCreated": 1742000500
    }
  }
}
```

**Response (404):**

```json
{
  "error": true,
  "code": "VERSION_NOT_FOUND",
  "message": "One or both versions not found"
}
```

### GET /definitions/{key}/export

Export a single definition (latest version) as a portable JSON document.

**Response (200):**

```json
{
  "error": false,
  "data": {
    "_exportVersion": "1.0",
    "definitionKey": "welcome_email_flow",
    "displayName": "Welcome Email Flow",
    "description": "Sends a welcome email to new users",
    "flowJson": "{...}",
    "tags": "email,onboarding",
    "parameterSchema": null,
    "version": 2,
    "exportedAt": 1742001000
  }
}
```

**Response (404):**

```json
{
  "error": true,
  "code": "DEFINITION_NOT_FOUND",
  "message": "Definition not found"
}
```

### GET /definitions/export-all

Export all definitions (latest version of each) as a single bundle.

**Response (200):**

```json
{
  "error": false,
  "data": {
    "exportedAt": 1742001000,
    "definitions": [
      {
        "_exportVersion": "1.0",
        "definitionKey": "welcome_email_flow",
        "displayName": "Welcome Email Flow",
        "description": "Sends a welcome email to new users",
        "flowJson": "{...}",
        "tags": "email,onboarding",
        "parameterSchema": null,
        "version": 2
      }
    ]
  }
}
```

### POST /definitions/import

Import a definition from an exported JSON document. If the definition key already exists, a new version is created automatically.

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
  "message": "Definition imported as v1",
  "data": {
    "id": 3,
    "definitionKey": "order_notification",
    "displayName": "Order Notification",
    "version": 1,
    "isActive": true
  }
}
```

**Response (400 — missing key):**

```json
{
  "error": true,
  "code": "KEY_REQUIRED",
  "message": "DefinitionKey is required"
}
```

**Response (400 — invalid flow):**

```json
{
  "error": true,
  "code": "IMPORT_FAILED",
  "message": "Import validation failed",
  "data": ["Root element must contain a 'name' property"]
}
```

### POST /definitions/extract-parameters

Extract all `{{placeholder}}` parameter names from a FlowJson string. Returns a sorted, deduplicated list.

**Request Body:**

```json
{
  "flowJson": "{\"name\": \"test\", \"nodes\": [{\"type\": \"HttpRequest\", \"name\": \"call\", \"params\": {\"url\": \"https://api.example.com/{{endpoint}}\", \"headers\": {\"Authorization\": \"Bearer {{api_token}}\"}}}]}"
}
```

**Response (200):**

```json
{
  "error": false,
  "data": ["api_token", "endpoint"]
}
```

---

## Flow Schedules

Endpoints for creating and managing automated flow triggers.

**Route prefix:** `/api/bikiran-engine/schedules`

### Endpoints

| Method   | Route                      | Description                          |
| -------- | -------------------------- | ------------------------------------ |
| `GET`    | `/schedules`               | List all schedules                   |
| `GET`    | `/schedules/{key}`         | Get a specific schedule              |
| `POST`   | `/schedules`               | Create a new schedule                |
| `PUT`    | `/schedules/{key}`         | Update an existing schedule          |
| `PATCH`  | `/schedules/{key}/toggle`  | Enable or disable a schedule         |
| `DELETE` | `/schedules/{key}`         | Soft-delete a schedule               |
| `POST`   | `/schedules/{key}/run-now` | Trigger a schedule immediately       |
| `GET`    | `/schedules/{key}/runs`    | List runs triggered by this schedule |

### GET /schedules

List all schedules, ordered by most recent first.

**Response (200):**

```json
{
  "error": false,
  "data": [
    {
      "id": 1,
      "scheduleKey": "daily_report",
      "displayName": "Daily Report",
      "definitionKey": "report_flow",
      "scheduleType": "cron",
      "cronExpression": "0 0 8 * * ?",
      "intervalMinutes": null,
      "runOnceAt": null,
      "defaultParameters": "{\"format\": \"pdf\"}",
      "isActive": true,
      "timeZone": "UTC",
      "maxConcurrent": 1,
      "lastRunAt": 1742000000,
      "nextRunAt": 1742086400,
      "lastRunServiceId": "a1b2c3d4-...",
      "lastRunStatus": "completed",
      "createdBy": 0,
      "timeCreated": 1741900000,
      "timeUpdated": 1742000000,
      "timeDeleted": 0
    }
  ]
}
```

### GET /schedules/{key}

Get schedule details including the next calculated fire time.

**Response (200):**

```json
{
  "error": false,
  "data": {
    "id": 1,
    "scheduleKey": "daily_report",
    "displayName": "Daily Report",
    "definitionKey": "report_flow",
    "scheduleType": "cron",
    "isActive": true,
    "lastRunAt": 1742000000,
    "lastRunStatus": "completed",
    "lastRunServiceId": "a1b2c3d4-...",
    "nextRunAt": 1742086400
  }
}
```

**Response (404):**

```json
{ "error": true, "message": "Schedule not found" }
```

### POST /schedules

Create a new schedule. The schedule is automatically registered with the scheduler on creation.

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

**Response (200):**

```json
{
  "error": false,
  "message": "Schedule created",
  "data": {
    "id": 1,
    "scheduleKey": "daily_report",
    "displayName": "Daily Report",
    "definitionKey": "report_flow",
    "scheduleType": "cron",
    "cronExpression": "0 0 8 * * ?",
    "isActive": true,
    "timeZone": "UTC",
    "maxConcurrent": 1
  }
}
```

**Response (400):**

```json
{ "error": true, "message": "ScheduleKey is required" }
```

### PUT /schedules/{key}

Update an existing schedule. The schedule is re-registered with the scheduler after update.

**Request Body:** Same fields as `POST /schedules`.

**Response (200):**

```json
{ "error": false, "message": "Schedule updated" }
```

**Response (404):**

```json
{ "error": true, "message": "Schedule not found" }
```

### PATCH /schedules/{key}/toggle

Enable or disable a schedule. Disabling unregisters it from the scheduler; enabling re-registers it.

**Response (200):**

```json
{ "error": false, "message": "Schedule is now paused" }
```

**Response (404):**

```json
{ "error": true, "message": "Schedule not found" }
```

### DELETE /schedules/{key}

Soft-delete a schedule and unregister it from the scheduler.

**Response (200):**

```json
{ "error": false, "message": "Schedule deleted" }
```

**Response (404):**

```json
{ "error": true, "message": "Schedule not found" }
```

### POST /schedules/{key}/run-now

Trigger a schedule immediately, bypassing the normal cron/interval timing. If the job is not currently registered, it will be registered first.

**Response (200):**

```json
{ "error": false, "message": "Schedule triggered immediately" }
```

**Response (404):**

```json
{ "error": true, "message": "Schedule not found" }
```

### GET /schedules/{key}/runs

List all runs triggered by this schedule, paginated. Matches runs where `triggerSource` is `FlowSchedule:{key}`.

**Query Parameters:** `page`, `pageSize`

**Response (200):**

```json
{
  "error": false,
  "data": [
    {
      "id": 5,
      "serviceId": "b2c3d4e5-f6a7-8901-bcde-234567890abc",
      "flowName": "report_flow",
      "status": "completed",
      "triggerSource": "FlowSchedule:daily_report",
      "totalNodes": 3,
      "completedNodes": 3,
      "durationMs": 2100,
      "startedAt": 1742000000,
      "completedAt": 1742000002,
      "errorMessage": null,
      "timeCreated": 1742000000,
      "timeUpdated": 1742000002,
      "timeDeleted": 0
    }
  ]
}
```

---

## Error Codes

The API uses specific error codes in failure responses for programmatic handling:

| Code                      | HTTP Status | Meaning                                                    |
| ------------------------- | ----------- | ---------------------------------------------------------- |
| `DEFINITION_NOT_FOUND`    | 404         | The requested definition key does not exist                |
| `DEFINITION_INACTIVE`     | 400         | The definition is disabled and cannot be triggered         |
| `INVALID_FLOW_JSON`       | 400         | The FlowJson failed validation                             |
| `VERSION_CONFLICT`        | 409         | Another update occurred since you last read the definition |
| `VERSION_NOT_FOUND`       | 404         | The requested version does not exist                       |
| `VALIDATION_RESULT`       | 200/400     | Result of a FlowJson validation check                      |
| `KEY_REQUIRED`            | 400         | The `definitionKey` field is required but was empty        |
| `PARAMETER_REQUIRED`      | 400         | A required parameter was not supplied                      |
| `PARAMETER_TYPE_MISMATCH` | 400         | A parameter value does not match the expected type         |
| `TRIGGER_FAILED`          | 400         | Flow execution could not be started                        |
| `IMPORT_FAILED`           | 400         | The imported definition failed validation                  |
| `RUN_NOT_FOUND`           | 404         | The requested flow run does not exist                      |
| `SCHEDULE_NOT_FOUND`      | 404         | The requested schedule does not exist                      |
