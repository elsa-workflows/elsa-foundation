# Contract: `RetryFaultedActivity/1` alteration (#2747 core)

Submitted through the existing alteration plan endpoints. It has the same authorization (`WorkflowRuntimeManage`), the same idempotency (`Idempotency-Key`), the same fencing and the same captured-concurrency checks as every alteration.

## Envelope

```json
{ "kind": "RetryFaultedActivity/1", "payload": { "sourceActivityExecutionId": "ae-8", "reason": "Delivery service restored" } }
```

`sourceActivityExecutionId` is required. `reason` is optional and at most 500 characters. No other property is accepted.

## Preflight (rejections)

| Code | When |
|---|---|
| `InvalidRetryFaultedActivityPayload` | The payload is malformed. |
| `WorkflowTerminal` | The run is terminal. Retrying a faulted run is out of scope. |
| `SourceActivityNotFound` | The source execution does not exist in the run. |
| `SourceActivityNotFaulted` | The source status is not `Faulted`. |
| `NoBlockingIncident` | The source has no blocking incident. This covers a retry that already happened, or an incident that was absorbed. |
| `BlockingIncidentOutsideSource` | A blocking incident in the source's subtree belongs to a descendant, not to the source. |
| `SourceWorkClaimed` | The source subtree has claimed scheduler work. |
| `PinnedExecutableUnavailable` | The source node is missing from the pinned executable. |
| captured-concurrency codes | As for `RescheduleActivity`. |

## Effect, in one alteration checkpoint

1. Every blocking incident on the source becomes `Resolved`, with outcome:
   - action kind `OperatorRetry`;
   - system source `OperatorRetryAlteration`;
   - metadata `runtime.alteration.jobId`, `runtime.alteration.planId`, `runtime.alteration.submittedBy`, and `runtime.alteration.reason` when given.
2. The source becomes `Superseded`, as in `RescheduleActivity`. A successor is created with:
   - the source's inputs, parent, scheduling activity execution id, branch, iteration and execution path;
   - the source's **scheduling cause**;
   - the source's metadata, plus `runtime.supersedesActivityExecutionId` and `runtime.retryOfActivityExecutionId`.
3. The successor's schedule or start work item is enqueued as a post-commit intent.

The successor's activity context resolves the same `OperationKey` and `TargetOperationKey` as the source, because the assigned keys travel in the copied metadata.

## Idempotency

- **Same `Idempotency-Key`:** returns the existing plan.
- **New key, after a successful retry:** rejected with `SourceActivityNotFaulted`, because the source is now `Superseded`.
- **New key, same source, submitted concurrently:** only one plan's preflight finds the incident blocking. The other is rejected with `NoBlockingIncident`, or by the captured-concurrency check.
