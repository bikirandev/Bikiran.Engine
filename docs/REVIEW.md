# Bikiran.Engine — Code Review Report

**Date:** 2026-03-23  
**Scope:** Full codebase review — code quality, correctness, consistency, documentation

---

## Issues Found & Fixes Applied

### 1. ParallelNode passes wrong CancellationToken to branches (Bug)

**File:** `Nodes/ParallelNode.cs`  
**Problem:** `RunBranchAsync` accepts both a `CancellationTokenSource cts` and a separate `CancellationToken cancellationToken` (the parent token), but passes only the parent token to child nodes. When `AbortOnBranchFailure` cancels `cts`, running branch nodes are **not** cancelled because they hold the original parent token, not the linked one.  
**Fix:** Pass `cts.Token` to child nodes instead of the unlinked parent token.

---

### 2. FlowRunner creates excessive DI scopes (Performance)

**File:** `Core/FlowRunner.cs`  
**Problem:** Each of 4 private database helper methods creates its own `IServiceScope` and resolves `EngineDbContext`. For a flow with N nodes, this creates up to ~4N scopes. This is wasteful and creates unnecessary GC pressure.  
**Fix:** Refactored `FlowRunner` to accept a single `EngineDbContext` instance and reuse it throughout the run session. The caller (`FlowBuilder.PrepareAsync`) creates the scope and passes the context.

---

### 3. FlowBuilder.PrepareAsync leaks DI scope (Resource Leak)

**File:** `Core/FlowBuilder.cs`  
**Problem:** `var scope = ServiceProvider?.CreateScope()` is created but never disposed. The scope (and its scoped services) are leaked for every flow execution.  
**Fix:** The scope is now created in `StartAsync`/`StartAndWaitAsync` and disposed properly after the flow completes.

---

### 4. FlowBuilder.StartAsync fire-and-forget swallows exceptions (Bug)

**File:** `Core/FlowBuilder.cs`  
**Problem:** `_ = runner.RunAsync(...)` discards the Task. If `RunAsync` throws a synchronous exception or fails before the first await, it's silently swallowed.  
**Fix:** Wrapped in a continuation that logs unhandled exceptions via the context logger.

---

### 5. OnFailureAction.Retry defined but never handled (Dead Code)

**File:** `Core/FlowRunner.cs`  
**Problem:** The `OnFailureAction` enum has `Stop`, `Continue`, and `Retry` values. `FlowRunner` only checks for `Stop` — `Retry` silently falls through to `Continue` behavior.  
**Fix:** Removed the `Retry` value from the enum. Retry behavior is already handled by `RetryNode`, which is the correct pattern. Documented this in the enum.

---

### 6. EngineStartupService doesn't call FlowSchedulerService.InitializeAsync (Bug)

**File:** `Extensions/EngineStartupService.cs`  
**Problem:** On startup, the engine runs schema migration but never loads existing schedules into Quartz. Previously saved schedules won't fire until manually re-registered.  
**Fix:** Added `FlowSchedulerService.InitializeAsync()` call in `EngineStartupService.StartAsync()`.

---

### 7. FlowDefinitionsController missing GET /{key}/runs endpoint (Missing Feature)

**File:** `Api/FlowDefinitionsController.cs`  
**Problem:** The REPORT.md and 08-admin-api.md document a `GET /definitions/{key}/runs` endpoint, but it doesn't exist in the controller.  
**Fix:** Added the endpoint.

---

### 8. SchemaMigrator.PackageVersion mismatch (Inconsistency)

**File:** `Database/Migration/SchemaMigrator.cs`  
**Problem:** `PackageVersion` is hardcoded as `"1.0.0"` while the csproj version is `1.0.1`.  
**Fix:** Updated to `"1.0.1"`.

---

### 9. SchemaMigrator fallback uses EnsureCreatedAsync despite comment warning against it (Bug)

**File:** `Database/Migration/SchemaMigrator.cs`  
**Problem:** The method comment says "EnsureCreatedAsync() is not used because it skips table creation when the database already contains tables from the host application" — but the non-MySQL fallback does exactly that. This means SQL Server and PostgreSQL deployments with existing host tables will silently skip engine table creation.  
**Fix:** Updated comment to clarify the limitation. The MySQL path uses raw SQL, while non-MySQL relies on EnsureCreatedAsync as a best-effort fallback. Added a warning log for non-MySQL providers.

---

### 10. FlowDefinitionParser uses non-thread-safe static Dictionary (Thread Safety)

**File:** `Definitions/FlowDefinitionParser.cs`  
**Problem:** `_customNodeTypes` is a `Dictionary<string, Type>` mutated by `RegisterNode<T>()`. If registration happens concurrently, the dictionary can corrupt.  
**Fix:** Changed to `ConcurrentDictionary<string, Type>`.

---

### 11. REPORT.md version says 1.0.0 (Inconsistency)

**File:** `docs/REPORT.md`  
**Problem:** The report header says version `1.0.0` but the csproj is `1.0.1`.  
**Fix:** Updated to `1.0.1`.

---

## Documentation Issues Fixed

| Doc File | Issue | Fix |
|----------|-------|-----|
| `REPORT.md` | Version listed as 1.0.0 | Updated to 1.0.1 |
| `04-built-in-nodes.md` | Claims TransformNode "never fails" | Corrected — it fails if no transform is set |
| `05-flow-definitions.md` | RetryNode not listed as unsupported in JSON | Added to limitations table |
| `REPORT.md` | SchemaMigrator description says "EnsureCreatedAsync" only | Clarified MySQL raw SQL + fallback behavior |
| `REPORT.md` | OnFailureAction lists "Retry" | Updated to reflect removal |
