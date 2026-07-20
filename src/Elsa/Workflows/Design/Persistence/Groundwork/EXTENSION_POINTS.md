# Extension points — Workflows.Design.Persistence.Groundwork domain

Groundwork provider catalog for workflow-design persistence replacement contracts. Contracts are defined in `Elsa.Workflows.Design.Persistence.Core`; this feature supplies the Groundwork document-store implementations when a shell selects Groundwork persistence.

## Replacement contracts

| Contract | Groundwork implementation |
|---|---|
| `IWorkflowDefinitionStore` | `GroundworkWorkflowDefinitionStore` |
| `IWorkflowDefinitionVersionStore` | `GroundworkWorkflowDefinitionVersionStore` |
| `IWorkflowDefinitionDraftStore` | `GroundworkWorkflowDefinitionDraftStore` |
| `IWorkflowDefinitionListProjectionStore` | `GroundworkWorkflowDefinitionListProjectionStore` |
| `IWorkflowDefinitionVersionLayoutStore` | `GroundworkWorkflowDefinitionVersionLayoutStore` |
| `IAddWorkflowDefinitionCommand` | `GroundworkAddWorkflowDefinitionCommand` |
| `IMaterializeWorkflowDefinitionCommand` | `GroundworkMaterializeWorkflowDefinitionCommand` |
| `IAddWorkflowDefinitionVersionCommand` | `GroundworkAddWorkflowDefinitionVersionCommand` |
| `IMaterializeWorkflowDefinitionVersionCommand` | `GroundworkMaterializeWorkflowDefinitionVersionCommand` |
| `ISaveWorkflowDefinitionCommand` | `GroundworkSaveWorkflowDefinitionCommand` |
| `IDeleteWorkflowDefinitionPermanentlyCommand` | `GroundworkDeleteWorkflowDefinitionPermanentlyCommand` |
| `ICreateDraftCommand` | `GroundworkCreateDraftCommand` |
| `IUpdateDraftCommand` | `GroundworkUpdateDraftCommand` |
| `IDiscardDraftCommand` | `GroundworkDiscardDraftCommand` |
| `IPromoteDraftToVersionCommand` | `GroundworkPromoteDraftToVersionCommand` |
| `ISubmitWorkflowDefinitionCommand` | `GroundworkSubmitWorkflowDefinitionCommand` |
| `ICloneDraftFromVersionCommand` | `GroundworkCloneDraftFromVersionCommand` |
| `IWorkflowDefinitionLookup` | Core `WorkflowDefinitionLookup` |

`AddGroundworkWorkflowsDesignStores()` removes existing registrations for the contracts in this
table before adding the Groundwork implementations, preserving the one-active-provider rule.

`IDraftStateDiffEngine` is intentionally absent: per-diff mutation-event publication is retired until an event-sourcing consumer exists, so the engine is no longer registered by this provider (it remains in Core as the tested contract).

## Feature specialization seams

| Contract | Default implementation |
|---|---|
| `IDesignAtomicWriter` | `GroundworkDesignAtomicWrite` |
| `IDraftOriginator` | `DraftOriginator` |

These feature-owned replacement seams use `TryAddScoped`, so a host or inheriting feature can
register one specialization before composing the Groundwork workflow-design stores.
`IDesignAtomicWriter` owns replay-safe multi-document mutation, durable operation markers, and
uncertain-commit reconciliation for both workflow and activity design commands.

`IDraftOriginator` is the provider-feature replacement seam used by the Groundwork create and
clone commands. It owns identity allocation, per-draft locking, validation, atomic persistence,
and lifecycle-event publication while each public command retains its own canonical operation
material. Replace it only when specializing that complete Groundwork origination lifecycle.

## Document model

The Groundwork `workflowDefinitionDraft` document embeds the draft entity and current designer
layout records in one document. Validation errors are derived state, not persisted. Origination,
update, and promotion derive errors through the inline validation event pipeline while holding
their aggregate lock; accepted document changes and the durable operation marker commit together
through `IDesignAtomicWriter`. Promotion refuses to create a version while errors exist. The read
port (`IWorkflowDefinitionDraftStore`) no longer exposes a validation-error read; the draft API
derives errors on demand from the already-loaded draft through the shielded validation gate.

## Cross-references

- EF Core provider catalog: [`../EFCore/EXTENSION_POINTS.md`](../EFCore/EXTENSION_POINTS.md)
- Validation extension points: [`../../Validations/EXTENSION_POINTS.md`](../../Validations/EXTENSION_POINTS.md)
- Repo-wide index: [`../../../../../../EXTENSION_POINTS.md`](../../../../../../EXTENSION_POINTS.md)
