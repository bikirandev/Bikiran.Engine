# Introduction

Bikiran.Engine is a workflow automation library for .NET 8 applications. It allows you to combine multiple steps — like making API calls, sending emails, querying databases, and running custom logic — into a single automated process called a **flow**. Flows run in the background, log every step to the database, and can be monitored through built-in REST endpoints.

Think of it as having a tool like n8n or Zapier built right into your .NET application, sharing the same database, services, and HTTP pipeline.

---

## What Can It Do?

| Capability                | Description                                                                         |
| ------------------------- | ----------------------------------------------------------------------------------- |
| Build workflows in C#     | Chain steps together using a simple, readable builder pattern                       |
| 9 ready-to-use step types | HTTP calls, emails, conditions, loops, retries, parallel tasks, and more            |
| Run in the background     | Flows execute without blocking the calling code                                     |
| Log everything            | Every step is saved to the database with timing, inputs, outputs, and errors        |
| Branch and loop           | Use conditions to take different paths, or repeat steps until a goal is met         |
| Handle failures           | Choose to stop or continue when a step fails; add retry logic to any step           |
| React to outcomes         | Run follow-up actions on success, failure, or always after a flow finishes          |
| Save reusable templates   | Store flow blueprints as JSON in the database and trigger them without code changes |
| Schedule flows            | Run flows on a cron schedule, at regular intervals, or at a specific future time    |
| Create custom steps       | Build your own step types for any business logic                                    |
| Manage credentials        | Register SMTP or API keys once and use them by name in any flow                     |
| Monitor via REST API      | View, manage, and trigger flows through built-in admin endpoints                    |
| Auto-manage database      | All required tables are created and updated automatically — no manual migrations    |

---

## How a Flow Works

A flow is an ordered series of steps. Here is what happens when you start one:

1. You build a flow using `FlowBuilder` and call `StartAsync()`.
2. The engine creates a unique ID (called a **ServiceId**) and saves a run record to the database.
3. The flow begins running in the background — your code continues immediately.
4. Each step runs one after another:
   - The step starts and is logged as "running."
   - The step finishes and is logged as "completed" or "failed."
   - A progress counter is updated.
5. When all steps finish, the run is marked "completed."
6. If a step fails, behavior depends on your setting — either stop the entire flow or skip the failed step and move on.

---

## Key Terms

| Term               | Meaning                                                                          |
| ------------------ | -------------------------------------------------------------------------------- |
| **Flow**           | A complete workflow made up of ordered steps                                     |
| **Node**           | A single step in a flow (for example: make an HTTP call, send an email, pause)   |
| **FlowRun**        | One execution of a flow, identified by a unique ServiceId                        |
| **ServiceId**      | A UUID that uniquely identifies a specific flow run                              |
| **FlowContext**    | Shared data that every step can read from and write to                           |
| **NodeLog**        | A database record capturing one step's execution details                         |
| **FlowDefinition** | A reusable flow template stored as JSON in the database                          |
| **FlowSchedule**   | A rule that triggers a flow automatically at defined times                       |
| **Credential**     | A named set of secrets (such as SMTP settings or API keys) registered at startup |

---

## How It Fits Into Your Application

Bikiran.Engine runs **inside** your .NET application — it is not a separate service. It shares your app's dependency injection container, database connection, and HTTP pipeline. This means:

- No communication overhead between separate services
- No separate deployment or infrastructure to manage
- Direct access to your app's services and database

The engine uses its own internal database context for storing flow-related data. Your application's database context is never modified — the engine manages its own tables independently.

---

## Documentation Overview

| Document                                       | Topic                                                                |
| ---------------------------------------------- | -------------------------------------------------------------------- |
| [Getting Started](02-getting-started.md)       | Installation, setup, and your first flow                             |
| [Building Flows](03-building-flows.md)         | The FlowBuilder API, configuration, and how flows execute            |
| [Node Reference](04-node-reference.md)         | Details on all 9 built-in step types                                 |
| [Flow Definitions](05-flow-definitions.md)     | Reusable JSON flow templates stored in the database                  |
| [Scheduling](06-scheduling.md)                 | Automated flow execution with cron, intervals, and one-time triggers |
| [Custom Nodes](07-custom-nodes.md)             | How to create your own step types                                    |
| [Admin API](08-admin-api.md)                   | REST endpoints for managing flows, definitions, and schedules        |
| [Database Reference](09-database-reference.md) | Database tables and schema details                                   |
| [Examples](10-examples.md)                     | Practical, ready-to-use code examples                                |
