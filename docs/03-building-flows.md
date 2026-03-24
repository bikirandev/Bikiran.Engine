# Building Flows

Flows are built using the `FlowBuilder` class, which provides a step-by-step method chain for defining what your workflow does
and how it behaves.

---

## Builder Overview

```csharp
var serviceId = await FlowBuilder
    .Create("flow_name")                               // 1. Name the flow
    .Configure(cfg => { /* runtime settings */ })       // 2. Set timeout, failure handling, etc.
    .WithContext(ctx => { /* inject services */ })       // 3. Provide database, HTTP context, etc.
    .AddNode(new WaitNode("step_1") { DelayMs = 500 })  // 4. Add steps in order
    .AddNode(new HttpRequestNode("step_2") { /* ... */ })
    .StartAsync();                                      // 5. Start and get the run ID
```

---

## Builder Methods

| Method                     | Required           | Description                                                              |
| -------------------------- | ------------------ | ------------------------------------------------------------------------ |
| `FlowBuilder.Create(name)` | Yes                | Creates a new builder with the given flow name                           |
| `.Configure(action)`       | No                 | Sets runtime options like timeout and failure strategy                   |
| `.WithContext(action)`     | No                 | Injects services (database, HTTP context, logger) into the flow          |
| `.AddNode(node)`           | Yes (at least one) | Adds a step to the execution sequence                                    |
| `.OnSuccess(node)`         | No                 | Adds a node that runs only when all main nodes complete successfully     |
| `.OnFail(node)`            | No                 | Adds a node that runs only when the flow fails (error or timeout)        |
| `.OnFinish(node)`          | No                 | Adds a node that always runs after success/fail handlers complete        |
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

| Setting             | Default    | Description                                                   |
| ------------------- | ---------- | ------------------------------------------------------------- |
| `MaxExecutionTime`  | 10 minutes | Maximum time the flow can run before being cancelled          |
| `OnFailure`         | `Stop`     | What happens when a step fails (see failure strategies below) |
| `EnableNodeLogging` | `true`     | Whether to write per-step records to the database             |
| `TriggerSource`     | `""`       | A label showing where the flow was triggered from             |

### Failure Strategies

| Strategy     | Behavior                                                                |
| ------------ | ----------------------------------------------------------------------- |
| **Stop**     | Cancel the entire flow immediately when a step fails                                   |
| **Continue** | Skip the failed step and move on to the next one                                       |

For retry behavior, wrap individual nodes with `RetryNode` instead of using a global failure strategy.

---

## Injecting Context

Use `.WithContext()` to provide services that nodes can access during execution:

```csharp
.WithContext(ctx => {
    ctx.DbContext = _context;          // Your app's DbContext (for DatabaseQueryNode)
    ctx.HttpContext = HttpContext;      // For capturing caller metadata
    ctx.Logger = _logger;              // For structured logging
    ctx.Services = _serviceProvider;   // General-purpose DI access
})
```

When `HttpContext` is provided, the engine automatically captures caller metadata (IP address, user ID, request path, user agent) and saves it with the run record for later debugging.

---

## Shared Variables (FlowContext)

Nodes communicate by reading and writing to a shared in-memory dictionary. This is the primary way data passes from one step to the next.

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

The `FlowContext` also provides access to:

- `ServiceId` — the unique run identifier
- `FlowName` — the name of the current flow
- `DbContext` — the host application's database context
- `HttpContext` — the original HTTP request context
- `Services` — the DI service provider
- `Logger` — a structured logger
- `GetCredential<T>(name)` — retrieves a named credential registered at startup

---

## Lifecycle Events

Use lifecycle event methods to run nodes after the main flow completes. This is useful for logging, cleanup, alerting, or any post-flow action.

