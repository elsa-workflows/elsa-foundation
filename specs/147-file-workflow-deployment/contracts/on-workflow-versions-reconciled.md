# Contract: `OnWorkflowVersionsReconciled` event

New extension point in `Elsa.Workflows.Design.Reconciliation.Core`. This is the catalog entry content destined for `src/Elsa/Workflows/Design/Reconciliation/EXTENSION_POINTS.md` (§2.22.1; CatalogParityTests-enforced).

| Aspect | Value |
|---|---|
| Class | `Elsa.Workflows.Design.Reconciliation.Core.OnWorkflowVersionsReconciled : IEvent` (`sealed`) |
| Semantics | A workflow-version reconcile pass completed with every contributed version materialized (or verified present). Not published when the pass aborts. |
| Payload | `IReadOnlyList<WorkflowVersionSourceClaim> Claims` — one claim per contributed envelope: `DefinitionId`, `Version`, `SemVerSortKey`, `SourceId`, `SourceKind`, `PublishRequested`, `Deleted`. |
| Delivery strategy | **Sequential** (`IInlineEventPublisher`) — subscribers run inside the reconcile startup task, i.e. inside `[SingleNodeTask]` + distributed lock, before shell activation completes (and therefore before `/health/ready` turns ready). |
| Dispatcher failure policy | No exception shielding (Sequential): a subscriber throw fails the reconcile pass and shell activation. |
| Subscriber obligations | Subscribers MUST NOT throw. Recoverable per-item failures are logged and swallowed by the subscriber (see `PublishReconciledWorkflowVersions`). |
| Publication site | `WorkflowsVersionReconciler.Reconcile`, after the per-version loop completes without error. |
| Expected audiences | Independent subscribers reacting to completed reconciliation — publication, cache invalidation, telemetry. Not a fan-in contribution event: no contributor interface, no aggregating-handler constraint. |
| Ordering guarantees | Claims preserve contribution order (source registration order, then file order within a source). At publish time all versions in the pass are persisted. |
| Known subscribers | `PublishReconciledWorkflowVersions` *(cross-domain — `Elsa.Workflows.Publishing`)*: publishes the latest reconciled version of each definition whose claim has `PublishRequested = true`, idempotently (publication-slot pre-check + `PublishWorkflow` replay short-circuit), never throws, per-definition failure isolation. |

## Companion (additive) contract changes in the same domain

- `IWorkflowReconciliationSource.RequestsPublication` — new default interface member (`=> false`). A source returning `true` asks for its latest reconciled version per definition to be published after the pass. *(Known implementations: `JsonWorkflowReconciliationSource` — bound to `PublishOnReconcile`; all others inherit `false`.)*
- `OnWorkflowVersionsReconciling.Claims` — new get-only `ICollection<WorkflowVersionSourceClaim>` populated by the aggregating handler beside `Versions`; consumed by the reconciler to assemble `OnWorkflowVersionsReconciled`.
