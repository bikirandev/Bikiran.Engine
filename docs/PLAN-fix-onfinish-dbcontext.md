# Plan: Fix OnFinish Disposed DbContext & Flow Status Issues

> **Status: COMPLETED** — All steps implemented. See commit history for details.

## Problem Summary

Two issues observed when the `DomainAddFinishLogNode` (an `OnFinish` lifecycle node) runs:

1. **Flow Status = Failed** — The flow itself is failing at an earlier node, so `OnFinish` correctly receives `FlowRunStatus.Failed`. This is **expected behavior** if any preceding node fails. However, the real concern is issue #2 which may also cause failures in earlier nodes.

2. **"Cannot access a disposed context instance"** — The `OnFinish` node cannot use any `DbContext` because it has been disposed.

---

## Root Cause Analysis

### Issue 2 — Disposed DbContext (the core bug)

The flow is started with `StartAsync()`, which fires-and-forgets via `Task.Run`:

```
HTTP Request scope ──▶ _context (AppDbContext) created by DI
                    ──▶ FlowBuilder.WithContext(ctx => ctx.DbContext = _context)
                    ──▶ FlowBuilder.StartAsync() → Task.Run(background)
                    ──▶ HTTP Request completes → DI disposes _context ❌
                    ...
                    ──▶ (background) OnFinish node tries to use _context → BOOM
```

**The `DbContext` set via `.WithContext(ctx => ctx.DbContext = _context)` is the HTTP-request-scoped instance.** Once the HTTP request ends, ASP.NET Core's DI container disposes it. But the background flow is still running.

Meanwhile, `FlowBuilder.PrepareAsync()` **does** create its own long-lived DI scope:

```csharp
var scope = ServiceProvider?.CreateScope();         // ← lives until Task.Run finally block
var engineDb = scope?.ServiceProvider.GetService<EngineDbContext>();
```

But this scope is **never exposed to user nodes** — it's only used for the engine's internal `EngineDbContext`. Nodes have no way to resolve `AppDbContext` (or any service) from the long-lived scope.

Additionally, `FlowContext.Services` exists as a property but is **never set** by `FlowBuilder`.

### Issue 1 — Flow Status = Failed

This is likely a **cascading consequence** of an earlier node also hitting the disposed `DbContext`, or failing for its own reasons (network error, API timeout, etc.). The engine correctly marks the flow as Failed and forwards the status to `OnFinish`. Once issue #2 is fixed, if the flow still fails, the problem is in a specific node (check node logs).

---

## Fix Plan

### Step 1: Expose the DI scope to FlowContext (Engine change)

In `FlowBuilder.PrepareAsync()`, after creating the scope, set `context.Services` so user nodes can resolve services:

```csharp
// In PrepareAsync(), after creating scope:
context.Services = scope?.ServiceProvider;
```

**File:** `Core/FlowBuilder.cs` → `PrepareAsync()`

### Step 2: Add `GetDbContext<T>()` convenience method (Engine change)

Add a helper method to `FlowContext` that resolves a `DbContext` from the DI scope:

```csharp
public T? GetDbContext<T>() where T : class
{
    return Services?.GetService(typeof(T)) as T;
}
```

This uses the long-lived scope created by `FlowBuilder`, NOT the HTTP-request scope.

**File:** `Core/FlowContext.cs`

### Step 3: Consumer code — stop passing `ctx.DbContext = _context` (User change)

In the user's flow builder call, change:

```csharp
// BEFORE (broken — captures request-scoped DbContext)
.WithContext(ctx => { ctx.DbContext = _context; })

// AFTER (correct — use DI-scoped resolution in nodes)
.WithContext(ctx => { })   // or remove .WithContext entirely if not needed
```

Nodes should call `context.GetDbContext<AppDbContext>()` instead of using `context.DbContext`.

The `DomainAddFinishLogNode` already does this correctly — it calls `context.GetDbContext<AppDbContext>()`. No changes needed in the node itself.

---

## Summary of Changes

| File | Change | Scope |
|------|--------|-------|
| `Core/FlowBuilder.cs` | Set `context.Services = scope?.ServiceProvider` in `PrepareAsync()` | Engine |
| `Core/FlowContext.cs` | Add `GetDbContext<T>()` method using `Services` | Engine |
| *User's controller* | Remove `ctx.DbContext = _context` from `.WithContext(...)` | Consumer app |

---

## After Fixing

- `OnFinish` (and all nodes) will be able to resolve `AppDbContext` via `context.GetDbContext<AppDbContext>()` from the engine's long-lived DI scope.
- The resolved `AppDbContext` will remain alive for the entire background flow.
- If the flow still shows `Status = Failed`, check the engine's node logs to identify which specific node is failing and why.
