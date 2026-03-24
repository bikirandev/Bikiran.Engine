# Examples

This document provides practical, ready-to-use code examples for common workflow patterns.

---

## Simple HTTP Request and Email

Fetch data from an API and send a notification email:

```csharp
var serviceId = await FlowBuilder
    .Create("order_confirmation")
    .Configure(cfg => {
        cfg.MaxExecutionTime = TimeSpan.FromMinutes(5);
        cfg.OnFailure = OnFailureAction.Stop;
        cfg.TriggerSource = nameof(OrdersV3Controller);
    })
    .WithContext(ctx => {
        ctx.DbContext = _context;
        ctx.HttpContext = HttpContext;
    })
    .AddNode(new HttpRequestNode("fetch_order") {
        Url = "https://api.example.com/order/123",
        Method = HttpMethod.Get,
        MaxRetries = 3,
        OutputKey = "order_data"
    })
    .AddNode(new EmailSendNode("notify_customer") {
        ToEmail = "customer@example.com",
        ToName = "Jane Doe",
        Subject = "Order Confirmed",
        Template = "ORDER_CREATE",
        Placeholders = new() { { "OrderId", "123" } }
    })
    .StartAsync();
```

---

## Database Lookup with Conditional Branching

Load a record from the database and do different things based on whether it exists:

```csharp
var serviceId = await FlowBuilder
    .Create("user_lookup_flow")
    .WithContext(ctx => { ctx.DbContext = _context; })
    .AddNode(new DatabaseQueryNode<AppDbContext>("find_user") {
        Query = async (db, ct) => await db.UserProfile
            .FirstOrDefaultAsync(u => u.Email == email && u.TimeDeleted == 0, ct),
        OutputKey = "existing_user",
        FailIfNull = false
    })
    .AddNode(new IfElseNode("user_exists_check") {
        Condition = ctx =>
            ctx.Has("existing_user") &&
            ctx.Get<UserProfile>("existing_user") != null,
        TrueBranch = [
            new TransformNode("use_existing_user") {
                Transform = ctx => ctx.Get<UserProfile>("existing_user")!.Id,
                OutputKey = "user_id"
            }
        ],
        FalseBranch = [
            new WaitNode("placeholder") { DelayMs = 100 }
        ]
    })
    .StartAsync();
```

---

## Database Query, Transform, and Email

Load a subscription, build a message from it, and send an email:

```csharp
var serviceId = await FlowBuilder
    .Create("subscription_reminder")
    .WithContext(ctx => { ctx.DbContext = _context; })
    .AddNode(new DatabaseQueryNode<AppDbContext>("fetch_subscription") {
        Query = async (db, ct) => await db.Subscription
            .Where(s => s.Id == subscriptionId && s.TimeDeleted == 0)
            .FirstOrDefaultAsync(ct),
        OutputKey = "subscription",
        FailIfNull = true,
        NullErrorMessage = "Subscription not found"
    })
    .AddNode(new TransformNode("build_message") {
        Transform = ctx => {
            var sub = ctx.Get<Subscription>("subscription");
            return $"Your subscription #{sub?.Id} is active until {sub?.ExpiryDate}.";
        },
        OutputKey = "reminder_message"
    })
    .AddNode(new EmailSendNode("send_reminder") {
        ToEmail = userEmail,
        Subject = "Subscription Reminder",
        HtmlBodyResolver = ctx =>
            $"<p>{ctx.Get<string>("reminder_message")}</p>"
    })
    .StartAsync();
```

---

## Retry with Exponential Backoff

Wrap an unreliable HTTP call with automatic retries and exponential backoff:

```csharp
.AddNode(new RetryNode("retry_payment_verify") {
    Inner = new HttpRequestNode("payment_verify") {
        Url = "https://api.sslcommerz.com/validator/api/validationserverAPI.php",
        Method = HttpMethod.Get,
        MaxRetries = 1,
        OutputKey = "payment_result"
    },
    MaxAttempts = 4,
    DelayMs = 3000,
    BackoffMultiplier = 2.0,   // delays: 3s → 6s → 12s
    RetryOn = result => !result.Success
})
```

