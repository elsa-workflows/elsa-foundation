# Extension points — Workflows.Runtime domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Workflows.Runtime.Core`; provider/default implementations may live in sibling runtime projects. Runtime execution contracts are Design-free and operate on runtime-owned executable artifacts and execution state.

---

## Overridable replacement contracts

### `IRuntimeCheckpointPersistencePolicy` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one policy decides how checkpoints flush in a runtime composition).
- **Signature:** `DecideAsync(RuntimeCheckpoint checkpoint, CancellationToken cancellationToken = default)`.
- **Usage:** separates checkpoint semantics from persistence timing. The checkpoint name says what changed; the policy decides immediate, deferred, or skipped flush.
- **Default implementation:** `ImmediateRuntimeCheckpointPersistencePolicy` *(intra-domain default)*.

### `IRuntimeCheckpointWriter` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one writer owns persistence of checkpoint envelopes for a runtime composition).
- **Signature:** `WriteAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)`.
- **Usage:** implemented by runtime persistence providers to commit the checkpoint boundary and its atomic state-change envelope.
- **Default implementation:** `InMemoryRuntimeCheckpointWriter` *(single-node in-memory default for the current runtime slice; durable providers replace this)*.

### `IRuntimePostCommitIntentDispatcher` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one dispatcher owns delivery of committed outbound runtime intents for a composition).
- **Signature:** `DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)`.
- **Default implementation:** `RuntimeSchedulerPostCommitIntentDispatcher` *(dispatches scheduler-work intents after checkpoint commit; durable outbox providers replace this for distributed delivery)*.
- **Usage:** dispatches post-commit intents in the order provided by the committed `RuntimeCheckpointCommit` only after `IRuntimeCheckpointWriter` completes successfully. This is a placeholder contract, not a full outbox processor.

### `IRuntimePostCommitOutboxStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one provider owns durable post-commit outbox state for a runtime composition).
- **Signature:** `SavePendingAsync(RuntimePostCommitOutboxItem item, ...)`, `GetDeliverableAsync(RuntimePostCommitOutboxQuery query, ...)`, `RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, ...)`.
- **Usage:** stores delivery state for post-commit intents so providers can preserve record, commit, deliver, and mark-delivered ordering.
- **Default implementation:** `InMemoryRuntimePostCommitOutboxStore` *(single-node in-memory default for the current runtime slice; durable providers replace this)*.

### `IRuntimeRecoveryScanner` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one scanner identifies interrupted workflow executions for a runtime composition).
- **Signature:** `ScanAsync(RuntimeRecoveryScanRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** provider implementations inspect operational state such as leases and heartbeats and return recovery candidates that requeue from the last checkpoint without invoking domain retry policy.
- **Default implementation:** `InMemoryRuntimeRecoveryScanner` *(single-node in-memory default for operational recovery candidate discovery)*.

### `IRuntimeResumptionService` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one service owns a single system-wide resumption sweep pass for a runtime composition).
- **Signature:** `SweepAsync(RuntimeResumptionSweepRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** one sweep pass re-delivers stranded post-commit outbox items system-wide (`ProcessAsync(workflowExecutionId: null, intentKind: EnqueueSchedulerWork)`), unions durable scheduler-queue backlog (`IWorkflowSchedulerWorkQueue.ListPendingWorkflowExecutionIdsAsync`) with `IRuntimeRecoveryScanner` candidates, and re-drives each discovered execution by enqueueing a `RunSchedulerWork` envelope through the agent mailbox — preserving single-writer discipline. The request bounds each sweep (`MaxExecutionsPerSweep`) and skips executions the caller is backing off (`ExcludedWorkflowExecutionIds`). Re-drive failures surface on the result and do not abort the sweep; callers own logging and backoff. It is not registered by the runtime API feature — only the `WorkflowsRuntimeResumption` shell feature registers it and drives it from a recurring pump.
- **Default implementation:** `RuntimeResumptionService` *(registered by the feature-gated `Elsa.Workflows.Runtime.Resumption` package)*.

### `IRuntimeDomainRetryPolicy` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one policy decides workflow/activity domain retry behavior for a runtime composition).
- **Signature:** `Decide(RuntimeDomainRetryRequest request)`.
- **Usage:** keeps workflow/activity retry decisions separate from operational recovery such as lost leases and interrupted execution agents.
- **Default implementation:** `NoopRuntimeDomainRetryPolicy` *(explicit do-not-retry baseline; workflow/activity retry policy providers replace this)*.

### `IRuntimeFaultCapturePolicy` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one policy decides how an exception is turned into structured fault information for a runtime composition).
- **Signature:** `Capture(Exception exception)` → `RuntimeFaultInfo`.
- **Usage:** unifies runtime fault capture (RT-12) so the drainer's handler-crash path and the post-commit outbox delivery path both record the same structured `RuntimeFaultInfo` (exception type + message, stack trace behind an opt-in flag) instead of two divergent `exception.ToString()` / `exception.Message` policies. `RuntimeFaultInfo.ToSummaryString()` yields `"{ExceptionType}: {Message}"`.
- **Default implementation:** `DefaultRuntimeFaultCapturePolicy` *(type full name + message; stack trace only when `RuntimeFaultCaptureOptions.CaptureStackTrace` is enabled)*.

### `IWorkflowSchedulerPoisonStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns poison/retry records for crashed scheduler work items in a runtime composition).
- **Signature:** `RecordAsync(RuntimeSchedulerPoisonRecord record, ...)`, `FindAsync(workflowExecutionId, workItemId, ...)`, `ListAsync(workflowExecutionId, ...)`.
- **Usage:** when a scheduler work handler crashes, the drainer captures the fault, consults `IRuntimeDomainRetryPolicy`, and records a `RuntimeSchedulerPoisonRecord` here instead of dropping the dequeued item (RT-1 gap b). Disposition is `Poisoned` (terminal, no retry) or `RetryScheduled` (carries `NextRetryAt`). The default `NoopRuntimeDomainRetryPolicy` yields `Poisoned` — a safe, non-looping baseline.
- **Default implementation:** `InMemoryWorkflowSchedulerPoisonStore` *(intra-domain default; a durable poison store is future provider work — see follow-ups below)*.
- **Follow-ups (W1 → W2):** `RetryNow` re-enqueues immediately through the `IWorkflowSchedulerWorkQueue` public contract and also records `RetryScheduled`; `RetryAfter(delay)` records `RetryScheduled` with `NextRetryAt` but does **not** re-enqueue — re-driving delayed retries is left to the durable resumption pump (`RuntimeResumptionPumpTask`; see [`docs/runtime-durable-resumption.md`](../../../../../docs/runtime-durable-resumption.md)), which avoids ignoring the delay / hot-looping. A durable poison store and the delayed re-drive are explicit follow-ups, not W1 scope.

### `IRuntimeVolatileWaitPolicy` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one policy decides whether in-memory volatile waits are allowed in a runtime composition).
- **Signature:** `Decide(RuntimeVolatileWaitPolicyRequest request)`.
- **Usage:** evaluates host support, requested duration, requested host-shutdown behavior, requested cancellation behavior, and durable fallback posture. Volatile waits remain scheduler continuation state and are not durable bookmark resume state.
- **Default implementation:** `DefaultRuntimeVolatileWaitPolicy` *(allows only when the host explicitly supports in-memory continuation; host-specific providers can replace this)*.

### `IRuntimeGeneratorEmissionScheduler` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one scheduler adapter turns in-workflow generator emissions into ordered runtime scheduler work).
- **Signature:** `ScheduleAsync(RuntimeGeneratorEmissionScheduleRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** enqueues `WorkflowExecutionCommandKind.GeneratedEvent` work through `IWorkflowSchedulerWorkQueue` using deterministic IDs derived from workflow execution and generated event identity. Generator registrations and generated-event lanes remain scheduler state; this is not a trigger provider, generator execution loop, or separate generator-state store.
- **Default implementation:** `RuntimeGeneratorEmissionScheduler` *(single-node scheduler queue adapter for the current runtime slice)*.

### `IControlPlaneStateStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns administrative control-plane state for a runtime composition).
- **Signature:** `SaveAsync(ControlPlaneState state, ...)`, `FindAsync(string controlPlaneStateId, ...)`, `ListForWorkflowExecutionAsync(string workflowExecutionId, ...)`, `ListAllAsync(...)`.
- **Usage:** stores pause/unpause administrative holds outside workflow continuation state. Durable or distributed control-plane providers can replace the default without changing workflow execution state contracts.
- **Default implementation:** `InMemoryControlPlaneStateStore` *(single-node in-memory default for the current runtime slice)*.

