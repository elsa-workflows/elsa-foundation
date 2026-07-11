# Extension points — Workflows.Publishing domain

The per-domain catalog (framework §2.22.1) of everything you can override in the Workflows.Publishing domain. Contracts live in `Elsa.Workflows.Publishing.Core` (carved out under DS-1, Elsa 4 architecture review 2026-07); the default implementations and the composition root are in `Elsa.Workflows.Publishing.Api`, where `WorkflowsPublishingApiFeature` wires them with `TryAdd`.

Per three-layer rule (framework §2.1): contracts in `.Core`, defaults + composition in the `.Api` feature. Every contract below is on the **override** axis (framework §2.22.1) — one implementation wins; register your own and the built-in steps aside.

> **Depends on Workflows.Runtime.** The publish flow compiles a `WorkflowExecutable` (a Runtime.Core model), persists it through the runtime's durable `IWorkflowExecutableStore`, and indexes its start-triggers through the runtime's `IWorkflowTriggerIndexer` (W7). Those seams belong to the runtime catalog: [`../../Runtime/EXTENSION_POINTS.md`](../../Runtime/EXTENSION_POINTS.md).

---

## Overridable contracts

| Contract | Default impl | Lifetime | Override when |
|---|---|---|---|
| `IWorkflowExecutableCompiler` | `WorkflowExecutableCompiler` (`.Api`) | `TryAddScoped` | You want a different compilation strategy (alternate slice metadata, custom artifact-id scheme, additional validation) while keeping the publish/test-run flow. |
| `ITransientWorkflowExecutableStore` | `InMemoryTransientWorkflowExecutableStore` (`.Api`) | `TryAddSingleton` | You want the short-lived pre-execution staging store for compiled executables backed by something other than process memory. |
| `IWorkflowTestRunStore` | `InMemoryWorkflowTestRunStore` (`.Api`) | `TryAddSingleton` | You want test-run records persisted (durable, shared) rather than held in the built-in expiry-bounded in-memory store. |

### `IWorkflowExecutableCompiler` *(Core — `Elsa.Workflows.Publishing.Core`)*
- **Signature:** `ValueTask<WorkflowExecutable> CompileAsync(WorkflowExecutableCompileRequest request, CancellationToken ct)`.
- **Behaviour:** compiles a published or transient (test-run) version into a runtime `WorkflowExecutable`. Compilation failures surface as `WorkflowExecutableCompilationException` (an `ArgumentException` carrying the DefinitionId/VersionId context, #397) — the publish/test-run handlers let the typed exception propagate rather than rewrapping it.
- **Override:** `services.Replace(ServiceDescriptor.Scoped<IWorkflowExecutableCompiler, MyCompiler>())`.

### `ITransientWorkflowExecutableStore` *(Core — `Elsa.Workflows.Publishing.Core`)*
- **Signature:** `SaveAsync(executable)`, `FindAsync(artifactId)`, `CleanupExpiredAsync(now)`.
- **Behaviour:** short-lived staging for a compiled executable between compile and execution on the test-run path — distinct from the durable published-artifact store (`IWorkflowExecutableStore`, runtime domain) the publish flow writes to. Entries carry an expiry; `CleanupExpiredAsync` drops lapsed ones.
- **Override:** `services.Replace(...)` for a distributed staging store.

### `IWorkflowTestRunStore` *(Core — `Elsa.Workflows.Publishing.Core`)*
- **Signature:** `SaveAsync(testRun)`, `FindAsync(testRunId)`, `CleanupExpiredAsync(now)`.
- **Behaviour:** records test-run outcomes. The default `InMemoryWorkflowTestRunStore` is bounded by a retention/expiry window (including rejected runs) so a long-lived process cannot leak test-run state unbounded (#398); reads lazily drop lapsed entries.
- **Override:** `services.Replace(...)` for a durable, shared test-run store.

---

## Cross-references

- Durable published-artifact store + trigger indexing (the runtime seams the publish flow consumes): [`../../Runtime/EXTENSION_POINTS.md`](../../Runtime/EXTENSION_POINTS.md).
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.1 (three-layer) + §2.22.1 (override axis).
