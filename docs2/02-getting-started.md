# Getting Started

This guide covers installation, setup, and running your first flow.

---

## Installation

Install the NuGet package:

```powershell
dotnet add package Bikiran.Engine
```

### Dependencies

The package brings in the following dependencies automatically:

| Package                                      | Purpose                        |
| -------------------------------------------- | ------------------------------ |
| Microsoft.EntityFrameworkCore 8.0            | Database persistence           |
| Microsoft.EntityFrameworkCore.Relational 8.0 | Relational database support    |
| Microsoft.EntityFrameworkCore.InMemory 8.0   | In-memory database for testing |
| Quartz.Extensions.Hosting 3.10               | Cron and interval scheduling   |
| MailKit 4.3                                  | SMTP email sending             |

You must also install an EF Core database provider for production use (MySQL, PostgreSQL, SQL Server, etc.).

---

## Setup

Setup requires two lines in `Program.cs`:

### Step 1 — Register Services

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

That's it. On first startup, all required database tables are created automatically.

---

## Full Setup with Database Provider

For production, provide your EF Core database provider:

```csharp
builder.Services.AddBikiranEngine(
    options =>
    {
        options.ConnectionString = builder.Configuration.GetConnectionString("Default");
        options.DefaultMaxExecutionTime = TimeSpan.FromMinutes(10);
        options.EnableNodeLogging = true;
    },
    dbOptions => dbOptions.UseMySql(
        builder.Configuration.GetConnectionString("Default"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("Default"))
    )
);

app.MapBikiranEngineEndpoints();
```

If you omit the second parameter, the engine uses an in-memory database (suitable for testing only).

---

## Configuration Options

| Option                    | Type     | Default    | Description                                                        |
| ------------------------- | -------- | ---------- | ------------------------------------------------------------------ |
| `ConnectionString`        | string   | —          | Database connection string for engine tables                       |
| `DefaultMaxExecutionTime` | TimeSpan | 10 minutes | Default maximum time a flow can run                                |
| `EnableNodeLogging`       | bool     | true       | Write per-step execution records to the database                   |
| `RequireAuthentication`   | bool     | false      | Protect admin API endpoints with authorization                     |
| `AuthorizationPolicy`     | string?  | null       | ASP.NET Core policy name (only when RequireAuthentication is true) |

---

## Registering Credentials

Register named credentials at startup so nodes can use them by name:

```csharp
builder.Services.AddBikiranEngine(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("Default");

    // SMTP credential for sending emails
    options.AddCredential("smtp_primary", new SmtpCredential
    {
        Host = "smtp.example.com",
        Port = 587,
        Username = "noreply@example.com",
        Password = builder.Configuration["Smtp:Password"],
        UseSsl = true,
        FromEmail = "noreply@example.com",
        FromName = "My App"
    });

    // Generic credential for API keys
    options.AddCredential("payment_api", new GenericCredential
    {
        Values = new()
        {
            { "ApiKey", builder.Configuration["Payment:ApiKey"] },
            { "BaseUrl", "https://api.payment.com" }
        }
    });
});
```

### Credential Types

| Type                | Properties                                                  | Purpose                                    |
| ------------------- | ----------------------------------------------------------- | ------------------------------------------ |
| `SmtpCredential`    | Host, Port, Username, Password, UseSsl, FromEmail, FromName | Email sending via SMTP                     |
| `GenericCredential` | Values (Dictionary)                                         | Any API keys or secrets as key-value pairs |

### Accessing Credentials in Nodes

```csharp
var smtp = context.GetCredential<SmtpCredential>("smtp_primary");
var api = context.GetCredential<GenericCredential>("payment_api");
var apiKey = api.Values["ApiKey"];
```

---

## Scheduling Setup

If you plan to use scheduled flows, add Quartz.NET before `AddBikiranEngine`:

```csharp
builder.Services.AddQuartz(q =>
{
    q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 10);
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

builder.Services.AddBikiranEngine(options => { /* ... */ });
```

---

## Your First Flow

Here is a minimal flow that pauses for one second, then calls an API:

```csharp
var serviceId = await FlowBuilder
    .Create("my_first_flow")
    .AddNode(new WaitNode("pause") { DelayMs = 1000 })
    .AddNode(new HttpRequestNode("call_api")
    {
        Url = "https://httpbin.org/get",
        OutputKey = "api_response"
    })
    .StartAsync();

// serviceId is a UUID like "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
// The flow runs in the background — your code continues here immediately
```

After running this, check the admin API at `GET /api/bikiran-engine/runs/{serviceId}` to see the execution details.

---

## What Happens Behind the Scenes

When you call `AddBikiranEngine()`, the engine registers:

- `EngineDbContext` — database context for engine tables
- `FlowDefinitionRunner` — loads and triggers JSON flow definitions
- `FlowDefinitionParser` — converts JSON to FlowBuilder
- `FlowJsonValidator` — validates flow JSON structure
- `SchemaMigrator` — handles automatic database schema management
- `FlowSchedulerService` — manages Quartz.NET job registrations
- `EngineStartupService` — runs on startup to apply migrations and load schedules

When the application starts, `EngineStartupService` automatically:

1. Wires the credential registry and DI container into FlowBuilder
2. Runs database schema migration (creates tables if needed, applies updates)
3. Loads all active schedules into the Quartz.NET scheduler

---

## Next Steps

- [Building Flows](03-building-flows.md) — Learn the FlowBuilder API
- [Built-in Nodes](04-built-in-nodes.md) — Explore all 9 node types
- [Examples](10-examples.md) — See practical code patterns
