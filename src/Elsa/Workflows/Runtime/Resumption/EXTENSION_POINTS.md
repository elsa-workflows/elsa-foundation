# Extension points — Workflows.Runtime.Resumption feature

The per-package catalog (framework §2.22.1) for `Elsa.Workflows.Runtime.Resumption`. This feature-gated
package turns durable runtime storage into durable *resumption*: it registers the resumption sweep
service and a recurring pump that re-drives interrupted executions. See the worked reference
[docs/runtime-durable-resumption.md](../../../../../docs/runtime-durable-resumption.md).

---

## Shell feature

### `WorkflowsRuntimeResumptionFeature : IShellFeature` *(`Elsa.Workflows.Runtime.Resumption`)*
- **Feature id:** `WorkflowsRuntimeResumption` (`DependsOn` the `Tasks` feature — the pump is an `IRecurringTask`).
- **Registers:** `IRuntimeResumptionService` → `RuntimeResumptionService` (`TryAddSingleton`), `RuntimeResumptionOptions` (mapped from `[ManifestSetting]`s), and the recurring pump `RuntimeResumptionPumpTask` as `IRecurringTask`.
- **Composition gate:** the Groundwork runtime feature (`GroundworkWorkflowRuntime`) declares `DependsOn = ["WorkflowsRuntimeResumption"]`, so selecting durable runtime stores pulls the pump into the shell — "durable stores ⇒ pump available" is machine-visible in the feature catalog.

## Contributor implementations

### `RuntimeResumptionPumpTask : IRecurringTask` *(`Elsa.Workflows.Runtime.Resumption`)*
- **Kind:** Contributor (recurring task; scheduled by the Tasks domain `TaskManager`).
- **Behavior:** each tick runs one `IRuntimeResumptionService.SweepAsync`. A whole-sweep geometric backoff (bounded by `MaxBackoffInterval`) throttles after consecutive sweep failures; a per-execution geometric backoff parks individual re-drive failures (passed to the sweep as `ExcludedWorkflowExecutionIds`) so one poisoned execution cannot starve the sweep. `MaxExecutionsPerSweep` bounds the executions re-driven per tick. Only `OperationCanceledException` propagates; other sweep exceptions are logged and counted.
- **Schedule:** `AdaptiveIntervalSchedule` *(`Elsa.Tasks.Schedules`)* re-evaluates the interval each run from `RuntimeResumptionOptions.SweepInterval` and the current backoff. Shared by every adaptive pump in the runtime (resumption, durable timers, recurring triggers, reference GC, placement, alterations).
- **Register:** provided by `WorkflowsRuntimeResumptionFeature`; not intended for standalone registration.

Post-commit intent kinds can contribute their own retry policy. Parent-resume delivery uses an unbounded positive-backoff policy because acknowledgement is safe only after durable bookmark consumption is observable. The outbox processor persists each failed attempt before logging a payload-free structured warning, allowing operators to alert on an indefinitely retrying stable item/kind/attempt tuple without exposing intent payloads or exception text. Unsupported kinds continue through the existing policy-selected failed/final path and are never silently acknowledged.

## Options

### `RuntimeResumptionOptions` *(`Elsa.Workflows.Runtime.Resumption`)*
- Sweep interval (default 10s), max backoff interval (default 5m), outbox/backlog/recovery batch sizes (default 100), `MaxExecutionsPerSweep` (default 100), and lease/heartbeat timeouts (default 5m, consumed by the recovery scanner path).

---

## Cross-references

- Swept contracts: `IRuntimeResumptionService`, `IWorkflowSchedulerWorkQueue.ListPendingWorkflowExecutionIdsAsync`, `IRuntimeRecoveryScanner`, `IRuntimePostCommitOutboxProcessor`, `IPostCommitOutboxLookupStore`, and `IWorkflowOutputSource` — see [`../EXTENSION_POINTS.md`](../EXTENSION_POINTS.md).
- Durable queue bridge: `GroundworkWorkflowSchedulerWorkQueue` (`Elsa.Persistence.Groundwork`).
- Worked reference: [docs/runtime-durable-resumption.md](../../../../../docs/runtime-durable-resumption.md).
- Repo-wide index: [`../../../EXTENSION_POINTS.md`](../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.2 + §2.22.1.
