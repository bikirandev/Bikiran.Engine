# Flow Definitions

Flow definitions let you store workflow templates as JSON in the database and trigger them without writing C# code or redeploying your application.

---

## How It Works

1. Save a flow as a JSON template using the admin API
2. Trigger it via API or from code, passing runtime parameters
3. The engine parses the JSON, substitutes parameters, and starts the flow

---

## JSON Format

A flow definition is a JSON object with three sections:

```json
{
  "name": "welcome_email_flow",
  "config": {
    "maxExecutionTimeSeconds": 60,
    "onFailure": "Stop",
    "enableNodeLogging": true
  },
  "nodes": [
    {
      "type": "Wait",
      "name": "brief_delay",
      "params": {
        "delayMs": 500
      }
    },
    {
      "type": "HttpRequest",
      "name": "call_api",
      "params": {
        "url": "https://api.example.com/users/{{userId}}",
        "method": "GET",
        "outputKey": "user_data"
      }
    },
    {
      "type": "EmailSend",
      "name": "send_welcome",
      "params": {
        "toEmail": "{{email}}",
        "toName": "{{name}}",
        "subject": "Welcome!",
        "htmlBody": "<h1>Welcome, {{name}}!</h1>"
      }
    }
  ]
}
```

### Config Options

| Field                     | Default  | Description                            |
| ------------------------- | -------- | -------------------------------------- |
| `maxExecutionTimeSeconds` | 600      | Maximum execution time in seconds      |
| `onFailure`               | `"Stop"` | `"Stop"` or `"Continue"`               |
| `enableNodeLogging`       | true     | Write per-node records to the database |

---

## Supported Node Types in JSON

| Type          | Description                  |
| ------------- | ---------------------------- |
| `Wait`        | Pause execution              |
| `HttpRequest` | Make an HTTP call            |
| `EmailSend`   | Send an email                |
| `Transform`   | Reshape context data         |
| `IfElse`      | Conditional branching        |
| `WhileLoop`   | Repeat steps in a loop       |
| `Parallel`    | Run branches concurrently    |
| `Retry`       | Wrap a node with retry logic |
| Custom types  | Any registered custom node   |

> **Not supported in JSON:** `DatabaseQueryNode` — EF Core query functions cannot be serialized to JSON.

---

## Parameters

Use `{{parameterName}}` placeholders anywhere in a flow definition. When the flow is triggered, parameters are substituted before the JSON is parsed.

**Example node with parameters:**

```json
{
  "type": "EmailSend",
  "name": "send_email",
  "params": {
    "toEmail": "{{recipientEmail}}",
    "subject": "Order #{{orderId}} Confirmed"
  }
}
```

**Triggering with parameters:**

```http
POST /api/bikiran-engine/definitions/order_confirmation/trigger
Content-Type: application/json

{
  "parameters": {
    "recipientEmail": "user@example.com",
    "orderId": "12345"
  },
  "triggerSource": "OrdersController"
}
```

### Built-in Parameters

These are always available without being passed explicitly:

| Parameter        | Value                            | Example      |
| ---------------- | -------------------------------- | ------------ |
| `{{today_date}}` | Current date (yyyy-MM-dd)        | `2026-03-24` |
| `{{unix_now}}`   | Current Unix timestamp (seconds) | `1774589184` |
| `{{year}}`       | Current year                     | `2026`       |
| `{{month}}`      | Current month (MM)               | `03`         |

User-provided parameters take priority over built-in ones.

### Parameter Schema

Define validation rules for runtime parameters:

```json
{
  "parameterSchema": "{\"recipientEmail\":{\"type\":\"string\",\"required\":true},\"orderId\":{\"type\":\"string\",\"required\":true},\"priority\":{\"type\":\"number\",\"required\":false,\"default\":\"1\"}}"
}
```

Schema format per parameter:

| Field      | Description                            |
| ---------- | -------------------------------------- |
| `type`     | `"string"`, `"number"`, or `"boolean"` |
| `required` | `true` or `false`                      |
| `default`  | Default value if not provided          |

When triggering, the engine validates parameters against the schema and returns errors for missing required parameters or type mismatches.

---

## Conditional Nodes in JSON

### IfElse

```json
{
  "type": "IfElse",
  "name": "check_user_type",
  "condition": "$ctx.user_type == \"premium\"",
  "trueBranch": [
    { "type": "Wait", "name": "premium_delay", "params": { "delayMs": 100 } }
  ],
  "falseBranch": [
    { "type": "Wait", "name": "standard_delay", "params": { "delayMs": 500 } }
  ]
}
```

### WhileLoop

