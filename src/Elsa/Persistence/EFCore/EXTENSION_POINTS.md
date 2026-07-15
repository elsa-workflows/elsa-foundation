# Extension points — `Elsa.Persistence.EFCore`

The per-domain catalog (framework §2.22.1) of everything you can implement or override in the EF Core persistence layer, plus the events it publishes. Three sections:

- **Overridable contracts** — interfaces with a default implementation you can *replace* (`services.Replace(...)` / register-your-own). Bring one implementation and the built-in one steps aside. This is the *override* axis: "I want my own data access / my own bulk-upsert SQL."
- **Implementable contributor interfaces** — *add-don't-replace* seams. Register an additional implementation alongside any others; a single aggregating handler runs them all (framework §2.6.1, §2.24.2). This is the *extend* axis.
- **Events** — the EF Core persistence-lifecycle seams this assembly publishes (`OnEntitySaving` / `OnEntityLoading`).

This is the repo-wide [`EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md) index's entry for this domain; the index links here for detail.

---

## Overridable contracts

| Contract | Default impl | Override when |
|---|---|---|
| Named per-aggregate **read ports** (e.g. `IWorkflowDefinitionStore`, `IWorkflowDefinitionVersionStore`, `IWorkflowDefinitionDraftStore`, `IWorkflowDefinitionVersionLayoutStore`, `IActivityDefinitionStore`, `IActivityDefinitionVersionStore`) *(defined per domain in `*.Design.Persistence.Core/Stores`)* | `EFCore<Aggregate>Store` adapters over the generic `EFCoreReadStore<TDbContext, TEntity>` (this assembly) | You want a different read strategy for one aggregate (caching layer, read replica, a non-EF/Groundwork store) while keeping the rest of the EF Core stack. This is the canonical *override* example: swap one read port, keep everything else. |
| `IUpsertCommandGenerator` | `UpsertCommandGenerator` | A provider needs different bulk-upsert SQL (dialect-specific `MERGE` / `ON CONFLICT`). |
| `IElsaDbContextSchema` | *(none — optional)* | A deployment needs the Elsa tables under a custom schema name during migration. |

### Named read ports + `EFCoreReadStore<TDbContext, TEntity>` *(closed-query read surface)*
- **Read contract:** each aggregate exposes a small, intent-revealing read port (in its domain `*.Design.Persistence.Core/Stores`) with closed methods (`GetAsync`, `FindBy…Async`, `ListBy…Async`, `ExistsAsync`, …). There is **no** `IQueryable`/LINQ surface — callers cannot express an arbitrary expression tree, so any provider can satisfy a port.
- **Shared plumbing:** the EF default impls (`EFCore<Aggregate>Store`) derive from `EFCoreReadStore<TDbContext, TEntity>` (this assembly), which translates the closed, provider-neutral `Query<TEntity>` spec (`Elsa.Persistence.Core.Queries`) to LINQ via `EFCoreQueryTranslator`, applies `IgnoreQueryFilters()` for tenant-agnostic reads, and publishes `OnEntityLoading` for every materialised entity. Filters project onto the spec through their `ToQuery()` method.
- **Replace:** register your own implementation of a specific read port (or a decorator) before/after the EF Core feature wires its default; or subclass `EFCoreReadStore` for cross-cutting read behaviour. Mutate-then-save commands that need change tracking do **not** go through the read ports (they use a tracked `DbContextFactory` context directly), so overriding a read port does not affect the write path.

### `IUpsertCommandGenerator` *(Feature contract — `Elsa.Persistence.EFCore`)*
- **Signature:** `GeneratedCommand Generate<TDbContext, TEntity>(TDbContext dbContext, IList<TEntity> entities, Expression<Func<TEntity, string>> keySelector) where TDbContext : DbContext where TEntity : Entity;`
- **Default impl:** `UpsertCommandGenerator`. Consumed by `EFCoreBulkUpsert` to build the raw upsert SQL.

### `IElsaDbContextSchema` *(Feature contract — `Elsa.Persistence.EFCore`)*
- **Signature:** `string Schema { get; }` — the schema name applied during migration.

---

## Implementable contributor interfaces

These are registered alongside any others and dispatched by a single aggregating handler — you never register your own `IEventHandler`.

### `IEntitySavingHandler<TDbContext, TEntity>` *(Feature contract — `Elsa.Persistence.EFCore`)*
- **Kind:** entity Handler (action-named contributor). **Lives in:** `Elsa.Persistence.EFCore` (`Contracts/`).
- **Signature:** `ValueTask Handle(TDbContext dbContext, TEntity entity, CancellationToken cancellationToken);`
- **Receives** the context + typed entity and **acts** — serialises `[NotMapped]` projections into backing `*Source` / payload columns before the row is flushed. The typed generics do the entity-type filtering (no inline `is` check).
- **Register:** `services.AddEntitySavingHandler<TDbContext, TEntity, THandler>()` or scan with `services.AddEntitySavingHandlersFrom(assembly)`.
- **Consumed by:** the single `ApplyEntitySavingHandlers : IEventHandler<OnEntitySaving>` (this assembly), registered once by `EFCorePersistenceShellFeatureBase` via `TryAddEnumerable`.

**Known implementations (shipped):**
- `Elsa.Activities.Design.Persistence.EFCore` — `ActivityDefinitionVersionSavingHandler` *(cross-domain)*
- `Elsa.Workflows.Design.Persistence.EFCore` — `WorkflowDefinitionVersionSavingHandler`, `WorkflowDefinitionDraftSavingHandler` *(cross-domain)*

### `IEntityLoadingHandler<TDbContext, TEntity>` *(Feature contract — `Elsa.Persistence.EFCore`)*
- **Kind:** entity Handler (action-named contributor). **Lives in:** `Elsa.Persistence.EFCore` (`Contracts/`).
- **Signature:** `ValueTask Handle(TDbContext dbContext, TEntity entity, CancellationToken cancellationToken);`
- **Receives** the context + just-materialised entity and **acts** — hydrates `[NotMapped]` projections from the backing columns.
- **Register:** `services.AddEntityLoadingHandler<TDbContext, TEntity, THandler>()` or scan with `services.AddEntityLoadingHandlersFrom(assembly)`.
- **Consumed by:** the single `ApplyEntityLoadingHandlers : IEventHandler<OnEntityLoading>` (this assembly), registered once by `EFCorePersistenceShellFeatureBase` via `TryAddEnumerable`.

**Known implementations (shipped):**
- `Elsa.Activities.Design.Persistence.EFCore` — `ActivityDefinitionVersionLoadingHandler` *(cross-domain)*
- `Elsa.Workflows.Design.Persistence.EFCore` — `WorkflowDefinitionVersionLoadingHandler`, `WorkflowDefinitionDraftLoadingHandler` *(cross-domain)*

### Out-of-band hooks (NOT event-dispatched)

These contributor interfaces run through their own dispatch mechanism, not through `OnEntitySaving` / `OnEntityLoading` — listed here for completeness:

- **`IGlobalEntitySavingHandler`** *(Feature contract — `Elsa.Persistence.EFCore`)* — `ValueTask Handle(DbContext dbContext, EntityEntry entity, CancellationToken cancellationToken);`. Runs for **every** modified entity (no per-type fan-in) directly from `ElsaDbContextBase.ApplyGlobalSavingHandlers`.
- **`IEntityModelCreatingHandler`** *(Feature contract — `Elsa.Persistence.EFCore`)* — `void Handle(ElsaDbContextBase dbContext, ModelBuilder modelBuilder, IMutableEntityType entityType);`. Runs during `OnModelCreating` (before any request scope exists), dispatched by `ElsaDbContextBase.ApplyEntityModelCreatingHandlers`.

---

## Writing a persistence feature (`EFCorePersistenceShellFeatureBase<TDbContext>`)

A domain's persistence feature derives from `EFCorePersistenceShellFeatureBase<TDbContext>`. The base
registers the two aggregators (`ApplyEntitySavingHandlers` / `ApplyEntityLoadingHandlers`) once, then
registers your typed `IEntitySavingHandler<,>` / `IEntityLoadingHandler<,>` from a single list — **both
directions together**, so you can never wire one and forget the other.

- **`protected virtual IEnumerable<Assembly> EntityHandlerAssemblies`** — override this to return the
  assembly/assemblies that hold your handlers. It defaults to `[GetType().Assembly]` (the concrete
  feature's own assembly), but handlers usually live in the intermediate `*.EFCore` domain assembly,
  so a domain base typically returns `[GetType().Assembly, typeof(<ThisDomainBase>).Assembly]`. The
  base scans this list for **both** saving and loading handlers (saving gated by `UseCommands`, loading
  by `UseQueries`). You do **not** call `AddEntitySavingHandlersFrom` / `AddEntityLoadingHandlersFrom`
  yourself.
- **Entity construction** — domain entities are built through their `I<Entity>Factory` (in the domain
  `.Design.Core`, returning the read interface) + the entity's static `From(IInterface)` at the persist
  boundary, not via object-mappers. Register the factory implementations (which live in the domain
  `.Design.Persistence.Core`) in the feature's `OnBeforeConfiguring` / `OnAfterConfigured`.

See `EFCoreActivitiesPersistenceFeatureBase` / `EFCoreWorkflowsPersistenceFeatureBase` for worked
examples (both override `EntityHandlerAssemblies` and register their factories).

---

## Events

Both events are `IEvent` (framework §2.6.1). They are the EF Core persistence-lifecycle seams: a row is about to be flushed (`OnEntitySaving`) or has just been materialised (`OnEntityLoading`). Both are **Sequential / contribution** events — the publisher needs the contributors to have run (columns serialised / projections hydrated) before it proceeds.

Each event has **exactly one** subscriber: a single aggregating `IEventHandler` that closes the typed contributor interface over the runtime DbContext + entity types, resolves every registered implementation, and invokes it. This is the same contributor-interface + single-aggregating-handler shape as `IDraftValidator` + `ExecuteValidations` (framework §2.24.2). Features never subscribe their own `IEventHandler<OnEntitySaving>` / `IEventHandler<OnEntityLoading>`; they register a typed `IEntitySavingHandler<,>` / `IEntityLoadingHandler<,>` (see Implementable contributor interfaces above) and let the aggregator dispatch it.

Heading convention per research item R4: `### <EventClassName>`.

### OnEntitySaving

**Semantic.** A modified `Entity` (Added or Modified) is about to be flushed. Contributors serialise rich, `[NotMapped]` projections into their backing `*Source` / payload columns and derive any computed columns BEFORE the row is written. The publisher awaits the dispatch so the columns are populated by the time the underlying write runs.

**Payload.**
- `DbContext : DbContext` — the context performing the save (its runtime type selects the closed contributor interface).
- `Entry : EntityEntry` — the change-tracker entry for the entity being saved; `Entry.Entity` is the row (its runtime type selects the closed contributor interface).

**Contributor interface.** `IEntitySavingHandler<TDbContext, TEntity>` — see the Implementable contributor interfaces section above for signature + registration.

**Delivery strategy.** Sequential (the default) — the save must not proceed until the source columns are written.

**Publication sites.**
- `ElsaDbContextBase.DispatchEntitySavingEvents` — published for every modified `Entity` inside `BeforeSavingChanges`, i.e. on every `SaveChangesAsync`.
- `EFCoreBulkUpsert.PublishEntitySavingEvents` — the bulk-upsert path bypasses `SaveChanges` (it executes raw upsert SQL), so it publishes `OnEntitySaving` itself before generating the SQL so source columns are populated.

**Expected handler.**
- Exactly one `IEventHandler<OnEntitySaving>`: `ApplyEntitySavingHandlers` (this assembly). Registered once per process by `EFCorePersistenceShellFeatureBase.ConfigureServices` via `TryAddEnumerable` (dedupes by implementation type even with several EF Core persistence features enabled).

**Contributing handlers (`IEntitySavingHandler<,>` impls).**
- `ActivityDefinitionVersionSavingHandler` (`Elsa.Activities.Design.Persistence.EFCore`) — serialises `Inputs`/`Outputs`/`DesignFacets` and the opaque descriptor payload into `DescriptorPayloadSource`; stable provider/consumer identity is producer-owned. The legacy `DescriptorType` column is an obsolete EF-only compatibility mapping, not runtime dispatch identity.
- `WorkflowDefinitionVersionSavingHandler` / `WorkflowDefinitionDraftSavingHandler` (`Elsa.Workflows.Design.Persistence.EFCore`) — serialise the workflow `State` / version payloads into their `*Source` columns.

**Ordering guarantees.**
- Fires for each modified entity BEFORE the underlying write (`SaveChangesAsync` / raw upsert SQL).
- Contributors for a given (DbContext, entity) run in DI-resolution order (no guaranteed inter-handler ordering — independent per framework §2.6.1).
- The unrelated `IGlobalEntitySavingHandler` (runs for *every* entity, no per-type fan-in) and `IEntityModelCreatingHandler` (runs during `OnModelCreating`) are separate mechanisms — not dispatched through this event.
- The Sequential path ships **no exception-shielding** (framework §2.6.6): a contributor that throws fails the save.

### OnEntityLoading

**Semantic.** An `Entity` has just been materialised from the store and needs hydrating: contributors deserialise the `*Source` / payload columns back into the rich, `[NotMapped]` projections. The publisher awaits the dispatch so the entity is fully hydrated before it is read or returned.

**Payload.**
- `DbContext : DbContext` — the context that loaded the entity (its runtime type selects the closed contributor interface).
- `Entity : Entity` — the materialised entity to hydrate (its runtime type selects the closed contributor interface).

**Contributor interface.** `IEntityLoadingHandler<TDbContext, TEntity>` — see the Implementable contributor interfaces section above for signature + registration.

**Delivery strategy.** Sequential (the default) — the caller must see a hydrated entity.

**Publication sites.**
- `EFCoreReadStore.QueryAsync` / `FirstOrDefaultAsync` (this assembly) — the **read path** behind every named read port. Published for every entity returned by a port read (per-item and per-list fan-out). These results are `AsNoTracking`; hydration is in-memory.
- `UpdateDraft.LoadAndHydrate` (`Elsa.Workflows.Design.Persistence.EFCore`) — the **mutate-then-save path**. The command loads the Draft through its own **tracked** `DbContextFactory` context (NOT a named read store, which returns a detached `AsNoTracking` entity it could not save), then publishes `OnEntityLoading` Sequential so the aggregator hydrates the already-tracked instance via the same context that will `SaveChangesAsync`.

**Expected handler.**
- Exactly one `IEventHandler<OnEntityLoading>`: `ApplyEntityLoadingHandlers` (this assembly). Registered once per process by `EFCorePersistenceShellFeatureBase.ConfigureServices` via `TryAddEnumerable`.

**Contributing handlers (`IEntityLoadingHandler<,>` impls).**
- `ActivityDefinitionVersionLoadingHandler` (`Elsa.Activities.Design.Persistence.EFCore`) — deserialises `*Source` + the implementation-descriptor payload (via the descriptor-kind registry) back into rich projections.
- `WorkflowDefinitionVersionLoadingHandler` / `WorkflowDefinitionDraftLoadingHandler` (`Elsa.Workflows.Design.Persistence.EFCore`) — hydrate the workflow `State` from its `StateSource` column.

**Ordering guarantees.**
- Fires AFTER materialisation, BEFORE the entity is read/returned by the caller.
- Contributors for a given (DbContext, entity) run in DI-resolution order.
- The Sequential path ships no exception-shielding — a contributor that throws fails the load.

---

## Cross-references

- The contributor interfaces, both aggregators, and the out-of-band hooks are also catalogued in the repo-root [`EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md) index.
- Constitutional basis: §2.6.1 (the single `IEvent` concept + contribution sub-pattern; action-named contributor suffixes) + §2.6.6 (delivery strategies) + §2.22.1 (per-domain extension-points catalog) + §2.24.2 (contributor interface + single aggregating handler).
