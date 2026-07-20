# Tasks — spec 113

## Phase 0 — Characterize (DONE)

- [x] T001 Trace the second `elsa.runtime.drain` trace to its source. Result: `RuntimeResumptionPumpTask` 10s
      sweep re-driving a terminal execution, not a passivation/idle timer. → [research.md](./research.md)
- [x] T002 Identify the seed: a residual scheduler work item stranded by the terminal-status drain guard (#293),
      surfaced by `ListPendingWorkflowExecutionIdsAsync` (no terminal filter). → research.md
- [x] T003 Confirm cost: unbounded durable-row growth + drain-span/lease churn per completed-with-residual
      execution; not a harmless idle timer. → [spec.md](./spec.md) "Cost".

## Phase 1 — Fix (DONE)

- [x] T010 `RuntimeResumptionService`: inject `IWorkflowExecutionStateStore`; skip + purge terminal executions in
      `SweepAsync`; add `IsTerminalAsync` + bounded `PurgeResidualSchedulerWorkAsync`.
- [x] T011 `RuntimeResumptionSweepResult`: add `TerminalExecutionsPurged` + `PurgedWorkItemCount`; fold into `DidWork`.
- [x] T012 `CoalescingWorkflowSchedulerWorkQueue`: delegate `DeleteAsync` to the inner durable queue (fills the
      throwing-default gap so purge works over the coalescing host).
- [x] T013 `RuntimeResumptionPumpTask`: surface purge counts in the debug log.
- [x] T014 Update direct constructions of `RuntimeResumptionService` (runtime, publishing-api, Groundwork tests).

## Phase 2 — Tests (DONE)

- [x] T020 Unit: terminal execution with residual work is purged, never activated; result counts asserted.
- [x] T021 Unit: non-terminal execution with backlog is still re-driven (regression guard).
- [x] T022 Durable+coalescing: `GroundworkCoalescingCrashConvergenceTests` — second sweep purges the stranded
      post-terminal item; convergence + terminal-status assertions unchanged.
- [x] T023 Full projects green: Runtime (1363), Resumption (17), Groundwork (639), Publishing.Api (381),
      Runtime.Distributed (54).

## Follow-ups (NOT this unit)

- [ ] #542 general in-process actor eviction / terminal-state passivation trigger (the original run's actor still
      lingers in the registry; this unit only stops the re-drive that keeps re-activating it).
