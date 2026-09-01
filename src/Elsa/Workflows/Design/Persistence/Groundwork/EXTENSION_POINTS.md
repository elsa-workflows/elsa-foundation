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

## Contributor interfaces

### `IWorkflowDefinitionPermanentDeletionGuard` / `IWorkflowDefinitionPublicationDeletionGuard` *(Core — `Elsa.Workflows.Design.Persistence.Core`)*
- **Kind:** Open-ended contribution (veto list) plus a marker sub-contract that names the publication check as its own capability (#1283).
- **Signature:** `EnsureCanDeleteAsync(definitionId, cancellationToken)` — throw to veto the permanent deletion. `IWorkflowDefinitionPublicationDeletionGuard` adds no members; it marks an implementation as *the publication check*.
- **Usage:** `GroundworkDeleteWorkflowDefinitionPermanentlyCommand` resolves the base contract as `IEnumerable<T>` and runs every guard before staging deletes. **Absence of the marker is a hard refusal**, decided inside the atomic writer's `beforeAttempt` — after the operation-marker replay lookup (so a retry of an already-committed delete still replays to success on any node) but before any definition row is read. This inverts the usual contribution semantics on purpose: an empty guard set must not read as permission, and contributing an unrelated veto must not make permanent deletion available. A design-only host composes no publication check and answers `DELETE …/permanent` with 501; soft deletion is unaffected.
- **Known implementations:** `PublishedWorkflowDeletionGuard` (`Elsa.Workflows.Publishing`, cross-domain, tagged in that feature's README) — implements both contracts; registered via `TryAddEnumerable` by `WorkflowsPublishingFeature`.
- **Safety:** a host that supplies its own publication check must implement the marker interface, not only the base veto contract; implementing only the base leaves permanent deletion refused with a message that names the publishing vertical.

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
`IGroundworkStorageLane` and nothing more. The lane declares its storage units directly against the
public v2 catalog — `AddGroundworkStorageUnit` over `WorkflowsDesignStorageManifest.CreateUnits()` — so
it contributes no composed host manifest and provisions its own schema. The identity exists because a
caller spanning lanes has to resolve which target holds this one before it can decide how to commit;
implementing `IGroundworkStorageManifestSource` instead would pull the lane back into the v1
document-store closure.

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

## Design post-commit outbox — extend

The same manifest owns a second document, `designPostCommitIntent`. A design mutation whose lanes are on
different Groundwork targets becomes done when the design commit lands, but anything it derives on another
target has to be written afterwards, by the caller. An intent staged **inside** the design commit is durable
at exactly that instant, so the derived write gains a second driver and no longer depends on the caller
coming back ([#1171](https://github.com/elsa-workflows/elsa-foundation/issues/1171), ADR 0066).

| Contract | Kind | Responsibility |
|---|---|---|
| `IDesignPostCommitIntentDeliverer` | Contributor (extend — one per intent kind) | Performs one kind of deferred write. Registered with `TryAddEnumerable` by the feature that owns the target lane, so the design lane never learns which target the write lands on. Delivery must be idempotent: a redrive can only learn an attempt succeeded by completing it, so a crash between a successful write and the intent's deletion redelivers. Two deliverers claiming one kind is a composition error. |

`GroundworkDesignPostCommitOutbox` owns the storage: `CreateSaveRequest` produces the write a command stages
in its own design commit, and `ClaimAsync`/`TryCompleteAsync`/`TryReleaseAsync` give a redrive a
visibility-bounded, fenced lease. `GroundworkDesignPostCommitRedrive.SweepAsync` claims one bounded page,
delivers each intent, and deletes the ones that land; a failure is released with an exponential backoff
capped at five minutes. There is no dead letter — the mutation already committed, so the obligation cannot be
abandoned, and an intent stuck at the backoff ceiling is the operator's signal.

Two shapes are load-bearing and easy to break silently:

- **The claim index fields are bare top-level document properties** (`claimableAt`, `recordedAt`, `intentId`),
  not the `content.entity.*` nesting `GroundworkDocumentWriter` produces for design entities. Writing an
  intent through the entity envelope puts all three one level too deep, and the declared index then matches
  nothing while every write and every by-id read still succeeds.
- **The projected `intentId` column is bounded at 256** (`DesignPostCommitIntentIdProjectionLength`), because
  SQL Server refuses an unbounded string as an index key column. `ProjectIntentId` digests anything longer and
  the document keeps the durable logical id.

**Known implementations:** `ActivityPublicationReceiptIntentDeliverer` in
`Elsa.Workflows.Publishing.Persistence.Groundwork` *(cross-domain)* — writes the publishing receipt of a
reusable-activity publication whose publishing lane is on another target.

## Registration and manifest sources

`AddGroundworkWorkflowsDesignStores()`
(`Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection`) is the lane registration method.
It:

- swaps every replacement contract in the tables above to its Groundwork implementation
  (`RemoveAll<T>()` then `AddScoped<T, …>()`), enforcing the one-active-provider rule;
- binds the lane with `AddGroundworkStorageLane<WorkflowsDesignGroundworkStorageManifestSource>()` and
  declares its v2 units with `AddGroundworkStorageUnit` over `WorkflowsDesignStorageManifest.CreateUnits()`;
- registers the `IDesignAtomicWriter` and `IDraftOriginator` specialization seams with `TryAddScoped`;
- registers the default entity factories.

## Schema readiness guard

The `GroundworkSchemaReadinessTask` start-phase guard (base `Elsa.Persistence.Groundwork`) validates
that the applied physical target matches the composed manifest and **never auto-applies or repairs**
schema unless the host opts into safe startup auto-apply. It is wired per provider by
`AddGroundworkSchemaReadinessGuard()`, called from each provider's document-store registration — not
by this lane. Schema application stays an operator/CLI responsibility.

## Host composition

The host registers one provider connection through `Elsa.Persistence.Groundwork.Composition`, then calls
`AddGroundworkWorkflowsDesignStores()` alongside whichever runtime, activity-design, distributed, publishing,
or dashboard lanes it needs. Each lane receives the target name explicitly, so a host can split independent
stores while keeping transaction-spanning operations co-located. Workbench composes the complete default
single-target shape through its provider and lane features.

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
- Provider connection and target composition: [`../../../../Persistence/Groundwork/EXTENSION_POINTS.md`](../../../../Persistence/Groundwork/EXTENSION_POINTS.md)
- Repo-wide index: [`../../../../../../EXTENSION_POINTS.md`](../../../../../../EXTENSION_POINTS.md)
