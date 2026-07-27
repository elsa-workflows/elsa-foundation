using Groundwork.Core.Indexing;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Xunit;

namespace Elsa.Persistence.Groundwork.Querying.Tests;

/// <summary>Guards the runtime-alteration store's bounded-query surface independently of provider plans.</summary>
public sealed class WorkflowAlterationQueryRouteCatalogTests
{
    [Fact]
    public void Runtime_alteration_routes_are_complete_and_match_their_bounded_store_operations()
    {
        var routes = ElsaGroundworkQueryRoutes.All
            .Where(route => route.DocumentKind is ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind or
                ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind ||
                route.DocumentKind == ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind &&
                route.PhysicalRoutes.Any(physical => physical.Identity == ElsaRuntimeStorageManifest.PageWorkflowExecutionsForAlterationCaptureQuery))
            .SelectMany(route => route.PhysicalRoutes.Select(physical => (Route: route, Physical: physical)))
            .OrderBy(pair => pair.Route.DocumentKind, StringComparer.Ordinal)
            .ThenBy(pair => pair.Physical.Identity, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(9, routes.Length);
        Assert.All(routes, pair =>
        {
            Assert.Equal(ElsaGroundworkQueryRoutes.MaximumResultCount, pair.Route.MaximumResultCount);
            Assert.Equal(ElsaGroundworkQueryResultOperation.Documents, pair.Route.ResultOperation);
        });

        AssertRoute(
            routes,
            ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
            ElsaRuntimeStorageManifest.PageWorkflowExecutionsForAlterationCaptureQuery,
            ElsaRuntimeStorageManifest.WorkflowExecutionAlterationCaptureOrderIndex,
            ElsaGroundworkQueryContinuation.Cursor,
            [ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField],
            Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryTenantIdField),
            Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryDefinitionIdField),
            Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryStatusField),
            Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryRunKindField),
            Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryCorrelationIdField),
            Equal(PhysicalDocumentFieldPaths.Id),
            Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField),
            GreaterThanOrEqual(ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField),
            LessThanOrEqual(ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField));

        AssertRoute(routes, ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind,
            ElsaRuntimeStorageManifest.ListWorkflowAlterationPlansQuery,
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanByCollection,
            ElsaGroundworkQueryContinuation.Cursor,
            [ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdField],
            Equal(ElsaRuntimeStorageManifest.CollectionField));
        AssertRoute(routes, ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind,
            ElsaRuntimeStorageManifest.FindWorkflowAlterationPlanByTenantAndIdempotencyQuery,
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanByTenantAndIdempotency,
            ElsaGroundworkQueryContinuation.Cursor,
            [ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdField],
            Equal(ElsaRuntimeStorageManifest.WorkflowAlterationPlanTenantPartitionField),
            Equal(ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdempotencyKeyHashField));
        AssertRoute(routes, ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind,
            ElsaRuntimeStorageManifest.PageActiveWorkflowAlterationPlansQuery,
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanStatusField,
            ElsaGroundworkQueryContinuation.Cursor,
            [ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdField],
            Equal(ElsaRuntimeStorageManifest.WorkflowAlterationPlanStatusField));
        AssertRoute(routes, ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind,
            ElsaRuntimeStorageManifest.PageActiveWorkflowAlterationPlansByTenantQuery,
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanByTenantAndStatus,
            ElsaGroundworkQueryContinuation.Cursor,
            [ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdField],
            Equal(ElsaRuntimeStorageManifest.WorkflowAlterationPlanTenantPartitionField),
            Equal(ElsaRuntimeStorageManifest.WorkflowAlterationPlanStatusField));

        AssertRoute(routes, ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind,
            ElsaRuntimeStorageManifest.PageWorkflowAlterationJobsByPlanQuery,
            ElsaRuntimeStorageManifest.WorkflowAlterationJobByPlan,
            ElsaGroundworkQueryContinuation.Cursor,
            [ElsaRuntimeStorageManifest.WorkflowAlterationJobCaptureOrdinalField, ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField],
            Equal(ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField));
        AssertRoute(routes, ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind,
            ElsaRuntimeStorageManifest.ListClaimableWorkflowAlterationJobsQuery,
            ElsaRuntimeStorageManifest.WorkflowAlterationJobByClaimability,
            ElsaGroundworkQueryContinuation.Cursor,
            [ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField, ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField],
            LessThanOrEqual(ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField));
        AssertRoute(routes, ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind,
            ElsaRuntimeStorageManifest.CountWorkflowAlterationJobsByPlanAndStatusQuery,
            "alteration_jobs_counts",
            ElsaGroundworkQueryContinuation.None,
            [ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField],
            Equal(ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField),
            Equal(ElsaRuntimeStorageManifest.WorkflowAlterationJobStatusField));
        AssertRoute(routes, ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind,
            ElsaRuntimeStorageManifest.FindWorkflowAlterationJobByCheckpointCommitIdQuery,
            "alteration_job_checkpoint",
            ElsaGroundworkQueryContinuation.None,
            [ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField],
            Equal(ElsaRuntimeStorageManifest.WorkflowAlterationJobCheckpointCommitIdField));
    }

    private static void AssertRoute(
        IReadOnlyCollection<(ElsaGroundworkQueryRoute Route, ElsaGroundworkPhysicalQueryRoute Physical)> routes,
        string documentKind,
        string identity,
        string indexIdentity,
        ElsaGroundworkQueryContinuation continuation,
        IReadOnlyList<string> ordering,
        params ExpectedPredicate[] predicates)
    {
        var route = Assert.Single(routes, pair =>
            pair.Route.DocumentKind == documentKind && pair.Physical.Identity == identity).Physical;
        Assert.Equal(indexIdentity, route.IndexIdentity);
        Assert.Equal(continuation, route.Continuation);
        Assert.Equal(ordering, route.OrderingFields);
        Assert.Equal(predicates.Select(predicate => predicate.Path), route.Predicates.Select(predicate => predicate.Path));
        foreach (var (expected, actual) in predicates.Zip(route.Predicates))
            Assert.Equal(expected.Operations.Order(), actual.Operations.Order());
    }

    private static ExpectedPredicate Equal(string path) => new(path, [PortableQueryOperation.Equal]);
    private static ExpectedPredicate GreaterThanOrEqual(string path) => new(path, [PortableQueryOperation.GreaterThanOrEqual]);
    private static ExpectedPredicate LessThanOrEqual(string path) => new(path, [PortableQueryOperation.LessThanOrEqual]);

    private sealed record ExpectedPredicate(string Path, IReadOnlyCollection<PortableQueryOperation> Operations);
}
