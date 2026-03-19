# Bikiran.Engine — Overview

Bikiran.Engine is a workflow automation engine built for .NET applications. It allows developers to define multi-step automated processes (called **flows**) using a simple builder pattern in C#. Think of it as an embedded version of tools like n8n or Zapier — but running directly inside your application, sharing the same database, dependency injection, and HTTP context.

---

## What It Does

- **Automates multi-step processes** — chain together HTTP calls, emails, SMS messages, database queries, and custom logic into a single executable flow.
- **Runs in the background** — flows execute asynchronously without blocking the calling request.
- **Tracks everything** — every step is logged to the database with timing, input/output, and error details.
- **Supports branching and loops** — use conditions to take different paths, or repeat steps until a condition is met.
- **Configurable failure handling** — choose to stop, continue, or retry when a step fails.
- **Admin-friendly** — view, monitor, and manage flows through admin API endpoints.

---

## Key Capabilities

| Capability               | Description                                                                                                |
| ------------------------ | ---------------------------------------------------------------------------------------------------------- |
| Fluent Builder API       | Build workflows by chaining `.AddNode(...)` calls                                                          |
| Node Library             | HTTP requests, conditional logic, loops, waits, email, SMS, push notifications, database queries, and more |
| Background Execution     | Flows run via `Task.Run` without blocking the HTTP request                                                 |
| Persistent Logging       | Every node execution is recorded in the database                                                           |
| Unique Run Tracking      | Each run gets a 32-character `ServiceId` for tracing                                                       |
| DB-Stored Definitions    | Save reusable flow templates as JSON in the database                                                       |
| Scheduled Execution      | Trigger flows on a cron schedule, fixed interval, or one-time future date                                  |
| NuGet-Ready Architecture | Designed to be extractable into standalone NuGet packages                                                  |

---

## Core Terminology

| Term               | Meaning                                                                     |
| ------------------ | --------------------------------------------------------------------------- |
| **Flow**           | A complete workflow made up of ordered steps (nodes)                        |
| **Node**           | A single unit of work — e.g., make an HTTP call, send an email, wait        |
| **FlowRun**        | One execution of a flow, identified by a unique `ServiceId`                 |
| **NodeLog**        | A database record capturing one node's execution within a FlowRun           |
| **FlowContext**    | Shared state passed to every node — carries variables and injected services |
| **ServiceId**      | A 32-character unique string that identifies a specific flow run            |
| **Branch**         | A conditional sub-list of nodes (used in `IfElseNode`)                      |
| **FlowDefinition** | A reusable flow template stored as JSON in the database                     |
| **FlowSchedule**   | A schedule record that triggers a flow automatically at defined times       |

---

## Development Phases

The engine is built incrementally across five phases:

| Phase       | Focus                  | Summary                                                                                                                |
| ----------- | ---------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| **Phase 1** | Core Engine            | Builder API, execution engine, basic nodes (HTTP, Wait, Email, If-Else, While Loop), database logging, admin endpoints |
| **Phase 2** | Enhanced Nodes         | Database queries, data transforms, retry wrappers, parallel execution, SMS, Firebase push notifications                |
| **Phase 3** | Flow Definitions in DB | Store reusable flow templates as JSON, trigger flows via admin API or code without redeployment                        |
| **Phase 4** | Scheduling             | Cron, interval, and one-time scheduled execution using Quartz.NET                                                      |
| **Phase 5** | NuGet Extraction       | Split the engine into modular NuGet packages for use in any .NET 9 application                                         |

---

## How a Flow Runs (High Level)

```
1. Developer builds a flow using FlowBuilder
2. FlowBuilder.StartAsync() is called
3. A unique ServiceId is generated and a FlowRun record is saved (status: pending)
4. The flow fires in the background via Task.Run
5. Each node executes in sequence:
     → Node starts → logged as "running"
     → Node completes → logged as "completed" (or "failed")
     → Progress counter is updated on the FlowRun
6. When all nodes finish → FlowRun is marked "completed"
7. If any node fails → behavior depends on the failure strategy (Stop / Continue / Retry)
```

---

## Quick Example

```csharp
var serviceId = await FlowBuilder
    .Create("order_notification")
    .Configure(cfg => {
        cfg.MaxExecutionTime = TimeSpan.FromMinutes(5);
        cfg.OnFailure = OnFailureAction.Stop;
    })
    .WithContext(ctx => {
        ctx.DbContext = _context;
        ctx.HttpContext = HttpContext;
        ctx.EmailSender = _emailSender;
    })
    .AddNode(new HttpRequestNode("fetch_order") {
        Url = "https://api.example.com/order/123",
        Method = HttpMethod.Get,
        OutputKey = "order_data"
    })
    .AddNode(new EmailSendNode("notify_customer") {
        ToEmail = "customer@example.com",
        Subject = "Order Confirmed",
        Template = "ORDER_CREATE"
    })
    .StartAsync();

// serviceId = "a1b2c3d4..." — use this to track the run
```

This creates a two-step flow that fetches order data via HTTP, then sends a confirmation email — all running in the background while the API response returns immediately.