### `IRuntimePauseDecisionProvider` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one provider decides whether runtime scheduler work may advance through named pause boundaries).
- **Signature:** `DecideAsync(RuntimePauseDecisionRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** evaluates active control-plane holds at safe runtime boundaries and returns `SchedulerPauseDecision`. Pause/unpause remain control-plane operations and are not durable suspend/resume or volatile continue semantics.
- **Default implementation:** `RuntimePauseDecisionProvider` *(matches effective holds by workflow/activity/generator/ingress/worker/host target and picks oldest hold then hold ID deterministically)*.

### `IBookmarkResumeResolver` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one resolver owns durable bookmark-to-artifact resume resolution for a runtime composition).
- **Signature:** `Resolve(BookmarkResumeRequest request)`.
- **Usage:** maps `BookmarkState.ResumeTargetId` through the pinned `WorkflowExecutable.ResumeTargets` table and returns the executable node plus runtime resume target. It does not load artifacts, invoke activity handlers, or implement the bookmark store.
- **Default implementation:** `BookmarkResumeResolver` *(intra-domain default)*.

### `IBookmarkStimulusLookup` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one lookup surface finds bookmark continuation state for a workflow execution and stimulus identity).
- **Signature:** `FindAsync(BookmarkStimulusLookupRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** matches non-expired `BookmarkState` records by workflow execution ID, stimulus type, and stimulus hash. Ambiguous matches are rejected instead of guessed. Durable providers can replace the default with indexed lookup.
- **Default implementation:** `BookmarkStimulusLookup` *(list-based default over `IBookmarkStateStore` for the current in-memory slice)*.

### `IBookmarkResumeDispatcher` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one dispatcher turns matched bookmark stimuli into workflow execution mailbox commands).
- **Signature:** `DispatchAsync(BookmarkResumeDispatchRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** uses `IBookmarkStimulusLookup`, workflow execution state, the pinned executable artifact, and `IBookmarkResumeResolver` to enqueue a `ResumeBookmark` command through `IWorkflowExecutionAgentProvider`. The command payload carries `ResumeTargetId`, not C# callback method names. It does not consume bookmarks or invoke activity resume handlers.
- **Default implementation:** `BookmarkResumeDispatcher`.

### `IRuntimeActivityOutputReader` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Query surface (read-only active-scope output lookup used by resolvers and resolution contexts).
- **Signature:** `TryGet(ActiveActivityOutputKey key, out ActiveActivityOutput output)`, `GetActivityOutputs(...)`.
- **Usage:** exposes active execution outputs without granting mutation rights to consumers that only materialize runtime input bindings.
- **Default implementation:** `InMemoryRuntimeActivityOutputRegister` *(through `IRuntimeActivityOutputRegister`)*.

### `IRuntimeActivityOutputRegister` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one active-scope output register owns execution-local activity output lookup for a runtime composition).
- **Signature:** inherits `IRuntimeActivityOutputReader`; adds `Set(ActiveActivityOutput output)`, `ClearActivityOutputs(...)`.
- **Usage:** stores activity outputs by `WorkflowExecutionId`, `ActivityExecutionId`, and output name while they remain in active execution scope. This is not durable continuation state.
- **Default implementation:** `InMemoryRuntimeActivityOutputRegister` *(intra-domain default, contract/default for current slice)*.

### `IRuntimeInputBindingResolver` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one resolver owns runtime input binding materialization rules for a runtime composition).
- **Signature:** `Resolve(RuntimeInputBinding binding, RuntimeInputBindingResolutionContext context)`.
- **Usage:** resolves literal, reference, durable-value, and active activity-output bindings without loading authored data links or history snapshots. Expression bindings remain declarations for expression middleware.
- **Default implementation:** `RuntimeInputBindingResolver` *(intra-domain default)*.

### `IRuntimeInputBindingValidator` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one validator owns executable binding diagnostics for a runtime composition).
- **Signature:** `Validate(RuntimeInputBinding binding, RuntimeInputBindingValidationContext context)`.
- **Usage:** reports artifact/build diagnostics for output references that cross suspension boundaries or are ambiguous in loop/parallel scopes.
- **Default implementation:** `RuntimeInputBindingValidator` *(intra-domain default)*.

### `IRuntimeActivityInputMaterializer` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one materializer owns conversion from executable input bindings to activity runtime arguments for a composition).
- **Signature:** `MaterializeInputsAsync(ExecutableNode node, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)` and `MaterializeInputsAsync(ExecutableNode node, RuntimeInputBindingResolutionContext resolutionContext, CancellationToken cancellationToken = default)`.
- **Usage:** constructs activity input arguments and memory values from runtime-owned executable node bindings. Supports literal, durable-value, active-output, and reference bindings (all requiring `typeName` metadata), plus `Expression` bindings — evaluated through the registered `IExpressionEvaluator` (e.g. JavaScript/Liquid) using the resolution context's `ServiceProvider`. Expression evaluation requires a service provider; the literal-only convenience overload passes one through for resume paths. Expressions resolve workflow variables, workflow inputs, and prior activity outputs supplied on `RuntimeInputBindingResolutionContext` (`WorkflowVariables`, `WorkflowInputs`, `ActivityOutputValues`): the materialization-time `IExpressionExecutionContext` implements `IMaterializationExpressionState`, which language pre-processors (e.g. `MaterializationAccessorsPreProcessor`) read to surface `variables`/`input`/`output` accessors without a live workflow execution context.
- **Default implementation:** `RuntimeActivityInputMaterializer`.

### `IMaterializationExpressionState` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Bridge (carries workflow-scoped values from `RuntimeInputBindingResolutionContext` to language pre-processors during activity-input materialization).
- **Signature:** `WorkflowVariables`, `WorkflowInputs`, `ActivityOutputValues` (name → value).
- **Usage:** implemented by the materialization-time `IExpressionExecutionContext` so expression-language pre-processors can resolve variable/input/output references before the activity's real execution context exists. Populate the snapshots on the resolution context from the durable values captured for the execution: `RuntimeInputBindingStateProjection.ProjectWorkflowVariables` / `ProjectWorkflowInputs` / `ProjectActivityOutputValues` rebuild the `variables.*` / `input.*` / `output.*` snapshots. Workflow variables and inputs become durable values via `RuntimeWorkflowStateSeed`, seeded at the `WorkflowStarted` checkpoint and tagged with the `runtime.variableName` / `runtime.inputName` metadata keys. The start entry points (`ExecuteWorkflowRequestHandler`, `StartWorkflowTestRunRequestHandler`) populate the seed's `Variables` with authored workflow variable defaults projected off the compiled executable's root structure (`RuntimeVariableScopeFactory.ProjectDeclaredVariableDefaultsByName`); caller-supplied `Inputs` are a deferred API-surface change (#286).
- **Default implementation:** private materialization context in `RuntimeActivityInputMaterializer`; consumed by `MaterializationAccessorsPreProcessor` *(Elsa.Workflows.Runtime.JavaScript)*.

### `IExecutionExpressionState` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Bridge (carries live execution-time workflow state — identity, inputs, variables, prior outputs — to language pre/post-processors during activity execution). Execution-time counterpart to `IMaterializationExpressionState` (ADR 0030).
- **Signature:** `WorkflowInstanceId`, `CorrelationId`, `WorkflowName`, `WorkflowDefinitionId`, `WorkflowDefinitionVersionId`, `WorkflowDefinitionVersion`, `WorkflowInputs`, `WorkflowVariables`, `ActivityOutputValues`.
- **Usage:** implemented by the execution-time `IExpressionExecutionContext` (`SimpleActivityExecutionContext`) so expression-language pre/post-processors resolve execution-time identity functions, named pascalized accessors, execution-time output accessors, and JavaScript variable write-back **without a DI-registered live workflow execution context** (ADR 0030 D1; retires `IWorkflowExecutionContext`). Populated by `WorkflowInvokeActivitySchedulerWorkHandler`: identity from `WorkflowExecutionState` + the pinned `WorkflowExecutableIdentity`; `WorkflowVariables`/`WorkflowInputs`/`ActivityOutputValues` from the durable-value projections (`RuntimeInputBindingStateProjection`). Variable writes route through the visible `VariableScope` (`IScopedVariableProvider`) and fold into the checkpoint-commit durable-value write-back (`BuildWorkflowScopeWriteBackChanges`) — no second persistence route. A narrow marker, not a general transient-properties bag (ADR 0030 Q3); Design-free (§E2.2/§E2.6).
- **Default implementation:** `SimpleActivityExecutionContext`; consumed by the re-pointed JavaScript pre/post-processors (`WorkflowFunctionsPreProcessor`, `WorkflowInputFunctionsPreProcessor`, `VariableFunctionsPreProcessor`, `ActivityOutputFunctionsPreProcessor`, `CopyVariablesToWorkflowContext`) in *Elsa.Workflows.Runtime.JavaScript*. The `JavaScriptWorkflowsRuntimeFeature` registration is covered by a resolve-and-evaluate guardrail test (ADR 0030 D4).

### `IRuntimePayloadCapturePolicy` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one policy decides which runtime observability payloads may be captured for a runtime composition).
- **Signature:** `Decide(RuntimePayloadCaptureRequest request)`.
- **Usage:** controls whether history, diagnostics, incidents, values, and input/output observations capture no payload, metadata only, or full payload. Continuation state does not read these observability payloads. The default excludes sensitive values and omits workflow/activity input and output snapshots.
- **Default implementation:** `DefaultRuntimePayloadCapturePolicy` *(intra-domain default)*.

### `IWorkflowExecutionAgentProvider` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one provider owns workflow-execution mailbox activation, routing, and passivation for a runtime composition).
- **Signature:** `Capabilities`, `GetAgentAsync(WorkflowExecutionAgentActivationRequest request, CancellationToken cancellationToken = default)`, `PassivateAsync(WorkflowExecutionAgentPassivationRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** provider implementations enforce one active mailbox/agent per `WorkflowExecutionId`. Commands are delivered through `WorkflowExecutionCommandEnvelope`, which carries command identity, workflow execution ID, idempotency key, optional sequence, delivery mode, and metadata. Actor frameworks are provider choices; checkpoint state remains the source of truth.
- **Default implementation:** `InProcessWorkflowExecutionAgentProvider` *(single-node actor-like mailbox; no distributed placement or actor framework dependency)*.

### `IRuntimeExecutionIdGenerator` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one generator owns runtime command-dispatch IDs for a runtime composition).
- **Signature:** `NewWorkflowExecutionId()`, `NewWorkflowExecutionCommandId()`, `NewWorkflowExecutionCommandEnvelopeId()`, `NewActivityExecutionId()`.
- **Usage:** provides runtime-owned identifiers for workflow execution start dispatch and concrete activity executions without leaking API, persistence, or provider-specific identity generation into command construction.
- **Default implementation:** `GuidRuntimeExecutionIdGenerator`.

### `IWorkflowExecutionStartDispatcher` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one dispatcher owns conversion from executable artifact start requests into workflow execution agent commands).
- **Signature:** `DispatchAsync(WorkflowExecutionStartDispatchRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** loads the runtime-owned executable artifact, pins its exact identity in a `WorkflowExecutionCommandKind.Start` payload, activates the workflow execution agent, and enqueues the command envelope. It does not execute activities inline.
- **Default implementation:** `WorkflowExecutionStartDispatcher`.

### `IWorkflowExecutionCommandProcessor` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one processor decides what an accepted workflow-execution command does inside the active agent mailbox).
- **Signature:** `ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)`.
- **Usage:** invoked by the in-process agent after dispatch metadata has been accepted and before the idempotency key is marked processed. The processor runs under the agent mailbox's single-writer boundary.
- **Default implementation:** `WorkflowSchedulerCommandProcessor` *(records accepted commands as scheduler work, applies the scheduler drain policy, then delegates command-triggered draining to `IWorkflowExecutionDrainCoordinator`; activity execution remains handler/provider behavior, not command acceptance behavior)*.

### `IWorkflowExecutionDrainCoordinator` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one coordinator owns command-triggered workflow execution drain orchestration for a runtime composition).
- **Signature:** `DrainAsync(WorkflowExecutionCommandEnvelope envelope, RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** bridges the accepted command boundary to scheduler draining after scheduler work is recorded and the drain policy requests immediate advancement. The default coordinator drains scheduler work, processes deliverable `RuntimePostCommitIntentKinds.EnqueueSchedulerWork` outbox items for the same workflow execution, and repeats scheduler draining until scheduler-intent delivery quiesces, a pause/fault stops scheduler draining, or the bounded cycle guard is reached. Checkpoint commit remains the durability boundary: commits record post-commit work, and the coordinator only delivers it after the commit path succeeds. `WorkflowExecutionDrainCoordinatorOptions` names the cycle and outbox batch limits; cycle-cap exhaustion throws `WorkflowExecutionDrainCycleLimitExceededException`.
- **Default implementation:** `WorkflowExecutionDrainCoordinator`.

### `IRuntimeExecutionOwnershipService` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one service owns single-writer fencing — lease acquisition, heartbeat, release, and stale-writer rejection — for a runtime composition).
- **Signature:** `AcquireAsync(string workflowExecutionId, ...)`, `HeartbeatAsync(RuntimeExecutionLease lease, ...)`, `ReleaseAsync(RuntimeExecutionLease lease, ...)`, `EnsureCurrentAsync(string workflowExecutionId, long fencingToken, ...)`.
- **Usage:** enforces RT-2 single-writer ownership. `WorkflowExecutionDrainCoordinator` acquires a lease at the start of a drain, pushes it onto `IRuntimeExecutionOwnershipContextAccessor`, and releases it in a `finally` (so a crash leaves the lease persisted for the recovery scanner to detect — closing W2's post-dequeue/pre-commit window). `RuntimeCheckpointCommitter` calls `EnsureCurrentAsync` at the single checkpoint-commit funnel and throws `RuntimeStaleFencingTokenException` when the presented fencing token is not the current one (equality is the only pass; tokens are strictly monotonic and never reused across release). Ownership state is backed by `IOperationalStateStore`; the lease `ExpiresAt` reuses the recovery scanner's existing lease-timeout honoring rather than a parallel knob.
- **Default implementation:** `RuntimeExecutionOwnershipService` *(operational-state-backed, monotonic fencing token preserved across release)*.

### `IRuntimeExecutionOwnershipContextAccessor` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one accessor owns the ambient current-lease scope for a runtime composition).
- **Signature:** `RuntimeExecutionLease? Current { get; }`, `Push(RuntimeExecutionLease lease) : IDisposable`.
- **Usage:** an AsyncLocal push/pop scope (mirroring `AsyncLocalWorkflowExecutionAmbientServicesAccessor`) that carries the active drain's lease from `IWorkflowExecutionDrainCoordinator` down to `RuntimeCheckpointCommitter` without threading it through every command/handler signature. It is a runtime-internal ambient accessor, not the ADR-0029-discouraged pipeline-context ambient.
- **Default implementation:** `AsyncLocalRuntimeExecutionOwnershipContextAccessor`.

### `IWorkflowSchedulerWorkQueue` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one queue owns recorded scheduler work for a runtime composition).
- **Signature:** `EnqueueAsync(RuntimeSchedulerWorkItem workItem, ...)`, `ListAsync(RuntimeSchedulerWorkQuery query, ...)`, `DequeueAsync(string workflowExecutionId, ...)`, `ListPendingWorkflowExecutionIdsAsync(int limit, ...)`.
- **Usage:** stores scheduler work by `WorkflowExecutionId` after an execution agent accepts a command envelope. The queue preserves per-workflow insertion order and is idempotent by scheduler work item ID within each workflow execution. `ListPendingWorkflowExecutionIdsAsync` returns the distinct execution ids with queued work, up to `limit`, so a resumption sweep can discover durable backlog after a restart when nothing else knows the interrupted execution ids. Draining and activity execution remain separate scheduler behavior.
- **Default implementation:** `InMemoryWorkflowSchedulerWorkQueue` *(single-node in-memory default for the current runtime slice)*. `GroundworkWorkflowSchedulerWorkQueue` *(durable `IDocumentStore`-backed bridge; swapped in by `AddGroundworkRuntimeStores` so scheduler work survives a process crash — see [docs/runtime-durable-resumption.md](../../../../../docs/runtime-durable-resumption.md))*.

### `IDurableTimerStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns durable timers for a runtime composition).
- **Signature:** `SaveAsync(DurableTimer timer, ...)`, `FindAsync(string workflowExecutionId, string timerId, ...)`, `DeleteAsync(string workflowExecutionId, string timerId, ...)`, `ListDueAsync(DateTimeOffset now, int limit, ...)`.
- **Usage:** persists `DurableTimer` records (a due-time-indexed document kind, `durableTimer`) keyed by `(WorkflowExecutionId, TimerId)`. `SaveAsync` is a deterministic upsert so a pre-commit crash re-executing the owning activity re-writes the same timer rather than duplicating it. `ListDueAsync` returns timers with `DueTime <= now`, ordered by `(DueTime, TimerId)`, capped at `limit`; the durable timer pump (`DurableTimerPumpTask`) drains this and fires each due timer through `IBookmarkResumeDispatcher`. The pump owns idempotency: it deletes a timer on `Dispatched`/`Duplicate` (the resume is durably enqueued into `IWorkflowSchedulerWorkQueue` before the dispatcher returns — see `WorkflowSchedulerCommandProcessor.ProcessAsync` — so deletion cannot lose the resume), and treats a past-grace `NotFound` as an already-consumed bookmark.
- **Default implementation:** `InMemoryDurableTimerStore` *(single-node in-memory default; Delay works but is **not** restart-durable without a durable store)*. `GroundworkDurableTimerStore` *(durable `IDocumentStore`-backed bridge; swapped in by `AddGroundworkRuntimeStores`)*.
- **Follow-ups (W8):**
  - **Native due-time range index.** Groundwork is equality-index only this wave, so `ListDueAsync` loads the whole timer partition (equality query on a constant collection key) and filters/orders `DueTime` in memory. `MaxTimersPerTick` bounds the *dispatch* burst, not the *load*. A native range/due-time index in Groundwork is the scale follow-up.
  - **Timer/Cron start triggers** (recurring schedules that *start* a workflow) are OUT this wave — they depend on W7's trigger/stimulus index. The `durableTimer` kind is shaped so a `start-trigger` timer variant can plug in later without a schema change.
  - **Atomic timer registration (Option B).** Delay registers its timer activity-side, strictly before the bookmark (so "bookmark committed, timer missing" is structurally excluded). A fully atomic timer==bookmark lifecycle via a post-commit `RegisterDurableTimer` intent (`IRuntimePostCommitIntentDispatcher`) is the alternative if orphaned timers ever prove noisy.

### `IDurableTimerScheduler` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one scheduler owns durable-timer registration for a runtime composition).
- **Signature:** `ScheduleAsync(DurableTimer timer, ...)`.
- **Usage:** thin activity-facing wrapper over `IDurableTimerStore.SaveAsync`. The `Delay` activity builds the `DurableTimer` (deriving `DueTime` from the injected `TimeProvider`) and calls this to write its timer before creating the matching bookmark.
- **Default implementation:** `DurableTimerScheduler` *(registered by the `WorkflowsRuntimeScheduling` feature)*.

### `IActivityExecutionStateStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns split continuation state for concrete activity executions in a runtime composition).
- **Signature:** `SaveAsync(ActivityExecutionState state, ...)`, `FindAsync(string workflowExecutionId, string activityExecutionId, ...)`, `ListAsync(string workflowExecutionId, ...)`.
- **Usage:** stores `ActivityExecutionState` keyed by `WorkflowExecutionId` and durable `ActivityExecutionId`. `SaveAsync` is an upsert for future lifecycle transitions. The default scheduler uses it to record `Scheduled` state when `ScheduleActivity` work is drained, but it does not overwrite an existing activity execution state when replaying the same schedule work. It does not invoke activities, store authored workflow documents, or project diagnostics/history.
- **Default implementation:** `InMemoryActivityExecutionStateStore` *(single-node in-memory default for the current runtime slice)*.

### `IActivityExecutionInspectionStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement query surface (one store owns committed inspection projections for concrete activity executions in a runtime composition).
- **Signature:** `FindAsync(string workflowExecutionId, string activityExecutionId, ...)`, `ListSummariesAsync(string workflowExecutionId, ...)`.
- **Usage:** reads runtime-owned inspection evidence keyed by concrete activity execution identity. Consumers use this store for lightweight per-instance activity execution summaries and selected execution detail without loading authored workflow documents.
- **Default implementation:** `InMemoryActivityExecutionInspectionStore` *(single-node in-memory default for the current runtime slice)*.
- **Known provider implementations:** `Elsa.Persistence.Groundwork` — `GroundworkActivityExecutionInspectionStore` *(cross-domain persistence provider replacement)*.

### `IActivityExecutionInspectionWriter` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement command surface (one writer owns committed inspection projection upserts for concrete activity executions in a runtime composition).
- **Signature:** `SaveAsync(ActivityExecutionInspectionProjection projection, ...)`.
- **Usage:** writes runtime-owned inspection evidence from accepted checkpoint commits through the activity-execution-inspection lane, so inspection evidence does not get ahead of lifecycle state. The command surface is split from `IActivityExecutionInspectionStore` to preserve command/query separation.
- **Default implementation:** `InMemoryActivityExecutionInspectionStore` *(single-node in-memory default for the current runtime slice)*.
- **Known provider implementations:** `Elsa.Persistence.Groundwork` — `GroundworkActivityExecutionInspectionStore` *(cross-domain persistence provider replacement)*.

### `IRuntimeActivityExecutionInspectionAccumulator` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one accumulator assembles checkpoint-scoped activity execution inspection projections for a runtime composition).
- **Signature:** `BuildProjectionAsync(ActivityExecutionState state, string checkpointId, DateTimeOffset committedAt, ...)`.
- **Usage:** merges lifecycle state with committed outcome, bookmark, incident, value-snapshot, provenance, checkpoint, and metadata evidence before the checkpoint writer persists the inspection projection. Provider implementations can replace this when a runtime composition needs different projection merge/enrichment behavior while preserving the checkpoint lane contract.
- **Default implementation:** `RuntimeActivityExecutionInspectionAccumulator` *(intra-domain default)*.

### `IBookmarkStateStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns split continuation state for durable bookmark resume handles in a runtime composition).
- **Signature:** `SaveAsync(BookmarkState state, ...)`, `DeleteAsync(string workflowExecutionId, string bookmarkId, ...)`, `FindAsync(string workflowExecutionId, string bookmarkId, ...)`, `ListAsync(string workflowExecutionId, ...)`.
- **Usage:** stores `BookmarkState` keyed by `WorkflowExecutionId` and `BookmarkId`. The in-memory checkpoint writer projects bookmark upserts and deletes from accepted checkpoint commits into this store. Stimulus lookup indexes and resume dispatch behavior are separate runtime surfaces and are not part of this store boundary.
- **Default implementation:** `InMemoryBookmarkStateStore` *(single-node in-memory default for the current runtime slice)*.

### `IDurableValueStateStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns split continuation state for declared durable runtime values in a runtime composition).
- **Signature:** `SaveAsync(DurableValueState state, ...)`, `DeleteAsync(string workflowExecutionId, string durableValueId, ...)`, `FindAsync(string workflowExecutionId, string durableValueId, ...)`, `ListAsync(string workflowExecutionId, ...)`.
- **Usage:** stores `DurableValueState` keyed by `WorkflowExecutionId` and `DurableValueId`. The in-memory checkpoint writer projects durable value upserts and deletes from accepted checkpoint commits into this store. Storage drivers, capture middleware, and history snapshots are separate runtime surfaces.
- **Default implementation:** `InMemoryDurableValueStateStore` *(single-node in-memory default for the current runtime slice)*.

### `IIncidentStateStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns split continuation state for execution-affecting incidents in a runtime composition).
- **Signature:** `TryAddAsync(IncidentState state, ...)`, `SaveAsync(IncidentState state, ...)`, `FindAsync(string workflowExecutionId, string incidentId, ...)`, `ListAsync(string workflowExecutionId, ...)`, `ListBlockingAsync(string workflowExecutionId, ...)`.
- **Usage:** stores `IncidentState` keyed by `WorkflowExecutionId` and `IncidentId`. The in-memory checkpoint writer projects incident appends as insert-only changes and incident upserts as replacements from accepted checkpoint commits into this store. Incident history projections, diagnostic payloads, retry, compensation, and intervention behavior are separate runtime surfaces.
- **Default implementation:** `InMemoryIncidentStateStore` *(single-node in-memory default for the current runtime slice)*.

### `IOperationalStateStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns split continuation state for runtime operational coordination in a runtime composition).
- **Signature:** `SaveAsync(OperationalState state, ...)`, `FindAsync(string workflowExecutionId, string operationalStateId, ...)`, `ListAsync(string workflowExecutionId, ...)`, `ListAllAsync(...)`.
- **Usage:** stores `OperationalState` keyed by `WorkflowExecutionId` and `OperationalStateId`. The in-memory checkpoint writer projects operational state upserts from accepted checkpoint commits into this store. Recovery scanning, outbox delivery processing, domain retry, and actor-provider lease enforcement remain separate runtime surfaces.
- **Default implementation:** `InMemoryOperationalStateStore` *(single-node in-memory default for the current runtime slice)*.

### `ISchedulerStateStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns the split scheduler continuation-state snapshot in a runtime composition).
- **Signature:** `SaveAsync(SchedulerState state, ...)`, `FindAsync(string workflowExecutionId, ...)`, `ListAsync(...)`.
- **Usage:** stores `SchedulerState` keyed by `WorkflowExecutionId`. The in-memory checkpoint writer projects scheduler state upserts from accepted checkpoint commits into this store. This is distinct from `IWorkflowSchedulerWorkQueue`, which records accepted scheduler work commands before/driving drains.
- **Default implementation:** `InMemorySchedulerStateStore` *(single-node in-memory default for the current runtime slice)*.

### `IWorkflowSchedulerDrainer` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one drainer owns deterministic dispatch of queued scheduler work for a runtime composition).
- **Signature:** `DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** dequeues scheduler work for one workflow execution and dispatches each work item to an `IWorkflowSchedulerWorkHandler`. The default drainer stops on the first handler fault and returns per-item drain results. It does not execute activities, write checkpoints, or implement retry.
- **Default implementation:** `WorkflowSchedulerDrainer` *(contract-only drain boundary for the current runtime slice)*.

### `IWorkflowSchedulerPauseGate` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one gate evaluates whether queued scheduler work may cross named pause boundaries).
- **Signature:** `EvaluateAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)`.
- **Usage:** maps supported scheduler command kinds to `RuntimePauseDecisionRequest` values and delegates to `IRuntimePauseDecisionProvider`. The default drainer peeks the next queued item, consults this gate, and stops without dequeuing when the decision blocks advancement.
- **Default implementation:** `WorkflowSchedulerPauseGate` *(maps `StartActivity`/`InvokeActivity` to `BeforeActivityExecutionStart` and `GeneratedEvent` to `BeforeGeneratorEmission`)*.

### `IWorkflowSchedulerDrainPolicy` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one policy decides whether recorded scheduler work triggers a drain in the runtime composition).
- **Signature:** `CreateDrainRequest(WorkflowExecutionCommandEnvelope envelope, RuntimeSchedulerWorkItem workItem)`.
- **Usage:** command processing records scheduler work first, then asks this policy whether to drain. Returning `null` defers draining.
- **Default implementation:** `ImmediateWorkflowSchedulerDrainPolicy`.

### `IWorkflowSchedulerDrainObserver` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contributor (observers consume coordinated drain results produced by workflow execution draining).
- **Signature:** `OnDrainedAsync(WorkflowExecutionCommandEnvelope envelope, RuntimeSchedulerDrainResult result, CancellationToken cancellationToken = default)`.
- **Usage:** modules can project one command-triggered coordinated drain outcome into diagnostics or future checkpoint/outbox behavior without making history continuation state. Coordinated results aggregate scheduler work item results across scheduler drain passes, include post-commit outbox delivery counts/results, and expose a stop reason such as quiesced, paused, faulted, or outbox delivery failed.
- **Default implementation:** `NoopWorkflowSchedulerDrainObserver`.
- **Known implementations (shipped):** `BlockingIncidentWorkflowFaultObserver` *(RT-1a/RT-5 — after a drain turn, if the workflow has one or more blocking incidents and is still non-terminal, commits a `WorkflowFaulted` checkpoint that transitions the workflow to `Faulted`; registered additively via `TryAddEnumerable`)*.

### `IWorkflowSchedulerWorkHandler` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contributor (handlers consume drained scheduler work items).
- **Signature:** `Name`, `CanHandle(RuntimeSchedulerWorkItem workItem)`, `HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)`.
- **Usage:** modules can handle specific scheduler command kinds without replacing the drainer. The drainer evaluates ordinary handlers before fallback handlers.
- **Default implementations:** `WorkflowStartSchedulerWorkHandler` *(turns `Start` work into `ScheduleActivity` work for executable start nodes)*, `WorkflowScheduleActivitySchedulerWorkHandler` *(records `Scheduled` `ActivityExecutionState` and queues `StartActivity` work for one executable node)*, `WorkflowStartActivitySchedulerWorkHandler` *(transitions scheduled activity state to `Running` and queues `InvokeActivity` work)*, `WorkflowCompleteActivitySchedulerWorkHandler` *(drains deterministic activity completion work)*, `WorkflowCheckpointSchedulerWorkHandler` *(commits named checkpoint scheduler work through `RuntimeCheckpointCommitter`)*, `MissingActivityInvocationSchedulerWorkHandler` *(fallback that faults `InvokeActivity` when no provider is composed)*, `MissingBookmarkResumeSchedulerWorkHandler` *(fallback that faults `ResumeBookmark` when no bookmark resume provider is composed)*, `MissingGeneratedEventSchedulerWorkHandler` *(fallback that faults `GeneratedEvent` when no generated-event provider is composed)*, and `NoopWorkflowSchedulerWorkHandler` *(fallback that acknowledges drained work that has no required provider-specific handler)*.

### `IFallbackWorkflowSchedulerWorkHandler` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contributor marker (handlers consume drained scheduler work items only after ordinary handlers decline them).
- **Signature:** inherits `IWorkflowSchedulerWorkHandler`.
- **Usage:** modules can register catch-all scheduler work handlers without becoming priority handlers. The default drainer evaluates these handlers after ordinary `IWorkflowSchedulerWorkHandler` registrations.
- **Default implementation:** `NoopWorkflowSchedulerWorkHandler`.

### `IWorkflowExecutableStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns runtime executable artifact lookup for a runtime composition).
- **Signature:** `SaveAsync(WorkflowExecutable executable, ...)`, `FindAsync(string artifactId, ...)`, `ListAsync(...)`.
- **Usage:** stores and retrieves runtime-owned `WorkflowExecutable` artifacts. Publishing writes artifacts through this contract; Runtime execution reads artifacts through this contract and does not load Design-owned workflow state.
- **Default implementation:** `InMemoryWorkflowExecutableStore` *(intra-domain demo default for the vertical slice; durable persistence remains future provider work)*.

