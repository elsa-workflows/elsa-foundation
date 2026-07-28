# 112 — Runtime child-subtree cancellation (BPMN Phase 2, seam A)

## Goal

Give structural parents a first-class, staged way to cancel one scheduled child's entire
activity-execution subtree — descendant executions, bookmarks, durable timers, queued scheduler
work, and open incidents — applied atomically at checkpoint commit, in the same commit as the
parent's structural continuation. This is the linchpin seam for the BPMN events tier: event-based
gateways (first catch wins, cancel the other candidates), boundary events (host completes → cancel
the listener; interrupting listener fires → cancel the host), event subprocesses, and transactions.
The seam is generic runtime infrastructure: Flowchart-style composites benefit equally.

## Context (what exists today)

- `IRuntimeActivityExecutionContext.ScheduleChildActivity` stages child schedules on
  `SimpleActivityExecutionContext`; the scheduler work handlers read them after the structural
  callback and turn them into post-commit work items. Cancellation has no such channel.
- A complete but **unwired** scope-cancellation path exists:
  `CancelActivityScopeCommand` → `WorkflowCancelActivityScopeSchedulerWorkHandler` (BFS subtree
  traversal over `ParentActivityExecutionId`, terminalization to `Cancelled`/`ScopeCancelled`) +
  `IActivityScopeCleanupStore` (captures bookmarks, durable timers, and queued scheduler work for
  the scope; applied by the checkpoint store inside the same atomic boundary). No production code
  enqueues the command, and its constructor pins `activityExecutionId == executionScopeId`
  (activity-boundary scopes only).
- Flowchart (`CancelPendingPathsForBreak`, race scopes) and `BpmnProcess` (terminate, canceled
  tokens) cancel children **logically only**, in composite private state. The descendant
  executions, bookmarks and timers leak until the workflow ends; late completions are absorbed by
  the `parentState.Status != Running` guard and by engine-level canceled-path/token bookkeeping.
- No code path ever transitions an `IncidentState` out of `Blocking`. `Resolved`/`Suppressed` and
  `ResolvedAt` exist in the model but are never written. After every drain,
  `BlockingIncidentWorkflowFaultObserver` faults the whole workflow if any blocking incident
  remains.

## In scope (this slice)

- **Staging API**: `RequestChildSubtreeCancellation(childActivityExecutionId, reason, metadata?)`
  on `IRuntimeActivityExecutionContext` + `SimpleActivityExecutionContext`, staging a
  `RuntimeChildSubtreeCancellationRequest`, with a `GetChildSubtreeCancellationRequests()` reader —
  mirroring the `ScheduleChildActivity` pattern.
- **Application at checkpoint commit**: `WorkflowParentActivityCompletionSchedulerWorkHandler`
  reads staged requests after `OnChildCompletedAsync`/`OnChildFaultedAsync` and folds, per request,
  the subtree terminalization (`Status = Cancelled`), the captured
  `ActivityScopeCleanupRequest`, incident suppressions, and inspection projections into the **same**
  `RuntimeCheckpointCommit` that persists the parent's continuation (deferred and completed paths).
  No separate commit, no new scheduler hop.
- **Shared planner**: extract the traversal/terminalization/cleanup-capture logic from
  `WorkflowCancelActivityScopeSchedulerWorkHandler` into one shared service used by both the
  existing handler and the new staged path (single mutation home, DRY).
- **Incident suppression**: non-terminal incidents attached to executions in a cancelled subtree
  are transitioned to `Suppressed` with `ResolvedAt` in the same commit, so a cancelled subtree can
  never fault the workflow afterwards. The existing scope-cancellation handler gains the same
  suppression (today it leaves incidents dangling — a cancelled scope could still fault the run at
  the next drain).
