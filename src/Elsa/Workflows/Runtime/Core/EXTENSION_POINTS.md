# Extension points — Workflows.Runtime domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Workflows.Runtime.Core`; provider/default implementations may live in sibling runtime projects. Runtime execution contracts are Design-free and operate on runtime-owned executable artifacts and execution state.

> **ADR 0033 split.** Since the contracts/engine split, `Elsa.Workflows.Runtime.Core` holds only the
> contracts, models, constants, pipeline contract surface (middleware attribute/bases/placeholders,
> `RuntimePipelinePlanBuilder`), and validators. Unless an entry states otherwise, every **default
> implementation** named in this catalog now lives in the sibling
> [`Elsa.Workflows.Runtime`](../EXTENSION_POINTS.md) implementation package (moved types keep their
> `Elsa.Workflows.Runtime.Core.*` namespaces). Replacing a default still works the same way: register
> your implementation against the `.Core` contract; the engine package's registrations all use
> `TryAdd*`.

---

## Overridable replacement contracts

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
- **Usage:** one sweep pass re-delivers stranded post-commit outbox items system-wide (`ProcessAsync(workflowExecutionId: null, intentKind: EnqueueSchedulerWork)`), unions durable scheduler-queue backlog (`IWorkflowSchedulerWorkQueue.ListPendingWorkflowExecutionIdsAsync`) with `IRuntimeRecoveryScanner` candidates, and re-drives each discovered execution by enqueueing a `RunSchedulerWork` envelope through the actor mailbox — preserving single-writer discipline. The request bounds each sweep (`MaxExecutionsPerSweep`) and skips executions the caller is backing off (`ExcludedWorkflowExecutionIds`). Re-drive failures surface on the result and do not abort the sweep; callers own logging and backoff. It is not registered by the runtime API feature — only the `WorkflowsRuntimeResumption` shell feature registers it and drives it from a recurring pump.
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
- **Signature:** `DispatchAsync(BookmarkResumeDispatchRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** uses `IBookmarkStimulusLookup`, workflow execution state, the pinned executable artifact, and `IBookmarkResumeResolver` to enqueue a `ResumeBookmark` command through `IWorkflowExecutionActorProvider`. The command payload carries `ResumeTargetId`, not C# callback method names. It does not consume bookmarks or invoke activity resume handlers.
- **Default implementation:** `BookmarkResumeDispatcher`.

### `IBookmarkStimulusIndex` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (narrow cross-execution read surface over bookmark state; segregated from `IBookmarkStateStore` so it can be widened/replaced independently).
- **Signature:** `ListByStimulusAsync(string stimulusType, string stimulusHash, CancellationToken cancellationToken = default)`; `ListByStimulusTypeAsync(string stimulusType, CancellationToken cancellationToken = default)` *(spec 089 D)*.
- **Usage:** returns raw `BookmarkState` records matching a stimulus **across every workflow execution** (E3-5 fan-in), unlike `IBookmarkStimulusLookup` which is scoped to one `workflowExecutionId`. `ListByStimulusAsync` matches (type, hash); `ListByStimulusTypeAsync` (spec 089 D) is a hash-agnostic type-scoped scan that enumerates every waiting bookmark of a stimulus family (e.g. all `HttpEndpoint` bookmarks) so the route-table resolver can union their templates. Both are raw scans — neither filters expiry or correlation (that stays in `IGlobalBookmarkStimulusLookup`). Implemented by the bookmark state store itself (in-memory and Groundwork). `ListByStimulusAsync` uses the additive `bookmarkState` `by-stimulus` (hash) index; `ListByStimulusTypeAsync` — like the sibling `GroundworkWorkflowTriggerBindingStore.ListByStimulusTypeAsync` (spec 089 B) — does a clause-free full scan narrowed by type in code (the hash index cannot serve a type-only query), so NO new index is added and `SchemaVersion` is unchanged; it feeds the route-table refresh, not a hot per-request path. Note the Condition 7 gap (see [`docs/serialization.md`](../../../../../docs/serialization.md)): bookmarks written before the `by-stimulus` index existed are not backfilled until re-saved.
- **Default implementation:** `InMemoryBookmarkStateStore` / `GroundworkBookmarkStateStore` *(each also implements this interface)*.

### `IGlobalBookmarkStimulusLookup` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one cross-execution lookup surface finds every waiting bookmark for a stimulus).
- **Signature:** `FindWaitingAsync(GlobalBookmarkStimulusLookupRequest request, CancellationToken cancellationToken = default)`; `FindWaitingByTypeAsync(GlobalBookmarkStimulusTypeLookupRequest request, CancellationToken cancellationToken = default)` *(spec 089 D)*.
- **Usage:** builds the fan-in resume set for the stimulus router by querying `IBookmarkStimulusIndex`, filtering expired bookmarks against the evaluated time and (when supplied) a passive correlation scope carried in bookmark metadata. Correlation is a threaded metadata value only — not a correlation subsystem. `FindWaitingByTypeAsync` (spec 089 D) is the type-scoped counterpart used by the mid-flow HttpEndpoint route-table resolver and middleware: it returns the non-expired `Matches` snapshots (incl. `Metadata`) for a stimulus type regardless of hash, so a consumer can read the durable route template + endpoint options a mid-flow suspension stored. Expiry filtering lives here, not in the raw index; no correlation scoping (mid-flow bookmark resumes are instance-scoped).
- **Default implementation:** `GlobalBookmarkStimulusLookup`.

### `IWorkflowTriggerBindingStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one provider owns the durable trigger-binding index for a runtime composition).
- **Signature:** `SaveAsync(...)`, `ListByStimulusAsync(stimulusType, stimulusHash, ...)`, `ListByStimulusTypeAsync(stimulusType, ...)`, `ListByArtifactAsync(artifactId, ...)`, `DeleteByArtifactAsync(artifactId, ...)`.
- **Usage:** stores `WorkflowTriggerBinding` documents mapping a stimulus identity to a start-trigger inside a published artifact. `ListByStimulus` is the cross-artifact fan-out the router uses to start every workflow waiting on a stimulus; `ListByStimulusType` is a type-scoped full scan (no hash) used to rebuild a per-shell projection over one stimulus family (e.g. the HTTP route table); `by-artifact` scoping supports republish replacement.
- **Default implementation:** `InMemoryWorkflowTriggerBindingStore` *(single-node in-memory default; `GroundworkWorkflowTriggerBindingStore` replaces it for durable storage over the `workflowTriggerBinding` document kind)*.

