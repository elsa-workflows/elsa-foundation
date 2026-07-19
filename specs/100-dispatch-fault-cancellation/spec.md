# Feature Specification: Complete Child Fault and Cancellation Semantics

**Feature Branch**: `codex/dispatch-workflow-program`

**Created**: 2026-07-16

**Status**: Approved

**Input**: GitHub issue #680, “Handle child faults and parent-child cancellation”, under parent #674

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Complete a waited dispatch with a safe terminal result (Priority: P1)

As a workflow author, I can branch on a waited child fault or cancellation without the DispatchWorkflow activity itself faulting and without receiving partial or unsafe child data.

**Why this priority**: Fault and cancellation are ordinary terminal child results. Treating either as an activity exception would bypass authored graph outcomes and risk leaking child failure detail.

**Independent Test**: Drive linked children to Faulted and Cancelled terminal checkpoints, redeliver each terminal notification, and verify one parent completion with the matching graph outcome, no activity fault, no child outputs, and only allow-listed diagnostics.

**Acceptance Scenarios**:

1. **Given** a waited child reaches Faulted, **When** its terminal checkpoint commits, **Then** the dispatch becomes Faulted and one replay-stable parent-resume responsibility is recorded atomically.
2. **Given** a waited child reaches Cancelled, **When** its terminal checkpoint commits, **Then** the dispatch becomes Cancelled and one replay-stable parent-resume responsibility is recorded atomically.
3. **Given** a Faulted or Cancelled resume payload, **When** DispatchWorkflow resumes, **Then** it completes normally with the matching `Faulted` or `Cancelled` graph outcome rather than faulting the activity.
4. **Given** partial child outputs, exception detail, stack traces, or arbitrary incident metadata, **When** a non-success result is built, **Then** none is disclosed; only stable incident identifiers and fixed safe summaries/classifications are present.
5. **Given** no outgoing connection for the emitted non-success outcome, **When** normal graph processing continues, **Then** no implicit escalation or parent fault is introduced.

---

### User Story 2 - Cancel a pending child before admission (Priority: P1)

As an operator, I can cancel a waiting parent before the child is admitted and know that the child-start responsibility cannot later materialize the child.

**Why this priority**: A status check separated from admission leaves a race in which cancellation appears successful while an in-flight delivery still starts an orphaned child.

**Independent Test**: Race the parent cancellation checkpoint against claimed and unclaimed child-start delivery, repeat both orders, and prove one durable linearization point selects either cancelled-before-admission or admitted-then-cancelled behavior.

**Acceptance Scenarios**:

1. **Given** a wait-mode dispatch with propagation enabled and no admitted child, **When** the parent cancellation checkpoint wins the admission race, **Then** it atomically marks the dispatch Cancelled and every later or duplicate start delivery is a successful no-op.
2. **Given** child admission wins the same race, **When** parent cancellation observes the admitted dispatch, **Then** it records one deterministic child-cancellation responsibility rather than trying to undo admission.
3. **Given** process failure around either side of the race, **When** Groundwork services restart and claims expire, **Then** the persisted winner is honored and no child escapes cancellation responsibility.

---

### User Story 3 - Propagate cancellation to an admitted child exactly once (Priority: P1)

As a workflow author, I get propagation by default for waited children, while opt-out and detached dispatches remain independent.

**Why this priority**: Cancellation is at-least-once work and must tolerate duplicates, reordering, synchronous child completion, and the short interval between durable admission and visible child state.

**Independent Test**: Cancel after admission, deliver the cancellation responsibility repeatedly and before/after child visibility and terminal notification, and verify one deterministic Cancel command whose duplicates preserve the child's real terminal state.

**Acceptance Scenarios**:

1. **Given** `WaitForCompletion=true` with the default cancellation setting, **When** the parent is cancelled after child admission, **Then** one deterministic child Cancel command is delivered idempotently through the configured runtime command path.
2. **Given** cancellation delivery temporarily precedes child visibility, **When** delivery retries, **Then** the responsibility remains durable until the child accepts the command or is already terminal.
3. **Given** the child completes or faults while cancellation races, **When** the Cancel command arrives, **Then** the existing terminal outcome is preserved.
4. **Given** `CancelChildOnParentCancellation=false`, **When** the waited parent is cancelled, **Then** the child continues independently and no child-cancel responsibility is created.
5. **Given** fire-and-forget mode, **When** the parent completes or is cancelled regardless of the input value, **Then** the child remains independent and no child-cancel responsibility is created.

### Edge Cases

