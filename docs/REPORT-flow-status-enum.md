# FlowStatus Enum Refactoring — Scope Report

> **Package:** Bikiran.Engine v1.3.2 → v1.3.2  
> **Date:** 2026-03-24  
> **Objective:** Replace all magic status strings with strongly-typed enums  
> **Status:** ✅ Completed

---

## Identified Enums

### 1. `FlowRunStatus` — Flow execution lifecycle

| Value       | Used In Code | Description                          |
| ----------- | ------------ | ------------------------------------ |
| `Pending`   | Yes          | Created but not yet started          |
| `Running`   | Yes          | Currently executing nodes            |
| `Completed` | Yes          | All main nodes finished successfully |
| `Failed`    | Yes          | Stopped due to error or timeout      |
| `Cancelled` | Docs only    | Cancelled externally (reserved)      |

### 2. `FlowNodeStatus` — Individual node execution state

| Value       | Used In Code  | Description                 |
| ----------- | ------------- | --------------------------- |
| `Pending`   | Yes (default) | Queued but not yet executed |
| `Running`   | Yes           | Currently executing         |
| `Completed` | Yes           | Finished successfully       |
| `Failed`    | Yes           | Finished with an error      |
| `Skipped`   | Docs only     | Node bypassed (reserved)    |

### 3. `FlowScheduleRunStatus` — Schedule trigger outcome

| Value       | Used In Code | Description                       |
| ----------- | ------------ | --------------------------------- |
| `Triggered` | Yes          | Definition triggered successfully |
| `Error`     | Yes          | Failed to trigger definition      |

---

## Scopes (Files to Change)

| #   | File                                        | Change Description                                                             |
| --- | ------------------------------------------- | ------------------------------------------------------------------------------ |
| 1   | **NEW** `Core/FlowRunStatus.cs`             | Create `FlowRunStatus` enum                                                    |
| 2   | **NEW** `Core/FlowNodeStatus.cs`            | Create `FlowNodeStatus` enum                                                   |
| 3   | **NEW** `Core/FlowScheduleRunStatus.cs`     | Create `FlowScheduleRunStatus` enum                                            |
| 4   | `Core/FlowContext.cs`                       | Change `FlowStatus` from `string?` to `FlowRunStatus?`                         |
| 5   | `Core/FlowRunner.cs`                        | Replace all `"running"`, `"completed"`, `"failed"` strings                     |
| 6   | `Core/FlowBuilder.cs`                       | Replace `"pending"` in FlowRun creation                                        |
| 7   | `Database/Entities/FlowRun.cs`              | Change `Status` from `string` to `string` (stored as string via `.ToString()`) |
| 8   | `Database/Entities/FlowNodeLog.cs`          | Same pattern — status stays string in DB, enum used in logic                   |
| 9   | `Database/Entities/FlowSchedule.cs`         | `LastRunStatus` stays `string?` in DB entity                                   |
| 10  | `Scheduling/ScheduledFlowJob.cs`            | Use `FlowScheduleRunStatus` enum `.ToString()`                                 |
| 11  | `Api/FlowRunsController.cs`                 | Status filtering uses string (DB query), no enum change needed                 |
| 12  | `Scheduling/DTOs/FlowScheduleSummaryDTO.cs` | Stays `string?` (API contract)                                                 |

### Design Decision: DB stays `varchar`, code uses enum

The database columns remain `varchar(20)` for backward compatibility. The enum is used in C# code (FlowRunner, FlowBuilder, FlowContext) and converted to lowercase string via a helper when writing to the DB. Reads from DB remain string-based since EF queries happen against string columns.

---

## Verification

- [x] `dotnet build` passes with zero errors
- [x] `FlowContext.FlowStatus` is now `FlowRunStatus?`
- [x] All `"pending"`, `"running"`, `"completed"`, `"failed"` in FlowRunner replaced with enum
- [x] `FlowBuilder.PrepareAsync()` uses `FlowRunStatus.Pending`
- [x] `ScheduledFlowJob` uses `FlowScheduleRunStatus` enum
- [x] Docs updated to reference enum values
- [x] Version bumped to 1.3.2
