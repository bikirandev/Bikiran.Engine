# NuGet Package

Bikiran.Engine is distributed as a **single NuGet package** that provides the complete workflow engine — builder API, execution engine, all built-in nodes, database persistence, flow definitions, scheduling, and admin API endpoints.

---

## Package

```
Bikiran.Engine
├── Core: IFlowNode, NodeResult, FlowContext, FlowBuilder, FlowRunner
├── Nodes: Wait, HttpRequest, EmailSend, IfElse, WhileLoop, DatabaseQuery, Transform, Retry, Parallel
├── Logging: IFlowLogger, FlowDbLogger, InMemoryFlowLogger
├── Definitions: FlowDefinitionRunner, NodeDescriptorRegistry
├── Scheduling: FlowSchedulerService, FlowScheduleJob (Quartz.NET)
├── Admin API: Run, Definition, and Schedule controllers (/api/bikiran-engine/*)
├── Database: EF Core entities, configurations, and auto-migration
└── Credentials: Named credential system for external services (SMTP, etc.)
```

### Dependencies

| Dependency                                  | Purpose                        |
| ------------------------------------------- | ------------------------------ |
| `Microsoft.EntityFrameworkCore`             | Database persistence           |
| `Microsoft.Extensions.DependencyInjection`  | Service registration           |
| `Microsoft.Extensions.Logging.Abstractions` | Structured logging             |
| `Quartz.Extensions.Hosting`                 | Scheduled flow execution       |
| `Microsoft.AspNetCore.Mvc.Core`             | Admin API controller endpoints |

---

## Installation

```powershell
dotnet add package Bikiran.Engine
```

---

## Setup

### Step 1 — Register Services

In `Program.cs`, call `AddBikirianEngine()` with a database connection string. The package auto-configures the database schema on startup and applies migrations on package updates.

```csharp
builder.Services.AddBikirianEngine(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("Default");

    // Optional: register named credentials for external services
    options.AddCredential("smtp_primary", new SmtpCredential
    {
        Host = "smtp.example.com",
        Port = 587,
        Username = "noreply@example.com",
        Password = builder.Configuration["Smtp:Password"],
        UseSsl = true
    });

    options.AddCredential("smtp_transactional", new SmtpCredential
    {
        Host = "smtp.sendgrid.net",
        Port = 587,
        Username = "apikey",
        Password = builder.Configuration["SendGrid:ApiKey"],
        UseSsl = true
    });

    // Optional: configure defaults
    options.DefaultMaxExecutionTime = TimeSpan.FromMinutes(10);
    options.EnableNodeLogging = true;
});
```

### Step 2 — Map Admin Endpoints

```csharp
app.MapBikirianEngineEndpoints();   // Maps /api/bikiran-engine/* routes
```

That's it. The package handles:

- **Database schema creation** — all required tables (`FlowRun`, `FlowNodeLog`, `FlowDefinition`, `FlowDefinitionRun`, `FlowSchedule`, `EngineSchemaVersion`) are created automatically on first startup.
- **Auto-migration on update** — when you update the NuGet package, the engine detects schema version differences and applies incremental migrations at startup.
- **Quartz.NET registration** — the scheduler is configured and active schedules are loaded automatically.
- **Admin API routing** — all admin endpoints are mapped under `/api/bikiran-engine/*`.

---

## Database Auto-Configuration

The package manages its own database schema independently from the host application's EF Core migrations.

### How It Works

1. On startup, the engine checks for an `EngineSchemaVersion` table.
2. If the table doesn't exist, the engine creates all tables from scratch (initial setup).
3. If the table exists, the engine compares the stored schema version with the package's expected version.
4. If a version mismatch is found, the engine applies incremental migration scripts to bring the schema up to date.
5. The `EngineSchemaVersion` table is updated with the new version.

### Schema Version Table

| Column           | Type        | Description                                      |
| ---------------- | ----------- | ------------------------------------------------ |
| `Id`             | INT (PK)    | Always 1 (singleton row)                         |
| `SchemaVersion`  | VARCHAR(20) | Current schema version (matches package version) |
| `AppliedAt`      | BIGINT      | Unix timestamp of last migration                 |
| `PackageVersion` | VARCHAR(20) | NuGet package version that applied the migration |

