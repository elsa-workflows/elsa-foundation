# Contract: operator actions (runtime seam R-5 and the alteration payloads)

All three actions are submitted through the existing plan endpoint, `POST runtime/workflows/alteration-plans`. Every submission:

- requires `workflows/runtime:manage` (else 403, no plan);
- requires an `Idempotency-Key` header of at most 256 characters (else `MissingIdempotencyKey` or `InvalidIdempotencyKey`);
- names one run by id: `"target": { "executionIds": ["<run>"] }`;
- carries exactly one envelope.

Same key and same body: the existing plan, `submissionDisposition: "Replayed"`. Same key and a different body: a conflict, nothing created (spec 141 FR-041). A job applies all of its staged changes in one checkpoint, or records one failed outcome with a code and applies nothing.

## Retry — `RetryFaultedActivity/1` (existing, one refusal added)

```json
{
  "target": { "executionIds": ["wf-42"] },
  "alterations": [
    { "kind": "RetryFaultedActivity", "schemaVersion": 1,
      "payload": { "sourceActivityExecutionId": "ae-11", "reason": "Delivery service restored" } }
  ]
}
```

`sourceActivityExecutionId` is the latest failed attempt (the faulted handler or step). `reason` is optional, at most 500 characters. Refusals: as in [spec 251](../../251-compensation-uncertainty/contracts/retry-faulted-activity.md), plus:

| Code | When |
|---|---|
| `SourceParentNotRunning` | **New.** The source's parent is not running, for example a scope that a Compensate cancelled. Retrying would start the handler under a cancelled scope (research R9). Checked after `SourceActivityNotFaulted`. |

Effect: unchanged (incident `Resolved` with `OperatorRetry` / `OperatorRetryAlteration`, successor scheduled).

## Resolve and Abandon — `DecideIntervention/1` (new, built-in)

```json
{
  "target": { "executionIds": ["wf-42"] },
  "alterations": [
    { "kind": "DecideIntervention", "schemaVersion": 1,
      "payload": { "incidentId": "inc-5", "decision": "compensation.resolve.effect-absent",
                   "reason": "Refunded manually, ticket FIN-221" } }
  ]
}
```

```json
{ "kind": "DecideIntervention", "schemaVersion": 1,
  "payload": { "incidentId": "inc-5", "decision": "compensation.abandon", "reason": "Order written off by finance" } }
```

| Property | Rule |
|---|---|
| `incidentId` | Required. The waiting incident (the row's "Show incident"). |
| `decision` | Required. One of the codes the requester offered on that incident. |
| `reason` | Required. Trimmed, 1 to 500 characters, no control characters. |

No other property is accepted.

### Preflight (refusals, in this order)

| Code | When |
|---|---|
| `InvalidDecideInterventionPayload` | The payload is malformed, has another property, or the reason is missing, blank, too long or has control characters. |
| `WorkflowTerminal` | The run is terminal (completed, faulted or cancelled). |
| captured-concurrency codes | The run changed artifact, revision or authority since capture (as for every alteration). |
| `IncidentNotFound` | No incident with that id in the run. |
| `InterventionNotPending` | The incident is not `Blocking` with a `WaitForIntervention` outcome from `StructuralIntervention`: someone already retried, resolved or abandoned it, or the run's incident strategy owns it. This is the stale-action code. |
| `DecisionNotOffered` | The decision is not in the outcome's `runtime.interventionDecisions` (including a wait that offered none, such as one that started before M4). |
| `RequesterNotFound` | `runtime.interventionRequestedBy` names no activity execution in the run. |
| `RequesterNotRunning` | The requester is not `Running` (cancelled, completed or faulted). |
| `RequesterWorkClaimed` | The requester's execution scope has claimed scheduler work; try again. |
| `PinnedExecutableUnavailable` | The requester's node is missing from the pinned executable. |

### Effect, in one alteration checkpoint

1. The incident becomes `Resolved`, with outcome:
   - action kind `OperatorDecision`, system source `OperatorDecisionAlteration`, applied at the job's time;
   - metadata `runtime.alteration.jobId`, `runtime.alteration.planId`, `runtime.alteration.submittedBy`, `runtime.alteration.reason`, `runtime.interventionDecision`, `runtime.interventionRequestedBy`.

   The prior `WaitForIntervention` kind and source move to incident metadata.
2. A post-commit intent enqueues a `DeliverInterventionDecision` work item for the requester, id derived from the job and the incident.

The faulted activity is not touched: it stays `Faulted`, with its incident resolved.

### Delivery (R-5 callback)

```csharp
public interface IRuntimeActivityInterventionDecisionHandler
{
    ValueTask<RuntimeStructuralContinuation> OnInterventionDecidedAsync(
        IRuntimeActivityExecutionContext context, ActivityInterventionDecidedContext decision);
}

public sealed record ActivityInterventionDecidedContext(
    string IncidentId,
    string Decision,
    string FaultedActivityExecutionId,
    string FaultedExecutableNodeId,
    string ViaChildActivityExecutionId,   // the requester's direct child on the path to the faulted activity
    string ViaChildExecutableNodeId);
```

- The handler reactivates the requester from its pinned snapshot, like a notification. A requester that is no longer `Running` acks the item without a callback.
- The callback may: schedule children, request a child subtree cancellation (with `Defer` or `Complete` only), stage private state, notify its parent, notify an ancestor (with `Complete` only, as in a child-completion evaluation, so a scope that completes `Done` after a resolve still hands over; research R18), and return `Defer`, `Complete` or `Fault`. It may not absorb or request intervention: there is no fault being evaluated.
- The delivery records `runtime.interventionDecisionProcessedWorkItemId` on the requester; a repeated item is acked.
- A requester that offered decisions must accept any of them while its incident waits. It may ignore a decision for an incident it no longer waits on (a repeat); that changes nothing.

### Offering decisions (R-5 request side)

```csharp
void RequestChildFaultIntervention(
    string incidentId, string reason,
    IReadOnlyDictionary<string, string>? metadata,
    IReadOnlyCollection<string> decisions);
```

- 1 to 8 codes; each matches `^[a-z0-9]+(\.[a-z0-9-]+)+$`, at most 64 characters, and does not start with `runtime.`.
- The requester's CLR type must implement `IRuntimeActivityInterventionDecisionHandler`; otherwise the evaluation faults as for any seam misuse.
- The runtime writes the codes to the outcome metadata as `runtime.interventionDecisions` (JSON array, offer order) and adds `runtime.interventionRequestedBy`.

### Incident transition rule

`WaitForIntervention` → `Resolved` is allowed for exactly two pairs, each on the same activity execution: `OperatorRetry` / `OperatorRetryAlteration` (M2) and `OperatorDecision` / `OperatorDecisionAlteration` (M4). Every other committed outcome stays write-once.

## Explicit targets and execution authority (FR-406)

An explicit execution id is captured when the run exists and belongs to the caller's tenant partition, whatever execution authority started it. The run's authority is still captured for the concurrency check. Query targets keep the authority filter. Another tenant's run, or a missing one, fails `TargetNotFound` and nothing is applied.

## Idempotency and races

| Case | Result |
|---|---|
| Same key, same body | `Replayed`, the existing plan; nothing runs twice. |
| Two keys, same incident | The run's actor runs the jobs one at a time. The first resolves the incident; the second is refused `InterventionNotPending` (or `NoBlockingIncident` for a retry). |
| Retry and decision on the same incident | Same: the first wins, the other is refused. |
| A kill after the alteration checkpoint | The delivery work item is durable and runs once after restart. |
| A kill before the job commits | The job runs after restart, or is refused if the incident no longer waits. |
