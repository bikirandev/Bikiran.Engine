# Flow Definitions — Enhancement Plan

> **Package:** Bikiran.Engine v1.3.2  
> **Date:** 2026-03-24  
> **Status:** Planning  
> **Scope:** Extend the Flow Definitions subsystem with flow lifecycle events, new JSON node support, validation, auth, import/export, versioning, parameter schemas, error handling, and testing.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Phase 0 — Flow Lifecycle Events (OnSuccess / OnFail / OnFinish)](#2-phase-0--flow-lifecycle-events-onsuccess--onfail--onfinish)
3. [Phase 1 — JSON Node Support Expansion](#3-phase-1--json-node-support-expansion)
4. [Phase 2 — FlowJson Validation on Save](#4-phase-2--flowjson-validation-on-save)
5. [Phase 3 — API Authentication & Authorization](#5-phase-3--api-authentication--authorization)
6. [Phase 4 — Definition Import / Export](#6-phase-4--definition-import--export)
7. [Phase 5 — Versioning & Rollback](#7-phase-5--versioning--rollback)
8. [Phase 6 — Parameter Schema & Validation](#8-phase-6--parameter-schema--validation)
9. [Phase 7 — Error Handling & Debugging](#9-phase-7--error-handling--debugging)
10. [Phase 8 — Testing Strategy](#10-phase-8--testing-strategy)
11. [File Impact Matrix](#11-file-impact-matrix)
12. [Verification Checklist](#12-verification-checklist)
13. [Design Decisions](#13-design-decisions)
14. [Open Questions](#14-open-questions)

---

## 1. Overview

The current Flow Definitions feature allows saving reusable JSON flow templates in the database and triggering them via REST API or C# code. It supports a limited set of JSON-compatible nodes (**Wait**, **HttpRequest**, **EmailSend**, **Transform**), while more powerful nodes (**IfElse**, **Parallel**, **Retry**, **WhileLoop**, **DatabaseQuery**) require C# code.

### Goals

- Expand JSON node support to include **IfElse**, **Parallel**, and **Retry**
- Add comprehensive FlowJson validation before persisting definitions
- Introduce API authentication and authorization
- Enable definition import/export for portability
- Improve versioning with explicit activation and rollback
- Add parameter schemas with validation on trigger
- Standardize error handling with structured error codes
- Establish a thorough testing strategy

### Non-Goals

- **DatabaseQueryNode** in JSON (requires generic typed delegate — fundamentally incompatible)
- Full visual flow designer or drag-and-drop UI
- Distributed execution across multiple hosts (planned for v2.0)

---

## 2. Phase 0 — Flow Lifecycle Events (OnSuccess / OnFail / OnFinish)

> **Status:** ✅ Completed (v1.3.2)

### 2.1 Overview

Add three lifecycle event hooks to `FlowBuilder` that let callers attach nodes to run **after** the main node sequence finishes:

| Event         | Fires When                                       | Use Case                      |
| ------------- | ------------------------------------------------ | ----------------------------- |
| **OnSuccess** | All main nodes completed without failure         | Success logging, cleanup      |
| **OnFail**    | The flow ended with a failure (error or timeout) | Alert notifications, rollback |
| **OnFinish**  | Always, after success/fail handlers have run     | Final cleanup, audit logging  |

**Execution order:** Main nodes → OnSuccess _or_ OnFail → OnFinish

### 2.2 FlowBuilder API

New fluent methods on `FlowBuilder`:

```csharp
.OnSuccess(IFlowNode node)   // called once per handler; can chain multiple
.OnFail(IFlowNode node)
.OnFinish(IFlowNode node)
```

**Usage example:**

```csharp
var serviceId = await FlowBuilder
    .Create("add_domain")
    .AddNode(new CfDNSAddNode("add_cf_record") { ... })
    .AddNode(new WaitNode("wait_for_dns") { DelayMs = 15000 })
    .OnSuccess(new SuccessLogNode("on_success_log") { Param1 = "" })
    .OnFail(new FailLogNode("on_fail_log") { Param1 = "" })
    .OnFinish(new FinishLogNode("on_finish_log") { Param1 = "" })
    .StartAsync();
```

### 2.3 FlowContext Additions

Expose flow outcome information to lifecycle event nodes:

```csharp
public FlowRunStatus? FlowStatus { get; internal set; }   // FlowRunStatus.Completed or FlowRunStatus.Failed
public string? FlowError { get; internal set; }            // error message if failed, null on success
```

### 2.4 FlowRunner Changes

After the main node loop completes:

1. Determine `flowError` (null = success, non-null = failure)
2. Set `context.FlowStatus` and `context.FlowError`
3. If success and `OnSuccessNodes` is not empty → execute them sequentially (failures logged, do not change flow status)
4. If failure and `OnFailNodes` is not empty → execute them sequentially (failures logged, do not change flow status)
5. Always: if `OnFinishNodes` is not empty → execute them sequentially (failures logged, do not change flow status)
6. Lifecycle event nodes are logged in the node log table with their own entries if `EnableNodeLogging` is on

### 2.5 Files Changed

| File                        | Change                                                                                        |
| --------------------------- | --------------------------------------------------------------------------------------------- |
| `Core/FlowBuilder.cs`       | Add `_onSuccessNodes`, `_onFailNodes`, `_onFinishNodes` lists, fluent methods, pass to runner |
| `Core/FlowRunner.cs`        | Execute lifecycle nodes after main loop                                                       |
| `Core/FlowContext.cs`       | Add `FlowStatus` and `FlowError` properties                                                   |
| `docs/03-building-flows.md` | Document lifecycle events                                                                     |
| `docs/10-examples.md`       | Add lifecycle event example                                                                   |

---

## 3. Phase 1 — JSON Node Support Expansion

### 2.1 Recursive Node Parsing _(blocks 2.2 – 2.4)_

**Problem:** The current parser handles a flat list of nodes. Nested structures (branches, inner nodes, loop bodies) require recursive deserialization.

**Changes:**

- Refactor `FlowDefinitionParser.Parse()` — extract the node-building switch into a reusable method:
  ```csharp
  private IFlowNode? BuildNode(JsonElement nodeElement, Dictionary<string, string> parameters, BikiranEngineOptions options)
  ```
- This method is called recursively for nested node arrays
- **File:** `Definitions/FlowDefinitionParser.cs`

---

### 2.2 IfElse Node JSON Support _(depends on 2.1)_

**Problem:** `IfElseNode.Condition` is a `Func<FlowContext, bool>` delegate — cannot be represented in JSON.

**Solution:** Create a shared expression evaluator that evaluates string-based conditions against `FlowContext` values at runtime.

**JSON Schema:**

```json
{
  "type": "IfElse",
  "name": "check_status",
  "params": {
    "condition": "$ctx.order_status == \"active\" && $ctx.total >= 100",
    "trueBranch": [
      {
        "type": "HttpRequest",
        "name": "notify_api",
        "params": { "url": "..." }
      }
    ],
    "falseBranch": [
      {
        "type": "EmailSend",
        "name": "alert_admin",
        "params": { "toEmail": "..." }
      }
    ]
  }
}
```

**New File:** `Core/ExpressionEvaluator.cs`

- Extract and generalize the expression evaluation logic from `HttpRequestNode.ValidateExpression()`
- Support `$ctx.key` references (reads from `FlowContext` via `context.Get<T>(key)`)
- Support operators: `==`, `!=`, `>`, `<`, `>=`, `<=`, `&&`, `||`
- Support string and numeric comparisons
- Support nested path access: `$ctx.result.status`

**File:** `Definitions/FlowDefinitionParser.cs` — add IfElse case to `BuildNode()`

---

### 2.3 Parallel Node JSON Support _(depends on 2.1)_

**JSON Schema:**

```json
{
  "type": "Parallel",
  "name": "fetch_all_data",
  "params": {
    "branches": [
      [
        {
          "type": "HttpRequest",
          "name": "fetch_users",
          "params": { "url": "..." }
        }
      ],
      [
        {
          "type": "HttpRequest",
          "name": "fetch_orders",
          "params": { "url": "..." }
        }
      ]
    ],
    "waitAll": true,
    "abortOnBranchFailure": false
  }
}
```

- Each branch is an array of node objects parsed recursively via `BuildNode()`
- `waitAll` and `abortOnBranchFailure` are plain booleans
- **File:** `Definitions/FlowDefinitionParser.cs`

---

### 2.4 Retry Node JSON Support _(depends on 2.1)_

**JSON Schema:**

```json
{
  "type": "Retry",
  "name": "resilient_call",
  "params": {
    "inner": {
      "type": "HttpRequest",
      "name": "flaky_api",
      "params": { "url": "...", "maxRetries": 0 }
    },
    "maxAttempts": 3,
    "delayMs": 2000,
    "backoffMultiplier": 1.5
  }
}
```

- `inner` is a single node object parsed recursively via `BuildNode()`
- `RetryOn` delegate omitted — defaults to "retry on any failure"
- **File:** `Definitions/FlowDefinitionParser.cs`

---

### 2.5 WhileLoop & DatabaseQuery Decisions

| Node              | Decision    | Reason                                                                                                                          |
| ----------------- | ----------- | ------------------------------------------------------------------------------------------------------------------------------- |
| **WhileLoopNode** | **Support** | Use expression-based conditions (same evaluator as IfElse). Require `maxIterations` (hard cap: 1000) to prevent infinite loops. |
| **DatabaseQuery** | **C#-only** | Requires `Func<TContext, CancellationToken, Task<object?>>` — fundamentally incompatible with JSON.                             |

**WhileLoop JSON Schema** (if approved):

```json
{
  "type": "WhileLoop",
  "name": "poll_status",
  "params": {
    "condition": "$ctx.poll_result != \"ready\"",
    "maxIterations": 50,
    "iterationDelayMs": 1000,
    "body": [
      { "type": "HttpRequest", "name": "check_api", "params": { "url": "..." } }
    ]
  }
}
```

- **File:** `Definitions/FlowDefinitionParser.cs`, update `docs/05-flow-definitions.md`

---

## 4. Phase 2 — FlowJson Validation on Save

### 3.1 Create `FlowJsonValidator`

**New File:** `Definitions/FlowJsonValidator.cs`

**Validation Rules:**

| Rule                       | Level   | Description                                                                    |
| -------------------------- | ------- | ------------------------------------------------------------------------------ |
| Valid JSON syntax          | Error   | Must be parseable JSON                                                         |
| Required top-level fields  | Error   | `name` (string), `nodes` (non-empty array)                                     |
| Valid `config` block       | Warning | Only known fields: `maxExecutionTimeSeconds`, `onFailure`, `enableNodeLogging` |
| Node structure             | Error   | Each node must have `type` (string), `name` (string), `params` (object)        |
| Recognized node type       | Error   | Must be a built-in or registered custom type                                   |
| No duplicate node names    | Error   | All node names within a flow must be unique                                    |
| Recursive nested validity  | Error   | Branches, inner, body arrays validated recursively                             |
| HttpRequest `allowedHosts` | Warning | Must contain valid hostnames if present                                        |
| IfElse `condition`         | Error   | Must be a non-empty string expression                                          |
| WhileLoop `maxIterations`  | Error   | Required, must be between 1 and 1000                                           |

**Return Type:**

```csharp
public class FlowValidationResult
{
    public bool IsValid { get; set; }
    public List<FlowValidationError> Errors { get; set; }
}

public class FlowValidationError
{
    public string Path { get; set; }     // e.g., "nodes[2].params.trueBranch[0]"
    public string Message { get; set; }
    public string Severity { get; set; } // "error" | "warning"
}
```

---

### 3.2 Integrate Validation in Controller _(depends on 3.1)_

- Call `FlowJsonValidator.Validate()` in POST and PUT endpoints of `FlowDefinitionsController`
- Return 400 with `{ "error": true, "code": "INVALID_FLOW_JSON", "message": "...", "details": [...errors] }`
- **File:** `Api/FlowDefinitionsController.cs`

---

### 3.3 Validate-Only Endpoint _(depends on 3.1)_

```
POST /api/bikiran-engine/definitions/validate
Content-Type: application/json

{ "flowJson": "..." }
```

- Returns validation result without saving
- **File:** `Api/FlowDefinitionsController.cs`

---

## 5. Phase 3 — API Authentication & Authorization

### 4.1 Auth Options

Add to `BikiranEngineOptions`:

```csharp
/// <summary>Named ASP.NET Core authorization policy for engine endpoints.</summary>
public string? AuthorizationPolicy { get; set; }

/// <summary>Shortcut: require any authenticated user.</summary>
public bool RequireAuthentication { get; set; } = false;
```

**File:** `Extensions/BikiranEngineOptions.cs`

---

### 4.2 Apply Auth to Endpoints _(depends on 4.1)_

- Apply configured policy using a custom `IControllerModelConvention` that adds `[Authorize]` to all engine controllers
- If `RequireAuthentication` is true, apply `[Authorize]` without a named policy
- If `AuthorizationPolicy` is set, apply `[Authorize(Policy = "...")]`
- **File:** `Extensions/EndpointExtensions.cs`

---

### 4.3 Track Authenticated User _(depends on 4.2)_

- Populate `FlowDefinition.LastModifiedBy` from `HttpContext.User` claims on create/update
- Populate `FlowDefinitionRun.TriggerUserId` from `HttpContext.User` claims on trigger
- **Files:** `Api/FlowDefinitionsController.cs`, `Definitions/FlowDefinitionRunner.cs`

---

## 6. Phase 4 — Definition Import / Export

### 5.1 Export Single Definition

```
GET /api/bikiran-engine/definitions/{key}/export
```

**Response:** Portable JSON containing `definitionKey`, `displayName`, `description`, `tags`, `flowJson`, `parameterSchema`. Excludes internal IDs, timestamps, and version numbers.

---

### 5.2 Import Definition

```
POST /api/bikiran-engine/definitions/import
Content-Type: application/json

{ "definitionKey": "...", "displayName": "...", "description": "...", "tags": "...", "flowJson": "...", "parameterSchema": "..." }
```

- If key exists: creates a new version
- If key is new: creates v1
- Runs FlowJson validation before import

---

### 5.3 Bulk Export

```
GET /api/bikiran-engine/definitions/export-all
```

- Returns all active (non-deleted) definitions as a JSON array
- **File (all):** `Api/FlowDefinitionsController.cs`

---

## 7. Phase 5 — Versioning & Rollback

### 6.1 Activate Specific Version

```
PATCH /api/bikiran-engine/definitions/{key}/versions/{version}/activate
```

- Sets specified version as active, deactivates all others for that key
- Update `FlowDefinitionRunner.TriggerAsync()` to query _active_ version instead of _latest_
- **Migration:** Existing definitions auto-activate their latest version

---

### 6.2 Version Diff

```
GET /api/bikiran-engine/definitions/{key}/versions/{v1}/diff/{v2}
```

- Returns both FlowJson payloads with metadata for client-side diffing:
  ```json
  {
    "definitionKey": "...",
    "version1": { "version": 1, "flowJson": "...", "timeCreated": ... },
    "version2": { "version": 2, "flowJson": "...", "timeCreated": ... }
  }
  ```

---

### 6.3 Optimistic Concurrency (Prevent Accidental Overwrites)

- Add `ETag` / `If-Match` header support on the PUT endpoint
- ETag value = current version number
- If client sends stale version → return **409 Conflict**
- **File:** `Api/FlowDefinitionsController.cs`

---

## 8. Phase 6 — Parameter Schema & Validation

### 7.1 Add ParameterSchema Column

**New column on `FlowDefinition`:**

```csharp
/// <summary>JSON schema describing expected trigger parameters.</summary>
[MaxLength(2000)]
public string? ParameterSchema { get; set; }
```

**Schema format (simplified):**

```json
{
  "parameters": [
    {
      "name": "email",
      "type": "string",
      "required": true,
      "default": null,
      "description": "Recipient email address"
    },
    {
      "name": "retryCount",
      "type": "number",
      "required": false,
      "default": "3",
      "description": "Number of retry attempts"
    }
  ]
}
```

**Files:** `Database/Entities/FlowDefinition.cs`, `Database/Migration/SchemaMigrator.cs`, `Definitions/DTOs/FlowDefinitionSaveRequestDTO.cs`

---

### 7.2 Validate Parameters on Trigger _(depends on 7.1)_

When triggering a definition with a parameter schema:

- Check all required parameters are present
- Validate types match (string, number, boolean)
- Apply defaults for missing optional parameters
- Return 400 with detailed errors if validation fails

**Files:** `Definitions/FlowDefinitionRunner.cs`, `Api/FlowDefinitionsController.cs`

---

### 7.3 Auto-Extract Parameters

```
POST /api/bikiran-engine/definitions/extract-parameters
Content-Type: application/json

{ "flowJson": "..." }
```

- Scans FlowJson for `{{placeholder}}` patterns
- Returns list of discovered parameter names (excluding system params like `today_date`, `unix_now`, `year`, `month`)
- Useful for auto-generating parameter schemas
- **File:** `Definitions/FlowDefinitionParser.cs` or new utility

---

## 9. Phase 7 — Error Handling & Debugging

### 8.1 Structured Error Codes

**New File:** `Api/ErrorCodes.cs`

```csharp
public static class ErrorCodes
{
    public const string DefinitionNotFound    = "DEFINITION_NOT_FOUND";
    public const string InvalidFlowJson       = "INVALID_FLOW_JSON";
    public const string ParameterRequired     = "PARAMETER_REQUIRED";
    public const string ParameterTypeMismatch = "PARAMETER_TYPE_MISMATCH";
    public const string VersionConflict       = "VERSION_CONFLICT";
    public const string DefinitionInactive    = "DEFINITION_INACTIVE";
    public const string TriggerFailed         = "TRIGGER_FAILED";
    public const string ValidationOnly        = "VALIDATION_RESULT";
}
```

Standardize response format across all definition endpoints:

```json
{ "error": true, "code": "DEFINITION_NOT_FOUND", "message": "...", "details": [...] }
```

---

### 8.2 Dry-Run Execution

```
POST /api/bikiran-engine/definitions/{key}/dry-run
Content-Type: application/json

{ "parameters": { "email": "test@example.com" } }
```

- Parses the definition and validates parameters
- Simulates the node sequence **without executing** any nodes
- Returns: list of nodes that would execute, resolved parameters, any validation warnings

**Response:**

```json
{
  "definitionKey": "welcome_email_flow",
  "version": 2,
  "resolvedNodes": [
    { "sequence": 1, "name": "brief_delay", "type": "Wait" },
    { "sequence": 2, "name": "send_welcome", "type": "EmailSend" }
  ],
  "resolvedParameters": {
    "email": "test@example.com",
    "today_date": "2026-03-24",
    "unix_now": "1742832000"
  },
  "warnings": []
}
```

**Files:** `Api/FlowDefinitionsController.cs`, `Definitions/FlowDefinitionRunner.cs`

---

### 8.3 Enhanced Node Error Context

When a node fails during definition-triggered execution, enrich the error log with:

- Definition key and version
- Node sequence position in the flow
- Resolved parameter values (sensitive values redacted)

**Files:** `Core/FlowRunner.cs`, `Definitions/FlowDefinitionRunner.cs`

---

## 10. Phase 8 — Testing Strategy

### 9.1 Test Project Setup

- Create `Bikiran.Engine.Tests` (xUnit + FluentAssertions + Moq)
- Reference `Bikiran.Engine` project
- Use InMemory EF Core provider for database tests

---

### 9.2 Test Categories

| Category                   | Scope                                                                                      |
| -------------------------- | ------------------------------------------------------------------------------------------ |
| **FlowDefinitionParser**   | All node types (existing + new), placeholders, custom nodes, SSRF, invalid JSON, recursion |
| **FlowJsonValidator**      | Valid/invalid inputs, missing fields, unknown types, duplicates, deep nesting              |
| **FlowDefinitionRunner**   | Trigger valid/invalid keys, param merging, inactive/deleted definitions, schema validation |
| **ExpressionEvaluator**    | All operators, `$ctx` references, null/missing keys, nested paths, edge cases              |
| **Controller Integration** | All CRUD endpoints, versioning, trigger, validation errors, import/export round-trip       |

---

### 9.3 Key Test Cases

**FlowDefinitionParser:**

- Parse Wait node with valid/invalid `delayMs`
- Parse HttpRequest with SSRF-blocked host — throws `InvalidOperationException`
- Parse IfElse with nested branches containing multiple node types
- Parse Parallel with 3 branches, each containing 2 nodes
- Parse Retry wrapping an HttpRequest node
- Parse WhileLoop with body nodes and expression condition
- Placeholder replacement with missing parameters — leaves `{{key}}` unreplaced
- Custom node registration and resolution via reflection

**FlowJsonValidator:**

- Valid minimal flow: `{ "name": "test", "nodes": [{ "type": "Wait", "name": "w", "params": { "delayMs": 100 } }] }`
- Missing `name` → error at path `$.name`
- Empty `nodes` array → error at path `$.nodes`
- Unknown node type → error at path `$.nodes[0].type`
- Duplicate node names → error listing both positions
- Deeply nested IfElse (3 levels) validates correctly
- WhileLoop missing `maxIterations` → error
- WhileLoop `maxIterations` > 1000 → error

**ExpressionEvaluator:**

- `$ctx.status == "active"` with context `{ status: "active" }` → true
- `$ctx.count >= 10` with context `{ count: 15 }` → true
- `$ctx.a == "x" && $ctx.b == "y"` — compound conditions
- `$ctx.missing == "value"` — missing key returns null, comparison → false
- Nested: `$ctx.result.status == "ok"` — nested path resolution

**Controller Integration (WebApplicationFactory):**

- POST → 201 + definition created with v1
- PUT → 200 + version incremented
- GET `/{key}` → returns latest active version
- GET `/{key}/versions` → returns all versions
- DELETE → soft-deletes all versions
- POST `/{key}/trigger` → 200 + returns ServiceId
- POST `/{key}/trigger` with missing required param → 400 + `PARAMETER_REQUIRED`
- POST `/validate` with invalid JSON → 400 + validation errors
- POST `/import` + GET `/{key}/export` → round-trip integrity
- PATCH `/{key}/versions/1/activate` → v1 becomes active, trigger uses v1

---

## 11. File Impact Matrix

| File                                               | Phase(s)      | Change Type                  |
| -------------------------------------------------- | ------------- | ---------------------------- |
| `Core/FlowBuilder.cs`                              | 0             | Add lifecycle event methods  |
| `Core/FlowRunner.cs`                               | 0, 7          | Lifecycle execution, logging |
| `Core/FlowContext.cs`                              | 0             | Add FlowStatus, FlowError    |
| `Definitions/FlowDefinitionParser.cs`              | 1             | Major refactor               |
| `Definitions/FlowDefinitionRunner.cs`              | 5, 6, 7       | Modify                       |
| `Api/FlowDefinitionsController.cs`                 | 2, 3, 4, 5, 7 | Modify (many new endpoints)  |
| `Database/Entities/FlowDefinition.cs`              | 6             | Add column                   |
| `Database/Migration/SchemaMigrator.cs`             | 6             | Add migration                |
| `Definitions/DTOs/FlowDefinitionSaveRequestDTO.cs` | 6             | Add field                    |
| `Extensions/BikiranEngineOptions.cs`               | 3             | Add properties               |
| `Extensions/EndpointExtensions.cs`                 | 3             | Add auth logic               |
| `Nodes/HttpRequestNode.cs`                         | 1             | Extract evaluator            |
| `docs/03-building-flows.md`                        | 0             | Document lifecycle events    |
| `docs/05-flow-definitions.md`                      | 1             | Update docs                  |
| `docs/10-examples.md`                              | 0             | Add lifecycle example        |
| **NEW** `Core/ExpressionEvaluator.cs`              | 1             | Create                       |
| **NEW** `Definitions/FlowJsonValidator.cs`         | 2             | Create                       |
| **NEW** `Api/ErrorCodes.cs`                        | 7             | Create                       |
| **NEW** `Bikiran.Engine.Tests/`                    | 8             | Create project               |

---

## 12. Verification Checklist

- [x] `dotnet build` passes with zero errors and zero warnings
- [ ] All unit tests pass (parser, validator, runner, expression evaluator)
- [ ] All integration tests pass (WebApplicationFactory controller tests)
- [x] **Lifecycle Events:** `.OnSuccess()` executes only on successful flow completion
- [x] **Lifecycle Events:** `.OnFail()` executes only when the flow fails
- [x] **Lifecycle Events:** `.OnFinish()` always executes after success/fail handlers
- [x] **Lifecycle Events:** Lifecycle node failures are logged but do not change flow status
- [ ] **IfElse:** Create definition with IfElse node → trigger with different params → correct branch executes
- [ ] **Parallel:** Create definition with Parallel node → all branches execute concurrently
- [ ] **Retry:** Create definition wrapping a failing HttpRequest → retries occur as configured
- [ ] **Validation:** POST invalid FlowJson → 400 with detailed structured errors
- [ ] **Import / Export:** Export → modify → import back → new version created successfully
- [ ] **Version rollback:** Create v1 and v2 → activate v1 → trigger → v1 executes
- [ ] **Parameter validation:** Trigger with missing required param → 400 with `PARAMETER_REQUIRED`
- [ ] **Schema migration:** Run on existing database → `ParameterSchema` column added without data loss

---

## 13. Design Decisions

| Decision                       | Choice                               | Rationale                                                             |
| ------------------------------ | ------------------------------------ | --------------------------------------------------------------------- |
| DatabaseQueryNode JSON support | **No** — C#-only                     | Requires generic typed delegate; fundamentally incompatible with JSON |
| Expression evaluator           | Extract from HttpRequestNode, extend | Proven logic exists; add `$ctx.key` references for FlowContext access |
| Auth mechanism                 | ASP.NET Core authorization policies  | Standard pattern; host app configures policy, engine applies it       |
| Versioning model               | Explicitly activated version         | More predictable than "always latest"; supports safe rollback         |
| Soft deletes                   | Keep `TimeDeleted == 0` pattern      | Consistent with existing codebase convention                          |
| Parameter schema format        | Simplified custom format             | Lighter than JSON Schema (draft-07); sufficient for v1 needs          |
| Controller vs Minimal API      | Keep controllers                     | Less disruption; apply auth via convention. Migrate in future major   |

---

## 14. Open Questions

| #   | Question                                                                          | Recommendation                                                  |
| --- | --------------------------------------------------------------------------------- | --------------------------------------------------------------- |
| 1   | Support WhileLoop in JSON with expression conditions + maxIterations cap (1000)?  | **Yes** — reuses IfElse evaluator; cap prevents infinite loops  |
| 2   | Keep controller-based routing or migrate to minimal API route groups for auth?    | **Keep controllers** for now; migrate in a future major version |
| 3   | Use simplified custom parameter schema or adopt JSON Schema (draft-07)?           | **Custom simplified** for v1; evaluate JSON Schema for v2       |
| 4   | Should dry-run resolve conditional branches (simulate which path would be taken)? | **No** for v1 — just list all nodes regardless of branching     |
| 5   | Should export include run history stats (total runs, last run time)?              | **No** — keep export portable and lightweight                   |
