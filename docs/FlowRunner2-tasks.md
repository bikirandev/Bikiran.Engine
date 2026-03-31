# FlowRunner v2 — Task List

## Problem Summary

Three issues found in the current engine:

1. **`NodeType` exposed to application layer** — Custom node developers can accidentally override `NodeType` with `throw new NotImplementedException()` (common IDE scaffold), causing cryptic runtime crashes when the engine reads the property for logging.
2. **No pre-run validation** — Node configuration errors (null names, missing required properties, duplicate names) are only discovered at runtime during execution, making debugging difficult.
3. **Unclear error messages** — Exception messages like "The method or operation is not implemented" don't tell the developer which node failed or what to fix.

---

## Tasks

### Task 1: Remove `NodeType` from the public `IFlowNode` contract

**Goal:** Make `NodeType` an internal concern that developers never see or touch.

**Changes:**

- [x] `IFlowNode.cs` — Remove the `NodeType` default interface member
- [x] `FlowNodeType.cs` — Mark enum as `internal` (only used by engine internals)
- [x] All 9 built-in nodes — Remove the `public FlowNodeType NodeType => ...` line
- [x] `FlowRunnerHelper.cs` — Resolve `NodeType` internally via a type-mapping dictionary instead of reading `node.NodeType`
- [x] `FlowRunnerHelper.cs` — Update `_skipProperties` to remove `"NodeType"`

**Validation:** Custom nodes compile without any `NodeType` property. Built-in node types still log correctly.

---

### Task 2: Add pre-run validation in `FlowBuilder.PrepareAsync()`

**Goal:** Validate all nodes and configuration before the flow starts executing. Throw clear `InvalidOperationException` messages so developers see problems immediately.

**Validations to add:**

- [x] Flow name is not null/empty
- [x] No duplicate node names across all node lists (main + lifecycle)
- [x] Each node's `Name` is valid PascalCase (call `FlowNodeNameValidator.Validate`)
- [x] `MaxExecutionTime` is positive
- [x] `FlowConfig` values are within reasonable bounds

**Changes:**

- [x] `FlowBuilder.cs` — Add `ValidateFlow()` method called from `PrepareAsync()`

---

### Task 3: Improve error messages to be clear and actionable

**Goal:** Every error message should tell the developer: what failed, which node, and what to do about it.

**Changes:**

- [x] `FlowRunner.cs` — Include node name in caught exception messages: `"Node '{nodeName}' failed: {ex.Message}"`
- [x] `FlowRunner.cs` — Improve `max_execution_time_exceeded` to include flow name and timeout value
- [x] `FlowRunner.cs` — Improve lifecycle timeout message similarly
- [x] `FlowRunnerHelper.cs` — Include node name in failure recovery log messages

---

### Task 4: Update documentation

**Changes:**

- [x] `07-custom-nodes.md` — Remove all references to `NodeType` property from custom node guidance
- [x] `04-node-reference.md` — Update Node Type Enum section to note it's internal
- [x] `03-building-flows.md` — Add note about pre-run validation behavior

---

## Completion Checklist

- [x] All tasks implemented
- [x] Build succeeds (`dotnet build`)
- [x] No regressions in existing API surface
