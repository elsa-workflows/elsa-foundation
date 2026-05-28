# Elsa.Activities.Design.Persistence.EFCore

Provider-agnostic EF Core persistence layer for the activity catalog. Inherits the base shell feature `EFCorePersistenceShellFeatureBase<ActivitiesDesignDbContext>`; provider-specific features (e.g. SQLite) inherit from `EFCoreActivitiesPersistenceFeatureBase` and supply the actual `ConfigureProvider` callback.

## What this feature provides

- **`ActivitiesDesignDbContext`** — `DbSet`s for `ActivityDefinitions`, `ActivityDefinitionVersions`, `ActivityDefinitionReconciliationStates`.
- **EF Core configurations** for each entity (composite unique indexes, foreign keys, max-length conventions).
- **`IAddActivityDefinitionCommand`** → `AddActivityDefinitionCommand` (transactional parent+version insert).
- **`IActivityDefinitionLookup`** → `ActivityDefinitionLookup` — the picker query (LEFT JOIN reconciliation-state, excludes `RemovedAt`).
- **`ActivityDefinitionVersionSavingHandler`** — migrated to the §2.6.1 `OnEntitySaving` domain-event surface (Unit B US5 / Unit A code-checklist closure). Serialises `Inputs`/`Outputs`/`Ports` + the implementation descriptor; derives `ImplementationKind` from `descriptor.Kind`.
- **`ActivityDefinitionVersionLoadingHandler`** — `IEntityLoadingHandler<,>`. Deserialises `*Source` columns + the `ImplementationDescriptorPayload` (using the descriptor registry's kind→type lookup) back into rich projections. Failures throw `ActivityDescriptorDeserialisationException` with version id + kind context.

## Cross-feature contributions (handlers this feature registers)

- **`IDomainEventHandler<OnEntitySaving>`** → `ActivityDefinitionVersionSavingHandler` (registered via `AddDomainEventHandlersFrom` in `EFCoreActivitiesPersistenceFeatureBase`).
- **`IEntityLoadingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>`** → `ActivityDefinitionVersionLoadingHandler`. Registered explicitly by provider features (e.g. `SqliteActivitiesDesignPersistenceShellFeature`).

## Feature inheritance chain

- `EFCorePersistenceShellFeatureBase<TDbContext>` *(framework persistence base; DbContextFactory, queries, commands, migration startup task)*
  - `EFCoreActivitiesPersistenceFeatureBase` *(this assembly — adds the activity-catalog lookup + commands + domain-event handler scan)*
    - `SqliteActivitiesDesignPersistenceShellFeature` *(SQLite provider; supplies `ConfigureProvider` + the SQLite `IEntityModelCreatingHandler`)*

## Persisted shape (Activities domain)

| Entity | Notes |
|---|---|
| `ActivityDefinition` | Identity layer. Immutable: `ActivityTypeKey`, `SourceKind`, `SourceId`, `ProvisionedAt`, `ProvisionedBy`. Unique composite index `(SourceKind, SourceId, ActivityTypeKey)`. |
| `ActivityDefinitionVersion` | Append-only. Immutable: `Version`, `DefinitionId`, `ActivityTypeKey`, `ImplementationKind`, `ImplementationDescriptorPayload`, `ExecutionType`, `Inputs/Outputs/Ports*Source`. `ImplementationDescriptor` is `[NotMapped]` — hydrated by the loading handler from the payload. |
| `ActivityDefinitionReconciliationState` | 1:0..1 sibling of `ActivityDefinition`. Mutable — rewritten each reconciliation pass. Unique index on `ActivityDefinitionId`; non-unique on `IsStale`. |

## Persistence invariants

- All immutable fields enforced via `[Immutable]` + `PreventImmutableChanges` (the central scanner in `ElsaDbContextBase`).
- `TenantId` index registered centrally via `ApplyTenantIdIndex` on every `TenantEntity` descendant.

## Owned exception surface

- **`ActivityDescriptorDeserialisationException`** (in `Elsa.Activities.Design.Persistence.Core/Exceptions/`) — raised by the loading handler when the persisted descriptor payload cannot be deserialised. Carries version id + kind. Replaces raw `JsonException` per framework §2.23.5.