### `IWorkflowTriggerBindingExtractor` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one extractor derives trigger bindings from a published executable).
- **Signature:** `Extract(WorkflowExecutable executable)`.
- **Usage:** walks the pinned executable's node tree, selects compiler-marked start-trigger nodes, and resolves each one's stimulus identity through the registered `IActivityTriggerStimulusProvider` set. A start-trigger node **no** provider recognizes (`ActivityTriggerStimulusResult.NotRecognized` from every provider) **throws**, failing the publish rather than persisting an unroutable trigger; a node a provider recognizes but returns **zero** descriptors for (a declared non-start, spec 089 D) yields no bindings without throwing.
- **Default implementation:** `WorkflowTriggerBindingExtractor`.

### `IWorkflowTriggerIndexer` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one indexer writes the trigger index for a published artifact).
- **Signature:** `IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)`.
- **Usage:** invoked inside the publish flow; extracts bindings (an unroutable trigger throws before any write) then replaces the artifact's prior bindings with the current set (delete-by-artifact then write) so a republished version's triggers fully supersede the previous version's. After the write succeeds — before returning — it notifies every `IWorkflowTriggerIndexObserver` with the artifact's new bindings. Any failure (including an observer's) propagates and **fails the publish** — no silently unindexed trigger.
- **Default implementation:** `WorkflowTriggerIndexer`.

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
- **Usage:** the E3-1/E3-5 spine. Snapshots the cross-execution resume set (via `IGlobalBookmarkStimulusLookup`) **before** starting new instances, starts matching published triggers (via `IWorkflowStartDispatcher`, deduped by `IStimulusStartDeduplicator` when an idempotency key is present), and resumes each waiting instance (via `IBookmarkResumeDispatcher`). All dispatch routes through the actor mailbox — the single-writer invariant is preserved. Correlation scope is a passive threaded metadata value. **Stimulus-input delivery (spec 089 A):** `StimulusDispatchRequest.Input` reaches BOTH sides — resumes receive it as the resume input (as before), and starts receive it on the first-class `WorkflowExecutionStartDispatchRequest.StimulusInput` field, seeded onto a reserved durable channel (`RuntimeMetadataKeys.StimulusInputName`, value-id prefix `stimulus:`) and surfaced to activities via `IExecutionExpressionState.StimulusInput`. Deliberately NOT the workflow-inputs bag: the payload can neither collide with an author-declared input nor be forged through the execute API's inputs map. **Trigger-node identity (spec 089 D):** the matched binding's `ExecutableNodeId` rides an analogous reserved channel — `StimulusRouter` forwards it on the first-class `WorkflowExecutionStartDispatchRequest.TriggerNodeId` field, seeded onto a reserved durable channel (`RuntimeMetadataKeys.TriggerNodeId`, value-id prefix `trigger:`) and surfaced via `IExecutionExpressionState.TriggerNodeId`, so a mid-flow-capable activity (e.g. `HttpEndpoint`) can tell whether it is the node that triggered this run. Null on direct (non-trigger) starts and resume-only paths; same collision/spoof-proofing.
- **Default implementation:** `StimulusRouter`.

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
- **Signature:** `WorkflowInstanceId`, `CorrelationId`, `WorkflowName`, `WorkflowDefinitionId`, `WorkflowDefinitionVersionId`, `WorkflowDefinitionVersion`, `WorkflowInputs`, `StimulusInput`, `TriggerNodeId`, `ResumeInput`, `WorkflowVariables`, `ActivityOutputValues`.
- **Usage:** implemented by the execution-time `IExpressionExecutionContext` (`SimpleActivityExecutionContext`) so expression-language pre/post-processors resolve execution-time identity functions, named pascalized accessors, execution-time output accessors, and JavaScript variable write-back **without a DI-registered live workflow execution context** (ADR 0030 D1; retires `IWorkflowExecutionContext`). Populated by `WorkflowInvokeActivitySchedulerWorkHandler`: identity from `WorkflowExecutionState` + the pinned `WorkflowExecutableIdentity`; `WorkflowVariables`/`WorkflowInputs`/`ActivityOutputValues` from the durable-value projections (`RuntimeInputBindingStateProjection`). Variable writes route through the visible `VariableScope` (`IScopedVariableProvider`) and fold into the checkpoint-commit durable-value write-back (`BuildWorkflowScopeWriteBackChanges`) — no second persistence route. `StimulusInput`/`TriggerNodeId` come from the same durable-value projection set; `ResumeInput` is populated only on the resume path (`WorkflowResumeBookmarkSchedulerWorkHandler` stashes the resume dispatch's input onto the carrier, spec 089 D) as a live per-invocation value — never durable state — so a context-shaped `[ResumeTarget]` reads the resuming request payload while keeping full `Set`/output access; null on start/invoke/parent-completion. A narrow marker, not a general transient-properties bag (ADR 0030 Q3); Design-free (§E2.2/§E2.6).
- **Default implementation:** `SimpleActivityExecutionContext`; consumed by the re-pointed JavaScript pre/post-processors (`WorkflowFunctionsPreProcessor`, `WorkflowInputFunctionsPreProcessor`, `VariableFunctionsPreProcessor`, `ActivityOutputFunctionsPreProcessor`, `CopyVariablesToWorkflowContext`) in *Elsa.Workflows.Runtime.JavaScript*. The `JavaScriptWorkflowsRuntimeFeature` registration is covered by a resolve-and-evaluate guardrail test (ADR 0030 D4).

### `IRuntimePayloadCapturePolicy` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one policy decides which runtime observability payloads may be captured for a runtime composition).
- **Signature:** `Decide(RuntimePayloadCaptureRequest request)`.
- **Usage:** controls whether history, diagnostics, incidents, values, and input/output observations capture no payload, metadata only, or full payload. Continuation state does not read these observability payloads. The default excludes sensitive values and omits workflow/activity input and output snapshots.
- **Default implementation:** `DefaultRuntimePayloadCapturePolicy` *(intra-domain default)*.

### `IWorkflowExecutionActorProvider` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one provider owns workflow-execution mailbox activation, routing, and passivation for a runtime composition).
- **Signature:** `Capabilities`, `GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default)`, `PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** provider implementations enforce one active mailbox/agent per `WorkflowExecutionId`. Commands are delivered through `WorkflowExecutionCommandEnvelope`, which carries command identity, workflow execution ID, idempotency key, optional sequence, delivery mode, and metadata. Actor frameworks are provider choices; checkpoint state remains the source of truth.
- **Default implementation:** `InProcessWorkflowExecutionActorProvider` *(single-node actor-like mailbox; no distributed placement or actor framework dependency)*.
- **Alternative implementation:** `DistributedWorkflowExecutionActorProvider` *(opt-in leaf `Elsa.Workflows.Runtime.Distributed`, W20/E3-3)* — clustered placement/routing over the in-process provider: claims per-execution placement, returns the local in-process actor when this node owns the execution, or a `ForwardingWorkflowExecutionActor` (durable-transport routing stub, `Deferred` result) when another node owns it. Placement is best-effort routing; W5 fencing at checkpoint commit is the authoritative double-execution guard. Enable with the `WorkflowsRuntimeDistributed` shell feature. See the Distributed placement and transport section below.

### Distributed placement and transport *(leaf — `Elsa.Workflows.Runtime.Distributed`, W20/E3-3)*

Leaf-owned contracts for clustered workflow-execution placement and cross-node command routing. Per §2.7 these live entirely in the provider leaf; `Elsa.Workflows.Runtime.Core` gains zero references to them. The leaf consumes the W5 single-writer fencing seam (`IRuntimeExecutionOwnershipService`) unchanged — placement decides *which* node drains (routing), fencing decides *whether* a write commits (safety).

- ### `IExecutionPlacementStore` *(leaf)*
  - **Kind:** Replacement (one store owns per-execution placement lease records for a distributed composition).
  - **Signature:** `TryClaimAsync`, `FindAsync`, `ReleaseAsync`, `ListAsync` (compare-and-swap on placement token; claim doubles as renew).
  - **Usage:** the CAS claim/renew primitive under placement ownership. Claiming an unowned or expired placement issues a strictly greater placement token; a claim against a live foreign lease fails without mutation.
  - **Default implementation:** `InMemoryExecutionPlacementStore` *(single-process/two-node-harness default)*. `GroundworkExecutionPlacementStore` *(durable `IDocumentStore`-backed bridge, W27 — opt-in leaf `Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork`, swapped in by the `WorkflowsRuntimeDistributedGroundworkPersistence` feature; exact cross-node CAS via the provider's ExpectedVersion contract, including create-only first claims)*.
- ### `IExecutionPlacementService` *(leaf)*
  - **Kind:** Replacement (one service owns this node's placement acquisition/renewal/release policy).
  - **Signature:** `NodeId`, `TryClaimAsync`, `FindOwnerAsync`, `ListOwnedAsync`, `ReleaseAsync`.
  - **Usage:** wraps the store with this node's identity, lease duration, and `TimeProvider` so all lease timing is deterministic and options-driven.
  - **Default implementation:** `ExecutionPlacementService`.
- ### `IExecutionCommandTransport` *(leaf)*
  - **Kind:** Replacement (one transport owns the durable cross-node command inbox for a distributed composition).
  - **Signature:** `SendAsync`, `LeaseAsync`, `AckAsync`, `ListPendingExecutionIdsAsync`, `CountPendingAsync` (ack-based lease/visibility, at-least-once).
  - **Usage:** commands for an execution owned by another node are durably enqueued, then leased/acked by the owning node's pump. A lease hides an item from other nodes until acked or expired; only the live lease holder may ack, so a superseded node's ack is refused and the item is re-driven on failover. Wire shape is frozen by the committed v1 golden fixture (§E6 kind `executionCommandTransport`).
  - **Default implementation:** `InMemoryExecutionCommandTransport` *(single-process/two-node-harness default)*. `GroundworkExecutionCommandTransport` *(durable `IDocumentStore`-backed bridge, W27 — same leaf/feature as the placement store; persists the frozen v1 `executionCommandTransport` item shape, store-enforced unique per-execution sequences, version-guarded lease/ack CAS)*.

### `ExecutionPlacementPumpTask` *(leaf — `Elsa.Workflows.Runtime.Distributed`, W20/E3-3)*
- **Kind:** Registered recurring task (`IRecurringTask`; one per node, DependsOn Tasks).
- **Usage:** each bounded sweep renews the placements this node holds, then discovers executions with visible transport backlog, claims any it can own, leases their commands, dispatches each to the local actor, and acks on a delivered outcome. Deferred/Rejected dispatches stay leased so lease expiry re-drives them (the failover loop). All cadence/bounds come from `ExecutionPlacementPumpOptions` evaluated against `TimeProvider`; a failing sweep is logged, never rethrown, and widens the interval geometrically.


- **Kind:** Replacement (one generator owns runtime command-dispatch IDs for a runtime composition).
- **Signature:** `NewWorkflowExecutionId()`, `NewWorkflowExecutionCommandId()`, `NewWorkflowExecutionCommandEnvelopeId()`, `NewActivityExecutionId()`.
- **Usage:** provides runtime-owned identifiers for workflow execution start dispatch and concrete activity executions without leaking API, persistence, or provider-specific identity generation into command construction.
- **Default implementation:** `ShortRuntimeExecutionIdGenerator`.

### `IWorkflowStartDispatcher` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one dispatcher owns conversion from executable artifact start requests into workflow execution agent commands).
- **Signature:** `DispatchAsync(WorkflowExecutionStartDispatchRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** loads the runtime-owned executable artifact, pins its exact identity in a `WorkflowExecutionCommandKind.Start` payload, activates the workflow execution agent, and enqueues the command envelope. It does not execute activities inline.
- **Default implementation:** `WorkflowExecutionStartDispatcher`.

### `IWorkflowExecutionCommandExecutor` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one processor decides what an accepted workflow-execution command does inside the active actor mailbox).
- **Signature:** `ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)`.
- **Usage:** invoked by the in-process agent after dispatch metadata has been accepted and before the idempotency key is marked processed. The processor runs under the actor mailbox's single-writer boundary.
- **Default implementation:** `WorkflowSchedulerCommandRouter` *(records accepted commands as scheduler work, applies the scheduler drain policy, then delegates command-triggered draining to `IWorkflowDrainOrchestrator`; activity execution remains handler/provider behavior, not command acceptance behavior)*.

### `IWorkflowDrainOrchestrator` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one coordinator owns command-triggered workflow execution drain orchestration for a runtime composition).
- **Signature:** `DrainAsync(WorkflowExecutionCommandEnvelope envelope, RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** bridges the accepted command boundary to scheduler draining after scheduler work is recorded and the drain policy requests immediate advancement. The default coordinator drains scheduler work, processes deliverable `RuntimePostCommitIntentKinds.EnqueueSchedulerWork` outbox items for the same workflow execution, and repeats scheduler draining until scheduler-intent delivery quiesces, a pause/fault stops scheduler draining, or the bounded cycle guard is reached. Checkpoint commit remains the durability boundary: commits record post-commit work, and the coordinator only delivers it after the commit path succeeds. `WorkflowDrainOrchestratorOptions` names the cycle and outbox batch limits; cycle-cap exhaustion throws `DrainCycleLimitExceededException`.
- **Default implementation:** `WorkflowDrainOrchestrator`.

### `IRuntimeExecutionOwnershipService` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one service owns single-writer fencing — lease acquisition, heartbeat, release, and stale-writer rejection — for a runtime composition).
- **Signature:** `AcquireAsync(string workflowExecutionId, ...)`, `HeartbeatAsync(RuntimeExecutionLease lease, ...)`, `ReleaseAsync(RuntimeExecutionLease lease, ...)`, `EnsureCurrentAsync(string workflowExecutionId, long fencingToken, ...)`.
- **Usage:** enforces RT-2 single-writer ownership. `WorkflowDrainOrchestrator` acquires a lease at the start of a drain, pushes it onto `IRuntimeExecutionOwnershipContextAccessor`, and releases it in a `finally` (so a crash leaves the lease persisted for the recovery scanner to detect — closing W2's post-dequeue/pre-commit window). `RuntimeCheckpointCommitter` calls `EnsureCurrentAsync` at the single checkpoint-commit funnel and throws `RuntimeStaleFencingTokenException` when the presented fencing token is not the current one (equality is the only pass; tokens are strictly monotonic and never reused across release). Ownership state is backed by `IExecutionLivenessStateStore`; the lease `ExpiresAt` reuses the recovery scanner's existing lease-timeout honoring rather than a parallel knob.
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
- **Default implementation:** `InMemoryWorkflowSchedulerWorkQueue` *(single-node in-memory default for the current runtime slice)*. `GroundworkWorkflowSchedulerWorkQueue` *(durable `IDocumentStore`-backed bridge; swapped in by `AddGroundworkRuntimeStores` so scheduler work survives a process crash — see [docs/runtime-durable-resumption.md](../../../../../docs/runtime-durable-resumption.md))*.

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
- **Usage:** persists `RecurringTriggerSchedule` records (a next-occurrence-indexed document kind, `recurringTriggerSchedule`) for Timer/Cron **start** triggers — the recurring-start counterpart to `IDurableTimerStore` (which is resume-only). `SaveAsync` is an idempotent upsert keyed by `ScheduleId`; republishing an artifact replaces its schedules via `DeleteByArtifactAsync` + re-save, mirroring the trigger index. The recurring-trigger pump (`RecurringTriggerPumpTask`, `WorkflowsRuntimeRecurringTriggers` feature) drains `ListDueAsync` and, for each due schedule, **claims the occurrence with `TryAdvanceAsync` (compare-and-swap on `NextOccurrence`) before firing** the trigger stimulus through `IStimulusRouter`. **Missed-occurrence policy:** on pump wake after downtime a schedule fires **at most once** and advances straight to the next future occurrence — the backlog is never replayed. **Cluster-safety hook (W20):** `TryAdvanceAsync` is the compare-and-swap a future clustered store keeps so at most one node fires an occurrence, without changing the pump.
- **Default implementation:** `InMemoryRecurringTriggerScheduleStore` *(single-node in-memory default; start triggers work but are **not** restart-durable without a durable store)*. `GroundworkRecurringTriggerScheduleStore` *(durable `IDocumentStore`-backed bridge; swapped in by `AddGroundworkRuntimeStores`)*.

### `IRecurringTriggerScheduleProvider` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contributor (fan-in; one provider per recurring-trigger activity type, resolved as `IEnumerable<T>` by the schedule indexer).
- **Signature:** `RecurringScheduleDescriptor? Describe(ExecutableNode node);`
- **Usage:** the recurring-schedule sibling of `IActivityTriggerStimulusProvider`. At **publish time** the schedule indexer asks each provider to describe a node; a provider returns the node's recurrence spec (interval / cron expression → next occurrence) when it recognizes the activity type, or `null` ("not mine"). A recurring-trigger activity contributes **both** seams: one trigger binding (so the router can route the pump's stimulus) and one schedule (so the pump knows when to fire). Providers read only the pinned published `ExecutableNode`; a non-literal recurrence spec **throws**, failing the publish rather than persisting an unfireable schedule.
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

### `IExecutionLivenessStateStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns split continuation state for runtime operational coordination in a runtime composition).
- **Signature:** `SaveAsync(ExecutionLivenessState state, ...)`, `FindAsync(string workflowExecutionId, string operationalStateId, ...)`, `ListAsync(string workflowExecutionId, ...)`, `ListAllAsync(...)`.
- **Usage:** stores `ExecutionLivenessState` keyed by `WorkflowExecutionId` and `OperationalStateId`. The in-memory checkpoint writer projects operational state upserts from accepted checkpoint commits into this store. Recovery scanning, outbox delivery processing, domain retry, and actor-provider lease enforcement remain separate runtime surfaces.
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
- **Usage:** RT-4; renamed from `AddWorkflowRuntimeCore` when the composition root moved to the engine package (ADR 0033). Registers the full hosting-agnostic runtime (stores, scheduler queue/drainer, checkpoint committer, pipelines + built-in middleware, dispatcher, ownership fencing, post-commit outbox) so a worker or test harness can compose and drive a drain **without** the API feature. `WorkflowsRuntimeApiFeature` composes it and adds only the API/endpoint concerns. All registrations use `TryAdd`, so a durable provider overrides any store; the reference stores/handlers are process-global **singletons** by design (see the composition-root XML docs and `docs/runtime-durable-resumption.md`).

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

### `IActivityTriggerStimulusProvider` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Contributor (fan-in; one provider per start-trigger activity type, resolved as `IEnumerable<T>` by `IWorkflowTriggerBindingExtractor`).
- **Signature:** `ActivityTriggerStimulusResult Describe(ExecutableNode node);`
- **Usage:** at **publish time** the trigger extractor asks each registered provider to describe a node; a provider returns `ActivityTriggerStimulusResult.Recognized([...])` carrying the node's stimulus identities `(stimulusType, stimulusHash, correlationScope?, metadata)` when it owns the activity type, or `ActivityTriggerStimulusResult.NotRecognized` ("not mine"). A **recognized** result may carry **zero** descriptors when the node declares itself a non-start (spec 089 D: a mid-flow `HttpEndpoint` with `CanStartWorkflow = false`) — that yields no trigger bindings and, crucially, does **not** fail the publish (a bare empty collection could not express this, being indistinguishable from "not mine"). Providers read only the pinned published `ExecutableNode` (its literal input bindings), never a running workflow, so stimulus identity is fixed at publish time. A provider whose trigger carries a non-literal, unresolvable stimulus key **throws**, failing the publish. A start-trigger node that **no** provider recognizes also **throws**.
- **Register:** `services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityTriggerStimulusProvider, MyProvider>())`.

**Known implementations (shipped):**
- `Elsa.Activities.Primitives` — `EventTriggerStimulusProvider` *(cross-domain — describes the named-event `Event` start trigger; stimulus type `Event`, hash over the event name)*.
- `Elsa.Activities.Http` — `HttpEndpointTriggerStimulusProvider` *(cross-domain — describes the `HttpEndpoint` start trigger; stimulus type `HttpEndpoint`, hash over the normalized request path so an inbound request routes to the matching published endpoint)*.
- `Elsa.Activities.Scheduling` — `TimerTriggerStimulusProvider` / `CronTriggerStimulusProvider` *(cross-domain — describe the `Timer` and `Cron` recurring start triggers; stimulus types `Timer` / `Cron`, hash over the interval / cron expression. These pair with the recurring-trigger schedule store + pump below: the stimulus identity is what the pump fires through `IStimulusRouter`, and the same providers also implement `IRecurringTriggerScheduleProvider` to register the schedule at publish time)*.

---

## Coalescing checkpoint persistence *(opt-in — W9 / E3-6 / RT-10)*

The burst-coalescing persistence policy is an **opt-in** durability/throughput trade, enabled with
`services.AddCoalescingRuntimeCheckpointPersistence()` (in `Elsa.Workflows.Runtime.Api.Coalescing`). It is
**not** registered by default: the default runtime keeps `ImmediateRuntimeCheckpointPersistencePolicy` and
the contracts/decorators below are absent, so the default path is byte-identical. When enabled, it swaps the
policy to `CoalescingRuntimeCheckpointPersistencePolicy` and layers ambient-session decorators over the
checkpoint commit store, scheduler queue, post-commit outbox, and state stores. See
[`docs/runtime-durable-resumption.md`](../../../../docs/runtime-durable-resumption.md#coalescing-checkpoint-persistence--the-deferred-flush-window-e3-6--rt-10)
and the [benchmark results](../../../../docs/reports/elsa-4-architecture-review-2026-07/w9-checkpoint-coalescing-benchmark.md).

> **Documented ADR 0033 deviation.** The two coalescing interfaces below moved to the
> `Elsa.Workflows.Runtime` engine package instead of staying in `.Core` with the other contracts:
> both expose the concrete `RuntimeCoalescingSession` (engine working state) on their signatures, so
> they cannot stand ahead of the engine, and their only consumers are the opt-in coalescing
> composition in `Runtime.Api` plus its tests. They keep their `Elsa.Workflows.Runtime.Core.Contracts`
> namespace. See the engine catalog: [`../EXTENSION_POINTS.md`](../EXTENSION_POINTS.md).

### `IRuntimeCoalescingSessionAccessor` *(engine — `Elsa.Workflows.Runtime`)*
- **Kind:** Replacement (one ambient accessor exposes the active coalescing session to the decorators).
- **Signature:** `RuntimeCoalescingSession? Current { get; }`, `IDisposable Push(RuntimeCoalescingSession? session)`.
- **Usage:** an `AsyncLocal` push/pop stack that makes the current drain segment's in-memory working set ambient to the coalescing store/queue/outbox decorators, mirroring the existing ambient ownership-scope resolution. Only registered by the opt-in extension.
- **Default implementation:** `AsyncLocalRuntimeCoalescingSessionAccessor` *(opt-in only)*.

### `IRuntimeCoalescingDrainScopeFactory` *(engine — `Elsa.Workflows.Runtime`)*
- **Kind:** Replacement (one factory opens the per-drain coalescing scope and performs the quiescence flush).
- **Signature:** `IRuntimeCoalescingDrainScope Begin(string workflowExecutionId)`; scope exposes `RuntimeCoalescingSession Session` and `ValueTask FlushAtQuiescenceAsync(CancellationToken)`.
- **Usage:** `WorkflowDrainOrchestrator` opens a scope around a drain when the factory is registered (greediest resolvable ctor), buffers intra-drain checkpoints in the session, and flushes one folded atomic commit at quiescence through `RuntimeCheckpointCommitter.CommitAsync` (so W5 ownership fencing still gates it). Only registered by the opt-in extension.
- **Default implementation:** `RuntimeCoalescingDrainScopeFactory` *(opt-in only)*.

## Cross-references

- HTTP endpoint behaviour overrides: [`Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md`](../Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.2 + §2.22.1.
