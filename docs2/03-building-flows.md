# Building Flows

Flows are built using the `FlowBuilder` class. You chain method calls to define what your workflow does and how it behaves.

---

## Basic Structure

```csharp
var serviceId = await FlowBuilder
    .Create("flow_name")                               // 1. Name the flow
    .Configure(cfg => { /* runtime settings */ })       // 2. Set timeout, failure handling
    .WithContext(ctx => { /* inject services */ })       // 3. Provide database, HTTP context
    .AddNode(new WaitNode("step_1") { DelayMs = 500 })  // 4. Add steps in order
    .AddNode(new HttpRequestNode("step_2") { /* ... */ })
    .StartAsync();                                      // 5. Start and get the run ID
```

---

## Builder Methods

| Method                     | Required           | Description                                                     |
| -------------------------- | ------------------ | --------------------------------------------------------------- |
| `FlowBuilder.Create(name)` | Yes                | Creates a new builder with the given flow name                  |
| `.Configure(action)`       | No                 | Sets runtime options (timeout, failure strategy)                |
| `.WithContext(action)`     | No                 | Injects services into the flow (database, HTTP context, logger) |
| `.AddNode(node)`           | Yes (at least one) | Adds a step to the execution sequence                           |
| `.OnSuccess(node)`         | No                 | Adds a handler that runs only when all main nodes succeed       |
| `.OnFail(node)`            | No                 | Adds a handler that runs only when the flow fails               |
| `.OnFinish(node)`          | No                 | Adds a handler that always runs after success/fail handlers     |
| `.StartAsync()`            | Yes                | Saves the run, starts background execution, returns ServiceId   |
| `.StartAndWaitAsync()`     | No                 | Same as StartAsync but waits for the flow to finish             |

---

## Configuration

Use `.Configure()` to control how the flow behaves:

```csharp
.Configure(cfg =>
{
    cfg.MaxExecutionTime = TimeSpan.FromMinutes(5);
    cfg.OnFailure = OnFailureAction.Stop;
    cfg.EnableNodeLogging = true;
    cfg.TriggerSource = "OrdersController";
})
```

| Setting             | Default    | Description                                       |
| ------------------- | ---------- | ------------------------------------------------- |
| `MaxExecutionTime`  | 10 minutes | Maximum time the flow can run before cancellation |
| `OnFailure`         | `Stop`     | What happens when a step fails                    |
| `EnableNodeLogging` | true       | Write per-step records to the database            |
| `TriggerSource`     | `""`       | A label showing where the flow was started from   |

### Failure Strategies

| Strategy     | Behavior                                             |
| ------------ | ---------------------------------------------------- |
| **Stop**     | Cancel the entire flow immediately when a step fails |
| **Continue** | Skip the failed step and move on to the next one     |

For retry behavior, wrap individual nodes with `RetryNode`. See [Built-in Nodes](04-built-in-nodes.md).

---

## Injecting Context

Use `.WithContext()` to provide services that nodes can access:

```csharp
.WithContext(ctx =>
{
    ctx.HttpContext = HttpContext;
    ctx.Logger = _logger;
})
```

When `HttpContext` is provided, the engine automatically captures caller metadata (IP address, user ID, request path, user agent) and saves it with the run record.

> **DI Scope:** The engine creates its own long-lived DI scope for each flow run. This scope outlives the HTTP request, so background flows can safely resolve services from it. Use `context.GetDbContext<T>()` inside nodes instead of passing a request-scoped `DbContext`.

---

## Shared Variables

Nodes communicate through a shared dictionary in `FlowContext`. This is how data passes from one step to the next.

**Writing a value:**

```csharp
context.Set("order_total", 1500.00);
```

**Reading a value:**

```csharp
var total = context.Get<double>("order_total");
```

**Checking if a key exists:**

```csharp
if (context.Has("order_total"))
{
    // key is available
}
```

### FlowContext Properties

| Property      | Type              | Description                                                |
| ------------- | ----------------- | ---------------------------------------------------------- |
| `ServiceId`   | string            | Unique run identifier (UUID)                               |
| `FlowName`    | string            | Name of the current flow                                   |
| `HttpContext` | HttpContext?      | Original HTTP request context                              |
| `Logger`      | ILogger?          | Structured logger                                          |
| `Services`    | IServiceProvider? | Flow-scoped DI service provider                            |
| `DbContext`   | object?           | Host app's database context (legacy — prefer GetDbContext) |
| `FlowStatus`  | FlowRunStatus?    | Final status after main nodes complete                     |
| `FlowError`   | string?           | Error message if flow failed; null on success              |

### FlowContext Methods

| Method                   | Returns | Description                                            |
| ------------------------ | ------- | ------------------------------------------------------ |
| `Set(key, value)`        | void    | Stores a value in shared context                       |
| `Get<T>(key)`            | T?      | Retrieves a typed value (default if not found)         |
| `Has(key)`               | bool    | Checks if a key exists                                 |
| `GetDbContext<T>()`      | T?      | Resolves a DbContext from the flow-scoped DI container |
| `GetCredential<T>(name)` | T       | Gets a named credential registered at startup          |

---

## Resolving DbContext in Nodes

When a flow runs in the background via `StartAsync()`, the HTTP request finishes immediately. Any request-scoped `DbContext` passed via `.WithContext()` will be disposed. To avoid errors, use `GetDbContext<T>()` inside your nodes:

