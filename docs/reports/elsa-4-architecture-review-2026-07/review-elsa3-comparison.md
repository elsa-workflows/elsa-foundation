# Elsa 3 vs Elsa 4 — Workflow Execution Engine Comparison

**Scope:** the *execution engine* only — activity scheduling, suspension/resumption, commit/durability, cancellation, faults, parallelism, mediator/event substrate. Elsa 3 = `/Users/sipke/Projects/Elsa/elsa-core` (baseline). Elsa 4 = `/Users/sipke/Projects/Elsa/elsa-foundation` (rewrite in progress).
**Question:** is Elsa 4's *drainer* on par with Elsa 3's *burst engine*, and what is missing?
**Verdict up front:** Elsa 4's drainer is a **more durable, more testable, architecturally superior core** (command/checkpoint‑sourced with a transactional outbox) but is **not yet at feature parity**: triggers, timers, distributed/clustered execution and the outer stimulus→instance routing layer are absent, and it pays a materially higher per‑step persistence cost. Details and evidence below.

---

## 1. ELSA 3 ENGINE SUMMARY (the "burst" model)

**Entry / invoker.** `WorkflowRunner.RunAsync(...)` builds a `WorkflowExecutionContext`, seeds the scheduler (`ScheduleWorkflow` / `ScheduleBookmark` / `ScheduleActivity` / `ScheduleActivityExecutionContext`), runs the pipeline once, then extracts + commits state.
- `src/modules/Elsa.Workflows.Core/Services/WorkflowRunner.cs` — `RunAsync(WorkflowExecutionContext)`: sends `WorkflowExecuting`/`WorkflowStarted`, calls `pipeline.ExecuteAsync(context)`, then `workflowStateExtractor.Extract`, `WorkflowExecuted`, `commitStateHandler.CommitAsync`.

**Scheduler = in‑memory stack (or queue).** The scheduler is an in‑process collection of `ActivityWorkItem`s, **not persisted**.
- `Contracts/IActivityScheduler.cs`, `Services/StackBasedActivityScheduler.cs` (LIFO `Stack<ActivityWorkItem>`), `Services/QueueBasedActivityScheduler.cs` (FIFO). Selected via `IActivitySchedulerFactory`.
- `Models/ActivityWorkItem.cs` — carries `Activity`, `Owner`, `Tag`, `Variables`, `Input`, scheduling provenance.

**Burst‑of‑execution.** One pipeline run drains the whole scheduler synchronously, in memory, until empty ("burst").
- `Middleware/Workflows/DefaultActivitySchedulerMiddleware.cs`: `while (scheduler.HasAny) { if cancellation break; var wi = scheduler.Take(); await ExecuteWorkItemAsync(...); }` then transitions to `Finished` (`AllActivitiesCompleted()`) or `Suspended`. This loop **is** the burst.

**Activity execution pipeline (per work item).**
- `Services/ActivityInvoker.cs` → activity execution pipeline → `Middleware/Activities/DefaultActivityInvokerMiddleware.cs`:
  - `EvaluateInputPropertiesAsync` (incl. ancestor `Composite` inputs), `CanExecuteAsync` → `Pending` if false, `EnterExecution()`, conditional commit (`ActivityExecuting`), `TransitionTo(Running)`, `ExecuteActivityAsync` (invokes `IActivity.ExecuteAsync` via `ExecuteDelegate`), auto‑burn resumed bookmark, `IncrementExecutionCount`, `ActivityCompleted` notification, conditional commit (`ActivityExecuted`).
- Middleware chain also includes `ExecutionLogMiddleware`, `LoggingMiddleware`, `NotificationPublishingMiddleware`, `ExceptionHandlingMiddleware`.

**Bookmarks (suspension/resumption).** Activities call `context.CreateBookmark(...)`; bookmarks live on the context and are diffed for persistence.
- `Contexts/ActivityExecutionContext.cs` — `CreateBookmark(s)`, `AddBookmark(s)` (lines ~402–478).
- `Contexts/WorkflowExecutionContext.cs` — `Bookmarks`, `OriginalBookmarks`, `BookmarksDiff = Diff.For(OriginalBookmarks, Bookmarks)`; resumption via `ScheduleBookmark` / `ResumedBookmarkContext` + `AutoBurn`.
- Runtime resumes by matching stored bookmarks/triggers (see runtime below).

**Completion / joins.** Parent activities register `ActivityCompletionCallbackEntry` (owner/child/callback); children pop and invoke them on completion.
- `Contexts/WorkflowExecutionContext.cs` — `AddCompletionCallback`, `PopCompletionCallback`, `CompletionCallbacks`.

