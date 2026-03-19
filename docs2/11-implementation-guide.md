# Implementation Guide

This document provides a step-by-step guide for building Bikiran.Engine, organized by phase. Each step is self-contained and testable before moving on.

> **Path convention:** Phases 1–4 use development-time paths (`Services/FlowRunner/`, `Tables/`, `Controllers/`). In Phase 5, all code is reorganized into the final NuGet package structure (`Core/`, `Nodes/`, `Builder/`, `Database/`, etc.). See the **File Structure Summary** at the end for the final layout.

---

## Phase 1: Core Engine

### Step 1 — Create Database Tables

**Create:**

- `Tables/FlowRun.cs`
- `Tables/FlowNodeLog.cs`
- `Database/EngineDbContext.cs` — the engine's own internal `DbContext` for engine tables only

> **Note:** The engine uses its own `EngineDbContext`, separate from the host application's `AppDbContext`. The host's `AppDbContext` is never modified.

**Verify:** `dotnet build` — no errors.

### Step 2 — Run Initial Schema Setup

During development, use EF Core migrations on `EngineDbContext`:

```powershell
dotnet ef migrations add InitEngineSchema --context EngineDbContext
dotnet ef database update --context EngineDbContext
```

> **Note:** In the final NuGet package (Phase 5), EF migrations are replaced by the auto-migration system. Consumers never run `dotnet ef migrations add` for engine tables.

### Step 3 — Create Core Abstractions

Create in this order inside `Services/FlowRunner/Core/`:

1. `FlowRunStatusEnum.cs`
2. `NodeResult.cs`
3. `IFlowNode.cs`
4. `FlowContext.cs`
5. `FlowRunConfig.cs`
6. `IFlowLogger.cs`
7. `FlowAbortException.cs`

**Verify:** `dotnet build` — no errors.

### Step 4 — Implement Nodes

Create each node in `Services/FlowRunner/Nodes/` in this order:

1. **WaitNode** — simplest, no dependencies
2. **HttpRequestNode** — no app context needed
3. **EmailSendNode** — uses credentials system for SMTP
4. **IfElseNode** — uses recursive node execution
5. **WhileLoopNode** — uses loop management + recursive execution

**Verify:** After each node, instantiate it and call `ExecuteAsync` in a test endpoint.

### Step 5 — Implement FlowDbLogger

**Create:** `Services/FlowRunner/FlowDbLogger.cs`