- `CancelChildOnParentCancellation` defaults to `true`, but its effective value is always `false` outside wait mode.
- A start claim or status read alone is not the admission boundary. Admission and pending cancellation require a provider-atomic compare-and-transition so exactly one wins.
- Once admission wins, cancellation may be delivered before the child execution is queryable; that condition is retryable, not successful acknowledgement or final failure.
- Duplicate child-start, child-cancel, child-terminal, parent-resume, and parent-cancel work must converge without changing an already terminal child or parent.
- Fault and cancellation results never contain child outputs. Fault diagnostics exclude exception messages, stack traces, diagnostic payloads, and arbitrary incident metadata.
- Cancellation of a parent that is already terminal is an ordinary runtime no-op and must not rewrite its result.
- Parent completion does not propagate cancellation. Only an actual parent cancellation checkpoint can do so.
- Retry exhaustion, dead-letter storage, and redrive remain #681; this slice uses the existing unbounded acknowledgement policy only for responsibilities that can validly precede materialization or consumption.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A child Faulted checkpoint linked to a wait-mode dispatch MUST atomically persist the Faulted lifecycle projection and one deterministic parent-resume intent.
- **FR-002**: A child Cancelled checkpoint linked to a wait-mode dispatch MUST atomically persist the Cancelled lifecycle projection and one deterministic parent-resume intent.
- **FR-003**: Terminal enrichment and replay MUST reuse the exact committed parent-resume intent or fail closed on any conflicting identity, payload, timestamp, or metadata.
- **FR-004**: A non-success `DispatchWorkflowResult` MUST carry the deterministic child ID and matching terminal status, MUST contain zero child outputs, and MUST contain only allow-listed diagnostic fields.
- **FR-005**: Fault diagnostics MUST use exactly `code=child-workflow-faulted`, `category=execution`, `summary=The child workflow faulted.`, invariant `incidentCount`, invariant `incidentIdsTruncated`, and at most 32 ordinal keys `incidentId.000` through `incidentId.031`; incident IDs MUST be deduplicated and ordinally sorted before truncation. Exception messages, exception types, stack traces, diagnostic payloads, output values, failure types, and arbitrary incident metadata MUST NOT cross the child-parent boundary.
- **FR-006**: Cancellation diagnostics MUST use exactly `code=child-workflow-cancelled`, `category=execution`, and `summary=The child workflow was cancelled.` and MUST expose no incident, output, exception-derived, or arbitrary child data.
- **FR-007**: The resume payload and activity resume target MUST accept exactly Completed, Faulted, or Cancelled linked-child terminal statuses and MUST reject Pending, Started, DispatchFailed, mismatched, or malformed payloads.
- **FR-008**: Faulted and Cancelled child results MUST complete DispatchWorkflow normally, set `ChildWorkflowExecutionId` and `Result`, and emit only the matching `Faulted` or `Cancelled` graph outcome.
- **FR-009**: Unconnected Faulted and Cancelled outcomes MUST follow existing ordinary graph semantics with no implicit activity exception, parent fault, or escalation.
- **FR-010**: `CancelChildOnParentCancellation` MUST retain its authored default of `true`, and its effective value MUST be persisted as disabled for every fire-and-forget dispatch.
- **FR-011**: The effective cancellation-propagation policy MUST be stored durably and replay-stably with the dispatch before child-start delivery can occur.
- **FR-012**: Parent cancellation handling MUST consider only that parent’s wait-mode, propagation-enabled, nonterminal dispatches and MUST be part of the parent cancellation checkpoint enrichment.
- **FR-013**: Pending child admission and parent cancellation MUST use one provider-atomic compare-and-transition boundary so exactly one wins under concurrent execution.
- **FR-014**: If cancellation wins before admission, the same parent checkpoint MUST transition the dispatch to Cancelled and later or duplicate child-start delivery MUST acknowledge without calling the workflow start dispatcher.
- **FR-015**: Every eligible Pending or Started dispatch MUST receive one deterministic child-cancel post-commit intent in the parent cancellation checkpoint to close stale-read races; the handler MUST contact the child actor only when provider state proves admission won.
- **FR-016**: Child-cancel delivery MUST load the durable dispatch for its `Partition` and `Authority.SystemIdentity`, use the configured workflow execution actor provider, deterministic Cancel command/envelope/idempotency identities, and ordinary at-least-once delivery.
- **FR-017**: Child-cancel delivery MUST use `IWorkflowExecutionStateStore` as authoritative child state, acknowledge cancelled-before-admission/DispatchFailed or an already terminal child, and retry with positive backoff for a missing admitted child, Rejected/Deferred delivery, or Accepted/AcceptedButFaulted/Duplicate delivery whose post-dispatch child state remains nonterminal.
- **FR-018**: Duplicate child-cancel delivery MUST be idempotent and MUST NOT overwrite a child that completed, faulted, or was already cancelled.
- **FR-019**: `CancelChildOnParentCancellation=false` in wait mode MUST create neither a pre-admission cancellation transition nor child-cancel intent, allowing the child to continue independently.
- **FR-020**: Fire-and-forget dispatches MUST remain independent of ordinary parent completion and cancellation regardless of the authored cancellation input.
- **FR-021**: Parent completion without cancellation MUST NOT create child-cancel work.
- **FR-022**: Cancellation and terminal-notification races in either order, including at least three duplicate deliveries, MUST converge on one durable dispatch terminal result and at most one logical parent activity completion.
- **FR-023**: Built-in in-memory and Groundwork providers MUST implement equivalent admission/cancellation atomicity, claim recovery, and stale-write rejection.
- **FR-024**: Groundwork restart tests MUST cover cancellation before admission, admission before cancellation, cancellation before child visibility, cancellation against each child terminal status, terminal notification before/after parent cancellation, and uncertain acknowledgement.
- **FR-025**: Provider wire changes, if any, MUST be versioned and upcast from committed golden fixtures; existing dispatch rows MUST retain safe compatibility defaults.
- **FR-026**: Existing Completed wait behavior, fire-and-forget behavior, artifact/input/context inheritance, safe output capture, unsupported-intent failure, and global mailbox boundaries MUST remain unchanged.
- **FR-027**: This slice MUST NOT implement retry exhaustion/dead-letter/redrive (#681), TestRun scope (#682), or distributed two-node placement/transport (#683).

### Key Entities

- **Terminal child result**: A Completed, Faulted, or Cancelled safe result delivered through the existing deterministic parent bookmark route.
- **Effective cancellation policy**: The replay-stable wait-only decision derived from `WaitForCompletion` and `CancelChildOnParentCancellation` and stored on the dispatch.
- **Admission boundary**: The provider-atomic transition that linearizes child admission against pre-materialization parent cancellation.
- **Child-cancel intent**: Deterministic post-commit responsibility created by an admitted child’s parent cancellation checkpoint.
- **Safe fault summary**: Fixed classification plus stable incident identifiers, excluding all free-form or exception-derived detail.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Replaying Faulted and Cancelled child terminal checkpoints at least three times each produces one equivalent parent-resume intent and one matching terminal dispatch projection.
- **SC-002**: Every non-success result corpus case contains zero child outputs, exception messages, exception types, stack traces, diagnostic payloads, and arbitrary incident metadata.
- **SC-003**: Faulted and Cancelled resumes each complete the activity once, emit exactly one matching graph outcome, and record zero DispatchWorkflow activity faults.
- **SC-004**: Barrier-controlled tests force both cancellation-wins and admission-wins interleavings and repeat the concurrent race at least 100 times; every run observes exactly one durable winner: no child start after cancellation wins, or one durable cancel responsibility after admission wins.
- **SC-005**: Three or more duplicate child-cancel deliveries yield one logical Cancel command identity and preserve Completed, Faulted, and Cancelled child terminal states.
- **SC-006**: Opt-out wait tests and all fire-and-forget cancellation tests record zero child-cancel intents and allow the child to proceed independently.
- **SC-007**: Groundwork recreation at every named boundary converges without an orphaned child, lost cancellation responsibility, duplicate parent completion, or stale-claim admission.
- **SC-008**: Regression suites report zero behavior changes for successful waited completion, fire-and-forget dispatch, and unsupported post-commit intent handling.
- **SC-009**: Architecture audits report no #681 dead-letter/redrive, #682 TestRun, #683 distributed transport, broker, Studio, or WorkflowDefinitionActivity expansion.

## Assumptions

- #675’s contributed handler registration remains the source of truth for intent-kind ownership; handlers expose `HandleAsync` and the aggregate exposes `DispatchAsync`.
- #678’s durable claim/fencing and Groundwork transaction scopes are the provider foundation for an additive admission/cancellation compare-and-transition capability.
- #679’s successful terminal enrichment, deterministic parent-resume identity, bookmark delivery, retry-until-consumed policy, and safe result channel are extended rather than replaced.
- The runtime Cancel command already preserves any child terminal status and is the authoritative child cancellation mechanism.
- Blocking incident state is durable before the workflow-level Faulted checkpoint; only stable incident IDs and fixed classifications are needed by the parent result.
- The broader constitution remains draft/provisional; accepted checkpoint, persistence, single-writer, and actor-boundary decisions govern this work.

## Scope Boundaries

### Included

- Waited-child Faulted and Cancelled terminal resume/result/outcome behavior.
- Safe incident identifiers and fixed non-sensitive diagnostic summaries.
- Wait-only cancellation policy persistence and default behavior.
- Provider-atomic pending cancellation versus child admission.
- Deterministic idempotent child Cancel delivery and race recovery.
- In-memory semantic tests and Groundwork crash/restart convergence.

### Excluded

- Retry exhaustion, dead-letter records, redrive, and operator incident APIs (#681).
- TestRun dispatch authorization, expiry, cancellation policy, and teardown (#682).
- Distributed placement, remote transport, and two-node execution (#683).
- Parent completion propagation, activity timeouts, brokers, Studio UI, and WorkflowDefinitionActivity.
