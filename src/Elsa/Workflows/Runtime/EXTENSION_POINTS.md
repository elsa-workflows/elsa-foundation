# Extension points — Workflows.Runtime domain

The canonical per-domain catalog (framework §2.22.1), anchored at the
`Elsa.Workflows.Runtime` composition root. Contracts remain in `Elsa.Workflows.Runtime.Core`;
provider/default implementations may live in sibling runtime projects. Runtime execution
contracts are Design-free and operate on runtime-owned executable artifacts and execution state.

> **ADR 0033 split.** Since the contracts/engine split, `Elsa.Workflows.Runtime.Core` holds only the
> contracts, models, constants, pipeline contract surface (middleware attribute/bases/placeholders,
> `RuntimePipelinePlanBuilder`), and validators. Unless an entry states otherwise, every **default
> implementation** named in this catalog now lives in this `Elsa.Workflows.Runtime` implementation
> package (moved types keep their
> `Elsa.Workflows.Runtime.Core.*` namespaces). Replacing a default still works the same way: register
> your implementation against the `.Core` contract; the engine package's registrations all use
> `TryAdd*`.

## Documented ADR 0033 deviations hosted at the composition root

Two contract-shaped surfaces live in the engine package rather than `.Core`, deliberately:

- **`IRuntimeCoalescingSessionAccessor` + `IRuntimeCoalescingDrainScopeFactory`** (namespace
  `Elsa.Workflows.Runtime.Core.Contracts`) expose the concrete `RuntimeCoalescingSession` engine
  working state. Their only consumers are the opt-in coalescing composition in Runtime.Api and its
  tests.
- **`ActivityRuntimePipelineBuilder` + `WorkflowRuntimePipelineBuilder`** (namespace
  `Elsa.Workflows.Runtime.Core.Builders`) bake concrete engine middleware and the concrete
  `RuntimeCheckpointCommitter` into their default plans. The declarative slot machinery remains in
  `.Core`, so third-party middleware authors do not need the engine package at compile time.

## Semantic guard

The ADR 0033 `RuntimeCoreEngineShapeGuardTests` architecture guard prevents concrete engine-role
types (`*Service`, `*Handler`, `*Dispatcher`, `*Drainer`, `*Orchestrator`, `*Materializer`,
`*Committer`, `*Scheduler`, `*Router`, `*Pipeline`, `*Session`, `*Scanner`, `*Processor`, and
`InMemory*`) from moving back into the `.Core` assembly.

---

## Overridable replacement contracts

### `IWorkflowDispatchStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one provider owns first-class workflow-dispatch lifecycle records).
- **Signature:** save/find plus list by parent workflow execution ID.
- **Usage:** `RuntimeCheckpointStateChangeSet.WorkflowDispatches` is applied atomically with activity state and post-commit outbox work. Built-in in-memory and Groundwork providers project it through their workflow-dispatch stores.
- **Safety:** operational records contain safe input descriptors, never raw child input values. Raw values exist only in the protected child-start intent payload required for delivery.

### `IWorkflowDispatchAdmissionStore` / `IWorkflowDispatchCancellationStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Additive provider capabilities over the configured `IWorkflowDispatchStore`.
- **Signature:** `TryAdmitAsync(dispatchId, admittedAt, ...)` conditionally advances Pending to Started; `ApplyCancellationAsync(request, ...)` resolves a replay-stable parent cancellation request against current admission state.
- **Usage:** DispatchWorkflow linearizes external child start against parent cancellation. Cancellation requests are carried in `RuntimeCheckpointStateChangeSet.WorkflowDispatchCancellations` and resolved in the same provider transaction as the parent Cancelled state and child-cancel outbox item.
- **Safety:** cancellation wins by producing a Cancelled-before-admission marker that suppresses later start; admission wins by preserving Started and adding cancellation-requested responsibility. Terminal state never regresses. DispatchWorkflow readiness fails when either capability is absent.
- **Default implementations:** `InMemoryWorkflowDispatchStore` and Groundwork's scoped `GroundworkWorkflowDispatchStore`.

### `IWorkflowTestScopeStore` / `IWorkflowTestScopeAdmissionStore` / `IWorkflowTestScopeCleanupStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Single-provider replacement family for finite test-run scope lifecycle, admission fencing, and detached cleanup.
- **Usage:** a test-run root carries one immutable scope snapshot through checkpoints and DispatchWorkflow descendants. Root/new-child checkpoint admission validates Open state; direct cleanup applies only to fire-and-forget dispatches, while waited children retain the production parent-cancellation contract.
- **Safety:** the guarded provider claim rejects conflicting durable providers in either composition order. Provider transactions serialize scope close against detached child admission and commit a Started child's cancellation marker with its deterministic cancel outbox responsibility.
- **Defaults:** `InMemoryWorkflowTestScopeStore`; Groundwork replaces the complete family.

### `IWorkflowExecutionStateStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one provider owns retained workflow-execution state and its executable-retention projection).
- **Signature:** in addition to save/find/list, `ListPinnedExecutableArtifactIdsAsync(...)` returns the distinct artifact IDs pinned by every retained execution status, and `DeleteAsync(workflowExecutionId, ...)` removes an execution under the host's retention policy.
- **Usage:** workflow-execution records are durable executable-retention roots. Completion or fault does not release an artifact; only deletion of the retained execution does. Providers must answer the distinct-root query without materializing every full workflow-execution document and must keep the projection consistent with save/delete.
- **Default implementation:** `InMemoryWorkflowExecutionStateStore`; durable persistence providers such as Groundwork replace it.

### `IWorkflowExecutableReferenceGarbageCollector` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one collector owns physical executable-artifact reclamation for a runtime composition).
- **Signature:** `SweepAsync(CancellationToken cancellationToken = default)`.
- **Usage:** an artifact is eligible only when it is outside creation/staging grace and absent from the transitive dependency closure of both root sets: live source references and retained workflow executions. Root writers acquire provider-backed leases for the full validated closure before committing either root. The collector first acquires a conditional deletion guard, then recomputes closure reachability, and only that matching guard may delete; leases and guards use provider CAS so the check/delete boundary is safe across hosts. Root-query, graph-integrity, or guard failures retain the artifact for a later sweep.
- **Default implementation:** `WorkflowExecutableReferenceGarbageCollector`; registered by the Runtime composition root. The opt-in `WorkflowsRuntimeReferenceGarbageCollection` feature schedules it and exposes cadence/grace policy.

### `IWorkflowExecutableRootWriteLeaseManager` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one coordinator scopes the provider-backed lease required to establish an executable-retention root).
- **Signature:** `ExecuteAsync(rootIdentity, leaseId, write, ...)` runs the durable write while acquiring, renewing, and finally releasing sorted leases for the exact root and dependency closure. The original artifact-ID overload remains for source compatibility and single-artifact writes.
- **Usage:** canonical publication, test-run, and workflow-execution checkpoint writers execute their durable root write through this coordinator. Persistence providers implement the underlying lease/guard transitions on `IWorkflowExecutableStore`; custom root writers must use the identity overload and provide closure-wide behavior. Lease loss cancels the write and is surfaced rather than silently reporting an unprotected root. Third-party implementations that rely on the default identity-overload fallback remain compatible but fence only the root until they override it.
- **Default implementation:** `WorkflowExecutableRootWriteLeaseManager`.

### `IWorkflowExecutableInputValidator` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one validator owns declared workflow-input contract enforcement).
- **Signature:** validates complete realized input bags or only statically known publication inputs and returns safe findings plus normalized inputs.
- **Usage:** DispatchWorkflow Design validates knowable literal bindings before publication; Runtime validates the realized bag against the exact pinned child before staging responsibility. Supported literal defaults are materialized during normalization, unknown type aliases fail closed, and findings never retain rejected raw values.
- **Default implementation:** `WorkflowExecutableInputValidator`.

### `IWorkflowExecutableStartPolicy` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (exactly one policy decides whether an otherwise-authorized future executable start may materialize).
- **Signature:** evaluates immutable executable, authority, and typed runtime context and returns an allow or classifiable deny decision.
- **Usage:** evaluated after retained/live authority, input, and dispatch-depth validation and before actor lookup. A denial creates no workflow execution state and does not mutate the executable. Multiple policy registrations fail composition deterministically.
- **Default implementation:** `AllowWorkflowExecutableStartPolicy`.

### `WorkflowExecutableDependencyGraph` *(Runtime implementation service)*
- **Kind:** Shared integrity service used by publication validation, closure leasing, and garbage collection.
- **Usage:** resolves deterministic de-duplicated closures by full artifact ID/hash identity. Missing artifacts, hash mismatches, conflicting identities, and exact-identity cycles are classified failures; same-definition different-artifact edges remain legal.

### `IRuntimeCheckpointPersistencePolicy` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one policy decides how checkpoints flush in a runtime composition).
- **Signature:** `DecideAsync(RuntimeCheckpoint checkpoint, CancellationToken cancellationToken = default)`.
- **Usage:** separates checkpoint semantics from persistence timing. The checkpoint name says what changed; the policy decides immediate, deferred, or skipped flush.
- **Default implementation:** `ImmediateRuntimeCheckpointPersistencePolicy` *(intra-domain default)*.
- **Alternative implementation:** `CoalescingRuntimeCheckpointPersistencePolicy` *(opt-in, W9/E3-6/RT-10)* — burst-coalescing folding of intra-drain checkpoints into one flush at quiescence; enable with `services.AddCoalescingRuntimeCheckpointPersistence()` (see the Coalescing checkpoint persistence section below).

### `IRuntimeCheckpointWriter` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one writer owns persistence of checkpoint envelopes for a runtime composition).
- **Signature:** `WriteAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)`.
- **Usage:** implemented by runtime persistence providers to commit the checkpoint boundary and its atomic state-change envelope.
- **Default implementation:** `InMemoryRuntimeCheckpointWriter` *(single-node in-memory default for the current runtime slice; durable providers replace this)*.

### `IRuntimeCheckpointCommitEnricher` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contributor (zero or more deterministic enrichment phases run before checkpoint fingerprinting and persistence).
- **Signature:** `Order` plus `EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)`.
- **Registration and ordering:** register contributors as `IEnumerable<IRuntimeCheckpointCommitEnricher>`. Lower `Order` values run first; equal values retain registration order. Existing contributors that do not override `Order` remain in the default phase (`0`). Later phases may depend only on state established by documented earlier phases.
- **Default implementations and dependents:** `WorkflowDispatchCheckpointEnricher` projects generic child terminal lifecycle in phase `0`; DispatchWorkflow parent cancellation runs in phase `50`; terminal result/resume enrichment runs in phase `100`. Replacements must be replay-deterministic and must fail closed on conflicting state.

### `IRuntimePostCommitIntentDispatcher` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one dispatcher owns delivery of committed outbound runtime intents for a composition).
- **Signature:** `DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)`.
- **Default implementation:** `RuntimePostCommitIntentDispatcher`, an ordinal keyed aggregate over contributed `IRuntimePostCommitIntentHandler` registrations.
- **Usage:** the outbox processor invokes this replacement contract after checkpoint commit. The default aggregate selects exactly one contributed handler by the persisted intent kind; hosts may still replace the aggregate as a unit.

