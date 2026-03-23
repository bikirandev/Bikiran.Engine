# Getting Started

This guide walks you through installing Bikiran.Engine, setting it up in your project, and running your first flow.

---

## Installation

Install the NuGet package:

```powershell
dotnet add package Bikiran.Engine
```

### Package Contents

The single package includes everything you need:

- Builder API and execution engine
- All 9 built-in node types
- Database persistence and auto-migration
- Flow definitions and scheduling (via Quartz.NET)
- Admin API endpoints
- Named credential system

### Dependencies

| Dependency                                  | Purpose                  |
| ------------------------------------------- | ------------------------ |
| `Microsoft.EntityFrameworkCore`             | Database persistence     |
| `Microsoft.Extensions.DependencyInjection`  | Service registration     |
| `Microsoft.Extensions.Logging.Abstractions` | Structured logging       |
| `Quartz.Extensions.Hosting`                 | Scheduled flow execution |
| `Microsoft.AspNetCore.Mvc.Core`             | Admin API controllers    |

---

## Setup

### Step 1 — Register Services

In your `Program.cs`, call `AddBikiranEngine()` with your database connection string:

```csharp
builder.Services.AddBikiranEngine(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("Default");
});
```

### Step 2 — Map Admin Endpoints

```csharp
app.MapBikiranEngineEndpoints();
```

That's it. On the first startup, the engine automatically creates all required database tables. When you update the NuGet package, schema changes are applied automatically — no manual migrations needed.

---

## Your First Flow

Here is a simple two-step flow that pauses for one second, then makes an HTTP call:

```csharp
var serviceId = await FlowBuilder
    .Create("my_first_flow")
    .AddNode(new WaitNode("pause") { DelayMs = 1000 })
    .AddNode(new HttpRequestNode("call_api") {
        Url = "https://httpbin.org/get",
        OutputKey = "api_response"
    })
    .StartAsync();

// serviceId is a UUID like "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
// Use it to track the run through the admin API
```

This flow runs in the background. The `serviceId` is returned immediately so your code can continue without waiting.

---

## Full Setup with Credentials

For flows that send emails or call external APIs with secrets, register named credentials at startup:

```csharp
builder.Services.AddBikiranEngine(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("Default");
    options.DefaultMaxExecutionTime = TimeSpan.FromMinutes(10);
    options.EnableNodeLogging = true;

    // Register SMTP credentials for sending emails
    options.AddCredential("smtp_primary", new SmtpCredential
    {
        Host = "smtp.example.com",
        Port = 587,
        Username = "noreply@example.com",
        Password = builder.Configuration["Smtp:Password"],
        UseSsl = true
    });

    // Register generic key-value credentials for any external service
    options.AddCredential("payment_gateway", new GenericCredential
    {
        Values = new()
        {
            { "ApiKey", "sk_live_abc123" },
            { "BaseUrl", "https://api.sslcommerz.com" }
        }
    });
});

app.MapBikiranEngineEndpoints();
```

### Credential Types

| Type                | Properties                                                                | Used By                            |
| ------------------- | ------------------------------------------------------------------------- | ---------------------------------- |
| `SmtpCredential`    | `Host`, `Port`, `Username`, `Password`, `UseSsl`, `FromEmail`, `FromName` | EmailSendNode                      |
| `GenericCredential` | `Values` (key-value dictionary)                                           | Custom nodes, any external service |

Credentials are accessed by name inside nodes:

```csharp
// In an EmailSendNode
new EmailSendNode("send_invoice") {
    CredentialName = "smtp_primary",
    ToEmail = "user@example.com",
    Subject = "Your Invoice",
    HtmlBody = "<h1>Invoice Ready</h1>"
}

// In a custom node
var cred = context.GetCredential<GenericCredential>("payment_gateway");
var apiKey = cred.Values["ApiKey"];
```

---

## What Happens Behind the Scenes

When your application starts with `AddBikiranEngine()` configured:

1. **Database tables are created** — `FlowRun`, `FlowNodeLog`, `FlowDefinition`, `FlowDefinitionRun`, `FlowSchedule`, and `FlowSchemaVersion` tables are set up automatically.
2. **Auto-migration runs** — if you update the NuGet package, the engine detects version differences and applies incremental schema changes.
3. **Quartz.NET starts** — the scheduler loads all active schedules and begins monitoring trigger times.
4. **Admin API is mapped** — all management endpoints become available under `/api/bikiran-engine/*`.

Your application's own `DbContext` and migrations are never touched. The engine manages its own tables through a separate internal `EngineDbContext`.

---

## Next Steps

- [Building Flows](03-building-flows.md) — Learn the full FlowBuilder API
- [Built-in Nodes](04-built-in-nodes.md) — See all available node types
- [Examples](10-examples.md) — Ready-to-use code patterns
