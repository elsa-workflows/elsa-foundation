# Feature Specification: Contributed Runtime Intent Handlers

**Feature Branch**: `codex/dispatch-workflow-program`

**Created**: 2026-07-16

**Status**: Approved

**Input**: GitHub issue #675, “Route runtime post-commit intents through contributed handlers”

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Contribute a runtime intent handler (Priority: P1)

As a runtime module author, I can register the handler for a named post-commit intent kind through one documented contribution surface so that cross-execution work can reuse the global runtime delivery path.

**Why this priority**: DispatchWorkflow cannot add child-start or parent-resume delivery until the scheduler-only path becomes extensible without replacing the runtime dispatcher.

**Independent Test**: Register a marker handler, commit a checkpoint containing its intent, run a global resumption sweep, and verify the marker handler receives that committed intent outside a workflow execution mailbox.

**Acceptance Scenarios**:

1. **Given** a module contributes one handler for a named kind, **When** committed work of that kind becomes deliverable, **Then** the global resumption sweep invokes that handler and records successful delivery.
2. **Given** the identical handler contribution is repeated, **When** runtime delivery is composed, **Then** the handler runs once.
3. **Given** different handlers claim the same kind, **When** runtime delivery is composed, **Then** composition fails deterministically and identifies the kind and conflicting handlers.

---

### User Story 2 - Preserve scheduler post-commit delivery (Priority: P1)

As a host operator, I can upgrade to the contributed-handler model without changing scheduler-work identifiers, validation, queueing, ordering, retry, or delivery semantics.

**Why this priority**: The prefactor is safe only if existing runtime behavior remains unchanged.

**Independent Test**: Run the existing scheduler checkpoint, outbox, command-drain, and resumption suites and compare the persisted scheduler intent and enqueued work identities.

**Acceptance Scenarios**:

1. **Given** a scheduler-work intent, **When** it is processed, **Then** the same persisted payload is validated and the same work item is enqueued.
2. **Given** malformed scheduler work, **When** delivery is attempted, **Then** the existing safe outbox failure path captures the failure and applies the configured retry policy.

---

### User Story 3 - Fail unsupported intent kinds safely (Priority: P2)

As a runtime operator, I receive a visible delivery failure when committed work names an unsupported intent kind, rather than having the work silently acknowledged or lost.

**Why this priority**: Extensibility must not weaken outbox safety or hide composition mistakes.

**Independent Test**: Commit an intent whose kind has no registered handler, process it through the global sweep, and verify safe non-delivery plus the existing policy-selected persisted failure state and actionable summary.

**Acceptance Scenarios**:

1. **Given** no handler claims an intent kind, **When** delivery is attempted, **Then** the outbox records the existing policy-selected failure state containing the unsupported kind without acknowledging delivery.

### Edge Cases

- Blank intent kinds and invalid committed intent payloads remain rejected by existing model validation.
- Duplicate contributions are idempotent only when both the intent kind and handler type are identical.
- Handler conflicts are resolved independently of module registration order.
- A handler exception follows the existing safe outbox failure and retry behavior.
- Cancellation interrupts delivery without recording a false successful or failed attempt.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Runtime modules MUST be able to contribute one handler for a named post-commit intent kind through one public, documented registration mechanism.
- **FR-002**: Repeating the same handler contribution for the same kind MUST be idempotent and MUST result in one invocation.
- **FR-003**: Distinct handlers claiming the same kind MUST fail deterministically with the intent kind and both handler identities in the failure context.
- **FR-004**: Scheduler-work delivery MUST preserve its existing persisted identifiers, payload validation, queueing behavior, delivery ordering, and failure semantics.
- **FR-005**: An intent kind with no contributed handler MUST fail through the existing safe outbox failure path and MUST NOT be acknowledged as delivered.
- **FR-006**: A global resumption sweep MUST process all deliverable registered intent kinds rather than filtering delivery to scheduler work.
- **FR-007**: Contributed cross-execution handlers MUST execute from the global post-commit/resumption path outside workflow execution actor mailboxes.
- **FR-008**: The implementation MUST include a guardrail that proves a contributed marker handler is invoked through a real checkpoint commit, outbox record, and resumption sweep.
- **FR-009**: The contribution model MUST introduce no broker-specific package or contract and no dependency on the workflow-definition activity.
- **FR-010**: The owning runtime extension-point catalog MUST document the contribution contract, registration mechanism, duplicate/conflict rules, delivery context, and failure semantics.

> **Amendment (ADR 0047 D1+D2, [spec 123](../123-replaysafe-hop-fusion/spec.md), 2026-07-22 — travels with the implementing unit, same discipline as the ADR 0032 R2 FR amendment).** FR-004's "delivery ordering / queueing behavior" and any wording elsewhere that presumes **one work item per scheduler stage** (the 5–7-hop-per-activity model) is scoped to the discrete durable path. Inside a live coalescing burst, for a `SideEffectProfile.ReplaySafe` activity with the fusion toggle on, the runtime MAY execute the schedule→start→invoke stages (and a single-predecessor `ReplaySafe`-parent completion cascade) as **fused in-process handler passes** — the intermediate `StartActivity` / `InvokeActivity` (and intermediate completion-cascade) work items are then **never enqueued**. This changes dispatch locality only: the durable wire contract, command kinds, persisted identifiers, payload validation, and the delivery contract for items that ARE enqueued are unchanged, and a run with fusion disabled commits byte-identical durable state. `External`/unmarked activities keep every per-stage work item exactly as this FR describes.

### Key Entities

- **Post-commit intent kind**: Stable ordinal identifier selecting the handler responsible for committed outbound runtime work.
- **Intent handler contribution**: The pairing of one intent kind with one handler identity in a runtime composition.
- **Committed post-commit intent**: Durable outbound runtime work created atomically with a checkpoint and delivered later through the outbox.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A marker contribution completes one end-to-end checkpoint-to-resumption delivery with exactly one handler invocation.
- **SC-002**: Repeating the same contribution any number of times still produces exactly one handler invocation.
- **SC-003**: Every conflicting same-kind composition fails before intent delivery and names all conflicting identities.
- **SC-004**: Every existing scheduler post-commit and runtime resumption test remains green without changing persisted scheduler identities.
- **SC-005**: Every unsupported intent delivery records the existing policy-selected failed state with a safe diagnostic and zero silent acknowledgements.

## Assumptions

- The existing checkpoint commit, outbox store, outbox processor, and global resumption service remain the authoritative durability and retry seams.
- The scheduler-work handler is migrated into the same contribution mechanism as future DispatchWorkflow handlers.
- Handler execution lifetime follows the runtime delivery scope; handlers do not execute inside workflow actor mailboxes.
- The broader constitution remains draft/provisional; accepted ADR 0020 and the current runtime contracts control checkpoint and post-commit behavior.

## Scope

### In Scope

- Generalizing post-commit intent delivery and registering the scheduler handler through the new mechanism.
- Unfiltering global resumption delivery so registered cross-execution kinds can be processed.
- Focused unit, integration-guardrail, scheduler-regression, and resumption tests.
- Runtime extension-point documentation.

### Out of Scope

- DispatchWorkflow child-start or parent-resume handlers themselves.
- Broker, service-bus, or transport-specific abstractions.
- WorkflowDefinitionActivity, Studio, and publication behavior.
- New retry, dead-letter, or redrive policy beyond the existing outbox behavior.
