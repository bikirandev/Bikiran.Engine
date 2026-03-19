# Node Library

Nodes are the building blocks of every flow. Each node performs a single task — making an HTTP request, sending an email, waiting, branching, or querying a database. This document covers every available node type.

---

## Node Summary

| Node                                    | Type Label      | Purpose                                    |
| --------------------------------------- | --------------- | ------------------------------------------ |
| [WaitNode](#waitnode)                   | `Wait`          | Pause execution for a set duration         |
| [HttpRequestNode](#httprequestnode)     | `HttpRequest`   | Make an outbound HTTP call                 |
| [EmailSendNode](#emailsendnode)         | `EmailSend`     | Send an email via template                 |
| [IfElseNode](#ifelsenode)               | `IfElse`        | Branch based on a condition                |
| [WhileLoopNode](#whileloopnode)         | `WhileLoop`     | Repeat nodes while a condition is true     |
| [DatabaseQueryNode](#databasequerynode) | `DatabaseQuery` | Run an EF Core query and store the result  |
| [TransformNode](#transformnode)         | `Transform`     | Reshape or derive values from context      |
| [RetryNode](#retrynode)                 | `Retry`         | Wrap any node with retry logic             |
| [ParallelNode](#parallelnode)           | `Parallel`      | Execute multiple branches at the same time |
| [SmsNode](#smsnode)                     | `Sms`           | Send an SMS message                        |
| [NotificationNode](#notificationnode)   | `Notification`  | Send a Firebase push notification          |

---

## WaitNode

Pauses the flow for a fixed number of milliseconds.

| Property  | Type   | Default  | Description          |
| --------- | ------ | -------- | -------------------- |
| `Name`    | string | required | Node name            |
| `DelayMs` | int    | `1000`   | Milliseconds to wait |

**Behavior:** Calls `Task.Delay(DelayMs)`. If cancelled, returns a failure result.

```csharp
new WaitNode("pause_before_retry") { DelayMs = 3000 }
```

---

## HttpRequestNode

Makes an outbound HTTP request with retry support.

| Property            | Type                         | Default             | Description                                              |
| ------------------- | ---------------------------- | ------------------- | -------------------------------------------------------- |
| `Name`              | string                       | required            | Node name                                                |
| `Url`               | string                       | required            | Target URL                                               |
| `Method`            | HttpMethod                   | `GET`               | HTTP verb                                                |
| `Headers`           | Dictionary\<string, string\> | empty               | Extra request headers                                    |
| `Body`              | string?                      | null                | Request body (JSON string)                               |
| `TimeoutSeconds`    | int                          | `30`                | Per-attempt timeout                                      |
| `MaxRetries`        | int                          | `3`                 | Number of retry attempts                                 |
| `RetryDelaySeconds` | int                          | `2`                 | Seconds between retries                                  |
| `OutputKey`         | string                       | `"{Name}_response"` | Context key where the response body is stored            |
| `ExpectStatusCode`  | int?                         | null                | If set, non-matching status codes are treated as failure |

**Behavior:**

1. Builds the HTTP request with headers and body.
2. Sends the request, retrying up to `MaxRetries` times on failure.
3. On success, stores the raw response body in context under `OutputKey`.
4. On failure after all retries, returns an error result.

```csharp
new HttpRequestNode("fetch_order") {
    Url = "https://api.example.com/order/123",
    Method = HttpMethod.Get,
    MaxRetries = 3,
    OutputKey = "order_data"
}
```

---

## EmailSendNode

Sends an email using the application's `EmailSenderV3Service`.

| Property              | Type                                               | Default  | Description                                 |
| --------------------- | -------------------------------------------------- | -------- | ------------------------------------------- |
| `Name`                | string                                             | required | Node name                                   |
| `ToEmail`             | string                                             | required | Recipient email address                     |
| `ToName`              | string                                             | `""`     | Recipient display name                      |
| `Subject`             | string                                             | required | Email subject                               |
| `Template`            | string                                             | required | Template key (e.g., `"ORDER_CREATE"`)       |
| `Placeholders`        | Dictionary\<string, string\>                       | empty    | Template placeholders                       |
| `PlaceholderResolver` | Func\<FlowContext, Dictionary\<string, string\>\>? | null     | Dynamically build placeholders from context |

**Requirement:** `FlowContext.EmailSender` must be set when building the flow.

```csharp
new EmailSendNode("notify_customer") {
    ToEmail = "user@example.com",
    ToName = "John",
    Subject = "Order Confirmed",
    Template = "ORDER_CREATE",
    Placeholders = new() { { "OrderId", "1234" } }
}
```

---

## IfElseNode

Evaluates a condition and runs one of two branches.

| Property      | Type                      | Description                          |
| ------------- | ------------------------- | ------------------------------------ |
| `Name`        | string                    | Node name                            |
| `Condition`   | Func\<FlowContext, bool\> | The condition to evaluate            |
| `TrueBranch`  | List\<IFlowNode\>         | Nodes to run when condition is true  |
| `FalseBranch` | List\<IFlowNode\>         | Nodes to run when condition is false |

**Behavior:**

1. Evaluates the condition.
2. Runs the matching branch nodes sequentially.
3. Each branch node is logged as its own `FlowNodeLog` entry.
4. Records which branch was taken (`"true"` or `"false"`).

```csharp
new IfElseNode("check_status") {
    Condition = ctx => ctx.Get<string>("order_data") != null,
    TrueBranch = [
        new EmailSendNode("send_confirmation") { /* ... */ }
    ],
    FalseBranch = [
        new WaitNode("wait_and_retry") { DelayMs = 5000 }
    ]
}
```

---

## WhileLoopNode

Repeats a set of nodes while a condition is true.

| Property           | Type                      | Default  | Description                             |
| ------------------ | ------------------------- | -------- | --------------------------------------- |
| `Name`             | string                    | required | Node name                               |
| `Condition`        | Func\<FlowContext, bool\> | required | Continue while this returns true        |
| `Body`             | List\<IFlowNode\>         | required | Nodes to run each iteration             |
| `MaxIterations`    | int                       | `10`     | Hard cap to prevent infinite loops      |
| `IterationDelayMs` | int                       | `0`      | Milliseconds to wait between iterations |

**Behavior:**

1. Checks the condition before each iteration.
2. Runs body nodes if the condition is true and iteration count is within the limit.
3. Stores the iteration count in context as `"{Name}_iteration_count"`.
4. Returns a failure if `MaxIterations` is reached.

```csharp
new WhileLoopNode("poll_status") {
    Condition = ctx => ctx.Get<string>("status") != "ready",
    Body = [
        new HttpRequestNode("check_status") { Url = "...", OutputKey = "status" },
        new WaitNode("poll_delay") { DelayMs = 5000 }
    ],
    MaxIterations = 12
}
```

---

## DatabaseQueryNode

Runs an EF Core query and stores the result in context.

| Property           | Type                                                     | Default                 | Description                              |
| ------------------ | -------------------------------------------------------- | ----------------------- | ---------------------------------------- |
| `Name`             | string                                                   | required                | Node name                                |
| `Query`            | Func\<AppDbContext, CancellationToken, Task\<object?\>\> | required                | The EF Core query delegate               |
| `OutputKey`        | string                                                   | `"{Name}_result"`       | Context key for the result               |
| `FailIfNull`       | bool                                                     | `false`                 | Return failure if the query returns null |
| `NullErrorMessage` | string                                                   | `"Query returned null"` | Error message when failing on null       |

**Requirement:** `FlowContext.DbContext` must be set.

**Important — always use EF Core LINQ or parameterized queries. Never use raw SQL string interpolation inside the delegate.**

```csharp
new DatabaseQueryNode("fetch_subscription") {
    Query = async (db, ct) => await db.Subscription
        .Where(s => s.Id == subscriptionId && s.TimeDeleted == 0)
        .FirstOrDefaultAsync(ct),
    OutputKey = "subscription",
    FailIfNull = true,
    NullErrorMessage = "Subscription not found"
}
```

---

## TransformNode

Reshapes or derives values from existing context variables. Does not make external calls.

| Property           | Type                                                     | Default           | Description                                |
| ------------------ | -------------------------------------------------------- | ----------------- | ------------------------------------------ |
| `Name`             | string                                                   | required          | Node name                                  |
| `Transform`        | Func\<FlowContext, object?\>?                            | null              | Synchronous transform                      |
| `TransformAsync`   | Func\<FlowContext, CancellationToken, Task\<object?\>\>? | null              | Asynchronous transform                     |
| `OutputKey`        | string                                                   | `"{Name}_output"` | Context key for the result                 |
| `SkipIfNullOutput` | bool                                                     | `true`            | Do not store the key if the result is null |

Provide either `Transform` or `TransformAsync` (not both). If both are set, `TransformAsync` takes priority. TransformNode never fails.

```csharp
new TransformNode("build_message") {
    Transform = ctx => {
        var sub = ctx.Get<Subscription>("subscription");
        return $"Your subscription #{sub?.Id} expires on {sub?.ExpiryDate}.";
    },
    OutputKey = "sms_message"
}
```

---

## RetryNode

Wraps any node and retries it on failure with configurable delay and backoff.

| Property            | Type                      | Default  | Description                                                      |
| ------------------- | ------------------------- | -------- | ---------------------------------------------------------------- |
| `Name`              | string                    | required | Node name                                                        |
| `Inner`             | IFlowNode                 | required | The node to wrap                                                 |
| `MaxAttempts`       | int                       | `3`      | Total attempts (1 = no retry)                                    |
| `DelayMs`           | int                       | `2000`   | Base delay between attempts (ms)                                 |
| `RetryOn`           | Func\<NodeResult, bool\>? | null     | Custom retry condition; if null, retries on any failure          |
| `BackoffMultiplier` | double                    | `1.0`    | Multiply delay by this on each retry (2.0 = exponential backoff) |

```csharp
new RetryNode("retry_payment") {
    Inner = new HttpRequestNode("verify_payment") {
        Url = "https://api.sslcommerz.com/validator/...",
        OutputKey = "payment_result"
    },
    MaxAttempts = 4,
    DelayMs = 3000,
    BackoffMultiplier = 2.0  // 3s → 6s → 12s
}
```

---

## ParallelNode

Executes multiple branches at the same time using `Task.WhenAll`.

| Property               | Type                      | Default  | Description                             |
| ---------------------- | ------------------------- | -------- | --------------------------------------- |
| `Name`                 | string                    | required | Node name                               |
| `Branches`             | List\<List\<IFlowNode\>\> | required | Each inner list is one parallel branch  |
| `WaitAll`              | bool                      | `true`   | Wait for all branches before proceeding |
| `AbortOnBranchFailure` | bool                      | `false`  | Cancel remaining branches if one fails  |

**Thread safety note:** The shared `FlowContext.Variables` dictionary is not thread-safe. Each parallel branch should write to its own unique key.

```csharp
new ParallelNode("multi_channel_notify") {
    Branches = [
        [new EmailSendNode("email") { /* ... */ }],
        [new SmsNode("sms") { /* ... */ }],
        [new NotificationNode("push") { /* ... */ }]
    ],
    AbortOnBranchFailure = false
}
```

---

## SmsNode

Sends an SMS message. Automatically selects the vendor based on the phone number's country code.

| Property          | Type                         | Default  | Description                                             |
| ----------------- | ---------------------------- | -------- | ------------------------------------------------------- |
| `Name`            | string                       | required | Node name                                               |
| `ToPhone`         | string                       | required | Phone number with country code (e.g., `+8801712345678`) |
| `Message`         | string                       | required | Plain text message                                      |
| `PhoneResolver`   | Func\<FlowContext, string\>? | null     | Resolve phone dynamically from context                  |
| `MessageResolver` | Func\<FlowContext, string\>? | null     | Resolve message dynamically from context                |
| `Vendor`          | SmsVendorEnum                | `Auto`   | `Auto` / `SmsApi` / `Infobip`                           |
| `MaxRetries`      | int                          | `2`      | Retry attempts on send failure                          |

**Vendor selection (when `Auto`):**

- Phone starts with `880` (Bangladesh) → uses SmsApi
- All other numbers → uses Infobip

```csharp
new SmsNode("notify_user") {
    ToPhone = "+8801712345678",
    Message = "Your order has been confirmed.",
    MaxRetries = 2
}
```

---

## NotificationNode

Sends a Firebase Cloud Messaging (FCM) push notification.

| Property              | Type                         | Default  | Description                            |
| --------------------- | ---------------------------- | -------- | -------------------------------------- |
| `Name`                | string                       | required | Node name                              |
| `DeviceToken`         | string?                      | null     | FCM device token (for a single device) |
| `Topic`               | string?                      | null     | FCM topic name (for broadcast)         |
| `Title`               | string                       | required | Notification title                     |
| `Body`                | string                       | required | Notification body text                 |
| `Data`                | Dictionary\<string, string\> | empty    | Custom data payload                    |
| `DeviceTokenResolver` | Func\<FlowContext, string\>? | null     | Resolve device token from context      |
| `TitleResolver`       | Func\<FlowContext, string\>? | null     | Resolve title from context             |
| `BodyResolver`        | Func\<FlowContext, string\>? | null     | Resolve body from context              |

Either `DeviceToken` or `Topic` must be provided. On success, stores the FCM message ID in context as `"{Name}_message_id"`.

```csharp
new NotificationNode("push_notify") {
    DeviceToken = deviceToken,
    Title = "Payment Confirmed",
    Body = "Your invoice has been paid.",
    Data = new() { { "invoiceId", "456" }, { "type", "payment" } }
}
```