### What This Means for Developers

- **No manual migrations** — you never run `dotnet ef migrations add` for engine tables.
- **No DbContext changes** — the engine uses its own internal `DbContext`; your `AppDbContext` is not touched.
- **Safe updates** — updating the NuGet package version automatically migrates the schema on next startup.
- **Connection reuse** — the engine uses the same connection string as your application.

---

## Credentials System

The named credential system allows developers to register external service credentials at startup and reference them by name in flows.

### Registering Credentials

```csharp
builder.Services.AddBikirianEngine(options =>
{
    options.ConnectionString = "...";

    // SMTP credentials
    options.AddCredential("smtp_primary", new SmtpCredential
    {
        Host = "smtp.example.com",
        Port = 587,
        Username = "noreply@example.com",
        Password = "secret",
        UseSsl = true
    });

    // Generic key-value credentials (for any external service)
    options.AddCredential("payment_gateway", new GenericCredential
    {
        Values = new()
        {
            { "ApiKey", "sk_live_abc123" },
            { "BaseUrl", "https://api.sslcommerz.com" }
        }
    });
});
```

### Using Credentials in Flows

```csharp
new EmailSendNode("send_invoice") {
    CredentialName = "smtp_primary",   // Uses the registered SMTP credential
    ToEmail = "user@example.com",
    Subject = "Invoice Ready",
    HtmlBody = "<h1>Your invoice is ready</h1>"
}
```

### Credential Types

| Type                | Properties                                                                | Used By       |
| ------------------- | ------------------------------------------------------------------------- | ------------- |
| `SmtpCredential`    | `Host`, `Port`, `Username`, `Password`, `UseSsl`, `FromEmail`, `FromName` | EmailSendNode |
| `GenericCredential` | `Values` (Dictionary\<string, string\>)                                   | Custom nodes  |

### Accessing Credentials in Custom Nodes

```csharp
var cred = context.GetCredential<GenericCredential>("payment_gateway");
var apiKey = cred.Values["ApiKey"];
```

---

## Custom Node Registration

Developers can register custom node types so they are available in JSON-based flow definitions:

```csharp
builder.Services.AddBikirianEngine(options =>
{
    options.ConnectionString = "...";

    // Register custom node types for JSON definitions
    options.RegisterNode<MyCustomNode>("MyCustom");
});
```

See [04-node-library.md](04-node-library.md) for details on creating custom nodes.

---

## Versioning

All releases follow semantic versioning:

| Change                           | Version Bump |
| -------------------------------- | ------------ |
| Breaking API change              | Major        |
| New node type or optional method | Minor        |
| Bug fix or documentation update  | Patch        |

The package version is embedded in the assembly and used by the auto-migration system to track which schema version is installed.

Publishing is handled via GitHub Actions on version tags (`v1.0.0`), pushing to `nuget.org`.

---

## Consumer Quick Start

### Minimal Setup

```csharp
// Program.cs
builder.Services.AddBikirianEngine(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("Default");
});

app.MapBikirianEngineEndpoints();
```

```csharp
// In a controller or service
var serviceId = await FlowBuilder
    .Create("my_flow")
    .AddNode(new WaitNode("pause") { DelayMs = 1000 })
    .AddNode(new HttpRequestNode("call_api") {
        Url = "https://httpbin.org/get",
        OutputKey = "api_data"
    })
    .StartAsync();
```

### Full Setup (with email + credentials + scheduling)

```csharp
builder.Services.AddBikirianEngine(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("Default");
    options.EnableNodeLogging = true;
    options.DefaultMaxExecutionTime = TimeSpan.FromMinutes(10);

    options.AddCredential("smtp_primary", new SmtpCredential
    {
        Host = "smtp.example.com",
        Port = 587,
        Username = "noreply@example.com",
        Password = builder.Configuration["Smtp:Password"],
        UseSsl = true
    });
});

app.MapBikirianEngineEndpoints();
```
