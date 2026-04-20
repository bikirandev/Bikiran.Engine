# Node Reference

Nodes are the building blocks of every flow. Each node performs one task — making an HTTP call, sending an email, pausing, branching, or querying a database. This document covers all 11 built-in node types.

---

## Node Type Classification

The engine internally classifies each node by type for logging and diagnostics. This is handled automatically — you never need to set a node type yourself.

Built-in nodes are classified as their specific type (e.g., `Wait`, `HttpRequest`). Custom nodes are automatically classified as `Custom`. The classification appears in the `NodeType` column of the `FlowNodeLog` database table.

| Type           | Description                            |
| -------------- | -------------------------------------- |
| Starting       | Marks the start of a flow              |
| Ending         | Marks the end of a flow                |
| Wait           | Pauses flow execution                  |
| HttpRequest    | Makes an outbound HTTP request         |
| EmailSend      | Sends an email via SMTP                |
| IfElse         | Evaluates a condition and branches     |
| WhileLoop      | Repeats steps while a condition holds  |
| DatabaseQuery  | Runs an EF Core query                  |
| Transform      | Reshapes or derives context data       |
| Retry          | Wraps a node with retry logic          |
| Parallel       | Runs branches concurrently             |
| Custom         | Any user-defined node                  |

---

## Naming Rules

All node names **must** follow PascalCase convention:

- Start with an uppercase letter
- No spaces or special characters
- Only letters and digits

Examples: `FetchOrder`, `SendEmail`, `CheckDnsStatus`

Invalid names throw an `ArgumentException` at construction time:

```csharp
// Valid
new WaitNode("PauseBeforeRetry");

// Invalid — throws ArgumentException
new WaitNode("pause_before_retry");
new WaitNode("Pause Before Retry");
```

---

## Progress Messages

Every node has an optional `ProgressMessage` property. When set, this message is persisted to the `FlowRun.CurrentProgressMessage` column while the node is executing. This is useful for long-running background flows where you want to show users what step is currently happening.

```csharp
new WaitNode("DnsPropagation") {
    Delay = TimeSpan.FromSeconds(30),
    ProgressMessage = "Waiting for DNS propagation"
}
```

The progress message is available through the `/runs/{serviceId}/progress` and `/runs/{serviceId}` API endpoints as `currentProgressMessage`.

When the node completes and the flow moves to the next node, the progress message is updated to the next node's message (or cleared if none is set).

---

## Approx Execution Time

Every node has an `ApproxExecutionTime` property (`TimeSpan`, default `00:00:01`). It declares how long you expect the node to take. The engine uses these values as **weights** to calculate a time-weighted progress percentage that is more accurate than a simple step count.

```csharp
new HttpRequestNode("FetchOrder") {
    Url = "https://api.example.com/order/123",
    ApproxExecutionTime = TimeSpan.FromSeconds(3)  // expected ~3 s for this call
}
```

### How progress is calculated

| Mode | Formula | Updated |
|------|---------|--------|
| **Weighted** | `CompletedApproxMs / TotalApproxMs * 100` | After each node finishes |
| **Live** | `(CompletedApproxMs + min(elapsed, CurrentNodeApproxMs)) / TotalApproxMs * 100` | Every time you call the progress endpoint |

Example — three nodes: `A = 2 s`, `B = 5 s`, `C = 3 s` (total 10 s).

- After A completes: weighted = **20 %**, live ≈ **20 %**
- 2 s into B: live = `(2000 + min(2000, 5000)) / 10000 * 100` = **40 %**
- After B completes: weighted = **70 %**

When `TotalApproxMs` is zero (all defaults left untouched), the engine falls back to a uniform step-count percentage.

---

## OutputKey Auto-Generation

Nodes that produce output (HttpRequestNode, DatabaseQueryNode, TransformNode) auto-generate their `OutputKey` as `"{Name}_Result"`. You do not need to set it manually unless you want a custom key.

```csharp
// OutputKey will be "FetchOrder_Result" automatically
new HttpRequestNode("FetchOrder") {
    Url = "https://api.example.com/order/123"
}
```

