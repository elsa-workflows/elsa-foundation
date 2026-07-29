# Feature Specification: Durable Runtime Alterations

**Feature Branch**: `codex/1016-runtime-alterations`

**Created**: 2026-07-26

**Status**: Approved

**Input**: User description: "Implement GitHub issue #1016 end to end with Elsa 3 alteration
parity for cancel, schedule, reschedule, variable modification, and migration; support single and
bulk targeting through one durable API with auditable per-instance results."

**Program Goal**: [Runtime Alterations](../../docs/program-goals/runtime-alterations.md)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Submit and track durable alterations (Priority: P1)

As an operator, I can submit an ordered alteration plan for one workflow execution or a query-selected
cohort and track durable per-execution outcomes, so large operational changes remain safe across
disconnects, retries, restarts, and partial target failures.

**Why this priority**: Durable orchestration, immutable target selection, and trustworthy results are
the shared foundation every alteration kind needs.

**Independent Test**: Submit the same plan against explicit and query-selected targets, interrupt and
resume processing, and verify one sealed target cohort, one atomic job per target, stable counts, and
idempotent submission and execution.

**Acceptance Scenarios**:

1. **Given** a valid plan selecting matching workflow executions, **when** it is accepted, **then** the
   service durably captures and deduplicates the complete target cohort before starting any job.
2. **Given** target capture spanning multiple pages, **when** an earlier page's workflows would no
   longer match after alteration, **then** later membership remains unchanged because execution starts
   only after the cohort is sealed.
3. **Given** one target whose ordered alterations fail preflight, **when** its job finishes, **then**
   none of that job's mutations commit, its failing alteration is recorded, later alterations are
   skipped, and jobs for other targets continue.
4. **Given** a timed-out submission, **when** the caller repeats the same tenant-scoped idempotency key
   and canonical request, **then** the existing plan is returned without duplicate work.
5. **Given** a plan in progress, **when** an authorized operator cancels it, **then** target capture and
   pending jobs stop while an already-running atomic job finishes and remains visible in the counts.

---

### User Story 2 - Cancel workflow executions (Priority: P1)

As an operator, I can cancel one or many workflow executions through the alteration surface so
existing runtime cancellation semantics are available as an authenticated, durable operation.

**Why this priority**: Cancellation is the issue's minimum required behavior and already has a robust
runtime meaning.

**Independent Test**: Submit `CancelWorkflow` for running, suspended, already-cancelled, and completed
executions and verify terminal cleanup, idempotent terminal outcomes, authorization, and per-target
results.

**Acceptance Scenarios**:

1. **Given** an active workflow execution, **when** its cancellation job commits, **then** the existing
   runtime cancellation path cleans up owned continuation work and records a successful result.
2. **Given** an already-terminal workflow execution, **when** cancellation is applied, **then** no
   history is rewritten and the result reports the terminal no-op deterministically.
3. **Given** `CancelWorkflow` combined with another alteration, **when** the plan is submitted,
   **then** submission fails before target capture because cancellation is exclusive within a job.

---

### User Story 3 - Modify workflow variables safely (Priority: P2)

As an operator, I can replace an existing workflow-scoped variable by stable reference identity so I
can correct durable workflow data without accidentally changing a shadowed container value or
overwriting concurrent runtime progress.

**Why this priority**: Variable repair is a common operational need but must respect lexical scope,
declared type, value protection, and optimistic concurrency.

**Independent Test**: Modify a root workflow variable across one and many executions, then exercise
missing references, invalid values, shadowed container values, stale revisions, and sensitive data
to verify atomic rejection and safe audit output.

**Acceptance Scenarios**:

1. **Given** an existing workflow-scoped variable and an unchanged captured root-frame revision,
   **when** a compatible replacement is applied, **then** only that stable variable reference changes.
2. **Given** a stale root-frame revision, missing reference, type mismatch, or container-scoped
   reference, **when** the job preflights, **then** the complete job fails without mutation.
3. **Given** a sensitive replacement value, **when** the plan and job are inspected, **then** neither
   the previous nor replacement value appears in durable result or audit records.

---

### User Story 4 - Schedule and reschedule activities (Priority: P2)

As an operator, I can schedule an authored activity under an explicit active parent or supersede an
eligible activity execution with a fresh logical execution, so I can repair continuation state
without rewriting history or inventing runtime identity.

