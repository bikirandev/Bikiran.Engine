# Examples

Practical patterns and complete examples for common Bikiran.Engine use cases.

---

## HTTP Request + Email Notification

Fetch data from an API, then send an email with the results.

```csharp
var serviceId = await FlowBuilder
    .Create("order_confirmation")
    .AddNode(new HttpRequestNode("fetch_order")
    {
        Url = "https://api.example.com/orders/123",
        Method = "GET",
        Headers = { ["Authorization"] = "Bearer token123" }
    })
    .AddNode(new EmailSendNode("send_confirmation")
    {
        CredentialName = "smtp_primary",
        ToEmail = "customer@example.com",
        Subject = "Order Confirmed",
        HtmlBodyResolver = ctx =>
        {
            var orderJson = ctx.Get<string>("fetch_order");
            return $"<h1>Order Confirmed</h1><pre>{orderJson}</pre>";
        }
    })
    .StartAsync();
```

---

## Database Lookup with Branching

Query a database, then take different paths based on the result.

```csharp
var serviceId = await FlowBuilder
    .Create("check_user_status")
    .InjectDbContext(serviceProvider)
    .AddNode(new DatabaseQueryNode<AppDbContext>("lookup_user")
    {
        QueryAsync = async (db, ctx, ct) =>
        {
            var userId = ctx.Get<long>("user_id");
            var user = await db.Users.FindAsync(new object[] { userId }, ct);
            ctx.Set("is_active", user?.IsActive ?? false);
            return user;
        }
    })
    .AddNode(new IfElseNode("check_active")
    {
        Condition = ctx => ctx.Get<bool>("is_active"),
        TrueNodes = new List<IFlowNode>
        {
            new EmailSendNode("welcome_email")
            {
                CredentialName = "smtp_primary",
                ToEmail = "user@example.com",
                Subject = "Welcome Back!",
                HtmlBody = "<p>Your account is active.</p>"
            }
        },
        FalseNodes = new List<IFlowNode>
        {
            new HttpRequestNode("notify_support")
            {
                Url = "https://api.example.com/support/tickets",
                Method = "POST",
                Body = "{\"issue\": \"inactive_user\"}"
            }
        }
    })
    .StartAsync();
```

---

## Transform Chain

Process data through multiple transformations.

```csharp
var serviceId = await FlowBuilder
    .Create("data_pipeline")
    .AddNode(new HttpRequestNode("fetch_raw_data")
    {
        Url = "https://api.example.com/data",
        Method = "GET"
    })
    .AddNode(new TransformNode("parse_data")
    {
        TransformAction = ctx =>
        {
            var raw = ctx.Get<string>("fetch_raw_data");
            var parsed = JsonSerializer.Deserialize<List<Record>>(raw);
            ctx.Set("records", parsed);
        }
    })
    .AddNode(new TransformNode("filter_active")
    {
        TransformAction = ctx =>
        {
            var records = ctx.Get<List<Record>>("records");
            var active = records.Where(r => r.IsActive).ToList();
            ctx.Set("active_records", active);
            ctx.Set("active_count", active.Count);
        }
    })
    .AddNode(new TransformNode("generate_report")
    {
        TransformAction = ctx =>
        {
            var count = ctx.Get<int>("active_count");
            ctx.Set("report", $"Found {count} active records");
        }
    })
    .StartAsync();
```

---

## Retry with Backoff

Wrap an unreliable API call in a RetryNode with exponential backoff.

```csharp
var serviceId = await FlowBuilder
    .Create("resilient_api_call")
    .AddNode(new RetryNode("retry_payment")
    {
        MaxRetries = 5,
        DelayMs = 1000,      // Start at 1 second
        BackoffMultiplier = 2, // 1s, 2s, 4s, 8s, 16s
        InnerNode = new HttpRequestNode("process_payment")
        {
            Url = "https://api.payments.com/charge",
            Method = "POST",
            Body = "{\"amount\": 99.99, \"currency\": \"USD\"}",
            Headers = { ["Authorization"] = "Bearer payment_key" },
            ExpectedStatusCode = 200
        }
    })
    .StartAsync();
```

---

## Parallel Notifications

Send multiple notifications at the same time.

```csharp
var serviceId = await FlowBuilder
    .Create("parallel_notifications")
    .AddNode(new ParallelNode("notify_all")
    {
        Branches = new List<List<IFlowNode>>
        {
            // Branch 1: Email
            new()
            {
                new EmailSendNode("email_admin")
                {
                    CredentialName = "smtp_primary",
                    ToEmail = "admin@example.com",
                    Subject = "New Alert",
                    HtmlBody = "<p>Something happened.</p>"
                }
            },
            // Branch 2: Webhook
            new()
            {
                new HttpRequestNode("webhook_slack")
                {
                    Url = "https://hooks.slack.com/services/xxx",
                    Method = "POST",
                    Body = "{\"text\": \"New alert triggered\"}"
                }
            },
            // Branch 3: Log to database
            new()
            {
                new TransformNode("log_event")
                {
                    TransformAction = ctx =>
                        ctx.Set("branch3_logged", true)
                }
            }
        }
    })
    .StartAsync();
```