---

## At a Glance

| Node                                      | NodeType Enum   | What It Does                                |
| ----------------------------------------- | --------------- | ------------------------------------------- |
| [StartingNode](#startingnode)             | `Starting`      | Mark the start of a flow with an optional pause |
| [EndingNode](#endingnode)                 | `Ending`        | Mark the successful end of a flow           |
| [WaitNode](#waitnode)                     | `Wait`          | Pause execution for a set duration          |
| [HttpRequestNode](#httprequestnode)       | `HttpRequest`   | Make an outbound HTTP call                  |
| [EmailSendNode](#emailsendnode)           | `EmailSend`     | Send an email via SMTP                      |
| [IfElseNode](#ifelsenode)                 | `IfElse`        | Take different paths based on a condition   |
| [WhileLoopNode](#whileloopnode)           | `WhileLoop`     | Repeat steps while a condition is true      |
| [DatabaseQueryNode](#databasequerynode)   | `DatabaseQuery` | Run a database query and store the result   |
| [TransformNode](#transformnode)           | `Transform`     | Reshape or derive values from existing data |
| [RetryNode](#retrynode)                   | `Retry`         | Wrap any node with retry logic              |
| [ParallelNode](#parallelnode)             | `Parallel`      | Run multiple branches at the same time      |

---

## StartingNode

A lightweight marker node that signals the beginning of a flow. Optionally pauses for a brief moment before handing off to the next node.

**Properties:**

| Property          | Type     | Default          | Description                             |
| ----------------- | -------- | ---------------- | --------------------------------------- |
| `Name`            | string   | `"StartingNode"` | Step name (PascalCase)                  |
| `ProgressMessage` | string?  | null             | Progress message shown during execution |
| `WaitTime`        | TimeSpan | `00:00:01`       | Pause duration before proceeding        |

**Direct usage:**

```csharp
new StartingNode
{
    ProgressMessage = $"Upgrading from {previousVersion} to {newVersion}",
    WaitTime = TimeSpan.FromSeconds(1)
}
```

**Builder shorthand:**

```csharp
FlowBuilder.Create("UpgradeFlow")
    .StartingNode($"Upgrading from {previousVersion} to {newVersion}", TimeSpan.FromSeconds(1))
    // ... other nodes ...
    .EndingNode("Upgrade complete.")
```

---

## EndingNode

A lightweight marker node that signals the successful completion of a flow. Returns immediately with a completion message.

**Properties:**

| Property          | Type    | Default         | Description                             |
| ----------------- | ------- | --------------- | --------------------------------------- |
| `Name`            | string  | `"EndingNode"`  | Step name (PascalCase)                  |
| `ProgressMessage` | string? | null            | Progress message shown during execution |

**Direct usage:**

```csharp
new EndingNode { ProgressMessage = "All done." }
```

**Builder shorthand:**

```csharp
.EndingNode("All done.")
```

---

## WaitNode

Pauses the flow for a specified number of milliseconds.

**Properties:**

| Property         | Type   | Default  | Description          |
| ---------------- | ------ | -------- | -------------------- |
| `Name`           | string | required | Step name (PascalCase) |
| `ProgressMessage`| string?| null     | Progress message shown during execution |
| `Delay`          | TimeSpan | `1s`     | Duration to wait     |

**Example:**

```csharp
new WaitNode("PauseBeforeRetry") { Delay = TimeSpan.FromSeconds(3) }
```

---

## HttpRequestNode

Makes an outbound HTTP request with built-in retry support.

**Properties:**

| Property            | Type                         | Default             | Description                                     |
| ------------------- | ---------------------------- | ------------------- | ----------------------------------------------- |
| `Name`              | string                       | required            | Step name (PascalCase)                          |
| `ProgressMessage`   | string?                      | null                | Progress message shown during execution         |
| `Url`               | string                       | required            | Target URL                                      |
| `Method`            | HttpMethod                   | `GET`               | HTTP method                                     |
| `Headers`           | Dictionary\<string, string\> | empty               | Extra request headers                           |
| `Body`              | string?                      | null                | Request body (JSON string)                      |
| `TimeoutSeconds`    | int                          | `30`                | Timeout per attempt                             |
| `MaxRetries`        | int                          | `3`                 | Number of retry attempts                        |
| `RetryDelaySeconds` | int                          | `2`                 | Seconds between retries                         |
| `OutputKey`         | string                       | `"{Name}_Result"`  | Context key where the response is stored        |
| `ExpectStatusCode`  | int?                         | null                | If set, non-matching status codes cause failure |
| `ExpectValue`       | string?                      | null                | JSON validation expression on the response      |

**How it works:**

1. Builds and sends the HTTP request with configured headers and body.
2. Retries up to `MaxRetries` times on failure.
3. Stores the raw response body in the shared context under `OutputKey`.
4. Optionally validates the status code and response content.

**Example:**

```csharp
new HttpRequestNode("FetchOrder") {
    Url = "https://api.example.com/order/123",
    Method = HttpMethod.Get,
    MaxRetries = 3
}
```

### Validating the Response

The `ExpectValue` property lets you validate the JSON response using a simple expression. The parsed JSON is available as `$val`.

Supported operators: `>=`, `<=`, `>`, `<`, `==`, `!=`, `&&`, `||`

```csharp
// Check that the response amount is at least 100
new HttpRequestNode("VerifyPayment") {
    Url = "https://api.example.com/payment/123",
    ExpectValue = "$val.data.amount >= 100 && $val.data.name == \"Bikiran\""
}

// Simple status check
new HttpRequestNode("CheckStatus") {
    Url = "https://api.example.com/health",
    ExpectValue = "$val.status == \"ok\""
}
```

When validation fails, the node returns an error describing which condition was not met.

---

## EmailSendNode

Sends an email using a registered SMTP credential or the application's email service.

**Properties:**

| Property              | Type                                               | Default  | Description                                   |
| --------------------- | -------------------------------------------------- | -------- | --------------------------------------------- |
| `Name`                | string                                             | required | Step name (PascalCase)                        |
| `ProgressMessage`     | string?                                            | null     | Progress message shown during execution       |
| `ToEmail`             | string                                             | required | Recipient email address                       |
| `ToName`              | string                                             | `""`     | Recipient display name                        |
| `Subject`             | string                                             | required | Email subject                                 |
| `CredentialName`      | string?                                            | null     | Named SMTP credential (registered at startup) |
| `Template`            | string?                                            | null     | Template key (e.g., `"ORDER_CREATE"`)         |
| `HtmlBody`            | string?                                            | null     | Raw HTML body                                 |
| `TextBody`            | string?                                            | null     | Plain text body                               |
| `HtmlBodyResolver`    | Func\<FlowContext, string\>?                       | null     | Build HTML body dynamically from context      |
| `TextBodyResolver`    | Func\<FlowContext, string\>?                       | null     | Build text body dynamically from context      |
| `Placeholders`        | Dictionary\<string, string\>                       | empty    | Template placeholders                         |
| `PlaceholderResolver` | Func\<FlowContext, Dictionary\<string, string\>\>? | null     | Build placeholders dynamically                |

### How the Body Is Chosen

The node uses the first available body option in this order:

1. **Template** — renders the named template with placeholders
2. **HtmlBody** or **HtmlBodyResolver** — sends raw HTML
3. **TextBody** or **TextBodyResolver** — sends plain text

At least one body source must be provided.

### Examples

```csharp
// Using a template
new EmailSendNode("NotifyCustomer") {
    ToEmail = "user@example.com",
    ToName = "John",
    Subject = "Order Confirmed",
    Template = "ORDER_CREATE",
    Placeholders = new() { { "OrderId", "1234" } }
}

// Using raw HTML with a credential
new EmailSendNode("SendInvoice") {
    CredentialName = "smtp_primary",
    ToEmail = "user@example.com",
    Subject = "Your Invoice",
    HtmlBody = "<h1>Invoice #1234</h1><p>Amount: $150.00</p>"
}

// Using dynamic HTML built from context
new EmailSendNode("SendReport") {
    ToEmail = "admin@example.com",
    Subject = "Daily Report",
    HtmlBodyResolver = ctx =>
        $"<h1>Report</h1><p>{ctx.Get<string>("report_summary")}</p>"
}
```

---

## IfElseNode

Evaluates a condition and runs one of two branches.

**Properties:**

| Property      | Type                      | Description                            |
| ------------- | ------------------------- | -------------------------------------- |
| `Name`        | string                    | Step name (PascalCase)                 |
| `ProgressMessage` | string?               | Progress message shown during execution |
| `Condition`   | Func\<FlowContext, bool\> | The condition to evaluate              |
| `TrueBranch`  | List\<IFlowNode\>         | Steps to run if the condition is true  |
| `FalseBranch` | List\<IFlowNode\>         | Steps to run if the condition is false |

**How it works:**

1. Evaluates the condition using the current flow context.
2. Runs the matching branch steps in order.
3. Each branch step gets its own log entry in the database.
4. Records which branch was taken (`"true"` or `"false"`).

**Example:**

```csharp
new IfElseNode("CheckStatus") {
    Condition = ctx => ctx.Get<string>("order_data") != null,
    TrueBranch = [
        new EmailSendNode("SendConfirmation") { /* ... */ }
    ],
    FalseBranch = [
        new WaitNode("WaitAndRetry") { Delay = TimeSpan.FromSeconds(5) }
    ]
}
```

---

## WhileLoopNode

Repeats a set of steps while a condition remains true.

**Properties:**

| Property           | Type                      | Default  | Description                             |
| ------------------ | ------------------------- | -------- | --------------------------------------- |
| `Name`             | string                    | required | Step name (PascalCase)                  |
| `ProgressMessage`  | string?                   | null     | Progress message shown during execution |
| `Condition`        | Func\<FlowContext, bool\> | required | Continue while this returns true        |
| `Body`             | List\<IFlowNode\>         | required | Steps to run each iteration             |
| `MaxIterations`    | int                       | `10`     | Hard cap to prevent infinite loops      |
| `IterationDelayMs` | int                       | `0`      | Milliseconds to wait between iterations |

**How it works:**

1. Checks the condition before each iteration.
2. Runs all body steps if the condition is true and the iteration count is within the limit.
3. Saves the current iteration count in context as `"{Name}_iteration_count"`.
4. Returns a failure if `MaxIterations` is reached.

**Example:**

```csharp
new WhileLoopNode("PollStatus") {
    Condition = ctx => ctx.Get<string>("status") != "ready",
    Body = [
        new HttpRequestNode("CheckStatus") {
            Url = "https://api.example.com/status"
        },
        new WaitNode("PollDelay") { Delay = TimeSpan.FromSeconds(5) }
    ],
    MaxIterations = 12
}
```

---

## DatabaseQueryNode

Runs an EF Core query against your application's database and stores the result in context. This node is generic over your database context type for type-safe queries.

**Properties:**

| Property           | Type                                                 | Default                 | Description                              |
| ------------------ | ---------------------------------------------------- | ----------------------- | ---------------------------------------- |
| `Name`             | string                                               | required                | Step name (PascalCase)                   |
| `ProgressMessage`  | string?                                              | null                    | Progress message shown during execution  |
| `Query`            | Func\<TContext, CancellationToken, Task\<object?\>\> | required                | The EF Core query to run                 |
| `OutputKey`        | string                                               | `"{Name}_Result"`       | Context key for the result               |
| `FailIfNull`       | bool                                                 | `false`                 | Return failure if the query returns null |
| `NullErrorMessage` | string                                               | `"Query returned null"` | Error message when failing on null       |

**How it resolves the database context:** The node first checks `FlowContext.DbContext`. If that is not set or is not the right type, it falls back to resolving the context from the flow-scoped DI container via `GetDbContext<TContext>()`. For background flows, the DI fallback is recommended.

> **Important:** Always use EF Core LINQ or parameterized queries. Never use raw SQL string interpolation.

**Example:**

```csharp
new DatabaseQueryNode<AppDbContext>("FetchSubscription") {
    Query = async (db, ct) => await db.Subscription
        .Where(s => s.Id == subscriptionId && s.TimeDeleted == 0)
        .FirstOrDefaultAsync(ct),
    FailIfNull = true,
    NullErrorMessage = "Subscription not found"
}
```

---

## TransformNode

Reshapes or derives new values from existing context data. Does not make any external calls.

**Properties:**

| Property           | Type                                                     | Default           | Description                                |
| ------------------ | -------------------------------------------------------- | ----------------- | ------------------------------------------ |
| `Name`             | string                                                   | required          | Step name (PascalCase)                     |
| `ProgressMessage`  | string?                                                  | null              | Progress message shown during execution    |
| `Transform`        | Func\<FlowContext, object?\>?                            | null              | Synchronous transform function             |
| `TransformAsync`   | Func\<FlowContext, CancellationToken, Task\<object?\>\>? | null              | Asynchronous transform function            |
| `OutputKey`        | string                                                   | `"{Name}_Result"` | Context key for the result                 |
| `SkipIfNullOutput` | bool                                                     | `true`            | Skip storing the key if the result is null |

Provide either `Transform` or `TransformAsync` (not both). If both are set, `TransformAsync` takes priority.

**Example:**

```csharp
new TransformNode("BuildMessage") {
    Transform = ctx => {
        var sub = ctx.Get<Subscription>("subscription");
        return $"Your subscription #{sub?.Id} expires on {sub?.ExpiryDate}.";
    }
}
```

---

## RetryNode

Wraps any node and retries it on failure with configurable delay and backoff.

**Properties:**

| Property            | Type                      | Default  | Description                                                    |
| ------------------- | ------------------------- | -------- | -------------------------------------------------------------- |
| `Name`              | string                    | required | Step name (PascalCase)                                         |
| `ProgressMessage`   | string?                   | null     | Progress message shown during execution                        |
| `Inner`             | IFlowNode                 | required | The node to wrap                                               |
| `MaxAttempts`       | int                       | `3`      | Total attempts (1 means no retry)                              |
| `DelayMs`           | int                       | `2000`   | Base delay between attempts in milliseconds                    |
| `RetryOn`           | Func\<NodeResult, bool\>? | null     | Custom retry condition; retries on any failure if null         |
| `BackoffMultiplier` | double                    | `1.0`    | Multiply delay on each retry (use 2.0 for exponential backoff) |

**Example:**

```csharp
new RetryNode("RetryPayment") {
    Inner = new HttpRequestNode("VerifyPayment") {
        Url = "https://api.sslcommerz.com/validator/..."
    },
    MaxAttempts = 4,
    DelayMs = 3000,
    BackoffMultiplier = 2.0  // delays: 3s → 6s → 12s
}
```

---

## ParallelNode

Runs multiple branches at the same time using `Task.WhenAll`.

**Properties:**

| Property               | Type                      | Default  | Description                             |
| ---------------------- | ------------------------- | -------- | --------------------------------------- |
| `Name`                 | string                    | required | Step name (PascalCase)                  |
| `ProgressMessage`      | string?                   | null     | Progress message shown during execution |
| `Branches`             | List\<List\<IFlowNode\>\> | required | Each inner list is one parallel branch  |
| `WaitAll`              | bool                      | `true`   | Wait for all branches before continuing |
| `AbortOnBranchFailure` | bool                      | `false`  | Cancel remaining branches if one fails  |

> **Thread safety:** The shared context dictionary is not thread-safe. Each parallel branch should write to its own unique key. Do not read a key that another branch is writing to at the same time.

**Example:**

```csharp
new ParallelNode("MultiChannelNotify") {
    Branches = [
        [new EmailSendNode("Email") { /* ... */ }],
        [new HttpRequestNode("Webhook") { /* ... */ }]
    ],
    AbortOnBranchFailure = false
}
```
