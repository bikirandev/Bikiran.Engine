# Bikiran.Engine

[![NuGet](https://img.shields.io/nuget/v/Bikiran.Engine.svg)](https://www.nuget.org/packages/Bikiran.Engine/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)

**Bikiran.Engine** is a workflow automation engine for .NET 8 applications. It lets you chain together multiple steps — HTTP calls, emails, database queries, conditions, loops, and custom logic — into automated background **flows** that run inside your own app.

Think of it as **n8n or Zapier embedded directly inside your .NET application**, sharing your database, services, and HTTP pipeline.

---

## Features

- **Fluent Builder API** — build workflows by chaining method calls in C#
- **9 Built-in Node Types** — HTTP requests, emails, conditions, loops, retries, parallel execution, and more
- **Background Execution** — flows run asynchronously without blocking the caller
- **Persistent Logging** — every step is recorded in the database with timing and error details
- **Lifecycle Events** — run handlers on success, failure, or always after a flow completes
- **Reusable JSON Definitions** — save flow templates as JSON and trigger them without redeploying code
- **Scheduled Execution** — run flows on a cron schedule, at fixed intervals, or at a specific future time
- **Custom Nodes** — create your own node types for any business logic
- **Named Credentials** — register SMTP or API credentials once and reference them by name
- **Admin REST API** — monitor, manage, and trigger flows through built-in endpoints
- **Auto Database Migration** — schema is created and updated automatically on startup

---

## Installation

```powershell
dotnet add package Bikiran.Engine
```

---

## Quick Setup

### 1 — Register Services

In `Program.cs`:

```csharp
builder.Services.AddBikiranEngine(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("Default");
});
```

> **Note:** You must also register your EF Core database provider (e.g., `AddMySql`, `AddNpgsql`, or `AddSqlServer`) and pass it to `AddBikiranEngine`. See the [Getting Started](docs/02-getting-started.md) guide for details.

### 2 — Map Admin Endpoints

```csharp
app.MapBikiranEngineEndpoints();
```

That's it. On first startup, all required database tables are created automatically.

---

## Your First Flow

```csharp
var serviceId = await FlowBuilder
    .Create("my_first_flow")
    .AddNode(new WaitNode("pause") { DelayMs = 1000 })
    .AddNode(new HttpRequestNode("call_api") {
        Url = "https://httpbin.org/get",
        OutputKey = "api_response"
    })
    .StartAsync();
// Returns immediately — the flow runs in the background
```

---

## Built-in Node Types

| Node | Purpose |
|---|---|
| `WaitNode` | Pause execution for a set duration |
| `HttpRequestNode` | Make an outbound HTTP call with retries |
| `EmailSendNode` | Send an email via SMTP |
| `IfElseNode` | Branch on a condition |
| `WhileLoopNode` | Repeat steps while a condition is true |
| `DatabaseQueryNode<T>` | Run an EF Core query |
| `TransformNode` | Reshape or derive values from context |
| `RetryNode` | Wrap any node with retry + backoff logic |
| `ParallelNode` | Run multiple branches concurrently |

---

## Full Setup Example

```csharp
builder.Services.AddBikiranEngine(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("Default");
    options.DefaultMaxExecutionTime = TimeSpan.FromMinutes(10);
    options.EnableNodeLogging = true;

    options.AddCredential("smtp_primary", new SmtpCredential
    {
        Host = "smtp.example.com",
        Port = 587,
        Username = "noreply@example.com",
        Password = builder.Configuration["Smtp:Password"],
        UseSsl = true
    });
});

app.MapBikiranEngineEndpoints();
```

---

## Documentation

| Document | Topic |
|---|---|
| [Introduction](docs/01-introduction.md) | Overview and architecture |
| [Getting Started](docs/02-getting-started.md) | Installation, setup, and your first flow |
| [Building Flows](docs/03-building-flows.md) | FlowBuilder API and execution model |
| [Built-in Nodes](docs/04-built-in-nodes.md) | All 9 node types with examples |
| [Flow Definitions](docs/05-flow-definitions.md) | Reusable JSON flow templates |
| [Scheduling](docs/06-scheduling.md) | Cron, interval, and one-time scheduling |
| [Custom Nodes](docs/07-custom-nodes.md) | Creating your own node types |
| [Admin API](docs/08-admin-api.md) | REST endpoints for managing flows |
| [Database Reference](docs/09-database-reference.md) | Schema details |
| [Examples](docs/10-examples.md) | Practical code examples |

---

## Admin API Endpoints

All routes are under `/api/bikiran-engine/`:

**Flow Runs** — `/runs`

| Method | Route | Description |
|---|---|---|
| `GET` | `/runs` | List all runs (paginated) |
| `GET` | `/runs/{serviceId}` | Get run details with node logs |
| `GET` | `/runs/{serviceId}/progress` | Get progress percentage |
| `GET` | `/runs/status/{status}` | Filter runs by status |
| `DELETE` | `/runs/{serviceId}` | Soft-delete a run |

**Flow Definitions** — `/definitions`

| Method | Route | Description |
|---|---|---|
| `GET` | `/definitions` | List definitions (latest versions) |
| `GET` | `/definitions/{key}` | Get latest version |
| `GET` | `/definitions/{key}/versions` | List all versions |
| `POST` | `/definitions` | Create a definition |
| `PUT` | `/definitions/{key}` | Update (auto-increments version) |
| `PATCH` | `/definitions/{key}/toggle` | Enable or disable |
| `DELETE` | `/definitions/{key}` | Soft-delete |
| `POST` | `/definitions/{key}/trigger` | Trigger with parameters |
| `POST` | `/definitions/{key}/dry-run` | Validate without executing |
| `GET` | `/definitions/{key}/runs` | List runs from this definition |
| `POST` | `/definitions/validate` | Validate FlowJson |
| `PATCH` | `/definitions/{key}/versions/{ver}/activate` | Activate a specific version |
| `GET` | `/definitions/{key}/versions/diff?v1=&v2=` | Compare two versions |
| `GET` | `/definitions/{key}/export` | Export a definition |
| `GET` | `/definitions/export-all` | Export all definitions |
| `POST` | `/definitions/import` | Import a definition |
| `POST` | `/definitions/extract-parameters` | Extract `{{placeholder}}` names |

**Flow Schedules** — `/schedules`

| Method | Route | Description |
|---|---|---|
| `GET` | `/schedules` | List all schedules |
| `GET` | `/schedules/{key}` | Get details with next fire time |
| `POST` | `/schedules` | Create a schedule |
| `PUT` | `/schedules/{key}` | Update and re-register in Quartz |
| `PATCH` | `/schedules/{key}/toggle` | Enable or disable |
| `DELETE` | `/schedules/{key}` | Soft-delete and unregister |
| `POST` | `/schedules/{key}/run-now` | Trigger immediately |
| `GET` | `/schedules/{key}/runs` | List runs from this schedule |

---

## License

MIT © [Bikiran Dev](https://github.com/bikirandev)
