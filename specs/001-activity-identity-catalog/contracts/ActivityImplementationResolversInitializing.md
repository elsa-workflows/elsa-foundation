# Contract: `ActivityImplementationResolversInitializing`

**Location.** `Elsa.Activities.Runtime.Core.Events.ActivityImplementationResolversInitializing`

**Kind.** Contribution event (framework §2.6.1). Drives Registry + StartUp Task sub-pattern population.

**Constitutional citation.** Framework §2.6.1; Elsa §E3.3 (canonical worked example).

## Surface

```csharp
namespace Elsa.Activities.Runtime.Core.Events;

public sealed record ActivityImplementationResolversInitializing(
    ICollection<IActivityImplementationResolver> Resolvers
) : IDomainEvent;
```

## Dispatch flow

Published once at startup by `ActivityImplementationResolverRegistryStartupTask`:

```csharp
public async Task Execute(CancellationToken ct)
{
    var resolvers = new List<IActivityImplementationResolver>();
    await sender.Send(new ActivityImplementationResolversInitializing(resolvers), ct);
    registry.RegisterAll(resolvers);
}
```

After this task completes, `IActivityFactory.Create` reads the populated registry sync.

## Source contract

Each handler adds zero or more `IActivityImplementationResolver` instances to the carried list. One resolver per `ImplementationKind` per host. Duplicate-kind registration is detected by the registry on `RegisterAll` and throws.

## Unit B seed

The activities runtime feature handles the event itself to add `ClrActivityImplementationResolver`. Unit G's bridge module will add `WorkflowActivityImplementationResolver` when it ships.

## Test surface

- Branch test: no handlers → registry is empty after startup; any subsequent factory call throws.
- Branch test: CLR resolver registered → registry resolves `ImplementationKind.Clr` to it.
- Branch test: two handlers register resolvers for the same kind → `RegisterAll` throws.
