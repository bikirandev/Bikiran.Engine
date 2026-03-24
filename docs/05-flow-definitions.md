# Flow Definitions

Flow definitions let you save reusable flow templates as JSON in the database. Once saved, these templates can be triggered with different parameters — without changing or redeploying code.

This is an optional feature that works alongside the code-based `FlowBuilder` approach. Both methods function together — code-defined flows continue to work alongside database-stored definitions.

---

## How It Works

1. An admin creates a **FlowDefinition** record containing a JSON description of the flow (its node types, settings, and parameters).
2. When the definition is triggered, the engine loads the JSON, replaces `{{placeholders}}` with the provided values, builds the nodes, and runs the flow.
3. A **FlowDefinitionRun** record links the resulting run to the definition and the parameters used.

---

## JSON Structure

The `FlowJson` field contains a structured JSON object with the flow name, configuration, and a list of nodes:

```json
{
  "name": "order_notification_flow",
  "config": {
    "maxExecutionTimeSeconds": 300,
    "onFailure": "Continue",
    "enableNodeLogging": true
  },
  "nodes": [
    {
      "type": "Wait",
      "name": "initial_pause",
      "params": {
        "delayMs": 1000
      }
    },
    {
      "type": "HttpRequest",
      "name": "fetch_order_data",
      "params": {
        "url": "https://api.example.com/orders/{{orderId}}",
        "method": "GET",
        "maxRetries": 3,
        "timeoutSeconds": 30,
        "outputKey": "order_data",
        "headers": {
          "Authorization": "Bearer {{apiToken}}"
        }
      }
    },
    {
      "type": "EmailSend",
      "name": "notify_customer",
      "params": {
        "toEmail": "{{customerEmail}}",
        "toName": "{{customerName}}",
        "subject": "Order {{orderId}} Confirmed",
        "template": "ORDER_CREATE",
        "placeholders": {
          "OrderId": "{{orderId}}",
          "Amount": "{{amount}}"
        }
      }
    }
  ]
}
```

### Configuration Options

| Field                     | Default  | Description                                            |
| ------------------------- | -------- | ------------------------------------------------------ |
| `maxExecutionTimeSeconds` | `600`    | Maximum time the flow can run (in seconds)             |
| `onFailure`               | `"Stop"` | What to do when a step fails: `"Stop"` or `"Continue"` |
| `enableNodeLogging`       | `true`   | Whether to write per-step logs to the database         |

### Supported Node Types in JSON

| JSON Type     | Maps To         | Description                                         |
| ------------- | --------------- | --------------------------------------------------- |
| `Wait`        | WaitNode        | Pause execution for a given delay                   |
| `HttpRequest` | HttpRequestNode | Make HTTP calls with retry, headers, and validation |
| `EmailSend`   | EmailSendNode   | Send email via SMTP using registered credentials    |
| `Transform`   | TransformNode   | Set a static value in the flow context              |
| `IfElse`      | IfElseNode      | Conditional branching using expression evaluation   |
| `Parallel`    | ParallelNode    | Run multiple branches at once                       |
| `Retry`       | RetryNode       | Wrap any node with configurable retry logic         |
| `WhileLoop`   | WhileLoopNode   | Repeat steps while a condition remains true         |

> `DatabaseQueryNode` cannot be used in JSON because it requires a typed EF Core delegate. Use code-defined flows for database queries.

---

## Control-Flow Nodes in JSON

### IfElse

Evaluates a condition expression and runs the matching branch:

```json
{
  "type": "IfElse",
  "name": "check_amount",
  "params": {
    "condition": "$ctx.order_total > 1000"
  },
  "trueBranch": [
    {
      "type": "EmailSend",
      "name": "vip_email",
      "params": { "toEmail": "{{email}}", "subject": "VIP Order" }
    }
  ],
  "falseBranch": [
    {
      "type": "EmailSend",
      "name": "standard_email",
      "params": { "toEmail": "{{email}}", "subject": "Order Received" }
    }
  ]
}
```

**Condition syntax:** Use `$ctx.key` to reference context values. Supported operators: `==`, `!=`, `>`, `<`, `>=`, `<=`, `&&`, `||`.

### WhileLoop

Repeats a body of steps while a condition is true:

