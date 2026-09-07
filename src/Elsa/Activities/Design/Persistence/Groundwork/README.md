# Elsa.Activities.Design.Persistence.Groundwork

Current-only Groundwork v2 persistence for Elsa activity design. The adapter uses the public
`Groundwork.Kernel`, `Groundwork.Query.Model`, and `Groundwork.Store` APIs through
`Elsa.Persistence.Groundwork.V2`; it does not open provider connections or execute provider-specific
queries.

## What this feature provides

- activity-definition and activity-version stores;
- reusable activity authoring, draft, publication, layout, fork, dependency, recommendation, and
  management-projection stores;
- v2 storage-unit declarations in `ActivitiesDesignStorageManifest`;
- replay-safe activity-definition and activity-version creation through the local
  `IDesignAtomicWriter` boundary.

Every row is scoped, keyed by its stable identity, and optimistically concurrent. Queries are built as
the public Groundwork query AST with deterministic order and bounded paging. Access is taken from the
current `IPersistenceAccessContextAccessor`; cross-scope reads require an explicit privileged context.

## Registering the provider

Call `AddGroundworkActivitiesDesignStores()` after registering the v2
`IGroundworkStorageSessionSource`, provider connection, and host payload serializer. The registration
adopts the activity-design units into the selected v2 target and replaces the activity-design contracts
with the implementations listed in `EXTENSION_POINTS.md`.

The selected provider owns schema admission. No migration, compatibility alias, fallback, or dual-write
path is registered by this feature.

The activity-definition-version projection has a pre-GA clean-schema boundary. Required
`definitionId`/`semVerSortKey` projections and their unique tuple live in the versioned physical table
`elsa_activity_definition_versions_v2` at storage schema version `2`. There is no in-place migration
from the earlier preview table. Before enabling this build, discard and reprovision the complete
activity-design Groundwork persistence set from the current manifest, then recreate or import the
activity designs. Retaining old version rows beside the new table can leave authoring, publication,
layout, and dependency records pointing at a generation the active store no longer reads.

## See also

- [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md) — replacement contracts and v2 seams.
- [`../../../../Persistence/Groundwork/EXTENSION_POINTS.md`](../../../../Persistence/Groundwork/EXTENSION_POINTS.md)
  — provider connection and target composition.