**Why this priority**: Activity scheduling parity is valuable only if scopes, bookmarks, provenance,
inputs, incidents, and lineage remain internally consistent.

**Independent Test**: Schedule an authored node beneath an active parent and reschedule eligible and
ineligible activity states; verify fresh deterministic identities, normal authored inputs, lineage,
resource cleanup, incident rules, and atomic rollback.

**Acceptance Scenarios**:

1. **Given** an authored node and compatible active parent activity execution, **when**
   `ScheduleActivity` commits, **then** the runtime creates a fresh execution with derived scope,
   provenance, artifact identity, and normally evaluated authored inputs.
2. **Given** an eligible scheduled, waiting, suspended, or faulted activity execution, **when**
   `RescheduleActivity` commits, **then** the source is superseded without being rewritten and a
   distinct server-identified execution records explicit lineage to it.
3. **Given** a running, completed, cancelled, or recovered activity execution, **when** rescheduling
   is attempted, **then** the job fails preflight without changing workflow state.
4. **Given** a faulted activity with a blocking incident, **when** rescheduling is attempted, **then**
   the job fails without mutation and requires the separate recovery/redrive operation to make the
   source incident non-blocking first.
5. **Given** a terminal faulted workflow, **when** an activity reschedule is requested, **then** it is
   rejected and the workflow is not implicitly recovered.

---

### User Story 5 - Migrate a compatible suspended execution (Priority: P2)

As an operator, I can migrate a safely suspended workflow execution to an exact compatible retained
artifact of the same definition without corrupting durable runtime state.

**Why this priority**: Migration completes the requested Elsa 3 alteration family while replacing its
unsafe graph swap with an explicit compatibility gate.

**Independent Test**: Migrate a quiescent suspended execution between compatible artifacts, then vary
definition identity, nodes, activity contracts, bookmarks, scopes, variables, and liveness to prove
that any incompatibility rejects the whole job.

**Acceptance Scenarios**:

1. **Given** a suspended, fully quiescent execution and an exact compatible target artifact from the
   same definition, **when** migration commits, **then** only the pinned artifact and its validated
   runtime projections advance atomically.
2. **Given** active work or an incompatible node, activity contract, bookmark, scope, or variable
   declaration, **when** migration preflights, **then** the execution remains pinned to its current
   artifact.
3. **Given** `Migrate` after another alteration or more than once, **when** the plan is submitted,
   **then** it is rejected before target capture.
4. **Given** variable or activity alterations after one valid migration, **when** the job preflights,
   **then** they are validated against the post-migration artifact and staged state.

---

### User Story 6 - Extend and inspect the alteration surface (Priority: P3)

As a trusted extension developer or operator, I can contribute schema-versioned alteration handlers
and inspect safe structured outcomes without coupling stored plans to implementation types.

**Why this priority**: Stable extension and audit contracts prevent the initial built-ins from
becoming another closed framework enum or a sensitive payload archive.

**Independent Test**: Register a namespaced custom alteration, submit and execute it through the same
atomic job boundary, and verify unknown versions, unsafe side effects, duplicate registrations, and
sensitive result material are rejected.

**Acceptance Scenarios**:

1. **Given** a trusted host-registered handler for a stable kind and schema version, **when** its
   envelope is submitted, **then** it participates in the same validation, atomic staging, retry, and
   result rules as a built-in alteration.
2. **Given** an unknown kind or schema version, **when** a plan is submitted, **then** it is rejected
   before target capture.
3. **Given** a custom handler that violates its trusted-host, side-effect-free preflight contract,
   **when** it fails, **then** no staged workflow mutation commits and the violation is treated as a
   host-extension defect rather than a client-authored alteration.
4. **Given** plan and job reads, **when** an authorized operator inspects them, **then** typed codes,
   safe messages, timestamps, structural identifiers, and operator provenance are available without
   CLR type names, variable values, payload copies, stack traces, or secrets.

### Edge Cases

- An explicit target identity appears more than once or a paged query yields duplicates.
- A query matches no workflow executions.
- Target capture is interrupted, retried, cancelled, or permanently fails before sealing.
- A targeted workflow is deleted or changes state, artifact, root-frame revision, or authority
  metadata after capture but before job execution.
