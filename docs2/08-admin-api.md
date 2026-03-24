# Admin API

Bikiran.Engine exposes a REST API for managing flow runs, definitions, and schedules. All endpoints live under the `/api/bikiran-engine/` base path.

---

## Response Format

Every response follows the same envelope:

```json
{
  "error": false,
  "message": "Description of what happened",
  "data": {}
}
```

| Field     | Type         | Description                                     |
| --------- | ------------ | ----------------------------------------------- |
| `error`   | boolean      | `true` if the request failed                    |
| `message` | string       | Human-readable status message                   |
| `code`    | string       | Machine-readable error code (present on errors) |
| `data`    | object/array | Response payload                                |

---

## Error Codes

| Code                      | Meaning                              |
| ------------------------- | ------------------------------------ |
| `DEFINITION_NOT_FOUND`    | No definition matches the given key  |
| `INVALID_FLOW_JSON`       | FlowJson failed validation           |
| `PARAMETER_REQUIRED`      | A required parameter is missing      |
| `PARAMETER_TYPE_MISMATCH` | A parameter has the wrong type       |
| `VERSION_CONFLICT`        | Optimistic concurrency check failed  |
| `DEFINITION_INACTIVE`     | Definition is disabled               |
| `TRIGGER_FAILED`          | Trigger could not execute            |
| `VALIDATION_RESULT`       | Response is a validation-only result |
| `VERSION_NOT_FOUND`       | Specified version does not exist     |
| `IMPORT_FAILED`           | Import validation failed             |
| `SCHEDULE_NOT_FOUND`      | No schedule matches the given key    |
| `RUN_NOT_FOUND`           | No run matches the given service ID  |
| `KEY_REQUIRED`            | A required key field is missing      |

---

## Pagination

Endpoints that return lists accept these query parameters:

| Parameter  | Default | Description           |
| ---------- | ------- | --------------------- |
| `page`     | 1       | Page number (1-based) |
| `pageSize` | 20      | Items per page        |

Results are ordered by most recent first.

---

## Flow Runs

Base path: `/api/bikiran-engine/runs`

### List All Runs

```
GET /api/bikiran-engine/runs?page=1&pageSize=20
```

Returns paginated flow runs, excluding soft-deleted records.

**Response:**

```json
{
  "error": false,
  "message": "Flow runs",
  "data": [
    {
      "serviceId": "abc-123",
      "flowName": "order_process",
      "status": "Completed",
      "triggerSource": "api",
      "totalNodes": 5,
      "completedNodes": 5,
      "durationMs": 1200,
      "startedAt": 1711900000,
      "completedAt": 1711900001
    }
  ]
}
```

### Get Run Details

```
GET /api/bikiran-engine/runs/{serviceId}
```

Returns full run information including all node logs and progress percentage.

**Response:**

```json
{
  "error": false,
  "message": "Flow run details",
  "data": {
    "serviceId": "abc-123",
    "flowName": "order_process",
    "status": "Completed",
    "triggerSource": "api",
    "totalNodes": 5,
    "completedNodes": 5,
    "progressPercent": 100,
    "durationMs": 1200,
    "startedAt": 1711900000,
    "completedAt": 1711900001,
    "errorMessage": null,
    "nodeLogs": [
      {
        "nodeName": "fetch_order",
        "nodeType": "HttpRequest",
        "status": "Completed",
        "sequence": 1,
        "durationMs": 300,
        "output": "{ ... }"
      }
    ]
  }
}
```

### Get Run Progress

```
GET /api/bikiran-engine/runs/{serviceId}/progress
```

Returns a lightweight progress snapshot.

**Response:**

```json
{
  "error": false,
  "data": {
    "serviceId": "abc-123",
    "status": "Running",
    "percent": 60,
    "completedNodes": 3,
    "totalNodes": 5
  }
}
```

### Filter Runs by Status

```
GET /api/bikiran-engine/runs/status/{status}?page=1&pageSize=20
```

Returns runs matching the given status string (e.g., `Running`, `Completed`, `Failed`).

### Delete Run (Soft Delete)

```
DELETE /api/bikiran-engine/runs/{serviceId}
```

Marks the run as deleted. The record remains in the database with a nonzero `TimeDeleted`.

---

## Flow Definitions

Base path: `/api/bikiran-engine/definitions`

### List Definitions

```
GET /api/bikiran-engine/definitions?page=1&pageSize=20
```

Returns the latest version of each definition, excluding soft-deleted records.

### Get Definition

```
GET /api/bikiran-engine/definitions/{key}
```

Returns the latest active version of a definition.

### Create Definition

```
POST /api/bikiran-engine/definitions
```

**Request Body:**

```json
{
  "definitionKey": "order_notification",
  "displayName": "Order Notification",
  "description": "Sends email when order is placed",
  "flowJson": "{ \"name\": \"order_notification\", ... }",
  "tags": "email,orders",
  "parameterSchema": "{ \"orderId\": { \"type\": \"string\", \"required\": true } }"
}
```

