# FlowRunner — Phase 3: Flow Definitions in Database

> **Status:** Planning  
> **Depends on:** Phase 1 (Core Engine) + Phase 2 (Enhanced Nodes) fully implemented  
> **Goal:** Persist reusable flow templates as structured JSON in the database. Allow admins to define, enable/disable, and trigger flows via API without code changes. Support parameter injection at trigger time.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Architecture Decisions](#2-architecture-decisions)
3. [Database Schema](#3-database-schema)
4. [Flow Definition JSON Format](#4-flow-definition-json-format)
5. [Node Descriptor Registry](#5-node-descriptor-registry)
6. [FlowDefinitionBuilder](#6-flowdefinitionbuilder)
7. [Trigger Mechanism](#7-trigger-mechanism)
8. [Admin API Endpoints](#8-admin-api-endpoints)
9. [DTO Definitions](#9-dto-definitions)
10. [File & Folder Structure](#10-file--folder-structure)
11. [Step-by-Step Implementation Guide](#11-step-by-step-implementation-guide)
12. [Usage Examples](#12-usage-examples)
13. [Limitations & Guardrails](#13-limitations--guardrails)

---

## 1. Overview

In Phase 1, flows are defined entirely in C# code — new flows require a code change and a deployment. Phase 3 adds a persistence layer:

- **`FlowDefinition`** table stores a named, versioned JSON description of a flow.
- Admins can create/update definitions via the admin API without touching source code.
- At trigger time, the `FlowDefinitionRunner` deserializes the JSON, instantiates the correct nodes, injects runtime parameters, and passes control to the Phase 1 `FlowRunner`.
- Code-defined flows (Phase 1) continue to work unchanged — the DB-backed system is an additional capability, not a replacement.

### Key Design Constraint

Because `IFlowNode` implementations can contain arbitrary C# delegates (e.g. `IfElseNode.Condition`), full visual flow editing is **not** possible in this phase. Instead, **parameter-driven node templates** are supported: each node type exposes a fixed set of string/int/bool parameters that can be stored in JSON and resolved at runtime.

Complex logic (custom predicates, EF queries) still requires code. Phase 3 covers the common 80% use case: sequential HTTP calls, waits, email sends, and SMS with static inputs.

---

## 2. Architecture Decisions

### 2.1 Node Descriptor vs Full Code Node

Each node in the JSON definition is a **NodeDescriptor** — a plain serializable record with:

- `type`: node type string (e.g. `"HttpRequest"`, `"EmailSend"`, `"Wait"`)
- `name`: lowercase_underscore name
- `params`: `Dictionary<string, object>` of type-specific parameters

The `FlowDefinitionRunner` maps `type` → concrete `IFlowNode` via the **Node Descriptor Registry** (`NodeDescriptorRegistry`).

### 2.2 Template Parameters

Flow definitions support `{{paramName}}` placeholders in string fields. At trigger time, callers supply a `Dictionary<string, string> parameters` which are interpolated before nodes are instantiated.

**Example:** A definition with `"url": "https://api.example.com/order/{{orderId}}"` accepts `{ "orderId": "12345" }` at trigger time, resolving to `https://api.example.com/order/12345`.

### 2.3 Versioning

Each save of a `FlowDefinition` increments `Version`. Old versions are kept for audit. Only the highest-version active definition with a given `DefinitionKey` is used when triggered.

### 2.4 Security Boundary

Flow definitions are admin-only. URL injection via parameters is limited by the existence of `AllowedHosts` on `HttpRequestNode` definitions — if set, the resolved URL domain must match. This prevents SSRF from malicious parameter values.

---

## 3. Database Schema

### 3.1 Table: `FlowDefinition`

```sql
CREATE TABLE FlowDefinition (
    Id              BIGINT AUTO_INCREMENT PRIMARY KEY,
    DefinitionKey   VARCHAR(100) NOT NULL,     -- slug: "order_notification", "domain_verify"
    DisplayName     VARCHAR(200) NOT NULL,     -- Human label shown in admin UI
    Description     TEXT         NOT NULL DEFAULT '',
    Version         INT          NOT NULL DEFAULT 1,
    IsActive        TINYINT(1)   NOT NULL DEFAULT 1,
    FlowJson        MEDIUMTEXT   NOT NULL,     -- JSON: FlowDefinitionJson
    Tags            VARCHAR(500) NOT NULL DEFAULT '',  -- comma-separated
    LastModifiedBy  BIGINT       NOT NULL DEFAULT 0,   -- UserId
    TimeCreated     BIGINT       NOT NULL,
    TimeUpdated     BIGINT       NOT NULL,
    TimeDeleted     BIGINT       NOT NULL DEFAULT 0,
    UNIQUE KEY uq_key_version (DefinitionKey, Version)
);
```

### 3.2 Table: `FlowDefinitionRun` (Trigger log)

Tracks which definition was used for each FlowRun (extends the Phase 1 `FlowRun` table relationship).

```sql
CREATE TABLE FlowDefinitionRun (
    Id              BIGINT AUTO_INCREMENT PRIMARY KEY,
    FlowRunServiceId VARCHAR(32)  NOT NULL,    -- FK → FlowRun.ServiceId
    DefinitionId    BIGINT       NOT NULL,     -- FK → FlowDefinition.Id
    DefinitionKey   VARCHAR(100) NOT NULL,
    DefinitionVersion INT        NOT NULL,
    Parameters      TEXT         NOT NULL DEFAULT '{}',  -- JSON: trigger-time params
    TriggerUserId   BIGINT       NOT NULL DEFAULT 0,
    TriggerSource   VARCHAR(100) NOT NULL DEFAULT '',
    TimeCreated     BIGINT       NOT NULL
);
```

### 3.3 C# Table Class: `FlowDefinition`

```csharp
// Tables/FlowDefinition.cs
[Table("FlowDefinition")]
public class FlowDefinition
{
    [Key, Column("Id")]
    public long Id { get; set; }

    [Required, MaxLength(100), Column("DefinitionKey")]
    public string DefinitionKey { get; set; } = string.Empty;

    [Required, MaxLength(200), Column("DisplayName")]
    public string DisplayName { get; set; } = string.Empty;

    [Column("Description", TypeName = "text")]
    public string Description { get; set; } = string.Empty;

    [Column("Version")]
    public int Version { get; set; } = 1;

    [Column("IsActive")]
    public bool IsActive { get; set; } = true;

    [Required, Column("FlowJson", TypeName = "mediumtext")]
    public string FlowJson { get; set; } = "{}";

    [MaxLength(500), Column("Tags")]
    public string Tags { get; set; } = string.Empty;

    [Column("LastModifiedBy")]
    public long LastModifiedBy { get; set; }

    [Column("TimeCreated")]
    public long TimeCreated { get; set; }

    [Column("TimeUpdated")]
    public long TimeUpdated { get; set; }

    [Column("TimeDeleted")]
    public long TimeDeleted { get; set; }
}
```

### 3.4 C# Table Class: `FlowDefinitionRun`

```csharp
// Tables/FlowDefinitionRun.cs
[Table("FlowDefinitionRun")]
public class FlowDefinitionRun
{
    [Key, Column("Id")]
    public long Id { get; set; }

    [Required, MaxLength(32), Column("FlowRunServiceId")]
    public string FlowRunServiceId { get; set; } = string.Empty;

    [Column("DefinitionId")]
    public long DefinitionId { get; set; }

    [Required, MaxLength(100), Column("DefinitionKey")]
    public string DefinitionKey { get; set; } = string.Empty;

    [Column("DefinitionVersion")]
    public int DefinitionVersion { get; set; }

    [Column("Parameters", TypeName = "text")]
    public string Parameters { get; set; } = "{}";

    [Column("TriggerUserId")]
    public long TriggerUserId { get; set; }

    [MaxLength(100), Column("TriggerSource")]
    public string TriggerSource { get; set; } = string.Empty;

    [Column("TimeCreated")]
    public long TimeCreated { get; set; }
}
```

---

## 4. Flow Definition JSON Format

A `FlowDefinition.FlowJson` stores a `FlowDefinitionJson` object:

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
    },
    {
      "type": "Sms",
      "name": "sms_confirmation",
      "params": {
        "toPhone": "{{customerPhone}}",
        "message": "Order {{orderId}} confirmed. Amount: BDT {{amount}}.",
        "vendor": "Auto",
        "maxRetries": 2
      }
    }
  ]
}
```

### Supported `config.onFailure` values
- `"Stop"` — abort flow on first failed node
- `"Continue"` — skip failed node and proceed
- (Retry works at node level via `RetryNode` in Phase 2)

### Parameter Placeholder Rules
- Syntax: `{{paramName}}` (double curly braces)
- Applied to all string values in `params` (including nested values in `headers` and `placeholders`)
- Parameters not matching any placeholder are ignored
- Unresolved placeholders (no matching key) remain as-is (warn in logs)

---

## 5. Node Descriptor Registry

**File:** `Services/FlowRunner/FlowDefinitionRunner/NodeDescriptorRegistry.cs`

A static registry that maps type strings to factory functions:

```csharp
public static class NodeDescriptorRegistry
{
    private static readonly Dictionary<string, Func<NodeDescriptor, IFlowNode>> _factories = new()
    {
        ["Wait"]         = d => new WaitNode(d.Name) { DelayMs = d.GetInt("delayMs", 1000) },
        ["HttpRequest"]  = d => new HttpRequestNode(d.Name) {
            Url              = d.GetString("url"),
            Method           = new HttpMethod(d.GetString("method", "GET")),
            Body             = d.GetString("body"),
            MaxRetries       = d.GetInt("maxRetries", 3),
            TimeoutSeconds   = d.GetInt("timeoutSeconds", 30),
            OutputKey        = d.GetString("outputKey", $"{d.Name}_response"),
            Headers          = d.GetDict("headers"),
            AllowedHosts     = d.GetStringList("allowedHosts"),  // SSRF guard
        },
        ["EmailSend"]    = d => new EmailSendNode(d.Name) {
            ToEmail      = d.GetString("toEmail"),
            ToName       = d.GetString("toName", ""),
            Subject      = d.GetString("subject"),
            Template     = d.GetString("template"),
            Placeholders = d.GetDict("placeholders"),
        },
        ["Sms"]          = d => new SmsNode(d.Name) {
            ToPhone    = d.GetString("toPhone"),
            Message    = d.GetString("message"),
            Vendor     = d.GetEnum("vendor", SmsVendorEnum.Auto),
            MaxRetries = d.GetInt("maxRetries", 2),
        },
        ["Transform"]    = d => new TransformNode(d.Name) {
            // Static transform: store a constant value (dynamic code not possible from JSON)
            Transform  = _ => d.GetString("value"),
            OutputKey  = d.GetString("outputKey", $"{d.Name}_output"),
        },
        // Phase 2 nodes — add as they become available:
        // ["Notification"] = ...
    };

    public static IFlowNode Build(NodeDescriptor descriptor)
    {
        if (!_factories.TryGetValue(descriptor.Type, out var factory))
            throw new InvalidOperationException($"Unknown node type: '{descriptor.Type}'");

        return factory(descriptor);
    }
}
```

### `NodeDescriptor` Helper Methods

`d.GetString(key, default)`, `d.GetInt(key, default)`, `d.GetBool(key, default)`, `d.GetDict(key)`, `d.GetEnum<T>(key, default)`, `d.GetStringList(key)` — all read from `NodeDescriptor.Params` with safe defaults.

---

## 6. FlowDefinitionBuilder

**File:** `Services/FlowRunner/FlowDefinitionRunner/FlowDefinitionRunner.cs`

The class responsible for:
1. Loading a `FlowDefinition` from DB by key.
2. Deserializing and parameter-interpolating the `FlowJson`.
3. Building `IFlowNode` instances via `NodeDescriptorRegistry`.
4. Delegating to the Phase 1 `FlowRunner.StartAsync()`.
5. Persisting a `FlowDefinitionRun` record.

```csharp
public class FlowDefinitionRunner(AppDbContext _context)
{
    /// <summary>
    /// Trigger a flow from a DB-stored definition.
    /// </summary>
    /// <param name="definitionKey">The DefinitionKey to load.</param>
    /// <param name="parameters">Runtime parameters to inject into {{placeholders}}.</param>
    /// <param name="contextSetup">Optional callback to inject DbContext, EmailSender, etc.</param>
    /// <returns>ServiceId of the created FlowRun.</returns>
    public async Task<string> TriggerAsync(
        string definitionKey,
        Dictionary<string, string>? parameters = null,
        Action<FlowContext>? contextSetup = null,
        string triggerSource = "")
    {
        // 1. Load active definition
        var definition = await _context.FlowDefinition
            .Where(d => d.DefinitionKey == definitionKey && d.IsActive && d.TimeDeleted == 0)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException($"No active FlowDefinition found for key '{definitionKey}'");

        // 2. Interpolate parameters into JSON
        var resolvedJson = InterpolateParameters(definition.FlowJson, parameters ?? new());

        // 3. Deserialize
        var defJson = JsonConvert.DeserializeObject<FlowDefinitionJson>(resolvedJson)
            ?? throw new InvalidOperationException("FlowDefinition JSON is malformed.");

        // 4. Build nodes
        var nodes = defJson.Nodes.Select(NodeDescriptorRegistry.Build).ToList();

        // 5. Build config
        var config = new FlowRunConfig
        {
            MaxExecutionTime = TimeSpan.FromSeconds(defJson.Config?.MaxExecutionTimeSeconds ?? 600),
            OnFailure = Enum2.Parse(defJson.Config?.OnFailure ?? "Stop", OnFailureAction.Stop),
            EnableNodeLogging = defJson.Config?.EnableNodeLogging ?? true,
            TriggerSource = triggerSource
        };

        // 6. Build context
        var context = new FlowContext { DbContext = _context };
        contextSetup?.Invoke(context);

        // 7. Trigger via Phase 1 FlowRunner
        var serviceId = await FlowBuilder
            .Create(defJson.Name)
            .Configure(_ => { /* already built above */ })
            ._StartWithPrebuilt(context, config, nodes); // internal method

        // 8. Log definition run
        _context.FlowDefinitionRun.Add(new FlowDefinitionRun {
            FlowRunServiceId = serviceId,
            DefinitionId     = definition.Id,
            DefinitionKey    = definition.DefinitionKey,
            DefinitionVersion = definition.Version,
            Parameters       = JsonConvert.SerializeObject(parameters ?? new()),
            TriggerSource    = triggerSource,
            TimeCreated      = TimeOperation.GetUnixTime()
        });
        await _context.SaveChangesAsync();

        return serviceId;
    }

    private static string InterpolateParameters(string json, Dictionary<string, string> parameters)
    {
        foreach (var (key, value) in parameters)
            json = json.Replace($"{{{{{key}}}}}", value,
                StringComparison.OrdinalIgnoreCase);

        return json;
    }
}
```

---

## 7. Trigger Mechanism

### 7.1 Manual Trigger (Admin API)

Admin calls `POST admin/flow-runner/definitions/{key}/trigger` with a JSON body of parameters. The engine loads the definition, interpolates, and runs it. Returns `serviceId`.

### 7.2 Programmatic Trigger (from C# code)

```csharp
// In any controller or service:
var _definitionRunner = new FlowDefinitionRunner(_context);

var serviceId = await _definitionRunner.TriggerAsync(
    definitionKey: "order_notification_flow",
    parameters: new() {
        { "orderId", order.Id.ToString() },
        { "customerEmail", user.Email },
        { "amount", invoice.Amount.ToString("F2") }
    },
    contextSetup: ctx => {
        ctx.HttpContext = HttpContext;
        ctx.EmailSender = _emailSender;
    },
    triggerSource: nameof(OrdersV3Controller)
);
```

### 7.3 Future: Webhook Trigger (Phase 4+)

A public endpoint `POST api/v3/flow-runner/webhook/{webhookToken}` can accept external HTTP POST and trigger a definition. The `webhookToken` is stored on the `FlowDefinition` record. **Do not implement in Phase 3** — note for Phase 4/5.

---

## 8. Admin API Endpoints

**Controller:** `ControllersAdm/FlowDefinitionAdmController.cs`  
**Route prefix:** `admin/flow-runner/definitions`

| Method | Route | Description |
|---|---|---|
| `GET` | `admin/flow-runner/definitions` | List all definitions (paginated) |
| `GET` | `admin/flow-runner/definitions/{key}` | Get latest version of a definition |
| `GET` | `admin/flow-runner/definitions/{key}/versions` | List all versions of a definition |
| `POST` | `admin/flow-runner/definitions` | Create new definition |
| `PUT` | `admin/flow-runner/definitions/{key}` | Update (creates new version) |
| `PATCH` | `admin/flow-runner/definitions/{key}/toggle` | Enable or disable a definition |
| `DELETE` | `admin/flow-runner/definitions/{key}` | Soft-delete a definition |
| `POST` | `admin/flow-runner/definitions/{key}/trigger` | Manually trigger a definition |
| `GET` | `admin/flow-runner/definitions/{key}/runs` | List runs triggered from this definition |

---

## 9. DTO Definitions

**File:** `Models/FlowRunner/V3/FlowDefinitionDTOs.cs`

```csharp
// Request: Create or Update a definition
public class FlowDefinitionSaveRequestDTO
{
    public string DefinitionKey { get; set; } = string.Empty;  // e.g. "order_notification"
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FlowJson { get; set; } = string.Empty;       // full JSON as string
    public string Tags { get; set; } = string.Empty;           // comma-separated
}

// Request: Trigger with parameters
public class FlowDefinitionTriggerRequestDTO
{
    public Dictionary<string, string> Parameters { get; set; } = new();
    public string TriggerSource { get; set; } = string.Empty;
}

// Response: Definition summary (list)
public class FlowDefinitionSummaryDTO
{
    public long Id { get; set; }
    public string DefinitionKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Version { get; set; }
    public bool IsActive { get; set; }
    public string Tags { get; set; } = string.Empty;
    public long TimeUpdated { get; set; }
}

// Response: Full definition detail
public class FlowDefinitionDetailDTO : FlowDefinitionSummaryDTO
{
    public string Description { get; set; } = string.Empty;
    public string FlowJson { get; set; } = string.Empty;
    public long LastModifiedBy { get; set; }
}

// Response: Trigger result
public class FlowDefinitionTriggerResponseDTO
{
    public string ServiceId { get; set; } = string.Empty;
    public string DefinitionKey { get; set; } = string.Empty;
    public int DefinitionVersion { get; set; }
    public string FlowName { get; set; } = string.Empty;
}
```

---

## 10. File & Folder Structure

```
Services/
└── FlowRunner/
    └── FlowDefinitionRunner/
        ├── FlowDefinitionRunner.cs      ← Loads definition, triggers flow
        ├── NodeDescriptorRegistry.cs    ← Type → IFlowNode factory map
        ├── NodeDescriptor.cs            ← NodeDescriptor model + helpers
        └── FlowDefinitionJson.cs        ← Deserialization models

Tables/
├── FlowDefinition.cs                    ← EF Core table model (NEW)
└── FlowDefinitionRun.cs                 ← EF Core table model (NEW)

ControllersAdm/
└── FlowDefinitionAdmController.cs       ← 9 admin endpoints

Models/
└── FlowRunner/
    └── V3/
        └── FlowDefinitionDTOs.cs        ← Request/Response DTOs

Data/
└── AppDbContext.cs                      ← Add DbSet<FlowDefinition>, DbSet<FlowDefinitionRun>

Migrations/
└── ..._AddFlowDefinitionTables.cs       ← EF migration
```

---

## 11. Step-by-Step Implementation Guide

### Step 1 — Create DB Tables

Create `Tables/FlowDefinition.cs`, `Tables/FlowDefinitionRun.cs`. Add `DbSet` entries to `AppDbContext`. Run EF migration.

---

### Step 2 — Create `FlowDefinitionJson` Models

**File:** `Services/FlowRunner/FlowDefinitionRunner/FlowDefinitionJson.cs`

```csharp
public class FlowDefinitionJson
{
    public string Name { get; set; } = string.Empty;
    public FlowDefinitionConfigJson? Config { get; set; }
    public List<NodeDescriptor> Nodes { get; set; } = new();
}

public class FlowDefinitionConfigJson
{
    public int MaxExecutionTimeSeconds { get; set; } = 600;
    public string OnFailure { get; set; } = "Stop";
    public bool EnableNodeLogging { get; set; } = true;
}
```

---

### Step 3 — Create `NodeDescriptor`

**File:** `Services/FlowRunner/FlowDefinitionRunner/NodeDescriptor.cs`

```csharp
public class NodeDescriptor
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object> Params { get; set; } = new();

    public string GetString(string key, string def = "") =>
        Params.TryGetValue(key, out var v) ? v?.ToString() ?? def : def;

    public int GetInt(string key, int def = 0) =>
        Params.TryGetValue(key, out var v) && int.TryParse(v?.ToString(), out var i) ? i : def;

    public bool GetBool(string key, bool def = false) =>
        Params.TryGetValue(key, out var v) && bool.TryParse(v?.ToString(), out var b) ? b : def;

    public Dictionary<string, string> GetDict(string key) =>
        Params.TryGetValue(key, out var v) && v is Newtonsoft.Json.Linq.JObject jo
            ? jo.ToObject<Dictionary<string, string>>() ?? new()
            : new();

    public List<string> GetStringList(string key) =>
        Params.TryGetValue(key, out var v) && v is Newtonsoft.Json.Linq.JArray ja
            ? ja.ToObject<List<string>>() ?? new()
            : new();

    public T GetEnum<T>(string key, T def) where T : struct =>
        Enum.TryParse<T>(GetString(key), true, out var e) ? e : def;
}
```

---

### Step 4 — Create `NodeDescriptorRegistry`

Build out the registry as described in Section 5. Start with `Wait`, `HttpRequest`, `EmailSend`, `Sms`. Add `Transform` and others.

---

### Step 5 — Create `FlowDefinitionRunner`

Implement the `TriggerAsync` method with parameter interpolation, deserialization, and Phase 1 delegation.

**Note:** This requires a minor internal method addition to `FlowBuilder` — `_StartWithPrebuilt(context, config, nodes)` — to allow passing pre-built context and config directly. Alternatively, use a constructor/factory approach on `FlowRunner` directly.

---

### Step 6 — Create Admin CRUD Controller

Implement `FlowDefinitionAdmController` with all 9 endpoints. Use `ApiResponseV3<T>` pattern. Validate JSON on create/update by deserializing it before saving.

---

### Step 7 — Create Admin Trigger Endpoint

The trigger endpoint should:
1. Validate definition exists and is active.
2. Run `FlowDefinitionRunner.TriggerAsync(...)`.
3. Return `{ serviceId, definitionKey, version, flowName }`.

---

### Step 8 — Write Integration Test

1. Create a definition via `POST admin/flow-runner/definitions`.
2. Trigger it with parameters via `POST admin/flow-runner/definitions/{key}/trigger`.
3. Poll `GET admin/flow-runner/runs/{serviceId}` to confirm it ran.
4. Update the definition via `PUT` and verify version incremented.

---

## 12. Usage Examples

### Creating a definition via Admin API

```http
POST /admin/flow-runner/definitions
Authorization: ...
Content-Type: application/json

{
  "definitionKey": "welcome_email_flow",
  "displayName": "Welcome Email Flow",
  "description": "Sends a welcome email to a new user",
  "tags": "auth,email,onboarding",
  "flowJson": "{\"name\":\"welcome_email_flow\",\"config\":{\"maxExecutionTimeSeconds\":60,\"onFailure\":\"Continue\"},\"nodes\":[{\"type\":\"Wait\",\"name\":\"brief_delay\",\"params\":{\"delayMs\":500}},{\"type\":\"EmailSend\",\"name\":\"send_welcome\",\"params\":{\"toEmail\":\"{{email}}\",\"toName\":\"{{name}}\",\"subject\":\"Welcome to n8n Clouds!\",\"template\":\"AUTH_CREATE_ACCOUNT\",\"placeholders\":{\"DisplayName\":\"{{name}}\",\"Email\":\"{{email}}\"}}}]}"
}
```

### Triggering from another controller

```csharp
// In AuthV3Controller after account creation:
var serviceId = await _definitionRunner.TriggerAsync(
    definitionKey: "welcome_email_flow",
    parameters: new() { { "email", userEmail }, { "name", displayName } },
    contextSetup: ctx => { ctx.HttpContext = HttpContext; ctx.EmailSender = _emailSender; },
    triggerSource: nameof(AuthV3Controller)
);
```

---

## 13. Limitations & Guardrails

| Limitation | Reason |
|---|---|
| No C# delegate support in JSON | Delegates cannot be serialized; use Phase 1 code-defined flows for complex conditions |
| `IfElseNode` not supported in Phase 3 | Requires a `Condition` delegate; cannot be expressed in JSON without an expression engine |
| `WhileLoopNode` not supported | Same reason as `IfElseNode` |
| `ParallelNode` partial support | JSON format for branches is complex; defer to Phase 4 |
| SSRF protection via `AllowedHosts` | Always set `allowedHosts` on `HttpRequest` nodes in production definitions |
| No regex/expression evaluation | Intentional — avoids code injection risk from admin-supplied JSON |

---

*End of Phase 3 Plan — Ready for implementation after Phase 2 is complete.*