```json
{
  "type": "WhileLoop",
  "name": "poll_status",
  "condition": "$ctx.status != \"done\"",
  "maxIterations": 10,
  "body": [
    {
      "type": "HttpRequest",
      "name": "check_status",
      "params": {
        "url": "https://api.example.com/status",
        "outputKey": "status"
      }
    }
  ]
}
```

### Expression Syntax

Conditions use a safe expression evaluator with context references:

| Pattern              | Meaning                       |
| -------------------- | ----------------------------- |
| `$ctx.key`           | Reference a FlowContext value |
| `==`, `!=`           | Equality comparison           |
| `>`, `<`, `>=`, `<=` | Numeric comparison            |
| `&&`, `\|\|`         | Logical AND / OR              |

Example: `$ctx.count >= 10 && $ctx.status == "active"`

---

## Parallel and Retry in JSON

### Parallel

```json
{
  "type": "Parallel",
  "name": "multi_notify",
  "branches": [
    [
      {
        "type": "EmailSend",
        "name": "email_user",
        "params": { "toEmail": "{{email}}", "subject": "Done" }
      }
    ],
    [
      {
        "type": "HttpRequest",
        "name": "webhook",
        "params": { "url": "https://hooks.example.com/done", "method": "POST" }
      }
    ]
  ]
}
```

### Retry

```json
{
  "type": "Retry",
  "name": "retry_api",
  "maxAttempts": 3,
  "delayMs": 2000,
  "backoffMultiplier": 2.0,
  "inner": {
    "type": "HttpRequest",
    "name": "flaky_api",
    "params": { "url": "https://api.example.com/unstable" }
  }
}
```

---

## Versioning

Each definition key can have multiple versions. When you update a definition via `PUT /definitions/{key}`, a new version is created automatically.

- Versions are numbered starting from 1
- Only the active version is triggered by default
- You can activate a specific version: `PATCH /definitions/{key}/versions/{ver}/activate`
- You can compare versions: `GET /definitions/{key}/versions/diff?v1=1&v2=2`
- Optimistic concurrency is supported via the `If-Match` header with the version number

---

## Validation

The engine validates flow JSON before saving. Validation checks include:

- JSON must be valid and the root must be an object
- `name` property is required and non-empty
- `nodes` array is required and non-empty
- Node names must be unique within the flow
- Each node type is validated for required properties (e.g., HttpRequest needs `url`)
- Config values are validated (`maxExecutionTimeSeconds` must be positive, `onFailure` must be "Stop" or "Continue")

**Validate without saving:**

```http
POST /api/bikiran-engine/definitions/validate
Content-Type: application/json

{
  "flowJson": "{\"name\":\"test\",\"nodes\":[{\"type\":\"Wait\",\"name\":\"x\",\"params\":{\"delayMs\":100}}]}"
}
```

---

## Import and Export

**Export a single definition:**

```http
GET /api/bikiran-engine/definitions/{key}/export
```

**Export all definitions:**

```http
GET /api/bikiran-engine/definitions/export-all
```

**Import a definition:**

```http
POST /api/bikiran-engine/definitions/import
Content-Type: application/json

{
  "definitionKey": "imported_flow",
  "displayName": "Imported Flow",
  "flowJson": "{ ... }"
}
```

If the key already exists, a new version is created.

---

## Triggering from Code

You can trigger definitions programmatically using `FlowDefinitionRunner`:

```csharp
var serviceId = await _definitionRunner.TriggerAsync(
    definitionKey: "welcome_email_flow",
    parameters: new() {
        { "email", userEmail },
        { "name", displayName }
    },
    contextSetup: ctx => {
        ctx.HttpContext = HttpContext;
    },
    triggerSource: "AuthController"
);
```

To trigger a specific version:

```csharp
var serviceId = await _definitionRunner.TriggerVersionAsync(
    definitionKey: "welcome_email_flow",
    version: 2,
    parameters: new() { { "email", userEmail } }
);
```

---

## Custom Nodes in JSON

Register custom node types at startup so they can be used in JSON definitions:

```csharp
builder.Services.AddBikiranEngine(options =>
{
    options.RegisterNode<InvoicePdfNode>("InvoicePdf");
});
```

Then use in JSON:

```json
{
  "type": "InvoicePdf",
  "name": "generate_invoice",
  "params": {
    "invoiceId": "{{invoiceId}}"
  }
}
```

---

## Security

- **SSRF protection:** HttpRequest nodes in JSON definitions can be restricted to specific hosts via the `allowedHosts` parameter
- **Expression safety:** The expression evaluator only supports comparison operators — no code execution, no method calls, no reflection
- **Parameter injection:** Parameters are substituted as plain text before JSON parsing; they cannot inject executable code
