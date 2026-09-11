# How a workflow executes

> **Audience:** a developer opening this repository for the first time.
> **Purpose:** trace one request from the process entry point to a durable checkpoint, naming the file that
> does each step. Every path below is relative to the repository root.
> **Knowledge role:** orientation. Definitions live in [`docs/glossary/elsa.md`](glossary/elsa.md); the
> runtime extension points live in [`src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md`](../src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md).

## The process entry point

The process starts in `src/Apps/Elsa.Workbench/Program.cs`. It is a top-level-statements ASP.NET Core program:
`WebApplication.CreateBuilder(args)`, then `app.Run()` at the bottom. Nothing else in the repository is an entry
point; every library under `src/Elsa` is composed by this file or by a host you write yourself.

`Program.cs` does three things that matter for execution:

1. It loads `shells.json` (and `shells.{Environment}.json`) into configuration.
2. It calls `builder.Services.AddCShellsAspNetCore(...)` and lists the assemblies that contain feature classes.
3. It calls `app.MapShells()`, which routes each request to a shell and swaps `HttpContext.RequestServices` to
   that shell's service provider.

A **shell** is a named service container built from a list of **features**. The list is the
`CShells:Shells:default:Features` object in `src/Apps/Elsa.Workbench/shells.json`. Each key there names a class
marked `[ShellFeature(name: "...")]` that implements `IShellFeature` (from the `CShells.Abstractions` package) and
registers services in `ConfigureServices(IServiceCollection)`. A feature that is not in `shells.json` registers
nothing. The three features on the execution path are `WorkflowsRuntimeApi`
(`src/Elsa/Workflows/Runtime/Api/WorkflowsRuntimeApiFeature.cs`), `ActivitiesRuntime`
(`src/Elsa/Activities/Runtime/ActivitiesRuntimeFeature.cs`) and `GroundworkWorkflowRuntime`
(`src/Elsa/Persistence/Groundwork/V2/Runtime/GroundworkWorkflowRuntimeFeature.cs`).

## The path we trace

A client sends `POST runtime/workflows/executables/{artifactId}/execute` to start a published workflow. The
default shell mounts at the root path (`WebRouting.Path` is `""` in `shells.json`), so the route is served from
the host root. The steps below follow that one request until the first activity has run and its state is on disk.

### 1. The endpoint

`src/Elsa/Workflows/Runtime/Api/Endpoints/Executables/Execute/Endpoint.cs` declares the route with
`[Post("runtime/workflows/executables/{artifactId}/execute")]` and requires the `WorkflowRuntimeExecute`
permission. It calls `IWorkflowExecutionStartService.ExecuteAsync`.

`src/Elsa/Workflows/Runtime/Api/Handlers/WorkflowExecutionStartService.cs` implements that service. It reads the
executable once through `IWorkflowExecutableStore.FindAsync(artifactId)` to seed authored variable defaults, then
builds a `WorkflowExecutionStartDispatchRequest` and calls `IWorkflowStartDispatcher.DispatchAsync`.

There is a second way in. The `HttpEndpoint` activity is served by
`src/Elsa/Activities/Http/Middleware/HttpEndpointMiddleware.cs`, mounted by `ActivitiesHttpFeature.UseMiddleware`
under the base path `/workflows/http`. It matches the request against the shell's `IRouteTable`, builds a
`StimulusDispatchRequest`, and hands it to `IStimulusRouter`
(`src/Elsa/Workflows/Runtime/Services/StimulusRouter.cs`). The router starts every published workflow whose
trigger index matches and resumes every waiting bookmark that matches. Both branches end in the same two
dispatchers the API path uses, so the rest of this document applies to it too.

### 2. Resolving the executable

Runtime never executes a workflow definition. It executes a `WorkflowExecutable`
(`src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutable.cs`): an immutable, content-addressed artifact holding
the root `ExecutableNode`, the resume targets and the incident strategy. Publishing produces it:
`src/Elsa/Workflows/Publishing/Handlers/PublishWorkflowRequestHandler.cs` compiles a definition version and calls
`IWorkflowExecutableStore.SaveAsync`.

`IWorkflowExecutableStore` (`src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowExecutableStore.cs`) is the store
Runtime reads from. The default is `InMemoryWorkflowExecutableStore`. With `GroundworkWorkflowRuntime` enabled,
`GroundworkV2RuntimeRegistration.RegisterExecutableStore` replaces it with `CachingWorkflowExecutableStore`
(`src/Elsa/Workflows/Runtime/Core/Services`) over `GroundworkV2WorkflowExecutableStore`
(`src/Elsa/Persistence/Groundwork/V2/Runtime`).

