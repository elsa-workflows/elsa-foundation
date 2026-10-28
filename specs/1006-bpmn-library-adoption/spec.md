# Feature Specification: BPMN library adoption with execution compatibility

**Feature Branch**: `agent/codex/2735-bpmn-switch`

**Created**: 2026-10-25

**Status**: Implemented — effective on merge of [PR #3338](https://github.com/elsa-workflows/elsa-foundation/pull/3338); required checks, review and protected delivery remain separate gates.

**Input**: [Foundation #2735](https://github.com/elsa-workflows/elsa-foundation/issues/2735), second of its three ordered PRs, under [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892). Sipke's end-to-end transfer and continuation authorize implementation. [Parity PR #3335](https://github.com/elsa-workflows/elsa-foundation/pull/3335) delivered the [release comparison](../../docs/reports/evidence/bpmn-adoption-0.5.0/README.md) and [cut ledger](../../docs/reports/evidence/bpmn-adoption-0.5.0/cut-ledger.md). Those are comparison evidence, not switch acceptance.

## User Scenarios & Testing

### User Story 1 - Continue existing processes after upgrading the host (Priority: P1)

An operator upgrades Foundation without losing an existing process's outstanding children, suspended catches, deferred fault or already accepted result. Completed or canceled work cannot restart because of the upgrade.

**Why this priority**: A new interpreter is unusable if it cannot safely continue work already accepted by the current host.

**Independent Test**: Capture actual executions using the current writer, retain their complete checkpoints, reopen them in a fresh upgraded host, and drive their queued callbacks and bookmarks to the expected outcome.

**Acceptance Scenarios**:

1. **Given** a resolved event race with a canceled loser and a trailing live catch, **when** the upgraded host reloads and receives a late loser completion or fault, **then** it preserves the winner, absorbs only the correct canceled invocation, cleans the canceled bookmark and completes the trailing path once.
2. **Given** loser completion is already queued before winner cancellation, **when** the upgraded host consumes both after restart, **then** it proves that order from captured queue/outbox evidence and neither faults nor advances the parent twice.
3. **Given** parallel multi-instance work at one node with distinct invocation and iteration identities, **when** completion, fault and replay are delivered after restart, **then** only the corresponding token progresses.
4. **Given** an actual committed legacy deferred-fault state, **when** the upgraded host resumes it, **then** the original fault and incident fidelity survive, outstanding work is handled according to the selected semantic profile and a native successor cannot run as success.
5. **Given** missing or ambiguous historical correlation, **when** the upgraded host reads it, **then** it reports a specific compatibility failure without choosing an arbitrary child, losing the original checkpoint or dispatching new work.

### User Story 2 - Run BPMN through the shared semantics and ordinary native parents (Priority: P1)

A workflow author embeds a BPMN process in a Sequence or Flowchart and receives the same parent-owned ordering, joins and durable outcome guarantees as other children. BPMN semantics come from the released library.

**Why this priority**: Removing the duplicated engine must deliver a working Foundation integration, including its cancellation and incident boundaries.

**Independent Test**: Run the affected runtime suites and fresh-host persisted scenarios through the actual Foundation host, then test BPMN within both native parents in both child-completion orders.

**Acceptance Scenarios**:

1. **Given** a process with gateways, loops, compensation, boundaries and event subprocesses, **when** its bound children complete, fault or signal, **then** the shared interpreter decides the semantic transition and Foundation commits the resulting state and owed work together.
2. **Given** a child signals and then deliberately faults, **when** an interrupting or non-interrupting BPMN parent consumes the combined outcome, **then** ordered notifications, the original incident and the fault disposition are handled once, including after outbox retry and restart.
3. **Given** the process is embedded in Sequence or Flowchart, **when** BPMN completes, is canceled or faults, **then** the enclosing composite follows its existing ordering/join and fault rules without a parent-type-specific BPMN adapter.
4. **Given** a refused secret collection variable, **when** a multi-instance process reads it, **then** the exact existing refusal code and message survive; the refusal cannot become an empty loop or missing value.
5. **Given** released fixes deliberately supersede old expectations, **when** terminate or same-evaluation interruption is evaluated, **then** no withdrawn work starts and each changed expectation has a named reason and a behavior witness.

### User Story 3 - Keep authoring and Studio interchange usable (Priority: P1)

An author imports, edits, publishes and exports an existing BPMN definition in the normal Studio host. Existing activity slots, inputs, outcomes and serialized authoring fields retain their meaning.

**Why this priority**: Library tests cannot show that Studio and Foundation still agree about the authored process.

**Independent Test**: Exercise registered interchange services and real Studio import → edit → publish → execute → export journeys, compare the frozen activity descriptors and reload representative authored documents.

**Acceptance Scenarios**:

1. **Given** an existing authored process, **when** it is opened and published, **then** the frozen public authoring fingerprint and existing authored schema/child references remain unchanged.
2. **Given** supported timer/message/signal catches, message publication, nested processes and called workflows, **when** imported and executed, **then** the real registered binder produces the intended Elsa activities with correct duration, event, scope and dispatch identity.
3. **Given** a source element identifies a default flow, **when** imported into Studio and exported, **then** the edge default marker and XML default refer to that same flow.
4. **Given** a library descriptor or typed data has no implemented Foundation representation or capability, **when** analyzed or imported, **then** the result explicitly reports the unsupported/degraded surface and never presents erased work or Unknown availability as executable readiness.
5. **Given** an ordinary task compensation handler or a called process with escalation, **when** exercised through the real host, **then** handler identity and interrupting/non-interrupting delivery survive.

6. **Given** a called workflow that ends with an authored BPMN error code, **when** its waited dispatch returns, **then** the caller matches the exact code through the exact call invocation; a different code remains uncaught. The called workflow's original incident remains truthful and ordinary DispatchWorkflow Faulted outcomes retain their current behavior.
7. **Given** a called workflow emits escalation while running or with its terminal outcome, **when** the caller receives it, **then** the exact call invocation receives the ordered control data durably. For a running child, interrupting cancellation reaches that invocation's child workflow and non-interrupting handling leaves it running. Terminal delivery preserves the child's actual terminal status and incident. A terminal signal-and-failure batch is evaluated together, without starting work that a later item withdraws.
8. **Given** concurrent calls, restart, redelivery, an unrelated root or a legacy dispatch without the new capability, **when** control delivery is attempted, **then** it cannot target another invocation, bypass dispatch authority or repeat accepted semantic advancement. Legacy payloads remain readable and absence of capability preserves existing ordinary dispatch policy.

9. **Given** a published broadcast whose first delivery is delayed, **when** matching waits appear before that first delivery, **then** they remain eligible under the existing delivery-time matching rule. The first delivery commits its complete selected cohort durably before dispatching any recipient, including eligible published-trigger starts with exact trigger/binding/executable identity and exact bookmark resumes.
10. **Given** that first recipient snapshot and a partial dispatch failure, concurrent retry or provider restart, **when** the broadcast is delivered again, **then** the same authoritative recipient set is reused with stable per-recipient idempotency. An originally selected recipient cannot advance twice, and a newly re-armed listener cannot receive the older broadcast through retry matching. A crash after snapshot commit but before the first dispatch preserves that same snapshot.

11. **Given** an empty first recipient cohort, **when** a matching wait or published trigger appears before a retry, **then** the durable empty cohort is reused. Likewise, a trigger published after a nonempty snapshot cannot join the older broadcast. Existing correlation, local-target, test-scope and pre-start wait-snapshot eligibility rules still apply.

### Edge Cases

- Re-armed listeners, repeated node invocations and terminal child rows sharing a node ID.
- Fault or completion arriving after cancellation, and duplicate accepted outcomes after parent consumption.
- A nested scope whose token IDs differ from those of its enclosing scope.
- Original deferred faults, diagnostics beyond the retention cap, unresolved stopped-work identities and active compensation runs.
- A failure after checkpoint commit but before outbox acknowledgement, including split/concurrent outbox claims.
- Unsupported date/cycle timers, signal publication, expression conditions and foreign XML retained by the library but not represented by the current authoring envelope.
- A rollback or cleanup that would discard the reader or replay identity required by accepted executions.

## Requirements

### Functional Requirements

- **FR-001**: Adopt released BPMN 0.5.0, qualified source `021d2c5bba494f17d346144d97763cd3b2a13a4b`, and preserve the separation between library payload 1.10.0, Elsa authoring schema 1.0.0 and private execution-state versions.
- **FR-002**: Capture real legacy execution checkpoints before deleting their writer. Each fixture must identify its source and include coherent parent/child state, scheduler work, outbox and bookmarks, plus supporting scoped values and incidents needed to resume.
- **FR-003**: Keep existing accepted definitions, state schemas and identifiers readable. Use an explicit version-aware compatibility contract; do not silently reinterpret old property names or implicitly convert active executions to a new storage schema.
- **FR-004**: Recover exact legacy work correlation from durable identities with validated parent, node/element, iteration and uniqueness. Never use a live-only projection, first match or scope-invocation metadata as a callback identity.
- **FR-005**: Apply ordered library commands and verdicts through Foundation's checkpoint authority. Preserve work-token identity, exact child handle, producing provenance, variable refusal, iteration scope and incident disposition.
- **FR-006**: Use the existing general combined child-parent outcome protocol for signals accompanying intentional fault. Preserve committed-parent addressing and ordinary native behavior; do not simulate separate callbacks against stale state.
- **FR-007**: Bind the existing supported work families to Delay, Event, PublishEvent, DispatchWorkflow and nested BpmnProcess with the same authoring/dispatch policy. Make unsupported release descriptors visible before execution and preserve representable data deliberately.
- **FR-008**: Preserve the frozen `BpmnProcess`/`BpmnDecision` fingerprint and the existing Elsa authored envelope and Studio mirror. A matching fingerprint alone is insufficient: published descriptors and import/edit/export behavior must also agree.
- **FR-009**: Remove the duplicate Foundation interpreter, graph/coordinator, behavior implementations and token-retention policy. Reuse released pruning only after restoring required legacy correlation. Capture diagnostics/history before pruning.
- **FR-010**: Guard against reintroducing deleted semantics and public library type-name duplication. Mutation controls must demonstrate the guard and risky behavior tests can fail.
- **FR-011**: Retain valid host scenarios and reconcile any moved or changed test against the permanent audit and current test discipline. No test-name-only, equal-count or library-only result substitutes for host behavior evidence.
- **FR-012**: Prove fresh upgraded-host recovery, native Sequence/Flowchart composition and actual Studio compatibility before accepting the switch. Record source/head identities, failures, skips and proof limits on the existing issue/PR.
- **FR-013**: Preserve readers and replay/retention identities for accepted executions; reject unsafe cleanup/rollback rather than stranding or resurrecting work.
- **FR-014**: Preserve authored BPMN error codes and ordered escalation across waited called-workflow boundaries using an explicit consumer capability tied to committed dispatch identity. Shared runtime transport must remain independent of BPMN. Carry authored control classification separately from fixed safe diagnostics and ordinary runtime faults; no exception detail or foreign incident absorption is implied. Ordinary DispatchWorkflow defaults, root-notification refusal without capability and existing legacy payload behavior remain unchanged. Terminal notifications and typed failure form one durable caller evaluation, with replay/cancellation tied to the exact dispatch and invocation.

- **FR-015**: Preserve delivery-time broadcast matching by committing the first delivery's complete selected recipient set durably before any recipient dispatch. Retries after partial delivery, acknowledgement loss, concurrent claims or provider restart must reuse that exact snapshot and stable per-recipient idempotency. First-snapshot creation must choose one authoritative set under concurrency, including an authoritative empty set. The cohort includes both eligible published-trigger starts with exact trigger/binding/executable identity and exact bookmark resumes; neither recipient class may be rediscovered on retry. Preserve current eligibility: correlation narrows resume matching, local publication confines ResumeOnly delivery to its publishing execution, test-scope isolation remains intact, and waits created by triggered starts are excluded by the pre-start wait snapshot rule. Waits created before the delayed first delivery remain eligible; listeners created or re-armed after snapshot commitment cannot join that older broadcast. Snapshot/recipient state must remain available while delivery responsibility is owed. A per-attempt in-memory snapshot or a snapshot at publisher commit does not satisfy this requirement. Keep ordinary dispatch defaults, execution/test-scope isolation and the frozen authoring fingerprint intact.

### Key Entities

- **Legacy checkpoint**: An actual committed execution boundary with all dependent durable identities and work needed for continuation.
- **Bound work invocation**: One exact child execution linked to one semantic token, element and optional iteration; node identity alone is insufficient.
- **Versioned state document**: A private execution-state envelope with an explicit compatible reader/writer policy.
- **Authored process**: The existing Elsa schema and activity contracts consumed by publishing, interchange and Studio.
- **Called-workflow control outcome**: Ordered authored notifications and optional terminal classification, authorized by one exact waited dispatch and interpreted by its opted-in consumer. It is distinct from exception diagnostics and the called workflow's incident.
- **Host capability result**: A truthful distinction between available, unavailable and unknown work, separate from lossless import or semantic support.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every committed legacy fixture reloads in a fresh upgraded host with its exact active/canceled work identity and reaches its expected outcome; deliberate missing/ambiguous identity cases fail safely without dispatch.
- **SC-002**: Both native parent witnesses pass both completion orders, cancellation/fault and outcome replay cases with no duplicate successor or parent completion.
- **SC-003**: The frozen fingerprint is byte-identical, actual published activity descriptors match it, and representative existing authored documents complete the Studio import/edit/publish/export journey without losing supported behavior.
- **SC-004**: The affected runtime/interchange suites and required repository checks pass on the reviewed switch head; each historical scenario is retained or explicitly reconciled.
- **SC-005**: No duplicate semantic engine or local pruning policy remains, and the guard fails under deliberate reintroduction.

## Assumptions

- The completed parity comparison and qualified 0.5.0 release are the starting point; new neutral gaps are upstreamed before claiming switch acceptance.
- This unit changes the embedded Foundation adapter. Direct production hosting #2898 and the six #2907 acceptance criteria remain separate until their own evidence exists.
- Native parents retain their existing fault-status differences. StateMachine, Orleans, task-runner follow-up, arbitrary alteration, live-instance migration and a general operator console are outside this unit.
- Existing execution compatibility is required even though Elsa 4 is unreleased. Actual legacy fixtures must decide whether a proposed transition is safe; synthetic state probes are supplemental.
- ADR0063 remains proposed throughout this PR. Acceptance is the third ordered PR after resulting-main delivery verification.

## Delivery refinement: called-workflow boundary

Source-host testing confirmed ordinary waited completion and catch-all error handling, but those baselines do not establish coded-error or escalation preservation. The existing same-workflow combined-fault protocol requires an actual faulted child and its local incident; DispatchWorkflow normally completes with a Faulted outcome. A called root also has no local activity parent. FR-014 refines the already-required Call Activity behavior so implementation cannot fake a leaf fault, infer authority from a parent workflow ID alone, overload safe diagnostics, or split a terminal control batch. This refinement authorizes the narrow generic transport needed for that behavior within this switch; it does not change ordinary dispatch policy or broaden production acceptance.

Refinement review completed with independent correctness, standards and acceptance lenses. The standards review corrected the running-versus-terminal escalation wording; all three lenses are clean. Plan/task refinement and transport implementation remain separate gates.

## Owner decision: broadcast recovery matching

The owner selected: "Keep delivery-time matching; persist its first recipient snapshot" in the continuation's broadcast recovery card. This settles the matching rule for #2735 and adds FR-015 to the existing switch delivery. Earlier per-attempt snapshot and happy-path fan-out receipts remain limited to their stated source; they do not establish this durable recipient-set recovery requirement. Specification review precedes the storage/dispatch plan and task refinement, then implementation and actual host/provider recovery proof. No additional owner authorization question is required for the selected rule.

FR-015 specification review completed with independent correctness, standards and acceptance lenses. The review made the complete cohort explicit: both published-trigger starts and exact bookmark resumes, including an authoritative empty set and existing eligibility constraints. The storage/dispatch plan (`84f4305f6`) and subsequent T046–T051 refinement were independently
reviewed in order. Implementation, actual legacy preflight and actual host/fresh-EF recovery remain
open; none is established by the planning reviews.
