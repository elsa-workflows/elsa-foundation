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