`src/Elsa/Workflows/Runtime/Services/WorkflowStartDispatcher.cs` does the resolution. It finds the artifact,
checks that a live `Published` source reference points at it (`IWorkflowExecutableSourceReferenceStore`,
ADR 0040), runs `IWorkflowExecutableStartPolicy`, and builds a `WorkflowExecutionCommandEnvelope` whose command
kind is `WorkflowExecutionCommandKind.Start`.

### 3. The mailbox: one writer per execution

The dispatcher does not run anything. It calls `IWorkflowExecutionActorProvider.GetAgentAsync` and then
`IWorkflowExecutionActor.EnqueueAsync(envelope)`. The provider is
`src/Elsa/Workflows/Runtime/Services/InProcessWorkflowExecutionActorProvider.cs`. It keeps one mailbox per
workflow execution id, and the mailbox admits one command at a time. That is the single-writer rule (ADR 0031):
every command for an execution goes through its mailbox, so at most one drain runs per execution.

The mailbox hands the envelope to `IWorkflowExecutionCommandExecutor`. The registered implementation is
`ScopedWorkflowExecutionCommandExecutor` (same folder), which opens one persistence scope per command and
resolves `WorkflowSchedulerCommandRouter` from it.

### 4. Enqueue, then drain

`src/Elsa/Workflows/Runtime/Services/WorkflowSchedulerCommandRouter.cs` turns the command into a
`RuntimeSchedulerWorkItem` and calls `IWorkflowSchedulerWorkQueue.EnqueueAsync`. The queue contract is
`src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowSchedulerWorkQueue.cs`; enqueue is idempotent by
`(WorkflowExecutionId, WorkItemId)`. The default queue is `InMemoryWorkflowSchedulerWorkQueue`; Groundwork
replaces it with `GroundworkV2WorkflowSchedulerWorkQueue`.

The router then asks `IWorkflowSchedulerDrainPolicy` for a drain request (the default,
`ImmediateWorkflowSchedulerDrainPolicy`, always drains now), pushes a `WorkflowBurstScope` for the drain, and
calls `IWorkflowDrainOrchestrator.DrainAsync`.

`src/Elsa/Workflows/Runtime/Services/WorkflowDrainOrchestrator.cs` acquires the execution ownership lease
(`IRuntimeExecutionOwnershipService.AcquireAsync`), runs the drainer, processes the post-commit outbox, and
notifies every `IWorkflowSchedulerDrainObserver`. The observers are where fault outcomes are decided; see
[runtime fault behavior](runtime-fault-behavior.md).

`src/Elsa/Workflows/Runtime/Services/WorkflowSchedulerDrainer.cs` is the loop. Each iteration claims the head
work item for this execution, checks the pause gate, picks the one `IWorkflowSchedulerWorkHandler` whose
`CanHandle` accepts the item's `CommandKind`, runs it, and completes the claim. The loop stops when the queue is
empty, a handler faults, the pause gate blocks, or the execution reaches a terminal status.

### 5. The handler chain for a start

Handlers are small classes named `*SchedulerWorkHandler`. Each consumes one command kind and enqueues the next.
For our request the chain is:

1. `Start` → `WorkflowStartSchedulerWorkHandler` (`src/Elsa/Workflows/Runtime/Services`). It re-reads the
   executable and enqueues a `Checkpoint` work item that records "workflow started" and carries one post-commit
   intent: enqueue `StartActivity` for the root node.
2. `Checkpoint` → `WorkflowCheckpointSchedulerWorkHandler`. It builds a `RuntimeCheckpointCommit` and commits it
   (section 6). After the commit, `RuntimeSchedulerPostCommitIntentDispatcher` turns the intent into the queued
   `StartActivity` item.
3. `StartActivity` → `WorkflowStartActivitySchedulerWorkHandler`. If the node is an engine intrinsic
   (`ExecutableNode.IntrinsicKind` is set: `Set`, `Merge`, `Reduce`, `Return`, `Control`, `SetOutput`, `Finish`, ...)
   it runs `WorkflowIntrinsicExecutor.ExecuteAsync` (`src/Elsa/Workflows/Runtime/Services/WorkflowIntrinsicExecutor.cs`),
   which reads and writes variable state without activating a CLR type and returns a commit. Otherwise it
   commits "activity started" with an `InvokeActivity` continuation.
4. `InvokeActivity` → `WorkflowInvokeActivitySchedulerWorkHandler`
   (`src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`). This is where user
   code runs. It materializes inputs, calls `IActivityActivator.ActivateAsync` (`ActivityActivator` picks an
   `IActivityActivationStrategy` by descriptor kind), and calls `activity.ExecuteAsync(context)`. Three outcomes:
   - completion: it commits "activity completed" with a `CompleteActivity` continuation;
   - suspension: it enqueues `CreateBookmark` (section 7);
   - exception: `ActivityFaultIncidentRecorder` commits an incident and the drain reports the item as completed.
