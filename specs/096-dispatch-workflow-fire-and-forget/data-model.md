# Data Model: Dispatch a Published Workflow Fire-and-Forget

## WorkflowDispatchRecord

Durable linkage and lifecycle state for one logical dispatch.

| Field | Meaning |
|---|---|
| DispatchId | Versioned deterministic identity for the parent activity execution |
| ParentWorkflowExecutionId | Owning parent execution |
| ParentActivityExecutionId | Concrete DispatchWorkflow activity execution |
| ChildWorkflowExecutionId | Reserved child execution identity |
| ChildExecutable | Full pinned `WorkflowExecutableIdentity` |
| ChildSource | Full pinned `WorkflowExecutableSourceProvenance` |
| Mode | `FireAndForget` in #676; wait mode is reserved |
| Status | `Pending` initially; full lifecycle uses Pending/Started/Completed/Faulted/Cancelled/DispatchFailed |
| CorrelationId | Explicit override or inherited parent correlation |
| TenantId | Inherited parent tenant, if present |
| Partition | Inherited runtime partition |
| RunKind | Inherited parent run classification |
| Authority | Immutable child authority/root-initiator snapshot |
| InputDescriptors | Safe names/type descriptors only; no raw values |
| CreatedAt / UpdatedAt | Runtime timestamps |
| Metadata | Safe immutable diagnostics, excluding raw inputs |

### Invariants

- `DispatchId`, parent IDs, child ID, executable/source identity, partition, and authority are nonblank/non-null.
- `ChildExecutable` and `ChildSource` are the exact content identity and authoritative source provenance resolved together from the selected source reference. They are not field-compared: deduplicated content identity can legitimately retain the source facts that first produced it, and provenance intentionally carries no artifact ID.
- Fire-and-forget creation status is `Pending`.
- Record identity is stable for the same parent/activity execution and different for a different activity execution.
- Operational projection contains no raw child input values.

## WorkflowDispatchIdentity

Canonical identity utility over `(parent workflow execution ID, parent activity execution ID)`.

Derived values:

- dispatch record ID
- child workflow execution ID
- child-start intent ID
- start idempotency key

All derivations share the same versioned SHA-256 digest and distinct textual prefixes.

## WorkflowDispatchCheckpointRequest

Activity-to-engine staging value containing exactly one record upsert and one child-start intent. Registered at most once in an activity execution context. The invoke handler consumes it only on the activity-completed checkpoint path.

## WorkflowDispatchStartPayload

Persisted payload needed to start the child without Design state:

- dispatch/parent/activity/child identities
- full pinned child executable and source provenance
- child input values on the workflow-input channel only
- correlation, tenant, partition, run kind
- typed authority/root-initiator snapshot

## WorkflowExecutionAuthoritySnapshot

| Field | Meaning |
|---|---|
| SystemIdentity | Active system identity for this execution; child uses the parent execution identity |
| RootInitiator | Original external/system initiator retained for audit |
| Metadata | Closed safe authority/audit facts reserved for later hardening |

Root starts default both identities from their validated `RequestedBy`. Child starts replace `SystemIdentity` with the parent execution identity and retain the parent snapshot’s root initiator.

## DispatchWorkflowResult

Reserved stable waited-result contract containing:

- `ChildWorkflowExecutionId`
- terminal `WorkflowDispatchStatus`
- JSON-safe completed output entries with name, declared type, redaction state, and an omitted value when redacted
- safe diagnostic metadata without raw exceptions, stack traces, redacted values, or partial outputs

#676 exposes the type but leaves the activity output unset. #679 owns terminal population and resume behavior.

## Workflow start additions

`WorkflowExecutionStartDispatchRequest`, `WorkflowExecutionStartCommandPayload`, `RuntimeCheckpointCommandPayload`, and `WorkflowExecutionState` carry:

- `ParentWorkflowExecutionId`
- `CorrelationId`
- `TenantId`
- explicit `WorkflowExecutionPartition`
- `WorkflowExecutionAuthoritySnapshot`
- existing `WorkflowRunKind`

These values are server/runtime channels, never copied from child `Inputs`.

## State transitions in #676

```text
No record
  -> parent checkpoint commits Pending + child-start intent + Dispatched
  -> child-start handler dispatches reserved child
```

Later status transitions are intentionally deferred to the owning downstream issues. Duplicate parent or outbox delivery is an idempotent replay of the same identity.