## Implementable contributor interfaces

### `IWorkflowRuntimeMiddleware` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contributor (workflow runtime pipeline step).
- **Signature:** `InvokeAsync(WorkflowRuntimePipelineContext context, WorkflowRuntimeMiddlewareDelegate next)`.
- **Register:** via `WorkflowRuntimePipelineBuilder.Use<TMiddleware>(slotName, order, name)`.
- **Usage:** registers workflow execution middleware into stable slots from `RuntimeWorkflowPipelineSlots`. Plans are inspectable through `BuildPlan()`.
- **Known implementations (shipped):** no-op built-in placeholders for load state, scheduling, checkpoint, and post-commit.

### `IActivityRuntimeMiddleware` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contributor (activity runtime pipeline step).
- **Signature:** `InvokeAsync(ActivityRuntimePipelineContext context, ActivityRuntimeMiddlewareDelegate next)`.
- **Register:** via `ActivityRuntimePipelineBuilder.Use<TMiddleware>(slotName, order, name)`.
- **Usage:** registers activity execution middleware into stable slots from `RuntimeActivityPipelineSlots`. Plans are inspectable through `BuildPlan()`.
- **Known implementations (shipped):** no-op built-in placeholders for load state, input evaluation, invoke, output capture, scheduling, checkpoint, and post-commit.

