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
- **Default impl:** `TaskExecutor` (this feature). It applies what a task class declares, in this order:
  1. **`[RequiresSchemaVersion]`** (`Elsa.Cluster.Core`): the requirements go to the shared
     `ISchemaDormancyCheck` first, and a task this node is dormant for is skipped without touching the lock, so a
     dormant node never holds the single-node lock while a capable node waits for it. A shell that composes no check
     has observed nothing, so a declared requirement is never met there: the task is skipped with a warning.
  2. **`[SingleNodeTask]`**: see [the guarantee](#singlenodetask-one-at-a-time-at-shell-start) below.
- **Diagnostics:** startup-task execution emits activity `elsa.startup_task` and histogram
  `elsa.startup_task.duration` from source/meter `Elsa.Tasks.Startup`. Dimensions are bounded to the
  registered task type and `success`, `failed`, `cancelled`, or `skipped`; the skipped outcome is
  determined at this seam because only the executor observes that the node is dormant for the task. A
  single-node lock that is never acquired, or lost while the task runs, is `failed`; only a cancellation
  of the shell's own token is `cancelled`.
- **Override:** `services.Replace(...)`.

### `[SingleNodeTask]`: one at a time, at shell start
The whole guarantee, and nothing more (#2192):

- **One at a time.** The executor takes a distributed lock keyed by the shell's name and the task's type name
  (`elsa:single-node-task:{shell}:{type}`, without the assembly version, so two releases of a task take the same lock
  during a rolling upgrade). Two shells of one process never contend.
- **Wait, then run.** A node that finds the lock held logs that it waits, waits for at most the lock provider's
  acquisition timeout (`LockAcquisitionTimeoutMinutes` on the locking feature, 10 minutes by default), and then runs
  the task itself. It is never skipped because another node ran it. When the wait runs out, the task fails with a
  `TimeoutException` naming the lock, and so does the shell's start.
- **A lost lock cancels the task.** The handle's `HandleLostToken` is linked into the token the task is given. A
  cancellation caused by the loss surfaces as an `InvalidOperationException`, so it is reported and logged as the
  failure it is, never as an orderly cancellation. A task that ignores its token and finishes anyway is logged as a
  warning. Releasing a lock whose connection is gone fails; that is logged as a warning and never replaces the task's
  own outcome.
- **No failover, no fencing, no record.** Nothing records that the task ran, nothing reruns it elsewhere, and nothing
  stops a write made after the lock was lost. The task must be safe to run on every node and after every restart.
  Work that must happen on exactly one node needs a claim of its own.
- **Only a shared lock makes it hold across nodes.** `DatabaseDistributedLocking` (`Elsa.Locking.Database`) holds the
  lock in the shared PostgreSQL, SQL Server or MySQL database. `FileSystemDistributedLocking`'s default folder is
  node-local, which is why a host that joined a cluster through a durable membership provider refuses it at startup;
  a SQLite composition keeps it and is single-node by definition. See the
  [Locking catalog](../Locking/FileSystem/EXTENSION_POINTS.md).
- It is meant for startup tasks. On a background or recurring task, each start, run and stop takes the lock the same
  way, which serializes the calls but elects no leader.

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
- **Attributes:** `[TaskDependency(typeof(OtherTask))]` — runs after `OtherTask`; `[Order(float)]` — relative priority; `[SingleNodeTask]` — runs one node at a time, every node in turn ([the guarantee](#singlenodetask-one-at-a-time-at-shell-start)); `[RequiresSchemaVersion(family, version)]` — skipped while this node is dormant for the family.

**Known implementations (shipped — cross-domain IStartupTask consumers):**
- `Elsa.Serialization` — `JsonPayloadConvertersInitializingStartupTask` *(cross-domain — initialises JSON converters)*
- `Elsa.Activities.Runtime` — `RegisterActivityTypesStartupTask` *(seeds the well-known-type registry with activity and I/O aliases)*
- `Elsa.Activities.Design.Reconciliation` — `ActivityVersionReconcilerStartupTask` *(cross-domain; runs on every node at once, no lock, because its inputs are node-local and concurrent passes converge, #2189)*
- `Elsa.Workflows.Design.Reconciliation` — `WorkflowsVersionReconcilerStartupTask` *(cross-domain; the same, #2187 and #2189)*
- `Elsa.Workflows.Runtime.Reconciliation` — `WorkflowArtifactReconcilerStartupTask` *(cross-domain; `[SingleNodeTask]`, so each node reconciles its own mounted set in turn)*
- `Elsa.Workflows.Design.Reconciliation.Git` — `GitWorkflowExportStartupTask` *(cross-domain; runs on every Writer node without a lock, because its fence is the fast-forward-only push, #2197)*

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
