# Contract: in-process routing pass and acknowledgement in the dispatch's commit (FR-004–FR-010)

Audience: the scheduler drainer (`WorkflowSchedulerDrainer`), the routing work handlers, the pipeline dispatcher and
the operator setting. All types named here are internal to `Elsa.Workflows.Runtime` except `RuntimeHopCollapseOptions`,
the feature setting and the diagnostics counters.

## Routing pass (FR-004, FR-005, FR-006)

- P1. **Engagement.** The drainer opens a pass for a claimed dispatch only when: `RuntimeHopCollapseOptions.Enabled`;
  a live-drain delivery scope applies to the execution; no coalescing session applies; the queue supports claim
  transitions; and the composition has durable `Scheduler` evidence. Otherwise no pass exists and every handler
  enqueues as today.
- P2. **Capture.** `RuntimeRoutingPass.RouteAsync(next)` captures `next` only while the pass is open for
  `next.WorkflowExecutionId`, before its boundary, and when `HopRoutingClassifier` classifies `next` routable
  ([research R7](../research.md#r7--routable-in-process-fr-005-reconciled-with-spec-228)). Otherwise it calls
  `IWorkflowSchedulerWorkQueue.EnqueueAsync(next)` and, if a pass was open, records a fallback.
- P3. **Inline dispatch.** After the claimed item's handler returns, the drainer dispatches captured items in capture
  order through `FindHandler` and the pipeline dispatcher, with the dispatch's ambient services, inside the same claim
  renewal and the same spec-105 consume frame. Before each item it evaluates the pause gate; after each it re-reads the
  terminal status.
- P4. **Boundary.** The pass closes when a commit in the dispatch consumed the claimed item, or when no captured item
  remains. A capture request after the boundary enqueues.
- P5. **Fallback.** On a closed pause gate, a terminal status, no accepting handler, or an exception from a routed step
  (other than `OperationCanceledException`, `RuntimeSchedulerWorkClaimLostException` and
  `RuntimeSchedulerWorkConsumeConflictException`, which propagate unchanged), the drainer enqueues that routed item
  (create-only by identity), records a fallback, closes the pass, and completes the claimed item's dispatch normally.
- P6. **Acknowledgement.** The claimed item is acknowledged exactly as today after the pass: skipped when a commit
  consumed it durably, otherwise `CompleteClaimAsync` (claim-capable) or the legacy dequeue.
- P7. **Crash.** Nothing the pass captured is durable. Until its boundary, the claimed item stays in the queue under a
  renewed claim; after a crash the sweep redelivers it and the pass is re-derived (FR-006). Routed item identities are
  derived from the claimed item's identities, so re-derivation yields the same identities.
- P8. **Results.** Inline routed steps are part of the claimed item's `RuntimeSchedulerWorkItemResult`; they add no
  result entries and no admission-load charge (as the spec-228 pump).

## Acknowledgement in the dispatch's commit (FR-008)

- A1. `RuntimeExecutionPipelineDispatcher` stages its own `IRuntimeConsumedSchedulerWorkClaimAccessor` on
  `RuntimePipelineWorkspace.ConsumedWorkClaim`.
- A2. When the switch is on, `RuntimeSchedulerWorkHandlerBase<TPayload>` and `WorkflowStartActivitySchedulerWorkHandler`
  link the accessor resolved from the provider they run the body on to the staged one, when it is a different
  `RuntimeConsumedSchedulerWorkClaimAccessor` instance, for the duration of the body.
- A3. A linked accessor delegates `PendingConsume`, `WasConsumedDurably`, `MarkConsumedDurably`,
  `BeginConsumeAttempt` and `IsConsumeInFlightOrDurable` to the drain's, so the drainer's skip-ack decision and its
  renewal loop (#1254) see a consume made by any committer in the dispatch.
- A4. While the switch is on, the committer does not fold the consume into an `ActivityAttemptClaimed` checkpoint; it
  folds into the dispatch's next commit. Off, folding is today's (first commit of the dispatch, same scope only).
- A5. Non-atomic and legacy providers, dispatches that made no commit, and the handler-fault arm keep spec 105 FR-006's
  separate acknowledgement and tripwire. A replaced accessor implementation is never linked.

## Switch (FR-009)

- S1. `RuntimeHopCollapseOptions { Enabled = true }` is the default. `WorkflowsRuntimeCheckpointPersistenceFeature`
  exposes it as the operator setting `CollapseHopChain` (default `true`).
- S2. Off: no `SchedulerWorkInCommit` entry is ever marked, no pass is ever opened, no accessor is ever linked, and
  `ResolveConsumedWorkItems` is today's. Durable state, EF command counts and commit counts equal today's for the
  three reference workflows (SC-004).

## Diagnostics (FR-010)

- D1. `RuntimeSchedulerDispatchDiagnostics.RecordInProcessRoutingStep()` once per inline routed dispatch.
- D2. `RuntimeSchedulerDispatchDiagnostics.RecordInProcessRoutingFallback()` once per routed candidate an open pass
  enqueued instead.
- D3. `RecordDispatch()` stays once per drainer `DispatchAsync`; inline routed steps do not add to it.

## Contract tests

| Case | File |
|---|---|
| P1 gate matrix: each precondition false → no pass, handlers enqueue | `tests/essentials/Workflows/Runtime/Tests/RuntimeRoutingPassTests.cs` |
| P2–P4 capture, inline dispatch, boundary close, capture-after-boundary enqueues | same |
| P5 each fallback trigger enqueues the routed item before the claimed item is acknowledged | same |
| P6/P7 claimed item stays claimed until the boundary; crash recovery | `RuntimeRoutingPassTests.cs`, `EfHopCollapseCrashConvergenceTests.cs` (K3–K6) |
| A1–A5 linking, attempt-claim exclusion, non-atomic and fault paths unchanged | `tests/essentials/Workflows/Runtime/Tests/RuntimeDrainStepSingleTransactionTests.cs`, `RuntimeSchedulerWorkHandlerBaseTests.cs` |
| S1 feature setting registration (conditional wiring, §2.23.1) | `tests/essentials/Workflows/Runtime/Tests/WorkflowsRuntimeCheckpointPersistenceFeatureTests.cs` |
| S2 off reproduces the baseline counts exactly | `tests/essentials/Activities/Http/IntegrationTests/CommandBudgets/EfCommandBudgetTests.cs` |
| D1–D3 counters | `RuntimeRoutingPassTests.cs` |
