# Elsa.Activities.Design.Persistence.EFCore

Provider-agnostic EF Core persistence layer for the activity catalog. Inherits the base shell feature `EFCorePersistenceShellFeatureBase<ActivitiesDesignDbContext>`; provider-specific features (e.g. SQLite) inherit from `EFCoreActivitiesPersistenceFeatureBase` and supply the actual `ConfigureProvider` callback.

## What this feature provides

- **`ActivitiesDesignDbContext`** — `DbSet`s for `ActivityDefinitions`, `ActivityDefinitionVersions`. *(Under Model X — Unit C 2026-05-28, pending 2026-06-01 review — the operational `ActivityDefinitionReconciliationState` sibling has been removed; reconciliation is one-shot at creation time and the immutable `ActivityDefinitionVersion.ProvisioningHash` carries the content hash used by duplicate detection.)*
- **EF Core configurations** for each entity (composite unique indexes, foreign keys, max-length conventions).
- **`IAddActivityDefinitionCommand`** → `AddActivityDefinitionCommand` (transactional parent+version insert).
- **`IActivityDefinitionLookup`** → `ActivityDefinitionLookup` — the picker query (Model X: catalog membership only; no removal filter).
- **`ActivityDefinitionVersionSavingHandler`** — a typed `IEntitySavingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>` contributor (framework §2.6.1, action-named handler). Serialises `Inputs`/`Outputs`/`Ports` + the implementation descriptor; derives `ImplementationKind` from `descriptor.Kind`. The single `ApplyEntitySavingHandlers` aggregator (registered once by the EF Core base feature) dispatches it when `OnEntitySaving` fires.
- **`ActivityDefinitionVersionLoadingHandler`** — a typed `IEntityLoadingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>` contributor. Deserialises `*Source` columns + the `ImplementationDescriptorPayload` (using the descriptor registry's kind→type lookup) back into rich projections. Dispatched by the single `ApplyEntityLoadingHandlers` aggregator on `OnEntityLoading`. Failures throw `ActivityDescriptorDeserialisationException` with version id + kind context.

## Cross-domain contributions

- **`IEntitySavingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>`** → `ActivityDefinitionVersionSavingHandler` (registered via the `AddEntitySavingHandlersFrom` assembly scan in `EFCoreActivitiesPersistenceFeatureBase`). Dispatched by the single `ApplyEntitySavingHandlers : IEventHandler<OnEntitySaving>` aggregator — this feature does NOT register its own `IEventHandler<OnEntitySaving>`.
- **`IEntityLoadingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>`** → `ActivityDefinitionVersionLoadingHandler`. Registered explicitly by provider features (e.g. `SqliteActivitiesDesignPersistenceShellFeature`). Dispatched by the single `ApplyEntityLoadingHandlers : IEventHandler<OnEntityLoading>` aggregator.

See [`Elsa.Persistence.EFCore/EXTENSION_POINTS.md`](../Elsa.Persistence.EFCore/EXTENSION_POINTS.md) for the saving/loading event contract (Events section) plus that domain's overridable contracts and contributor interfaces, and the repo-root [`EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md) for the cross-domain index.

## Feature inheritance chain

- `EFCorePersistenceShellFeatureBase<TDbContext>` *(framework persistence base; DbContextFactory, queries, commands, migration startup task)*
  - `EFCoreActivitiesPersistenceFeatureBase` *(this assembly — adds the activity-catalog lookup + commands + domain-event handler scan)*
    - `SqliteActivitiesDesignPersistenceShellFeature` *(SQLite provider; supplies `ConfigureProvider` + the SQLite `IEntityModelCreatingHandler`)*

## Persisted shape (Activities domain)

| Entity | Notes |
|---|---|
| `ActivityDefinition` | Identity layer. Immutable: `ActivityTypeKey`, `SourceKind`, `SourceId`, `ReconciledAt`, `ReconciledBy`. Unique composite index `(SourceKind, SourceId, ActivityTypeKey)`. |
| `ActivityDefinitionVersion` | Append-only. Immutable: `Version`, `DefinitionId`, `ActivityTypeKey`, `ImplementationKind`, `ImplementationDescriptorPayload`, `ExecutionType`, `Inputs/Outputs/Ports*Source`, `ProvisioningHash`. `ImplementationDescriptor` is `[NotMapped]` — hydrated by the loading handler from the payload. |

## Persistence invariants

- All immutable fields enforced via `[Immutable]` + `PreventImmutableChanges` (the central scanner in `ElsaDbContextBase`).
- `TenantId` index registered centrally via `ApplyTenantIdIndex` on every `TenantEntity` descendant.

## Owned exception surface

- **`ActivityDescriptorDeserialisationException`** (in `Elsa.Activities.Design.Persistence.Core/Exceptions/`) — raised by the loading handler when the persisted descriptor payload cannot be deserialised. Carries version id + kind. Replaces raw `JsonException` per framework §2.23.5.
