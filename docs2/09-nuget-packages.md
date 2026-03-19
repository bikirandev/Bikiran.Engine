# NuGet Package Extraction

This document describes how Bikiran.Engine is structured for extraction into reusable, independently-versioned NuGet packages — enabling any .NET 9 application to embed the workflow engine.

---

## Guiding Principles

1. **No breaking changes** — the in-app code keeps working after switching to NuGet packages.
2. **Layered opt-in** — install only the packages you need. A minimal setup requires only `Core`.
3. **Business logic stays in-app** — custom queries, email templates, and app-specific adapters remain in the host application.
4. **Framework abstractions** — the Core package has no dependency on `HttpContext`, EF Core, or Firebase.

---

## Package Overview

```
Bikiran.FlowRunner.Core
├── Core interfaces: IFlowNode, NodeResult, FlowContext
├── Engine: FlowBuilder, FlowRunner
├── Logging: IFlowLogger, InMemoryFlowLogger
└── Nodes: Wait, HttpRequest, IfElse, WhileLoop, Transform, Retry, Parallel

Bikiran.FlowRunner.EfCore
├── FlowDbLogger (IFlowLogger using DbContext)
├── DatabaseQueryNode (generic version)
├── EF entities: FlowRunEntity, FlowNodeLogEntity
└── IFlowRunnerDbContext interface

Bikiran.FlowRunner.Email
├── IFlowEmailSender interface
└── EmailSendNode

Bikiran.FlowRunner.Firebase
├── IFlowFirebaseInitializer interface
└── NotificationNode

Bikiran.FlowRunner.Scheduling
├── FlowScheduleJob (Quartz IJob)
├── FlowSchedulerService
├── FlowDefinitionRunner
└── EF entities: FlowScheduleEntity, FlowDefinitionEntity
```

---

## Package Dependencies

| Package      | Depends On                                                                                                   |
| ------------ | ------------------------------------------------------------------------------------------------------------ |
| `Core`       | Only `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `EfCore`     | Core + `Microsoft.EntityFrameworkCore`                                                                       |
| `Email`      | Core                                                                                                         |
| `Firebase`   | Core + `FirebaseAdmin`                                                                                       |
| `Scheduling` | Core + EfCore + `Quartz.Extensions.Hosting`                                                                  |

---

## Key Changes from In-App to Package

### FlowContext Simplification

In the in-app version, `FlowContext` directly holds `AppDbContext`, `EmailSenderV3Service`, and `HttpContext`. In the Core package, these are replaced by a generic `IServiceProvider`:

```csharp
// Package version — no app-specific types
public class FlowContext
{
    public string ServiceId { get; internal set; }
    public string FlowName { get; internal set; }
    public IServiceProvider? Services { get; set; }
    public ILogger? Logger { get; set; }

    // Shared in-memory state (unchanged)
    void Set(string key, object value);
    T? Get<T>(string key);
    bool Has(string key);
}
```

Consumer apps resolve their own services:

```csharp
var db = context.Services?.GetRequiredService<AppDbContext>();
```

### DatabaseQueryNode — Generic Version

The package uses a generic `DbContext` type parameter instead of `AppDbContext`:

```csharp
public class DatabaseQueryNode<TContext>(string name) : IFlowNode
    where TContext : DbContext
{
    public required Func<TContext, CancellationToken, Task<object?>> Query { get; set; }
    // ...
}
```

### Email — Interface Abstraction

The package defines `IFlowEmailSender` instead of depending on `EmailSenderV3Service`:

```csharp
public interface IFlowEmailSender
{
    Task SendAsync(string toEmail, string toName, string subject,
        string templateKey, Dictionary<string, string>? placeholders = null,
        CancellationToken cancellationToken = default);
}
```

The host application creates an adapter:

```csharp
public class BikirianFlowEmailSenderAdapter(EmailSenderV3Service emailSender) : IFlowEmailSender
{
    public async Task SendAsync(string toEmail, string toName, string subject,
        string templateKey, Dictionary<string, string>? placeholders, CancellationToken ct)
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

### EF Core — Consumer Interface

The host application's `DbContext` implements package-defined interfaces:

```csharp
public class AppDbContext : DbContext, IFlowRunnerDbContext
{
    public DbSet<FlowRunEntity> FlowRuns { get; set; }
    public DbSet<FlowNodeLogEntity> FlowNodeLogs { get; set; }
}
```

And registers the table configurations:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.AddFlowRunnerTables();   // from EfCore package
}
```

### InMemoryFlowLogger

A lightweight logger for testing or apps that don't need database persistence:

```csharp
public class InMemoryFlowLogger : IFlowLogger
{
    public List<FlowRunRecord> Runs { get; }
    public List<FlowNodeLogRecord> NodeLogs { get; }
    // All methods store records in memory lists
}
```

---

## DI Registration

Each package provides an `IServiceCollection` extension method:

```csharp
// Core
builder.Services.AddFlowRunner(opts => {
    opts.EnableNodeLogging = true;
    opts.DefaultMaxExecutionTime = TimeSpan.FromMinutes(10);
});

// EF Core — TContext must implement IFlowRunnerDbContext
builder.Services.AddFlowRunnerEfCore<AppDbContext>();