```json
{
  "type": "WhileLoop",
  "name": "poll_status",
  "params": {
    "condition": "$ctx.status != done",
    "maxIterations": 10,
    "iterationDelayMs": 5000
  },
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

### Parallel

Runs multiple branches at the same time:

```json
{
  "type": "Parallel",
  "name": "parallel_notifications",
  "params": {
    "waitAll": true,
    "abortOnBranchFailure": false
  },
  "branches": [
    [
      {
        "type": "EmailSend",
        "name": "email_admin",
        "params": { "toEmail": "admin@example.com", "subject": "Alert" }
      }
    ],
    [
      {
        "type": "HttpRequest",
        "name": "webhook",
        "params": {
          "url": "https://hooks.example.com/notify",
          "method": "POST"
        }
      }
    ]
  ]
}
```

### Retry

Wraps a single inner node with retry logic:

```json
{
  "type": "Retry",
  "name": "retry_api_call",
  "params": {
    "maxAttempts": 5,
    "delayMs": 3000,
    "backoffMultiplier": 2.0
  },
  "inner": {
    "type": "HttpRequest",
    "name": "fragile_api",
    "params": {
      "url": "https://unstable-api.example.com/data",
      "outputKey": "api_result"
    }
  }
}
```

---

## Parameter Placeholders

String values in node parameters support `{{paramName}}` placeholders. When the flow is triggered, the caller provides a dictionary of values that replace these placeholders.

**Rules:**

- Use double curly braces: `{{paramName}}`
- Placeholders are replaced in all string values, including nested values in `headers` and `placeholders`
- Unmatched parameters are ignored
- Placeholders with no matching value remain as-is

### Built-in Parameters

These are injected automatically and can be overridden:

| Placeholder      | Value                             |
| ---------------- | --------------------------------- |
| `{{today_date}}` | Current UTC date (`yyyy-MM-dd`)   |
| `{{unix_now}}`   | Current Unix timestamp in seconds |
| `{{year}}`       | Current year (`yyyy`)             |
| `{{month}}`      | Current month (`MM`)              |

---

## Parameter Schema

Definitions can include a `ParameterSchema` that describes expected parameters and their types. When triggered, parameters are validated against this schema.

```json
{
  "orderId": { "type": "string", "required": true },
  "amount": { "type": "number", "required": true },
  "sendEmail": { "type": "boolean", "required": false, "default": "true" }
}
```

**Supported types:** `string`, `number`, `boolean`

When `required` is `true` and no `default` is provided, the parameter must be supplied at trigger time. If a `default` is set, it is used when the parameter is not provided.

---

## Validation

FlowJson is validated automatically when creating or updating definitions. You can also validate without saving:

```http
POST /api/bikiran-engine/definitions/validate
Content-Type: application/json

{
  "flowJson": "{ \"name\": \"test\", \"nodes\": [...] }"
}
```

Validation checks:

- Valid JSON structure
- Required `name` property present
- Non-empty `nodes` array
- Each node has a valid `type` and unique `name`
- Type-specific parameter requirements (e.g., `url` for HttpRequest)
- Nested nodes validated recursively (IfElse branches, Parallel branches, etc.)

---

## Versioning

Every time a definition is saved, its `Version` number increases automatically. Previous versions are kept for reference.

### Active Version

When triggered, only the **latest active version** is used. You can activate a specific version:

```http
PATCH /api/bikiran-engine/definitions/{key}/versions/{version}/activate
```

This deactivates all other versions and sets the specified one as active.

### Comparing Versions

Compare two versions side-by-side:

```http
GET /api/bikiran-engine/definitions/{key}/versions/diff?v1=1&v2=3
```

### Preventing Conflicting Updates

Send an `If-Match` header with the expected version number when updating:

```http
PUT /api/bikiran-engine/definitions/{key}
If-Match: "3"
```

If another update occurred since you last read the definition, a `409 Conflict` is returned.

---

## Import and Export

### Export a single definition

```http
GET /api/bikiran-engine/definitions/{key}/export
```

### Export all definitions

```http
GET /api/bikiran-engine/definitions/export-all
```

### Import a definition

```http
POST /api/bikiran-engine/definitions/import
Content-Type: application/json

{
  "definitionKey": "order_notification_flow",
  "displayName": "Order Notification",
  "description": "Sends notification for new orders",
  "flowJson": "{ ... }",
  "tags": "orders,email",
  "parameterSchema": "{ \"orderId\": { \"type\": \"string\", \"required\": true } }"
}
```

Importing creates a new version if the key already exists.

---

## Dry Run

Test a definition without actually running it:

```http
POST /api/bikiran-engine/definitions/{key}/dry-run
Content-Type: application/json

{
  "parameters": { "orderId": "12345" },
  "triggerSource": "test"
}
```

This validates the definition, resolves parameters, and parses the flow without starting execution.

---

## Security

### SSRF Protection

`HttpRequest` nodes support an `allowedHosts` parameter — a list of permitted domain names. When set, the resolved URL must match one of the allowed hosts.

### Authentication

Enable authentication on engine endpoints:

```csharp
builder.Services.AddBikiranEngine(options =>
{
    options.RequireAuthentication = true;
    options.AuthorizationPolicy = "EngineAdmin"; // optional named policy
});
```

### Safe Expression Evaluation

Condition expressions in `IfElse` and `WhileLoop` nodes use a safe evaluator that only supports `$ctx.key` references and basic comparison/logical operators. No arbitrary code can be executed.

---

## Triggering a Definition

### From the Admin API

```http
POST /api/bikiran-engine/definitions/order_notification_flow/trigger
Content-Type: application/json

{
  "parameters": {
    "orderId": "12345",
    "customerEmail": "user@example.com",
    "customerName": "John Doe",
    "amount": "1500.00",
    "apiToken": "abc123"
  },
  "triggerSource": "AdminPanel"
}
```

### From C# Code

```csharp
var serviceId = await _definitionRunner.TriggerAsync(
    definitionKey: "order_notification_flow",
    parameters: new() {
        { "orderId", order.Id.ToString() },
        { "customerEmail", user.Email },
        { "amount", invoice.Amount.ToString("F2") }
    },
    contextSetup: ctx => {
        ctx.HttpContext = HttpContext;
    },
    triggerSource: nameof(OrdersV3Controller)
);
```

---

## Using Custom Nodes in JSON

Register custom node types at startup to make them available in JSON definitions:

```csharp
builder.Services.AddBikiranEngine(options =>
{
    options.ConnectionString = "...";
    options.RegisterNode<InvoicePdfNode>("InvoicePdf");
});
```

Then use the custom type in JSON:

```json
{
  "type": "InvoicePdf",
  "name": "generate_pdf",
  "params": {
    "invoiceId": "{{invoiceId}}",
    "outputKey": "pdf_url"
  }
}
```
