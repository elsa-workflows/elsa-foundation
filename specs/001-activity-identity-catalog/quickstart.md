# Quickstart — Unit B end-to-end seed flow

This is the narrative walkthrough of Unit B's seed flow once code lands: how a JSON file of activity definitions becomes runnable activities in the catalog, executed via the factory + resolver pipeline.

## Setup

1. Clone `elsa-foundation`; checkout branch `001-activity-identity-catalog`.
2. Open `Elsa.Server.slnx` in your IDE. Confirm the following projects exist after Unit B implementation:

   ```
   src/Elsa.Primitives/ (TenantEntity added)
   src/Elsa.Persistence.EFCore/ (OnEntitySaving event; IEntityModelCreatingHandler unchanged)
   src/Elsa.Activities.Design.Core/ (read contracts, smart-enums, descriptor interface, sealed records)
   src/Elsa.Activities.Design.Persistence.Core/ (3 entities)
   src/Elsa.Activities.Design.Persistence.EFCore/ (EF config + handlers)
   src/Elsa.Activities.Design.Persistence.EFCore.Sqlite/ (fresh initial migration)
   src/Elsa.Activities.Design.Reconciliation.Core/ (renamed; hasher contract)
   src/Elsa.Activities.Design.Reconciliation/ (renamed; reconciler service + default hasher)
   src/Elsa.Activities.Design.Reconciliation.Json/ (NEW; JSON-file source)
   src/Elsa.Activities.Runtime.Core/ (factory + resolver contracts)
   src/Elsa.Activities/ (CLR resolver implementation)
   ```

3. Build the solution: `dotnet build Elsa.Server.slnx`.

## Configure the JSON-file reconciliation source

In `Elsa.Server`'s `Program.cs` (or equivalent feature composition):

```csharp
builder.AddFeature<ActivitiesDesignReconciliationFeature>(opts =>
{
    opts.Hasher = ...; // optional override; default is SHA-256 canonical-JSON
});

builder.AddFeature<ActivitiesDesignReconciliationJsonFeature>(opts =>
{
    opts.FilePath = Path.Combine(AppContext.BaseDirectory, "elsa-core-activities.json");
});

builder.AddFeature<SqliteActivitiesDesignPersistenceShellFeature>(opts =>
{
    opts.ConnectionString = "Data Source=Elsa.db";
});
```

Drop the existing repo-root `elsa-core-activities.json` next to the executable (or point the option at its repo location).

## Run the host

```
dotnet run --project src/Apps/Elsa.Server/Elsa.Server.csproj
```

At startup:

1. **EF Core migration** — `RunMigrationsStartupTask` applies the fresh initial migration; the `Elsa.db` SQLite file is created with the new schema (catalog tables + reconciliation-state table).
2. **Resolver registry initialization** — `ActivityImplementationResolverRegistryStartupTask` publishes `OnActivityImplementationResolversInitializing`; the activities runtime feature handles it to register `ClrActivityImplementationResolver`.
3. **Reconciliation pass** — `ActivityVersionReconcilerStartupTask` invokes `IActivityVersionReconciler.Reconcile()`:
   - Publishes `OnActivityVersionsReconciling` with an empty `Versions` collection.
   - The JSON-file source feature handles the event, reads `elsa-core-activities.json`, contributes one `IActivityDefinitionVersion` per JSON entry (each tagged `SourceKind = SourceKind.Json`, `SourceId = <assembly name>`, etc.).
   - The reconciler processes each contributed version: find-or-create the parent `ActivityDefinition`, append the `ActivityDefinitionVersion`, invoke `IActivityDefinitionHasher` to compute the hash, write/update the `ActivityDefinitionReconciliationState` sibling.

After startup, the catalog is populated.

## Verify via the picker API

Query the catalog through the existing API endpoints:

```
GET /api/activities/design/definitions
```

Expected response: a paginated list of `ActivityDefinitionView` entries, one per JSON catalog entry, with the new field shape:

```json
{
  "id": "<surrogate>",
  "activityTypeKey": "Elsa.Http.SendRequest",
  "sourceKind": "Json",
  "sourceId": "Elsa.Http",
  "provisionedAt": "2026-05-27T...",
  "provisionedBy": "<machine-name>",
  "category": "HTTP",
  "displayName": "Send HTTP Request",
  "description": "..."
}
```

Note absence of `uniqueName` (renamed) and `isBrowsable` (removed).

```
GET /api/activities/design/definitions/{id}/versions/{version}
```

Returns the version detail with `implementationKind` and the polymorphic descriptor payload:

```json
{
  "version": 1,
  "implementationKind": "Clr",
  "implementationDescriptor": {
    "$kind": "Clr",
    "typeInfo": {
      "typeName": "SendRequest",
      "namespace": "Elsa.Http.Activities",
      "assemblyName": "Elsa.Http",
      "assemblyVersion": "1.0.0.0"
    }
  },
  "kind": "Action",
  "inputs": [ ... ],
  "outputs": [ ... ],
  "designFacets": [ ... ]
}
```

## Construct an activity at runtime (integration test scope)

This is the proof that the factory + resolver split works end-to-end:

```csharp
// Resolve from DI
var factory = host.Services.GetRequiredService<IActivityFactory>();

// Load a version from the catalog
var version = await catalogQueries.Get("Elsa.Http.SendRequest", 1);

// Construct
var activity = await factory.Create(
    version.ImplementationDescriptor,
    [new InputState("url", new ArgumentValue("https://example.com", ExpressionType.Literal))],
    Array.Empty<OutputState>(),
    CancellationToken.None);

// activity is an IActivity instance ready for the runtime middleware pipeline.
Assert.IsType<SendRequestActivity>(activity);
```

## Verify the non-CLR descriptor round-trip (Unit B's SC-014 proof)

Construct a `WorkflowImplementationDescriptor` by hand, persist it via the admin API or directly into the catalog, read it back. Confirm:

- The row persists.
- `ImplementationKind` round-trips as `Workflow`.
- `ImplementationDescriptor` deserialises into a `WorkflowImplementationDescriptor` carrying the expected workflow definition + version ids.
- Calling `IActivityFactory.Create(...)` with that descriptor throws `ActivityResolutionException` ("no resolver for kind 'Workflow'") — because Unit B does NOT ship the Workflow resolver; Unit G will.

This is the explicit boundary between Unit B (schema + dispatch infrastructure) and Unit G (workflow-as-activity bridge).

## Reconciliation idempotency check

Stop the host. Restart it. Confirm:

- The reconciliation pass re-runs.
- The hasher computes the same hash for unchanged catalog entries.
- The reconciler updates `LastSeenAt` on the reconciliation-state rows but does NOT rewrite the parent `ActivityDefinition` or version rows (no immutable-property violation).
- The integration test asserts: between two reconciliation passes with unchanged JSON input, `ActivityDefinition.LastModifiedAt` is unchanged.

## What's NOT in Unit B (deferred to subsequent units)

- Workflow resolver — Unit G.
- Picker context-aware visibility (tenant / role / feature-flag policy) — deferred policy layer.
- Stale-row removal / drift-detection lifecycle behaviour — Unit F.
- DTO updates for workflow-side endpoints (Workflows.Design entities reshape) — Units C/D/E.
- The constitutional §E2.x "catalog as source-of-truth" amendment — drafts during Unit B; ratifies with v2.x bump.
