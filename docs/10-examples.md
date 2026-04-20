# Examples

Practical, ready-to-use code examples for common workflow patterns.

---

## HTTP Request and Email Notification

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
        ctx.HttpContext = HttpContext;
    })
    .AddNode(new HttpRequestNode("FetchOrder") {
        Url = "https://api.example.com/order/123",
        Method = HttpMethod.Get,
        MaxRetries = 3,
        ProgressMessage = "Fetching order details"
    })
    .AddNode(new EmailSendNode("NotifyCustomer") {
        ToEmail = "customer@example.com",
        ToName = "Jane Doe",
        Subject = "Order Confirmed",
        Template = "ORDER_CREATE",
        Placeholders = new() { { "OrderId", "123" } },
        ProgressMessage = "Sending confirmation email"
    })
    .StartAsync();
```

---

## Database Lookup with Conditional Branching

Load a record from the database and take different paths based on whether it exists:

```csharp
var serviceId = await FlowBuilder
    .Create("user_lookup_flow")
    .AddNode(new DatabaseQueryNode<AppDbContext>("FindUser") {
        Query = async (db, ct) => await db.UserProfile
            .FirstOrDefaultAsync(u => u.Email == email && u.TimeDeleted == 0, ct),
        FailIfNull = false
    })
    .AddNode(new IfElseNode("UserExistsCheck") {
        Condition = ctx =>
            ctx.Has("FindUser_Result") &&
            ctx.Get<UserProfile>("FindUser_Result") != null,
        TrueBranch = [
            new TransformNode("UseExistingUser") {
                Transform = ctx => ctx.Get<UserProfile>("FindUser_Result")!.Id
            }
        ],
        FalseBranch = [
            new WaitNode("Placeholder") { Delay = TimeSpan.FromMilliseconds(100) }
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
    .AddNode(new DatabaseQueryNode<AppDbContext>("FetchSubscription") {
        Query = async (db, ct) => await db.Subscription
            .Where(s => s.Id == subscriptionId && s.TimeDeleted == 0)
            .FirstOrDefaultAsync(ct),
        FailIfNull = true,
        NullErrorMessage = "Subscription not found"
    })
    .AddNode(new TransformNode("BuildMessage") {
        Transform = ctx => {
            var sub = ctx.Get<Subscription>("FetchSubscription_Result");
            return $"Your subscription #{sub?.Id} is active until {sub?.ExpiryDate}.";
        }
    })
    .AddNode(new EmailSendNode("SendReminder") {
        ToEmail = userEmail,
        Subject = "Subscription Reminder",
        HtmlBodyResolver = ctx =>
            $"<p>{ctx.Get<string>("BuildMessage_Result")}</p>"
    })
    .StartAsync();
```

---

## Retry with Exponential Backoff

Wrap an unreliable HTTP call with automatic retries and increasing delays:

```csharp
.AddNode(new RetryNode("RetryPaymentVerify") {
    Inner = new HttpRequestNode("PaymentVerify") {
        Url = "https://api.sslcommerz.com/validator/api/validationserverAPI.php",
        Method = HttpMethod.Get,
        MaxRetries = 1
    },
    MaxAttempts = 4,
    DelayMs = 3000,
    BackoffMultiplier = 2.0,   // delays: 3s → 6s → 12s
    RetryOn = result => !result.Success,
    ProgressMessage = "Verifying payment with gateway"
})
```

---

## Parallel Notifications

Send notifications through multiple channels at the same time:

```csharp
.AddNode(new ParallelNode("MultiChannelNotify") {
    Branches = [
        // Branch 1: Email
        [new EmailSendNode("EmailNotify") {
            ToEmail = userEmail,
            Subject = "Payment Confirmed",
            HtmlBody = "<h1>Payment Confirmed</h1><p>Your invoice has been paid.</p>"
        }],
        // Branch 2: Webhook
        [new HttpRequestNode("WebhookNotify") {
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
  "flowJson": "{\"name\":\"welcome_email_flow\",\"config\":{\"maxExecutionTimeSeconds\":60,\"onFailure\":\"Continue\"},\"nodes\":[{\"type\":\"Wait\",\"name\":\"BriefDelay\",\"params\":{\"delayMs\":500}},{\"type\":\"EmailSend\",\"name\":\"SendWelcome\",\"params\":{\"toEmail\":\"{{email}}\",\"toName\":\"{{name}}\",\"subject\":\"Welcome!\",\"template\":\"AUTH_CREATE_ACCOUNT\",\"placeholders\":{\"DisplayName\":\"{{name}}\",\"Email\":\"{{email}}\"},\"progressMessage\":\"Sending welcome email\"}}]}"
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

## Lifecycle Events (OnSuccess, OnFail, OnFinish)

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
    .Wait("Waiting for DNS propagation", TimeSpan.FromSeconds(15))
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

1. Main steps run in sequence
2. Flow status is saved to the database
3. If all succeeded → `OnSuccess` handlers run
4. If any failed → `OnFail` handlers run instead
5. `OnFinish` handlers always run last

### Checking the Outcome in OnFinish

```csharp
.OnFinish(new EmailSendNode("final_report") {
    ToEmail = "devops@example.com",
    Subject = "Domain Flow Report",
    HtmlBodyResolver = ctx =>
        ctx.FlowStatus == FlowRunStatus.Completed
            ? $"<p>Flow <b>{ctx.FlowName}</b> completed successfully.</p>"
            : $"<p>Flow <b>{ctx.FlowName}</b> failed: {ctx.FlowError}</p>"
})
```

---

## End-to-End Test Flow

A complete flow for verifying the engine works correctly in your application:

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
            ctx.HttpContext = HttpContext;
        })
        .Wait("Initial wait", TimeSpan.FromMilliseconds(500))
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
