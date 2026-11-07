# Dashboard extension points

Owner/composition root: `Elsa.Studio.Dashboard.Management`. Contract layer: `Elsa.Studio.Dashboard.Core`. See the [domain guide](../README.md) for composition and the linked source for exact signatures. Implementations are internal and scoped unless explicitly described otherwise.

## Overridable contracts

| Contract / signature authority | Kind / layer | Default, lifetime and dependants |
| --- | --- | --- |
| [`IDashboardQueryService`](../Core/Contracts/DashboardQueryContracts.cs) | Replacement, Core | `DashboardQueryService`, scoped; native discovery/validate/execute/drill. API/resource validation/authoring consume it. Exactly one. |
| [`IDashboardResourceService`](../Core/Resources/DashboardResourceContracts.cs) | Replacement, Core | `DashboardResourceService`, scoped; native resources, current authorization, CAS/copy/history. API/authoring consume it. Exactly one. |
| [`IDashboardResourceStore`](../Core/Resources/DashboardResourceContracts.cs) | Replacement, Core | [EF store](../Persistence/EntityFrameworkCore/EfDashboardResourceStore.cs), scoped; resource service consumes atomic definitions/audiences/history/keyed-write outcomes. Exactly one, no result/credential/transcript cache. |
| [`IDashboardActorContext`](../Core/Resources/DashboardResourceContracts.cs) | Replacement, Core | `HttpDashboardActorContext`, scoped in API; REST, sources and Weaver use the selected current identity. Default maps normalized user/tenant/authentication subject. Exactly one. |
| [`IDashboardDefinitionValidator`](../Core/Resources/DashboardResourceContracts.cs) | Replacement, Core | `DashboardDefinitionValidator`, scoped; resources require current native validation/disclosure. Missing support fails closed. |
| [`IDashboardAudienceDirectory`](../Core/Resources/DashboardResourceContracts.cs) | Replacement, Core | `DashboardAudienceDirectory`, scoped in API; resource grants/candidate discovery consume current same-tenant users/roles and scoped cursors. |
| [`IDashboardAuthoringService`](../Core/Authoring/DashboardAuthoringContracts.cs) | Optional replacement, Core | `WeaverDashboardAuthoringService`, scoped in `Elsa.Agent.Dashboard` (cross-domain). Prepare/stream delegate existing Agent orchestration. Native resource/query operation works without it. Exactly one when composed. |

Use deliberate replacement registrations preserving current authority, cancellation, domain errors, bounds and identity. Management's shell initializer rejects duplicate registrations for every replacement above, including when the feature is composed without the API. It also requires its query/resource/definition-validator registrations; actor, audience, persistence and authoring remain optional for query-only composition. The native API's composition guard diagnoses required missing/duplicate registrations; the optional authoring feature validates its composition too. A caller attachment never supplies identity authority.

## Implementable contributor interfaces

All four are additive scoped **Sources**, layer **Core**. Their exact async signatures, cancellation and `Ownership: IReadOnlyCollection<DashboardDatasetOwner>` are defined in [DashboardQueryContracts.cs](../Core/Contracts/DashboardQueryContracts.cs). Register through `TryAddEnumerable(ServiceDescriptor.Scoped<TContract,TSource>())`; do not add another aggregator or central dataset registry.

| Contributor / method | Named event / single fan-in | Known implementations |
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

Query/result records and scope live in [DashboardQueryModels.cs](../Core/Models/DashboardQueryModels.cs); [the query contract](../../../../../specs/1217-ai-dashboards/contracts/query.md) defines supported limits and semantics. Initial Weaver authoring discloses declarations/definitions only. Future model-visible source facts require a separately reviewed original-evidence contract.
