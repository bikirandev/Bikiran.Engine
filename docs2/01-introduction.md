# Introduction

Bikiran.Engine is a workflow automation engine for .NET 8. It lets you chain multiple steps — HTTP calls, emails, database queries, conditions, loops, and custom logic — into automated background processes called **flows**.

It runs inside your .NET application. There is no separate service to deploy. It shares your app's database, dependency injection, and HTTP pipeline.

---

## What Can You Build?

- Send confirmation emails after an order is placed
- Call external APIs, wait for results, and act on them
- Run database queries and branch based on the outcome
- Retry unreliable operations with exponential backoff
- Schedule daily reports or periodic health checks
- Run multiple tasks in parallel (email + webhook + logging)

---

## Features

| Feature                 | Description                                                                     |
| ----------------------- | ------------------------------------------------------------------------------- |
| Fluent Builder API      | Build workflows by chaining method calls in C#                                  |
| 9 Built-in Nodes        | HTTP requests, emails, conditions, loops, retries, parallel execution, and more |
| Background Execution    | Flows run asynchronously — your code does not wait                              |
| Persistent Logging      | Every step is recorded in the database with timing and error details            |
| Lifecycle Events        | Run handlers on success, failure, or always after a flow completes              |
| JSON Definitions        | Save flow templates as JSON and trigger them without redeploying code           |
| Scheduled Execution     | Run flows on a cron schedule, at fixed intervals, or at a specific time         |
| Custom Nodes            | Create your own node types for any business logic                               |
| Named Credentials       | Register SMTP or API credentials once and use them by name                      |
| Admin REST API          | Monitor, manage, and trigger flows through built-in endpoints                   |
| Auto Database Migration | Tables are created and updated automatically on startup                         |
| API Authentication      | Optionally protect admin endpoints with authorization policies                  |

---

## How a Flow Works

A flow is a sequence of steps. Here is what happens when you start one:

```
1. You build a flow using FlowBuilder and call StartAsync()
2. The engine creates a unique ID (ServiceId) and saves a run record
3. The flow starts in the background — your code continues immediately
4. Each step runs in order:
     → Step starts → logged as "running"
     → Step finishes → logged as "completed" or "failed"
     → Progress counter is updated
5. When all steps finish → the run is marked "completed"
6. If a step fails → the engine either stops or continues (your choice)
7. Lifecycle handlers run: OnSuccess or OnFail, then OnFinish
```

---

## Key Terms

| Term                | Meaning                                                                        |
| ------------------- | ------------------------------------------------------------------------------ |
| **Flow**            | A complete workflow made of ordered steps                                      |
| **Node**            | A single step in a flow (e.g., make an HTTP call, send an email)               |
| **FlowRun**         | One execution of a flow, identified by a unique ServiceId                      |
| **ServiceId**       | A UUID that identifies a specific flow run                                     |
| **FlowContext**     | Shared state that every node can read from and write to                        |
| **NodeLog**         | A database record capturing one node's execution details                       |
| **FlowDefinition**  | A reusable flow template stored as JSON in the database                        |
| **FlowSchedule**    | A schedule that triggers a flow automatically at defined times                 |
| **Credential**      | A named set of secrets (SMTP settings, API keys) registered at startup         |
| **Lifecycle Event** | A handler that runs after the main flow finishes (OnSuccess, OnFail, OnFinish) |

---

## Architecture

Bikiran.Engine runs **inside** your .NET application:

- No separate service or deployment
- No inter-service communication overhead
- Direct access to your app's services and database

The engine uses its own database context (`EngineDbContext`) for flow data. Your application's `DbContext` is never modified.

```
Your .NET Application
│
├── Program.cs
│   ├── builder.Services.AddBikiranEngine(...)    ← registers engine services
│   └── app.MapBikiranEngineEndpoints()           ← maps admin REST routes
│
├── Your Controllers / Services
│   └── FlowBuilder.Create("flow_name")
│       .AddNode(...)
│       .StartAsync()                             ← runs in background
│
└── Bikiran.Engine (embedded)
    ├── Core/           FlowBuilder, FlowRunner, FlowContext
    ├── Nodes/          9 built-in node types
    ├── Credentials/    SmtpCredential, GenericCredential
    ├── Database/       EngineDbContext, 6 tables (auto-migrated)
    ├── Definitions/    JSON flow templates + parser + validator
    ├── Scheduling/     Quartz.NET integration
    ├── Api/            3 admin controllers (runs, definitions, schedules)
    └── Extensions/     AddBikiranEngine(), MapBikiranEngineEndpoints()
```

---

## Documentation Guide

| Document                                       | Topic                                               |
| ---------------------------------------------- | --------------------------------------------------- |
| [Getting Started](02-getting-started.md)       | Installation, setup, and your first flow            |
| [Building Flows](03-building-flows.md)         | FlowBuilder API, configuration, and execution model |
| [Built-in Nodes](04-built-in-nodes.md)         | All 9 node types with properties and examples       |
| [Flow Definitions](05-flow-definitions.md)     | Reusable JSON flow templates                        |
| [Scheduling](06-scheduling.md)                 | Cron, interval, and one-time scheduling             |
| [Custom Nodes](07-custom-nodes.md)             | Creating your own node types                        |
| [Admin API](08-admin-api.md)                   | REST endpoints for managing flows                   |
| [Database Reference](09-database-reference.md) | Schema details and auto-migration                   |
| [Examples](10-examples.md)                     | Practical code examples                             |