### `ResumeTargetAttribute` *(Core — `Elsa.Activities.Runtime.Core`)*
- **Kind:** Declaration surface (activity author contract).
- **Signature:** `[ResumeTarget("stable-resume-target-id")]` on an activity handler method.
- **Usage:** declares the stable resume target ID that compile/publish can place into `WorkflowExecutable.ResumeTargets`. Durable bookmarks store the ID, not the C# method name.
- **Not a runtime callback store** — handler method names and delegates are implementation details and are not persisted in `BookmarkState`.
- **Compiler indexing (W8):** `WorkflowExecutableCompiler` now reflects `[ResumeTarget]` methods off each node's resolved activity type and indexes them into `WorkflowExecutable.ResumeTargets` (previously always empty). `Delay` is the first suspending activity to exercise this. The map is keyed by the attribute's resume-target ID, so duplicate IDs across nodes fail compilation loudly.
- **Follow-up (W8) — node-scoped resume targets.** Because the key is the attribute ID (matching how the resume resolver and the create-bookmark handler already match), only **one instance** of a given resume-target activity is supported per workflow this wave (two `Delay`s in one workflow fail compilation). Node-scoped resume-target IDs (keyed by `ExecutableNodeId` + attribute ID) are the follow-up to lift this, and require a matching change in the resume resolver.