| Method         | Fires When                                       | Use Case                          |
| -------------- | ------------------------------------------------ | --------------------------------- |
| `.OnSuccess()` | All main nodes completed without failure          | Success logging, cleanup          |
| `.OnFail()`    | The flow ended with a failure (error or timeout)  | Alert notifications, rollback     |
| `.OnFinish()`  | Always, after success/fail handlers have run       | Final cleanup, audit logging      |

```csharp
var serviceId = await FlowBuilder
    .Create("provision_domain")
    .AddNode(new HttpRequestNode("add_dns") { /* ... */ })
    .AddNode(new WaitNode("propagation_delay") { DelayMs = 15000 })
    .OnSuccess(new HttpRequestNode("notify_success") {
        Url = "https://api.example.com/webhook/success",
        Method = HttpMethod.Post,
        Body = "{\"status\": \"ok\"}"
    })
    .OnFail(new EmailSendNode("alert_admin") {
        ToEmail = "admin@example.com",
        Subject = "Domain provisioning failed",
        HtmlBodyResolver = ctx => $"<p>Error: {ctx.FlowError}</p>"
    })
    .OnFinish(new HttpRequestNode("audit_log") {
        Url = "https://api.example.com/audit",
        Method = HttpMethod.Post,
        Body = "{\"event\": \"provision_finished\"}"
    })
    .StartAsync();
```

**Execution order:** Main nodes → OnSuccess _or_ OnFail → OnFinish

You can chain multiple handlers for each event — they run in the order they were added:

```csharp
.OnSuccess(new LogNode("log_success") { /* ... */ })
.OnSuccess(new WebhookNode("notify_team") { /* ... */ })
```

### Accessing Flow Outcome (OnFinish)

Since `OnFinish` runs regardless of outcome, you'll often need to check whether the flow succeeded or failed. Every lifecycle event node can read the outcome through `FlowContext`:

| Property       | Type      | Description                                      |
| -------------- | --------- | ------------------------------------------------ |
| `FlowStatus`  | `string?` | `"completed"` or `"failed"` after main nodes run |
| `FlowError`   | `string?` | Error message if failed; `null` on success        |

**Example — OnFinish node that branches on status:**

```csharp
public class FlowAuditNode : IFlowNode
{
    public string Name { get; }
    public string NodeType => "FlowAudit";

    public FlowAuditNode(string name) => Name = name;

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        var isSuccess = context.FlowStatus == "completed";
        var error = context.FlowError; // null when succeeded

        if (isSuccess)
        {
            // Flow succeeded — log or notify
            context.Set("audit_message", $"Flow '{context.FlowName}' completed successfully.");
        }
        else
        {
            // Flow failed — include error details
            context.Set("audit_message", $"Flow '{context.FlowName}' failed: {error}");
        }

        return NodeResult.Ok();
    }
}
```

**Usage:**

```csharp
var serviceId = await FlowBuilder
    .Create("process_order")
    .AddNode(new HttpRequestNode("charge_payment") { /* ... */ })
    .AddNode(new EmailSendNode("send_receipt") { /* ... */ })
    .OnFinish(new FlowAuditNode("audit_result"))
    .StartAsync();
```

You can also use built-in nodes with `HtmlBodyResolver` or `Transform` lambdas that check status inline:

```csharp
.OnFinish(new EmailSendNode("notify_admin") {
    ToEmail = "admin@example.com",
    Subject = "Flow finished",
    HtmlBodyResolver = ctx =>
        ctx.FlowStatus == "completed"
            ? $"<p>Flow <b>{ctx.FlowName}</b> completed successfully.</p>"
            : $"<p>Flow <b>{ctx.FlowName}</b> failed: {ctx.FlowError}</p>"
})
```

> **Note:** Lifecycle event node failures are logged in the database but do **not** change the flow's final status. If the main nodes succeeded, the flow status remains `"completed"` even if an OnSuccess or OnFinish handler fails.

---

## Execution Model

### What Happens When You Call StartAsync()

