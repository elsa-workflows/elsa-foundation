# Phase 0 — Research

**Status:** Complete. The clarify pass closed architectural ambiguity; this file consolidates the code-discovery findings + best-practice references that inform the plan.

## R1 — Existing `ElsaDbContextBase` mechanism

**Decision.** Migrate only the **saving** handlers (`IGlobalEntitySavingHandler` + typed `IEntitySavingHandler<,>`) to a new `EntitySaving` domain event. Keep `IEntityModelCreatingHandler` as-is — model-creating is a sync side-effect chain on the shared `ModelBuilder`, not a contribution flow in the §2.6.1 sense.

**Findings** (from [`src/Elsa.Persistence.EFCore/ElsaDbContextBase.cs`](../../src/Elsa.Persistence.EFCore/ElsaDbContextBase.cs)):

- `SaveChangesAsync` → `BeforeSavingChanges` → calls four steps:
  1. `PreventImmutableChanges` — `[Immutable]` enforcement. **Preserve as-is.**
  2. `ApplyTimestamps` — stamps `CreatedAt` / `LastModifiedAt`. **Preserve as-is.**
  3. `ApplyGlobalSavingHandlers` — `IGlobalEntitySavingHandler` from DI per entry. **Migrate to `EntitySaving(DbContext, EntityEntry)`** dispatched via `IDomainEventSender` (async; the surrounding `BeforeSavingChanges` is already async).
  4. `ApplyEntitySavingHandlers` — typed `IEntitySavingHandler<TDbContext, TEntity>` via reflection. **Migrate to the same `EntitySaving` event** with entity-type filtering inside the handler.
- `OnModelCreating` → `ConfigureEntityModel` → calls:
  1. `ApplyRowNumberIndex` — preserve. Pattern for `ApplyTenantIdIndex`.
  2. `ApplyEntityModelCreatingHandlers` — `IEntityModelCreatingHandler` from DI. **Preserve as-is.** `OnModelCreating` is intrinsically sync; `IDomainEventSender.Send` is async; forcing a sync-over-async dispatch is a smell with no architectural benefit. §2.6.1's domain-event contract addresses *contribution flows* (handlers contribute data the sender collects); model-creating is structurally different — each handler mutates the shared `ModelBuilder` and nothing flows back. The legacy provider-interface pattern is the right tool here.
  3. `ApplyImmutability` — preserve.

**Rationale.** §2.6.1 prescribes the domain-event mechanism for *contribution* (sender publishes; handlers add to a carried payload; sender uses what handlers contributed). `IEntityModelCreatingHandler` is a *side-effect chain* (each handler mutates the same external object; nothing returned). The two patterns coexist in the constitution as distinct mechanisms for distinct problem shapes. The activity-catalog saving handlers move to domain events because the saving path IS contribution-shaped (handlers populate JSON columns, the SUM of all handlers determines what gets persisted); model-creating stays on the provider interface because it isn't.

This carve-out is **codified as framework §2.6.5 (Sync contributor pattern — rare exception)**, added to the constitution as part of Unit B's amendments. Canonical worked example: **Elsa §E3.9** (`IEntityModelCreatingHandler` over EF Core's `OnModelCreating`). Future plans citing G21 will match against §2.6.5's three criteria rather than treating the legacy interface as a §2.6.1 violation.

**Alternatives considered.**
- Add a sync companion `IDomainEventDispatcher.Dispatch(IDomainEvent)` to `Elsa.Mediator.Core` and migrate `OnModelCreating` too. **Rejected** — over-engineers the mediator surface for a single use case whose structural shape isn't event-like.
- Migrate model-creating with `.GetAwaiter().GetResult()` at the dispatch site. **Rejected** — sync-over-async smell; no architectural benefit.
- Keep both saving + model-creating on the legacy provider interfaces. **Rejected** for saving — the saving path IS contribution-shaped and benefits from the §2.6.1 mechanism; the migration is constitutionally correct for that path.

## R2 — `TenantId` central index registration

**Decision.** Extend `ElsaDbContextBase.OnModelCreating` with a method that scans the model for entity types inheriting from `TenantEntity` and registers a `TenantId` index on each. Mirror of the existing `ApplyRowNumberIndex`.

**Pattern** (from `ApplyRowNumberIndex`):

