# Feature Specification: Durable and Inspectable Detached Dispatch

**Feature Branch**: `codex/dispatch-workflow-program`

**Created**: 2026-07-16

**Status**: Approved

**Input**: GitHub issue #678, “Make detached dispatch durable and inspectable with Groundwork,” under parent #674

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Recover detached dispatch after process failure (Priority: P1)

As an operator, I can restart a Groundwork-backed host after a parent committed detached dispatch and still observe exactly one logical child execution, because the dispatch record and child-start work were committed atomically and are resumed durably.

**Why this priority**: Fire-and-forget is trustworthy only if a successful parent checkpoint cannot lose its detached child during a crash.

**Independent Test**: Crash or recreate the runtime after the parent checkpoint, before delivery, during an outbox lease, and after child materialization; resume delivery and verify one dispatch, one child execution identity, and convergent outbox state.

**Acceptance Scenarios**:

1. **Given** a parent completes a `DispatchWorkflow` activity, **When** its Groundwork checkpoint commits, **Then** the Pending dispatch record, child-start outbox item, parent state, and commit marker become visible together or not at all.
2. **Given** the process stops after that checkpoint but before child-start delivery, **When** a new process resumes the outbox, **Then** the deterministic child execution is materialized exactly once.
3. **Given** delivery is interrupted after acquiring an outbox lease or after child materialization, **When** delivery is retried, **Then** idempotent dispatch converges without a second logical child.
4. **Given** the same parent checkpoint or child-start intent is delivered more than once, **When** Groundwork reconciles it, **Then** equivalent replay is accepted and conflicting replay fails closed.

---

### User Story 2 - Track detached child lifecycle independently (Priority: P1)

As an operator, I can follow a detached dispatch from Pending to Started and then to the child's terminal status even after the parent execution has completed.

**Why this priority**: Detaching the parent must not detach operational truth about the child.

**Independent Test**: Complete the parent, start and terminally checkpoint the child, and verify monotonic dispatch transitions remain queryable throughout.

**Acceptance Scenarios**:

1. **Given** committed child-start work, **When** the start is admitted or a duplicate start proves the same child already exists, **Then** the dispatch transitions from Pending to Started without changing immutable identity.
2. **Given** a Started dispatch and a child execution that completes, faults, or is cancelled, **When** the child's terminal checkpoint commits, **Then** the linked dispatch transitions to the corresponding terminal status in the same durable checkpoint boundary.
3. **Given** the parent has already completed, **When** the child later starts or becomes terminal, **Then** lifecycle tracking continues and the dispatch remains linked to both executions.
4. **Given** a replayed or out-of-order lifecycle observation, **When** it is projected, **Then** an equivalent state is idempotent, a legal forward transition succeeds, and regression or identity mutation fails closed.

---

### User Story 3 - Inspect dispatches safely through authenticated runtime capabilities (Priority: P1)

As an authenticated runtime operator, I can list and get dispatches by parent execution, child execution, and lifecycle status without seeing input values or unsafe diagnostics.

**Why this priority**: Durable detached work needs an operable control-room surface, not only storage documents.

**Independent Test**: Call the runtime list/get routes with and without runtime-read permission, exercise every filter, and inspect serialized response bodies for both expected safe fields and forbidden value/exception material.

**Acceptance Scenarios**:

1. **Given** an authenticated caller with runtime-read permission, **When** dispatches are listed by parent, child, status, or their supported intersection, **Then** a bounded stable result containing only authorized records is returned.
2. **Given** a dispatch identifier, **When** an authorized caller gets it, **Then** the safe lifecycle projection is returned, or not-found when it does not exist in the caller's access scope.
3. **Given** an unauthenticated or unauthorized caller, **When** either capability is invoked, **Then** the existing runtime endpoint authorization policy denies access.
4. **Given** inputs, failures, outputs, or diagnostic metadata associated with a dispatch, **When** a projection is serialized, **Then** it exposes lifecycle, child type/identity, capture descriptors, and safe diagnostic classification only—never raw input values, exception objects/messages, stack traces, or output values marked redacted.

---