**Commit strategies.** Persistence timing is pluggable via commit strategies evaluated at lifetime events.
- `CommitStates/` — `IWorkflowCommitStrategy`, `IActivityCommitStrategy`, `CommitAction {Default,Commit,Skip}`, `WorkflowLifetimeEvent`, `ActivityLifetimeEvent`. Evaluated in `DefaultActivitySchedulerMiddleware.ConditionallyCommitStateAsync` and `DefaultActivityInvokerMiddleware.ShouldCommit`. Default: commit **once at end of burst** (`WorkflowRunner` → `commitStateHandler.CommitAsync`).

**Cancellation.** Cooperative via `CancellationToken` + explicit `Cancel()`.
- `Contexts/WorkflowExecutionContext.Cancel.cs`, `CancelWorkflow` registration in ctor; `Middleware/Workflows/ExceptionHandlingMiddleware.cs` catches `OperationCanceledException` → `context.Cancel()`.

**Fault handling / incidents.** Exceptions become `ActivityIncident`s via incident strategies.
- `Middleware/Activities/ExceptionHandlingMiddleware.cs`: `context.Fault(e)` + `IIncidentStrategyResolver` → `strategy.HandleIncident(context)` (fault-and-continue vs fault-and-halt).
- `Middleware/Workflows/ExceptionHandlingMiddleware.cs`: builds `ActivityIncident`, `TransitionTo(Faulted)`.

**Parallelism.** Single‑threaded burst; "parallel" activities (e.g. `Parallel`, `Fork`) schedule multiple work items that interleave through the one scheduler. No OS‑thread parallelism in the engine itself. Cross‑workflow/background parallelism handled by the runtime dispatcher.

**Mediator role.** Heavy use of `Elsa.Mediator` notifications (`INotificationSender.SendAsync`) at every lifecycle point.
- `Runtime/Notifications/*` — `WorkflowExecuting/Started/Executed/Finished`, `ActivityCompleted`, `BookmarkSaved/Saving`, `WorkflowExecutionLogUpdated`, `WorkflowCancelled/Cancelling`, `WorkflowStateCommitted`, `IndexedWorkflowTriggers`, etc. Commands/requests (`ICommandHandler`, `IRequestHandler`) drive dispatch.

**Runtime (outer loop).** `IWorkflowRuntime` → `IWorkflowClient` create/run/resume/cancel; trigger indexing + bookmark routing + background dispatch + distributed locking.
- `Elsa.Workflows.Runtime/Contracts/IWorkflowRuntime.cs` (legacy methods now `[Obsolete]` in favor of client API).
- `Services/TriggerIndexer.cs`, `Services/WorkflowResumer.cs`, `Services/WorkflowDispatchOutboxProcessor.cs` — all use `IDistributedLock` for cross‑node safety.
- `Bookmarks/`, `Contracts/IBookmarkQueue.cs`, `Tasks/TriggerBookmarkQueueRecurringTask.cs`, `Tasks/PurgeBookmarkQueueRecurringTask.cs` — durable bookmark queue for deferred/ingress resumption.

---

## 2. ELSA 4 ENGINE SUMMARY (the "drainer" model)

Elsa 4 replaces the in‑memory burst with a **command‑sourced, checkpoint‑durable state machine**. Every state transition is a persisted `RuntimeSchedulerWorkItem` (a command) drained from a per‑execution queue; each transition commits a `RuntimeCheckpoint` with a **transactional outbox** that enqueues the next command only after the state is durably committed.

**Command intake.** `WorkflowSchedulerCommandProcessor.ProcessAsync` wraps a `WorkflowExecutionCommandEnvelope` into a `RuntimeSchedulerWorkItem`, enqueues it, asks the drain policy whether to drain, then invokes the drain coordinator.
- `src/Elsa/Workflows/Runtime/Core/Services/WorkflowSchedulerCommandProcessor.cs`.
- Work queue: `Contracts/IWorkflowSchedulerWorkQueue.cs` (Enqueue/List/Dequeue, isolated by `workflowExecutionId`), `Models/RuntimeSchedulerWorkItem.cs` (idempotencyKey, sequence, `WorkflowExecutionCommandKind`, JSON payload).

**Drain policy / persistence policy (≈ commit strategies).**
- `Services/ImmediateWorkflowSchedulerDrainPolicy.cs` — drain immediately after enqueue (default). Hook: `Contracts/IWorkflowSchedulerDrainPolicy.cs`.
- `Services/ImmediateRuntimeCheckpointPersistencePolicy.cs` — persist **every** checkpoint immediately. Hook: `Contracts/IRuntimeCheckpointPersistencePolicy.cs` (`Immediate`/`Skip`, with mandatory‑checkpoint guard). This is the Elsa‑4 analogue of the commit strategy — it can coalesce/skip *optional* checkpoints, but mandatory ones cannot be skipped.

