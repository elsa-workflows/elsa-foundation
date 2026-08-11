# Contract: `IActivityImplementationResolver<TDescriptor>` + Registry

**Location.** `Elsa.Activities.Runtime.Core.Contracts.IActivityImplementationResolver`

**Kind.** Contribution contract (per `ImplementationKind`). Registered via the `ActivityImplementationResolversInitializing` domain event (Registry + StartUp Task sub-pattern per framework §2.6.1).

**Constitutional citation.** Framework §2.6.1 (Domain events — the contribution mechanism); Elsa §E3.3 (canonical worked example of Registry + StartUp Task).

## Resolver surface

```csharp
namespace Elsa.Activities.Runtime.Core.Contracts;

public interface IActivityImplementationResolver<in TDescriptor>
    where TDescriptor : class, IImplementationDescriptor
{
    string Kind { get; }
    Type Resolve(TDescriptor descriptor);
}
```

A non-generic marker interface (`IActivityImplementationResolver`) MAY be introduced for the registry's storage type if generic dispatch via reflection at runtime is awkward — plan-stage decision.

## Registry surface

```csharp
public interface IActivityImplementationResolverRegistry
{
    void RegisterAll(IEnumerable<IActivityImplementationResolver> resolvers);
    IActivityImplementationResolver Resolve(IImplementationDescriptor descriptor);
}
```

`Resolve(...)` dispatches by descriptor kind (matching the resolver's `Kind` property). Unknown-kind lookup throws.

## Behaviour

- One implementation per `ImplementationKind`. Duplicate registration is a startup error.
- The resolver's only responsibility is `descriptor → CLR Type`. Instantiation + argument-state population is `IActivityFactory`'s job.
- The CLR resolver (shipped in Unit B):

   ```csharp
   public sealed class ClrActivityImplementationResolver
       : IActivityImplementationResolver<ClrImplementationDescriptor>
   {
       public string Kind => ImplementationKind.Clr.Value;
       public Type Resolve(ClrImplementationDescriptor descriptor) => descriptor.TypeInfo.LoadType();
   }
   ```

  Uses the existing `TypeInformation.LoadType()` helper from `Elsa.Primitives`.

## Contribution flow

1. `Elsa.Activities.Runtime.Core` declares the registry interface + the `ActivityImplementationResolversInitializing` event.
2. The activities runtime feature registers an `ActivityImplementationResolverRegistryStartupTask`:

   ```csharp
   var resolvers = new List<IActivityImplementationResolver>();
   await sender.Send(new ActivityImplementationResolversInitializing(resolvers), ct);
   registry.RegisterAll(resolvers);
   ```

3. The activities runtime feature itself handles the event to add the `ClrActivityImplementationResolver`.
4. Future features (Workflow bridge in Unit G, Remote in some future unit) handle the same event to add their resolvers.
5. After startup, `IActivityFactory.Create` reads the registry sync.

This is the canonical §2.6.1 Registry + StartUp Task sub-pattern.

## Test surface

- Registration test: the activities runtime feature registers the CLR resolver; the registry resolves `ImplementationKind.Clr` to it.
- Branch test: duplicate registration throws.
- Branch test: lookup for unregistered kind throws.
- Branch test: the CLR resolver returns the expected `Type` from a known `ClrImplementationDescriptor`.
