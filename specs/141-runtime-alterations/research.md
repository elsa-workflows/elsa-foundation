# Research: Durable Runtime Alterations

## Decision 1: Foundation improves Elsa 3 plan semantics

**Decision**: Keep Elsa 3's typed alteration families, durable plan/job intent, ordered alteration
lists, and per-target results, but replace live target evaluation and partial per-job mutation with
a sealed cohort and atomic preflight/apply per target.

**Rationale**: Elsa 3 enumerates query targets into jobs and executes handlers in order, but earlier
mutations can survive a later `Fail`. Its completion calculation can also classify a mixed
success/error log as successful. Foundation's checkpoint boundary can provide a stronger and
unambiguous contract.

**Rejected**:

- Cancel-only delivery: explicitly rejected in favor of the full initial family.
- Elsa 3 partial application: violates the locked atomic job result.
- One transaction across every target: destroys independent progress and is not operationally
  bounded.

## Decision 2: Capture uses an immutable scan key and seals before execution

**Decision**: Add an alteration-specific target scan ordered by immutable
`(tenantPartition, workflowExecutionId)` rather than reuse run-history's mutable
`(effectiveTimestamp desc, workflowExecutionId)` cursor. Each workflow is visited once, filter
membership is evaluated during the durable capture epoch, matched identities and concurrency facts
are persisted, and no job executes before sealing.

**Rationale**: Existing `WorkflowExecutionStatePageQuery` has the desired filters but its ordering
timestamp changes as workflows execute. Reusing that cursor could skip or duplicate candidates
during a long capture. An immutable scan key makes paging/restart stable. Sealing prevents the
alterations themselves from changing later pages.

**Semantics**: This is a cohort constructed during `CapturingTargets`, not an MVCC claim that every
mutable predicate was evaluated at one physical instant. Once a target is captured, later state
changes cannot remove it; relevant state changes instead cause job preflight conflicts. Natural
workflow changes before a candidate is visited affect whether that candidate matches.

**Rejected**:

- Fixed target cap or silent truncation: paging/backpressure is sufficient and preserves API intent.
- Executing each page immediately: mutations can change remaining query membership.
- A long database snapshot transaction: cannot survive worker restarts and is not provider-neutral.
- Existing history cursor: its primary sort key is mutable.

## Decision 3: Captured target records become jobs only after seal

**Decision**: Store a plan plus one deduplicated `AlterationTargetState` per matched or explicitly
requested execution. During capture it is not claimable. Sealing makes every valid target a pending
job without an unbounded one-transaction job-materialization step.

**Rationale**: Persisting all targets in the plan document is unbounded. Creating every job only at
seal is another unbounded batch. One target document supports paged capture, deterministic identity,
claims, outcomes, result paging, and checkpoint integration.

**Rejected**:

- One plan document containing all IDs/results.
- Separate target and job documents with a bulk copy at seal.
- Queue messages as job truth; durable state, not delivery, owns truth.

## Decision 4: Terminal job evidence joins the workflow checkpoint

**Decision**: Extend `RuntimeCheckpointStateChangeSet` with alteration job changes. Success, validation
failure, and ordered outcomes commit in the same fenced provider unit as workflow mutations. A
no-mutation validation failure still writes a mandatory alteration checkpoint.

**Rationale**: Writing job success after the checkpoint creates an uncertain acknowledgement window;
writing it first creates false success. Deterministic commit/job IDs then make replays
reconcilable.

**Rejected**:

- Separate job store update before or after checkpoint.
- Treating actor enqueue/acceptance as completion.
- Blindly re-running after an acknowledgement loss.

## Decision 5: Plans reconcile aggregates from authoritative jobs

**Decision**: The plan document owns capture state and terminal summary, while terminal job records
own per-target truth. A reconciler updates progress/final counts idempotently and marks a plan
terminal only when every sealed target is terminal.

**Rationale**: Atomically incrementing one shared plan counter from concurrent workflow checkpoints
creates a hot CAS conflict. Reconciliation avoids contention; the terminal invariant is still exact.

**Rejected**:

- Shared atomic counter mutation in every job checkpoint.
- API-only counts from an unbounded in-memory scan.
- Approximate terminal totals.

## Decision 6: Handler registration is stable and trusted

**Decision**: Runtime Core exposes exact `(kind, schemaVersion)` descriptors, a scoped handler
contract, and `AddWorkflowAlterationHandler<T>`. Startup builds an immutable registry and rejects
duplicates, unknown built-in overrides, invalid versions, and non-namespaced custom kinds. Persisted
envelopes never contain service types.

Handlers receive one coherent read/staging context. The runtime can constrain committed mutations,
but trusted module code is contractually replay-safe and externally side-effect-free rather than
sandboxed.

**Rationale**: This preserves host extensibility without a closed enum or durable CLR identity and
honestly reflects what in-process trusted code can enforce.

**Rejected**:

- Framework enum for all kinds.
- Persisted polymorphic CLR objects.
- Client-supplied code.
- Claiming arbitrary trusted code can be mechanically prevented from performing I/O.