### `IRuntimePostCommitIntentHandler` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contributor (one handler owns one stable post-commit intent kind).
- **Signature:** `HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)`.
- **Registration:** call `services.AddRuntimePostCommitIntentHandler<THandler>(intentKind)`. The registration argument is the sole kind authority; handlers expose no second kind property that could disagree. The built-in scheduler delivery registers `RuntimeSchedulerPostCommitIntentDispatcher` for `RuntimePostCommitIntentKinds.EnqueueSchedulerWork` through this same surface.
- **Composition rules:** kinds use ordinal comparison. Repeating the same `(intent kind, handler type)` is idempotent. Different handler types claiming one kind fail deterministically with the kind and sorted handler identities; the aggregate defensively repeats that validation when composed.
- **Lifetime and boundary:** the extension registers handlers as scoped defaults, and the aggregate resolves the selected handler from the active outbox-delivery scope. The global resumption sweep processes every deliverable intent kind outside workflow execution actor mailboxes. The per-execution `WorkflowDrainOrchestrator` remains intentionally filtered to scheduler work only.
- **Failure behavior:** handler exceptions and unsupported kinds flow through `RuntimePostCommitOutboxProcessor`; the existing outbox retry policy selects the persisted failed/final state. Neither case is silently acknowledged as delivered.

### `IRuntimePostCommitOutboxStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one provider owns durable post-commit outbox state for a runtime composition).
- **Signature:** `SavePendingAsync(RuntimePostCommitOutboxItem item, ...)`, `GetDeliverableAsync(RuntimePostCommitOutboxQuery query, ...)`, `RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, ...)`.
- **Usage:** stores delivery state for post-commit intents so providers can preserve record, commit, deliver, and mark-delivered ordering.
- **Default implementation:** `InMemoryRuntimePostCommitOutboxStore` *(single-node in-memory default for the current runtime slice; durable providers replace this)*.

### `IPostCommitFailureProjector` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Optional replacement contract. Exactly one active owning feature may recognize and safely project its own effective-final intent failures; feature registration rejects an existing replacement with a clear diagnostic.
- **Signature:** `ProjectAsync(RuntimePostCommitOutboxItem item, RuntimePostCommitOutboxDeliveryResult finalResult, ...)` returns an optional dispatch/follow-up aggregate.
- **Usage and safety:** the outbox processor invokes it only at effective final failure, then passes its result to the fenced claim-completion store. The projection must validate the canonical intent/dispatch identity and may expose only fixed diagnostics and stable identifiers; it cannot persist independently of the claimed outbox completion.
- **Known cross-domain replacement:** `Elsa.Activities.DispatchWorkflow.Runtime.WorkflowDispatchDeliveryFailureProjector`, selected by `ActivitiesDispatchWorkflowRuntime`; registration rejects a second replacement instead of silently choosing one.

### `IWorkflowDispatchRedriveStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Additive atomic provider capability for separately authorized delivery recovery.
- **Signature:** `RedriveAsync(WorkflowDispatchRedriveRequest request, ...)` returns a safe disposition and lifecycle summary.
- **Usage and safety:** only a fire-and-forget, delivery-caused `DispatchFailed` record linked to its exact `FailedFinal` start item can be reopened. The provider advances generation and fencing while reusing the original dispatch, child, intent, payload, retry policy, and idempotency identities. Wait dispatches and ordinary terminal states remain closed, and ordinary `IWorkflowDispatchStore.SaveAsync` still rejects `DispatchFailed -> Pending`.
- **Default and replacements:** `InMemoryRuntimeCheckpointCommitStore` supplies the process-local atomic implementation. Durable providers must bind this capability to the same physical dispatch/outbox transaction owner they use for fenced completion.

### `IPostCommitOutboxLookupStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement read capability (one provider resolves an exact already-committed post-commit outbox item without widening the base outbox store).
- **Signature:** `FindAsync(string outboxItemId, CancellationToken cancellationToken = default)`.
- **Usage and dependents:** successful DispatchWorkflow terminal enrichment uses the exact committed item to preserve replay identity after uncertain acknowledgement. The in-memory checkpoint store, coalescing overlay, and Groundwork outbox provider implement the capability alongside their write store.
- **Default implementation and override guidance:** `InMemoryRuntimeCheckpointCommitStore` supplies the process-local default. Durable providers that replace `IRuntimePostCommitOutboxStore` must also register a matching lookup implementation when a feature depends on committed-intent replay; the lookup must share the same physical records and access scope as the writer.

### `IWorkflowOutputSource` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement projection (one source exposes policy-safe terminal workflow outputs to runtime features).
- **Signature:** `ReadAsync(string workflowExecutionId, IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? pendingDurableValueChanges = null, CancellationToken cancellationToken = default)`.
- **Usage and dependents:** DispatchWorkflow successful completion reads the child execution's durable workflow-output channel and overlays the current terminal checkpoint changes before recording a parent result. Redacted entries retain name/type metadata but never expose their value.
- **Default implementation and override guidance:** `WorkflowOutputSource` projects `IDurableValueStateStore` through `IRuntimePayloadCapturePolicy`. Replacements must preserve execution scoping, terminal-change overlay semantics, ordinal output ordering, declared types, and explicit value-free redaction.

