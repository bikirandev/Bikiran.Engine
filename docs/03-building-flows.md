# Building Flows

Flows are built using the `FlowBuilder` class. It provides a step-by-step method chain for defining what your workflow does and how it behaves.

---

## Quick Overview

```csharp
var serviceId = await FlowBuilder
    .Create("flow_name")                                   // 1. Name the flow
    .Configure(cfg => { /* runtime settings */ })           // 2. Set timeout, failure handling, etc.
    .WithContext(ctx => { /* provide services */ })          // 3. Provide HTTP context, logger, etc.
    .Wait("Pausing briefly", TimeSpan.FromMilliseconds(500))  // 4. Add steps in order
    .AddNode(new HttpRequestNode("Step2") { /* ... */ })
    .StartAsync();                                          // 5. Start and get the run ID
```

---

## Builder Methods

| Method                     | Required           | What It Does                                                             |
| -------------------------- | ------------------ | ------------------------------------------------------------------------ |
| `FlowBuilder.Create(name)` | Yes                | Creates a new builder with the given flow name                           |
| `.Configure(action)`       | No                 | Sets runtime options like timeout and failure strategy                   |
| `.WithContext(action)`     | No                 | Provides services (HTTP context, logger) to the flow                     |
| `.AddNode(node)`           | Yes (at least one) | Adds a step to the execution sequence                                    |
| `.StartingNode(message)`   | No                 | Adds a starting marker node with an optional pause                       |
| `.EndingNode(message)`     | No                 | Adds an ending marker node                                               |
| `.Wait(message, delay)`    | No                 | Adds a wait step that pauses for the given `TimeSpan`                    |
| `.OnSuccess(node)`         | No                 | Adds a step that runs only when all main steps succeed                   |
| `.OnFail(node)`            | No                 | Adds a step that runs only when the flow fails                           |
| `.OnFinish(node)`          | No                 | Adds a step that always runs after success/fail handlers                 |
| `.StartAsync()`            | Yes                | Saves the run record, starts background execution, returns the ServiceId |
| `.StartAndWaitAsync()`     | No                 | Same as `StartAsync()` but waits for the flow to finish before returning |

---

## Configuration

Use `.Configure()` to control how the flow behaves at runtime:

```csharp
.Configure(cfg => {
    cfg.MaxExecutionTime = TimeSpan.FromMinutes(5);
    cfg.OnFailure = OnFailureAction.Stop;
    cfg.EnableNodeLogging = true;
    cfg.TriggerSource = "OrdersV3Controller";
})
```

| Setting             | Default    | Description                                          |
| ------------------- | ---------- | ---------------------------------------------------- |
| `MaxExecutionTime`  | 10 minutes | Maximum time the flow can run before being cancelled |
| `OnFailure`         | `Stop`     | What happens when a step fails                       |
| `EnableNodeLogging` | `true`     | Whether to write per-step records to the database    |
| `TriggerSource`     | `""`       | A label showing where the flow was triggered from    |

### Failure Strategies

| Strategy     | What Happens                                         |
| ------------ | ---------------------------------------------------- |
| **Stop**     | Cancel the entire flow immediately when a step fails |
| **Continue** | Skip the failed step and move on to the next one     |

For retry behavior, wrap individual steps with `RetryNode` instead of changing the global failure strategy.

---

## Pre-Run Validation

When you call `.StartAsync()` or `.StartAndWaitAsync()`, the engine validates the entire flow **before** execution begins. If any configuration is invalid, an `InvalidOperationException` is thrown immediately with a clear, actionable message — so you can fix problems during development rather than debugging failures at runtime.

**What is validated:**

| Check                    | Error Example                                                                |
| ------------------------ | ---------------------------------------------------------------------------- |
| Flow name is not empty   | _"Flow name is required. Pass a non-empty name to FlowBuilder.Create(...)."_ |
| At least one node exists | _"Flow 'my_flow' has no nodes. Add at least one node with .AddNode()..."_    |
| MaxExecutionTime > 0     | _"Flow 'my_flow': MaxExecutionTime must be a positive duration..."_          |
| All node names are valid | _"Flow 'my_flow', main phase: Node name 'bad name' must be PascalCase..."_   |
| No duplicate node names  | _"Flow 'my_flow': Duplicate node name 'FetchOrder' found..."_                |

This validation runs across all phases: main nodes, OnSuccess, OnFail, and OnFinish.

---

## Providing Context

Use `.WithContext()` to give the flow access to services that steps can use during execution:

```csharp
.WithContext(ctx => {
    ctx.HttpContext = HttpContext;      // Captures caller metadata (IP, user ID, etc.)
    ctx.Logger = _logger;              // For structured logging
})
```

When `HttpContext` is provided, the engine automatically saves caller metadata (IP address, user ID, request path, user agent) with the run record.

