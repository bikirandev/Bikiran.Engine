# Usage Examples

This document provides practical, ready-to-use examples covering common workflow patterns.

---

## Basic Flow: HTTP Request + Email

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
        ctx.EmailSender = _emailSender;
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

## Database Query + Conditional Branching

Load a record from the database and branch based on whether it exists:

```csharp
var serviceId = await FlowBuilder
    .Create("user_lookup_flow")
    .WithContext(ctx => { ctx.DbContext = _context; })
    .AddNode(new DatabaseQueryNode("find_user") {
        Query = async (db, ct) => await db.UserProfile
            .FirstOrDefaultAsync(u => u.Email == email && u.TimeDeleted == 0, ct),
        OutputKey = "existing_user",
        FailIfNull = false
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
            // Handle user not found — perhaps create one or send an error
            new WaitNode("placeholder") { DelayMs = 100 }
        ]
    })
    .StartAsync();
```

---

## Database Query + Transform + SMS

Load a subscription, build a message, and send an SMS:

```csharp
var serviceId = await FlowBuilder
    .Create("subscription_reminder")
    .WithContext(ctx => { ctx.DbContext = _context; })
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

## Retry Wrapping a Flaky HTTP Call

Wrap an HTTP request with exponential backoff retry:

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
    BackoffMultiplier = 2.0,   // 3s → 6s → 12s
    RetryOn = result => !result.Success
})
```

---

## Parallel Notification (Email + SMS + Push)

Send notifications through three channels simultaneously:

```csharp
.AddNode(new ParallelNode("multi_channel_notify") {
    Branches = [
        // Branch 1: Email
        [new EmailSendNode("email_notify") {
            ToEmail = userEmail,
            Subject = "Payment Confirmed",
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
            Data = new() {
                { "invoiceId", invoiceId.ToString() },
                { "type", "payment" }
            }
        }]
    ],
    AbortOnBranchFailure = false  // Don't fail the flow if one channel is down
})
```

---

## DB-Stored Flow Definition (Admin API)

### Create the Definition

```http
POST /admin/flow-runner/definitions
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
        ctx.EmailSender = _emailSender;
    },
    triggerSource: nameof(AuthV3Controller)
);
```

---

## Scheduled Daily Report (Cron)

```http
POST /admin/flow-runner/schedules
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

## Scheduled Health Check (Interval)

```http
POST /admin/flow-runner/schedules
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

## One-Time Delayed Welcome (Programmatic)

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

## Integration Test Flow

A complete flow for end-to-end testing:

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
            ctx.EmailSender = _emailSender;
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
