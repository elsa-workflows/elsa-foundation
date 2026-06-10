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
- **Signature:** `WriteAsync(RuntimeCheckpoint checkpoint, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)`.
- **Usage:** implemented by runtime persistence providers after the checkpoint state envelope is specified.
- **Known implementations (shipped):** none yet; this first runtime execution slice defines the contract only.

### `IWorkflowExecutionAgentProvider` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Replacement (one provider owns workflow-execution mailbox resolution for a runtime composition).
- **Signature:** `GetAgentAsync(string workflowExecutionId, CancellationToken cancellationToken = default)`.
- **Usage:** provider implementations enforce one active mailbox/agent per `WorkflowExecutionId`. Actor frameworks are provider choices; checkpoint state remains the source of truth.
- **Known implementations (shipped):** none yet; this first runtime execution slice defines the contract only.

## Implementable contributor interfaces

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
