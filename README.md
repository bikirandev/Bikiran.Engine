# Bikiran.Engine

[![NuGet](https://img.shields.io/nuget/v/Bikiran.Engine.svg)](https://www.nuget.org/packages/Bikiran.Engine/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)

**Bikiran.Engine** is a workflow automation engine for .NET 8 applications. It lets you chain together multiple steps — HTTP calls, emails, database queries, conditions, loops, and custom logic — into automated background **flows** that run inside your own app.

Think of it as **n8n or Zapier embedded directly inside your .NET application**, sharing your database, services, and HTTP pipeline.

---

## Features

- **Fluent Builder API** — build workflows by chaining method calls in C#
- **11 Built-in Node Types** — start/end markers, HTTP requests, emails, conditions, loops, retries, parallel execution, and more
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
    .StartingNode("Flow is starting")
    .AddNode(new WaitNode("pause") { DelayMs = 1000 })
    .AddNode(new HttpRequestNode("call_api") {
        Url = "https://httpbin.org/get",
        OutputKey = "api_response"
    })
    .EndingNode("Flow completed successfully.")
    .StartAsync();
// Returns immediately — the flow runs in the background
```

---

## Built-in Node Types

| Node                   | Purpose                                                    |
| ---------------------- | ---------------------------------------------------------- |
| `StartingNode`         | Mark the start of a flow with an optional pause (optional) |
| `EndingNode`           | Mark the successful end of a flow (optional)               |
| `WaitNode`             | Pause execution for a set duration                         |
| `HttpRequestNode`      | Make an outbound HTTP call with retries                    |
| `EmailSendNode`        | Send an email via SMTP                                     |
| `IfElseNode`           | Branch on a condition                                      |
| `WhileLoopNode`        | Repeat steps while a condition is true                     |
| `DatabaseQueryNode<T>` | Run an EF Core query                                       |
| `TransformNode`        | Reshape or derive values from context                      |
| `RetryNode`            | Wrap any node with retry + backoff logic                   |
| `ParallelNode`         | Run multiple branches concurrently                         |

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

| Document                                            | Topic                                    |
| --------------------------------------------------- | ---------------------------------------- |
| [Introduction](docs/01-introduction.md)             | Overview and architecture                |
| [Getting Started](docs/02-getting-started.md)       | Installation, setup, and your first flow |
| [Building Flows](docs/03-building-flows.md)         | FlowBuilder API and execution model      |
| [Built-in Nodes](docs/04-node-reference.md)         | All 11 node types with examples          |
| [Flow Definitions](docs/05-flow-definitions.md)     | Reusable JSON flow templates             |
| [Scheduling](docs/06-scheduling.md)                 | Cron, interval, and one-time scheduling  |
| [Custom Nodes](docs/07-custom-nodes.md)             | Creating your own node types             |
| [Admin API](docs/08-admin-api.md)                   | REST endpoints for managing flows        |
| [Database Reference](docs/09-database-reference.md) | Schema details                           |
| [Examples](docs/10-examples.md)                     | Practical code examples                  |

---

## Admin API Endpoints

All routes are under `/api/bikiran-engine/`:

**Flow Runs** — `/runs`

| Method   | Route                        | Description                    |
| -------- | ---------------------------- | ------------------------------ |
| `GET`    | `/runs`                      | List all runs (paginated)      |
| `GET`    | `/runs/{serviceId}`          | Get run details with node logs |
| `GET`    | `/runs/{serviceId}/progress` | Get progress percentage        |
| `GET`    | `/runs/status/{status}`      | Filter runs by status          |
| `DELETE` | `/runs/{serviceId}`          | Soft-delete a run              |

**Flow Definitions** — `/definitions`

| Method   | Route                                        | Description                        |
| -------- | -------------------------------------------- | ---------------------------------- |
| `GET`    | `/definitions`                               | List definitions (latest versions) |
| `GET`    | `/definitions/{key}`                         | Get latest version                 |
| `GET`    | `/definitions/{key}/versions`                | List all versions                  |
| `POST`   | `/definitions`                               | Create a definition                |
| `PUT`    | `/definitions/{key}`                         | Update (auto-increments version)   |
| `PATCH`  | `/definitions/{key}/toggle`                  | Enable or disable                  |
| `DELETE` | `/definitions/{key}`                         | Soft-delete                        |
| `POST`   | `/definitions/{key}/trigger`                 | Trigger with parameters            |
| `POST`   | `/definitions/{key}/dry-run`                 | Validate without executing         |
| `GET`    | `/definitions/{key}/runs`                    | List runs from this definition     |
| `POST`   | `/definitions/validate`                      | Validate FlowJson                  |
| `PATCH`  | `/definitions/{key}/versions/{ver}/activate` | Activate a specific version        |
| `GET`    | `/definitions/{key}/versions/diff?v1=&v2=`   | Compare two versions               |
| `GET`    | `/definitions/{key}/export`                  | Export a definition                |
| `GET`    | `/definitions/export-all`                    | Export all definitions             |
| `POST`   | `/definitions/import`                        | Import a definition                |
| `POST`   | `/definitions/extract-parameters`            | Extract `{{placeholder}}` names    |

**Flow Schedules** — `/schedules`

| Method   | Route                      | Description                      |
| -------- | -------------------------- | -------------------------------- |
| `GET`    | `/schedules`               | List all schedules               |
| `GET`    | `/schedules/{key}`         | Get details with next fire time  |
| `POST`   | `/schedules`               | Create a schedule                |
| `PUT`    | `/schedules/{key}`         | Update and re-register in Quartz |
| `PATCH`  | `/schedules/{key}/toggle`  | Enable or disable                |
| `DELETE` | `/schedules/{key}`         | Soft-delete and unregister       |
| `POST`   | `/schedules/{key}/run-now` | Trigger immediately              |
| `GET`    | `/schedules/{key}/runs`    | List runs from this schedule     |

---

## 📄 License

MIT License - see the [LICENSE](https://github.com/bikirandev/Bikiran.Engine/blob/main/LICENSE) file for details.

## 👨‍💻 Author

**Developed by [Bikiran](https://bikiran.com/)**

- 🌐 Website: [bikiran.com](https://bikiran.com/)
- 📧 Email: [Contact](https://bikiran.com/contact)
- 🐙 GitHub: [@bikirandev](https://github.com/bikirandev)

---

<div align="center">

**Made with ❤️ for the .NET community**

[⭐ Star this repo](https://github.com/bikirandev/Bikiran.Engine) • [🐛 Report Bug](https://github.com/bikirandev/Bikiran.Engine/issues) • [💡 Request Feature](https://github.com/bikirandev/Bikiran.Engine/issues/new)

</div>

---

## 🏢 About Bikiran

**[Bikiran](https://bikiran.com/)** is a software development and cloud infrastructure company founded in 2012, headquartered in Khulna, Bangladesh. With 15,000+ clients and over a decade of experience, Bikiran builds and operates a suite of products spanning domain services, cloud hosting, app deployment, workflow automation, and developer tools.

| SL  | Topic        | Product                                                              | Description                                             |
| --- | ------------ | -------------------------------------------------------------------- | ------------------------------------------------------- |
| 1   | Website      | [Bikiran](https://bikiran.com/)                                      | Main platform — Domain, hosting & cloud services        |
| 2   | Website      | [Edusoft](https://www.edusoft.com.bd/)                               | Education management software for institutions          |
| 3   | Website      | [n8n Clouds](https://n8nclouds.com/)                                 | Managed n8n workflow automation hosting                 |
| 4   | Website      | [Timestamp Zone](https://www.timestamp.zone/)                        | Unix timestamp converter & timezone tool                |
| 5   | Website      | [PDFpi](https://pdfpi.bikiran.com/)                                  | Online PDF processing & manipulation tool               |
| 6   | Website      | [Blog](https://blog.bikiran.com/)                                    | Technical articles, guides & tutorials                  |
| 7   | Website      | [Support](https://support.bikiran.com/)                              | 24/7 customer support portal                            |
| 8   | Website      | [Probackup](https://probackup.bikiran.com/)                          | Automated database backup for SQL, PostgreSQL & MongoDB |
| 9   | Service      | [Domain](https://www.bikiran.com/domain)                             | Domain registration, transfer & DNS management          |
| 10  | Service      | [Hosting](https://www.bikiran.com/services/hosting/web)              | Web, app & email hosting on NVMe SSD                    |
| 11  | Service      | Email & SMS                                                          | Bulk email & SMS notification service                   |
| 12  | npm          | [Chronopick](https://www.npmjs.com/package/@bikiran/chronopick)      | Date & time picker React component                      |
| 13  | npm          | [Rich Editor](https://www.npmjs.com/package/@bikiran/editor)         | WYSIWYG rich text editor for React                      |
| 14  | npm          | [Button](https://www.npmjs.com/package/@bikiran/button)              | Reusable React button component library                 |
| 15  | npm          | [Electron Boilerplate](https://www.npmjs.com/package/create-edx-app) | CLI to scaffold Electron.js project templates           |
| 16  | NuGet        | [Bkash](https://www.nuget.org/packages/Bikiran.Payment.Bkash)        | bKash payment gateway integration for .NET              |
| 17  | NuGet        | [Bikiran Engine](https://www.nuget.org/packages/Bikiran.Engine)      | Core .NET engine library for Bikiran services           |
| 18  | Open Source  | [PDFpi](https://github.com/bikirandev/pdfpi)                         | PDF processing tool — open source                       |
| 19  | Open Source  | [Bikiran Engine](https://github.com/bikirandev/Bikiran.Engine)       | Core .NET engine — open source                          |
| 20  | Open Source  | [Drive CLI](https://github.com/bikirandev/DriveCLI)                  | CLI tool to manage Google Drive from terminal           |
| 21  | Docker       | [Pgsql](https://github.com/bikirandev/docker-pgsql)                  | Docker setup for PostgreSQL                             |
| 22  | Docker       | [n8n](https://github.com/bikirandev/docker-n8n)                      | Docker setup for n8n automation                         |
| 23  | Docker       | [Pgadmin](https://github.com/bikirandev/docker-pgadmin)              | Docker setup for pgAdmin                                |
| 24  | Social Media | [LinkedIn](https://www.linkedin.com/company/bikiran12)               | Bikiran on LinkedIn                                     |
| 25  | Social Media | [Facebook](https://www.facebook.com/bikiran12)                       | Bikiran on Facebook                                     |
| 26  | Social Media | [YouTube](https://www.youtube.com/@bikiranofficial)                  | Bikiran on YouTube                                      |
| 27  | Social Media | [FB n8nClouds](https://www.facebook.com/n8nclouds)                   | n8n Clouds on Facebook                                  |
