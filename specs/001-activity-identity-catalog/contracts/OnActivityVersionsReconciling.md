# Contract: `OnActivityVersionsReconciling`

**Location.** `Elsa.Activities.Design.Reconciliation.Core.OnActivityVersionsReconciling`

**Kind.** Contribution event (framework §2.6.1). Sources contribute candidate `IActivityDefinitionVersion` instances by handling this event.

**Constitutional citation.** Framework §2.6.1 (Domain events — the contribution mechanism); Sipke item 6 (reconciliation as idempotent lifecycle).

## Surface

```csharp
namespace Elsa.Activities.Design.Reconciliation.Core;

public sealed record OnActivityVersionsReconciling(
    ICollection<IActivityDefinitionVersion> Versions
) : IDomainEvent;
```

## Dispatch flow

`ActivityVersionReconciler` (formerly `ActivityVersionProvisioner`) is the single sender. Its `Reconcile(CancellationToken)` method:

```csharp
public async Task Reconcile(CancellationToken ct)
{
    var versions = new Collection<IActivityDefinitionVersion>();
    await sender.Send(new OnActivityVersionsReconciling(versions), ct);

    foreach (var version in versions)
        await ReconcileVersion(version, ct);
}
```

Each handler adds to the `Versions` collection; the reconciler processes the merged set.

## Source contract

Each contributed `IActivityDefinitionVersion` MUST have a non-null `Definition` (reachable via the read contract's navigation) with `SourceKind`, `SourceId`, `ProvisionedAt`, `ProvisionedBy` populated by the source. The reconciler does NOT inject these fields — the source knows its own identity.

Sources MAY contribute multiple versions of the same `ActivityTypeKey` (append-only history); the reconciler dedupes by `(DefinitionId, Version)` per the unique constraint.

## Behaviour expectations

- **Idempotent.** Each reconciliation pass re-fires the event; sources contribute the same data; the reconciler detects no-change via `ProvisioningHash` and skips writes.
- **Awaited end-to-end.** Per §2.6.1 — the reconciler does not proceed until all handlers complete.
- **Multiple sources coexist.** A CLR-scanner source and a JSON-file source can both register handlers; both contribute to the same `Versions` collection.

## Seed source (Unit B)

`Elsa.Activities.Design.Reconciliation.Json` ships `JsonActivityVersionsReconcilingHandler` — reads from a configured JSON file (e.g. `elsa-core-activities.json`) and contributes versions with:

```csharp
new ActivityDefinitionVersion
{
    Definition = new ActivityDefinition
    {
        ActivityTypeKey = entry.Definition.UniqueName,
        SourceKind = SourceKind.Json,
        SourceId = entry.TypeInfo.AssemblyName,
        ProvisionedAt = clock.UtcNow,
        ProvisionedBy = Environment.MachineName,
        // ...display metadata
    },
    Version = entry.Version,
    ActivityTypeKey = entry.Definition.UniqueName,  // denormalised
    ImplementationKind = ImplementationKind.Clr,
    ImplementationDescriptor = new ClrImplementationDescriptor(entry.TypeInfo),
    Kind = entry.Kind,
    Inputs = entry.Inputs,
    Outputs = entry.Outputs,
    DesignFacets = entry.DesignFacets
};
```

## Test surface

- Registration test: the reconciliation feature registers `ActivityVersionReconciler`; the seed JSON source feature registers its handler; both resolve from DI.
- Branch test: handler with empty input set → reconciler sends the event, receives empty `Versions`, no writes.
- Branch test: handler contributes one version → reconciler writes `ActivityDefinition` + `ActivityDefinitionVersion` rows with expected provenance.
- Branch test: same source re-runs unchanged → no rewrites (hash unchanged).
- Branch test: same source re-runs changed → reconciliation-state hash updates; `LastSeenAt` refreshed.
