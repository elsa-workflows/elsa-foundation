# Extension points — Activities.Runtime domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Activities.Runtime` — the composition root where `ActivitiesRuntimeFeature` registers `RegisterActivityImplementationResolvers`, `RegisterImplementationDescriptors`, and the built-in CLR sources.

> **Two Core domains' contributor interfaces are aggregated here.** `IActivityImplementationResolverSource` (from `Elsa.Activities.Runtime.Core`) and `IImplementationDescriptorSource` (from `Elsa.Activities.Design.Core`) both feed into this feature's aggregating handlers. The contract origins are noted per entry below.

---

## Implementable contributor interfaces

### `IActivityImplementationResolverSource` *(Core — `Elsa.Activities.Runtime.Core`)*
- **Kind:** Source (returns a set of resolvers — pull pattern).
- **Signature:** `IEnumerable<IActivityImplementationResolver> GetResolvers();`
- **Register:** `services.AddScoped<IActivityImplementationResolverSource, MySource>()`.
- **Aggregated by:** the single `RegisterActivityImplementationResolvers : IEventHandler<OnActivityImplementationResolversInitializing>` (this feature), which injects all sources, collects their resolvers, and registers each into the resolver registry.

**Known implementations (shipped):**
- `Elsa.Activities.Runtime` — `ClrActivityImplementationResolverSource` *(intra-domain — default; provides CLR-based resolvers)*

### `IActivityImplementationResolver<TDescriptor>` *(Core — `Elsa.Activities.Runtime.Core`)*
The resolved interface returned by sources. Not registered directly via DI; returned by `IActivityImplementationResolverSource.GetResolvers()`.
- **Signature:** `string Kind { get; }`, `Type Resolve(TDescriptor descriptor);`
- **Purpose:** given a descriptor of a specific kind, return the CLR `Type` that implements the activity.

### `IImplementationDescriptorSource` *(Core — `Elsa.Activities.Design.Core`)* — cross-domain contract aggregated here
- **Kind:** Source (returns a set of descriptor registrations — pull pattern).
- **Contract defined in:** `Elsa.Activities.Design.Core` (a different domain); the aggregating handler lives in this feature (`Elsa.Activities.Runtime`).
- **Signature:** `IEnumerable<ImplementationDescriptorRegistration> GetRegistrations();`
- **Register:** `services.AddScoped<IImplementationDescriptorSource, MySource>()`.
- **Aggregated by:** the single `RegisterImplementationDescriptors : IEventHandler<OnImplementationDescriptorsInitializing>` (this feature), which injects all sources, collects their registrations, and registers each into the descriptor registry.

**Known implementations (shipped):**
- `Elsa.Activities.Runtime` — `ClrImplementationDescriptorSource` *(intra to Runtime — provides CLR-based descriptor registrations)*
- Activity provider features *(cross-domain — each activity feature ships its own `IImplementationDescriptorSource`)*

---

## Events

`CatalogParityTests` scans both `Elsa.Activities.Runtime.Core` and `Elsa.Activities.Design.Core` assemblies, each paired with this catalog file, for `IEvent` types.

### OnActivityImplementationResolversInitializing
`(ICollection<IActivityImplementationResolver> Resolvers)`

**Semantic.** The activity implementation resolver registry is initialising. `IActivityImplementationResolverSource` implementations contribute their resolvers to the `Resolvers` collection.

**Delivery strategy.** Sequential — all resolvers must be registered before the first activity execution.

**Publication site.** `ActivityImplementationResolverRegistryStartupTask` (`Elsa.Activities.Runtime`) — fired at startup.

**Expected handler.** Exactly one: `RegisterActivityImplementationResolvers` (this feature).

### OnImplementationDescriptorsInitializing
`(ICollection<ImplementationDescriptorRegistration> Registrations)`

**Semantic.** The implementation descriptor registry is initialising. `IImplementationDescriptorSource` implementations contribute their descriptor registrations.

**Delivery strategy.** Sequential.

**Publication site.** `ImplementationDescriptorRegistryStartupTask` (`Elsa.Activities.Runtime`) — fired at startup.

**Expected handler.** Exactly one: `RegisterImplementationDescriptors` (this feature).

---

## Cross-references

- `IImplementationDescriptorSource` contract: `Elsa.Activities.Design.Core` (cross-domain origin).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
