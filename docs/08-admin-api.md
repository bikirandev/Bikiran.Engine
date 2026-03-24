# Admin API Reference

Bikiran.Engine includes built-in REST endpoints for managing flow runs, definitions, and schedules. All routes are under the `/api/bikiran-engine` prefix.

---

## Table of Contents

1. [Setup](#setup)
2. [Conventions](#conventions)
3. [Flow Runs](#1-flow-runs)
4. [Flow Definitions](#2-flow-definitions)
5. [Flow Schedules](#3-flow-schedules)
6. [Logging & Observability](#4-logging--observability)
7. [Error Reference](#5-error-reference)

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

### Response Envelope

Every response follows a consistent envelope:

```json
{
  "error": false,
  "message": "Human-readable summary",
  "data": { }
}
```

| Field     | Type          | Description                                       |
| --------- | ------------- | ------------------------------------------------- |
| `error`   | `bool`        | `false` on success, `true` on failure             |
| `message` | `string`      | Short description of the result                   |
| `data`    | `object/array`| Response payload (omitted on some error responses)|

### Pagination

Paginated endpoints accept these query parameters:

| Parameter  | Type  | Default | Description                 |
| ---------- | ----- | ------- | --------------------------- |
| `page`     | `int` | `1`     | Page number (1-indexed)     |
| `pageSize` | `int` | `20`    | Items per page              |

### Soft Deletes

Records are never physically deleted. A `TimeDeleted` field (Unix timestamp) is set to a non-zero value. All list/get endpoints filter by `TimeDeleted == 0` automatically.

### Timestamps

All timestamp fields use **Unix seconds** (e.g., `1742000000`). Duration fields (`DurationMs`) use **milliseconds**.

---

## 1. Flow Runs

Endpoints for viewing and managing individual flow executions.

**Route prefix:** `/api/bikiran-engine/runs`

### Endpoint Summary

| Method   | Route                        | Description                                             |
| -------- | ---------------------------- | ------------------------------------------------------- |
| `GET`    | `/runs`                      | List all runs (paginated)                               |
| `GET`    | `/runs/{serviceId}`          | Get full details of a run, including all node logs      |
| `GET`    | `/runs/{serviceId}/progress` | Get the current progress percentage                     |
| `GET`    | `/runs/status/{status}`      | Filter runs by status (paginated)                       |
| `DELETE` | `/runs/{serviceId}`          | Soft-delete a run record                                |

---

### GET `/runs`

List all flow runs, paginated and ordered by most recent first.

**Query Parameters:** `page`, `pageSize`

**Response** `200 OK`:

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

---

### GET `/runs/{serviceId}`

Get full details of a single run including all node execution logs.

**Path Parameters:**

| Parameter   | Type     | Description                    |
| ----------- | -------- | ------------------------------ |
| `serviceId` | `string` | UUID identifying the flow run  |

**Response** `200 OK`:

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

**Response** `404 Not Found`:

```json
{ "error": true, "message": "Flow run not found" }
```

---

### GET `/runs/{serviceId}/progress`

Get the current progress percentage of a running (or completed) flow.

**Path Parameters:** `serviceId` (string)

**Response** `200 OK`:

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

---

### GET `/runs/status/{status}`

Filter runs by status, paginated and ordered by most recent first.

**Path Parameters:**

| Parameter | Type     | Valid values                                          |
| --------- | -------- | ----------------------------------------------------- |
| `status`  | `string` | `pending`, `running`, `completed`, `failed`, `cancelled` |

**Query Parameters:** `page`, `pageSize`

**Response** `200 OK`:

```json
{
  "error": false,
  "message": "Runs with status 'running'",
  "data": [ /* FlowRun objects */ ]
}
```

---

### DELETE `/runs/{serviceId}`

Soft-delete a run record. Sets `TimeDeleted` to the current Unix timestamp.

**Path Parameters:** `serviceId` (string)

**Response** `200 OK`:

```json
{ "error": false, "message": "Flow run deleted" }
```

**Response** `404 Not Found`:

```json
{ "error": true, "message": "Flow run not found" }
```

---

## 2. Flow Definitions

Endpoints for creating, updating, and triggering database-stored flow templates. Definitions are versioned — each update creates a new version while preserving the full history.

**Route prefix:** `/api/bikiran-engine/definitions`

### Endpoint Summary

| Method   | Route                         | Description                                        |
| -------- | ----------------------------- | -------------------------------------------------- |
| `GET`    | `/definitions`                | List all definitions (paginated)                   |
| `GET`    | `/definitions/{key}`          | Get the latest version of a definition             |
| `GET`    | `/definitions/{key}/versions` | List all versions of a definition                  |
| `POST`   | `/definitions`                | Create a new definition (v1)                       |
| `PUT`    | `/definitions/{key}`          | Update a definition (auto-increments version)      |
| `PATCH`  | `/definitions/{key}/toggle`   | Enable or disable a definition                     |
| `DELETE` | `/definitions/{key}`          | Soft-delete all versions of a definition           |
| `POST`   | `/definitions/{key}/trigger`  | Trigger a definition with runtime parameters       |
| `GET`    | `/definitions/{key}/runs`     | List runs triggered from this definition           |

---

### GET `/definitions`

List all definitions (latest version per key), paginated.

**Query Parameters:** `page`, `pageSize`

**Response** `200 OK`:

```json
{
  "error": false,
  "message": "Flow definitions",
  "data": [
    {
      "id": 1,
      "definitionKey": "welcome_email_flow",
      "displayName": "Welcome Email Flow",
      "description": "Sends a welcome email after signup",
      "version": 2,
      "isActive": true,
      "flowJson": "{...}",
      "tags": "auth,email,onboarding",
      "lastModifiedBy": 0,
      "timeCreated": 1742000000,
      "timeUpdated": 1742000500,
      "timeDeleted": 0
    }
  ]
}
```

---

### GET `/definitions/{key}`

Get the latest version of a specific definition.

**Path Parameters:**

| Parameter | Type     | Description                                  |
| --------- | -------- | -------------------------------------------- |
| `key`     | `string` | Unique slug (e.g., `welcome_email_flow`)      |

**Response** `200 OK`:

```json
{
  "error": false,
  "data": {
    "id": 2,
    "definitionKey": "welcome_email_flow",
    "displayName": "Welcome Email Flow",
    "description": "Sends a welcome email after signup",
    "version": 2,
    "isActive": true,
    "flowJson": "{...}",
    "tags": "auth,email,onboarding",
    "lastModifiedBy": 0,
    "timeCreated": 1742000500,
    "timeUpdated": 1742000500,
    "timeDeleted": 0
  }
}
```

**Response** `404 Not Found`:

```json
{ "error": true, "message": "Definition not found" }
```

---

### GET `/definitions/{key}/versions`

List all versions of a definition, ordered by most recent first.

**Path Parameters:** `key` (string)

**Response** `200 OK`:

```json
{
  "error": false,
  "data": [
    { "id": 2, "definitionKey": "welcome_email_flow", "version": 2, "isActive": true, "...": "..." },
    { "id": 1, "definitionKey": "welcome_email_flow", "version": 1, "isActive": true, "...": "..." }
  ]
}
```

---

### POST `/definitions`

Create a new flow definition (starts at version 1).

**Request Body** (`FlowDefinitionSaveRequestDTO`):

| Field           | Type     | Required | Description                                     |
| --------------- | -------- | -------- | ----------------------------------------------- |
| `definitionKey` | `string` | Yes      | Unique slug (e.g., `order_notification`)        |
| `displayName`   | `string` | No       | Human-readable label                            |
| `description`   | `string` | No       | What this flow does                             |
| `flowJson`      | `string` | No       | JSON string describing the flow (default: `{}`) |
| `tags`          | `string` | No       | Comma-separated tags (e.g., `email,auth`)       |

**Example Request:**

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

**Response** `200 OK`:

```json
{
  "error": false,
  "message": "Definition created",
  "data": {
    "id": 1,
    "definitionKey": "welcome_email_flow",
    "displayName": "Welcome Email Flow",
    "version": 1,
    "isActive": true,
    "...": "..."
  }
}
```

**Response** `400 Bad Request`:

```json
{ "error": true, "message": "DefinitionKey is required" }
```

---

### PUT `/definitions/{key}`

Update a definition. This creates a **new version** (auto-incremented) rather than modifying the existing record.

**Path Parameters:** `key` (string)

**Request Body:** Same as POST (`FlowDefinitionSaveRequestDTO`), but `definitionKey` field is ignored — the path parameter is used.

**Example Request:**

```http
PUT /api/bikiran-engine/definitions/welcome_email_flow
Content-Type: application/json

{
  "displayName": "Welcome Email Flow (v2)",
  "description": "Updated with a longer delay",
  "tags": "auth,email,onboarding",
  "flowJson": "{\"name\":\"welcome_email_flow\",\"config\":{\"maxExecutionTimeSeconds\":120},\"nodes\":[{\"type\":\"Wait\",\"name\":\"brief_delay\",\"params\":{\"delayMs\":2000}},{\"type\":\"EmailSend\",\"name\":\"send_welcome\",\"params\":{\"toEmail\":\"{{email}}\",\"subject\":\"Welcome!\"}}]}"
}
```

**Response** `200 OK`:

```json
{
  "error": false,
  "message": "Definition updated to v2",
  "data": { "id": 2, "definitionKey": "welcome_email_flow", "version": 2, "...": "..." }
}
```

---

### PATCH `/definitions/{key}/toggle`

Toggle a definition between active and inactive. An inactive definition cannot be triggered.

**Path Parameters:** `key` (string)

**Request Body:** None

**Response** `200 OK`:

```json
{ "error": false, "message": "Definition is now inactive" }
```

**Response** `404 Not Found`:

```json
{ "error": true, "message": "Definition not found" }
```

---

### DELETE `/definitions/{key}`

Soft-delete **all versions** of a definition.

**Path Parameters:** `key` (string)

**Response** `200 OK`:

```json
{ "error": false, "message": "Definition deleted" }
```

**Response** `404 Not Found`:

```json
{ "error": true, "message": "Definition not found" }
```

---

### POST `/definitions/{key}/trigger`

Trigger execution of a definition with runtime parameters. The latest active version is used.

**Path Parameters:** `key` (string)

**Request Body** (`FlowDefinitionTriggerRequestDTO`):

| Field           | Type                       | Required | Description                                          |
| --------------- | -------------------------- | -------- | ---------------------------------------------------- |
| `parameters`    | `Dictionary<string,string>`| No       | Key-value pairs that replace `{{placeholders}}`      |
| `triggerSource` | `string`                   | No       | Label identifying where this trigger originated      |

**System parameters** are injected automatically and can be used in FlowJson without passing them:

| Placeholder       | Value                          |
| ----------------- | ------------------------------ |
| `{{today_date}}`  | Current date (`YYYY-MM-DD`)    |
| `{{unix_now}}`    | Current Unix timestamp         |
| `{{year}}`        | Current year (`YYYY`)          |
| `{{month}}`       | Current month (`MM`)           |

**Example Request:**

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

**Response** `200 OK`:

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

**Response** `400 Bad Request` (definition not found, inactive, or invalid):

```json
{ "error": true, "message": "Flow definition 'unknown_flow' not found or is inactive." }
```

> **Note:** The trigger returns immediately after queuing the flow. Use the `serviceId` with the [Flow Runs](#1-flow-runs) endpoints to track progress.

---

### GET `/definitions/{key}/runs`

List all runs that were triggered from this definition, paginated.

**Path Parameters:** `key` (string)  
**Query Parameters:** `page`, `pageSize`

**Response** `200 OK`:

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
      "parameters": "{\"email\":\"user@example.com\",\"name\":\"Jane Doe\"}",
      "triggerUserId": 0,
      "triggerSource": "AdminPanel",
      "timeCreated": 1742001000
    }
  ]
}
```

---

### FlowJson Format

The `flowJson` field contains a JSON string with this structure:

```json
{
  "name": "flow_name",
  "config": {
    "maxExecutionTimeSeconds": 60,
    "onFailure": "Continue",
    "enableNodeLogging": true
  },
  "nodes": [
    {
      "type": "NodeType",
      "name": "unique_node_name",
      "params": { }
    }
  ]
}
```

**Config options:**

| Field                       | Type     | Default       | Description                          |
| --------------------------- | -------- | ------------- | ------------------------------------ |
| `maxExecutionTimeSeconds`   | `int`    | `600` (10min) | Timeout for the entire flow          |
| `onFailure`                 | `string` | `"Stop"`      | `"Stop"` or `"Continue"` on failure  |
| `enableNodeLogging`         | `bool`   | `true`        | Log per-node execution details       |

**Supported node types:**

| Type          | Description                                   | Key params                                    |
| ------------- | --------------------------------------------- | --------------------------------------------- |
| `Wait`        | Pause execution for a duration                | `delayMs`                                     |
| `HttpRequest` | Make an outbound HTTP call                    | `url`, `method`, `body`, `headers`, `maxRetries`, `timeoutSeconds`, `outputKey`, `expectStatusCode`, `expectValue`, `allowedHosts` |
| `EmailSend`   | Send an email via SMTP                        | `toEmail`, `toName`, `subject`, `credentialName`, `template`, `htmlBody`, `textBody`, `placeholders` |
| `Transform`   | Set a static value in context                 | `outputKey`, `value`                          |

> Nodes that require C# code (IfElse, Parallel, Retry, WhileLoop, DatabaseQuery) are not available in JSON definitions. See [07-custom-nodes.md](07-custom-nodes.md).

---

## 3. Flow Schedules

Endpoints for creating, updating, and managing automated flow schedule triggers. Powered by Quartz.NET.

**Route prefix:** `/api/bikiran-engine/schedules`

### Endpoint Summary

| Method   | Route                      | Description                                   |
| -------- | -------------------------- | --------------------------------------------- |
| `GET`    | `/schedules`               | List all schedules                            |
| `GET`    | `/schedules/{key}`         | Get schedule details with next fire time      |
| `POST`   | `/schedules`               | Create a new schedule                         |
| `PUT`    | `/schedules/{key}`         | Update a schedule (re-registers in Quartz)    |
| `PATCH`  | `/schedules/{key}/toggle`  | Enable or disable (pause/resume in Quartz)    |
| `DELETE` | `/schedules/{key}`         | Soft-delete and unregister from Quartz        |
| `POST`   | `/schedules/{key}/run-now` | Trigger the schedule immediately              |
| `GET`    | `/schedules/{key}/runs`    | List runs triggered by this schedule          |

---

### GET `/schedules`

List all schedules, ordered by most recent first.

**Response** `200 OK`:

```json
{
  "error": false,
  "data": [
    {
      "id": 1,
      "scheduleKey": "daily_report",
      "displayName": "Daily Report Email",
      "definitionKey": "daily_report_flow",
      "scheduleType": "cron",
      "cronExpression": "0 0 8 * * ?",
      "intervalMinutes": null,
      "runOnceAt": null,
      "defaultParameters": "{\"adminEmail\":\"admin@example.com\"}",
      "isActive": true,
      "timeZone": "Asia/Dhaka",
      "maxConcurrent": 1,
      "lastRunAt": 1742000000,
      "nextRunAt": 1742086400,
      "lastRunServiceId": "abc123...",
      "lastRunStatus": "completed",
      "timeCreated": 1741900000,
      "timeUpdated": 1742000000,
      "timeDeleted": 0
    }
  ]
}
```

---

### GET `/schedules/{key}`

Get schedule details including the next calculated fire time.

**Path Parameters:**

| Parameter | Type     | Description                               |
| --------- | -------- | ----------------------------------------- |
| `key`     | `string` | Unique schedule key (e.g., `daily_report`) |

**Response** `200 OK`:

```json
{
  "error": false,
  "data": {
    "id": 1,
    "scheduleKey": "daily_report",
    "displayName": "Daily Report Email",
    "definitionKey": "daily_report_flow",
    "scheduleType": "cron",
    "isActive": true,
    "lastRunAt": 1742000000,
    "lastRunStatus": "completed",
    "lastRunServiceId": "abc123...",
    "nextRunAt": 1742086400
  }
}
```

**Response** `404 Not Found`:

```json
{ "error": true, "message": "Schedule not found" }
```

---

### POST `/schedules`

Create a new schedule and register it with Quartz.

**Request Body** (`FlowScheduleSaveRequestDTO`):

| Field               | Type                        | Required | Description                                             |
| ------------------- | --------------------------- | -------- | ------------------------------------------------------- |
| `scheduleKey`       | `string`                    | Yes      | Unique slug for the schedule                            |
| `displayName`       | `string`                    | No       | Human-readable label                                    |
| `definitionKey`     | `string`                    | Yes      | Must match an existing flow definition key              |
| `scheduleType`      | `string`                    | Yes      | `"cron"`, `"interval"`, or `"once"`                     |
| `cronExpression`    | `string`                    | *        | Quartz 6-field cron expression (required for `cron`)    |
| `intervalMinutes`   | `int`                       | *        | Repeat interval in minutes (required for `interval`)    |
| `runOnceAt`         | `long`                      | *        | Unix timestamp for execution (required for `once`)      |
| `defaultParameters` | `Dictionary<string,string>` | No       | Key-value pairs passed to the definition on each run    |
| `timeZone`          | `string`                    | No       | IANA timezone ID (default: `"UTC"`)                     |
| `maxConcurrent`     | `int`                       | No       | Max concurrent runs; `1` = skip overlapping (default: `1`) |

**Schedule types:**

| Type       | Description                              | Required field      |
| ---------- | ---------------------------------------- | ------------------- |
| `cron`     | Runs on a cron schedule                  | `cronExpression`    |
| `interval` | Runs every N minutes                     | `intervalMinutes`   |
| `once`     | Runs once at a specific time             | `runOnceAt`         |

**Example: Cron Schedule**

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

**Example: Interval Schedule**

```http
POST /api/bikiran-engine/schedules
Content-Type: application/json

{
  "scheduleKey": "health_check",
  "displayName": "Health Check Every 5 Minutes",
  "definitionKey": "health_check_flow",
  "scheduleType": "interval",
  "intervalMinutes": 5,
  "maxConcurrent": 1
}
```

**Example: One-Time Schedule**

```http
POST /api/bikiran-engine/schedules
Content-Type: application/json

{
  "scheduleKey": "migration_task",
  "displayName": "Run Migration at March 25 Midnight",
  "definitionKey": "migration_flow",
  "scheduleType": "once",
  "runOnceAt": 1742860800,
  "timeZone": "Asia/Dhaka"
}
```

**Response** `200 OK`:

```json
{ "error": false, "message": "Schedule created", "data": { "...": "..." } }
```

**Response** `400 Bad Request`:

```json
{ "error": true, "message": "ScheduleKey is required" }
```

---

### PUT `/schedules/{key}`

Update a schedule and re-register it in Quartz.

**Path Parameters:** `key` (string)  
**Request Body:** Same as POST (`FlowScheduleSaveRequestDTO`). The `scheduleKey` field is ignored — the path parameter is used.

**Response** `200 OK`:

```json
{ "error": false, "message": "Schedule updated" }
```

**Response** `404 Not Found`:

```json
{ "error": true, "message": "Schedule not found" }
```

---

### PATCH `/schedules/{key}/toggle`

Toggle a schedule between active and paused. When paused, the Quartz job is unregistered. When re-activated, it is re-registered.

**Path Parameters:** `key` (string)  
**Request Body:** None

**Response** `200 OK`:

```json
{ "error": false, "message": "Schedule is now paused" }
```

---

### DELETE `/schedules/{key}`

Soft-delete a schedule and unregister it from Quartz.

**Path Parameters:** `key` (string)

**Response** `200 OK`:

```json
{ "error": false, "message": "Schedule deleted" }
```

---

### POST `/schedules/{key}/run-now`

Trigger the scheduled flow immediately, outside of its normal schedule. If the Quartz job is not registered (e.g., the schedule was paused), it is re-registered first.

**Path Parameters:** `key` (string)  
**Request Body:** None

**Response** `200 OK`:

```json
{ "error": false, "message": "Schedule triggered immediately" }
```

---

### GET `/schedules/{key}/runs`

List all runs triggered by this schedule, paginated. Matches runs where `TriggerSource == "FlowSchedule:{key}"`.

**Path Parameters:** `key` (string)  
**Query Parameters:** `page`, `pageSize`

**Response** `200 OK`:

```json
{
  "error": false,
  "data": [
    {
      "id": 5,
      "serviceId": "abc123...",
      "flowName": "daily_report_flow",
      "status": "completed",
      "triggerSource": "FlowSchedule:daily_report",
      "totalNodes": 3,
      "completedNodes": 3,
      "durationMs": 5200,
      "startedAt": 1742000000,
      "completedAt": 1742000005,
      "errorMessage": null,
      "timeCreated": 1742000000
    }
  ]
}
```

---

### Finding Schedule-Triggered Runs via SQL

To query schedule-triggered runs directly:

```sql
SELECT * FROM FlowRun WHERE TriggerSource LIKE 'FlowSchedule:%';
```

The `FlowDefinitionRun` table provides additional detail including the definition key, version, and serialized parameters used for each run.

---

## 4. Logging & Observability

### What Gets Recorded for Each Run

| Moment                          | Status      | Fields Updated                                       |
| ------------------------------- | ----------- | ---------------------------------------------------- |
| `FlowBuilder.StartAsync()`     | `pending`   | ServiceId, FlowName, Config, TotalNodes, ContextMeta |
| `FlowRunner` begins execution  | `running`   | StartedAt                                            |
| All nodes complete successfully | `completed` | CompletedAt, DurationMs                              |
| A node failure occurs           | `failed`    | ErrorMessage, CompletedAt                            |

### What Gets Recorded for Each Node

| Moment              | Status      | Fields Updated                        |
| ------------------- | ----------- | ------------------------------------- |
| Node starts         | `running`   | StartedAt, Sequence, InputData        |
| Node succeeds       | `completed` | OutputData, CompletedAt, DurationMs   |
| Node fails          | `failed`    | ErrorMessage, RetryCount              |
| Node is skipped     | `skipped`   | _(no timing data)_                    |
| IfElse resolves     | `completed` | BranchTaken (`"true"` or `"false"`)   |

### Run Status Lifecycle

```
pending → running → completed
                  → failed
                  → cancelled (timeout)
```

### ContextMeta

Each run captures caller metadata (stored as JSON in `FlowRun.ContextMeta`):

```json
{
  "ipAddress": "192.168.1.100",
  "userId": 42,
  "requestPath": "/api/orders/create",
  "userAgent": "Mozilla/5.0...",
  "timestamp": 1742000000
}
```

---

## 5. Error Reference

### HTTP Status Codes

| Code  | Meaning               | When                                                          |
| ----- | --------------------- | ------------------------------------------------------------- |
| `200` | OK                    | Successful operation (including creates and deletes)          |
| `400` | Bad Request           | Missing required field, invalid input, or trigger failure     |
| `404` | Not Found             | Resource with given ID/key does not exist or is soft-deleted  |

### Common Error Responses

**Missing required field:**

```json
{ "error": true, "message": "DefinitionKey is required" }
```

**Resource not found:**

```json
{ "error": true, "message": "Definition not found" }
```

**Trigger failure (definition inactive or missing):**

```json
{ "error": true, "message": "Flow definition 'unknown_key' not found or is inactive." }
```

### Run Status Values

| Status      | Description                                          |
| ----------- | ---------------------------------------------------- |
| `pending`   | Run created, waiting to start                        |
| `running`   | Actively executing nodes                             |
| `completed` | All nodes finished successfully                      |
| `failed`    | A node failed (and `OnFailure` was set to `Stop`)    |
| `cancelled` | Execution timed out (`MaxExecutionTime` exceeded)    |

### Node Status Values

| Status      | Description                          |
| ----------- | ------------------------------------ |
| `running`   | Node is currently executing          |
| `completed` | Node finished successfully           |
| `failed`    | Node threw an error                  |
| `skipped`   | Node was bypassed (e.g., IfElse)     |
