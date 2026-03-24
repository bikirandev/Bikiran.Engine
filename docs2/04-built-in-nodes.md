# Built-in Nodes

Bikiran.Engine includes 9 ready-to-use node types. Each one handles a common workflow task.

---

## Summary

| Node                   | Purpose                                    |
| ---------------------- | ------------------------------------------ |
| `WaitNode`             | Pause execution for a set duration         |
| `HttpRequestNode`      | Make an outbound HTTP call with retries    |
| `EmailSendNode`        | Send an email via SMTP                     |
| `IfElseNode`           | Branch on a condition                      |
| `WhileLoopNode`        | Repeat steps while a condition is true     |
| `DatabaseQueryNode<T>` | Run an EF Core query                       |
| `TransformNode`        | Reshape or derive values from context      |
| `RetryNode`            | Wrap any node with retry and backoff logic |
| `ParallelNode`         | Run multiple branches at the same time     |

---

## WaitNode

Pauses the flow for a specified duration.

**Properties:**

| Property  | Type   | Default | Description                    |
| --------- | ------ | ------- | ------------------------------ |
| `Name`    | string | —       | Node name (required)           |
| `DelayMs` | int    | 1000    | Pause duration in milliseconds |

**Example:**

```csharp
new WaitNode("pause_before_email") { DelayMs = 5000 }
```

---

## HttpRequestNode

Makes an outbound HTTP request with optional retries, status validation, and response body validation.

**Properties:**

| Property            | Type       | Default             | Description                                              |
| ------------------- | ---------- | ------------------- | -------------------------------------------------------- |
| `Name`              | string     | —                   | Node name (required)                                     |
| `Url`               | string     | —                   | Target URL (required)                                    |
| `Method`            | HttpMethod | GET                 | HTTP method                                              |
| `Headers`           | Dictionary | empty               | Additional request headers                               |
| `Body`              | string?    | null                | JSON request body (for POST/PUT/PATCH)                   |
| `TimeoutSeconds`    | int        | 30                  | Per-attempt timeout                                      |
| `MaxRetries`        | int        | 3                   | Maximum retry attempts on transient failures             |
| `RetryDelaySeconds` | int        | 2                   | Seconds between retries                                  |
| `OutputKey`         | string?    | `"{Name}_response"` | Context key where the response body is stored            |
| `ExpectStatusCode`  | int?       | null                | Required HTTP status code or the node fails              |
| `ExpectValue`       | string?    | null                | JSON validation expression (e.g., `$val.status == "ok"`) |

**Example:**

```csharp
new HttpRequestNode("call_payment_api")
{
    Url = "https://api.payment.com/verify",
    Method = HttpMethod.Post,
    Headers = new() { { "Authorization", "Bearer sk_live_..." } },
    Body = "{\"transactionId\": \"TXN123\"}",
    MaxRetries = 3,
    RetryDelaySeconds = 2,
    ExpectStatusCode = 200,
    ExpectValue = "$val.status == \"valid\"",
    OutputKey = "payment_result"
}
```

**Retry behavior:** Retries on `TaskCanceledException` and `HttpRequestException` (transient network errors). Does not retry on validation failures.

**ExpectValue expressions:** Use `$val.field` to reference JSON response fields. Supports dot notation for nested values. Operators: `==`, `!=`, `>`, `<`, `>=`, `<=`, `&&`, `||`.

---

## EmailSendNode

Sends an email via SMTP using a named credential registered at startup.

**Properties:**

| Property              | Type       | Default | Description                                        |
| --------------------- | ---------- | ------- | -------------------------------------------------- |
| `Name`                | string     | —       | Node name (required)                               |
| `ToEmail`             | string     | —       | Recipient email address (required)                 |
| `ToName`              | string     | `""`    | Recipient display name                             |
| `Subject`             | string     | —       | Email subject (required)                           |
| `CredentialName`      | string?    | null    | Name of the registered SmtpCredential              |
| `Template`            | string?    | null    | Template key (generates an HTML template)          |
| `HtmlBody`            | string?    | null    | Raw HTML body                                      |
| `TextBody`            | string?    | null    | Plain text body                                    |
| `HtmlBodyResolver`    | Func?      | null    | Function that builds HTML from FlowContext         |
| `TextBodyResolver`    | Func?      | null    | Function that builds text from FlowContext         |
| `Placeholders`        | Dictionary | empty   | Template placeholder values                        |
| `PlaceholderResolver` | Func?      | null    | Function that builds placeholders from FlowContext |