```csharp
public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
{
    var db = context.GetDbContext<AppDbContext>();
    if (db == null)
        return NodeResult.Fail("AppDbContext not registered in DI");

    var record = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
    // ...
    return NodeResult.Ok(record);
}
```

This resolves `AppDbContext` from the engine's long-lived DI scope, which stays alive for the entire flow.

---

## Lifecycle Events

Use lifecycle methods to run nodes after the main flow completes. This is useful for logging, cleanup, and alerts.

| Method         | Fires When                               | Use Case                       |
| -------------- | ---------------------------------------- | ------------------------------ |
| `.OnSuccess()` | All main nodes completed without failure | Success logging, notifications |
| `.OnFail()`    | The flow ended with a failure            | Alert notifications, rollback  |
| `.OnFinish()`  | Always, after success/fail handlers run  | Final cleanup, audit logging   |

```csharp
var serviceId = await FlowBuilder
    .Create("provision_domain")
    .AddNode(new HttpRequestNode("add_dns") { /* ... */ })
    .AddNode(new WaitNode("propagation_delay") { DelayMs = 15000 })
    .OnSuccess(new HttpRequestNode("notify_success") {
        Url = "https://api.example.com/webhook/success",
        Method = HttpMethod.Post
    })
    .OnFail(new EmailSendNode("alert_admin") {
        ToEmail = "admin@example.com",
        Subject = "Provisioning failed",
        HtmlBodyResolver = ctx => $"<p>Error: {ctx.FlowError}</p>"
    })
    .OnFinish(new HttpRequestNode("audit_log") {
        Url = "https://api.example.com/audit",
        Method = HttpMethod.Post
    })
    .StartAsync();
```

### Execution Order

```
Main nodes → Flow status saved to DB → OnSuccess or OnFail → OnFinish
```

### Important Rules

- Lifecycle nodes are **not** counted in `TotalNodes` or `CompletedNodes`
- They do **not** affect `DurationMs`, `CompletedAt`, or `ErrorMessage`
- The flow's final status is committed to the database **before** lifecycle nodes run
- Failures in lifecycle nodes are logged but never change the flow's final status
- You can chain multiple handlers per event — they run in the order added

### Checking the Outcome in OnFinish

Since `OnFinish` runs regardless of the outcome, check `FlowStatus` and `FlowError`:

```csharp
.OnFinish(new TransformNode("audit") {
    Transform = ctx =>
    {
        var succeeded = ctx.FlowStatus == FlowRunStatus.Completed;
        var error = ctx.FlowError; // null if succeeded

        return succeeded
            ? $"Flow completed successfully"
            : $"Flow failed: {error}";
    },
    OutputKey = "audit_message"
})
```

---

## Execution Model

### StartAsync (Background)

```
StartAsync()
  ├── Generate a unique ServiceId (UUID)
  ├── Save a FlowRun record (status = pending)
  ├── Start background execution via Task.Run
  └── Return the ServiceId immediately
```

### StartAndWaitAsync (Blocking)

Same as `StartAsync()` but blocks the caller until the flow finishes before returning the ServiceId.

### How Steps Run

```
Background Execution
  ├── Update FlowRun status → running
  ├── Start a timeout timer (MaxExecutionTime)
  │
  ├── For each node in order:
  │     ├── Save a NodeLog record (status = running)
  │     ├── Run the node's ExecuteAsync method
  │     ├── Update NodeLog (status = completed or failed)
  │     ├── Update FlowRun progress counter
  │     │
  │     └── If the node fails:
  │           ├── Stop     → abort the entire flow
  │           └── Continue → skip and move to next node
  │
  ├── Set FlowContext.FlowStatus and FlowContext.FlowError
  ├── Save final FlowRun status to DB (completed or failed)
  │
  ├── Run OnSuccess or OnFail handlers
  └── Run OnFinish handlers
```

---

## Flow Statuses

### Flow Run Status

| Status      | Meaning                              |
| ----------- | ------------------------------------ |
| `Pending`   | Created but not yet started          |
| `Running`   | Currently executing nodes            |
| `Completed` | All main nodes finished successfully |
| `Failed`    | Stopped due to error or timeout      |
| `Cancelled` | Cancelled externally (reserved)      |

### Node Log Status

| Status      | Meaning                     |
| ----------- | --------------------------- |
| `Pending`   | Queued but not yet executed |
| `Running`   | Currently executing         |
| `Completed` | Finished successfully       |
| `Failed`    | Finished with an error      |
| `Skipped`   | Node was bypassed           |

---

## Progress Tracking

The engine tracks progress as nodes complete:

- `TotalNodes` — number of main nodes in the flow (excludes lifecycle nodes)
- `CompletedNodes` — number of main nodes completed so far
- Progress percentage: `CompletedNodes / TotalNodes × 100`

Check progress via the admin API: `GET /api/bikiran-engine/runs/{serviceId}/progress`

---

## Context Metadata

When `HttpContext` is provided, the engine captures and saves:

| Field        | Source                                      |
| ------------ | ------------------------------------------- |
| IP Address   | `HttpContext.Connection.RemoteIpAddress`    |
| User ID      | Custom header or claim                      |
| Request Path | `HttpContext.Request.Path`                  |
| User Agent   | `HttpContext.Request.Headers["User-Agent"]` |
| Timestamp    | Current UTC time                            |

This metadata is stored in the `ContextMeta` column of the `FlowRun` table as JSON.