```csharp
private static void ApplyTenantIdIndex(ModelBuilder modelBuilder)
{
    foreach (var entity in modelBuilder.Model.GetEntityTypes())
    {
        if (typeof(TenantEntity).IsAssignableFrom(entity.ClrType))
        {
            modelBuilder.Entity(entity.ClrType)
                .HasIndex(nameof(TenantEntity.TenantId));
        }
    }
}
```

**Rationale.** Central registration removes per-entity boilerplate (no per-configuration `HasIndex(x => x.TenantId)` calls) and ensures the index is consistently applied across activity-catalog AND workflow-side entities. Per spec FR-012 / SC-016.

## R3 — `IDomainEventSender` API

**Decision.** Use the existing `IDomainEventSender.Send(IDomainEvent, CancellationToken)` API to dispatch the new events. Confirmed from [`src/Elsa.Mediator.Core/Contracts/IDomainEventSender.cs`](../../src/Elsa.Mediator.Core/Contracts/IDomainEventSender.cs):

```csharp
public interface IDomainEventSender
{
    Task Send(IDomainEvent domainEvent, CancellationToken cancellationToken);
}
```

The existing `ActivityVersionProvisioner` already consumes this contract — pattern is established. Handler registration is via DI; the pipeline awaits all handlers end-to-end per framework §2.6.1.

## R4 — EF Core shadow-column pattern for the descriptor