5. `CompleteActivity` → `WorkflowCompleteActivitySchedulerWorkHandler` enqueues the completion checkpoint and a
   second `CompleteActivity` item whose payload kind is `ParentCompletionEvaluation`. Only
   `WorkflowParentActivityCompletionSchedulerWorkHandler` (`src/Elsa/Activities/Runtime/Services`) accepts that
   kind; it lets the container activity (for example `src/Elsa/Activities/Sequence/Activities/Sequence.cs`) pick
   the successor and enqueues `ScheduleActivity` for it. `WorkflowScheduleActivitySchedulerWorkHandler` then
   enqueues `StartActivity`, and the chain repeats from step 3.

The full command vocabulary is the `WorkflowExecutionCommandKind` enum in
`src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutionCommand.cs`.

### 6. Writing state: the checkpoint commit

Every handler that changes state produces a `RuntimeCheckpointCommit`
(`src/Elsa/Workflows/Runtime/Core/Models/RuntimeCheckpointCommit.cs`): a named `RuntimeCheckpoint`, a
`RuntimeCheckpointStateChangeSet` (workflow execution, activity executions, bookmarks, durable values, incidents,
liveness) and a list of `RuntimePostCommitIntent` continuations. Nothing is written by handlers directly.

`src/Elsa/Workflows/Runtime/Services/RuntimeCheckpointCommitter.cs` commits it. In order: run the
`IRuntimeCheckpointCommitEnricher` set, stamp the ownership lease as `ExpectedFence`, ask
`IRuntimeCheckpointPersistencePolicy` whether to persist now (`ImmediateRuntimeCheckpointPersistencePolicy` by
default; the `WorkflowsRuntimeCheckpointPersistence` feature in `shells.json` switches to `Coalesced`), fold the
post-commit intents into an outbox and the claimed work item's deletion into the same change set, and call
`IRuntimeCheckpointCommitStore.CommitAsync`.

`IRuntimeCheckpointCommitStore` (`src/Elsa/Workflows/Runtime/Core/Contracts/IRuntimeCheckpointCommitStore.cs`)
has two implementations in this repository:

- `InMemoryRuntimeCheckpointCommitStore` (`src/Elsa/Workflows/Runtime/Services`): the default from
  `AddWorkflowRuntime`. It applies the change set to the in-memory state stores. Nothing survives a restart.
- `GroundworkV2RuntimeCheckpointWriter` (`src/Elsa/Persistence/Groundwork/V2/Runtime`): registered by
  `GroundworkWorkflowRuntimeFeature` through `GroundworkV2RuntimeRegistration.AddGroundworkV2RuntimeStores`. It
  writes every row of the change set, the outbox items and a create-only checkpoint marker in one Groundwork
  unit of work. The marker is what makes a replayed commit a no-op.

In the Workbench the Groundwork provider is SQLite (`GroundworkProviderSqlite` in `shells.json`, implemented in
`src/Apps/Elsa.Workbench/Groundwork/GroundworkSqliteProviderFeature.cs`); the file is
`src/Apps/Elsa.Workbench/elsa-groundwork.db`.

### 7. Suspending on a bookmark and resuming

When an activity returns a suspend transition, the invoke handler enqueues `CreateBookmark`.
`WorkflowCreateBookmarkSchedulerWorkHandler` (`src/Elsa/Workflows/Runtime/Services`) commits a `BookmarkState`
(`src/Elsa/Workflows/Runtime/Core/Models/BookmarkState.cs`) keyed by `(StimulusType, StimulusHash)` and marks the
activity execution `BookmarkWaiting`. It also notifies `BookmarkLifecycleNotifier`, which is how the HTTP route
table learns about a mid-flow `HttpEndpoint`. The drain then quiesces and the mailbox is idle.

Resumption starts with a stimulus: the HTTP middleware above, or `POST runtime/workflows/stimuli`
(`src/Elsa/Workflows/Runtime/Api/Endpoints/Stimuli/Dispatch/Endpoint.cs`). `IBookmarkResumeDispatcher`
(`src/Elsa/Workflows/Runtime/Services/BookmarkResumeDispatcher.cs`) looks up matching bookmarks through
`IBookmarkStimulusLookup`, builds a `ResumeBookmark` envelope and sends it to the execution's mailbox. The drain
runs `WorkflowResumeBookmarkSchedulerWorkHandler`
(`src/Elsa/Activities/Runtime/Services/WorkflowResumeBookmarkSchedulerWorkHandler.cs`), which re-reads the
executable, the activity execution and the bookmark, consumes the bookmark, and resumes the activity with the
stimulus payload as input. From there the chain continues as in section 5.

