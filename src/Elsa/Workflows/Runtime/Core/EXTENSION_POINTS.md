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
- **Known implementations (shipped):** none yet; this runtime execution slice defines the contract only.

### `IRuntimePostCommitIntentDispatcher` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one dispatcher owns delivery of committed outbound runtime intents for a composition).
- **Signature:** `DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)`.
- **Usage:** dispatches post-commit intents in the order provided by the committed `RuntimeCheckpointCommit` only after `IRuntimeCheckpointWriter` completes successfully. This is a placeholder contract, not a full outbox processor.
- **Known implementations (shipped):** none yet; this runtime execution slice defines the contract only.

### `IRuntimePostCommitOutboxStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one provider owns durable post-commit outbox state for a runtime composition).
- **Signature:** `SavePendingAsync(RuntimePostCommitOutboxItem item, ...)`, `GetDeliverableAsync(RuntimePostCommitOutboxQuery query, ...)`, `RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, ...)`.
- **Usage:** stores delivery state for post-commit intents so providers can preserve record, commit, deliver, and mark-delivered ordering.
- **Known implementations (shipped):** none yet; this runtime execution slice defines the contract only.

### `IRuntimeRecoveryScanner` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one scanner identifies interrupted workflow executions for a runtime composition).
- **Signature:** `ScanAsync(RuntimeRecoveryScanRequest request, CancellationToken cancellationToken = default)`.
- **Usage:** provider implementations inspect operational state such as leases and heartbeats and return recovery candidates that requeue from the last checkpoint without invoking domain retry policy.
- **Known implementations (shipped):** none yet; this runtime execution slice defines the contract only.

### `IRuntimeDomainRetryPolicy` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one policy decides workflow/activity domain retry behavior for a runtime composition).
- **Signature:** `Decide(RuntimeDomainRetryRequest request)`.
- **Usage:** keeps workflow/activity retry decisions separate from operational recovery such as lost leases and interrupted execution agents.
- **Known implementations (shipped):** none yet; this runtime execution slice defines the contract only.

### `IRuntimeVolatileWaitPolicy` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one policy decides whether in-memory volatile waits are allowed in a runtime composition).
- **Signature:** `Decide(RuntimeVolatileWaitPolicyRequest request)`.
- **Usage:** evaluates host support, requested duration, requested host-shutdown behavior, requested cancellation behavior, and durable fallback posture. Volatile waits remain scheduler continuation state and are not durable bookmark resume state.
- **Known implementations (shipped):** none yet; this runtime execution slice defines the contract only.

### `IBookmarkResumeResolver` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one resolver owns durable bookmark-to-artifact resume resolution for a runtime composition).
- **Signature:** `Resolve(BookmarkResumeRequest request)`.
- **Usage:** maps `BookmarkState.ResumeTargetId` through the pinned `WorkflowExecutable.ResumeTargets` table and returns the executable node plus runtime resume target. It does not load artifacts, invoke activity handlers, or implement the bookmark store.
- **Default implementation:** `BookmarkResumeResolver` *(intra-domain default)*.

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

### `IRuntimePayloadCapturePolicy` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one policy decides which runtime observability payloads may be captured for a runtime composition).
- **Signature:** `Decide(RuntimePayloadCaptureRequest request)`.
- **Usage:** controls whether history, diagnostics, incidents, values, and input/output observations capture no payload, metadata only, or full payload. Continuation state does not read these observability payloads. The default excludes sensitive values and omits workflow/activity input and output snapshots.
- **Default implementation:** `DefaultRuntimePayloadCapturePolicy` *(intra-domain default)*.

### `IWorkflowExecutionAgentProvider` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one provider owns workflow-execution mailbox resolution for a runtime composition).
- **Signature:** `GetAgentAsync(string workflowExecutionId, CancellationToken cancellationToken = default)`.
- **Usage:** provider implementations enforce one active mailbox/agent per `WorkflowExecutionId`. Actor frameworks are provider choices; checkpoint state remains the source of truth.
- **Known implementations (shipped):** none yet; this first runtime execution slice defines the contract only.

### `IWorkflowExecutableStore` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one store owns runtime executable artifact lookup for a runtime composition).
- **Signature:** `SaveAsync(WorkflowExecutable executable, ...)`, `FindAsync(string artifactId, ...)`, `ListAsync(...)`.
- **Usage:** stores and retrieves runtime-owned `WorkflowExecutable` artifacts. Publishing writes artifacts through this contract; Runtime execution reads artifacts through this contract and does not load Design-owned workflow state.
- **Default implementation:** `InMemoryWorkflowExecutableStore` *(intra-domain demo default for the vertical slice; durable persistence remains future provider work)*.

### `IWorkflowExecutor` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one executor owns direct workflow artifact execution semantics for a runtime composition).
- **Signature:** `ExecuteAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)`.
- **Usage:** executes a runtime-owned artifact. The first implementation is deliberately sequential/literal-only and rejects unsupported shapes through deterministic diagnostics.
- **Default implementation:** `SequentialWorkflowExecutor` *(intra-domain vertical-slice default)*.

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