**Decision.** No `*Source` CLR property; no custom `JsonConverter`. `ActivityDefinitionVersion` exposes only `[NotMapped] IImplementationDescriptor ImplementationDescriptor` (the combined rich projection — the descriptor's runtime CLR type IS the kind binding). The persisted form is an **EF Core shadow column** named `ImplementationDescriptor` (string, JSON payload), accessed in handlers via `EntityEntry.Property("ImplementationDescriptor").CurrentValue`.

**Loading mechanism.** The loading handler:
1. Reads the entity's `ImplementationKind` (smart-enum value-record column).
2. Resolves the matching CLR descriptor type via `IImplementationDescriptorRegistry.Resolve(kind)` — an explicit registry following the canonical §2.6.1 Registry + StartUp Task sub-pattern (per `contracts/IImplementationDescriptorRegistry.md`).
3. Reads the shadow column's string value.
4. Calls `IPayloadSerializer.Deserialize(json, type)` — the existing Elsa serialisation contract. If the API is non-generic, use it directly; if generic-only, construct via reflection: `typeof(IPayloadSerializer).GetMethod(nameof(IPayloadSerializer.Deserialize)).MakeGenericMethod(type).Invoke(serializer, [json])`.
5. Assigns the result to the entity's `[NotMapped] ImplementationDescriptor` property.

The registry is populated at startup by `ImplementationDescriptorRegistryStartupTask`, which publishes `OnImplementationDescriptorsInitializing` and flushes contributions into the registry. The activities runtime feature contributes the CLR mapping; Unit G later contributes the Workflow mapping; future units contribute analogously. This is symmetric with the existing `IActivityImplementationResolverRegistry` and its `OnActivityImplementationResolversInitializing` event — two registries, two events, two startup tasks, same canonical §2.6.1 pattern.

**Saving mechanism.** The saving handler does the inverse — `IPayloadSerializer.Serialize(entity.ImplementationDescriptor)` → write to `entry.Property("ImplementationDescriptor").CurrentValue`.

**Why no custom `JsonConverter`.** A `JsonConverter` would couple the descriptor type's serialisation contract to System.Text.Json conventions and create framework-coupling at the `IImplementationDescriptor` declaration site. Since `IPayloadSerializer` is the existing Elsa serialisation seam (provider-agnostic; can be Newtonsoft or System.Text.Json), driving deserialisation through it via type-resolution-then-deserialise is simpler, less coupling, and works with any future payload serializer implementation.

**Why no `*Source` CLR property.** The `*Source` suffix pattern (used today for `InputsSource` / `OutputsSource` / `DesignFacetsSource`) exposes the persistence form at the entity surface. For the new descriptor we use a shadow column instead — the entity surface carries one name (`ImplementationDescriptor`, the rich projection); the persisted form is invisible at the CLR level. Same EF Core mechanism as any other shadow property; cleaner surface.

**Alternatives considered.**
- `JsonPolymorphic` + `JsonDerivedType` attributes on the interface declaration. **Rejected** — closes the open-discriminator door (every kind must be declared at the interface's source location).
- Custom `JsonConverter<IImplementationDescriptor>` with runtime type-map. **Rejected** — adds System.Text.Json coupling at the descriptor's interface; redundant with what `IPayloadSerializer` + type-resolution already does.
- `*Source` suffix on a CLR property (existing pattern). **Rejected** — shadow column is cleaner for the entity's external surface; the descriptor's serialisation is purely an internal concern.
- Derive kind-→-type mapping from each registered `IActivityImplementationResolver<TDescriptor>` via reflection on the generic argument. **Rejected at session 4 clarify** — couples the descriptor-registry concern (persistence-layer deserialisation) to the resolver-registry concern (runtime construction). Symmetric application of the §2.6.1 pattern (two registries, two events, two startup tasks) keeps each registry's contract clear and means features can register descriptor types without registering a matching resolver (e.g. a descriptor present in storage for a kind whose resolver is in a not-yet-installed feature).

## R5 — Smart-enum value-record persistence

**Decision.** Persist `ImplementationKind` and `SourceKind` as the wrapped `string Value`. The EF Core column type is `string`; a converter (`ValueConverter<TKind, string>`) maps between record ↔ string at the persistence boundary.

**Pattern.**

```csharp
public sealed record ImplementationKind(string Value)
{
    public static readonly ImplementationKind Clr = new("Clr");
    public static readonly ImplementationKind Workflow = new("Workflow");
}

// EF Core mapping:
builder.Property(x => x.ImplementationKind)
    .HasConversion(
        kind => kind.Value,
        value => new ImplementationKind(value));
```

Equality is value-based (record default); reference comparison works as well because `static readonly` instances are de-duplicated within the AppDomain — but code should prefer `Equals` / pattern matching by value.

## R6 — CLR Type activation pattern for the factory

**Decision.** The `IActivityFactory` resolves an `IActivity` instance through DI when possible, falling back to `ActivatorUtilities.CreateInstance` for ad-hoc activation. The resolved CLR `Type` from `IActivityImplementationResolver` is the activation target.

**Sketch.**

```csharp
public sealed class ActivityFactory : IActivityFactory
{
    private readonly IServiceProvider _services;
    private readonly IActivityImplementationResolverRegistry _resolvers;

    public async ValueTask<IActivity> Create(
        IImplementationDescriptor descriptor,
        IEnumerable<InputState> inputs,
        IEnumerable<OutputState> outputs,
        CancellationToken ct)
    {
        var resolver = _resolvers.Resolve(descriptor);  // dispatches by descriptor.Kind
        var clrType = resolver.Resolve(descriptor);
        var activity = (IActivity)ActivatorUtilities.CreateInstance(_services, clrType);
        ApplyInputs(activity, inputs);
        ApplyOutputs(activity, outputs);
        return activity;
    }
}
```

`ApplyInputs` / `ApplyOutputs` transform each `*State` into the corresponding `Input<T>` / `Output<T>` property on the activity via reflection (mapped by `ReferenceKey` → `ArgumentDefinition.ReferenceKey`). The `ArgumentValue.ExpressionType` selects which `IExpression` concrete to instantiate (literal, JavaScript, Liquid, …).

**Why `ValueTask`?** Resolvers are sync (`Type Resolve(...)`) but the *transformation* of states into expressions can be async (e.g. resolving an expression's prepared form, looking up cached compilations). `ValueTask` lets sync paths skip allocation.

## R7 — Resolver contribution via Registry + StartUp Task

**Decision.** Per framework §2.6.1 sub-pattern (worked example in Elsa §E3.3):

1. `Elsa.Activities.Runtime.Core` defines `IActivityImplementationResolverRegistry` and `OnActivityImplementationResolversInitializing(List<IActivityImplementationResolver>)`.
2. The activities-runtime feature registers a StartUp task that:
   ```csharp
   var resolvers = new List<IActivityImplementationResolver>();
   await sender.Send(new OnActivityImplementationResolversInitializing(resolvers), ct);
   registry.RegisterAll(resolvers);
   ```
3. Other features (Workflow bridge in Unit G, Remote feature later, …) handle the event to add their resolvers.
4. `IActivityFactory.Create(...)` reads the populated registry at construction time — sync dispatch by kind; the async population happened once at startup.

Matches the canonical Elsa §E3.3 worked example.

## R8 — Existing `JsonCatalogEntry` shape ([`elsa-core-activities.json`](../../elsa-core-activities.json))

**Decision.** Map the existing JSON shape to the new entity model. Each catalog entry currently carries `typeInfo`, `version`, `kind`, `definition.uniqueName`, `definition.category`, `definition.displayName`, `definition.description`, `definition.isBrowsable`, `inputs`, `outputs`, `designFacets`.

**Mapping**:

| JSON field | Maps to |
|---|---|
| `definition.uniqueName` | `ActivityDefinition.ActivityTypeKey` (rename) |
| `definition.category` / `displayName` / `description` | `ActivityDefinition.Category` / `DisplayName` / `Description` (unchanged) |
| `definition.isBrowsable` | **DROPPED** — field removed per Q3 of clarify session 1 mid-pass |
| `typeInfo` | `ActivityDefinitionVersion.ImplementationDescriptor` (`[NotMapped]`) ← `new ClrImplementationDescriptor(TypeInformation)`; persisted to the EF shadow column `ImplementationDescriptor` (string JSON) by the saving handler. `ImplementationKind = ImplementationKind.Clr`. |
| `version` | `ActivityDefinitionVersion.Version` |
| `kind` | `ActivityDefinitionVersion.Kind` (existing `ActivityKind` enum unchanged) |
| `inputs` / `outputs` / `designFacets` | `InputsSource` / `OutputsSource` / `DesignFacetsSource` (unchanged shadow-JSON pattern) |
| *(provenance fields — supplied by the JSON-file source itself)* | `SourceKind = SourceKind.Json`; `SourceId = <assembly name from `typeInfo.assemblyName`>`; `ProvisionedAt = DateTimeOffset.UtcNow`; `ProvisionedBy = Environment.MachineName` |

The seed JSON file (`elsa-core-activities.json` at the repo root) doubles as the bootstrap dataset for the integration test in `Elsa.Activities.Design.Tests`.

## R9 — Workflow-side `TenantEntity` switch — impact

**Decision.** Workflow-side entities (`WorkflowDefinition`, `WorkflowDefinitionVersion`, `WorkflowDefinitionDraft`) switch from `: Entity` to `: TenantEntity`. The `WorkflowsDesignDbContext`'s initial migration must be regenerated.

**Scope of touch.** ~3 entity files + 3 EF Core configuration files + 1 migration regeneration. No entity field shape changes (those belong to Units C/D/E). The switch is mechanical: replace `Entity` with `TenantEntity` in the inheritance clause; everything else continues to compile because `TenantEntity` re-exposes `TenantId` with the same `string?` type.

**Risk.** Units C/D/E will reshape these entities further. Their migrations will likely re-generate. Unit B regenerates once now for the inheritance switch; Units C/D/E regenerate again for their own reshapes. Net: 2 regenerations per workflow-side migration during the units cycle — acceptable given there's no production data.

## R10 — Existing tests survey (§2.21.1 golden rule scope)

**Approach.** Before any code change, enumerate the test projects that touch:
- `ActivityVersionProvisioner` / `ActivityVersionProvisionerStartupTask`
- `ActivityDefinitionVersionSavingHandler` / `LoadingHandler`
- `IGlobalEntitySavingHandler` / `IEntityModelCreatingHandler`
- `IActivityDefinition` / `IActivityDefinitionVersion` consumers

Existing test project at `Test.Activities.Import` likely targets the Elsa3 import flow (consumes new activity-catalog shape via mapping). Verify its tests stay green after the reshape.

If any test must be retired because the subject is no longer applicable (e.g. tests asserting `IsBrowsable=false` behaviour), record the deletion + architect approval per §2.21.1 in the PR description.

**Output of this survey is a tasks-stage artifact, not plan-stage.** The plan just commits to running the survey.

---

**All NEEDS CLARIFICATION items resolved.** Phase 0 closed; Phase 1 design follows in `data-model.md` + `contracts/` + `quickstart.md`.
