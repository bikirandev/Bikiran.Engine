# Bikiran.Engine — NuGet Package Development Report

**Package:** `Bikiran.Engine`  
**Version:** `1.0.0`  
**Target Framework:** .NET 8.0  
**Date:** 2026-03-19  
**Author:** Bikiran Dev

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Project Structure](#project-structure)
4. [Components](#components)
   - [Core Layer](#core-layer)
   - [Built-in Nodes](#built-in-nodes)
   - [Credentials System](#credentials-system)
   - [Database Layer](#database-layer)
   - [Auto-Migration](#auto-migration)
   - [Flow Definitions](#flow-definitions)
   - [Scheduling](#scheduling)
   - [Admin API](#admin-api)
   - [DI Extensions](#di-extensions)
5. [Dependencies](#dependencies)
6. [How to Use](#how-to-use)
   - [Installation](#installation)
   - [Registering Services](#registering-services)
   - [Running a Flow](#running-a-flow)
   - [Custom Nodes](#custom-nodes)
7. [Security Considerations](#security-considerations)
8. [Update Log / Version History](#update-log--version-history)
9. [Known Limitations](#known-limitations)
10. [Future Roadmap](#future-roadmap)

---

## Overview

`Bikiran.Engine` is a self-contained workflow automation engine for .NET 8 applications. It provides a fluent builder API to define and execute multi-step automated flows — with built-in support for HTTP calls, email sending, database queries, conditional branching, loops, retries, parallel execution, cron scheduling, and a REST admin API — all running **inside** your existing .NET application.

The engine follows the principle of **zero separate infrastructure**: it shares the host application's dependency injection container, database connection string, HTTP pipeline, and services. It manages its own set of database tables independently, never touching the host application's `DbContext`.

---

## Architecture

```
Your .NET Application
│
├── Program.cs
│   ├── builder.Services.AddBikiranEngine(...)    ← registers all engine services
│   └── app.MapBikiranEngineEndpoints()           ← maps admin REST routes
│
├── Your Controllers / Services
│   └── FlowBuilder.Create("flow_name")
│       .Configure(...)
│       .WithContext(...)
│       .AddNode(new HttpRequestNode(...))
│       .AddNode(new EmailSendNode(...))
│       .StartAsync()                            ← returns ServiceId, runs in background
│
└── Bikiran.Engine (embedded)
    ├── Core/             FlowBuilder, FlowRunner, FlowContext, NodeResult
    ├── Nodes/            9 built-in node types
    ├── Credentials/      SmtpCredential, GenericCredential
    ├── Database/         EngineDbContext, 6 entity tables
    ├── Definitions/      JSON flow templates + FlowDefinitionRunner
    ├── Scheduling/       Quartz.NET integration + FlowSchedulerService
    ├── Api/              3 admin controllers (runs, definitions, schedules)
    └── Extensions/       AddBikiranEngine(), MapBikiranEngineEndpoints()
```

---

## Project Structure

```
Bikiran.Engine/
├── Bikiran.Engine.csproj
│
├── Core/
│   ├── IFlowNode.cs               # Contract every node implements
│   ├── NodeResult.cs              # Ok() / Fail() outcome
│   ├── FlowContext.cs             # Shared state, credentials, DI
│   ├── FlowConfig.cs              # MaxExecutionTime, OnFailure, logging
│   ├── OnFailureAction.cs         # Stop / Continue / Retry enum
│   ├── ContextMeta.cs             # HTTP request snapshot for audit
│   ├── FlowBuilder.cs             # Fluent builder + StartAsync
│   └── FlowRunner.cs              # Sequential execution engine
│
├── Nodes/
│   ├── WaitNode.cs
│   ├── HttpRequestNode.cs
│   ├── EmailSendNode.cs
│   ├── IfElseNode.cs
│   ├── WhileLoopNode.cs
│   ├── DatabaseQueryNode.cs
│   ├── TransformNode.cs
│   ├── RetryNode.cs
│   └── ParallelNode.cs
│
├── Credentials/
│   ├── IEngineCredential.cs
│   ├── SmtpCredential.cs
│   └── GenericCredential.cs
│
├── Database/
│   ├── EngineDbContext.cs
│   ├── Entities/
│   │   ├── FlowRun.cs
│   │   ├── FlowNodeLog.cs
│   │   ├── FlowDefinition.cs
│   │   ├── FlowDefinitionRun.cs
│   │   ├── FlowSchedule.cs
│   │   └── EngineSchemaVersion.cs
│   └── Migration/
│       └── SchemaMigrator.cs
│
├── Definitions/
│   ├── FlowDefinitionParser.cs
│   ├── FlowDefinitionRunner.cs
│   └── DTOs/
│       ├── FlowDefinitionSaveRequestDTO.cs
│       ├── FlowDefinitionTriggerRequestDTO.cs
│       └── FlowDefinitionTriggerResponseDTO.cs
│
├── Scheduling/
│   ├── FlowSchedulerService.cs
│   ├── ScheduledFlowJob.cs
│   └── DTOs/
│       ├── FlowScheduleSaveRequestDTO.cs
│       └── FlowScheduleSummaryDTO.cs
│
├── Api/
│   ├── FlowRunsController.cs
│   ├── FlowDefinitionsController.cs
│   └── FlowSchedulesController.cs
│
└── Extensions/
    ├── BikiranEngineOptions.cs
    ├── ServiceCollectionExtensions.cs
    ├── EngineStartupService.cs
    └── EndpointExtensions.cs
```

**Total source files:** 37  
**Namespaces:** 9 logical layers

---

## Components

### Core Layer

The core layer defines the fundamental contracts and execution model.

#### `IFlowNode`
Every node — built-in or custom — implements this interface:
```csharp
public interface IFlowNode
{
    string Name { get; }        // Unique within the flow (lowercase_underscore)
    string NodeType { get; }    // Type label (PascalCase, used in logs and JSON)
    Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken);
}
```

#### `NodeResult`
Immutable value object returned by every node:
```csharp
NodeResult.Ok(output)          // success with optional output value
NodeResult.Fail("message")     // failure with a descriptive message
```

#### `FlowContext`
Shared dictionary-based state that every node can read from and write to:
- `Set(key, value)` / `Get<T>(key)` / `Has(key)` for shared data
- `DbContext`, `HttpContext`, `Logger`, `Services` for injected services
- `GetCredential<T>(name)` for retrieving named credentials

#### `FlowConfig`
Runtime options: `MaxExecutionTime` (default 10 min), `OnFailure` (Stop/Continue/Retry), `EnableNodeLogging`, `TriggerSource`.

#### `FlowBuilder`
Fluent builder with:
- `FlowBuilder.Create(name)` — start a new builder
- `.Configure(action)` — set runtime options
- `.WithContext(action)` — inject services
- `.AddNode(node)` — add steps
- `.StartAsync()` — start in background, return ServiceId immediately
- `.StartAndWaitAsync()` — start and block until completion

#### `FlowRunner`
Internal executor that:
1. Updates `FlowRun.Status` → `"running"`
2. Runs each node in sequence with timeout protection
3. Creates/updates `FlowNodeLog` records for each step
4. Handles `OnFailure` strategy (Stop / Continue)
5. Updates final `FlowRun` status to `"completed"` or `"failed"`

---

### Built-in Nodes

All 9 built-in node types are fully implemented:

| Node | Type Label | Key Properties |
|---|---|---|
| `WaitNode` | `Wait` | `DelayMs` (default 1000 ms) |
| `HttpRequestNode` | `HttpRequest` | `Url`, `Method`, `Headers`, `Body`, `MaxRetries`, `ExpectStatusCode`, `ExpectValue`, `OutputKey` |
| `EmailSendNode` | `EmailSend` | `ToEmail`, `Subject`, `CredentialName`, `Template`, `HtmlBody`, `HtmlBodyResolver` |
| `IfElseNode` | `IfElse` | `Condition`, `TrueBranch`, `FalseBranch` |
| `WhileLoopNode` | `WhileLoop` | `Condition`, `Body`, `MaxIterations` (default 10), `IterationDelayMs` |
| `DatabaseQueryNode<T>` | `DatabaseQuery` | `Query`, `OutputKey`, `FailIfNull`, `NullErrorMessage` |
| `TransformNode` | `Transform` | `Transform`, `TransformAsync`, `OutputKey`, `SkipIfNullOutput` |
| `RetryNode` | `Retry` | `Inner`, `MaxAttempts`, `DelayMs`, `BackoffMultiplier`, `RetryOn` |
| `ParallelNode` | `Parallel` | `Branches`, `WaitAll`, `AbortOnBranchFailure` |

**Notable implementation details:**

- `HttpRequestNode` implements a full retry loop with configurable delay, and validates JSON responses using a mini expression evaluator (supporting `>=`, `<=`, `>`, `<`, `==`, `!=`, `&&`, `||` operators with `$val.field` references).
- `EmailSendNode` uses **MailKit** for SMTP, supporting HTML body, plain text body, template keys, and all body resolver delegates.
- `RetryNode` supports exponential backoff via `BackoffMultiplier` and a custom `RetryOn` predicate.
- `ParallelNode` uses `Task.WhenAll` and linked `CancellationTokenSource` for `AbortOnBranchFailure`.
- `DatabaseQueryNode<T>` is generic over the host application's `DbContext` type for type-safe queries.

---

### Credentials System

Named credentials are registered at startup via `BikiranEngineOptions.AddCredential()`:

```csharp
options.AddCredential("smtp_primary", new SmtpCredential {
    Host = "smtp.example.com",
    Port = 587,
    Username = "noreply@example.com",
    Password = "...",
    UseSsl = true
});

options.AddCredential("payment_api", new GenericCredential {
    Values = new() { { "ApiKey", "sk_live_..." }, { "BaseUrl", "https://..." } }
});
```

Credentials are accessed from within any node via `context.GetCredential<T>(name)`.

**Credential types:**

| Type | Purpose |
|---|---|
| `SmtpCredential` | SMTP settings for EmailSendNode |
| `GenericCredential` | Key-value dictionary for any external service |

---

### Database Layer

`EngineDbContext` manages 6 tables exclusively for the engine. The host application's `DbContext` is never touched.

| Table | Entity | Purpose |
|---|---|---|
| `FlowRun` | `FlowRun` | One record per workflow execution |
| `FlowNodeLog` | `FlowNodeLog` | One record per node within a run |
| `FlowDefinition` | `FlowDefinition` | Versioned JSON flow templates |
| `FlowDefinitionRun` | `FlowDefinitionRun` | Definition → run linkage |
| `FlowSchedule` | `FlowSchedule` | Cron/interval/once automated triggers |
| `EngineSchemaVersion` | `EngineSchemaVersion` | Single-row schema version tracker |

All timestamps are stored as Unix seconds (`BIGINT`). Soft-delete uses `TimeDeleted = 0` (active) or `> 0` (deleted). Unique indexes are applied to `FlowRun.ServiceId`, `(FlowDefinition.DefinitionKey, FlowDefinition.Version)`, and `FlowSchedule.ScheduleKey`.

---

### Auto-Migration

`SchemaMigrator` runs on every application startup via `EngineStartupService`:

1. Calls `EnsureCreatedAsync()` to create all tables if they don't exist.
2. Reads `EngineSchemaVersion` to check the stored schema version.
3. If no record exists, inserts version `1.0.0` (first-time setup).
4. If the version is outdated, incremental migration scripts are applied (extensible via the `ApplyMigrationsAsync` method for future versions).
5. Updates the version record.

This means you never run `dotnet ef migrations add` for engine tables and the schema is always in sync after a package update.

---

### Flow Definitions

Flow definitions allow admins to create and manage reusable flow templates as JSON — no code changes or redeployment required.

**`FlowDefinitionParser`** converts a JSON definition string into a `FlowBuilder`:
- Supports `{{placeholderKey}}` substitution in all string fields
- Builds built-in node types: `Wait`, `HttpRequest`, `EmailSend`, `Transform`
- Supports custom node types registered via `options.RegisterNode<T>(typeName)`
- SSRF protection for `HttpRequest` nodes via `allowedHosts` parameter
- Does not support expression evaluation from JSON (intentional — prevents code injection)

**`FlowDefinitionRunner`** loads the latest active version of a definition from the database, resolves parameters (including built-in date/time placeholders: `{{today_date}}`, `{{unix_now}}`, `{{year}}`, `{{month}}`), starts execution, and records a `FlowDefinitionRun` link.

**DTOs:**
- `FlowDefinitionSaveRequestDTO` — create/update definitions
- `FlowDefinitionTriggerRequestDTO` — trigger with runtime parameters
- `FlowDefinitionTriggerResponseDTO` — response with `ServiceId`, key, version

---

### Scheduling

Three schedule types are supported via Quartz.NET:

| Type | Description |
|---|---|
| `cron` | Quartz 6-field cron expression (e.g., `"0 0 8 * * ?"` for daily at 8 AM) |
| `interval` | Repeats every N minutes |
| `once` | Fires once at a specific Unix timestamp |

**`FlowSchedulerService`** handles Quartz registration:
- `InitializeAsync()` — loads all active schedules on startup and registers them with Quartz
- `RegisterScheduleAsync(schedule)` — hot-add without restart
- `UnregisterScheduleAsync(key)` — remove from Quartz
- `GetNextFireTimeAsync(key)` — returns next trigger time

**`ScheduledFlowJob`** is the Quartz `IJob` implementation:
- Decorated with `[DisallowConcurrentExecution]` to enforce `MaxConcurrent = 1`
- Loads schedule parameters, calls `FlowDefinitionRunner.TriggerAsync()`, updates `LastRunAt` / `LastRunStatus`

**Misfire handling:**
- Cron: `WithMisfireHandlingInstructionDoNothing` (skip missed fires)
- Interval: `WithMisfireHandlingInstructionNextWithRemainingCount` (continue from now)
- Once: `WithMisfireHandlingInstructionFireNow` (fire on restart if missed)

---

### Admin API

All endpoints are under `/api/bikiran-engine/` and are activated by `app.MapBikiranEngineEndpoints()`.

#### Flow Runs — `/api/bikiran-engine/runs`

| Method | Route | Description |
|---|---|---|
| `GET` | `/runs?page=1&pageSize=20` | List all runs, paginated |
| `GET` | `/runs/{serviceId}` | Full run details with node logs and progress% |
| `GET` | `/runs/{serviceId}/progress` | Current progress percentage |
| `GET` | `/runs/status/{status}` | Filter by status |
| `DELETE` | `/runs/{serviceId}` | Soft-delete a run |

#### Flow Definitions — `/api/bikiran-engine/definitions`

| Method | Route | Description |
|---|---|---|
| `GET` | `/definitions` | List all definitions (latest version per key) |
| `GET` | `/definitions/{key}` | Get latest version |
| `GET` | `/definitions/{key}/versions` | All versions |
| `POST` | `/definitions` | Create a new definition |
| `PUT` | `/definitions/{key}` | Update (auto-increments version) |
| `PATCH` | `/definitions/{key}/toggle` | Enable/disable |
| `DELETE` | `/definitions/{key}` | Soft-delete all versions |
| `POST` | `/definitions/{key}/trigger` | Trigger with parameters |
| `GET` | `/definitions/{key}/runs` | Runs from this definition |

#### Flow Schedules — `/api/bikiran-engine/schedules`

| Method | Route | Description |
|---|---|---|
| `GET` | `/schedules` | List all schedules |
| `GET` | `/schedules/{key}` | Details + next fire time |
| `POST` | `/schedules` | Create a schedule |
| `PUT` | `/schedules/{key}` | Update + re-register in Quartz |
| `PATCH` | `/schedules/{key}/toggle` | Enable/disable (pause/resume in Quartz) |
| `DELETE` | `/schedules/{key}` | Soft-delete + unregister from Quartz |
| `POST` | `/schedules/{key}/run-now` | Trigger immediately |
| `GET` | `/schedules/{key}/runs` | Runs triggered by this schedule |

---

### DI Extensions

#### `AddBikiranEngine(options, dbOptionsAction?)`

Registers all engine services:
- `EngineDbContext` (with optional `DbContextOptionsBuilder` override for production DB providers)
- `FlowDefinitionRunner` (scoped)
- `FlowDefinitionParser` (singleton — holds static custom node registry)
- `SchemaMigrator` (scoped)
- `FlowSchedulerService` (singleton)
- `ScheduledFlowJob` (scoped)
- `EngineStartupService` (hosted service — wires FlowBuilder and runs migrations)

#### `MapBikiranEngineEndpoints()`

Calls `endpoints.MapControllers()` to activate the three admin controllers.

---

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.EntityFrameworkCore` | 8.0.0 | Database persistence |
| `Microsoft.EntityFrameworkCore.Relational` | 8.0.0 | Relational database support |
| `Microsoft.EntityFrameworkCore.InMemory` | 8.0.0 | In-memory DB for testing/demos |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 8.0.0 | DI contracts |
| `Microsoft.Extensions.Logging.Abstractions` | 8.0.0 | Logging contracts |
| `Microsoft.Extensions.Hosting.Abstractions` | 8.0.0 | `IHostedService` |
| `Microsoft.AspNetCore.App` (framework ref) | 8.0 | Admin API controllers + routing |
| `Quartz.Extensions.Hosting` | 3.10.0 | Cron/interval/once scheduling |
| `MailKit` | 4.3.0 | SMTP email sending |

No known security vulnerabilities in any dependency (verified against GitHub Advisory Database before inclusion).

---

## How to Use

### Installation

```powershell
dotnet add package Bikiran.Engine
```

### Registering Services

Minimal setup (`Program.cs`):

```csharp
// Register with in-memory DB (for demos/testing)
builder.Services.AddBikiranEngine(options =>
{
    options.EnableNodeLogging = true;
});

// For production with a real DB, provide a dbOptionsAction:
builder.Services.AddBikiranEngine(
    options => { options.EnableNodeLogging = true; },
    dbOptions => dbOptions.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
);

app.MapBikiranEngineEndpoints();
```

For scheduling support, also add Quartz.NET before calling `AddBikiranEngine`:

```csharp
builder.Services.AddQuartz(q => {
    q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 10);
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
```

### Running a Flow

```csharp
var serviceId = await FlowBuilder
    .Create("order_notification_flow")
    .Configure(cfg => {
        cfg.MaxExecutionTime = TimeSpan.FromMinutes(5);
        cfg.OnFailure = OnFailureAction.Stop;
        cfg.TriggerSource = nameof(OrdersController);
    })
    .WithContext(ctx => {
        ctx.DbContext = _context;    // your app's DbContext
        ctx.HttpContext = HttpContext;
        ctx.Logger = _logger;
    })
    .AddNode(new HttpRequestNode("fetch_order") {
        Url = "https://api.example.com/order/123",
        OutputKey = "order_data",
        MaxRetries = 3
    })
    .AddNode(new IfElseNode("order_exists") {
        Condition = ctx => ctx.Has("order_data"),
        TrueBranch = [
            new EmailSendNode("notify") {
                CredentialName = "smtp_primary",
                ToEmail = "user@example.com",
                Subject = "Order Ready",
                HtmlBody = "<h1>Your order is ready!</h1>"
            }
        ],
        FalseBranch = [
            new WaitNode("wait") { DelayMs = 3000 }
        ]
    })
    .StartAsync();
```

### Custom Nodes

```csharp
// 1. Create the node class
public class InvoicePdfNode : IFlowNode
{
    public string Name { get; }
    public string NodeType => "InvoicePdf";
    public string InvoiceId { get; set; } = "";
    public string OutputKey { get; set; } = "pdf_url";

    public InvoicePdfNode(string name) => Name = name;

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        var pdfService = context.Services?.GetRequiredService<IPdfService>();
        var pdfUrl = await pdfService!.GenerateAsync(InvoiceId, ct);
        context.Set(OutputKey, pdfUrl);
        return NodeResult.Ok(pdfUrl);
    }
}

// 2. Register for JSON definitions (optional)
builder.Services.AddBikiranEngine(options => {
    options.RegisterNode<InvoicePdfNode>("InvoicePdf");
});

// 3. Use in a flow
.AddNode(new InvoicePdfNode("generate_pdf") {
    InvoiceId = "INV-2024-001",
    OutputKey = "pdf_url"
})
```

---

## Security Considerations

### Implemented Protections

1. **SSRF Protection in JSON Definitions**  
   `HttpRequest` nodes in JSON definitions support an `allowedHosts` parameter. When set, the resolved URL's host is validated against the allowed list before the request is made. If the host is not allowed, an `InvalidOperationException` is thrown with a clear error message.

2. **No Code Injection from JSON**  
   The JSON definition system intentionally does not support expression evaluation or arbitrary code execution. Conditions, loops, and database queries must use code-defined flows via `FlowBuilder`.

3. **Soft Deletes Only**  
   All destructive admin operations (delete run, delete definition, delete schedule) use soft-delete (`TimeDeleted` timestamp) to prevent accidental data loss.

4. **Cancellation Token Propagation**  
   All async operations throughout the engine accept and respect `CancellationToken`, enabling proper timeout handling and graceful shutdown.

5. **Input Validation**  
   All API controllers validate required fields and return structured `{ error: true, message: "..." }` responses on invalid input.

6. **Credential Isolation**  
   Credentials are stored only in memory (registered at startup via `BikiranEngineOptions`). They are never persisted to the database.

### Remaining Considerations for Production

- **API Authentication:** The admin endpoints (`/api/bikiran-engine/*`) have no built-in authentication. Production deployments must add authorization middleware (e.g., `[Authorize]` attributes or API gateway protection).
- **SMTP Credentials:** SMTP passwords and API keys registered via `AddCredential()` should be loaded from secrets management (environment variables, Azure Key Vault, AWS Secrets Manager) rather than hardcoded.
- **SQL Injection:** All database queries use EF Core LINQ or parameterized queries. Raw SQL string interpolation is explicitly prohibited in the documentation for `DatabaseQueryNode`.

---

## Update Log / Version History

### v1.0.0 — 2026-03-19 (Initial Release)

**Core Engine**
- `FlowBuilder` fluent API with `StartAsync()` and `StartAndWaitAsync()`
- `FlowRunner` sequential executor with timeout, per-node logging, and failure strategies
- `FlowContext` shared state with credential access and DI services
- `OnFailureAction` enum: `Stop`, `Continue`, `Retry`

**Built-in Nodes (9)**
- `WaitNode` — configurable delay
- `HttpRequestNode` — full retry loop, JSON response validation with expression evaluator
- `EmailSendNode` — MailKit SMTP, template/HTML/text bodies, resolver delegates
- `IfElseNode` — condition branching with per-branch logging
- `WhileLoopNode` — bounded iteration with `MaxIterations` guard
- `DatabaseQueryNode<T>` — generic EF Core query node
- `TransformNode` — sync and async transforms, `SkipIfNullOutput`
- `RetryNode` — configurable delay, exponential backoff, custom retry predicate
- `ParallelNode` — `Task.WhenAll` execution, `AbortOnBranchFailure` support

**Credentials**
- `SmtpCredential` for SMTP email
- `GenericCredential` for arbitrary key-value secrets

**Database (6 tables, auto-migrated)**
- `FlowRun`, `FlowNodeLog`, `FlowDefinition`, `FlowDefinitionRun`, `FlowSchedule`, `EngineSchemaVersion`
- `SchemaMigrator` with `EnsureCreatedAsync` + incremental migration hook

**Flow Definitions**
- `FlowDefinitionParser` — JSON → `FlowBuilder` with `{{placeholder}}` substitution
- `FlowDefinitionRunner` — load latest active version, built-in date/time placeholders, trigger and record

**Scheduling (Quartz.NET)**
- `FlowSchedulerService` — `InitializeAsync`, hot `RegisterScheduleAsync`, `UnregisterScheduleAsync`
- `ScheduledFlowJob` — `[DisallowConcurrentExecution]` Quartz job
- Schedule types: `cron`, `interval`, `once`
- Misfire handling for all three types
- Timezone support (IANA IDs)

**Admin API (22 endpoints across 3 controllers)**
- `FlowRunsController` — list, get, progress, filter by status, soft-delete
- `FlowDefinitionsController` — CRUD, versioning, toggle, trigger, runs list
- `FlowSchedulesController` — CRUD, toggle, run-now, runs list

**DI Extensions**
- `AddBikiranEngine(options, dbOptionsAction?)` — full service registration
- `MapBikiranEngineEndpoints()` — maps admin controllers
- `EngineStartupService` — wires FlowBuilder + runs migration on startup

---

## Known Limitations

| Limitation | Reason / Workaround |
|---|---|
| No `IfElseNode` in JSON definitions | Requires C# delegate for condition — use code-defined flows |
| No `WhileLoopNode` in JSON definitions | Same reason |
| No `DatabaseQueryNode` in JSON definitions | EF Core query delegates cannot be serialized |
| No `ParallelNode` in JSON definitions | Complex branch structure — planned for v1.1 |
| No built-in API authentication | Must be added by the host application |
| In-memory DB is default for testing | Production deployments must configure a real EF Core provider |
| `FlowContext` data dictionary is not thread-safe | `ParallelNode` branches must use unique keys |

---

## Future Roadmap

| Version | Planned Feature |
|---|---|
| **v1.1** | `ParallelNode` support in JSON definitions |
| **v1.1** | Built-in API key / Bearer token authentication for admin endpoints |
| **v1.2** | Web UI dashboard for flow monitoring |
| **v1.2** | Flow run cancellation endpoint |
| **v1.3** | Event-driven triggers (webhook, message queue) |
| **v2.0** | Distributed execution across multiple app instances |

---

*Generated as part of the Bikiran.Engine v1.0.0 NuGet package release preparation.*
