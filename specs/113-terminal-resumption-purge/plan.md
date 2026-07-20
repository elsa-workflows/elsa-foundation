# Plan — spec 113: terminal-aware resumption sweep

## Approach

Make `RuntimeResumptionService` terminal-aware. Between discovery and re-drive, read each discovered execution's
persisted status. Terminal executions are purged (residual scheduler work deleted) and skipped; non-terminal
executions are re-driven exactly as before.

Chosen over the alternatives:

- **Change the drainer to delete residue on terminal.** Rejected: touches the hot drain path and inverts #293's
  established "leave siblings queued" contract (and its test). The resumption-service janitor runs out-of-band,
  only for executions confirmed terminal in durable state, so it is strictly safer.
- **Skip re-drive without purging.** Rejected: the stranded item stays and keeps occupying a discovery slot every
  sweep, which at scale starves genuine backlog out of the capped per-sweep set. Purging removes the seed so the
  execution stops being rediscovered.

## Files

- `src/Elsa/Workflows/Runtime/Services/RuntimeResumptionService.cs` — add required `IWorkflowExecutionStateStore`;
  in `SweepAsync`, for each discovered id: if terminal, `PurgeResidualSchedulerWorkAsync` + skip; else re-drive.
  Purge lists a bounded page (`BacklogBatchSize`) and deletes by identity, looping to a safety cap.
- `src/Elsa/Workflows/Runtime/Core/Models/RuntimeResumption.cs` — add `TerminalExecutionsPurged` +
  `PurgedWorkItemCount` to `RuntimeResumptionSweepResult`; include purges in `DidWork`.
- `src/Elsa/Workflows/Runtime/Resumption/RuntimeResumptionPumpTask.cs` — surface purge counts in the debug log.
- `src/Elsa/Workflows/Runtime/Services/Coalescing/CoalescingWorkflowSchedulerWorkQueue.cs` — delegate
  `DeleteAsync` to the inner durable queue (the wrapper previously left it as the throwing interface default;
  purge runs outside any coalescing session, mirroring `ListPendingWorkflowExecutionIdsAsync`).

## Wiring

`IRuntimeResumptionService` is DI-registered (`TryAddScoped`), so the new required `IWorkflowExecutionStateStore`
dependency resolves from the runtime-core registration. Test/host direct constructions updated to pass the store.

## Test strategy

- Unit (`RuntimeResumptionServiceTests`): terminal execution with residual work is purged + never activated;
  non-terminal execution with backlog is still re-driven. Existing sweep tests keep passing because an empty state
  store reports no terminal execution (behaviour-identical to pre-change for non-terminal ids).
- Durable + coalescing (`GroundworkCoalescingCrashConvergenceTests`): the previously-stranded post-terminal item is
  now purged on the second sweep; convergence + terminal status assertions unchanged.
- Full projects run at QA (never filtered subsets).
