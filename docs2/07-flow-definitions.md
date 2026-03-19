# Flow Definitions in Database

This document explains how to store reusable flow templates as JSON in the database, allowing admins to create, update, and trigger flows without code changes or redeployment.

---

## Purpose

By default, flows are defined in C# code. Database-stored flow definitions add a second option: **save a flow template as JSON**, then **trigger it later** with different parameters — all without touching source code.

- Code-defined flows (via `FlowBuilder`) continue to work unchanged.
- DB-stored definitions are an additional capability, not a replacement.

---

## How It Works

1. An admin creates a **FlowDefinition** record containing a JSON description of the flow (node types, parameters, config).
2. At trigger time, the system loads the definition, replaces `{{placeholders}}` with runtime parameters, builds the nodes, and passes them to the core execution engine.
3. A **FlowDefinitionRun** record links the resulting FlowRun to the definition and parameters used.

---

## Flow Definition JSON Format

The `FlowJson` field stores a structured JSON object:

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

### Config Options

| Field                     | Default  | Description                      |
| ------------------------- | -------- | -------------------------------- |
| `maxExecutionTimeSeconds` | `600`    | Maximum seconds the flow can run |
| `onFailure`               | `"Stop"` | `"Stop"` or `"Continue"`         |
| `enableNodeLogging`       | `true`   | Whether to write per-node logs   |

### Supported Node Types in JSON

| Type          | Maps To                            |
| ------------- | ---------------------------------- |
| `Wait`        | WaitNode                           |
| `HttpRequest` | HttpRequestNode                    |
| `EmailSend`   | EmailSendNode                      |
| `Transform`   | TransformNode (static values only) |

> **Note:** Nodes that require C# delegates (`IfElseNode`, `WhileLoopNode`, `DatabaseQueryNode`) cannot be expressed in JSON. Use code-defined flows for those.

---

## Parameter Placeholders

String values in node params support `{{paramName}}` placeholders. When the flow is triggered, the caller provides a dictionary of parameters that replace the placeholders.

**Rules:**

- Syntax: `{{paramName}}` (double curly braces)
- Applied to all string values in `params`, including nested values in `headers` and `placeholders`
- Parameters that don't match any placeholder are ignored
- Unresolved placeholders (no matching key) remain as-is and generate a log warning

**Example:** A definition with `"url": "https://api.example.com/order/{{orderId}}"` triggered with `{ "orderId": "12345" }` resolves to `https://api.example.com/order/12345`.

---

## Node Descriptor Registry

The registry maps type strings to factory functions that create `IFlowNode` instances:

```csharp
["Wait"]        → WaitNode
["HttpRequest"] → HttpRequestNode
["EmailSend"]   → EmailSendNode
["Transform"]   → TransformNode
```

Each factory reads parameters from the JSON descriptor (e.g., `d.GetString("url")`, `d.GetInt("maxRetries", 3)`) and builds the corresponding node.

---

## Versioning

Every save of a `FlowDefinition` increments the `Version` number. Old versions are kept for audit. When triggered, only the **highest-version active** definition with a given `DefinitionKey` is used.

---

## Security

### SSRF Protection

`HttpRequest` node definitions support an `allowedHosts` parameter — a list of permitted domain names. If set, the resolved URL domain must match one of the allowed hosts. This prevents malicious parameter values from redirecting HTTP calls to arbitrary servers.

### No Code Injection

The system intentionally does not support regex evaluation, expression engines, or arbitrary code execution from JSON. Complex logic requires code-defined flows.

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
    "customerPhone": "+8801712345678",
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

## DTOs

### Create/Update Request

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

### Trigger Request

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

| Limitation                     | Reason                                                         |
| ------------------------------ | -------------------------------------------------------------- |
| No `IfElseNode` in JSON        | Requires a C# delegate for the condition                       |
| No `WhileLoopNode` in JSON     | Same — requires a delegate                                     |
| No `DatabaseQueryNode` in JSON | EF query delegates cannot be serialized                        |
| No `ParallelNode` in JSON      | Branch structure is complex; deferred to a future phase        |
| No expression evaluation       | Intentional — prevents code injection from admin-supplied JSON |

For these cases, use code-defined flows via `FlowBuilder`.

---

## File Structure

```
Definitions/
├── FlowDefinitionRunner.cs       ← Loads definition, interpolates params, triggers flow
├── NodeDescriptorRegistry.cs     ← Maps type strings to IFlowNode factories
├── NodeDescriptor.cs             ← Descriptor model with helper methods
└── FlowDefinitionJson.cs         ← JSON deserialization models

Database/Entities/
├── FlowDefinition.cs
└── FlowDefinitionRun.cs

Api/
└── BikiranEngineDefinitionController.cs

Models/
└── FlowDefinitionDTOs.cs
```
