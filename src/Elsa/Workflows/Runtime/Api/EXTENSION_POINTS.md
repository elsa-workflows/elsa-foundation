# Extension points — Workflow Runtime API

`WorkflowsRuntimeApiFeature` is the host-independent composition root for the supported Runtime management-client routes documented in [README.md](README.md). It does not depend on `Elsa.Server`.

## API-facing replacement contracts

| Contract/service | Role in this API |
|---|---|
| `IWorkflowExecutableStore` | Lists and loads immutable executable artifacts for inspection and execution. Persistence providers replace the in-memory implementation. |
| `IWorkflowExecutableReferenceStore` | Supplies read-only source provenance. Mutation remains with Publishing operations. |
| `IWorkflowExecutionStateStore` | Supplies instance, activity-execution, and incident projections and retained executable roots. |
| `WorkflowExecutableInspector` | Assembles stable executable and provenance views from Runtime-owned stores. It is registered with `TryAddScoped`, so a host may replace its projection strategy. |
| Runtime diagnostics settings store/handlers | Back the canonical diagnostics settings resource; durable or centrally managed hosts may replace the underlying settings persistence. |

The Runtime API also dispatches through the engine contracts composed by `AddWorkflowRuntime()`. Those execution, checkpoint, actor, trigger, scheduling, retention, and recovery seams are documented in the [Runtime domain extension catalog](../EXTENSION_POINTS.md); this API catalog does not duplicate them.

Optional stimulus/trigger providers are additive contributors to Runtime routing. Executable/reference/execution stores are single-owner persistence ports and must be replaced deliberately.

Canonical ownership and retention rules are defined in the [domain-owned API spec](../../../../../specs/092-domain-owned-apis/spec.md); terminology is defined in the [Elsa glossary](../../../../../docs/glossary/elsa.md).