### User Story 4 - Detect unsafe production composition and retain linked evidence (Priority: P1)

As a host operator, I receive an unsafe readiness result when production detached dispatch is enabled without the complete durable checkpoint, outbox, and background resumption composition, and retained dispatch evidence is not removed while either linked execution remains retained.

**Why this priority**: A partially durable host is more dangerous than an explicitly lightweight in-memory host because it can advertise guarantees it cannot provide.

**Independent Test**: Evaluate readiness for valid in-memory development, complete Groundwork production, and each incomplete production composition; delete linked execution state in both orders and verify dispatch eligibility only after both are gone.

**Acceptance Scenarios**:

1. **Given** production detached dispatch is enabled with a non-durable checkpoint store, outbox store, or no background resumption service, **When** readiness is evaluated, **Then** it reports unsafe with stable component classifications and no connection secrets.
2. **Given** complete Groundwork checkpoint, dispatch, outbox, durable scheduler, and resumption composition, **When** readiness is evaluated, **Then** it reports ready.
3. **Given** explicit in-memory development composition, **When** readiness is evaluated, **Then** it reports its process-local restart guarantee rather than claiming production durability.
4. **Given** a retained dispatch and one or both linked workflow executions, **When** normal retention removes only one execution, **Then** the dispatch remains; it becomes deletion-eligible only after both linked executions are no longer retained.

### Edge Cases

