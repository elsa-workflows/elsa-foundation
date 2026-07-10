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
- **Override:** `services.Replace(ServiceDescriptor.Singleton<ITaskManager, MyManager>())`.

### `ITaskExecutor` *(Core — `Elsa.Tasks.Core`)*
- **Default impl:** `TaskExecutor` (this feature).
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
- `Elsa.Activities.Runtime` — `ActivityConstructorsStartupTask` *(populates the constructor registry)*, `RegisterActivityTypesStartupTask` *(seeds the well-known-type registry with activity aliases)*
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

---

## Cross-references

- `ITaskSchedule` for recurring task schedules lives in `Elsa.Tasks.Schedules`.
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.2 + §2.22.1.