> **Important:** Each branch should write to unique context keys. Do not read keys that another branch writes to.

---

## While Loop

Poll an API until a condition is met.

```csharp
var serviceId = await FlowBuilder
    .Create("poll_status")
    .AddNode(new TransformNode("init_counter")
    {
        TransformAction = ctx => ctx.Set("attempts", 0)
    })
    .AddNode(new WhileLoopNode("wait_for_ready")
    {
        Condition = ctx => ctx.Get<string>("status") != "ready",
        MaxIterations = 10,
        Body = new List<IFlowNode>
        {
            new HttpRequestNode("check_status")
            {
                Url = "https://api.example.com/job/status",
                Method = "GET"
            },
            new TransformNode("parse_status")
            {
                TransformAction = ctx =>
                {
                    var response = ctx.Get<string>("check_status");
                    var obj = JsonSerializer.Deserialize<StatusResponse>(response);
                    ctx.Set("status", obj.Status);
                    ctx.Set("attempts", ctx.Get<int>("attempts") + 1);
                }
            },
            new WaitNode("pause") { Seconds = 5 }
        }
    })
    .StartAsync();
```

---

## Lifecycle Events

Use lifecycle hooks to add logging, cleanup, or external notifications.

```csharp
var serviceId = await FlowBuilder
    .Create("monitored_flow")
    .Configure(config =>
    {
        config.OnFailureAction = OnFailureAction.Continue;
        config.GlobalTimeoutSeconds = 300;
    })
    .OnStart(ctx =>
    {
        Console.WriteLine($"Flow started: {ctx.FlowName} ({ctx.ServiceId})");
    })
    .OnNodeComplete((ctx, nodeName, result) =>
    {
        Console.WriteLine($"  Node '{nodeName}' completed: {result.Success}");
    })
    .OnFinish(ctx =>
    {
        var status = ctx.Status;
        Console.WriteLine($"Flow finished: {status}");

        if (status == FlowRunStatus.Failed)
            Console.WriteLine($"  Error: {ctx.ErrorMessage}");
    })
    .AddNode(new WaitNode("step1") { Seconds = 1 })
    .AddNode(new TransformNode("step2")
    {
        TransformAction = ctx => ctx.Set("result", "done")
    })
    .StartAsync();
```

> **Note:** `OnFinish` runs after all nodes complete, regardless of success or failure. Check `ctx.Status` to determine the outcome.

---

## JSON Flow Definition via API

### Create a Definition

```bash
curl -X POST http://localhost:5000/api/bikiran-engine/definitions \
  -H "Content-Type: application/json" \
  -d '{
    "definitionKey": "welcome_email",
    "displayName": "Welcome Email Flow",
    "description": "Sends a welcome email to new users",
    "flowJson": "{\"name\":\"welcome_email\",\"nodes\":[{\"type\":\"EmailSend\",\"name\":\"send_welcome\",\"params\":{\"credentialName\":\"smtp_primary\",\"toEmail\":\"{{userEmail}}\",\"subject\":\"Welcome!\",\"htmlBody\":\"<h1>Hello {{userName}}</h1>\"}}]}",
    "tags": "email,onboarding",
    "parameterSchema": "{\"userEmail\":{\"type\":\"string\",\"required\":true},\"userName\":{\"type\":\"string\",\"required\":true}}"
  }'
```

### Trigger with Parameters

```bash
curl -X POST http://localhost:5000/api/bikiran-engine/definitions/welcome_email/trigger \
  -H "Content-Type: application/json" \
  -d '{
    "parameters": {
      "userEmail": "new.user@example.com",
      "userName": "Alice"
    },
    "triggerSource": "registration_webhook"
  }'
```

### Check Run Progress

```bash
curl http://localhost:5000/api/bikiran-engine/runs/{serviceId}/progress
```

---

## Scheduled Report

### Create the Definition

