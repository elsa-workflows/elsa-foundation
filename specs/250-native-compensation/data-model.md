# Data model: native compensation scopes

No new tables. Authored and executable structure live in the workflow artifact. The journal lives in the
scope's private state, committed with the runtime checkpoint like Flowchart and BPMN state.

## Authored structure — kind `elsa.compensation-scope.structure`, schema `1.0.0`

```json
{
  "steps": [
    { "name": "Reserve inventory", "activity": { "nodeId": "a1", "…": "ActivityNode" },
      "compensation": { "nodeId": "h1", "…": "ActivityNode" } },
    { "name": "Reserve delivery", "activity": { "nodeId": "a2" }, "compensation": { "nodeId": "h2" } },
    { "name": "Confirm order", "activity": { "nodeId": "a3" }, "compensation": null }
  ]
}
```

- `name`: required, unique (ordinal comparison after trimming) within the scope. It is the row label and the
  stable key in slot names, diagnostics and inspection.
- `activity`: the forward activity, nullable while drafting. A null forward activity is a draft validation
  error.
- `compensation`: optional handler.

Child slots, declared with `[ActivityChildSlot]` like Switch cases:

| Slot | Template | Cardinality |
|---|---|---|
| Step | `CompensationScope.Step[{name}]` | single, collection `steps`, child `activity`, label `name` |
| Compensation | `CompensationScope.Compensation[{name}]` | single, collection `steps`, child `compensation`, label `name` |

## Executable structure — same kind and schema

```json
{ "steps": [ { "name": "Reserve inventory", "activity": "x-a1", "compensation": "x-h1" }, … ] }
```

Executable node ids are remapped by the structure handler. `faultAfterCompensation` (boolean, default `false`) is
an ordinary activity input that publication pins; it is not part of the structure payload.

## Private state — type `Elsa.CompensationScope.State`, version `1`

```json
{
  "phase": "Compensating",
  "nextStepIndex": 3,
  "nextOrdinal": 3,
  "cause": { "kind": "fault", "step": "Confirm order", "activityExecutionId": "ae-7", "incidentId": "inc-2" },
  "entries": [
    { "id": "e1", "step": "Reserve inventory", "ordinal": 1, "status": "Registered",
      "forwardActivityExecutionId": "ae-3", "forwardIterationId": null,
      "handlerActivityExecutionIds": [] },
    { "id": "e2", "step": "Reserve delivery", "ordinal": 2, "status": "Compensating",
      "forwardActivityExecutionId": "ae-5", "forwardIterationId": null,
      "handlerActivityExecutionIds": [] }
  ]
}
```

`nextStepIndex` is the index of the next step to schedule. It advances when the scope schedules a step, in the same
staged state as that schedule request, so the running step is always `nextStepIndex - 1`: execution schedules step 0
and stages `1`; the completion of step `i` appends its entry and, unless `i` is the last step, schedules step `i + 1`
and stages `i + 2`, all in one commit. When `i` is the last step, the same commit enters `Completed` and the scope
completes with `Done` without scheduling another step (`contracts/semantics.md`). A fault is the running step's only
if it comes from step `nextStepIndex - 1`.

`nextOrdinal` is the completion ordinal the next entry receives. It starts at `1` and always equals one more than the
highest ordinal in `entries`: appending an entry takes `nextOrdinal` as the entry's ordinal and increments it in the
same commit. M1 runs one step at a time, so only one completion appends at a time; a later milestone that runs steps
concurrently must define its own allocation rule.

The runtime clears structural private state when the scope completes. The journal therefore lives only while the
scope is active. Completed-scope inspection (M1) is derived from run data (FR-019); late compensation of a
completed scope (OD3, M3) needs the runtime to keep a durable journal past completion, which does not exist yet.

- The fault message is not copied into private state. Inspection reads it from the incident record through the
  existing disclosure policy.
- `handlerActivityExecutionIds` is a list so that M2 retries append attempts without a schema change. M1 writes
  at most one.
- The state is persisted with `ValueProtectionPolicy.InstanceInline`, the precedent used by BPMN and Flowchart
  state.
- A version bump needs an upcaster before any state written by the previous version can be read.

## Inspection view model (Studio, derived)

Studio derives this view from the run data it already loads; there is no inspection endpoint (FR-019).

| Field | Source |
|---|---|
| Scope execution | latest execution whose authored or executable node id is the scope's |
| Forward state per step | latest child of that execution matching the step's forward node, excluding handlers |
| Recovery state per step | `NotNeeded` when the step has no handler; otherwise the latest handler execution among the scope execution's children (scheduling cause `compensation`) matching the step's handler node, `Pending` when the scope is compensating and none exists yet, `Kept` when the scope has ended (completed, cancelled, compensated or faulted) without running it, or `Registered` |
| Recovery order | handler executions ordered by `scheduledAt` |
| Phase | scope status; `Compensated` when its outcomes include `Compensated` |
| Cause | the faulted forward step and the incident on its execution (failure type and message, under the incident disclosure policy) |

`forward.state`: `NotRun` | `Running` | `Completed` | `Faulted` | `Cancelled`. `recovery.state`: `NotApplicable` (did not
complete) | `NotNeeded` (no handler) | `Registered` (eligible, scope still running) | `Kept` (scope ended without undoing it) | `Pending` | `Running` | `Compensated` |
`Failed`. Run lists carry the parent execution inside `provenance`, so matching reads it there.
