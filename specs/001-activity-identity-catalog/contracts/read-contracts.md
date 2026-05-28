# Read Contracts — `Elsa.Activities.Design.Core`

Stable read-only interfaces exposed by `Activities.Design.Core` per spec FR-008. Consumers (picker, designer, runtime resolver registry, audit endpoints, Elsa3 importer) depend on these — not on the persistence entity classes.

## IActivityDefinition

```csharp
namespace Elsa.Activities.Design.Core.Contracts;

public interface IActivityDefinition
{
    string Id { get; }
    string? TenantId { get; }

    string ActivityTypeKey { get; }

    SourceKind SourceKind { get; }
    string SourceId { get; }
    DateTimeOffset ProvisionedAt { get; }
    string? ProvisionedBy { get; }

    string Category { get; }
    string? DisplayName { get; }
    string? Description { get; }
}
```

**Removed surface:**
- `UniqueName` (renamed to `ActivityTypeKey`).
- `IsBrowsable` (field removed entirely).

**No reconciliation-state fields.** Per spec FR-008 and SC-011: reconciliation state lives on the separate `IActivityDefinitionReconciliationState` contract; `IActivityDefinition` does NOT expose it.

## IActivityDefinitionVersion

```csharp
namespace Elsa.Activities.Design.Core.Contracts;

public interface IActivityDefinitionVersion
{
    string Id { get; }
    string? TenantId { get; }

    int Version { get; }
    string DefinitionId { get; }
    string ActivityTypeKey { get; }            // denormalised from parent

    ImplementationKind ImplementationKind { get; }
    IImplementationDescriptor ImplementationDescriptor { get; }
    ActivityKind Kind { get; }                  // existing closed enum

    IEnumerable<InputDefinition> Inputs { get; }
    IEnumerable<OutputDefinition> Outputs { get; }
    IEnumerable<ActivityPortDefinition> Ports { get; }

    IActivityDefinition Definition { get; }
}
```

**Removed surface:**
- `TypeInfo` (was `TypeInformation`; replaced by `ImplementationDescriptor` which carries `TypeInformation` inside `ClrImplementationDescriptor` for the CLR kind).

## IActivityDefinitionReconciliationState

```csharp
namespace Elsa.Activities.Design.Core.Contracts;

public interface IActivityDefinitionReconciliationState
{
    string Id { get; }
    string? TenantId { get; }

    string ActivityDefinitionId { get; }

    string? SourceVersion { get; }
    string? ProvisioningHash { get; }
    DateTimeOffset LastSeenAt { get; }
    DateTimeOffset LastProvisionedAt { get; }
    string? LastProvisionedBy { get; }
    bool IsStale { get; }
    DateTimeOffset? RemovedAt { get; }
}
```

Consumed by:
- Unit F's reconciler implementation.
- Audit / diagnostic endpoints that need provisioning history.
- The picker query (filters out rows where `RemovedAt` is set).

NOT consumed by:
- Standard `IActivityDefinition` read consumers (picker, designer, runtime resolution) — they see only `IActivityDefinition` + `IActivityDefinitionVersion`.

## Implementation by the persistence entities

Each entity implements its corresponding read interface. Property bodies are simple getters. The entity classes are `public sealed` (per framework §2.23.3 visibility rule for logic-bearing implementations); they live in `Elsa.Activities.Design.Persistence.Core.Entities`.

## Test surface

- Branch test: a persisted `ActivityDefinition` instance exposes the expected `IActivityDefinition` surface (every getter returns the entity's value).
- Branch test: `IActivityDefinition` MUST NOT expose any reconciliation-state field — runtime reflection check that the interface declaration excludes them.
- Branch test: `IActivityDefinitionVersion.ImplementationDescriptor` returns the hydrated polymorphic descriptor matching the entity's `ImplementationKind`.
