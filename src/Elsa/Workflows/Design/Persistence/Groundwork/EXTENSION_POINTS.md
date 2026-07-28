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

These feature-owned replacement seams use `TryAddScoped`, so a host can register one
specialization before composing the Groundwork workflow-design stores. An inheriting feature that
specializes after its base registration must use
`services.Replace(ServiceDescriptor.Scoped<IContract, Implementation>())`; direct `AddScoped`
would create an invalid duplicate replacement registration. Both pre-composition preservation and
post-composition replacement are covered by registration tests.
`IDesignAtomicWriter` owns replay-safe multi-document mutation, durable operation markers, and
uncertain-commit reconciliation for both workflow and activity design commands.

`IDraftOriginator` is the provider-feature replacement seam used by the Groundwork create and
clone commands. It owns identity allocation, per-draft locking, validation, atomic persistence,
and lifecycle-event publication while each public command retains its own canonical operation
material. Replace it only when specializing that complete Groundwork origination lifecycle.

## Storage manifest declaration

`WorkflowsDesignGroundworkStorageManifestSource` (feature identity `elsa-workflows-design`) implements
`IGroundworkStorageManifestSource`. It physicalizes `WorkflowsDesignStorageManifest.Create()` through
`LegacyGroundworkStorageManifestPhysicalizer.Physicalize` and declares the read ports it owns
(`IWorkflowDefinitionStore`, `IWorkflowDefinitionVersionStore`, `IWorkflowDefinitionDraftStore`,
`IWorkflowDefinitionListProjectionStore`, `IWorkflowDefinitionVersionLayoutStore`).

The manifest declares each storage unit's projected columns, logical and physical indexes, and the
bounded, scale-bearing queries (`BoundedQueryExecutionClass.ScaleBearing`) that the provider admits.
There is no load-all or client-side evaluation route.

### Bounded-width rules

Projected columns are width-bounded so every declared compound index key stays under SQL Server's
1700-byte nonclustered index limit:

| Column class | Limit | Constant |
|---|---|---|
| Searchable text (name, description, …) | 256 chars | `TextColumnLength` |
| Identity / sort-key (id, version_id, …) | 128 chars | `IdentityColumnLength` |

Over-limit values **fail projection validation rather than truncate**, per the ratified data model
(the `AtomicityProjectionOverLimitRejection` contract). All workflow-design projected members are
keyword strings or `DateTime` (`ValueKind`); there are no numeric projected members in this lane.

## Design atomic writer and shared operation document

`IDesignAtomicWriter` (defined in `Elsa.Persistence.Groundwork.Querying`, default
`GroundworkDesignAtomicWrite`) owns replay-safe multi-document mutation: durable operation markers,
staged writes, and uncertain-commit reconciliation for both workflow- and activity-design commands.
Its durable ledger is the shared `designOperation` document declared by
`GroundworkDesignAtomicWriteStorageManifest` (owner `elsa.design.atomic-write`, route
`design-atomic-write`, topology requirement `multi-document-transactions`). Both design lanes
contribute `GroundworkDesignAtomicWriteStorageManifestSource` via `TryAddEnumerable`, so the operation
document is declared exactly once regardless of composition order.

## Registration and manifest sources

`AddGroundworkWorkflowsDesignStores()`
(`Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection`) is the lane registration method.
It:

- swaps every replacement contract in the tables above to its Groundwork implementation
  (`RemoveAll<T>()` then `AddScoped<T, …>()`), enforcing the one-active-provider rule;
- contributes `WorkflowsDesignGroundworkStorageManifestSource` and the shared
  `GroundworkDesignAtomicWriteStorageManifestSource` as `IGroundworkStorageManifestSource` enumerables;
- registers the `IDesignAtomicWriter` and `IDraftOriginator` specialization seams with `TryAddScoped`;
- registers the default entity factories.

## Schema readiness guard

The `GroundworkSchemaReadinessTask` start-phase guard (base `Elsa.Persistence.Groundwork`) validates
that the applied physical target matches the composed manifest and **never auto-applies or repairs**
schema unless the host opts into safe startup auto-apply. It is wired per provider by
`AddGroundworkSchemaReadinessGuard()`, called from each provider's document-store registration — not
by this lane. Schema application stays an operator/CLI responsibility.

## Host composition (unified vs lane-specific)

- **Unified (shipped reference host):** enabling one provider feature —
  `AddGroundwork{Sqlite|SqlServer|PostgreSql|MongoDb}UnifiedPersistence(…)` — routes through
  `AddGroundworkUnifiedStoreFamilies()`, which composes this lane
  (`AddGroundworkWorkflowsDesignStores`) alongside the activities-design and other store families over
  one physical document store. The selected provider's document-store registration owns the readiness
  guard. See [`../../../../Persistence/Groundwork/Unified/README.md`](../../../../Persistence/Groundwork/Unified/README.md).
- **Lane-specific:** a host may call `AddGroundworkWorkflowsDesignStores()` directly after registering
  a provider `IDocumentStore` and the host `IPayloadSerializer`, composing only this lane (the shape
  the registration tests exercise).

## Document model

The Groundwork `workflowDefinitionDraft` document embeds the draft entity and current designer
layout records in one document. Validation errors are derived state, not persisted. Origination,
update, and promotion derive errors through the inline validation event pipeline while holding
their aggregate lock; accepted document changes and the durable operation marker commit together
through `IDesignAtomicWriter`. Promotion refuses to create a version while errors exist. The read
port (`IWorkflowDefinitionDraftStore`) no longer exposes a validation-error read; the draft API
derives errors on demand from the already-loaded draft through the shielded validation gate.

## Cross-references

- The EF Core design persistence implementation is removed by spec 093 US4; Groundwork is the sole
  workflow-design persistence provider.
- Validation extension points: [`../../Validations/EXTENSION_POINTS.md`](../../Validations/EXTENSION_POINTS.md)
- Unified provider selection and schema operations: [`../../../../Persistence/Groundwork/Unified/README.md`](../../../../Persistence/Groundwork/Unified/README.md)
- Repo-wide index: [`../../../../../../EXTENSION_POINTS.md`](../../../../../../EXTENSION_POINTS.md)
