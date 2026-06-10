# Data Model: Checkpoint Commit Envelope

## RuntimeCheckpointCommit

- `CommitId`: durable identity of this checkpoint commit attempt.
- `Checkpoint`: named runtime boundary and workflow execution identity.
- `StateChanges`: atomic collection of continuation-state changes represented by this commit.
- `PostCommitIntents`: outbound work recorded before commit and delivered only after commit succeeds.
- `Metadata`: provider/runtime metadata that does not alter checkpoint semantics.

## RuntimeCheckpointStateChangeSet

Groups the state categories locked by the runtime execution reports:

- `WorkflowExecution`: optional workflow execution state change.
- `Scheduler`: optional scheduler state change.
- `ActivityExecutions`: activity execution state changes.
- `Bookmarks`: bookmark state references, including `ResumeTargetId` where applicable.
- `DurableValues`: durable value state changes using the serialization/value boundary from slice 1.
- `Incidents`: incident state references.
- `Operational`: recovery/lease/control-plane operational state references.

## RuntimeStateChange

Wraps a concrete runtime-owned state model with a state id, operation, and metadata.

## RuntimeStateChangeReference

Represents a state change for categories whose concrete state models are intentionally out of scope for this slice.

## RuntimePostCommitIntent

Placeholder for outbound work that depends on a committed checkpoint. It carries intent identity, kind, workflow/activity correlation, optional idempotency key, JSON payload, and metadata. It is not an outbox implementation.
