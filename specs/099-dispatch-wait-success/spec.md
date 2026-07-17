# Feature Specification: Wait for a Successful Child and Return Safe Outputs

**Feature Branch**: `codex/dispatch-workflow-program`

**Created**: 2026-07-16

**Status**: Approved

**Input**: GitHub issue #679, “Wait for a successful child and return safe outputs”, under parent #674

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Commit the parent wait before starting the child (Priority: P1)

As a workflow author, I can enable `WaitForCompletion` and know that the parent cannot lose its place or expose a child before the durable parent wait exists.

**Why this priority**: The parent bookmark, suspension, dispatch record, and start responsibility must share one atomic boundary or a crash can create an orphaned child or a permanently waiting parent.

**Independent Test**: Execute `DispatchWorkflow` in wait mode while delivery is paused, then verify one checkpoint contains the Pending wait-mode dispatch, reserved child ID, suspended activity state, non-expiring bookmark, and child-start intent, while no child is yet externally visible.

**Acceptance Scenarios**:

1. **Given** a valid pinned child and `WaitForCompletion=true`, **When** the activity reaches its mandatory checkpoint, **Then** one atomic commit persists the wait-mode dispatch, reserved child ID, bookmark, suspended parent activity state, and child-start intent.
2. **Given** failure before that commit becomes durable, **When** state is inspected, **Then** none of the wait, dispatch, or child-start responsibility is visible and no child exists.
3. **Given** the commit is durable but child delivery is paused, **When** the parent is inspected, **Then** the activity remains suspended on one deterministic bookmark with no expiry and the child is not yet externally visible.
4. **Given** replay of the same activity execution, **When** the wait checkpoint is rebuilt, **Then** the same dispatch, child, bookmark, stimulus, intent, and idempotency identities are used.

---

### User Story 2 - Record successful child completion as durable parent-resume work (Priority: P1)

As a workflow operator, I can rely on a successful child terminal checkpoint to durably create the exact work required to resume its waiting parent, even if the process stops immediately afterward.

**Why this priority**: Observing completion and recording resume responsibility in separate writes would leave an unrecoverable gap.

**Independent Test**: Complete the child, stop before parent-resume delivery, recreate runtime services, and verify that the child Completed state, dispatch Completed projection, safe output snapshot, and one deterministic parent-resume intent committed together.

**Acceptance Scenarios**:

1. **Given** a wait-mode dispatch whose child completes, **When** the child Completed checkpoint commits, **Then** it atomically records the Completed dispatch projection and one deterministic parent-resume intent.
2. **Given** declared child outputs, **When** the Completed checkpoint is enriched, **Then** only committed child workflow outputs are projected through the configured capture policy into the resume payload.
3. **Given** an output that policy redacts, **When** resume work is recorded, **Then** its entry retains its name, declared type, and redaction state while carrying no value.
4. **Given** uncertain acknowledgement or replay of the child Completed checkpoint, **When** enrichment runs again, **Then** the already committed resume intent is reused byte-equivalently and no second logical resume is created.

---

### User Story 3 - Resume once and complete with a safe result (Priority: P1)

As a workflow author, I receive one `Completed` outcome and a structured result containing the child ID, Completed status, and safe outputs after the child succeeds.

**Why this priority**: The feature is user-visible only when the durable completion signal consumes the bookmark and completes the parent activity exactly once.

**Independent Test**: Deliver the same terminal and parent-resume work repeatedly and verify one bookmark consumption, one parent activity completion, one ordinary graph `Completed` outcome, and one equivalent safe result.

**Acceptance Scenarios**:

1. **Given** committed parent-resume work, **When** the global resumption path delivers it, **Then** it resumes the exact parent bookmark outside both workflow actor mailboxes using a deterministic idempotency key.
2. **Given** resume delivery is accepted but the bookmark is not yet consumed, **When** delivery completes its attempt, **Then** the work remains retryable with backoff rather than being acknowledged.
3. **Given** the bookmark is consumed or the parent is already terminal, **When** delivery is rechecked, **Then** the resume work is acknowledged and further duplicates are harmless.
4. **Given** the DispatchWorkflow resume target runs, **When** it receives a valid Completed payload, **Then** it sets `ChildWorkflowExecutionId`, sets `Result`, and emits only `Completed` through ordinary graph outcome semantics.
5. **Given** parent-resume delivery remains unconsumed, **When** retryable attempts are recorded, **Then** each attempt emits an alertable structured signal containing only stable identifiers, intent kind, attempt count, and next-availability timing.