**Body resolution priority:** HtmlBodyResolver → HtmlBody → TextBodyResolver → TextBody → Template

**Example with static body:**

```csharp
new EmailSendNode("send_welcome")
{
    CredentialName = "smtp_primary",
    ToEmail = "user@example.com",
    ToName = "Jane Doe",
    Subject = "Welcome!",
    HtmlBody = "<h1>Welcome!</h1><p>Your account is ready.</p>"
}
```

**Example with dynamic body:**

```csharp
new EmailSendNode("send_receipt")
{
    CredentialName = "smtp_primary",
    ToEmail = "user@example.com",
    Subject = "Your Receipt",
    HtmlBodyResolver = ctx =>
    {
        var total = ctx.Get<double>("order_total");
        return $"<p>Your order total is <b>${total}</b>.</p>";
    }
}
```

**Example with template and placeholders:**

```csharp
new EmailSendNode("send_notification")
{
    CredentialName = "smtp_primary",
    ToEmail = "user@example.com",
    Subject = "Password Changed",
    Template = "AUTH_PASSWORD_CHANGED",
    Placeholders = new()
    {
        { "UserName", "Jane Doe" },
        { "Date", DateTime.UtcNow.ToString("yyyy-MM-dd") }
    }
}
```

---

## IfElseNode

Splits the flow into two paths based on a condition. Only the matching branch runs.

**Properties:**

| Property      | Type                    | Default | Description                          |
| ------------- | ----------------------- | ------- | ------------------------------------ |
| `Name`        | string                  | —       | Node name (required)                 |
| `Condition`   | Func<FlowContext, bool> | —       | Condition function (required)        |
| `TrueBranch`  | List<IFlowNode>         | empty   | Nodes to run when condition is true  |
| `FalseBranch` | List<IFlowNode>         | empty   | Nodes to run when condition is false |

The chosen branch is recorded in the context as `"{Name}_branch_taken"` with value `"true"` or `"false"`.

**Example:**

```csharp
new IfElseNode("check_payment_status")
{
    Condition = ctx =>
    {
        var status = ctx.Get<string>("payment_status");
        return status == "paid";
    },
    TrueBranch =
    [
        new EmailSendNode("send_receipt") { /* ... */ }
    ],
    FalseBranch =
    [
        new EmailSendNode("send_payment_reminder") { /* ... */ }
    ]
}
```

---

## WhileLoopNode

Repeats a set of nodes while a condition remains true. Has a hard iteration cap to prevent infinite loops.

**Properties:**

| Property           | Type                    | Default | Description                                 |
| ------------------ | ----------------------- | ------- | ------------------------------------------- |
| `Name`             | string                  | —       | Node name (required)                        |
| `Condition`        | Func<FlowContext, bool> | —       | Continue while this returns true (required) |
| `Body`             | List<IFlowNode>         | empty   | Nodes to execute each iteration             |
| `MaxIterations`    | int                     | 10      | Maximum iterations allowed                  |
| `IterationDelayMs` | int                     | 0       | Pause between iterations in milliseconds    |

The iteration count is stored in context as `"{Name}_iteration_count"`.

**Example:**

```csharp
new WhileLoopNode("poll_status")
{
    Condition = ctx => ctx.Get<string>("job_status") != "done",
    MaxIterations = 20,
    IterationDelayMs = 5000,
    Body =
    [
        new HttpRequestNode("check_job")
        {
            Url = "https://api.example.com/job/123/status",
            OutputKey = "job_status_response"
        },
        new TransformNode("extract_status")
        {
            Transform = ctx => "done", // simplified for example
            OutputKey = "job_status"
        }
    ]
}
```

---

## DatabaseQueryNode\<T\>

Runs an EF Core database query using your application's `DbContext`.

**Properties:**

| Property           | Type                                      | Default                 | Description                                |
| ------------------ | ----------------------------------------- | ----------------------- | ------------------------------------------ |
| `Name`             | string                                    | —                       | Node name (required)                       |
| `Query`            | Func<T, CancellationToken, Task<object?>> | —                       | EF Core query (required)                   |
| `OutputKey`        | string?                                   | `"{Name}_result"`       | Context key for the result                 |
| `FailIfNull`       | bool                                      | false                   | Fail the node if the query returns null    |
| `NullErrorMessage` | string                                    | `"Query returned null"` | Error message when FailIfNull is triggered |

**T** is your application's `DbContext` type (e.g., `AppDbContext`).