---

## Parallel Notifications

Send notifications through multiple channels at the same time:

```csharp
.AddNode(new ParallelNode("multi_channel_notify") {
    Branches = [
        // Branch 1: Email
        [new EmailSendNode("email_notify") {
            ToEmail = userEmail,
            Subject = "Payment Confirmed",
            HtmlBody = "<h1>Payment Confirmed</h1><p>Your invoice has been paid.</p>"
        }],
        // Branch 2: Webhook
        [new HttpRequestNode("webhook_notify") {
            Url = "https://hooks.example.com/notify",
            Method = HttpMethod.Post,
            Body = $"{{\"invoiceId\": \"{invoiceId}\", \"status\": \"paid\"}}"
        }]
    ],
    AbortOnBranchFailure = false
})
```

---

## JSON Flow Definition via Admin API

### Create the Definition

```http
POST /api/bikiran-engine/definitions
Content-Type: application/json

{
  "definitionKey": "welcome_email_flow",
  "displayName": "Welcome Email Flow",
  "description": "Sends a welcome email to a new user after a brief delay",
  "tags": "auth,email,onboarding",
  "flowJson": "{\"name\":\"welcome_email_flow\",\"config\":{\"maxExecutionTimeSeconds\":60,\"onFailure\":\"Continue\"},\"nodes\":[{\"type\":\"Wait\",\"name\":\"brief_delay\",\"params\":{\"delayMs\":500}},{\"type\":\"EmailSend\",\"name\":\"send_welcome\",\"params\":{\"toEmail\":\"{{email}}\",\"toName\":\"{{name}}\",\"subject\":\"Welcome!\",\"template\":\"AUTH_CREATE_ACCOUNT\",\"placeholders\":{\"DisplayName\":\"{{name}}\",\"Email\":\"{{email}}\"}}}]}"
}
```

### Trigger from a Controller

```csharp
var serviceId = await _definitionRunner.TriggerAsync(
    definitionKey: "welcome_email_flow",
    parameters: new() {
        { "email", userEmail },
        { "name", displayName }
    },
    contextSetup: ctx => {
        ctx.HttpContext = HttpContext;
    },
    triggerSource: nameof(AuthV3Controller)
);
```

---

## Scheduled Daily Report

```http
POST /api/bikiran-engine/schedules
Content-Type: application/json

{
  "scheduleKey": "daily_subscription_expiry_report",
  "displayName": "Daily Subscription Expiry Email",
  "definitionKey": "subscription_expiry_warn_flow",
  "scheduleType": "cron",
  "cronExpression": "0 0 8 * * ?",
  "timeZone": "Asia/Dhaka",
  "defaultParameters": {
    "adminEmail": "admin@example.com",
    "reportDate": "{{today_date}}"
  }
}
```

---

## Scheduled Health Check

```http
POST /api/bikiran-engine/schedules
Content-Type: application/json

{
  "scheduleKey": "api_health_check",
  "displayName": "API Health Check",
  "definitionKey": "api_ping_flow",
  "scheduleType": "interval",
  "intervalMinutes": 15,
  "defaultParameters": {}
}
```

---

## One-Time Delayed Task

Schedule a welcome email to go out 24 hours after signup:

```csharp
var oneDayFromNow = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();

_context.FlowSchedule.Add(new FlowSchedule {
    ScheduleKey = $"welcome_sequence_{userId}",
    DisplayName = $"Welcome Sequence for User {userId}",
    DefinitionKey = "user_welcome_sequence_flow",
    ScheduleType = "once",
    RunOnceAt = oneDayFromNow,
    DefaultParameters = JsonConvert.SerializeObject(new {
        userId = userId.ToString(),
        email = userEmail
    }),
    IsActive = true,
    TimeCreated = TimeOperation.GetUnixTime(),
    TimeUpdated = TimeOperation.GetUnixTime()
});
await _context.SaveChangesAsync();

await _schedulerService.RegisterScheduleAsync(schedule);
```

