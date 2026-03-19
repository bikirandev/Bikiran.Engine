# Node Library

Nodes are the building blocks of every flow. Each node performs a single task — making an HTTP request, sending an email, waiting, branching, or querying a database. This document covers every available node type.

---

## Node Summary

| Node                                    | Type Label      | Purpose                                    |
| --------------------------------------- | --------------- | ------------------------------------------ |
| [WaitNode](#waitnode)                   | `Wait`          | Pause execution for a set duration         |
| [HttpRequestNode](#httprequestnode)     | `HttpRequest`   | Make an outbound HTTP call                 |
| [EmailSendNode](#emailsendnode)         | `EmailSend`     | Send an email (template, HTML, or text)    |
| [IfElseNode](#ifelsenode)               | `IfElse`        | Branch based on a condition                |
| [WhileLoopNode](#whileloopnode)         | `WhileLoop`     | Repeat nodes while a condition is true     |
| [DatabaseQueryNode](#databasequerynode) | `DatabaseQuery` | Run an EF Core query and store the result  |
| [TransformNode](#transformnode)         | `Transform`     | Reshape or derive values from context      |
| [RetryNode](#retrynode)                 | `Retry`         | Wrap any node with retry logic             |
| [ParallelNode](#parallelnode)           | `Parallel`      | Execute multiple branches at the same time |

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
| `ExpectValue`       | string?                      | null                | JSON validation expression on the parsed response (see below) |

**Behavior:**

1. Builds the HTTP request with headers and body.
2. Sends the request, retrying up to `MaxRetries` times on failure.
3. On success, stores the raw response body in context under `OutputKey`.
4. If `ExpectStatusCode` is set, non-matching status codes cause failure.
5. If `ExpectValue` is set, the response body is parsed as JSON and the expression is evaluated. If it returns `false`, the node fails.
6. On failure after all retries, returns an error result.

### ExpectValue — Response Validation

The `ExpectValue` property accepts a simple expression that validates the JSON response. The parsed JSON is available as `$val`.

**Supported operators:** `>=`, `<=`, `>`, `<`, `==`, `!=`, `&&`, `||`

**Examples:**

```csharp
// Validate that the response contains an amount >= 100 and a specific name
new HttpRequestNode("verify_payment") {
    Url = "https://api.example.com/payment/123",
    Method = HttpMethod.Get,
    OutputKey = "payment_data",
    ExpectValue = "$val.data.amount >= 100 && $val.data.name == \"Bikiran\""
}

// Simple boolean check
new HttpRequestNode("check_status") {
    Url = "https://api.example.com/health",
    OutputKey = "health_result",
    ExpectValue = "$val.status == \"ok\""
}
```

When `ExpectValue` fails, the node returns an error with a message describing which condition was not met.

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

Sends an email using a registered SMTP credential or the application's email service.

| Property              | Type                                               | Default  | Description                                 |
| --------------------- | -------------------------------------------------- | -------- | ------------------------------------------- |
| `Name`                | string                                             | required | Node name                                   |
| `ToEmail`             | string                                             | required | Recipient email address                     |
| `ToName`              | string                                             | `""`     | Recipient display name                      |
| `Subject`             | string                                             | required | Email subject                               |
| `CredentialName`      | string?                                            | null     | Named SMTP credential (registered at startup) |
| `Template`            | string?                                            | null     | Template key (e.g., `"ORDER_CREATE"`) — optional |
| `HtmlBody`            | string?                                            | null     | Raw HTML body content                       |
| `TextBody`            | string?                                            | null     | Plain text body content                     |
| `HtmlBodyResolver`    | Func\<FlowContext, string\>?                       | null     | Dynamically build HTML body from context    |
| `TextBodyResolver`    | Func\<FlowContext, string\>?                       | null     | Dynamically build text body from context    |
| `Placeholders`        | Dictionary\<string, string\>                       | empty    | Template placeholders                       |
| `PlaceholderResolver` | Func\<FlowContext, Dictionary\<string, string\>\>? | null     | Dynamically build placeholders from context |

**Body resolution priority:** The node uses the first available option:
1. `Template` — renders the named template with placeholders
2. `HtmlBody` / `HtmlBodyResolver` — sends raw HTML
3. `TextBody` / `TextBodyResolver` — sends plain text

At least one of `Template`, `HtmlBody`, `HtmlBodyResolver`, `TextBody`, or `TextBodyResolver` must be provided.

```csharp
// Using a template
new EmailSendNode("notify_customer") {
    ToEmail = "user@example.com",
    ToName = "John",
    Subject = "Order Confirmed",
    Template = "ORDER_CREATE",
    Placeholders = new() { { "OrderId", "1234" } }
}

// Using HTML body
new EmailSendNode("send_invoice") {
    CredentialName = "smtp_primary",
    ToEmail = "user@example.com",
    Subject = "Your Invoice",
    HtmlBody = "<h1>Invoice #1234</h1><p>Amount: $150.00</p>"
}

// Using dynamic HTML body from context
new EmailSendNode("send_report") {
    ToEmail = "admin@example.com",
    Subject = "Daily Report",
    HtmlBodyResolver = ctx => $"<h1>Report</h1><p>{ctx.Get<string>("report_summary")}</p>"
}

// Using plain text body
new EmailSendNode("send_alert") {
    ToEmail = "ops@example.com",
    Subject = "Alert",
    TextBody = "CPU usage exceeded 90% on server-01."
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

Runs an EF Core query against the host application's database and stores the result in context. Generic over the host app's `DbContext` type for type-safe queries.

| Property           | Type                                                       | Default                 | Description                              |
| ------------------ | ---------------------------------------------------------- | ----------------------- | ---------------------------------------- |
| `Name`             | string                                                     | required                | Node name                                |
| `Query`            | Func\<TContext, CancellationToken, Task\<object?\>\>       | required                | The EF Core query delegate               |
| `OutputKey`        | string                                                     | `"{Name}_result"`       | Context key for the result               |
| `FailIfNull`       | bool                                                       | `false`                 | Return failure if the query returns null |
| `NullErrorMessage` | string                                                     | `"Query returned null"` | Error message when failing on null       |

**Requirement:** `FlowContext.DbContext` must be set to an instance of `TContext`.

**Important — always use EF Core LINQ or parameterized queries. Never use raw SQL string interpolation inside the delegate.**

```csharp
new DatabaseQueryNode<AppDbContext>("fetch_subscription") {
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
    OutputKey = "reminder_message"
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
        [new HttpRequestNode("webhook") { /* ... */ }]
    ],
    AbortOnBranchFailure = false
}
```

---

## Creating Custom Nodes

Developers can create custom nodes inside their own application by implementing `IFlowNode`. Custom nodes have full access to the `FlowContext` — including shared variables, credentials, and injected services.

> For a comprehensive guide covering naming conventions, error handling, testing, thread safety, and advanced patterns, see [12-custom-node-guide.md](12-custom-node-guide.md).

### Step 1 — Implement IFlowNode

```csharp
public class InvoicePdfNode : IFlowNode
{
    public string Name { get; }
    public string NodeType => "InvoicePdf";

    public string InvoiceId { get; set; } = "";
    public string OutputKey { get; set; } = "pdf_url";

    public InvoicePdfNode(string name) => Name = name;

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        // Access registered credentials
        var cred = context.GetCredential<GenericCredential>("pdf_service");
        var apiKey = cred.Values["ApiKey"];

        // Use shared variables from previous nodes
        var orderId = context.Get<string>("order_id");

        // Your custom logic
        var pdfUrl = await GeneratePdfAsync(orderId, apiKey, ct);

        // Store result for downstream nodes
        context.Set(OutputKey, pdfUrl);

        return NodeResult.Ok(pdfUrl);
    }
}
```

### Step 2 — Use in a Flow

Custom nodes are used exactly like built-in nodes:

```csharp
var serviceId = await FlowBuilder
    .Create("invoice_flow")
    .AddNode(new HttpRequestNode("fetch_order") {
        Url = "https://api.example.com/order/123",
        OutputKey = "order_data"
    })
    .AddNode(new InvoicePdfNode("generate_pdf") {
        OutputKey = "pdf_url"
    })
    .AddNode(new EmailSendNode("send_invoice") {
        ToEmail = "customer@example.com",
        Subject = "Your Invoice",
        HtmlBodyResolver = ctx =>
            $"<p>Download your invoice: {ctx.Get<string>("pdf_url")}</p>"
    })
    .StartAsync();
```

### Step 3 — Register for JSON Definitions (Optional)

To use custom nodes in database-stored flow definitions (JSON), register the type at startup:

```csharp
builder.Services.AddBikiranEngine(options =>
{
    options.ConnectionString = "...";
    options.RegisterNode<InvoicePdfNode>("InvoicePdf");
});
```

Once registered, the node can be used in JSON flow definitions:

```json
{
  "type": "InvoicePdf",
  "name": "generate_pdf",
  "params": {
    "invoiceId": "{{invoiceId}}",
    "outputKey": "pdf_url"
  }
}
```

