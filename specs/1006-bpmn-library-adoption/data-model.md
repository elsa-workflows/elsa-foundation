# State and identity boundaries

The library owns semantic token/work/race/loop/compensation state. Foundation owns the versioned private-state envelope, activity execution identities, incidents, bookmarks, queue/outbox and the checkpoint transaction. The authored envelope remains separate.

| Entity | Fields and validation |
|---|---|
| Legacy execution document | Alias Elsa.Bpmn.ExecutionState/version1, activeChildren, producingActivityExecutionId and all remaining state fields. Missing/corrupt data never becomes an initial state. |
| Library state | activeWork, producingWorkHandle, optional stoppedHandle, original pendingFault and all semantic collections. State is the interpreter's result; only released Prune trims it. |
| Work identity | Parent execution + child execution handle + token ExecutionPathId + node/element + iteration. Validate exact uniqueness against direct durable children, including terminal rows and queued provenance. |
| Scope invocation | Fixed metadata captured at invocation, distinct from each callback's correlation. |
| Coherent fixture | Source and pins, executable and all parent/child/value/incident/bookmark/queue/outbox documents at one actual committed boundary. Capture preconditions and old-host reload control accompany it. |
| Combined parent outcome | Existing durable outcome ID, committed parent and faulted child, ordered notifications, original typed fault and incident. One parent evaluation consumes it once. |

Storage transitions: absence of private state permits a fresh execution; known v1 invokes the compatibility reader/writer without restamping; the explicit new version uses its capable reader/writer; unknown versions or ambiguous work identity fail before new dispatch. Active execution format conversion and cleanup are not implicit side effects of reading.

## Called-workflow refinement (proposed)

The [control boundary contract](contracts/called-workflow-control.md) adds an explicit consumer capability, bounded versioned authored-control batch, additive activity-state projection and optional waited-result transport. Its exact dispatch/root/source identity and durable processed identity are separate from semantic state and safe diagnostic metadata. Root-local committed ordinals and an exact-call inbox with next ordinal and pending batches preserve cross-batch order. Inbox changes share the parent checkpoint; plan review must validate both buffered and consumed paths before implementation.

## FR-015 proposed delivery-state refinement

The [recipient contract](contracts/broadcast-recipient-snapshot.md#state-and-identities) specifies
immutable versioned start/bookmark recipient models and explicit source-delivery context. Store the
snapshot on the existing exact outbox row, separate from its immutable Intent and private workflow
semantic state. Absent is uninitialized; present empty is authoritative; corrupt present data refuses.
The only installation transition is absent → one complete immutable winner under row revision and
claim authority. Claim/renew/retry/completion/redrive preserve it. Legacy ambiguous absence holds
responsibility instead of pretending a first match. New declared outbox family versions protect the
state from old writers. Saved-start authority and existing GC roots bind executable material to the
same owed source, without another persistence ledger.
