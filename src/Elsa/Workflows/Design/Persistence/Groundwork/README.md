# Elsa.Workflows.Design.Persistence.Groundwork

Groundwork (document-store) provider for workflow-design persistence. The read/write contracts live in
`Elsa.Workflows.Design.Persistence.Core`; this feature supplies their durable Groundwork
implementations. The EF Core design persistence implementation is removed by spec 093 US4, so
Groundwork is the sole workflow-design persistence provider.

## What this feature provides

- **Store and command adapters** — Groundwork implementations of the workflow-design read ports and
  commands (`GroundworkWorkflowDefinitionStore`, `GroundworkWorkflowDefinitionDraftStore`,
  `GroundworkCreateDraftCommand`, `GroundworkPromoteDraftToVersionCommand`, …). See
  [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md) for the full replacement-contract table.
- **Storage manifest** — `WorkflowsDesignGroundworkStorageManifestSource` declares the physical shape
  (projected columns, indexes, bounded scale-bearing queries, and width limits). Searchable text
  columns are bounded to 256 characters and identity/sort-key columns to 128; over-limit values fail
  projection validation rather than truncate.
- **Atomic writer seam** — `IDesignAtomicWriter` (default `GroundworkDesignAtomicWrite`, from
  `Elsa.Persistence.Groundwork.Querying`) provides replay-safe multi-document mutation over the shared
  `designOperation` ledger, contributed once across both design lanes.
- **Draft origination seam** — `IDraftOriginator` (default `DraftOriginator`) owns identity allocation,
  per-draft locking, validation, atomic persistence, and lifecycle-event publication for the create and
  clone commands.

## Registering the provider

`AddGroundworkWorkflowsDesignStores()`
(`Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection`) swaps the design read ports to
their Groundwork implementations, contributes the manifest sources, and registers the specialization
seams. A provider feature must register the concrete `Groundwork.Documents.Store.IDocumentStore` and
the host `Elsa.Serialization.Core.IPayloadSerializer` that these adapters consume.

Two composition shapes:

- **Unified** — enable one provider feature
  (`AddGroundwork{Sqlite|SqlServer|PostgreSql|MongoDb}UnifiedPersistence(…)`). It routes through
  `AddGroundworkUnifiedStoreFamilies()`, which composes this lane with the other Groundwork store
  families over one physical store, and the provider's document-store registration owns the
  `GroundworkSchemaReadinessTask` startup guard.
- **Lane-specific** — call `AddGroundworkWorkflowsDesignStores()` directly against a manually registered
  provider document store and payload serializer.

Schema application is an operator/CLI responsibility; the readiness guard validates the applied target
at startup and never auto-applies unless the host opts into safe startup auto-apply.

## See also

- [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md) — replacement contracts, manifest, seams, and composition.
- [Unified Groundwork persistence README](../../../../Persistence/Groundwork/Unified/README.md) —
  provider selection, connection secrets, MongoDB topology, and schema CLI.
