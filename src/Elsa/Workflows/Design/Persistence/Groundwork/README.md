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
- **Storage manifest** — `WorkflowsDesignStorageManifest` declares the physical shape (projected
  columns, indexes, bounded scale-bearing queries, and width limits). Its units are declared directly
  against the public v2 catalog by the lane registration; `WorkflowsDesignGroundworkStorageManifestSource`
  is the lane identity that publishes them and lets a cross-lane caller resolve this lane's target.
  Searchable text columns are bounded to 256 characters and identity/sort-key columns to 128;
  over-limit values fail projection validation rather than truncate.
- **Atomic writer seam** — `IDesignAtomicWriter` (default `GroundworkDesignAtomicWrite`, in this
  project) provides replay-safe multi-unit mutation over this lane's own operation ledger,
  `workflowDesignOperation`. The activity-design lane owns a separate `activityDesignOperation`
  ledger: the two carry distinct unit ids because a single-target host composing both would otherwise
  declare one id twice, with different tables and schemas, and fail at composition.
- **Draft origination seam** — `IDraftOriginator` (default `DraftOriginator`) owns identity allocation,
  per-draft locking, validation, atomic persistence, and lifecycle-event publication for the create and
  clone commands.

## Registering the provider

`AddGroundworkWorkflowsDesignStores()`
(`Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection`) swaps the design read ports to
their Groundwork implementations, contributes the manifest sources, and registers the specialization
seams. A provider feature must register an `IGroundworkStorageSessionSource`
(`Elsa.Persistence.Groundwork.Composition`) bound to a provider connection, plus the host
`Elsa.Serialization.Core.IPayloadSerializer` that these adapters consume.

Two composition shapes:

- **Unified** — enable one provider feature
  (`AddGroundwork{Sqlite|SqlServer|PostgreSql|MongoDb}UnifiedPersistence(…)`). It routes through
  `AddGroundworkUnifiedStoreFamilies()`, which composes this lane with the other Groundwork store
  families over one provider connection.
- **Lane-specific** — call `AddGroundworkWorkflowsDesignStores()` directly against a manually registered
  provider connection and payload serializer.

Schema application is not an operator step. The storage session source admits every registered unit at
startup, so the host creates and validates its own schema; a unit whose live schema has drifted from its
declaration fails admission rather than being silently re-applied.

## See also

- [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md) — replacement contracts, manifest, seams, and composition.