> **About Dependency Injection:** The engine creates its own long-lived DI scope for each flow run. This scope outlives the HTTP request, so background flows can safely access services from it. Inside your steps, use `context.GetDbContext<T>()` or `context.Services` instead of passing a request-scoped database context through `.WithContext()`. See [Using the Database in Steps](#using-the-database-in-steps) below.

---

## Sharing Data Between Steps

Steps communicate through a shared in-memory dictionary. This is the primary way data passes from one step to the next.

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

The shared context also provides access to:

| Property                 | Description                                                                  |
| ------------------------ | ---------------------------------------------------------------------------- |
| `ServiceId`              | The unique run identifier                                                    |
| `FlowName`               | The name of the current flow                                                 |
| `HttpContext`            | The original HTTP request context                                            |
| `Services`               | The flow-scoped DI service provider                                          |
| `Logger`                 | A structured logger                                                          |
| `GetDbContext<T>()`      | Resolves a database context from the flow-scoped DI container                |
| `GetCredential<T>(name)` | Retrieves a named credential registered at startup                           |
| `FlowStatus`             | The final flow status (available in lifecycle event handlers)                |
| `FlowError`              | The error message if the flow failed (available in lifecycle event handlers) |

---

## Using the Database in Steps

When a flow runs in the background via `StartAsync()`, the HTTP request that created the flow finishes immediately — and any request-scoped database context is disposed. To avoid "Cannot access a disposed context" errors, use `GetDbContext<T>()` inside your steps:

```csharp
public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
{
    var db = context.GetDbContext<AppDbContext>();
    if (db == null)
        return NodeResult.Fail("AppDbContext not registered in DI");

    var record = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
    // ...
}
```

This resolves your database context from the engine's long-lived DI scope, which remains alive for the entire flow execution.

> **Important:** Do not pass a database context through `.WithContext()` for background flows. That instance will be disposed when the HTTP request ends.

---

## Lifecycle Events

Lifecycle events let you run steps after the main flow completes. This is useful for logging, cleanup, alerts, or audit trails.

| Method         | When It Runs                             | Common Use                     |
| -------------- | ---------------------------------------- | ------------------------------ |
| `.OnSuccess()` | All main steps completed without failure | Success notifications, cleanup |
| `.OnFail()`    | The flow ended with a failure            | Alert notifications, rollback  |
| `.OnFinish()`  | Always, after success/fail handlers      | Final cleanup, audit logging   |

### How They Work

- The flow's final status (`completed` or `failed`), completion time, duration, and error message are saved to the database **before** any lifecycle event runs.
- Lifecycle event steps are **not** counted in `TotalNodes` or `CompletedNodes`. They do not affect the flow's duration or error message.
- If a lifecycle event step fails, it is logged but does **not** change the flow's final status.
- Lifecycle event nodes run with a fresh 5-minute timeout, independent of the main flow's `MaxExecutionTime`. This ensures cleanup and notification nodes can execute even if the main flow's timeout expired.

### Example

```csharp
var serviceId = await FlowBuilder
    .Create("provision_domain")
    .AddNode(new HttpRequestNode("AddDns") { /* ... */ })
    .AddNode(new WaitNode("PropagationDelay") {
        Delay = TimeSpan.FromSeconds(15),
        ProgressMessage = "Waiting for DNS propagation"
    })
    .OnSuccess(new HttpRequestNode("NotifySuccess") {
        Url = "https://api.example.com/webhook/success",
        Method = HttpMethod.Post,
        Body = "{\"status\": \"ok\"}"
    })
    .OnFail(new EmailSendNode("AlertAdmin") {
        ToEmail = "admin@example.com",
        Subject = "Domain provisioning failed",
        HtmlBodyResolver = ctx => $"<p>Error: {ctx.FlowError}</p>"
    })
    .OnFinish(new HttpRequestNode("AuditLog") {
        Url = "https://api.example.com/audit",
        Method = HttpMethod.Post,
        Body = "{\"event\": \"provision_finished\"}"
    })
    .StartAsync();
```

**Execution order:** Main steps → Status saved to database → OnSuccess _or_ OnFail → OnFinish

You can add multiple handlers for each event — they run in the order they were added:

```csharp
.OnSuccess(new HttpRequestNode("LogSuccess") { /* ... */ })
.OnSuccess(new HttpRequestNode("NotifyTeam") { /* ... */ })
```

### Checking the Outcome in OnFinish

Since `OnFinish` runs regardless of the outcome, you can check what happened using `FlowContext`:

```csharp
.OnFinish(new EmailSendNode("FinalReport") {
    ToEmail = "admin@example.com",
    Subject = "Flow Report",
    HtmlBodyResolver = ctx =>
        ctx.FlowStatus == FlowRunStatus.Completed
            ? $"<p>Flow <b>{ctx.FlowName}</b> completed successfully.</p>"
            : $"<p>Flow <b>{ctx.FlowName}</b> failed: {ctx.FlowError}</p>"
})
```

---

## How Execution Works

### When You Call StartAsync()

1. A unique ServiceId (UUID) is generated.
2. A FlowRun record is saved to the database with status "pending."
3. Background execution starts via `Task.Run`.
4. The ServiceId is returned immediately.

### Step-by-Step Execution

1. The FlowRun entity is loaded and cached to avoid redundant lookups.
2. The FlowRun status is updated to "running."
3. A timeout timer starts based on `MaxExecutionTime`.
4. Each step runs in order:
   - A NodeLog record is created (status = running).
   - If the step has a `ProgressMessage`, it is persisted to the run record.
   - The step's `ExecuteAsync` method runs.
   - The NodeLog is updated (status = completed or failed).
   - On success, the progress counter increments. On failure with **Continue** strategy, the counter also increments. On failure with **Stop** strategy, the flow aborts without incrementing.
   - If a step takes longer than its declared `ApproxExecutionTime`, the engine adjusts both `CompletedApproxMs` and `TotalApproxMs` so that progress never jumps backward.
   - If a step fails: **Stop** aborts the flow, **Continue** skips to the next step.
5. The flow's status and error (if any) are saved to the context.
6. The final status is committed to the database.
7. Lifecycle event handlers execute with a fresh timeout (OnSuccess or OnFail, then OnFinish).
