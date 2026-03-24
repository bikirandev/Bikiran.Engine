# Introduction

Bikiran.Engine is a workflow automation engine for .NET applications. It lets you chain together multiple steps — such as HTTP calls, emails, database queries, and custom logic — into a single automated process called a **flow**. Flows run in the background, track progress in the database, and can be monitored through built-in admin endpoints.

Think of it as having tools like n8n or Zapier embedded directly inside your .NET application, sharing the same database, services, and HTTP pipeline.

---

## Key Features

| Feature                     | Description                                                                          |
| --------------------------- | ------------------------------------------------------------------------------------ |
| **Fluent Builder API**      | Build workflows by chaining method calls in C#                                       |
| **9 Built-in Node Types**   | HTTP requests, emails, conditions, loops, retries, parallel execution, and more      |
| **Background Execution**    | Flows run asynchronously without blocking the calling request                        |
| **Persistent Logging**      | Every step is recorded in the database with timing, input, output, and error details |
| **Branching and Loops**     | Use conditions to take different paths, or repeat steps until a goal is met          |
| **Failure Handling**        | Choose to stop or continue when a step fails; use RetryNode for retry logic |
| **Lifecycle Events**        | Run handlers on success, failure, or always after a flow completes                   |
| **Reusable Definitions**    | Save flow templates as JSON in the database and trigger them without code changes    |
| **Scheduled Execution**     | Run flows on a cron schedule, at fixed intervals, or at a specific future time       |
| **Custom Nodes**            | Create your own node types for any business logic                                    |
| **Named Credentials**       | Register SMTP or API credentials once and reference them by name in any flow         |
| **Admin API**               | Monitor, manage, and trigger flows through REST endpoints                            |
| **Auto Database Migration** | Database tables are created and updated automatically — no manual migrations needed  |

---

## How a Flow Works

A flow is a sequence of steps that execute one after another. Here is what happens when you start a flow:

```
1. You build a flow using FlowBuilder and call StartAsync()
2. The engine generates a unique ID (ServiceId) and saves a run record to the database
3. The flow starts running in the background — your code continues without waiting
4. Each step runs in order:
     → Step starts → logged as "running"
     → Step finishes → logged as "completed" or "failed"
     → Progress counter is updated
5. When all steps finish → the run is marked "completed"
6. If a step fails → behavior depends on your failure setting (Stop / Continue)
   For retry behavior, wrap individual nodes with RetryNode.
```

---

## Terminology

| Term               | Meaning                                                                      |
| ------------------ | ---------------------------------------------------------------------------- |
| **Flow**           | A complete workflow made up of ordered steps                                 |
| **Node**           | A single step in a flow (e.g., make an HTTP call, send an email, wait)       |
| **FlowRun**        | One execution of a flow, identified by a unique ServiceId                    |
| **ServiceId**      | A UUID that uniquely identifies a specific flow run                          |
| **FlowContext**    | Shared state that every node can read from and write to                      |
| **NodeLog**        | A database record capturing one node's execution details                     |
| **Branch**         | A conditional path within an IfElse node                                     |
| **FlowDefinition** | A reusable flow template stored as JSON in the database                      |
| **FlowSchedule**   | A schedule that triggers a flow automatically at defined times               |
| **Credential**     | A named set of secrets (e.g., SMTP settings, API keys) registered at startup |

---

## Architecture at a Glance

Bikiran.Engine runs **inside** your .NET application — it is not a separate service. It shares your app's dependency injection container, database connection, and HTTP pipeline. This means:

- No inter-service communication overhead
- No separate deployment or infrastructure
- Direct access to your app's services and database

The engine uses its own internal database context (`EngineDbContext`) for storing flow-related data. Your application's `DbContext` is never modified — the engine manages its own tables independently.

---

## What's in This Documentation

| Document                                             | Topic                                                                |
| ---------------------------------------------------- | -------------------------------------------------------------------- |
| [02-getting-started.md](02-getting-started.md)       | Installation, setup, and your first flow                             |
| [03-building-flows.md](03-building-flows.md)         | FlowBuilder API, configuration, and execution model                  |
| [04-built-in-nodes.md](04-built-in-nodes.md)         | Reference for all 9 built-in node types                              |
| [05-flow-definitions.md](05-flow-definitions.md)     | Reusable JSON flow templates                                         |
| [06-scheduling.md](06-scheduling.md)                 | Automated flow execution with cron, intervals, and one-time triggers |
| [07-custom-nodes.md](07-custom-nodes.md)             | How to create your own node types                                    |
| [08-admin-api.md](08-admin-api.md)                   | REST endpoints for managing flows                                    |
| [09-database-reference.md](09-database-reference.md) | Database tables and schema details                                   |
| [10-examples.md](10-examples.md)                     | Practical, ready-to-use code examples                                |