| Field             | Type   | Required | Description                        |
| ----------------- | ------ | -------- | ---------------------------------- |
| `definitionKey`   | string | Yes      | Unique identifier slug             |
| `displayName`     | string | No       | Human-readable name                |
| `description`     | string | No       | What this flow does                |
| `flowJson`        | string | Yes      | JSON flow definition               |
| `tags`            | string | No       | Comma-separated tags               |
| `parameterSchema` | string | No       | JSON schema for runtime parameters |

The engine validates `flowJson` before saving. If validation fails, a `400` response is returned with error details.

### Update Definition

```
PUT /api/bikiran-engine/definitions/{key}
```

Creates a new version of the definition. The version number auto-increments.

**Optimistic Concurrency:** Send an `If-Match` header with the expected version number to detect conflicts:

```
If-Match: "3"
```

If the current version doesn't match, a `409 Conflict` response is returned with code `VERSION_CONFLICT`.

On success, the response includes an `ETag` header with the new version number.

### Toggle Definition

```
PATCH /api/bikiran-engine/definitions/{key}/toggle
```

Flips `IsActive` between `true` and `false`. Inactive definitions cannot be triggered.

### Delete Definition

```
DELETE /api/bikiran-engine/definitions/{key}
```

Soft-deletes all versions of the definition.

### Trigger Definition

```
POST /api/bikiran-engine/definitions/{key}/trigger
```

**Request Body:**

```json
{
  "parameters": {
    "orderId": "12345",
    "customerEmail": "user@example.com"
  },
  "triggerSource": "webhook"
}
```

**Response:**

```json
{
  "error": false,
  "message": "Definition triggered",
  "data": {
    "serviceId": "generated-uuid",
    "definitionKey": "order_notification",
    "definitionVersion": 3,
    "flowName": "Order Notification"
  }
}
```

### Dry Run

```
POST /api/bikiran-engine/definitions/{key}/dry-run
```

Same request body as trigger. Validates and parses the definition without executing it. Returns code `VALIDATION_RESULT` on success.

### List Definition Runs

```
GET /api/bikiran-engine/definitions/{key}/runs?page=1&pageSize=20
```

Returns all runs triggered from this definition (from the `FlowDefinitionRun` table).

### Validate FlowJson

```
POST /api/bikiran-engine/definitions/validate
```

Validates a `flowJson` string without saving. Returns validation errors or a success message.

**Request Body:**

```json
{
  "flowJson": "{ \"name\": \"test\", \"nodes\": [] }"
}
```

### List Versions

```
GET /api/bikiran-engine/definitions/{key}/versions
```

Returns all versions of a definition, ordered newest first.

### Activate Version

```
PATCH /api/bikiran-engine/definitions/{key}/versions/{version}/activate
```

Sets the specified version as active and deactivates all other versions of that definition.

### Compare Versions

```
GET /api/bikiran-engine/definitions/{key}/versions/diff?v1=1&v2=3
```

Returns a side-by-side comparison of two versions including FlowJson, ParameterSchema, DisplayName, and timestamps.

**Response:**

```json
{
  "error": false,
  "data": {
    "key": "order_notification",
    "version1": {
      "version": 1,
      "flowJson": "...",
      "parameterSchema": "...",
      "displayName": "Order Notification v1",
      "isActive": false,
      "timeCreated": 1711800000
    },
    "version2": {
      "version": 3,
      "flowJson": "...",
      "parameterSchema": "...",
      "displayName": "Order Notification v3",
      "isActive": true,
      "timeCreated": 1711900000
    }
  }
}
```

### Export Definition

```
GET /api/bikiran-engine/definitions/{key}/export
```

Exports the latest version as a portable JSON document.

### Export All Definitions

```
GET /api/bikiran-engine/definitions/export-all
```

Exports the latest version of every definition.

### Import Definition

```
POST /api/bikiran-engine/definitions/import
```

Imports a definition from an exported JSON document. If the key already exists, a new version is created. FlowJson is validated before import.