## Decision 7: Protected payloads are separate from read models

**Decision**: Submission canonicalizes and hashes the authorized request, then protects the execution
envelope with a tenant-bound `IWorkflowAlterationPayloadProtector`. The plan stores the hash and
protected payload separately. Read models never decrypt it. Durable composition requires a
restart-stable key; in-memory development may use a process-local key.

**Rationale**: `ModifyVariable` necessarily carries sensitive replacement data across an async
boundary. Result redaction alone does not protect the deferred plan at rest.

**Rejected**:

- Plain JSON envelope persistence.
- Reusing audit/result documents as execution input.
- A Runtime dependency on the Secrets domain.

## Decision 8: Cancel and schedule logic becomes reusable staging

**Decision**: Extract state-change planners from existing cancellation and scheduling handlers so the
ordinary command paths and alteration jobs share the same semantics while the alteration owns one
checkpoint containing its job outcome.

**Rationale**: Nesting an existing command would produce a second checkpoint and could not atomically
join job success. Copying handler logic would drift.

**Rejected**:

- Dispatching `Cancel` or `ScheduleActivity` and marking the job successful on acceptance.
- Duplicating cancellation/scheduling state transitions.

## Decision 9: Reschedule is a new supersession transition

**Decision**: Add `ActivityExecutionStatus.Superseded` plus successor ID, time, and lineage. The
replacement uses a new deterministic execution/scope identity, clones pinned input values, and
atomically reclaims source-owned bookmarks, timers, scheduler work, descendants, and continuation.
It accepts Scheduled, Waiting, Suspended, or Faulted sources, but not Running or terminal sources.
A faulted source incident must already be non-blocking.

**Rationale**: Retry and resume preserve logical execution identity or have narrower fault-boundary
rules. General rescheduling needs a distinct durable meaning.

**Rejected**:

- Resetting the existing activity execution to Running.
- Reusing its ID.
- Re-evaluating authored inputs.
- Implicit incident resolution or workflow recovery.

## Decision 10: Schedule requires an activity-owned compiled capability

**Decision**: The exact pinned executable must show the requested node in a direct child slot of the
selected parent node and that relation must pin an exact operator-scheduling capability. The parent
activity module owns the capability's allowed parent states, scope/path/branch derivation, duplicate
rule, any private-state staging, and child-completion consumption. The slot must have no conflicting
live child. Runtime derives every identity/provenance value and evaluates normal authored inputs.

**Rationale**: Topology alone cannot prove a Sequence, Flowchart, loop, or third-party parent can
incorporate an operator-injected child into its control state. Merely finding both IDs can create
orphan work, duplicate branches, or completion a parent cannot consume.

**Rejected**:

- Any existing ancestor as parent.
- Treating every direct child slot as operator-schedulable.
- A generic Runtime rule that guesses module-owned parent state.
- Client-supplied scope/provenance/execution identity.
- Arbitrary input overrides.

## Decision 11: Migration uses explicit compatibility proof

**Decision**: Admit only suspended, fully quiescent workflows and an exact retained artifact from the
same definition. Validate all retained node descriptors/contracts, bookmark resume targets, scopes,
variable declarations, artifact-bound inspection/provenance, requirements, and dependency/reference
leases. Migration updates every affected projection in one checkpoint. Later alterations see the
staged target artifact.

**Rationale**: `WorkflowExecutableIdentityComparer` proves equality, not compatibility. Foundation
state is split across several artifact-bound document families.

**Rejected**:

- Replacing only `WorkflowExecutionState.PinnedExecutable`.
- Active migration.
- Caller-authored mappings.
- Restricting to upgrades; exact compatible downgrades are not inherently different.

## Decision 12: One durable REST path

**Decision**: Use:

- `POST /runtime/workflows/alteration-plans`
- `GET /runtime/workflows/alteration-plans/{planId}`
- `GET /runtime/workflows/alteration-plans/{planId}/jobs`
- `GET /runtime/workflows/alteration-plans/{planId}/jobs/{jobId}`
- `POST /runtime/workflows/alteration-plans/{planId}/cancel`

Submission requires `Idempotency-Key` and returns `202` with `Location`. A client may submit and poll;
there is no immediate server `run` route.

**Rationale**: One execution model keeps retry, authorization, result, and audit semantics coherent.

**Rejected**:

- Synchronous run endpoint.
- Separate single-instance mutation endpoints.
- Waiting on target capture in the submission request.

## Provider inventory

Runtime has two implementation layers relevant to this work:

1. `InMemory*` stores in `Elsa.Workflows.Runtime` for development and focused tests.
2. Unified Groundwork stores and checkpoint writer in `Elsa.Persistence.Groundwork`.

The SQLite, PostgreSQL, SQL Server, and MongoDB shell features select a Groundwork database adapter;
they do not require four distinct alteration store implementations. Conformance and golden wire
tests target the shared Groundwork layer, with existing adapter admission/smoke lanes guarding the
four compositions.
