# 115 — Runtime handled child fault (BPMN Phase 2, seam B)

## Goal

Let a structural parent **absorb** a child's fault: during a child-fault evaluation the parent's
continuation resolves the child's blocking incident and reclaims the faulted child's leftover
subtree, so the composite consumes the fault and keeps routing (e.g. emits a BPMN error token to an
error boundary) instead of choosing between "fault the whole composite" and "leave a blocking
incident that faults the workflow at the next drain". This is core seam B of the BPMN events tier;
error boundary events and event subprocesses are its first consumers, and Flowchart-style
fault-join composites benefit equally.

## Context (what exists today)

- Child faults propagate to parents that implement `IRuntimeActivityChildFaultHandler` via
  `ChildFaultParentEvaluation` rider work items; `ActivityChildFaultedContext` already carries the
  `IncidentId`. A parent that does not opt in halts the branch (attempt ended with `Suspend`).
- A fault-handling parent has exactly two useful continuations: `Faulted` (composite re-faults,
  workflow ends `Faulted`) or `Defer` (branch parked; the incident stays `Blocking`, and
  `BlockingIncidentWorkflowFaultObserver` faults the whole workflow after the drain). There is no
  absorb path: nothing in the runtime ever transitions an incident out of `Blocking` except spec
  112's subtree-cancellation suppression.
- The incident model already has everything needed: `IncidentStatus.Resolved`, `ResolvedAt`,
  `IncidentResolutionAction.Continue`.
- Spec 112 shipped `ActivitySubtreeCancellationPlanner` (subtree traversal, terminalization,
  `IActivityScopeCleanupStore` capture, incident suppression) and the single-commit fold
  (`SubtreeCancellationCommitChanges`) in the parent-evaluation handler.
- A composite child that faults leaves its own live descendants orphaned (suspended waits,
  bookmarks, timers) — the pre-existing leak; if the fault is absorbed and the run continues, that
  leak must not survive.

## In scope (this slice)

- **Staging API**: `RequestChildFaultAbsorption(incidentId, reason, metadata?)` on
  `IRuntimeActivityExecutionContext` + `SimpleActivityExecutionContext`, staging a
  `RuntimeChildFaultAbsorptionRequest`, with a reader — the spec-112 staging pattern.
- **Application in the child-fault evaluation** (`WorkflowParentActivityCompletionSchedulerWorkHandler`),
  folded into the same checkpoint commit as the parent's continuation:
  - the absorbed incident is upserted to `Resolved` with `ResolvedAt` = commit time,
    `ResolutionAction = Continue`, and absorption provenance (reason, absorbing parent, work item)
    in metadata;
  - the faulted child's subtree is reclaimed via the spec-112 planner rooted at the faulted child:
    live descendants terminalize to `Cancelled`/`"FaultAbsorbed"`, their bookmarks/durable
    timers/queued scheduler work are captured for atomic cleanup, and their other non-terminal
    incidents are suppressed. The faulted child's own state stays `Faulted` (terminal history is
    not rewritten).
- **Validation** (deterministic evaluation fault, spec-112 FR-2 style) and **timing tolerance**.
- **Harness tests**; module extension-point docs.

## Out of scope (deferred)

- BPMN consumption (error boundary events, event subprocesses — later Phase 2 slices).
- Retry-from-incident / operator resolution surfaces (`IncidentResolutionAction.Retry` etc.).
- Rewriting the faulted child's `ActivityExecutionState` (no `Recovered` transitions here).
- Absorbing incidents of non-child executions or bulk absorption.

## Functional requirements

**FR-1 — Staging.** During a child-fault evaluation the parent may stage exactly one child-fault
absorption naming the evaluation's incident id, a non-empty reason, and optional metadata. Staging
performs no I/O.

**FR-2 — Structural validation (deterministic fault).** The evaluation faults when an absorption
request:
- is staged outside a child-fault evaluation (initial structural execution — rejected by the invoke
  handler like spec 112 — or a child-completion evaluation);
- names an incident id different from the evaluation's incident id, or the evaluation carries no
  incident id;
- is staged more than once in one evaluation;
- accompanies a `Fault` or `Cancel` continuation (absorb-and-refault is contradictory; absorption
  is honored with `Defer` and `Complete`);
- names an incident that does not exist in the incident store.

**FR-3 — Timing tolerance (benign no-op).** If the named incident is already terminal
(`Resolved`/`Suppressed`), the request is skipped without error (at-least-once redelivery and
overlapping cleanups are legal). The subtree reclamation still applies to any remaining live
descendants.

**FR-4 — Incident resolution.** The absorbed incident's state change (`Resolved`, `ResolvedAt`,
`ResolutionAction = Continue`, provenance metadata) rides the same commit as the parent's
continuation. After the commit, `ListBlockingAsync` no longer reports it; a workflow whose only
blocking incident was absorbed completes normally.

**FR-5 — Subtree reclamation.** The spec-112 planner runs rooted at the faulted child: cancellable
descendants → `Cancelled`/`"FaultAbsorbed"` with reason + absorbing-parent metadata; one
`ActivityScopeCleanupRequest` (scope id = faulted child's execution id) captures the subtree's
bookmarks, durable timers, and queued scheduler work; other non-terminal incidents in the subtree
are suppressed. The absorbed incident itself is excluded from suppression (it resolves per FR-4).
All changes fold into the continuation's commit — no separate commit, no new scheduler hop.

**FR-6 — Composition with spec 112.** Absorption and child-subtree cancellation requests may be
staged in the same evaluation (a boundary handler may absorb the host's fault and cancel sibling
listeners); each keeps its own validation rules and all effects share the one commit.

**FR-7 — Observability.** Cancelled descendants receive inspection projections (spec-112 parity);
the checkpoint lists the faulted child's reclaimed execution ids; commit metadata names the
absorbing parent, the incident, and the reason.

## Invariants that MUST survive

- Spec 112 invariants (single-commit atomicity, cleanup inside the checkpoint store's atomic
  boundary, `Cancelled` terminality, determinism ordering, terminal-vs-staged-schedule validation).
- The faulted child's `ActivityExecutionState` remains `Faulted`; absorption changes incident and
  descendant state only.
- A parent that stages nothing behaves exactly as before (fault stays blocking on `Defer`;
  composite re-fault path untouched).
- Fault-wins drain race unchanged: absorption only helps when the incident's recording and the
  absorbing evaluation land in the same drain; once `BlockingIncidentWorkflowFaultObserver` has
  faulted the workflow, the parent is no longer `Running` and the evaluation is dropped.
- `ChildFaultParentEvaluation` rider construction and the non-opt-in parent bypass are untouched.

## Success criteria

- Runtime harness tests (no BPMN dependency) cover: leaf-child fault absorbed with `Defer` then
  completion via a sibling (incident `Resolved`, workflow `Completed`); composite-child fault
  absorbed with `Complete` where the faulted composite had a waiting descendant (descendant
  `Cancelled`/`"FaultAbsorbed"`, bookmark deleted, descendant's own incident suppressed, absorbed
  incident `Resolved`, workflow `Completed`); each FR-2 rejection (wrong incident id, absorption in
  a completion evaluation, duplicate staging, `Fault` continuation, initial-execution staging).
- No behavior change for existing fault paths: full runtime/Flowchart/BPMN/Graph/ControlFlow suites
  green; architecture guards green; full solution build clean.
