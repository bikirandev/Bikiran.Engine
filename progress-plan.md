# Time-Weighted Progress Plan

## Goal
Replace the naive step-count progress (`CompletedNodes / TotalNodes`) with a
time-weighted model that produces a more accurate and interactive progress
percentage during a flow run.

---

## Concept

### Weighted (post-node) progress
Each node declares an approximate execution time (`ApproxExecutionTime`).
After a node finishes, the cumulative weight of all completed nodes is divided
by the total weight of all nodes.

```
weightedProgress = CompletedApproxMs / TotalApproxMs * 100
```

Example: node A = 2 s, node B = 1 s.
- After A: 2000 / 3000 * 100 = 66.67 %
- After B: 3000 / 3000 * 100 = 100 %

### Live (intra-node) progress
While a node is running, the elapsed time within that node is used to
interpolate progress smoothly up to—but never exceeding—the node's own weight.

```
intraNodeContribution = min(elapsedMsInCurrentNode, CurrentNodeApproxMs)
liveProgress = (CompletedApproxMs + intraNodeContribution) / TotalApproxMs * 100
```

Example: node A = 5 s, after 2 s inside A:
```
liveProgress = (0 + min(2000, 5000)) / 5000 * 100 = 40 %
```

---

## Implementation Steps

### Step 1 — `IFlowNode`: add `ApproxExecutionTime`
- Add `TimeSpan ApproxExecutionTime { get; set; }` to `IFlowNode` with a
  default value of `TimeSpan.FromSeconds(1)`.
- Implement the property in every concrete node class
  (`StartingNode`, `EndingNode`, `HttpRequestNode`, `EmailSendNode`,
  `DatabaseQueryNode`, `IfElseNode`, `WhileLoopNode`, `ParallelNode`,
  `RetryNode`, `TransformNode`, `WaitNode`).

### Step 2 — Database: new columns
**`FlowNodeLog`** — add:
- `ApproxExecutionMs` (long) — the node's declared approx time at run-time.

**`FlowRun`** — add:
- `TotalApproxMs` (long) — sum of all main nodes' `ApproxExecutionTime` in ms.
- `CompletedApproxMs` (long) — rolling sum of completed nodes' approx times.
- `CurrentNodeApproxMs` (long) — approx time of the currently executing node.
- `CurrentNodeStartedAtMs` (long) — UTC ms when the current node began.

Run a schema migration (`SchemaMigrator`) to add these columns.

### Step 3 — `FlowRunnerHelper`: persist new fields
- Before execution starts, compute `TotalApproxMs` from the main-node list and
  save it to `FlowRun`.
- When a node starts, set `CurrentNodeApproxMs` and `CurrentNodeStartedAtMs` on
  `FlowRun`.
- When a node completes, add its approx time to `CompletedApproxMs`, clear
  `CurrentNodeApproxMs` / `CurrentNodeStartedAtMs`, and save
  `ApproxExecutionMs` to the `FlowNodeLog` row.

### Step 4 — `FlowRunsController`: update progress endpoints
**`GET /runs/{serviceId}`** — add to the response body:
```json
{
  "approxTotalMs": 8000,
  "completedApproxMs": 3000,
  "weightedProgressPercent": 37.5,
  "liveProgressPercent": 50.0
}
```

**`GET /runs/{serviceId}/progress`** — update to return the same four fields
instead of the old step-count `percent`.

Live percent is calculated in the controller by reading
`CurrentNodeStartedAtMs` and `CurrentNodeApproxMs` from `FlowRun` and
computing `intraNodeContribution = min(utcNowMs - CurrentNodeStartedAtMs, CurrentNodeApproxMs)`.

### Step 5 — Docs update
Update all affected documentation files:
- `docs/04-node-reference.md` — document the `ApproxExecutionTime` property.
- `docs/08-admin-api.md` — update the progress endpoint response schema.
- `docs/09-database-reference.md` — document the new `FlowRun` and
  `FlowNodeLog` columns.
- `README.md` — mention time-weighted progress in the feature list.
