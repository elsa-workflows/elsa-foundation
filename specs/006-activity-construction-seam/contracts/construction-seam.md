# Contract — Runtime Construction Seam (`Elsa.Activities.Runtime.Core`)

All types here live in `Elsa.Activities.Runtime.Core` and reference **no** `Elsa.*.Design.*` type (§E2.2 / G15). Signatures are illustrative identity anchors; exact shapes finalize in code.

## Contract kinds (G5)

| Type | Kind | Home | Why |
|---|---|---|---|
| `IActivityConstructor` / `IActivityConstructor<TDescriptor>` | **contribution** | `Runtime.Core` | features contribute per-descriptor-type constructors |
| `IActivityFactory` | **replacement** | `Runtime.Core` | single swappable construction entry point |
| `IActivityConstructorRegistry` | **replacement** | `Runtime.Core` | single swappable sync-access registry |
| `ActivityConstructorsInitializing` | domain event | `Runtime.Core` | Registry + StartUp Task population (G21) |
| `IActivityArgumentBinder` | **neither** → NOT in Core | `Elsa.Activities.Primitives` | feature-internal helper (core-not-a-bucket rule) |

## `IActivityFactory` (replacement)

```csharp
public interface IActivityFactory
{
    // Pure dispatch: resolve the constructor for descriptorType, delegate.
    // Throws UnknownDescriptorTypeException (domain failure) if none registered.
    ValueTask<IActivity> Create(
        string descriptorType,
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken = default);
}
```

## `IActivityConstructor` + `IActivityConstructor<TDescriptor>` (contribution)

```csharp
public interface IActivityConstructor
{
    string DescriptorType { get; }                      // = typeof(TDescriptor).FullName!
    ValueTask<IActivity> Construct(JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken);
}

public interface IActivityConstructor<TDescriptor> : IActivityConstructor where TDescriptor : class
{
    ValueTask<IActivity> Construct(TDescriptor descriptor,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken);
}
```

**No base class.** Each impl provides the one-line bridge:
```csharp
string IActivityConstructor.DescriptorType => typeof(TDescriptor).FullName!;
ValueTask<IActivity> IActivityConstructor.Construct(JsonElement p, var i, var o, var ct)
    => Construct(p.Deserialize<TDescriptor>()!, i, o, ct);
```
Deserialization failure surfaces here, runtime-side (not in design).

## `IActivityConstructorRegistry` (replacement)

```csharp
public interface IActivityConstructorRegistry
{
    void Add(IActivityConstructor constructor);   // throws DuplicateActivityConstructorException on a 2nd same DescriptorType
    IActivityConstructor Resolve(string descriptorType); // throws UnknownDescriptorTypeException (domain failure) if absent
}
```
- Invariant: **one constructor per `DescriptorType`** (FR-006), enforced at `Add` time (startup), loud not last-wins.

## `ActivityConstructorsInitializing` (domain event — Registry + StartUp Task, G21)

- `ActivityConstructorsInitializing : IEvent` exposes the registry (or its mutable collection).
- Published **Sequential** once by `ActivityConstructorsStartupTask`.
- Single aggregating handler `RegisterActivityConstructors : IEventHandler<ActivityConstructorsInitializing>` (in `Elsa.Activities.Runtime`) adds every registered `IActivityConstructor` to the registry.
- Consumers sync-read via `IActivityConstructorRegistry` after startup.

## Domain failures (not system faults — G29)

- `UnknownDescriptorTypeException(descriptorType)` — no owning feature installed; thrown at construction. Cataloguing/reading the row is unaffected.
- `DuplicateActivityConstructorException(descriptorType)` — two contributors claim one type; thrown at startup.
- Payload-deserialization failure — thrown by the owning constructor's bridge, attributable to its `DescriptorType`.
