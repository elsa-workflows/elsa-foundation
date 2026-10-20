# Data model: uncertain effects and resumable compensation (M2)

Extends [spec 250 data-model.md](../250-native-compensation/data-model.md). The authored structure is unchanged.

## Scope input

| Input | Type | Default | Rule |
|---|---|---|---|
| `handlerMaxAttempts` | integer | `3` | ≥ 1. The total number of runs of one entry's handler, operator retries included, before the scope waits for intervention. Pinned at publication like every input. |

## Runtime metadata keys

| Key | Set by | Read by |
|---|---|---|
| `runtime.operationKey` | a structural parent, in the child's scheduling metadata | the runtime, to fill `OperationKey` |
| `runtime.targetOperationKey` | a structural parent | the runtime, to fill `TargetOperationKey` |
| `runtime.retryOfActivityExecutionId` | `RetryFaultedActivity/1`, on the successor | inspection |
| `compensation.forwardOutcome` = `unknown` | the scope, in absorption or intervention metadata for an uncertain step | Studio |
| `compensation.scopeActivityExecutionId`, `compensation.entryId`, `compensation.step`, `compensation.attempt`, `compensation.maxAttempts` | the scope, in intervention metadata | Studio, operators |

Compensation scope keys:

- **Forward step:** `operationKey = {scopeActivityExecutionId}/step/{index}`.
- **Handler:** `operationKey = {forwardKey}/compensate` and `targetOperationKey = {forwardKey}`.

## Private state — type `Elsa.CompensationScope.State`, version `2`

```json
{
  "phase": "AwaitingIntervention",
  "nextStepIndex": 3,
  "nextOrdinal": 3,
  "cause": { "kind": "uncertain", "step": "Reserve delivery", "activityExecutionId": "ae-5", "incidentId": "inc-2" },
  "entries": [
    { "id": "e1", "step": "Reserve inventory", "ordinal": 1, "status": "Compensated",
      "forwardOutcome": "Completed", "operationKey": "ae-scope/step/0",
      "forwardActivityExecutionId": "ae-3", "forwardIterationId": null,
      "handlerAttempts": [ { "activityExecutionId": "ae-9", "result": "Completed" } ],
      "interventionIncidentId": null },
    { "id": "e2", "step": "Reserve delivery", "ordinal": 2, "status": "Failed",
      "forwardOutcome": "Uncertain", "operationKey": "ae-scope/step/1",
      "forwardActivityExecutionId": "ae-5", "forwardIterationId": null,
      "handlerAttempts": [
        { "activityExecutionId": "ae-6", "incidentId": "inc-3", "result": "Faulted" },
        { "activityExecutionId": "ae-7", "incidentId": "inc-4", "result": "Faulted" },
        { "activityExecutionId": "ae-8", "incidentId": "inc-5", "result": "Faulted" } ],
      "interventionIncidentId": "inc-5" }
  ]
}
```

| Field | Rule |
|---|---|
| `phase` | M1 values plus `AwaitingIntervention` |
| `cause.kind` | `fault` or `uncertain`. It is set once, when recovery starts, and is never overwritten. |
| `entries[].status` | `Registered`, `NoHandler`, `Uncertain`, `Compensating`, `Compensated` or `Failed` |
| `entries[].forwardOutcome` | `Completed` or `Uncertain`. It keeps its value when `status` changes. |
| `entries[].operationKey` | The key the forward step was given. For entries upgraded from version 1, it is `forwardActivityExecutionId`, which is what M1 participants received as the invocation id. |
| `entries[].handlerAttempts` | Append-only. Each item is `{ activityExecutionId, incidentId?, result: Running \| Completed \| Faulted }`. Only the last item may be `Running`. Its length is never above `handlerMaxAttempts`. |
| `entries[].interventionIncidentId` | Set while the entry is `Failed` and waiting; cleared when its handler is retried. |

## Upcast from version 1

| Version-1 value | Version-2 value |
|---|---|
| `status` | unchanged |
| `handlerActivityExecutionIds[i]` | `handlerAttempts[i] = { activityExecutionId, result: Completed }` when the entry is `Compensated`. The last attempt of a `Compensating` entry becomes `Running`. |
| (missing) `forwardOutcome` | `Completed` |
| (missing) `operationKey` | `forwardActivityExecutionId` |
| (missing) `interventionIncidentId` | `null` |
| `cause` | `kind` is kept (version 1 only has `fault`) |

The persister writes version 2 on the next commit. A version-2 state is never written as version 1.

## Incident evidence (runtime)

| Situation | Status | Outcome action kind | Outcome system source | Outcome metadata |
|---|---|---|---|---|
| Uncertain step with a handler (absorbed) | `Resolved` | `AbsorbFault` | `StructuralFaultAbsorption` | `compensation.forwardOutcome=unknown` |
| Uncertain step without a handler | `Blocking` | `WaitForIntervention` | `StructuralIntervention` | `compensation.forwardOutcome=unknown`, scope, entry and step |
| Handler fault, attempts left (absorbed) | `Resolved` | `AbsorbFault` | `StructuralFaultAbsorption` | `compensation.attempt`, `compensation.maxAttempts` |
| Handler fault, attempts exhausted | `Blocking` | `WaitForIntervention` | `StructuralIntervention` | scope, entry, step, attempt and max attempts |
| Retried by an operator | `Resolved` | `OperatorRetry` | `OperatorRetryAlteration` | `runtime.alteration.jobId`, `runtime.alteration.planId`, submitter and reason |

## Studio view model additions

`forward.state` adds `Uncertain`, `recovery.state` adds `NeedsAction`, and the phase adds `AwaitingIntervention` (labelled "Needs action"). Each row gains `recovery.attempts: { count, max }` and `recovery.incidentId` (set when `NeedsAction`). `IncidentStateSummary` replaces `resolutionAction` with `resolutionOutcome: { actionKind, systemSource?, metadata }`.
