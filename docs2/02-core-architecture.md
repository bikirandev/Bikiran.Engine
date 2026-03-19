# Core Architecture

This document explains the design decisions, execution model, and foundational abstractions that power Bikiran.Engine.

---

## Architecture Approach

### Embedded, Not Standalone

Bikiran.Engine runs inside your .NET application — it is not a separate service. It shares your app's dependency injection container, database context, and HTTP pipeline. This eliminates the need for inter-service communication and makes setup straightforward.

### In-Project First, Package Later

The engine starts as an in-project service (inside `Services/FlowRunner/`). The abstractions are structured so that extracting it into NuGet packages later requires minimal changes.

**Layer separation:**

```
Package layer (abstract)  → No database knowledge, no AppDbContext
Application layer (concrete) → Implements IFlowLogger using AppDbContext
```

### Programmatic Flow Creation

Flows are **not** stored as persistent definitions by default — they are built in code at runtime using the fluent builder API. Each call to `FlowBuilder.StartAsync()` creates one execution record. Database-stored flow definitions are an optional feature added in a later phase.

---

## Execution Model

### Background Processing

When `FlowBuilder.StartAsync()` is called:

1. A UUID `ServiceId` is generated.
2. A `FlowRun` database record is created with status `pending`.
3. The flow is fired in the background via `Task.Run` — the calling thread is not blocked.
4. The `ServiceId` is returned immediately to the caller.

### Sequential Node Execution

Nodes run one after another in the order they were added. Each node:

- Receives the shared `FlowContext` and a `CancellationToken`.
- Returns a `NodeResult` indicating success or failure.
- Can read from and write to the shared context variables.

### Timeout Protection

A `CancellationTokenSource` is created with the configured `MaxExecutionTime` (default: 10 minutes). If the timeout expires, the running node receives a cancellation signal, and the flow is marked as `failed` with the message `"max_execution_time_exceeded"`.

### Failure Strategies

When a node fails, the behavior depends on the `OnFailure` setting:

| Strategy     | Behavior                                                                |
| ------------ | ----------------------------------------------------------------------- |
| **Stop**     | Abort the entire flow immediately                                       |
| **Continue** | Skip the failed node and proceed to the next one                        |
| **Retry**    | Let the node handle retries internally (using its `MaxRetries` setting) |

---

## Core Abstractions

All core abstractions live in `Services/FlowRunner/Core/`.

### IFlowNode

Every node implements this interface:

```csharp
public interface IFlowNode
{
    string Name { get; }        // lowercase_underscore name (e.g. "fetch_user_data")
    string NodeType { get; }    // type label (e.g. "HttpRequest", "IfElse")

    Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken);
}
```

### NodeResult

The result returned by every node after execution:

```csharp
public class NodeResult
{
    public bool Success { get; set; }
    public object? Output { get; set; }
    public string? ErrorMessage { get; set; }
    public string? BranchTaken { get; set; }   // "true" or "false" for IfElse nodes
    public int RetryCount { get; set; }

    public static NodeResult Ok(object? output = null);
    public static NodeResult Fail(string error, int retryCount = 0);
}
```

### FlowContext — Shared State

Nodes communicate through a shared `FlowContext` that carries:

- **Variables** — an in-memory dictionary (`Dictionary<string, object>`) for passing data between nodes.
- **Injected services** — `AppDbContext`, `HttpContext`, `EmailSenderV3Service`, `IServiceProvider`, `ILogger`.

```csharp
public class FlowContext
{
    public string ServiceId { get; internal set; }
    public string FlowName { get; internal set; }

    // Injected services
    public AppDbContext? DbContext { get; set; }
    public HttpContext? HttpContext { get; set; }
    public IServiceProvider? Services { get; set; }
    public ILogger? Logger { get; set; }
    public EmailSenderV3Service? EmailSender { get; set; }

    // Shared variables
    void Set(string key, object value);
    T? Get<T>(string key);
    bool Has(string key);
    IReadOnlyDictionary<string, object> Variables { get; }
}
```

**Setting a value from one node:**

```csharp
context.Set("order_total", 1500.00);
```

**Reading it in a later node:**

```csharp
var total = context.Get<double>("order_total");
```

### FlowRunConfig

Controls how a flow behaves at runtime:

```csharp
public class FlowRunConfig
{
    public TimeSpan MaxExecutionTime { get; set; } = TimeSpan.FromMinutes(10);
    public OnFailureAction OnFailure { get; set; } = OnFailureAction.Stop;
    public bool EnableNodeLogging { get; set; } = true;
    public string TriggerSource { get; set; } = string.Empty;  // e.g. "OrdersV3Controller"
}
```

### Status Enums

**Flow run statuses:**

| Status      | Meaning                            |
| ----------- | ---------------------------------- |
| `Pending`   | Created but not yet started        |
| `Running`   | Currently executing nodes          |
| `Completed` | All nodes finished successfully    |
| `Failed`    | Stopped due to an error or timeout |
| `Cancelled` | Cancelled externally               |

**Node log statuses:**

| Status      | Meaning                                     |
| ----------- | ------------------------------------------- |
| `Pending`   | Queued but not yet executed                 |
| `Running`   | Currently executing                         |
| `Completed` | Finished successfully                       |
| `Failed`    | Finished with an error                      |
| `Skipped`   | Skipped (e.g., when `OnFailure = Continue`) |

---

## Branching (If-Else)

The `IfElseNode` evaluates a condition and routes execution to one of two sub-lists of nodes:

- **TrueBranch** — runs if the condition returns `true`.
- **FalseBranch** — runs if the condition returns `false`.

Branch nodes execute inline during the flow. Each branch node is logged individually.

---

## Logging Abstraction

The engine uses an `IFlowLogger` interface for persistence:

```csharp
public interface IFlowLogger
{
    Task CreateRunAsync(FlowRun run);
    Task UpdateRunAsync(FlowRun run);
    Task CreateNodeLogAsync(FlowNodeLog log);
    Task UpdateNodeLogAsync(FlowNodeLog log);
}
```

The default implementation (`FlowDbLogger`) uses `AppDbContext`. If no database context is available, logging is silently skipped — it is optional but enabled by default.

---

## File Structure

```
Services/FlowRunner/
├── Core/
│   ├── IFlowNode.cs
│   ├── NodeResult.cs
│   ├── FlowContext.cs
│   ├── FlowRunConfig.cs
│   ├── FlowRunStatusEnum.cs
│   ├── IFlowLogger.cs
│   └── FlowAbortException.cs
├── Nodes/
│   └── (all node implementations)
├── FlowBuilder.cs
├── FlowRunner.cs
└── FlowDbLogger.cs
```