- Multiple workers receive the same target-capture page, job, or execution command.
- A worker loses acknowledgement after a checkpoint commits.
- An operator loses permission after a plan is accepted.
- Cancellation races with target sealing, job dispatch, and an in-flight actor checkpoint.
- A plan mixes duplicate variable references, duplicate activity sources, or activity operations
  whose scopes conflict.
- A schedule references an inactive or structurally incompatible parent.
- A reschedule source owns bookmarks, timers, queued work, incidents, private state, or descendant
  executions that cannot be reconciled atomically.
- A migration target artifact disappears or is no longer the exact requested identity.
- A supported handler is removed after a plan is accepted but before its jobs execute.
- Result paging occurs while jobs are completing.
- A configured persistence provider restarts between each durable lifecycle phase.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose one durable alteration-plan submission contract for both
  single-execution and bulk operations; it MUST NOT expose a separate immediate server-side `run`
  contract.
- **FR-002**: A submission MUST contain exactly one target selector: a non-empty set of explicit
  workflow execution identities or a workflow-execution query.
- **FR-003**: Query selection MUST use the supported workflow-execution filter vocabulary and MUST
  remain constrained to the caller's authorized tenant and execution-authority scope. The concrete
  allowed fields, matching rules, and stable ordering MUST be frozen in this work unit's public API
  contract before implementation.
- **FR-004**: Every accepted plan MUST enter a durable target-capture phase that uses stable keyset
  pagination, persists and deduplicates every matching execution identity, and seals the complete
  cohort before any job starts.
- **FR-005**: The public contract MUST impose no fixed target-count limit and MUST never silently
  truncate target capture; deployment-level storage quotas or backpressure MAY reject or pause work
  explicitly without changing membership semantics.
- **FR-006**: Target capture MUST be replay-safe and resumable after interruption without losing or
  duplicating target membership.
- **FR-007**: Target-capture failure MUST start no jobs; cancellation during capture MUST stop further
  capture and start no jobs.
- **FR-008**: A sealed target snapshot MUST remain immutable even when a workflow's mutable query
  fields change later.
- **FR-009**: The plan MUST create one durable, deterministically identified alteration job per sealed
  workflow execution target.
- **FR-010**: Jobs for different targets MUST progress independently with bounded configurable
  concurrency that never requires loading the complete cohort into memory; one target's validation
  or execution failure MUST NOT stop other targets.
- **FR-011**: Each job MUST preserve the submitted alteration order, preflight the complete sequence
  against one coherent projected state, and apply all admitted effects through that workflow
  execution's single-writer boundary.
- **FR-012**: A job MUST commit all of its alteration effects in one atomic fenced checkpoint or commit
  none; on failure it MUST record the failing alteration and mark every later alteration skipped.
- **FR-013**: A target deleted or rendered ineligible after capture MUST produce a final safe job
  failure rather than changing the sealed cohort or silently disappearing.
- **FR-013a**: Each captured target MUST retain its workflow identity, tenant and execution-authority
  partition, exact five-field pinned artifact identity, workflow concurrency facts, root variable
  frame revision when relevant, and the source activity concurrency facts needed by submitted
  activity alterations. A job MUST compare every relevant captured fact before staging mutation.
- **FR-013b**: `CancelWorkflow` MUST evaluate the target's current terminal state rather than reject
  solely because lifecycle state changed after capture, preserving its deterministic terminal no-op.
- **FR-014**: Submitted alterations MUST use a stable envelope containing exact kind, schema version,
  and payload; stored contracts MUST NOT contain CLR implementation type identities or client-supplied
  executable code.
- **FR-015**: The system MUST provide built-in `CancelWorkflow`, `ScheduleActivity`,
  `RescheduleActivity`, `ModifyVariable`, and `Migrate` alteration kinds.
- **FR-016**: Trusted host modules MUST be able to register a handler for a unique supported kind and
  schema version; unknown, unsupported, or duplicate registrations MUST fail deterministically.
- **FR-017**: All handlers MUST accept only a staging-oriented alteration context for workflow
  mutations and MUST honor a trusted-host contract that preflight is replay-safe and externally
  side-effect-free. The runtime guarantees atomic staged state, but cannot sandbox arbitrary trusted
  module code; an external side effect is a host-extension defect.
- **FR-018**: `CancelWorkflow` MUST be the only alteration in its job.
- **FR-019**: Cancellation of an active workflow MUST use the established runtime cancellation
  semantics, including owned continuation cleanup; cancelling an already-terminal workflow MUST be a
  deterministic successful no-op that does not rewrite history.