Implements `IFlowLogger` using `EngineDbContext` (the engine's own internal context). This writes to the engine's `FlowRun` and `FlowNodeLog` tables.

### Step 6 — Implement FlowRunner (Execution Engine)

**Create:** `Services/FlowRunner/FlowRunner.cs`

This is the core engine. Key responsibilities:

- Generate `ServiceId` using `Guid.NewGuid().ToString()`
- Persist initial `FlowRun` record via `IFlowLogger`
- Fire `Task.Run(ExecuteFlowAsync)` for background execution
- Handle per-node logging, error handling, and timeout via `CancellationTokenSource`

**Verify:** Build a minimal 2-node flow and confirm `FlowRun` + `FlowNodeLog` rows are created.

### Step 7 — Implement FlowBuilder

**Create:** `Services/FlowRunner/FlowBuilder.cs`

The fluent API entry point. Accumulates configuration, context, and nodes, then delegates to `FlowRunner`.

**Verify:** Full end-to-end: build a 3-node flow, call `StartAsync()`, confirm `ServiceId` is returned and database records exist.

### Step 8 — Create Admin Controller

**Create:** `Controllers/BikiranEngineController.cs`

Implement the five admin endpoints (list runs, get run detail, get progress, filter by status, soft-delete). All routes under `/api/bikiran-engine/runs`.

**Verify:** Hit endpoints via Swagger.

### Step 9 — Register Services in Program.cs

Register the engine's internal services. During development (before NuGet extraction in Phase 5), register manually:

```csharp
builder.Services.AddHttpClient();
builder.Services.AddDbContext<EngineDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
builder.Services.AddScoped<IFlowLogger, FlowDbLogger>();
```

> **Note:** In Phase 5, all of this is encapsulated inside `AddBikiranEngine()`.

### Step 10 — Integration Test

Create a `TestController` endpoint that builds and runs a full flow, then check the admin API to confirm records.

---

## Phase 2: Enhanced Nodes

### Step 1 — TransformNode

Implement first — no external dependencies, pure context manipulation.

**Verify:** Set a string in context, transform it, read it back.

### Step 2 — RetryNode

Wrap an existing node with configurable retry logic.

**Verify:** Wrap an `HttpRequestNode` pointing to a failing URL with `MaxAttempts=3`. Observe retry behavior in node logs.

### Step 3 — DatabaseQueryNode

Generic over `TContext : DbContext`. Uses the host app's `DbContext` from `FlowContext.DbContext`. Test by querying a known record.

**Verify:** Query a `UserProfile` row and confirm the `OutputKey` is populated.

### Step 4 — ParallelNode

Implement after all other nodes are stable. Test with 2 branches: one `WaitNode` + one `HttpRequestNode`.

**Verify:** Both branches execute concurrently. Total duration should be close to the longest branch, not the sum.

---

## Phase 3: Flow Definitions in Database

### Step 1 — Create DB Tables

Create `Tables/FlowDefinition.cs` and `Tables/FlowDefinitionRun.cs`. Add `DbSet` entries to `EngineDbContext`. Run EF migration on `EngineDbContext`.

### Step 2 — Create JSON Models

**Create:** `Services/FlowRunner/FlowDefinitionRunner/FlowDefinitionJson.cs`

Define `FlowDefinitionJson`, `FlowDefinitionConfigJson` classes.

### Step 3 — Create NodeDescriptor

**Create:** `Services/FlowRunner/FlowDefinitionRunner/NodeDescriptor.cs`

Include helper methods: `GetString()`, `GetInt()`, `GetBool()`, `GetDict()`, `GetEnum<T>()`, `GetStringList()`.

### Step 4 — Create NodeDescriptorRegistry

**Create:** `Services/FlowRunner/FlowDefinitionRunner/NodeDescriptorRegistry.cs`

Map type strings to `IFlowNode` factory functions. Start with: `Wait`, `HttpRequest`, `EmailSend`, `Transform`.

### Step 5 — Create FlowDefinitionRunner

**Create:** `Services/FlowRunner/FlowDefinitionRunner/FlowDefinitionRunner.cs`

Implements `TriggerAsync()`: load definition → interpolate parameters → deserialize JSON → build nodes → delegate to FlowRunner → persist FlowDefinitionRun record.

### Step 6 — Create Admin CRUD Controller

**Create:** `Controllers/BikiranEngineDefinitionController.cs`

All 9 endpoints. Validate JSON on create/update by deserializing before saving. Routes under `/api/bikiran-engine/definitions`.

### Step 7 — Integration Test

1. Create a definition via POST.
2. Trigger it with parameters.
3. Poll the run to confirm it executed.
4. Update the definition and verify the version incremented.

---

## Phase 4: Scheduling

### Step 1 — Create FlowSchedule Table

Create `Tables/FlowSchedule.cs`. Add `DbSet` to `EngineDbContext`. Run migration on `EngineDbContext`.

### Step 2 — Create FlowScheduleJob

**Create:** `Services/FlowRunner/Scheduling/FlowScheduleJob.cs`

Quartz `IJob` implementation. Must use scoped service provider for `EngineDbContext`.

### Step 3 — Create FlowSchedulerService

**Create:** `Services/FlowRunner/Scheduling/FlowSchedulerService.cs`

Implements `InitializeAsync()`, `RegisterScheduleAsync()`, `UnregisterScheduleAsync()`, `GetNextFireTimeAsync()`.

### Step 4 — Register Quartz in Program.cs

Register Quartz services, `FlowSchedulerService`, and wire up `ApplicationStarted` to call `InitializeAsync()`.

**Verify:** App starts without errors. Check log output for schedule registration.

### Step 5 — Create Admin Controller

**Create:** `Controllers/BikiranEngineScheduleController.cs`

8 endpoints. On create/update: save to DB then register in Quartz. On toggle: update `IsActive` and register/unregister. On delete: soft-delete and unregister. Routes under `/api/bikiran-engine/schedules`.

### Step 6 — Integration Test

1. Create a `FlowDefinition`.
2. Create a schedule with `interval` type, 1-minute interval.
3. Wait and verify a `FlowRun` row appears.
4. Disable the schedule and confirm no new runs appear.

---

## Phase 5: NuGet Package

### Step 1 — Create Package Solution

```powershell
mkdir Bikiran.Engine
cd Bikiran.Engine
dotnet new sln -n Bikiran.Engine
dotnet new classlib -n Bikiran.Engine --framework net9.0
dotnet sln add src/Bikiran.Engine
```

### Step 2 — Organize Package Structure

Organize all engine code into the package project with a clean folder structure:

- `Core/` — `IFlowNode`, `NodeResult`, `FlowContext`, `FlowRunConfig`, `IFlowLogger`
- `Nodes/` — All built-in nodes
- `Builder/` — `FlowBuilder`, `FlowRunner`
- `Logging/` — `FlowDbLogger`, `InMemoryFlowLogger`
- `Database/` — `EngineDbContext`, `AutoMigrationRunner`
- `Database/Entities/` — All EF Core entity classes (`FlowRun`, `FlowNodeLog`, etc.)
- `Definitions/` — `FlowDefinitionRunner`, `NodeDescriptorRegistry`
- `Scheduling/` — `FlowScheduleJob`, `FlowSchedulerService`
- `Credentials/` — `SmtpCredential`, `GenericCredential`, `CredentialStore`
- `Api/` — Admin API controllers (`/api/bikiran-engine/*`)
- `Configuration/` — `BikiranEngineOptions`, `ServiceCollectionExtensions`
- `Models/` — DTO classes

### Step 3 — Implement Auto-Migration System

Create the `EngineSchemaVersion` table and migration runner. On startup:

1. Check if `EngineSchemaVersion` table exists.
2. If not → run all SQL scripts to create tables from scratch.
3. If yes → compare stored version with package version, apply incremental migrations.
4. Update the `EngineSchemaVersion` record.

### Step 4 — Implement Credentials System

Create `BikiranEngineOptions.AddCredential()` and the credential store. Implement `FlowContext.GetCredential<T>()`. Update `EmailSendNode` to resolve SMTP credentials by name.

### Step 5 — Implement Custom Node Registration

Create `BikiranEngineOptions.RegisterNode<T>()` that adds custom types to `NodeDescriptorRegistry`.

### Step 6 — Create Extension Methods

```csharp
public static IServiceCollection AddBikiranEngine(
    this IServiceCollection services,
    Action<BikiranEngineOptions> configure);

public static IEndpointRouteBuilder MapBikiranEngineEndpoints(
    this IEndpointRouteBuilder endpoints);
```

### Step 7 — Write Unit Tests

Create `tests/Bikiran.Engine.Tests/`. Test:

- WaitNode delay behavior
- HttpRequestNode retry logic and `ExpectValue` validation
- IfElseNode branch selection
- WhileLoopNode iteration limit
- FlowBuilder end-to-end
- InMemoryFlowLogger record collection
- Custom node registration
- Credential resolution

### Step 8 — Local Pack and Test

```powershell
dotnet pack --configuration Release --output ./nupkgs
```

Add a local NuGet source in a test app, install the package, and verify all flows work.

### Step 9 — Publish to NuGet

Set up the GitHub repository and CI/CD. Tag `v1.0.0` to trigger the first publish.

---

## File Structure Summary

```
Bikiran.Engine/
├── Core/
│   ├── IFlowNode.cs
│   ├── NodeResult.cs
│   ├── FlowContext.cs
│   ├── FlowRunConfig.cs
│   ├── FlowRunStatusEnum.cs
│   ├── IFlowLogger.cs
│   └── FlowAbortException.cs
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
├── Builder/
│   ├── FlowBuilder.cs
│   └── FlowRunner.cs
├── Logging/
│   ├── FlowDbLogger.cs
│   └── InMemoryFlowLogger.cs
├── Credentials/
│   ├── SmtpCredential.cs
│   ├── GenericCredential.cs
│   └── CredentialStore.cs
├── Definitions/
│   ├── FlowDefinitionRunner.cs
│   ├── NodeDescriptorRegistry.cs
│   ├── NodeDescriptor.cs
│   └── FlowDefinitionJson.cs
├── Scheduling/
│   ├── FlowScheduleJob.cs
│   └── FlowSchedulerService.cs
├── Database/
│   ├── EngineDbContext.cs
│   ├── AutoMigrationRunner.cs
│   └── Entities/
│       ├── FlowRun.cs
│       ├── FlowNodeLog.cs
│       ├── FlowDefinition.cs
│       ├── FlowDefinitionRun.cs
│       ├── FlowSchedule.cs
│       └── EngineSchemaVersion.cs
├── Api/
│   ├── BikiranEngineController.cs
│   ├── BikiranEngineDefinitionController.cs
│   └── BikiranEngineScheduleController.cs
├── Configuration/
│   ├── BikiranEngineOptions.cs
│   └── ServiceCollectionExtensions.cs
└── Models/
    ├── FlowDefinitionDTOs.cs
    └── FlowScheduleDTOs.cs
```