// Email
builder.Services.AddFlowRunnerEmail();
builder.Services.AddScoped<IFlowEmailSender, BikirianFlowEmailSenderAdapter>();

// Firebase
builder.Services.AddFlowRunnerFirebase();
builder.Services.AddSingleton<IFlowFirebaseInitializer, FirebaseInitializer>();

// Scheduling
builder.Services.AddFlowRunnerScheduling<AppDbContext>();
```

---

## Migration Steps (In-App to NuGet)

### Step 1: Install Packages

```xml
<PackageReference Include="Bikiran.FlowRunner.Core" Version="1.0.0" />
<PackageReference Include="Bikiran.FlowRunner.EfCore" Version="1.0.0" />
<PackageReference Include="Bikiran.FlowRunner.Email" Version="1.0.0" />
<PackageReference Include="Bikiran.FlowRunner.Firebase" Version="1.0.0" />
<PackageReference Include="Bikiran.FlowRunner.Scheduling" Version="1.0.0" />
```

### Step 2: Delete In-App Source Files

Remove the `Services/FlowRunner/` folder and EF table classes (`Tables/FlowRun.cs`, etc.) — these are now provided by the packages.

**Keep in the app:**

- `BikirianFlowEmailSenderAdapter.cs` — bridges `EmailSenderV3Service` to `IFlowEmailSender`
- App-specific `DatabaseQueryNode` usages with `AppDbContext`-typed queries
- Flow definitions registered in controllers

### Step 3: Update AppDbContext

Implement the package interfaces and register table configurations:

```csharp
public class AppDbContext : DbContext, IFlowRunnerDbContext, IFlowSchedulerDbContext
{
    public DbSet<FlowRunEntity> FlowRuns { get; set; }
    public DbSet<FlowNodeLogEntity> FlowNodeLogs { get; set; }
    public DbSet<FlowScheduleEntity> FlowSchedules { get; set; }
    public DbSet<FlowDefinitionEntity> FlowDefinitions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddFlowRunnerTables();
        modelBuilder.AddFlowSchedulerTables();
    }
}
```

### Step 4: Update DI in Program.cs

Replace direct class registrations with the package extension methods (shown in the DI Registration section above).

---

## Solution Structure

```
Bikiran.FlowRunner/                        ← Separate GitHub repository
├── Bikiran.FlowRunner.sln
├── src/
│   ├── Bikiran.FlowRunner.Core/
│   │   ├── Core/         ← IFlowNode, NodeResult, FlowContext, etc.
│   │   ├── Nodes/        ← WaitNode, HttpRequestNode, IfElseNode, etc.
│   │   ├── Builder/      ← FlowBuilder, FlowRunner, FlowRunConfig
│   │   └── Logging/      ← IFlowLogger, InMemoryFlowLogger
│   │
│   ├── Bikiran.FlowRunner.EfCore/
│   │   ├── Entities/
│   │   ├── Configurations/
│   │   ├── DbLogger/
│   │   ├── Nodes/        ← DatabaseQueryNode<TContext>
│   │   └── Extensions/
│   │
│   ├── Bikiran.FlowRunner.Email/
│   │   ├── Abstractions/ ← IFlowEmailSender
│   │   └── Nodes/        ← EmailSendNode
│   │
│   ├── Bikiran.FlowRunner.Firebase/
│   │   ├── Abstractions/ ← IFlowFirebaseInitializer
│   │   └── Nodes/        ← NotificationNode
│   │
│   └── Bikiran.FlowRunner.Scheduling/
│       ├── Abstractions/
│       ├── Entities/
│       ├── Jobs/         ← FlowScheduleJob
│       ├── Services/     ← FlowSchedulerService, FlowDefinitionRunner
│       └── Extensions/
│
├── tests/
│   ├── Bikiran.FlowRunner.Core.Tests/
│   └── Bikiran.FlowRunner.EfCore.Tests/
│
└── docs/
```

---

## Versioning and Publishing

All packages share the same version number for compatibility. Semantic versioning applies:

| Change                           | Version Bump |
| -------------------------------- | ------------ |
| Breaking API change              | Major        |
| New node type or optional method | Minor        |
| Bug fix or documentation update  | Patch        |

Publishing is handled via GitHub Actions on version tags (`v1.0.0`), pushing to `nuget.org`.

---

## Consumer Quick Start

### Minimal Setup (Core only, no database)

```csharp
builder.Services.AddHttpClient();
builder.Services.AddFlowRunner();

var serviceId = await FlowBuilder
    .Create("my_flow")
    .AddNode(new WaitNode("pause") { DelayMs = 1000 })
    .AddNode(new HttpRequestNode("call_api") {
        Url = "https://httpbin.org/get",
        OutputKey = "api_data"
    })
    .StartAsync();
```

### Full Setup (DB + email + scheduling)

```csharp
builder.Services.AddFlowRunner(opts => { opts.EnableNodeLogging = true; });
builder.Services.AddFlowRunnerEfCore<AppDbContext>();
builder.Services.AddFlowRunnerEmail();
builder.Services.AddScoped<IFlowEmailSender, MyEmailAdapter>();
builder.Services.AddFlowRunnerScheduling<AppDbContext>();
```