- **FR-019a**: A cancellation job MUST report success only after the runtime cancellation checkpoint
  has durably committed or that commit has been reconciled; command acceptance alone is not success.
- **FR-020**: `ModifyVariable` MUST address only an existing workflow-scoped root-frame variable by
  stable variable reference key and MUST NOT address a value by display name.
- **FR-021**: `ModifyVariable` MUST validate the replacement against the declaration's pinned type,
  storage, and value-protection contract.
- **FR-022**: `ModifyVariable` MUST use the target's captured root-frame revision as its optimistic
  concurrency precondition; a missing reference, non-root scope, or stale revision MUST fail the
  complete job.
- **FR-023**: `ScheduleActivity` MUST require an exact authored node identity and an explicit active
  parent activity-execution identity. Compatibility requires the exact pinned artifact to declare
  the node through a direct structural relation carrying an exact operator-scheduling capability
  owned by that parent activity kind, the parent to satisfy that capability's active-state and scope
  rules, the capability to define how parent completion consumes the child, and no conflicting live
  execution for the same structural slot.
- **FR-024**: `ScheduleActivity` MUST derive the pinned artifact, scope, provenance, execution path,
  fresh deterministic activity execution identity, and authored input evaluation from runtime state;
  its payload MUST NOT accept those runtime-owned values or arbitrary input overrides.
- **FR-025**: `RescheduleActivity` MUST identify one existing source activity execution and MAY
  supersede it only when its state is scheduled, waiting, suspended, or faulted.
- **FR-026**: `RescheduleActivity` MUST reject running, completed, cancelled, and recovered source
  executions without mutation.
- **FR-027**: A successful reschedule MUST preserve the source as immutable history, remove or
  supersede its live bookmarks, timers, queued work, and continuation ownership atomically, and create
  a distinct fresh deterministic activity execution with explicit lineage to the source.
- **FR-027a**: Rescheduling MUST add a durable `Superseded` source status plus successor identity and
  supersession lineage. This explicit lifecycle transition preserves prior execution evidence while
  proving the source no longer owns live continuation.
- **FR-027b**: A rescheduled execution MUST retain the source's immutable pinned input values rather
  than re-evaluate authored input expressions; only a new `ScheduleActivity` evaluates current
  authored inputs.
- **FR-028**: Rescheduling MUST remain distinct from ordinary retry and bookmark resumption and MUST
  NOT reuse the historical activity execution identity.
- **FR-029**: Rescheduling a faulted source MUST require a non-terminal workflow and a source incident
  that is already non-blocking. It MUST NOT resolve any incident; incident recovery/redrive is a
  separate explicit operation.
- **FR-030**: Activity rescheduling MUST NOT implicitly recover or reopen a terminal workflow.
- **FR-031**: `Migrate` MUST appear at most once and, when present, MUST be the first alteration in its
  job.
- **FR-032**: Migration MUST accept only an exact target artifact for the same workflow definition and
  MUST require the execution to be operationally suspended and fully quiescent with no in-flight
  invocation, scheduler work, or uncommitted continuation. Operational suspension means either the
  workflow aggregate is `Suspended` or at least one retained activity is `Suspended`; because a parked
  Elsa execution can retain a `Running` aggregate, the full quiescence proof MUST additionally reject
  every running activity or active delivery/ownership lane.
- **FR-032a**: The migration request MUST identify the target by its complete immutable artifact
  identity: artifact ID, definition ID, definition version ID, artifact version, and artifact hash.
  A compatible retained artifact MAY be older or newer than the current artifact.
- **FR-033**: Migration preflight MUST prove identity compatibility for every retained executable
  node, activity contract, bookmark resume target, execution scope, variable declaration, and other
  durable artifact-bound reference.
- **FR-034**: Migration incompatibility or target-artifact unavailability MUST leave the execution
  pinned to its prior artifact with no partial projection changes.
- **FR-035**: Active-execution migration and caller-supplied node, bookmark, scope, or variable mapping
  MUST remain outside the initial contract.
- **FR-036**: Alterations after a valid migration MUST preflight against the post-migration artifact
  and staged state.
- **FR-037**: Duplicate or conflicting variable references, activity sources, target nodes, or scope
  mutations within a job MUST fail preflight.
