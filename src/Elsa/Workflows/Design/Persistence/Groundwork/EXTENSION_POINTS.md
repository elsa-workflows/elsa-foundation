# Extension points — Workflows.Design.Persistence.Groundwork domain

Groundwork provider catalog for workflow-design persistence replacement contracts. Contracts are defined in `Elsa.Workflows.Design.Persistence.Core`; this feature supplies the Groundwork document-store implementations when a shell selects Groundwork persistence.

## Replacement contracts

| Contract | Groundwork implementation |
|---|---|
| `IWorkflowDefinitionStore` | `GroundworkWorkflowDefinitionStore` |
| `IWorkflowDefinitionPageStore` | `GroundworkWorkflowDefinitionStore` (only advertises the paged API relation when `IBoundedDocumentStore` is admitted) |
| `IWorkflowFolderStore` | `GroundworkWorkflowFolderStore` (bounded direct-child browse plus tenant-scoped point and batched ancestry reads) |
| `IWorkflowDefinitionVersionStore` | `GroundworkWorkflowDefinitionVersionStore` |
| `IWorkflowDefinitionDraftStore` | `GroundworkWorkflowDefinitionDraftStore` |
| `IWorkflowDefinitionVersionLayoutStore` | `GroundworkWorkflowDefinitionVersionLayoutStore` |
| `IAddWorkflowDefinitionCommand` | `GroundworkAddWorkflowDefinitionCommand` |
| `ISaveWorkflowDefinitionCommand` | `GroundworkSaveWorkflowDefinitionCommand` |
| `IDeleteWorkflowDefinitionPermanentlyCommand` | `GroundworkDeleteWorkflowDefinitionPermanentlyCommand` |
| `ICreateDraftCommand` | `GroundworkCreateDraftCommand` |
| `IUpdateDraftCommand` | `GroundworkUpdateDraftCommand` |
| `IDiscardDraftCommand` | `GroundworkDiscardDraftCommand` |
| `IPromoteDraftToVersionCommand` | `GroundworkPromoteDraftToVersionCommand` |
| `ISubmitWorkflowDefinitionCommand` | `GroundworkSubmitWorkflowDefinitionCommand` |
| `ICloneDraftFromVersionCommand` | `GroundworkCloneDraftFromVersionCommand` |
| `IWorkflowDefinitionLookup` | Core `WorkflowDefinitionLookup` |

`AddGroundworkWorkflowsDesignStores()` removes existing registrations for these contracts before adding the Groundwork implementations, preserving the one-active-implementation replacement-contract rule.

Workflow-definition paging uses a scale-bearing cursor route when no search term is supplied. Name,
description, and ID substring search uses a separate ordinary bounded cursor route with the same
`LastModifiedAt DESC, Id ASC` order. The split is required because MongoDB's case-insensitive regular-expression
semantics cannot be certified as an indexed B-tree operation; both routes still apply provider-side filtering
before the requested materialization limit.

Folder-aware definition pages compose direct folder or Unfiled selection with lifecycle and search on
the same bounded definition route. Page projection resolves distinct containing folders through
`IWorkflowFolderStore.FindManyWithAncestorsAsync`; the Groundwork implementation point-loads each
distinct folder and shared ancestor once, keeping mutable folder paths out of definition identity.

`IDraftStateDiffEngine` is intentionally absent: per-diff mutation-event publication is retired until an event-sourcing consumer exists, so the engine is no longer registered by this provider (it remains in Core as the tested contract).

## Document model

The Groundwork `workflowDefinitionDraft` document embeds the draft entity and current designer layout records in one document. Validation errors are derived state, not persisted: create/update persist draft state and layout atomically through `IDocumentStore.SaveAllAsync`, and the mutation and promotion commands re-run the validators in-lock via the shared `DraftValidationGate` to derive the current error set (promotion refuses to create a version while errors exist). The read port (`IWorkflowDefinitionDraftStore`) no longer exposes a validation-error read — the draft API derives errors on demand from the already-loaded draft through the shielded gate.

## Cross-references

- EF Core provider catalog: [`../EFCore/EXTENSION_POINTS.md`](../EFCore/EXTENSION_POINTS.md)
- Validation extension points: [`../../Validations/EXTENSION_POINTS.md`](../../Validations/EXTENSION_POINTS.md)
- Repo-wide index: [`../../../../../../EXTENSION_POINTS.md`](../../../../../../EXTENSION_POINTS.md)
