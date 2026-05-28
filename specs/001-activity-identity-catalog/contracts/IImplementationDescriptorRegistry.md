# Contract: `IImplementationDescriptorRegistry` + `OnImplementationDescriptorsInitializing`

**Location.** `Elsa.Activities.Design.Core`

**Kind.** Registry contract following the canonical §2.6.1 Registry + StartUp Task sub-pattern (worked example: Elsa §E3.3). Symmetric with `IActivityImplementationResolverRegistry`.

**Constitutional citation.** Framework §2.6.1; Elsa §E3.3 (worked example of Registry + StartUp Task).

**Why explicit (vs. deriving from the resolver registry):** the registry concern (kind → CLR descriptor type) is semantically distinct from the resolver concern (descriptor → IActivity). Deriving one from the other via reflection muddles two registries' lifecycles and surfaces. The §2.6.1 pattern applied twice keeps each registry's contract clear — own contract, own event, own startup task.

## Surface

```csharp
namespace Elsa.Activities.Design.Core.Models;

public sealed record ImplementationDescriptorRegistration(
    ImplementationKind Kind,
    Type DescriptorType);
```

```csharp
namespace Elsa.Activities.Design.Core.Contracts;

public interface IImplementationDescriptorRegistry
{
    void Register(ImplementationDescriptorRegistration registration);
    void RegisterAll(IEnumerable<ImplementationDescriptorRegistration> registrations);
    Type? Resolve(ImplementationKind kind);
}
```

```csharp
namespace Elsa.Activities.Design.Core.Events;

public sealed record OnImplementationDescriptorsInitializing(
    ICollection<ImplementationDescriptorRegistration> Registrations
) : IDomainEvent;
```

## Default implementation

Thin dictionary-backed implementation in the same `.Core`:

```csharp
namespace Elsa.Activities.Design.Core.Models;

public sealed class ImplementationDescriptorRegistry : IImplementationDescriptorRegistry
{
    private readonly Dictionary<string, Type> _byKindValue = new();

    public void Register(ImplementationDescriptorRegistration r) => _byKindValue[r.Kind.Value] = r.DescriptorType;
    public void RegisterAll(IEnumerable<ImplementationDescriptorRegistration> rs)
    {
        foreach (var r in rs) Register(r);
    }
    public Type? Resolve(ImplementationKind kind) => _byKindValue.GetValueOrDefault(kind.Value);
}
```

Duplicate-kind behaviour: **last write wins** at the registry level. Conflict diagnostics (the §2.6.2 conflict-detection rule for replacement contracts) is a startup-validation concern; if a use case demands explicit conflict surfacing here, add it as a separate diagnostic pass — not a registry primitive.

## Dispatch flow

`ImplementationDescriptorRegistryStartupTask` (in the activities runtime feature) is invoked at startup:

```csharp
public async Task Execute(CancellationToken ct)
{
    var registrations = new List<ImplementationDescriptorRegistration>();
    await sender.Send(new OnImplementationDescriptorsInitializing(registrations), ct);
    registry.RegisterAll(registrations);
}
```

After this task completes, `IImplementationDescriptorRegistry.Resolve(...)` is sync-readable by both the EF loading handler and any other consumer.

## Contribution flow

Each feature that introduces a new descriptor type handles `OnImplementationDescriptorsInitializing` and adds its registrations to the carried collection. Unit B's contributing feature is the activities runtime feature itself:

```csharp
public class ActivitiesRuntimeFeature : ...,
    IDomainEventHandler<OnImplementationDescriptorsInitializing>
{
    public ValueTask Handle(OnImplementationDescriptorsInitializing e, CancellationToken ct)
    {
        e.Registrations.Add(new ImplementationDescriptorRegistration(
            ImplementationKind.Clr,
            typeof(ClrImplementationDescriptor)));
        return ValueTask.CompletedTask;
    }
}
```

Unit G's bridge module will handle the same event to contribute `(ImplementationKind.Workflow, typeof(WorkflowImplementationDescriptor))`. Future features (Remote, Script, …) contribute analogously.

## Consumers

- **`ActivityDefinitionVersionLoadingHandler`** (`Elsa.Activities.Design.Persistence.EFCore`) — calls `registry.Resolve(entity.ImplementationKind)` to determine the deserialisation target for the shadow column `ImplementationDescriptor` (string JSON). Combined with `IPayloadSerializer.Deserialize(json, type)` via reflection-constructed generic method invocation.
- Any future consumer needing kind → descriptor-type resolution.

## Failure modes

| Cause | Path |
|---|---|
| Unknown kind looked up at runtime (registry returns null) | Loading handler throws with a clear diagnostic ("no descriptor type registered for kind X — the contributing feature is not installed"). Per Elsa §E2.6.1, this is a domain/runtime failure, not a system failure. |
| Two features contribute conflicting types for the same kind | Last-write-wins at registry level. Operator detection via §2.23.1 registration test that asserts known kinds resolve to expected types. |
| Startup task fails mid-flight | Registry is left empty for un-contributed kinds; subsequent `Resolve` returns null → handler throws on use. Failure surfaces at startup or first-use, not silently. |

## Test surface

- Branch test: empty registrations → registry resolves unknown kind to `null`.
- Branch test: register `(Clr, typeof(ClrImplementationDescriptor))` via the event flow → `registry.Resolve(ImplementationKind.Clr)` returns `typeof(ClrImplementationDescriptor)`.
- Branch test: register two handlers for the event, each contributing a distinct kind → both kinds resolve correctly after startup.
- §2.23.1 registration test: activities runtime feature wires the registry + startup task; the feature's own handler is registered; after running, the registry contains the CLR mapping.
