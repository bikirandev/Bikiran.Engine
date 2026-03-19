# FlowRunner — Phase 2: Enhanced Node Library

> **Status:** Planning  
> **Depends on:** Phase 1 (Core Engine) fully implemented  
> **Goal:** Extend the node library with six new node types covering database queries, data transformation, retry wrapping, parallel execution, SMS, and Firebase push notifications.

---

## Table of Contents

1. [Overview](#1-overview)
2. [New Nodes Summary](#2-new-nodes-summary)
3. [DatabaseQueryNode](#3-databasequerynode)
4. [TransformNode](#4-transformnode)
5. [RetryNode](#5-retrynode)
6. [ParallelNode](#6-parallelnode)
7. [SmsNode](#7-smsnode)
8. [NotificationNode (Firebase)](#8-notificationnode-firebase)
9. [Update FlowContext for New Services](#9-update-flowcontext-for-new-services)
10. [File & Folder Structure](#10-file--folder-structure)
11. [Step-by-Step Implementation Guide](#11-step-by-step-implementation-guide)
12. [Usage Examples](#12-usage-examples)

---

## 1. Overview

Phase 2 adds six new nodes to the FlowRunner library, each covering a common automation need:

| Node                | Use Case                                            |
| ------------------- | --------------------------------------------------- |
| `DatabaseQueryNode` | Query DB via EF Core, store result in context       |
| `TransformNode`     | Reshape/derive context values without side effects  |
| `RetryNode`         | Wrap any node with configurable retry logic         |
| `ParallelNode`      | Execute multiple branches concurrently              |
| `SmsNode`           | Send SMS via SmsAPI (BD) or Infobip (international) |
| `NotificationNode`  | Send Firebase push notification to a device/topic   |

All new nodes follow the existing `IFlowNode` interface contract and use `NodeResult.Ok` / `NodeResult.Fail` returns. No changes to the Phase 1 engine are required — all changes are additive.

---

## 2. New Nodes Summary

```
Services/FlowRunner/Nodes/
├── DatabaseQueryNode.cs    (NEW)
├── TransformNode.cs        (NEW)
├── RetryNode.cs            (NEW)
├── ParallelNode.cs         (NEW)
├── SmsNode.cs              (NEW)
└── NotificationNode.cs     (NEW)
```

---

## 3. DatabaseQueryNode

**File:** `Services/FlowRunner/Nodes/DatabaseQueryNode.cs`

**Purpose:** Execute a database query using the `AppDbContext` from `FlowContext`. The result is stored in context under `OutputKey`. Uses a delegate (`Func<AppDbContext, CancellationToken, Task<object?>>`) to keep SQL injection impossible — all queries go through EF Core.

### Properties

| Property           | Type                                                   | Default                 | Description                                             |
| ------------------ | ------------------------------------------------------ | ----------------------- | ------------------------------------------------------- |
| `Name`             | `string`                                               | required                | Node name (lowercase_underscore)                        |
| `Query`            | `Func<AppDbContext, CancellationToken, Task<object?>>` | required                | EF query delegate                                       |
| `OutputKey`        | `string`                                               | `"{Name}_result"`       | Context key to store query result                       |
| `FailIfNull`       | `bool`                                                 | `false`                 | Return failure if query returns null                    |
| `NullErrorMessage` | `string`                                               | `"Query returned null"` | Error message when `FailIfNull=true` and result is null |

### Behavior

1. Check `FlowContext.DbContext` is not null — fail if missing.
2. Invoke `Query(context.DbContext, cancellationToken)`.
3. If result is null and `FailIfNull=true`: return `NodeResult.Fail(NullErrorMessage)`.
4. Store result: `context.Set(OutputKey, result)`.
5. Return `NodeResult.Ok(result)`.

### Code Skeleton

```csharp
public class DatabaseQueryNode(string name) : IFlowNode
{
    public string Name { get; } = name;
    public string NodeType => "DatabaseQuery";

    public required Func<AppDbContext, CancellationToken, Task<object?>> Query { get; set; }
    public string OutputKey { get; set; } = $"{name}_result";
    public bool FailIfNull { get; set; } = false;
    public string NullErrorMessage { get; set; } = "Query returned null";

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        if (context.DbContext == null)
            return NodeResult.Fail("DatabaseQueryNode requires FlowContext.DbContext to be set.");

        var result = await Query(context.DbContext, ct);

        if (result == null && FailIfNull)
            return NodeResult.Fail(NullErrorMessage);

        if (result != null)
            context.Set(OutputKey, result);

        return NodeResult.Ok(result);
    }
}
```

### Important Notes

- **Security:** Never use raw SQL string interpolation inside the delegate. Always use EF Core LINQ or parameterized queries.
- **Correct pattern:**
  ```csharp
  Query = async (db, ct) => await db.Subscription
      .Where(s => s.Id == subscriptionId && s.TimeDeleted == 0)
      .FirstOrDefaultAsync(ct)
  ```
- **Incorrect pattern (SQL injection risk):**
  ```csharp
  Query = async (db, ct) => await db.Database
      .ExecuteSqlRawAsync($"SELECT * FROM Subscription WHERE Id = {id}", ct) // NEVER DO THIS
  ```

---

## 4. TransformNode

**File:** `Services/FlowRunner/Nodes/TransformNode.cs`

**Purpose:** Apply a synchronous or asynchronous transformation to the `FlowContext`. Does NOT make external calls. Used to derive, reshape, or calculate values from existing context variables before the next node consumes them.

### Properties

| Property           | Type                                                  | Default           | Description                                      |
| ------------------ | ----------------------------------------------------- | ----------------- | ------------------------------------------------ |
| `Name`             | `string`                                              | required          | Node name                                        |
| `Transform`        | `Func<FlowContext, object?>`                          | null              | Sync transform delegate                          |
| `TransformAsync`   | `Func<FlowContext, CancellationToken, Task<object?>>` | null              | Async transform delegate                         |
| `OutputKey`        | `string`                                              | `"{Name}_output"` | Context key to store result                      |
| `SkipIfNullOutput` | `bool`                                                | `true`            | Do not set context key if transform returns null |

> One of `Transform` or `TransformAsync` must be provided — but not both. `TransformAsync` takes precedence if both are set.

### Behavior

1. Execute the appropriate transform delegate.
2. If result is not null (or `SkipIfNullOutput=false`): `context.Set(OutputKey, result)`.
3. Always return `NodeResult.Ok(result)` — TransformNode never fails.

### Code Skeleton

```csharp
public class TransformNode(string name) : IFlowNode
{
    public string Name { get; } = name;
    public string NodeType => "Transform";

    public Func<FlowContext, object?>? Transform { get; set; }
    public Func<FlowContext, CancellationToken, Task<object?>>? TransformAsync { get; set; }
    public string OutputKey { get; set; } = $"{name}_output";
    public bool SkipIfNullOutput { get; set; } = true;

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        object? result;

        if (TransformAsync != null)
            result = await TransformAsync(context, ct);
        else if (Transform != null)
            result = Transform(context);
        else
            return NodeResult.Fail("TransformNode requires either Transform or TransformAsync to be set.");

        if (result != null || !SkipIfNullOutput)
            context.Set(OutputKey, result!);

        return NodeResult.Ok(result);
    }
}
```

---

## 5. RetryNode

**File:** `Services/FlowRunner/Nodes/RetryNode.cs`

**Purpose:** Wraps an inner `IFlowNode` and retries it on failure up to `MaxAttempts` times with configurable delay. Unlike `HttpRequestNode`'s built-in retry (which only retries HTTP errors), `RetryNode` can wrap ANY node and retry based on a custom condition.

### Properties

| Property            | Type                      | Default  | Description                                                                |
| ------------------- | ------------------------- | -------- | -------------------------------------------------------------------------- |
| `Name`              | `string`                  | required | Node name                                                                  |
| `Inner`             | `IFlowNode`               | required | The node to wrap                                                           |
| `MaxAttempts`       | `int`                     | `3`      | Total attempts (1 = no retry)                                              |
| `DelayMs`           | `int`                     | `2000`   | Delay between attempts                                                     |
| `RetryOn`           | `Func<NodeResult, bool>?` | null     | Custom predicate; if null, retries on any failure                          |
| `BackoffMultiplier` | `double`                  | `1.0`    | Multiply delay by this on each retry (1.0 = no backoff, 2.0 = exponential) |

### Behavior

1. Execute `Inner.ExecuteAsync(context, ct)`.
2. If result matches `RetryOn` (or is failed when `RetryOn` is null) AND attempts remaining:
   - Increment attempt counter.
   - Apply `Task.Delay(currentDelay, ct)` then `currentDelay *= BackoffMultiplier`.
   - Retry from step 1.
3. After max attempts: return the last result from inner (success or fail).
4. Store `RetryCount` in the final `NodeResult`.

### Code Skeleton

```csharp
public class RetryNode(string name) : IFlowNode
{
    public string Name { get; } = name;
    public string NodeType => "Retry";

    public required IFlowNode Inner { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public int DelayMs { get; set; } = 2000;
    public Func<NodeResult, bool>? RetryOn { get; set; }
    public double BackoffMultiplier { get; set; } = 1.0;

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        NodeResult? lastResult = null;
        int delay = DelayMs;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            lastResult = await Inner.ExecuteAsync(context, ct);

            bool shouldRetry = RetryOn != null
                ? RetryOn(lastResult)
                : !lastResult.Success;

            if (!shouldRetry || attempt == MaxAttempts)
            {
                lastResult.RetryCount = attempt - 1;
                return lastResult;
            }

            if (delay > 0)
                await Task.Delay(delay, ct);

            delay = (int)(delay * BackoffMultiplier);
        }

        lastResult!.RetryCount = MaxAttempts - 1;
        return lastResult;
    }
}
```

---

## 6. ParallelNode

**File:** `Services/FlowRunner/Nodes/ParallelNode.cs`

**Purpose:** Execute multiple node branches concurrently using `Task.WhenAll`. Each branch is an independent list of `IFlowNode` items. All branches share the same `FlowContext` — set unique `OutputKey`s in each branch to avoid collisions.

### Properties

| Property               | Type                    | Default  | Description                                         |
| ---------------------- | ----------------------- | -------- | --------------------------------------------------- |
| `Name`                 | `string`                | required | Node name                                           |
| `Branches`             | `List<List<IFlowNode>>` | required | Each inner list = one parallel branch               |
| `WaitAll`              | `bool`                  | `true`   | Wait for ALL branches to complete before proceeding |
| `AbortOnBranchFailure` | `bool`                  | `false`  | Abort remaining branches if one fails               |

### Behavior

1. Create one `Task` per branch by executing its nodes sequentially.
2. If `WaitAll=true`: `await Task.WhenAll(branchTasks)`.
3. If `WaitAll=false`: fire-and-forget each branch (best-effort).
4. If `AbortOnBranchFailure=true` and any branch fails: cancel remaining via shared `CancellationTokenSource`.
5. Collect results from each branch into `Output` as `List<NodeResult>`.
6. Return `NodeResult.Ok(branchResults)` if all succeeded, else `NodeResult.Fail(...)`.

### Important Notes

- **Thread Safety:** `FlowContext.Variables` (a `Dictionary<string, object>`) is NOT thread-safe. When using `ParallelNode`, each branch should write to a unique key. If branches compete for the same key, use a `ConcurrentDictionary` override or the `TransformNode` in sequence after `ParallelNode`.
- **Logging:** Each branch node logs independently to `FlowNodeLog`. Logs from parallel branches may appear interleaved in the DB — use the `Sequence` field combined with `BranchName` for ordering.
- **Branch naming:** Branch nodes have their sequence prefixed: `"parallel_{branchIndex}_{sequence}"`.

### Code Skeleton

```csharp
public class ParallelNode(string name) : IFlowNode
{
    public string Name { get; } = name;
    public string NodeType => "Parallel";

    public required List<List<IFlowNode>> Branches { get; set; }
    public bool WaitAll { get; set; } = true;
    public bool AbortOnBranchFailure { get; set; } = false;

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var branchResults = new System.Collections.Concurrent.ConcurrentBag<(int, NodeResult)>();

        async Task RunBranch(int branchIndex, List<IFlowNode> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var result = await node.ExecuteAsync(context, cts.Token);
                branchResults.Add((branchIndex, result));

                if (!result.Success && AbortOnBranchFailure)
                {
                    cts.Cancel();
                    return;
                }
            }
        }

        var tasks = Branches
            .Select((branch, idx) => RunBranch(idx, branch))
            .ToList();

        if (WaitAll)
            await Task.WhenAll(tasks);
        else
            _ = Task.WhenAll(tasks); // fire-and-forget

        var results = branchResults.OrderBy(r => r.Item1).Select(r => r.Item2).ToList();
        bool anyFailed = results.Any(r => !r.Success);

        return anyFailed
            ? NodeResult.Fail($"One or more parallel branches failed ({results.Count(r => !r.Success)} of {results.Count})")
            : NodeResult.Ok(results);
    }
}
```

---

## 7. SmsNode

**File:** `Services/FlowRunner/Nodes/SmsNode.cs`

**Purpose:** Send an SMS message using the existing `SmsApi` / Infobip infrastructure. Automatically selects the vendor: `smsapi` for Bangladesh (`880` prefix), `infobip` for all other numbers — matching the existing pattern in `OtpOperationSend.cs`.

### Properties

| Property          | Type                         | Default  | Description                                            |
| ----------------- | ---------------------------- | -------- | ------------------------------------------------------ |
| `Name`            | `string`                     | required | Node name                                              |
| `ToPhone`         | `string`                     | required | Phone number with country code (e.g. `+8801712345678`) |
| `Message`         | `string`                     | required | Plain text SMS message                                 |
| `PhoneResolver`   | `Func<FlowContext, string>?` | null     | Dynamic phone from context                             |
| `MessageResolver` | `Func<FlowContext, string>?` | null     | Dynamic message from context                           |
| `Vendor`          | `SmsVendorEnum`              | `Auto`   | Force a specific vendor, or Auto                       |
| `MaxRetries`      | `int`                        | `2`      | Retry attempts on send failure                         |

### `SmsVendorEnum`

```csharp
// Services/FlowRunner/Nodes/SmsNode.cs (nested)
public enum SmsVendorEnum
{
    Auto,      // 880-prefix → smsapi; otherwise → infobip
    SmsApi,    // Force local BD vendor
    Infobip    // Force international vendor
}
```

### Behavior

1. Resolve phone and message (use resolvers if provided, otherwise use static properties).
2. Determine vendor: if `Auto`, check if phone starts with `880` → `SmsApi`, else → `Infobip`.
3. Send via the appropriate underlying model:
   - `SmsApi` → Use `BikiranWebAPI.Models.SmsSender.SmsApi.SendAsync(phone, message)`
   - `Infobip` → Use Infobip SMS endpoint (same pattern as existing OTP SMS sender)
4. On success: return `NodeResult.Ok()`.
5. On failure: retry up to `MaxRetries`, then return `NodeResult.Fail(errorMessage)`.

### Code Skeleton

```csharp
public class SmsNode(string name) : IFlowNode
{
    public string Name { get; } = name;
    public string NodeType => "Sms";

    public string ToPhone { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Func<FlowContext, string>? PhoneResolver { get; set; }
    public Func<FlowContext, string>? MessageResolver { get; set; }
    public SmsVendorEnum Vendor { get; set; } = SmsVendorEnum.Auto;
    public int MaxRetries { get; set; } = 2;

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        var phone = PhoneResolver != null ? PhoneResolver(context) : ToPhone;
        var message = MessageResolver != null ? MessageResolver(context) : Message;

        if (string.IsNullOrWhiteSpace(phone))
            return NodeResult.Fail("SmsNode: phone number is empty.");

        if (string.IsNullOrWhiteSpace(message))
            return NodeResult.Fail("SmsNode: message is empty.");

        // Strip '+' for vendor detection
        var normalizedPhone = phone.TrimStart('+');
        var vendor = Vendor == SmsVendorEnum.Auto
            ? (normalizedPhone.StartsWith("880") ? SmsVendorEnum.SmsApi : SmsVendorEnum.Infobip)
            : Vendor;

        for (int attempt = 1; attempt <= MaxRetries + 1; attempt++)
        {
            try
            {
                if (vendor == SmsVendorEnum.SmsApi)
                    await SmsApi.SendAsync(phone, message);
                else
                    await InfobipSmsSender.SendAsync(phone, message);

                return NodeResult.Ok(new { Phone = phone, Vendor = vendor.ToString() });
            }
            catch (Exception ex)
            {
                if (attempt > MaxRetries)
                    return NodeResult.Fail($"SMS failed after {MaxRetries + 1} attempts: {ex.Message}", MaxRetries);

                await Task.Delay(2000, ct);
            }
        }

        return NodeResult.Fail("SMS send failed.");
    }
}
```

### Dependency

`SmsNode` calls the existing SMS infrastructure. Ensure `SMSAPICredentials` and Infobip credentials are configured via `ConfigsManager`. No new DI registrations are needed.

---

## 8. NotificationNode (Firebase)

**File:** `Services/FlowRunner/Nodes/NotificationNode.cs`

**Purpose:** Send a Firebase Cloud Messaging (FCM) push notification to a specific device token or topic. Uses the existing `FirebaseAdmin` SDK already referenced in the project.

### Properties

| Property              | Type                         | Default  | Description                          |
| --------------------- | ---------------------------- | -------- | ------------------------------------ |
| `Name`                | `string`                     | required | Node name                            |
| `DeviceToken`         | `string`                     | null     | FCM device token (for single device) |
| `Topic`               | `string`                     | null     | FCM topic name (for topic broadcast) |
| `Title`               | `string`                     | required | Notification title                   |
| `Body`                | `string`                     | required | Notification body                    |
| `Data`                | `Dictionary<string, string>` | empty    | Custom data payload                  |
| `DeviceTokenResolver` | `Func<FlowContext, string>?` | null     | Resolve token from context           |
| `TitleResolver`       | `Func<FlowContext, string>?` | null     | Resolve title from context           |
| `BodyResolver`        | `Func<FlowContext, string>?` | null     | Resolve body from context            |

> Either `DeviceToken` or `Topic` must be provided (or their resolver equivalents).

### Behavior

1. Initialize Firebase via `FirebaseUserService2.InitializeFirebase()` (idempotent).
2. Resolve all string values (use resolvers if provided).
3. Build `FirebaseAdmin.Messaging.Message`:
   - If device token: `Message.Token = token`
   - If topic: `Message.Topic = topic`
4. Send via `FirebaseMessaging.DefaultInstance.SendAsync(message, ct)`.
5. On success: store the FCM message ID in context as `"{Name}_message_id"`.
6. Return `NodeResult.Ok(messageId)`.

### Code Skeleton

```csharp
using FirebaseAdmin.Messaging;

public class NotificationNode(string name) : IFlowNode
{
    public string Name { get; } = name;
    public string NodeType => "Notification";

    public string? DeviceToken { get; set; }
    public string? Topic { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, string> Data { get; set; } = new();
    public Func<FlowContext, string>? DeviceTokenResolver { get; set; }
    public Func<FlowContext, string>? TitleResolver { get; set; }
    public Func<FlowContext, string>? BodyResolver { get; set; }

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        FirebaseUserService2.InitializeFirebase();

        var token = DeviceTokenResolver != null ? DeviceTokenResolver(context) : DeviceToken;
        var topic = Topic;
        var title = TitleResolver != null ? TitleResolver(context) : Title;
        var body = BodyResolver != null ? BodyResolver(context) : Body;

        if (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(topic))
            return NodeResult.Fail("NotificationNode: either DeviceToken or Topic must be set.");

        var message = new Message
        {
            Notification = new Notification { Title = title, Body = body },
            Data = Data.Count > 0 ? Data : null,
            Token = string.IsNullOrWhiteSpace(token) ? null : token,
            Topic = string.IsNullOrWhiteSpace(topic) ? null : topic
        };

        try
        {
            var messageId = await FirebaseMessaging.DefaultInstance.SendAsync(message, ct);
            context.Set($"{Name}_message_id", messageId);
            return NodeResult.Ok(new { MessageId = messageId });
        }
        catch (FirebaseMessagingException ex)
        {
            return NodeResult.Fail($"Firebase notification failed: {ex.MessagingErrorCode} — {ex.Message}");
        }
    }
}
```

---

## 9. Update FlowContext for New Services

No new services need to be added to `FlowContext` for Phase 2. All new nodes rely on:

- `FlowContext.DbContext` — for `DatabaseQueryNode` (already exists in Phase 1)
- `FlowContext.Services` — optional fallback for any DI-resolved services
- Static utility calls — for `SmsNode` and `NotificationNode`

However, a minor documentation update to `FlowContext.cs` should be added:

```csharp
// In FlowContext.cs — add XML doc comment on Services property
/// <summary>
/// Optional DI service provider. Resolve scoped services here when static helpers are insufficient.
/// Example: context.Services?.GetRequiredService{MyService}()
/// </summary>
public IServiceProvider? Services { get; set; }
```

---

## 10. File & Folder Structure

```
Services/FlowRunner/Nodes/
├── DatabaseQueryNode.cs     (NEW — Phase 2)
├── EmailSendNode.cs         (Phase 1)
├── HttpRequestNode.cs       (Phase 1)
├── IfElseNode.cs            (Phase 1)
├── NotificationNode.cs      (NEW — Phase 2)
├── ParallelNode.cs          (NEW — Phase 2)
├── RetryNode.cs             (NEW — Phase 2)
├── SmsNode.cs               (NEW — Phase 2)
├── TransformNode.cs         (NEW — Phase 2)
├── WaitNode.cs              (Phase 1)
└── WhileLoopNode.cs         (Phase 1)
```

---

## 11. Step-by-Step Implementation Guide

### Step 1 — `TransformNode`

Implement first — no external dependencies, pure context manipulation. Easiest to verify correctness.

**Verify:** In a test flow, set a string in context, transform it to uppercase, and read it back.

---

### Step 2 — `RetryNode`

Implement second — wraps existing nodes, can be tested with `HttpRequestNode` pointing to a failing URL.

**Verify:** Wrap an `HttpRequestNode` with `MaxAttempts=3`. Observe three `FlowNodeLog` entries from the inner node executions.

---

### Step 3 — `DatabaseQueryNode`

**Requires** `FlowContext.DbContext` to be set. Test by querying a known `UserProfile` row.

**Verify:** Call `DatabaseQueryNode` querying `_context.UserProfile.FindAsync(1, ct)`. Assert `OutputKey` is populated in context.

---

### Step 4 — `SmsNode`

**Requires** `SMSAPI_SENDER`, `SMSAPI_ID`, `SMSAPI_KEY` env vars in `.env`. Test with a controlled phone number.

**Verify:** Send SMS to a test number. Check `LogNotificationEmail` (or any log table) for a send record.

---

### Step 5 — `NotificationNode`

**Requires** Firebase configured (`.conf.bucket` or `FIREBASE_JSON` env var). Test with a known FCM device token.

**Verify:** Receive push notification on a test device. Check `NodeResult.Output` for `MessageId`.

---

### Step 6 — `ParallelNode`

Implement after all branch nodes are stable. Test with 2 branches: one `WaitNode` + one `HttpRequestNode`.

**Verify:** Both branches execute concurrently. Total `DurationMs` is close to the max branch time, not the sum.

---

### Step 7 — Update Documentation

Update `FlowRunner/README.md` (create if absent) listing all available nodes with their properties.

---

## 12. Usage Examples

### Example A: Query + Transform + Notify

```csharp
var serviceId = await FlowBuilder
    .Create("order_process_and_notify")
    .WithContext(ctx => {
        ctx.DbContext = _context;
        ctx.HttpContext = HttpContext;
    })
    .AddNode(new DatabaseQueryNode("fetch_subscription") {
        Query = async (db, ct) => await db.Subscription
            .Where(s => s.Id == subscriptionId && s.TimeDeleted == 0)
            .FirstOrDefaultAsync(ct),
        OutputKey = "subscription",
        FailIfNull = true,
        NullErrorMessage = "Subscription not found"
    })
    .AddNode(new TransformNode("build_sms_text") {
        Transform = ctx => {
            var sub = ctx.Get<Subscription>("subscription");
            return $"Your subscription #{sub?.Id} is active until {sub?.ExpiryDate}.";
        },
        OutputKey = "sms_message"
    })
    .AddNode(new SmsNode("notify_via_sms") {
        ToPhone = "+8801712345678",
        MessageResolver = ctx => ctx.Get<string>("sms_message") ?? "",
        MaxRetries = 2
    })
    .StartAsync();
```

---

### Example B: Retry Wrapping a Flaky HTTP Call

```csharp
.AddNode(new RetryNode("retry_payment_verify") {
    Inner = new HttpRequestNode("payment_verify") {
        Url = "https://api.sslcommerz.com/validator/api/validationserverAPI.php",
        Method = HttpMethod.Get,
        MaxRetries = 1,  // Inner handles 1 retry; RetryNode adds outer retries
        OutputKey = "payment_result"
    },
    MaxAttempts = 4,
    DelayMs = 3000,
    BackoffMultiplier = 2.0,  // 3s → 6s → 12s
    RetryOn = result => !result.Success
})
```

---

### Example C: Parallel Email + SMS + Push

```csharp
.AddNode(new ParallelNode("multi_channel_notify") {
    Branches = new List<List<IFlowNode>> {
        // Branch 1: Email
        [new EmailSendNode("email_notify") {
            ToEmail = userEmail, Subject = "Payment Confirmed",
            Template = "PAYMENT_VERIFIED"
        }],
        // Branch 2: SMS
        [new SmsNode("sms_notify") {
            ToPhone = userPhone,
            Message = $"Payment confirmed. Invoice #{invoiceId}."
        }],
        // Branch 3: Firebase Push
        [new NotificationNode("push_notify") {
            DeviceToken = deviceToken,
            Title = "Payment Confirmed",
            Body = $"Invoice #{invoiceId} has been paid.",
            Data = new() { { "invoiceId", invoiceId.ToString() }, { "type", "payment" } }
        }]
    },
    AbortOnBranchFailure = false  // Notification failures should not stop the flow
})
```

---

### Example D: Load-or-Create Pattern with DatabaseQueryNode + IfElseNode

```csharp
.AddNode(new DatabaseQueryNode("find_user") {
    Query = async (db, ct) => await db.UserProfile
        .FirstOrDefaultAsync(u => u.Email == email && u.TimeDeleted == 0, ct),
    OutputKey = "existing_user",
    FailIfNull = false  // null is OK — we'll create in the false branch
})
.AddNode(new IfElseNode("user_exists_check") {
    Condition = ctx => ctx.Has("existing_user") && ctx.Get<UserProfile>("existing_user") != null,
    TrueBranch = [
        new TransformNode("use_existing_user") {
            Transform = ctx => ctx.Get<UserProfile>("existing_user")!.Id,
            OutputKey = "user_id"
        }
    ],
    FalseBranch = [
        // ... create user and set "user_id" in context
    ]
})
```

---

_End of Phase 2 Plan — Ready for implementation after Phase 1 is complete._
