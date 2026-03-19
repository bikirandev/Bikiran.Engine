# FlowRunner — Embedded Workflow Engine: Comprehensive Implementation Plan

> **Status:** Planning  
> **Goal:** Build an n8n-style workflow automation engine embedded inside the Bikiran Web API, optionally extractable as a NuGet package.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Architecture Decisions](#2-architecture-decisions)
3. [Core Concepts & Terminology](#3-core-concepts--terminology)
4. [Database Schema](#4-database-schema)
5. [Core Abstractions](#5-core-abstractions)
6. [Node Library](#6-node-library)
7. [FlowBuilder Fluent API](#7-flowbuilder-fluent-api)
8. [Execution Engine](#8-execution-engine)
9. [Progress Logging & Observability](#9-progress-logging--observability)
10. [Admin API Endpoints](#10-admin-api-endpoints)
11. [File & Folder Structure](#11-file--folder-structure)
12. [Step-by-Step Implementation Guide](#12-step-by-step-implementation-guide)
13. [Usage Examples](#13-usage-examples)
14. [Future Roadmap](#14-future-roadmap)

---

## 1. Overview

**FlowRunner** is an embedded workflow automation engine that lets developers define multi-step automated processes (flows) using a fluent builder API. It is inspired by n8n but lives entirely inside your .NET application, sharing its DI container, database context, and HTTP context.

### Key Capabilities

| Capability               | Details                                                     |
| ------------------------ | ----------------------------------------------------------- |
| **Fluent Builder API**   | Chain `.AddNode(...)` calls to compose a workflow           |
| **Node Library**         | HTTP Request, If-Else, While Loop, Wait, Email Send         |
| **Contextual**           | Accepts `AppDbContext`, `HttpContext`, custom services      |
| **Persistent Logs**      | Every node execution logged to DB, queryable by `ServiceId` |
| **Configurable**         | Max execution time, on-failure strategy, retry policy       |
| **Unique Service ID**    | Each run returns a 32-char `ServiceId` for tracing          |
| **Background Execution** | Flows run inside `Task.Run` without blocking the request    |

---

## 2. Architecture Decisions

### 2.1 In-Project vs NuGet Package

**Decision:** Start as an in-project service (inside `Services/FlowRunner/`). The abstractions should be clean enough that extraction to a NuGet package later requires minimal changes.

**Layer separation for future NuGet extraction:**

```
Package (abstract layer) → No DB knowledge, no AppDbContext
App (concrete layer)     → Implements IFlowLogger with AppDbContext
```

For now, `AppDbContext` is injected via an optional `IFlowLogger` interface, keeping the engine itself clean.

### 2.2 Execution Model

- Flows are **not** persisted as definitions; they are **created programmatically** at runtime.
- Each `FlowBuilder.StartAsync()` call creates one **FlowRun** database record.
- Each node execution creates one **FlowNodeLog** record.
- Flows run **asynchronously** (`Task.Run`) and do not block the calling thread.

### 2.3 Branching (If-Else)

- `IfElseNode` evaluates a predicate and routes to either a `TrueBranch` or `FalseBranch` list of nodes.
- Branches are sub-lists of `IFlowNode`, evaluated inline during the run.

### 2.4 Shared State

- Nodes communicate via a `FlowContext.Variables` dictionary (`Dictionary<string, object>`).
- Output of any node can be stored under a key: `ctx.Set("response_body", ...)`.
- Subsequent nodes can read it: `ctx.Get<string>("response_body")`.

---

## 3. Core Concepts & Terminology

| Term            | Definition                                                  |
| --------------- | ----------------------------------------------------------- |
| **Flow**        | A complete workflow composed of ordered nodes               |
| **Node**        | A single unit of work (HTTP request, wait, condition, etc.) |
| **FlowRun**     | One execution of a Flow, identified by a `ServiceId`        |
| **NodeLog**     | DB record for one node's execution within a FlowRun         |
| **FlowContext** | Shared state & injected services passed to every node       |
| **ServiceId**   | 32-char unique identifier for a FlowRun                     |
| **Branch**      | Conditional sub-list of nodes inside an `IfElseNode`        |
| **OnFailure**   | Strategy when a node fails: `Stop`, `Continue`, or `Retry`  |

---

## 4. Database Schema

### 4.1 Table: `FlowRun`

Tracks each workflow execution.

```sql
CREATE TABLE FlowRun (
    Id            BIGINT AUTO_INCREMENT PRIMARY KEY,
    ServiceId     VARCHAR(32)  NOT NULL UNIQUE,   -- 32-char unique run ID
    FlowName      VARCHAR(100) NOT NULL,           -- Human-readable name
    Status        VARCHAR(20)  NOT NULL,           -- pending|running|completed|failed|cancelled
    TriggerSource VARCHAR(100) NOT NULL DEFAULT '', -- e.g. "OrdersV3Controller"
    Config        TEXT         NOT NULL,            -- JSON: FlowRunConfig
    ContextMeta   TEXT         NOT NULL,            -- JSON: caller context info
    TotalNodes    INT          NOT NULL DEFAULT 0,
    CompletedNodes INT         NOT NULL DEFAULT 0,
    ErrorMessage  VARCHAR(500)         DEFAULT NULL,
    StartedAt     BIGINT       NOT NULL DEFAULT 0,
    CompletedAt   BIGINT       NOT NULL DEFAULT 0,
    DurationMs    BIGINT       NOT NULL DEFAULT 0,
    CreatorUserId BIGINT       NOT NULL DEFAULT 0,
    IpString      VARCHAR(100) NOT NULL DEFAULT '',
    TimeCreated   BIGINT       NOT NULL,
    TimeUpdated   BIGINT       NOT NULL,
    TimeDeleted   BIGINT       NOT NULL DEFAULT 0
);
```

### 4.2 Table: `FlowNodeLog`

One record per node per run.

```sql
CREATE TABLE FlowNodeLog (
    Id          BIGINT AUTO_INCREMENT PRIMARY KEY,
    ServiceId   VARCHAR(32)  NOT NULL,       -- FK → FlowRun.ServiceId
    NodeName    VARCHAR(100) NOT NULL,       -- lowercase_underscore
    NodeType    VARCHAR(50)  NOT NULL,       -- HttpRequest|IfElse|WhileLoop|Wait|EmailSend
    Sequence    INT          NOT NULL,       -- 1-based order in the flow
    Status      VARCHAR(20)  NOT NULL,       -- pending|running|completed|failed|skipped
    InputData   TEXT         NOT NULL,       -- JSON snapshot of node input
    OutputData  TEXT         NOT NULL,       -- JSON snapshot of node output
    ErrorMessage VARCHAR(500)        DEFAULT NULL,
    BranchTaken VARCHAR(20)          DEFAULT NULL, -- "true"/"false" for IfElse
    RetryCount  INT          NOT NULL DEFAULT 0,
    StartedAt   BIGINT       NOT NULL DEFAULT 0,
    CompletedAt BIGINT       NOT NULL DEFAULT 0,
    DurationMs  BIGINT       NOT NULL DEFAULT 0,
    TimeCreated BIGINT       NOT NULL,
    TimeUpdated BIGINT       NOT NULL
);
```

### 4.3 C# Table Classes

**`Tables/FlowRun.cs`**

```csharp
[Table("FlowRun")]
public class FlowRun
{
    [Key]
    [Column("Id")]
    public long Id { get; set; }

    [Required, MaxLength(32), Column("ServiceId")]
    public string ServiceId { get; set; } = string.Empty;

    [Required, MaxLength(100), Column("FlowName")]
    public string FlowName { get; set; } = string.Empty;

    [Required, MaxLength(20), Column("Status")]
    public string Status { get; set; } = "pending";  // FlowRunStatusEnum

    [Required, MaxLength(100), Column("TriggerSource")]
    public string TriggerSource { get; set; } = string.Empty;

    [Required, Column("Config", TypeName = "text")]
    public string Config { get; set; } = "{}";        // JSON: FlowRunConfig

    [Required, Column("ContextMeta", TypeName = "text")]
    public string ContextMeta { get; set; } = "{}";   // JSON: caller metadata

    [Column("TotalNodes")]
    public int TotalNodes { get; set; }

    [Column("CompletedNodes")]
    public int CompletedNodes { get; set; }

    [MaxLength(500), Column("ErrorMessage")]
    public string? ErrorMessage { get; set; }

    [Column("StartedAt")]
    public long StartedAt { get; set; }

    [Column("CompletedAt")]
    public long CompletedAt { get; set; }

    [Column("DurationMs")]
    public long DurationMs { get; set; }

    [Column("CreatorUserId")]
    public long CreatorUserId { get; set; }

    [MaxLength(100), Column("IpString")]
    public string IpString { get; set; } = string.Empty;

    [Column("TimeCreated")]
    public long TimeCreated { get; set; }

    [Column("TimeUpdated")]
    public long TimeUpdated { get; set; }

    [Column("TimeDeleted")]
    public long TimeDeleted { get; set; }
}
```

**`Tables/FlowNodeLog.cs`**

```csharp
[Table("FlowNodeLog")]
public class FlowNodeLog
{
    [Key]
    [Column("Id")]
    public long Id { get; set; }

    [Required, MaxLength(32), Column("ServiceId")]
    public string ServiceId { get; set; } = string.Empty;

    [Required, MaxLength(100), Column("NodeName")]
    public string NodeName { get; set; } = string.Empty;

    [Required, MaxLength(50), Column("NodeType")]
    public string NodeType { get; set; } = string.Empty;

    [Column("Sequence")]
    public int Sequence { get; set; }

    [Required, MaxLength(20), Column("Status")]
    public string Status { get; set; } = "pending";   // NodeLogStatusEnum

    [Required, Column("InputData", TypeName = "text")]
    public string InputData { get; set; } = "{}";

    [Required, Column("OutputData", TypeName = "text")]
    public string OutputData { get; set; } = "{}";

    [MaxLength(500), Column("ErrorMessage")]
    public string? ErrorMessage { get; set; }

    [MaxLength(20), Column("BranchTaken")]
    public string? BranchTaken { get; set; }

    [Column("RetryCount")]
    public int RetryCount { get; set; }

    [Column("StartedAt")]
    public long StartedAt { get; set; }

    [Column("CompletedAt")]
    public long CompletedAt { get; set; }

    [Column("DurationMs")]
    public long DurationMs { get; set; }

    [Column("TimeCreated")]
    public long TimeCreated { get; set; }

    [Column("TimeUpdated")]
    public long TimeUpdated { get; set; }
}
```

---

## 5. Core Abstractions

All core abstractions live in `Services/FlowRunner/Core/`.

### 5.1 `IFlowNode` Interface

```csharp
// Services/FlowRunner/Core/IFlowNode.cs
public interface IFlowNode
{
    /// <summary>Node name — must be lowercase_underscore (e.g. "fetch_user_data")</summary>
    string Name { get; }

    /// <summary>Human-readable type label (e.g. "HttpRequest", "IfElse")</summary>
    string NodeType { get; }

    /// <summary>Execute the node and return a result.</summary>
    Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken);
}
```

### 5.2 `NodeResult`

```csharp
// Services/FlowRunner/Core/NodeResult.cs
public class NodeResult
{
    public bool Success { get; set; }
    public object? Output { get; set; }
    public string? ErrorMessage { get; set; }
    public string? BranchTaken { get; set; }  // "true" | "false" for IfElse
    public int RetryCount { get; set; }

    public static NodeResult Ok(object? output = null) =>
        new() { Success = true, Output = output };

    public static NodeResult Fail(string error, int retryCount = 0) =>
        new() { Success = false, ErrorMessage = error, RetryCount = retryCount };
}
```

### 5.3 `FlowContext`

```csharp
// Services/FlowRunner/Core/FlowContext.cs
public class FlowContext
{
    public string ServiceId { get; internal set; } = string.Empty;
    public string FlowName { get; internal set; } = string.Empty;

    // Injected application services (optional)
    public AppDbContext? DbContext { get; set; }
    public HttpContext? HttpContext { get; set; }
    public IServiceProvider? Services { get; set; }
    public ILogger? Logger { get; set; }

    // Email service shortcut
    public EmailSenderV3Service? EmailSender { get; set; }

    // Shared in-memory state (pass data between nodes)
    private readonly Dictionary<string, object> _variables = new();

    public void Set(string key, object value) => _variables[key] = value;

    public T? Get<T>(string key) =>
        _variables.TryGetValue(key, out var val) && val is T typed ? typed : default;

    public bool Has(string key) => _variables.ContainsKey(key);

    public IReadOnlyDictionary<string, object> Variables => _variables;
}
```

### 5.4 `FlowRunConfig`

```csharp
// Services/FlowRunner/Core/FlowRunConfig.cs
public class FlowRunConfig
{
    /// <summary>Max wall-clock time the entire flow may run. Default: 10 minutes.</summary>
    public TimeSpan MaxExecutionTime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>What to do when a node fails.</summary>
    public OnFailureAction OnFailure { get; set; } = OnFailureAction.Stop;

    /// <summary>Whether to write per-node logs to the DB.</summary>
    public bool EnableNodeLogging { get; set; } = true;

    /// <summary>Caller label used for tracing (e.g. controller name).</summary>
    public string TriggerSource { get; set; } = string.Empty;
}

public enum OnFailureAction
{
    Stop,       // Abort the flow immediately
    Continue,   // Skip failed node and continue
    Retry       // Retry using node's RetryCount setting
}
```

### 5.5 Status Enums

```csharp
// Services/FlowRunner/Core/FlowRunStatusEnum.cs
public enum FlowRunStatusEnum
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public enum NodeLogStatusEnum
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}
```

---

## 6. Node Library

All nodes live in `Services/FlowRunner/Nodes/`.

---

### 6.1 `HttpRequestNode`

**File:** `Services/FlowRunner/Nodes/HttpRequestNode.cs`

**Purpose:** Makes an outbound HTTP call. On success, stores response body in `FlowContext` under a configurable key.

**Properties:**

| Property            | Type                        | Default             | Description                                 |
| ------------------- | --------------------------- | ------------------- | ------------------------------------------- |
| `Name`              | `string`                    | required            | Node name (lowercase_underscore)            |
| `Url`               | `string`                    | required            | Target URL                                  |
| `Method`            | `HttpMethod`                | `GET`               | HTTP verb                                   |
| `Headers`           | `Dictionary<string,string>` | empty               | Extra request headers                       |
| `Body`              | `string?`                   | null                | Request body (JSON string)                  |
| `TimeoutSeconds`    | `int`                       | `30`                | Per-attempt timeout                         |
| `MaxRetries`        | `int`                       | `3`                 | Number of retry attempts on failure         |
| `RetryDelaySeconds` | `int`                       | `2`                 | Wait between retries                        |
| `OutputKey`         | `string`                    | `"{Name}_response"` | Key to store raw response body in context   |
| `ExpectStatusCode`  | `int?`                      | null                | If set, non-matching status codes = failure |

**Behavior:**

1. Build `HttpRequestMessage` with headers + body.
2. Send with retry loop up to `MaxRetries`.
3. On success: store response body in `context.Set(OutputKey, body)`.
4. On failure after all retries: return `NodeResult.Fail(errorMessage)`.

---

### 6.2 `IfElseNode`

**File:** `Services/FlowRunner/Nodes/IfElseNode.cs`

**Purpose:** Evaluates a predicate on `FlowContext` and routes execution to one of two sub-node lists.

**Properties:**

| Property      | Type                      | Description                        |
| ------------- | ------------------------- | ---------------------------------- |
| `Name`        | `string`                  | Node name                          |
| `Condition`   | `Func<FlowContext, bool>` | Predicate evaluated at runtime     |
| `TrueBranch`  | `List<IFlowNode>`         | Nodes to run if condition is true  |
| `FalseBranch` | `List<IFlowNode>`         | Nodes to run if condition is false |

**Behavior:**

1. Evaluate `Condition(context)`.
2. Set `BranchTaken` to `"true"` or `"false"`.
3. Run all nodes in the chosen branch, sequentially.
4. Log each branch node as its own `FlowNodeLog` (with sequence prefixed by branch).
5. Return `NodeResult.Ok(branchTaken)`.

> **Note:** The `IfElseNode` itself is logged as one row; the inner branch nodes are each logged separately.

---

### 6.3 `WhileLoopNode`

**File:** `Services/FlowRunner/Nodes/WhileLoopNode.cs`

**Purpose:** Repeatedly executes a body of nodes while a condition is true.

**Properties:**

| Property           | Type                      | Default  | Description                        |
| ------------------ | ------------------------- | -------- | ---------------------------------- |
| `Name`             | `string`                  | required | Node name                          |
| `Condition`        | `Func<FlowContext, bool>` | required | Loop continues while this is true  |
| `Body`             | `List<IFlowNode>`         | required | Nodes to execute each iteration    |
| `MaxIterations`    | `int`                     | `10`     | Hard cap to prevent infinite loops |
| `IterationDelayMs` | `int`                     | `0`      | Wait between iterations (ms)       |

**Behavior:**

1. Check `Condition(context)` before each iteration.
2. If true and iteration count < `MaxIterations`: run body nodes.
3. If `MaxIterations` is reached: return `NodeResult.Fail("max_iterations_reached")`.
4. Store iteration count in context as `"{Name}_iteration_count"`.
5. Apply `IterationDelayMs` between iterations.

---

### 6.4 `WaitNode`

**File:** `Services/FlowRunner/Nodes/WaitNode.cs`

**Purpose:** Pauses execution for a fixed duration.

**Properties:**

| Property  | Type     | Default  | Description          |
| --------- | -------- | -------- | -------------------- |
| `Name`    | `string` | required | Node name            |
| `DelayMs` | `int`    | `1000`   | Milliseconds to wait |

**Behavior:**

1. Call `Task.Delay(DelayMs, cancellationToken)`.
2. Return `NodeResult.Ok()`.
3. If cancelled: return `NodeResult.Fail("cancelled")`.

---

### 6.5 `EmailSendNode`

**File:** `Services/FlowRunner/Nodes/EmailSendNode.cs`

**Purpose:** Sends an email using the application's `EmailSenderV3Service`.

**Properties:**

| Property              | Type                                            | Default  | Description                          |
| --------------------- | ----------------------------------------------- | -------- | ------------------------------------ |
| `Name`                | `string`                                        | required | Node name                            |
| `ToEmail`             | `string`                                        | required | Recipient email                      |
| `ToName`              | `string`                                        | `""`     | Recipient display name               |
| `Subject`             | `string`                                        | required | Email subject                        |
| `Template`            | `string`                                        | required | Template key (e.g. `"ORDER_CREATE"`) |
| `Placeholders`        | `Dictionary<string,string>`                     | empty    | Template placeholders                |
| `PlaceholderResolver` | `Func<FlowContext, Dictionary<string,string>>?` | null     | Dynamic placeholder factory          |

**Behavior:**

1. Retrieve `EmailSenderV3Service` from `context.EmailSender` (or `context.Services`).
2. If `PlaceholderResolver` is set, merge its result into `Placeholders`.
3. Send via fluent API.
4. On success: return `NodeResult.Ok()`.
5. On failure: return `NodeResult.Fail(ex.Message)`.

> **Requirement:** `FlowContext.EmailSender` must be injected via `.WithContext(c => c.EmailSender = _emailSender)`.

---

## 7. FlowBuilder Fluent API

**File:** `Services/FlowRunner/FlowBuilder.cs`

### 7.1 API Surface

```csharp
// Create a flow
var serviceId = await FlowBuilder
    .Create("my_flow_name")                        // (1) Initialize with name
    .Configure(cfg => {                            // (2) Optional config
        cfg.MaxExecutionTime = TimeSpan.FromMinutes(5);
        cfg.OnFailure = OnFailureAction.Stop;
        cfg.TriggerSource = nameof(OrdersV3Controller);
    })
    .WithContext(ctx => {                          // (3) Inject contexts
        ctx.DbContext = _context;
        ctx.HttpContext = HttpContext;
        ctx.EmailSender = _emailSender;
        ctx.Logger = _logger;
    })
    .AddNode(new HttpRequestNode("fetch_order") {  // (4) Add nodes
        Url = "https://api.example.com/order/1",
        Method = HttpMethod.Get,
        MaxRetries = 3,
        OutputKey = "order_data"
    })
    .AddNode(new IfElseNode("check_order_status") {
        Condition = ctx => ctx.Get<string>("order_data") != null,
        TrueBranch = new List<IFlowNode> {
            new EmailSendNode("notify_customer") {
                ToEmail = "user@example.com",
                Subject = "Order Ready",
                Template = "ORDER_NOTIFY"
            }
        },
        FalseBranch = new List<IFlowNode> {
            new WaitNode("wait_before_retry") { DelayMs = 3000 }
        }
    })
    .AddNode(new WaitNode("final_wait") { DelayMs = 500 })
    .StartAsync();                                 // (5) Fire and return ServiceId

// serviceId is a 32-char string you can store or return to the caller
```

### 7.2 Method Reference

| Method                     | Description                                                        |
| -------------------------- | ------------------------------------------------------------------ |
| `FlowBuilder.Create(name)` | Start builder; sets flow name                                      |
| `.Configure(action)`       | Configure `FlowRunConfig` (optional)                               |
| `.WithContext(action)`     | Set `FlowContext` properties (DbContext, HttpContext, etc.)        |
| `.AddNode(node)`           | Append a node to the sequential execution list                     |
| `.StartAsync()`            | Persist `FlowRun` record, fire async execution, return `ServiceId` |
| `.StartAndWaitAsync()`     | Same but awaits full completion (for sync use cases)               |

---

## 8. Execution Engine

**File:** `Services/FlowRunner/FlowRunner.cs`

### 8.1 Execution Flow

```
StartAsync()
  │
  ├── Generate ServiceId (IdGenerator.Generate32())
  ├── Persist FlowRun (Status=pending, TotalNodes=N)
  ├── Fire Task.Run(ExecuteFlowAsync)  <── non-blocking
  └── Return ServiceId

ExecuteFlowAsync()
  │
  ├── Update FlowRun.Status = running, StartedAt = now
  ├── Create CancellationTokenSource (MaxExecutionTime)
  │
  ├── ForEach node in sequence:
  │     ├── Persist FlowNodeLog (Status=running, Sequence=i)
  │     ├── node.ExecuteAsync(context, ct)
  │     ├── Update FlowNodeLog (Status=completed/failed, Output, DurationMs)
  │     ├── Update FlowRun.CompletedNodes++
  │     │
  │     └── If FAILED:
  │           ├── OnFailure=Stop   → throw FlowAbortException (exits loop)
  │           ├── OnFailure=Continue → log warning, continue to next node
  │           └── OnFailure=Retry  → handled by node internally (MaxRetries)
  │
  ├── Update FlowRun.Status = completed, CompletedAt, DurationMs
  │
  └── On any exception:
        └── Update FlowRun.Status = failed, ErrorMessage
```

### 8.2 Timeout Handling

A `CancellationTokenSource` linked to `FlowRunConfig.MaxExecutionTime` is created at the start. All nodes receive this token and are expected to pass it to any async operations. If the token is cancelled:

- The currently running node will throw `OperationCanceledException`.
- The engine catches this, marks the run as `failed` with `"max_execution_time_exceeded"`.

### 8.3 Progress Tracking

After each node completes, the engine updates `FlowRun.CompletedNodes`. The progress percentage can be calculated as:

```
ProgressPercent = (CompletedNodes / TotalNodes) * 100
```

This is queryable via the admin endpoint at any time.

### 8.4 `FlowAbortException`

```csharp
// Services/FlowRunner/Core/FlowAbortException.cs
public class FlowAbortException(string nodeN, string reason)
    : Exception($"Flow aborted at node '{nodeN}': {reason}");
```

---

## 9. Progress Logging & Observability

### 9.1 Log Writer Interface

```csharp
// Services/FlowRunner/Core/IFlowLogger.cs
public interface IFlowLogger
{
    Task CreateRunAsync(FlowRun run);
    Task UpdateRunAsync(FlowRun run);
    Task CreateNodeLogAsync(FlowNodeLog log);
    Task UpdateNodeLogAsync(FlowNodeLog log);
}
```

**Default implementation:** `Services/FlowRunner/FlowDbLogger.cs`

Uses `AppDbContext` passed via `FlowContext.DbContext`. If no `DbContext` is available, logs are silently skipped (logging is optional but enabled by default).

### 9.2 What Gets Logged

**FlowRun record updates:**

| Moment                | Status      | Fields Updated                                  |
| --------------------- | ----------- | ----------------------------------------------- |
| `StartAsync()` called | `pending`   | `ServiceId`, `FlowName`, `Config`, `TotalNodes` |
| Execution starts      | `running`   | `StartedAt`, `Status`                           |
| All nodes done        | `completed` | `CompletedAt`, `DurationMs`, `Status`           |
| Any failure           | `failed`    | `ErrorMessage`, `Status`, `CompletedAt`         |

**FlowNodeLog record updates:**

| Moment          | Status      | Fields Updated                            |
| --------------- | ----------- | ----------------------------------------- |
| Node starts     | `running`   | `StartedAt`, `Sequence`, `InputData`      |
| Node succeeds   | `completed` | `OutputData`, `CompletedAt`, `DurationMs` |
| Node fails      | `failed`    | `ErrorMessage`, `RetryCount`              |
| Node skipped    | `skipped`   | (no time data)                            |
| IfElse resolved | `completed` | `BranchTaken`                             |

### 9.3 Context Metadata Snapshot

When a flow is created, a context metadata snapshot is saved as JSON in `FlowRun.ContextMeta`:

```json
{
  "IpAddress": "192.168.1.1",
  "UserId": 12345,
  "RequestPath": "/api/v3/orders",
  "UserAgent": "Mozilla/5.0 ...",
  "Timestamp": 1742000000
}
```

This is populated automatically if `HttpContext` is provided.

---

## 10. Admin API Endpoints

**Controller:** `ControllersAdm/FlowRunnerAdmController.cs`  
**Route prefix:** `admin/flow-runner`

### 10.1 Endpoints

| Method   | Route                                         | Description                 |
| -------- | --------------------------------------------- | --------------------------- |
| `GET`    | `admin/flow-runner/runs`                      | List all runs (paginated)   |
| `GET`    | `admin/flow-runner/runs/{serviceId}`          | Get run details + node logs |
| `GET`    | `admin/flow-runner/runs/{serviceId}/progress` | Get live progress %         |
| `GET`    | `admin/flow-runner/runs/status/{status}`      | Filter runs by status       |
| `DELETE` | `admin/flow-runner/runs/{serviceId}`          | Soft-delete a run record    |

### 10.2 Response: Run Detail

```json
{
  "error": false,
  "message": "Flow run details",
  "data": {
    "serviceId": "a1b2c3d4...",
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
        "errorMessage": null
      }
    ]
  }
}
```

---

## 11. File & Folder Structure

```
Services/
└── FlowRunner/
    ├── Core/
    │   ├── IFlowNode.cs            ← Node interface
    │   ├── NodeResult.cs           ← Result returned by nodes
    │   ├── FlowContext.cs          ← Shared state + injected services
    │   ├── FlowRunConfig.cs        ← Configuration (timeout, onFailure)
    │   ├── FlowRunStatusEnum.cs    ← Enums for FlowRun & NodeLog status
    │   ├── IFlowLogger.cs          ← Persistence abstraction
    │   └── FlowAbortException.cs   ← Thrown when OnFailure=Stop
    │
    ├── Nodes/
    │   ├── HttpRequestNode.cs
    │   ├── IfElseNode.cs
    │   ├── WhileLoopNode.cs
    │   ├── WaitNode.cs
    │   └── EmailSendNode.cs
    │
    ├── FlowBuilder.cs              ← Fluent builder API
    ├── FlowRunner.cs               ← Execution engine
    └── FlowDbLogger.cs             ← IFlowLogger impl using AppDbContext

Tables/
├── FlowRun.cs                      ← EF Core table model
└── FlowNodeLog.cs                  ← EF Core table model

ControllersAdm/
└── FlowRunnerAdmController.cs      ← Admin read endpoints

Data/
└── AppDbContext.cs                 ← Add DbSet<FlowRun> and DbSet<FlowNodeLog>

Migrations/
└── ..._AddFlowRunnerTables.cs      ← EF migration

docs-backend-plan/
└── 22-flowrunner-workflow-engine-plan.md  ← This document
```

---

## 12. Step-by-Step Implementation Guide

Follow these steps in order. Each step is self-contained and testable before moving on.

---

### Step 1 — Create Database Tables

**Files to create:**

- `Tables/FlowRun.cs`
- `Tables/FlowNodeLog.cs`

**Files to modify:**

- `Data/AppDbContext.cs` — add `DbSet<FlowRun>` and `DbSet<FlowNodeLog>`

**Verify:** Run `dotnet build` — no errors.

---

### Step 2 — Create EF Core Migration

```powershell
dotnet ef migrations add AddFlowRunnerTables
dotnet ef database update
```

**Verify:** Tables `FlowRun` and `FlowNodeLog` appear in the database.

---

### Step 3 — Create Core Abstractions

**Files to create (in order):**

1. `Services/FlowRunner/Core/FlowRunStatusEnum.cs`
2. `Services/FlowRunner/Core/NodeResult.cs`
3. `Services/FlowRunner/Core/IFlowNode.cs`
4. `Services/FlowRunner/Core/FlowContext.cs`
5. `Services/FlowRunner/Core/FlowRunConfig.cs`
6. `Services/FlowRunner/Core/IFlowLogger.cs`
7. `Services/FlowRunner/Core/FlowAbortException.cs`

**Verify:** `dotnet build` — no errors. No node implementations yet.

---

### Step 4 — Implement Nodes

Create each node in `Services/FlowRunner/Nodes/`. Each must implement `IFlowNode`.

**Recommended order:**

1. `WaitNode` — simplest, no dependencies
2. `HttpRequestNode` — no app context needed
3. `EmailSendNode` — needs `EmailSenderV3Service`
4. `IfElseNode` — needs recursive node execution
5. `WhileLoopNode` — needs recursive node execution + loop management

**Verify after each node:** Instantiate and call `ExecuteAsync` in a `TestController` endpoint.

---

### Step 5 — Implement `FlowDbLogger`

**File:** `Services/FlowRunner/FlowDbLogger.cs`

Implements `IFlowLogger` using the `AppDbContext` from `FlowContext.DbContext`.

**Important:** If `DbContext` is null (logging disabled), methods must be no-ops:

```csharp
public async Task CreateRunAsync(FlowRun run)
{
    if (_context == null) return;
    _context.FlowRun.Add(run);
    await _context.SaveChangesAsync();
}
```

**Verify:** Write a unit test or use a TestController endpoint to call each logger method.

---

### Step 6 — Implement `FlowRunner` (Execution Engine)

**File:** `Services/FlowRunner/FlowRunner.cs`

This is the core engine. Key responsibilities:

- Generate `ServiceId` using `IdGenerator.Generate32()`
- Persist initial `FlowRun` via `IFlowLogger`
- Fire `Task.Run(ExecuteFlowAsync)` (non-blocking)
- Handle per-node logging, error handling, and flow-level timeout

**Key methods:**

- `internal async Task<string> StartAsync(FlowContext ctx, FlowRunConfig cfg, List<IFlowNode> nodes)`
- `private async Task ExecuteFlowAsync(...)` — the actual async run loop
- `private async Task ExecuteNodeAsync(IFlowNode node, int sequence, ...)` — single-node executor

**Verify:** Use a TestController to build a minimal 2-node flow and confirm `FlowRun` + `FlowNodeLog` rows are created.

---

### Step 7 — Implement `FlowBuilder`

**File:** `Services/FlowRunner/FlowBuilder.cs`

The fluent API entry point. `FlowBuilder` is a simple builder that accumulates config, context, and nodes, then delegates to `FlowRunner` on `StartAsync()`.

```csharp
public class FlowBuilder
{
    private readonly string _name;
    private FlowRunConfig _config = new();
    private FlowContext _context = new();
    private readonly List<IFlowNode> _nodes = new();

    private FlowBuilder(string name) => _name = name;

    public static FlowBuilder Create(string name) => new(name);

    public FlowBuilder Configure(Action<FlowRunConfig> configure) { ... }
    public FlowBuilder WithContext(Action<FlowContext> configure) { ... }
    public FlowBuilder AddNode(IFlowNode node) { _nodes.Add(node); return this; }

    public async Task<string> StartAsync() { ... }         // Fire-and-forget
    public async Task<string> StartAndWaitAsync() { ... }  // Await completion
}
```

**Verify:** Full end-to-end test: build a 3-node flow, call `StartAsync()`, confirm `serviceId` is returned, check DB records.

---

### Step 8 — Create Admin Controller

**File:** `ControllersAdm/FlowRunnerAdmController.cs`

Implements the 5 endpoints from Section 10. Uses standard `ApiResponseV3<T>` pattern. Requires founder/admin auth.

**Verify:** Hit endpoints with Swagger. Confirm paginated list, detail view, and progress endpoint all work.

---

### Step 9 — Register Services in `Program.cs`

The `FlowRunner` service is **not** registered in DI — `FlowBuilder` is constructed directly. However, you may optionally register a `FlowBuilderFactory` if you want DI-resolved instances:

```csharp
// No registration needed for FlowBuilder (static factory pattern)
// But ensure these are already registered:
builder.Services.AddScoped<EmailSenderV3Service>();
builder.Services.AddHttpClient(); // for HttpRequestNode's IHttpClientFactory
```

For `HttpRequestNode` to use `IHttpClientFactory`, register it via `AddHttpClient()`.

---

### Step 10 — Integration Test

Use the `TestController` (dev-only) to create a full end-to-end test flow:

```csharp
[HttpGet("test-flow")]
public async Task<ActionResult> TestFlow()
{
    var serviceId = await FlowBuilder
        .Create("test_flow")
        .Configure(c => {
            c.MaxExecutionTime = TimeSpan.FromSeconds(30);
            c.TriggerSource = "TestController";
        })
        .WithContext(ctx => {
            ctx.DbContext = _context;
            ctx.HttpContext = HttpContext;
            ctx.EmailSender = _emailSender;
        })
        .AddNode(new WaitNode("initial_wait") { DelayMs = 500 })
        .AddNode(new HttpRequestNode("check_api") {
            Url = "https://httpbin.org/get",
            Method = HttpMethod.Get,
            MaxRetries = 2,
            OutputKey = "api_response"
        })
        .AddNode(new IfElseNode("evaluate_result") {
            Condition = ctx => ctx.Has("api_response"),
            TrueBranch = [new WaitNode("success_wait") { DelayMs = 200 }],
            FalseBranch = [new WaitNode("fail_wait") { DelayMs = 200 }]
        })
        .StartAsync();

    return Ok(new { ServiceId = serviceId });
}
```

Then poll `GET admin/flow-runner/runs/{serviceId}` to observe the run progressing.

---

## 13. Usage Examples

### Example A: Post-Order Notification Flow

```csharp
// In OrdersV3Controller, after creating an order:
var serviceId = await FlowBuilder
    .Create("order_post_processing")
    .Configure(cfg => {
        cfg.MaxExecutionTime = TimeSpan.FromMinutes(2);
        cfg.OnFailure = OnFailureAction.Continue; // Don't fail order for notification errors
        cfg.TriggerSource = nameof(OrdersV3Controller);
    })
    .WithContext(ctx => {
        ctx.DbContext = _context;
        ctx.HttpContext = HttpContext;
        ctx.EmailSender = _emailSender;
    })
    .AddNode(new WaitNode("brief_delay") { DelayMs = 1000 })
    .AddNode(new EmailSendNode("send_order_confirmation") {
        ToEmail = userEmail,
        ToName = userName,
        Subject = $"Order #{orderId} Confirmed",
        Template = "ORDER_CREATE",
        Placeholders = new() {
            { "OrderId", orderId.ToString() },
            { "Amount", amount.ToString("F2") }
        }
    })
    .AddNode(new HttpRequestNode("notify_webhook") {
        Url = ConfigsManager.GetString("ORDER_WEBHOOK_URL", ""),
        Method = HttpMethod.Post,
        Body = JsonConvert.SerializeObject(new { orderId, status = "created" }),
        MaxRetries = 3,
        TimeoutSeconds = 10
    })
    .StartAsync(); // Non-blocking, returns immediately

// Store serviceId in the order record for tracing
order.FlowServiceId = serviceId;
```

---

### Example B: Polling Until Ready

```csharp
var serviceId = await FlowBuilder
    .Create("wait_for_provisioning")
    .WithContext(ctx => { ctx.DbContext = _context; ctx.HttpContext = HttpContext; })
    .AddNode(new WhileLoopNode("poll_status") {
        Condition = ctx => ctx.Get<string>("provision_status") != "ready",
        MaxIterations = 20,
        IterationDelayMs = 5000, // Poll every 5 seconds
        Body = [
            new HttpRequestNode("check_status") {
                Url = $"https://api.appocean.com/instance/{instanceId}/status",
                MaxRetries = 2,
                OutputKey = "provision_status"
            }
        ]
    })
    .AddNode(new EmailSendNode("notify_ready") {
        ToEmail = userEmail,
        Subject = "Your instance is ready!",
        Template = "INSTANCE_READY",
        Placeholders = new() { { "InstanceId", instanceId.ToString() } }
    })
    .StartAsync();
```

---

### Example C: Conditional Branching Flow

```csharp
var serviceId = await FlowBuilder
    .Create("payment_verification_flow")
    .Configure(cfg => cfg.OnFailure = OnFailureAction.Stop)
    .WithContext(ctx => { ctx.DbContext = _context; ctx.HttpContext = HttpContext; })
    .AddNode(new HttpRequestNode("verify_payment") {
        Url = $"https://api.payment.com/verify/{paymentId}",
        OutputKey = "payment_status",
        MaxRetries = 3
    })
    .AddNode(new IfElseNode("payment_check") {
        Condition = ctx => ctx.Get<string>("payment_status") == "verified",
        TrueBranch = [
            new EmailSendNode("success_email") {
                ToEmail = userEmail, Subject = "Payment Verified",
                Template = "PAYMENT_VERIFIED"
            }
        ],
        FalseBranch = [
            new WaitNode("wait_before_alert") { DelayMs = 2000 },
            new EmailSendNode("failure_alert") {
                ToEmail = adminEmail, Subject = "Payment Failed",
                Template = "PAYMENT_FAILED_ADMIN"
            }
        ]
    })
    .StartAsync();
```

---

## 14. Future Roadmap

Each phase has its own detailed plan document. Implement sequentially — later phases depend on earlier ones being stable.

---

### Phase 2 — Enhanced Nodes

> **Full plan:** [phase-2-enhanced-nodes.md](phase-2-enhanced-nodes.md)

Adds 6 new node types:

| Node | Description |
|---|---|
| `DatabaseQueryNode` | EF Core query; result stored in context |
| `TransformNode` | Pure context transformation (no I/O) |
| `RetryNode` | Wrap any node with configurable retry + exponential backoff |
| `ParallelNode` | Run multiple node branches concurrently via `Task.WhenAll` |
| `SmsNode` | SMS via SMSAPI (BD) or Infobip (international), auto-detected |
| `NotificationNode` | Firebase FCM push to device token or topic |

---

### Phase 3 — Flow Definitions in Database

> **Full plan:** [phase-3-flow-definitions-in-db.md](phase-3-flow-definitions-in-db.md)

Store reusable flow templates as JSON in the `FlowDefinition` table. Admins can create, version, and trigger flows without code changes.

**Key additions:**
- `FlowDefinition` + `FlowDefinitionRun` tables
- `FlowDefinitionJson` serialization format with `{{placeholder}}` parameter injection
- `NodeDescriptorRegistry` — maps type string → `IFlowNode` at runtime
- `FlowDefinitionRunner` — loads definition from DB, interpolates params, delegates to Phase 1 engine
- Admin CRUD API: `ControllersAdm/FlowDefinitionAdmController.cs` (9 endpoints)

**Limitation:** C# delegates (`IfElseNode.Condition`, `WhileLoopNode.Condition`, `DatabaseQueryNode.Query`) cannot be stored in JSON — use Phase 1 code-defined flows for those.

---

### Phase 4 — Scheduled Flow Execution (Quartz.NET)

> **Full plan:** [phase-4-scheduling.md](phase-4-scheduling.md)

Run flows automatically on a schedule. Leverages **Quartz.NET** already installed in the project (`Quartz 3.15.1`).

**Key additions:**
- `FlowSchedule` table: cron, interval, or one-time schedules
- `FlowScheduleJob` — Quartz `IJob` that triggers `FlowDefinitionRunner.TriggerAsync`
- `FlowSchedulerService` — loads all active schedules from DB on startup, supports runtime add/remove
- Built-in dynamic placeholders: `{{today_date}}`, `{{unix_now}}`, `{{year}}`, `{{month}}`
- Admin API: `ControllersAdm/FlowScheduleAdmController.cs` (8 endpoints)
- Misfire handling: cron skips missed fires; interval continues from last position

**Schedule types:**

| Type | Description | Example |
|---|---|---|
| `cron` | Quartz 6-field cron expression | `"0 0 8 * * ?"` = every day at 8 AM |
| `interval` | Fixed period in minutes | Every 15 minutes |
| `once` | Single future Unix timestamp | Run once tomorrow at noon |

---

### Phase 5 — NuGet Package Extraction

> **Full plan:** [phase-5-nuget-extraction.md](phase-5-nuget-extraction.md)

Extract FlowRunner into a standalone multi-package NuGet library. Any .NET 9 application can then use FlowRunner without the Bikiran web application.

**5 packages:**

| Package | Contents |
|---|---|
| `Bikiran.FlowRunner.Core` | Engine, builder, all generic nodes, `InMemoryFlowLogger` |
| `Bikiran.FlowRunner.EfCore` | `FlowDbLogger`, `DatabaseQueryNode<T>`, EF entities + extensions |
| `Bikiran.FlowRunner.Email` | `IFlowEmailSender` abstraction + `EmailSendNode` |
| `Bikiran.FlowRunner.Firebase` | `NotificationNode` + `IFlowFirebaseInitializer` |
| `Bikiran.FlowRunner.Scheduling` | `FlowScheduleJob`, `FlowSchedulerService`, definition runner |

**Migration path:** Install packages → implement bridge adapters in-app (`BikirianFlowEmailSenderAdapter`, `FirebaseInitializer`) → delete in-app source → run tests.

---

*End of Plan — Ready for Step 1 implementation.*
