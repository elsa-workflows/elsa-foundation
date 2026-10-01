# Extension points — Workflows.Design.Persistence.EntityFrameworkCore domain

Opt-in EF Core provider for workflow-design persistence.
one backend is selected per shell through the provider registration contract.

## Replacement contracts

The registration supplies the EF replacements for the workflow-design persistence ports:

| Surface | Implementation |
|---|---|
| `IWorkflowDefinitionStore` | `EfWorkflowDefinitionStore` |
| `IWorkflowDefinitionVersionStore` | `EfWorkflowDefinitionVersionStore` |
| `IWorkflowDefinitionDraftStore` | `EfWorkflowDefinitionDraftStore` |
| `IWorkflowDefinitionListProjectionStore` | `EfWorkflowDefinitionListProjectionStore` |
| `IWorkflowDefinitionVersionLayoutStore` | `EfWorkflowDefinitionVersionLayoutStore` |
| Workflow-design lifecycle commands | `EfWorkflowDesignCommands` implementations |
| `IDesignAtomicWriter` | `EfDesignAtomicWriter` |

`AddWorkflowsDesignEntityFrameworkCore` rejects a different already-selected backend. Provider
switching is intentionally not implicit; a host must expose an explicitly named replacement flow.

## Feature and provider binding

`WorkflowsDesignEntityFrameworkCoreFeature` is an opt-in shell feature. Its provider, connection
string, and connection name settings flow into `AddWorkflowsDesignEntityFrameworkCore`. The EF
project is provider-neutral; relational provider packages are bound by the selected context and
are not exposed by the Core contracts.

The atomic writer persists the operation marker in the same transaction as staged definition,
version, draft, and layout changes. Replays and marker-race losers do not publish duplicate
post-commit lifecycle events.

A save that loses an optimistic-concurrency race, because a row changed after it was read, surfaces
as a `DesignPersistenceException` with `FailureKind` `Concurrency`. Nothing was committed, so reading
again and writing afresh can succeed. Every other provider failure has `FailureKind` `Provider`.

Markers are permanent, with one exception. A permanent delete removes, in its own transaction, every
marker the workflow reconciler wrote for the definition (`WorkflowReconciliationOperationKeys`): the
materialization of the definition and its versions, and its metadata writes, including those written
under the per-version key used before #2187. A source that still lists the definition therefore imports
it again instead of replaying markers that write no row.

A definition materialization's fingerprint covers whether the definition is deleted, not its `DeletedAt`
time (#2189). Each node stamps its own time on a definition its source marks deleted, so two nodes
importing it now send the same request, and the second replays the first, keeping the first's time.
Markers written earlier fingerprint the time. They are never compared with a new request, because the
reconciler materializes only a definition it found missing, and the permanent delete retires the marker
together with the definition.