- **FR-038**: Submission MUST require the existing workflow-runtime management permission and MUST
  evaluate tenant and execution-authority scope before accepting work.
- **FR-039**: An accepted plan MUST persist its sealed tenant and execution-authority scope plus
  submitting-operator provenance; background workers MUST execute only within that scope without
  depending on the submitter's later session validity.
- **FR-040**: A later permission revocation MUST NOT reinterpret an accepted plan; an operator
  currently authorized for the sealed scope MUST be able to cancel its remaining work.
- **FR-041**: Submission MUST require a tenant-scoped idempotency key; repeating the same key with the
  same canonical request MUST return the existing plan, while different content MUST return a
  conflict and create no work.
- **FR-041a**: Canonicalization MUST preserve alteration order, normalize JSON object field order and
  omitted defaults, deduplicate and deterministically order set-valued target identifiers and query
  values, include the sealed tenant/authority scope, and use the exact schema-versioned interpretation
  of scalar payload values.
- **FR-042**: Plan, job, and workflow-command identities MUST be deterministic and all background
  delivery MUST be safe under at-least-once processing.
- **FR-043**: Transient pre-commit failures MAY retry, validation failures MUST be final, and an
  uncertain commit acknowledgement MUST be reconciled from durable checkpoint evidence before any
  retry.
- **FR-044**: Plan cancellation MUST stop target capture and undispatched jobs but MUST allow a job
  already inside its workflow single-writer boundary to complete its atomic checkpoint.
- **FR-044a**: Cancelling during target capture MUST leave the plan unsealed. Already-persisted
  provisional jobs MUST remain non-claimable and be deleted in bounded, restartable pages while the
  plan is `Cancelling`; no provisional jobs may remain when the plan becomes terminal.
  Cancelling a sealed plan MUST mark each never-started job `Cancelled` and its alteration outcomes
  `Skipped` with a plan-cancelled code.
- **FR-045**: Plan reads MUST expose durable lifecycle state and target/job counts; job reads MUST
  expose per-alteration succeeded, failed, or skipped outcomes in submitted order.
- **FR-046**: The stable plan lifecycle MUST distinguish target capture, queued/executing work,
  complete success, completion with failures, terminal capture/orchestration failure, and
  cancellation.
- **FR-046a**: A terminal sealed plan MUST satisfy
  `targetCount = succeededJobCount + failedJobCount + cancelledJobCount` and have no pending or
  running jobs. An unsealed cancelled or failed capture MUST report captured-so-far separately and a
  target count of zero.
- **FR-047**: Durable audit and result records MUST contain typed outcome codes, policy-safe messages,
  timestamps, structural identifiers, and submitting-operator provenance.
- **FR-048**: Durable audit and result records MUST NOT copy variable values, arbitrary alteration
  payloads, stack traces, raw exceptions, or secrets; detailed exceptions MUST remain confined to
  protected server diagnostics.
- **FR-048a**: Deferred alteration execution payloads MUST be stored in a protected tenant-bound
  representation or protected durable reference, MUST be redacted from every plan/job read DTO, and
  MUST NOT be the stored source for idempotency comparison. Host retention policy MAY erase the
  protected payload after the plan becomes terminal.
- **FR-049**: The Runtime API MUST expose authenticated operations to submit a plan, read a plan, page
  its jobs/results, read an individual job, and cancel remaining plan work.
- **FR-050**: Successful submission MUST return an accepted durable plan identity and inspection
  links rather than wait for target capture or job completion.
- **FR-051**: A client library MAY provide submit-and-poll convenience using the same durable
  contracts, but it MUST NOT require or imply a second server execution path.
- **FR-052**: Plan, target, job, idempotency, and checkpoint-reconciliation records MUST be tenant
  partitioned and durable across host restarts.
- **FR-052a**: Alteration job/result and checkpoint-reconciliation evidence MUST commit in the same
  provider atomic unit as the workflow checkpoint, or through a provider protocol that proves the
  same no-false-success/no-duplicate invariant under acknowledgement loss.
- **FR-053**: Every supported durable runtime persistence provider MUST implement the alteration
  contracts and pass shared conformance, wire-shape, idempotency, paging, and restart tests.
- **FR-054**: Feature composition and API capability discovery MUST advertise the alteration surface
  only when its runtime orchestration, endpoints, and required persistence services are active.