```
StartAsync()
  ├── Generate a unique ServiceId (UUID)
  ├── Save a FlowRun record to the database (status = pending)
  ├── Start background execution via Task.Run
  └── Return the ServiceId immediately
```

### How Steps Run

```
Background Execution
  ├── Update FlowRun status → "running"
  ├── Start a timeout timer (based on MaxExecutionTime)
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
  │
  ├── If all nodes succeeded and OnSuccess handlers exist:
  │     └── Run OnSuccess nodes sequentially (failures logged only)
  ├── If the flow failed and OnFail handlers exist:
  │     └── Run OnFail nodes sequentially (failures logged only)
  ├── If OnFinish handlers exist:
  │     └── Run OnFinish nodes sequentially (failures logged only)
  │
  ├── All nodes done → FlowRun status = "completed"
  └── On unhandled error → FlowRun status = "failed"
```

### Timeout Protection

A cancellation timer is set based on `MaxExecutionTime` (default: 10 minutes). If the timer expires, the currently running node receives a cancellation signal, and the flow is marked as `failed` with the message `"max_execution_time_exceeded"`.

---

## Progress Tracking

After each step completes, the engine updates `FlowRun.CompletedNodes`. You can calculate progress as:

```
Progress = (CompletedNodes / TotalNodes) × 100
```

This is available in real time through the admin API's progress endpoint.

---

## Run Statuses

### Flow Run Statuses

| Status      | Meaning                            |
| ----------- | ---------------------------------- |
| `Pending`   | Created but not yet started        |
| `Running`   | Currently executing steps          |
| `Completed` | All steps finished successfully    |
| `Failed`    | Stopped due to an error or timeout |
| `Cancelled` | Cancelled externally               |

### Node Log Statuses

| Status      | Meaning                                           |
| ----------- | ------------------------------------------------- |
| `Pending`   | Queued but not yet executed                       |
| `Running`   | Currently executing                               |
| `Completed` | Finished successfully                             |
| `Failed`    | Finished with an error                            |
| `Skipped`   | Skipped (e.g., when failure strategy is Continue) |

---

## Context Metadata

When `HttpContext` is provided, the engine automatically saves a snapshot of the caller's information:

```json
{
  "IpAddress": "192.168.1.1",
  "UserId": 12345,
  "RequestPath": "/api/v3/orders",
  "UserAgent": "Mozilla/5.0 ...",
  "Timestamp": 1742000000
}
```

This is stored in the `FlowRun.ContextMeta` field and is visible through the admin API.

---

## Complete Example

```csharp
var serviceId = await FlowBuilder
    .Create("order_notification_flow")
    .Configure(cfg => {
        cfg.MaxExecutionTime = TimeSpan.FromMinutes(5);
        cfg.OnFailure = OnFailureAction.Stop;
        cfg.TriggerSource = nameof(OrdersV3Controller);
    })
    .WithContext(ctx => {
        ctx.DbContext = _context;
        ctx.HttpContext = HttpContext;
        ctx.Logger = _logger;
    })
    .AddNode(new HttpRequestNode("fetch_order") {
        Url = "https://api.example.com/order/1",
        Method = HttpMethod.Get,
        MaxRetries = 3,
        OutputKey = "order_data"
    })
    .AddNode(new IfElseNode("check_order_status") {
        Condition = ctx => ctx.Get<string>("order_data") != null,
        TrueBranch = [
            new EmailSendNode("notify_customer") {
                ToEmail = "user@example.com",
                Subject = "Order Ready",
                Template = "ORDER_NOTIFY"
            }
        ],
        FalseBranch = [
            new WaitNode("wait_before_retry") { DelayMs = 3000 }
        ]
    })
    .AddNode(new WaitNode("final_wait") { DelayMs = 500 })
    .StartAsync();
```

This flow fetches order data via HTTP, checks whether the data was received, sends an email if it was (or waits if not), then pauses briefly before finishing. The entire flow runs in the background, and the `serviceId` is returned immediately.
