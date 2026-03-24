# Flow Definitions

Flow definitions let you save reusable flow templates as JSON in the database. Once saved, these templates can be triggered with different parameters — without touching or redeploying code.

This is an **optional feature** that adds flexibility on top of the code-based `FlowBuilder` approach. Both methods work together — code-defined flows continue to function alongside database-stored definitions.

---

## How It Works

1. An admin creates a **FlowDefinition** record containing a JSON description of the flow (node types, settings, and parameters).
2. When the definition is triggered, the engine loads the JSON, replaces `{{placeholders}}` with the provided runtime values, builds the nodes, and executes the flow.
3. A **FlowDefinitionRun** record links the resulting run to the definition and parameters that were used.

---

## JSON Format

The `FlowJson` field stores a structured JSON object with the flow name, configuration, and list of nodes:

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

### Supported Node Types

| JSON Type     | Maps To         | Description                                              |
| ------------- | --------------- | -------------------------------------------------------- |
| `Wait`        | WaitNode        | Pause execution for a given delay                        |
| `HttpRequest` | HttpRequestNode | Make HTTP calls with retry, headers, and validation      |
| `EmailSend`   | EmailSendNode   | Send email via SMTP using registered credentials         |
| `Transform`   | TransformNode   | Set a static value in the flow context                   |
| `IfElse`      | IfElseNode      | Conditional branching using expression evaluation        |
| `Parallel`    | ParallelNode    | Run multiple branches concurrently                       |
| `Retry`       | RetryNode       | Wrap any node with configurable retry logic              |
| `WhileLoop`   | WhileLoopNode   | Repeat steps while a condition remains true              |

> `DatabaseQueryNode` cannot be expressed in JSON because it requires a typed EF Core delegate. Use code-defined flows for database queries.

---

## Conditional & Control-Flow Nodes

### IfElse

Evaluates a condition expression against the flow context and runs the appropriate branch:

```json
{
  "type": "IfElse",
  "name": "check_amount",
  "params": {
    "condition": "$ctx.order_total > 1000"
  },
  "trueBranch": [
    { "type": "EmailSend", "name": "vip_email", "params": { "toEmail": "{{email}}", "subject": "VIP Order" } }
  ],
  "falseBranch": [
    { "type": "EmailSend", "name": "standard_email", "params": { "toEmail": "{{email}}", "subject": "Order Received" } }
  ]
}
```

**Condition syntax:** Supports `$ctx.key` references, comparison operators (`==`, `!=`, `>`, `<`, `>=`, `<=`), and logical operators (`&&`, `||`).

### WhileLoop

Repeats a body of steps while a condition is true, up to a maximum iteration count:

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
      "params": { "url": "https://api.example.com/status", "outputKey": "status" }
    }
  ]
}
```

### Parallel

Runs multiple branches concurrently. Each branch is an array of nodes:

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
      { "type": "EmailSend", "name": "email_admin", "params": { "toEmail": "admin@example.com", "subject": "Alert" } }
    ],
    [
      { "type": "HttpRequest", "name": "webhook", "params": { "url": "https://hooks.example.com/notify", "method": "POST" } }
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
    "params": { "url": "https://unstable-api.example.com/data", "outputKey": "api_result" }
  }
}
```

---

## Parameter Placeholders

String values in node parameters support `{{paramName}}` placeholders. When the flow is triggered, the caller provides a dictionary of parameter values that replace these placeholders.

**Rules:**

- Use double curly braces: `{{paramName}}`
- Placeholders are replaced in all string values, including nested values in `headers` and `placeholders`
- Unmatched parameters are ignored
- Placeholders with no matching value remain as-is

**Built-in parameters** (injected automatically, can be overridden):

| Parameter     | Value                              |
| ------------- | ---------------------------------- |
| `today_date`  | `yyyy-MM-dd` UTC                   |
| `unix_now`    | Current Unix timestamp (seconds)   |
| `year`        | Current year (`yyyy`)              |
| `month`       | Current month (`MM`)               |