---

### User Story 4 - Recover every successful wait boundary with Groundwork (Priority: P1)

As a production host operator, I can restart at any successful wait boundary and still converge on one child and one parent completion.

**Why this priority**: Wait mode is not durable unless the provider-backed path proves the complete parent-to-child-to-parent cycle rather than isolated writes.

**Independent Test**: Inject a process stop after each named Groundwork boundary, recreate provider and runtime services, drain background resumption, and verify one logical child, one consumed bookmark, one parent completion, and the same safe result.

**Acceptance Scenarios**:

1. **Given** a restart after parent suspension but before child delivery, **When** resumption drains, **Then** the deterministic child starts once and the parent remains waiting until completion.
2. **Given** a restart after child completion or terminal-intent recording but before resume delivery, **When** resumption drains, **Then** the existing terminal intent resumes the parent once.
3. **Given** a restart during resume delivery or after bookmark consumption but before acknowledgement, **When** claims expire and work is redelivered, **Then** delivery converges without a second completion.
4. **Given** redacted and unredacted output fixtures, **When** every crash path is inspected, **Then** redacted values are absent and the final unredacted result is JSON-safe and equivalent.

### Edge Cases

- Wait mode has no built-in timeout. Its bookmark has no expiry; timeout behavior must be authored explicitly outside this activity.
- `CancelChildOnParentCancellation` retains its stable default but successful wait mode does not implement cancellation propagation; #680 owns parent/child fault and cancellation semantics.
- A child Faulted or Cancelled checkpoint may update lifecycle inspection through #678, but this slice does not resume the parent with those outcomes; #680 owns that behavior.
- Final child-start failure can yield `DispatchFailed` lifecycle state through #678, but completing the waiting parent from exhaustion, dead-letter, or redrive belongs to #681.
- The parent-resume intent alone receives retry-until-consumed policy. Scheduler, child-start, and unsupported intent kinds retain their existing policy-selected behavior; unsupported kinds are never silently acknowledged.
- Retry observability must be payload-safe and alertable; it must not serialize child outputs, resume payload values, exception detail, or actor-envelope content.
- A missing bookmark is successful duplicate delivery only when the parent activity or workflow is already terminal. A missing bookmark for a nonterminal suspended activity is retried, not acknowledged.
- Output capture failure fails the child terminal checkpoint rather than committing Completed without durable resume responsibility.
- Fire-and-forget mode keeps its existing completion checkpoint, `Dispatched` outcome, and retry behavior.
- In-memory wait execution is asynchronous and idempotent within one process but does not become process-crash durable.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `WaitForCompletion=true` MUST select wait mode while the existing default remains `false` and fire-and-forget behavior remains unchanged.
- **FR-002**: Wait mode MUST derive deterministic dispatch, child execution, child-start intent, start idempotency, bookmark, stimulus, parent-resume intent, and parent-resume idempotency identities from the parent workflow and activity execution identities.
- **FR-003**: One mandatory parent checkpoint MUST atomically persist the Pending wait-mode dispatch record, reserved child ID, suspended DispatchWorkflow activity state, non-expiring bookmark, and child-start post-commit intent.
- **FR-004**: The child-start intent MUST NOT become deliverable unless the corresponding parent wait state and bookmark committed successfully.
- **FR-005**: Replay of the parent wait checkpoint MUST be byte-equivalent or fail closed on conflicting state.
- **FR-006**: Wait mode MUST continue using the exact retained child artifact, validated inputs, depth, inherited tenant/partition/authority context, deterministic materialization, and durable claim boundaries established by #677 and #678. TestRun authorization and run-kind propagation remain #682.
- **FR-007**: A child `Completed` checkpoint linked to a wait-mode dispatch MUST atomically append the Completed dispatch lifecycle projection and any required parent-resume post-commit intent.
- **FR-008**: Parent-resume intent identity, payload, timestamps, and metadata MUST be replay-stable; uncertain acknowledgement MUST reuse the already committed intent rather than recapturing a different result.
- **FR-009**: Child output capture MUST read only the child execution’s durable workflow-output channel and MUST merge any output changes carried by the terminal checkpoint before projection.
- **FR-010**: Output capture MUST occur only as part of establishing a committed child Completed checkpoint and MUST NOT expose partial pre-terminal output snapshots to the parent.
- **FR-011**: Each captured output entry MUST preserve its name, stable declared type, redaction state, and a JSON-safe value only when capture policy permits disclosure.
- **FR-012**: Redacted output entries MUST remain present but MUST carry no value; resume payloads, outbox state, diagnostics, and final result serialization MUST contain zero redacted values.
- **FR-013**: Output projection failure or unavailable required output state MUST fail closed before the child Completed checkpoint is committed without resume responsibility.
- **FR-014**: Parent-resume delivery MUST run from the global post-commit/resumption path outside both parent and child workflow actor mailboxes and MUST reuse the existing bookmark resume dispatcher and configured actor provider.
- **FR-015**: Parent-resume dispatch MUST use a deterministic idempotency key so duplicate commands converge on one bookmark consumption.
- **FR-016**: Parent-resume delivery MUST retry with positive backoff until the exact bookmark is consumed, the parent activity is completed, or the parent workflow is terminal/removed.
- **FR-017**: Retry-until-consumed MUST be an explicit policy of the contributed parent-resume intent kind and MUST NOT change the default `RetryPolicy.None` behavior of other or unsupported kinds.
- **FR-018**: Retry-until-consumed MUST have no attempt-exhaustion boundary and MUST remain claim/fencing safe across process failure; attempt accounting MUST not overflow into invalid state.
- **FR-019**: A missing bookmark for a nonterminal suspended parent MUST remain retryable; a consumed bookmark or terminal parent MUST be acknowledged as idempotent success.
- **FR-020**: The DispatchWorkflow resume target MUST validate that the payload matches the deterministic parent/activity/child dispatch identity before using it.
- **FR-021**: Successful resume MUST set `ChildWorkflowExecutionId`, populate `DispatchWorkflowResult` with `Completed`, copy only safe output entries, and emit `Completed` through ordinary graph outcome semantics.
- **FR-022**: Duplicate child start, child Completed checkpoint, terminal intent delivery, bookmark resume command, and resume callback MUST converge on one child execution, one bookmark consumption, and one parent activity completion.
- **FR-023**: Wait mode MUST create no automatic deadline or bookmark expiration.
- **FR-024**: Built-in in-memory and Groundwork outbox stores MUST support deterministic lookup of an already committed outbox item needed to preserve terminal-checkpoint replay identity, without widening the existing base store contract.
- **FR-025**: Groundwork MUST persist the additive retry-policy wire shape as the pre-GA v3 clean baseline, keep minimum-readable equal to current, reject older generations, and require the documented datastore reset without adding Elsa compatibility machinery.
- **FR-026**: Groundwork restart tests MUST cover parent suspension, child delivery, child completion, terminal intent recording, output capture, resume delivery, bookmark consumption, completion propagation, claim expiry, and uncertain acknowledgement.
- **FR-027**: Tests MUST prove fire-and-forget behavior and unsupported-kind failure behavior remain unchanged.
- **FR-028**: This slice MUST NOT implement child fault/cancellation or parent cancellation propagation (#680), finite exhaustion/dead-letter/redrive (#681), TestRun dispatch scope (#682), or distributed two-node placement/transport (#683).
- **FR-029**: Every retryable parent-resume attempt MUST emit an alertable structured operational signal with stable dispatch/work identifiers, intent kind, saturated attempt count, and next-availability timing, while excluding child output values, resume payload values, exception detail, and actor-envelope content.

### Key Entities

- **Wait-mode dispatch checkpoint**: The atomic parent commit containing suspended activity state, deterministic bookmark, Pending dispatch, and child-start responsibility.
- **Dispatch completion payload**: A replay-stable safe snapshot of a successful child ID, Completed status, and typed/redacted output entries.
- **Parent-resume intent**: Deterministic cross-execution work recorded by the child terminal checkpoint and delivered by the global resumption pump.
- **Retry-until-consumed policy**: An intent-kind-specific unbounded retry policy with positive backoff and claim-safe attempt accounting.
- **Safe child output entry**: One named durable workflow output with declared type, redaction state, and an optional JSON value.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of injected parent-checkpoint failures, dispatch, bookmark, suspended activity state, and child-start outbox are either all absent or all committed under one marker.
- **SC-002**: Replaying one wait-mode activity at least three times produces one dispatch ID, one child ID, one bookmark ID, one start intent, and one resume intent.
- **SC-003**: Every successful child completion test observes its Completed workflow state, Completed dispatch projection, and parent-resume outbox item in the same committed checkpoint.
- **SC-004**: Across the output security corpus, every declared output name and type is retained, every redacted value is absent, and every disclosed value parses as JSON.
- **SC-005**: Duplicate start, terminal, and resume delivery at least three times each produces exactly one child execution, one bookmark consumption, and one parent activity completion.
- **SC-006**: Parent-resume work remains retryable after accepted-but-unconsumed, deferred, and process-crash attempts, and is acknowledged within one successful sweep after bookmark consumption or terminal-parent detection.
- **SC-007**: Groundwork process-recreation tests pass at every named crash boundary and converge on an equivalent `DispatchWorkflowResult` in 100% of scenarios.
- **SC-008**: Wait-mode completion emits exactly one `Completed` graph outcome; it emits none of `Dispatched`, `Faulted`, `Cancelled`, or `DispatchFailed` on the successful path.
- **SC-009**: Regression suites show zero behavior changes for fire-and-forget dispatch and zero silent acknowledgements for unsupported post-commit intent kinds.
- **SC-010**: Architecture audits report no fault/cancellation propagation, dead-letter/redrive, TestRun, broker, Studio, or distributed-placement expansion.
- **SC-011**: Repeated unconsumed-resume tests observe one payload-safe alertable signal per recorded retryable attempt, with monotonic/saturated attempt information and zero result or exception values.

## Assumptions

- #675’s contributed handler registration remains the source of truth for intent kind ownership, and handlers expose only `HandleAsync` while the aggregate dispatcher exposes `DispatchAsync`.
- #677’s exact retained artifact, validated input contract, dependency retention, and bounded depth are authoritative and are reused without widening author-controlled inputs.
- #678’s Groundwork atomic checkpoint/outbox storage, dispatch lifecycle projection, durable claim/visibility fencing, deterministic child materialization, readiness, and background resumption are complete prerequisites.
- Existing bookmark consumption commits activity completion and bookmark deletion atomically and records ordinary completion-propagation work after that checkpoint.
- Existing durable workflow outputs and the configured runtime capture policy are the authoritative output source; activity-output inspection snapshots are not child workflow results.
- The parent workflow remains in its ordinary running/waiting engine state while the DispatchWorkflow activity is durably Suspended; this slice does not redefine global workflow status semantics.
- The broader constitution remains draft/provisional; accepted checkpoint, artifact, persistence, and single-writer decisions govern this work.

## Scope Boundaries

### Included

- Atomic wait-mode parent checkpoint and deterministic non-expiring bookmark.
- Successful child Completed checkpoint enrichment and safe output snapshot.
- Deterministic parent-resume intent, global delivery, retry-until-consumed, and idempotent acknowledgement.
- Payload-safe structured retry observability suitable for operator alerting without finite exhaustion or dead-letter behavior.
- Successful `DispatchWorkflowResult` and `Completed` graph outcome.
- In-memory semantic tests plus Groundwork crash/restart convergence and wire compatibility.

### Excluded

- Child fault/cancellation result and parent/child cancellation propagation (#680).
- Retry exhaustion, dead-letter storage, operator diagnostics/incidents, and redrive (#681).
- TestRun dispatch authorization, expiry, and teardown (#682).
- Distributed two-node placement, transport, and remote execution (#683).
- Activity-level timeout, transport selector, broker contract, Studio UI, or construct-only workflow-definition activity changes.
