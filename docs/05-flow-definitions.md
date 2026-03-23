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

| JSON Type     | Maps To                            |
| ------------- | ---------------------------------- |
| `Wait`        | WaitNode                           |
| `HttpRequest` | HttpRequestNode                    |
| `EmailSend`   | EmailSendNode                      |
| `Transform`   | TransformNode (static values only) |

Nodes that require C# code (like `IfElseNode`, `WhileLoopNode`, `DatabaseQueryNode`, and `ParallelNode`) cannot be expressed in JSON. Use code-defined flows via `FlowBuilder` for those.

---

## Parameter Placeholders

String values in node parameters support `{{paramName}}` placeholders. When the flow is triggered, the caller provides a dictionary of parameter values that replace these placeholders.

**Rules:**

- Use double curly braces: `{{paramName}}`
- Placeholders are replaced in all string values, including nested values in `headers` and `placeholders`
- Unmatched parameters are ignored
- Placeholders with no matching value remain as-is and generate a log warning

**Example:** A definition with `"url": "https://api.example.com/order/{{orderId}}"` triggered with `{ "orderId": "12345" }` resolves to `"url": "https://api.example.com/order/12345"`.

---

## Versioning

Every time a definition is saved, its `Version` number increases automatically. Previous versions are kept for reference. When triggered, only the **latest active version** of a definition key is used.

---

## Security

### SSRF Protection

`HttpRequest` nodes in definitions support an `allowedHosts` parameter — a list of permitted domain names. When set, the resolved URL must match one of the allowed hosts. This prevents parameter values from redirecting HTTP calls to unintended servers.

### No Code Injection

The system intentionally does not support expression evaluation or arbitrary code execution from JSON. Complex logic that requires conditionals or custom code must use code-defined flows.

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

If you create custom node types and want them available in JSON definitions, register them at startup:

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

| Limitation                     | Reason                                                 |
| ------------------------------ | ------------------------------------------------------ |
| No `IfElseNode` in JSON        | Requires a C# delegate for the condition               |
| No `WhileLoopNode` in JSON     | Requires a C# delegate                                 |
| No `DatabaseQueryNode` in JSON | EF Core query delegates cannot be serialized           |
| No `ParallelNode` in JSON      | Complex branch structure; planned for a future release |
| No `RetryNode` in JSON         | Use `HttpRequestNode.MaxRetries` or code-defined flows |
| No expression evaluation       | Prevents code injection from admin-supplied JSON       |

For flows that need these capabilities, use code-defined flows via `FlowBuilder`.
