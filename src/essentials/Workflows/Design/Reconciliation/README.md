# Elsa.Workflows.Design.Reconciliation

Reconciliation lifecycle for the workflow definition catalog. Mirrors the activity reconciliation pattern: sources contribute desired workflow versions via `IWorkflowReconciliationSource`; the reconciler diffs against the stored catalog and upserts.

## Credential literals refused per item

Before it writes anything for an item, the reconciler judges the item's state with the credential-literal rule
(spec 188, FR-008; `ICredentialLiteralValidator`, registered by `WorkflowDesignValidations`, on which the concrete
reconciliation features depend). An item that binds a literal, an object, a value read or an expression to an input
its activity declares a credential is refused on its own, like an outdated item: no definition record, no metadata
update and no version row is written for it, and its claim does not reach `WorkflowVersionsReconciled`, so
publish-on-reconcile never sees it. The pass logs one warning per refused binding, naming the rule, the definition,
the version, the node and the input, never the value, and goes on: a pass whose only problems are refusals completes
and does not keep the host from becoming ready. A node whose activity the catalog does not hold cannot be judged and
is reconciled; publication refuses it.

## Cross-domain contributions

- **`IStartupTask`** *(Core — `Elsa.Tasks.Core`)* — `WorkflowsVersionReconcilerStartupTask` runs the reconciliation pass at startup on every node, without a lock: its sources are node-local, and concurrent passes converge (#2187, #2189, #2192). Catalog: [`Elsa.Tasks/EXTENSION_POINTS.md`](../Elsa.Tasks/EXTENSION_POINTS.md)
