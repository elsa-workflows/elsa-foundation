# Elsa.Activities.Runtime

Runtime-side composition for activity construction. Hosts the activity factory, the resolver registry, the descriptor-type registry, the CLR resolver, and the canonical §2.6.1 Registry + StartUp Task population pattern for both registries.

## What this feature provides

- **`IActivityFactory`** → `ActivityFactory` — kind-agnostic construction dispatcher. Resolves the descriptor's kind via the resolver registry, asks the resolver for the CLR type, activates via `ActivatorUtilities`. Throws `ActivityResolutionException` for unknown kinds (Elsa §E2.6.1 domain-failure path).
- **`IActivityImplementationResolverRegistry`** → `ActivityImplementationResolverRegistry` — kind→resolver dispatch table for runtime. Singleton; populated at startup.
- **`IImplementationDescriptorRegistry`** → `Elsa.Activities.Design.Core.Models.ImplementationDescriptorRegistry` — kind→CLR descriptor type for persistence-side deserialisation. Singleton; populated at startup.
- **`ClrActivityImplementationResolver`** — owns the `"Clr"` kind. Resolves `ClrImplementationDescriptor` to a live `Type` via `TypeInformation.LoadType()`.

## Cross-feature contributions (contributor interfaces this feature implements + registers)

Both clusters use the **return-style contributor (`I…Source`) + single aggregating handler** pattern:
a source returns its items; one action-named `IEventHandler<T>` resolves `IEnumerable<TSource>`,
iterates, and writes the aggregate into the event's `ICollection` once.

- **`IActivityImplementationResolverSource`** → `ClrActivityImplementationResolverSource` (returns `ClrActivityImplementationResolver`). The single handler `RegisterActivityImplementationResolvers : IEventHandler<OnActivityImplementationResolversInitializing>` aggregates all sources into the runtime registry event.
- **`IImplementationDescriptorSource`** → `ClrImplementationDescriptorSource` (returns the `("Clr", typeof(ClrImplementationDescriptor))` registration). The single handler `RegisterImplementationDescriptors : IEventHandler<OnImplementationDescriptorsInitializing>` aggregates all sources.

Other kind-owning modules contribute by implementing `IActivityImplementationResolverSource` /
`IImplementationDescriptorSource` and registering via DI — **not** by adding their own event handler.

## Cross-feature contributions (events this feature publishes)

- **`OnActivityImplementationResolversInitializing`** — carried by `ActivityImplementationResolverRegistryStartupTask`. Other kind-owning modules (Unit G workflow bridge, future remote bridge) contribute resolvers by implementing `IActivityImplementationResolverSource`.
- **`OnImplementationDescriptorsInitializing`** — carried by `ImplementationDescriptorRegistryStartupTask`. Other kind-owning modules register their descriptor types by implementing `IImplementationDescriptorSource` so the persistence-side loader can deserialise.

## Startup tasks

- **`ActivityImplementationResolverRegistryStartupTask`** — publishes the resolver-initialisation event; flushes contributions into the runtime registry.
- **`ImplementationDescriptorRegistryStartupTask`** — publishes the descriptor-initialisation event; flushes contributions into the persistence-side registry.

## Owned well-known values

- `ClrActivityImplementationResolver.KindValue = "Clr"` — the kind string this module owns. Three places agree on this value: the resolver, the descriptor (`ClrImplementationDescriptor.Kind => "Clr"`), and the contributing source (`ClrImplementationDescriptorSource`). The framework constitution does not enumerate the legal set.

## Failure modes

- **`ActivityResolutionException`** — thrown by the factory or registry when no resolver is registered for a descriptor's kind, or when the resolver throws while resolving. Per Elsa §E2.6.1 this is a *domain* failure, not a system failure — callers may catch and translate to a graceful response.
