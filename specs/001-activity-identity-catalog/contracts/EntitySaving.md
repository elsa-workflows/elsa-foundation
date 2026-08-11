# Contract: `EntitySaving`

**Location.** `Elsa.Persistence.EFCore.Events.EntitySaving`

**Kind.** Contribution event (framework §2.6.1). Replaces the legacy `IGlobalEntitySavingHandler` provider interface for the activity-catalog handlers (Unit A code-checklist item folded into Unit B).

**Constitutional citation.** Framework §2.6.1; Unit A follow-up code checklist (entity-handler migration).

## Surface

```csharp
namespace Elsa.Persistence.EFCore.Events;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

public sealed record EntitySaving(
    DbContext DbContext,
    EntityEntry Entry,
    CancellationToken CancellationToken
) : IDomainEvent;
```

## Dispatch flow

`ElsaDbContextBase.BeforeSavingChanges` publishes one `EntitySaving` event per modified `Entity` (Added or Modified state). The existing `ApplyGlobalSavingHandlers` and `ApplyEntitySavingHandlers` paths are replaced by this single dispatch:

```csharp
private async Task BeforeSavingChanges(CancellationToken ct)
{
    var entries = ChangeTracker.Entries<Entity>();
    PreventImmutableChanges(entries);
    ApplyTimestamps(entries);
    foreach (var entry in entries.Where(IsModifiedEntity))
        await sender.Send(new EntitySaving(this, entry, ct), ct);
}
```

`IDomainEventSender` is resolved from the request's `IServiceProvider` (via `ServiceProvider.CreateScope()` as today).

## Handler contract

Handlers register against `EntitySaving` via DI. Each handler inspects `Entry.Entity.GetType()` to filter for the entity types it cares about (replaces the reflection-based typed handler dispatch the legacy `ApplyEntitySavingHandlers` performed).

Activity-catalog handlers (`ActivityDefinitionVersionSavingHandler`, etc.) become event handlers:

```csharp
public sealed class ActivityDefinitionVersionSavingHandler : IDomainEventHandler<EntitySaving>
{
    public ValueTask Handle(EntitySaving e, CancellationToken ct)
    {
        if (e.Entry.Entity is ActivityDefinitionVersion version)
        {
            // serialise descriptor + inputs/outputs/design facets to *Source columns
            version.InputsSource = JsonSerializer.Serialize(version.Inputs);
            // ...
        }
        return ValueTask.CompletedTask;
    }
}
```

## Migration

- Activity-catalog **saving** handlers MIGRATE in Unit B (this contract).
- Activity-catalog **model-creating** handlers stay on `IEntityModelCreatingHandler` — model-creating is a sync side-effect chain, not a contribution flow per §2.6.1. See clarification session 3 + plan.md G21 note.
- Workflow-side saving handlers MIGRATE in Units C/D/E (their own scope).
- Other features' saving handlers MIGRATE per their unit; until then, the legacy `IGlobalEntitySavingHandler` / `IEntitySavingHandler<,>` paths in `ElsaDbContextBase` remain active for backward compatibility.

The legacy saving-handler paths are NOT removed in Unit B; they coexist with the new domain-event dispatch. Removal happens when the wider Unit A code-checklist item closes.

## Test surface

- Branch test: saving an `ActivityDefinitionVersion` triggers the handler; the `*Source` columns are populated.
- Branch test: handler ignores unrelated entity types (e.g. `WorkflowDefinitionVersion`).
- Branch test: multiple handlers all run per §2.6.1 (no early exit).