```bash
curl -X POST http://localhost:5000/api/bikiran-engine/definitions \
  -H "Content-Type: application/json" \
  -d '{
    "definitionKey": "daily_report",
    "displayName": "Daily Sales Report",
    "flowJson": "{\"name\":\"daily_report\",\"nodes\":[{\"type\":\"HttpRequest\",\"name\":\"fetch_sales\",\"params\":{\"url\":\"https://api.example.com/sales/today\",\"method\":\"GET\"}},{\"type\":\"EmailSend\",\"name\":\"send_report\",\"params\":{\"credentialName\":\"smtp_primary\",\"toEmail\":\"{{reportEmail}}\",\"subject\":\"Daily Sales Report\",\"htmlBody\":\"<p>See attached data.</p>\"}}]}",
    "parameterSchema": "{\"reportEmail\":{\"type\":\"string\",\"required\":true,\"default\":\"reports@example.com\"}}"
  }'
```

### Create a Schedule

```bash
curl -X POST http://localhost:5000/api/bikiran-engine/schedules \
  -H "Content-Type: application/json" \
  -d '{
    "scheduleKey": "nightly_sales_report",
    "displayName": "Nightly Sales Report",
    "definitionKey": "daily_report",
    "scheduleType": "cron",
    "cronExpression": "0 0 2 * * ?",
    "defaultParameters": { "reportEmail": "reports@example.com" },
    "timeZone": "America/New_York",
    "maxConcurrent": 1
  }'
```

### Trigger Immediately

```bash
curl -X POST http://localhost:5000/api/bikiran-engine/schedules/nightly_sales_report/run-now
```

---

## Creating a Schedule from Code

```csharp
builder.Services.AddBikiranEngine(options =>
{
    options.ConnectionString = "Server=...";
    options.DatabaseProvider = "mysql";

    options.AddSchedule(schedule =>
    {
        schedule.ScheduleKey = "cleanup_job";
        schedule.DisplayName = "Weekly Cleanup";
        schedule.DefinitionKey = "cleanup_old_records";
        schedule.ScheduleType = "cron";
        schedule.CronExpression = "0 0 3 ? * SUN";
        schedule.TimeZone = "UTC";
    });
});
```

---

## Full Flow with All Features

A comprehensive example combining multiple patterns.

```csharp
var serviceId = await FlowBuilder
    .Create("complete_order_process")
    .InjectDbContext(serviceProvider)
    .InjectHttpContext(httpContext)
    .Configure(config =>
    {
        config.OnFailureAction = OnFailureAction.Continue;
        config.GlobalTimeoutSeconds = 120;
    })
    .OnStart(ctx =>
        Console.WriteLine($"Processing order: {ctx.ServiceId}"))
    .OnNodeComplete((ctx, name, result) =>
        Console.WriteLine($"  {name}: {(result.Success ? "OK" : "FAIL")}"))
    .OnFinish(ctx =>
        Console.WriteLine($"Order process {ctx.Status}"))

    // Step 1: Validate the order
    .AddNode(new DatabaseQueryNode<AppDbContext>("validate_order")
    {
        QueryAsync = async (db, ctx, ct) =>
        {
            var order = await db.Orders.FindAsync(new object[] { 42L }, ct);
            ctx.Set("order_valid", order != null);
            ctx.Set("order_total", order?.Total ?? 0m);
            return order;
        }
    })

    // Step 2: Branch based on validation
    .AddNode(new IfElseNode("check_valid")
    {
        Condition = ctx => ctx.Get<bool>("order_valid"),
        TrueNodes = new List<IFlowNode>
        {
            // Step 3: Process payment with retries
            new RetryNode("retry_payment")
            {
                MaxRetries = 3,
                DelayMs = 2000,
                BackoffMultiplier = 2,
                InnerNode = new HttpRequestNode("charge_card")
                {
                    Url = "https://api.payments.com/charge",
                    Method = "POST",
                    BodyResolver = ctx => JsonSerializer.Serialize(new
                    {
                        amount = ctx.Get<decimal>("order_total")
                    }),
                    ExpectedStatusCode = 200
                }
            },

            // Step 4: Send notifications in parallel
            new ParallelNode("notifications")
            {
                Branches = new List<List<IFlowNode>>
                {
                    new()
                    {
                        new EmailSendNode("customer_email")
                        {
                            CredentialName = "smtp_primary",
                            ToEmail = "customer@example.com",
                            Subject = "Order Confirmed",
                            HtmlBody = "<p>Your order has been confirmed.</p>"
                        }
                    },
                    new()
                    {
                        new HttpRequestNode("webhook_erp")
                        {
                            Url = "https://erp.example.com/orders/notify",
                            Method = "POST",
                            Body = "{\"status\": \"confirmed\"}"
                        }
                    }
                }
            }
        },
        FalseNodes = new List<IFlowNode>
        {
            new TransformNode("log_invalid")
            {
                TransformAction = ctx =>
                    ctx.Set("error_reason", "Order not found or invalid")
            }
        }
    })
    .StartAsync();
```
