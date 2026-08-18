# Workflows Publishing (engine)

`Elsa.Workflows.Publishing` is the endpoint-free **publish + compile engine**. It compiles a persisted
Design workflow version into a canonical Runtime executable and coordinates the publication authority
transition (slot CAS, projection reconciliation, records) — all without mounting any HTTP transport and
without any authorization dependency. A headless Runtime node can therefore compose the publish
capability directly; the management-client HTTP surface is a separate concern owned by
`Elsa.Workflows.Publishing.Api`, which obtains this engine by `DependsOn` composition (framework §2.11).

The engine is a **bridge** over the two stable seams it connects — persisted Design metadata and canonical
Runtime executable contracts. Like its Api sibling it references neither the Runtime implementation's `.Api`
nor any Design `.Api` feature, so it does not couple the sub-domains it keeps apart (§E2.2).

The canonical authority decision is [ADR 0043](../../../../docs/adr/0043-publication-slots-define-start-authority.md).
Use the [Elsa glossary](../../../../docs/glossary/elsa.md) and
[root glossary](../../../../docs/glossary/root.md) for shared vocabulary; this README describes how the
module realizes those concepts rather than redefining them.

## Composition

Enable the `WorkflowsPublishing` shell feature. It depends on `WorkflowsRuntimeTriggers` and `Events`, and
registers the compiler and its collaborator graph, the publication authority stores, the
policy/preflight/activation/projection services, the activity-template provider registries, the executable
compilation/node-metadata fan-in, and the `PublishWorkflow` mediator handler. Every store default is
process-local (`InMemory*`), which is convenient for tests and single-process development but must be made
durable for a production host.

For Groundwork-backed authority state, reference `Elsa.Workflows.Publishing.Persistence.Groundwork` and
compose:

```csharp
services.AddGroundworkPublishingStores();
```

This replaces (or, when composed first, prevents) the in-memory defaults for
`IPublicationRecordStore`, `IPublicationPolicyStore`, and the activity
publication receipt store. It does **not** cover activation: the slot ledger is
`IWorkflowActivationAuthority`, owned by the runtime store family (spec 151, FR-B-006). The host must also compose the Runtime persistence used for executable artifacts,
source references, trigger bindings, and recurring schedules.

## What the engine owns vs. what the API owns

| Concern | Owner |
|---|---|
| `PublishWorkflow` command + `PublishedWorkflowView` response | `Elsa.Workflows.Publishing.Core` |
| Compiler, publication authority stores, policy/preflight/activation/projection, template registries, compilation fan-in | **this engine** |
| HTTP endpoints, API capabilities, transport authorization, activity-draft publish/test-run | `Elsa.Workflows.Publishing.Api` |

Authorization is deliberately a transport concern: `IActivityPublishingAuthorizationContext` and the
activity-draft services that consume it live in the Api feature only. The engine registers no authorization
context and introduces no neutral default, so composing the engine alone yields a fully wired publish surface
with zero transport.

## Cross-domain contributions

This feature satisfies one Design-owned contribution contract (#1283):

- `PublishedWorkflowDeletionGuard : IWorkflowDefinitionPublicationDeletionGuard` (and the base
  `IWorkflowDefinitionPermanentDeletionGuard`) — the publication check that vetoes permanently deleting a
  definition a live publication still references. The marker sub-contract carries composition weight: the
  design lane's permanent-delete command refuses outright (HTTP 501) on any host where no publication
  check is composed, so composing this feature is what makes permanent deletion available at all.
  Contract semantics: [design-persistence extension-point
  catalog](../Design/Persistence/Groundwork/EXTENSION_POINTS.md#contributor-interfaces).

The engine registers one independent event subscriber for a Design-side event (spec 147, #1157):

- `PublishReconciledWorkflowVersions : IEventHandler<WorkflowVersionsReconciled>` — publish-on-reconcile.
  After a workflow-reconciliation pass completes, it publishes the latest reconciled version of each
  definition whose source opted in (`PublishOnReconcile` on the JSON source → `PublishRequested` on the
  claim) via the in-process `PublishWorkflow` request. Idempotent across restarts (publication-slot
  pre-check + the publish handler's unchanged-artifact replay); per-definition failures are logged and
  isolated — the handler never throws (Sequential delivery would otherwise fail shell activation).
  Contract and delivery semantics:
  [reconciliation extension-point catalog](../Design/Reconciliation/EXTENSION_POINTS.md).

## Publication lifecycle

The authority transition is one coordinated operation (identical to what the API drives, minus HTTP):

1. Resolve intent using `explicit request > workflow policy > host policy`. With no override, publishing
   replaces the `default` slot; side-by-side publication requires an explicit non-default slot.
2. Compile the candidate and preflight its publication-scoped trigger claims. `Exclusive` claims conflict
   with authoritative claims in other slots; `FanOut` claims may coexist.
3. Prepare inactive trigger-binding and recurring-schedule projections (invisible to new starts, the HTTP
   route table, and the recurring pump).
4. Activate the slot with compare-and-swap on its expected revision, then switch the prepared serving
   projections to the new publication and retire the replaced one.
5. A failed activation compensates in-process: the coordinator restores the previous authority and re-activates
   its projections before removing the candidate's. There is no delivery-intent ledger to replay — the retry is
   a fresh request, and every step is idempotent.
6. Retire or restore the publication source reference as provenance. Existing executions stay pinned to their
   immutable executable artifact; unpublishing does not delete that artifact.

## References

- Supported replacements and provider work: [engine extension-point catalog](EXTENSION_POINTS.md).
- Management-client HTTP surface: [`Elsa.Workflows.Publishing.Api` README](Api/README.md).
- Publication authority decision: [ADR 0043](../../../../docs/adr/0043-publication-slots-define-start-authority.md).
