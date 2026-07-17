# Data Model: Complete Child Fault and Cancellation Semantics

## Effective cancellation policy metadata

One immutable dispatch metadata entry stores `true` or `false`:

- wait mode: explicit authored value, with absent input resolved to default true;
- fire-and-forget: always false;
- legacy missing key: true only for wait mode.

## Cancellation lifecycle metadata

Two sanctioned mutable values are provider-owned:

- `parent-before-admission`: Pending was cancelled before child admission; status becomes Cancelled.
- `parent-cancellation-requested`: admission already won; status remains Started until child terminal projection.

Child terminal projection preserves the latter marker while advancing to Completed, Faulted, or Cancelled.

## WorkflowDispatchCancellationRequest

Canonical checkpoint state:

- `DispatchId`
- `ParentWorkflowExecutionId`
- `ParentActivityExecutionId`
- `ChildWorkflowExecutionId`
- `RequestedAt` from checkpoint occurrence time

Constructor validation recomputes `WorkflowDispatchIdentity`. Requests are ordinally ordered and unique by dispatch ID. The checkpoint fingerprint includes the request itself, not the provider-resolved resulting record.

## Admission result

`TryAdmitAsync(dispatchId, admittedAt)` returns one disposition:

- `Admitted`: Pending changed to Started.
- `AlreadyAdmitted`: Started already exists; deterministic start may be repeated.
- `CancelledBeforeAdmission`: provider marker proves start must be suppressed.
- `Terminal`: Completed, Faulted, Cancelled without the before-admission marker, or DispatchFailed.

Unknown/missing dispatch is an invariant failure.

## Child-cancel identity and payload

`WorkflowDispatchIdentity` adds distinct deterministic prefixes for:

- child-cancel intent ID;
- child-cancel idempotency key;
- Cancel command ID;
- Cancel envelope ID.

The payload carries only dispatch, parent workflow/activity, and child workflow IDs. Actor metadata may add the same stable IDs; it contains no result, incident, exception, or output data.

## Terminal result diagnostics

`DispatchWorkflowResult` remains the stable result model.

| Status | Outputs | Diagnostic metadata |
|---|---:|---|
| Completed | safe captured outputs | empty unless existing safe data applies |
| Faulted | none | `code=child-workflow-faulted`; `category=execution`; `summary=The child workflow faulted.`; invariant `incidentCount`; invariant `incidentIdsTruncated`; up to 32 deduplicated, ordinally sorted `incidentId.000`–`incidentId.031` entries |
| Cancelled | none | exactly `code=child-workflow-cancelled`; `category=execution`; `summary=The child workflow was cancelled.` |

Allowed values are created internally. Parent payload validation accepts only Completed, Faulted, and Cancelled.

The fault list is deduplicated and ordinally sorted, `incidentCount` records the pre-truncation count, the first 32 IDs are retained, and `incidentIdsTruncated` is the lowercase invariant boolean indicating overflow.

## Lifecycle sequences

```text
Cancellation wins:
Pending --parent checkpoint directive--> Cancelled(parent-before-admission)
Start delivery --> acknowledged no-op
Child-cancel intent --> acknowledged no-op

Admission wins:
Pending --TryAdmit--> Started
Parent checkpoint directive --> Started(parent-cancellation-requested) + child-cancel intent
Cancel command --> child Cancelled (or terminal result wins)
Child terminal checkpoint --> dispatch Cancelled/Completed/Faulted
```

Opt-out and fire-and-forget records produce neither directive nor child-cancel intent.

## Groundwork persistence

- The parent Cancelled state, activity cancellations, dispatch directive resolution, child-cancel outbox item, and checkpoint marker share one cross-unit transaction.
- Admission uses the dispatch document’s optimistic version as a compare-and-transition fence.
- Transaction conflict retries/rebuilds never change directive identity.
- Existing workflow-dispatch document version remains valid because policy and markers use record metadata.
- Existing v1 fixtures with no effective-policy key deserialize without an upcaster: wait mode evaluates true and fire-and-forget evaluates false.