### 8. Recovery after a restart

Durable stores keep checkpoints, outbox items and queued work across a crash, but nothing acts on them until a
sweep runs. `WorkflowsRuntimeResumptionFeature` (`src/Elsa/Workflows/Runtime/Resumption/WorkflowsRuntimeResumptionFeature.cs`)
registers `RuntimeResumptionPumpTask` as an `IRecurringTask` (it depends on the `Tasks` feature). Every
`GroundworkWorkflowRuntime` shell depends on it, so a durable store is never composed without the pump.

Each tick calls `IRuntimeResumptionService.SweepAsync`, implemented by
`src/Elsa/Workflows/Runtime/Services/RuntimeResumptionService.cs`. One sweep does three things: deliver pending
post-commit outbox items, list executions that still have queued work
(`IWorkflowSchedulerWorkQueue.ListPendingWorkflowExecutionIdsAsync`) plus candidates from
`IRuntimeRecoveryScanner.ScanPageAsync`, and re-drive each execution by sending a `RunSchedulerWork` envelope to
its mailbox. Re-driving never bypasses the mailbox, so the single-writer rule holds during recovery too.

`IRuntimeRecoveryScanner` (`src/Elsa/Workflows/Runtime/Core/Contracts/IRuntimeRecoveryScanner.cs`) defaults to
`InMemoryRuntimeRecoveryScanner`, which reads `IExecutionLivenessStateStore`; Groundwork replaces it with
`GroundworkV2RuntimeRecoveryScanner`. The crash windows this covers are worked through in
[durable resumption](runtime-durable-resumption.md).

## Where the defaults are registered

`src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs` holds `AddWorkflowRuntime()`.
Every registration in it uses `TryAdd`, so a persistence feature that registers first, or removes and re-adds,
wins. Reading that one method tells you the default implementation of every runtime contract: the `InMemory*`
stores, the `IWorkflowSchedulerWorkHandler` set (`TryAddEnumerable`), the drainer, the committer, the policies
and the kill switches (`RuntimeBurstCacheOptions`, `RuntimeInProcessHopFastPathOptions`).

`WorkflowsRuntimeApiFeature.ConfigureServices` calls `AddWorkflowRuntime()`; a non-HTTP host can call it directly.
`ActivitiesRuntimeFeature` adds the four activity-side handlers (`InvokeActivity`, `CompleteActivity` parent
evaluation, `NotifyParentActivity`, `ResumeBookmark`). `GroundworkWorkflowRuntimeFeature` replaces the whole
persistence family through `AddGroundworkV2RuntimeStores`. Which of these run is decided by the feature list in
`src/Apps/Elsa.Workbench/shells.json`.

## Glossary of the ten words you need

- **Definition**: the named, logical workflow authored on the Design side; `WorkflowDefinition` in
  `src/Elsa/Workflows/Design/Persistence/Core/Entities/WorkflowDefinition.cs` owns many drafts and many versions.
- **Version**: one immutable, SemVer-numbered snapshot of a definition's authored state
  (`WorkflowDefinitionVersion`, same folder), optionally promoted from a draft via `SourceDraftId`.
- **Draft**: the mutable authored state of one definition between versions
  (`IWorkflowDefinitionDraft` in `src/Elsa/Workflows/Design/Core/Contracts`).
- **Executable**: the immutable, content-addressed artifact compiled from a version at publish time
  (`WorkflowExecutable`); Runtime runs this and never reads the definition.
- **Execution**: one run of one executable, pinned to that executable's identity, with a status and correlation
  (`WorkflowExecutionState`); the API routes call it an *instance*.
- **Activity execution**: one concrete execution of one `ExecutableNode` inside a workflow execution
  (`ActivityExecutionState`, with its identity in `ActivityExecution`).
- **Bookmark**: the durable resume handle one activity execution leaves behind while it waits, matched by
  `(StimulusType, StimulusHash)` (`BookmarkState`).
- **Checkpoint**: a named boundary at which state changed (`RuntimeCheckpoint`); the atomic envelope that carries
  those changes to the store is a `RuntimeCheckpointCommit`.
- **Incident**: the durable record of an execution-affecting failure, with severity, status and resolution
  (`IncidentState`); creating one does not by itself terminate the workflow.
- **Scheduler work item**: one queued command for one workflow execution (`RuntimeSchedulerWorkItem`, with a
  `CommandKind`); the drainer runs them one at a time per execution.

All of these models live under `src/Elsa/Workflows/Runtime/Core/Models` unless a path says otherwise.
