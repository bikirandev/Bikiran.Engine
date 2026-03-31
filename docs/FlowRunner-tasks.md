# FlowRunner — Optimization Task List

## Summary

FlowRunner handles flow execution, database persistence, serialization, and failure recovery all in a single 330+ line class. This document identifies inconsistencies and optimization opportunities, then defines the tasks to build an optimized FlowRunner with separated concerns.

---

## Identified Issues

### 1. Excessive Database Round-Trips (N+1 Pattern)

Every node triggers ~4 separate `SaveChangesAsync()` calls:

| Operation                   | DB Query | DB Save |
| --------------------------- | -------- | ------- |
| Create node log             | 0        | 1       |
| Update run progress message | 1        | 1       |
| Node executes...            | —        | —       |
| Update node log (completed) | 1        | 1       |
| Update run progress counter | 1        | 1       |

**Per node: 3 queries + 4 saves = 7 DB operations.**
For a 10-node flow: **70 DB operations** just for the main loop.

### 2. Redundant FlowRun Lookups

`UpdateRunStatusAsync`, `UpdateRunProgressAsync`, and `UpdateRunProgressMessageAsync` each call `FirstOrDefaultAsync` to find the same `FlowRun` record every time. The entity could be cached once and reused for the entire run.

### 3. Mixed Concerns (Single Responsibility Violation)

FlowRunner handles:

- Main flow execution loop
- Lifecycle event execution
- Database CRUD for FlowRun records
- Database CRUD for FlowNodeLog records
- Failure recovery logging
- Node input/output serialization
- Type reflection for serialization

These should be separated into:

- **FlowRunner** — Main execution logic only
- **FlowRunnerHelper** — All database, serialization, and failure recovery helpers

### 4. Inconsistent Error Truncation

Three different truncation patterns for the same 500-char limit:

```csharp
// Pattern A (UpdateRunStatusAsync, UpdateNodeLogAsync)
errorMessage[..Math.Min(errorMessage.Length, 500)]

// Pattern B (TryRecordFailingNodeLogAsync)
errorMessage.Length > 500 ? errorMessage[..500] : errorMessage
```

Should be a single `TruncateError()` helper.

### 5. CancellationToken Leak into Lifecycle Nodes

After `MaxExecutionTime` expires, the same `ct` (already cancelled) is passed to lifecycle event nodes (`OnSuccess`/`OnFail`/`OnFinish`). These nodes may fail immediately with `OperationCanceledException` instead of running their cleanup logic.

### 6. Serialization Gaps

`IsSerializableType` handles `string`, primitives, enums, `HttpMethod`, and `Dictionary<string, T>` — but does **not** handle `List<T>` or arrays. Node properties using lists are silently excluded from input logging.

### 7. Redundant Progress Message Call

`UpdateRunProgressMessageAsync` is called for every node even when `ProgressMessage` is `null`. The null-check inside the method returns early, but the call overhead (method dispatch + null check) is unnecessary.

### 8. Doc Inconsistency: Progress Counter

Doc 03 says "The progress counter increments" unconditionally after each step. In code, the counter only increments on success or when `OnFailure == Continue`. On `Stop`, it breaks without incrementing.

---

## Architecture: FlowRunner + FlowRunnerHelper

### FlowRunner (Core/FlowRunner.cs)

Responsibilities:

- Main node execution loop
- Lifecycle event orchestration
- CancellationToken management
- Delegates all DB/serialization to helper

### FlowRunnerHelper (Core/FlowRunnerHelper.cs)

Responsibilities:

- FlowRun entity cache + CRUD
- FlowNodeLog CRUD
- Failure recovery logging
- Node input/output serialization
- Error truncation utility

---

## Tasks

### Task 1: Create FlowRunnerHelper

- [x] Extract all database helper methods from FlowRunner
- [x] Extract all serialization helpers from FlowRunner
- [x] Extract `TryRecordFailingNodeLogAsync` failure recovery
- [x] Cache FlowRun entity to eliminate redundant lookups
- [x] Add unified `TruncateError()` method
- [x] Add `List<T>` and array support to `IsSerializableType`

### Task 2: Create FlowRunner

- [x] Main execution loop uses FlowRunnerHelper for all DB/serialization
- [x] Lifecycle event nodes get a fresh CancellationToken (not the expired one)
- [x] Skip `UpdateRunProgressMessageAsync` call when ProgressMessage is null
- [x] Clean separation: only flow orchestration logic in this class

### Task 3: Update FlowBuilder

- [x] Switch from FlowRunner to FlowRunner

### Task 4: Update Documentation

- [x] Fix progress counter description in 03-building-flows.md
- [x] Document FlowRunner architecture and optimization rationale