**The drain loop (≈ burst).**
- `Services/WorkflowSchedulerDrainer.cs` `DrainAsync`: reads terminal status once, then loops: `PeekAsync` → pause‑gate `EvaluatePauseAsync` → `DequeueAsync` → `DispatchAsync` (route to a `IWorkflowSchedulerWorkHandler`, optionally via `IRuntimeExecutionPipelineDispatcher` runtime middleware) → stop on Faulted → re‑check terminal status (#293). Fallback handlers (`IFallbackWorkflowSchedulerWorkHandler`) are tried after custom handlers; unmatched → `FaultingMissingSchedulerWorkHandler` throws.
- `Services/WorkflowExecutionDrainCoordinator.cs` `DrainAsync`: repeats *drain + post‑commit outbox delivery* for up to `MaxDrainCycles` until quiesced/faulted/paused/outbox‑empty; then notifies `IWorkflowSchedulerDrainObserver`s. **The "drain cycle" is the durable equivalent of one burst iteration.**

**Per‑activity lifecycle = a chain of commands + checkpoints.** Each activity walks: `ScheduleActivity → StartActivity → InvokeActivity → CompleteActivity(ActivityCompleted → ParentCompletionEvaluation → ContinuationScheduling) → Checkpoint`. Each step is its own queue hop; most emit a checkpoint.
- `Services/WorkflowScheduleActivitySchedulerWorkHandler.cs` — creates `ActivityExecutionState{Scheduled}`, commits `ActivityScheduled` checkpoint, post‑commit intent enqueues `StartActivity`.
- `Services/WorkflowStartActivitySchedulerWorkHandler.cs` — transitions to `Running`, `ActivityStarted` checkpoint, post‑commit intent enqueues `InvokeActivity`.
- **Real activity code runs here:** `src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs` (967 lines):
  - Materializes inputs (`IRuntimeActivityInputMaterializer`), builds `VariableScope`, constructs the activity (`IActivityFactory.Create`), builds `SimpleActivityExecutionContext`, then **`await activity.ExecuteAsync(context)`** (line 244).
  - Handles `CanExecuteAsync` → skip; bookmark requests → suspend (enqueue `WorkflowCreateBookmarkSchedulerWorkHandler`); child scheduling (composites/flowchart); recorded outputs; workflow‑scope variable write‑back (#286); `SetOutput`/`Correlate`/`SetName`/`Finish` control‑leaf intents folded atomically into the completion checkpoint; faults → `ActivityFaultIncidentRecorder` (`RecordFaultAsync`).
- `Services/WorkflowCompleteActivitySchedulerWorkHandler.cs` + `src/Elsa/Activities/Runtime/Services/WorkflowParentActivityCompletionSchedulerWorkHandler.cs` (843 lines) — parent/join completion evaluation, continuation scheduling, terminal detection → `ActivityCompleted`/`WorkflowCompleted` checkpoint.

**Durability (the core innovation): checkpoint commit + transactional outbox.**
- `Services/RuntimeCheckpointCommitter.cs` — `DecideAsync` (policy) then folds `PostCommitIntents` into the applied `RuntimeCheckpointStateChangeSet` so the store persists state changes **and** the outbox atomically (`WithPostCommitOutbox`); verifies the store acknowledged every outbox item ("continuation work would be silently dropped" guard); mandatory checkpoints cannot be skipped.
- `Services/RuntimePostCommitOutboxProcessor.cs` — after commit, delivers outbox intents (enqueue next scheduler work) with retryable/failed status recording. This is a classic **transactional‑outbox / durable‑execution** design (Temporal/orchestrator‑style).
- Durable backend exists (not only in‑memory): `src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimeCheckpointWriter.cs` applies a checkpoint through **one document unit‑of‑work** (lifecycle state + inspections + side‑effect state + commit marker succeed/rollback together), keyed by `CommitId` for redelivery idempotency; provider‑neutral manifest supports SQLite/SQL Server/PostgreSQL/MongoDB (`ElsaRuntimeStorageManifest.cs`). Default wiring uses `InMemory*` stores (`Runtime/Core/Services/InMemory*StateStore.cs`, `InMemoryRuntimeCheckpointCommitStore.cs`).

**Cancellation.** A first‑class `Cancel` command, not just a token.
- `Services/WorkflowCancelSchedulerWorkHandler.cs` — sets workflow `Cancelled`, transitions all cancellable activity states to `Cancelled`, commits an `ActivityCancelled` checkpoint (can stage into pipeline `PendingCheckpointCommit`).

**Faults / incidents.** `IncidentState` + `Services/InMemoryIncidentStateStore.cs`; `Activities/Runtime/Services/ActivityFaultIncidentRecorder.cs`; drain stops on `RuntimeSchedulerWorkItemResultStatus.Faulted`.

**Concurrency / single‑writer.** No distributed lock; instead a per‑execution mailbox serializes commands + idempotency dedup.
- `Services/InProcessWorkflowExecutionAgentProvider.cs` — per‑execution `SemaphoreSlim(1,1)` lifecycle lock + inner agent `_mailbox = new SemaphoreSlim(1,1)` serializing `ProcessAsync`; `RememberProcessedIdempotencyKey` dedups replays; capabilities = `InProcessMailbox | Passivation`. Only an **in‑process** provider exists.
- Crash recovery scaffold: `Services/InMemoryRuntimeRecoveryScanner.cs` scans `OperationalState` for `InterruptedExecution{Detected}` candidates.

**Bookmarks.** Persisted `BookmarkState` + create/resume commands + stimulus lookup.
- `Services/WorkflowCreateBookmarkSchedulerWorkHandler.cs`, `Services/BookmarkResumeDispatcher.cs`, `Activities/Runtime/Services/WorkflowResumeBookmarkSchedulerWorkHandler.cs`, `Services/BookmarkStimulusLookup.cs` (matches bookmarks *within a known* `workflowExecutionId`).

**Events substrate (≈ mediator notifications).** `Elsa.Events` replaces notifications.
- `src/Elsa/Events/Core/Contracts/IEvent.cs`, `IEventHandler.cs` (`IEventHandler<in T> { Task Handle(T, CancellationToken); }`), `Services/EventPublisher.cs`, `Strategies/{Sequential,Parallel,Background}ProcessingStrategy.cs`, `Channels/BackgroundEventPublisher.cs`.
- `Elsa.Mediator` retains **command/request** pipelines only (`Mediator/Commands/*`, `Mediator/Requests/*`); notifications moved to the events substrate.

---

## 3. PARITY MATRIX

Status legend: ✅ present · 🟡 partial · ❌ missing · 🔵 intentionally‑different tradeoff.

| Capability | Elsa 3 | Elsa 4 | Status | Evidence |
|---|---|---|---|---|
| Activity scheduling | In‑memory `Stack/Queue<ActivityWorkItem>` burst | Persisted per‑execution `IWorkflowSchedulerWorkQueue` of commands drained per cycle | 🔵 | E3: `StackBasedActivityScheduler.cs`, `DefaultActivitySchedulerMiddleware.cs`; E4: `IWorkflowSchedulerWorkQueue.cs`, `WorkflowSchedulerDrainer.cs` |
| Activity invocation (real `ExecuteAsync`) | `DefaultActivityInvokerMiddleware` | `WorkflowInvokeActivitySchedulerWorkHandler` (in `Elsa.Activities`) | ✅ | E4 line 244 `await activity.ExecuteAsync(context)` |
| Core runtime can invoke activities standalone | Yes (Core) | No — core has only `MissingActivityInvocation/BookmarkResume/GeneratedEvent` fallbacks that throw | 🟡 | `Runtime/Core/Services/Missing*SchedulerWorkHandler.cs` |
| Suspension / resumption (bookmarks) | `CreateBookmark` + `ScheduleBookmark` + `AutoBurn` | `BookmarkState` + Create/Resume commands + `BookmarkStimulusLookup` | ✅ | E3 `ActivityExecutionContext.cs`; E4 `WorkflowCreateBookmarkSchedulerWorkHandler.cs`, `WorkflowResumeBookmarkSchedulerWorkHandler.cs` |
| Triggers / trigger indexing (start from stimulus) | `TriggerIndexer`, trigger stores, `IBookmarkQueue` | none in runtime (grep for `Trigger` in `Workflows/Runtime` = empty) | ❌ | E3 `Runtime/Services/TriggerIndexer.cs`; E4: no trigger types anywhere in `src/Elsa` |
| Timers / scheduled resumption | Yes (recurring tasks, timer activities) | none | ❌ | E4: no timer files in runtime |
| Cancellation | `CancellationToken` + `Cancel()` | first‑class `Cancel` command + checkpoint | ✅/🔵 | E3 `WorkflowExecutionContext.Cancel.cs`; E4 `WorkflowCancelSchedulerWorkHandler.cs` |
| Fault propagation / incidents | `ActivityIncident` + incident strategies | `IncidentState` + `ActivityFaultIncidentRecorder`; drain stops on Faulted | ✅ | E3 `Middleware/Activities/ExceptionHandlingMiddleware.cs`; E4 `RecordFaultAsync`, `InMemoryIncidentStateStore.cs` |
| Compensation / sagas | not a first‑class engine feature | not present | ❌ (both) | — |
| Parallel branches / join semantics | multiple work items interleave; completion callbacks | multiple commands interleave (serialized by mailbox); parent‑completion evaluation handler | ✅/🔵 | E3 `WorkflowExecutionContext` completion callbacks; E4 `WorkflowParentActivityCompletionSchedulerWorkHandler.cs` |
| Composite / child activities | `Composite`, completion callbacks, ancestor input eval | child schedule requests → `ScheduleActivity` per child; scope service | ✅ | E4 `WorkflowInvokeActivitySchedulerWorkHandler.cs` (child scheduling, `RuntimeContainerScopeService`) |
| Variables / memory model | `MemoryRegister` on live context | `DurableValueState` + `VariableScope` + workflow‑scope write‑back (#286), ADR‑0027 container scopes | 🔵 | E3 `WorkflowExecutionContext.MemoryRegister`; E4 `RuntimeContainerScopeService`, durable value changes |
| Expressions at runtime | evaluated in invoker middleware | `IRuntimeActivityInputMaterializer` + `RuntimeInputBindingResolutionContext` | ✅ | E4 `WorkflowInvokeActivitySchedulerWorkHandler.cs` L146‑170 |
| Commit / persistence strategy | commit strategies at lifetime events; default = 1 commit/burst | checkpoint per transition + `IRuntimeCheckpointPersistencePolicy` (Immediate default) + transactional outbox | 🔵 (E4 finer‑grained, higher cost) | E3 `CommitStates/*`; E4 `RuntimeCheckpointCommitter.cs`, `ImmediateRuntimeCheckpointPersistencePolicy.cs` |
| Durability across crash | only at commit boundaries; in‑memory burst lost on crash | every checkpoint atomic + commit‑marker idempotency; recovery scanner | ✅ (E4 stronger) | E4 `GroundworkRuntimeCheckpointWriter.cs`, `InMemoryRuntimeRecoveryScanner.cs` |
| Background / dispatch execution | `IWorkflowDispatcher`, outbox, hosted services | `WorkflowExecutionStartDispatcher`, `BookmarkResumeDispatcher`, agent mailbox, `BackgroundEventPublisher` | 🟡 | E3 `Runtime/Services/WorkflowDispatchOutboxProcessor.cs`; E4 dispatchers exist, no cross‑process transport |
| Distributed concurrency / locking | `IDistributedLock` per instance (cross‑node) | in‑process mailbox single‑writer + idempotency; only `InProcess` agent provider | 🟡 (no cluster yet) | E3 `TriggerIndexer/WorkflowResumer` lock usage; E4 `InProcessWorkflowExecutionAgentProvider.cs` |
| Versioning / migration of running instances | definition versions; running‑instance updates | `PinnedExecutable` (immutable, versioned, hashed) artifact per instance; Design/Reconciliation for defs | 🔵 (no in‑place migration; deterministic replay) | E4 `WorkflowExecutable`, `SchedulerWorkHandlerHelpers.ValidatePinnedExecutable`, `Workflows/Design/Reconciliation/*` |
| Activity execution logs / journal | `ExecutionLog` + `Journal` | `ActivityExecutionInspectionProjection` + `IRuntimeActivityExecutionInspectionAccumulator` | ✅/🔵 | E3 `WorkflowRunner` `Journal`; E4 inspection accumulator per checkpoint |
| Idempotency / exactly‑once continuation | not explicit (re‑run from last commit) | idempotency keys + commit markers + outbox ack guard | ✅ (E4 stronger) | E4 `RuntimeSchedulerWorkItem.IdempotencyKey`, `RememberProcessedIdempotencyKey`, commit‑ledger marker |
| Mediator / eventing | `INotificationSender` (many emit points) + commands/requests | `IEventHandler<T>` events substrate + commands/requests; fewer emit points | 🟡 | E3 `Runtime/Notifications/*`; E4 `Events/Core/Contracts/*`, `Mediator/{Commands,Requests}` |

---

## 4. CONCEPT MAPPING (Elsa 3 → Elsa 4) — migration crib

| Elsa 3 | Elsa 4 | Notes |
|---|---|---|
| `WorkflowRunner` / `pipeline.ExecuteAsync` (one burst) | `WorkflowSchedulerDrainer.DrainAsync` (one drain) + `WorkflowExecutionDrainCoordinator` (drain cycles until quiesced) | burst → drain cycle |
| burst‑of‑execution | drain cycle(s) + post‑commit outbox delivery until `Quiesced` | |
| `IActivityScheduler` (in‑memory `Stack`/`Queue`) | `IWorkflowSchedulerWorkQueue` (durable, per‑execution) | in‑memory → persisted |
| `ActivityWorkItem` | `RuntimeSchedulerWorkItem` (+ `WorkflowExecutionCommandEnvelope`) | now a durable, idempotent command |
| `IActivityInvoker` + activity middleware | `WorkflowInvokeActivitySchedulerWorkHandler` + `IActivityRuntimeMiddleware`/`RuntimeActivityExecutionPipeline` | |
| `ActivityExecutionContext` (live god‑object, 818 LOC) | `ActivityExecutionState` (persisted) + `SimpleActivityExecutionContext` (per‑invoke facade) | decomposed |
| `WorkflowExecutionContext` (720 LOC) | `WorkflowExecutionState` + `DurableValueState` + `SchedulerState` + `OperationalState` | decomposed persisted state |
| `Bookmark` + `CreateBookmark` + `AutoBurn` | `BookmarkState` + `CreateBookmark`/`ResumeBookmark` commands | |
| completion callbacks (join) | `CompleteActivity` phases + `WorkflowParentActivityCompletionSchedulerWorkHandler` | |
| commit strategy / `ICommitStateHandler` / `CommitAsync` | `RuntimeCheckpoint` + `RuntimeCheckpointCommitter` + `IRuntimeCheckpointPersistencePolicy` | commit → **checkpoint** |
| notification handler (`INotificationHandler`/`INotificationSender`) | `IEventHandler<T>` / `IEventPublisher` (+ post‑commit outbox for durable side‑effects) | |
| `ICommandHandler` / `IRequestHandler` | unchanged — `Elsa.Mediator` commands/requests | |
| `IDistributedLock` per instance | `IWorkflowExecutionAgent` mailbox (single‑writer) + idempotency keys | |
| `ActivityIncident` / incident strategy | `IncidentState` + `ActivityFaultIncidentRecorder` | |
| `ExecutionLog` / `Journal` | `ActivityExecutionInspectionProjection` / inspection accumulator | |
| definition version + running‑instance migration | `PinnedExecutable` artifact (immutable/versioned/hashed) + Design/Reconciliation | intentionally different |
| `MemoryRegister` variables | `DurableValueState` + `VariableScope`/container scopes (ADR‑0027) | |
| `CancellationToken` + `Cancel()` | `Cancel` command + `WorkflowCancelSchedulerWorkHandler` | |
| trigger indexer / `IBookmarkQueue` | **no equivalent yet** | gap (see §5) |

---

## 5. FINDINGS (roadmap input) — E3‑n with severity

Severity: **Critical** = production‑blocking parity gap · **High** · **Medium** · **Low/Info**.

- **E3‑1 (Critical) — No triggers / trigger indexing in Elsa 4 runtime.** There is no way to start (or select) a workflow from an external stimulus. Grep for `Trigger` across `src/Elsa/Workflows/Runtime` and the whole `src/Elsa` returns nothing engine‑relevant. Elsa 3 has `Runtime/Services/TriggerIndexer.cs`, trigger stores and `IBookmarkQueue`. Without this, event‑driven start is impossible. *Roadmap: build an Elsa‑4 trigger index + stimulus→(definition|instance) routing layer feeding `WorkflowExecutionStartDispatcher`/`BookmarkResumeDispatcher`.*

- **E3‑2 (Critical) — No timers / scheduled resumption.** No timer activities or recurring scheduler in the Elsa 4 runtime (Elsa 3 has recurring tasks + `Delay`/`Timer`/`Cron`). Time‑based workflows cannot resume. *Roadmap: durable timer store that enqueues `ResumeBookmark` commands at due time.*

- **E3‑3 (Critical) — Only an in‑process execution agent; no distributed/clustered runtime.** `InProcessWorkflowExecutionAgentProvider` is the sole provider; concurrency safety is a per‑process `SemaphoreSlim` mailbox, not a cross‑node lock/lease. Elsa 3 uses `IDistributedLock` for multi‑node safety. The abstraction (`IWorkflowExecutionAgentProvider`, `Capabilities`, `Passivation`) is designed for it, but no cluster provider exists. *Roadmap: a distributed agent provider (lease/partition ownership) + durable command transport.* Evidence: `InProcessWorkflowExecutionAgentProvider.cs`.

- **E3‑4 (High) — The runtime *core* cannot execute anything on its own.** `MissingActivityInvocationSchedulerWorkHandler`, `MissingBookmarkResumeSchedulerWorkHandler`, `MissingGeneratedEventSchedulerWorkHandler` are fallbacks that **throw** unless `Elsa.Activities` (and other feature modules) are composed. This is intentional composition, but means "the drainer" ≠ "a working engine" without those modules; parity claims must include `Elsa.Activities`. Evidence: `Runtime/Core/Services/Missing*SchedulerWorkHandler.cs` vs real handlers in `Activities/Runtime/Services/*`.

- **E3‑5 (High) — Stimulus routing is intra‑instance only.** `BookmarkStimulusLookup.FindAsync` matches bookmarks **within a given `workflowExecutionId`** (`_bookmarkStateStore.ListAsync(request.WorkflowExecutionId)`). There is no global index to answer "which instance(s) anywhere are waiting for stimulus X?" — the outer correlation layer Elsa 3 provides via trigger/bookmark stores + `BookmarkQueue`. Blocks external‑event fan‑in. Evidence: `Services/BookmarkStimulusLookup.cs`.

- **E3‑6 (Medium, performance) — Per‑step persistence overhead is materially higher than the burst.** A single activity traverses ≥4 queue hops (`ScheduleActivity→StartActivity→InvokeActivity→CompleteActivity…`) and commits multiple checkpoints (`ActivityScheduled`, `ActivityStarted`, `ActivityCompleted`), each an atomic store write under `ImmediateRuntimeCheckpointPersistencePolicy`. Elsa 3's default runs an entire workflow to completion in **one in‑memory burst with a single end‑of‑burst commit**. On durable providers this is a large constant‑factor cost multiplier. Mitigation exists (`IRuntimeCheckpointPersistencePolicy` can `Skip` optional checkpoints; drain policy can batch), but the default is immediate and mandatory checkpoints (schedule/start/complete of each activity) cannot be skipped. *Roadmap: a "burst‑equivalent" persistence policy that coalesces intra‑drain checkpoints into one commit at quiescence for non‑durable segments.* Evidence: handler chains in §2; `RuntimeCheckpointCommitter.IsMandatoryCheckpoint`.

- **E3‑7 (Medium, intentional tradeoff) — No in‑place migration of running instances.** Elsa 3 can move a running instance onto a new definition version; Elsa 4 **pins** each instance to an immutable, hashed `PinnedExecutable` artifact and validates it on every step (`ValidatePinnedExecutable`). This buys deterministic replay/durability but removes hot‑migration. Document as a deliberate 🔵 difference, not a bug. Evidence: `WorkflowExecutable*`, `SchedulerWorkHandlerHelpers`, `Workflows/Design/Reconciliation/*`.

- **E3‑8 (Medium) — Durable persistence exists but default wiring is in‑memory; provider breadth/maturity < Elsa 3.** `GroundworkRuntimeCheckpointWriter` gives atomic unit‑of‑work checkpoint commits with commit‑marker idempotency across SQLite/SQL Server/PostgreSQL/MongoDB — architecturally sound — but the default services register `InMemory*` stores, and the management side (definitions/instances/logs query APIs) is thinner than Elsa 3's mature EF/Mongo stack. Evidence: `Persistence/Groundwork/*`, `Runtime/Core/Services/InMemory*StateStore.cs`.

- **E3‑9 (Medium) — Eventing emit points are sparse relative to Elsa 3 notifications.** Elsa 4's `IEventHandler<T>` substrate + post‑commit outbox is cleaner and durable, but the rewrite has not re‑emitted the breadth of Elsa 3's lifecycle notifications (`Runtime/Notifications/*`: bookmark saved/deleted, execution‑log updated, state‑committed, triggers indexed, etc.). Integrations relying on those hooks must be re‑wired. Evidence: E3 `Runtime/Notifications/*` vs E4 `Events/*`.

- **E3‑10 (Low/Info) — Parallelism model is equivalent, not regressed.** Both engines are single‑threaded per instance: Elsa 3 interleaves work items in one burst; Elsa 4 serializes commands through the per‑execution mailbox and interleaves via the queue. Fork/join is preserved (`WorkflowParentActivityCompletionSchedulerWorkHandler`). The terminal‑status re‑check in the drainer (#293) correctly prevents post‑completion sibling work. No parity loss.

- **E3‑11 (Low/Info) — Cancellation is upgraded.** Elsa 4's `Cancel` command atomically transitions the workflow and every cancellable activity and commits a checkpoint — more durable/inspectable than Elsa 3's token‑driven `Cancel()`. 🔵/✅.

---

## 6. JUDGMENT

**Architecturally, Elsa 4's drainer is superior to Elsa 3's burst for the properties that matter in production durability**, at the cost of throughput and complexity, while **feature parity for the "outer" engine (triggers/timers/distribution) is not yet rebuilt.**

**Where Elsa 4 is clearly better (pain points fixed):**
1. **Durability & crash‑consistency.** Elsa 3's burst is in‑memory; a crash mid‑burst loses all progress since the last commit boundary, and re‑running can re‑execute already‑done activities unless a commit strategy persisted between them. Elsa 4 persists **every** transition atomically with its continuation via the transactional outbox (`RuntimeCheckpointCommitter` + `RuntimePostCommitOutboxProcessor` + Groundwork unit‑of‑work), giving crash‑resumable, **exactly‑once continuation** (idempotency keys + commit markers + outbox‑ack guard).
2. **Testability.** Handlers are small, single‑responsibility, pure‑ish state transformations with injected `TimeProvider` and in‑memory stores — far easier to unit test than Elsa 3's 720/818‑line context god‑objects and side‑effecting middleware.
3. **Clarity of state.** Decomposed persisted state (`WorkflowExecutionState`/`ActivityExecutionState`/`DurableValueState`/`IncidentState`/inspection projections) is inspectable and queryable, versus Elsa 3's single mutable in‑memory graph extracted at the end.
4. **Concurrency correctness.** Explicit single‑writer mailbox + idempotency replaces coarse instance‑level distributed locks; replays are safe by construction.
5. **Determinism.** Pinned, hashed executable artifacts eliminate definition/instance version drift during a run.

**Where Elsa 4 is worse or not yet there (strengths lost / not rebuilt):**
1. **Throughput / latency.** The many‑hop command chain + immediate per‑checkpoint persistence is a large constant‑factor slowdown versus a single in‑memory burst with one commit. Needs a coalescing persistence policy to approach Elsa 3's hot‑path performance (**E3‑6**).
2. **The whole "how workflows meet the outside world" layer is missing:** triggers (**E3‑1**), timers (**E3‑2**), global stimulus routing (**E3‑5**). Elsa 3's engine is production‑usable largely *because* of this layer; Elsa 4's drainer can execute and suspend/resume a *known* instance but cannot yet be *driven* by events/time at scale.
3. **No distributed/clustered runtime** (**E3‑3**) — Elsa 3 scales out via distributed locks; Elsa 4 is single‑process today.
4. **Complexity.** The 4‑phase `CompleteActivity` handoff, outbox, drain cycles, and pinned‑executable validation are significantly more moving parts than Elsa 3's completion‑callback burst. This is justified by durability but raises the bar for contributors and debugging.
5. **Ecosystem breadth** (notifications/emit points, mature persistence/management providers, activity library) is thinner (**E3‑8, E3‑9**).

**Bottom line for the roadmap.** The Elsa 4 drainer is the *right* core and is genuinely ahead of Elsa 3 on durability, testability, idempotency and determinism — it is not a toy: real activities execute, faults become incidents, bookmarks suspend/resume, joins evaluate, and commits are atomic with a durable backend. To claim parity, prioritize, in order: **(1) triggers + global stimulus routing (E3‑1/E3‑5), (2) timers (E3‑2), (3) a burst‑coalescing persistence policy for throughput (E3‑6), (4) a distributed agent provider (E3‑3),** then close the ecosystem gaps (E3‑8/E3‑9). The pinned‑executable/no‑migration choice (E3‑7) and the events‑vs‑notifications substrate should be documented as intentional design changes, not regressions.

---

### Appendix — primary files read
**Elsa 3:** `Elsa.Workflows.Core/Services/WorkflowRunner.cs`, `Middleware/Workflows/DefaultActivitySchedulerMiddleware.cs`, `Middleware/Activities/DefaultActivityInvokerMiddleware.cs`, `Services/StackBasedActivityScheduler.cs`, `Contracts/IActivityScheduler.cs`, `Models/ActivityWorkItem.cs`, `Contexts/WorkflowExecutionContext.cs`, `Contexts/ActivityExecutionContext.cs`, `Middleware/Activities/ExceptionHandlingMiddleware.cs`, `Middleware/Workflows/ExceptionHandlingMiddleware.cs`, `CommitStates/*`; `Elsa.Workflows.Runtime/Contracts/IWorkflowRuntime.cs`, `Notifications/*`, `Contracts/IBookmarkQueue.cs`.
**Elsa 4:** `Workflows/Runtime/Core/Services/{WorkflowSchedulerCommandProcessor,WorkflowSchedulerDrainer,WorkflowExecutionDrainCoordinator,WorkflowScheduleActivitySchedulerWorkHandler,WorkflowStartActivitySchedulerWorkHandler,WorkflowCompleteActivitySchedulerWorkHandler,WorkflowCancelSchedulerWorkHandler,RuntimeCheckpointCommitter,RuntimePostCommitOutboxProcessor,ImmediateRuntimeCheckpointPersistencePolicy,ImmediateWorkflowSchedulerDrainPolicy,InProcessWorkflowExecutionAgentProvider,InMemoryRuntimeCheckpointCommitStore,InMemoryRuntimeRecoveryScanner,BookmarkStimulusLookup,Missing*SchedulerWorkHandler}.cs`, `Contracts/{IWorkflowSchedulerWorkHandler,IWorkflowSchedulerWorkQueue,IRuntimeCheckpointPersistencePolicy,IWorkflowExecutionAgent}.cs`, `Models/RuntimeSchedulerWorkItem.cs`; `Activities/Runtime/Services/{WorkflowInvokeActivitySchedulerWorkHandler,WorkflowParentActivityCompletionSchedulerWorkHandler}.cs`; `Events/Core/Contracts/*`, `Events/README.md`; `Persistence/Groundwork/Stores/GroundworkRuntimeCheckpointWriter.cs`, `Persistence/Groundwork/ElsaRuntimeStorageManifest.cs`.
