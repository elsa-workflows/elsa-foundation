# Contract: `IImplementationDescriptor`

**Location.** `Elsa.Activities.Design.Core.Contracts.IImplementationDescriptor`

**Kind.** Marker interface — polymorphic input contract for `IActivityImplementationResolver`. No methods; concrete implementations carry the binding payload.

**Constitutional citation.** Framework §2.6.4 (design-time vs runtime contract split is informed by this); Elsa §E2.6 (runtime contract — executable-always-runs).

## Surface

```csharp
namespace Elsa.Activities.Design.Core.Contracts;

public interface IImplementationDescriptor { }
```

## Concrete implementations shipped in Unit B

```csharp
namespace Elsa.Activities.Design.Core.Models;

public sealed record ClrImplementationDescriptor(TypeInformation TypeInfo)
    : IImplementationDescriptor;

public sealed record WorkflowImplementationDescriptor(
    string WorkflowDefinitionId,
    int WorkflowVersionId
) : IImplementationDescriptor;
```

`ClrImplementationDescriptor` is the seed CLR descriptor — wraps the existing `Elsa.Primitives.Models.TypeInformation`. The `WorkflowImplementationDescriptor` ships as the non-CLR round-trip-test proof per spec FR-007 / FR-021; **Unit B does NOT ship the matching resolver** — the Workflow resolver lives in Unit G's bridge module.

## Storage

`IImplementationDescriptor` instances are persisted as an EF Core **shadow column** named `ImplementationDescriptor` on `ActivityDefinitionVersion` (string, JSON payload). The CLR entity exposes one combined `[NotMapped] IImplementationDescriptor ImplementationDescriptor` property — the rich projection — hydrated by the loading handler. The descriptor's runtime CLR type IS the kind binding; no `$type` discriminator inside the JSON.

**Loading path:**
1. Read entity's `ImplementationKind` (column).
2. Resolve kind → concrete CLR descriptor type via `IImplementationDescriptorRegistry.Resolve(kind)` (see `IImplementationDescriptorRegistry.md`).
3. Read the shadow column's string via `entry.Property("ImplementationDescriptor").CurrentValue`.
4. Call `IPayloadSerializer.Deserialize(json, type)` — Elsa's existing payload-serialisation seam. Reflection-driven generic method invocation if the API is generic-only.
5. Assign to the `[NotMapped] ImplementationDescriptor` property.

**Saving path:** inverse — serialise the rich projection via `IPayloadSerializer.Serialize`; write to the shadow column.

No custom `JsonConverter`; no `JsonPolymorphic` attributes; no framework-coupling at the descriptor interface site. See `research.md` §R4 for the rationale.

## Adding a new kind

1. Declare a new concrete record implementing `IImplementationDescriptor`.
2. Declare a corresponding `ImplementationKind` static instance (e.g. `ImplementationKind.Remote = new("Remote")`).
3. Handle `OnImplementationDescriptorsInitializing` in the contributing feature and add `new ImplementationDescriptorRegistration(kind, typeof(TheNewDescriptor))` to the carried collection.
4. (Optional, separate concern) Ship the matching `IActivityImplementationResolver<TDescriptor>` for the new kind by handling `ActivityImplementationResolversInitializing` — see `IActivityImplementationResolver.md`. The two registrations are independent: a descriptor type can be registered (for storage round-trip) without a matching resolver, in which case the catalog row persists but cannot be constructed into an `IActivity` until the resolver feature ships. This is by design — `IActivityFactory` fails through a runtime/domain path on unknown-kind lookup per Elsa §E2.6.1.

No changes to `Activities.Design.Core`'s descriptor interface declaration are required.