The node resolves the context from `context.DbContext` (explicitly set) or from `context.GetDbContext<T>()` (DI-resolved, recommended for background flows).

**Example:**

```csharp
new DatabaseQueryNode<AppDbContext>("find_user")
{
    Query = async (db, ct) => await db.Users
        .FirstOrDefaultAsync(u => u.Email == email, ct),
    OutputKey = "user_record",
    FailIfNull = true,
    NullErrorMessage = $"No user found with email {email}"
}
```

---

## TransformNode

Reshapes or derives new values from the context. Useful for data transformation between steps.

**Properties:**

| Property           | Type                                                 | Default           | Description                                      |
| ------------------ | ---------------------------------------------------- | ----------------- | ------------------------------------------------ |
| `Name`             | string                                               | —                 | Node name (required)                             |
| `Transform`        | Func<FlowContext, object?>?                          | null              | Synchronous transform function                   |
| `TransformAsync`   | Func<FlowContext, CancellationToken, Task<object?>>? | null              | Asynchronous transform function (takes priority) |
| `OutputKey`        | string?                                              | `"{Name}_output"` | Context key for the result                       |
| `SkipIfNullOutput` | bool                                                 | true              | Skip writing to context if result is null        |

You must provide either `Transform` or `TransformAsync`. If both are set, `TransformAsync` is used.

**Example:**

```csharp
new TransformNode("build_summary")
{
    Transform = ctx =>
    {
        var name = ctx.Get<string>("user_name");
        var total = ctx.Get<double>("order_total");
        return $"{name} ordered ${total} worth of items";
    },
    OutputKey = "order_summary"
}
```

> If a transform throws an exception, the node fails and the error message is recorded.

---

## RetryNode

Wraps any node with automatic retry logic. Supports configurable delays and exponential backoff.

**Properties:**

| Property            | Type                    | Default | Description                                       |
| ------------------- | ----------------------- | ------- | ------------------------------------------------- |
| `Name`              | string                  | —       | Node name (required)                              |
| `Inner`             | IFlowNode               | —       | The node to retry (required)                      |
| `MaxAttempts`       | int                     | 3       | Total attempts (1 means no retry)                 |
| `DelayMs`           | int                     | 2000    | Base delay between attempts in milliseconds       |
| `BackoffMultiplier` | double                  | 1.0     | Multiplier applied to delay after each attempt    |
| `RetryOn`           | Func<NodeResult, bool>? | null    | Custom condition to decide if retry should happen |

The retry count is stored in context as `"{Name}_retry_count"`.

**Delay formula:** `DelayMs × (BackoffMultiplier ^ attemptNumber)`

With `DelayMs = 2000` and `BackoffMultiplier = 2.0`: delays are 2s → 4s → 8s.

**Example:**

```csharp
new RetryNode("retry_api_call")
{
    Inner = new HttpRequestNode("unreliable_api")
    {
        Url = "https://api.example.com/process",
        MaxRetries = 1,
        OutputKey = "api_result"
    },
    MaxAttempts = 4,
    DelayMs = 3000,
    BackoffMultiplier = 2.0,
    RetryOn = result => !result.Success
}
```

---

## ParallelNode

Runs multiple branches at the same time. Each branch is a list of nodes that execute sequentially within the branch.

**Properties:**

| Property               | Type                  | Default | Description                             |
| ---------------------- | --------------------- | ------- | --------------------------------------- |
| `Name`                 | string                | —       | Node name (required)                    |
| `Branches`             | List<List<IFlowNode>> | —       | List of branch node lists (required)    |
| `WaitAll`              | bool                  | true    | Wait for all branches before continuing |
| `AbortOnBranchFailure` | bool                  | false   | Cancel remaining branches if any fails  |

**Example:**

```csharp
new ParallelNode("send_notifications")
{
    Branches =
    [
        // Branch 1: email
        [
            new EmailSendNode("email_user") { /* ... */ }
        ],
        // Branch 2: webhook
        [
            new HttpRequestNode("webhook_notify")
            {
                Url = "https://hooks.example.com/notify",
                Method = HttpMethod.Post
            }
        ],
        // Branch 3: log to external service
        [
            new HttpRequestNode("log_event")
            {
                Url = "https://api.example.com/log",
                Method = HttpMethod.Post
            }
        ]
    ],
    AbortOnBranchFailure = false
}
```

> **Thread safety:** All branches share the same `FlowContext`. Each branch must write to unique context keys to avoid conflicts. The context dictionary is not thread-safe.
