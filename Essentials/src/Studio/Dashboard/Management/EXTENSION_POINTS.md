# Dashboard extension points

Owner/composition root: `Elsa.Studio.Dashboard.Management`. Contract layer: `Elsa.Studio.Dashboard.Core`. See the [domain guide](../README.md) for composition and the linked source for exact signatures. Implementations are internal and scoped unless explicitly described otherwise.

## Overridable contracts

| Contract / signature authority | Kind / layer | Default, lifetime and dependants |
| --- | --- | --- |
| [`IDashboardQueryService`](../Core/Contracts/DashboardQueryContracts.cs) | Replacement, Core | `DashboardQueryService`, scoped; native discovery/validate/execute/drill. API/resource validation/authoring consume it. Exactly one. |

Resource storage now adds scoped replacements `IDashboardResourceService` (`DashboardResourceService`), `IDashboardDefinitionValidator` (`DashboardDefinitionValidator`, or the fail-closed unavailable fallback for standalone resource composition) and `IDashboardResourceStore` (`EfDashboardResourceStore<TContext>` in the EF package). The actor context and audience directory are still host-supplied; their API defaults and Weaver authoring follow in later slices. Resource operations use current access/CAS and store definitions only, never result caches or transcripts.

Use deliberate replacement registrations preserving current authority, cancellation, domain errors, bounds and identity. Management's shell initializer rejects duplicate registrations for every replacement above, including when the feature is composed without the API. It requires its query, resource and definition-validator registrations; actor, audience, persistence and authoring remain optional for query-only composition. Native API and optional Weaver authoring implementations arrive in later slices. A caller attachment never supplies identity authority.

## Implementable contributor interfaces

All four are additive scoped **Sources**, layer **Core**. Their exact async signatures, cancellation and `Ownership: IReadOnlyCollection<DashboardDatasetOwner>` are defined in [DashboardQueryContracts.cs](../Core/Contracts/DashboardQueryContracts.cs). Register through `TryAddEnumerable(ServiceDescriptor.Scoped<TContract,TSource>())`; do not add another aggregator or central dataset registry.

| Contributor / method | Named event / single fan-in | Planned later implementations |
| --- | --- | --- |
| `IDashboardDatasetExtension.CollectAsync(DashboardDatasetsCollecting, CancellationToken)` | `DashboardDatasetsCollecting` / `CollectDashboardDatasets` | `WorkflowDashboardQuerySource`, `Elsa.Workflows.Dashboard.Queries` (cross-domain); `ProcessingDashboardSource`, `Acme.Processing` (cross-domain independent reference example). |
| `IDashboardQueryValidationExtension.ValidateAsync(DashboardQueryValidating, CancellationToken)` | `DashboardQueryValidating` / `ValidateDashboardQuery` | Same source modules; source meaning/authority supplements generic structural validation. |
| `IDashboardQueryExecutionExtension.ExecuteAsync(DashboardQueryExecuting, CancellationToken)` | `DashboardQueryExecuting` / `ExecuteDashboardQuery` | Same source modules; authorize records/fields/aggregate before computation, order or pagination. |
| `IDashboardQueryDrillExtension.DrillAsync(DashboardQueryDrilling, CancellationToken)` | `DashboardQueryDrilling` / `DrillDashboardQuery` | Same source modules; recompute current authorized membership and bound periods/cursors. |

Each method returns `ValueTask`. [The four aggregators](Query/DashboardQueryHandlers.cs) are registered once by [DashboardManagementFeature](DashboardManagementFeature.cs) using `TryAddEventHandler`. Every contributor declares module/dataset ownership; duplicate claims fail even if a claimant would withhold discovery. A source cannot take over another dataset silently.

## Events

[DashboardQueryEvents.cs](../Core/Events/DashboardQueryEvents.cs) defines the named Core `IEvent` types. Payload collections are initialized/get-only and mutable only for contribution. Delivery uses the existing Sequential inline publisher: source failure propagates through the owning domain boundary and cannot become empty discovery or a partial successful aggregate.

| Event | Payload | Single aggregator | Publication site |
| --- | --- | --- | --- |
| `DashboardDatasetsCollecting` | `Scope: DashboardRequestScope`; `Datasets: ICollection<DashboardDatasetDeclaration>` | `CollectDashboardDatasets` | [DashboardQueryService.DiscoverCoreAsync](Query/DashboardQueryService.cs), before catalog validation. |
| `DashboardQueryValidating` | `Query: DashboardQueryContext`; `Results: ICollection<DashboardQueryValidation>` | `ValidateDashboardQuery` | [DashboardQueryService.PrepareAsync](Query/DashboardQueryService.cs), after generic shape/support/parameter/period resolution. |
| `DashboardQueryExecuting` | `Query: DashboardQueryContext`; `Limits: DashboardQueryLimits`; `Results: ICollection<DashboardQueryResult>` | `ExecuteDashboardQuery` | [DashboardQueryService.ExecuteAsync](Query/DashboardQueryService.cs), after current validation. |
| `DashboardQueryDrilling` | `Query: DashboardQueryContext`; `Request: DashboardDrillRequest`; `Limits: DashboardQueryLimits`; `Results: ICollection<DashboardQueryResult>` | `DrillDashboardQuery` | [DashboardQueryService.DrillAsync](Query/DashboardQueryService.cs), after current validation and descriptor/period binding. |

Query/result records and scope live in [DashboardQueryModels.cs](../Core/Models/DashboardQueryModels.cs); [the query contract](../../../../../specs/1217-ai-dashboards/contracts/query.md) defines supported limits and semantics. The planned Weaver authoring profile discloses declarations/definitions only. Future model-visible source facts require a separately reviewed original-evidence contract.

The independently packaged [Acme Processing module](../../../../../Extensions/Examples/src/Acme.Processing/Module/EXTENSION_POINTS.md) now implements the four contributor contracts using only public Core contracts. It owns business fields/record access and adds no Workflow or central-query branch.
