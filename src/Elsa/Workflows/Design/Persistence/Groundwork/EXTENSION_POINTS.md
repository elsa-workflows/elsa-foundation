# Extension points — Workflows.Design.Persistence.Groundwork domain

Groundwork provider catalog for workflow-design persistence replacement contracts. Contracts are defined in `Elsa.Workflows.Design.Persistence.Core`; this feature supplies the Groundwork document-store implementations when a shell selects Groundwork persistence.

## Replacement contracts

| Contract | Groundwork implementation |
|---|---|
| `IWorkflowDefinitionStore` | `GroundworkWorkflowDefinitionStore` |
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

`IDraftStateDiffEngine` is intentionally absent: per-diff mutation-event publication is retired until an event-sourcing consumer exists, so the engine is no longer registered by this provider (it remains in Core as the tested contract).

## Document model

The Groundwork `workflowDefinitionDraft` document embeds the draft entity and current designer layout records in one document. Validation errors are derived state, not persisted: create/update persist draft state and layout atomically through `IDocumentStore.SaveAllAsync`, and both the promotion gate and the read port re-run the validators in-lock to derive the current error set (promotion refuses to create a version while errors exist).

## Cross-references

- EF Core provider catalog: [`../EFCore/EXTENSION_POINTS.md`](../EFCore/EXTENSION_POINTS.md)
- Validation extension points: [`../../Validations/EXTENSION_POINTS.md`](../../Validations/EXTENSION_POINTS.md)
- Repo-wide index: [`../../../../../../EXTENSION_POINTS.md`](../../../../../../EXTENSION_POINTS.md)