- The Groundwork provider reports no cross-unit atomic transaction boundary.
- The same dispatch document ID exists with different immutable context or a backwards lifecycle transition.
- A checkpoint commit acknowledgement is uncertain after the provider may already have committed.
- An outbox lease expires while another process resumes the same child-start work.
- The child materializes but the process stops before marking the child-start item delivered or projecting Started.
- A terminal child checkpoint arrives before a separate Started projection.
- The parent completes or is removed before the child reaches a terminal state, and vice versa.
- Parent, child, and status filters produce no matches, invalid status text, or an unbounded request.
- Tenant or partition context conflicts with a persisted dispatch record.
- Metadata contains raw values, exception text, stack traces, output payloads, or keys not explicitly approved for projection.
- A host replaces only some in-memory runtime services with Groundwork-backed implementations.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Groundwork MUST persist workflow-dispatch state changes through the same document unit-of-work as parent workflow state, activity state, post-commit outbox work, and checkpoint commit marker.
- **FR-002**: A Groundwork checkpoint containing dispatch changes MUST require a provider transaction boundary that is atomic across every participating runtime document kind.
- **FR-003**: Groundwork MUST persist one document per deterministic dispatch identity and MUST reject same-ID writes that change immutable identity or context.
- **FR-004**: Equivalent checkpoint replay MUST resolve through the durable checkpoint marker and return the same pending post-commit work identities; a different fingerprint for the same commit MUST fail closed.
- **FR-005**: Post-commit outbox delivery MUST use a compatibility-safe additive claim capability with an atomic provider-backed owner, fencing token, and visibility expiry; only the current claim owner MAY acknowledge delivery or record a delivery failure, while the existing `IRuntimePostCommitOutboxStore` surface and model constructors remain source compatible.
- **FR-006**: Child-start delivery MUST remain recoverable from the durable outbox after process recreation and claim expiry; a stale owner MUST be rejected and the next valid claimant MUST be able to resume the item.
- **FR-006a**: Duplicate delivery before, during, or after child materialization MUST converge on the deterministic child execution identity and MUST NOT create a second logical child.
- **FR-006b**: Dispatch-start replay MUST reuse deterministic command, envelope, scheduler-work, and root activity execution identities so process recreation cannot turn one logical child into a second root chain before its first checkpoint.
- **FR-007**: Dispatch lifecycle MUST support monotonic Pending, Started, and terminal Completed, Faulted, Cancelled, or DispatchFailed states while preserving immutable linkage and context.
- **FR-008**: A successfully admitted child start, including idempotent duplicate admission for the same deterministic child, MUST durably advance its dispatch to Started.
- **FR-009**: A child terminal workflow checkpoint MUST durably project the linked dispatch terminal state in the same checkpoint transaction as the child's own terminal execution state.
- **FR-010**: Lifecycle projection MUST continue independently of parent liveness or terminal status.
- **FR-011**: Replayed lifecycle projection MUST be idempotent; status regression, illegal transitions, timestamp regression, or immutable-context mutation MUST fail closed.
- **FR-011a**: Pending record creation MUST be accepted only from its parent execution checkpoint; non-Pending lifecycle projection MUST be accepted only from the exact linked child execution checkpoint, the trusted child-admission lifecycle service, or the claim-fenced final-delivery-failure service; unrelated execution commits MUST fail closed.
- **FR-011b**: Child-terminal projection MUST deterministically derive status and `UpdatedAt` from the child checkpoint and MUST reproduce the same state change and checkpoint fingerprint on replay even when the dispatch is already terminal.
- **FR-011c**: A child terminal state reached synchronously before the post-start Started update MUST supersede Started; the later Started update MUST become a safe no-op rather than regress the terminal record.
- **FR-012**: Runtime composition MUST provide an additive dispatch query contract supporting bounded stable queries by parent workflow execution ID, child workflow execution ID, lifecycle status, and supported filter intersections while preserving the existing `IWorkflowDispatchStore` surface for third-party implementers.
- **FR-013**: Groundwork query routes MUST use declared provider-neutral indexes and MUST enforce tenant/access scope before returning dispatches.
- **FR-014**: Runtime HTTP list/get endpoints MUST use the existing authenticated runtime-read permission policy.
- **FR-015**: Operational views MUST expose only stable dispatch identity/linkage, lifecycle, mode, child executable type/identity, safe input capture descriptors, timestamps, and an explicit allowlist of diagnostic classifications.
- **FR-016**: Operational views MUST NOT expose raw input values, variables, stimulus, authority secrets, exception objects or messages, stack traces, raw output values, or values classified as redacted.
- **FR-017**: List results MUST be bounded and deterministically ordered; malformed filters MUST produce safe client errors rather than scans or ambiguous matches.
- **FR-018**: A dispatch record MUST remain retained while either its parent or child workflow execution state remains retained.
- **FR-018a**: A Pending or Started dispatch MUST retain its pinned child executable artifact until child materialization and normal linked-record retention no longer require it.
- **FR-019**: Dispatch retention cleanup MUST delete only terminal records for which both linked execution states are absent at a guarded final recheck; Pending or Started records and uncertain reads MUST be retained.
- **FR-020**: Production detached-dispatch readiness MUST verify durable checkpoint commit, workflow-dispatch store, post-commit outbox, durable scheduler/continuation path, and background resumption services as one composition.
- **FR-021**: Missing or incompatible required durability components MUST yield an unsafe readiness result with stable component codes and without provider connection details.
- **FR-022**: Explicit in-memory composition MUST identify its guarantee as process-local and MUST NOT be represented as restart-safe production durability.
- **FR-022a**: Runtime MUST expose readiness through `IWorkflowDispatchReadinessAssessor.AssessAsync` and integrate the assessment with host readiness/health reporting; this slice MUST report Unsafe or ProcessLocal composition accurately without changing existing host startup behavior.
- **FR-022b**: Final child-start delivery failure and its `DispatchFailed` lifecycle projection MUST be authorized by the current outbox claim and committed atomically, so a process failure cannot leave a terminal outbox item with a permanently Pending dispatch.
- **FR-023**: Groundwork registration, storage manifest, coverage ledger, document versions/fixtures, and provider initialization tests MUST include the workflow-dispatch storage unit and its query indexes.
- **FR-024**: Provider-backed acceptance tests MUST cover atomic rollback, uncertain acknowledgement reconciliation, restart before delivery, lease expiry, duplicate materialization, Started projection, terminal projection after parent completion, and retention eligibility.
- **FR-025**: This slice MUST NOT add waited result/resume behavior (#679), define new child fault/cancellation propagation policy (#680), add retry exhaustion/dead-letter/redrive (#681), enable TestRun dispatch (#682), or add distributed placement/transport (#683).

### Key Entities

- **Durable dispatch record**: Provider-backed lifecycle document identified by the deterministic dispatch ID and linked immutably to one parent activity and one child execution.
- **Dispatch lifecycle projection**: The monotonic operational state derived from committed child-start admission and child terminal checkpoints.
- **Safe dispatch view**: Runtime API representation containing approved identifiers, status, type, capture shape, timestamps, and classified diagnostics but no values or exception material.
- **Dispatch readiness assessment**: Stable report describing whether the selected host composition provides process-local or production restart-safe detached dispatch.
- **Linked-execution retention rule**: A dispatch is retained until neither the parent nor child execution remains retained.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At every tested parent-checkpoint crash boundary, the dispatch document and child-start outbox document are either both absent or both committed with the same commit marker.
- **SC-002**: Restart and duplicate-delivery scenarios produce exactly one deterministic child execution ID and one dispatch record in 100% of provider tests.
- **SC-003**: Every tested dispatch follows one legal monotonic lifecycle path, and all illegal regression or immutable-mutation attempts are rejected.
- **SC-004**: Child terminal status remains observable after parent completion in all completed, faulted, and cancelled lifecycle projection tests without introducing their propagation semantics.
- **SC-005**: Authorized list/get tests resolve records by parent, child, and status; unauthorized tests return the existing authorization denial and expose zero records.
- **SC-006**: Serialized operational responses contain zero raw input/output values, exception messages, stack traces, or non-allowlisted diagnostic metadata across the security fixture corpus.
- **SC-007**: A retained record survives removal of either linked execution alone and is deleted only after both are absent in 100% of retention tests.
- **SC-008**: Every incomplete production composition test reports unsafe and the complete Groundwork composition reports ready; explicit in-memory composition reports process-local guarantees.
- **SC-009**: Groundwork runtime, SQLite, PostgreSQL, SQL Server, and MongoDB manifest/registration compatibility suites accept the new storage unit without provider-specific domain branching.
- **SC-010**: Architecture audits report no waited resume/result, fault/cancellation propagation policy, redrive, TestRun, broker, or distributed-placement expansion.

## Assumptions

- Existing deterministic dispatch, child execution, start-intent, and idempotency identities from #676 remain authoritative. The retained-start dispatcher derives stable internal command, envelope, scheduler-work, and root activity identities from the committed server-owned dispatch record without widening the public start request.
- Existing Groundwork checkpoint commit markers, cross-unit document transactions, durable outbox documents, durable scheduler queue, and background resumption pump are extended. #678 adds the missing provider-backed outbox claim/visibility lease rather than assuming one exists.
- The existing runtime-read endpoint permission is the authentication/authorization gate for dispatch inspection; store access context provides the tenant boundary.
- Existing workflow terminal checkpoints already establish Completed, Faulted, and Cancelled execution status. This slice mirrors that status into detached dispatch inspection but leaves propagation and parent behavior to #680.
- DispatchFailed is a lifecycle representation for safe delivery failure; retry exhaustion, dead-letter handling, and redrive remain #681.
- Normal workflow execution retention is authoritative. This slice adds linked cleanup eligibility and a safe collector seam rather than inventing a second retention duration.
- The broader constitution remains draft/provisional; accepted checkpoint, persistence, API permission, and provider-neutral manifest contracts govern this work.

## Scope Boundaries

### Included

- Atomic Groundwork dispatch/outbox checkpoint persistence and restart convergence.
- Durable lifecycle projection through Started and child terminal states.
- Authenticated safe runtime list/get inspection by parent, child, and status.
- Linked-execution dispatch retention cleanup.
- Production composition readiness and explicit in-memory guarantee reporting.
- Provider-neutral manifests, versions, fixtures, provider tests, documentation, and architecture audits.

### Excluded

- Wait-for-completion, output projection, bookmark resume, and success result semantics (#679).
- Parent/child fault and cancellation propagation policy (#680).
- Retry exhaustion, dead-letter storage, redrive APIs, and operator redrive authorization (#681).
- TestRun dispatch authorization/scope (#682).
- Distributed placement, cross-node execution, or transport selection (#683).
- Raw operational input/output values and unrestricted diagnostic metadata.
