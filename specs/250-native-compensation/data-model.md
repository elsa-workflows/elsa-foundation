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
{ "steps": [ { "name": "Reserve inventory", "activity": "x-a1", "compensation": "x-h1" }, … ],
  "afterCompensation": "complete" }
```

Executable node ids are remapped by the structure handler. `afterCompensation` (`complete` | `fault`) is an
activity input that publication pins.

## Private state — type `elsa.compensation-scope.state`, version `1`

```json
{
  "phase": "Compensating",
  "nextStep": 3,
  "currentStepActivityExecutionId": null,
  "nextOrdinal": 3,
  "cause": { "kind": "fault", "step": "Confirm order", "activityExecutionId": "ae-7",
             "incidentId": "inc-2", "faultCode": "order.rejected" },
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

- The fault message is not copied into private state. Inspection reads it from the incident record through the
  existing disclosure policy.
- `handlerActivityExecutionIds` is a list so that M2 retries append attempts without a schema change. M1 writes
  at most one.
- The state is persisted with `ValueProtectionPolicy.InstanceInline`, the precedent used by BPMN and Flowchart
  state.
- A version bump needs an upcaster before any state written by the previous version can be read.

## Inspection projection — `GET …/activity-executions/{scopeActivityExecutionId}/compensation`

```json
{
  "scopeActivityExecutionId": "ae-1",
  "phase": "Compensated",
  "outcome": "Compensated",
  "cause": { "kind": "fault", "step": "Confirm order", "activityExecutionId": "ae-7", "incidentId": "inc-2",
             "faultCode": "order.rejected", "message": { "presence": "Present", "value": "Order rejected" } },
  "steps": [
    { "name": "Reserve inventory", "forward": { "state": "Completed", "activityExecutionId": "ae-3" },
      "recovery": { "state": "Compensated", "ordinal": 1, "activityExecutionIds": ["ae-11"] } },
    { "name": "Reserve delivery", "forward": { "state": "Completed", "activityExecutionId": "ae-5" },
      "recovery": { "state": "Compensated", "ordinal": 2, "activityExecutionIds": ["ae-9"] } },
    { "name": "Confirm order", "forward": { "state": "Faulted", "activityExecutionId": "ae-7" },
      "recovery": { "state": "NotApplicable" } }
  ],
  "recoveryOrder": ["Reserve delivery", "Reserve inventory"]
}
```

- `forward.state`: `NotRun` | `Running` | `Completed` | `Faulted`.
- `recovery.state`: `NotApplicable` (did not complete) | `NotNeeded` (no handler) | `Pending` (`Registered`
  during recovery) | `Registered` (not recovering) | `Running` (`Compensating`) | `Compensated`, plus the M2+
  statuses.
- 404 when the activity execution is not a compensation scope; 403 under the same rule as the activity
  execution read.