### `IRuntimeRecoveryScanner` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one scanner identifies interrupted workflow executions for a runtime composition).
- **Signature:** `ScanAsync(RuntimeRecoveryScanRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** provider implementations inspect operational state such as leases and heartbeats and return recovery candidates that requeue from the last checkpoint without invoking domain retry policy.
- **Default implementation:** `InMemoryRuntimeRecoveryScanner` *(single-node in-memory default for operational recovery candidate discovery)*.

### `IRuntimeResumptionService` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one service owns a single system-wide resumption sweep pass for a runtime composition).
- **Signature:** `SweepAsync(RuntimeResumptionSweepRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** one sweep pass re-delivers stranded post-commit outbox items system-wide (`ProcessAsync(workflowExecutionId: null, intentKind: null)`) across all contributed kinds, unions durable scheduler-queue backlog (`IWorkflowSchedulerWorkQueue.ListPendingWorkflowExecutionIdsAsync`) with `IRuntimeRecoveryScanner` candidates, and re-drives each discovered execution by enqueueing a `RunSchedulerWork` envelope through the actor mailbox — preserving single-writer discipline. The request bounds each sweep (`MaxExecutionsPerSweep`) and skips executions the caller is backing off (`ExcludedWorkflowExecutionIds`). Re-drive failures surface on the result and do not abort the sweep; callers own logging and backoff. It is not registered by the runtime API feature — only the `WorkflowsRuntimeResumption` shell feature registers it and drives it from a recurring pump.
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

### `IWorkflowEngineTracer` *(Core — `Elsa.Workflows.Runtime.Core.Diagnostics`)*
- **Kind:** Replacement (one tracer owns engine-phase span emission for a runtime composition).
- **Signature:** `StartDrainCycle(RuntimeSchedulerDrainRequest)`, `StartDispatch(RuntimeSchedulerWorkItem)`, `StartActivityExecution(RuntimeSchedulerWorkItem)`, `StartCheckpointCommit(RuntimeCheckpointCommit)` — each returns `Activity?`.
- **Usage:** engine self-instrumentation (MS-9). The four hot-path phases (drain → dispatch → activity.execute / checkpoint.commit) start spans on the `Elsa.Workflows.Runtime` `ActivitySource`; nesting is via `Activity.Current`, tags are set through `activity?.SetTag(...)` after values exist. Instrumentation is behaviour-preserving: no new awaits inside the fenced drain/commit sequences, no W12 slot reordering, and the no-op path allocates nothing. Span/tag names are the stable contract in `WorkflowEngineTelemetry`. This is **engine telemetry** (emits spans), not the `Elsa.Diagnostics.OpenTelemetry` ingestion domain (receives OTLP) — see [`docs/reference/engine-telemetry.md`](../../../../docs/reference/engine-telemetry.md).
- **Default implementation:** `NullWorkflowEngineTracer` *(allocation-free no-op; registered by the runtime composition root)*.
- **Alternative implementation:** `ActivitySourceWorkflowEngineTracer` *(opt-in — composed by the `WorkflowsRuntimeTracing` shell feature in `Elsa.Workflows.Runtime.Tracing`, which `services.Replace(...)`s the no-op; still costs nothing until an `ActivityListener`/OpenTelemetry `AddSource` attaches)*.

### `IWorkflowSchedulerPoisonStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns poison/retry records for crashed scheduler work items in a runtime composition).
- **Signature:** `RecordAsync(RuntimeSchedulerPoisonRecord record, ...)`, `FindAsync(workflowExecutionId, workItemId, ...)`, `ListAsync(workflowExecutionId, ...)`.
- **Usage:** when a scheduler work handler crashes, the drainer captures the fault, consults `IRuntimeDomainRetryPolicy`, and records a `RuntimeSchedulerPoisonRecord` here instead of dropping the dequeued item (RT-1 gap b). Disposition is `Poisoned` (terminal, no retry) or `RetryScheduled` (carries `NextRetryAt`). The default `NoopRuntimeDomainRetryPolicy` yields `Poisoned` — a safe, non-looping baseline.
- **Default implementation:** `InMemoryWorkflowSchedulerPoisonStore` *(intra-domain default; a durable poison store is future provider work — see follow-ups below)*.
- **Follow-ups (W1 → W2):** `RetryNow` re-enqueues immediately through the `IWorkflowSchedulerWorkQueue` public contract and also records `RetryScheduled`; `RetryAfter(delay)` records `RetryScheduled` with `NextRetryAt` but does **not** re-enqueue — re-driving delayed retries is left to the durable resumption pump (`RuntimeResumptionPumpTask`; see [`docs/runtime-durable-resumption.md`](../../../../docs/runtime-durable-resumption.md)), which avoids ignoring the delay / hot-looping. A durable poison store and the delayed re-drive are explicit follow-ups, not W1 scope.

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

### `IWorkflowHoldStateStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns administrative control-plane state for a runtime composition).
- **Signature:** `SaveAsync(WorkflowHoldState state, ...)`, `FindAsync(string controlPlaneStateId, ...)`, `ListForWorkflowExecutionAsync(string workflowExecutionId, ...)`, `ListAllAsync(...)`.
- **Usage:** stores pause/unpause administrative holds outside workflow continuation state. Durable or distributed control-plane providers can replace the default without changing workflow execution state contracts.
- **Default implementation:** `InMemoryWorkflowHoldStateStore` *(single-node in-memory default for the current runtime slice)*.

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
- **Signature:** `DispatchAsync(BookmarkResumeDispatchRequest request, WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)`. *(spec 089 E)*
- **Usage:** uses `IBookmarkStimulusLookup`, workflow execution state, the pinned executable artifact, and `IBookmarkResumeResolver` to enqueue a `ResumeBookmark` command through `IWorkflowExecutionActorProvider` (threading `dispatchOptions ?? Default`). The command payload carries `ResumeTargetId`, not C# callback method names. It does not consume bookmarks or invoke activity resume handlers. **Ambient-services passthrough (spec 089 E-D4):** as `IWorkflowStartDispatcher`, `dispatchOptions.AmbientServices` reaches the inline resume drain — so scenario 5.5's resuming request writes the workflow's subsequent live response in its own exchange (each resuming request dispatches with its own request scope). Same never-durable / never-cross-process invariant.
- **Default implementation:** `BookmarkResumeDispatcher`.

### `IBookmarkStimulusIndex` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (narrow cross-execution read surface over bookmark state; segregated from `IBookmarkStateStore` so it can be widened/replaced independently).
- **Signature:** `ListByStimulusPageAsync(BookmarkStimulusPageQuery, ...)`; `ListByStimulusTypePageAsync(BookmarkStimulusTypePageQuery, ...)` *(spec 089 D)*.
- **Usage:** returns finite, continuation-bearing pages of raw `BookmarkState` records matching a stimulus **across every workflow execution** (E3-5 fan-in), unlike `IBookmarkStimulusLookup` which is scoped to one `workflowExecutionId`. Exact-stimulus and type-only lookups are provider-filtered and ordered by `(workflowExecutionId, bookmarkId)` over dedicated composite physical indexes. Neither filters expiry or correlation; that stays in `IGlobalBookmarkStimulusLookup`. Callers that truly require the complete fan-in opt into `ListAllByStimulusAsync` or `ListAllByStimulusTypeAsync`, which explicitly traverse bounded pages.
- **Default implementation:** `InMemoryBookmarkStateStore` / `GroundworkBookmarkStateStore` *(each also implements this interface)*.

### `IGlobalBookmarkStimulusLookup` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one cross-execution lookup surface finds every waiting bookmark for a stimulus).
- **Signature:** `FindWaitingAsync(GlobalBookmarkStimulusLookupRequest request, CancellationToken cancellationToken = default)`; `FindWaitingByTypeAsync(GlobalBookmarkStimulusTypeLookupRequest request, CancellationToken cancellationToken = default)` *(spec 089 D)*.
- **Usage:** builds the fan-in resume set for the stimulus router by querying `IBookmarkStimulusIndex`, filtering expired bookmarks against the evaluated time and (when supplied) a passive correlation scope carried in bookmark metadata. Correlation is a threaded metadata value only — not a correlation subsystem. `FindWaitingByTypeAsync` (spec 089 D) is the type-scoped counterpart used by the mid-flow HttpEndpoint route-table resolver and middleware: it returns the non-expired `Matches` snapshots (incl. `Metadata`) for a stimulus type regardless of hash, so a consumer can read the durable route template + endpoint options a mid-flow suspension stored. Expiry filtering lives here, not in the raw index; no correlation scoping (mid-flow bookmark resumes are instance-scoped).
- **Default implementation:** `GlobalBookmarkStimulusLookup`.

### `IWorkflowTriggerBindingStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one provider owns the durable trigger-binding index for a runtime composition).
- **Signature:** `SaveAsync(...)`, `ListByStimulusAsync(WorkflowTriggerBindingPageQuery, ...)`, `ListByStimulusTypeAsync(stimulusType, ...)`, `ListByArtifactAsync(artifactId, ...)`, `DeleteByArtifactAsync(artifactId, ...)`.
- **Usage:** stores `WorkflowTriggerBinding` documents mapping a stimulus identity to a start-trigger inside a published artifact. `ListByStimulusAsync` is the finite, continuation-bearing cross-artifact lookup over active bindings with an exact stimulus type/hash; `ListAllByStimulusAsync` traverses those bounded pages for business operations that must fan out to every match. `ListByStimulusTypeAsync` is a type-scoped full scan (no hash) used to rebuild a per-shell projection over one stimulus family (e.g. the HTTP route table); `by-artifact` scoping supports republish replacement.
- **Default implementation:** `InMemoryWorkflowTriggerBindingStore` *(single-node in-memory default; `GroundworkWorkflowTriggerBindingStore` replaces it for durable storage over the `workflowTriggerBinding` document kind)*.

### `IWorkflowTriggerBindingExtractor` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one extractor derives trigger bindings from a published executable).
- **Signature:** `Evaluate(WorkflowExecutable executable)` returns a non-persisted `WorkflowTriggerPreflightOutcome`; the compatibility projection `Extract(WorkflowExecutable executable)` remains available.
- **Usage:** walks the pinned executable's node tree, selects compiler-marked start-trigger nodes, and evaluates every registered `IActivityTriggerStimulusProvider` strategy exactly once per node. Exactly one provider must recognize each node. Zero claims, multiple claims, a blank provider id, invalid descriptor identity, or duplicate deterministic binding ids fail with `WorkflowTriggerPreflightException` before index mutation. One recognized provider returning zero descriptors records `IntentionallyNonStarting` and yields no binding. Public preflight/index contracts declare their typed failures with XML `<exception>` documentation.
- **Default implementation:** `WorkflowTriggerBindingExtractor`.

### `IWorkflowTriggerIndexer` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one indexer writes the trigger index for a published artifact).
- **Signature:** `IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)`.
- **Usage:** invoked inside the publish flow; completes preflight for the whole artifact, runs every `IWorkflowTriggerIndexValidator` over the completed binding set, then replaces the artifact's prior bindings (delete-by-artifact then write). All semantic failures occur before mutation. After the write succeeds — before returning — it notifies every `IWorkflowTriggerIndexObserver` with the artifact's new bindings. Store or observer failures after mutation begins still propagate; this seam does not promise publication-wide transactionality.
- **Default implementation:** `WorkflowTriggerIndexer`.

### `IWorkflowTriggerIndexValidator` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contribution (fan-in; enumerable). Register with `services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowTriggerIndexValidator, MyValidator>())`; the scoped indexer resolves `IEnumerable<IWorkflowTriggerIndexValidator>` without capturing an access-bound trigger store.
- **Signature:** `ValidateAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken ct = default)` — the snapshot carries `ArtifactId` + the artifact's extracted, about-to-be-written `IReadOnlyCollection<WorkflowTriggerBinding>`.
- **Usage:** PRE-write validation (the pre-write counterpart of `IWorkflowTriggerIndexObserver`, issue #592 item 2) so a stimulus family can enforce publish-time constraints over the index without the indexer knowing any stimulus type. Called after extraction, before delete-and-resave. **Failure policy:** a throw fails the publish with the durable index untouched — no rollback needed, and a bad publish can never poison the store for later publishes or startup. Keep family-specific constraints in the owning module: do NOT enforce cross-definition stimulus uniqueness generically — for most stimulus types (e.g. two definitions on one Timer cron) shared identity is legitimate fan-out.
- **Default implementation:** none (an unvalidated index is valid). `HttpEndpointRoutingUniquenessValidator` (in `Elsa.Workflows.Runtime.Http`) is the shipped consumer — HTTP `(template, method)` cross-definition uniqueness.

### `IWorkflowTriggerIndexObserver` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contribution (fan-in; enumerable). Register with `services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowTriggerIndexObserver, MyObserver>())` (or Singleton); the indexer resolves `IEnumerable<IWorkflowTriggerIndexObserver>`.
- **Signature:** `OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken ct = default)` — the snapshot carries `ArtifactId` + the artifact's new `IReadOnlyCollection<WorkflowTriggerBinding>`.
- **Usage:** post-index notification so a projection derived from the trigger index (e.g. the per-shell HTTP route table) refreshes as an atomic part of the publish, without the indexer depending on any consumer. Called after delete-and-resave, before `IndexAsync` returns. **Failure policy:** exceptions are NOT swallowed — an observer that throws fails the publish (same rule as an unindexed trigger). Keep observer work idempotent so a retried publish converges.
- **Default implementation:** none (an unobserved index is valid). `RouteTableTriggerIndexObserver` (in `Elsa.Workflows.Runtime.Http`) is the shipped consumer.

### `IBookmarkLifecycleObserver` *(Core — `Elsa.Workflows.Runtime.Core`, spec 089 D)*
- **Kind:** Contribution (fan-in; enumerable). Register with `services.TryAddEnumerable(ServiceDescriptor.Singleton<IBookmarkLifecycleObserver, MyObserver>())`; fanned in by `BookmarkLifecycleNotifier`, which the two commit sites resolve.
- **Signature:** `OnBookmarkCreatedAsync(BookmarkState bookmark, CancellationToken ct = default)`, `OnBookmarkConsumedAsync(BookmarkState bookmark, CancellationToken ct = default)` — each carries the committed `BookmarkState` (incl. `Metadata`).
- **Usage:** post-commit notification so a projection derived from waiting bookmarks (e.g. the per-shell HTTP route table for mid-flow endpoints) refreshes as bookmarks come and go, without the runtime depending on any consumer. `OnBookmarkConsumedAsync` fires AFTER the bookmark-consumed checkpoint commits (`BookmarkConsumptionCheckpointService`, inline). `OnBookmarkCreatedAsync` fires AFTER the bookmark-created checkpoint commits from **whichever commit site ran**: because the drainer dispatches `CreateBookmark` through the ADR-0029 activity pipeline, the created notification fires from `RuntimeActivityCheckpointMiddleware` (the pipeline Checkpoint slot) after it commits a staged `BookmarkCreated` checkpoint; `WorkflowCreateBookmarkSchedulerWorkHandler`'s direct `HandleAsync` also notifies after its inline commit for the non-pipeline/unit path. The two paths are mutually exclusive per dispatch (the drainer uses the pipeline), and observer work is a full re-projection anyway, so a redundant refresh would be harmless. **Failure policy (opposite of `IWorkflowTriggerIndexObserver`):** this fires on the RUN path — an observer exception is caught and logged by `BookmarkLifecycleNotifier` and NEVER faults the run (a stale route simply 404s until the next refresh). Keep observer work idempotent and cheap; a throw is swallowed.
- **Default implementation:** none (an unobserved bookmark lifecycle is valid). `RouteTableBookmarkObserver` (in `Elsa.Workflows.Runtime.Http`, spec 089 D Worker B / T010) is the shipped consumer.

### `IStimulusStartDeduplicator` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (narrow best-effort dedup for the stimulus START path, Condition A).
- **Signature:** `TryBeginStart(string idempotencyKey)`.
- **Usage:** when the router is given an `idempotencyKey`, a duplicate at-least-once delivery under the same key does not double-start. The default is in-process and best-effort (not a durable cross-node ledger); when no key is supplied the start path is plainly at-least-once and **may double-start**. Hosts needing restart-durable start-once semantics replace this contract.
- **Default implementation:** `InMemoryStimulusStartDeduplicator`.

### `IStimulusRouter` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one routing spine turns an external stimulus into starts and/or resumes).
- **Signature:** `RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** the E3-1/E3-5 spine. Snapshots the cross-execution resume set before starting new instances, starts matching published triggers, and resumes each waiting instance through the actor mailbox. Stimulus payload and trigger-node identity travel on reserved durable channels and are delivered through typed invocation/trigger contracts; they never enter the author workflow-input bag or an ambient expression context. Optional matched bindings and dispatch options are forwarded across both fan-out paths.
- **Default implementation:** `StimulusRouter`.

### `IRuntimeInputBindingResolver` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one resolver owns runtime input binding materialization rules for a runtime composition).
- **Signature:** `Resolve(RuntimeInputBinding binding, RuntimeInputBindingResolutionContext context)`.
- **Usage:** resolves only the canonical role-owned sources: literal, workflow request, variable read, causal activity result, and portable expression. Results are selected from committed records by structural and causal identity; there is no latest/active output register.
- **Default implementation:** `RuntimeInputBindingResolver` *(intra-domain default)*.

### `IRuntimeActivityInputMaterializer` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one materializer owns immutable invocation-input snapshots).
- **Signature:** `MaterializeSnapshotAsync(ActivityContract contract, string invocationId, RuntimeInputBindingResolutionContext resolutionContext, CancellationToken cancellationToken)`.
- **Usage:** resolves canonical bindings, evaluates portable expressions from their explicit immutable parameter map, validates persistability, and produces one typed `ActivityInputSnapshot` suitable for checkpointing before user code. It never creates argument wrappers, memory blocks, or an ambient expression context.
- **Default implementation:** `RuntimeActivityInputMaterializer`.

### `IRuntimePayloadCapturePolicy` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one policy decides which runtime observability payloads may be captured for a runtime composition).
- **Signature:** `Decide(RuntimePayloadCaptureRequest request)`.
- **Usage:** controls whether history, diagnostics, incidents, values, and input/output observations capture no payload, metadata only, or full payload. Continuation state does not read these observability payloads. The default excludes sensitive values and omits workflow/activity input and output snapshots.
- **Default implementation:** `DefaultRuntimePayloadCapturePolicy` *(intra-domain default)*.

### `IWorkflowExecutionActorProvider` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one provider owns workflow-execution mailbox activation, routing, and passivation for a runtime composition).
- **Signature:** `Capabilities`, `GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default)`, `PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** provider implementations enforce one active mailbox/agent per `(WorkflowExecutionPartition, WorkflowExecutionId)`. Commands are delivered through `WorkflowExecutionCommandEnvelope`, which carries the typed provider-neutral partition alongside command identity, workflow execution ID, idempotency key, optional sequence, delivery mode, and metadata. Equal execution IDs and idempotency keys in different partitions remain independent. Actor frameworks are provider choices; checkpoint state remains the source of truth.
- **Default implementation:** `InProcessWorkflowExecutionActorProvider` *(single-node actor-like mailbox; no distributed placement or actor framework dependency)*.
- **Alternative implementation:** `DistributedWorkflowExecutionActorProvider` *(opt-in leaf `Elsa.Workflows.Runtime.Distributed`, W20/E3-3)* — clustered placement/routing over the in-process provider: claims per-execution placement, returns the local in-process actor when this node owns the execution, or a `ForwardingWorkflowExecutionActor` (durable-transport routing stub, `Deferred` result) when another node owns it. Placement is best-effort routing; W5 fencing at checkpoint commit is the authoritative double-execution guard. Enable with the `WorkflowsRuntimeDistributed` shell feature. See the Distributed placement and transport section below.

### `IWorkflowExecutionPartitionAccessor` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement bridge from the host's provider-neutral operation context to runtime command construction.
- **Signature:** `WorkflowExecutionPartition Current { get; }`.
- **Usage:** start and bookmark/resumption dispatchers snapshot the current partition into every envelope and activation/passivation request. The default engine implementation maps `PersistenceScope` without exposing a persistence-provider type from Runtime.Core. A host replacement must return one immutable nonblank partition for the current operation.
- **Default implementation:** `PersistenceWorkflowExecutionPartitionAccessor` *(engine — scoped)*.

### Distributed placement and transport *(leaf — `Elsa.Workflows.Runtime.Distributed`, W20/E3-3)*

Leaf-owned contracts for clustered workflow-execution placement and cross-node command routing. Per §2.7 these live entirely in the provider leaf; `Elsa.Workflows.Runtime.Core` gains zero references to them. The leaf consumes the W5 single-writer fencing seam (`IRuntimeExecutionOwnershipService`) unchanged — placement decides *which* node drains (routing), fencing decides *whether* a write commits (safety).

- ### `IExecutionPlacementStore` *(leaf)*
  - **Kind:** Replacement (one store owns per-execution placement lease records for a distributed composition).
  - **Signature:** `TryClaimAsync`, `FindAsync`, `ReleaseAsync`, `ListAsync` (compare-and-swap on placement token; claim doubles as renew).
  - **Usage:** the CAS claim/renew primitive under placement ownership. Claiming an unowned or expired placement issues a strictly greater placement token; a claim against a live foreign lease fails without mutation.
  - **Default implementation:** `InMemoryExecutionPlacementStore` *(single-process/two-node-harness default; a scoped adapter over shared state keyed by partition + execution ID)*. `GroundworkExecutionPlacementStore` *(durable `IDocumentStore`-backed bridge, W27 — opt-in leaf `Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork`, swapped in by the `WorkflowsRuntimeDistributedGroundworkPersistence` feature; exact cross-node CAS via the provider's ExpectedVersion contract, including create-only first claims)*.
- ### `IExecutionPlacementService` *(leaf)*
  - **Kind:** Replacement (one service owns this node's placement acquisition/renewal/release policy).
  - **Signature:** `NodeId`, `TryClaimAsync`, `FindOwnerAsync`, `ListOwnedAsync`, `ReleaseAsync`.
  - **Usage:** wraps the store with this node's identity, lease duration, and `TimeProvider` so all lease timing is deterministic and options-driven.
  - **Default implementation:** `ExecutionPlacementService`.
- ### `IExecutionCommandTransport` *(leaf)*
  - **Kind:** Replacement (one transport owns the durable cross-node command inbox for a distributed composition).
  - **Signature:** `SendAsync`, `LeaseAsync`, `AckAsync`, `ListPendingExecutionIdsAsync`, `CountPendingAsync` (ack-based lease/visibility, at-least-once).
  - **Usage:** commands for an execution owned by another node are durably enqueued, then leased/acked by the owning node's pump. A lease hides an item from other nodes until acked or expired; only the live lease holder may ack, so a superseded node's ack is refused and the item is re-driven on failover. Wire shape is frozen by the committed v1 golden fixture (§E6 kind `executionCommandTransport`).
  - **Default implementation:** `InMemoryExecutionCommandTransport` *(single-process/two-node-harness default; a scoped adapter over shared partitioned inbox state and rejects envelope/operation partition mismatch)*. `GroundworkExecutionCommandTransport` *(durable `IDocumentStore`-backed bridge, W27 — same leaf/feature as the placement store; persists the frozen v1 `executionCommandTransport` item shape, store-enforced unique per-execution sequences, version-guarded lease/ack CAS)*.

### `ExecutionPlacementPumpTask` *(leaf — `Elsa.Workflows.Runtime.Distributed`, W20/E3-3)*
- **Kind:** Registered recurring task (`IRecurringTask`; one per node, DependsOn Tasks).
- **Usage:** each tick obtains the finite partition snapshot through `IPersistenceScopeRunner` and opens one fresh ordinary scope per partition. Within each bounded partition sweep it renews placements this node holds, discovers visible transport backlog, claims any it can own, leases commands, verifies each stored envelope still matches the current partition, dispatches it with that partition, and acks on a delivered outcome. Deferred/Rejected dispatches stay leased so lease expiry re-drives them (the failover loop). The runner visits the remaining partitions before surfacing failures; the task's aggregate consecutive-failure count controls its node-wide backoff interval. All cadence/bounds come from `ExecutionPlacementPumpOptions` evaluated against `TimeProvider`.


- **Kind:** Replacement (one generator owns runtime command-dispatch IDs for a runtime composition).
- **Signature:** `NewWorkflowExecutionId()`, `NewWorkflowExecutionCommandId()`, `NewWorkflowExecutionCommandEnvelopeId()`, `NewActivityExecutionId()`.
- **Usage:** provides runtime-owned identifiers for workflow execution start dispatch and concrete activity executions without leaking API, persistence, or provider-specific identity generation into command construction.
- **Default implementation:** `ShortRuntimeExecutionIdGenerator`.

### `IWorkflowStartDispatcher` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one dispatcher owns conversion from executable artifact start requests into workflow execution agent commands).
- **Signature:** `DispatchAsync(WorkflowExecutionStartDispatchRequest request, WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)` (plus `DispatchTransientAsync` with the same optional options param). *(spec 089 E)*
- **Usage:** loads the runtime-owned executable artifact, pins its exact identity in a `WorkflowExecutionCommandKind.Start` payload, activates the workflow execution agent, and enqueues the command envelope (threading `dispatchOptions ?? Default` into `agent.EnqueueAsync`). It does not execute activities inline. **Ambient-services passthrough (spec 089 E-D4/FR-019):** `dispatchOptions.AmbientServices` reaches the inline drain's activity context (spec-069 chain), so a request-affine `WriteHttpResponse` can write the live sync response. The options are a live reference — NOT durable state; they never serialize into the envelope, and `ForwardingWorkflowExecutionActor` DROPS them (returns `Deferred`), so ambient services never cross the process boundary (FR-021 invariant, tested).
- **Default implementation:** `WorkflowExecutionStartDispatcher`.

### `IWorkflowExecutionCommandExecutor` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one processor decides what an accepted workflow-execution command does inside the active actor mailbox).
- **Signature:** `ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)`.
- **Usage:** invoked by the in-process agent after dispatch metadata has been accepted and before the partition-qualified idempotency key is marked processed. The host-lifetime mailbox calls the default scope-crossing executor, which opens a fresh DI operation scope bound to the envelope partition before resolving any persistence-consuming command graph.
- **Default implementation:** `ScopedWorkflowExecutionCommandExecutor` *(host-lifetime bridge over `IPersistenceOperationScopeFactory`; resolves scoped `WorkflowSchedulerCommandRouter`, which records accepted commands as scheduler work, applies the scheduler drain policy, then delegates command-triggered draining to `IWorkflowDrainOrchestrator`)*.

### `IRuntimeAdmissionLoadSignal` / `IRuntimeAdmissionController` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement pair for dispatch admission control (RB1, #1235): the load signal owns the in-flight measurement and its reservation, the controller owns the admit-or-shed decision and the adaptive limit.
- **Signature:** `TryOpenCharge(double limit, out long observedLoad)` is one atomic compare-and-reserve on the gated path; `OpenCharge()` opens a charge unconditionally, for the paths the controller has already decided are not subject to the limit; `RecordDispatch()` weights the ambient charge one unit per scheduler dispatch. `IRuntimeAdmissionController.TryAdmit()` returns an admitted decision carrying the charge and a completion callback, or a shed carrying observed load, limit, and retry-after.
- **Usage:** the default scheduler command router gates externally arriving live work through a router-internal deny-list predicate (change which kinds are gated by replacing the scoped router via `IWorkflowExecutionCommandExecutor`, not through a seam of its own). Recovery and control-plane commands (`RunSchedulerWork`, `Cancel`, `PauseWorkflowExecution`, `UnpauseWorkflowExecution`) bypass admission: the sweep carries its own bound (`RuntimeResumptionOptions.MaxExecutionsPerSweep`) and refusing an operator's cancel at capacity would remove the tool for ending the overload. `AlterWorkflow` **is** gated (#1325): the gate sits above the alteration hand-off, so an alteration runs inside the admission charge and the work it performs is counted like any other live command's — its plan store admits by idempotency key, which is registration and not a load bound, so before it was gated an alteration weighed zero against the limiter. It is the one gated kind besides `Start` whose refusal writes nothing: parking an `AlterWorkflow` work item would hand it to `NoopWorkflowSchedulerWorkHandler`, which matches every kind except `InvokeActivity`/`GeneratedEvent`/`ResumeBookmark` and silently drops it. The outright refusal is safe because the alteration pump (`WorkflowAlterationOrchestrationPumpTask`, registered unconditionally by `AddWorkflowRuntime`) re-claims the job once its lease lapses. The default signal meters **dispatches, not commands** — an External-leaf run pays ~56 dispatches where a fusable run pays ~5, so a command-count limit would under-report exactly the shape production traffic has.
- **Safety:** a replacement signal must keep compare-and-reserve atomic; a separate read followed by a reserve lets N simultaneous callers all observe the same below-limit load and all pass, which is the burst the limiter exists to stop. `InFlightDispatches` must also remain a cheap, always-safe read: the controller reads it on the exempt paths (admission disabled, ambient charge), so the property may not throw or block. The adaptive controller and the `StaticLimit` operator override share one code path (the override clamps the adaptive range to a single value), so a replacement must not fork them.
- **Default implementations:** `DispatchRuntimeAdmissionLoadSignal` and `RuntimeAdmissionController`, both `TryAdd`-registered singletons.

### `IWorkflowDrainOrchestrator` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one coordinator owns command-triggered workflow execution drain orchestration for a runtime composition).
- **Signature:** `DrainAsync(WorkflowExecutionCommandEnvelope envelope, RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** bridges the accepted command boundary to scheduler draining after scheduler work is recorded and the drain policy requests immediate advancement. The default coordinator drains scheduler work, processes deliverable `RuntimePostCommitIntentKinds.EnqueueSchedulerWork` outbox items for the same workflow execution, and repeats scheduler draining until scheduler-intent delivery quiesces, a pause/fault stops scheduler draining, or the bounded cycle guard is reached. While ownership is active it renews the lease at one-third of the acquired lease duration; a rejected or failed heartbeat cancels the drain and surfaces `RuntimeExecutionOwnershipLostException`. Cleanup stops renewal before releasing with a non-cancelable token, so caller cancellation cannot strand a false recovery candidate. Checkpoint commit remains the durability boundary: commits record post-commit work, and the coordinator only delivers it after the commit path succeeds. `WorkflowDrainOrchestratorOptions` names the cycle and outbox batch limits; cycle-cap exhaustion throws `DrainCycleLimitExceededException`.
- **Default implementation:** `WorkflowDrainOrchestrator`.

### `IRuntimeExecutionOwnershipService` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one service owns single-writer fencing — lease acquisition, heartbeat, release, and stale-writer rejection — for a runtime composition).
- **Signature:** `AcquireAsync(string workflowExecutionId, ...)`, `HeartbeatAsync(RuntimeExecutionLease lease, ...)`, `ReleaseAsync(RuntimeExecutionLease lease, ...)`, `EnsureCurrentAsync(string workflowExecutionId, long fencingToken, ...)`.
- **Usage:** enforces RT-2 single-writer ownership. `WorkflowDrainOrchestrator` acquires a lease at the start of a drain, pushes it onto `IRuntimeExecutionOwnershipContextAccessor`, renews it while the drain is active, and conditionally releases it after renewal stops (so a crash leaves the lease persisted for the recovery scanner to detect — closing W2's post-dequeue/pre-commit window). Allocation, heartbeat, and release are revision-CAS transitions; stale operations never overwrite a successor, and tokens are strictly monotonic and never reused. `RuntimeCheckpointCommitter` stamps the ambient lease fence onto the provider-facing commit. `EnsureCurrentAsync` is only an early convenience check: the checkpoint store atomically fence-touches ownership in the same unit of work as checkpoint state, outbox state, and the idempotency marker, and that durable decision is authoritative. Ownership state is backed by `IExecutionLivenessStateStore`; the lease `ExpiresAt` reuses the recovery scanner's existing lease-timeout honoring rather than a parallel knob.
- **Default implementation:** `RuntimeExecutionOwnershipService` *(operational-state-backed, monotonic fencing token preserved across release)*.

### `IRuntimeExecutionOwnershipContextAccessor` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one accessor owns the ambient current-lease scope for a runtime composition).
- **Signature:** `RuntimeExecutionLease? Current { get; }`, `Push(RuntimeExecutionLease lease) : IDisposable`.
- **Usage:** an AsyncLocal push/pop scope that carries the active drain's lease from `IWorkflowDrainOrchestrator` down to `RuntimeCheckpointCommitter` without threading it through every command/handler signature. It is a runtime-internal ambient accessor, not the ADR-0029-discouraged pipeline-context ambient. (This is a deliberately retained runtime-internal ambient — distinct from the pipeline-context/ambient-services service locators RT-7 removed from the drain path, whose services now flow explicitly via `RuntimePipelineWorkspace.AmbientServices`.)
- **Default implementation:** `AsyncLocalRuntimeExecutionOwnershipContextAccessor`.

### `IWorkflowSchedulerWorkQueue` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one queue owns recorded scheduler work for a runtime composition).
- **Signature:** `EnqueueAsync(RuntimeSchedulerWorkItem workItem, ...)`, `ListAsync(RuntimeSchedulerWorkQuery query, ...)`, `DequeueAsync(string workflowExecutionId, ...)`, `ListPendingWorkflowExecutionIdsAsync(int limit, ...)`.
- **Usage:** stores scheduler work by `WorkflowExecutionId` after an execution agent accepts a command envelope. The queue preserves per-workflow insertion order and is idempotent by scheduler work item ID within each workflow execution. `ListPendingWorkflowExecutionIdsAsync` returns the distinct execution ids with queued work, up to `limit`, so a resumption sweep can discover durable backlog after a restart when nothing else knows the interrupted execution ids. Draining and activity execution remain separate scheduler behavior.
- **Default implementation:** `InMemoryWorkflowSchedulerWorkQueue` *(single-node in-memory default for the current runtime slice)*. `GroundworkWorkflowSchedulerWorkQueue` *(durable `IDocumentStore`-backed bridge; swapped in by `AddGroundworkRuntimeStores` so scheduler work survives a process crash — see [docs/runtime-durable-resumption.md](../../../../docs/runtime-durable-resumption.md))*.

### `IDurableTimerStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns durable timers for a runtime composition).
- **Signature:** `SaveAsync(DurableTimer timer, ...)`, `FindAsync(string workflowExecutionId, string timerId, ...)`, `DeleteAsync(string workflowExecutionId, string timerId, ...)`, `ListDueAsync(DateTimeOffset now, int limit, ...)`.
- **Usage:** persists `DurableTimer` records (a due-time-indexed document kind, `durableTimer`) keyed by `(WorkflowExecutionId, TimerId)`. `SaveAsync` is a deterministic upsert so a pre-commit crash re-executing the owning activity re-writes the same timer rather than duplicating it. `ListDueAsync` returns timers with `DueTime <= now`, ordered by `(DueTime, TimerId)`, capped at `limit`; the durable timer pump (`DurableTimerPumpTask`) drains this and fires each due timer through `IBookmarkResumeDispatcher`. The pump owns idempotency: it deletes a timer on `Dispatched`/`Duplicate` (the resume is durably enqueued into `IWorkflowSchedulerWorkQueue` before the dispatcher returns — see `WorkflowSchedulerCommandRouter.ProcessAsync` — so deletion cannot lose the resume), and treats a past-grace `NotFound` as an already-consumed bookmark.
- **Default implementation:** `InMemoryDurableTimerStore` *(single-node in-memory default; Delay works but is **not** restart-durable without a durable store)*. `GroundworkDurableTimerStore` *(durable `IDocumentStore`-backed bridge; swapped in by `AddGroundworkRuntimeStores`)*.
- **Follow-ups (W8):**
  - **Native due-time range index.** Groundwork is equality-index only this wave, so `ListDueAsync` loads the whole timer partition (equality query on a constant collection key) and filters/orders `DueTime` in memory. `MaxTimersPerTick` bounds the *dispatch* burst, not the *load*. A native range/due-time index in Groundwork is the scale follow-up.
  - **Timer/Cron start triggers** (recurring schedules that *start* a workflow) ship via a **dedicated recurring-trigger schedule store + pump** (see `IRecurringTriggerScheduleStore` / `IRecurringTriggerScheduleProvider` below and the `WorkflowsRuntimeRecurringTriggers` feature), **not** the `durableTimer` store. Rationale: the durable-timer pump resumes an *existing* execution (it has a `WorkflowExecutionId`); a start trigger has none, so it needs the trigger/stimulus router (W7) to *start* a workflow. The `durableTimer` kind therefore remains resume-only.
  - **Atomic timer registration (Option B).** Delay registers its timer activity-side, strictly before the bookmark (so "bookmark committed, timer missing" is structurally excluded). A fully atomic timer==bookmark lifecycle via a post-commit `RegisterDurableTimer` intent (`IRuntimePostCommitIntentDispatcher`) is the alternative if orphaned timers ever prove noisy.

### `IDurableTimerScheduler` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one scheduler owns durable-timer registration for a runtime composition).
- **Signature:** `ScheduleAsync(DurableTimer timer, ...)`.
- **Usage:** thin activity-facing wrapper over `IDurableTimerStore.SaveAsync`. The `Delay` activity builds the `DurableTimer` (deriving `DueTime` from the injected `TimeProvider`) and calls this to write its timer before creating the matching bookmark.
- **Default implementation:** `DurableTimerScheduler` *(registered by the `WorkflowsRuntimeScheduling` feature)*.

### `IRecurringTriggerScheduleStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns recurring-start schedules for a runtime composition).
- **Signature:** `SaveAsync(RecurringTriggerSchedule schedule, ...)`, `ListDueAsync(DateTimeOffset asOf, int limit, ...)`, `FindAsync(string scheduleId, ...)`, `TryAdvanceAsync(string scheduleId, DateTimeOffset expectedNextOccurrence, DateTimeOffset newNextOccurrence, ...)`, `DeleteByArtifactAsync(string artifactId, ...)`, `DeleteAsync(string scheduleId, ...)`.
- **Usage:** persists `RecurringTriggerSchedule` records (a next-occurrence-indexed document kind, `recurringTriggerSchedule`) for Timer/Cron **start** triggers — the recurring-start counterpart to `IDurableTimerStore` (which is resume-only). `SaveAsync` is an idempotent upsert keyed by `ScheduleId`; republishing an artifact replaces its schedules via `DeleteByArtifactAsync` + re-save, mirroring the trigger index. The recurring-trigger pump (`RecurringTriggerPumpTask`, `WorkflowsRuntimeRecurringTriggers` feature) drains `ListDueAsync` and, for each due schedule, **claims the occurrence with `TryAdvanceAsync` (compare-and-swap on `NextOccurrence`) before firing** the trigger stimulus through `IStimulusRouter`. **Missed-occurrence policy:** on pump wake after downtime a schedule fires **at most once** and advances straight to the next future occurrence — the backlog is never replayed. Durable stores must implement `TryAdvanceAsync` with provider CAS so at most one node fires an occurrence; the Groundwork bridge uses the loaded document version as the expected version.
- **Default implementation:** `InMemoryRecurringTriggerScheduleStore` *(single-node in-memory default; start triggers work but are **not** restart-durable without a durable store)*. `GroundworkRecurringTriggerScheduleStore` *(durable `IDocumentStore`-backed bridge; swapped in by `AddGroundworkRuntimeStores`)*.

### `IRecurringTriggerScheduleProvider` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Strategy set (context-selected, exact-one owner per recurring-trigger node; not a contributor fan-in).
- **Signature:** additive stable nonblank `ProviderId`; `IReadOnlyCollection<RecurringScheduleDescriptor> Describe(ExecutableNode node);`
- **Usage:** the recurring-schedule sibling of `IActivityTriggerStimulusProvider`. At **publish time** the schedule indexer asks every provider to describe a node; a provider returns the node's recurrence descriptors (interval / cron expression → next occurrence) when it recognizes the activity type, or an **empty collection** ("no contribution": not-my-type or recognized-with-no-schedules). A single-schedule trigger (`Timer`/`Cron`) returns one descriptor; a composite start-trigger activity may **fan out** one descriptor per authored recurring start element (spec 117 — a BPMN process with several timer start events). Multiple *providers* claiming one node fail with contextual provider ids rather than selecting by registration order. `RecurringTriggerScheduleIndexer` materializes the complete schedule candidate set before it invokes the inner trigger indexer, keying single-descriptor nodes by `RecurringTriggerSchedule.BuildId(artifactId, nodeId)` (unchanged) and fan-out nodes by `BuildFanOutId(artifactId, nodeId, stimulusHash)` so their rows do not collapse. Invalid expressions, calculator failures, and exhausted Cron schedules are wrapped in contextual `WorkflowTriggerPreflightException` failures before either schedule or binding replacement begins. A recurring-trigger activity contributes both seams: a trigger binding and a materialized schedule per element, sharing the same `(StimulusType, StimulusHash)`. Providers read only the pinned published `ExecutableNode`.
- **Register:** `services.TryAddEnumerable(ServiceDescriptor.Singleton<IRecurringTriggerScheduleProvider, MyProvider>())`.

**Known implementations (shipped):**
- `Elsa.Activities.Scheduling` — `TimerRecurringScheduleProvider` / `CronRecurringScheduleProvider` *(cross-domain — describe the `Timer` (fixed interval) and `Cron` (cron expression, via Cronos) recurring start schedules)*.

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

### `IIncidentStrategy` / `IIncidentResolutionAction` *(Core contracts; Runtime defaults)*

- **Kind:** Additive, versioned strategy extension. A published executable pins one exact
  `IncidentStrategyReference` (`Alias`, `Version`); Runtime never re-reads Design options or the host's current
  default while executing it.
- **Register:** call `services.AddIncidentStrategy<TStrategy>(descriptor)`, or annotate the concrete strategy with
  `[IncidentStrategy(alias, version, ...)]` and call `AddIncidentStrategy<TStrategy>()`. Strategy implementations
  are scoped. Alias comparison is ordinal case-insensitive, version comparison is exact, duplicate identities
  fail, and custom aliases must be dotted. Discovery reads descriptors without constructing strategies.
- **Decision model:** `ResolveAsync(IncidentStrategyContext, CancellationToken)` receives immutable, policy-safe
  snapshots and returns one executable `IIncidentResolutionAction`. Actions execute against a guarded
  `IncidentResolutionActionContext`; they may transition only the target incident, request workflow faulting,
  add bounded outcome metadata, and request separately registered strategy-safe post-commit intents. They cannot
  mutate activities, retry/suspend/complete workflows, absorb/suppress structural faults, or access checkpoints.
  Incident metadata is projected through an explicit correlation/classification allowlist; exception messages,
  stack traces, payloads, variables, and private runtime state are excluded. Strategy-owned outcome and intent
  metadata is limited to 32 nonblank, control-character-free string pairs (128-character keys and 1024-character
  values).
- **Built-ins:** Runtime always registers `Fault/1` and `ContinueWithIncidents/1`. `Fault` keeps the incident
  blocking and faults the workflow. `ContinueWithIncidents` opens the incident while preserving the faulted
  activity and lets independent scheduled work finish.
- **Failure/replay:** null decisions, strategy failures, action failures, and invalid custom action kinds discard
  that action's staging and apply a fresh runtime-owned `FaultWorkflow` decision. All outcomes and permitted
  intents commit in one ordered end-of-drain checkpoint; a durable outcome prevents reevaluation. A missing pinned
  implementation before start records `WaitForIntervention` with `MissingStrategyImplementation`, leaves the run
  pending, and schedules no retry.
- **Safe intents:** register a third-party kind with `AddIncidentStrategySafeIntent(...)`. Runtime-control,
  scheduler, dispatch, stimulus, and retry kinds are rejected. Runtime derives intent identity, idempotency,
  timestamp, and workflow/activity correlation.

### `IExecutionLivenessStateStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns split continuation state for runtime operational coordination in a runtime composition).
- **Signature:** `SaveAsync(ExecutionLivenessState state, ...)`, `TrySaveAsync(ExecutionLivenessState state, long expectedRevision, ...)`, `FindAsync(...)`, `FindVersionedAsync(...)`, `ListAsync(...)`, `ListAllAsync(...)`.
- **Usage:** stores `ExecutionLivenessState` keyed by `WorkflowExecutionId` and `OperationalStateId`. Revision `0` means create-only and a positive revision means compare-and-swap, allowing ownership allocation, heartbeat, and release to converge across independent clients without an adapter-instance lock. The in-memory checkpoint writer projects operational state upserts from accepted checkpoint commits into this store. Recovery scanning, outbox delivery processing, domain retry, and actor-provider lease enforcement remain separate runtime surfaces.
- **Default implementation:** `InMemoryExecutionLivenessStateStore` *(single-node in-memory default for the current runtime slice)*.

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
- **Known implementations (shipped):** `PoisonedSchedulerWorkIncidentObserver`, then
  `IncidentStrategyResolutionDrainObserver`, then `BlockingIncidentWorkflowFaultObserver`. This order projects
  system incidents, resolves eligible ordinary activity-fault incidents once causal work is quiescent, and only
  applies terminal safety to legacy/emergency blocking incidents that still have no outcome.

### `IWorkflowSchedulerWorkHandler` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contributor (handlers consume drained scheduler work items).
- **Signature:** `Name`, `CanHandle(RuntimeSchedulerWorkItem workItem)`, `HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)`.
- **Usage:** modules can handle specific scheduler command kinds without replacing the drainer. The drainer evaluates ordinary handlers before fallback handlers.
- **Default implementations:** `WorkflowStartSchedulerWorkHandler` *(turns `Start` work into `ScheduleActivity` work for executable start nodes)*, `WorkflowScheduleActivitySchedulerWorkHandler` *(records `Scheduled` `ActivityExecutionState` and queues `StartActivity` work for one executable node)*, `WorkflowStartActivitySchedulerWorkHandler` *(transitions scheduled activity state to `Running` and queues `InvokeActivity` work)*, `WorkflowCompleteActivitySchedulerWorkHandler` *(drains deterministic activity completion work)*, `WorkflowCheckpointSchedulerWorkHandler` *(commits named checkpoint scheduler work through `RuntimeCheckpointCommitter`)*, `MissingActivityInvocationSchedulerWorkHandler` *(fallback that faults `InvokeActivity` when no provider is composed)*, `MissingBookmarkResumeSchedulerWorkHandler` *(fallback that faults `ResumeBookmark` when no bookmark resume provider is composed)*, `MissingGeneratedEventSchedulerWorkHandler` *(fallback that faults `GeneratedEvent` when no generated-event provider is composed)*, and `NoopWorkflowSchedulerWorkHandler` *(fallback that acknowledges drained work that has no required provider-specific handler)*.

`RuntimeSchedulerWorkHandlerBase<TPayload>` (project `Elsa.Workflows.Runtime`, same namespace) is the
optional dispatch scaffold for payload-typed handlers that also implement `IRuntimePipelineWorkHandler`:
it deserializes the payload once (throwing validation errors before any scope is created) and runs the
handler body against the pipeline's ambient services or a fresh scope. A derivation supplies `Name`,
`CanHandle`, `DeserializePayload`, and `HandleWithServicesAsync`. Shipped derivations:
`WorkflowInvokeActivitySchedulerWorkHandler`, `WorkflowParentActivityCompletionSchedulerWorkHandler`,
`WorkflowNotifyParentActivitySchedulerWorkHandler` *(all cross-domain — `Elsa.Activities.Runtime`)*.
A handler that must not gain pipeline dispatch (the resume handler) implements the interfaces directly.

### `IFallbackWorkflowSchedulerWorkHandler` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contributor marker (handlers consume drained scheduler work items only after ordinary handlers decline them).
- **Signature:** inherits `IWorkflowSchedulerWorkHandler`.
- **Usage:** modules can register catch-all scheduler work handlers without becoming priority handlers. The default drainer evaluates these handlers after ordinary `IWorkflowSchedulerWorkHandler` registrations.
- **Default implementation:** `NoopWorkflowSchedulerWorkHandler`.

### `IWorkflowExecutableStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns runtime executable artifact lookup for a runtime composition).
- **Signature:** `SaveAsync(WorkflowExecutable executable, ...)`, unconditional `DeleteAsync(string artifactId, ...)`, guarded `DeleteAsync(WorkflowExecutableDeletionGuard, now, ...)`, root-write lease acquire/renew/release, deletion-guard begin/cancel, `FindAsync(string artifactId, ...)`, and `ListAsync(...)`.
- **Usage:** stores and retrieves runtime-owned immutable `WorkflowExecutable` artifacts by content-addressed artifact ID. Publishing writes artifacts through this contract; runtime start, resume, and scheduler paths read them without loading Design-owned workflow state. Mutable source-reference publication, retirement, scope, and expiry checks remain outside this store and authoritative.
- **Default implementation:** `InMemoryWorkflowExecutableStore` *(single-process default)*. Groundwork supplies `GroundworkWorkflowExecutableStore` as a keyed backend; SQLite reference features select a bounded `CachingWorkflowExecutableStore`, while PostgreSQL/distributed and legacy direct registrations preserve direct reads by default. The cache coalesces same-ID misses, retains only positive provider results, invalidates on save/unconditional delete and successful guarded delete, and never populates from `ListAsync`. Root-write lease and deletion-guard transitions pass through to the provider unchanged; custom providers can replace the keyed backend without a concrete dependency.
- **Durable-provider controls:** `CacheWorkflowExecutables` defaults to `true` for SQLite and `false` for PostgreSQL/distributed features; `WorkflowExecutableCacheCapacity` defaults to `256` positive artifacts per shell/provider. Disable caching to restore direct durable reads. Telemetry reports bounded hit/miss, eviction reason, and provider-load outcome/duration dimensions without workflow or artifact IDs. Cache state and mutation invalidation are process-local; PostgreSQL hosts must explicitly opt in with an accepted immutable-retention/invalidation policy.

### `WorkflowExecutableStoreDecorator` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Derivation base (pass-through decorator scaffold over `IWorkflowExecutableStore`).
- **Signature:** every `IWorkflowExecutableStore` member as a `virtual` forward to the wrapped store, exposed through the protected `Inner` property.
- **Usage:** derive and override only the operations the decorator intercepts; everything else forwards unchanged. `CachingWorkflowExecutableStore` (read caching + mutation invalidation) and `InvalidatingWorkflowExecutableStore` (mutation invalidation with direct reads) are the shipped derivations.

### `IExecutableActivityTemplateStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns immutable, content-addressed executable templates for reusable activity versions).
- **Signature:** find by template id or behavior hash, list, save, and delete unreferenced templates.
- **Usage:** Publishing compiles provider-neutral activity versions into `ExecutableActivityTemplate` artifacts. Workflow artifacts pin exact template identities and closed dependency sets; Runtime loads only these artifacts and never falls back to Design state. Equal canonical behavior can share a template even when source version labels differ.
- **Default implementation:** `InMemoryExecutableActivityTemplateStore`; Groundwork replaces it for durable hosts.

### `IWorkflowExecutableSourceReferenceStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns source/version/publication references to content-addressed workflow and activity artifacts).
- **Signature:** find/list by source reference or artifact, save, retire/delete, and discover unreferenced artifacts.
- **Usage:** keeps mutable lifecycle, retention, expiry, and layout sidecars outside immutable behavior artifacts. Source-owned CLR reconciliation creates a distinct source reference per definition version even when versions share one template hash. Runtime start pins the exact referenced artifact; garbage collection follows live references.
- **Default implementation:** `InMemoryWorkflowExecutableSourceReferenceStore`; Groundwork replaces it for durable hosts.

### `IActivityExecutionHierarchyStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement read/write surface for checkpoint-committed activity execution hierarchy and composite-boundary layout.
- **Signature:** write `ActivityExecutionHierarchyRecord`; read cursor-paged descendants, boundary metadata, pinned execution layout, and previous/next attempt navigation by workflow/activity execution identity.
- **Usage:** preserves one workflow execution while making reusable composite boundaries navigable. Inspection clients can click through a graph activity into its full descendant execution graph and complete pinned structural layout, including authored nodes that did not execute, without loading authored Design documents. Boundary lifecycle remains separate from descendant aggregate state. Records are checkpoint-gated and use ordinary activity execution identities/scopes; there is no special “custom activity” scope and no child workflow identity.
- **Default implementation:** `RuntimeInMemoryActivityExecutionHierarchyStore`; Groundwork replaces it for durable hosts. Opaque page cursors are encoded by `IActivityExecutionHierarchyCursorCodec`.

Cursor failures carry the cursor class, boundary/query/access binding results, and a recoverable restart action. Authorization-profile changes invalidate issued cursors but do not alter committed hierarchy history.

`ExecuteWorkflow` remains a separate explicit operation for starting another workflow. It does not use the reusable activity template/hierarchy boundary to disguise a child workflow as an activity.

## Implementable contributor interfaces

### `IWorkflowRuntimeMiddleware` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contributor (workflow runtime pipeline step).
- **Signature:** `InvokeAsync(WorkflowRuntimePipelineContext context, WorkflowRuntimeMiddlewareDelegate next)`.
- **Register:** via `WorkflowRuntimePipelineBuilder.Use<TMiddleware>(slotName, order, name)`.
- **Usage:** registers workflow execution middleware into stable slots from `RuntimeWorkflowPipelineSlots`. Plans are inspectable through `BuildPlan()`.
- **Known implementations (shipped):** built-in load-state / scheduling / post-commit steps; the `Invoke` slot (`RuntimeWorkflowInvokeMiddleware`) runs the dispatcher-staged handler before `next`, and the `Checkpoint` slot (`RuntimeWorkflowCheckpointMiddleware`) drains the handler-staged commit list (ADR 0029 Move 2 — slot-invoked handler model).

### `IActivityRuntimeMiddleware` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contributor (activity runtime pipeline step).
- **Signature:** `InvokeAsync(ActivityRuntimePipelineContext context, ActivityRuntimeMiddlewareDelegate next)`.
- **Register:** via `ActivityRuntimePipelineBuilder.Use<TMiddleware>(slotName, order, name)`.
- **Usage:** registers activity execution middleware into stable slots from `RuntimeActivityPipelineSlots`. Plans are inspectable through `BuildPlan()`.
- **Known implementations (shipped):** built-in load-state / input-evaluation / output-capture / scheduling / post-commit steps; the `Invoke` slot (`RuntimeActivityInvokeMiddleware`) runs the dispatcher-staged handler before `next`, and the `Checkpoint` slot (`RuntimeActivityCheckpointMiddleware`) drains the handler-staged commit list (ADR 0029 Move 2 — slot-invoked handler model).

### `IRuntimePipelineWorkHandler` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contributor opt-in (a migrated scheduler work handler's context-aware overload).
- **Signature:** `HandleAsync(RuntimeSchedulerWorkItem workItem, IRuntimePipelineContext pipelineContext, CancellationToken)`.
- **Usage:** ADR 0029 Move 2 slot-invoked handler model. A scheduler work handler additionally implements this interface to run inside the pipeline's `Invoke` slot with the per-dispatch context threaded **explicitly** (no ambient/AsyncLocal accessor). The handler either **stages** its assembled `RuntimeCheckpointCommit`(s) on `IRuntimePipelineContext.Workspace` for the `Checkpoint` slot to commit **in order, one committer call per staged entry** (never folded — folding is the coalescing decorators' job), or, for the nested-invoke handlers whose commits must go through a dynamically-resolved provider, commits **inline** in the `Invoke` slot and stages nothing. Handlers that have not migrated keep only `IWorkflowSchedulerWorkHandler` and run their plain path unchanged. `RuntimeExecutionPipelineDispatcher` stages the selected handler on the workspace and any migrated handler is picked up by a runtime cast.
- **Staging surface:** `RuntimePipelineWorkspace` — `StageCheckpointCommit(...)` / `PendingCheckpointCommits` (ordered list), the `PendingCheckpointCommit` single-commit convenience, and `AmbientServices` (the explicit carrier for the drain's request-scoped provider that RT-7 substituted for the removed ambient service locator).
- **Known implementations (shipped):** workflow `Cancel` + `Checkpoint`; activity `CreateBookmark`, `ScheduleActivity`, `StartActivity` (stage), and the nested-invoke `InvokeActivity` + `ParentActivityCompletion` (inline-commit, stage nothing).

### `AddWorkflowRuntime(IServiceCollection)` *(engine — `Elsa.Workflows.Runtime`)*
- **Kind:** Composition root (host-agnostic runtime registration).
- **Usage:** RT-4; renamed from `AddWorkflowRuntimeCore` when the composition root moved to the engine package (ADR 0033). Registers the full hosting-agnostic runtime (stores, scheduler queue/drainer, checkpoint committer, pipelines + built-in middleware, dispatcher, ownership fencing, post-commit outbox) so a worker or test harness can compose and drive a drain **without** the API feature. `WorkflowsRuntimeApiFeature` composes it and adds only the API/endpoint concerns. All registrations use `TryAdd`, so a durable provider overrides any store. Reference in-memory state is process-global; persistence-consuming operation graphs are scoped. Host-lifetime actor mailboxes and recurring tasks open and dispose a fresh scope per command or tick (see the composition-root XML docs and `docs/runtime-durable-resumption.md`).

### `WorkflowsRuntimeCheckpointPersistenceFeature` *(Runtime.Api — `Elsa.Workflows.Runtime.Api`)*
- **Kind:** Shell-scoped policy selector and post-provider decorator.
- **Usage:** Configure `Mode` as `Immediate` (default/pass-through) or `Coalesced`, with a positive `MaxSegmentCheckpoints` (default 50). The feature implements `IPostConfigureShellServices` so provider packages first replace the runtime stores and coalescing then wraps the selected implementations. Duplicate composition is idempotent. See `docs/runtime-durable-resumption.md` for the latency, replay, and cap trade-offs.

### `ResumeTargetAttribute` *(Core — `Elsa.Activities.Runtime.Core`)*
- **Kind:** Declaration surface (activity author contract).
- **Signature:** `[ResumeTarget("stable-resume-target-id")]` on an activity handler method.
- **Usage:** declares the stable resume target ID that compile/publish can place into `WorkflowExecutable.ResumeTargets`. Durable bookmarks store the ID, not the C# method name.
- **Not a runtime callback store** — handler method names and delegates are implementation details and are not persisted in `BookmarkState`.
- **Compiler indexing (W8, node-scoped since the BPMN Phase-2 catch-events prerequisite):** `WorkflowExecutableCompiler` reflects `[ResumeTarget]` methods off each node's resolved activity type and indexes them into `WorkflowExecutable.ResumeTargets`. The map key is **node-scoped** (`{executableNodeId}:{attributeId}`, with the attribute id preserved as `LocalResumeTargetId`) — the same rescoped shape the activity-template placer emits — so multiple instances of the same resume-target activity coexist in one workflow (two `Delay`s, or several synthesized BPMN timer catch events). Activities keep registering trigger registrations by their local attribute id; the resume resolver's `(ExecutableNodeId, LocalResumeTargetId)` fallback binds them, and durable bookmarks store the node-scoped id.

### `IRuntimeStructuralActivity` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Engine-only structural activity protocol; not a DI contributor or ordinary activity-author callback surface.
- **Signature:** `ExecuteStructureAsync(IRuntimeActivityExecutionContext context)` returns `ValueTask<RuntimeStructuralContinuation>`.
- **Usage:** lets a composite schedule and coordinate executable children without placing child scheduling or continuation methods on the minimal `IActivity` contract. The initial runtime invocation uses this method instead of `IActivity.ExecuteAsync`. `IRuntimeActivityChildCompletionHandler` and `IRuntimeActivityChildFaultHandler` provide the corresponding parent re-evaluation callbacks.
- **Decision model:** every structural entry point returns one immutable `RuntimeStructuralContinuation`: `Complete(outcome)`, `Defer`, `Faulted(ActivityFault)`, or `Cancel(reason)`. A terminal decision cannot schedule children in the same evaluation. Initial execution may defer only after scheduling at least one child; a child callback may defer while awaiting existing children or after scheduling the next child.
- **Child-subtree cancellation (spec 112):** a child callback may stage `RequestChildSubtreeCancellation(childActivityExecutionId, reason)` against a live direct child. `ActivitySubtreeCancellationPlanner` traverses the subtree, terminalizes cancellable members to `Cancelled`/`ParentCancelled`, captures an `ActivityScopeCleanupRequest` (bookmarks, durable timers, queued scheduler work), and suppresses the subtree's non-terminal incidents; the parent-evaluation handler folds it all into the same checkpoint commit as the continuation (honored with `Defer`/`Complete`; terminal targets skip as a legal race; structural misuse faults the evaluation). The same planner backs the `CancelActivityScope` scheduler command.
- **Child-fault absorption (spec 115):** a child-fault callback may stage `RequestChildFaultAbsorption(incidentId, reason)` naming the evaluation's incident. The incident resolves with an `AbsorbFault` outcome sourced from `StructuralFaultAbsorption`, and the faulted child's leftover subtree is reclaimed via the same planner (`Cancelled`/`FaultAbsorbed` descendants, cleanup capture, other incidents suppressed) in the continuation's commit. Absorption is a runtime-internal structural operation; public strategy actions cannot request it.
- **Trigger identity (spec 089 D / 117 D4):** `IRuntimeActivityExecutionContext` exposes the committed, spoof-proof start identity — `TriggerNodeId` (the matched binding's executable node id) and `TriggerMetadata` (the binding's free-form `string→string` map, carried verbatim from `WorkflowTriggerBinding.Metadata`). Both are seeded onto reserved durable channels at the workflow-started checkpoint (never the `input:*` namespace) and projected back by `RuntimeInputBindingStateProjection`. A structural trigger activity (e.g. `BpmnProcess`) compares `TriggerNodeId` to its own node id to detect a trigger delivery and reads per-descriptor routing facets from `TriggerMetadata` (e.g. the BPMN start element id under `"bpmn.startElementId"`). The stimulus router forwards `binding.Metadata` through the start dispatch; the recurring-trigger pump's `StartOnly` path rides the same router code, so timer-start metadata flows for free.
- **Live child activities (spec 119 D4; spec 121):** a structural activity that also implements the opt-in marker `IRuntimeLiveChildActivityConsumer` receives its parent's direct, non-terminal child executions on `IRuntimeActivityExecutionContext.GetLiveChildActivities()` (each `RuntimeLiveChildActivity` = activity-execution id + executable node id + status + **iteration id**), populated **only** during a child-completion/child-fault evaluation and loaded from committed state via `IActivityExecutionStateStore.ListAllByParentAsync` (read-only, spoof-proof). Non-consumer parents pay nothing; the collection is empty for initial/resume evaluations. This is the lookup that lets a first-catch-wins racer (the BPMN event-based gateway) resolve a losing sibling's node id to its live child activity-execution id before staging that subtree's `RequestChildSubtreeCancellation`. `IterationId` (spec 121, populated from the child's `ActivityExecutionState.IterationId`) disambiguates N concurrent same-node children of a multi-instance host, so a consumer keys the lookup by `(executable node id, iteration id)` rather than node id alone; it is `null` for ordinary single-run children.
- **Scoped-variable read (spec 123 D1):** a structural activity that implements the opt-in marker `IRuntimeScopedVariableReader` may read its enclosing container-scoped variable values through `IRuntimeActivityExecutionContext.TryReadScopedVariableValue(name, out ValueEnvelope? envelope)`, populated by the runtime on the invoke, child-completion/child-fault, and bookmark-resume paths (all three handlers, marker-gated). The projection is built from committed frame state via `RuntimeContainerScopeService.ProjectScopedVariablesForReaderAsync` (which reuses `BuildVisibleFramesAsync` with `includeOwnContainer: true` + `ProjectVisibleVariableEnvelopes`, the same name→key resolution the leaf input-binding path uses), so there is one variable-value truth and no re-implemented envelope-unwrap. Resolution is by name across the activity's own visible chain only — own iteration → own container → ancestors → the workflow root frame — innermost scope winning for a shadowed name; it is read-only, spoof-proof (the ancestor walk is the ownership guard), and exposes committed values only (a concurrent-branch write becomes visible only once committed, on a later evaluation's basis). A non-marker activity, or a name that resolves to no visible declared variable, reads `false` with a `null` envelope — it never throws on an unpopulated seam. `BpmnProcess` uses it to read a collection-mode multi-instance host's collection variable once at loop start (spec 123 D2). The `VariableScope` type and the `variableScope: null` handler arguments are untouched (still unwired).
- **Input re-materialization (issue #977):** a structural activity that implements the opt-in marker `IRuntimeRematerializeInputsOnChildCompletion` has its input snapshot re-materialized from live committed variable frames before every child-completion/child-fault evaluation (`RuntimeActivityInputSnapshotMaterializer`, the same materialization the start handler pins the entry snapshot with, so frame visibility is identical to entry — ancestors + root, not the own container). The fresh snapshot is **transient**: the pinned `ActivityExecutionState.InputSnapshot` committed at activation stays immutable (ADR 0045 §3's invocation record is unchanged), and the re-read is a pure function of committed frame state, so redelivery converges. Non-marker composites reuse the pinned snapshot byte-for-byte; the notification and bookmark-resume evaluations never re-materialize. `While` is the sole implementer — its loop condition must observe a body's committed `Set` of the condition variable to terminate.
- **Known implementations (shipped):** `Sequence`, `Flowchart`, `Parallel`, `If`, `Switch`, `For`, `ForEach`, `While` (also an `IRuntimeRematerializeInputsOnChildCompletion`, issue #977), `Do`, and `BpmnProcess` (also a `[TriggerActivity]`, spec 117, an `IRuntimeLiveChildActivityConsumer`, spec 119, and an `IRuntimeScopedVariableReader`, spec 123).

### `IActivityTriggerStimulusProvider` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Strategy set (context-selected, exact-one owner per executable trigger node; not a contributor fan-in).
- **Signature:** additive stable nonblank `ProviderId`; additive `Cardinality` (`FanOut` compatibility default); `ActivityTriggerStimulusResult Describe(ExecutableNode node)`.
- **Usage:** at **publish time** the trigger extractor evaluates every registered strategy once for each compiler-marked node. Exactly one strategy must return `Recognized`; zero owners and multiple owners fail rather than selecting by registration order. The selected provider's stable id appears in the non-persisted preflight outcome and contextual typed failures. `Recognized([...])` carries the node's stimulus identities `(stimulusType, stimulusHash, correlationScope?, metadata)`. `Recognized([])` deliberately means an intentional non-start (for example a mid-flow `HttpEndpoint` with `CanStartWorkflow = false`) and succeeds without a binding. Providers read only the pinned published `ExecutableNode`, never Design state or a running workflow. Public contract XML documents `WorkflowTriggerPreflightException`; parser/calculator exceptions are wrapped at the publication boundary and retained as inner exceptions.
- **Register:** `services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityTriggerStimulusProvider, MyProvider>())`.

**Known implementations (shipped):**
- `Elsa.Activities.Primitives` — `EventTriggerStimulusProvider` *(cross-domain — describes the named-event `Event` start trigger; stimulus type `Event`, hash over the event name)*.
- `Elsa.Activities.Http` — `HttpEndpointTriggerStimulusProvider` *(cross-domain — describes the `HttpEndpoint` start trigger; stimulus type `HttpEndpoint`, hash over the normalized request path so an inbound request routes to the matching published endpoint)*.
- `Elsa.Activities.Scheduling` — `TimerTriggerStimulusProvider` / `CronTriggerStimulusProvider` *(cross-domain — describe the `Timer` and `Cron` recurring start triggers; stimulus types `Timer` / `Cron`, hash over the interval / cron expression. These pair with the recurring-trigger schedule store + pump below: the stimulus identity is what the pump fires through `IStimulusRouter`, and the same providers also implement `IRecurringTriggerScheduleProvider` to register the schedule at publish time)*.
- `Elsa.Activities.Bpmn` — `BpmnProcessTriggerStimulusProvider` *(cross-domain, spec 117 — describes a `BpmnProcess`'s event-defined start events: one descriptor per message/signal start (named-event stimulus, same `(type, hash)` as `Event`) and per timer start (BPMN-owned `Bpmn.TimerStart` stimulus, element id folded into the hash), each carrying the start element id in `Metadata`. Pairs with `BpmnProcessRecurringScheduleProvider` for timer starts. `Recognized([])` for a none-start-only or nested (`CanStartWorkflow = false`) process)*.

---

## Coalescing checkpoint persistence *(opt-in — W9 / E3-6 / RT-10)*

The burst-coalescing persistence policy is an **opt-in** durability/throughput trade, enabled with
`services.AddCoalescingRuntimeCheckpointPersistence()` (in `Elsa.Workflows.Runtime.Api.Coalescing`). It is
**not** registered by default: the default runtime keeps `ImmediateRuntimeCheckpointPersistencePolicy` and
the contracts/decorators below are absent, so the default path is byte-identical. When enabled, it swaps the
policy to `CoalescingRuntimeCheckpointPersistencePolicy` and layers ambient-session decorators over the
checkpoint commit store, scheduler queue, post-commit outbox, and state stores (including a durable-baseline
read memo over the inspection store — spec 131). See
[`docs/runtime-durable-resumption.md`](../../../../docs/runtime-durable-resumption.md#coalescing-checkpoint-persistence--the-deferred-flush-window-e3-6--rt-10)
and the [benchmark results](../../../../docs/reports/elsa-4-architecture-review-2026-07/w9-checkpoint-coalescing-benchmark.md).

> **Documented ADR 0033 deviation.** The two coalescing interfaces below moved to the
> `Elsa.Workflows.Runtime` engine package instead of staying in `.Core` with the other contracts:
> both expose the concrete `RuntimeCoalescingSession` (engine working state) on their signatures, so
> they cannot stand ahead of the engine, and their only consumers are the opt-in coalescing
> composition in `Runtime.Api` plus its tests. They keep their `Elsa.Workflows.Runtime.Core.Contracts`
> namespace. This canonical catalog covers both the contracts and engine-hosted deviations.

### `IRuntimeCoalescingSessionAccessor` *(engine — `Elsa.Workflows.Runtime`)*
- **Kind:** Replacement (one ambient accessor exposes the active coalescing session to the decorators).
- **Signature:** `RuntimeCoalescingSession? Current { get; }`, `IDisposable Push(RuntimeCoalescingSession? session)`.
- **Usage:** an `AsyncLocal` push/pop stack that makes the current drain segment's in-memory working set ambient to the coalescing store/queue/outbox decorators, mirroring the existing ambient ownership-scope resolution. Only registered by the opt-in extension.
- **Default implementation:** `AsyncLocalRuntimeCoalescingSessionAccessor` *(opt-in only)*.

### `IRuntimeCoalescingDrainScopeFactory` *(engine — `Elsa.Workflows.Runtime`)*
- **Kind:** Replacement (one factory opens the per-drain coalescing scope and performs the quiescence flush).
- **Signature:** `IRuntimeCoalescingDrainScope Begin(string workflowExecutionId, int? maxSegmentCheckpoints = null)`; scope exposes `RuntimeCoalescingSession Session` and `ValueTask FlushAtQuiescenceAsync(CancellationToken)`. The optional cap overrides the host-configured `MaxSegmentCheckpoints` for that run (ADR 0032 R5 per-workflow authored cadence).
- **Usage:** `WorkflowDrainOrchestrator` opens a scope around a drain when the factory is registered (greediest resolvable ctor), buffers intra-drain checkpoints in the session, and flushes one folded atomic commit at quiescence through `RuntimeCheckpointCommitter.CommitAsync` (so W5 ownership fencing still gates it). Per execution, the orchestrator first consults `IRuntimeCheckpointCadenceResolver` (per-run stamp > authored-on-executable cadence > host default) and skips the scope entirely for an authored-Immediate run. Only registered by the opt-in extension.
- **Default implementation:** `RuntimeCoalescingDrainScopeFactory` *(opt-in only)*.

### `IRuntimeLiveDrainDeliveryAccessor` *(engine — `Elsa.Workflows.Runtime`)*
- **Kind:** Replacement (one ambient accessor marks the live drain that owns an execution's post-commit intent delivery — WU-2, spec 105-runtime-live-drain-delivery).
- **Signature:** `RuntimeLiveDrainDeliveryScope? Current { get; }`, `IDisposable Push(RuntimeLiveDrainDeliveryScope? scope)`.
- **Usage:** an `AsyncLocal` push/pop stack, mirroring `IRuntimeCoalescingSessionAccessor`. `WorkflowDrainOrchestrator.DrainCoreAsync` pushes a scope on the Immediate (no coalescing scope factory) branch, bounded by the RT-2 single-writer lease. While the scope is ambient, `RuntimePostCommitOutboxProcessor` delivers `EnqueueSchedulerWork` intents for the owning execution in-memory — idempotent `EnqueueAsync` plus a direct `Pending -> Delivered` mark via the existing non-claim recording contract — skipping the durable claim round-trip. It never engages while a coalescing session is active (the overlay is authoritative) and leaves other intent kinds, other executions, and unmarked callers (recovery sweep, v1 stores) on the durable claim path. Registered by default because Immediate is the default cost model.
- **Default implementation:** `AsyncLocalRuntimeLiveDrainDeliveryAccessor`.

## Cross-references

- HTTP endpoint behaviour overrides: [`Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md`](Http/EXTENSION_POINTS.md).
- Repo-wide index: [`EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.2 + §2.22.1.

## Runtime alteration contracts

| Contract | Ownership and use |
|---|---|
| `IWorkflowAlterationStore` | Single-owner durable plan/target/job storage. Its claim, seal, terminal, and paging operations preserve fenced at-least-once execution. |
| `IWorkflowAlterationPayloadProtector` | Protects deferred envelope JSON with plan/tenant/request associated data. Durable hosts replace the development-only key material with a restart-stable retained key ring. |
| `IWorkflowAlterationRegistry` | Singleton descriptor-only catalog. It returns stable kind/version/display metadata and never resolves handler instances. |
| `IWorkflowAlterationPreflightHandler` | Scoped, trusted custom contribution registered through `AddWorkflowAlterationHandler<T>`. It validates its own payload and may only update projection/staging state. |
| `IWorkflowAlterationProjectedState` | Per-job mutable in-memory view shared in envelope order. A handler can expose its staged logical state to later handlers without persistence. |
| `IWorkflowAlterationStagingWorkspace` | Per-job collector for deterministic checkpoint-bound changes. Duplicate change keys fail preflight. |
| `IWorkflowAlterationRuntimeCheckpointStagedChange` | Handler-owned change that applies through the Runtime alteration checkpoint builder after all preflights pass. |
| `IWorkflowAlterationCheckpointWriter` | Single-owner atomic checkpoint collaborator. It persists terminal job evidence with workflow/activity/bookmark/value/scheduler/outbox changes and supports acknowledgement reconciliation. |
| `IWorkflowAlterationHandlerResolver` | Scoped exact `(kind, schemaVersion)` resolver. Hosts do not replace it to choose a handler dynamically or infer a latest version. |

Custom handlers are trusted code. They must use only stable envelope identity, must not serialize or log payloads,
must return bounded safe failures, and must make their changes replay-safe under the deterministic job checkpoint.
They must not use the alteration path to bypass activity ownership, authorization sealed at submission, workflow actor
serialization, or Runtime's persistence/checkpoint boundary.

The public envelope and persisted plan use only `kind`, `schemaVersion`, and protected JSON. Do not persist a CLR
type name, assembly-qualified type, service key, or executable payload schema type as an alteration identity.
