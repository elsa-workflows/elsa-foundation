# Elsa.Activities.Design.Persistence.Groundwork

Groundwork (document-store) provider for activity-design persistence. The read/write contracts live in
`Elsa.Activities.Design.Persistence.Core` (and the reusable-activity ports in
`Elsa.Activities.Design.Core`); this feature supplies their durable Groundwork implementations. The EF
Core design persistence implementation is removed by spec 093 US4, so Groundwork is the sole
activity-design persistence provider.

## What this feature provides

- **Store and command adapters** — Groundwork implementations of the activity-design read ports and
  commands (`GroundworkActivityDefinitionStore`, `GroundworkActivityDefinitionManagementProjectionStore`,
  `GroundworkActivityDependencyProjection`, `GroundworkActivityUpgradePlanStore`, the
  `GroundworkReusableActivityStores` aggregate that backs the reusable-activity authoring, draft,
  publication, layout, fork, and dependency ports, and
  `GroundworkRecommendedActivityDefinitionPickerStore`, which reads recommendation pages from the stable
  management projection). See
  [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md) for the full replacement-contract table.
- **Storage manifest** — `ActivitiesDesignGroundworkStorageManifestSource` declares the physical shape
  (projected columns, indexes, bounded scale-bearing queries, and width limits). The enum-kind members
  (`ManagementAuthorityField`, `ManagementDraftStatusField`, `ManagementVersionLifecycleField`) are
  declared numeric via `NumericMemberPaths`; every other member is a keyword string. Over-limit values
  fail projection validation rather than truncate.
- **Atomic writer seam** — `IDesignAtomicWriter` (default `GroundworkDesignAtomicWrite`, from
  `Elsa.Persistence.Groundwork.Querying`) provides replay-safe multi-document mutation over the shared
  `designOperation` ledger, contributed once across both design lanes.

## Registering the provider

`AddGroundworkActivitiesDesignStores()`
(`Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection`) swaps the design read ports to
their Groundwork implementations, contributes the manifest sources, and registers the atomic-writer
seam. A provider feature must register the concrete `Groundwork.Documents.Store.IDocumentStore` **and**
`IBoundedDocumentStore` (the bounded store backs the admitted activity-management and reusable-activity
queries) plus the host `Elsa.Serialization.Core.IPayloadSerializer`.

Two composition shapes:

- **Unified** — enable one provider feature
  (`AddGroundwork{Sqlite|SqlServer|PostgreSql|MongoDb}UnifiedPersistence(…)`). It routes through
  `AddGroundworkUnifiedStoreFamilies()`, which composes this lane with the other Groundwork store
  families over one physical store; the provider's document-store registration owns the
  `GroundworkSchemaReadinessTask` startup guard.
- **Lane-specific** — call `AddGroundworkActivitiesDesignStores()` directly against a manually
  registered provider document store, bounded store, and payload serializer.

Schema application is an operator/CLI responsibility; the readiness guard validates the applied target
at startup and never auto-applies unless the host opts into safe startup auto-apply.

## See also

- [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md) — replacement contracts, manifest, seams, and composition.
- [Unified Groundwork persistence README](../../../../Persistence/Groundwork/Unified/README.md) —
  provider selection, connection secrets, MongoDB topology, and schema CLI.
