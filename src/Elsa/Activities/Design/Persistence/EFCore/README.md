# Elsa.Activities.Design.Persistence.EFCore

Provider-agnostic EF Core persistence layer for the activity catalog. Inherits the base shell feature `EFCorePersistenceShellFeatureBase<ActivitiesDesignDbContext>`; provider-specific features (e.g. SQLite) inherit from `EFCoreActivitiesPersistenceFeatureBase` and supply the actual `ConfigureProvider` callback.

## What this feature provides

- **`ActivitiesDesignDbContext`** — `DbSet`s for `ActivityDefinitions`, `ActivityDefinitionVersions`. *(Under Model X — Unit C 2026-05-28, pending 2026-06-01 review — the operational `ActivityDefinitionReconciliationState` sibling has been removed; reconciliation is one-shot at creation time and the immutable `ActivityDefinitionVersion.ProvisioningHash` carries the content hash used by duplicate detection.)*
- **EF Core configurations** for each entity (composite unique indexes, foreign keys, max-length conventions).
- **`IAddActivityDefinitionCommand`** → `AddActivityDefinitionCommand` (transactional parent+version insert).
- **`IActivityDefinitionLookup`** → `ActivityDefinitionLookup` — the picker query (Model X: catalog membership only; no removal filter).
- **`ActivityDefinitionVersionSavingHandler`** — a typed `IEntitySavingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>` contributor (framework §2.6.1, action-named handler). Serialises `Inputs`/`Outputs`/`DesignFacets` and writes the opaque descriptor payload from the `DescriptorPayload` `JsonElement` into `DescriptorPayloadSource`. `DescriptorType` is set by the producer (reconciler / design API), never derived here — the design domain has no `Kind`. The single `ApplyEntitySavingHandlers` aggregator (registered once by the EF Core base feature) dispatches it when `OnEntitySaving` fires.
- **`ActivityDefinitionVersionLoadingHandler`** — a typed `IEntityLoadingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>` contributor. Deserialises `*Source` columns and parses `DescriptorPayloadSource` into a `JsonElement` (`DescriptorPayload`). It resolves **no** descriptor CLR type — there is no kind→type registry — so it needs no descriptor-type dependency (Elsa §E2.2). Dispatched by the single `ApplyEntityLoadingHandlers` aggregator on `OnEntityLoading`.

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
| `ActivityDefinitionVersion` | Append-only. Immutable: `Version`, `DefinitionId`, `DescriptorType`, `DescriptorPayloadSource`, `ExecutionType`, `Inputs/Outputs/DesignFacetsSource`, `SourceKind`, `SourceId`, `Hash`. `DescriptorPayload` (`JsonElement`) is `[NotMapped]` — hydrated by the loading handler by parsing `DescriptorPayloadSource`. |

## Persistence invariants

- `Entity.RowNumber` and `Entity.CreatedAt` are write-once on every entity; enforced centrally via `ApplyBaseEntityImmutability` in `ElsaDbContextBase`.
- Domain-specific write-once properties (e.g. `ActivityTypeKey`, `DescriptorType`, `DescriptorPayloadSource`) are declared via `PropertySaveBehavior.Throw` in each entity's `IEntityTypeConfiguration<T>`.
- `TenantId` index registered centrally via `ApplyTenantIdIndex` on every `TenantEntity` descendant.

## Owned exception surface

None. The loading handler resolves no descriptor CLR type and never deserialises the descriptor into a
concrete type, so there is no descriptor-deserialisation failure to surface here — the design domain treats
the payload as opaque JSON. (The former `ActivityDescriptorDeserialisationException` was removed with the
descriptor-opaque reshape; the only descriptor deserialisation failure now occurs runtime-side, inside the
owning constructor's deserialize bridge.)
