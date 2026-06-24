# Extension points — Activities.Design.Persistence domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Activities.Design.Persistence.EFCore` — the EF Core persistence feature that provides the default `IAddActivityDefinitionCommand` implementation and the `IEntitySavingHandler`/`IEntityLoadingHandler` impls for the activity catalog entities.

---

## Overridable contracts

### `IAddActivityDefinitionCommand` *(Core — `Elsa.Activities.Design.Persistence.Core`)*
- **Signature:** `Task Execute(ActivityDefinition definition, ActivityDefinitionVersion version, CancellationToken ct)`
- **Default impl:** EF Core transactional insert in this feature.
- **Override:** `services.Replace(ServiceDescriptor.Scoped<IAddActivityDefinitionCommand, MyCommand>())` — e.g. to use a different store or add additional side-effects on activity creation.

### `IActivityDefinitionLookup` *(Core — `Elsa.Activities.Design.Persistence.Core`)*
- **Default impl:** Core `ActivityDefinitionLookup` — read path via the named `IActivityDefinitionStore` + `IActivityDefinitionVersionStore` ports.
- **Override:** replace with a custom lookup (caching layer, alternate store).

---

## Entity persistence contributors

This feature ships two `IEntitySavingHandler` + `IEntityLoadingHandler` implementations for the activity catalog entities. These contributor interfaces are defined in `Elsa.Persistence.EFCore` — see [`Elsa.Persistence.EFCore/EXTENSION_POINTS.md`](../Elsa.Persistence.EFCore/EXTENSION_POINTS.md) for the interface contracts and aggregating handlers.

- **`ActivityDefinitionVersionSavingHandler`** — serialises `Inputs`/`Outputs`/`DesignFacets` + the implementation descriptor; derives `ImplementationKind`. Registered via `AddEntitySavingHandlersFrom(assembly)` in `EFCoreActivitiesPersistenceFeatureBase`.
- **`ActivityDefinitionVersionLoadingHandler`** — deserialises `*Source` columns + the `ImplementationDescriptorPayload` back into rich projections. Failures throw `ActivityDescriptorDeserialisationException` (with version id + kind context).

---

## Cross-references

- `IEntitySavingHandler`/`IEntityLoadingHandler` contracts + aggregators: [`Elsa.Persistence.EFCore/EXTENSION_POINTS.md`](../Elsa.Persistence.EFCore/EXTENSION_POINTS.md).
- Activity reconciliation extension points: [`Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md`](../Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.2 + §2.22.1.
