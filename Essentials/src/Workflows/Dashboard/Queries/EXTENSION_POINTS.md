# Workflow Dashboard query extension points

## Contributions

`WorkflowDashboardQueriesFeature` is the composition root. It contributes one scoped `WorkflowDashboardQuerySource` under each of `IDashboardDatasetExtension`, `IDashboardQueryValidationExtension`, `IDashboardQueryExecutionExtension` and `IDashboardQueryDrillExtension`, through `TryAddEnumerable`. These additive Core contracts and their four named Sequential events are owned by the [Dashboard catalog](../../../Studio/Dashboard/Management/EXTENSION_POINTS.md#implementable-contributor-interfaces). Ownership is `Elsa.Workflows.Dashboard.Queries` / `elsa.workflows.runs`; no second aggregator is registered here.

`WorkflowDashboardQueryPermissionExtension` is a singleton `IPermissionContributor`, contributing `workflows/dashboard-queries:read` through [Foundation Identity](../../../Foundation/Identity/Core/EXTENSION_POINTS.md#ipermissioncontributor). Saved resource access never substitutes for this source permission.

## Consumed replacements

The source consumes Runtime Core's `IWorkflowExecutionStateStore` and [Inspection Core's `IActivityInspectionContextAsync`](../../Runtime/Inspection/Core/EXTENSION_POINTS.md) for current per-run authorization. The feature declares `WorkflowsRuntimeApi` to compose the existing policy implementation, but its package references only contracts. Optional Design `IWorkflowDefinitionAuthorizer` / `IWorkflowLibraryStore` supply current authorized organization; their absence withholds folder fields. `IDashboardActorContext` and `IPermissionAuthorizationService` bind the trusted current request.

## Owned replacement contracts and events

None. Query computation and source policy remain internal. See the [source guide](README.md) for cohort, fraction, duration, coverage and drill semantics.
