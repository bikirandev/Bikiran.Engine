# Flow Builder API and Execution Engine

This document covers how to build and run flows using the fluent builder API, and how the execution engine processes them.

---

## Building a Flow

Use `FlowBuilder` to compose a flow step by step:

```csharp
var serviceId = await FlowBuilder
    .Create("my_flow_name")                         // 1. Set the flow name
    .Configure(cfg => { /* optional settings */ })   // 2. Configure runtime behavior
    .WithContext(ctx => { /* inject services */ })    // 3. Provide context and services
    .AddNode(new WaitNode("step_1") { DelayMs = 500 })  // 4. Add nodes in order
    .AddNode(new HttpRequestNode("step_2") { /* ... */ })
    .StartAsync();                                   // 5. Start and get the ServiceId
```

---

## Builder Methods

| Method                     | Required           | Description                                                                  |
| -------------------------- | ------------------ | ---------------------------------------------------------------------------- |
| `FlowBuilder.Create(name)` | Yes                | Creates a new builder with the given flow name                               |
| `.Configure(action)`       | No                 | Configures runtime options (timeout, failure strategy, etc.)                 |
| `.WithContext(action)`     | No                 | Injects services into the FlowContext (database, email sender, etc.)         |
| `.AddNode(node)`           | Yes (at least one) | Appends a node to the execution sequence                                     |
| `.StartAsync()`            | Yes                | Persists the FlowRun record, fires background execution, returns `ServiceId` |
| `.StartAndWaitAsync()`     | —                  | Same as `StartAsync()` but waits for the flow to complete before returning   |

---

## Configuration Options

Configure runtime behavior via `.Configure()`:

```csharp
.Configure(cfg => {
    cfg.MaxExecutionTime = TimeSpan.FromMinutes(5);   // Default: 10 minutes
    cfg.OnFailure = OnFailureAction.Stop;              // Default: Stop
    cfg.EnableNodeLogging = true;                      // Default: true
    cfg.TriggerSource = "OrdersV3Controller";          // Where this flow was triggered
})
```

| Setting             | Default    | Description                                                            |
| ------------------- | ---------- | ---------------------------------------------------------------------- |
| `MaxExecutionTime`  | 10 minutes | Maximum wall-clock time the entire flow may run before being cancelled |
| `OnFailure`         | `Stop`     | What happens when a node fails: `Stop`, `Continue`, or `Retry`         |
| `EnableNodeLogging` | `true`     | Whether to write per-node execution records to the database            |
| `TriggerSource`     | `""`       | Label for tracing where the flow was triggered (e.g., controller name) |

---

## Injecting Context

Provide services and shared state via `.WithContext()`:

```csharp
.WithContext(ctx => {
    ctx.DbContext = _context;          // For DatabaseQueryNode
    ctx.HttpContext = HttpContext;      // For capturing caller metadata
    ctx.EmailSender = _emailSender;    // For EmailSendNode
    ctx.Logger = _logger;              // For structured logging
    ctx.Services = _serviceProvider;   // General-purpose DI access
})
```

---

## Execution Flow

Here is what happens internally when `StartAsync()` is called:

```
StartAsync()
  │
  ├─ Generate a UUID ServiceId
  ├─ Save a FlowRun record (status = pending, TotalNodes = N)
  ├─ Fire Task.Run(ExecuteFlowAsync)   ← background, non-blocking
  └─ Return ServiceId
```

```
ExecuteFlowAsync()
  │
  ├─ Update FlowRun → status = running, StartedAt = now
  ├─ Create a CancellationTokenSource with MaxExecutionTime
  │
  ├─ For each node (in order):
  │    ├─ Save FlowNodeLog (status = running, sequence = i)
  │    ├─ Call node.ExecuteAsync(context, cancellationToken)
  │    ├─ Update FlowNodeLog (status = completed/failed, output, duration)
  │    ├─ Increment FlowRun.CompletedNodes
  │    │
  │    └─ If the node FAILED:
  │         ├─ OnFailure = Stop     → throw FlowAbortException (exit loop)
  │         ├─ OnFailure = Continue → log warning, move to next node
  │         └─ OnFailure = Retry    → handled by the node internally
  │
  ├─ Update FlowRun → status = completed, CompletedAt, DurationMs
  │
  └─ On any unhandled exception:
       └─ Update FlowRun → status = failed, ErrorMessage
```

---

## Progress Tracking

After each node completes, the engine updates `FlowRun.CompletedNodes`. Progress can be calculated as:

```
ProgressPercent = (CompletedNodes / TotalNodes) × 100
```

This is available in real time through the admin API endpoint.

---

## Context Metadata Snapshot

If `HttpContext` is provided, the engine automatically captures caller metadata and saves it as JSON in `FlowRun.ContextMeta`:

```json
{
  "IpAddress": "192.168.1.1",
  "UserId": 12345,
  "RequestPath": "/api/v3/orders",
  "UserAgent": "Mozilla/5.0 ...",
  "Timestamp": 1742000000
}
```

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
        ctx.EmailSender = _emailSender;
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

This flow:

1. Fetches order data via HTTP.
2. Checks if the order data was received.
3. If yes — sends a notification email.
4. If no — waits 3 seconds.
5. Waits 500ms before finishing.

The entire flow runs in the background. The `serviceId` is returned immediately.
