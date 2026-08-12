# Extension points — Tasks domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Tasks` — the composition root where `TasksFeature` registers `TaskManager`, `TaskExecutor`, and `TopologicalTaskSorter`. Two sections apply.

---

## Overridable contracts

### `ITaskManager` *(Core — `Elsa.Tasks.Core`)*
- **Default impl:** `TaskManager` (this feature). Registered as a **shell-singleton**: it owns the
  start/stop lifecycle of shell-lifetime background and recurring tasks (and the singleton
  `IEventChannel`), so it must live for the shell's lifetime — a scoped manager is disposed at the
  end of the shell-initializer scope and would tear those singletons down shortly after activation.
- **Lifecycle:** `RunShellTasksInitializer` starts tasks in CShells' `Start` phase;
  `StopShellTasksTerminator` stops them at the mirrored `Start`-phase teardown point, before later
  terminators flush stores and before the shell provider is disposed. `TaskManager.DisposeAsync`
  awaits the same idempotent stop operation as a fallback.
- **Override:** `services.Replace(ServiceDescriptor.Singleton<ITaskManager, MyManager>())`. Implement
  `IStoppableTaskManager` as an additive capability when the replacement owns shell-lifetime work that
  must stop before provider disposal; existing `ITaskManager` implementations remain compatible and
  otherwise keep their previous DI-disposal behavior.

### `ITaskExecutor` *(Core — `Elsa.Tasks.Core`)*
- **Default impl:** `TaskExecutor` (this feature).
- **Diagnostics:** startup-task execution emits activity `elsa.startup_task` and histogram
  `elsa.startup_task.duration` from source/meter `Elsa.Tasks.Startup`. Dimensions are bounded to the
  registered task type and `success`, `failed`, `cancelled`, or `skipped`; the skipped outcome is
  determined at this seam because only the executor observes an unavailable single-node lock.
- **Override:** `services.Replace(...)`.

### `ITopologicalTaskSorter` *(Core — `Elsa.Tasks.Core`)*
- **Default impl:** `TopologicalTaskSorter` (this feature) — orders startup tasks respecting `[TaskDependency]` + `[Order]` attributes.
- **Override:** `services.Replace(...)` to provide a custom ordering strategy.

> **Note:** `ITaskStateManager` is created internally by `TaskManager` and is not DI-registered — it is not an override seam.

---

## Implementable contributor interfaces

### `IStartupTask : ITask` *(Core — `Elsa.Tasks.Core`)*
- **Kind:** Contributor — run once at application startup in topological order (respecting `[TaskDependency]` + `[Order]`).
- **Signature:** `ValueTask ExecuteAsync(CancellationToken cancellationToken);`
- **Register:** `services.AddScoped<IStartupTask, MyTask>()`.
- **Attributes:** `[TaskDependency(typeof(OtherTask))]` — runs after `OtherTask`; `[Order(float)]` — relative priority; `[SingleNodeTask]` — only one instance runs in a multi-node deployment.

**Known implementations (shipped — cross-domain IStartupTask consumers):**
- `Elsa.Persistence.EFCore` — `RunMigrationsStartupTask` *(cross-domain — runs EF Core migrations)*
- `Elsa.Serialization` — `JsonPayloadConvertersInitializingStartupTask` *(cross-domain — initialises JSON converters)*
- `Elsa.Activities.Runtime` — `RegisterActivityTypesStartupTask` *(seeds the well-known-type registry with activity and I/O aliases)*
- `Elsa.Activities.Design.Reconciliation` — `ActivityVersionReconcilerStartupTask` *(cross-domain)*
- `Elsa.Workflows.Design.Reconciliation` — `WorkflowsVersionReconcilerStartupTask` *(cross-domain)*

### `IRecurringTask : ITask` *(Core — `Elsa.Tasks.Core`)*
- **Kind:** Contributor — run on a schedule. Configure schedule via `ITaskSchedule` (`Elsa.Tasks.Schedules`).
- **Signature:** `ValueTask ExecuteAsync(CancellationToken cancellationToken);`
- **Register:** `services.AddScoped<IRecurringTask, MyTask>()`. Configure interval via `RecurringTaskSchedule.ConfigureTask<T>(TimeSpan)` / `(string cronExpression)`.

### `IBackgroundTask : ITask` *(Core — `Elsa.Tasks.Core`)*
- **Kind:** Contributor — long-running background work (hosted service lifecycle).
- **Register:** `services.AddScoped<IBackgroundTask, MyTask>()`.

**Known implementations:**
- `Elsa.Events` — `BackgroundEventPublisher` *(cross-domain — background event dispatch worker)*

## Writing a bounded-sweep pump (`BackoffSweepPumpTask`)

A recurring background pump that runs one bounded sweep per tick derives from `BackoffSweepPumpTask`
(project `Elsa.Tasks.Schedules`) instead of implementing `IRecurringTask` and re-carrying the failure
skeleton. The base owns it once: `ExecuteAsync` runs the derived `SweepAsync`, resets the failure count
on success, and on any handled exception increments it and reports through `OnSweepFailed` without
rethrowing (so a failing sweep cannot crash the host); `CurrentSweepInterval` widens geometrically from
`SweepInterval` toward `MaxBackoffInterval`, and `GetSchedule` feeds it to an `AdaptiveIntervalSchedule`.

A derivation supplies the sweep, its failure log message, and optionally: which exceptions feed the
backoff (`IsHandledSweepException`; everything by default — override to let fatal exceptions escape),
and which cancellations escape (`ShouldRethrowCancellation`; every `OperationCanceledException` by
default — override to narrow to the pump's own token so a dependency's unrelated timeout backs off
instead). The protected `ComputeBackoff` is reusable for per-item parking maps.

All six shipped pumps are cross-domain consumers: `RuntimeResumptionPumpTask`, `DurableTimerPumpTask`,
`RecurringTriggerPumpTask`, `WorkflowExecutableReferenceGarbageCollectionPumpTask`,
`ExecutionPlacementPumpTask`, and `WorkflowAlterationOrchestrationPumpTask` *(all cross-domain — the
pumps live in `Elsa.Workflows.Runtime.*` projects, which already reference `Elsa.Tasks.Schedules`)*.

---

## Cross-references

- `ITaskSchedule` for recurring task schedules lives in `Elsa.Tasks.Schedules`.
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.2 + §2.22.1.
