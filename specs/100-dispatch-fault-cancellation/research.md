# Research: Complete Child Fault and Cancellation Semantics

## Decision 1: Extend the existing terminal resume path

**Decision**: Broaden `WorkflowDispatchCompletionEnricher`, `WorkflowDispatchParentResumePayload`, and the activity resume target from Completed-only to exactly Completed, Faulted, and Cancelled.

**Rationale**: #679 already owns deterministic intent replay, global bookmark delivery, and ordinary activity completion. Reusing it preserves mailbox boundaries and duplicate handling.

**Alternatives rejected**:

- Fault the DispatchWorkflow activity: contradicts authored outcome semantics.
- Mutate the parent directly from the child checkpoint: violates the per-execution writer boundary.
- Add new stimulus or resume-target identifiers: breaks already durable wait bookmarks.

## Decision 2: Use a strict non-success diagnostic allowlist

**Decision**: Completed alone reads workflow outputs. Faulted reads stable blocking-incident IDs and emits exactly `code=child-workflow-faulted`, `category=execution`, `summary=The child workflow faulted.`, invariant `incidentCount`, invariant `incidentIdsTruncated`, and no more than 32 `incidentId.000`–`incidentId.031` entries after ordinal deduplication, sorting, and truncation. Cancelled emits exactly `code=child-workflow-cancelled`, `category=execution`, and `summary=The child workflow was cancelled.`. Neither reads partial outputs.

**Rationale**: Incident messages, exception types, stack traces, payloads, and metadata may contain secrets. Stable IDs let operators correlate with child inspection without copying those details into parent data.

**Alternatives rejected**:

- Copy incident messages or failure types: capture policy does not guarantee they are safe for a parent result.
- Serialize child inspection projections: diagnostic evidence is not an activity result channel.
- Omit all fault correlation: the issue explicitly allows incident identifiers and safe summaries.

## Decision 3: Persist effective cancellation policy in immutable metadata

**Decision**: Store a canonical lowercase boolean in DispatchWorkflow-owned record metadata. Missing legacy wait metadata means true; fire-and-forget always evaluates false. Explicitly treat an absent runtime input as the authored default true.

**Rationale**: The policy must survive replay and parent cancellation but does not justify breaking every public `WorkflowDispatchRecord` constructor or changing the Groundwork wire shape.

## Decision 4: Carry a query-independent cancellation directive in the parent checkpoint

**Decision**: Add `WorkflowDispatchCancellationRequest` state to `RuntimeCheckpointStateChangeSet`. The parent cancellation enricher creates one canonical request and child-cancel intent per eligible dispatch. The provider resolves current dispatch state within the same transaction.

**Rationale**: A concrete record calculated from a pre-commit query can become stale, change the replay fingerprint, or lose an admission race. A directive fingerprints the parent’s stable intent while the provider applies current-state rules atomically.

**Provider resolution**:

- Pending → Cancelled plus `parent-before-admission` marker.
- Started → Started plus `parent-cancellation-requested` marker.
- Completed/Faulted/Cancelled/DispatchFailed → unchanged; terminal wins.

**Alternatives rejected**:

- Only check parent state in the start handler: check-then-start has a TOCTOU gap.
- Cancel the start outbox item: an already claimed handler can still perform the side effect.
- Derive a Pending-to-Cancelled record in the enricher: current state can change before commit and replay shape becomes query-dependent.

## Decision 5: Linearize start with a conditional admission capability

**Decision**: Add an optional `IWorkflowDispatchAdmissionStore` implemented by built-in stores. `TryAdmitAsync` atomically transitions Pending to Started and returns a stable disposition for already Started, cancelled-before-admission, or terminal records. ChildStartExecutor requires this capability when a persisted dispatch store is present and performs it before external start dispatch.

**Rationale**: Whichever provider mutation commits first becomes the durable winner. Repeating an admitted start is safe because child identity and start idempotency are deterministic.

## Decision 6: Always record deterministic cancel responsibility for eligible nonterminal dispatches

**Decision**: The parent cancellation checkpoint records a child-cancel intent alongside the directive for both observed Pending and Started records. The handler acknowledges the provider’s before-admission marker without contacting a child.

**Rationale**: This closes stale-read and in-flight races. If admission wins, the intent cancels the child. If cancellation wins, the same intent is a harmless auditable no-op. Terminal races are also harmless.

## Decision 7: Deliver Cancel through the existing actor provider

**Decision**: `ChildCancelExecutor` reloads `WorkflowDispatchRecord` for `Partition` and `Authority.SystemIdentity`, queries `IWorkflowExecutionStateStore` for authoritative child status, constructs deterministic Cancel command, envelope, and idempotency identities, activates the exact child with `ControlPlaneCommand`, then enqueues with ordinary at-least-once mode.

**Rationale**: `WorkflowCancelSchedulerWorkHandler` already owns terminal preservation and idempotent Cancel processing. A generic second command bus would duplicate actor routing.

**Acknowledgement rules**:

- acknowledge cancelled-before-admission and DispatchFailed without actor contact;
- acknowledge a child already Completed, Faulted, or Cancelled;
- retry with positive backoff while an admitted child is missing;
- retry Rejected or Deferred delivery;
- after Accepted, AcceptedButFaulted, or Duplicate, re-read child state and acknowledge only terminal state; otherwise retry.

## Decision 8: Keep unbounded delivery separate from #681 exhaustion

**Decision**: Register child-cancel with the existing `RetryUntilAcknowledged` policy. Do not add attempts exhaustion, dead-letter state, or redrive.

**Rationale**: Cancellation can validly precede child visibility and therefore needs convergence. #681 owns finite terminal delivery failure operations.

## Compatibility and Scope Findings

- Preserve all activity inputs, outputs, outcomes, stimulus IDs, resume-target IDs, and public record constructors.
- Preserve the base `IWorkflowDispatchStore`; admission and cancellation are additive capabilities.
- Cancellation marker metadata is sanctioned lifecycle metadata; effective policy metadata remains immutable.
- Preserve Completed results and fire-and-forget behavior/source/schema compatibility. New fire-and-forget rows add canonical effective-policy metadata, so serialized bytes may differ; existing v1 golden fixtures deserialize with effective false and require no upcaster or schema-version bump.
- No schema bump is needed when all policy/marker data remains metadata and the directive lives inside the already versioned checkpoint commit payload.
- #681, #682, and #683 remain excluded.