---

## Parameter Schema

Definitions can include a `ParameterSchema` that describes the expected parameters and their types. When a definition is triggered, parameters are validated against this schema.

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

FlowJson is automatically validated when creating or updating definitions. You can also validate without saving:

```http
POST /api/bikiran-engine/definitions/validate
Content-Type: application/json

{
  "flowJson": "{ \"name\": \"test\", \"nodes\": [...] }"
}
```

Validation checks:
- Valid JSON structure
- Required `name` property
- Non-empty `nodes` array
- Each node has a valid `type` and unique `name`
- Type-specific parameter requirements (e.g., `url` for HttpRequest)
- Recursive validation of nested nodes (IfElse branches, Parallel branches, etc.)

---

## Versioning

Every time a definition is saved, its `Version` number increases automatically. Previous versions are kept for reference.

### Active Version

When triggered, only the **latest active version** of a definition key is used. You can activate a specific version:

```http
PATCH /api/bikiran-engine/definitions/{key}/versions/{version}/activate
```

This deactivates all other versions and sets the specified one as active.

### Version Comparison

Compare two versions side-by-side:

```http
GET /api/bikiran-engine/definitions/{key}/versions/diff?v1=1&v2=3
```

### Optimistic Concurrency

Send an `If-Match` header with the expected version number when updating:

```http
PUT /api/bikiran-engine/definitions/{key}
If-Match: "3"
```

If another update occurred since you last read the definition, a `409 Conflict` is returned.

---

## Import / Export

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

## Dry-Run

Test a definition without actually executing it:

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

### Expression Evaluation

Condition expressions in `IfElse` and `WhileLoop` nodes use a safe evaluator that only supports `$ctx.key` references and basic comparison/logical operators. No arbitrary code execution is possible.

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

## Registering Custom Nodes for JSON

Register custom node types at startup to use them in JSON definitions:

```csharp
builder.Services.AddBikiranEngine(options =>
{
    options.ConnectionString = "...";
    options.RegisterNode<InvoicePdfNode>("InvoicePdf");
});
```

Once registered, the custom type can be used in JSON:

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

---

## Request and Response Models

### Create or Update a Definition

```csharp
public class FlowDefinitionSaveRequestDTO
{
    public string DefinitionKey { get; set; }     // e.g., "order_notification"
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public string FlowJson { get; set; }          // Full JSON string
    public string Tags { get; set; }              // Comma-separated
    public string? ParameterSchema { get; set; }  // Optional JSON schema
}
```

### Trigger a Definition

```csharp
public class FlowDefinitionTriggerRequestDTO
{
    public Dictionary<string, string> Parameters { get; set; }
    public string TriggerSource { get; set; }
}
```

### Trigger Response

```csharp
public class FlowDefinitionTriggerResponseDTO
{
    public string ServiceId { get; set; }
    public string DefinitionKey { get; set; }
    public int DefinitionVersion { get; set; }
    public string FlowName { get; set; }
}
```

---

## Limitations

| Limitation                     | Reason                                           |
| ------------------------------ | ------------------------------------------------ |
| No `DatabaseQueryNode` in JSON | EF Core query delegates cannot be serialized     |

For flows that need typed database queries, use code-defined flows via `FlowBuilder`.

## Limitations

| Limitation                     | Reason                                                 |
| ------------------------------ | ------------------------------------------------------ |
| No `IfElseNode` in JSON        | Requires a C# delegate for the condition               |
| No `WhileLoopNode` in JSON     | Requires a C# delegate                                 |
| No `DatabaseQueryNode` in JSON | EF Core query delegates cannot be serialized           |
| No `ParallelNode` in JSON      | Complex branch structure; planned for a future release |
| No `RetryNode` in JSON         | Use `HttpRequestNode.MaxRetries` or code-defined flows |
| No expression evaluation       | Prevents code injection from admin-supplied JSON       |

For flows that need these capabilities, use code-defined flows via `FlowBuilder`.