- **FR-055**: Runtime and API project references MUST preserve the established Design/Runtime
  dependency boundary and artifact-only execution rule.
- **FR-056**: The clean addition MUST include focused domain, orchestration, handler, authorization,
  endpoint, persistence-provider, replay, and backend REST end-to-end tests.

### Key Entities

- **Alteration envelope**: Stable kind, exact schema version, and data payload resolved by a trusted
  host handler without persisting its implementation type.
- **Alteration plan**: Durable authorized submission containing an ordered alteration sequence,
  idempotency identity, sealed authority and operator provenance, target selector, lifecycle, and
  aggregate counts.
- **Captured target**: Deduplicated workflow execution identity and required optimistic concurrency
  facts durably collected before execution and sealed into plan membership.
- **Alteration job**: Deterministically identified per-target application and result record that
  preflights and commits the plan's ordered alterations atomically.
- **Alteration outcome**: Safe typed evidence that one submitted alteration succeeded, failed, or was
  skipped, without copying sensitive workflow values or executable implementation details.
- **Alteration handler**: Trusted host contribution that validates one exact envelope and stages
  bounded runtime changes through the atomic job context.
- **Migration compatibility result**: Structured proof or safe rejection explaining whether all
  retained artifact-bound runtime identities remain compatible with the exact target artifact.
- **Activity execution lineage**: Durable relationship from a fresh rescheduled execution to the
  immutable source it supersedes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Backend end-to-end tests submit and inspect all five built-in alteration kinds through
  the real authenticated REST, persistence, actor, checkpoint, and runtime paths.
- **SC-002**: Automated bulk tests capture more targets than one storage page, mutate query-relevant
  state during execution, and prove the sealed cohort contains every original match exactly once.
- **SC-003**: Crash/replay tests interrupt target capture, job dispatch, pre-commit execution, and
  post-commit acknowledgement and prove no target or committed alteration is duplicated.
- **SC-004**: Multi-alteration tests prove complete preflight and one atomic checkpoint per target:
  when alteration N fails, no earlier mutation persists and every later result is skipped.
- **SC-005**: Cancellation race tests prove undispatched work stops while an in-flight job completes
  coherently and final aggregate counts equal the number of sealed targets.
- **SC-006**: Migration tests cover every compatibility dimension and prove incompatible or active
  executions remain byte-equivalent to their pre-job state.
- **SC-007**: Schedule and reschedule tests prove runtime-owned deterministic identities, immutable
  source history, exact lineage, normal authored inputs, and complete cleanup of superseded live
  continuation resources.
- **SC-008**: Variable tests prove stable reference addressing, root-scope confinement, declared-value
  validation, captured-revision conflicts, and absence of before/after values from results and logs.
- **SC-009**: Extension tests register and execute a namespaced schema-versioned handler without
  framework enum changes or persisted CLR type names, and reject unknown or duplicate contracts.
- **SC-010**: Authorization tests prove submission, inspection, cancellation, tenant isolation, sealed
  execution authority, and post-submission permission-revocation behavior.
- **SC-011**: Every supported durable provider passes the shared alteration store suite, golden wire
  fixtures, keyset paging, idempotency conflict, restart, and reconciliation scenarios.
- **SC-012**: Existing runtime cancellation, retry/resume, incident resolution, scheduler, publishing,
  persistence, and full-solution test suites remain green.

## Assumptions

- Elsa Foundation is unreleased, so this additive API and persistence surface requires no legacy
  alteration-plan migration or dual-read path.
- Elsa 3 parity names the requested alteration families and durable plan/job intent; Foundation
  intentionally improves Elsa 3's live query drift, partial per-job commits, ambiguous outcomes, and
  mutable execution-graph behavior.
- The existing workflow execution query vocabulary, runtime management permission, actor mailbox,
  checkpoint/outbox boundary, artifact store, value contracts, and incident recovery surfaces are
  reused rather than duplicated.
- Result retention and deployment storage quotas remain host policy; the API does not promise
  infinite physical resources or silently truncate accepted target membership.
- Studio UX, active migration, caller-authored migration mapping, arbitrary schedule-time input
  overrides, and implicit terminal workflow recovery are outside this initial work unit.
- The current constitutions are draft/provisional; this work applies their relevant quality gates
  without ratifying or amending them.
