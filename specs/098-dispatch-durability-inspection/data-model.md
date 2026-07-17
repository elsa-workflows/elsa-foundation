# Data Model: Durable and Inspectable Detached Dispatch

## WorkflowDispatchRecord

Existing immutable linkage remains authoritative:

- `DispatchId`
- `ParentWorkflowExecutionId`
- `ParentActivityExecutionId`
- `ChildWorkflowExecutionId`
- exact child executable identity and source provenance
- mode, correlation, tenant, partition, run kind, authority
- safe input name/type descriptors
- created timestamp
- internal metadata

Lifecycle-mutable fields are limited to `Status`, `UpdatedAt`, and explicitly modeled safe diagnostic classification if added. Every other field must compare equal across updates.

The class exposes an intentional transition factory/method that copies immutable fields and changes only approved lifecycle fields; callers do not reconstruct records ad hoc.

## WorkflowDispatchStatus

```text
Pending -> Started -> Completed
        |          -> Faulted
        |          -> Cancelled
        -> Cancelled
        -> DispatchFailed
        -> Completed
        -> Faulted
```

Equivalent state is idempotent. Terminal states have no outgoing transition. A terminal child checkpoint may repair `Pending` directly to the corresponding child terminal state only when exact child linkage proves materialization; this is treated as a collapsed observation of the missed Started step.

Pending creation is parent-checkpoint-owned. Non-Pending checkpoint projection is child-checkpoint-owned. Direct Started updates are allowed only through the trusted lifecycle service after exact child admission. Direct DispatchFailed updates are allowed only as part of claim-fenced atomic final delivery failure.

## WorkflowDispatchQuery

- optional `ParentWorkflowExecutionId`
- optional `ChildWorkflowExecutionId`
- optional `Status`
- `Take` bounded to the store contract maximum

Tenant scope is supplied only by the active persistence access context and is not a caller-controlled query field.

At least one operational filter or an explicit collector collection query is required. Results order by `CreatedAt`, then `DispatchId`, ordinal.

## GroundworkWorkflowDispatchDocument

- `Collection`: constant `workflowDispatch`
- `ParentWorkflowExecutionId`: parent index projection
- `ChildWorkflowExecutionId`: child index projection
- `Status`: stable enum-name index projection
- `TenantId`: access-scope projection
- `Record`: complete runtime record serialized by the versioned runtime serializer

Physical document ID is derived from deterministic `DispatchId`. The store verifies loaded logical identity to detect physical-ID collisions.

## WorkflowDispatchView

Allowed fields:

- dispatch, parent execution/activity, and child execution IDs
- mode and lifecycle status
- child artifact/definition/version identity and safe source type
- input descriptor names and type aliases only
- created and updated timestamps
- allowlisted diagnostic `Code` and `Category`

Forbidden fields include raw values, variables, stimulus, authority snapshots/secrets, arbitrary metadata, exception type/message/object, stack trace, raw outputs, and redacted values.

## WorkflowDispatchReadinessReport

- `Guarantee`: `ProcessLocal`, `DurableReady`, or `Unsafe`
- `Ready`: boolean
- `Components`: stable component assessments for checkpoint, dispatch store, outbox, scheduler/continuation queue, and background resumption
- `ReasonCodes`: deterministic missing/incompatible component codes
- no service type assembly details, connection strings, or provider exceptions

## RuntimePostCommitOutboxClaim

- `OutboxItemId`
- opaque `OwnerId`
- positive monotonically increasing `FencingToken`
- `ClaimedAt`
- `VisibleAfter` / lease expiry

Only the matching current owner/token can acknowledge or record failure. An expired claim is reclaimable with a higher fencing token. Claim metadata is operational state and never copied into dispatch inspection views.

The existing outbox document becomes schema v2. The v1-to-v2 upcaster initializes absent claim token and expiry to the unclaimed state.

## Retention Eligibility

A record is eligible only when:

```text
dispatch terminal AND parent execution absent AND child execution absent
```

Both execution facts must be re-read immediately before deletion. The delete then compares the complete selected terminal snapshot and provider version atomically. Pending/Started or otherwise changed state, a concurrency conflict, cancellation, or read failure yields retain.

## Deterministic Dispatch Start Materialization

The deterministic dispatch identity derives stable IDs for:

- child workflow execution
- start intent and idempotency key
- runtime command and envelope
- scheduler work item
- child root activity execution

Replay after process recreation must reproduce the same identities and equivalent durable scheduler payload. IDs beyond the existing public request are derived internally after validating the committed server-owned dispatch record.