### `ISignalHandler` *(Core — `Elsa.Activities.Runtime.Core`)*
- **Kind:** Contributor (receives a signal and acts — push pattern).
- **Signature:** `ValueTask ReceiveSignalAsync(object signal, SignalContext context);`
- **Usage:** implement on activity classes to receive signals sent to the workflow. `ActivityBase` exposes `ReceiveSignalAsync` which dispatches to the activity's `ISignalHandler` implementation.
- **Not a fan-in aggregator** — each activity implements this directly; there is no aggregating event handler. Signals are dispatched to activities in the workflow graph, not via the DI container.
- **Sub-interface:** `IBehavior : ISignalHandler` — for behaviour objects composable onto activities.

**Known implementations (shipped):**
- Activity classes that override `ReceiveSignalAsync` in the codebase.

### `IActivityCompletionHandler` *(Core — `Elsa.Activities.Runtime.Core`)*
- **Kind:** Overridable single-impl (one handler expected at a time, injected by DI).
- **Signature:** `CompleteActivityAsync(IActivityExecutionContext context)`, `CompleteActivityAsync(IActivityExecutionContext context, object result)`, `CompleteActivityAsync(IActivityExecutionContext context, IEnumerable<string> outcomes)`, `CompleteActivityAsync(IActivityExecutionContext context, IEnumerable<string> outcomes, object result)`.
- **Register:** `services.Replace(ServiceDescriptor.Scoped<IActivityCompletionHandler, MyHandler>())` — single-impl; a replacement steps aside the previous one.
- **Consumed by:** `ActivityBase.CompleteAsync` — resolves `IActivityCompletionHandler` from the execution context's service provider.

**Known implementations (shipped):**
- `Elsa.Workflows.Runtime.JavaScript` — `ActivityCompletionHandler` *(cross-domain — test implementation for JS-context activity completion)*

---

## Cross-references

- HTTP endpoint behaviour overrides: [`Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md`](../Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.2 + §2.22.1.
