# FlowRunner — Phase 5: NuGet Package Extraction

> **Status:** Planning  
> **Depends on:** All Phases 1–4 fully implemented and stable in-app  
> **Goal:** Extract the FlowRunner engine into a reusable, independently-versioned NuGet package. Enable any .NET 9 application to embed FlowRunner without depending on the Bikiran web application.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Package Architecture](#2-package-architecture)
3. [Package 1: `Bikiran.FlowRunner.Core`](#3-package-1-bikiranflowrunnercore)
4. [Package 2: `Bikiran.FlowRunner.EfCore`](#4-package-2-bikiranflowrunnerefcore)
5. [Package 3: `Bikiran.FlowRunner.Email`](#5-package-3-bikiranflowrunneremail)
6. [Package 4: `Bikiran.FlowRunner.Firebase`](#6-package-4-bikiranflowrunnerfirebase)
7. [Package 5: `Bikiran.FlowRunner.Scheduling`](#7-package-5-bikiranflowrunnerscheduling)
8. [Refactoring the In-App Code](#8-refactoring-the-in-app-code)
9. [DI Registration API](#9-di-registration-api)
10. [Versioning & Publishing Strategy](#10-versioning--publishing-strategy)
11. [File & Folder Structure (Solution-Level)](#11-file--folder-structure-solution-level)
12. [Step-by-Step Implementation Guide](#12-step-by-step-implementation-guide)
13. [Integration Guide for Consumers](#13-integration-guide-for-consumers)

---

## 1. Overview

After running FlowRunner in production across Phases 1–4, the codebase will be stable enough to extract as a reusable NuGet package family. The goal is to make FlowRunner consumable by any Bikiran or partner .NET application that needs workflow automation.

### Extraction Principles

1. **No breaking changes** — the in-app code continues to work by pulling from the NuGet packages it previously implemented directly. The switch is transparent to callers.
2. **Layered opt-in** — a consumer application installs only the packages it needs. A minimal deployment needs only `Core`.
3. **App-specific code stays in-app** — business logic (`DatabaseQueryNode` queries, custom email templates) stays in the web application; only the engine and generic nodes move to packages.
4. **Framework abstractions** — the Core package is framework-agnostic (no `HttpContext`, no EF, no Firebase). Framework integrations are in separate packages.

---

## 2. Package Architecture

```
Bikiran.FlowRunner.Core
│   ├── IFlowNode, NodeResult, FlowContext (AppContext-agnostic)
│   ├── FlowBuilder, FlowRunner (engine)
│   ├── IFlowLogger, InMemoryFlowLogger
│   ├── Nodes: WaitNode, HttpRequestNode, TransformNode, RetryNode, ParallelNode
│   └── IfElseNode, WhileLoopNode
│
├── Bikiran.FlowRunner.EfCore
│   ├── Depends on: Core + Microsoft.EntityFrameworkCore
│   ├── FlowDbLogger (IFlowLogger using DbContext)
│   ├── DatabaseQueryNode
│   ├── FlowRunEntity, FlowNodeLogEntity (IEntityTypeConfiguration)
│   └── IFlowRunnerDbContext + AddFlowRunnerTables() EF extension
│
├── Bikiran.FlowRunner.Email
│   ├── Depends on: Core
│   ├── IFlowEmailSender (interface — replaces EmailSenderV3Service)
│   └── EmailSendNode (uses IFlowEmailSender)
│
├── Bikiran.FlowRunner.Firebase
│   ├── Depends on: Core + FirebaseAdmin
│   └── NotificationNode
│
└── Bikiran.FlowRunner.Scheduling
    ├── Depends on: Core + EfCore + Quartz.Extensions.Hosting
    ├── FlowScheduleJob (IJob)
    ├── FlowSchedulerService
    ├── FlowScheduleEntity, FlowDefinitionEntity
    └── FlowDefinitionRunner
```

### Dependency Matrix

| Package      | Core | EfCore | Email | Firebase | Quartz |
| ------------ | ---- | ------ | ----- | -------- | ------ |
| `Core`       | —    | —      | —     | —        | —      |
| `EfCore`     | ✓    | —      | —     | —        | —      |
| `Email`      | ✓    | —      | —     | —        | —      |
| `Firebase`   | ✓    | —      | —     | —        | —      |
| `Scheduling` | ✓    | ✓      | —     | —        | ✓      |

---

## 3. Package 1: `Bikiran.FlowRunner.Core`

**Project name:** `Bikiran.FlowRunner.Core`  
**NuGet ID:** `Bikiran.FlowRunner.Core`  
**Target framework:** `net9.0`

### What Moves Here

| Class/Interface                          | Notes                                                                                                                             |
| ---------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `IFlowNode`                              | Unchanged                                                                                                                         |
| `NodeResult`                             | Unchanged                                                                                                                         |
| `FlowContext`                            | Remove app-specific types (`AppDbContext`, `EmailSenderV3Service`, `HttpContext`); replace with `IServiceProvider? Services` only |
| `FlowRunConfig`                          | Unchanged                                                                                                                         |
| `OnFailureAction`                        | Unchanged                                                                                                                         |
| `FlowRunStatusEnum`, `NodeLogStatusEnum` | Unchanged                                                                                                                         |
| `IFlowLogger`                            | Unchanged                                                                                                                         |
| `FlowAbortException`                     | Unchanged                                                                                                                         |
| `FlowRunner`                             | Unchanged (no app coupling)                                                                                                       |
| `FlowBuilder`                            | Unchanged                                                                                                                         |
| `WaitNode`                               | Unchanged                                                                                                                         |
| `HttpRequestNode`                        | Remove `IHttpClientFactory` injection — use `new HttpClient()` or accept `HttpClient` via constructor override (see below)        |
| `IfElseNode`                             | Unchanged                                                                                                                         |
| `WhileLoopNode`                          | Unchanged                                                                                                                         |
| `TransformNode`                          | Unchanged                                                                                                                         |
| `RetryNode`                              | Unchanged                                                                                                                         |
| `ParallelNode`                           | Unchanged                                                                                                                         |
| `InMemoryFlowLogger`                     | New — no-op logger for testing/minimal deployments                                                                                |

### `FlowContext` Simplification

In the in-app version, `FlowContext` holds `AppDbContext`, `EmailSenderV3Service`, and `HttpContext` directly for convenience. In the package, these are replaced with the generic `IServiceProvider? Services`:

```csharp
// Core package FlowContext — no app-specific types
public class FlowContext
{
    public string ServiceId { get; internal set; } = string.Empty;
    public string FlowName { get; internal set; } = string.Empty;

    /// <summary>Optional DI scope for resolving services inside nodes.</summary>
    public IServiceProvider? Services { get; set; }

    /// <summary>Optional structured logger.</summary>
    public ILogger? Logger { get; set; }

    // Shared in-memory state
    private readonly Dictionary<string, object> _variables = new();
    public void Set(string key, object value) => _variables[key] = value;
    public T? Get<T>(string key) => _variables.TryGetValue(key, out var v) && v is T t ? t : default;
    public bool Has(string key) => _variables.ContainsKey(key);
    public IReadOnlyDictionary<string, object> Variables => _variables;
}
```

### `InMemoryFlowLogger`

A lightweight no-op logger for use in tests and apps that don't need persistence:

```csharp
public class InMemoryFlowLogger : IFlowLogger
{
    public List<FlowRunRecord> Runs { get; } = new();
    public List<FlowNodeLogRecord> NodeLogs { get; } = new();

    public Task CreateRunAsync(FlowRunRecord run)    { Runs.Add(run); return Task.CompletedTask; }
    public Task UpdateRunAsync(FlowRunRecord run)    { /* update existing */ return Task.CompletedTask; }
    public Task CreateNodeLogAsync(FlowNodeLogRecord log) { NodeLogs.Add(log); return Task.CompletedTask; }
    public Task UpdateNodeLogAsync(FlowNodeLogRecord log) { /* update existing */ return Task.CompletedTask; }
}
```

### `HttpRequestNode` — HttpClient Handling

The Core package should not register `IHttpClientFactory` itself. Instead:

```csharp
public class HttpRequestNode(string name) : IFlowNode
{
    // ... properties ...

    // Consumer can inject a custom HttpClient for testing/pooling
    public HttpClient? HttpClient { get; set; }

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        // Use injected client, or resolve from DI, or create new
        var client = HttpClient
            ?? context.Services?.GetService<IHttpClientFactory>()?.CreateClient("FlowRunner")
            ?? new HttpClient();
        // ...
    }
}
```

### Package Dependencies

```xml
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.*" />
```

No EF Core, no Firebase, no Quartz.

---

## 4. Package 2: `Bikiran.FlowRunner.EfCore`

**NuGet ID:** `Bikiran.FlowRunner.EfCore`  
**Target framework:** `net9.0`

### What Moves Here

| Class                    | Notes                                                  |
| ------------------------ | ------------------------------------------------------ |
| `FlowDbLogger`           | Uses `DbContext`; implements `IFlowLogger` from Core   |
| `DatabaseQueryNode`      | Generic version using `DbContext` (not `AppDbContext`) |
| `FlowRunEntity`          | EF entity (renamed from `FlowRun` table class)         |
| `FlowNodeLogEntity`      | EF entity (renamed from `FlowNodeLog` table class)     |
| `IFlowRunnerDbContext`   | Interface consumer apps must implement                 |
| `FlowRunnerDbExtensions` | EF extension method to configure tables                |

### `IFlowRunnerDbContext`

The package cannot reference `AppDbContext` — it exposes an interface instead:

```csharp
// In Bikiran.FlowRunner.EfCore
public interface IFlowRunnerDbContext
{
    DbSet<FlowRunEntity> FlowRuns { get; }
    DbSet<FlowNodeLogEntity> FlowNodeLogs { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

Consumer app's `AppDbContext` implements this:

```csharp
// In BikiranWebAPI (consumer)
public class AppDbContext : DbContext, IFlowRunnerDbContext
{
    public DbSet<FlowRunEntity> FlowRuns { get; set; } = default!;
    public DbSet<FlowNodeLogEntity> FlowNodeLogs { get; set; } = default!;
}
```

### EF Extension

```csharp
// In Bikiran.FlowRunner.EfCore
public static class FlowRunnerDbExtensions
{
    /// <summary>Call from AppDbContext.OnModelCreating to configure FlowRunner tables.</summary>
    public static void AddFlowRunnerTables(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new FlowRunEntityConfiguration());
        modelBuilder.ApplyConfiguration(new FlowNodeLogEntityConfiguration());
    }
}
```

Consumer usage:

```csharp
// In AppDbContext.OnModelCreating:
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.AddFlowRunnerTables();
    // ... rest of model config
}
```

### `DatabaseQueryNode` Generic Version

```csharp
public class DatabaseQueryNode<TContext>(string name) : IFlowNode
    where TContext : DbContext
{
    public required Func<TContext, CancellationToken, Task<object?>> Query { get; set; }
    public string OutputKey { get; set; } = $"{name}_result";

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        var db = context.Services?.GetRequiredService<TContext>()
            ?? throw new InvalidOperationException("DbContext not available in FlowContext.Services");

        var result = await Query(db, ct);
        if (result != null) context.Set(OutputKey, result);
        return NodeResult.Ok(result);
    }
}
```

---

## 5. Package 3: `Bikiran.FlowRunner.Email`

**NuGet ID:** `Bikiran.FlowRunner.Email`  
**Target framework:** `net9.0`

### What Moves Here

| Class              | Notes                                               |
| ------------------ | --------------------------------------------------- |
| `IFlowEmailSender` | New interface abstracting email sending             |
| `EmailSendNode`    | Uses `IFlowEmailSender` from `FlowContext.Services` |

### `IFlowEmailSender`

```csharp
// In Bikiran.FlowRunner.Email
public interface IFlowEmailSender
{
    Task SendAsync(
        string toEmail,
        string toName,
        string subject,
        string templateKey,
        Dictionary<string, string>? placeholders = null,
        CancellationToken cancellationToken = default
    );
}
```

### Bikiran-App Bridge

In the web application, `EmailSenderV3Service` implements `IFlowEmailSender`:

```csharp
// In BikiranWebAPI — adapter (stays in-app)
public class BikirianFlowEmailSenderAdapter(EmailSenderV3Service emailSender) : IFlowEmailSender
{
    public async Task SendAsync(string toEmail, string toName, string subject,
        string templateKey, Dictionary<string, string>? placeholders = null,
        CancellationToken cancellationToken = default)
    {
        var sender = emailSender
            .To(new EmailContact { Email = toEmail, Name = toName })
            .Subject(subject)
            .Template(templateKey);

        if (placeholders != null)
            sender = sender.AddPlaceholders(placeholders);

        await sender.SendAsync();
    }
}
```

Registered in DI:

```csharp
builder.Services.AddScoped<IFlowEmailSender, BikirianFlowEmailSenderAdapter>();
```

### `EmailSendNode` Package Version

```csharp
public class EmailSendNode(string name) : IFlowNode
{
    public string Name { get; } = name;
    public string NodeType => "EmailSend";

    public string ToEmail { get; set; } = string.Empty;
    public string ToName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public Dictionary<string, string> Placeholders { get; set; } = new();
    public Func<FlowContext, Dictionary<string, string>>? PlaceholderResolver { get; set; }

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        var emailSender = context.Services?.GetRequiredService<IFlowEmailSender>()
            ?? throw new InvalidOperationException("IFlowEmailSender not registered in FlowContext.Services");

        var allPlaceholders = new Dictionary<string, string>(Placeholders);
        if (PlaceholderResolver != null)
            foreach (var kv in PlaceholderResolver(context))
                allPlaceholders[kv.Key] = kv.Value;

        try
        {
            await emailSender.SendAsync(ToEmail, ToName, Subject, Template, allPlaceholders, ct);
            return NodeResult.Ok();
        }
        catch (Exception ex)
        {
            return NodeResult.Fail(ex.Message);
        }
    }
}
```

---

## 6. Package 4: `Bikiran.FlowRunner.Firebase`

**NuGet ID:** `Bikiran.FlowRunner.Firebase`  
**Target framework:** `net9.0`

### What Moves Here

| Class                      | Notes                                                   |
| -------------------------- | ------------------------------------------------------- |
| `NotificationNode`         | Generic form using `FirebaseAdmin` SDK directly         |
| `IFlowFirebaseInitializer` | Allows consumer apps to provide Firebase initialization |

### `IFlowFirebaseInitializer`

```csharp
public interface IFlowFirebaseInitializer
{
    void EnsureInitialized();
}
```

Consumer app registers:

```csharp
builder.Services.AddSingleton<IFlowFirebaseInitializer, FirebaseInitializer>();
// Where FirebaseInitializer calls FirebaseUserService2.InitializeFirebase()
```

### Package Dependencies

```xml
<PackageReference Include="FirebaseAdmin" Version="3.*" />
<PackageReference Include="Bikiran.FlowRunner.Core" Version="*" />
```

---

## 7. Package 5: `Bikiran.FlowRunner.Scheduling`

**NuGet ID:** `Bikiran.FlowRunner.Scheduling`  
**Target framework:** `net9.0`

### What Moves Here

| Class                   | Notes                                                 |
| ----------------------- | ----------------------------------------------------- |
| `FlowScheduleJob`       | Quartz `IJob` — generic, uses `IFlowDefinitionRunner` |
| `FlowSchedulerService`  | Generic registration service                          |
| `FlowScheduleEntity`    | EF entity for `FlowSchedule` table                    |
| `FlowDefinitionEntity`  | EF entity for `FlowDefinition` table                  |
| `IFlowDefinitionRunner` | Interface for triggering a definition by key          |
| `FlowDefinitionRunner`  | Generic implementation using `IFlowRunnerDbContext`   |

### Package Dependencies

```xml
<PackageReference Include="Bikiran.FlowRunner.Core" Version="*" />
<PackageReference Include="Bikiran.FlowRunner.EfCore" Version="*" />
<PackageReference Include="Quartz.Extensions.Hosting" Version="3.*" />
```

---

## 8. Refactoring the In-App Code

Once packages are published, the in-app FlowRunner code is replaced with package references. The migration is a 3-step process:

### Step 1: Install Packages

```xml
<!-- In BikiranWebAPI.csproj -->
<PackageReference Include="Bikiran.FlowRunner.Core" Version="1.0.0" />
<PackageReference Include="Bikiran.FlowRunner.EfCore" Version="1.0.0" />
<PackageReference Include="Bikiran.FlowRunner.Email" Version="1.0.0" />
<PackageReference Include="Bikiran.FlowRunner.Firebase" Version="1.0.0" />
<PackageReference Include="Bikiran.FlowRunner.Scheduling" Version="1.0.0" />
```

### Step 2: Delete In-App Source Files

Remove the `Services/FlowRunner/` folder (except app-specific adapters). Remove `Tables/FlowRun.cs`, `Tables/FlowNodeLog.cs` etc. — these are now in the EfCore package as `IEntityTypeConfiguration` entries.

**Keep in-app (not in packages):**

- `BikirianFlowEmailSenderAdapter.cs` — bridges `EmailSenderV3Service` to `IFlowEmailSender`
- `DatabaseQueryNode` usages with `AppDbContext`-typed queries
- Any flow definitions registered in controllers

### Step 3: Update `AppDbContext`

```csharp
// Implement the package interface
public class AppDbContext : DbContext, IFlowRunnerDbContext, IFlowSchedulerDbContext
{
    public DbSet<FlowRunEntity> FlowRuns { get; set; } = default!;
    public DbSet<FlowNodeLogEntity> FlowNodeLogs { get; set; } = default!;
    public DbSet<FlowScheduleEntity> FlowSchedules { get; set; } = default!;
    public DbSet<FlowDefinitionEntity> FlowDefinitions { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddFlowRunnerTables();      // from EfCore package
        modelBuilder.AddFlowSchedulerTables();   // from Scheduling package
        // ... existing config
    }
}
```

### Step 4: Update DI in `Program.cs`

```csharp
builder.Services.AddFlowRunner(options => {
    options.EnableNodeLogging = true;
    options.DefaultMaxExecutionTime = TimeSpan.FromMinutes(10);
});

builder.Services.AddFlowRunnerEfCore<AppDbContext>();

builder.Services.AddFlowRunnerEmail();
builder.Services.AddScoped<IFlowEmailSender, BikirianFlowEmailSenderAdapter>();

builder.Services.AddFlowRunnerFirebase();
builder.Services.AddSingleton<IFlowFirebaseInitializer, FirebaseInitializer>();

builder.Services.AddFlowRunnerScheduling<AppDbContext>();
```

---

## 9. DI Registration API

Each package provides a clean `IServiceCollection` extension method following the .NET convention:

```csharp
// Bikiran.FlowRunner.Core
public static IServiceCollection AddFlowRunner(this IServiceCollection services,
    Action<FlowRunnerOptions>? configure = null) { ... }

// Bikiran.FlowRunner.EfCore — TContext must implement IFlowRunnerDbContext
public static IServiceCollection AddFlowRunnerEfCore<TContext>(this IServiceCollection services)
    where TContext : DbContext, IFlowRunnerDbContext { ... }

// Bikiran.FlowRunner.Email
public static IServiceCollection AddFlowRunnerEmail(this IServiceCollection services) { ... }

// Bikiran.FlowRunner.Firebase
public static IServiceCollection AddFlowRunnerFirebase(this IServiceCollection services) { ... }

// Bikiran.FlowRunner.Scheduling
public static IServiceCollection AddFlowRunnerScheduling<TContext>(this IServiceCollection services)
    where TContext : DbContext, IFlowRunnerDbContext, IFlowSchedulerDbContext { ... }
```

---

## 10. Versioning & Publishing Strategy

### Version Numbering

Follow semantic versioning (`MAJOR.MINOR.PATCH`):

| Change Type                        | Version Bump |
| ---------------------------------- | ------------ |
| Breaking change to a public API    | MAJOR        |
| New node type, new optional method | MINOR        |
| Bug fix, doc update                | PATCH        |

All packages share the same version number (e.g. all `1.2.3`) to simplify compatibility tracking.

### Publishing

1. Create a new GitHub repository: `bikirandev/Bikiran.FlowRunner`
2. Use a multi-project solution with one project per package.
3. Publish to `nuget.org` (public) or the Bikiran private NuGet feed.
4. CI/CD: GitHub Actions triggered on version tag (`v1.0.0`).

```yaml
# .github/workflows/publish.yml
on:
  push:
    tags: ["v*"]

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: "9.x" }
      - run: dotnet pack --configuration Release /p:PackageVersion=${GITHUB_REF_NAME#v}
      - run: dotnet nuget push **/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json
```

### README Badges

Each NuGet package README should include:

```markdown
[![NuGet](https://img.shields.io/nuget/v/Bikiran.FlowRunner.Core)](https://www.nuget.org/packages/Bikiran.FlowRunner.Core)
[![Downloads](https://img.shields.io/nuget/dt/Bikiran.FlowRunner.Core)](https://www.nuget.org/packages/Bikiran.FlowRunner.Core)
```

---

## 11. File & Folder Structure (Solution-Level)

The packages live in a **separate solution** from `7201DOT-Bikiran-API`:

```
Bikiran.FlowRunner/                    ← New GitHub repo
├── Bikiran.FlowRunner.sln
├── src/
│   ├── Bikiran.FlowRunner.Core/
│   │   ├── Bikiran.FlowRunner.Core.csproj
│   │   ├── Core/            ← IFlowNode, NodeResult, FlowContext, etc.
│   │   ├── Nodes/           ← WaitNode, HttpRequestNode, IfElseNode, etc.
│   │   ├── Builder/         ← FlowBuilder, FlowRunner, FlowRunConfig
│   │   └── Logging/         ← IFlowLogger, InMemoryFlowLogger
│   │
│   ├── Bikiran.FlowRunner.EfCore/
│   │   ├── Bikiran.FlowRunner.EfCore.csproj
│   │   ├── Entities/        ← FlowRunEntity, FlowNodeLogEntity
│   │   ├── Configurations/  ← EF IEntityTypeConfiguration
│   │   ├── DbLogger/        ← FlowDbLogger
│   │   ├── Nodes/           ← DatabaseQueryNode<TContext>
│   │   └── Extensions/      ← FlowRunnerDbExtensions
│   │
│   ├── Bikiran.FlowRunner.Email/
│   │   ├── Bikiran.FlowRunner.Email.csproj
│   │   ├── Abstractions/    ← IFlowEmailSender
│   │   └── Nodes/           ← EmailSendNode
│   │
│   ├── Bikiran.FlowRunner.Firebase/
│   │   ├── Bikiran.FlowRunner.Firebase.csproj
│   │   ├── Abstractions/    ← IFlowFirebaseInitializer
│   │   └── Nodes/           ← NotificationNode
│   │
│   └── Bikiran.FlowRunner.Scheduling/
│       ├── Bikiran.FlowRunner.Scheduling.csproj
│       ├── Abstractions/    ← IFlowDefinitionRunner
│       ├── Entities/        ← FlowScheduleEntity, FlowDefinitionEntity
│       ├── Jobs/            ← FlowScheduleJob
│       ├── Services/        ← FlowSchedulerService, FlowDefinitionRunner
│       └── Extensions/      ← AddFlowRunnerScheduling<T>
│
├── tests/
│   ├── Bikiran.FlowRunner.Core.Tests/
│   │   └── ... xUnit tests for all Core nodes and builder
│   └── Bikiran.FlowRunner.EfCore.Tests/
│       └── ... integration tests using SQLite in-memory
│
└── docs/
    ├── README.md
    ├── getting-started.md
    ├── nodes-reference.md
    └── migration-guide.md     ← For upgrading from in-app to NuGet
```

---

## 12. Step-by-Step Implementation Guide

### Step 1 — Create New .NET Solution

```powershell
mkdir Bikiran.FlowRunner
cd Bikiran.FlowRunner
dotnet new sln -n Bikiran.FlowRunner
dotnet new classlib -n Bikiran.FlowRunner.Core --framework net9.0
dotnet sln add src/Bikiran.FlowRunner.Core
# Repeat for each package
```

---

### Step 2 — Copy and Adapt Core Code

Copy from `Services/FlowRunner/Core/` and `Services/FlowRunner/Nodes/` (except `EmailSendNode`, `NotificationNode`, `DatabaseQueryNode`). Remove all `AppDbContext`, `EmailSenderV3Service`, `HttpContext` references.

---

### Step 3 — Write Core Unit Tests

Create `tests/Bikiran.FlowRunner.Core.Tests`. Use xUnit. Test:

- `WaitNode` respects delay
- `HttpRequestNode` retries on failure (use `MockHttpMessageHandler`)
- `IfElseNode` runs correct branch
- `WhileLoopNode` respects `MaxIterations`
- `FlowBuilder` builds and returns a `ServiceId`
- `InMemoryFlowLogger` collects run + node log records

Aim for ≥80% code coverage.

---

### Step 4 — Implement EfCore Package

Copy `FlowDbLogger` from app. Abstract `AppDbContext` to `IFlowRunnerDbContext`. Write an integration test using SQLite in-memory DbContext.

---

### Step 5 — Implement Email Package

Create `IFlowEmailSender`. Adapt `EmailSendNode`. Write `BikirianFlowEmailSenderAdapter` **in the web app** (not the package).

---

### Step 6 — Implement Firebase Package

Copy `NotificationNode`. Create `IFlowFirebaseInitializer`. Keep `FirebaseAdmin` as a package dependency.

---

### Step 7 — Implement Scheduling Package

Copy `FlowScheduleJob` and `FlowSchedulerService`. Create `IFlowSchedulerDbContext` for the schedule entities. Write integration test with a mock `IScheduler`.

---

### Step 8 — Pack and Test Locally

```powershell
dotnet pack --configuration Release --output ./nupkgs
# In BikiranWebAPI, add local feed:
dotnet nuget add source ../../Bikiran.FlowRunner/nupkgs --name LocalFlowRunner
dotnet add package Bikiran.FlowRunner.Core --version 1.0.0
```

Verify the web app compiles and all flows work as before.

---

### Step 9 — Publish to NuGet

Set up GitHub repo `bikirandev/Bikiran.FlowRunner`. Configure GitHub Actions for publish-on-tag. Tag `v1.0.0` to trigger first publish.

---

### Step 10 — Migrate In-App Version

Follow the 4-step migration in Section 8. Run all existing API tests to confirm no regressions.

---

## 13. Integration Guide for Consumers

### Minimal Setup (Core only, no DB logging)

```csharp
// Program.cs
builder.Services.AddHttpClient();
builder.Services.AddFlowRunner();

// Usage:
var serviceId = await FlowBuilder
    .Create("my_flow")
    .AddNode(new WaitNode("pause") { DelayMs = 1000 })
    .AddNode(new HttpRequestNode("call_api") {
        Url = "https://httpbin.org/get",
        OutputKey = "api_data"
    })
    .StartAsync();
```

### Full Setup (DB logging + email + scheduling)

```csharp
// Program.cs
builder.Services.AddFlowRunner(opts => {
    opts.EnableNodeLogging = true;
});
builder.Services.AddFlowRunnerEfCore<AppDbContext>();
builder.Services.AddFlowRunnerEmail();
builder.Services.AddScoped<IFlowEmailSender, MyEmailSenderAdapter>();
builder.Services.AddFlowRunnerScheduling<AppDbContext>();

// Usage with DI-resolved context:
public class OrderService(IFlowBuilderFactory flowFactory) {
    public async Task<string> NotifyAsync(Order order) {
        return await flowFactory
            .Create("order_notify")
            .WithServices(_scopedServices)
            .AddNode(new EmailSendNode("notify") {
                ToEmail = order.CustomerEmail,
                Template = "ORDER_CREATE"
            })
            .StartAsync();
    }
}
```

---

_End of Phase 5 Plan — NuGet extraction is a future milestone after Phases 1–4 are stable in production._
