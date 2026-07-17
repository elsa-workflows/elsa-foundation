# Extension points — Workflow Runtime API

`WorkflowsRuntimeApiFeature` is the host-independent composition root for the supported Runtime management-client routes documented in [README.md](README.md). It does not depend on `Elsa.Server`.

## API-facing replacement contracts

| Contract/service | Role in this API |
|---|---|
| `IWorkflowExecutableStore` | Lists and loads immutable executable artifacts for inspection and execution. Persistence providers replace the in-memory implementation. |
| `IWorkflowExecutableReferenceStore` | Supplies read-only source provenance. Mutation remains with Publishing operations. |
| `IWorkflowExecutionStateStore` | Supplies instance, activity-execution, and incident projections and retained executable roots. |
| `WorkflowExecutableInspector` | Assembles stable executable and provenance views from Runtime-owned stores. It is registered with `TryAddScoped`, so a host may replace its projection strategy. |
| `IActivityExecutionInspectionAuthorizationContext` | Separately authorizes structural inspection, redacted value evidence, and raw captured-value payload resolution. Its authorization profile participates in cursor binding. |
| `IActivityExecutionValuePayloadReader` | Owns the non-disclosing raw-payload resolution result contract. The feature registers only this contract; hosts may replace the implementation without coupling request handlers to the default reader. Resolution fails closed unless the authorization context supplies a stable attributable audit subject. |
| `IActivityExecutionValuePayloadAuditSink` | Receives every authorized, denied, or unavailable raw value-payload resolution attempt, including the stable audit subject and request correlation ID, without receiving the payload itself. The default writes structured metadata to the host logger. |
| `ActivityExecutionLayoutReader` | Projects composite-boundary structure from the pinned executable artifact and joins optional source-reference geometry. It never loads Design state and retains authored nodes that did not execute, including immediate nested boundary roots while keeping their descendants lazy. |
| Runtime diagnostics settings store/handlers | Back the canonical diagnostics settings resource; durable or centrally managed hosts may replace the underlying settings persistence. |

The Runtime API also dispatches through the engine contracts composed by `AddWorkflowRuntime()`. Those execution, checkpoint, actor, trigger, scheduling, retention, and recovery seams are documented in the [Runtime domain extension catalog](../EXTENSION_POINTS.md); this API catalog does not duplicate them.

Optional stimulus/trigger providers are additive contributors to Runtime routing. Executable/reference/execution stores are single-owner persistence ports and must be replaced deliberately.

Activity-execution detail exposes capture truth and stable evidence identities but never embeds raw captured payloads. Clients resolve a payload through the dedicated `value-evidence/{evidenceId}/payload` capability only after the separate payload-resolution authorization and audit path succeeds.

Canonical ownership and retention rules are defined in the [domain-owned API spec](../../../../../specs/092-domain-owned-apis/spec.md); terminology is defined in the [Elsa glossary](../../../../../docs/glossary/elsa.md).