---

## End-to-End Test Flow

A complete flow for testing that the engine works correctly in your application:

```csharp
[HttpGet("test-flow")]
public async Task<ActionResult> TestFlow()
{
    var serviceId = await FlowBuilder
        .Create("test_flow")
        .Configure(c => {
            c.MaxExecutionTime = TimeSpan.FromSeconds(30);
            c.TriggerSource = "TestController";
        })
        .WithContext(ctx => {
            ctx.DbContext = _context;
            ctx.HttpContext = HttpContext;
        })
        .AddNode(new WaitNode("initial_wait") { DelayMs = 500 })
        .AddNode(new HttpRequestNode("check_api") {
            Url = "https://httpbin.org/get",
            Method = HttpMethod.Get,
            MaxRetries = 2,
            OutputKey = "api_response"
        })
        .AddNode(new TransformNode("extract_data") {
            Transform = ctx => {
                var raw = ctx.Get<string>("api_response");
                return $"Response length: {raw?.Length ?? 0}";
            },
            OutputKey = "summary"
        })
        .StartAsync();

    return Ok(new { serviceId });
}
```

After running this endpoint, check the admin API at `/api/bikiran-engine/runs/{serviceId}` to see the full execution details.

---

## Lifecycle Events (OnSuccess / OnFail / OnFinish)

Attach handlers that run after the main flow completes — useful for logging, alerts, and cleanup:

```csharp
var serviceId = await FlowBuilder
    .Create("add_domain")
    .Configure(cfg => {
        cfg.MaxExecutionTime = TimeSpan.FromMinutes(5);
        cfg.OnFailure = OnFailureAction.Stop;
        cfg.TriggerSource = "DomainController";
    })
    .WithContext(ctx => {
        ctx.HttpContext = HttpContext;
        ctx.Logger = _logger;
    })
    .AddNode(new HttpRequestNode("add_dns_record") {
        Url = "https://api.cloudflare.com/v4/zones/abc/dns_records",
        Method = HttpMethod.Post,
        Body = "{\"type\":\"A\",\"name\":\"app.example.com\",\"content\":\"1.2.3.4\"}",
        OutputKey = "dns_result"
    })
    .AddNode(new WaitNode("wait_for_dns") { DelayMs = 15000 })
    .AddNode(new HttpRequestNode("verify_dns") {
        Url = "https://dns.google/resolve?name=app.example.com&type=A",
        Method = HttpMethod.Get,
        OutputKey = "dns_verify_result"
    })
    .OnSuccess(new HttpRequestNode("log_success") {
        Url = "https://api.example.com/hooks/domain-provisioned",
        Method = HttpMethod.Post,
        Body = "{\"domain\":\"app.example.com\",\"status\":\"ok\"}"
    })
    .OnFail(new EmailSendNode("alert_on_failure") {
        ToEmail = "devops@example.com",
        Subject = "Domain provisioning failed",
        HtmlBodyResolver = ctx =>
            $"<p>Flow <b>{ctx.FlowName}</b> failed: {ctx.FlowError}</p>"
    })
    .OnFinish(new HttpRequestNode("audit_webhook") {
        Url = "https://api.example.com/audit",
        Method = HttpMethod.Post,
        Body = "{\"event\":\"domain_flow_finished\"}"
    })
    .StartAsync();
```

**Execution order:**
1. Main nodes run in sequence (add_dns_record → wait_for_dns → verify_dns)
2. If all succeeded → `OnSuccess` handlers run
3. If any failed → `OnFail` handlers run instead
4. `OnFinish` handlers always run last

Lifecycle event nodes can access `context.FlowStatus` (`"completed"` or `"failed"`) and `context.FlowError` to inspect the outcome.
