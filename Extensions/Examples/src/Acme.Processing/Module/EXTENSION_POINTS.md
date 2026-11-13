# Acme Processing extension points

Composition root: [`ProcessingFeature`](ProcessingFeature.cs), feature `AcmeProcessing`. Public contracts live in [`Acme.Processing.Core`](../Core/ProcessingContracts.cs). The [module guide](../README.md) owns data meaning, permissions, reference-storage limits and package qualification.

## Overridable contracts

Each contract below is a scoped **replacement** in **Core**. Register one host implementation before `ProcessingFeature.ConfigureServices`; `TryAddScoped` preserves it. Replacing a default afterward requires removing its registration first. `ProcessingCompositionGuard` checks all four contracts at host startup and shell initialization and rejects missing or duplicate registrations.

| Contract / signature authority | Default / owning project | When to override / dependants |
| --- | --- | --- |
| [`IProcessingActorContext.GetAsync(CancellationToken)`](../Core/ProcessingContracts.cs) → `ValueTask<ProcessingActor>` | `StudioProcessingActorContext`, `Acme.Processing`; [implementation](ProcessingRecords.cs) | Supply a trusted current tenant/canonical-subject integration. Native records and Dashboard source authority consume it through `ProcessingAccess`. The default delegates to Dashboard's installed `IDashboardActorContext`. |
| [`IProcessingRecordReader.FindAsync(string, string, CancellationToken)` / `ReadPageAsync(string, int, string?, CancellationToken)`](../Core/ProcessingContracts.cs) → `ValueTask<ProcessingRecord?>` / `ValueTask<ProcessingRecordPage>` | `InMemoryProcessingStore`, `Acme.Processing`; [implementation](ProcessingRecords.cs) | Install persistent or external module-owned reads. Native records and Dashboard source reads consume it; preserve exact tenant scope, finite stable paging and domain failure semantics. This trusted port does not replace caller-facing authorization. |
| [`IProcessingRecordWriter.AddAsync(ProcessingRecord, CancellationToken)`](../Core/ProcessingContracts.cs) → `ValueTask` | `InMemoryProcessingStore`, `Acme.Processing`; [implementation](ProcessingRecords.cs) | Install matching command storage while preserving tenant identity and immutable record ownership. `ProcessingRecords.CreateAsync` consumes it after current authorization and input validation. |
| [`IProcessingRecords.CreateAsync(ProcessingCreate, CancellationToken)` / `FindAsync(string, CancellationToken)` / `ReadAsync(int, string?, CancellationToken)`](../Core/ProcessingContracts.cs) → `ValueTask<string>` / `ValueTask<ProcessingRecordView?>` / `ValueTask<ProcessingRecordViewPage>` | `ProcessingRecords`, `Acme.Processing`; [implementation](ProcessingRecords.cs) | Replace the native record facade while preserving current tenant, record and category authority. The ordinary HTTP create/list/detail endpoints consume it. |

The default reader/writer share singleton `ProcessingMemoryState`; this application-wide reference store is ephemeral. Replacement integrations supply their own storage lifecycle. Logic services and request authority remain scoped.

## Implementable contributor interfaces

Acme defines no additive contributor interface of its own. Its feature contributes to the following public contracts owned by other domains:

| Contract / signature authority | Kind / layer / registration | Named event / aggregator / implementation |
| --- | --- | --- |
| [`IDashboardDatasetExtension.CollectAsync(DashboardDatasetsCollecting, CancellationToken)`](../../../../../Essentials/src/Studio/Dashboard/Core/Contracts/DashboardQueryContracts.cs) → `ValueTask`; `Ownership` declares dataset/module identity | Context-acting extension, Core; `TryAddEnumerable(ServiceDescriptor.Scoped<IDashboardDatasetExtension, ProcessingDashboardSource>())` | `DashboardDatasetsCollecting` / `CollectDashboardDatasets`; `ProcessingDashboardSource` *(cross-domain)*. |
| [`IDashboardQueryValidationExtension.ValidateAsync(DashboardQueryValidating, CancellationToken)`](../../../../../Essentials/src/Studio/Dashboard/Core/Contracts/DashboardQueryContracts.cs) → `ValueTask`; same ownership declaration | Context-acting extension, Core; scoped `TryAddEnumerable` | `DashboardQueryValidating` / `ValidateDashboardQuery`; `ProcessingDashboardSource` *(cross-domain)*. |
| [`IDashboardQueryExecutionExtension.ExecuteAsync(DashboardQueryExecuting, CancellationToken)`](../../../../../Essentials/src/Studio/Dashboard/Core/Contracts/DashboardQueryContracts.cs) → `ValueTask`; same ownership declaration | Context-acting extension, Core; scoped `TryAddEnumerable` | `DashboardQueryExecuting` / `ExecuteDashboardQuery`; `ProcessingDashboardSource` *(cross-domain)*. |
| [`IDashboardQueryDrillExtension.DrillAsync(DashboardQueryDrilling, CancellationToken)`](../../../../../Essentials/src/Studio/Dashboard/Core/Contracts/DashboardQueryContracts.cs) → `ValueTask`; same ownership declaration | Context-acting extension, Core; scoped `TryAddEnumerable` | `DashboardQueryDrilling` / `DrillDashboardQuery`; `ProcessingDashboardSource` *(cross-domain)*. |
| [`IPermissionContributor.Contribute()`](../../../../../Essentials/src/Foundation/Identity/Core/EXTENSION_POINTS.md) → `IEnumerable<Permission>`; `OwnerId` identifies the module | Permission declaration source, Core; `TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionContributor, ProcessingPermissionExtension>())` | Existing Identity catalog aggregation by `CompositePermissionCatalog`; immutable `ProcessingPermissionExtension` *(cross-domain)*. |

[`ProcessingFeature.ConfigureServices`](ProcessingFeature.cs) owns these registrations. [`ProcessingDashboardSource`](ProcessingDashboardSource.cs) owns source meaning and current authorization. The [Dashboard catalog](../../../../../Essentials/src/Studio/Dashboard/Management/EXTENSION_POINTS.md) and [Identity catalog](../../../../../Essentials/src/Foundation/Identity/Core/EXTENSION_POINTS.md) own the exact upstream payloads, publication sites and fan-in contracts. The [Cross-domain contributions note](../README.md#cross-domain-contributions) describes this module's participation.

## Events

Acme publishes no events and registers no event handlers. Its Dashboard extensions feed the owning domain's named Sequential events through one aggregator per operation; it adds no direct subscription or alternate query dispatcher. Permission declarations use Identity's existing `CompositePermissionCatalog` aggregation.

## Tasks

One `ProcessingCompositionGuard` instance implements `IHostedService` and `IShellInitializer`; both validate descriptor cardinality without constructing request-scoped replacements. No recurring tasks are registered.
