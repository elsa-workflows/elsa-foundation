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
| `IDraftStateDiffEngine` | Core `DraftStateDiffEngine` |

`AddGroundworkWorkflowsDesignStores()` removes existing registrations for these contracts before adding the Groundwork implementations, preserving the one-active-implementation replacement-contract rule.

## Document model

The Groundwork `workflowDefinitionDraft` document embeds the draft entity, current designer layout records, and current validation errors in one document. Promotion reads that embedded validation state and refuses to create a version while errors exist; create/update persist draft state, layout, and validation atomically through `IDocumentStore.SaveAllAsync`.

## Cross-references

- EF Core provider catalog: [`../EFCore/EXTENSION_POINTS.md`](../EFCore/EXTENSION_POINTS.md)
- Validation extension points: [`../../Validations/EXTENSION_POINTS.md`](../../Validations/EXTENSION_POINTS.md)
- Repo-wide index: [`../../../../../../EXTENSION_POINTS.md`](../../../../../../EXTENSION_POINTS.md)