- **Generalized late-completion tolerance** (Flowchart Break/#304, generalized): a
  parent-completion evaluation whose completed/faulted child execution is already terminal
  (`Cancelled`) is acked as a no-op before the structural callback runs, alongside the existing
  `parentState.Status != Running` and `WasParentCompletionProcessed` guards.
- **Harness tests** covering the functional requirements below.

## Out of scope (deferred)

- BPMN consumption of the seam (event-based gateway, boundary events — later Phase 2 slices).
- Seam B: handled child fault / incident **absorption** by a parent continuation (next spec; this
  slice only suppresses incidents inside a subtree being cancelled).
- Compensation-aware cancellation ordering (Phase 3).
- A public management/API surface for scope cancellation (the command handler stays internal).
- Retiring the Flowchart/BPMN logical-cancellation bookkeeping in favor of this seam (candidates
  for a later cleanup slice; their guards remain load-bearing for engine-level token semantics).

## Functional requirements

**FR-1 — Staging.** A structural parent may stage any number of child-subtree cancellation
requests during a child-completion or child-fault evaluation. Each request names one target child
activity-execution id, a non-empty reason, and optional metadata. Requests are inert until the
handler applies them; staging performs no I/O.

**FR-2 — Structural validation (deterministic fault).** At application time the handler faults the
evaluation (existing parent-fault path) when a request:
- targets an activity execution that does not exist in this workflow execution;
- targets an execution whose `ParentActivityExecutionId` is not the evaluating parent's execution
  id (only direct children may be targeted; their subtrees follow);
- duplicates another staged request's target;
- is staged during an initial structural execution (`ExecuteStructureAsync`) — no child can exist
  yet, and `WorkflowInvokeActivitySchedulerWorkHandler` rejects staged cancellations the same way
  it rejects terminal-plus-schedules today;
- accompanies a `Fault` or `Cancel` continuation. Cancellation requests are honored only with
  `Defer` and `Complete` continuations. (A faulting composite's own cleanup is a separate concern;
  a `Cancel` continuation already escalates to workflow cancellation.)

**FR-3 — Timing tolerance (benign no-op).** A request whose target execution is already terminal
(`Completed`, `Faulted`, `Cancelled`, `Recovered`) is skipped without error. Rationale: in a
first-completion-wins race the loser may complete before the winner's callback cancels it; that
race is legal and must not fault the composite. The skip leaves no state change.

**FR-4 — Subtree terminalization.** For each honored request, the subtree rooted at the target
(BFS over `ParentActivityExecutionId`, same traversal as the scope-cancellation handler) is
computed from a single activity-state snapshot. Every cancellable member (`Scheduled`, `Running`,
`Waiting`, `Suspended`) is terminalized to `Status = Cancelled`, `SubStatus = "ParentCancelled"`,
with the reason and requesting parent's execution id recorded in metadata. Ordering is
deterministic: `ExecutionSequence`, then activity-execution id, ordinal.

**FR-5 — Resource cleanup.** For each honored request the handler captures one
`ActivityScopeCleanupRequest` via `IActivityScopeCleanupStore.CaptureAsync`, with
`ExecutionScopeId` = the target child's activity-execution id (satisfying the durable writer's
scope-id ∈ activity-execution-ids validation), covering the subtree's bookmarks, durable timers,
and queued scheduler work items. All cleanups ride the same commit's `ActivityScopeCleanups` slot
and are applied inside the store's atomic boundary.

**FR-6 — Incident suppression.** Incidents whose `ActivityExecutionId` belongs to a cancelled
subtree and whose status is non-terminal (`Open`, `Blocking`) are upserted in the same commit as
`Status = Suppressed`, `ResolvedAt` = commit time, with suppression provenance in metadata. The
same suppression is added to `WorkflowCancelActivityScopeSchedulerWorkHandler`.

**FR-7 — Single-commit atomicity.** Terminalizations, cleanups, suppressions, and inspection
projections for all honored requests fold into the parent evaluation's existing commit (the
child-scheduling deferral commit or the parent-completion commit). The cancelled execution ids are
appended to the checkpoint's `ActivityExecutionIds`. Commit count per evaluation is unchanged.

**FR-8 — Late-completion tolerance.** A `ParentCompletionEvaluation` work item whose
completed-child execution is `Cancelled` returns without invoking the structural callback. Queued
subtree work deleted by FR-5 never surfaces; anything already in flight (e.g. an evaluation
enqueued before the cancellation committed) is absorbed by this guard.

**FR-9 — Observability.** Cancelled executions receive inspection projections in the same commit
(scope-cancellation parity). The commit metadata names the requesting parent, the reason, and the
work item that applied the cancellation.

## Invariants that MUST survive

- A terminal structural continuation still cannot co-exist with staged **child schedules**; the new
  rule set (FR-2) extends, and must not weaken, that validation.
- Cleanup application stays inside the checkpoint store's atomic boundary; no cleanup happens
  outside a committed checkpoint (crash between callback and commit = no partial cancellation).
- `Cancelled` is terminal: no code path resurrects a cancelled execution, and cancelled BPMN/
  Flowchart engine tokens are never pruned from composite private state (late-completion absorption
  depends on both).
- Determinism discipline: identical evaluation inputs produce identical commits (ordering rules in
  FR-4; no wall-clock-derived record ids).
- Fault-wins race semantics are unchanged: if a blocking incident's drain completes before the
  cancelling evaluation runs, `BlockingIncidentWorkflowFaultObserver` faults the workflow and the
  cancellation never applies (parent no longer `Running`). Suppression only helps incidents whose
  recording and cancellation land in the same drain — this is deliberate; the observer's semantics
  are untouched.
- `ChildFaultParentEvaluation` propagation to parents that opt into fault handling is untouched;
  a fault inside a subtree races cancellation and either resolution is legal.

## Success criteria

- Runtime harness tests (no BPMN dependency) cover: waiting-child subtree cancelled on sibling
  completion → bookmarks/timers/queued work deleted, states `Cancelled`/`ParentCancelled`, single
  commit; nested subtree (grandchildren) fully traversed; terminal-target no-op race; each FR-2
  validation rejection; incident inside cancelled subtree suppressed and workflow completes instead
  of faulting; late completion of a cancelled child acked as no-op; deferred and completing
  continuations both honor staged requests.
- Scope-cancellation handler behavior preserved by its existing tests, now backed by the shared
  planner, plus a new test for its incident suppression.
- No behavior change for structural activities that stage no cancellation requests (full existing
  runtime/Flowchart/BPMN test suites green).
- Architecture guards green; no new runtime-core dependencies.