Same request body as [Create Definition](#create-definition).

### Extract Parameters

```
POST /api/bikiran-engine/definitions/extract-parameters
```

Extracts all `{{placeholder}}` parameter names from a FlowJson string. Returns a sorted, deduplicated list of parameter names.

**Response:**

```json
{
  "error": false,
  "data": ["customerEmail", "orderId"]
}
```

---

## Flow Schedules

Base path: `/api/bikiran-engine/schedules`

### List Schedules

```
GET /api/bikiran-engine/schedules
```

Returns all schedules, excluding soft-deleted records. Not paginated.

### Get Schedule

```
GET /api/bikiran-engine/schedules/{key}
```

Returns schedule details including the next fire time from Quartz.

**Response:**

```json
{
  "error": false,
  "data": {
    "id": 1,
    "scheduleKey": "nightly_report",
    "displayName": "Nightly Report",
    "definitionKey": "report_generator",
    "scheduleType": "cron",
    "isActive": true,
    "lastRunAt": 1711800000,
    "lastRunStatus": "Completed",
    "lastRunServiceId": "abc-123",
    "nextRunAt": 1711886400
  }
}
```

### Create Schedule

```
POST /api/bikiran-engine/schedules
```

**Request Body:**

```json
{
  "scheduleKey": "nightly_report",
  "displayName": "Nightly Report",
  "definitionKey": "report_generator",
  "scheduleType": "cron",
  "cronExpression": "0 0 2 * * ?",
  "defaultParameters": { "format": "pdf" },
  "timeZone": "America/New_York",
  "maxConcurrent": 1
}
```

| Field               | Type   | Required     | Description                                     |
| ------------------- | ------ | ------------ | ----------------------------------------------- |
| `scheduleKey`       | string | Yes          | Unique identifier                               |
| `displayName`       | string | No           | Human-readable name                             |
| `definitionKey`     | string | Yes          | Flow definition to trigger                      |
| `scheduleType`      | string | Yes          | `"cron"`, `"interval"`, or `"once"`             |
| `cronExpression`    | string | For cron     | Quartz 6-field cron expression                  |
| `intervalMinutes`   | int    | For interval | Repeat interval in minutes                      |
| `runOnceAt`         | long   | For once     | Unix timestamp for one-time execution           |
| `defaultParameters` | object | No           | Parameters passed to definition on each trigger |
| `timeZone`          | string | No           | IANA timezone ID (default: `"UTC"`)             |
| `maxConcurrent`     | int    | No           | Max concurrent runs (default: `1`)              |

The schedule is registered with Quartz immediately after creation.

### Update Schedule

```
PUT /api/bikiran-engine/schedules/{key}
```

Updates all fields and re-registers the schedule in Quartz with the new configuration.

### Toggle Schedule

```
PATCH /api/bikiran-engine/schedules/{key}/toggle
```

Flips `IsActive`. When deactivated, the schedule is unregistered from Quartz. When reactivated, it is re-registered.

### Delete Schedule

```
DELETE /api/bikiran-engine/schedules/{key}
```

Soft-deletes the schedule and unregisters it from Quartz.

### Run Now

```
POST /api/bikiran-engine/schedules/{key}/run-now
```

Triggers the schedule immediately, outside its normal timing. If the job is not registered in Quartz, it is registered first.

### List Schedule Runs

```
GET /api/bikiran-engine/schedules/{key}/runs?page=1&pageSize=20
```

Returns all flow runs triggered by this schedule. Matches runs where `triggerSource` equals `"FlowSchedule:{key}"`.

---

## Endpoint Summary

### Flow Runs (5 endpoints)

| Method | Path                         | Description                    |
| ------ | ---------------------------- | ------------------------------ |
| GET    | `/runs`                      | List all runs (paginated)      |
| GET    | `/runs/{serviceId}`          | Get run details with node logs |
| GET    | `/runs/{serviceId}/progress` | Get progress percentage        |
| GET    | `/runs/status/{status}`      | Filter runs by status          |
| DELETE | `/runs/{serviceId}`          | Soft-delete a run              |

### Flow Definitions (17 endpoints)

| Method | Path                                       | Description                        |
| ------ | ------------------------------------------ | ---------------------------------- |
| GET    | `/definitions`                             | List definitions (latest versions) |
| GET    | `/definitions/{key}`                       | Get latest version                 |
| POST   | `/definitions`                             | Create a definition                |
| PUT    | `/definitions/{key}`                       | Update (new version)               |
| PATCH  | `/definitions/{key}/toggle`                | Enable/disable                     |
| DELETE | `/definitions/{key}`                       | Soft-delete all versions           |
| POST   | `/definitions/{key}/trigger`               | Trigger with parameters            |
| POST   | `/definitions/{key}/dry-run`               | Validate without executing         |
| GET    | `/definitions/{key}/runs`                  | List triggered runs                |
| POST   | `/definitions/validate`                    | Validate FlowJson                  |
| GET    | `/definitions/{key}/versions`              | List all versions                  |
| PATCH  | `/definitions/{key}/versions/{v}/activate` | Activate a version                 |
| GET    | `/definitions/{key}/versions/diff?v1=&v2=` | Compare two versions               |
| GET    | `/definitions/{key}/export`                | Export definition                  |
| GET    | `/definitions/export-all`                  | Export all definitions             |
| POST   | `/definitions/import`                      | Import a definition                |
| POST   | `/definitions/extract-parameters`          | Extract placeholders               |

### Flow Schedules (8 endpoints)

| Method | Path                       | Description                      |
| ------ | -------------------------- | -------------------------------- |
| GET    | `/schedules`               | List all schedules               |
| GET    | `/schedules/{key}`         | Get schedule with next fire time |
| POST   | `/schedules`               | Create a schedule                |
| PUT    | `/schedules/{key}`         | Update a schedule                |
| PATCH  | `/schedules/{key}/toggle`  | Enable/disable                   |
| DELETE | `/schedules/{key}`         | Soft-delete and unregister       |
| POST   | `/schedules/{key}/run-now` | Trigger immediately              |
| GET    | `/schedules/{key}/runs`    | List schedule runs               |
