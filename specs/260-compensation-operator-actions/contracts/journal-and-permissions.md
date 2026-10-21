# Contract: journal endpoint (activity state view) and permission signal

## Activity state view (runtime seam R-6, generic)

```
GET runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/state-view
```

- Capability link `activity-execution-state-view` (templated) in `elsa.api.runtime`, advertised only when at least one projector is registered.
- Permission `workflows/runtime:read`, then the run inspection grant (`CanInspectStructureAsync`), which also checks the tenant.
- 404 when the run or activity is unknown, the caller lacks the grant, or the run belongs to another tenant. The response never says which.
- Read-only. No query parameters.

### Projector contract

```csharp
public interface IActivityStateViewProjector
{
    string StateTypeAlias { get; }        // the private-state type alias it reads, e.g. "Elsa.CompensationScope.State"
    string ViewKind { get; }              // e.g. "compensation.journal"
    int ViewVersion { get; }              // e.g. 1
    ValueTask<JsonElement> ProjectAsync(ActivityStateViewContext context, CancellationToken cancellationToken = default);
}

public sealed record ActivityStateViewContext(
    WorkflowExecutionState WorkflowExecution,
    ActivityExecutionState ActivityExecution,   // with its private state
    ExecutableNode ExecutableNode,              // from the pinned executable
    IReadOnlyCollection<IncidentState> Incidents,
    bool CanInspectSensitiveValues);
```

At most one projector per alias; a duplicate fails at startup. The runtime picks the projector by the private state's type alias.

### Response

```json
{
  "workflowExecutionId": "wf-42",
  "activityExecutionId": "ae-scope",
  "executableNodeId": "order-scope",
  "activityStatus": "Running",
  "available": true,
  "unavailableReason": null,
  "viewKind": "compensation.journal",
  "viewVersion": 1,
  "canInspectValues": false,
  "view": { }
}
```

| `unavailableReason` | When |
|---|---|
| `StateCleared` | The activity has no private state, such as a completed scope (spec 258 R1). `view` is null. |
| `NoProjector` | The private state's alias has no projector. `view` is null. |

### View `compensation.journal`, version 1

```json
{
  "phase": "AwaitingIntervention",
  "stateVersion": 4,
  "cause": {
    "kind": "fault",
    "step": "Confirm order",
    "activityExecutionId": "ae-7",
    "incidentId": "inc-1",
    "failureType": "ActivityReturnedFault",
    "message": null,
    "messageWithheld": true,
    "requestedBy": null,
    "target": null
  },
  "entries": [
    {
      "id": "e2",
      "step": "Reserve delivery",
      "ordinal": 2,
      "status": "Failed",
      "forwardOutcome": "Uncertain",
      "operationKey": "ae-scope/step/1",
      "forward": { "activityExecutionId": "ae-5", "iterationId": null },
      "handler": { "executableNodeId": "cancel-delivery", "remaining": true },
      "attempts": [
        { "number": 1, "activityExecutionId": "ae-9", "incidentId": "inc-3", "result": "Faulted" },
        { "number": 2, "activityExecutionId": "ae-10", "incidentId": "inc-4", "result": "Faulted" },
        { "number": 3, "activityExecutionId": "ae-11", "incidentId": "inc-5", "result": "Faulted" }
      ],
      "waiting": { "incidentId": "inc-5", "decisions": ["compensation.resolve.effect-absent", "compensation.resolve.effect-accepted", "compensation.abandon"] },
      "resolution": null,
      "origin": null,
      "confirmedBy": null
    }
  ],
  "remaining": ["e2", "e1"],
  "confirmations": [ { "activityExecutionId": "ae-12", "entryIds": ["e0"] } ],
  "handOvers": { "received": ["ae-inner"], "inProgress": [ { "originScope": "ae-inner-2", "batches": [0] } ] },
  "abandonment": null
}
```

| Field | Source and rule |
|---|---|
| `phase`, `entries[].status`, `forwardOutcome`, `operationKey`, `ordinal` | Private state, as stored. |
| `cause.failureType` | The cause incident's failure type. |
| `cause.message` | The cause incident's message only when `CanInspectSensitiveValues`; otherwise `null` with `messageWithheld: true`. |
| `entries[].forward`, `attempts[].activityExecutionId` | Occurrence ids. Studio opens them through the activity-execution inspection endpoint, which applies the sensitive-values gate to captured inputs and outputs. The journal carries no captured value. |
| `entries[].handler.executableNodeId` | The handed-over entry's own handler node, or the step's handler from the pinned structure. `null` when the step has none. |
| `entries[].handler.remaining` | `true` when the entry is eligible and its handler has not completed. |
| `entries[].waiting` | Present while `interventionIncidentId` is set: the incident and the decisions its outcome offers. Absent offers (a wait from before M4) give `[]`. |
| `entries[].resolution` | `{ outcome, incidentId }` for `ResolvedManually`. |
| `entries[].confirmedBy` | The confirmation that lists the entry. |
| `remaining` | Entry ids still to undo, in recovery order (descending ordinal, eligible only). Empty unless the scope can still recover. |
| `abandonment` | `{ incidentId, entryId, fromPhase }` when abandoned. |

Operator reasons and actors are not in the journal: they are on the incidents (OD14).

## Permission signal (FR-416)

`GET runtime/workflows/instances/{workflowExecutionId}` (existing, `workflows/runtime:read`) gains:

```json
{
  "permissions": { "canManage": true, "canInspectValues": false }
}
```

| Field | Rule |
|---|---|
| `canManage` | `IWorkflowRuntimeOperatorAccess.CanManageAsync(run)`: the Identity permission evaluator for `workflows/runtime:manage`, with the caller's tenant and the run as resource. `false` for an untrusted or anonymous principal. |
| `canInspectValues` | `CanInspectSensitiveValuesAsync(run)`, as the response already uses it for redaction. |

- The flag is a display hint. The submit endpoint still enforces the permission (403), and capture still enforces the tenant.
- An older server that omits `permissions` is read as `canManage: false`.

## Error shapes

| Endpoint | Status | Body |
|---|---|---|
| state view | 404 | the runtime's standard not-found problem |
| alteration submit | 401 | unauthenticated (host) |
| alteration submit | 403 | missing `workflows/runtime:manage` |
| alteration submit | 400 | `MissingIdempotencyKey`, `InvalidIdempotencyKey` |
| alteration submit | 409 | `AlterationIdempotencyConflict`: the same key with a different body |
| alteration submit | 422 | `InvalidAlterationRequest`: an unknown kind or an invalid envelope |
| alteration submit | 429 | `AlterationAdmissionBackpressure`, with `Retry-After` |
| plan job | 200 | job `Failed` with an outcome `{ code, message }` from the preflight tables in [operator-actions.md](operator-actions.md) |
